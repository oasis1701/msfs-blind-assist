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

    [Fact]
    public void Resolve_reports_a_designated_node_chosen_via_the_plain_path_as_designated()
    {
        // The only node within MAX_SNAP_M is an HS node at 20 m — outside the 15 m
        // designated PREFERENCE, so it is selected through the plain fallback. The
        // flag describes the NODE, so it must still read designated.
        var graph = BuildGraph(Edge(20, 0, "HSND", 80, 0, ""));

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("N2E", LatN(0), LonE(0), "runway") });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 20, 0).NodeId, hp.NodeId);
        Assert.True(hp.SnappedToDesignatedNode);
    }

    [Fact]
    public void Resolve_duplicate_ranking_ignores_a_designated_node_beyond_the_preference_radius()
    {
        // Regression pin: this must pass BEFORE and AFTER the flag fix. Same name
        // twice — one occurrence sees only a designated node at 20 m (outside the
        // 15 m preference), the other a plain node at 18 m. The nearer plain node
        // must win: a designated node that far out can be a DIFFERENT hold line
        // (measured — EDDF M15 sits 91 m off its own point).
        var graph = BuildGraph(
            Edge(20, 0, "HSND", 80, 0, ""),
            Edge(18, 200, "", 80, 200, ""));

        var result = NamedHoldingPointResolver.Resolve(graph, new[]
        {
            ("A4", LatN(0), LonE(0), "runway"),
            ("A4", LatN(0), LonE(200), "runway"),
        });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 18, 200).NodeId, hp.NodeId);
        Assert.False(hp.SnappedToDesignatedNode);
    }

    [Fact]
    public void Resolve_never_snaps_to_a_parking_node()
    {
        // Characterization pin for an untested safety rule: a stand connector is not
        // a holding point. The parking node at 3 m must be skipped in favour of the
        // plain taxiway node at 12 m.
        var graph = BuildGraph(
            Edge(3, 0, "P", 3, 60, "P", name: "STAND"),
            Edge(12, 0, "", 80, 0, ""));

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("VIKAS", LatN(0), LonE(0), "intermediate") });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 12, 0).NodeId, hp.NodeId);
    }

    // --- Edge-projection fallback (EGLL LOMAN) -------------------------------

    [Fact]
    public void Resolve_projects_onto_the_edge_when_the_point_is_on_pavement_but_between_vertices()
    {
        // The EGLL LOMAN case, to scale: a 200 m taxiway leg with vertices only at its
        // ends, and a painted point ON the centreline at the midpoint — 0 m from the
        // edge but 100 m from either vertex, so no node-snap radius can reach it.
        var graph = BuildGraph(Edge(0, 0, "", 200, 0, ""));
        int nodesBefore = graph.Nodes.Count;

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("LOMAN", LatN(100), LonE(0), "intermediate") });

        var hp = Assert.Single(result);
        Assert.Equal("LOMAN", hp.Name);
        Assert.True(hp.InsertedOnEdge);
        Assert.False(hp.SnappedToDesignatedNode);
        // Placed exactly on the paint, not 100 m up or down the taxiway.
        Assert.Equal(0.0, hp.SnapDistanceMeters, 1.0);
        Assert.Equal(LatN(100), hp.Latitude, 6);

        // A pure subdivision: one node added, degree 2, both halves present.
        Assert.Equal(nodesBefore + 1, graph.Nodes.Count);
        var inserted = graph.Nodes[hp.NodeId];
        Assert.Equal(TaxiNodeType.Normal, inserted.Type);
        Assert.Equal(2, graph.Adjacency[hp.NodeId].Count);
        Assert.Equal(graph.Nodes[NodeNear(graph, 0, 0).NodeId].ComponentId, inserted.ComponentId);
    }

    [Fact]
    public void Resolve_drops_a_point_that_is_near_neither_a_node_nor_the_pavement()
    {
        // EGLL A12/AB12/AY1: 55-85 m from any edge. Off-pavement stays dropped —
        // the fallback is an "on the pavement?" test, not a wider search radius.
        var graph = BuildGraph(Edge(0, 0, "", 200, 0, ""));
        int nodesBefore = graph.Nodes.Count;

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("A12", LatN(100), LonE(60), "ILS") });

        Assert.Empty(result);
        Assert.Equal(nodesBefore, graph.Nodes.Count);   // nothing inserted on a miss
    }

    [Fact]
    public void Resolve_prefers_an_existing_node_over_projecting_a_new_one()
    {
        // The fallback is strictly a fallback: a point 12 m from a real vertex takes
        // that vertex, even though projecting it would land 0 m from the centreline.
        var graph = BuildGraph(Edge(0, 0, "", 200, 0, ""));
        int nodesBefore = graph.Nodes.Count;

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("VIKAS", LatN(12), LonE(0), "intermediate") });

        var hp = Assert.Single(result);
        Assert.False(hp.InsertedOnEdge);
        Assert.Equal(NodeNear(graph, 0, 0).NodeId, hp.NodeId);
        Assert.Equal(nodesBefore, graph.Nodes.Count);
    }

    [Fact]
    public void Resolve_duplicate_ranking_keeps_the_node_snap_over_a_nearer_edge_projection()
    {
        // Additive-only guarantee: a name that resolves today must resolve to the SAME
        // node after the change. One painted line sits 25 m from a vertex (a valid node
        // snap); its twin sits exactly on a different taxiway's centreline, which would
        // project at 0 m. The node snap must still win, or the pilot would silently be
        // selecting the other physical line (EGLL A4/SATUN are parallel painted lines).
        var graph = BuildGraph(
            Edge(25, 0, "", 200, 0, ""),
            Edge(0, 400, "", 200, 400, "", name: "B"));

        var result = NamedHoldingPointResolver.Resolve(graph, new[]
        {
            ("A4", LatN(0), LonE(0), "runway"),
            ("A4", LatN(100), LonE(400), "runway"),
        });

        var hp = Assert.Single(result);
        Assert.False(hp.InsertedOnEdge);
        Assert.Equal(NodeNear(graph, 25, 0).NodeId, hp.NodeId);
    }

    [Fact]
    public void Resolve_is_idempotent_across_repeated_calls_on_the_same_graph()
    {
        // The form re-resolves until the async online fetch has been seen, so the same
        // graph can be resolved several times. The second pass must find the node the
        // first inserted, not split the (now halved) edge again.
        var graph = BuildGraph(Edge(0, 0, "", 200, 0, ""));
        var points = new[] { ("LOMAN", LatN(100), LonE(0), "intermediate") };

        var first = NamedHoldingPointResolver.Resolve(graph, points);
        int afterFirst = graph.Nodes.Count;
        var second = NamedHoldingPointResolver.Resolve(graph, points);

        Assert.Equal(afterFirst, graph.Nodes.Count);
        Assert.Equal(first[0].NodeId, second[0].NodeId);
        Assert.False(second[0].InsertedOnEdge);   // second pass is a plain node snap
    }

    [Fact]
    public void Resolve_never_projects_onto_a_parking_connector_edge()
    {
        // Same rule as the node snap: a stand connector is not a holding point. The
        // point sits on the stand lead-in, 40 m from the taxiway — nothing resolves.
        var graph = BuildGraph(
            Edge(0, 0, "P", 0, 200, "P", name: "STAND"),
            Edge(40, 0, "", 40, 200, ""));
        int nodesBefore = graph.Nodes.Count;

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("VIKAS", LatN(0), LonE(100), "intermediate") });

        Assert.Empty(result);
        Assert.Equal(nodesBefore, graph.Nodes.Count);
    }

    [Theory]
    [InlineData("RUNWAY", "N2E (runway hold)")]
    [InlineData("ils", "A11 (ILS hold)")]
    [InlineData("  intermediate  ", "A11 (intermediate hold)")]
    public void DisplayLabel_tolerates_kind_casing_and_whitespace(string kind, string expected)
    {
        string name = expected.Split(' ')[0];
        var hp = new NamedHoldingPoint { Name = name, Kind = kind };
        Assert.Equal(expected, hp.DisplayLabel);
    }
}
