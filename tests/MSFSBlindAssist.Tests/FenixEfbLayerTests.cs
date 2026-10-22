// Guards the Fenix v3 EFB accessibility layer (plan 03, Owner decision 6 and Global Constraints): the
// script that stamps roles, names and Tab stops on the page's own elements. CI runs xUnit and does not
// run Node, so these source scans pin what must hold without the jsdom tests: the script keeps its
// guard, never makes a request, removes an element or writes a value, never fires an event, changes a
// class or text, moves the focus, adds a node or stores anything (it only adds attributes and key
// handlers that call the page's own click, and that click is its only one), ships with the app, and is
// injected before the first navigation. How the layer behaves on the page is tested offline in
// tools/fenix-efb-test.

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

    // Owner decision 6: never press anything, never change behaviour. No request, no removed element,
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

    // Ruling R35: the layer only adds role, aria-*, tabindex and key handlers. It never fires an event of its own,
    // changes the page's classes or text, ticks a box, moves the focus, adds or moves a node, or keeps anything in
    // storage. Scanned over the whole text, comments included: a comment that needs one of these words is reworded.
    [Theory]
    [InlineData("dispatchEvent(")]
    [InlineData("classList.add")]
    [InlineData("classList.remove")]
    [InlineData("classList.toggle")]
    [InlineData("className =")]
    [InlineData("textContent =")]
    [InlineData("nodeValue =")]
    [InlineData(".checked =")]
    [InlineData(".focus(")]
    [InlineData("appendChild")]
    [InlineData("insertBefore")]
    [InlineData("localStorage")]
    public void The_script_never_fires_events_changes_classes_or_text_moves_focus_adds_nodes_or_stores_anything(string banned)
    {
        Assert.DoesNotContain(banned, ReadScript());
    }

    // The one press the layer makes is the page's own click, from its Enter / Space key handler: `.click(` occurs
    // exactly once in the whole text, and that once is inside onButtonKey.
    [Fact]
    public void The_script_presses_only_through_the_pages_own_click_in_its_key_handler()
    {
        string script = ReadScript();
        Assert.Single(Regex.Matches(script, Regex.Escape(".click(")));

        Match signature = Regex.Match(script, @"function\s+onButtonKey\s*\(");
        Assert.True(signature.Success, "the key handler onButtonKey is not in the script");
        int open = script.IndexOf('{', signature.Index);
        int depth = 0;
        int close = -1;
        for (int i = open; i < script.Length && close < 0; i++)
        {
            if (script[i] == '{') depth++;
            else if (script[i] == '}' && --depth == 0) close = i;
        }
        Assert.True(close > open, "onButtonKey has no closing brace");
        Assert.Contains(".click(", script.Substring(open, close - open + 1));
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
