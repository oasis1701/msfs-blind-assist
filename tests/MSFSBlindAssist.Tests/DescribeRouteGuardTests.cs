using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

/// <summary>Source-text guards on the EFB's Describe Route press, whose WinForms body cannot be unit-tested.</summary>
public class DescribeRouteGuardTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    private static string Efb() => File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Forms", "ElectronicFlightBagForm.cs"));

    /// <summary>The body of one method: from its signature to the next member at class indentation.</summary>
    private static string Method(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var next = new Regex(@"\n    (?:private|public|internal|protected)\s").Match(source, start + signature.Length);
        return next.Success ? source[start..next.Index] : source[start..];
    }

    [Fact]
    public void The_api_key_is_checked_before_the_taxi_routes_are_computed()
    {
        string body = Method(Efb(), "private async Task DescribeRouteAsync()");
        int keyCheck = body.IndexOf("AiProviderFactory.HasApiKey()", StringComparison.Ordinal);
        int taxi = body.IndexOf("BuildTaxiRoutesBlockAsync(", StringComparison.Ordinal);
        Assert.True(keyCheck >= 0 && keyCheck < taxi, "HasApiKey must be asked before BuildTaxiRoutesBlockAsync");
    }

    [Fact]
    public void A_briefing_that_outlives_its_window_is_never_dropped_in_silence()
    {
        string body = Method(Efb(), "private async Task DescribeRouteAsync()");
        Assert.DoesNotMatch(new Regex(@"if \(IsDisposed\) return;"), body);
    }

    [Fact]
    public void A_briefing_never_writes_the_status_of_a_closed_window()
    {
        // A briefing now runs on after its flight bag closed (a database switch closes it), so every UpdateStatus it
        // makes -- the catch blocks' included -- must land on a window that may be gone: UpdateStatus itself returns
        // first thing when the form is disposed, before it touches the status label or Invoke.
        string body = Method(Efb(), "private void UpdateStatus(string message)");
        int guard = body.IndexOf("if (IsDisposed) return;", StringComparison.Ordinal);
        int touch = body.IndexOf("InvokeRequired", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < touch, "UpdateStatus must return when disposed, before InvokeRequired");
    }

    [Fact]
    public void Loading_a_plan_never_re_enables_Describe_while_a_briefing_runs()
    {
        string body = Method(Efb(), "private void LoadSimBriefFlightPlan()");
        Assert.Matches(new Regex(@"describeRouteButton\.Enabled\s*=\s*!_descriptionSession\.IsGenerating\s*&&"), body);
    }

    [Fact]
    public void A_briefing_of_a_plan_that_was_replaced_is_discarded()
    {
        string body = Method(Efb(), "private async Task DescribeRouteAsync()");
        Assert.Contains("ReferenceEquals(plan, _flightPlanManager.CurrentFlightPlan)", body);
    }

    [Fact]
    public void The_position_read_is_bounded_by_WaitAsync()
    {
        string body = Method(Efb(), "private async Task<OwnPosition?> ReadOwnPositionAsync()");
        Assert.Contains(".WaitAsync(", body);
        Assert.DoesNotContain("Task.WhenAny", body);
    }

    [Fact]
    public void Only_a_successful_SimBrief_load_erases_the_route_description()
    {
        // The one place the kept description is erased is Load SimBrief, and only once LoadFromSimBrief has returned
        // (it throws on failure without replacing the plan, so a failed load must leave the description alone).
        string efb = Efb();
        Assert.Single(Regex.Matches(efb, Regex.Escape("_descriptionSession.Clear()")));

        string root = Path.Combine(RepoRoot(), "MSFSBlindAssist");
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (Path.GetFileName(file) == "ElectronicFlightBagForm.cs") continue;
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("routeDescriptionSession.Clear()", text);
            Assert.DoesNotContain("_descriptionSession.Clear()", text);
        }

        string body = Method(efb, "private void LoadSimBriefFlightPlan()");
        int load = body.IndexOf("LoadFromSimBrief(", StringComparison.Ordinal);
        int clear = body.IndexOf("_descriptionSession.Clear()", StringComparison.Ordinal);
        Assert.True(load >= 0 && clear > load, "Clear must come after LoadFromSimBrief( inside LoadSimBriefFlightPlan");
    }
}
