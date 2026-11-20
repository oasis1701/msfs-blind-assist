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

    [Fact]
    public void Shift_moves_every_pick_down_by_the_taxiways_that_went()
    {
        Assert.Equal(new List<int> { 0, 3 }, UndrivenClearanceStart.Shift(new List<int> { 2, 5 }, 2));

        var picks = UndrivenClearanceStart.Shift(new Dictionary<int, string> { [5] = "07C" }, 2);
        Assert.Equal("07C", Assert.Single(picks!).Value);
        Assert.Equal(3, picks!.Keys.Single());
    }

    // Hold-short, runway, taxiway, holding-point and Turn picks are all keyed by row.
    [Fact]
    public void PicksAllow_refuses_on_any_one_of_the_pick_lists()
    {
        Assert.False(UndrivenClearanceStart.PicksAllow(1, null, null, null, null, new[] { 0 }));
        Assert.True(UndrivenClearanceStart.PicksAllow(1, new[] { 1 }, null, new[] { 2 }, null, new[] { 3 }));
    }

    [Fact]
    public void Shift_moves_picks_of_any_kind()
    {
        var picks = UndrivenClearanceStart.Shift(new Dictionary<int, TurnSide> { [2] = TurnSide.Left, [4] = TurnSide.Right }, 1);

        Assert.Equal(TurnSide.Left, picks![1]);
        Assert.Equal(TurnSide.Right, picks[3]);
    }

    [Fact]
    public void Shift_keeps_null_as_null()
    {
        Assert.Null(UndrivenClearanceStart.Shift((List<int>?)null, 2));
        Assert.Null(UndrivenClearanceStart.Shift((Dictionary<int, string>?)null, 2));
    }

    // --- note ----------------------------------------------------------------------

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
