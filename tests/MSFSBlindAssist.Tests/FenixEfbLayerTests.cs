// Guards the Fenix v3 EFB accessibility layer: the
// script that stamps roles, names and Tab stops on the page's own elements. CI runs xUnit and does not
// run Node, so these source scans pin what must hold without the jsdom tests: the script keeps its
// guard, never makes a request, removes an element or writes a value, never fires an event, changes a
// class or text, adds a node or stores anything (it only adds attributes and key handlers that call the
// page's own click, and that click is its only one), moves the focus only through one helper called from
// its key handler (the runway condition's arrows) and its "More options" menu rule,
// writes an id only as "msfsba-<n>", ships with the app, and is injected before the
// first navigation, after the window's Escape listener, which leaves Escape to an open overlay. How
// the layer behaves on the page is tested offline in tools/fenix-efb-test.

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixEfbLayerTests
{
    private const string ScriptName = "fenix-efb-accessibility.js";

    private static string ReadScript() =>
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Resources", ScriptName));

    private static string ReadForm() =>
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixEFBForm.cs"));

    [Fact]
    public void The_script_exists_and_opens_its_iife_with_the_guard()
    {
        string path = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Resources", ScriptName);
        Assert.True(File.Exists(path), $"{path} is missing");

        // The first statement of the one IIFE is the double-injection guard (a second injection, or the
        // page reloading the script, must do nothing).
        Assert.Matches(
            new Regex(@"\(function\s*\(\)\s*\{\s*if\s*\(\s*window\.__msfsbaFenixEfb\s*\)\s*\{\s*return;\s*\}"),
            ReadScript());
    }

    // The owner's decision: never press anything, never change behaviour. No request, no removed element,
    // no written value. Scanned over the whole text, comments included, so a comment cannot hide a use.
    [Theory]
    [InlineData("fetch(")]
    [InlineData("XMLHttpRequest")]
    [InlineData("WebSocket")]
    [InlineData(".remove(")]
    [InlineData("removeChild")]
    [InlineData("innerHTML =")]
    [InlineData(".value =")]
    public void The_script_never_makes_a_request_removes_an_element_or_writes_a_value(string banned)
    {
        Assert.DoesNotContain(banned, ReadScript());
    }

    // The layer only adds role, aria-*, tabindex and key handlers. It never fires an event of its own,
    // changes the page's classes or text, ticks a box (it reads `checked`, never writes it), adds or moves a node, or
    // keeps anything in storage. Scanned over the whole text, comments included: a comment that needs one of these
    // words is reworded. (Moving the focus is pinned to its one place below.)
    [Theory]
    [InlineData("dispatchEvent(")]
    [InlineData("classList.add")]
    [InlineData("classList.remove")]
    [InlineData("classList.toggle")]
    [InlineData("className =")]
    [InlineData("textContent =")]
    [InlineData("nodeValue =")]
    [InlineData(".checked =")]
    [InlineData("appendChild")]
    [InlineData("insertBefore")]
    [InlineData("localStorage")]
    public void The_script_never_fires_events_changes_classes_or_text_adds_nodes_or_stores_anything(string banned)
    {
        Assert.DoesNotContain(banned, ReadScript());
    }

    // Where a named function's body lies in the script: from its opening brace to its closing one.
    private static (int Open, int Close) FunctionSpan(string script, string name)
    {
        Match signature = Regex.Match(script, @"function\s+" + name + @"\s*\(");
        Assert.True(signature.Success, $"the function {name} is not in the script");
        int open = script.IndexOf('{', signature.Index);
        int depth = 0;
        int close = -1;
        for (int i = open; i < script.Length && close < 0; i++)
        {
            if (script[i] == '{') depth++;
            else if (script[i] == '}' && --depth == 0) close = i;
        }
        Assert.True(close > open, $"{name} has no closing brace");
        return (open, close);
    }

    // The text of the key handler onButtonKey, from its opening brace to its closing one.
    private static string KeyHandler(string script)
    {
        (int open, int close) = FunctionSpan(script, "onButtonKey");
        return script.Substring(open, close - open + 1);
    }

    // The one press the layer makes is the page's own click, from its key handler (Enter / Space on the control, or an
    // arrow on the runway condition's next code): `.click(` occurs exactly once in the whole text, inside onButtonKey.
    [Fact]
    public void The_script_presses_only_through_the_pages_own_click_in_its_key_handler()
    {
        string script = ReadScript();
        Assert.Single(Regex.Matches(script, Regex.Escape(".click(")));
        Assert.Contains(".click(", KeyHandler(script));
    }

    // The layer moves the focus through one helper, moveFocus, which holds the script's only `.focus(`.
    // It is called from two places only: the key handler (the runway condition's arrows, after pressing the
    // code they move to) and the "More options" menu rule followMenu (into an opened menu, and back to its button when
    // it closes and the focus is lost).
    [Fact]
    public void The_script_moves_the_focus_only_through_its_one_helper_from_the_key_handler_and_the_menu_rule()
    {
        string script = ReadScript();
        Assert.Single(Regex.Matches(script, Regex.Escape(".focus(")));
        (int helperOpen, int helperClose) = FunctionSpan(script, "moveFocus");
        int focusAt = script.IndexOf(".focus(", StringComparison.Ordinal);
        Assert.True(focusAt > helperOpen && focusAt < helperClose, "`.focus(` is inside moveFocus");

        (int keyOpen, int keyClose) = FunctionSpan(script, "onButtonKey");
        (int menuOpen, int menuClose) = FunctionSpan(script, "followMenu");
        var calls = Regex.Matches(script, @"(?<!function\s)\bmoveFocus\(").Select(m => m.Index).ToList();
        Assert.NotEmpty(calls);
        foreach (int call in calls)
        {
            bool inKeyHandler = call > keyOpen && call < keyClose;
            bool inMenuRule = call > menuOpen && call < menuClose;
            Assert.True(inKeyHandler || inMenuRule, $"moveFocus is called at {call}, outside the key handler and the menu rule");
        }
        Assert.Contains(calls, c => c > keyOpen && c < keyClose);
        Assert.Contains(calls, c => c > menuOpen && c < menuClose);

        string handler = KeyHandler(script);
        Assert.True(handler.IndexOf(".click(", StringComparison.Ordinal) < handler.IndexOf("moveFocus(", StringComparison.Ordinal),
            "the arrow moves the focus to the code it pressed, after pressing it");
    }

    // Escape closes an open CDK overlay (a "More options" menu, the Panel States pop-up) before the window. The
    // window's injected capture-phase listener posts the close message only when no overlay pane with content is open;
    // otherwise Escape is the page's (CdkConnectedOverlay and MatDialog close on it).
    [Fact]
    public void The_window_leaves_Escape_to_an_open_overlay_and_closes_otherwise()
    {
        string source = ReadForm();
        Match call = Regex.Match(source,
            @"AddScriptToExecuteOnDocumentCreatedAsync\(\s*(?<literals>(""(?:[^""\\]|\\.)*""\s*\+?\s*)+)\)",
            RegexOptions.Singleline);
        Assert.True(call.Success, "the Escape listener's injection is not in the form");
        string listener = string.Concat(Regex.Matches(call.Groups["literals"].Value, @"""((?:[^""\\]|\\.)*)""")
            .Select(m => m.Groups[1].Value));
        Assert.Contains("msfsba-efb-close", listener);
        Assert.Single(Regex.Matches(listener, Regex.Escape("postMessage(")));
        Assert.Matches(new Regex(
            @"^window\.addEventListener\('keydown',function\(e\)\{" +
            @"if\(e\.key==='Escape'&&!document\.querySelector\('\.cdk-overlay-pane > \*'\)\)\{" +
            @"window\.chrome\.webview\.postMessage\('msfsba-efb-close'\);\}\},true\);$"), listener);
    }

    // The only ids the layer writes are its own, for the results grid's aria-owns: every
    // setAttribute('id', ...) builds its value from the "msfsba-" prefix, no id property is assigned, and no helper is
    // handed the id attribute to write.
    [Fact]
    public void The_script_writes_ids_only_with_the_msfsba_prefix()
    {
        string script = ReadScript();
        MatchCollection writes = Regex.Matches(script, @"setAttribute\(\s*['""]id['""]\s*,\s*(?<value>[^)]*)\)");
        Assert.NotEmpty(writes);
        foreach (Match write in writes)
        {
            Assert.StartsWith("'msfsba-' +", write.Groups["value"].Value.Trim());
        }
        Assert.DoesNotMatch(new Regex(@"\.id\s*=(?!=)"), script);
        Assert.DoesNotMatch(new Regex(@"(setAttr|ownState)\(\s*[^,()]+,\s*['""]id['""]"), script);
        Assert.DoesNotMatch(new Regex(@"setAttributeNS\s*\("), script);
    }

    [Fact]
    public void The_project_copies_the_script_to_the_output_folder()
    {
        XDocument csproj = XDocument.Load(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "MSFSBlindAssist.csproj"));

        XElement? entry = csproj.Descendants("None")
            .FirstOrDefault(e => (string?)e.Attribute("Update") == @"Resources\fenix-efb-accessibility.js");

        Assert.NotNull(entry);
        Assert.Equal("PreserveNewest", (string?)entry!.Element("CopyToOutputDirectory"));
    }

    [Fact]
    public void The_window_injects_the_script_it_reads_from_the_output_folder_before_the_first_navigation()
    {
        string source = ReadForm();

        // Read once, from next to the executable (the csproj entry copies it there); a missing file is logged.
        Assert.Contains(
            "Path.Combine(AppContext.BaseDirectory, \"Resources\", \"fenix-efb-accessibility.js\")", source);
        Assert.Contains("Log.Warn(\"FenixEFB\"", source);

        // The text that was read is what AddScriptToExecuteOnDocumentCreatedAsync receives, so the
        // layer is in place for the first document; the Escape listener's own injection stays.
        Match read = Regex.Match(source, @"(\w+)\s*=\s*ReadLayerScript\(\)");
        Assert.True(read.Success, "InitWebViewAsync does not read the layer through ReadLayerScript()");
        Match inject = Regex.Match(source, @"AddScriptToExecuteOnDocumentCreatedAsync\(\s*" + read.Groups[1].Value + @"\s*\)");
        Assert.True(inject.Success, "the text read is not what AddScriptToExecuteOnDocumentCreatedAsync receives");
        Assert.True(inject.Index < source.IndexOf("_webViewReady = true;", StringComparison.Ordinal),
            "the layer must be registered before the window navigates");
        Assert.Contains("msfsba-efb-close", source);
    }
}
