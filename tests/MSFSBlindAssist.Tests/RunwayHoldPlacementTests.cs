// Where a runway entry's or crossing's hold goes: the scenery's own hold line within 150 m, else the
// nearest node clear of the runway (half-width + 10 m), else the route's start node (a start hold).
// Every candidate is at or before the runway and off its pavement, so a stop can only move earlier.
// Shapes: LEBL D5, EGKK C, KBOS C, KSFO's double crossing (docs/taxi-guidance.md).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class RunwayHoldPlacementTests
{
    private static TaxiRoute RouteOf(params TaxiNode[] nodes) => new() { Segments = Route(nodes) };

    private static IReadOnlyList<TaxiRouteRunwayEvent> Pass(TaxiRoute route, TaxiGraph.RunwayCenterline[] runways,
        string destination = "", bool allowStartHold = true,
        RouteRunwayCrossings.HoldPointPassed? passed = null, Func<bool>? startPassed = null)
        => RouteRunwayCrossings.InsertRunwayHoldShorts(route, runways, destination, allowStartHold, passed, startPassed);

    private static TaxiRoute Lebl() => RouteOf(
        Node(1, 1000, 250), Node(2, 1000, 105, TaxiNodeType.HoldShort, "runway 06L at D5"),
        Node(3, 1000, 51), Node(4, 1000, 21), Node(5, 1000, -21), Node(6, 1000, -105));

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
        var route = RouteOf(Node(1, 1000, 35), Node(2, 1000, 10), Node(3, 1000, -60));

        var ev = Assert.Single(Pass(route, new[] { EastWest() }, startPassed: () => true));

        Assert.Null(route.StartHoldRunway);
        Assert.False(ev.Held);
    }

    [Fact]
    public void A_stop_the_aircraft_has_passed_is_recorded_but_not_placed()
    {
        var route = Lebl();

        var ev = Assert.Single(Pass(route, new[] { EastWest("06L", "24R") },
            passed: seg => ReferenceEquals(seg, route.Segments[0])));

        Assert.All(route.Segments, s => Assert.False(s.IsHoldShortPoint));
        Assert.False(ev.Held);
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
        var picked = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Assert.True(RouteRunwayCrossings.ApplyUserRunwayHold(picked, EastWest(), "27", runStartSegmentIndex: 0, allowStartHold: true));
        Assert.True(picked.Segments[0].IsHoldShortPoint);
        Assert.Equal("runway 27", picked.Segments[0].HoldShortRunway);

        var late = RouteOf(Node(1, 500, -300), Node(2, 500, -100), Node(3, 500, 0), Node(4, 500, 100));
        Assert.False(RouteRunwayCrossings.ApplyUserRunwayHold(late, EastWest(), "27", runStartSegmentIndex: 3, allowStartHold: true));
        Assert.All(late.Segments, s => Assert.False(s.IsHoldShortPoint));
    }

    [Fact]
    public void An_explicit_pick_whose_crossing_is_the_first_segment_starts_held_instead_of_tagging_the_far_side()
    {
        // KBOS C shape on segment 0: the old fallback tagged the crossing segment, whose end is past the runway.
        var route = RouteOf(Node(1, 500, -60), Node(2, 500, 60));

        Assert.True(RouteRunwayCrossings.ApplyUserRunwayHold(route, EastWest(), "27", runStartSegmentIndex: 0, allowStartHold: true));

        Assert.Equal("runway 27", route.StartHoldRunway);
        Assert.False(route.Segments[0].IsHoldShortPoint);
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
}
