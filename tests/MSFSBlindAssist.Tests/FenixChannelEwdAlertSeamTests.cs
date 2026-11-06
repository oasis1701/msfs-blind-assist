using System.Text.RegularExpressions;
using Xunit;

namespace MSFSBlindAssist.Tests;

// Source test over the Fenix definition's E/WD alert seam (Aircraft/FenixA320Definition.ChannelEwdAlerts.cs): the announcer host
// calls it every tick, and it takes one E/WD snapshot, steps the announcer and speaks what it returns.
public class FenixChannelEwdAlertSeamTests
{
    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string Code()
    {
        string path = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft",
            "FenixA320Definition.ChannelEwdAlerts.cs");
        return Comment.Replace(File.ReadAllText(path), "");
    }

    [Fact]
    public void The_seam_takes_one_ewd_snapshot_and_speaks_what_the_announcer_returns()
    {
        string code = Code();

        Assert.Contains("partial void SampleChannelEwdAlerts(ChannelAnnounceContext ctx)", code);
        Assert.Single(Regex.Matches(code, @"\.Snapshot\("));
        Assert.Contains("Snapshot(Services.FenixChannelDisplay.Ewd)", code);
        Assert.Matches(@"\.Step\([^;]*Services\.FenixChannelAnnouncementRows\.IsMuted", code);
        Assert.Contains("ReportFeatureProblem(\"E/WD alerts\"", code);
        Assert.Matches(@"FenixChannelEwdAlertAnnouncer\.Speak\(\s*\w+\s*,\s*ctx\.Announcer\s*\)", code);
    }
}
