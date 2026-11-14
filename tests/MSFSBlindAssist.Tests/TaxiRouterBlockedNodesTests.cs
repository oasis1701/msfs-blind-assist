// Blocked nodes in TaxiRouter's searches, and BuildRouteThrough's off-chart segment.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiRouterBlockedNodesTests
{
    private static TaxiPath P(double la1, double lo1, double la2, double lo2, string name) =>
        new TaxiPath { StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2, Name = name };

    private static TaxiNode FindNode(TaxiGraph graph, double lat, double lon)
    {
        TaxiNode? best = null; double bestD = double.MaxValue;
        foreach (var n in graph.Nodes.Values)
        {
            double d = TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best!;
    }

    // Square ring: N-E and E-S are "A"; S-W and W-N are "B".
    private static (TaxiGraph graph, TaxiRouter router, int n, int e, int s, int w) BuildRing()
    {
        var paths = new List<TaxiPath>
        {
            P(37.001, -122.000, 37.000, -121.999, "A"),
            P(37.000, -121.999, 36.999, -122.000, "A"),
            P(36.999, -122.000, 37.000, -122.001, "B"),
            P(37.000, -122.001, 37.001, -122.000, "B"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        return (graph, new TaxiRouter(graph),
            FindNode(graph, 37.001, -122.000).NodeId, FindNode(graph, 37.000, -121.999).NodeId,
            FindNode(graph, 36.999, -122.000).NodeId, FindNode(graph, 37.000, -122.001).NodeId);
    }

    private static List<int> Nodes(TaxiRoute r)
    {
        var ids = new List<int> { r.Segments[0].FromNode.NodeId };
        ids.AddRange(r.Segments.Select(s => s.ToNode.NodeId));
        return ids;
    }

    [Fact]
    public void Empty_block_set_changes_nothing()
    {
        var (graph, router, n, _, s, _) = BuildRing();
        var before = Nodes(router.FindShortestPath(n, s)!);
        var second = new TaxiRouter(graph);
        Assert.Empty(second.BlockedNodes);
        Assert.Equal(before, Nodes(second.FindShortestPath(n, s)!));
    }

    [Fact]
    public void Shortest_path_goes_round_a_blocked_node()
    {
        var (_, router, n, e, s, w) = BuildRing();
        router.BlockedNodes.Add(e);
        var ids = Nodes(router.FindShortestPath(n, s)!);
        Assert.Contains(w, ids);
        Assert.DoesNotContain(e, ids);
    }

    [Fact]
    public void Distances_do_not_reach_through_a_blocked_node()
    {
        var (_, router, n, e, s, w) = BuildRing();
        router.BlockedNodes.Add(e);
        router.BlockedNodes.Add(w);
        Assert.False(router.ComputeGraphDistancesFrom(n).ContainsKey(s));
    }

    [Fact]
    public void A_taxiway_cut_by_a_block_is_not_routable_along_itself()
    {
        var paths = new List<TaxiPath>
        {
            P(37.000, -122.000, 37.001, -122.000, "A"),
            P(37.001, -122.000, 37.002, -122.000, "A"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var router = new TaxiRouter(graph);
        int p = FindNode(graph, 37.000, -122.000).NodeId;
        int q = FindNode(graph, 37.001, -122.000).NodeId;
        int r = FindNode(graph, 37.002, -122.000).NodeId;
        router.BlockedNodes.Add(q);
        Assert.Null(router.FindConstrainedPath(p, r, new List<string> { "A" }));
    }

    [Fact]
    public void BuildRouteThrough_inserts_one_off_chart_segment()
    {
        var (graph, router, n, _, s, _) = BuildRing();
        var route = router.BuildRouteThrough(new[] { n, s }, n, s);
        Assert.NotNull(route);
        var seg = Assert.Single(route!.Segments);
        Assert.True(seg.IsOffChartTurn);
        Assert.Equal("", seg.TaxiwayName);
        Assert.Equal(0, seg.PathWidth);
        var a = graph.Nodes[n]; var b = graph.Nodes[s];
        Assert.InRange(seg.DistanceMeters,
            TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) - 1,
            TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) + 1);
        Assert.InRange(seg.BearingDegrees, 179, 181);
    }

    [Fact]
    public void BuildRouteThrough_computes_turns_across_both_joins()
    {
        var (_, router, n, e, s, w) = BuildRing();
        var route = router.BuildRouteThrough(new[] { w, n, s, e }, n, s);
        Assert.NotNull(route);
        Assert.Equal(3, route!.Segments.Count);
        Assert.True(route.Segments[1].IsOffChartTurn);
        Assert.NotEqual(0, route.Segments[1].TurnAngleDegrees);
        Assert.NotEqual(0, route.Segments[2].TurnAngleDegrees);
        Assert.Equal(route.TotalDistanceMeters, route.Segments[^1].CumulativeDistanceMeters, 6);
    }

    [Fact]
    public void BuildRouteThrough_returns_null_for_a_pair_with_no_edge_that_is_not_the_off_chart_pair()
    {
        var (_, router, n, _, s, _) = BuildRing();
        Assert.Null(router.BuildRouteThrough(new[] { n, s }));
    }
}
