// Characterization tests for RunwayEndCountdownGate — when the runway-end countdown hands off
// to "vacated" or to backtracking (PR #236 review, finding F1: the old stop-or-turn rule had no
// distance gate, so a normal mid-runway vacate was told "End of runway. Turn around.") — and for
// the spoken backtrack heading (finding F9: it spoke TRUE heading).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayEndCountdownGateTests
{
    private static RunwayEndCountdownAction Decide(
        double distToEndFeet, double groundSpeedKts, double headingDeltaAbsDeg,
        bool clear = false, bool noticeGiven = false)
        => RunwayEndCountdownGate.Decide(distToEndFeet, groundSpeedKts, headingDeltaAbsDeg, clear, noticeGiven);

    // PR #236 follow-up. "At the runway end" decides whether a stop means "turn around and
    // backtrack" or just a notice, so it must not move when the pilot switches the app between
    // feet and metres. It used to be read out of the SPOKEN milestone table by position, and that
    // table is built from the display-unit setting — 500 ft in feet mode, 150 m (492 ft) in metres.
    [Fact]
    public void The_runway_end_window_is_a_fixed_distance_not_the_spoken_milestone()
    {
        Assert.Equal(500.0, RolloutExitGate.NearRunwayEndFeet, 3);
        Assert.Equal(RunwayEndCountdownAction.BacktrackAtEnd, Decide(499, 1, 0));
        Assert.Equal(RunwayEndCountdownAction.StoppedMidRunwayNotice, Decide(501, 1, 0));
    }

    [Fact]
    public void Rolling_straight_down_the_runway_continues_the_countdown()
        => Assert.Equal(RunwayEndCountdownAction.Continue, Decide(6000, 120, 1));

    [Fact]
    public void A_crabbed_slow_touchdown_far_from_the_end_does_not_backtrack()
        => Assert.Equal(RunwayEndCountdownAction.Continue, Decide(6000, 70, 20));

    [Fact]
    public void Turning_off_mid_runway_waits_until_clear_then_reports_vacated()
    {
        Assert.Equal(RunwayEndCountdownAction.Continue, Decide(5000, 25, 40));
        Assert.Equal(RunwayEndCountdownAction.Vacated, Decide(5000, 20, 60, clear: true));
    }

    [Fact]
    public void Laterally_clear_wins_over_every_other_rule()
        => Assert.Equal(RunwayEndCountdownAction.Vacated, Decide(100, 0, 170, clear: true));

    [Fact]
    public void Turning_around_mid_runway_backtracks_without_claiming_the_runway_end()
        => Assert.Equal(RunwayEndCountdownAction.BacktrackMidRunway, Decide(4000, 5, 155));

    [Fact]
    public void Turning_around_near_the_end_is_the_end_of_runway_backtrack()
        => Assert.Equal(RunwayEndCountdownAction.BacktrackAtEnd, Decide(300, 5, 160));

    [Fact]
    public void Stopping_near_or_past_the_end_backtracks()
    {
        Assert.Equal(RunwayEndCountdownAction.BacktrackAtEnd, Decide(400, 2, 0));
        Assert.Equal(RunwayEndCountdownAction.BacktrackAtEnd, Decide(-50, 1, 20));
    }

    // PR #236 follow-up. A pilot rolling to the end of the runway and turning onto the taxiway
    // there is doing the RIGHT thing, and at 15-90 degrees of heading change that is
    // indistinguishable from the start of a turnaround. The old rule committed immediately and
    // said "End of runway. Turn around." over a correct turn-off — and because backtracking is a
    // different guidance state, the "Runway vacated" rule below could never take it back. Keep
    // counting instead: a real turn-off becomes laterally clear, and a real turnaround passes
    // 150 degrees or stops. Only a STOP near the end still means there is no pavement left.
    [Fact]
    public void Turning_off_at_the_end_keeps_counting_until_the_aircraft_is_clear()
    {
        Assert.Equal(RunwayEndCountdownAction.Continue, Decide(450, 20, 30));
        Assert.Equal(RunwayEndCountdownAction.Continue, Decide(200, 12, 75));
        Assert.Equal(RunwayEndCountdownAction.Vacated, Decide(150, 10, 88, clear: true));
    }

    [Fact]
    public void A_turn_past_the_runway_end_still_waits_for_the_aircraft_to_clear()
        => Assert.Equal(RunwayEndCountdownAction.Continue, Decide(-50, 10, 20));

    [Fact]
    public void Stopping_mid_runway_gives_one_notice_then_waits()
    {
        Assert.Equal(RunwayEndCountdownAction.StoppedMidRunwayNotice, Decide(4000, 1, 2));
        Assert.Equal(RunwayEndCountdownAction.Continue, Decide(4000, 1, 2, noticeGiven: true));
    }

    [Fact]
    public void A_heading_swing_at_90_kt_or_more_is_not_a_turn()
        => Assert.Equal(RunwayEndCountdownAction.Continue, Decide(400, 95, 20));

    [Theory]
    [InlineData(-14.54, 165)]   // KSEA 34L: true 0.34, variation +14.88
    [InlineData(84.48, 264)]    // CYVR 08L: true 99.93, variation +15.45
    [InlineData(165.47, 345)]   // KSEA 16L: true 180.35, variation +14.88
    [InlineData(301.4, 121)]
    [InlineData(180.2, 360)]    // wraps to 0.2 and is spoken as 360, never 0
    [InlineData(179.6, 360)]    // rounds up to 360
    [InlineData(-180.0, 360)]
    public void The_backtrack_heading_is_the_magnetic_reciprocal_spoken_1_to_360(double headingMag, int expected)
        => Assert.Equal(expected, RunwayHeadings.SpokenReciprocalMagnetic(headingMag));
}
