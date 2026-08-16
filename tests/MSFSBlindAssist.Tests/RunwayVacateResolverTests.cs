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

        // (b) furthest same-named non-End exit outranks the extension node.
        all.Add(new LandingExit
        {
            NodeId = NodeAtLon(g, 89), TaxiwayName = "B",
            DistanceFromThresholdFeet = 2000.0, ExitType = "Normal",
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
}
