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

    private static double LateralMetres(TaxiGraph g, int nodeId) =>
        Math.Abs(g.Nodes[nodeId].Latitude) * M_PER_DEG; // east-west runway ⇒ lateral = north offset

    [Fact]
    public void Faro_style_F_exit_is_detected_as_a_shallow_first_edge_implicit_exit()
    {
        var g = BuildFaroFStyleGraph();

        var f = Assert.Single(g.GetLandingExits(Runway0927()), e => e.TaxiwayName == "F");

        // The ~23° first stub classifies F as High-speed — which is what gated the
        // early handoff (TryEarlyExitHandoff fires only for High-speed exits) that
        // routed the LPFR aircraft to the on-runway extension node.
        Assert.Equal("High-speed", f.ExitType);

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
}
