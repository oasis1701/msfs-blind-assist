// Guards the Fenix v3 EFB accessibility layer (plan 03, Owner decision 6 and Global Constraints): the
// script that stamps roles, names and Tab stops on the page's own elements. CI runs xUnit and does not
// run Node, so these source scans pin what must hold without the jsdom tests: the script keeps its
// guard, never makes a request, removes an element or writes a value (it only adds attributes and key
// handlers that call the page's own click), ships with the app, and is injected before the first
// navigation. How the layer behaves on the page is tested offline in tools/fenix-efb-test.

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
