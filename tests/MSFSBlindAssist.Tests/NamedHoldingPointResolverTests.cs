// Characterization tests for MSFSBlindAssist.Navigation.NamedHoldingPointResolver.
//
// The resolver attaches online-sourced NAMED holding points (OSM
// aeroway=holding_position refs — VIKAS, N2E, A11…) onto navdata taxi-graph
// nodes, alias-style: name adopted, geometry always the navdata node's. These
// tests pin the safety-relevant ranking rules on a synthetic TaxiGraph
// (TaxiGraph.Build accepts plain lists — no SQLite needed) laid out on a local
// metre grid at (37.0, -122.0), where 1e-5 deg latitude ≈ 1.113 m.
//
// This is characterization, not spec verification: if a literal ever disagrees
// with actual output, the test must be corrected to match real output, not the
// other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class NamedHoldingPointResolverTests
{
    private const double REF_LAT = 37.0;
    private const double BASE_LON = -122.0;
    private const double DEG_TO_M_LAT = 111320.0;

    private static double LatN(double northM) => REF_LAT + northM / DEG_TO_M_LAT;

    private static double LonE(double eastM)
    {
        double degToMLon = DEG_TO_M_LAT * Math.Cos(REF_LAT * Math.PI / 180.0);
        return BASE_LON + eastM / degToMLon;
    }

    // A short edge whose endpoints become graph nodes at the given north/east
    // metre offsets. Endpoint type strings follow navdata ("HS"/"HSND" →
    // HoldShort, "IHS"/"IHSND" → ILSHoldShort, "" → Normal).
    private static TaxiPath Edge(double northA, double eastA, string typeA,
                                  double northB, double eastB, string typeB,
                                  string name = "A") => new TaxiPath
    {
        StartLat = LatN(northA), StartLon = LonE(eastA), StartType = typeA,
        EndLat = LatN(northB), EndLon = LonE(eastB), EndType = typeB,
        Name = name,
        Width = 50,
    };

    private static TaxiGraph BuildGraph(params TaxiPath[] paths) =>
        TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static TaxiNode NodeNear(TaxiGraph graph, double northM, double eastM)
    {
        double lat = LatN(northM), lon = LonE(eastM);
        TaxiNode? best = null; double bestD = double.MaxValue;
        foreach (var n in graph.Nodes.Values)
        {
            double d = TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best!;
    }

    // --- Snapping ------------------------------------------------------------

    [Fact]
    public void Resolve_snaps_to_nearest_plain_node_within_max_snap()
    {
        // Nodes at 0 m and 100 m north; the online point sits 10 m north of the first.
        var graph = BuildGraph(Edge(0, 0, "", 100, 0, ""));
        var points = new[] { ("VIKAS", LatN(10), LonE(0), "intermediate") };

        var result = NamedHoldingPointResolver.Resolve(graph, points);

        var expected = NodeNear(graph, 0, 0);
        var hp = Assert.Single(result);
        Assert.Equal("VIKAS", hp.Name);
        Assert.Equal(expected.NodeId, hp.NodeId);
        Assert.Equal(expected.Latitude, hp.Latitude);   // navdata geometry, not the online coordinate
        Assert.Equal(expected.Longitude, hp.Longitude);
        Assert.False(hp.SnappedToDesignatedNode);
        Assert.Equal(10.0, hp.SnapDistanceMeters, 1.0);
    }

    [Fact]
    public void Resolve_prefers_designated_hold_node_over_a_nearer_plain_node()
    {
        // Plain node 3 m from the point, designated HS node 12 m from it —
        // the painted hold line (HS) must win over the nearer centerline vertex.
        var graph = BuildGraph(
            Edge(3, 0, "", 60, 0, ""),
            Edge(-12, 0, "HSND", -60, 0, ""));
        var designated = NodeNear(graph, -12, 0);
        Assert.Equal(TaxiNodeType.HoldShort, designated.Type);   // fixture sanity

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("N2E", LatN(0), LonE(0), "runway") });

        var hp = Assert.Single(result);
        Assert.Equal(designated.NodeId, hp.NodeId);
        Assert.True(hp.SnappedToDesignatedNode);
    }

    [Fact]
    public void Resolve_designated_preference_stops_beyond_designated_snap_radius()
    {
        // Designated node at 20 m (> DESIGNATED_SNAP_M 15) loses to the plain node at 5 m.
        var graph = BuildGraph(
            Edge(5, 0, "", 60, 0, ""),
            Edge(-20, 0, "HSND", -60, 0, ""));

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("A11", LatN(0), LonE(0), "ILS") });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 5, 0).NodeId, hp.NodeId);
        Assert.False(hp.SnappedToDesignatedNode);
    }

    [Fact]
    public void Resolve_drops_points_with_no_node_within_max_snap()
    {
        // Nearest node is 40 m away (> MAX_SNAP_M 30) — the point must be
        // DROPPED, never attached to far-away geometry.
        var graph = BuildGraph(Edge(40, 0, "", 100, 0, ""));

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("SNAPA", LatN(0), LonE(0), "") });

        Assert.Empty(result);
    }

    // --- Duplicate names -----------------------------------------------------

    [Fact]
    public void Resolve_collapses_duplicate_names_keeping_the_designated_snap()
    {
        // Same name twice (parallel painted lines): one occurrence snaps a plain
        // node at 2 m, the other a designated node at 10 m. Designated wins.
        var graph = BuildGraph(
            Edge(2, 0, "", 60, 0, ""),
            Edge(-10, 200, "HSND", -60, 200, ""));

        var result = NamedHoldingPointResolver.Resolve(graph, new[]
        {
            ("SATUN", LatN(0), LonE(0), "intermediate"),
            ("SATUN", LatN(0), LonE(200), "intermediate"),
        });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, -10, 200).NodeId, hp.NodeId);
        Assert.True(hp.SnappedToDesignatedNode);
    }

    [Fact]
    public void Resolve_collapses_same_class_duplicates_by_snap_distance()
    {
        var graph = BuildGraph(
            Edge(8, 0, "", 60, 0, ""),
            Edge(-3, 200, "", -60, 200, ""));

        var result = NamedHoldingPointResolver.Resolve(graph, new[]
        {
            ("A4", LatN(0), LonE(0), "runway"),
            ("A4", LatN(0), LonE(200), "runway"),
        });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, -3, 200).NodeId, hp.NodeId);
    }

    // --- Output shape --------------------------------------------------------

    [Fact]
    public void Resolve_sorts_results_by_name_and_skips_blank_names()
    {
        var graph = BuildGraph(Edge(0, 0, "", 0, 500, ""));

        var result = NamedHoldingPointResolver.Resolve(graph, new[]
        {
            ("VIKAS", LatN(0), LonE(0), ""),
            ("", LatN(0), LonE(0), ""),
            ("  ", LatN(0), LonE(0), ""),
            ("DASSO", LatN(0), LonE(500), ""),
        });

        Assert.Equal(new[] { "DASSO", "VIKAS" }, result.Select(p => p.Name).ToArray());
    }

    [Theory]
    [InlineData("runway", "N2E (runway hold)")]
    [InlineData("ILS", "A11 (ILS hold)")]
    [InlineData("intermediate", "A11 (intermediate hold)")]
    [InlineData("", "A11")]
    public void DisplayLabel_appends_the_kind_suffix(string kind, string expected)
    {
        string name = expected.Split(' ')[0];
        var hp = new NamedHoldingPoint { Name = name, Kind = kind };
        Assert.Equal(expected, hp.DisplayLabel);
    }
}
