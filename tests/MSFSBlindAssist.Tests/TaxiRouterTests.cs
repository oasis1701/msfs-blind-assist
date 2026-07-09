// Characterization tests for MSFSBlindAssist.Navigation.TaxiRouter.
//
// FindBestIntersection and ComputeGraphDistancesFrom were promoted from
// private to internal (zero logic change) so this suite can drive them
// directly on a synthetic graph -- large real airport graphs are out of
// scope for a unit test, so coverage here is deliberately the tractable
// subset: the -1/no-Euclidean-fallback contract, graph-distance correctness,
// component-based unreachability, and the turn-direction bucketing. Deeper
// routing scenarios (runway bridges, recalculation fallbacks, lead-in
// snapping) are exercised in tools/ProgressiveTaxiProbe against real navdata
// and are out of scope here.
//
// This is characterization, not spec verification: values are derived by
// reasoning about the source and confirmed by running the tests; if a literal
// ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiRouterTests
{
    // Builds a small "L"-shaped graph:
    //   A1 --A-- A2 --A-- A3       (north-south chain, taxiway "A")
    //           A2 --B-- B2        (east from the A2/B1 junction, taxiway "B")
    // plus a fully disconnected island (taxiway "C") for component tests.
    private static (TaxiGraph graph, TaxiRouter router) BuildLGraph()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 37.000, StartLon = -122.000, EndLat = 37.001, EndLon = -122.000, Name = "A" },
            new TaxiPath { StartLat = 37.001, StartLon = -122.000, EndLat = 37.002, EndLon = -122.000, Name = "A" },
            new TaxiPath { StartLat = 37.001, StartLon = -122.000, EndLat = 37.001, EndLon = -121.999, Name = "B" },
            new TaxiPath { StartLat = 38.000, StartLon = -122.000, EndLat = 38.001, EndLon = -122.000, Name = "C" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        return (graph, new TaxiRouter(graph));
    }

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

    // --- FindBestIntersection ----------------------------------------------------

    [Fact]
    public void FindBestIntersection_returns_the_node_common_to_both_taxiways()
    {
        var (graph, router) = BuildLGraph();
        var a1 = FindNode(graph, 37.000, -122.000);
        var junction = FindNode(graph, 37.001, -122.000); // A2 == B1
        var b2 = FindNode(graph, 37.001, -121.999);

        var distFromB2 = router.ComputeGraphDistancesFrom(b2.NodeId);
        int result = router.FindBestIntersection(a1.NodeId, b2.NodeId, distFromB2, "A", "B");

        Assert.Equal(junction.NodeId, result);
    }

    [Fact]
    public void FindBestIntersection_returns_minus_one_when_the_taxiways_never_meet()
    {
        var (graph, router) = BuildLGraph();
        var a1 = FindNode(graph, 37.000, -122.000);
        var dist = router.ComputeGraphDistancesFrom(a1.NodeId);

        // "A" and "C" share no node anywhere in the graph (and "C" is a disjoint
        // island besides) -- must return -1, never fall back to a Euclidean guess.
        int result = router.FindBestIntersection(a1.NodeId, a1.NodeId, dist, "A", "C");

        Assert.Equal(-1, result);
    }

    // --- ComputeGraphDistancesFrom -----------------------------------------------

    [Fact]
    public void ComputeGraphDistancesFrom_is_zero_at_the_source_and_matches_edge_distance_for_a_neighbor()
    {
        var (graph, router) = BuildLGraph();
        var a1 = FindNode(graph, 37.000, -122.000);
        var a2 = FindNode(graph, 37.001, -122.000);

        var dist = router.ComputeGraphDistancesFrom(a1.NodeId);

        Assert.Equal(0.0, dist[a1.NodeId]);
        Assert.True(dist.ContainsKey(a2.NodeId));
        // Edge.DistanceMeters is computed at Build time via TaxiGraph.CalculateDistanceMeters
        // (haversine), not the FastDistanceMeters equirectangular approximation.
        Assert.Equal(
            TaxiGraph.CalculateDistanceMeters(a1.Latitude, a1.Longitude, a2.Latitude, a2.Longitude),
            dist[a2.NodeId], 1);
    }

    [Fact]
    public void ComputeGraphDistancesFrom_does_not_reach_a_disconnected_component()
    {
        var (graph, router) = BuildLGraph();
        var a1 = FindNode(graph, 37.000, -122.000);
        var c1 = FindNode(graph, 38.000, -122.000);

        var dist = router.ComputeGraphDistancesFrom(a1.NodeId);

        Assert.False(dist.ContainsKey(c1.NodeId));
    }

    [Fact]
    public void ComputeGraphDistancesFrom_returns_empty_for_an_unknown_source_node()
    {
        var (graph, router) = BuildLGraph();

        var dist = router.ComputeGraphDistancesFrom(999999);

        Assert.Empty(dist);
    }

    // --- FindShortestPath / FindConstrainedPath (component filtering) -----------

    [Fact]
    public void FindShortestPath_returns_null_when_start_and_end_are_in_different_components()
    {
        var (graph, router) = BuildLGraph();
        var a1 = FindNode(graph, 37.000, -122.000);
        var c1 = FindNode(graph, 38.000, -122.000);

        var route = router.FindShortestPath(a1.NodeId, c1.NodeId);

        Assert.Null(route);
    }

    [Fact]
    public void FindConstrainedPath_follows_the_requested_taxiway_sequence()
    {
        var (graph, router) = BuildLGraph();
        var a1 = FindNode(graph, 37.000, -122.000);
        var b2 = FindNode(graph, 37.001, -121.999);

        var route = router.FindConstrainedPath(a1.NodeId, b2.NodeId, new List<string> { "A", "B" });

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var names = route.Segments.Select(s => s.TaxiwayName).ToList();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
    }

    // --- GetTurnDirection ---------------------------------------------------------

    [Theory]
    [InlineData(0, "straight")]
    [InlineData(19, "straight")]
    [InlineData(-19, "straight")]
    [InlineData(20, "slight right")]
    [InlineData(-20, "slight left")]
    [InlineData(59, "slight right")]
    [InlineData(-59, "slight left")]
    [InlineData(60, "right")]
    [InlineData(-60, "left")]
    [InlineData(150, "right")]
    [InlineData(-150, "left")]
    public void GetTurnDirection_buckets_by_absolute_angle(double angle, string expected)
    {
        Assert.Equal(expected, TaxiRouter.GetTurnDirection(angle));
    }
}
