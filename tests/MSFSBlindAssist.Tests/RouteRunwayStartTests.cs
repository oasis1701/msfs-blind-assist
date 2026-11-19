// Where a taxi route starts for an aircraft standing on a runway (Navigation.RouteRunwayStart).
//
// Live RJAA 34L, 2026-11-06: stopped on the centreline between exits, a clearance that could not
// be routed, and a route that began on the parallel taxiway 200 m to the side. The tone aimed
// straight across the infield. Nodes on the runway's own pavement are the honest start.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class RouteRunwayStartTests
{
    private const int Component = 1;

    private static TaxiNode Named(int id, double east, double north, string name)
    {
        var node = Node(id, east, north);
        node.TaxiwayNames.Add(name);
        node.ComponentId = Component;
        return node;
    }

    // East-west runway 09/27, 3,000 m long, pavement half-width 30 m, centreline at north = 0.
    private static TaxiGraph Graph(params TaxiNode[] nodes)
    {
        var graph = new TaxiGraph();
        graph.RunwayCenterlines.Add(EastWest("09", "27", lengthM: 3000.0, halfWidthM: 30.0));
        foreach (var node in nodes) graph.Nodes[node.NodeId] = node;
        return graph;
    }

    private static TaxiNode? Pick(TaxiGraph graph, double heading = 90.0, string? cleared = null,
        double east = 1000.0, double north = 0.0)
        => RouteRunwayStart.Pick(graph, Lat(north), Lon(east), heading, cleared, Component);

    [Fact]
    public void Cleared_taxiway_on_the_runway_ahead_is_the_start()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(2, 1700, 1, "A1"));

        Assert.Equal(2, Pick(graph, cleared: "A1")!.NodeId);
    }

    [Fact]
    public void No_clearance_takes_the_nearest_node_ahead()
    {
        var graph = Graph(Named(2, 1700, 1, "A1"), Named(1, 1400, 1, "A2"));

        Assert.Equal(1, Pick(graph)!.NodeId);
    }

    [Fact]
    public void Cleared_taxiway_off_the_runway_falls_to_the_nearest_node_ahead()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(3, 1200, 200, "A"));

        Assert.Equal(1, Pick(graph, cleared: "A")!.NodeId);
    }

    [Fact]
    public void Nodes_off_the_pavement_are_never_picked()
    {
        var graph = Graph(Named(3, 1050, 200, "A"));

        Assert.Null(Pick(graph));
    }

    [Fact]
    public void Facing_the_other_way_flips_ahead()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(4, 700, 1, "A3"));

        Assert.Equal(4, Pick(graph, heading: 270.0)!.NodeId);
    }

    [Fact]
    public void No_node_ahead_takes_the_nearest_behind()
    {
        var graph = Graph(Named(5, 300, 1, "A4"), Named(4, 700, 1, "A3"));

        Assert.Equal(4, Pick(graph)!.NodeId);
    }

    [Fact]
    public void A_node_under_the_nose_is_skipped()
    {
        var graph = Graph(Named(6, 1010, 1, "B"), Named(1, 1400, 1, "A2"));

        Assert.Equal(1, Pick(graph)!.NodeId);
    }

    [Fact]
    public void Off_the_runway_returns_null()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"));

        Assert.Null(Pick(graph, north: 200.0));
    }

    [Fact]
    public void Crossing_the_runway_returns_null()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"));

        Assert.Null(Pick(graph, heading: 0.0));
    }

    [Fact]
    public void A_heading_more_than_45_degrees_off_the_runway_returns_null()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"));

        Assert.Null(Pick(graph, heading: 140.0));
    }

    [Theory]
    [InlineData(90.0, 1)]
    [InlineData(0.0, 7)]
    public void At_an_intersection_the_aligned_runway_decides(double heading, int expectedNode)
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(7, 1000, 300, "K"));
        graph.RunwayCenterlines.Add(NorthSouth("18", "36", 1000.0, -1500.0, 1500.0));

        Assert.Equal(expectedNode, Pick(graph, heading: heading)!.NodeId);
    }

    [Fact]
    public void Other_component_nodes_are_excluded()
    {
        var island = Named(8, 1200, 1, "Z");
        island.ComponentId = Component + 1;
        var graph = Graph(island, Named(1, 1400, 1, "A2"));

        Assert.Equal(1, Pick(graph)!.NodeId);
    }

    [Fact]
    public void Candidates_come_cleared_first_then_ahead_then_behind()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(2, 1700, 1, "A1"), Named(4, 700, 1, "A3"));

        var ids = RouteRunwayStart.Candidates(graph, Lat(0), Lon(1000), 90.0, "A1", Component).Select(n => n.NodeId);

        Assert.Equal(new[] { 2, 1, 4 }, ids);
    }

    [Fact]
    public void Candidates_are_capped()
    {
        var nodes = Enumerable.Range(1, 12).Select(i => Named(i, 1000 + 100 * i, 1, "T" + i)).ToArray();

        Assert.Equal(RouteRunwayStart.MaxCandidates,
            RouteRunwayStart.Candidates(Graph(nodes), Lat(0), Lon(1000), 90.0, null, Component).Count);
    }

    [Fact]
    public void Candidates_skip_other_nodes_of_the_same_exit()
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(9, 1420, 5, "A2"), Named(2, 1700, 1, "A1"));

        var ids = RouteRunwayStart.Candidates(graph, Lat(0), Lon(1000), 90.0, null, Component).Select(n => n.NodeId);

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Theory]
    [InlineData(1040.0)]   // just ahead, inside the lead-in range
    [InlineData(960.0)]    // just behind: stopped abeam or just past the cleared exit
    public void A_cleared_taxiway_within_lead_in_range_keeps_the_older_start(double clearedEast)
    {
        var graph = Graph(Named(1, 1400, 1, "A2"), Named(2, clearedEast, 1, "A1"));

        Assert.Empty(RouteRunwayStart.Candidates(graph, Lat(0), Lon(1000), 90.0, "A1", Component));
    }

    [Fact]
    public void A_node_beyond_an_intersecting_runway_is_dropped()
    {
        var graph = Graph(Named(1, 1100, 1, "B"), Named(2, 1400, 1, "A2"));
        graph.RunwayCenterlines.Add(NorthSouth("18", "36", 1200.0, -1500.0, 1500.0));

        var ids = RouteRunwayStart.Candidates(graph, Lat(0), Lon(1000), 90.0, null, Component).Select(n => n.NodeId);

        Assert.Equal(new[] { 1 }, ids);
    }

    [Fact]
    public void A_runway_the_aircraft_already_stands_on_does_not_block_the_roll()
    {
        var graph = Graph(Named(2, 1400, 1, "A2"));
        graph.RunwayCenterlines.Add(NorthSouth("18", "36", 1000.0, -1500.0, 1500.0));

        Assert.Equal(2, Pick(graph)!.NodeId);
    }

    [Theory]
    [InlineData(true, false, 0, 400.0, true)]    // on the roll to the start node
    [InlineData(true, false, 0, 40.0, false)]    // inside the look-ahead: the walk takes over
    [InlineData(true, true, 0, 400.0, false)]    // already on the route
    [InlineData(true, false, 1, 400.0, false)]   // past the first segment
    [InlineData(false, false, 0, 400.0, false)]  // not a route started on a runway
    public void The_tone_aims_at_the_start_node_only_on_the_roll_to_it(
        bool startedOnRunway, bool joined, int segment, double distance, bool expected)
        => Assert.Equal(expected, RouteRunwayStart.AimsAtStartNode(startedOnRunway, joined, segment, distance, 50.0));

    private static bool Returns(TaxiGraph graph, params TaxiNode[] route)
        => RouteRunwayStart.RouteReturnsToRunway(graph, Route(route), Lat(0), Lon(1000), 90.0);

    [Fact]
    public void A_route_that_leaves_then_comes_back_onto_the_runway_returns_to_it()
        => Assert.True(Returns(Graph(), Node(1, 1100, 1), Node(2, 1200, 50), Node(3, 1300, 60), Node(4, 1400, -5), Node(5, 1450, -60)));

    [Fact]
    public void A_route_that_simply_leaves_does_not_return()
        => Assert.False(Returns(Graph(), Node(1, 1100, 1), Node(2, 1200, 50), Node(3, 1300, 100)));

    [Fact]
    public void A_route_straight_across_does_not_return()
        => Assert.False(Returns(Graph(), Node(1, 1100, 1), Node(2, 1100, -60), Node(3, 1100, -120)));

    [Theory]
    [InlineData(false, null, false, true)]
    [InlineData(false, "", false, true)]
    [InlineData(true, null, false, false)]
    [InlineData(false, "A3", false, false)]
    [InlineData(false, null, true, false)]
    public void Applies_only_to_a_plain_taxi_route(
        bool isRunwayDestination, string? startTaxiwayName, bool landingRolloutRoute, bool expected)
        => Assert.Equal(expected, RouteRunwayStart.Applies(isRunwayDestination, startTaxiwayName, landingRolloutRoute));
}
