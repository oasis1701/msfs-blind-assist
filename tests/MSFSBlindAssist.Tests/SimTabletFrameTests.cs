using MSFSBlindAssist.SimConnect;
using El = MSFSBlindAssist.SimConnect.CoherentPmdgEfbClient.ScrapeElement;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The sim tablet's frames from another origin (STAB-14): an app such as GSX Remote or Navigraph Charts
/// lives in a cross-origin iframe, which the reader marks and the client reads in the frame's own
/// execution context. These pin how the client merges a frame's elements into the page and routes a
/// press on one of them back to its frame.
/// </summary>
public class SimTabletFrameTests
{
    private static El Page(int idx, string text, string kind = "static") => new() { idx = idx, kind = kind, text = text };
    private static El Marker(int idx, string origin, string size = "554x743") => new() { idx = idx, kind = "frame", text = size, value = origin };

    [Fact]
    public void AFrameMarker_IsReplacedByTheFramesElements_InPlace_WithItsIdxMovedToTheSlot()
    {
        var page = new List<El> { Page(1, "More apps", "tab"), Marker(2, "http://127.0.0.1:8744"), Page(3, "Notifications: 1") };
        var frames = new Dictionary<int, (int, List<El>)?>
        {
            [1] = (25, new List<El> { Page(1, "GSX Menu", "button"), Page(2, "Our iniBuilds A350 is Parked") }),
        };

        var merged = CoherentPmdgEfbClient.SpliceFrames(page, frames, out var slots);

        Assert.Equal(new[] { "More apps", "GSX Menu", "Our iniBuilds A350 is Parked", "Notifications: 1" }, merged.Select(e => e.text));
        Assert.Equal(new[] { 1, 1_000_001, 1_000_002, 3 }, merged.Select(e => e.idx));
        Assert.Equal(25, slots[1]);
        // The frame's own list is left alone: the client reuses it while the frame is unchanged.
        Assert.Equal(1, frames[1]!.Value.Item2[0].idx);
    }

    [Fact]
    public void AFrameThatCannotBeReadYet_SaysSo_AndTakesNoSlot()
    {
        var page = new List<El> { Marker(4, "https://chartspanel-v8.navigraph.com") };
        var merged = CoherentPmdgEfbClient.SpliceFrames(page, new Dictionary<int, (int, List<El>)?> { [1] = null }, out var slots);

        Assert.Equal("This app's page is still loading.", Assert.Single(merged).text);
        Assert.Empty(slots);
    }

    [Fact]
    public void TwoFramesOfOneOrigin_AreTwoSlots_EachWithItsOwnContext()
    {
        var page = new List<El> { Marker(1, "http://127.0.0.1:8744"), Marker(2, "http://127.0.0.1:8744") };
        var frames = new Dictionary<int, (int, List<El>)?>
        {
            [1] = (10, new List<El> { Page(1, "First") }),
            [2] = (11, new List<El> { Page(1, "Second") }),
        };

        var merged = CoherentPmdgEfbClient.SpliceFrames(page, frames, out var slots);

        Assert.Equal(new[] { 1_000_001, 2_000_001 }, merged.Select(e => e.idx));
        Assert.Equal(10, slots[1]);
        Assert.Equal(11, slots[2]);
    }

    [Fact]
    public void AFrameInsideAFrame_IsNotRead_ItsMarkerIsDropped()
    {
        var page = new List<El> { Marker(1, "http://127.0.0.1:8744") };
        var frames = new Dictionary<int, (int, List<El>)?>
        {
            [1] = (10, new List<El> { Page(1, "Outer"), Marker(2, "https://example.com") }),
        };

        var merged = CoherentPmdgEfbClient.SpliceFrames(page, frames, out _);

        Assert.Equal("Outer", Assert.Single(merged).text);
    }

    [Fact]
    public void APageWithNoFrame_IsReturnedAsItIs()
    {
        var page = new List<El> { Page(1, "Settings") };
        Assert.Same(page, CoherentPmdgEfbClient.SpliceFrames(page, new Dictionary<int, (int, List<El>)?>(), out var slots));
        Assert.Empty(slots);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(990_001, null)]          // the SimBrief action stays the page's
    [InlineData(1_000_001, "1/1")]
    [InlineData(2_000_017, "2/17")]
    public void AnIdx_SaysWhichFrameItBelongsTo(int idx, string? expected)
    {
        var split = CoherentPmdgEfbClient.SplitFrameIdx(idx);
        Assert.Equal(expected, split == null ? null : $"{split.Value.Slot}/{split.Value.Local}");
    }

    [Theory]
    [InlineData("554x743", "554x743", true)]
    [InlineData("552x741", "554x743", true)]   // a border's few pixels
    [InlineData("0x0", "0x0", false)]          // every hidden app's frame
    [InlineData("610x738", "554x743", false)]
    [InlineData("", "554x743", false)]
    [InlineData("554x743", "", false)]
    public void AFramesWindow_MatchesItsMarker_BySize(string window, string marker, bool same) =>
        Assert.Equal(same, CoherentPmdgEfbClient.SameFrameSize(window, marker));
}
