// Where a landing exit STANDS on its branch (PR #252 review, 2026-09-26): its bearing, side and angle are
// read at its own node, and a node whose own arm is blocked is never given an arm on the other side of the
// runway.
//
// Fixture frame, as in ExitBranchTests: a due-east runway on the equator, threshold at (0,0). Along-runway
// metres = longitude * 111132 and NORTH metres = latitude * 111132; north is LEFT of the landing direction,
// so a node the fs2024 rows put R metres to the right is written at north = -R.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitPlaceTests
{
    private const double M_PER_DEG = 111132.0;

    private static Runway Runway09(double lengthM, double widthFt = 164.0) => new()
    {
        RunwayID = "09", StartLat = 0.0, StartLon = 0.0, Heading = 90.0,
        Length = lengthM / 0.3048, Width = widthFt, ThresholdOffset = 0.0,
    };

    private static TaxiPath Seg(double a1, double n1, double a2, double n2, string name = "", string endType = "") => new()
    {
        Type = "T", Width = 98.0, Name = name, EndType = endType,
        StartLat = n1 / M_PER_DEG, StartLon = a1 / M_PER_DEG,
        EndLat = n2 / M_PER_DEG, EndLon = a2 / M_PER_DEG,
    };

    private static TaxiGraph Build(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static double LateralRight(LandingExit e) => -e.Latitude * M_PER_DEG;

    [Fact]
    public void A_hold_short_node_just_past_the_clear_line_takes_its_own_taxiways_bearing()
    {
        // Review V3-a: an unnamed lead line J(900,0)-A(1000,1 right); taxiway X runs out from A through
        // B (36 m right, past the 35 m clear line) to its hold-short node H (39 m) and on to C (80 m). The
        // branch is measured to B, and H was not on it: its bearing was taken from the lead-in start J -
        // the lead line's chord, 38.7° off the runway, for a taxiway leaving at 90°.
        var g = Build(
            Seg(900, 0, 1000, -1),
            Seg(1000, -1, 1000, -36, "X"),
            Seg(1000, -36, 1000, -39, "X", endType: "HSND"),
            Seg(1000, -39, 1000, -80, "X"));
        var x = g.GetLandingExits(Runway09(3000.0)).Single(e => e.TaxiwayName == "X");
        double rel = RolloutExitGate.ExitRelativeBearingDeg(x.ExitBearingTrue, 90.0);
        Assert.InRange(rel, 85.0, 95.0);
        Assert.Equal("Right", x.ExitSide);
        Assert.Equal("Normal", x.ExitType);
    }

    // WSSS 20L MY6 (fs2024 nodes 960, 2857-2860, 2854-2856, 2880-2873; runway 196 ft wide): a diagonal
    // crossing the runway from behind on the right to ahead on the left. Its right-hand half leads BACKWARD
    // for 20L (a turnaround) into A6/A7; the forward way off starts where it crosses the centreline.
    private static TaxiGraph Wsss20LMy6() => Build(
        Seg(1912.9, -30.6, 1942.6, -21.7, "MY6"),
        Seg(1942.6, -21.7, 1965.1, -15.8, "MY6"),
        Seg(1965.1, -15.8, 1981.6, -12.5, "MY6"),
        Seg(1981.6, -12.5, 2010.8, -6.5, "MY6"),
        Seg(2010.8, -6.5, 2030.6, -3.6, "MY6"),
        Seg(2030.6, -3.6, 2030.9, -1.6, "MY6"),
        Seg(2030.9, -1.6, 2045.1, 1.7, "MY6"),
        Seg(2045.1, 1.7, 2065.9, 5.0, "MY6"),
        Seg(2065.9, 5.0, 2089.5, 7.6, "MY6"),
        Seg(2089.5, 7.6, 2119.4, 13.9, "MY6"),
        Seg(2119.4, 13.9, 2137.0, 18.6, "MY6"),
        Seg(2137.0, 18.6, 2160.3, 25.8, "MY6"),
        Seg(2160.3, 25.8, 2186.4, 34.1, "MY6"),
        Seg(2186.4, 34.1, 2197.9, 38.9, "MY6"),
        Seg(2197.9, 38.9, 2207.9, 42.3, "MY6"),
        Seg(2207.9, 42.3, 2228.5, 52.0, "MY6"),
        // A6 / A7 at MY6's right-hand end (node 960): other taxiways, which the name-filtered walk never
        // takes - so MY6's right-hand half dead-ends there on its own name.
        Seg(1912.9, -30.6, 1877.4, -44.8, "A6"),
        Seg(1877.4, -44.8, 1856.1, -52.9, "A6"),
        Seg(1912.9, -30.6, 1936.0, -39.2, "A7"),
        Seg(1936.0, -39.2, 1954.3, -46.2, "A7"),
        Seg(1912.9, -30.6, 1894.2, -24.7, "A7"),
        Seg(1894.2, -24.7, 1867.4, -17.7, "A7"));

    [Fact]
    public void A_crossing_is_offered_where_it_leaves_forward_never_at_its_backward_halfs_node_with_the_other_side()
    {
        var exits = Wsss20LMy6().GetLandingExits(Runway09(4003.0, widthFt: 196.0));
        var my6 = exits.Where(e => e.TaxiwayName == "MY6").ToList();
        var forward = my6.Single(e => e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg);
        // Before the fix: node 2857, 21.7 m RIGHT, "Normal 81.3° Left" - the left half's 2.0 m jog and side,
        // while "turn now" steered at the node on the right.
        Assert.Equal("High-speed", forward.ExitType);
        Assert.Equal("Left", forward.ExitSide);
        Assert.InRange(LateralRight(forward), -5.0, 5.0);   // at the centreline crossing
        Assert.InRange(forward.ExitAngleDegrees, 20.0, 25.0);
    }

    [Fact]
    public void An_exit_whose_own_stretch_runs_along_the_runway_is_steered_toward_where_it_clears()
    {
        // KSFB 36 C (fs2024, nodes 515-480, 151 ft wide): the hold-short node sits 3 m left, and the branch
        // runs 67 m 0.1° RIGHT of the runway heading before curving off LEFT to a dead end past the clear
        // line - no corridor node within reach. With nothing further out to aim at, the side was that 0.1°:
        // "Right" for an exit to the left.
        var g = Build(
            Seg(1423.2, 0.8, 1427.8, 3.0, "C", endType: "HSND"),
            Seg(1427.8, 3.0, 1495.2, 2.9, "C"), Seg(1495.2, 2.9, 1508.6, 4.3, "C"),
            Seg(1508.6, 4.3, 1519.2, 10.3, "C"), Seg(1519.2, 10.3, 1529.4, 19.9, "C"),
            Seg(1529.4, 19.9, 1536.4, 34.7, "C"),
            // An ordinary exit D, as KSFB has, so the hold-short list is not all "End" (which lists C's
            // lead-in start instead).
            Seg(1000, 0, 1000, 30, "D", endType: "HSND"), Seg(1000, 30, 1000, 60, "D"));
        var c = g.GetLandingExits(Runway09(1825.8, widthFt: 151.0)).Single(e => e.TaxiwayName == "C");
        Assert.InRange(c.Longitude * M_PER_DEG, 1427.0, 1428.6);   // listed at its hold-short node
        Assert.Equal("Left", c.ExitSide);
        Assert.InRange(RolloutExitGate.ExitRelativeBearingDeg(c.ExitBearingTrue, 90.0), -25.0, -10.0);
    }
    [Fact]
    public void A_curved_rapid_exit_keeps_how_steeply_it_leaves_its_node()
    {
        // EDDB 24L M3 (fs2024, nodes 909-905, 197 ft wide): leaves its node at 6.9° and turns 24.3° in all.
        // The angle types it; the divergence is what the overshoot margin and the alignment handoff read.
        var g = Build(
            Seg(2285.5, 0.3, 2321.3, -4.1, "M3"), Seg(2321.3, -4.1, 2390.2, -17.6, "M3"),
            Seg(2390.2, -17.6, 2450.7, -36.5, "M3"), Seg(2450.7, -36.5, 2500.8, -59.1, "M3"));
        var m3 = g.GetLandingExits(Runway09(4000.0, widthFt: 197.0)).Single(e => e.TaxiwayName == "M3");
        Assert.InRange(m3.Longitude * M_PER_DEG, 2285.0, 2286.0);
        Assert.Equal("High-speed", m3.ExitType);
        Assert.InRange(m3.ExitAngleDegrees, 23.5, 25.0);
        Assert.InRange(m3.DivergenceAngleDegrees, 6.5, 7.5);
    }
}
