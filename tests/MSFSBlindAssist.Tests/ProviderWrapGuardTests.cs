using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Source-text guard: every non-null assignment of MainForm's airportDataProvider field must go
/// through WithTaxiAugmentation. RefreshDatabaseProvider once assigned the raw
/// DatabaseSelector.SelectProvider() result directly, so after any Database Settings visit or a
/// database auto-switch the session silently lost online taxiway names, aliases and the route
/// briefing's OpenStreetMap tier. WinForms body, so this is a source-text scan (same pattern as
/// GroundTrafficRequestIdTests' hand-numbered-id scan and DescribeRouteGuardTests).
/// </summary>
public class ProviderWrapGuardTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Every_provider_assignment_goes_through_the_augmentation_wrapper()
    {
        string app = Path.Combine(RepoRoot(), "MSFSBlindAssist");
        var assignment = new Regex(@"\bairportDataProvider\s*=\s*(?!=)([^;]+);");
        var found = new List<string>();
        foreach (string file in Directory.EnumerateFiles(app, "MainForm*.cs"))
            foreach (Match m in assignment.Matches(File.ReadAllText(file)))
                found.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value.Trim()}");

        Assert.NotEmpty(found);
        Assert.All(found, f => Assert.Matches(new Regex(@": (?:null|WithTaxiAugmentation\()"), f));
    }
}
