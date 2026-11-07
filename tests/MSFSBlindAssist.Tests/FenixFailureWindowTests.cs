// Pins the Failures window's shape. The window is sim-facing; these source scans hold the rules a
// screen-reader user depends on (CORE-12, no check boxes, VAT-13, a No default on every write) and the
// one write path: a single POST, to saveManual, and nothing else that can change the pilot's flight.
using System.Text.RegularExpressions;
using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixFailureWindowTests
{
    private static string Root => ClaudeContextBudgetTests.RepoRoot();

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root, "MSFSBlindAssist" }.Concat(parts).ToArray()));

    [Fact]
    public void Registry_lists_Failures_once()
    {
        var e = Assert.Single(FenixWindowRegistry.Build(), x => x.Name == "Failures");
        Assert.Equal("Arm, fail or clear Fenix failures by ATA chapter", e.Description);
    }

    [Fact]
    public void Window_keeps_the_screen_reader_rules()
    {
        string form = Source("Forms", "Fenix", "FenixFailuresForm.cs");
        string dialog = Source("Forms", "Fenix", "FenixFailuresArmDialog.cs");

        Assert.Contains("NativeAccessibleTreeView", form);
        Assert.Contains("ReadOnly = true", form);
        Assert.Contains("MessageBoxDefaultButton.Button2", form);

        foreach (string source in new[] { form, dialog })
        {
            Assert.DoesNotMatch(new Regex(@"new\s+TreeView\b"), source);
            Assert.DoesNotContain("CheckBoxes", source);
        }
    }

    [Fact]
    public void Only_one_write_and_only_to_saveManual()
    {
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(Path.Combine(Root, "MSFSBlindAssist", "Forms", "Fenix"), "FenixFailures*.cs"));
        files.Add(Path.Combine(Root, "MSFSBlindAssist", "Forms", "Fenix", "FenixWindowRegistry.Failures.cs"));
        files.AddRange(Directory.GetFiles(Path.Combine(Root, "MSFSBlindAssist", "Services"), "FenixFailure*.cs"));
        Assert.True(files.Count >= 6, "the form, the dialog, the registry partial and the three services");

        var postLines = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string source = File.ReadAllText(file);
            postLines.AddRange(source.Split('\n').Where(l => l.Contains("PostRestAsync(", StringComparison.Ordinal)));

            Assert.DoesNotMatch(new Regex(@"Write(Int|Bool|Float|String)Async"), source);
            Assert.False(source.Contains("mutation", StringComparison.Ordinal), $"{name} contains \"mutation\"");
            Assert.False(source.Contains("localhost", StringComparison.Ordinal), $"{name} contains \"localhost\"");
            Assert.False(source.Contains("/failures/arrival", StringComparison.Ordinal), $"{name} contains \"/failures/arrival\"");
            if (name != "FenixFailureRequests.cs")
                Assert.False(source.Contains("\"/fenix/failures", StringComparison.Ordinal), $"{name} contains a failures path literal");
        }

        string line = Assert.Single(postLines);
        Assert.Contains("FenixFailureRequests.SavePath", line);
    }
}
