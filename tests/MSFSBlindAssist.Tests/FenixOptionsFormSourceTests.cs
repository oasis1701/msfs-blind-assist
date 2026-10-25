// Pins the Fenix options window's shape. The window is sim-facing; these source scans hold
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

    // A value read out directly, or a whole FenixSetting interpolated: FenixSetting is a positional record, so
    // its ToString() prints Value too (for a credential, the credential). The names are the ones the options
    // window's code gives a FenixSetting (or a list of them, indexed).
    const string ValueLeak = @"\.Value\b|RowText|FormatValue|DetailLines"
        + @"|\{\s*(?:option|now|before|landed|present|setting|settings|s|o|a|b|row)\s*(?:\[[^\]]*\])?\s*!?\s*(?:\.ToString\(\))?\s*[,:}]";

    [Fact] public void Never_logs_an_option_value()
    {
        var root = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist");
        var files = Directory.GetFiles(Path.Combine(root, "Services"), "FenixOption*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "Forms", "Fenix"), "FenixOptions*.cs"));
        foreach (var line in files.SelectMany(File.ReadAllLines).Where(l => l.Contains("Log.")))
            Assert.DoesNotMatch(ValueLeak, line);
    }

    // The scan itself: it catches a whole setting interpolated, and lets the keys-and-counts lines through.
    [Theory]
    [InlineData("Log.Debug(LogCategory, $\"Setting {option} sent\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"Read back {now}\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"Was {before,20}\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"Got {landed!}\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"Got {s.ToString()}\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"First {settings[0]}\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"Value {option.Value}\");", true)]
    [InlineData("Log.Debug(LogCategory, $\"Setting {what} sent for {key}\");", false)]
    [InlineData("Log.Debug(LogCategory, $\"Read {settings.Count} options in {groups.Count} groups\");", false)]
    [InlineData("Log.Debug(LogCategory, $\"Options read failed: {ex.GetType().Name}: {ex.Message}\");", false)]
    public void The_log_scan_catches_a_whole_setting(string line, bool leaks)
        => Assert.Equal(leaks, System.Text.RegularExpressions.Regex.IsMatch(line, ValueLeak));

    [Fact] public void Registry_lists_Fenix_options_once()
        => Assert.Single(MSFSBlindAssist.Forms.Fenix.FenixWindowRegistry.Build(), e => e.Name == "Fenix options");
}
