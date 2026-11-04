// Source tests over the FMA sampler seam (Aircraft/FenixA320Definition.ChannelFma.cs), as FenixChannelDispatchTests reads
// the key dispatch: the seam is a partial method on the definition that needs a live monitor, so what it must do is pinned
// by reading its code (comments stripped first, so a word in a comment never satisfies or breaks a test). The state
// machine it feeds is tested in FenixChannelFmaAnnouncerTests, the words in FenixChannelFmaSpeechTests.

using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class FenixChannelFmaSeamTests
{
    [Fact]
    public void The_seam_takes_one_pfd_snapshot_per_sample()
    {
        string code = SeamCode();

        // One snapshot gives the verdict and the 16 slots from one instant (spec 2.2), and it is the PFD's.
        Assert.Equal(1, Regex.Matches(code, @"\.Snapshot\s*\(").Count);
        Assert.Matches(@"\.Snapshot\s*\(\s*(?:Services\.)?FenixChannelDisplay\.Pfd\s*\)", code);
    }

    [Fact]
    public void The_seam_checks_the_fma_row_and_never_interrupts()
    {
        string code = SeamCode();

        // The row the seam asks about is the FMA's own: FmaKey is IsMuted's argument, not merely named nearby.
        Assert.Matches(
            @"\bIsMuted\s*\(\s*(?:(?:Services\.)?FenixChannelAnnouncementRows\.)?FmaKey\s*\)", code);
        Assert.Matches(@"\bctx\.Announcer\.Announce\s*\(", code);
        Assert.DoesNotMatch(@"\bAnnounceImmediate\b", code);
    }

    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string SeamCode()
    {
        string path = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft",
            "FenixA320Definition.ChannelFma.cs");
        Assert.True(File.Exists(path), "FenixA320Definition.ChannelFma.cs does not exist");
        return Comment.Replace(File.ReadAllText(path), "");
    }
}
