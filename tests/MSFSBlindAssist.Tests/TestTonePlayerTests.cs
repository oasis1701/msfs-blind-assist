// Characterization tests for TestTonePan, the pure half of the shared "Test Tone" audition
// used by the Audio, Taxi Guidance and Hand Fly settings panels. Only the sweep is testable
// here: TestTonePlayer itself owns a WinForms Button and a WASAPI tone, neither of which a CI
// runner has.

using MSFSBlindAssist.Forms.Settings;

namespace MSFSBlindAssist.Tests;

public class TestTonePlayerTests
{
    // The audition exists to answer "did that come out of the right speakers, both of them?".
    // The previous AudioPanel copy ran sin(i * 0.15) for i in 0..19 -- an argument span of
    // 0..2.85 rad, entirely inside [0, pi] -- so pan never went negative and the left channel
    // was never exercised. A dead left driver passed the audition and then made every "steer
    // left" taxi cue inaudible.
    [Fact]
    public void FullCycle_ReachesBothChannels()
    {
        float[] pans = TestTonePan.FullCycle(20);

        Assert.True(pans.Min() < -0.7f, "the sweep must reach the left channel");
        Assert.True(pans.Max() > 0.7f, "the sweep must reach the right channel");
    }

    // Starts only -- NOT "starts and ends", which is what this was called until the arithmetic
    // was checked: the last sample of a full cycle is one step SHORT of the wrap, so pans[19]
    // of FullCycle(20) is -0.247, not ~0. The tone begins centred; it does not end centred.
    [Fact]
    public void FullCycle_StartsAtCentre()
    {
        float[] pans = TestTonePan.FullCycle(20);

        Assert.True(Math.Abs(pans[0]) < 0.05f);
    }

    [Fact]
    public void FullCycle_StaysInRange()
    {
        foreach (float pan in TestTonePan.FullCycle(40))
        {
            Assert.InRange(pan, -1.0f, 1.0f);
        }
    }

    // Every panel drives its tick loop by indexing this array with the loop counter, so a
    // count mismatch would be an IndexOutOfRangeException on a background thread -- i.e. a
    // silently dropped audition, not a visible crash.
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    public void FullCycle_ReturnsOneValuePerTick(int ticks)
    {
        Assert.Equal(ticks, TestTonePan.FullCycle(ticks).Length);
    }

    // The three panels pass their own tick counts; a nonsensical one must degrade to an empty
    // sweep rather than throw, because the tone itself has already started by then.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FullCycle_DegradesToEmpty(int ticks)
    {
        Assert.Empty(TestTonePan.FullCycle(ticks));
    }

    // Both channels must be reached at EVERY duration the three panels use, not just the one
    // the first test happens to pin -- the defect being fixed was duration-dependent (20 ticks
    // of a 0.15 rad/tick sweep never went negative, 40 did).
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    public void FullCycle_ReachesBothChannelsAtEveryPanelDuration(int ticks)
    {
        float[] pans = TestTonePan.FullCycle(ticks);

        Assert.True(pans.Min() < -0.7f, $"{ticks}-tick sweep must reach the left channel");
        Assert.True(pans.Max() > 0.7f, $"{ticks}-tick sweep must reach the right channel");
    }
}
