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
        string destination = "", AircraftPosition? aircraft = null)
        => RouteRunwayCrossings.InsertRunwayHoldShorts(route, runways, destination, aircraft);

    /// <summary>
    /// A route adopted by a RECALCULATION: built from an aircraft already committed to where it is
    /// going, so it never starts held. Since PR #238 deferred finding §2 the caller says so on the
    /// position itself (<c>MayStartHeld</c>) rather than through a phase string.
    /// </summary>
    private static AircraftPosition Recalculating(double eastM, double northM)
        => new(Lat(northM), Lon(eastM), MayStartHeld: false);

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
        // The hold NODE is still never the stop — that is this test's subject and no segment is tagged.
        // 14 is nonetheless HELD: the route enters 11/29 first, whose own stop is the route's start
        // node (a start hold), and that stop sits before BOTH runways, so 14 shares it and the label
        // names both. It used to be dropped entirely — see
        // A_second_runway_crossed_beyond_the_first_shares_the_start_hold.
        // "32", not "14": the crossing sits just past that runway's midpoint, so the shape names
        // the nearer end — which is what the pilot hears at the stop.
        Assert.Equal("runway 11 and runway 32", alongEleven.StartHoldRunway);
        Assert.True(Assert.Single(events, e => RouteRunwayCrossings.CenterlineHasDesignator(runway14, e.Designator)).Held);
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
        // A recalculation is built from an aircraft already committed to where it is going, and a
        // start hold stops the aircraft where it stands — on top of "Route changed". The adopter
        // says so on the position itself; a ground-speed gate was tried and withdrawn (PR #243).
        var route = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }, aircraft: Recalculating(1000, 35)));

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

        // 6 m along the route: taken as its first point while standing on the runway, so the route starts on the runway and meets nothing.
        Assert.Empty(Pass(route, new[] { EastWest() }, aircraft: new AircraftPosition(Lat(15), Lon(1000))));
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
            picked, runway, new[] { runway }, "27", runStartSegmentIndex: 0));
        Assert.True(picked.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 27", picked.Segments[0].HoldShortRunway);

        var late = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Assert.Equal(UserRunwayHoldResult.NotOnRoute, RouteRunwayCrossings.ApplyUserRunwayHold(
            late, runway, new[] { runway }, "27", runStartSegmentIndex: 3));
        Assert.All(late.Segments, s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void An_explicit_pick_whose_crossing_is_the_first_segment_starts_held_instead_of_tagging_the_far_side()
    {
        // KBOS C shape on segment 0: the old fallback tagged the crossing segment, whose end is past the runway.
        var runway = EastWest();
        var route = RouteOf(Node(1, 500, -60), Node(2, 500, 60));

        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runway, new[] { runway }, "27", runStartSegmentIndex: 0));

        Assert.Equal("runway 27", route.StartHoldRunway);
        Assert.False(route.Segments[0].IsHoldShortPoint);
    }

    [Fact]
    public void An_explicit_pick_that_cannot_be_placed_says_so()
    {
        var runway14 = NorthSouth("14", "32", eastM: 1100, fromNorthM: -1500, toNorthM: 1500);
        var runways = new[] { EastWest("11", "29"), runway14 };

        Assert.Equal(UserRunwayHoldResult.NotHeld, RouteRunwayCrossings.ApplyUserRunwayHold(
            AlongElevenAcrossFourteen(), runway14, runways, "14", runStartSegmentIndex: 0));
        Assert.Equal(UserRunwayHoldResult.NotOnRoute, RouteRunwayCrossings.ApplyUserRunwayHold(
            AlongElevenAcrossFourteen(), EastWest("09", "27", northM: 5000), runways, "09",
            runStartSegmentIndex: 0));

        var leblRunway = EastWest("06L", "24R");
        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            Lebl(), leblRunway, new[] { leblRunway }, "06L", runStartSegmentIndex: 0));
    }

    [Fact]
    public void An_explicit_pick_names_its_runway_at_an_end_of_taxiway_stop()
    {
        var runway = EastWest("06L", "24R");

        var picked = LeblWithEndOfTaxiwayStop();
        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            picked, runway, new[] { runway }, "06L", runStartSegmentIndex: 0));
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
    public void A_cleared_progressive_crossing_stops_being_announced_at_all()
    {
        // The strip removes the stop but used to leave the recorded event standing, so the leg's
        // summary still said "crossing runway 09" for a runway the pilot is cleared across — and
        // now that an unheld event is flagged, it would have read "with no hold short point for
        // runway 09" over a deliberate clearance. The Progressive terminator already names it.
        var single = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Pass(single, new[] { EastWest() });
        Assert.Equal("crossing runway 09", RouteRunwayCrossings.DescribeRunwayEvents(single.RunwayEvents));

        RouteRunwayCrossings.StripClearedCrossing(single, "27");

        Assert.Empty(single.RunwayEvents);
        Assert.Equal("", RouteRunwayCrossings.DescribeRunwayEvents(single.RunwayEvents));
    }

    [Fact]
    public void Clearing_one_runway_of_a_shared_stop_leaves_the_others_event_standing()
    {
        // The shared stop itself survives (the other runway is not cleared), so its event must too.
        var (shared, runways) = Intersection();
        Pass(shared, runways);

        RouteRunwayCrossings.StripClearedCrossing(shared, "09");

        Assert.True(shared.Segments[0].IsHoldShortPoint);
        var remaining = Assert.Single(shared.RunwayEvents);
        Assert.Equal("01", remaining.Designator);
        Assert.True(remaining.Held);
    }

    [Fact]
    public void Missing_runways_fail_loudly_and_a_missing_route_meets_nothing()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RouteRunwayCrossings.InsertRunwayHoldShorts(Lebl(), null!, ""));
        Assert.Empty(RouteRunwayCrossings.InsertRunwayHoldShorts(null!, new[] { EastWest() }, ""));
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
            route, runway, new[] { runway }, "27", runStartSegmentIndex: 0));
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

    [Fact]
    public void A_second_runway_crossed_beyond_the_first_shares_the_start_hold()
    {
        // A start hold tags NO segment, so it used to be invisible to the next runway's walk:
        // 01's walk back meets 09's pavement, latches crossedOther, finds no "existing stop" at
        // node 0 (IsExistingStop is false there by construction) and returned -1 — so the pilot
        // heard only "hold short of runway 09", pressed Continue, and crossed BOTH runways.
        // The start hold sits before everything on the route, so it is always a safe stop to share.
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1200, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 900, 35), Node(2, 1000, 0), Node(3, 1300, 0), Node(4, 1300, -60));

        var events = Pass(route, runways);

        Assert.Equal("runway 09 and runway 01", route.StartHoldRunway);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.True(e.Held));
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void A_second_runway_is_still_unheld_when_there_is_no_start_hold_to_share()
    {
        // The same shape with the start hold refused (a recalculation): nothing safe exists, so
        // the -1 "no stop" verdict must survive. The fix must not manufacture a stop out of nothing.
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1200, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 900, 35), Node(2, 1000, 0), Node(3, 1300, 0), Node(4, 1300, -60));

        var events = Pass(route, runways, aircraft: Recalculating(900, 35));

        Assert.Null(route.StartHoldRunway);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.False(e.Held));
        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
    }
}

// PR #238 deferred finding §2: `allowStartHold` was a SECOND, differently-shaped answer to "is the
// aircraft on the runway".
//
// Both landing-handoff sites computed it as !IsWithinRolloutRunwayLaterally(lat, lon) — a predicate
// that is lateral-only, single-runway, along-track UNBOUNDED, carries a 10 m margin and falls back
// to a 200 ft width — and handed it to a pass that then asked the same question again through
// RunwayUnder -> RunwayShape.Contains, which is extent-bounded, zero-margin and falls back to 75 ft.
// The two disagree on the same lat/lon in the same call chain, and every disagreement DROPS a
// legitimate start hold, which since PR #238 is announced out loud as "with no hold short point for
// runway X".
//
// The parameter is gone. What it genuinely encoded that the pass could not see — "the aircraft is
// moving / already committed" — is supplied as ground speed instead.
public class StartHoldWithoutTheFlagTests
{
    private static TaxiRoute RouteOf(params TaxiNode[] nodes) => new() { Segments = Route(nodes) };

    private static IReadOnlyList<TaxiRouteRunwayEvent> Pass(
        TaxiRoute route, TaxiGraph.RunwayCenterline[] runways, AircraftPosition? aircraft = null)
        => RouteRunwayCrossings.InsertRunwayHoldShorts(route, runways, "", aircraft);

    // A route whose only safe stop is its own start node, with the aircraft standing there — 45 m
    // out, clear of the 30 m half-width by more than the 10 m margin a start hold now requires.
    private static TaxiRoute StartsAtItsOnlyStop() =>
        RouteOf(Node(1, 1000, 45), Node(2, 1000, 10), Node(3, 1000, -60));

    [Fact]
    public void A_standing_aircraft_still_starts_held()
    {
        var route = StartsAtItsOnlyStop();
        var ev = Assert.Single(Pass(route, new[] { EastWest() }, new AircraftPosition(Lat(45), Lon(1000))));

        Assert.Equal("runway 09", route.StartHoldRunway);
        Assert.True(ev.Held);
    }

    // The replacement for the deleted flag: an aircraft already rolling is committed to where it is
    // going, so a stop at its own position is not an instruction it can take. This is what keeps a
    // recalculated route — always built from a moving aircraft — from starting held.
    [Fact]
    public void A_recalculation_never_starts_held_however_still_the_aircraft_stands()
    {
        var route = StartsAtItsOnlyStop();
        var ev = Assert.Single(Pass(route, new[] { EastWest() }, new AircraftPosition(Lat(45), Lon(1000), MayStartHeld: false)));

        Assert.Null(route.StartHoldRunway);
        Assert.False(ev.Held);
    }

    [Fact]
    public void A_shared_stop_names_the_runway_met_first_whichever_pass_placed_it()
    {
        // The edge N2->N3 jumps 09/27 mid-edge, and N3 is already on 01/19 (axis at east 1420):
        // both walks resolve to N2, and the route meets 09/27 first. The pilot picked 01, and the
        // pick pass runs BEFORE the automatic one — so without reordering the label read
        // "runway 01 and runway 09" and the staged hold asked for 01's clearance first.
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1420, fromNorthM: -1400, toNorthM: 1600) };
        var route = RouteOf(Node(1, 1200, -200), Node(2, 1340, -60), Node(3, 1400, 60), Node(4, 1600, 200));

        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runways[1], runways, "01", runStartSegmentIndex: 0));
        Assert.Equal("runway 01", route.Segments[0].HoldShortRunway);

        Pass(route, runways);

        Assert.Equal("runway 09 and runway 01", route.Segments[0].HoldShortRunway);
        Assert.Equal(new[] { "09", "01" }, RunwayHoldStages.From(route.Segments[0].HoldShortRunway));
    }

    // DIVERGENCE 1 (§2): an aircraft that has rolled off the FAR END of the runway — a short field,
    // an overrun, a backtrack turnaround — is still inside the lateral-only predicate's infinite
    // strip, so offRunwayAtHandoff read false and its re-route's start hold was refused. The pass's
    // own RunwayUnder is extent-bounded and says, correctly, that it is not on the runway.
    [Fact]
    public void An_aircraft_stopped_past_the_runway_end_still_starts_held()
    {
        // 09/27 runs east 0..3000. The aircraft sits 250 m PAST the 27 end, ON the axis — inside the
        // lateral-only predicate's infinite strip, outside RunwayShape's extent — and its route
        // crosses 01/19 with nowhere earlier to stop.
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 3300, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 3250, 0), Node(2, 3350, 0));

        var ev = Assert.Single(Pass(route, runways, new AircraftPosition(Lat(0), Lon(3250))));

        Assert.Equal("runway 01", route.StartHoldRunway);
        Assert.True(ev.Held);
    }

    // DIVERGENCE 2 (§2): an aircraft a few metres outside the pavement edge is inside the lateral
    // predicate's 10 m margin — "still on the runway" — while RunwayUnder, which carries no margin,
    // says it is off. The start hold it was refused is legitimate.
    [Fact]
    public void An_aircraft_stopped_inside_the_clear_margin_never_starts_held()
    {
        // Half-width 30 m: the aircraft is 35 m out — off the pavement, but inside the 40 m the pass
        // demands of any stop it invents. Its tail is still over the runway; a start hold here is a
        // hold ON the runway (PR #243 review). Its route crosses 01/19 with nowhere earlier to stop.
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1300, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 1250, 35), Node(2, 1350, 35));

        var ev = Assert.Single(Pass(route, runways, new AircraftPosition(Lat(35), Lon(1250))));

        Assert.Null(route.StartHoldRunway);
        Assert.False(ev.Held);
    }

    [Fact]
    public void An_aircraft_stopped_beyond_the_clear_margin_still_starts_held()
    {
        var runways = new[] { EastWest("09", "27"), NorthSouth("01", "19", eastM: 1300, fromNorthM: -1500, toNorthM: 1500) };
        var route = RouteOf(Node(1, 1250, 45), Node(2, 1350, 45));

        var ev = Assert.Single(Pass(route, runways, new AircraftPosition(Lat(45), Lon(1250))));

        // 01/19 is one pavement and the crossing sits just past its midfield, so the stop is named
        // after the 19 end — NameAt's own closer-end rule, unrelated to this finding.
        Assert.Equal("runway 19", route.StartHoldRunway);
        Assert.True(ev.Held);
    }

    // Unchanged and load-bearing: whatever the speed, no start hold while the aircraft stands on any
    // runway's pavement — that stop would be in the worst possible place.
    [Fact]
    public void A_stopped_aircraft_on_a_runway_still_never_starts_held()
    {
        var runways = new[] { EastWest("11", "29"), NorthSouth("14", "32", eastM: 1000, fromNorthM: 45, toNorthM: 3000) };
        var route = RouteOf(Node(1, 940, 35), Node(2, 1000, 80), Node(3, 1000, 200));

        var ev = Assert.Single(Pass(route, runways, new AircraftPosition(Lat(0), Lon(940))));

        Assert.Null(route.StartHoldRunway);
        Assert.False(ev.Held);
    }
}

// PR #238 deferred finding §3: the LIMBO BAND past a runway end.
//
// RunwayShape.Contains requires `along` INSIDE the extent; RunwayShape.IsClearOf tested |lateral|
// ONLY. A node beyond the runway's along-track extent but near its axis was therefore neither "on
// the runway" nor "clear of" it: walk 2 stepped over it — and over every node behind it — and fell
// through to HoldStop(0), a START hold. The pilot was told "Stop. Hold short of runway 09" before
// moving, hundreds of metres from the real hold line, while no hold was placed where the route
// actually meets the pavement.
//
// Trigger shape: a taxiway running off the end of a runway on or near its extended centreline — a
// turnpad lead-in, or any approach to a crossing from beyond the end.
//
// Walk 1 had the mirror asymmetry: it tested a bare |lateral| > HalfWidthMeters with NO extent term,
// so an on-axis scenery hold line beyond the end read as being "on the pavement" and was rejected.
// Both walks now ask RunwayShape.IsClearOfAt, which is extent-aware — but they keep their DIFFERENT
// lateral margins, deliberately (owner ruling): walk 1 stays on the bare half-width so a scenery
// hold line hugging the pavement edge is still usable (measured: SC99's line is 7.2 m out on a
// 4.0 m half-width), walk 2 keeps half-width + RunwayClearMarginM.
public class HoldStopPastTheRunwayEndTests
{
    private static TaxiRoute RouteOf(params TaxiNode[] nodes) => new() { Segments = Route(nodes) };

    private static IReadOnlyList<TaxiRouteRunwayEvent> Pass(
        TaxiRoute route, TaxiGraph.RunwayCenterline[] runways)
        => RouteRunwayCrossings.InsertRunwayHoldShorts(route, runways, "");

    // 09/27 runs east 0..3000, half-width 30. The route comes in from beyond the 09 threshold, on
    // the extended centreline, and enters the runway. The node 50 m off the end is where the hold
    // belongs; before the fix the whole walk fell through to a start hold.
    [Fact]
    public void A_route_approaching_from_beyond_the_runway_end_holds_at_the_real_node()
    {
        var route = RouteOf(
            Node(1, -300, 20), Node(2, -50, 5), Node(3, 200, 0), Node(4, 400, 20));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }));

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);
        Assert.Null(route.StartHoldRunway);
        Assert.True(ev.Held);
    }

    // Walk 1, same limbo band: an on-axis scenery hold line BEYOND the end is a real hold line, not
    // a node on the pavement.
    [Fact]
    public void A_scenery_hold_line_beyond_the_runway_end_is_usable_as_the_stop()
    {
        var route = RouteOf(
            Node(1, -300, 20),
            Node(2, -50, 2, TaxiNodeType.HoldShort, "runway 09"),
            Node(3, 200, 0), Node(4, 400, 20));

        Pass(route, new[] { EastWest() });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);
        Assert.Null(route.StartHoldRunway);
    }

    // The deliberate asymmetry, pinned: walk 1 keeps the BARE half-width, so a painted hold line
    // inside the 10 m clear margin is still the stop. SC99's line is 7.2 m out on a 4.0 m
    // half-width; tightening walk 1 to IsClearOf would reject real hold lines.
    [Fact]
    public void A_scenery_hold_line_inside_the_ten_metre_margin_is_still_the_stop()
    {
        var route = RouteOf(
            Node(1, 1000, 250),
            Node(2, 1000, 35, TaxiNodeType.HoldShort, "runway 09"),   // 5 m outside a 30 m half-width
            Node(3, 1000, -60));

        Pass(route, new[] { EastWest() });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 09", route.Segments[0].HoldShortRunway);
    }

    // ... and a hold NODE on the pavement is still never the stop, at either end of the band.
    [Fact]
    public void A_hold_node_on_the_pavement_is_still_never_the_stop()
    {
        var route = RouteOf(
            Node(1, 1000, 250), Node(2, 1000, 60),
            Node(3, 1000, 20, TaxiNodeType.HoldShort, "runway 09"), Node(4, 1000, -60));

        Pass(route, new[] { EastWest() });

        Assert.True(route.Segments[0].IsHoldShortPoint);
        Assert.False(route.Segments[1].IsHoldShortPoint);
    }
}

// PR #238 deferred finding §7: an explicit pilot hold-short pick could vanish from the route
// summary.
//
// ApplyUserRunwayHold placed the stop but recorded NO TaxiRouteRunwayEvent, and
// InsertRunwayHoldShorts then RESETS route.RunwayEvents. When the automatic pass skips that same
// passage — the destination-strip arrival skip — the pick is named NOWHERE: DescribeRunwayEvents
// says nothing, and CountNonRunwayHoldShorts also skips it because its label DOES name a runway.
// The pilot picks "hold short of runway 04R" on a route to 04R, hears no mention of it in the
// summary, and is then stopped by a hold they were never told about.
//
// The pick now carries its own event, merged in after the automatic pass. The destination-strip
// skip itself is untouched — it has its own incident history (a blanket same-runway skip once
// dropped genuine mid-route crossings of the active runway, 2026-08-24) and still skips ONLY the
// route's own final arrival.
public class UserPickEventTests
{
    private static TaxiRoute RouteOf(params TaxiNode[] nodes) => new() { Segments = Route(nodes) };

    private static TaxiRouteRunwayEvent Event(RunwayEventKind kind, string designator, bool held = true)
        => new() { Kind = kind, Designator = designator, Held = held };

    [Fact]
    public void A_pick_the_automatic_pass_never_recorded_is_added()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100));
        route.RunwayEvents = new List<TaxiRouteRunwayEvent>();

        RouteRunwayCrossings.MergeUserPickEvents(route, new[] { Event(RunwayEventKind.Entry, "04R") });

        var ev = Assert.Single(route.RunwayEvents);
        Assert.Equal("04R", ev.Designator);
        Assert.Equal(RunwayEventKind.Entry, ev.Kind);
        Assert.True(ev.Held);
    }

    [Fact]
    public void A_pick_the_automatic_pass_already_recorded_is_not_counted_twice()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100));
        route.RunwayEvents = new List<TaxiRouteRunwayEvent> { Event(RunwayEventKind.Crossing, "09") };

        RouteRunwayCrossings.MergeUserPickEvents(route, new[] { Event(RunwayEventKind.Crossing, "09") });

        Assert.Single(route.RunwayEvents);
    }

    // 26R and 08L are one piece of pavement, so a pick spelled as the other end is the same passage.
    [Fact]
    public void A_pick_spelled_as_the_reciprocal_end_is_not_counted_twice()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100));
        route.RunwayEvents = new List<TaxiRouteRunwayEvent> { Event(RunwayEventKind.Crossing, "26R") };

        RouteRunwayCrossings.MergeUserPickEvents(route, new[] { Event(RunwayEventKind.Crossing, "08L") });

        Assert.Single(route.RunwayEvents);
    }

    // ... but the same runway met a DIFFERENT way is a different passage and keeps its own event.
    [Fact]
    public void The_same_runway_recorded_with_a_different_kind_still_gets_its_event()
    {
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100));
        route.RunwayEvents = new List<TaxiRouteRunwayEvent> { Event(RunwayEventKind.Crossing, "09") };

        RouteRunwayCrossings.MergeUserPickEvents(route, new[] { Event(RunwayEventKind.Entry, "09") });

        Assert.Equal(2, route.RunwayEvents.Count);
    }

    // The pick reports the passage it bound to, so the manager has something to merge.
    [Fact]
    public void An_honoured_pick_reports_its_own_event()
    {
        var runway = EastWest();
        var route = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));

        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runway, new[] { runway }, "27", runStartSegmentIndex: 0, placed: out var placed));

        Assert.NotNull(placed);
        Assert.Equal("27", placed!.Designator);
        Assert.Equal(RunwayEventKind.Crossing, placed.Kind);
        Assert.True(placed.Held);
    }

    [Fact]
    public void A_pick_that_could_not_be_placed_reports_nothing_to_merge()
    {
        var runway14 = NorthSouth("14", "32", eastM: 1100, fromNorthM: -1500, toNorthM: 1500);
        var runways = new[] { EastWest("11", "29"), runway14 };
        var route = RouteOf(Node(1, 900, 0), Node(2, 1000, 0), Node(3, 1060, 0), Node(4, 1100, 0),
            Node(5, 1140, 0), Node(6, 1300, 0));

        Assert.Equal(UserRunwayHoldResult.NotHeld, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runway14, runways, "14", runStartSegmentIndex: 0, placed: out var placed));

        Assert.Null(placed);
    }

    // The whole point, end to end: a route TO runway 04R whose pilot asked to hold short of 04R.
    // The automatic pass skips the arrival, so without the merge the summary says nothing at all.
    [Fact]
    public void A_pick_on_the_destination_strip_is_named_in_the_summary()
    {
        var runway = EastWest("04R", "22L");
        var route = RouteOf(Node(1, 1000, -300), Node(2, 1000, -100), Node(3, 1000, 0));

        Assert.Equal(UserRunwayHoldResult.Held, RouteRunwayCrossings.ApplyUserRunwayHold(
            route, runway, new[] { runway }, "04R", runStartSegmentIndex: 0, placed: out var placed));

        var auto = RouteRunwayCrossings.InsertRunwayHoldShorts(route, new[] { runway }, "Runway 04R");
        Assert.Empty(auto);                                    // the arrival is skipped, as it must be
        Assert.Equal("", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));

        RouteRunwayCrossings.MergeUserPickEvents(route, new[] { placed! });

        Assert.Contains("04R", RouteRunwayCrossings.DescribeRunwayEvents(route.RunwayEvents));
    }
}
