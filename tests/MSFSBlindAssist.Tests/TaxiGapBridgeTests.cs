// Characterization tests for TaxiGraph.BridgeSameNamedGaps — the Build-time repair
// that joins two dead-end chains of ONE named taxiway across a small modelling gap.
//
// Regression pinned: EDDB rapid-exit M5 (2026-08-30). The scenery models M5 as two
// pieces with a 31 m hole: the runway-side chain dead-ends 47.6 m from the 24L
// centreline, while the outer piece — joining M4 and carrying the holding points at
// 120 m / 165 m — was unreachable except via runway pavement. The vacate walk
// therefore stopped the pilot at 47.6 m and guidance announced "Off the runway.
// Stop and hold position." short of the holding point, against the EDDB AIP's
// bolded "when vacating via M4/M5 always continue until you have crossed the
// holding point" instruction.
//
// Every gate is tested from BOTH sides: the M5 shape must bridge, and each
// near-miss shape (different name, wide gap, hairpin, nearby-connected loop,
// runway-crossing) must NOT — a wrong bridge silently changes safety-critical
// routing, which is worse than the gap it repairs.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiGapBridgeTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    // Runway 18 on the equator/prime meridian, same frame as RunwayVacateResolverTests:
    // lateral offset east of the axis is just longitude in metres.
    private static Runway Runway18() => new Runway
    {
        RunwayID = "18",
        StartLat = 0.03, StartLon = 0.0,
        EndLat = 0.0, EndLon = 0.0,
        Heading = 180.0,
        Length = 0.03 * M_PER_DEG / 0.3048,
        Width = 197.0,                        // EDDB 06R/24L — half-width 30.0 m
    };

    private static TaxiPath Path(double lat1, double lon1, double lat2, double lon2,
                                 string name, string type = "PT")
        => new TaxiPath
        {
            StartLat = lat1, StartLon = lon1,
            EndLat = lat2, EndLon = lon2,
            Name = name, Type = type,
            StartType = "N", EndType = "N",
            Width = 75.0,
        };

    private static double Lon(double metres) => metres * DEG_PER_M;
    private const double JunctionLat = 0.015;

    /// <summary>
    /// EDDB-M5-shaped fixture: an exit chain from the runway axis dead-ending at
    /// 48 m east, and a separate colinear same-named chain 61 m → 102 m east whose
    /// near end faces the gap. Gap ≈ 13 m along the axis + 13 m lateral ≈ 18 m.
    /// </summary>
    private static List<TaxiPath> M5Shape(string outerName = "M5", double gapExtraLonM = 0.0)
    {
        double l0 = JunctionLat;
        double l1 = JunctionLat - 30.0 * DEG_PER_M;
        double l2 = JunctionLat - 55.0 * DEG_PER_M;       // inner dead end
        double l3 = JunctionLat - 68.0 * DEG_PER_M;       // outer near end
        double l4 = JunctionLat - 95.0 * DEG_PER_M;
        return new List<TaxiPath>
        {
            Path(l0, Lon(0),  l1, Lon(20), "M5"),
            Path(l1, Lon(20), l2, Lon(48), "M5"),
            Path(l3, Lon(61 + gapExtraLonM), l4, Lon(102 + gapExtraLonM), outerName),
        };
    }

    private static TaxiGraph Build(List<TaxiPath> paths, bool withCenterline = false)
    {
        // Start rows are what RunwayCenterlines are paired from — supply them only
        // for the tests that exercise the never-bridge-over-a-runway gate.
        var starts = withCenterline
            ? new List<StartPosition>
              {
                  new StartPosition { RunwayName = "18", Latitude = 0.03, Longitude = 0.0, Heading = 180.0 },
                  new StartPosition { RunwayName = "36", Latitude = 0.0, Longitude = 0.0, Heading = 0.0 },
              }
            : new List<StartPosition>();
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), starts,
                               new List<Runway> { Runway18() });
    }

    private static int NodeAtLon(TaxiGraph g, double metresEast)
    {
        foreach (var n in g.Nodes.Values)
            if (Math.Abs(n.Longitude * M_PER_DEG - metresEast) < 1.0)
                return n.NodeId;
        throw new Xunit.Sdk.XunitException($"no node at {metresEast} m east");
    }

    [Fact]
    public void M5Shape_GapIsBridged_AndVacateWalkReachesTheHoldingPosition()
    {
        var g = Build(M5Shape());

        var bridge = Assert.Single(g.BridgedGaps);
        Assert.Equal("M5", bridge.TaxiwayName);
        Assert.InRange(bridge.GapMeters, 10.0, 25.0);

        // The repaired graph must let the vacate walk continue past the old dead
        // end (48 m — off the 30 m pavement but short of the 90 m holding
        // position) to the outer chain's 102 m node.
        int junction = NodeAtLon(g, 0);
        int innerEnd = NodeAtLon(g, 48);
        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, innerEnd, junction, Runway18(), 180.0,
            out _, out double endLateral);
        Assert.Equal(NodeAtLon(g, 102), dest);
        Assert.True(endLateral >= 90.0, $"still short of the holding position: {endLateral:F1} m");
    }

    [Fact]
    public void BridgeEdge_CarriesTheTaxiwayName_BothDirections()
    {
        var g = Build(M5Shape());
        int a = NodeAtLon(g, 48);
        int b = NodeAtLon(g, 61);
        var ab = g.Adjacency[a].Single(e => e.ToNodeId == b);
        var ba = g.Adjacency[b].Single(e => e.ToNodeId == a);
        Assert.Equal("M5", ab.TaxiwayName);
        Assert.Equal("M5", ba.TaxiwayName);
        Assert.Equal(ab.DistanceMeters, ba.DistanceMeters, 3);
    }

    [Fact]
    public void DifferentNames_AreNotBridged()
    {
        var g = Build(M5Shape(outerName: "M4"));
        Assert.Empty(g.BridgedGaps);
    }

    [Fact]
    public void GapBeyondCap_IsNotBridged()
    {
        // Push the outer chain 45 m further east: gap ≈ 58 m > the 40 m cap.
        var g = Build(M5Shape(gapExtraLonM: 45.0));
        Assert.Empty(g.BridgedGaps);
    }

    [Fact]
    public void HairpinEnds_FacingAway_AreNotBridged()
    {
        // Two same-named chains whose dead ends are 20 m apart but whose
        // directions of travel run PARALLEL (both eastbound) — the "bridge" would
        // be a sideways chord, not a continuation.
        double lA = JunctionLat;
        double lB = JunctionLat - 20.0 * DEG_PER_M;
        var g = Build(new List<TaxiPath>
        {
            Path(lA, Lon(100), lA, Lon(160), "H"),   // ends eastbound at 160 m
            Path(lB, Lon(160), lB, Lon(220), "H"),   // STARTS at 160 m, also eastbound
        });
        Assert.Empty(g.BridgedGaps);
    }

    [Fact]
    public void EndsAlreadyConnectedNearby_AreNotBridged()
    {
        // Two head-on colinear dead ends 20 m apart — every geometric gate passes —
        // but a ~280 m loop of real pavement already connects them. Bridging would
        // create a shortcut chord that CHANGES existing routes, so the
        // connected-within-walk gate must refuse it.
        double l = JunctionLat;
        double l2 = JunctionLat - 30.0 * DEG_PER_M;
        var g = Build(new List<TaxiPath>
        {
            Path(l, Lon(100), l, Lon(150), "U"),   // west chain, dead end at 150 facing east
            Path(l, Lon(170), l, Lon(220), "U"),   // east chain, dead end at 170 facing west
            Path(l, Lon(100), l2, Lon(100), "U"),  // loop: down 30 m
            Path(l2, Lon(100), l2, Lon(220), "U"), // across 120 m
            Path(l2, Lon(220), l, Lon(220), "U"),  // up 30 m
        });
        Assert.Empty(g.BridgedGaps);
    }

    [Fact]
    public void GapStraddlingARunway_IsNotBridged()
    {
        // Two colinear same-named stubs facing each other ACROSS the runway
        // centreline: 35 m gap, colinear, dead-ended, unconnected — every other
        // gate passes, but the bridge segment crosses the centreline and its
        // midpoint sits inside the 30 m-half-width corridor, so it is refused.
        double l = JunctionLat;
        var g = Build(new List<TaxiPath>
        {
            Path(l, Lon(-100), l, Lon(-20), "X"),
            Path(l, Lon(15),   l, Lon(100), "X"),
        }, withCenterline: true);
        Assert.Empty(g.BridgedGaps);
    }

    [Fact]
    public void M5Shape_BridgesEvenWithACenterlinePresent()
    {
        // The runway gate must refuse crossings, not proximity to an airport that
        // HAS runways — the M5 shape (well off the corridor, parallel to it) still
        // bridges when the centreline exists.
        var g = Build(M5Shape(), withCenterline: true);
        Assert.Single(g.BridgedGaps);
    }

    // ------------------------------------------------------------------
    // BridgeTinyDeadEndGaps — the DIFFERENT-taxiway missed-junction repair.
    //
    // Regression pinned: EGKK 26L (2026-08-31, live). Taxiway AS's easternmost
    // vertex sits 3.56 m from taxiway A — above the 1.5 m endpoint merge, so the
    // junction never existed in the graph. An aircraft holding AT A1, cleared
    // "via A1, line up 26L", was routed 712 m the wrong way (back along AS, up P,
    // around the AN north arm, down A) to reach a runway entry 94 m in front of
    // its nose, tone pointing 176° behind.
    // ------------------------------------------------------------------

    /// <summary>Node lookup by BOTH coordinates — the A chain stacks several nodes
    /// on one longitude, which <see cref="NodeAtLon"/> cannot tell apart.</summary>
    private static int NodeAt(TaxiGraph g, double lat, double metresEast)
    {
        foreach (var n in g.Nodes.Values)
            if (Math.Abs(n.Longitude * M_PER_DEG - metresEast) < 1.0 &&
                Math.Abs(n.Latitude - lat) * M_PER_DEG < 1.0)
                return n.NodeId;
        throw new Xunit.Sdk.XunitException($"no node at ({lat}, {metresEast} m east)");
    }

    /// <summary>
    /// EGKK-AS-shaped fixture: an east-west chain "AS" dead-ending
    /// <paramref name="gapM"/> short of a mid-chain vertex of the north-south
    /// taxiway "A". The A vertex at (JunctionLat, 200+gap m) is NOT a dead end.
    /// </summary>
    private static List<TaxiPath> AsAShape(double gapM = 3.5, string deadEndName = "AS")
    {
        double aLon = 200.0 + gapM;
        double lN = JunctionLat + 60.0 * DEG_PER_M;
        double lS = JunctionLat - 60.0 * DEG_PER_M;
        return new List<TaxiPath>
        {
            Path(JunctionLat, Lon(100), JunctionLat, Lon(160), deadEndName),
            Path(JunctionLat, Lon(160), JunctionLat, Lon(200), deadEndName),   // dead end at 200 m
            Path(lN, Lon(aLon), JunctionLat, Lon(aLon), "A"),
            Path(JunctionLat, Lon(aLon), lS, Lon(aLon), "A"),                  // mid-chain vertex at gap
        };
    }

    [Fact]
    public void TinyGap_DifferentNames_IsBridged_AndJoinsTheComponents()
    {
        var g = Build(AsAShape());

        var bridge = Assert.Single(g.TinyGapBridges);
        Assert.Equal("AS", bridge.TaxiwayName);
        Assert.InRange(bridge.GapMeters, 2.5, 4.5);
        Assert.Empty(g.BridgedGaps);   // the same-named pass has no claim here

        // The routing-visible fact: both taxiways are now one connected component.
        int asEnd = NodeAt(g, JunctionLat, 200);
        int aMid = NodeAt(g, JunctionLat, 203.5);
        Assert.Equal(g.Nodes[asEnd].ComponentId, g.Nodes[aMid].ComponentId);
    }

    [Fact]
    public void TinyGap_BridgeEdge_CarriesTheDeadEndsName_BothDirections()
    {
        var g = Build(AsAShape());
        int a = NodeAt(g, JunctionLat, 200);
        int b = NodeAt(g, JunctionLat, 203.5);
        var ab = g.Adjacency[a].Single(e => e.ToNodeId == b);
        var ba = g.Adjacency[b].Single(e => e.ToNodeId == a);
        Assert.Equal("AS", ab.TaxiwayName);
        Assert.Equal("AS", ba.TaxiwayName);
        Assert.Equal(ab.DistanceMeters, ba.DistanceMeters, 3);
    }

    [Fact]
    public void TinyGap_BeyondCap_IsNotBridged()
    {
        // 8 m is beyond the 6 m cap — a gap that size is a modelling decision,
        // not a digitising seam.
        var g = Build(AsAShape(gapM: 8.0));
        Assert.Empty(g.TinyGapBridges);
    }

    [Fact]
    public void TinyGap_UnnamedDeadEnd_IsNotBridged()
    {
        // Unnamed stubs and parking lead-ins stay untouched, same as the
        // same-named pass.
        var g = Build(AsAShape(deadEndName: ""));
        Assert.Empty(g.TinyGapBridges);
    }

    [Fact]
    public void TinyGap_AlreadyConnectedNearby_IsNotBridged()
    {
        // Same shape, plus real pavement joining AS to A ~200 m around (inside
        // the 300 m walk gate) — the bridge would be a shortcut chord, so the
        // connected-within-walk gate must refuse it.
        var paths = AsAShape();
        double lN = JunctionLat + 60.0 * DEG_PER_M;
        paths.Add(Path(JunctionLat, Lon(160), lN, Lon(160), "L"));
        paths.Add(Path(lN, Lon(160), lN, Lon(203.5), "L"));
        var g = Build(paths);
        Assert.Empty(g.TinyGapBridges);
    }

    [Fact]
    public void TinyGap_InsideARunwayCorridor_IsNotBridged()
    {
        // A named stub dead-ending 4 m from another taxiway's vertex, but the
        // whole gap sits INSIDE the runway corridor (|lon| < 30 m half-width):
        // a tiny gap is never an excuse to add pavement on a runway.
        double lS = JunctionLat - 80.0 * DEG_PER_M;
        var g = Build(new List<TaxiPath>
        {
            Path(JunctionLat, Lon(-90), JunctionLat, Lon(-4), "Q"),   // dead end 4 m west of axis
            Path(JunctionLat, Lon(0), lS, Lon(0), "R1"),              // vertex ON the axis
            Path(lS, Lon(0), lS, Lon(90), "R1"),
        }, withCenterline: true);
        Assert.Empty(g.TinyGapBridges);
    }

    [Fact]
    public void TinyGap_PicksTheNearestCandidate()
    {
        // Two candidate vertices, 3.5 m and 5.5 m from the dead end — the bridge
        // must land on the nearer one. B is a 3-piece chain so its candidate
        // vertex is mid-chain (its own dead ends sit 45 m away from everything
        // and start no bridge of their own).
        var paths = AsAShape(gapM: 3.5);
        double lN2 = JunctionLat + 45.0 * DEG_PER_M;
        double lS2 = JunctionLat - 45.0 * DEG_PER_M;
        paths.Add(Path(lN2, Lon(205.5), JunctionLat, Lon(205.5), "B"));
        paths.Add(Path(JunctionLat, Lon(205.5), lS2, Lon(205.5), "B"));
        var g = Build(paths);
        var bridge = Assert.Single(g.TinyGapBridges);
        Assert.Equal(NodeAt(g, JunctionLat, 203.5), bridge.TargetNode);
    }
}
