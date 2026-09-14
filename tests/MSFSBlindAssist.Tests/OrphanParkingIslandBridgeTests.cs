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

    /// <summary>A navdata stand lead-in: connector ("N") to stand ("P").</summary>
    private static TaxiPath LeadIn(double connLatN, double connLonE, double standLatN, double standLonE) => new()
    {
        Name = "", Type = "P", Width = 60.0, StartType = "N", EndType = "P",
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

    // ---------------------------------------------------------------- network end and runways

    /// <summary>Runway 09/27 on the equator from (0,0) to (0,1000 m E).</summary>
    private static List<StartPosition> Starts09And27() => new()
    {
        new() { RunwayName = "09", Type = "R", Heading = 90, Latitude = 0, Longitude = 0 },
        new() { RunwayName = "27", Type = "R", Heading = 270, Latitude = 0, Longitude = 1000 * M },
    };

    private static List<Runway> Runway09(double widthFt) => new()
    {
        new() { RunwayID = "09", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = 1000 * M, Width = widthFt },
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
    public void A_network_node_on_runway_pavement_is_never_the_network_end()
    {
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
}
