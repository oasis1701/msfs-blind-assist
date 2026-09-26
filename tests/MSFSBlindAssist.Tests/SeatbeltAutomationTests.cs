using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class SeatbeltAutomationTests
{
    private readonly List<bool> signs = new();
    private readonly List<string> spoken = new();
    private SeatbeltAutomation Make(FoSeatbeltMode mode)
        => new(signs.Add, spoken.Add) { Mode = mode };

    // ---- 10k mode ----
    [Fact]
    public void TenK_OffClimbingThrough_OnDescendingThrough()
    {
        var s = Make(FoSeatbeltMode.TenThousand);
        s.Update(9_600, 1500);    // baseline: below
        s.Update(10_400, 1500);   // climbed through -> OFF
        s.Update(10_400, -1500);  // baseline reset above
        s.Update(9_600, -1500);   // descended through -> ON
        Assert.Equal(new[] { false, true }, signs);
    }

    [Fact]
    public void TenK_HysteresisBandDoesNotThrash()
    {
        var s = Make(FoSeatbeltMode.TenThousand);
        s.Update(9_600, 1500);
        s.Update(10_400, 1500);   // OFF
        s.Update(10_100, -50);    // inside band, no crossing
        s.Update(10_400, 50);
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void Disabled_NeverActuates()
    {
        var s = Make(FoSeatbeltMode.Disabled);
        s.Update(9_600, 1500);
        s.Update(10_400, 1500);
        Assert.Empty(signs);
    }

    // ---- TOC/TOD mode ----
    [Fact]
    public void TocTod_TocTurnsOffAfterSustainedLevelAboveFloor()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        s.Update(20_000, 1500);            // climbing above floor, arm
        for (int i = 0; i < 20; i++) s.Update(35_000, 50);  // 20 level ticks
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_TodOnlyAfterTocAndRealAltitudeLoss()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        s.Update(20_000, 1500);
        for (int i = 0; i < 20; i++) s.Update(35_000, 50);  // TOC -> OFF
        // 15 descent ticks AND >1000 ft below the 35,000 peak
        for (int i = 0; i < 15; i++) s.Update(33_500, -800);
        Assert.Equal(new[] { false, true }, signs);
    }

    [Fact]
    public void TocTod_TurbulenceSpikeDoesNotFireTod()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        s.Update(20_000, 1500);
        for (int i = 0; i < 20; i++) s.Update(35_000, 50);  // TOC -> OFF
        // Momentary turbulence downdrafts at cruise: brief -800 spikes, but altitude
        // never sustains a 1000 ft loss (bounces back to 34,900).
        for (int i = 0; i < 15; i++) { s.Update(34_900, -800); s.Update(35_000, 400); }
        Assert.Equal(new[] { false }, signs);  // no TOD
    }

    [Fact]
    public void TocTod_TodRequiresTocFirst()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        // Sustained descent from a high altitude but TOC never fired (never levelled).
        s.Update(35_000, -900);
        for (int i = 0; i < 20; i++) s.Update(35_000 - i * 900, -900);
        Assert.DoesNotContain(true, signs);
    }
}
