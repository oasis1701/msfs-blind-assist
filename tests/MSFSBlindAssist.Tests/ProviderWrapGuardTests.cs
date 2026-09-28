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
        Assert.All(found, f => Assert.True(IsWrapped(f[(f.IndexOf(": ", StringComparison.Ordinal) + 2)..]), f));
    }

    /// <summary>Whether an assignment's right-hand side is exactly <c>null</c> or a WithTaxiAugmentation(…) call —
    /// anchored on the TRIMMED text (review M-3): unanchored, "nullableProvider" or "raw ?? WithTaxiAugmentation(x)"
    /// passed.</summary>
    private static bool IsWrapped(string rhs) => Regex.IsMatch(rhs.Trim(), @"^(?:null$|WithTaxiAugmentation\()");

    [Theory]
    [InlineData("null", true)] [InlineData(" WithTaxiAugmentation(DatabaseSelector.SelectProvider())", true)]
    [InlineData("WithTaxiAugmentation(null)", true)]
    [InlineData("nullableProvider", false)] [InlineData("raw ?? WithTaxiAugmentation(x)", false)] [InlineData("null ?? raw", false)]
    public void The_guard_accepts_only_null_or_the_wrapper(string rhs, bool wrapped) => Assert.Equal(wrapped, IsWrapped(rhs));
}
