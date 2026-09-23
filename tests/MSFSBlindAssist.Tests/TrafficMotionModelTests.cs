// How traffic is moving. PR #247 review R14: the nose heading is not the direction of travel for an
// aircraft pushing back, and "coming toward you" on the route must be judged against the route, not
// against the pilot's own heading.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TrafficMotionModelTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    private const double DegLatPerMetre = 1.0 / 110540.0;
    private const double DegLonPerMetreAt50 = 1.0 / (111320.0 * 0.642788);

    private static PositionFix At(double northM, double eastM, double seconds)
        => new(50.0 + northM * DegLatPerMetre, eastM * DegLonPerMetreAt50, T0.AddSeconds(seconds));

    // ── local bearing ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 1.0, 0.0)]       // due north
    [InlineData(1.0, 0.0, 90.0)]      // due east
    [InlineData(0.0, -1.0, 180.0)]    // due south
    [InlineData(-1.0, 0.0, 270.0)]    // due west
    [InlineData(0.0, 0.0, 0.0)]       // zero offset
    public void LocalBearingDeg_computes_true_bearing(double dxEast, double dyNorth, double expectedDeg)
        => Assert.Equal(expectedDeg, GroundTrafficLogic.LocalBearingDeg(dxEast, dyNorth), 1e-9);

    // ── effective direction ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_pushback_moves_opposite_to_its_nose()
        => Assert.Equal(180.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 0), At(-5, 0, 2)), 0);

    [Fact]
    public void Forward_taxi_keeps_the_nose()
        => Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 5.0, At(0, 0, 0), At(5, 0, 1)));

    [Fact]
    public void A_skid_short_of_reversing_keeps_the_nose()
        => Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 0), At(0, 5, 2)));

    [Theory]
    [InlineData(0.3, 0, 0)]      // too slow to trust a track
    [InlineData(2.0, -1, 0)]     // moved too little
    public void Noisy_or_slow_keeps_the_nose(double gs, double northM, double eastM)
        => Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, gs, At(0, 0, 0), At(northM, eastM, 2)));

    [Fact]
    public void A_stale_or_missing_previous_fix_keeps_the_nose()
    {
        Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 0), At(-20, 0, 8)));
        Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, null, At(-20, 0, 1)));
    }

    // The two timestamp cases below use the pushback geometry: nose north, moved 20 m due south
    // (track 180°, beyond ReversingAngleDeg). Each first shows that geometry DOES flip to the track
    // with a valid dt, so only the timestamp guard can be what keeps the nose.

    [Fact]
    public void Same_timestamp_between_fixes_keeps_the_nose()
    {
        Assert.Equal(180.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 0), At(-20, 0, 1)), 0);
        Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 1), At(-20, 0, 1)));
    }

    [Fact]
    public void Previous_fix_later_than_current_keeps_the_nose()
    {
        Assert.Equal(180.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 1), At(-20, 0, 5)), 0);
        Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, At(0, 0, 5), At(-20, 0, 1)));
    }

    // ── track history (PR #247 B1 review, implementer concern 2) ─────────────────────────
    // One previous sample is not enough at the 1 s cadence: a 2-4 kt pushback covers 1-2 m per
    // sample, under ReversingMinMoveM, and a TCAS sweep can land milliseconds after the monitor's
    // own. A nearby pushback is exactly what switches the 1 s cadence on.

    private static PositionFix At(double northM, double eastM, double seconds, double altitudeFt)
        => At(northM, eastM, seconds) with { AltitudeFt = altitudeFt };

    // A 2 kt pushback: 1.03 m/s due south, nose north.
    private const double PushbackMps = 2 * 0.514444;
    private static PositionFix Pushback(double seconds) => At(-PushbackMps * seconds, 0, seconds);

    [Fact]
    public void A_two_knot_pushback_is_read_from_the_newest_sample_three_metres_back()
    {
        var history = new List<PositionFix> { Pushback(0), Pushback(1), Pushback(2), Pushback(3) };
        var current = Pushback(4);

        Assert.Equal(Pushback(1), GroundTrafficLogic.TrackAnchor(history, current));
        Assert.Equal(180.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0,
            GroundTrafficLogic.TrackAnchor(history, current), current), 0);
        // The old behaviour — the immediately preceding sample, 1 m back — reads the nose. This is
        // why the history exists.
        Assert.Equal(0.0, GroundTrafficLogic.EffectiveDirection(0.0, 2.0, Pushback(3), current));
    }

    [Fact]
    public void A_sample_milliseconds_before_the_current_one_does_not_hide_an_older_anchor()
    {
        // A TCAS sweep landing 5 ms after the monitor's own.
        var history = new List<PositionFix> { Pushback(0), Pushback(1), Pushback(2), Pushback(3), Pushback(3.995) };
        Assert.Equal(Pushback(1), GroundTrafficLogic.TrackAnchor(history, Pushback(4)));
    }

    [Fact]
    public void Nothing_three_metres_back_within_five_seconds_is_no_anchor()
    {
        // 0.5 m/s: 2 m over the whole window.
        var history = new List<PositionFix> { At(0, 0, 0), At(-0.5, 0, 1), At(-1, 0, 2), At(-1.5, 0, 3) };
        Assert.Null(GroundTrafficLogic.TrackAnchor(history, At(-2, 0, 4)));
        Assert.Null(GroundTrafficLogic.TrackAnchor(new List<PositionFix>(), At(-2, 0, 4)));
    }

    [Fact]
    public void A_sample_more_than_five_seconds_old_is_never_the_anchor()
    {
        // 20 m back: exactly 5 s old it qualifies; 5.5 s old it does not.
        Assert.Equal(At(18, 0, -1), GroundTrafficLogic.TrackAnchor(
            new List<PositionFix> { At(18, 0, -1), At(-1, 0, 2), At(-1.5, 0, 3) }, At(-2, 0, 4)));
        Assert.Null(GroundTrafficLogic.TrackAnchor(
            new List<PositionFix> { At(18, 0, -1.5), At(-1, 0, 2), At(-1.5, 0, 3) }, At(-2, 0, 4)));
    }

    // ── climb rate ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClimbFpm_is_the_altitude_change_per_minute()
        => Assert.Equal(1800.0, GroundTrafficLogic.ClimbFpm(new[] { At(0, 0, 0, 100) }, At(0, 0, 2, 160))!.Value, 6);

    [Fact]
    public void ClimbFpm_needs_both_altitudes()
    {
        Assert.Null(GroundTrafficLogic.ClimbFpm(new[] { At(0, 0, 0, 100) }, At(0, 0, 2)));   // current unknown
        Assert.Null(GroundTrafficLogic.ClimbFpm(new[] { At(0, 0, 0) }, At(0, 0, 2, 160)));   // baseline unknown
    }

    [Fact]
    public void ClimbFpm_needs_half_a_second_between_its_samples()
    {
        Assert.Null(GroundTrafficLogic.ClimbFpm(new[] { At(0, 0, 1.6, 150) }, At(0, 0, 2, 160)));
        // A sample too new is skipped for the newest one old enough.
        Assert.Equal(1800.0, GroundTrafficLogic.ClimbFpm(
            new[] { At(0, 0, 0, 100), At(0, 0, 1.8, 150) }, At(0, 0, 2, 160))!.Value, 6);
    }

    [Fact]
    public void ClimbFpm_never_uses_a_sample_more_than_ten_seconds_old()
    {
        Assert.Equal(360.0, GroundTrafficLogic.ClimbFpm(new[] { At(0, 0, -8, 100) }, At(0, 0, 2, 160))!.Value, 6);
        Assert.Null(GroundTrafficLogic.ClimbFpm(new[] { At(0, 0, -9, 100) }, At(0, 0, 2, 160)));
    }

    // ── the history itself ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddToHistory_ignores_a_sample_that_is_not_newer_than_the_last()
    {
        var h = new List<PositionFix>();
        GroundTrafficLogic.AddToHistory(h, At(0, 0, 5));
        GroundTrafficLogic.AddToHistory(h, At(1, 0, 5));   // the same instant
        GroundTrafficLogic.AddToHistory(h, At(2, 0, 4));   // older
        Assert.Equal(new[] { At(0, 0, 5) }, h);
    }

    [Fact]
    public void AddToHistory_drops_samples_more_than_ten_seconds_older_than_the_newest()
    {
        var h = new List<PositionFix>();
        GroundTrafficLogic.AddToHistory(h, At(0, 0, 0));
        GroundTrafficLogic.AddToHistory(h, At(0, 0, 1));
        GroundTrafficLogic.AddToHistory(h, At(0, 0, 10));     // t=0 exactly 10 s older: kept
        Assert.Equal(3, h.Count);
        GroundTrafficLogic.AddToHistory(h, At(0, 0, 10.5));   // now 10.5 s older: dropped
        Assert.Equal(new[] { At(0, 0, 1), At(0, 0, 10), At(0, 0, 10.5) }, h);
    }

    [Fact]
    public void AddToHistory_keeps_at_most_twelve_samples()
    {
        var h = new List<PositionFix>();
        for (int i = 0; i < 15; i++) GroundTrafficLogic.AddToHistory(h, At(0, 0, i * 0.5));
        Assert.Equal(12, h.Count);
        Assert.Equal(At(0, 0, 1.5), h[0]);   // the three oldest went
        Assert.Equal(At(0, 0, 7.0), h[^1]);
    }

    [Fact]
    public void NewestSampleAged_bounds_are_inclusive()
    {
        var h = new[] { At(0, 0, 0) };
        Assert.Equal(At(0, 0, 0), GroundTrafficLogic.NewestSampleAged(h, T0.AddSeconds(0.5), 0.5, 5.0));
        Assert.Equal(At(0, 0, 0), GroundTrafficLogic.NewestSampleAged(h, T0.AddSeconds(5.0), 0.5, 5.0));
        Assert.Null(GroundTrafficLogic.NewestSampleAged(h, T0.AddSeconds(0.49), 0.5, 5.0));
        Assert.Null(GroundTrafficLogic.NewestSampleAged(h, T0.AddSeconds(5.01), 0.5, 5.0));
    }

    [Fact]
    public void NewestSampleAged_takes_the_newest_sample_old_enough()
        => Assert.Equal(At(0, 0, 2), GroundTrafficLogic.NewestSampleAged(
            new[] { At(0, 0, 0), At(0, 0, 2), At(0, 0, 3.8) }, T0.AddSeconds(4), 0.5, 5.0));

    // ── route-relative motion ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, 5, 0, RouteRelativeMotion.Along)]
    [InlineData(170, 5, 0, RouteRelativeMotion.Toward)]
    [InlineData(90, 5, 0, RouteRelativeMotion.Crossing)]
    [InlineData(90, 1.9, 0, RouteRelativeMotion.Stopped)]
    [InlineData(355, 5, 15, RouteRelativeMotion.Along)]     // wraps through north
    public void ClassifyAlongRoute(double direction, double gs, double segmentBearing, RouteRelativeMotion expected)
        => Assert.Equal(expected, GroundTrafficLogic.ClassifyAlongRoute(direction, gs, segmentBearing));

    [Fact]
    public void Traffic_moving_along_a_route_that_turns_back_is_along_not_toward()
    {
        // The route goes north, east, then SOUTH down a parallel taxiway. Traffic on that last leg
        // heading 180 moves the way the route goes (away from the pilot along the route), although
        // against the pilot's own heading of 000 it is "opposite direction".
        var route = new[]
        {
            new GroundTrafficRoutePoint(50.0, 0.0, "A", 0),
            new GroundTrafficRoutePoint(50.0 + 150 * DegLatPerMetre, 0.0, "A", 150),
            new GroundTrafficRoutePoint(50.0 + 150 * DegLatPerMetre, 120 * DegLonPerMetreAt50, "L", 270),
            new GroundTrafficRoutePoint(50.0 - 250 * DegLatPerMetre, 120 * DegLonPerMetreAt50, "B", 670),
        };
        var p = GroundTrafficLogic.ProjectOntoRoute(route, 50.0 - 50 * DegLatPerMetre, 120 * DegLonPerMetreAt50);
        Assert.NotNull(p);
        Assert.Equal("B", p!.Value.Taxiway);
        Assert.Equal(180.0, p.Value.SegmentBearingDeg, 0);
        Assert.Equal(RouteRelativeMotion.Along, GroundTrafficLogic.ClassifyAlongRoute(180, 10, p.Value.SegmentBearingDeg));
        Assert.Equal(TrafficMotion.OppositeDirection, GroundTrafficLogic.ClassifyMotion(0, 180, 10, 113));
    }

    [Fact]
    public void ProjectOntoRoute_reports_each_legs_bearing()
    {
        var route = new[]
        {
            new GroundTrafficRoutePoint(50.000, 0.000, "A", 0),
            new GroundTrafficRoutePoint(50.001, 0.000, "A", 110.54),
            new GroundTrafficRoutePoint(50.001, 0.001, "B", 110.54 + 71.55),
        };
        Assert.Equal(0.0, GroundTrafficLogic.ProjectOntoRoute(route, 50.0005, 0.00001)!.Value.SegmentBearingDeg, 0);
        Assert.Equal(90.0, GroundTrafficLogic.ProjectOntoRoute(route, 50.00101, 0.0005)!.Value.SegmentBearingDeg, 0);
    }
}
