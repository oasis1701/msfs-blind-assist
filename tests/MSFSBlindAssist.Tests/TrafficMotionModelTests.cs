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

    [Fact]
    public void Same_timestamp_between_fixes_keeps_the_nose()
    {
        // When two fixes have the same timestamp (dt = 0), even though they are far apart
        // and the track points away from the nose heading, keep the nose heading.
        Assert.Equal(90.0, GroundTrafficLogic.EffectiveDirection(90.0, 2.0, At(0, 0, 1), At(-100, 0, 1)));
    }

    [Fact]
    public void Previous_fix_later_than_current_keeps_the_nose()
    {
        // When the previous fix is timestamped later than the current one (dt < 0),
        // keep the nose heading.
        Assert.Equal(45.0, GroundTrafficLogic.EffectiveDirection(45.0, 2.0, At(0, 0, 5), At(10, 0, 1)));
    }

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
