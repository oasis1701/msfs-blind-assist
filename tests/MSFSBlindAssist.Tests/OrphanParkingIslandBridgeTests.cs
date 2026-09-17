// Characterization tests for TaxiGraph.Build's orphan stand-stub bridge.
//
// Regression pinned: OMDB gate B 18R, issue #228. The fs2024 navdata models the stand and its
// connector as one "P" lead-in row whose open end stops 12 m short of taxiway U. Build merges
// endpoints only within 1.5 m, so the stub was its own connected component, and LoadRoute's
// destination-component start-node filter found no start node.
//
// The rule these tests pin:
//   - Only a component whose EVERY edge is a stand lead-in ("P") and that contains a stand
//     (a navdata "P" endpoint) is bridged. Islands that carry a taxiway are left alone — the
//     GCLP S5 shape the start-node filter exists to reject.
//   - The island end of a bridge is never a stand or a hold-short node.
//   - One bridge per island, at the closest valid pair within 50 m, typed
//     TaxiGraph.StandBridgePathType in both directions.
//   - The network end is never a stand, a hold-short node, a node on runway pavement or a node on
//     another stand's lead-in chain, and a pair whose straight line touches a runway is skipped.
//
// Fixtures are shaped like navdata: lead-ins are Type "P", StartType "N", EndType "P".
// Geometry sits on the equator, where TaxiGraph's 111,132 m/deg makes metres = degrees x
// 111132 in both axes, so every distance quoted below is exact.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class OrphanParkingIslandBridgeTests
{
    private const double M = 1.0 / 111132.0;   // degrees per metre on the equator

    private static TaxiPath Taxiway(string name, double latN, double lonE, double lat2N, double lon2E,
                                    string type = "T", string startType = "N", string endType = "N",
                                    double widthFt = 75.0) => new()
    {
        Name = name, Type = type, Width = widthFt, StartType = startType, EndType = endType,
        StartLat = latN * M, StartLon = lonE * M, EndLat = lat2N * M, EndLon = lon2E * M,
    };

    /// <summary>A navdata stand lead-in: connector ("N") to stand ("P"). Real "P" rows are not
    /// always unnamed (measured: 1,810 of 319,004 in the real fs2024 database carry a name) --
    /// <paramref name="name"/> defaults to "" to match the common case, but a caller can pass a
    /// real taxiway name to reproduce the collision shape (Important 1, PR #238 review).</summary>
    private static TaxiPath LeadIn(double connLatN, double connLonE, double standLatN, double standLonE,
                                   string name = "") => new()
    {
        Name = name, Type = "P", Width = 60.0, StartType = "N", EndType = "P",
        StartLat = connLatN * M, StartLon = connLonE * M, EndLat = standLatN * M, EndLon = standLonE * M,
    };

    /// <summary>Taxiway "U" along the equator: nodes at 0, 60, 120, 180 and 240 m east.</summary>
    private static List<TaxiPath> MainTaxiwayU() =>
        Enumerable.Range(0, 4).Select(i => Taxiway("U", 0, i * 60, 0, (i + 1) * 60)).ToList();

    private static TaxiGraph BuildGraph(IEnumerable<TaxiPath> paths, List<ParkingSpot>? parking = null,
                                        List<StartPosition>? starts = null, List<Runway>? runways = null) =>
        TaxiGraph.Build(paths.ToList(), parking ?? new List<ParkingSpot>(),
                        starts ?? new List<StartPosition>(), runways);

    private static TaxiNode NodeAt(TaxiGraph g, double latN, double lonE) =>
        g.Nodes.Values.Single(n => Math.Abs(n.Latitude - latN * M) < 1e-9 && Math.Abs(n.Longitude - lonE * M) < 1e-9);

    private static List<TaxiEdge> BridgeEdges(TaxiGraph g) =>
        g.Adjacency.Values.SelectMany(es => es)
         .Where(e => e.PathType == TaxiGraph.StandBridgePathType).ToList();

    [Fact]
    public void A_stub_12_m_from_the_taxiway_is_bridged_from_its_connector()
    {
        // The OMDB B 18R shape: connector 12 m north of U120, stand 39 m further out (51 m).
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));

        var connector = NodeAt(g, 12, 120);
        var u120 = NodeAt(g, 0, 120);
        var stand = NodeAt(g, 51, 120);
        Assert.Equal(u120.ComponentId, stand.ComponentId);

        var bridges = BridgeEdges(g);
        Assert.Equal(2, bridges.Count);   // one fabricated edge, both directions
        Assert.Contains(bridges, e => e.FromNodeId == connector.NodeId && e.ToNodeId == u120.NodeId);
        Assert.Contains(bridges, e => e.FromNodeId == u120.NodeId && e.ToNodeId == connector.NodeId);
        Assert.All(bridges, e => Assert.InRange(e.DistanceMeters, 11.9, 12.1));
        Assert.All(bridges, e => Assert.Equal("", e.TaxiwayName));
    }

    [Fact]
    public void The_island_end_is_the_connector_even_when_the_stand_is_nearer()
    {
        // Stand 20 m from U120 (nearer), connector 45 m out. The stand must never be the end.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(45, 120, 20, 120)));

        var connector = NodeAt(g, 45, 120);
        var stand = NodeAt(g, 20, 120);
        var bridges = BridgeEdges(g);

        Assert.Equal(2, bridges.Count);
        Assert.DoesNotContain(bridges, e => e.FromNodeId == stand.NodeId || e.ToNodeId == stand.NodeId);
        Assert.Contains(bridges, e => e.FromNodeId == connector.NodeId && e.ToNodeId == NodeAt(g, 0, 120).NodeId);
        Assert.All(bridges, e => Assert.InRange(e.DistanceMeters, 44.9, 45.1));
    }

    [Fact]
    public void No_bridge_when_only_the_stand_is_within_range()
    {
        // Stand 20 m from U120, connector 60 m out: nothing valid within 50 m.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(60, 120, 20, 120)));

        Assert.Empty(BridgeEdges(g));
        Assert.NotEqual(NodeAt(g, 0, 120).ComponentId, NodeAt(g, 20, 120).ComponentId);
    }

    [Fact]
    public void One_bridge_per_island_even_with_two_network_nodes_in_range()
    {
        // Connector at 10 N, 145 E: 26.9 m to U120 and 36.4 m to U180 — both in range.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(10, 145, 49, 145)));

        var bridges = BridgeEdges(g);
        Assert.Equal(2, bridges.Count);
        var connector = NodeAt(g, 10, 145);
        Assert.Contains(bridges, e => e.FromNodeId == connector.NodeId && e.ToNodeId == NodeAt(g, 0, 120).NodeId);
    }

    [Fact]
    public void An_island_that_carries_a_taxiway_is_never_bridged()
    {
        // Taxiway S5 starts 12 m from U120 and ends at a stand lead-in. Not a stand stub.
        var paths = MainTaxiwayU();
        paths.Add(Taxiway("S5", 12, 120, 40, 120));
        paths.Add(LeadIn(40, 120, 79, 120));
        var g = BuildGraph(paths);

        Assert.Empty(BridgeEdges(g));
        Assert.NotEqual(NodeAt(g, 0, 120).ComponentId, NodeAt(g, 79, 120).ComponentId);
    }

    [Fact]
    public void A_taxiway_island_labelled_parking_by_proximity_is_left_disconnected()
    {
        // The parking pass stamps TaxiNodeType.Parking on the node nearest a spot, in any
        // component. That label must not make a taxiway island look like a stand stub.
        var paths = MainTaxiwayU();
        paths.Add(Taxiway("S5", 12, 120, 60, 120, type: "PT"));
        var parking = new List<ParkingSpot>
        {
            new() { Name = "B", Number = 20, Latitude = 65 * M, Longitude = 120 * M },
        };
        var g = BuildGraph(paths, parking);

        var s5 = g.Nodes.Values.Where(n => n.TaxiwayNames.Contains("S5")).ToList();
        Assert.Contains(s5, n => n.Type == TaxiNodeType.Parking);   // the label did land
        Assert.Empty(BridgeEdges(g));
        Assert.All(s5, n => Assert.NotEqual(NodeAt(g, 0, 120).ComponentId, n.ComponentId));
    }

    [Fact]
    public void A_stub_already_on_the_network_gains_no_bridge()
    {
        // The lead-in starts exactly on U120, so Build's 1.5 m merge joins them already.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(0, 120, 39, 120)));

        Assert.Empty(BridgeEdges(g));
        Assert.Equal(NodeAt(g, 0, 120).ComponentId, NodeAt(g, 39, 120).ComponentId);
    }

    [Fact]
    public void An_east_west_gap_is_measured_in_metres_at_arctic_latitudes()
    {
        // ENSB latitude: a uniform-degree search cell would span only ~10 m east-west here,
        // missing a candidate 30 m east. The stub runs further east from 30 m past the last node.
        const double lat = 78.246;
        double lonPerM = 1.0 / (111132.0 * Math.Cos(lat * Math.PI / 180.0));
        var paths = Enumerable.Range(0, 4).Select(i => new TaxiPath
        {
            Name = "U", Type = "T", Width = 75.0, StartType = "N", EndType = "N",
            StartLat = lat, StartLon = (i * 60.0) * lonPerM, EndLat = lat, EndLon = ((i + 1) * 60.0) * lonPerM,
        }).ToList();
        paths.Add(new TaxiPath
        {
            Name = "", Type = "P", Width = 60.0, StartType = "N", EndType = "P",
            StartLat = lat, StartLon = 270.0 * lonPerM, EndLat = lat, EndLon = 309.0 * lonPerM,
        });

        var g = BuildGraph(paths);

        var stand = g.Nodes.Values.Single(n => Math.Abs(n.Longitude - 309.0 * lonPerM) < 1e-9);
        var taxiway = g.Nodes.Values.First(n => n.TaxiwayNames.Contains("U"));
        Assert.Equal(taxiway.ComponentId, stand.ComponentId);
        Assert.Equal(2, BridgeEdges(g).Count);
    }

    [Fact]
    public void The_main_component_id_is_the_largest_component()
    {
        // Stub 200 m out stays unbridged: MainComponentId is U's component, not the stub's.
        var far = BuildGraph(MainTaxiwayU().Append(LeadIn(200, 120, 239, 120)));
        Assert.Equal(NodeAt(far, 0, 120).ComponentId, far.MainComponentId);
        Assert.NotEqual(NodeAt(far, 239, 120).ComponentId, far.MainComponentId);

        // After a bridge the renumbered component that holds both is the main one.
        var near = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));
        Assert.Equal(NodeAt(near, 51, 120).ComponentId, near.MainComponentId);
    }

    [Fact]
    public void A_tied_largest_component_is_broken_by_the_smallest_lat_lon_pair_not_row_order()
    {
        // Two same-size (2-node), never-bridged taxiway islands -- neither has a stand ("P")
        // endpoint, so IsStandStubIsland is false for both and they stay genuinely disconnected,
        // tied at 2 nodes each. "A" sits at the smaller (lat, lon); "B" at the larger. Under the
        // retired row-order tie-break, whichever island's rows were listed FIRST got the lower
        // component id (BFS assigns ids by node-insertion order, which follows row order) and so
        // won the tie regardless of geometry -- the new rule must pick A in BOTH orderings.
        var listedBFirst = BuildGraph(new List<TaxiPath>
        {
            Taxiway("B", 100, 100, 100, 110),
            Taxiway("A", 0, 0, 0, 10),
        });
        var listedAFirst = BuildGraph(new List<TaxiPath>
        {
            Taxiway("A", 0, 0, 0, 10),
            Taxiway("B", 100, 100, 100, 110),
        });

        foreach (var g in new[] { listedBFirst, listedAFirst })
        {
            var aNode = NodeAt(g, 0, 0);
            var bNode = NodeAt(g, 100, 100);
            Assert.NotEqual(aNode.ComponentId, bNode.ComponentId);                     // sanity: two components
            Assert.Equal(2, g.Nodes.Values.Count(n => n.ComponentId == aNode.ComponentId));
            Assert.Equal(2, g.Nodes.Values.Count(n => n.ComponentId == bNode.ComponentId));  // tied size

            Assert.Equal(aNode.ComponentId, g.MainComponentId);
        }
    }

    // ---------------------------------------------------------------- network end and runways

    /// <summary>Runway 09/27 on the equator from (0, 10 m E) to (0, 1010 m E) — never literally
    /// (0, 0), which RunwayShape.For (routed through since the RunwayPavement rewrite in Task 3)
    /// treats as an unset pavement end and silently falls back to the start-row half-width
    /// instead of the runway table's (see RunwayFixture's own "never (0, 0)" comment). At (0,0)
    /// this coincidentally left A_bridge_never_starts_on_runway_pavement passing (150 ft / 2 = the
    /// same 75 ft default the fallback uses) while breaking this class's other runway test, whose
    /// 60 ft width does not match the default. The 10 m offset only moves the runway itself; every
    /// taxiway/connector coordinate below is unchanged.</summary>
    private static List<StartPosition> Starts09And27() => new()
    {
        new() { RunwayName = "09", Type = "R", Heading = 90, Latitude = 0, Longitude = 10 * M },
        new() { RunwayName = "27", Type = "R", Heading = 270, Latitude = 0, Longitude = 1010 * M },
    };

    private static List<Runway> Runway09(double widthFt) => new()
    {
        new() { RunwayID = "09", StartLat = 0, StartLon = 10 * M, EndLat = 0, EndLon = 1010 * M, Width = widthFt },
    };

    [Fact]
    public void A_hold_short_node_is_never_the_network_end()
    {
        // A1 runs north from U120 to a hold line (HSND) at 30 N. The stub's connector at
        // (40 N, 125 E) is 11.2 m from that hold node but 40.3 m from U120: the bridge must
        // take U120.
        var paths = MainTaxiwayU();
        paths.Add(Taxiway("A1", 0, 120, 30, 120, endType: "HSND"));
        paths.Add(LeadIn(40, 125, 79, 125));
        var g = BuildGraph(paths);

        var bridges = BridgeEdges(g);
        var connector = NodeAt(g, 40, 125);
        Assert.Equal(2, bridges.Count);
        Assert.Contains(bridges, e => e.FromNodeId == connector.NodeId && e.ToNodeId == NodeAt(g, 0, 120).NodeId);
        Assert.DoesNotContain(bridges, e => e.ToNodeId == NodeAt(g, 30, 120).NodeId);
    }

    [Fact]
    public void A_bridge_never_starts_on_runway_pavement()
    {
        // Pinned by ChooseBridgePair's segment-touches-pavement check, not by
        // IsEligibleMainEndpoint's pavement exclusion (deleting that exclusion still leaves this passing).
        // E1 ends on the runway centreline at 500 E. The stub's connector 35 m south of the
        // centreline is within 50 m of only that on-pavement node, so nothing is bridged.
        var paths = new List<TaxiPath>
        {
            Taxiway("E1", 60, 500, 30, 500),
            Taxiway("E1", 30, 500, 0, 500),
            LeadIn(-35, 500, -74, 500),
        };
        var g = BuildGraph(paths, starts: Starts09And27(), runways: Runway09(150));

        Assert.Single(g.RunwayCenterlines);
        Assert.Empty(BridgeEdges(g));
    }

    [Fact]
    public void A_bridge_line_that_would_cross_a_runway_uses_the_next_valid_pair()
    {
        // Runway pavement half-width 9.14 m (60 ft). The main network runs south of the runway
        // (S, lat -12), crosses it on X at 600 E, and returns north on N to 340 E. The stub's
        // connector at (12 N, 300 E) is 24 m from S300, but that line crosses the runway; the
        // next pair, N340 at 40 m along lat 12 N, stays clear of the pavement.
        var paths = new List<TaxiPath>
        {
            Taxiway("S", -12, 100, -12, 300),
            Taxiway("S", -12, 300, -12, 600),
            Taxiway("X", -12, 600, 12, 600),
            Taxiway("N", 12, 600, 12, 340),
            LeadIn(12, 300, 51, 300),
        };
        var g = BuildGraph(paths, starts: Starts09And27(), runways: Runway09(60));

        var bridges = BridgeEdges(g);
        var connector = NodeAt(g, 12, 300);
        Assert.Equal(2, bridges.Count);
        Assert.Contains(bridges, e => e.FromNodeId == connector.NodeId && e.ToNodeId == NodeAt(g, 12, 340).NodeId);
        Assert.All(bridges, e => Assert.InRange(e.DistanceMeters, 39.9, 40.1));
    }

    [Fact]
    public void The_bridge_never_lands_on_a_neighbouring_stands_lead_in_bend()
    {
        // Navdata draws a neighbour's multi-segment lead-in as an unnamed PT row from U60 to a
        // bend at 20 N, then a P row to its stand at 59 N. The orphan connector at (30 N, 85 E) is
        // 26.9 m from that bend but 39.05 m from U60: the bend is on the neighbour's lead-in chain
        // and must never be the network end.
        var paths = MainTaxiwayU();
        paths.Add(Taxiway("", 0, 60, 20, 60, type: "PT"));
        paths.Add(LeadIn(20, 60, 59, 60));
        paths.Add(LeadIn(30, 85, 69, 85));
        var g = BuildGraph(paths);

        var bridges = BridgeEdges(g);
        var connector = NodeAt(g, 30, 85);
        Assert.Equal(2, bridges.Count);
        Assert.Contains(bridges, e => e.FromNodeId == connector.NodeId && e.ToNodeId == NodeAt(g, 0, 60).NodeId);
        Assert.All(bridges, e => Assert.InRange(e.DistanceMeters, 38.9, 39.2));
    }

    [Fact]
    public void The_lead_in_chain_stops_at_the_cap_on_a_long_unbranched_path()
    {
        // A tiny airport whose only taxiway L is one unbranched path from a stand at (0,0) out to a
        // dead end at 205 E. The chain rule marks nodes within 100 m of the stand (40 E and 95 E)
        // and must stop there, so the dead end at 205 E still takes the orphan stub 20 m north.
        // This test already passes before the change; it guards the cap against a future rule
        // that would exclude the whole path.
        var paths = new List<TaxiPath>
        {
            LeadIn(0, 40, 0, 0),
            Taxiway("L", 0, 40, 0, 95),
            Taxiway("L", 0, 95, 0, 150),
            Taxiway("L", 0, 150, 0, 205),
            LeadIn(20, 205, 59, 205),
        };
        var g = BuildGraph(paths);

        var bridges = BridgeEdges(g);
        Assert.Equal(2, bridges.Count);
        Assert.Contains(bridges, e => e.FromNodeId == NodeAt(g, 20, 205).NodeId && e.ToNodeId == NodeAt(g, 0, 205).NodeId);
    }

    // ---------------------------------------------------------------- Task 6 Defect A: a bridge-only
    // stand stub must not become a route start (it must stay reachable as a destination)

    [Fact]
    public void Bridged_island_members_are_marked_bridge_only_stand_stubs()
    {
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));

        var connector = NodeAt(g, 12, 120);
        var stand = NodeAt(g, 51, 120);
        var u120 = NodeAt(g, 0, 120);

        // Both island members qualify -- the stand itself has no other way out either.
        Assert.True(g.IsBridgeOnlyStandStub(connector.NodeId));
        Assert.True(g.IsBridgeOnlyStandStub(stand.NodeId));
        // The bridge's network end keeps its real taxiway edges and must never qualify.
        Assert.False(g.IsBridgeOnlyStandStub(u120.NodeId));
    }

    [Fact]
    public void An_unbridged_islands_nodes_are_never_marked_bridge_only_stand_stubs()
    {
        // Stand 20 m from U120, connector 60 m out: nothing valid within 50 m, so nothing is
        // bridged at all. These nodes stay genuinely disconnected -- the pre-existing
        // requiredComponentId filter already excludes them; they must not also get this label.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(60, 120, 20, 120)));

        var connector = NodeAt(g, 60, 120);
        var stand = NodeAt(g, 20, 120);
        Assert.False(g.IsBridgeOnlyStandStub(connector.NodeId));
        Assert.False(g.IsBridgeOnlyStandStub(stand.NodeId));
    }

    [Fact]
    public void FindNearestNode_excludes_a_bridge_only_stand_stub_from_route_start_candidates_on_request()
    {
        // The OMDB B 18R shape: the stub's connector ends up objectively nearer to a point on
        // the taxiway than any real network vertex -- "real taxiway vertices exist only at
        // junctions and bends and can be 40 m away" (Task 6 Defect A).
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));
        var connector = NodeAt(g, 12, 120);
        var u120 = NodeAt(g, 0, 120);

        // 10 N, 120 E: 2 m from the connector, 10 m from u120.
        double lat = 10, lon = 120;

        // Unfiltered (the default): the stub is still the nearest node -- e.g. a destination
        // lookup for the stand itself must keep finding it.
        var unfiltered = g.FindNearestNode(lat * M, lon * M);
        Assert.Equal(connector.NodeId, unfiltered!.NodeId);

        // A route-start picker asks to exclude it and lands on the real network node instead.
        var filtered = g.FindNearestNode(lat * M, lon * M, excludeBridgeOnlyStandStubs: true);
        Assert.Equal(u120.NodeId, filtered!.NodeId);
    }

    [Fact]
    public void FindNearestNodeInDirection_excludes_a_bridge_only_stand_stub_from_route_start_candidates_on_request()
    {
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));
        var connector = NodeAt(g, 12, 120);
        var u120 = NodeAt(g, 0, 120);

        // Aircraft at 6.5 N, 120 E: 5.5 m from the connector (north, ahead, beyond the "node right
        // under us" 5 m exclusion) and 6.5 m from u120 (south, behind) -- DELIBERATELY UNEQUAL (PR
        // #238 review, Important 2). This test used to place the aircraft at an exact 6/6 m tie: at
        // an exact tie the fallback's strict "dist < bestDist" (inside FindNearestNode, called from
        // FindNearestNodeInDirection's "nothing ahead" branch) returns u120 regardless of whether
        // excludeBridgeOnlyStandStubs is actually threaded into that call, so reverting the
        // threading at TaxiGraph.cs's "var fallback = FindNearestNode(...)" line left every test in
        // this file green -- the assertion was passing on coordinate symmetry, not on the filter
        // (mutation-verified: reverting that one call's argument, this test alone fails; restoring
        // it, passes). With the connector unambiguously nearer than u120, an unfiltered fallback
        // would return the connector instead of u120 and the second assertion below would catch it.
        // The connector still wins the "ahead" search outright when unfiltered (score 5.5 vs the
        // stand's 44.5, both dead ahead on bearing 0); u120 stays behind (bearing 180) either way.
        double lat = 6.5, lon = 120;

        var unfiltered = g.FindNearestNodeInDirection(lat * M, lon * M, headingDeg: 0);
        Assert.Equal(connector.NodeId, unfiltered!.NodeId);

        // Excluding the stub removes it from the "ahead" candidate set too (not merely the
        // fallback) -- nothing else is ahead, so this must fall through to the overall-nearest
        // fallback and land on u120, not return null.
        var filtered = g.FindNearestNodeInDirection(lat * M, lon * M, headingDeg: 0, excludeBridgeOnlyStandStubs: true);
        Assert.Equal(u120.NodeId, filtered!.NodeId);
    }

    // ---------------------------------------------------------------- Task 6 Defect B: a fabricated
    // bridge must never host a projected holding point

    [Fact]
    public void InsertHoldingPointNodeOnEdge_never_projects_onto_a_fabricated_bridge()
    {
        // The bridge runs (0,120)->(12,120), pure latitude, longitude fixed at 120 E. A point at
        // (6, 120) projects to perpendicular distance 0 at t=0.5 on the bridge -- and, because its
        // longitude matches u120's exactly, projects to t=0/t=1 (excluded by the endpoint clamp
        // check) on the two flanking real taxiway-U segments, and to a clamped-negative t (also
        // excluded) on the stand lead-in. The bridge is the ONLY edge that would otherwise qualify,
        // and neither of its endpoints is Parking-typed (Defect B: "a bridge's network end is
        // guaranteed NOT to be a stand, so neither endpoint carries that flag").
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));

        var result = g.InsertHoldingPointNodeOnEdge(6 * M, 120 * M, maxPerpMeters: 30);

        Assert.Null(result);
    }

    [Fact]
    public void InsertHoldingPointNodeOnEdge_still_projects_onto_a_real_taxiway_near_a_bridge()
    {
        // Same bridge as above, but the probed point (30 N, 90 E) sits over the middle of the real
        // U60-U120 taxiway segment instead -- confirms Defect B's fix (skipping bridges) does not
        // also break the ordinary, non-bridge case at an airport that happens to have one.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120)));

        var result = g.InsertHoldingPointNodeOnEdge(0, 90 * M, maxPerpMeters: 5);

        Assert.NotNull(result);
        Assert.Contains("U", result!.TaxiwayNames);
    }

    // ---------------------------------------------------------------- Minor 5: SplitEdgeAt must mark
    // a node it mints INSIDE an already-bridged stand-stub island

    [Fact]
    public void SplitEdgeAt_marks_a_new_node_on_an_interior_bridged_lead_in_edge_as_a_bridge_only_stand_stub()
    {
        // A 3-node bridged island: connector (12,120) -- mid (30,120) -- stand (51,120), BOTH legs
        // typed "P" so IsStandStubIsland still holds (every edge in the island is a lead-in). The
        // parking pass stamps TaxiNodeType.Parking on only the single node nearest a spot, so an
        // interior lead-in node like "mid" is never Parking-typed -- InsertHoldingPointNodeOnEdge's
        // "a.Type == Parking || b.Type == Parking" guard cannot see it. It also isn't the fabricated
        // bridge itself (PathType stays "P", not StandBridgePathType), so Defect B's IsStandBridge
        // skip doesn't apply either. The connector-mid leg is the live candidate that guard was
        // missing (PR #238 review, Minor 5).
        var paths = MainTaxiwayU();
        paths.Add(new TaxiPath
        {
            Name = "", Type = "P", Width = 60.0, StartType = "N", EndType = "N",
            StartLat = 12 * M, StartLon = 120 * M, EndLat = 30 * M, EndLon = 120 * M,
        });
        paths.Add(new TaxiPath
        {
            Name = "", Type = "P", Width = 60.0, StartType = "N", EndType = "P",
            StartLat = 30 * M, StartLon = 120 * M, EndLat = 51 * M, EndLon = 120 * M,
        });
        var g = BuildGraph(paths);

        var connector = NodeAt(g, 12, 120);
        var mid = NodeAt(g, 30, 120);
        var stand = NodeAt(g, 51, 120);
        Assert.Equal(2, BridgeEdges(g).Count);   // sanity: the island did bridge, from the connector
        Assert.True(g.IsBridgeOnlyStandStub(connector.NodeId));
        Assert.True(g.IsBridgeOnlyStandStub(mid.NodeId));
        Assert.True(g.IsBridgeOnlyStandStub(stand.NodeId));

        // Probe (20 N, 120 E): t = (20-12)/(30-12) = 0.444 on the connector-mid leg (perpendicular
        // distance exactly 0, same longitude), outside the bridge's own 0-12 span and the
        // mid-stand leg's 30-51 span, so this is the only edge that can qualify.
        var inserted = g.InsertHoldingPointNodeOnEdge(20 * M, 120 * M, maxPerpMeters: 5);

        Assert.NotNull(inserted);
        Assert.True(g.IsBridgeOnlyStandStub(inserted!.NodeId));
    }

    // ---------------------------------------------------------------- Important 1: FindNearestNodeOnTaxiway
    // is a third unfiltered route-start picker (PR #238 review) -- a "P" lead-in row is NOT always
    // unnamed, so a bridge-only stand stub's node can register on a real taxiway name too

    [Fact]
    public void FindNearestNodeOnTaxiway_excludes_a_bridge_only_stand_stub_carrying_a_taxiway_name()
    {
        // Measured against the real fs2024 database: 1,810 of 319,004 "P" (stand lead-in) rows
        // carry a non-empty name (KNZY 503, KCLT 119, plus LEMG/LEMD/CYVR/EPWR) -- ResolveNode adds
        // a row's name to both endpoints unconditionally, regardless of PathType. So a bridge-only
        // stand stub's node CAN acquire a taxiway-name entry; this corrects the "every 'P' row
        // carries an EMPTY TaxiwayName... structurally never acquire a taxiway-name entry" premise
        // group-d-report.md used to leave this method unfiltered. Reuse taxiway "U"'s own name on
        // the lead-in, matching the live EPWR/KCLT shape where the stub's taxiway name collides with
        // a real one on the main network.
        var g = BuildGraph(MainTaxiwayU().Append(LeadIn(12, 120, 51, 120, name: "U")));
        var connector = NodeAt(g, 12, 120);
        var u120 = NodeAt(g, 0, 120);

        // 10 N, 120 E: 2 m from the connector, 10 m from u120. Unfiltered (the default): the stub
        // is still the nearest "U" node -- e.g. a destination lookup for the stand itself must keep
        // finding it.
        var unfiltered = g.FindNearestNodeOnTaxiway(10 * M, 120 * M, "U");
        Assert.Equal(connector.NodeId, unfiltered!.NodeId);

        // A route-start picker asks to exclude it and lands on the real network node instead.
        var filtered = g.FindNearestNodeOnTaxiway(10 * M, 120 * M, "U", excludeBridgeOnlyStandStubs: true);
        Assert.Equal(u120.NodeId, filtered!.NodeId);
    }
}
