// Characterization tests for ResolveHoldStop's holding-area lookback (RouteRunwayCrossings,
// HoldingAreaLookbackMetres / HoldingAreaLateralMetres).
//
// Regression pinned: CYYC 35L via C1, U (tools/VirtualPilot with VP_AMDBDIR, 2026-09-23). The route
// crosses C1's hold line 116 m from the centreline — navdata and the X-Plane painted line agree to
// 2 m — then runs diagonally down U to the runway. C1's hold was more than 150 m back along the
// route, so the stop was invented on U 61 m from the centreline, inside the painted line. Measured
// across 100 airports: 151 such stops fixed, wrong-turn warnings unchanged, RunwayCrossingSweep
// totals unchanged.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class HoldingAreaLookbackTests
{
    private static TaxiRoute RouteOf(params TaxiNode[] nodes) => new() { Segments = Route(nodes) };

    private static void Pass(TaxiRoute route) =>
        RouteRunwayCrossings.InsertRunwayHoldShorts(route, new[] { EastWest("06L", "24R") }, "");

    [Fact]
    public void Hold_line_beyond_150_m_on_a_diagonal_approach_is_still_the_stop()
    {
        // Down "C1" to its hold line 116 m out, then diagonally down "U" to the runway: ~230 m of
        // route between the hold line and the pavement (over the old 150 m, under the 250 m cap).
        var route = RouteOf(
            Node(1, 1000, 300),
            Node(2, 1000, 116, TaxiNodeType.HoldShort, "runway 06L at C1"),
            Node(3, 1050, 98), Node(4, 1100, 78), Node(5, 1150, 55), Node(6, 1180, 35),
            Node(7, 1195, 5), Node(8, 1210, -60));

        Pass(route);

        Assert.True(route.Segments[0].IsHoldShortPoint, "the stop must be C1's own hold line, 116 m out");
        Assert.All(route.Segments.Skip(1), s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void Route_that_leaves_the_runway_and_comes_back_does_not_reach_the_far_hold()
    {
        // The same hold, but the route swings AWAY from the runway after it (out to 140 m) before
        // coming back: that is not an approach through the holding area, so the far line must not be
        // reused (the EGKK double-crossing lesson) — the stop falls back near the runway as before.
        var route = RouteOf(
            Node(1, 1000, 300),
            Node(2, 1000, 116, TaxiNodeType.HoldShort, "runway 06L at C1"),
            Node(3, 1030, 140), Node(4, 1070, 100), Node(5, 1110, 60), Node(6, 1130, 35),
            Node(7, 1140, 5), Node(8, 1150, -60));

        Pass(route);

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.Contains(route.Segments, s => s.IsHoldShortPoint);
    }
}
