// Characterization test for TaxiGraph.GetLandingExits' cross-runway exit naming —
// the exit's NAME and ExitBearingTrue must sit on the SAME side of the runway as the
// node the handoff route actually terminates at (ApronNodeId).
//
// Regression pinned: HESH (Sharm el-Sheikh) runway 04L, live 2026-08-26.
//   Every turnoff on 04L CROSSES the runway and carries a different name on each side
//   off ONE shared centreline junction node: F/J at 8233 ft, E/K at 6306 ft, G/H at
//   10082 ft. Both sides of such a junction fold to the same off-axis angle, so the
//   best-edge tie-break ("prefer the edge that turns most off-axis") was decided by a
//   fraction of a degree — 89.8 vs 89.6 at F/J — and picked the RIGHT-hand edge, while
//   ExitPathLeavesCorridor's BFS produced an ApronNodeId 91 m to the LEFT. The exit was
//   therefore announced as "taxiway J" with ExitBearingTrue 132.6 deg while every part
//   of the system that steers used the left-hand node on F.
//
// That is not cosmetic. ExitBearingTrue drives ResolveExitTurnDirection (the spoken
// "Turn LEFT/RIGHT now"), the Normal-exit tone after the turn-now callout,
// TryEarlyExitHandoff's pan floor, and the post-handoff alignedWithExitPH commit test —
// so a wrong side is a confident instruction to turn the wrong way across an active
// runway. For a blind pilot that is worse than no instruction at all.
//
// Fixture: an east-west runway on the equator (lat 0) so lateral offset in metres is
// simply |node.lat| x 111132 (cos(0) = 1). Runway heading 90 deg means +lat (north) is
// LEFT of the landing direction and -lat (south) is RIGHT.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitCrossingSideTests
{
    private const double M_PER_DEG = 111132.0;   // TaxiGraph's shared equirectangular constant
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private const double RunwayWidthFt = 148.0;
    private const double HalfWidthM = RunwayWidthFt * 0.5 * 0.3048;      // 22.56 m
    private const double CorridorTolM = HalfWidthM + 15.0;               // 37.56 m — both legs stop beyond this

    private static Runway Runway0927() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,                              // due east (true, per DB model)
        Length = 0.03 * M_PER_DEG / 0.3048,          // ~10938 ft
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    private const double JunctionLon = 0.009;        // ~1000 m along the runway

    /// <summary>
    /// A crossing turnoff off ONE centreline junction: "F" to the north (LEFT) and "J"
    /// to the south (RIGHT), both reaching clear of the runway corridor.
    ///
    /// The J leg is given the WIDER off-axis angle (due south, 90.0 deg) than F (5 deg
    /// east of north, 85.0 deg). That 5 deg is standing in for the 0.2 deg that decided
    /// HESH 04L: it makes the best-edge tie-break ("prefer the edge that turns most
    /// off-axis") choose J, independently of which side ExitPathLeavesCorridor's BFS
    /// returns as ApronNodeId. Both legs stop beyond the 37.56 m corridor so neither far
    /// node is itself picked up as a second exit.
    ///
    /// <paramref name="northFirst"/> controls path insertion order, which is what seeds
    /// the corridor BFS — so the same geometry can be built with the vacate side landing
    /// on either bank of the runway.
    /// </summary>
    private static TaxiGraph BuildCrossingGraph(bool northFirst)
    {
        // North (LEFT) leg: 92 m north, 8 m east ⇒ bearing ~5 deg ⇒ off-axis 85.0 deg.
        double fLat = 92.0 * DEG_PER_M, fLon = JunctionLon + 8.0 * DEG_PER_M;
        // South (RIGHT) leg: due south ⇒ bearing 180 deg ⇒ off-axis 90.0 deg (wider).
        double jLat = -92.0 * DEG_PER_M, jLon = JunctionLon;

        var north = new TaxiPath { StartLat = 0.0, StartLon = JunctionLon, EndLat = fLat, EndLon = fLon, Name = "F" };
        var south = new TaxiPath { StartLat = 0.0, StartLon = JunctionLon, EndLat = jLat, EndLon = jLon, Name = "J" };

        var paths = northFirst
            ? new List<TaxiPath> { north, south }
            : new List<TaxiPath> { south, north };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static double LateralSign(double bearingTrue, double rwyHeading)
    {
        double rel = bearingTrue - rwyHeading;
        while (rel > 180.0) rel -= 360.0;
        while (rel < -180.0) rel += 360.0;
        return Math.Sign(Math.Sin(rel * Math.PI / 180.0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossingExit_NameAndBearing_FollowTheSideTheRouteVacatesTo(bool northFirst)
    {
        // The invariant: whatever node the handoff route terminates at, and whatever the
        // pilot is told to turn toward, must be on the SAME bank of the runway. Before
        // the fix the name and bearing came from the wider-angle edge (always "J" here)
        // while ApronNodeId came from the corridor walk — the HESH 04L split.
        var graph = BuildCrossingGraph(northFirst);
        var rwy = Runway0927();

        var exit = Assert.Single(graph.GetLandingExits(rwy));

        Assert.True(exit.ApronNodeId > 0);
        Assert.True(graph.Nodes.TryGetValue(exit.ApronNodeId, out var apron));
        bool apronIsNorth = apron!.Latitude > 0;

        // Runway heading 090: north of the centreline is LEFT of the landing direction.
        Assert.Equal(apronIsNorth ? "F" : "J", exit.TaxiwayName);
        Assert.Equal(apronIsNorth ? "Left" : "Right", exit.ExitSide);
        Assert.Equal(apronIsNorth ? -1 : 1, LateralSign(exit.ExitBearingTrue, rwy.Heading));
    }

    [Fact]
    public void CrossingExit_ReconciledAngle_ComesFromTheReconciledEdge()
    {
        // The angle must be re-derived from the edge that won, not left behind on the
        // one that lost — ExitAngleDegrees gates the High-speed/Normal/End classification
        // and, through it, TryEarlyExitHandoff and the rapid-exit callouts.
        var graph = BuildCrossingGraph(northFirst: true);
        var rwy = Runway0927();

        var exit = Assert.Single(graph.GetLandingExits(rwy));

        Assert.True(graph.Nodes.TryGetValue(exit.ApronNodeId, out var apron));
        if (apron!.Latitude > 0)
            Assert.Equal(85.0, exit.ExitAngleDegrees, 1);   // F: 5 deg east of due north
        else
            Assert.Equal(90.0, exit.ExitAngleDegrees, 1);   // J: due south
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossingExit_JunctionJustOffThePavement_IsStillReconciled(bool northFirst)
    {
        // CYYZ class (D3/H/N/R/S/T, found by the 2026-08-26 consistency sweep): some
        // sceneries model the crossing's shared node just OFF the pavement edge —
        // 38-42 m off-centre against a 30.5 m half-width — but still inside the
        // +15 m corridor slack that admitted it as an exit in the first place. The
        // reconcile's on-pavement gate used the bare half-width, so these crossings
        // skipped reconciliation entirely and kept ExitBearingTrue on the edge
        // pointing back ACROSS the runway: spoken side (from ApronNodeId) and tone
        // side (from ExitBearingTrue) then contradicted each other. The gate now
        // shares the corridor tolerance (half-width + EXIT_CORRIDOR_SLACK_M).
        //
        // Junction at 30 m north of the centreline: outside the 22.56 m pavement,
        // inside the 37.56 m corridor. One leg continues north (clear at 130 m),
        // the other crosses to 95 m south; the south leg gets the wider off-axis
        // fold so the best-edge tie-break picks it — the CYYZ failure shape.
        double jN = 30.0 * DEG_PER_M;
        double nLat = 130.0 * DEG_PER_M, nLon = JunctionLon + 8.0 * DEG_PER_M; // ~5° east of north
        double sLat = -95.0 * DEG_PER_M, sLon = JunctionLon;                   // due south

        var north = new TaxiPath { StartLat = jN, StartLon = JunctionLon, EndLat = nLat, EndLon = nLon, Name = "D" };
        var south = new TaxiPath { StartLat = jN, StartLon = JunctionLon, EndLat = sLat, EndLon = sLon, Name = "D" };
        var paths = northFirst
            ? new List<TaxiPath> { north, south }
            : new List<TaxiPath> { south, north };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var rwy = Runway0927();

        var exit = Assert.Single(graph.GetLandingExits(rwy));

        Assert.True(exit.ApronNodeId > 0);
        Assert.True(graph.Nodes.TryGetValue(exit.ApronNodeId, out var apron));
        bool apronIsNorth = apron!.Latitude > jN * 0.5;

        // Whatever bank the handoff routes to, the bearing (and therefore the spoken
        // side AND the post-turn-now tone) must be on that same bank.
        Assert.Equal(apronIsNorth ? "Left" : "Right", exit.ExitSide);
        Assert.Equal(apronIsNorth ? -1 : 1, LateralSign(exit.ExitBearingTrue, rwy.Heading));
    }

    [Fact]
    public void UnnamedExit_StillGetsBearingAngleAndSide()
    {
        // 8,418 of the 8,450 direction-less Normal exits in the 2026-08-26 sweep were
        // exits onto UNNAMED taxiways: the named-only best-edge pick discarded their
        // geometry, leaving ExitBearingTrue = 0, a default 90 deg angle, no ExitSide,
        // no spoken turn direction and no turn-tone target. The unnamed-edge fallback
        // recovers all of it from the edge's real shape; only the name stays empty.
        // The real cohort arrives via hold-short markers: GA/small sceneries mark the
        // hold line but leave the taxiway unnamed (the implicit-exit BFS requires a
        // named edge, so an unnamed junction can ONLY be admitted as an HS node).
        double fLat = 92.0 * DEG_PER_M, fLon = JunctionLon + 8.0 * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0, StartLon = JunctionLon, EndLat = fLat, EndLon = fLon,
                           Name = "", StartType = "HSND", EndType = "N" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var rwy = Runway0927();

        var exit = Assert.Single(graph.GetLandingExits(rwy));

        Assert.Equal("", exit.TaxiwayName);              // no invented name
        Assert.True(exit.ExitBearingTrue > 0.0);         // real bearing recovered
        Assert.Equal("Left", exit.ExitSide);             // north of an 090 runway = left
        Assert.Equal(85.0, exit.ExitAngleDegrees, 1);    // 5 deg east of due north
        Assert.Equal(-1, LateralSign(exit.ExitBearingTrue, rwy.Heading));
    }

    [Fact]
    public void SingleSidedExit_IsUnchanged_NoReconcile()
    {
        // Control: one named leg only, no cross-runway ambiguity. The reconcile must be
        // a no-op — the overwhelming majority of exits take this path.
        double fLat = 92.0 * DEG_PER_M, fLon = JunctionLon + 8.0 * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0, StartLon = JunctionLon, EndLat = fLat, EndLon = fLon, Name = "F" },
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var rwy = Runway0927();

        var exit = Assert.Single(graph.GetLandingExits(rwy));

        Assert.Equal("F", exit.TaxiwayName);
        Assert.Equal("Left", exit.ExitSide);
        Assert.Equal(85.0, exit.ExitAngleDegrees, 1);   // 5 deg east of due north
    }
}
