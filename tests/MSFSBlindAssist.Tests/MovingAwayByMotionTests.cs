// "Moving away" judged from MOTION, beside the distance-rate test (GroundTrafficLogic.OpeningSpeedMps /
// IsLeadGrowing / IsOpeningByMotion). PR #247 author's fix 4 (9190e869): traffic pulling away ahead
// earned "Stop, … very close" because the distance-growth test needs two evaluations; opening is also
// judged from the relative velocity (about 2 kt) and, for traffic on the route ahead, from its growing
// lead ALONG the route — through a bend the straight-line gap is the wrong measure.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class MovingAwayByMotionTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private const double KtsToMps = 0.514444;

    // ── the relative velocity along the line of sight ──────────────────────────────────────

    [Fact]
    public void The_authors_scenario_opens_at_about_seven_metres_a_second()
    {
        // 95 m ahead (east), 20 kt against the pilot's 6 kt, both heading east.
        double opening = GroundTrafficLogic.OpeningSpeedMps(95, 0, (20 - 6) * KtsToMps, 0);
        Assert.Equal(7.2, opening, 1);
        Assert.True(GroundTrafficLogic.IsOpeningByMotion(trafficGsKts: 20, opening, leadGrowing: false));
    }

    [Fact]
    public void Closing_is_negative()
        => Assert.Equal(-5.0, GroundTrafficLogic.OpeningSpeedMps(0, 200, 0, -5), 6);

    [Fact]
    public void Crossing_the_line_of_sight_neither_opens_nor_closes()
        => Assert.Equal(0.0, GroundTrafficLogic.OpeningSpeedMps(100, 0, 0, 8), 6);

    [Fact]
    public void Only_the_component_along_the_line_of_sight_counts()
        // 3-4-5 geometry: offset (30, 40), relative velocity (3, 0) → 3 × 30 / 50.
        => Assert.Equal(1.8, GroundTrafficLogic.OpeningSpeedMps(30, 40, 3, 0), 6);

    [Fact]
    public void Within_a_metre_there_is_no_line_of_sight()
        => Assert.Equal(0.0, GroundTrafficLogic.OpeningSpeedMps(0.5, 0.5, 10, 10));

    // ── the lead along the route ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(100.0, 101.0, true)]    // 1 m/s: the threshold
    [InlineData(100.0, 100.9, false)]
    [InlineData(100.0, 95.0, false)]    // closing along the route
    public void The_lead_grows_at_a_metre_a_second_or_more(double before, double after, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsLeadGrowing(before, T0, after, T0.AddSeconds(1)));

    [Fact]
    public void Without_a_previous_lead_it_is_not_growing()
    {
        Assert.False(GroundTrafficLogic.IsLeadGrowing(double.NaN, T0, 150, T0.AddSeconds(1)));
        Assert.False(GroundTrafficLogic.IsLeadGrowing(100, T0, double.NaN, T0.AddSeconds(1)));
    }

    [Theory]
    [InlineData(0.2)]    // too close together to be a rate
    [InlineData(10.0)]   // too far apart to still describe it
    public void A_gap_outside_a_fifth_of_a_second_to_ten_seconds_is_no_basis(double seconds)
        => Assert.False(GroundTrafficLogic.IsLeadGrowing(100, T0, 200, T0.AddSeconds(seconds)));

    // ── moving traffic only ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2.9, 5.0, true, false)]    // parked for this rule, whatever the numbers say
    [InlineData(3.0, 1.0, false, true)]    // opening at the threshold
    [InlineData(3.0, 0.9, false, false)]
    [InlineData(10.0, -2.0, true, true)]   // closing in a straight line, pulling away along the route
    [InlineData(10.0, -2.0, false, false)]
    public void Opening_by_motion_needs_moving_traffic(double trafficGs, double openingMps, bool leadGrowing, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsOpeningByMotion(trafficGs, openingMps, leadGrowing));

    // ── a "Stop" withheld while the traffic opens stays withheld while it keeps pulling away ────────
    // PR #247 integration review Q2: without this, "Stop" fired the moment a departing leader's opening
    // fell below 1 m/s — a pilot following it out of a queue, catching up to its speed, heard "Stop" while
    // the gap was still growing.

    [Fact]
    public void A_withheld_Stop_stays_withheld_while_the_traffic_is_still_opening()
    {
        Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: true, stopHeld: true, trafficGsKts: 10, openingMps: 2.0));
        Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: true, stopHeld: false, trafficGsKts: 10, openingMps: 2.0));
    }

    [Theory]
    [InlineData(3.0, 0.0)]      // still moving, the gap steady
    [InlineData(3.0, -0.49)]    // closing, but slower than half a metre a second
    [InlineData(12.0, 0.6)]     // opening, too slowly to count as opening by motion
    public void A_withheld_Stop_stays_withheld_while_the_traffic_moves_at_3_kt_and_is_not_closing(double trafficGs, double openingMps)
        => Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGs, openingMps));

    [Fact]
    public void A_withheld_Stop_speaks_once_the_traffic_is_below_3_kt()
        => Assert.False(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGsKts: 2.9, openingMps: 0.0));

    [Fact]
    public void A_withheld_Stop_speaks_once_the_traffic_closes_at_half_a_metre_a_second()
        => Assert.False(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGsKts: 10, openingMps: -0.5));

    [Fact]
    public void Nothing_is_held_without_a_Stop_withheld_while_opening()
        => Assert.False(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: false, trafficGsKts: 10, openingMps: 0.0));

    // ── The HELD branch is never held inside the floor, whatever the speeds ─────────────────────────
    // PR #247 integration review R1: the held branch above otherwise never releases a pilot closing at
    // under HeldStopReleaseClosingMps on a leader still moving at MovingTrafficKts or more — at ANY gap,
    // until the leader stops or closes faster. StopHoldFloorFt is the floor below which that branch
    // releases regardless. Scoped to the held branch only: traffic genuinely opening RIGHT NOW is still
    // never a threat, however close — see the last test below.

    [Fact]
    public void A_held_Stop_speaks_once_the_gap_shrinks_below_the_floor()
        => Assert.False(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGsKts: 10,
            openingMps: 0.0, distFt: 199.9));

    [Fact]
    public void A_held_Stop_still_holds_just_outside_the_floor()
        => Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGsKts: 10,
            openingMps: 0.0, distFt: 200.1));

    [Fact]
    public void The_floor_itself_releases_the_hold()
        // "≤" the floor releases it: exactly 200 ft is inside, not outside.
        => Assert.False(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGsKts: 10,
            openingMps: 0.0, distFt: GroundTrafficLogic.StopHoldFloorFt));

    [Fact]
    public void The_floor_never_touches_traffic_that_is_genuinely_opening_right_now()
        // openingNow is unconditional (PR #247 Q2's original rule): a leader actually accelerating away is
        // not a threat regardless of distance, so the floor must not apply to this half — only to the HELD
        // half, whose "still opening" is stale evidence rather than a live measurement. Reproduces the
        // regression a first, over-broad R1 patch caused (GroundTrafficMonitorRuleTests' pulls-away-then-
        // stops-inside-the-Warning-distance test, at 60 m/197 ft — inside this very floor).
        => Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: true, stopHeld: false, trafficGsKts: 20,
            openingMps: 5.0, distFt: 150.0));

    [Fact]
    public void With_no_distance_supplied_the_floor_never_applies()
    {
        // The default keeps every pre-R1 call site (and test) byte-identical.
        Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: true, stopHeld: false, trafficGsKts: 10, openingMps: 2.0));
        Assert.True(GroundTrafficLogic.IsMovingAwayOrHeld(openingNow: false, stopHeld: true, trafficGsKts: 10, openingMps: 0.0));
    }

    [Fact]
    public void Withholding_a_Warning_escalation_as_moving_away_holds_the_Stop()
    {
        Assert.True(GroundTrafficLogic.StopHeldAfterMovingAway(GroundZone.Warning, GroundZone.Caution, stopHeld: false));
        Assert.True(GroundTrafficLogic.StopHeldAfterMovingAway(GroundZone.Warning, GroundZone.None, stopHeld: false));
    }

    [Fact]
    public void A_zone_below_Warning_releases_the_hold()
    {
        Assert.False(GroundTrafficLogic.StopHeldAfterMovingAway(GroundZone.Caution, GroundZone.Caution, stopHeld: true));
        Assert.False(GroundTrafficLogic.StopHeldAfterMovingAway(GroundZone.Awareness, GroundZone.Caution, stopHeld: true));
    }

    [Fact]
    public void A_Warning_that_is_no_escalation_leaves_the_hold_as_it_was()
    {
        Assert.True(GroundTrafficLogic.StopHeldAfterMovingAway(GroundZone.Warning, GroundZone.Warning, stopHeld: true));
        Assert.False(GroundTrafficLogic.StopHeldAfterMovingAway(GroundZone.Warning, GroundZone.Warning, stopHeld: false));
    }
}
