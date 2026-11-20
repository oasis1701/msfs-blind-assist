// Tests for MSFSBlindAssist.Navigation.UndrivenClearanceStart.
// VTBS 02R, 2026-11-18: cleared E7, E8, D6 after landing, but the aircraft vacated one exit early, at
// E10, onto the parallel E. The router met E7 only at its junction with E8, so the route ran up E,
// out along E8 to that junction and straight back. Equator fixture: metres = degrees x 111132.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class UndrivenClearanceStartTests
{
    private static TaxiPath P(double la1, double lo1, double la2, double lo2, string name) => new()
    {
        StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75.0,
    };

    private static int NodeAt(TaxiGraph g, double lat, double lon)
        => g.Nodes.Values.OrderBy(n => TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude)).First().NodeId;

    // The parallel E runs north. E9 (where the aircraft stopped after vacating) joins it at the south
    // end. 556 m north, E8 leaves west to the planned exit E7, and D6 leaves east to the stand.
    private static TaxiGraph MissedExit() => TaxiGraph.Build(new List<TaxiPath>
    {
        P(0, -0.0005, 0, 0, "E9"),
        P(0, 0, 0.005, 0, "E"), P(0.005, 0, 0.007, 0, "E"),
        P(0.005, 0, 0.005, -0.001, "E8"),
        P(0.005, -0.001, 0.003, -0.003, "E7"),
        P(0.005, 0, 0.005, 0.004, "D6"),
    }, new List<ParkingSpot>(), new List<StartPosition>());

    private static int CountFrom(TaxiGraph g, params string[] clearance)
        => UndrivenClearanceStart.Count(new TaxiRouter(g), NodeAt(g, 0, 0), NodeAt(g, 0.005, 0.004), clearance,
            destinationIsRunway: false, out _);

    private static TaxiRoute? RouteFrom(TaxiGraph g, params string[] clearance)
    {
        UndrivenClearanceStart.Count(new TaxiRouter(g), NodeAt(g, 0, 0), NodeAt(g, 0.005, 0.004), clearance,
            destinationIsRunway: false, out var route);
        return route;
    }

    // Route loading takes this route as built, so the router runs once and logs one route.
    [Fact]
    public void Count_hands_back_the_route_for_the_clearance_it_settled_on()
    {
        var route = RouteFrom(MissedExit(), "E7", "E8", "D6");

        Assert.NotNull(route);
        Assert.Equal(new[] { "E", "D6" }, route!.Segments.Select(s => s.TaxiwayName).Distinct());
    }

    [Fact]
    public void Count_hands_back_the_full_clearances_route_when_nothing_is_left_out()
    {
        var route = RouteFrom(MissedExit(), "E", "D6");

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(new[] { "E", "D6" }, route.Segments.Select(s => s.TaxiwayName).Distinct());
    }

    [Fact]
    public void Count_hands_back_the_fallback_route_when_the_router_cannot_honour_the_clearance()
    {
        var route = RouteFrom(MissedExit(), "ZZ", "D6");

        Assert.NotNull(route);
        Assert.NotNull(route!.ConstrainedFallbackReason);
    }

    [Fact]
    public void Count_finds_the_cleared_taxiway_the_route_would_double_back_at()
    {
        Assert.Equal(1, CountFrom(MissedExit(), "E7", "E8", "D6"));
    }

    [Fact]
    public void Count_is_zero_when_the_route_drives_the_first_cleared_taxiway()
    {
        Assert.Equal(0, CountFrom(MissedExit(), "E", "D6"));
    }

    // E8 is met only at its junction with D6, but the route carries straight on: it stays cleared.
    [Fact]
    public void Count_is_zero_when_the_route_passes_through_the_first_cleared_taxiways_junction()
    {
        Assert.Equal(0, CountFrom(MissedExit(), "E8", "D6"));
    }

    [Fact]
    public void Count_is_zero_for_a_single_cleared_taxiway()
    {
        Assert.Equal(0, CountFrom(MissedExit(), "E7"));
    }

    [Fact]
    public void Count_is_zero_when_the_router_cannot_honour_the_clearance()
    {
        Assert.Equal(0, CountFrom(MissedExit(), "ZZ", "D6"));
    }

    // --- picks ---------------------------------------------------------------------

    [Fact]
    public void PicksAllow_refuses_when_a_pick_is_keyed_to_a_taxiway_that_would_go()
    {
        Assert.False(UndrivenClearanceStart.PicksAllow(2, new[] { 1 }, null, null));
        Assert.False(UndrivenClearanceStart.PicksAllow(2, null, new[] { 0 }, null));
        Assert.False(UndrivenClearanceStart.PicksAllow(2, null, null, new[] { 1 }));
    }

    [Fact]
    public void PicksAllow_accepts_picks_on_the_taxiways_that_stay()
    {
        Assert.True(UndrivenClearanceStart.PicksAllow(2, new[] { 2 }, new[] { 5 }, null));
        Assert.True(UndrivenClearanceStart.PicksAllow(2, null, null, null));
    }

    // Hold-short, runway, taxiway, holding-point and Turn picks are all keyed by row. Moving them with
    // the rows that remain is ClearanceRows' job (ClearanceRowsTests).
    [Fact]
    public void PicksAllow_refuses_on_any_one_of_the_pick_lists()
    {
        Assert.False(UndrivenClearanceStart.PicksAllow(1, null, null, null, null, new[] { 0 }));
        Assert.True(UndrivenClearanceStart.PicksAllow(1, new[] { 1 }, null, new[] { 2 }, null, new[] { 3 }));
    }

    // --- a route already built for the full clearance ------------------------------

    // Route loading has often just built this route while choosing a start on the runway: Count takes
    // it as its first answer instead of asking the router again.
    [Fact]
    public void Count_starts_from_a_full_clearance_route_it_is_handed()
    {
        var g = MissedExit();
        var router = new TaxiRouter(g);
        var built = router.FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 0.005, 0.004), new List<string> { "E", "D6" });

        int count = UndrivenClearanceStart.Count(router, NodeAt(g, 0, 0), NodeAt(g, 0.005, 0.004), new[] { "E", "D6" },
            destinationIsRunway: false, out var route, fullClearanceRoute: built);

        Assert.Equal(0, count);
        Assert.Same(built, route);
    }

    [Fact]
    public void Count_still_leaves_out_from_a_handed_route_that_doubles_back()
    {
        var g = MissedExit();
        var router = new TaxiRouter(g);
        var clearance = new List<string> { "E7", "E8", "D6" };
        var built = router.FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 0.005, 0.004), clearance);

        int count = UndrivenClearanceStart.Count(router, NodeAt(g, 0, 0), NodeAt(g, 0.005, 0.004), clearance,
            destinationIsRunway: false, out var route, fullClearanceRoute: built);

        Assert.Equal(1, count);
        Assert.NotSame(built, route);
    }

    // --- note ----------------------------------------------------------------------

    // A first cleared taxiway the route turns straight back at, but that could not be left out (a pick
    // keyed to it, the only cleared taxiway, a clearance the router cannot honour without it): the
    // route is kept, on the pavement, and the pilot is told what it does.
    [Fact]
    public void TurnBackNote_says_the_route_goes_out_to_the_taxiway_and_turns_back()
    {
        Assert.Equal("Taxiway E7 is not on the way from here. The route goes out to it and turns back the way it came.",
            UndrivenClearanceStart.TurnBackNote("E7"));
    }

    [Fact]
    public void Note_names_one_taxiway()
    {
        Assert.Equal("Taxiway E7 is not on the way from here and was left out.",
            UndrivenClearanceStart.Note(new[] { "E7" }));
    }

    [Fact]
    public void Note_names_several_taxiways()
    {
        Assert.Equal("Taxiways E7 and E8 are not on the way from here and were left out.",
            UndrivenClearanceStart.Note(new[] { "E7", "E8" }));
        Assert.Equal("Taxiways A, B and C are not on the way from here and were left out.",
            UndrivenClearanceStart.Note(new[] { "A", "B", "C" }));
    }
}
