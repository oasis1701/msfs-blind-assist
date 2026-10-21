// Characterization tests for LandingExitDestination.PathTurnsBack — the test behind the
// exit list's "(sharp turn back)" label and the touchdown "Sharp turn back, slow down early."
//
// VirtualPilot 2026-09-18: a first-leg-only test missed exits that HOOK back (KDTW 09L V2:
// 40°, 333°, 285° on an 89° runway) and exits that hairpin partway (YBBN 01R A7), and every
// one of them opened the handoff with "Make a U-turn" on the runway with no warning in the
// exit list. A plain right-angle exit must stay unflagged — the label steers the list's
// default pick away from flagged exits.
//
// Fixture: a runway due east along the equator, so 1 m north = 1/111132° of latitude.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ExitTurnBackTests
{
    private const double DEG_PER_M = 1.0 / 111132.0;
    private const double RunwayHeading = 90.0;
    private const double JLon = 0.009;   // junction ~1000 m down the runway

    /// <summary>Builds a single named taxiway through the given (eastM, northM) points from the junction.</summary>
    private static (TaxiGraph g, int from, int to) Path(params (double e, double n)[] pts)
    {
        var paths = new List<TaxiPath>();
        for (int i = 0; i + 1 < pts.Length; i++)
            paths.Add(new TaxiPath
            {
                StartLat = pts[i].n * DEG_PER_M, StartLon = JLon + pts[i].e * DEG_PER_M,
                EndLat = pts[i + 1].n * DEG_PER_M, EndLon = JLon + pts[i + 1].e * DEG_PER_M,
                Name = "X",
            });
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        int Node((double e, double n) p) => g.FindNearestNode(p.n * DEG_PER_M, JLon + p.e * DEG_PER_M)!.NodeId;
        return (g, Node(pts[0]), Node(pts[^1]));
    }

    [Fact]
    public void A_right_angle_exit_does_not_turn_back()
    {
        var (g, from, to) = Path((0, 0), (0, 40), (5, 90), (30, 120));
        Assert.False(LandingExitDestination.PathTurnsBack(g, from, to, RunwayHeading));
    }

    [Fact]
    public void An_exit_that_hooks_back_down_the_runway_turns_back()
    {
        // KDTW 09L V2 shape: slightly forward, then round to the left and back west.
        var (g, from, to) = Path((0, 0), (14, 11), (7, 25), (-20, 32), (-60, 45));
        Assert.True(LandingExitDestination.PathTurnsBack(g, from, to, RunwayHeading));
    }

    [Fact]
    public void An_exit_that_hairpins_partway_turns_back()
    {
        // YBBN 01R A7 shape: off to the left at ~50°, then a hairpin to the right.
        var (g, from, to) = Path((0, 0), (25, 30), (45, 55), (45, 20), (50, -10));
        Assert.True(LandingExitDestination.PathTurnsBack(g, from, to, RunwayHeading));
    }

    [Fact]
    public void A_turn_back_beyond_the_window_does_not_count()
    {
        // Straight off at right angles for 200 m, then back west: a pilot is long off the
        // runway by then — nothing to warn about at touchdown.
        var (g, from, to) = Path((0, 0), (0, 100), (0, 200), (-60, 210));
        Assert.False(LandingExitDestination.PathTurnsBack(g, from, to, RunwayHeading));
    }
}
