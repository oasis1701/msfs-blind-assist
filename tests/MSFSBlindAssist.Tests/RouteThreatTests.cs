// With a taxi route to judge by, which traffic may earn "Slow down"/"Stop" (GroundTrafficLogic.IsRouteThreat).
// PR #247 author's fix 2 (9190e869): the straight-line closest approach is a threat test for MOVING
// traffic only. For a parked aircraft it assumes the pilot keeps going straight, so where the route bends
// toward one before turning away it predicted a near pass the route never makes — "Slow down, … ahead,
// 160 metres" (and "Stop" at speed) for aircraft parked 100 m beside the route; 421 such calls in his
// simulated-traffic runs over 100 airports. Anything that is not a threat drops to an Awareness ping.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteThreatTests
{
    [Fact]
    public void A_parked_aircraft_straight_ahead_of_a_bending_route_is_not_a_threat()
    {
        // The author's scenario: 170 m dead ahead, 90 m from a route that turns off after 80 m. The pilot's
        // own 15 kt puts the closest approach at 0 m in 22 s — a prediction the route never makes.
        Assert.False(GroundTrafficLogic.IsRouteThreat(nearRoute: false, dcpaM: 0.0, tcpaSec: 22.0,
            trafficGsKts: 0.0, veryClose: false));
    }

    [Fact]
    public void Moving_traffic_on_a_collision_course_is_a_threat()
        => Assert.True(GroundTrafficLogic.IsRouteThreat(nearRoute: false, dcpaM: 20.0, tcpaSec: 15.0,
            trafficGsKts: 12.0, veryClose: false));

    [Theory]
    [InlineData(2.9, false)]   // creeping: still parked for this rule
    [InlineData(3.0, true)]    // MovingTrafficKts
    public void Moving_means_at_least_three_knots(double trafficGs, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsRouteThreat(nearRoute: false, dcpaM: 10.0, tcpaSec: 10.0,
            trafficGsKts: trafficGs, veryClose: false));

    [Theory]
    [InlineData(59.9, 30.0, true)]
    [InlineData(60.0, 10.0, false)]   // passes 60 m clear
    [InlineData(10.0, 30.1, false)]   // too far off to be the next thing to act on
    public void The_closest_approach_limits_are_unchanged(double dcpa, double tcpa, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.IsRouteThreat(nearRoute: false, dcpaM: dcpa, tcpaSec: tcpa,
            trafficGsKts: 10.0, veryClose: false));

    [Fact]
    public void A_parked_aircraft_near_the_route_ahead_is_still_a_threat()
        => Assert.True(GroundTrafficLogic.IsRouteThreat(nearRoute: true, dcpaM: 200.0, tcpaSec: 0.0,
            trafficGsKts: 0.0, veryClose: false));

    [Fact]
    public void A_parked_aircraft_very_close_is_still_a_threat()
        => Assert.True(GroundTrafficLogic.IsRouteThreat(nearRoute: false, dcpaM: 200.0, tcpaSec: 0.0,
            trafficGsKts: 0.0, veryClose: true));
}
