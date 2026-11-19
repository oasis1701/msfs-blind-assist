using MSFSBlindAssist.Aircraft.MD11;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The two FO silencing modes on the MD-11 announcement gate. A quiet ACTUATION (the FO flipped
/// this control and its own flow narration already said so) suppresses the owner's lamp speech
/// but RECORDS the new state, so a later dark-edge re-compose of the same state stays silent. A
/// global MUTE (the annunciator light test lights ~488 lamps) suppresses everything and records
/// NOTHING, so when the lamps return to normal the composed text equals the pre-test record and
/// the dedup keeps the whole panel silent.
/// </summary>
public class Md11AnnouncementGateQuietTests
{
    [Fact]
    public void QuietActuation_SuppressesAndRecords_SoTheSameStateLaterStaysSilent()
    {
        var gate = new Md11AnnouncementGate();
        gate.NoteQuietActuation("EXT", nowMs: 0, windowMs: Md11AnnouncementGate.FoQuietWindowMs);

        Assert.False(gate.ShouldSpeakBackground("EXT", "On", nowMs: 1000));   // inside quiet: silent
        Assert.False(gate.ShouldSpeakBackground("EXT", "On", nowMs: 9000));   // after: same text deduped
        Assert.True(gate.ShouldSpeakBackground("EXT", "Off", nowMs: 9500));   // a real later change speaks
    }

    [Fact]
    public void QuietActuation_OnlyAffectsItsOwnOwner()
    {
        var gate = new Md11AnnouncementGate();
        gate.NoteQuietActuation("EXT", 0, Md11AnnouncementGate.FoQuietWindowMs);
        Assert.True(gate.ShouldSpeakBackground("APU", "Available", 1000));
    }

    [Fact]
    public void GlobalMute_SuppressesWithoutRecording_SoTheRestoredStateStaysSilent()
    {
        var gate = new Md11AnnouncementGate();
        Assert.True(gate.ShouldSpeakBackground("BATT", "On", 0));              // pre-test baseline recorded

        gate.MuteAll(nowMs: 100, windowMs: 10_000);
        Assert.True(gate.IsMuted(5_000));
        Assert.False(gate.ShouldSpeakBackground("BATT", "Off", 5_000));        // test lit a legend: muted
        Assert.False(gate.IsMuted(10_200));
        Assert.False(gate.ShouldSpeakBackground("BATT", "On", 12_000));        // back to normal: deduped
    }

    [Fact]
    public void Reset_ClearsQuietAndMute()
    {
        var gate = new Md11AnnouncementGate();
        gate.NoteQuietActuation("EXT", 0, 5000);
        gate.MuteAll(0, 5000);
        gate.Reset();
        Assert.False(gate.IsMuted(1));
        Assert.True(gate.ShouldSpeakBackground("EXT", "On", 1));
    }

    [Fact]
    public void MuteAll_NeverShortensAnExistingMute()
    {
        var gate = new Md11AnnouncementGate();
        gate.MuteAll(0, 10_000);
        gate.MuteAll(1_000, 1_000);
        Assert.True(gate.IsMuted(9_000));
    }

    /// <summary>
    /// For read-outs that do not go through ShouldSpeakBackground (the spoiler lever's travel and
    /// ground-spoiler sentences): they ask whether their owner is inside an FO quiet window.
    /// </summary>
    [Fact]
    public void IsQuietFor_TrueOnlyInsideThatOwnersWindow()
    {
        var gate = new Md11AnnouncementGate();
        Assert.False(gate.IsQuietFor("MD11_SPDBRK_HANDLE", 0));
        gate.NoteQuietActuation("MD11_SPDBRK_HANDLE", 1_000, Md11AnnouncementGate.FoQuietWindowMs);
        Assert.True(gate.IsQuietFor("MD11_SPDBRK_HANDLE", 3_000));
        Assert.False(gate.IsQuietFor("MD11_FLAP_LATCH", 3_000));
        Assert.False(gate.IsQuietFor("MD11_SPDBRK_HANDLE", 6_000));
        gate.NoteQuietActuation("MD11_SPDBRK_HANDLE", 7_000, 5_000);
        gate.Reset();
        Assert.False(gate.IsQuietFor("MD11_SPDBRK_HANDLE", 8_000));
    }
}
