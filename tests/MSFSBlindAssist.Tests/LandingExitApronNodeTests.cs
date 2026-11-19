// Characterization test for TaxiGraph.GetLandingExits' ApronNodeId computation on
// IMPLICIT exits (airports with no HoldShort/ILSHoldShort nodes — the fallback path).
//
// Regression pinned: LPFR (Faro) runway 28 → taxiway F, 2026-07-18.
//   F is modelled from the runway centreline outward as a curved stub: the junction
//   node sits ON the centreline, the first node off it is only ~12 m laterally (inside
//   the 22.6 m runway half-width), and the taxiway only clears the pavement two nodes
//   out (~77 m). F's first segment leaves the centreline at ~23° — just above the 20°
//   MIN_FALLBACK_EXIT_ANGLE_DEG gate — so the shallow-angle branch that runs
//   ExitPathLeavesCorridor was SKIPPED and ApronNodeId stayed -1. The LandingRollout →
//   Taxiing handoff then fell back to FindExitExtensionNode, which returned the FIRST
//   adjacent node (still on the runway), and the route "arrived" (Stop) with the
//   aircraft still on the pavement — Alt+Y reported "runway 10", ATC "not vacated".
//
// The fix computes the corridor-exit node for EVERY implicit exit, not just the
// shallow (<20°) ones, so ApronNodeId always points to a node clear of the runway.
//
// Fixture: an east-west runway on the equator (lat 0) so lateral offset in metres is
// simply |node.lat| x 111132 (cos(0) = 1). Taxiway F mirrors the LPFR shape: junction
// J on the centreline, node A ~12 m off (on the pavement), node B ~35 m off (inside the
// 37.6 m corridor tolerance), node C ~77 m off (clear). Nodes step away from the
// threshold as they go off-axis so the High-speed dedup keeps J (threshold-nearest) as
// the exit — matching LPFR, where the on-centreline junction was the selected node.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitApronNodeTests
{
    private const double M_PER_DEG = 111132.0;   // TaxiGraph's shared equirectangular constant
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    // Runway 09/27: threshold (0,0) → (0, 0.03). Width 148 ft (LPFR's runway width):
    // half-width 22.56 m; GetLandingExits' lateral corridor tolerance = 22.56 + 15 = 37.56 m.
    private const double RunwayWidthFt = 148.0;
    private const double HalfWidthM = RunwayWidthFt * 0.5 * 0.3048;      // 22.56 m
    private const double CorridorTolM = HalfWidthM + 15.0;               // 37.56 m

    private static Runway Runway0927() => new Runway
    {
        StartLat = 0.0,
        StartLon = 0.0,
        Heading = 90.0,                              // due east (true, per DB model)
        Length = 0.03 * M_PER_DEG / 0.3048,          // ~10938 ft
        Width = RunwayWidthFt,
        ThresholdOffset = 0.0,
    };

    // Taxiway F: J on the centreline at 1000 m along, curving off to the north.
    // Off-axis nodes step further from the threshold (larger lon) so the High-speed
    // dedup keeps J (nearest the threshold) as the selected exit.
    private static TaxiGraph BuildFaroFStyleGraph()
    {
        const double jLon = 0.009;                   // 1000.2 m along
        // First edge J→A: 12 m north over 28.3 m east ⇒ ~23° off the runway axis
        // (above the 20° MIN_FALLBACK gate — the precise condition that skipped the
        // shallow-angle ExitPathLeavesCorridor branch and left ApronNodeId = -1).
        double aLat = 12.0 * DEG_PER_M, aLon = jLon + 28.3 * DEG_PER_M;
        double bLat = 35.0 * DEG_PER_M, bLon = jLon + 55.0 * DEG_PER_M;   // inside corridor (35 < 37.56)
        double cLat = 77.0 * DEG_PER_M, cLon = jLon + 80.0 * DEG_PER_M;   // clear (77 > 37.56)

        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0,  StartLon = jLon, EndLat = aLat, EndLon = aLon, Name = "F" },
            new TaxiPath { StartLat = aLat, StartLon = aLon, EndLat = bLat, EndLon = bLon, Name = "F" },
            new TaxiPath { StartLat = bLat, StartLon = bLon, EndLat = cLat, EndLon = cLon, Name = "F" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    // Same first two segments as BuildFaroFStyleGraph (J -> A -> B), but B dead-ends
    // instead of continuing to a real "clear" node C. An orphan stand stub's connector
    // sits 10 m north of B — inside TaxiGraph's 50 m bridge cap — with a lateral offset
    // (45 m) outside the 37.56 m corridor tolerance. Because B is a dead end for the real
    // taxiway, a fabricated bridge is the ONLY way the un-fixed corridor BFS
    // (ExitPathLeavesCorridor) could ever leave the runway strip from here.
    private static TaxiGraph BuildFaroFStyleGraphWithDeadEndAndStandBridge()
    {
        const double jLon = 0.009;
        double aLat = 12.0 * DEG_PER_M, aLon = jLon + 28.3 * DEG_PER_M;
        double bLat = 35.0 * DEG_PER_M, bLon = jLon + 55.0 * DEG_PER_M;
        double connectorLat = 45.0 * DEG_PER_M;
        double standLat = 60.0 * DEG_PER_M;

        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0,  StartLon = jLon, EndLat = aLat, EndLon = aLon, Name = "F" },
            new TaxiPath { StartLat = aLat, StartLon = aLon, EndLat = bLat, EndLon = bLon, Name = "F" },
            new TaxiPath
            {
                StartLat = connectorLat, StartLon = bLon, EndLat = standLat, EndLon = bLon,
                Type = "P", StartType = "N", EndType = "P", Width = 60.0,
            },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static double LateralMetres(TaxiGraph g, int nodeId) =>
        Math.Abs(g.Nodes[nodeId].Latitude) * M_PER_DEG; // east-west runway ⇒ lateral = north offset

    [Fact]
    public void Faro_style_F_exit_is_detected_as_a_shallow_first_edge_implicit_exit()
    {
        var g = BuildFaroFStyleGraph();

        var f = Assert.Single(g.GetLandingExits(Runway0927()), e => e.TaxiwayName == "F");

        // The ~23° first stub alone would classify F as High-speed — which is what gated the
        // early handoff (TryEarlyExitHandoff fires only for High-speed exits) that routed the
        // LPFR aircraft to the on-runway extension node. But F's own route bends on to 40°
        // and then 59° off the runway before it is clear, so it is listed as the Normal turn
        // its route makes (GetLandingExits' CorroborateHighSpeed — the real LPFR 28 F turns
        // 87°): no early handoff, no rapid-exit callout, the spoken turn cue kept.
        Assert.Equal("Normal", f.ExitType);

        // The selected exit node is the junction on the runway centreline — routing to
        // it (or to its immediate neighbour) would strand the aircraft on the pavement.
        Assert.True(LateralMetres(g, f.NodeId) < HalfWidthM,
            $"exit NodeId should be on the runway; was {LateralMetres(g, f.NodeId):F1} m off centreline");
    }

    [Fact]
    public void ApronNodeId_points_to_a_node_clear_of_the_runway()
    {
        var g = BuildFaroFStyleGraph();

        var f = Assert.Single(g.GetLandingExits(Runway0927()), e => e.TaxiwayName == "F");

        // The fix: ApronNodeId is now set (was -1 before) and points to a node beyond
        // the runway corridor tolerance — the handoff destination is off the pavement,
        // so the route no longer "arrives" (Stop) while still on the runway.
        Assert.True(f.ApronNodeId > 0, "ApronNodeId must be set for the implicit F exit");
        Assert.NotEqual(f.NodeId, f.ApronNodeId);
        Assert.True(LateralMetres(g, f.ApronNodeId) > CorridorTolM,
            $"ApronNodeId should be clear of the runway corridor (> {CorridorTolM:F1} m); " +
            $"was {LateralMetres(g, f.ApronNodeId):F1} m off centreline");
    }

    [Fact]
    public void ApronNodeId_never_crosses_a_fabricated_stand_bridge()
    {
        var g = BuildFaroFStyleGraphWithDeadEndAndStandBridge();

        // The fixture really produced a fabricated bridge, so this cannot pass vacuously.
        Assert.Contains(g.Adjacency.Values.SelectMany(es => es), TaxiGraph.IsStandBridge);

        var f = Assert.Single(g.GetLandingExits(Runway0927()), e => e.TaxiwayName == "F");

        // With the bridge ignored there is no real taxiway node beyond B that clears the
        // runway corridor, so ApronNodeId must come back "not found" (-1) rather than
        // land on the stand stub the fabricated bridge reaches.
        Assert.Equal(-1, f.ApronNodeId);
    }

    // ------------------------------------------------------------------------
    // Exit-angle corroboration (KDTW 22L Y3, live 2026-08-18).
    //
    // Y3's junction node carries exactly ONE edge, a 34 m stub running 207.2° against a
    // 208.6° runway — 1.4° off axis, which read as a rapid exit taxiway. The pavement
    // then turns through 208°, 187°, 154°, 127°: an 81° right-angle turnoff. Believing
    // the stub fired the 900 ft rapid-exit callout and TryEarlyExitHandoff, which pans
    // the tone hard toward the exit from 300 ft out and skips the 150 ft "turn now"
    // verbal — the pilot got a 22° left pan at 38 kt with no spoken cue and missed Y3.
    //
    // Fixture: the same equatorial 09/27 runway. Exit "Y3" leaves the centreline with a
    // 34 m stub along the runway axis, then arcs away; a control exit "S5" is a genuine
    // ~30° rapid exit and must stay High-speed.
    // ------------------------------------------------------------------------

    private static TaxiGraph BuildStubThenTurnGraph()
    {
        const double jLon = 0.009;                          // Y3 junction, ~1000 m along
        // 34 m dead along the runway axis (due east), lateral offset unchanged.
        double s1Lat = 0.0,                    s1Lon = jLon + 34.0 * DEG_PER_M;
        // Then the arc: north-east, then north, clearing the 37.6 m corridor.
        double s2Lat = 20.0 * DEG_PER_M,       s2Lon = s1Lon + 20.0 * DEG_PER_M;
        double s3Lat = 60.0 * DEG_PER_M,       s3Lon = s2Lon + 10.0 * DEG_PER_M;

        // Control: a real RET at 2000 m along — one continuous ~30° divergence.
        const double rLon = 0.018;
        double r1Lat = 30.0 * DEG_PER_M,       r1Lon = rLon + 52.0 * DEG_PER_M;
        double r2Lat = 60.0 * DEG_PER_M,       r2Lon = r1Lon + 52.0 * DEG_PER_M;

        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0,   StartLon = jLon,  EndLat = s1Lat, EndLon = s1Lon, Name = "Y3" },
            new TaxiPath { StartLat = s1Lat, StartLon = s1Lon, EndLat = s2Lat, EndLon = s2Lon, Name = "Y3" },
            new TaxiPath { StartLat = s2Lat, StartLon = s2Lon, EndLat = s3Lat, EndLon = s3Lon, Name = "Y3" },

            new TaxiPath { StartLat = 0.0,   StartLon = rLon,  EndLat = r1Lat, EndLon = r1Lon, Name = "S5" },
            new TaxiPath { StartLat = r1Lat, StartLon = r1Lon, EndLat = r2Lat, EndLon = r2Lon, Name = "S5" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void A_stub_along_the_runway_does_not_make_a_right_angle_turnoff_a_rapid_exit()
    {
        var g = BuildStubThenTurnGraph();

        var y3 = Assert.Single(g.GetLandingExits(Runway0927()), e => e.TaxiwayName == "Y3");

        // First edge is ~0° off the runway axis; the path is not.
        Assert.Equal("Normal", y3.ExitType);
        Assert.True(y3.ExitAngleDegrees > 50.0,
            $"Y3 should be measured off its arc, not its stub; was {y3.ExitAngleDegrees:F1}°");
    }

    [Fact]
    public void A_genuine_rapid_exit_stays_high_speed()
    {
        var g = BuildStubThenTurnGraph();

        var s5 = Assert.Single(g.GetLandingExits(Runway0927()), e => e.TaxiwayName == "S5");

        // The corroboration only ever RAISES the angle, and a real RET never turns far
        // enough to cross the 50° ceiling — measured on live navdata: EIDW S5 21°,
        // EDDM B6 25°, LEMD L2 14°, KJFK J 21°, EHAM V1 20°, LPFR RG 29°.
        Assert.Equal("High-speed", s5.ExitType);
        Assert.True(s5.ExitAngleDegrees <= 50.0,
            $"S5 should stay inside the rapid-exit band; was {s5.ExitAngleDegrees:F1}°");
    }
}
