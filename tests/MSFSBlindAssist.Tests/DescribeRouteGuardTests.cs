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
    public void A_failed_briefing_never_writes_the_status_of_a_closed_window()
    {
        // The finally block already checks IsDisposed before touching the form; the two catch blocks must too — a
        // database switch closes the flight bag while the briefing is still running (RBR Task 3 follow-up).
        string body = Method(Efb(), "private async Task DescribeRouteAsync()");
        int catches = body.IndexOf("        catch (", StringComparison.Ordinal);
        int fin = body.IndexOf("        finally", StringComparison.Ordinal);
        Assert.True(catches >= 0 && fin > catches, "the catch blocks were not found");
        string handlers = body[catches..fin];
        var calls = Regex.Matches(handlers, @"UpdateStatus\(");
        Assert.NotEmpty(calls);
        Assert.All(calls, m => Assert.EndsWith("if (!IsDisposed) ", handlers[..m.Index]));
    }

    [Fact]
    public void Loading_a_plan_never_re_enables_Describe_while_a_briefing_runs()
    {
        string body = Method(Efb(), "private void LoadSimBriefFlightPlan()");
        Assert.Matches(new Regex(@"describeRouteButton\.Enabled\s*=\s*!_describingRoute\s*&&"), body);
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
}
