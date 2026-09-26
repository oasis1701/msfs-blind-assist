// Relocating a landing exit to where its branch leaves the runway must never REMOVE the exit
// (worldwide sweep, 2026-09-26). TaxiGraph.RefineExitByBranch moves an exit to its branch's junction,
// or to its forward sibling's; where that junction fails a distance rule the exit's own node passed,
// the exit stays where the producer found it instead of vanishing:
//   - a forward exit keeps its own node, with the branch's angle and type (the keepNode behaviour) -
//     KMTC 19 B and LSGL 18 L were dropped because their lead-ins start under 500 ft;
//   - a turnaround whose forward sibling fails is recorded as the turnaround, 130 degrees / End, at its
//     own node - 111 of the 154 directions that lost every exit, e.g. 0KS5 09. The rescue scan still
//     drops it: a backtrack is what that scan exists to avoid.
//
// Fixture frame, as in ExitBranchTests: a due-east runway on the equator, threshold at (0,0), 164 ft
// wide (half-width 25.0 m, clear boundary 35.0 m, corridor 40.0 m). Along-runway metres =
// longitude * 111132 and NORTH metres = latitude * 111132 (north = LEFT of the landing direction).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitRelocationTests
{
    private const double M_PER_DEG = 111132.0;

    private static Runway Runway09(double lengthM) => new()
    {
        RunwayID = "09", StartLat = 0.0, StartLon = 0.0, Heading = 90.0,
        Length = lengthM / 0.3048, Width = 164.0, ThresholdOffset = 0.0,
    };

    private static TaxiPath Seg(double a1, double n1, double a2, double n2, string name = "") => new()
    {
        Type = "T", Width = 98.0, Name = name,
        StartLat = n1 / M_PER_DEG, StartLon = a1 / M_PER_DEG,
        EndLat = n2 / M_PER_DEG, EndLon = a2 / M_PER_DEG,
    };

    private static TaxiGraph Build(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static TaxiNode NodeAt(TaxiGraph g, double along, double north)
        => g.Nodes.Values.OrderBy(n =>
               Math.Abs(n.Longitude * M_PER_DEG - along) + Math.Abs(n.Latitude * M_PER_DEG - north))
           .First();

    // A rapid exit "A" whose lead-in starts at (120,0) - 394 ft, under the 500 ft MIN_DIST_FT - and
    // curves out left through (160,6) (525 ft), (200,14), (240,24), (280,36) and (320,50). Its
    // sharpest turn up to the clear line is 16.7 degrees.
    private static TaxiGraph EarlyRapidExit() => Build(
        Seg(120, 0, 160, 6, "A"), Seg(160, 6, 200, 14, "A"), Seg(200, 14, 240, 24, "A"),
        Seg(240, 24, 280, 36, "A"), Seg(280, 36, 320, 50, "A"), Seg(320, 50, 360, 70, "A"));

    [Fact]
    public void A_forward_exit_whose_junction_is_under_500_ft_is_kept_at_its_own_node()
    {
        var g = EarlyRapidExit();

        var a = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "A");

        var node = NodeAt(g, 160, 6);
        Assert.Equal(node.NodeId, a.NodeId);
        Assert.InRange(a.DistanceFromThresholdFeet, 520.0, 530.0);
        // The branch's sharpest turn to clear, typed where the exit stands.
        Assert.InRange(a.ExitAngleDegrees, 15.5, 18.0);
        Assert.Equal("High-speed", a.ExitType);
    }

    [Fact]
    public void The_rescue_scan_keeps_a_forward_exit_whose_junction_is_behind_the_aircraft_at_its_own_node()
    {
        // The aircraft stands 500 ft down the runway: the lead-in start (394 ft) is behind it, the
        // exit's own node (525 ft) is ahead.
        var g = EarlyRapidExit();

        var a = Assert.Single(g.FindDownfieldExits(Runway09(3000.0), afterDistanceFromThresholdFeet: 500.0),
            e => e.TaxiwayName == "A");

        Assert.Equal(NodeAt(g, 160, 6).NodeId, a.NodeId);
        Assert.InRange(a.ExitAngleDegrees, 15.5, 18.0);
    }

    [Fact]
    public void An_exit_kept_at_its_own_node_takes_its_branchs_bearing_not_a_backward_edges()
    {
        // The KMTC 19 B shape: exit "K"'s lead line starts at (120,0) - 394 ft, under the 500 ft
        // MIN_DIST_FT - and runs along the centreline through (170,3) and (220,4) before its arm turns
        // off left through (240,12), (250,30) and (255,50). The first node past 500 ft, (170,3), has a
        // backward edge (3.4 degrees off the axis) that is more off-axis than its forward one (1.1),
        // so the producer read it as a backward peel: 130 degrees, End, with a bearing pointing back
        // down the runway. Kept at that node (its junction fails the 500 ft rule), the exit is now
        // typed Normal by its branch - and after "turn now" the rollout steers a Normal exit by its
        // bearing, so the bearing must be the branch's (forward, left), never the backward edge's.
        var g = Build(
            Seg(120, 0, 170, 3, "K"), Seg(170, 3, 220, 4, "K"), Seg(220, 4, 240, 12, "K"),
            Seg(240, 12, 250, 30, "K"), Seg(250, 30, 255, 50, "K"), Seg(255, 50, 260, 90, "K"));

        var k = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "K");

        Assert.Equal(NodeAt(g, 170, 3).NodeId, k.NodeId);
        Assert.Equal("Normal", k.ExitType);
        double relative = ((k.ExitBearingTrue - 90.0) % 360.0 + 540.0) % 360.0 - 180.0;
        Assert.True(RolloutExitGate.IsPlausibleExitBearing(k.ExitBearingTrue, 90.0), $"bearing {relative:F1} off the runway");
        Assert.InRange(relative, -90.0, -5.0);   // forward and LEFT (north of an eastbound runway)
        Assert.Equal("Left", k.ExitSide);
    }

    [Fact]
    public void A_ninety_degree_exit_that_hooks_back_beyond_the_runway_edge_is_listed_as_normal()
    {
        // Round 2, S2 (CYVR 26L D1, SNOL 30, MURU 06): the exit leaves the pavement at 90 degrees and
        // hooks back 134 degrees onto the taxiway system only between the edge and the clear line.
        var g = Build(Seg(1000, 0, 1000, 28, "D1"), Seg(1000, 28, 992, 36.3, "D1"), Seg(992, 36.3, 900, 40, "D1"));

        var d1 = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "D1");

        Assert.Equal("Normal", d1.ExitType);
        Assert.Equal(90.0, d1.ExitAngleDegrees);
    }

    // The KMEM M6 Y shape moved toward the threshold: the forward arm (unnamed) leaves the runway at
    // (68,0), 223 ft - under the 500 ft MIN_DIST_FT - and the backward arm (named M6, a 160-degree
    // turn) at (222,1), 728 ft, with a lead-in tail on to (254,-1); both meet the stem at (150,55).
    private static TaxiGraph EarlyYExit() => Build(
        Seg(68, 0, 126, 15), Seg(126, 15, 144, 33), Seg(144, 33, 150, 55),
        Seg(222, 1, 180, 16, "M6"), Seg(180, 16, 165, 32, "M6"), Seg(165, 32, 150, 55, "M6"),
        Seg(222, 1, 254, -1, "M6"), Seg(150, 55, 150, 85, "M6"));

    [Fact]
    public void A_turnaround_whose_forward_sibling_is_under_500_ft_is_recorded_as_a_turnaround()
    {
        var g = EarlyYExit();

        var m6 = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "M6");

        Assert.Equal(RolloutExitGate.TurnaroundExitAngleDeg, m6.ExitAngleDegrees);
        Assert.Equal("End", m6.ExitType);
        // At its own node - never moved to the sibling's junction the distance rules refused.
        Assert.NotEqual(NodeAt(g, 68, 0).NodeId, m6.NodeId);
        Assert.True(m6.DistanceFromThresholdFeet >= 500.0);
    }

    [Fact]
    public void The_rescue_scan_still_drops_a_turnaround_whose_forward_sibling_is_under_500_ft()
    {
        var g = EarlyYExit();

        Assert.DoesNotContain(g.FindDownfieldExits(Runway09(3000.0), afterDistanceFromThresholdFeet: 0.0),
            e => e.TaxiwayName == "M6");
    }

    [Fact]
    public void An_exit_found_at_a_node_it_shares_with_another_exit_stays_on_its_own_taxiway()
    {
        // The KATL 26L B4 shape (worldwide sweep, 2026-09-26: B4 listed as a copy of E3 at 7,760 ft).
        // B4 and E3 share node N (1000,30), where B4's 90-degree edge makes N a "B4" candidate. E3
        // comes in from its junction (900,0) through (960,10), nearer the centreline than B4's own
        // inward neighbour (985,20), so an unfiltered walk from N measured B4 along E3's arm and moved
        // it onto E3's junction. B4 leaves the runway at its own junction, (970,0) - 3,182 ft.
        var g = Build(
            Seg(900, 0, 960, 10, "E3"), Seg(960, 10, 1000, 30, "E3"), Seg(1000, 30, 1040, 50, "E3"),
            Seg(970, 0, 985, 20, "B4"), Seg(985, 20, 1000, 30, "B4"), Seg(1000, 30, 1000, 60, "B4"));

        var exits = g.GetLandingExits(Runway09(3000.0));

        var b4 = Assert.Single(exits, e => e.TaxiwayName == "B4");
        var e3 = Assert.Single(exits, e => e.TaxiwayName == "E3");
        Assert.InRange(b4.DistanceFromThresholdFeet, 3170.0, 3195.0);
        Assert.InRange(e3.DistanceFromThresholdFeet, 2940.0, 2965.0);
    }
}
