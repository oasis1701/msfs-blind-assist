// RunwayRouteClassifier: whether a route ENTERS or CROSSES a runway is decided by which side of
// the runway the route is on before and after, never by which side of a line one node falls on
// to the centimetre. Each shape below is a real case from the PR #238 review (docs/taxi-guidance.md,
// "Runway crossings and entries").

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class RunwayRouteClassifierTests
{
    private static List<RunwayPassage> Classify(TaxiGraph.RunwayCenterline rwy, params (double East, double North)[] points)
        => RunwayRouteClassifier.Classify(Nodes(points), RunwayShape.For(rwy));

    [Fact]
    public void A_node_exactly_on_a_north_south_centerline_between_clear_nodes_is_a_crossing()
    {
        // P19 / KBDN: the strict per-edge test saw neither edge cross, because the middle node sat
        // on the line to the centimetre.
        var rwy = NorthSouth("01", "19", eastM: 500.0, fromNorthM: 0.0, toNorthM: 2000.0);

        var passage = Assert.Single(Classify(rwy, (420.0, 900.0), (500.0, 900.0), (580.0, 900.0)));

        Assert.Equal(RunwayEventKind.Crossing, passage.Kind);
        Assert.Equal(0, passage.EntryIndex);
        Assert.Equal(1, passage.FirstOnIndex);
        Assert.Equal(2, passage.ExitIndex);
        Assert.Equal(1, passage.ReachIndex);
        Assert.Equal("01", passage.Designator);
    }

    [Fact]
    public void An_edge_along_the_centerline_on_a_route_that_starts_on_the_runway_is_nothing()
    {
        // ESMX: the two nodes straddle the line by 15 cm and 40 cm; the route only vacates.
        Assert.Empty(Classify(EastWest(), (500.0, 0.149), (700.0, -0.402), (720.0, -80.0)));
    }

    [Fact]
    public void Going_onto_the_runway_and_back_off_the_same_side_is_an_entry()
    {
        var passage = Assert.Single(Classify(EastWest(), (500.0, 90.0), (500.0, 20.0), (600.0, 20.0), (600.0, 90.0)));

        Assert.Equal(RunwayEventKind.Entry, passage.Kind);
        Assert.Equal(0, passage.EntryIndex);
        Assert.Equal(1, passage.FirstOnIndex);
        Assert.Equal(3, passage.ExitIndex);
    }

    [Fact]
    public void An_exit_junction_centimetres_over_the_line_on_a_route_starting_on_the_runway_is_nothing()
    {
        // KORD 10R exit W5: node 29 cm past the centerline, then off the runway.
        Assert.Empty(Classify(EastWest(), (2800.0, 0.0), (2950.0, -0.292), (2960.0, -60.0)));
    }

    [Fact]
    public void A_route_starting_just_outside_the_pavement_edge_and_crossing_is_a_crossing()
    {
        // KATL shape: 25.7 m out on a 150 ft (22.86 m half-width) runway, then across. Detection
        // uses the half-width alone; the 10 m clear margin is only for hold placement.
        var rwy = EastWest(halfWidthM: 22.86);

        var passage = Assert.Single(Classify(rwy, (1000.0, -25.7), (1000.0, 20.0), (1000.0, 70.0)));

        Assert.Equal(RunwayEventKind.Crossing, passage.Kind);
        Assert.Equal(0, passage.EntryIndex);
    }

    [Fact]
    public void A_re_entry_after_vacating_is_found_from_the_start_but_not_from_after_it()
    {
        // KATL loop: start on the runway, vacate south, loop, then cross north.
        var rwy = EastWest();
        var nodes = new[]
        {
            Node(1, 1000.0, 5.0), Node(2, 1000.0, -120.0), Node(3, 2000.0, -120.0),
            Node(4, 2000.0, 10.0), Node(5, 2000.0, 120.0),
        };
        var segments = Route(nodes);

        var fromStart = RunwayRouteClassifier.Classify(RunwayRouteClassifier.NodesFrom(segments, 0), RunwayShape.For(rwy));
        var fromSegment3 = RunwayRouteClassifier.Classify(RunwayRouteClassifier.NodesFrom(segments, 3), RunwayShape.For(rwy));

        var passage = Assert.Single(fromStart);
        Assert.Equal(RunwayEventKind.Crossing, passage.Kind);
        Assert.Equal(2, passage.EntryIndex);
        Assert.Empty(fromSegment3);
    }

    [Fact]
    public void One_long_edge_spanning_the_runway_is_a_crossing_met_on_the_centerline()
    {
        // KBOS taxiway C over 04L: both nodes about 35 m out, no node on the pavement.
        var passage = Assert.Single(Classify(EastWest(), (1200.0, -35.0), (1210.0, 35.0)));

        Assert.Equal(RunwayEventKind.Crossing, passage.Kind);
        Assert.Equal(0, passage.EntryIndex);
        Assert.Equal(-1, passage.FirstOnIndex);
        Assert.Equal(1, passage.ExitIndex);
        Assert.Equal(1, passage.ReachIndex);
        Assert.InRange((passage.MeetLat - BaseLat) * M, -0.5, 0.5);
        Assert.Equal("09", passage.Designator);
    }

    [Fact]
    public void A_taxiway_looping_round_the_runway_end_is_nothing()
    {
        Assert.Empty(Classify(EastWest(), (-100.0, -60.0), (-100.0, 60.0)));
        // Through a node exactly on the extended axis beyond the end: that node has no side.
        Assert.Empty(Classify(EastWest(), (-150.0, -60.0), (-150.0, 0.0), (-150.0, 60.0)));
    }

    [Fact]
    public void A_route_ending_on_the_runway_is_an_entry_with_no_exit()
    {
        var passage = Assert.Single(Classify(EastWest(), (800.0, -90.0), (800.0, -10.0), (900.0, 0.0)));

        Assert.Equal(RunwayEventKind.Entry, passage.Kind);
        Assert.Equal(-1, passage.ExitIndex);
    }

    [Fact]
    public void The_same_runway_crossed_twice_is_two_crossings()
    {
        var passages = Classify(EastWest(),
            (500.0, -90.0), (500.0, 0.0), (500.0, 90.0), (900.0, 90.0), (900.0, 0.0), (900.0, -90.0));

        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal(RunwayEventKind.Crossing, p.Kind));
        Assert.Equal(0, passages[0].EntryIndex);
        Assert.Equal(3, passages[1].EntryIndex);
    }

    [Fact]
    public void A_route_starting_in_a_runway_intersection_and_running_out_along_one_runway_meets_neither()
    {
        // KBDR: the route starts in the 06/24 x 11/29 intersection, 3 cm west of the 11/29
        // centerline, and runs east along 06/24 out of the 11/29 pavement. The old per-edge
        // first-match probe read that edge as crossing 29 and held on the intersection; the
        // route is on both runways at its start, so it crosses and enters neither.
        var r0624 = EastWest("06", "24");
        var r1129 = NorthSouth("11", "29", eastM: 1500.0, fromNorthM: -1500.0, toNorthM: 1500.0);
        var nodes = Nodes((1499.97, 0.1), (2000.0, 0.0), (2000.0, 150.0));

        Assert.Empty(RunwayRouteClassifier.ClassifyAll(nodes, new[] { r0624, r1129 }));
    }

    [Fact]
    public void Taxiing_along_one_runway_across_another_from_clear_of_it_is_a_crossing_of_that_runway_only()
    {
        // Each runway is judged on its own: starting on 06/24 means nothing for 06/24, while
        // arriving at 11/29 from clear of it and leaving on the far side crosses 11/29.
        var r0624 = EastWest("06", "24");
        var r1129 = NorthSouth("11", "29", eastM: 1500.0, fromNorthM: -1500.0, toNorthM: 1500.0);
        var nodes = Nodes((1000.0, 0.0), (1480.0, 0.0), (1520.0, 0.0), (2000.0, 0.0), (2000.0, 150.0));

        var passage = Assert.Single(RunwayRouteClassifier.ClassifyAll(nodes, new[] { r0624, r1129 }));

        Assert.Same(r1129, passage.Runway);
        Assert.Equal(RunwayEventKind.Crossing, passage.Kind);
    }

    [Fact]
    public void Crossing_another_runways_extended_axis_beyond_its_end_is_no_passage_of_it()
    {
        // 11/29 ends 60 m north of the 06/24 centerline; a route along 06/24 passes the end of 29.
        var r0624 = EastWest("06", "24");
        var r1129 = NorthSouth("11", "29", eastM: 1500.0, fromNorthM: 3060.0, toNorthM: 60.0);
        var nodes = Nodes((1000.0, 0.0), (2000.0, 0.0), (2000.0, -150.0));

        Assert.Empty(RunwayRouteClassifier.ClassifyAll(nodes, new[] { r0624, r1129 }));
    }

    [Fact]
    public void Passages_from_several_runways_are_merged_in_route_order()
    {
        var south = EastWest("09L", "27R", northM: 0.0);
        var north = EastWest("09R", "27L", northM: 400.0);
        var nodes = Nodes((700.0, 600.0), (700.0, 400.0), (700.0, 200.0), (700.0, 0.0), (700.0, -200.0));

        var passages = RunwayRouteClassifier.ClassifyAll(nodes, new[] { south, north });

        Assert.Equal(2, passages.Count);
        Assert.Same(north, passages[0].Runway);
        Assert.Same(south, passages[1].Runway);
    }

    [Fact]
    public void Null_nodes_are_skipped_and_bad_indices_give_no_nodes()
    {
        var nodes = new TaxiNode?[] { Node(1, 500.0, -90.0), null, Node(3, 500.0, 0.0), Node(4, 500.0, 90.0) };
        Assert.Single(RunwayRouteClassifier.Classify(nodes, RunwayShape.For(EastWest())));

        Assert.Empty(RunwayRouteClassifier.NodesFrom(null, 0));
        Assert.Empty(RunwayRouteClassifier.NodesFrom(new List<TaxiRouteSegment>(), 0));
        Assert.Empty(RunwayRouteClassifier.NodesFrom(Route(Node(1, 0, 0), Node(2, 1, 1)), 5));
    }
}
