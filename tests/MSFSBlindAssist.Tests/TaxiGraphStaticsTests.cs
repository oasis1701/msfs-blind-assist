// Characterization tests for the pure static geometry helpers on
// MSFSBlindAssist.Navigation.TaxiGraph, plus the alias/collision/ambiguity
// guards on the instance method ResolveTaxiwayName.
//
// This is characterization, not spec verification: values are derived by
// reasoning about the source and confirmed by running the tests; if a literal
// ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiGraphStaticsTests
{
    // --- FastDistanceMeters ---------------------------------------------------

    [Fact]
    public void FastDistanceMeters_one_degree_of_latitude_is_111132_metres()
    {
        Assert.Equal(111132.0, TaxiGraph.FastDistanceMeters(0, 0, 1, 0), 1);
    }

    [Fact]
    public void FastDistanceMeters_is_zero_for_the_same_point()
    {
        Assert.Equal(0.0, TaxiGraph.FastDistanceMeters(37.5, -122.3, 37.5, -122.3), 6);
    }

    // --- PerpendicularDistanceMetersStatic -------------------------------------

    [Fact]
    public void PerpendicularDistanceMetersStatic_is_zero_for_a_point_on_the_segment()
    {
        // Segment running north along the equator, midpoint of the segment.
        double d = TaxiGraph.PerpendicularDistanceMetersStatic(0.5, 0.0, 0.0, 0.0, 1.0, 0.0);
        Assert.Equal(0.0, d, 1);
    }

    [Fact]
    public void PerpendicularDistanceMetersStatic_measures_the_perpendicular_offset()
    {
        // Segment runs north along the equator (lon=0); point is offset 0.001 deg
        // east at the segment's midpoint latitude. At the equator, 1 deg lon = 1 deg
        // lat = 111132 m (cos(0)=1), so the perpendicular offset is ~111.132 m.
        double d = TaxiGraph.PerpendicularDistanceMetersStatic(0.5, 0.001, 0.0, 0.0, 1.0, 0.0);
        Assert.Equal(111.132, d, 1);
    }

    [Fact]
    public void PerpendicularDistanceMetersStatic_clamps_to_the_nearest_endpoint_beyond_the_segment()
    {
        // Point is due north of segment endpoint b=(1,0) -- outside the segment, so
        // distance must clamp to the endpoint rather than the infinite line (which
        // would also give 0 here, masking the clamp).
        double d = TaxiGraph.PerpendicularDistanceMetersStatic(2.0, 0.0, 0.0, 0.0, 1.0, 0.0);
        Assert.Equal(111132.0, d, 1);
    }

    // --- EdgeCrossesRunwayStatic (strict proper intersection) ------------------

    [Fact]
    public void EdgeCrossesRunwayStatic_true_for_a_perpendicular_crossing()
    {
        // Runway centerline runs east-west through (37.0, -122.0); taxi edge runs
        // north-south through the same point -- a proper crossing.
        bool crosses = TaxiGraph.EdgeCrossesRunwayStatic(
            aLat: 36.999, aLon: -122.000, bLat: 37.001, bLon: -122.000,
            t1Lat: 37.000, t1Lon: -122.001, t2Lat: 37.000, t2Lon: -121.999);

        Assert.True(crosses);
    }

    [Fact]
    public void EdgeCrossesRunwayStatic_false_for_a_parallel_edge_that_never_crosses()
    {
        // Taxi edge runs east-west, offset north of and parallel to the runway --
        // never crosses.
        bool crosses = TaxiGraph.EdgeCrossesRunwayStatic(
            aLat: 37.0005, aLon: -122.001, bLat: 37.0005, bLon: -121.999,
            t1Lat: 37.000, t1Lon: -122.001, t2Lat: 37.000, t2Lon: -121.999);

        Assert.False(crosses);
    }

    [Fact]
    public void EdgeCrossesRunwayStatic_false_when_the_edge_only_touches_a_threshold_endpoint()
    {
        // Edge endpoint 'a' sits exactly at the runway's t1 threshold -- touching,
        // not a proper (strict opposite-sides) crossing.
        bool crosses = TaxiGraph.EdgeCrossesRunwayStatic(
            aLat: 37.000, aLon: -122.001, bLat: 37.001, bLon: -122.001,
            t1Lat: 37.000, t1Lon: -122.001, t2Lat: 37.000, t2Lon: -121.999);

        Assert.False(crosses);
    }

    // --- MatchHoldShortRunwayName -------------------------------------------------

    private static TaxiGraph.RunwayCenterline MakeCenterline() => new TaxiGraph.RunwayCenterline
    {
        Lat1 = 37.000, Lon1 = -122.001, Name1 = "09",
        Lat2 = 37.000, Lon2 = -121.999, Name2 = "27",
        HeadingDeg1 = 90,
        HalfWidthMeters = 23,
    };

    [Fact]
    public void MatchHoldShortRunwayName_returns_the_closer_end_designator()
    {
        var centerlines = new List<TaxiGraph.RunwayCenterline> { MakeCenterline() };

        // Near the west (09) end.
        string? west = TaxiGraph.MatchHoldShortRunwayName(37.0001, -122.0009, centerlines, 200);
        Assert.Equal("09", west);

        // Near the east (27) end.
        string? east = TaxiGraph.MatchHoldShortRunwayName(37.0001, -121.9991, centerlines, 200);
        Assert.Equal("27", east);
    }

    [Fact]
    public void MatchHoldShortRunwayName_returns_null_beyond_the_match_tolerance()
    {
        var centerlines = new List<TaxiGraph.RunwayCenterline> { MakeCenterline() };

        string? result = TaxiGraph.MatchHoldShortRunwayName(37.01, -122.0005, centerlines, maxMatchMeters: 50);

        Assert.Null(result);
    }

    [Fact]
    public void MatchHoldShortRunwayName_returns_null_when_no_centerlines_are_given()
    {
        string? result = TaxiGraph.MatchHoldShortRunwayName(37.0, -122.0005, Array.Empty<TaxiGraph.RunwayCenterline>(), 200);
        Assert.Null(result);
    }

    // --- GetNodesOnTaxiway -----------------------------------------------------

    [Fact]
    public void GetNodesOnTaxiway_returns_every_node_registered_under_that_name_case_insensitively()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 37.000, StartLon = -122.000, EndLat = 37.001, EndLon = -122.000, Name = "A" },
            new TaxiPath { StartLat = 37.001, StartLon = -122.000, EndLat = 37.002, EndLon = -122.000, Name = "A" },
            new TaxiPath { StartLat = 37.000, StartLon = -121.900, EndLat = 37.001, EndLon = -121.900, Name = "B" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var onA = graph.GetNodesOnTaxiway("A");
        Assert.Equal(3, onA.Count); // three distinct nodes along the A chain

        var onALower = graph.GetNodesOnTaxiway("a");
        Assert.Equal(onA.Count, onALower.Count);

        var onB = graph.GetNodesOnTaxiway("B");
        Assert.Equal(2, onB.Count);
    }

    [Fact]
    public void GetNodesOnTaxiway_returns_empty_for_an_unknown_or_blank_name()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 37.000, StartLon = -122.000, EndLat = 37.001, EndLon = -122.000, Name = "A" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        Assert.Empty(graph.GetNodesOnTaxiway("Z"));
        Assert.Empty(graph.GetNodesOnTaxiway(""));
    }

    // --- ResolveTaxiwayName: alias / collision / ambiguity guards ---------------

    [Fact]
    public void ResolveTaxiwayName_resolves_a_bare_alias_to_its_canonical_navdata_name()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath
            {
                StartLat = 37.0, StartLon = -122.0, EndLat = 37.001, EndLon = -122.0,
                Name = "K", Aliases = new List<string> { "B" },
            },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        Assert.Equal("K", graph.ResolveTaxiwayName("B"));
        Assert.Equal("K", graph.ResolveTaxiwayName("b")); // normalized, case-insensitive
    }

    [Fact]
    public void ResolveTaxiwayName_never_remaps_a_name_that_is_itself_a_real_taxiway()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath
            {
                StartLat = 37.0, StartLon = -122.0, EndLat = 37.001, EndLon = -122.0,
                Name = "K", Aliases = new List<string> { "B" },
            },
            new TaxiPath { StartLat = 37.0, StartLon = -121.9, EndLat = 37.001, EndLon = -121.9, Name = "B" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        // "B" is a genuine navdata taxiway -- must resolve to itself, not "K".
        Assert.Equal("B", graph.ResolveTaxiwayName("B"));
    }

    [Fact]
    public void ResolveTaxiwayName_leaves_an_ambiguous_bare_alias_unresolved()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath
            {
                StartLat = 37.0, StartLon = -122.0, EndLat = 37.001, EndLon = -122.0,
                Name = "K", Aliases = new List<string> { "B" },
            },
            new TaxiPath
            {
                StartLat = 37.0, StartLon = -121.9, EndLat = 37.001, EndLon = -121.9,
                Name = "M", Aliases = new List<string> { "B" },
            },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        // Two different canonicals both alias "B" -- the bare form can't safely
        // pick one, so it passes through unresolved.
        Assert.Equal("B", graph.ResolveTaxiwayName("B"));
    }

    [Fact]
    public void ResolveTaxiwayName_resolves_an_exact_disambiguated_label_even_when_the_bare_alias_is_ambiguous()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath
            {
                StartLat = 37.0, StartLon = -122.0, EndLat = 37.001, EndLon = -122.0,
                Name = "K", Aliases = new List<string> { "B" },
            },
            new TaxiPath
            {
                StartLat = 37.0, StartLon = -121.9, EndLat = 37.001, EndLon = -121.9,
                Name = "M", Aliases = new List<string> { "B" },
            },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        Assert.Equal("K", graph.ResolveTaxiwayName("B (K)"));
        Assert.Equal("M", graph.ResolveTaxiwayName("B (M)"));
    }

    // --- GetNamedEdges -----------------------------------------------------
    //
    // Feeds SayIntentionsTaxiPathSnapper.Snap, which needs flat (name,
    // endpoint-coordinate) tuples for real airport pavement. Node-id
    // assignment is deterministic and start-before-end per path (see
    // TaxiGraph.Build / ResolveNode), so for these fixtures -- no two
    // endpoints within the 1.5 m merge threshold of each other except where
    // a chain deliberately shares a coordinate -- the expected node ids,
    // and therefore the expected (From ->To) direction and overall order,
    // can be derived by reasoning about Build's own registration order.

    [Fact]
    public void GetNamedEdges_returns_each_named_edge_once_with_endpoint_coordinates_and_excludes_unnamed()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 37.000, StartLon = -122.000, EndLat = 37.001, EndLon = -122.000, Name = "A" },
            new TaxiPath { StartLat = 37.000, StartLon = -121.900, EndLat = 37.001, EndLon = -121.900, Name = "B" },
            // Unnamed segment -- must never reach the output as an empty leg name.
            new TaxiPath { StartLat = 37.002, StartLon = -121.800, EndLat = 37.003, EndLon = -121.800, Name = "" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var edges = graph.GetNamedEdges();

        var expected = new List<(string Name, double FromLat, double FromLon, double ToLat, double ToLon)>
        {
            ("A", 37.000, -122.000, 37.001, -122.000),
            ("B", 37.000, -121.900, 37.001, -121.900),
        };
        Assert.Equal(expected, edges);
    }

    [Fact]
    public void GetNamedEdges_returns_each_segment_of_a_multi_segment_taxiway_separately()
    {
        // Two consecutive straight segments of the same taxiway "A", sharing the
        // middle coordinate -- must come back as two separate edges, not merged
        // into one taxiway-level entry (the snapper matches per straight segment).
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 37.000, StartLon = -122.000, EndLat = 37.001, EndLon = -122.000, Name = "A" },
            new TaxiPath { StartLat = 37.001, StartLon = -122.000, EndLat = 37.002, EndLon = -122.000, Name = "A" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var edges = graph.GetNamedEdges();

        var expected = new List<(string Name, double FromLat, double FromLon, double ToLat, double ToLon)>
        {
            ("A", 37.000, -122.000, 37.001, -122.000),
            ("A", 37.001, -122.000, 37.002, -122.000),
        };
        Assert.Equal(expected, edges);
    }

    [Fact]
    public void GetNamedEdges_skips_an_edge_whose_node_id_is_missing_from_Nodes()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 37.000, StartLon = -122.000, EndLat = 37.001, EndLon = -122.000, Name = "A" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        // Defensive case: Build() never leaves Adjacency pointing at a node id that
        // is missing from Nodes, but GetNamedEdges must not synthesize a (0,0)
        // endpoint if that ever happens -- a (0,0) edge would sit ~5000 km from any
        // real airport and could still win a nearest-edge comparison for an
        // outlier point.
        graph.Nodes.Clear();

        var edges = graph.GetNamedEdges();

        Assert.Empty(edges);
    }

    // GetNamedEdges' order must be intrinsic to the edges themselves, never to the order
    // Build() happened to process its `paths` list in. Node ids are assigned by a monotonic
    // counter in path-processing order, so a node-id-keyed sort (the original
    // implementation) reorders the SAME physical airport whenever the input path list
    // arrives in a different order -- e.g. a navdata re-import that changes taxi_path_id
    // assignment. That matters because the consumer (SayIntentionsTaxiPathSnapper) resolves
    // nearest-edge ties with a strict "<", so whichever edge is first decides which taxiway
    // a blind pilot is told about at an exact tie (two taxiways meeting at a junction can
    // legitimately be equidistant from a point).
    //
    // Fixture: two hub junctions, far enough apart that nothing merges across them, each
    // with three named spokes -- six distinct names total, so the expected order
    // (alphabetical by name) is unambiguous. Every spoke's Start is its hub, so under the
    // OLD node-id key the hub is always node 1 (created first by whichever path is
    // processed first) and the sort tie-breaks on ToNodeId -- i.e. on creation order, which
    // depends entirely on which order the paths were supplied in. Feeding the reversed path
    // list creates the SECOND hub first instead, giving completely different node ids and
    // therefore (under the old key) a completely different -- in fact exactly reversed --
    // output order (A,B,C,D,E,F vs F,E,D,C,B,A). Under the new name+coordinate key every
    // edge's FROM side is its hub, so the sort is decided by TaxiwayName alone, and both
    // orderings must produce the identical alphabetical sequence.
    [Fact]
    public void GetNamedEdges_order_is_independent_of_the_order_paths_were_supplied_to_Build()
    {
        TaxiPath Spoke(double hubLat, double hubLon, double endLat, double endLon, string name) =>
            new TaxiPath { StartLat = hubLat, StartLon = hubLon, EndLat = endLat, EndLon = endLon, Name = name };

        var a = Spoke(37.000, -122.000, 37.001, -122.000, "A");
        var b = Spoke(37.000, -122.000, 37.000, -121.999, "B");
        var c = Spoke(37.000, -122.000, 36.999, -122.000, "C");
        var d = Spoke(38.000, -123.000, 38.001, -123.000, "D");
        var e = Spoke(38.000, -123.000, 38.000, -122.999, "E");
        var f = Spoke(38.000, -123.000, 37.999, -123.000, "F");

        var graphForward = TaxiGraph.Build(
            new List<TaxiPath> { a, b, c, d, e, f },
            new List<ParkingSpot>(), new List<StartPosition>());

        // Same six physical edges, fully reversed input order -- builds the second hub
        // first, assigning every node a different id than the forward build.
        var graphReversed = TaxiGraph.Build(
            new List<TaxiPath> { f, e, d, c, b, a },
            new List<ParkingSpot>(), new List<StartPosition>());

        var edgesForward = graphForward.GetNamedEdges().ToList();
        var edgesReversed = graphReversed.GetNamedEdges().ToList();

        Assert.Equal(edgesForward, edgesReversed);

        // Pin the actual order too (not just that the two builds agree with each other):
        // alphabetical by taxiway name, independent of hub or processing order.
        var expectedNames = new[] { "A", "B", "C", "D", "E", "F" };
        Assert.Equal(expectedNames, edgesForward.Select(edge => edge.Name).ToArray());
    }

    // --- Canonical taxiway spelling -------------------------------------------------
    //
    // CYVR navdata genuinely contains both "D" and "d" as taxi_path.name values. The two
    // accessors then disagreed: GetAllTaxiwayNames() dedupes into a HashSet with
    // StringComparer.OrdinalIgnoreCase, so the form's list held ONE of them, while
    // GetNamedEdges() returned the raw name and the SayIntentions snapper saw BOTH. A live
    // 2026-08-19 import applied "d" and "D" as separate legs and the form could seat only
    // one of them (skipped=[d]).
    //
    // The canonical spelling is the ordinally smallest, which is deterministic regardless
    // of input order — the same property GetNamedEdges' own sort key requires — and picks
    // the conventional uppercase form, since 'D' < 'd' ordinally.

    private static List<TaxiPath> CyvrCaseVariantPaths() => new()
    {
        new TaxiPath { Name = "D", StartLat = 0, StartLon = 0.000, EndLat = 0, EndLon = 0.002 },
        new TaxiPath { Name = "d", StartLat = 0, StartLon = 0.002, EndLat = 0, EndLon = 0.004 },
    };

    [Fact]
    public void BuildCanonicalTaxiwayNames_maps_every_spelling_to_the_ordinally_smallest()
    {
        var map = TaxiGraph.BuildCanonicalTaxiwayNames(CyvrCaseVariantPaths());

        Assert.Equal("D", map["D"]);
        Assert.Equal("D", map["d"]);
    }

    [Fact]
    public void BuildCanonicalTaxiwayNames_does_not_depend_on_input_order()
    {
        var forward = CyvrCaseVariantPaths();
        var reversed = CyvrCaseVariantPaths();
        reversed.Reverse();

        Assert.Equal(
            TaxiGraph.BuildCanonicalTaxiwayNames(forward)["d"],
            TaxiGraph.BuildCanonicalTaxiwayNames(reversed)["d"]);
    }

    [Fact]
    public void BuildCanonicalTaxiwayNames_ignores_blank_and_whitespace_names()
    {
        var map = TaxiGraph.BuildCanonicalTaxiwayNames(new List<TaxiPath>
        {
            new TaxiPath { Name = "", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = 0.002 },
            new TaxiPath { Name = "   ", StartLat = 0, StartLon = 0.002, EndLat = 0, EndLon = 0.004 },
        });

        Assert.Empty(map);
    }

    [Fact]
    public void A_taxiway_spelled_two_ways_reaches_the_graph_as_one_name()
    {
        var graph = TaxiGraph.Build(
            CyvrCaseVariantPaths(), new List<ParkingSpot>(), new List<StartPosition>());

        Assert.Equal(new[] { "D" }, graph.GetAllTaxiwayNames());
    }

    [Fact]
    public void Two_case_variant_paths_between_the_same_nodes_are_one_edge()
    {
        // Same pavement under two spellings. Before canonicalisation AddEdge's ordinal ==
        // dedup treated them as different taxiways and kept both.
        var graph = TaxiGraph.Build(
            new List<TaxiPath>
            {
                new TaxiPath { Name = "D", StartLat = 0, StartLon = 0.000, EndLat = 0, EndLon = 0.002 },
                new TaxiPath { Name = "d", StartLat = 0, StartLon = 0.000, EndLat = 0, EndLon = 0.002 },
            },
            new List<ParkingSpot>(), new List<StartPosition>());

        Assert.Equal(new[] { "D" }, graph.GetAllTaxiwayNames());
        Assert.All(graph.Adjacency.Values, edges => Assert.Single(edges));
    }

    [Fact]
    public void The_two_name_accessors_agree_on_the_same_graph()
    {
        // This is the actual defect: the SayIntentions import resolves its clearance
        // against GetAllTaxiwayNames() and snaps its geometry against GetNamedEdges(),
        // so a name only one of them reports is a leg the form cannot seat.
        //
        // Scoped to a graph with NO aliases, which this fixture is. GetAllTaxiwayNames also
        // returns alias DISPLAY labels ("HAWKER (D)") that GetNamedEdges never emits, so the
        // two accessors are not required to agree in general — only about the spelling of
        // the real taxiway names they both carry, which is what this pins.
        var graph = TaxiGraph.Build(
            CyvrCaseVariantPaths(), new List<ParkingSpot>(), new List<StartPosition>());

        var fromEdges = graph.GetNamedEdges().Select(e => e.Name).Distinct().ToList();

        Assert.Equal(graph.GetAllTaxiwayNames(), fromEdges);
    }
}
