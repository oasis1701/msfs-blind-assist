// Where a runway entry's or crossing's hold goes: the scenery's own hold line within 150 m, else the
// nearest node clear of the runway (half-width + 10 m), else the route's start node (a start hold).
// Every candidate is at or before the runway and off the pavement of every runway, so a stop can only
// move earlier. "Passed" is measured along the route from the aircraft, which also counts as the
// route's first point. Shapes: LEBL D5, EGKK C, KBOS C, KSFO's double crossing (docs/taxi-guidance.md).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;
using AircraftPosition = MSFSBlindAssist.Navigation.RouteRunwayCrossings.AircraftPosition;
using UserRunwayHoldResult = MSFSBlindAssist.Navigation.RouteRunwayCrossings.UserRunwayHoldResult;

namespace MSFSBlindAssist.Tests;

public class RunwayHoldPlacementTests
{
    private static TaxiRoute RouteOf(params TaxiNode[] nodes) => new() { Segments = Route(nodes) };

    private static IReadOnlyList<TaxiRouteRunwayEvent> Pass(TaxiRoute route, TaxiGraph.RunwayCenterline[] runways,
        string destination = "", bool allowStartHold = true, AircraftPosition? aircraft = null)
        => RouteRunwayCrossings.InsertRunwayHoldShorts(route, runways, destination, allowStartHold, aircraft);

    private static TaxiRoute Lebl() => RouteOf(
        Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 06L at D5"),
        Node(3, 1000, 51), Node(4, 1000, 21), Node(5, 1000, -21), Node(6, 1000, -105));

    // LEBL's shape with a pilot's "end of taxiway D" stop already on the segment ending 51 m out.
    private static TaxiRoute LeblWithEndOfTaxiwayStop()
    {
        var route = RouteOf(Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 06L at D5"),
            Node(3, 1000, 70), Node(4, 1000, 51), Node(5, 1000, 21), Node(6, 1000, -21), Node(7, 1000, -105));
        route.Segments[2].IsHoldShortPoint = true;
        route.Segments[2].HoldShortRunway = "end of taxiway D";
        return route;
    }

    // Along 11/29's centerline and across 14/32 (at east 1100): every node before 14/32 is on 11/29.
    private static TaxiRoute AlongElevenAcrossFourteen() => RouteOf(
        Node(1, 900, 0), Node(2, 1000, 0), Node(3, 1060, 0), Node(4, 1100, 0), Node(5, 1140, 0), Node(6, 1300, 0));

    [Fact]
    public void The_painted_hold_line_within_150_metres_wins()
    {
        var route = Lebl();
        var ev = Assert.Single(Pass(route, new[] { EastWest("06L", "24R") }));

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 06L at D5", route.Segments[0].HoldShortRunway);
        Assert.All(route.Segments.Skip(1), s => Assert.False(s.IsHoldShortPoint));
        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.Equal("06L", ev.Designator);
        Assert.True(ev.Held);
    }

    [Fact]
    public void A_hold_node_on_the_pavement_is_never_the_stop()
    {
        var route = RouteOf(Node(1, 1000, 250), Node(2, 1000, 60),
            Node(3, 1000, 20, TaxiNodeType.HoldShort, "runway 06L"), Node(4, 1000, -60));

        Pass(route, new[] { EastWest("06L", "24R") });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 06L", route.Segments[0].HoldShortRunway);
        Assert.False(route.Segments[1].IsHoldShortPoint);

        // Nor on another runway's: a "runway 14" hold node on 11/29's pavement, from which one edge jumps across
        // 14/32, is not the stop even though it is the walk's own starting node.
        var runway14 = NorthSouth("14", "32", eastM: 1000, fromNorthM: -1500, toNorthM: 1500);
        var alongEleven = RouteOf(Node(1, 800, 100), Node(2, 960, 20, TaxiNodeType.HoldShort, "runway 14"),
            Node(3, 1040, 20), Node(4, 1200, 100));

        var events = Pass(alongEleven, new[] { EastWest("11", "29"), runway14 });

        Assert.All(alongEleven.Segments, s => Assert.False(s.IsHoldShortPoint));
        Assert.False(Assert.Single(events, e => RouteRunwayCrossings.CenterlineHasDesignator(runway14, e.Designator)).Held);
    }

    [Fact]
    public void A_hold_line_for_another_runway_ends_the_search_and_the_nearest_clear_node_is_used()
    {
        // EGKK C across 26R: the line behind it belongs to 26L.
        var route = RouteOf(Node(1, 2500, 300), Node(2, 2500, 120, TaxiNodeType.HoldShort, "runway 26L at C"),
            Node(3, 2500, 42.6), Node(4, 2500, 10), Node(5, 2500, -60));

        Pass(route, new[] { EastWest("08L", "26R") });

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.True(route.Segments[1].IsHoldShortPoint);
        Assert.Equal("runway 26R", route.Segments[1].HoldShortRunway);
    }

    [Fact]
    public void An_ILS_hold_line_within_150_metres_wins()
    {
        var route = RouteOf(Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.ILSHoldShort, "runway 06L at D5"),
            Node(3, 1000, 51), Node(4, 1000, 21), Node(5, 1000, -21), Node(6, 1000, -105));

        Pass(route, new[] { EastWest("06L", "24R") });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 06L at D5", route.Segments[0].HoldShortRunway);
        Assert.All(route.Segments.Skip(1), s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void A_hold_line_naming_no_runway_is_used_for_this_runway()
    {
        var route = RouteOf(Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "D5"),
            Node(3, 1000, 51), Node(4, 1000, 21), Node(5, 1000, -21), Node(6, 1000, -105));

        Pass(route, new[] { EastWest("06L", "24R") });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 06L at D5", route.Segments[0].HoldShortRunway);
        Assert.All(route.Segments.Skip(1), s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void A_hold_line_beyond_150_metres_is_not_reached()
    {
        // Walk 1 covers 179 m reaching node 2, so the stop falls back to the nearest clear node.
        var route = RouteOf(Node(1, 1000, 400), Node(2, 1000, 200, TaxiNodeType.HoldShort, "runway 06L at D5"),
            Node(3, 1000, 120), Node(4, 1000, 51), Node(5, 1000, 21), Node(6, 1000, -60));

        Pass(route, new[] { EastWest("06L", "24R") });

        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.True(route.Segments[2].IsHoldShortPoint);
        Assert.Equal("runway 06L", route.Segments[2].HoldShortRunway);
    }

    [Fact]
    public void A_hold_line_behind_an_existing_stop_is_never_reached()
    {
        var route = LeblWithEndOfTaxiwayStop();

        var ev = Assert.Single(Pass(route, new[] { EastWest("06L", "24R") }));

        Assert.True(ev.Held);
        Assert.False(route.Segments[0].IsHoldShortPoint);
        Assert.False(route.Segments[1].IsHoldShortPoint);
        Assert.True(route.Segments[2].IsHoldShortPoint);
        Assert.Equal("end of taxiway D", route.Segments[2].HoldShortRunway);
    }

    [Fact]
    public void A_crossing_inside_a_displaced_threshold_band_is_held()
    {
        // OMDB 12R: the start row sits 600 m inside the pavement.
        var runway = EastWest("12R", "30L");
        runway.Lon1 = Lon(600);
        var route = RouteOf(Node(1, 300, 250), Node(2, 300, 105, TaxiNodeType.HoldShort, "runway 12R at K5"),
            Node(3, 300, 51), Node(4, 300, 21), Node(5, 300, -21), Node(6, 300, -105));

        var ev = Assert.Single(Pass(route, new[] { runway }));

        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.Equal("12R", ev.Designator);
        Assert.True(ev.Held);
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 12R at K5", route.Segments[0].HoldShortRunway);
    }

    [Fact]
    public void A_node_inside_the_10_metre_margin_is_not_a_stop()
    {
        var route = RouteOf(Node(1, 1000, 300), Node(2, 1000, 200), Node(3, 1000, 35), Node(4, 1000, 10), Node(5, 1000, -60));

        Pass(route, new[] { EastWest() });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.False(route.Segments[1].IsHoldShortPoint);
    }

    [Fact]
    public void With_no_clear_node_before_the_runway_the_route_starts_held()
    {
        var route = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }));

        Assert.Equal("runway 09", route.StartHoldRunway);
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
        Assert.True(ev.Held);
    }

    [Fact]
    public void A_recalculated_route_never_starts_held()
    {
        var route = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }, allowStartHold: false));

        Assert.Null(route.StartHoldRunway);
        Assert.False(ev.Held);
    }

    [Fact]
    public void A_start_hold_the_aircraft_has_rolled_past_is_not_set()
    {
        // The aircraft stands 75 m along the route, past the runway: it is on the route, so it is not taken
        // as the route's first point, and the start node is behind it.
        var route = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }, aircraft: new AircraftPosition(Lat(-40), Lon(1000))));

        Assert.Null(route.StartHoldRunway);
        Assert.False(ev.Held);
    }

    [Fact]
    public void A_stop_the_aircraft_has_passed_is_recorded_but_not_placed()
    {
        // The aircraft stands 54 m along the route past the hold line at 105; the resolver does not fall forward.
        var route = Lebl();

        var ev = Assert.Single(Pass(route, new[] { EastWest("06L", "24R") },
            aircraft: new AircraftPosition(Lat(51), Lon(1000))));

        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
        Assert.False(ev.Held);
    }

    [Fact]
    public void A_hold_far_ahead_on_a_looping_route_is_never_treated_as_passed()
    {
        // Projected onto the hold segment's own axis, this hold read as 45 m behind the aircraft.
        var route = RouteOf(Node(1, 0, 60), Node(2, 0, 1000), Node(3, 800, 1000), Node(4, 800, 200),
            Node(5, 800, 105, TaxiNodeType.HoldShort, "runway 09 at K"), Node(6, 800, 51), Node(7, 800, 21),
            Node(8, 800, -21), Node(9, 800, -105));

        var ev = Assert.Single(Pass(route, new[] { EastWest("09", "27") }, aircraft: new AircraftPosition(Lat(60), Lon(0))));

        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.True(ev.Held);
        Assert.Same(route.Segments[3], Assert.Single(route.Segments, s => s.IsHoldShortPoint));
        Assert.Equal("runway 09 at K", route.Segments[3].HoldShortRunway);
    }

    [Fact]
    public void RouteProgressMeters_measures_along_the_route_not_along_one_segment()
    {
        var segments = Route(Node(1, 0, 0), Node(2, 0, 100), Node(3, 100, 100));

        Assert.Equal(50.0, RouteRunwayCrossings.RouteProgressMeters(segments, Lat(50), Lon(0)), 0.5);
        Assert.Equal(150.0, RouteRunwayCrossings.RouteProgressMeters(segments, Lat(100), Lon(50)), 0.5);
        Assert.Equal(0.0, RouteRunwayCrossings.RouteProgressMeters(segments, Lat(-30), Lon(-30)), 0.5);
        // 40 m beside segment 0 the aircraft has not joined the route: no progress.
        Assert.Equal(0.0, RouteRunwayCrossings.RouteProgressMeters(segments, Lat(50), Lon(-40)), 0.5);
    }

    [Fact]
    public void Standing_at_a_hold_line_starts_held_even_when_the_route_starts_on_the_runway()
    {
        // Calculate at a hold line often starts the route at the runway node ahead (KORD 22R at D).
        var route = RouteOf(Node(1, 1000, 21), Node(2, 1000, -21), Node(3, 1000, -105));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }, aircraft: new AircraftPosition(Lat(105), Lon(1000))));

        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.True(ev.Held);
        Assert.Equal("runway 09", route.StartHoldRunway);

        var withoutAircraft = RouteOf(Node(1, 1000, 21), Node(2, 1000, -21), Node(3, 1000, -105));
        Assert.Empty(Pass(withoutAircraft, new[] { EastWest() }));
    }

    [Fact]
    public void An_aircraft_on_the_runway_gets_no_passage_or_start_hold_for_a_route_leaving_it()
    {
        var route = RouteOf(Node(1, 1000, 21), Node(2, 1000, -21), Node(3, 1000, -105));

        Assert.Empty(Pass(route, new[] { EastWest() }, aircraft: new AircraftPosition(Lat(10), Lon(1000))));
        Assert.Null(route.StartHoldRunway);
    }

    [Fact]
    public void Standing_beside_the_runway_a_route_starting_along_it_still_starts_held()
    {
        // KORD 04L at G: the aircraft waits 45 m beside a route that starts on the runway and runs along it.
        // It has not joined the route, so it is not 40 m along it and the start is not passed.
        var route = RouteOf(Node(1, 1000, 0), Node(2, 1300, 0), Node(3, 1300, -60), Node(4, 1300, -200));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }, aircraft: new AircraftPosition(Lat(45), Lon(1040))));

        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.True(ev.Held);
        Assert.Equal("runway 09", route.StartHoldRunway);
    }

    private static (TaxiRoute Route, TaxiGraph.RunwayCenterline[] Runways) Intersection()
    {
        var route = RouteOf(Node(1, 1200, -200), Node(2, 1340, -60), Node(3, 1400, 0), Node(4, 1460, 60), Node(5, 1600, 200));
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1400, fromNorthM: -1400, toNorthM: 1600) };
        return (route, runways);
    }

    [Fact]
    public void An_existing_stop_is_never_walked_through_and_a_shared_stop_names_both_runways()
    {
        var (route, runways) = Intersection();

        var events = Pass(route, runways);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.Held));
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09 and runway 01", route.Segments[0].HoldShortRunway);
        Assert.All(route.Segments.Skip(1), s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void A_stop_is_never_placed_on_another_runways_pavement_and_shares_that_runways_stop()
    {
        // LEBL: 20's stop once landed on 06L/24R, 38 m before 20. The route meets 02/20 40 m from its 02 end.
        var route = RouteOf(Node(1, 1400, -300), Node(2, 1400, -105, TaxiNodeType.HoldShort, "runway 06L at D5"),
            Node(3, 1400, -51), Node(4, 1400, -21), Node(5, 1400, 21), Node(6, 1460, 70), Node(7, 1460, 200));
        var runways = new[] { EastWest("06L", "24R"), NorthSouth("02", "20", eastM: 1460, fromNorthM: 30, toNorthM: 3000) };

        var events = Pass(route, runways);

        Assert.Same(route.Segments[0], Assert.Single(route.Segments, s => s.IsHoldShortPoint));
        Assert.Equal("runway 06L at D5 and runway 02", route.Segments[0].HoldShortRunway);
        Assert.False(route.Segments[3].IsHoldShortPoint);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.Held));
    }

    [Fact]
    public void A_route_starting_on_one_runway_gets_no_start_hold_for_the_next()
    {
        var runways = new[] { EastWest("11", "29"), NorthSouth("14", "32", eastM: 1100, fromNorthM: -1500, toNorthM: 1500) };

        foreach (var aircraft in new AircraftPosition?[] { null, new AircraftPosition(Lat(0), Lon(900)) })
        {
            var route = AlongElevenAcrossFourteen();

            var ev = Assert.Single(Pass(route, runways, aircraft: aircraft));

            Assert.Equal("14", ev.Designator);
            Assert.False(ev.Held);
            Assert.Null(route.StartHoldRunway);
            Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
        }
    }

    [Fact]
    public void No_start_hold_while_the_aircraft_stands_on_another_runway()
    {
        var runways = new[] { EastWest("11", "29"), NorthSouth("14", "32", eastM: 1000, fromNorthM: 45, toNorthM: 3000) };

        // The aircraft stands on 11/29's pavement, where a start hold would stop it.
        var onRunway = RouteOf(Node(1, 940, 35), Node(2, 1000, 80), Node(3, 1000, 200));
        var ev = Assert.Single(Pass(onRunway, runways, aircraft: new AircraftPosition(Lat(0), Lon(940))));
        Assert.Equal("14", ev.Designator);
        Assert.False(ev.Held);
        Assert.Null(onRunway.StartHoldRunway);

        var withoutAircraft = RouteOf(Node(1, 940, 35), Node(2, 1000, 80), Node(3, 1000, 200));
        Assert.True(Assert.Single(Pass(withoutAircraft, runways)).Held);
        Assert.Equal("runway 14", withoutAircraft.StartHoldRunway);
    }

    [Fact]
    public void The_arrival_on_the_destination_strip_is_skipped_but_an_earlier_crossing_of_it_is_held_under_the_pilots_designator()
    {
        var route = RouteOf(Node(1, 2500, -300), Node(2, 2500, -100), Node(3, 2500, 0), Node(4, 2500, 100),
            Node(5, 2000, 100), Node(6, 1000, 60), Node(7, 1000, 0));

        var ev = Assert.Single(Pass(route, new[] { EastWest("04R", "22L") }, destination: "Runway 04R"));

        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.Equal("04R", ev.Designator);
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 04R", route.Segments[0].HoldShortRunway);
    }

    [Fact]
    public void Two_crossings_of_the_same_runway_each_get_a_hold()
    {
        // KSFO: the only route onto Q re-crossed 28R.
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100),
            Node(5, 900, 300), Node(6, 900, 100), Node(7, 900, 0), Node(8, 900, -100));

        var events = Pass(route, new[] { EastWest() });

        Assert.Equal(2, events.Count);
        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.True(route.Segments[4].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[4].HoldShortRunway);
    }

    [Fact]
    public void An_explicit_pick_is_honoured_only_at_or_after_its_taxiway_run()
    {
        var runway = EastWest();
        var picked = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            picked, runway, new[] { runway }, "27", runStartSegmentIndex: 0, allowStartHold: true));
        Assert.True(picked.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 27", picked.Segments[0].HoldShortRunway);

        var late = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Assert.Equal(UserRunwayHoldResult.NotOnRoute, RouteRunwayCrossings.ApplyUserRunwayHold(
            late, runway, new[] { runway }, "27", runStartSegmentIndex: 3, allowStartHold: true));
        Assert.All(late.Segments, s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void An_explicit_pick_whose_crossing_is_the_first_segment_starts_held_instead_of_tagging_the_far_side()
    {
        // KBOS C shape on segment 0: the old fallback tagged the crossing segment, whose end is past the runway.
        var runway = EastWest();
        var route = RouteOf(Node(1, 500, -60), Node(2, 500, 60));

        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runway, new[] { runway }, "27", runStartSegmentIndex: 0, allowStartHold: true));

        Assert.Equal("runway 27", route.StartHoldRunway);
        Assert.False(route.Segments[0].IsHoldShortPoint);
    }

    [Fact]
    public void An_explicit_pick_that_cannot_be_placed_says_so()
    {
        var runway14 = NorthSouth("14", "32", eastM: 1100, fromNorthM: -1500, toNorthM: 1500);
        var runways = new[] { EastWest("11", "29"), runway14 };

        Assert.Equal(UserRunwayHoldResult.NotHeld, RouteRunwayCrossings.ApplyUserRunwayHold(
            AlongElevenAcrossFourteen(), runway14, runways, "14", runStartSegmentIndex: 0, allowStartHold: true));
        Assert.Equal(UserRunwayHoldResult.NotOnRoute, RouteRunwayCrossings.ApplyUserRunwayHold(
            AlongElevenAcrossFourteen(), EastWest("09", "27", northM: 5000), runways, "09",
            runStartSegmentIndex: 0, allowStartHold: true));

        var leblRunway = EastWest("06L", "24R");
        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            Lebl(), leblRunway, new[] { leblRunway }, "06L", runStartSegmentIndex: 0, allowStartHold: true));
    }

    [Fact]
    public void An_explicit_pick_names_its_runway_at_an_end_of_taxiway_stop()
    {
        var runway = EastWest("06L", "24R");

        var picked = LeblWithEndOfTaxiwayStop();
        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            picked, runway, new[] { runway }, "06L", runStartSegmentIndex: 0, allowStartHold: true));
        Assert.Equal("runway 06L", picked.Segments[2].HoldShortRunway);

        // The automatic pass never touches a pilot's "end of taxiway" label.
        var automatic = LeblWithEndOfTaxiwayStop();
        Pass(automatic, new[] { runway });
        Assert.Equal("end of taxiway D", automatic.Segments[2].HoldShortRunway);
    }

    [Fact]
    public void The_progressive_strip_removes_a_stop_for_the_cleared_runway_alone_and_keeps_a_shared_one()
    {
        var (shared, runways) = Intersection();
        Pass(shared, runways);
        RouteRunwayCrossings.StripClearedCrossing(shared, "09");
        Assert.True(shared.Segments[0].IsHoldShortPoint);

        var single = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Pass(single, new[] { EastWest() });
        RouteRunwayCrossings.StripClearedCrossing(single, "27");
        Assert.All(single.Segments, s => Assert.False(s.IsHoldShortPoint));

        var started = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));
        Pass(started, new[] { EastWest() });
        RouteRunwayCrossings.StripClearedCrossing(started, "27");
        Assert.Null(started.StartHoldRunway);
    }

    [Fact]
    public void Missing_runways_fail_loudly_and_a_missing_route_meets_nothing()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RouteRunwayCrossings.InsertRunwayHoldShorts(Lebl(), null!, "", allowStartHold: true));
        Assert.Empty(RouteRunwayCrossings.InsertRunwayHoldShorts(null!, new[] { EastWest() }, "", allowStartHold: true));
    }

    [Fact]
    public void A_second_crossing_of_the_same_runway_never_shares_the_firsts_stop()
    {
        // Reviewer probe S1: a "runway 09" hold node 35 m south of a first crossing, then two nodes
        // 41 m north, 20 m apart along the runway, forming a second crossing. The second crossing's
        // walk must not reach back across the first crossing's own pavement to share that hold node.
        var route = RouteOf(
            Node(1, 1000, -300), Node(2, 1000, -150), Node(3, 1000, -35, TaxiNodeType.HoldShort, "runway 09"),
            Node(4, 1000, 0), Node(5, 1000, 41), Node(6, 1020, 41), Node(7, 1020, 0), Node(8, 1020, -150));

        var events = Pass(route, new[] { EastWest() });

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.Held));
        Assert.True(route.Segments[1].IsHoldShortPoint);
        Assert.True(route.Segments[4].IsHoldShortPoint);
    }

    [Fact]
    public void A_route_starting_on_the_runway_that_re_crosses_it_never_gets_a_start_hold()
    {
        // Reviewer probe S4 (Minor 3): the route begins ON the runway (no entry to hold at), leaves
        // to nodes inside the 10 m clear margin, then re-crosses. The start node sits on the
        // pavement, so it must never become a start hold.
        var route = RouteOf(Node(1, 1000, 0), Node(2, 1000, 35), Node(3, 1060, 35), Node(4, 1060, 0), Node(5, 1060, -60));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }));

        Assert.Equal(RunwayEventKind.Crossing, ev.Kind);
        Assert.False(ev.Held);
        Assert.Null(route.StartHoldRunway);
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void An_event_names_the_designator_its_own_stop_actually_announces()
    {
        // Reviewer probe S5: pick "27" for a crossing whose geometry sits in the 09 half, then run
        // the automatic pass. The event must name "27" (what the stop says), not "09" (the
        // designator the crossing geometry reports).
        var runway = EastWest();
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));

        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runway, new[] { runway }, "27", runStartSegmentIndex: 0, allowStartHold: true));
        var events = Pass(route, new[] { runway });

        Assert.Equal("runway 27", route.Segments[0].HoldShortRunway);
        var ev = Assert.Single(events);
        Assert.Equal("27", ev.Designator);
        Assert.Equal("crossing runway 27", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));
    }

    [Fact]
    public void A_kept_scenery_label_naming_the_reciprocal_end_is_what_the_event_records()
    {
        var route = RouteOf(
            Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 27 at C"),
            Node(3, 1000, 51), Node(4, 1000, 21), Node(5, 1000, -21), Node(6, 1000, -105));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }));

        Assert.Equal("runway 27 at C", route.Segments[0].HoldShortRunway);
        Assert.Equal("27", ev.Designator);
    }
}
