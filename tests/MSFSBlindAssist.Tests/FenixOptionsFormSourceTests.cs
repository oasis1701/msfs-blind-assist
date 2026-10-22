// Pins the Fenix options window's shape (plan 11, Task 4). The window is sim-facing; these source scans hold
// the rules a screen-reader user depends on (CORE-12, CORE-18, VAT-13), the UI-thread awaits, and that no
// file of the options window or its services logs an option's value (keys and counts only).
namespace MSFSBlindAssist.Tests;

public class FenixOptionsFormSourceTests
{
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(p).ToArray()));

    [Fact] public void Tree_is_the_native_accessible_one_and_status_is_never_a_label()
    {
        string form = Src("Forms", "Fenix", "FenixOptionsForm.cs");
        Assert.Contains("new NativeAccessibleTreeView", form);
        Assert.DoesNotMatch(@"new\s+(System\.Windows\.Forms\.)?TreeView\b", form);
        Assert.DoesNotMatch(@"new\s+(System\.Windows\.Forms\.)?Label\b", form);
        Assert.DoesNotContain("CheckBoxes = true", form);
        Assert.DoesNotContain("ConfigureAwait(false)", form);
    }

    [Fact] public void Never_logs_an_option_value()
    {
        var root = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist");
        var files = Directory.GetFiles(Path.Combine(root, "Services"), "FenixOption*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "Forms", "Fenix"), "FenixOptions*.cs"));
        foreach (var line in files.SelectMany(File.ReadAllLines).Where(l => l.Contains("Log.")))
            Assert.DoesNotMatch(@"\.Value\b|RowText|FormatValue|DetailLines", line);
    }

    [Fact] public void Registry_lists_Fenix_options_once()
        => Assert.Single(MSFSBlindAssist.Forms.Fenix.FenixWindowRegistry.Build(), e => e.Name == "Fenix options");
}
