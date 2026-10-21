// Characterization tests for RunwayVacateResolver — the stage that pushes a
// landing-exit route destination past the runway-holding position.
//
// Regression pinned: EVRA (Riga) runway 18 → taxiway B, 2026-08-07.
//   The exit junction sits ON the runway centreline. The next node down B is 33 m
//   laterally (runway half-width 22.6 m), the one after 89 m, and the scenery's own
//   HSND hold-short node — the painted line — is at 106 m. The handoff routed to the
//   FIRST adjacent node and announced "hold position" with the aircraft 33 m from the
//   centreline: ~10 m past the pavement edge, 73 m short of the hold line, tail still
//   in the runway strip. Tower could not clear a departure to line up and asked the
//   pilot to continue past the hold line.
//
// Fixture: a north-south runway on the equator (lat 0 for the along-axis, so lateral
// offset in metres is longitude x 111132 x cos(0) = 111132). Runway heading 180
// (southbound) mirrors EVRA 18; exit nodes step south (decreasing lat) and east
// (increasing lon) exactly as B does at the real airport.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayVacateResolverTests
{
    private const double M_PER_DEG = 111132.0;      // TaxiGraph's shared constant
    private const double DEG_PER_M = 1.0 / M_PER_DEG;
    private const double RunwayWidthFt = 148.0;     // EVRA 18/36 — half-width 22.56 m

    // Runway 18: threshold at (0.03, 0) running south to (0, 0). Heading 180 true.
    private static Runway Runway18() => new Runway
    {
        RunwayID = "18",
        StartLat = 0.03,
        StartLon = 0.0,
        EndLat = 0.0,
        EndLon = 0.0,
        Heading = 180.0,
        Length = 0.03 * M_PER_DEG / 0.3048,
        Width = RunwayWidthFt,
    };

    private static TaxiPath Path(double lat1, double lon1, double lat2, double lon2,
                                 string name, string endType = "N")
        => new TaxiPath
        {
            StartLat = lat1, StartLon = lon1,
            EndLat = lat2, EndLon = lon2,
            Name = name,
            StartType = "N",
            EndType = endType,
            Width = 98.0,
        };

    /// <summary>A navdata stand lead-in: connector ("N") to stand ("P") — the shape
    /// TaxiGraph.BridgeOrphanParkingIslands looks for when reattaching an orphan stub.</summary>
    private static TaxiPath LeadIn(double connLat, double connLon, double standLat, double standLon)
        => new TaxiPath
        {
            StartLat = connLat, StartLon = connLon,
            EndLat = standLat, EndLon = standLon,
            Type = "P", StartType = "N", EndType = "P",
            Width = 60.0,
        };

    // Lateral offsets in metres east of the runway axis, mirroring EVRA's B.
    private const double JunctionLat = 0.015;                  // mid-runway, on the axis
    private static double Lon(double metres) => metres * DEG_PER_M;

    /// <summary>
    /// EVRA-shaped taxiway B: junction on the centreline → 33 m → 89 m → 106 m
    /// (hold-short node) → 134 m. Each hop also steps south so the geometry is a
    /// real angled exit rather than a perpendicular stub.
    /// </summary>
    private static TaxiGraph BuildEvraStyleB(bool withHoldNode = true)
    {
        double l0 = JunctionLat;
        double l1 = JunctionLat - 40.0 * DEG_PER_M;
        double l2 = JunctionLat - 44.0 * DEG_PER_M;
        double l3 = JunctionLat - 38.0 * DEG_PER_M;
        double l4 = JunctionLat - 26.0 * DEG_PER_M;

        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0),   l1, Lon(33),  "B"),
            Path(l1, Lon(33),  l2, Lon(89),  "B"),
            Path(l2, Lon(89),  l3, Lon(106), "B", endType: withHoldNode ? "HSND" : "N"),
            Path(l3, Lon(106), l4, Lon(134), "B"),
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static int NodeAtLon(TaxiGraph g, double metresEast)
    {
        foreach (var n in g.Nodes.Values)
            if (Math.Abs(n.Longitude * M_PER_DEG - metresEast) < 1.0)
                return n.NodeId;
        throw new Xunit.Sdk.XunitException($"no node at {metresEast} m east");
    }

    [Fact]
    public void EvraB_ExtendsPastTheHoldLine_NotTheFirstAdjacentNode()
    {
        var g = BuildEvraStyleB();
        int junction = NodeAtLon(g, 0);
        int firstAdjacent = NodeAtLon(g, 33);   // what FindExitExtensionNode returns

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, firstAdjacent, junction, Runway18(), 180.0,
            out double startLateral, out double endLateral);

        Assert.Equal(33.0, startLateral, 1);         // the 2026-08-07 stop point
        Assert.NotEqual(firstAdjacent, dest);
        // One hop PAST the 106 m hold node, so the whole airframe clears the line.
        Assert.Equal(NodeAtLon(g, 134), dest);
        Assert.Equal(134.0, endLateral, 1);
    }

    [Fact]
    public void WithoutAHoldNode_StopsAtTheFirstNodePastTheClearanceTarget()
    {
        var g = BuildEvraStyleB(withHoldNode: false);
        int junction = NodeAtLon(g, 0);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 33), junction, Runway18(), 180.0,
            out _, out double endLateral);

        // 89 m is short of the 90 m holding-position target; 106 m clears it and is
        // no longer a hold node, so the walk stops there rather than continuing.
        Assert.Equal(NodeAtLon(g, 106), dest);
        Assert.Equal(106.0, endLateral, 1);
        Assert.True(endLateral >= RunwayVacateResolver.VacatedClearanceMetres);
    }

    [Fact]
    public void AnAlreadyClearDestinationIsLeftAlone()
    {
        var g = BuildEvraStyleB(withHoldNode: false);
        int alreadyClear = NodeAtLon(g, 106);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, alreadyClear, NodeAtLon(g, 89), Runway18(), 180.0,
            out double startLateral, out double endLateral);

        Assert.Equal(alreadyClear, dest);
        Assert.Equal(106.0, startLateral, 1);
        Assert.Equal(106.0, endLateral, 1);
    }

    [Fact]
    public void ADestinationThatIsItselfTheHoldLineStillGetsTheTailClearanceHop()
    {
        // Same geometry the walk produces, but handed in as the starting destination
        // (as ApronNodeId can). It must land one hop past the line either way, so the
        // stop point doesn't depend on which branch supplied the node.
        var g = BuildEvraStyleB();
        int holdNode = NodeAtLon(g, 106);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, holdNode, NodeAtLon(g, 89), Runway18(), 180.0,
            out double startLateral, out double endLateral);

        Assert.Equal(NodeAtLon(g, 134), dest);
        Assert.Equal(106.0, startLateral, 1);
        Assert.Equal(134.0, endLateral, 1);
    }

    [Fact]
    public void AShortStubDegradesToItsFurthestNode_NeverBackTowardTheRunway()
    {
        // Exit taxiway that dead-ends 45 m out: no node reaches the 90 m target.
        var paths = new List<TaxiPath>
        {
            Path(JunctionLat, Lon(0), JunctionLat - 30.0 * DEG_PER_M, Lon(30), "B"),
            Path(JunctionLat - 30.0 * DEG_PER_M, Lon(30),
                 JunctionLat - 45.0 * DEG_PER_M, Lon(45), "B"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 30), NodeAtLon(g, 0), Runway18(), 180.0,
            out double startLateral, out double endLateral);

        Assert.Equal(NodeAtLon(g, 45), dest);        // furthest reachable, not the 30 m node
        Assert.Equal(30.0, startLateral, 1);
        Assert.Equal(45.0, endLateral, 1);
    }

    [Fact]
    public void NeverWalksBackOntoTheRunway()
    {
        // A parallel taxiway that touches the exit at 33 m and runs back to the
        // runway axis. The walk must refuse the runway-ward branch.
        double l1 = JunctionLat - 40.0 * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            Path(JunctionLat, Lon(0), l1, Lon(33), "B"),
            Path(l1, Lon(33), JunctionLat - 90.0 * DEG_PER_M, Lon(2), "K"),   // back to the axis
            Path(l1, Lon(33), l1 - 20.0 * DEG_PER_M, Lon(95), "B"),           // away — the right way
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 33), NodeAtLon(g, 0), Runway18(), 180.0,
            out _, out double endLateral);

        Assert.Equal(NodeAtLon(g, 95), dest);
        Assert.Equal(95.0, endLateral, 1);
    }

    /// <summary>
    /// Start rows for 18/36 so <c>TaxiGraph.Build</c> produces a real RunwayCenterline —
    /// without them the graph has none and every "is this node on runway pavement" test
    /// is vacuously false. Half-width is TaxiGraph's fixed 75 ft (22.86 m) default.
    /// </summary>
    private static List<StartPosition> Starts1836() => new()
    {
        new StartPosition { RunwayName = "18", Type = "R", Heading = 180.0, Latitude = 0.03, Longitude = 0.0 },
        new StartPosition { RunwayName = "36", Type = "R", Heading =   0.0, Latitude = 0.0,  Longitude = 0.0 },
    };

    [Fact]
    public void TransitsTheLandingRunwaysOwnPavementToReachClearance()
    {
        // KJFK 04L / taxiway J shape, measured 2026-08-08: the scenery models the first
        // node of the exit taxiway still INSIDE the runway width, so a walk that refuses
        // all runway pavement dead-ends with the aircraft ON the runway. Across 60
        // airports that stranded 35 exits at 0-10 m from the centreline — worse than the
        // EVRA defect this class was written for.
        double l0 = JunctionLat;
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0),  l0 - 10.0 * DEG_PER_M, Lon(10), "J"),   // still on pavement
            Path(l0 - 10.0 * DEG_PER_M, Lon(10), l0 - 45.0 * DEG_PER_M, Lon(95), "J"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), Starts1836());

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 10), NodeAtLon(g, 0), Runway18(), 180.0,
            out double startLateral, out double endLateral);

        Assert.Equal(10.0, startLateral, 1);          // the stranded-on-the-runway stop
        Assert.Equal(NodeAtLon(g, 95), dest);
        Assert.Equal(95.0, endLateral, 1);
    }

    [Fact]
    public void NeverCrossesToTheFarSideOfTheRunway()
    {
        // A taxiway that continues straight ACROSS the runway. Permitting transit of the
        // landing runway's own pavement means |offset| alone would rate the far side
        // (100 m west) as better progress than the near side (40 m east) — routing the
        // pilot back over the runway they just vacated. The side latch must prevent it,
        // even though it means stopping short of the 90 m target.
        double l0 = JunctionLat;
        double lA = l0 - 10.0 * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0), lA, Lon(10), "K"),                       // on pavement, east
            Path(lA, Lon(10), lA - 20.0 * DEG_PER_M, Lon(40), "K"),   // east, short of 90 m
            Path(lA, Lon(10), lA - 20.0 * DEG_PER_M, Lon(-100), "K"), // across, far side
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), Starts1836());

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 10), NodeAtLon(g, 0), Runway18(), 180.0,
            out _, out double endLateral);

        Assert.Equal(NodeAtLon(g, 40), dest);        // near side, NOT the far-side node
        Assert.Equal(40.0, endLateral, 1);
    }

    [Fact]
    public void StillRefusesToStepOntoADIFFERENTRunway()
    {
        // An exit feeding straight into a CROSSING runway must stop short of it. This is
        // what the pavement block exists for and it must survive the same-runway
        // exemption. The crossing runway (09/27) runs east-west across the exit at 60 m.
        double l0 = JunctionLat;
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0), l0 - 25.0 * DEG_PER_M, Lon(30), "L"),
            Path(l0 - 25.0 * DEG_PER_M, Lon(30), l0 - 30.0 * DEG_PER_M, Lon(100), "L"),
        };
        var starts = Starts1836();
        // 09/27 threshold pair, crossing east-west at the latitude of the second node.
        double xLat = l0 - 30.0 * DEG_PER_M;
        starts.Add(new StartPosition { RunwayName = "09", Type = "R", Heading = 90.0,  Latitude = xLat, Longitude = Lon(-1500) });
        starts.Add(new StartPosition { RunwayName = "27", Type = "R", Heading = 270.0, Latitude = xLat, Longitude = Lon(1500) });

        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 30), NodeAtLon(g, 0), Runway18(), 180.0,
            out _, out double endLateral);

        Assert.Equal(NodeAtLon(g, 30), dest);        // stopped short of the crossing runway
        Assert.Equal(30.0, endLateral, 1);
    }

    [Fact]
    public void CrossesAParallelStretchThatTheGreedyWalkCannot()
    {
        // VABB 09 / taxiway Q shape, measured 2026-08-08: the exit runs PARALLEL to the
        // runway for its first stretch (1.3 m out, next node also 1.3 m) before turning
        // away. A walk that demands a strictly increasing offset at every hop stops dead
        // on the pavement. The fallback search may traverse that stretch, provided the
        // node it settles on is genuinely clear.
        double l0 = JunctionLat;
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0), l0 - 5.0 * DEG_PER_M, Lon(12), "Q"),
            // Parallel run: same offset, 60 m further down the runway.
            Path(l0 - 5.0 * DEG_PER_M, Lon(12), l0 - 65.0 * DEG_PER_M, Lon(12), "Q"),
            // Then it finally turns away.
            Path(l0 - 65.0 * DEG_PER_M, Lon(12), l0 - 90.0 * DEG_PER_M, Lon(95), "Q"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), Starts1836());

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 12), NodeAtLon(g, 0), Runway18(), 180.0,
            out double startLateral, out double endLateral);

        Assert.Equal(12.0, startLateral, 1);          // greedy walk's dead stop
        Assert.Equal(NodeAtLon(g, 95), dest);
        Assert.Equal(95.0, endLateral, 1);
    }

    [Fact]
    public void TheFallbackSearchCannotAlterAnExitTheWalkAlreadyResolves()
    {
        // The search is gated on the greedy walk finishing short of the holding
        // position. Adding a far-flung branch off the EVRA fixture — which the search
        // would happily find — must change nothing, because the walk already succeeds
        // and the search is never entered.
        var g = BuildEvraStyleB();
        int junction = NodeAtLon(g, 0);
        int firstAdjacent = NodeAtLon(g, 33);

        int baseline = RunwayVacateResolver.ExtendClearOfRunway(
            g, firstAdjacent, junction, Runway18(), 180.0);

        Assert.Equal(NodeAtLon(g, 134), baseline);   // identical to the EVRA pin above
    }

    [Fact]
    public void TheFallbackSearchStillWillNotCrossTheRunway()
    {
        // Same crossing shape as the greedy-walk test, but arranged so only the FAR
        // side offers a node past the holding position. The search must decline it and
        // leave the near-side stop in place rather than route back over the runway.
        double l0 = JunctionLat;
        double lA = l0 - 10.0 * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0), lA, Lon(10), "K"),
            Path(lA, Lon(10), lA - 20.0 * DEG_PER_M, Lon(40), "K"),    // near side, 40 m
            Path(lA, Lon(10), lA - 20.0 * DEG_PER_M, Lon(-150), "K"),  // far side, past 90 m
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), Starts1836());

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 10), NodeAtLon(g, 0), Runway18(), 180.0,
            out _, out double endLateral);

        Assert.Equal(NodeAtLon(g, 40), dest);
        Assert.Equal(40.0, endLateral, 1);
    }

    [Theory]
    // A 148 ft runway (EVRA 18/36) is 22.6 m half-width; off-pavement needs 15 m more.
    [InlineData(148.0, 33.0, false)]   // the 2026-08-07 EVRA stop — 10 m past the edge
    [InlineData(148.0, 38.0, true)]
    [InlineData(197.0, 40.0, false)]   // a 60 m-wide runway needs correspondingly more
    [InlineData(197.0, 46.0, true)]
    [InlineData(0.0,   30.0, false)]   // no width in navdata -> TaxiGraph's 75 ft default
    [InlineData(0.0,   38.0, true)]
    public void OffPavementScalesWithRunwayWidth(double widthFt, double lateralM, bool expected)
    {
        var rwy = Runway18();
        rwy.Width = widthFt;
        Assert.Equal(expected, RunwayVacateResolver.IsOffPavement(lateralM, rwy));
    }

    [Fact]
    public void OffPavementIsAWeakerTestThanTheHoldingPosition()
    {
        // The two must never be conflated: an exit can be off the concrete and still
        // well short of where a controller wants you. Only the FORMER decides whether
        // the arrival callout may say "hold position".
        var rwy = Runway18();
        Assert.True(RunwayVacateResolver.IsOffPavement(45.0, rwy));
        Assert.True(45.0 < RunwayVacateResolver.VacatedClearanceMetres);
    }

    [Fact]
    public void DestinationPickPrefersApronThenSameNamedRetThenExtension()
    {
        var g = BuildEvraStyleB();
        var exit = new LandingExit
        {
            NodeId = NodeAtLon(g, 0),
            TaxiwayName = "B",
            ExitBearingTrue = 90.0,
            DistanceFromThresholdFeet = 1000.0,
        };
        var all = new List<LandingExit> { exit };

        // (c) extension node — no apron, no same-named sibling.
        exit.ApronNodeId = -1;
        Assert.Equal(NodeAtLon(g, 33), LandingExitDestination.Pick(g, exit, all, out string src));
        Assert.Equal("ext", src);

        // (b) furthest same-named non-End exit outranks the extension node —
        // provided it CHAINS from the chosen exit (300 ft here, a realistic
        // multi-segment RET step; see the chained-continuation cap test below).
        all.Add(new LandingExit
        {
            NodeId = NodeAtLon(g, 89), TaxiwayName = "B",
            DistanceFromThresholdFeet = 1300.0, ExitType = "Normal",
        });
        Assert.Equal(NodeAtLon(g, 89), LandingExitDestination.Pick(g, exit, all, out src));
        Assert.Equal("sameNamedRet", src);

        // (a) ApronNodeId outranks everything.
        exit.ApronNodeId = NodeAtLon(g, 106);
        Assert.Equal(NodeAtLon(g, 106), LandingExitDestination.Pick(g, exit, all, out src));
        Assert.Equal("apron", src);
    }

    [Fact]
    public void DestinationPickIgnoresAnEndExitAsASameNamedContinuation()
    {
        // An "End" exit is at the far end of the runway, not a continuation of this
        // RET — treating it as one would route the aircraft down the whole runway.
        var g = BuildEvraStyleB();
        var exit = new LandingExit
        {
            NodeId = NodeAtLon(g, 0), TaxiwayName = "B", ExitBearingTrue = 90.0,
            DistanceFromThresholdFeet = 1000.0, ApronNodeId = -1,
        };
        var all = new List<LandingExit>
        {
            exit,
            new LandingExit
            {
                NodeId = NodeAtLon(g, 89), TaxiwayName = "B",
                DistanceFromThresholdFeet = 9000.0, ExitType = "End",
            },
        };

        Assert.Equal(NodeAtLon(g, 33), LandingExitDestination.Pick(g, exit, all, out string src));
        Assert.Equal("ext", src);
    }

    [Fact]
    public void MissingGraphOrRunwayIsANoOp()
    {
        var g = BuildEvraStyleB();
        int node = NodeAtLon(g, 33);

        Assert.Equal(node, RunwayVacateResolver.ExtendClearOfRunway(null, node, 0, Runway18(), 180.0));
        Assert.Equal(node, RunwayVacateResolver.ExtendClearOfRunway(g, node, 0, null, 180.0));
        Assert.Equal(0, RunwayVacateResolver.ExtendClearOfRunway(g, 0, 0, Runway18(), 180.0));
    }

    [Fact]
    public void NeverFollowsAFabricatedStandBridgeOffTheRunway()
    {
        // Taxiway B makes one real hop off the runway (junction -> A, 33 m out) and then
        // dead-ends. An orphan stand stub sits 45 m further east of A — inside TaxiGraph's
        // 50 m bridge cap — so Build fabricates a StandBridgePathType edge straight from A
        // onto the stub. Because the stub reads as MORE progress away from the runway axis
        // than any real taxiway node, the un-fixed greedy walk (and its fallback search)
        // followed it and handed a just-landed pilot a stand they never chose (original
        // review finding 5, EDUA).
        double l1 = JunctionLat - 40.0 * DEG_PER_M;
        const double ConnectorLonM = 33.0 + 45.0;   // 45 m east of A — inside the 50 m bridge cap
        const double StandLonM = ConnectorLonM + 30.0;

        var paths = new List<TaxiPath>
        {
            // Pads the main component so it is not size-tied with the 2-node island —
            // ComputeMainComponentId must be unambiguous about which side is "main".
            Path(JunctionLat, Lon(0), JunctionLat, Lon(-50), "PAD"),
            Path(JunctionLat, Lon(0), l1, Lon(33), "B"),
            LeadIn(l1, Lon(ConnectorLonM), l1, Lon(StandLonM)),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        int junction = NodeAtLon(g, 0);
        int nodeA = NodeAtLon(g, 33);
        int connector = NodeAtLon(g, ConnectorLonM);
        int stand = NodeAtLon(g, StandLonM);

        // The fixture really produced a fabricated bridge, so this cannot pass vacuously.
        var bridgeEdge = Assert.Single(g.Adjacency[nodeA], e => e.ToNodeId == connector);
        Assert.Equal(TaxiGraph.StandBridgePathType, bridgeEdge.PathType);
        Assert.True(TaxiGraph.IsStandBridge(bridgeEdge));

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, nodeA, junction, Runway18(), 180.0, out _, out double endLateral);

        Assert.NotEqual(connector, dest);
        Assert.NotEqual(stand, dest);
        Assert.Equal(nodeA, dest);        // the node the walk reaches with the bridge ignored
        Assert.Equal(33.0, endLateral, 1);
    }

    [Fact]
    public void ExtensionNodePickerNeverExtendsOntoAFabricatedStandBridge()
    {
        // A stand stub sits 40 m due east of the exit junction — inside the 50 m bridge cap
        // — so Build fabricates a bridge from the junction straight onto it. Its bearing
        // (090, dead east) is a far closer match to the exit's own bearing (090) than the
        // REAL taxiway B (~140 degrees), so the un-fixed extension-node picker
        // (LandingExitDestination.FindExitExtensionNode) preferred the fabricated straight
        // line over the real taxiway.
        double l1 = JunctionLat - 40.0 * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            Path(JunctionLat, Lon(0), l1, Lon(33), "B"),
            // Pads the main component so it is not size-tied with the 2-node island —
            // ComputeMainComponentId must be unambiguous about which side is "main".
            Path(JunctionLat, Lon(0), JunctionLat, Lon(-50), "PAD"),
            LeadIn(JunctionLat, Lon(40), JunctionLat, Lon(70)),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        int junction = NodeAtLon(g, 0);
        int nodeA = NodeAtLon(g, 33);
        int connector = NodeAtLon(g, 40);

        var bridgeEdge = Assert.Single(g.Adjacency[junction], e => e.ToNodeId == connector);
        Assert.True(TaxiGraph.IsStandBridge(bridgeEdge));

        var exit = new LandingExit
        {
            NodeId = junction, TaxiwayName = "B", ExitBearingTrue = 90.0,
            DistanceFromThresholdFeet = 1000.0, ApronNodeId = -1,
        };

        int dest = LandingExitDestination.Pick(g, exit, new List<LandingExit> { exit }, out string src);

        Assert.Equal(nodeA, dest);
        Assert.Equal("ext", src);
    }

    [Fact]
    public void DestinationPickIgnoresAFarSameNamedSiblingAsAContinuation()
    {
        // KBNA class: single-letter-named sceneries carry several PHYSICALLY
        // SEPARATE turnoffs under one name (the coverage gap fill re-admits them
        // ≥ 1400 ft apart; EGLL 09R keeps both S5W junctions of a U-link 946 ft
        // apart). Those are siblings, not continuations of the chosen RET's arc —
        // an uncapped "furthest same-named" resolved the vacate destination to a
        // junction thousands of feet downfield and steered the pilot along the
        // runway-parallel taxiway past their own exit. A continuation must CHAIN
        // in short steps from the chosen exit.
        var g = BuildEvraStyleB();
        var exit = new LandingExit
        {
            NodeId = NodeAtLon(g, 0), TaxiwayName = "G", ExitBearingTrue = 90.0,
            DistanceFromThresholdFeet = 3700.0, ApronNodeId = -1,
        };
        var all = new List<LandingExit>
        {
            exit,
            new LandingExit
            {
                NodeId = NodeAtLon(g, 89), TaxiwayName = "G",
                DistanceFromThresholdFeet = 6155.0, ExitType = "Normal",
            },
        };

        Assert.Equal(NodeAtLon(g, 33), LandingExitDestination.Pick(g, exit, all, out string src));
        Assert.Equal("ext", src);

        // A genuine multi-segment arc still chains THROUGH intermediate nodes:
        // 3700 → 4000 → 4350 walks two ≤500 ft steps and returns the furthest.
        all.Add(new LandingExit
        {
            NodeId = NodeAtLon(g, 33), TaxiwayName = "G",
            DistanceFromThresholdFeet = 4000.0, ExitType = "High-speed",
        });
        all.Add(new LandingExit
        {
            NodeId = NodeAtLon(g, 106), TaxiwayName = "G",
            DistanceFromThresholdFeet = 4350.0, ExitType = "High-speed",
        });
        Assert.Equal(NodeAtLon(g, 106), LandingExitDestination.Pick(g, exit, all, out src));
        Assert.Equal("sameNamedRet", src);
    }

    // ---------------------------------------------------------------- crossing runway
    //
    // KDTW class (04R × 09L, found by the 2026-08-26 consistency sweep: 2,212 exits
    // DB-wide): at a runway intersection the exit's ApronNodeId can sit dead-centre
    // ON the crossing runway while being 95 m from the LANDING runway's axis. The
    // lateral-only "already vacated" early-exit then returned it untouched, and the
    // arrival callout told the pilot to stop and hold position — parked on 09L.
    //
    // Fixture: landing runway 18 as above; a crossing runway 09/27 built from start
    // rows running east-west through lat 0.010. Taxiway Y leaves the 18 centreline
    // at (0.0102, 0), reaches node A ON the 09/27 centreline 95 m east of 18's axis,
    // then continues north-east to node B — 150 m east of 18's axis and 60 m north
    // of the 09/27 centreline (default centerline half-width is 22.86 m), i.e.
    // genuinely clear of both runways.

    private const double CrossLat = 0.010;   // 09/27 centreline latitude

    private static StartPosition Start(string name, double heading, double lat, double lon)
        => new StartPosition { RunwayName = name, Type = "R", Heading = heading, Latitude = lat, Longitude = lon };

    private static TaxiGraph BuildCrossingRunwayGraph(bool withCrossingRunway)
    {
        double jLat = 0.0102;
        double aLat = CrossLat,               aLon = Lon(95);
        double bLat = CrossLat + 60.0 * DEG_PER_M, bLon = Lon(150);

        var paths = new List<TaxiPath>
        {
            Path(jLat, Lon(0), aLat, aLon, "Y"),
            Path(aLat, aLon,   bLat, bLon, "Y"),
        };
        var starts = new List<StartPosition>();
        if (withCrossingRunway)
        {
            starts.Add(Start("09", 90.0, CrossLat, -0.005));
            starts.Add(Start("27", 270.0, CrossLat, 0.005));
        }
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), starts,
                               new List<Runway> { Runway18() });
    }

    [Fact]
    public void DestinationOnACrossingRunway_IsWalkedOffIt()
    {
        var g = BuildCrossingRunwayGraph(withCrossingRunway: true);
        Assert.NotEmpty(g.RunwayCenterlines);
        int junction = NodeAtLon(g, 0);
        int a = NodeAtLon(g, 95);
        int b = NodeAtLon(g, 150);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, a, junction, Runway18(), 180.0,
            out double startLateral, out double endLateral);

        // Node A is 95 m from 18's axis — "already vacated" by the lateral-only test —
        // but sits ON the 09/27 centreline. The walk must continue to B.
        Assert.Equal(95.0, startLateral, 0);
        Assert.Equal(b, dest);
        Assert.Equal(150.0, endLateral, 0);
    }

    [Fact]
    public void AnotherRunwaysHoldLine_StopsTheWalkBeforeIt()
    {
        // Landing 18; the exit path leads toward crossing runway 09/27's own
        // hold-short line. The walk must stop at the node BEFORE that line — the
        // protected area between another runway's hold line and its pavement is
        // "off-pavement" by the landing runway's test, but parking in it is the
        // incursion the line exists to prevent. Gate fires only when the name
        // AND the geometry agree (the hold sits closer to 09/27's centreline
        // than to 18's axis).
        double jLat = 0.0106;                       // 66.7 m north of 09/27's centreline
        double hLat = 0.0105;                       // hold: 55.6 m from 09/27, 70 m from 18
        double tLat = 0.0104;
        var paths = new List<TaxiPath>
        {
            Path(jLat, Lon(0),  jLat, Lon(45), "Y"),
            Path(jLat, Lon(45), hLat, Lon(70), "Y", endType: "HSND"),
            Path(hLat, Lon(70), tLat, Lon(100), "Y"),
        };
        var starts = new List<StartPosition>
        {
            Start("09", 90.0, CrossLat, -0.005),
            Start("27", 270.0, CrossLat, 0.005),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts,
                                new List<Runway> { Runway18() });
        int junction = NodeAtLon(g, 0);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 45), junction, Runway18(), 180.0,
            out _, out double endLateral);

        // Stops at the 45 m node — off 18's pavement (edge + margin is 37.6 m),
        // short of 09/27's hold line at 70 m; never at or past the line itself.
        Assert.Equal(NodeAtLon(g, 45), dest);
        Assert.Equal(45.0, endLateral, 0);
    }

    [Fact]
    public void MisBoundHoldName_DoesNotStopTheWalk()
    {
        // The same crossing runway exists, but the hold node sits NEARER the
        // landing runway's axis (50 m) than 09/27's centreline (66.7 m) — the
        // classic nearest-centerline naming mis-bind at close parallels (12TS
        // class): the node is really the landing runway's own hold wearing the
        // other runway's name. Geometry outvotes the name and the walk proceeds
        // past it to the clearance target, exactly as before the gate existed.
        double jLat = 0.0106;
        var paths = new List<TaxiPath>
        {
            Path(jLat, Lon(0),  jLat, Lon(50), "Y", endType: "HSND"),
            Path(jLat, Lon(50), jLat, Lon(100), "Y"),
            Path(jLat, Lon(100), jLat, Lon(134), "Y"),
        };
        var starts = new List<StartPosition>
        {
            Start("09", 90.0, CrossLat, -0.005),
            Start("27", 270.0, CrossLat, 0.005),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts,
                                new List<Runway> { Runway18() });
        int junction = NodeAtLon(g, 0);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 50), junction, Runway18(), 180.0,
            out _, out double endLateral);

        // Past the mis-named hold, stopping at the first plain node beyond the
        // 90 m holding-position target.
        Assert.Equal(NodeAtLon(g, 100), dest);
        Assert.Equal(100.0, endLateral, 0);
    }

    [Fact]
    public void ClearOfOtherRunways_TellsTheTwoNodesApart()
    {
        var g = BuildCrossingRunwayGraph(withCrossingRunway: true);
        int a = NodeAtLon(g, 95);
        int b = NodeAtLon(g, 150);

        Assert.False(RunwayVacateResolver.IsClearOfOtherRunways(g, a, Runway18(), 180.0));
        Assert.True(RunwayVacateResolver.IsClearOfOtherRunways(g, b, Runway18(), 180.0));
    }

    [Fact]
    public void CenterlineHalfWidth_ComesFromTheRealRunwayWidth()
    {
        // The 75 ft default corridor under-reads a wide runway: a node 26 m off a
        // 200 ft-wide runway's centreline is ON its pavement but outside the 22.86 m
        // default, so IsOnDifferentRunway called it clear (708 resolver-invisible
        // stop points DB-wide). Build now matches each centerline to its runway by
        // collinearity and takes the real half-width.
        double jLat = 0.0102;
        var paths = new List<TaxiPath>
        {
            Path(jLat, Lon(0), CrossLat, Lon(95), "Y"),
        };
        // Crossing runway 09/27 through lat 0.010, 200 ft wide (half 30.48 m).
        var starts = new List<StartPosition>
        {
            Start("09", 90.0, CrossLat, -0.005),
            Start("27", 270.0, CrossLat, 0.005),
        };
        var wide = new Runway
        {
            RunwayID = "09",
            StartLat = CrossLat, StartLon = -0.005,
            EndLat = CrossLat, EndLon = 0.005,
            Heading = 90.0,
            Length = 0.01 * M_PER_DEG / 0.3048,
            Width = 200.0,
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts,
                                new List<Runway> { Runway18(), wide });

        var cl = Assert.Single(g.RunwayCenterlines);
        Assert.Equal(200.0 * 0.5 * 0.3048, cl.HalfWidthMeters, 2);

        // A node 26 m from the 09/27 centreline: on the real pavement, outside the
        // old default corridor — must now read as ON the runway.
        var probe = new TaxiPath
        {
            StartLat = CrossLat + 26.0 * DEG_PER_M, StartLon = Lon(95),
            EndLat = CrossLat + 120.0 * DEG_PER_M, EndLon = Lon(95),
            Name = "Z", StartType = "N", EndType = "N", Width = 98.0,
        };
        var g2 = TaxiGraph.Build(new List<TaxiPath>(paths) { probe },
                                 new List<ParkingSpot>(), starts,
                                 new List<Runway> { Runway18(), wide });
        int onPavement = 0;
        foreach (var n in g2.Nodes.Values)
            if (Math.Abs((n.Latitude - CrossLat) * M_PER_DEG - 26.0) < 1.0)
                onPavement = n.NodeId;
        Assert.True(onPavement > 0);
        Assert.False(RunwayVacateResolver.IsClearOfOtherRunways(g2, onPavement, Runway18(), 180.0));
    }

    [Fact]
    public void ExtBranchThatStalls_FallsBackToTheJunctionWalk()
    {
        // The extension-node pick commits the vacate walk to one branch of the
        // junction. Fixture: bearing points down branch A (10 m out, dead end);
        // branch B walks clear past 90 m. The junction-anchored walk (the pre-ext
        // behaviour) must win when the ext branch stalls inside the pavement.
        double l0 = JunctionLat;
        // Branch A: barely off the pavement side, dead end at 10 m east.
        double aLat = JunctionLat - 4.0 * DEG_PER_M;
        // Branch B: clean escape west... same side (east) to satisfy the side latch:
        // 40 m then 110 m east, stepping south.
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0),  aLat, Lon(10), "", endType: "N"),                     // A (stalls)
            Path(l0, Lon(0),  JunctionLat - 30.0 * DEG_PER_M, Lon(40), ""),         // B leg 1
            Path(JunctionLat - 30.0 * DEG_PER_M, Lon(40),
                 JunctionLat - 60.0 * DEG_PER_M, Lon(110), ""),                     // B leg 2
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(),
                                new List<Runway> { Runway18() });
        int junction = NodeAtLon(g, 0);
        int stallNode = NodeAtLon(g, 10);
        int clearNode = NodeAtLon(g, 110);

        // Exit whose bearing points at branch A: due east = 90 deg true.
        var exit = new LandingExit
        {
            NodeId = junction, TaxiwayName = "", ExitBearingTrue = 90.0,
            DistanceFromThresholdFeet = 1000.0, ApronNodeId = -1,
        };

        int dest = LandingExitDestination.Resolve(
            g, exit, new List<LandingExit> { exit }, Runway18(), 180.0,
            out _, out double endLateralM, out string src);

        Assert.NotEqual(stallNode, dest);
        Assert.Equal(clearNode, dest);
        Assert.True(endLateralM >= RunwayVacateResolver.VacatedClearanceMetres);
        Assert.Contains("junctionWalk", src);
    }

    [Fact]
    public void WithoutTheCrossingRunway_TheAlreadyClearDestinationIsStillLeftAlone()
    {
        // Control: identical geometry, no crossing runway in the graph. The 95 m node
        // is genuinely clear, and the early-exit must return it untouched — pinning
        // that the fix changes nothing for ordinary already-vacated destinations.
        var g = BuildCrossingRunwayGraph(withCrossingRunway: false);
        int junction = NodeAtLon(g, 0);
        int a = NodeAtLon(g, 95);

        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, a, junction, Runway18(), 180.0, out _, out double endLateral);

        Assert.Equal(a, dest);
        Assert.Equal(95.0, endLateral, 0);
        Assert.True(RunwayVacateResolver.IsClearOfOtherRunways(g, a, Runway18(), 180.0));
    }

    // ---- EGLL 09L -> A1 shape (VirtualPilot 2026-09-25) ----
    // The exit taxiway CURVES after clearing 90 m: 120 m, 123 m, then back to 120 m before the
    // runway's own painted line at 141 m. The greedy walk needs every step to get further out,
    // so it stopped at the first node past 90 m — 21 m short of the line — and the pilot was told "Off the runway"
    // inside the protected area. RunwayVacateResolver.ExtendPastOwnHoldAhead finds the line.
    private static TaxiGraph BuildCurvingExit(double holdM, double pastHoldM, bool shortHopPast = true)
    {
        double l0 = JunctionLat;
        double l1 = JunctionLat - 30.0 * DEG_PER_M;
        double l2 = JunctionLat - 60.0 * DEG_PER_M;
        double l3 = JunctionLat - 70.0 * DEG_PER_M;
        double l4 = JunctionLat - 85.0 * DEG_PER_M;
        double l5 = JunctionLat - 100.0 * DEG_PER_M;
        double l6 = JunctionLat - (shortHopPast ? 104.0 : 400.0) * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            Path(l0, Lon(0),   l1, Lon(60),  "A1"),
            Path(l1, Lon(60),  l2, Lon(120), "A1"),
            Path(l2, Lon(120), l3, Lon(123), "A1"),
            Path(l3, Lon(123), l4, Lon(120.5), "A1"),
            Path(l4, Lon(120.5), l5, Lon(holdM), "A1", endType: "HSND"),
            Path(l5, Lon(holdM), l6, Lon(pastHoldM), "A1"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(),
                                new List<Runway> { Runway18() });
        // Name the painted line for the runway just landed on, as the scenery does.
        g.Nodes[NodeAtLon(g, holdM)].HoldShortName = "A1, Runway 36";
        return g;
    }

    [Fact]
    public void CurvingExit_StopsPastTheLandedRunwaysOwnLine()
    {
        var g = BuildCurvingExit(141, 145);
        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 60), NodeAtLon(g, 0), Runway18(), 180.0,
            out _, out double endLateral);
        Assert.Equal(NodeAtLon(g, 145), dest);
        Assert.Equal(145.0, endLateral, 1);
    }

    [Fact]
    public void CurvingExit_WithoutTheSearch_KeepsTheOldStop()
    {
        var g = BuildCurvingExit(141, 145);
        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 60), NodeAtLon(g, 0), Runway18(), 180.0,
            out _, out _, ownHoldSearch: false);
        Assert.Equal(NodeAtLon(g, 120), dest);
    }

    [Fact]
    public void CurvingExit_NoShortHopPastTheLine_KeepsTheOldStop()
    {
        // Stopping ON the line with only a long leg onward leaves the taxi planner starting the
        // route somewhere else (KCVG 09 C: 298 m) — not worth it.
        var g = BuildCurvingExit(141, 300, shortHopPast: false);
        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 60), NodeAtLon(g, 0), Runway18(), 180.0, out _, out _);
        Assert.Equal(NodeAtLon(g, 120), dest);
    }

    [Fact]
    public void CurvingExit_ALineBeyond180m_IsNotAHoldingPosition()
    {
        var g = BuildCurvingExit(190, 194);
        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 60), NodeAtLon(g, 0), Runway18(), 180.0, out _, out _);
        Assert.Equal(NodeAtLon(g, 120), dest);
    }

    [Fact]
    public void CurvingExit_ALineNamedForAnotherRunway_IsNotFollowed()
    {
        var g = BuildCurvingExit(141, 145);
        g.Nodes[NodeAtLon(g, 141)].HoldShortName = "A1, Runway 09";
        int dest = RunwayVacateResolver.ExtendClearOfRunway(
            g, NodeAtLon(g, 60), NodeAtLon(g, 0), Runway18(), 180.0, out _, out _);
        Assert.Equal(NodeAtLon(g, 120), dest);
    }
}
