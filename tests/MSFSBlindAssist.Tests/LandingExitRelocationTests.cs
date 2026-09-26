// Where TaxiGraph.RefineExitByBranch places a landing exit (worldwide sweep, 2026-09-26, and the
// round-2 rulings that followed it):
//   - a FORWARD exit always keeps its own node, with its branch's angle, type and bearing (the bearing
//     measured where the exit stands). Moving it to its lead-in start put "turn now" early - R1 lets a
//     lead line run up to 150 m - and dropped exits whose lead-ins start under 500 ft (KMTC 19 B,
//     LSGL 18 L);
//   - a TURNAROUND moves only to where its forward sibling leaves the centreline (the sibling's
//     divergence node, never its lead-in start); when that node fails a distance rule it is recorded as
//     the turnaround, 130 degrees / End, at its own node - 111 of the 154 directions that lost every
//     exit, e.g. 0KS5 09. The rescue scan still drops it: a backtrack is what that scan exists to avoid.
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
    public void A_forward_exit_keeps_its_own_node_however_early_its_lead_line_starts()
    {
        // Round 2, S1 (KMIA 08R Z): "Z" leaves the runway from (1100,1) as a RET through (1150,10),
        // (1200,25) and (1250,45); its unnamed lead line starts 100 m earlier, at (1000,0). Moving the exit
        // to the lead-line start would put "turn now" 328 ft before the RET leaves the runway.
        var g = Build(
            Seg(1000, 0, 1100, 1), Seg(1100, 1, 1150, 10, "Z"), Seg(1150, 10, 1200, 25, "Z"),
            Seg(1200, 25, 1250, 45, "Z"));

        var z = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "Z");

        Assert.Equal(NodeAt(g, 1100, 1).NodeId, z.NodeId);
        Assert.InRange(z.DistanceFromThresholdFeet, 1100.0 / 0.3048 - 5.0, 1100.0 / 0.3048 + 5.0);
        Assert.Equal("High-speed", z.ExitType);
    }

    [Fact]
    public void A_kept_exit_takes_its_bearing_where_it_stands_not_at_its_lead_in_start()
    {
        // Review I1. "X" leaves the runway at 90 degrees, north (left), from (1000,1); its unnamed lead
        // line starts 100 m earlier, at (900,0), which is the branch's junction. Measured from the
        // junction, the lead line's first edge is shallow, so the bearing fell back to the chord to the
        // corridor node: -31 degrees, and after "turn now" the tone under-turned the pilot by 59.
        var g = Build(Seg(900, 0, 1000, 1), Seg(1000, 1, 1000, 60, "X"));

        var x = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "X");

        Assert.Equal(NodeAt(g, 1000, 1).NodeId, x.NodeId);
        double relative = ((x.ExitBearingTrue - 90.0) % 360.0 + 540.0) % 360.0 - 180.0;
        Assert.InRange(relative, -93.0, -87.0);
        Assert.Equal("Left", x.ExitSide);
    }

    [Fact]
    public void A_sibling_swap_lands_where_the_sibling_arm_leaves_the_centreline()
    {
        // Round 3, T1. The KMEM M6 Y shape, but the forward (unnamed) arm has a 120 m lead line along the
        // centreline, (798,1) -> (858,1) -> (918,0), before it leaves the band toward (976,15). The swap
        // for M6's backward arm lands at the divergence node (918,0) - 3,012 ft - not at the lead-line
        // start (798,1), which would put "turn now" nearly 400 ft early.
        var g = Build(
            Seg(798, 1, 858, 1), Seg(858, 1, 918, 0),
            Seg(918, 0, 976, 15), Seg(976, 15, 994, 33), Seg(994, 33, 1000, 55),
            Seg(1072, 1, 1030, 16, "M6"), Seg(1030, 16, 1015, 32, "M6"), Seg(1015, 32, 1000, 55, "M6"),
            Seg(1072, 1, 1104, -1, "M6"), Seg(1000, 55, 1000, 85, "M6"));

        var m6 = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "M6");

        Assert.Equal(NodeAt(g, 918, 0).NodeId, m6.NodeId);
        Assert.InRange(m6.DistanceFromThresholdFeet, 918.0 / 0.3048 - 5.0, 918.0 / 0.3048 + 5.0);
        Assert.Equal("Normal", m6.ExitType);
    }

    [Fact]
    public void A_nearer_turnaround_never_hides_a_forward_exit_of_the_same_name()
    {
        // Round 3, T3 (ULWB 33, YCAB 30). "A" has a backward stub at 700 m (a turnaround with no forward
        // arm, recorded as 130 degrees / End) and, 100 m on, a separate 90-degree exit at 800 m. The
        // per-name dedup used to keep the nearer entry - the turnaround - and the coverage window then
        // hid the forward exit behind it.
        var g = Build(Seg(700, 0, 660, 40, "A"), Seg(800, 0, 800, 60, "A"));

        var exits = g.GetLandingExits(Runway09(3000.0));

        var a = Assert.Single(exits, e => e.TaxiwayName == "A");
        Assert.Equal(NodeAt(g, 800, 0).NodeId, a.NodeId);
        Assert.Equal(90.0, a.ExitAngleDegrees);
    }

    [Fact]
    public void A_shared_runway_node_nearer_the_centreline_never_hides_the_real_forward_arm()
    {
        // Review Minor 1, pinning FindForwardSibling's pass order (09GE 32, 0AK 08, 0AL1 36: the other
        // order lost 267 runway directions' last usable exit). "Y"'s backward arm leaves from J (1060,1)
        // straight to the merge M (1040,30), then the stem runs on to (1040,60); its unnamed forward arm
        // leaves from its own junction F (1010,2) through (1030,15) to M. With M the backward arm's first
        // node, the arm's own nodes are just J. A walk allowed to END on J from the start follows J - it
        // is nearer the centreline than the forward arm - and measures the backward arm again.
        var g = Build(
            Seg(1060, 1, 1040, 30, "Y"), Seg(1040, 30, 1040, 60, "Y"),
            Seg(1010, 2, 1030, 15), Seg(1030, 15, 1040, 30));

        var y = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "Y");

        Assert.Equal(NodeAt(g, 1010, 2).NodeId, y.NodeId);
        Assert.True(y.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg);
    }

    [Fact]
    public void A_same_name_turnaround_is_never_coverage_for_a_forward_exit()
    {
        // Review Minor 2, pinning T3's coverage exception. "A" at 1,000 ft (forward, 90 degrees) takes the
        // name's slot; A's isolated backward stub at 2,600 ft (a recorded turnaround) is re-admitted by the
        // coverage fill, 1,600 ft from A 1,000; the forward A at 3,500 ft is 900 ft from that turnaround and
        // 2,500 ft from A 1,000. It is listed: a turnaround of its own name does not cover it.
        const double ftToM = 0.3048;
        var g = Build(
            Seg(1000 * ftToM, 0, 1000 * ftToM, 60, "A"),
            Seg(2600 * ftToM, 0, 2600 * ftToM - 40, 40, "A"),
            Seg(3500 * ftToM, 0, 3500 * ftToM, 60, "A"));

        var exits = g.GetLandingExits(Runway09(3000.0));

        Assert.Contains(exits, e => e.TaxiwayName == "A" && e.ExitAngleDegrees <= 90.0
            && Math.Abs(e.DistanceFromThresholdFeet - 3500.0) < 5.0);
    }

    [Fact]
    public void A_sibling_swap_is_judged_by_the_distance_rules_where_it_lands()
    {
        // Review Minor 2, pinning T1's distance rules at the divergence node. The sibling-swap fixture
        // above moved 750 m toward the threshold: the forward arm's lead line now starts at (48,1),
        // 157 ft - under the 500 ft MIN_DIST_FT - but the arm leaves the centreline at (168,0), 551 ft.
        // Judged where it lands, the swap stands; judged at the lead-line start, it was refused and M6
        // was recorded as a 130-degree turnaround.
        var g = Build(
            Seg(48, 1, 108, 1), Seg(108, 1, 168, 0),
            Seg(168, 0, 226, 15), Seg(226, 15, 244, 33), Seg(244, 33, 250, 55),
            Seg(322, 1, 280, 16, "M6"), Seg(280, 16, 265, 32, "M6"), Seg(265, 32, 250, 55, "M6"),
            Seg(322, 1, 354, -1, "M6"), Seg(250, 55, 250, 85, "M6"));

        var m6 = Assert.Single(g.GetLandingExits(Runway09(3000.0)), e => e.TaxiwayName == "M6");

        Assert.Equal(NodeAt(g, 168, 0).NodeId, m6.NodeId);
        Assert.Equal("Normal", m6.ExitType);
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

    [Fact]
    public void An_exit_on_a_lead_line_shared_with_another_named_exit_takes_its_own_arms_angle()
    {
        // Round 2, S3 (KMIA 08R M7): M7's first node sits on a lead line M6 crosses at 90 degrees. M7 is a
        // RET of about 22 degrees; M6's crossing is not M7.
        var g = ExitBranchTests.BuildSharedLeadLine();

        var exits = g.GetLandingExits(Runway09(3000.0));

        var m7 = exits.Where(e => e.TaxiwayName == "M7").OrderBy(e => e.DistanceFromThresholdFeet).First();
        Assert.Equal("High-speed", m7.ExitType);
        Assert.InRange(m7.ExitAngleDegrees, 15.0, 25.0);
        var m6 = Assert.Single(exits, e => e.TaxiwayName == "M6");
        Assert.Equal(90.0, m6.ExitAngleDegrees);
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
