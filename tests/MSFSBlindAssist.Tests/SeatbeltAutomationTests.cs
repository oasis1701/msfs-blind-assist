using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class SeatbeltAutomationTests
{
    private readonly List<bool> signs = new();
    private readonly List<string> spoken = new();
    private DateTime now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private SeatbeltAutomation Make(FoSeatbeltMode mode, int? plannedCruiseFt = null)
        => new(signs.Add, spoken.Add, () => now) { Mode = mode, PlannedCruiseFt = plannedCruiseFt };

    // One position sample, one second after the previous one (the FO window's own cadence).
    private void Tick(SeatbeltAutomation s, double alt, double vs, bool onGround = false, double seconds = 1)
    {
        now = now.AddSeconds(seconds);
        s.Update(alt, vs, onGround);
    }

    private void Hold(SeatbeltAutomation s, double alt, double vs, int seconds, bool onGround = false)
    {
        for (int i = 0; i < seconds; i++) Tick(s, alt, vs, onGround);
    }

    // A climb from the runway to the given altitude, in 1,000 ft steps at 2,000 fpm.
    private void ClimbTo(SeatbeltAutomation s, double fromFt, double toFt)
    {
        for (double a = fromFt; a < toFt; a += 1000) Tick(s, a, 2000, seconds: 30);
    }

    // A descent in 1,000 ft steps at -1,500 fpm.
    private void DescendTo(SeatbeltAutomation s, double fromFt, double toFt)
    {
        for (double a = fromFt; a > toFt; a -= 1000) Hold(s, a, -1500, 40);
    }

    // ---- 10k mode ----
    [Fact]
    public void TenK_OffClimbingThrough_OnDescendingThrough()
    {
        var s = Make(FoSeatbeltMode.TenThousand);
        Tick(s, 9_600, 1500);    // baseline: below
        Tick(s, 10_400, 1500);   // climbed through -> OFF
        Tick(s, 10_400, -1500);  // baseline reset above
        Tick(s, 9_600, -1500);   // descended through -> ON
        Assert.Equal(new[] { false, true }, signs);
    }

    [Fact]
    public void TenK_HysteresisBandDoesNotThrash()
    {
        var s = Make(FoSeatbeltMode.TenThousand);
        Tick(s, 9_600, 1500);
        Tick(s, 10_400, 1500);   // OFF
        Tick(s, 10_100, -50);    // inside band, no crossing
        Tick(s, 10_400, 50);
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void Disabled_NeverActuates()
    {
        var s = Make(FoSeatbeltMode.Disabled);
        Tick(s, 9_600, 1500);
        Tick(s, 10_400, 1500);
        Assert.Empty(signs);
    }

    // ---- TOC/TOD mode, with the SimBrief cruise altitude known ----
    [Fact]
    public void TocTod_TocTurnsOffAfterSustainedLevelAtPlannedCruise()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 35_000);
        Hold(s, 35_000, 50, 25);
        Assert.Equal(new[] { false }, signs);
        Assert.Contains("Cruise. Seat belt signs off.", spoken);
    }

    [Fact]
    public void TocTod_LevelOffInTheClimbBelowPlannedCruiseIsNotTopOfClimb()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 15_000);
        Hold(s, 15_000, 0, 300);          // ATC holds the climb at FL150 for five minutes
        Assert.Empty(signs);
        ClimbTo(s, 15_000, 35_000);
        Hold(s, 35_000, 0, 25);           // the real top of climb
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_CruiseAssignedSlightlyBelowThePlanStillCountsAsCruise()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 36_000);
        ClimbTo(s, 0, 34_000);
        Hold(s, 34_000, 0, 25);           // ATC gave FL340, not the planned FL360
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_TodOnlyAfterTocAndRealAltitudeLoss()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 35_000);
        Hold(s, 35_000, 50, 25);          // TOC -> OFF
        Hold(s, 33_500, -800, 16);        // 15 s descending AND >1000 ft below the peak
        Assert.Equal(new[] { false, true }, signs);
    }

    [Fact]
    public void TocTod_TurbulenceSpikeDoesNotFireTod()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 35_000);
        Hold(s, 35_000, 50, 25);          // TOC -> OFF
        // Brief -800 downdrafts at cruise; the altitude never loses 1,000 ft.
        for (int i = 0; i < 15; i++) { Tick(s, 34_900, -800); Tick(s, 35_000, 400); }
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_TodRequiresTocFirst()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        // Sustained descent from a high altitude but TOC never fired (never levelled).
        for (int i = 0; i < 20; i++) Tick(s, 35_000 - i * 900, -900);
        Assert.DoesNotContain(true, signs);
    }

    // ---- the reported defect: level-offs on a long arrival ----
    [Fact]
    public void TocTod_ArrivalLevelOffsNeverTurnTheSignsOffAgain()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 35_000);
        Hold(s, 35_000, 0, 60);           // TOC -> OFF
        DescendTo(s, 35_000, 24_000);     // TOD -> ON
        Hold(s, 24_000, 0, 300);          // step on the arrival
        DescendTo(s, 24_000, 15_000);
        Hold(s, 15_000, 0, 300);
        DescendTo(s, 15_000, 10_000);
        Hold(s, 9_800, -1500, 20);        // briefly below the 10,000 ft floor
        Hold(s, 10_050, 0, 300);          // then level just above it (10,000 ft restriction)
        DescendTo(s, 10_050, 3_000);
        Assert.Equal(new[] { false, true }, signs);
    }

    [Fact]
    public void TocTod_StateStartedDuringTheDescentNeverCallsALevelOffCruise()
    {
        // The FO window (or a SimBrief load, which resets the automation) during the
        // descent: the first level-off it sees is on the arrival, not at cruise.
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        DescendTo(s, 25_000, 20_000);
        Hold(s, 20_000, 0, 600);
        DescendTo(s, 20_000, 12_000);
        Hold(s, 12_000, 0, 600);
        Assert.Empty(signs);
    }

    [Fact]
    public void TocTod_WithoutAPlanAStateStartedAtAnArrivalLevelOffNeverCallsItCruise()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        Hold(s, 20_000, 0, 600);          // first sample already level, ten minutes of it
        DescendTo(s, 20_000, 12_000);
        Hold(s, 12_000, 0, 600);
        Assert.Empty(signs);
    }

    [Fact]
    public void TocTod_ADescentOnTheClimbOutBelowTenThousandDoesNotEndTheClimb()
    {
        // Climbed to 8,000 ft, ATC then sends the aircraft down to 6,000 ft before
        // clearing it higher: the leg has not started its descent from cruise.
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 8_000);
        Hold(s, 8_000, 0, 60);
        DescendTo(s, 8_000, 6_000);
        Hold(s, 6_000, 0, 120);
        ClimbTo(s, 6_000, 35_000);
        Hold(s, 35_000, 0, 25);
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_AClimbSteppedDownAndBackUpStillFindsTheRealTopOfClimb()
    {
        // Held at FL310, taken down to FL290 for traffic, then cleared to FL350.
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 31_000);
        Hold(s, 31_000, 0, 120);
        DescendTo(s, 31_000, 29_000);
        Hold(s, 29_000, 0, 120);
        ClimbTo(s, 29_000, 35_000);
        Hold(s, 35_000, 0, 25);
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_ACruiseFarBelowThePlanCountsAfterTenMinutesLevelAtTheTop()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 31_000);
        Hold(s, 31_000, 0, 300);          // five minutes: still possibly a climb hold
        Assert.Empty(signs);
        Hold(s, 31_000, 0, 301);          // past ten minutes level at the top: the cruise
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_AStateStartedAtCruiseDoesNotTurnTheSignsOff()
    {
        // SimBrief loaded (which resets the automation) or the FO window first opened in
        // cruise: no climb seen, so it cannot know the pilot has not set the signs on for a
        // reason — it leaves them alone.
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        Hold(s, 35_000, 0, 120);
        Assert.Empty(signs);
    }

    [Fact]
    public void TocTod_ThePlanIsForgottenOnLandingSoAStalePlanCannotMatchAClimbStep()
    {
        // Flight 1 filed FL200; flight 2 flies without reloading SimBrief.
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 20_000);
        ClimbTo(s, 0, 20_000);
        Hold(s, 20_000, 0, 25);                   // flight 1 TOC
        DescendTo(s, 20_000, 1_000);              // flight 1 TOD
        Hold(s, 0, 0, 60, onGround: true);
        Assert.Null(s.PlannedCruiseFt);
        ClimbTo(s, 0, 20_000);
        Hold(s, 20_000, 0, 90);                   // flight 2's climb held at FL200
        Assert.Equal(new[] { false, true }, signs);
    }

    [Fact]
    public void TocTod_APlanLoadedOnTheGroundBeforeTheFirstFlightIsKept()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        Hold(s, 0, 0, 60, onGround: true);
        Assert.Equal(35_000, s.PlannedCruiseFt);
    }

    [Fact]
    public void TocTod_AnAirborneZeroAltitudeSampleIsIgnored()
    {
        // A bogus reading must not fake a climb for a state started at cruise.
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        Tick(s, 0, 0);
        Hold(s, 35_000, 0, 60);
        Assert.Empty(signs);
    }

    // ---- TOC/TOD mode, no SimBrief plan ----
    [Fact]
    public void TocTod_WithoutAPlanAShortLevelOffInTheClimbIsNotTopOfClimb()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        ClimbTo(s, 0, 15_000);
        Hold(s, 15_000, 0, 90);           // a minute and a half at FL150
        Assert.Empty(signs);
        ClimbTo(s, 15_000, 35_000);
        Hold(s, 35_000, 0, 200);          // cruise: a sustained level-off at the top
        Assert.Equal(new[] { false }, signs);
    }

    [Fact]
    public void TocTod_LevelOffBelowThePeakIsNeverTopOfClimb()
    {
        var s = Make(FoSeatbeltMode.TocTod);
        ClimbTo(s, 0, 30_000);
        DescendTo(s, 30_000, 20_000);     // never levelled at the top
        Hold(s, 20_000, 0, 600);
        Assert.Empty(signs);
    }

    // ---- timing is by the clock, not by how many samples arrive ----
    [Fact]
    public void TocTod_ManySamplesInAFewSecondsDoNotConfirmALevelOff()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 35_000);
        // Several features request positions; 40 deliveries in five seconds is not 20 s level.
        for (int i = 0; i < 40; i++) Tick(s, 35_000, 0, seconds: 0.125);
        Assert.Empty(signs);
    }

    // ---- leg re-arming ----
    [Fact]
    public void TocTod_ReArmsForTheNextLegOnlyOnTheGround()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        ClimbTo(s, 0, 35_000);
        Hold(s, 35_000, 0, 25);
        DescendTo(s, 35_000, 1_000);
        Hold(s, 0, 0, 60, onGround: true);        // landed, turnaround
        s.PlannedCruiseFt = 35_000;               // next flight's SimBrief plan loaded
        ClimbTo(s, 0, 35_000);
        Hold(s, 35_000, 0, 25);
        DescendTo(s, 35_000, 30_000);
        Assert.Equal(new[] { false, true, false, true }, signs);
    }

    [Fact]
    public void TocTod_NeverActuatesOnTheGround()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 5_000);
        Hold(s, 12_000, 0, 120, onGround: true);  // high-elevation field, parked
        Assert.Empty(signs);
    }

    [Fact]
    public void TocTod_ResetKeepsThePlannedCruise()
    {
        var s = Make(FoSeatbeltMode.TocTod, plannedCruiseFt: 35_000);
        s.Reset();
        Assert.Equal(35_000, s.PlannedCruiseFt);
    }
}
