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

    // S runs north to P1; taxiway A goes on north in two sections 55 m apart (past BridgeSameNamedGaps'
    // 40 m), joined only through a node M on a differently named edge X, so strict A* cannot cross and
    // gap chaining must (the second step of "S, A", which has no other entry to try); B is a separate
    // taxiway elsewhere whose node can be blocked.
    private static (TaxiGraph graph, int s0, int p4, int m, int b) BuildGappedA()
    {
        var paths = new List<TaxiPath>
        {
            P(36.9990, -122.0000, 37.0000, -122.0000, "S"),
            P(37.0000, -122.0000, 37.0010, -122.0000, "A"),
            P(37.0010, -122.0000, 37.00125, -121.9994, "X"),
            P(37.00125, -121.9994, 37.0015, -122.0000, "X"),
            P(37.0015, -122.0000, 37.0025, -122.0000, "A"),
            P(37.0000, -121.9900, 37.0010, -121.9900, "B"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        return (graph, FindNode(graph, 36.9990, -122.0000).NodeId, FindNode(graph, 37.0025, -122.0000).NodeId,
            FindNode(graph, 37.00125, -121.9994).NodeId, FindNode(graph, 37.0010, -121.9900).NodeId);
    }

    [Fact]
    public void A_gap_in_a_taxiway_is_still_chained_when_the_block_is_on_another_taxiway()
    {
        var (graph, s0, p4, m, b) = BuildGappedA();
        var seq = new List<string> { "S", "A" };

        // Without blocks: chained across the gap through M.
        var plain = new TaxiRouter(graph).FindConstrainedPath(s0, p4, seq);
        Assert.NotNull(plain);
        Assert.Null(plain!.ConstrainedFallbackReason);
        Assert.Contains(m, Nodes(plain));

        // A block on taxiway B, which A never touches, changes nothing.
        var router = new TaxiRouter(graph);
        router.BlockedNodes.Add(b);
        var blocked = router.FindConstrainedPath(s0, p4, seq);
        Assert.NotNull(blocked);
        Assert.Null(blocked!.ConstrainedFallbackReason);
        Assert.Equal(Nodes(plain), Nodes(blocked));
    }

    // A junction J on taxiway A (the axis, running north). AHEAD of J, A runs J - V - P2, then a 55 m
    // navdata gap crossed only through M on an edge named X, then A again P3 - P4. BEHIND J, A runs
    // J - Pm - P0. Y is a way round the junction, from P2 to P0.
    private static (TaxiGraph graph, Func<double, int> onA, int m) BuildJunctionOnGappedA()
    {
        var paths = new List<TaxiPath>
        {
            P(36.9988, -122.0000, 36.9996, -122.0000, "A"),
            P(36.9996, -122.0000, 37.0000, -122.0000, "A"),
            P(37.0000, -122.0000, 37.0005, -122.0000, "A"),
            P(37.0005, -122.0000, 37.0010, -122.0000, "A"),
            P(37.0010, -122.0000, 37.00125, -121.9994, "X"),
            P(37.00125, -121.9994, 37.0015, -122.0000, "X"),
            P(37.0015, -122.0000, 37.0025, -122.0000, "A"),
            P(37.0010, -122.0000, 37.0010, -122.0012, "Y"),
            P(37.0010, -122.0012, 36.9988, -122.0012, "Y"),
            P(36.9988, -122.0012, 36.9988, -122.0000, "Y"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        return (graph, lat => FindNode(graph, lat, -122.0000).NodeId, FindNode(graph, 37.00125, -121.9994).NodeId);
    }

    [Fact]
    public void A_navdata_gap_ahead_of_a_blocked_junction_is_still_chained()
    {
        // KBOS, 2026-11-14: a left turn onto C at the H junction, and C has 400 m with no name 900 m
        // further east. Blocking the junction must not cost the route that gap.
        var (graph, onA, m) = BuildJunctionOnGappedA();
        var router = new TaxiRouter(graph);
        router.BlockedNodes.Add(onA(37.0000));

        var path = router.AStarSearchWithGapChaining(onA(37.0005), onA(37.0025), "A");

        Assert.NotNull(path);
        Assert.Equal(new[] { onA(37.0005), onA(37.0010), m, onA(37.0015), onA(37.0025) }, path);
    }

    [Fact]
    public void The_block_itself_is_never_chained_across_as_if_it_were_a_gap()
    {
        var (graph, onA, _) = BuildJunctionOnGappedA();
        int v = onA(37.0005), p0 = onA(36.9988);
        // Open, A runs straight through the junction ...
        Assert.NotNull(new TaxiRouter(graph).AStarSearchWithGapChaining(v, p0, "A"));
        // ... and with the junction blocked there IS a way round (Y), which a hop could take.
        var router = new TaxiRouter(graph);
        router.BlockedNodes.Add(onA(37.0000));
        Assert.NotNull(router.FindShortestPath(v, p0));

        // But A behind the junction is no "next section": what parts it from V is the block, not a gap.
        Assert.Null(router.AStarSearchWithGapChaining(v, p0, "A"));
    }

    [Fact]
    public void Nor_is_a_section_behind_the_block_that_a_gap_of_its_own_detaches()
    {
        // As above, but A behind the junction stops at Pm and resumes 66 m on (Pz1 - Pz), joined only
        // through G on an edge named X2; the way round, Y, ends at Pz. Neither Pm's section (it touches
        // the block) nor the detached one (the block stands between it and V) may be hopped to.
        var paths = new List<TaxiPath>
        {
            P(36.9996, -122.0000, 37.0000, -122.0000, "A"),
            P(37.0000, -122.0000, 37.0005, -122.0000, "A"),
            P(37.0005, -122.0000, 37.0010, -122.0000, "A"),
            P(36.9996, -122.0000, 36.9993, -121.9996, "X2"),
            P(36.9993, -121.9996, 36.9990, -122.0000, "X2"),
            P(36.9990, -122.0000, 36.9980, -122.0000, "A"),
            P(37.0010, -122.0000, 37.0010, -122.0012, "Y"),
            P(37.0010, -122.0012, 36.9980, -122.0012, "Y"),
            P(36.9980, -122.0012, 36.9980, -122.0000, "Y"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        int At(double lat) => FindNode(graph, lat, -122.0000).NodeId;
        int v = At(37.0005), pz = At(36.9980);
        Assert.NotNull(new TaxiRouter(graph).AStarSearchWithGapChaining(v, pz, "A"));   // open: through J, across the gap
        var router = new TaxiRouter(graph);
        router.BlockedNodes.Add(At(37.0000));
        Assert.NotNull(router.FindShortestPath(v, pz));

        Assert.Null(router.AStarSearchWithGapChaining(v, pz, "A"));
    }

    [Fact]
    public void A_block_well_off_to_the_side_of_a_gap_hop_does_not_stand_between_its_ends()
    {
        // A has a 400 m navdata gap from E to N, crossed only by X round to the west. A separate piece of
        // A lies 150 m east of the gap's middle (a pick's other arm, blocked): inside the circle on the hop
        // as diameter, so the ends are more than 90 degrees apart seen from it, but nowhere near the hop's
        // line. The hop must still be made.
        var paths = new List<TaxiPath>
        {
            P(36.9990, -122.0000, 37.0000, -122.0000, "A"),
            P(37.0000, -122.0000, 37.0010, -122.0000, "A"),
            P(37.0046, -122.0000, 37.0060, -122.0000, "A"),
            P(37.0010, -122.0000, 37.0010, -122.0020, "X"),
            P(37.0010, -122.0020, 37.0046, -122.0020, "X"),
            P(37.0046, -122.0020, 37.0046, -122.0000, "X"),
            P(37.0028, -121.99831, 37.0028, -121.9915, "A"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        int start = FindNode(graph, 36.9990, -122.0000).NodeId, goal = FindNode(graph, 37.0060, -122.0000).NodeId;
        int n = FindNode(graph, 37.0046, -122.0000).NodeId;
        var router = new TaxiRouter(graph);
        router.BlockedNodes.Add(FindNode(graph, 37.0028, -121.99831).NodeId);

        var path = router.AStarSearchWithGapChaining(start, goal, "A");

        Assert.NotNull(path);
        Assert.Contains(n, path!);
        Assert.Equal(goal, path![^1]);
    }

    [Fact]
    public void BuildRouteThrough_inserts_one_off_chart_segment()
    {
        var (graph, router, n, _, s, _) = BuildRing();
        var route = router.BuildRouteThrough(new[] { n, s }, new[] { (n, s) });
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
        var route = router.BuildRouteThrough(new[] { w, n, s, e }, new[] { (n, s) });
        Assert.NotNull(route);
        Assert.Equal(3, route!.Segments.Count);
        Assert.True(route.Segments[1].IsOffChartTurn);
        Assert.NotEqual(0, route.Segments[1].TurnAngleDegrees);
        Assert.NotEqual(0, route.Segments[2].TurnAngleDegrees);
        Assert.Equal(route.TotalDistanceMeters, route.Segments[^1].CumulativeDistanceMeters, 6);
    }

    [Fact]
    public void BuildRouteThrough_builds_every_off_chart_pair_it_is_given()
    {
        // n-s and e-w are the ring's diagonals, with no edge: w-n (B), n-s (off-chart), s-e (A), e-w (off-chart).
        var (_, router, n, e, s, w) = BuildRing();
        var route = router.BuildRouteThrough(new[] { w, n, s, e, w }, new[] { (n, s), (e, w) });
        Assert.NotNull(route);
        Assert.Equal(4, route!.Segments.Count);
        Assert.Equal(new[] { false, true, false, true }, route.Segments.Select(x => x.IsOffChartTurn));
    }

    [Fact]
    public void BuildRouteThrough_returns_null_for_an_edgeless_pair_not_in_the_collection()
    {
        var (_, router, n, e, s, w) = BuildRing();
        // (n, s) and (e, w) are given; (w, e) is the same diagonal the other way and is not.
        Assert.Null(router.BuildRouteThrough(new[] { n, s, e, w, e }, new[] { (n, s), (e, w) }));
    }

    [Fact]
    public void BuildRouteThrough_returns_null_for_a_pair_with_no_edge_that_is_not_the_off_chart_pair()
    {
        var (_, router, n, _, s, _) = BuildRing();
        Assert.Null(router.BuildRouteThrough(new[] { n, s }));
    }
}
