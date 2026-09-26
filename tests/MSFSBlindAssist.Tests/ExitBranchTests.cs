// ExitBranch — measuring a landing exit by its whole branch (KMEM 36L, 2026-09-26).
//
// Fixture frame: a due-east runway on the equator, threshold at (0,0), 164 ft wide (half-width
// 25.0 m, clear boundary 35.0 m, corridor 40.0 m). Along-runway metres = longitude * 111132 and
// NORTH metres = latitude * 111132; with heading 090 the lateral offset is -north (north = LEFT).
// Every assertion uses |lateral|, so the side never matters here.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ExitBranchTests
{
    private const double M_PER_DEG = 111132.0;
    private static readonly RunwayAxis Axis = new(0.0, 0.0, 90.0, 164.0 * 0.3048 * 0.5);

    private static TaxiPath Seg(double a1, double n1, double a2, double n2, string name = "") => new()
    {
        Type = "T", Width = 98.0, Name = name,
        StartLat = n1 / M_PER_DEG, StartLon = a1 / M_PER_DEG,
        EndLat = n2 / M_PER_DEG, EndLon = a2 / M_PER_DEG,
    };

    private static TaxiGraph Build(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static int NodeAt(TaxiGraph g, double along, double north)
        => g.Nodes.Values.OrderBy(n =>
               Math.Abs(n.Longitude * M_PER_DEG - along) + Math.Abs(n.Latitude * M_PER_DEG - north))
           .First().NodeId;

    [Fact]
    public void Axis_projects_along_and_lateral()
    {
        var (along, lateral) = Axis.Project(10.0 / M_PER_DEG, 1000.0 / M_PER_DEG);
        Assert.InRange(along, 999.9, 1000.1);
        Assert.InRange(lateral, -10.1, -9.9);   // north of an eastbound runway is its left
        Assert.InRange(Axis.ClearLateralMetres, 34.9, 35.1);
        Assert.InRange(Axis.CorridorLateralMetres, 39.9, 40.1);
    }

    [Fact]
    public void A_perpendicular_exit_turns_ninety_degrees()
    {
        var g = Build(Seg(1000, 0, 1000, 60, "A"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0));
        Assert.True(b.IsMeasured);
        Assert.False(b.IsTurnaround);
        Assert.Equal(NodeAt(g, 1000, 0), b.JunctionNodeId);
        Assert.InRange(b.TurnToClearDeg, 89.0, 91.0);
    }

    [Fact]
    public void A_curved_fillet_is_measured_by_its_sharpest_turn_not_its_first_segment()
    {
        // KMEM M5's 36L arm: 13.8°, 26.8°, 46.5°, 72.6°.
        var g = Build(
            Seg(1000.0, 0.0, 1018.3, 4.5, "M5"),
            Seg(1018.3, 4.5, 1039.6, 15.3, "M5"),
            Seg(1039.6, 15.3, 1057.0, 33.6, "M5"),
            Seg(1057.0, 33.6, 1064.0, 56.0, "M5"));
        var fromOuterNode = ExitBranch.Analyze(g, Axis, NodeAt(g, 1039.6, 15.3));
        Assert.Equal(NodeAt(g, 1000, 0), fromOuterNode.JunctionNodeId);
        Assert.InRange(fromOuterNode.TurnToClearDeg, 71.5, 73.7);
        Assert.Equal(NodeAt(g, 1064.0, 56.0), fromOuterNode.ClearNodeId);
    }

    [Fact]
    public void A_stub_peeling_back_toward_the_approach_end_is_a_turnaround_with_no_sibling()
    {
        var g = Build(Seg(1000, 0, 915, 85, "Z9"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0));
        Assert.True(b.IsTurnaround);
        Assert.Null(ExitBranch.FindForwardSibling(g, Axis, b, "Z9"));
    }

    // The KMEM M6 shape: a forward arm (unnamed) from a junction behind the merge, a backward arm
    // (named) from a junction ahead of it with a lead-in tail along the centerline, and a named stub.
    private static TaxiGraph BuildY(string forwardArmName = "", double mergeNorth = 55.0)
        => Build(
            Seg(918, 0, 976, 15, forwardArmName),
            Seg(976, 15, 994, 33, forwardArmName),
            Seg(994, 33, 1000, mergeNorth, forwardArmName),
            Seg(1072, 1, 1030, 16, "M6"),
            Seg(1030, 16, 1015, 32, "M6"),
            Seg(1015, 32, 1000, mergeNorth, "M6"),
            Seg(1072, 1, 1104, -1, "M6"),
            Seg(1000, mergeNorth, 1000, 85, "M6"));

    [Fact]
    public void The_wrong_direction_arm_of_a_Y_exit_is_a_turnaround()
    {
        var g = BuildY();
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1030, 16));
        Assert.Equal(NodeAt(g, 1072, 1), b.JunctionNodeId);
        Assert.True(b.IsTurnaround);
    }

    [Fact]
    public void The_right_direction_sibling_of_a_Y_exit_is_found()
    {
        var g = BuildY();
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1030, 16));
        var sibling = ExitBranch.FindForwardSibling(g, Axis, backward, "M6");
        Assert.NotNull(sibling);
        Assert.Equal(NodeAt(g, 918, 0), sibling!.JunctionNodeId);
        Assert.False(sibling.IsTurnaround);
        Assert.InRange(sibling.TurnToClearDeg, 73.0, 76.0);
    }

    [Fact]
    public void A_sibling_carrying_another_taxiways_name_is_not_this_exit()
    {
        var g = BuildY(forwardArmName: "M5");
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1030, 16));
        Assert.Null(ExitBranch.FindForwardSibling(g, Axis, backward, "M6"));
    }

    [Fact]
    public void A_merge_between_the_clear_and_corridor_boundaries_still_finds_the_sibling()
    {
        var g = BuildY(mergeNorth: 37.0);
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1030, 16));
        var sibling = ExitBranch.FindForwardSibling(g, Axis, backward, "M6");
        Assert.NotNull(sibling);
        Assert.Equal(NodeAt(g, 918, 0), sibling!.JunctionNodeId);
    }

    [Fact]
    public void A_candidate_on_a_lead_in_tail_ahead_of_the_junction_is_measured_from_the_junction()
    {
        var g = BuildY();
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1104, -1));
        Assert.Equal(NodeAt(g, 1072, 1), b.JunctionNodeId);
        Assert.True(b.IsMeasured);
        Assert.True(b.IsTurnaround);
    }

    [Fact]
    public void A_dead_end_that_never_leaves_the_runway_is_unmeasured()
    {
        var g = Build(Seg(1000, 0, 1030, 20, "D"));
        Assert.False(ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0)).IsMeasured);
    }

    [Fact]
    public void At_a_crossing_the_seed_picks_the_side()
    {
        var g = Build(Seg(1000, 0, 1000, 60, "B"), Seg(1000, 0, 1000, -60, "B"));
        int j = NodeAt(g, 1000, 0), north = NodeAt(g, 1000, 60), south = NodeAt(g, 1000, -60);
        Assert.Equal(north, ExitBranch.Analyze(g, Axis, j, north).ClearNodeId);
        Assert.Equal(south, ExitBranch.Analyze(g, Axis, j, south).ClearNodeId);
    }

    // --- Review fix round 1 ---------------------------------------------------------------------

    [Fact]
    public void A_candidate_that_never_gets_closer_to_the_centerline_is_unmeasured()
    {
        // The candidate (1000,37) already sits beyond the runway half-width (25.0 m here); its only
        // neighbour (1000,90) is even further out, so the inward walk can't step closer to the
        // centerline at all and gives up right where it started — off the runway pavement. That must
        // never be reported as a measured, 0-degree-turn exit.
        var g = Build(Seg(1000, 37, 1000, 90, "A"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 37));
        Assert.False(b.IsMeasured);
    }

    [Fact]
    public void A_back_angled_stub_that_never_reaches_the_runway_is_unmeasured()
    {
        // Same shape as above but the one reachable neighbour is also back-angled (a would-be
        // turnaround) rather than straight out — still off the pavement, still unmeasured.
        var g = Build(Seg(1000, 37, 930, 100, "Z"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 37));
        Assert.False(b.IsMeasured);
    }

    [Fact]
    public void A_sibling_search_does_not_cross_a_differently_named_taxiway_to_reach_an_unrelated_connector()
    {
        // Z9 (1000,0)->(915,85) is the same turnaround stub as the no-sibling test above. Its clear
        // node (915,85) also happens to be the west end of an unrelated NAMED taxiway "A", running
        // east to (975,85); "A" in turn meets an unnamed connector that drops straight down to the
        // runway centerline at (975,0). Physically the connector's mouth sits close to Z9's clear
        // point, but it belongs to "A", not to Z9 — the flood must not cross "A" to reach it.
        var g = Build(
            Seg(1000, 0, 915, 85, "Z9"),
            Seg(915, 85, 975, 85, "A"),
            Seg(975, 85, 975, 0, ""));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0));
        Assert.True(backward.IsTurnaround);
        Assert.Null(ExitBranch.FindForwardSibling(g, Axis, backward, "Z9"));
    }

    // --- Task 3b R1: the band walk follows a simple lead-in line only ---------------------------
    // A lead-in line is a simple chain until it meets other pavement. The walk back along the band
    // may start only from an in-band node with at most two walkable neighbours, continues only from
    // nodes with exactly two, stops at the first node where the chain meets anything else, and never
    // follows more than 150 m of band (worldwide sweep, 2026-09-26: 1,591 exits relocated > 300 ft by
    // sliding down centreline taxi paths and other exits' lead lines).

    [Fact]
    public void An_exit_meeting_a_centreline_taxi_path_is_measured_from_the_meeting_node()
    {
        // A taxi path drawn down the whole runway centreline; exit A meets it at (1000,0), a node with
        // three neighbours. The old walk slid 300-400 m down the centreline toward the threshold.
        var paths = new List<TaxiPath>();
        for (int a = 0; a < 2000; a += 50) paths.Add(Seg(a, 0, a + 50, 0));
        paths.Add(Seg(1000, 0, 1000, 60, "A"));
        var g = Build(paths.ToArray());
        int meeting = NodeAt(g, 1000, 0);

        var fromOuterNode = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 60));
        Assert.Equal(meeting, fromOuterNode.JunctionNodeId);
        Assert.True(fromOuterNode.IsMeasured);
        Assert.InRange(fromOuterNode.TurnToClearDeg, 89.0, 91.0);

        var fromJunction = ExitBranch.Analyze(g, Axis, meeting, NodeAt(g, 1000, 60));
        Assert.Equal(meeting, fromJunction.JunctionNodeId);
    }

    [Fact]
    public void A_lead_in_chain_that_meets_another_exits_lead_line_stops_at_the_meeting_node()
    {
        // Exit B: lead line from (1100,0) along the band to M (1200,1), where B's arm leaves.
        // Exit A: arm from (1400,60) into the band at (1300,4), then a lead-in chain (1250,2) -> M.
        // The old walk carried on past M down B's lead line to (1100,0).
        var g = Build(
            Seg(1100, 0, 1200, 1, "B"), Seg(1200, 1, 1230, 10, "B"), Seg(1230, 10, 1270, 60, "B"),
            Seg(1400, 60, 1300, 4, "A"), Seg(1300, 4, 1250, 2, "A"), Seg(1250, 2, 1200, 1, "A"));

        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1400, 60));

        Assert.Equal(NodeAt(g, 1200, 1), b.JunctionNodeId);
        Assert.True(b.IsMeasured);
        Assert.False(b.IsTurnaround);
    }

    [Fact]
    public void The_band_walk_follows_at_most_150_metres_of_lead_in_line()
    {
        // Exit A drops into the band at (1300,3) and its lead-in line runs 300 m on toward the
        // threshold in 20 m segments. 150 m of band walk ends at (1160,3); the old walk ran on to
        // its hop limit at (1080,3).
        var paths = new List<TaxiPath> { Seg(1300, 60, 1300, 3, "A") };
        for (int a = 1300; a > 1000; a -= 20) paths.Add(Seg(a, 3, a - 20, 3, "A"));
        var g = Build(paths.ToArray());

        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1300, 60));

        Assert.Equal(NodeAt(g, 1160, 3), b.JunctionNodeId);
    }

    // --- Task 3b R6: a Y exit's forward sibling is on the same side, nearby -----------------------

    [Fact]
    public void A_same_named_taxiway_crossing_to_a_connector_on_the_other_side_gives_no_sibling()
    {
        // The KMCI 01L shape. Backward stub "E" from (1000,0) out to (950,40); a taxiway also named
        // "E" runs on from (950,40) to (900,40) and straight across the runway to (900,-40), where an
        // unnamed connector drops forward onto the runway at (840,0). That connector is 160 m from the
        // stub's junction but on the OTHER side: it is not the stub's other arm.
        var g = Build(
            Seg(1000, 0, 950, 40, "E"),
            Seg(950, 40, 900, 40, "E"), Seg(900, 40, 900, -40, "E"),
            Seg(900, -40, 870, -20), Seg(870, -20, 840, 0));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), NodeAt(g, 950, 40));
        Assert.True(backward.IsTurnaround);

        Assert.Null(ExitBranch.FindForwardSibling(g, Axis, backward, "E"));
    }

    [Fact]
    public void A_same_named_parallel_taxiway_leading_to_a_connector_over_300_m_away_gives_no_sibling()
    {
        // Backward stub "E" from (1000,0) out to (950,40); a parallel taxiway also named "E" runs on
        // to (830,40), where a connector drops forward onto the runway at (630,0) - 370 m along the
        // runway from the stub's junction. Too far to be the other arm of the same exit.
        var g = Build(
            Seg(1000, 0, 950, 40, "E"),
            Seg(950, 40, 830, 40, "E"),
            Seg(830, 40, 730, 20), Seg(730, 20, 630, 0));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), NodeAt(g, 950, 40));
        Assert.True(backward.IsTurnaround);

        Assert.Null(ExitBranch.FindForwardSibling(g, Axis, backward, "E"));
    }

    // --- Task 3b R7: the refinement's inward walk stays on the exit's own taxiway ---------------

    // The KATL 26L B4 shape: B4 and E3 share node N (1000,30). E3 comes in from its junction (900,0)
    // through (960,10), B4 from its junction (970,0) through (985,20); E3's inward neighbour is the
    // nearer the centreline, so an unfiltered walk from N follows E3.
    private static TaxiGraph BuildSharedNode() => Build(
        Seg(900, 0, 960, 10, "E3"), Seg(960, 10, 1000, 30, "E3"), Seg(1000, 30, 1040, 50, "E3"),
        Seg(970, 0, 985, 20, "B4"), Seg(985, 20, 1000, 30, "B4"), Seg(1000, 30, 1000, 60, "B4"));

    [Fact]
    public void A_name_filtered_walk_from_a_shared_node_is_not_measured_along_the_other_exits_arm()
    {
        var g = BuildSharedNode();
        int n = NodeAt(g, 1000, 30);

        Assert.Equal(NodeAt(g, 900, 0), ExitBranch.Analyze(g, Axis, n).JunctionNodeId);   // unfiltered: E3's
        var b4 = ExitBranch.Analyze(g, Axis, n, nameFilter: "b4");
        Assert.Equal(NodeAt(g, 970, 0), b4.JunctionNodeId);
        Assert.True(b4.IsMeasured);
    }

    [Fact]
    public void A_name_filtered_walk_that_cannot_reach_the_runway_on_its_own_taxiway_is_unmeasured()
    {
        // "K" is only the outer end of N: nothing named K (or unnamed) leads from N to the runway.
        var g = Build(
            Seg(900, 0, 960, 10, "E3"), Seg(960, 10, 1000, 30, "E3"), Seg(1000, 30, 1000, 60, "K"));

        Assert.False(ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 30), nameFilter: "K").IsMeasured);
    }

    // --- Task 3b R8: a Y exit whose arms merge inside the clear line ------------------------------

    [Fact]
    public void A_Y_exit_whose_arms_merge_inside_the_clear_line_finds_its_forward_sibling()
    {
        // The VADE 26 / KIXA 20 shape: the two arms merge at M (1000,30), inside the 35 m clear line
        // of this 25 m half-width runway. The backward arm's path runs junction .. M .. stem (1000,85),
        // so treating the whole path as its own arm walled the flood off from the forward arm at M.
        var g = Build(
            Seg(918, 0, 960, 12), Seg(960, 12, 985, 24), Seg(985, 24, 1000, 30),
            Seg(1072, 1, 1030, 16, "M6"), Seg(1030, 16, 1015, 25, "M6"), Seg(1015, 25, 1000, 30, "M6"),
            Seg(1072, 1, 1104, -1, "M6"), Seg(1000, 30, 1000, 85, "M6"));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1030, 16));
        Assert.Equal(NodeAt(g, 1072, 1), backward.JunctionNodeId);
        Assert.True(backward.IsTurnaround);

        var sibling = ExitBranch.FindForwardSibling(g, Axis, backward, "M6");

        Assert.NotNull(sibling);
        Assert.Equal(NodeAt(g, 918, 0), sibling!.JunctionNodeId);
        Assert.False(sibling.IsTurnaround);
    }

    // --- Task 3b round 2, S2: a turnaround is judged by how the branch LEAVES the pavement -------

    [Fact]
    public void A_branch_that_leaves_the_pavement_at_90_degrees_and_hooks_back_beyond_the_edge_is_not_a_turnaround()
    {
        // The CYVR 26L D1 / SNOL 30 shape: straight out to (1000,28), 3 m past the 25 m half-width, then
        // a 134-degree hook back to (992,36.3), past the 35 m clear line, onto the taxiway system.
        var g = Build(Seg(1000, 0, 1000, 28, "D1"), Seg(1000, 28, 992, 36.3, "D1"), Seg(992, 36.3, 900, 40, "D1"));

        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), NodeAt(g, 1000, 28));

        Assert.True(b.IsMeasured);
        Assert.InRange(b.TurnToLeaveDeg, 89.0, 91.0);
        Assert.InRange(b.TurnToClearDeg, 133.0, 135.0);
        Assert.False(b.IsTurnaround);
    }

    [Fact]
    public void An_arm_leaving_the_pavement_at_147_degrees_is_still_a_turnaround()
    {
        // KMEM M6's 18R arm leaves a 36L landing at about 147 degrees.
        var g = Build(Seg(1000, 0, 958, 27, "M6"), Seg(958, 27, 950, 60, "M6"));

        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), NodeAt(g, 958, 27));

        Assert.InRange(b.TurnToLeaveDeg, 146.0, 148.5);
        Assert.True(b.IsTurnaround);
    }

    // --- Task 3b round 2, S3: the outward search stays on the exit's own taxiway -----------------

    // The KMIA 08R M7 shape: M7's lead line runs along the centreline from (900,0) through (1000,1) and
    // (1080,2) to M (1100,2), where M6 crosses at 90 degrees out to (1100,60); M7 carries on to (1250,3)
    // and leaves as a RET through (1300,15), (1350,30) and (1400,50).
    internal static TaxiGraph BuildSharedLeadLine() => Build(
        Seg(900, 0, 1000, 1), Seg(1000, 1, 1080, 2), Seg(1080, 2, 1100, 2, "M7"),
        Seg(1100, 2, 1100, 60, "M6"),
        Seg(1100, 2, 1250, 3, "M7"), Seg(1250, 3, 1300, 15, "M7"), Seg(1300, 15, 1350, 30, "M7"),
        Seg(1350, 30, 1400, 50, "M7"));

    [Fact]
    public void A_candidate_on_a_lead_line_shared_with_another_named_exit_is_measured_along_its_own_arm()
    {
        var g = BuildSharedLeadLine();
        int candidate = NodeAt(g, 1080, 2);

        // Unfiltered (the hold-short gate's call), the nearest way off is M6's arm.
        Assert.Equal(NodeAt(g, 1100, 60), ExitBranch.Analyze(g, Axis, candidate).ClearNodeId);

        var m7 = ExitBranch.Analyze(g, Axis, candidate, nameFilter: "M7");
        Assert.Equal(NodeAt(g, 1400, 50), m7.ClearNodeId);
        Assert.InRange(m7.TurnToClearDeg, 15.0, 25.0);
    }

    [Fact]
    public void A_name_filtered_branch_that_never_clears_on_its_own_taxiway_is_unmeasured()
    {
        // Only M6 leads off the runway from K's lead line; nothing named K (or unnamed) clears.
        var g = Build(Seg(900, 0, 1000, 1, "K"), Seg(1000, 1, 1100, 2, "K"), Seg(1100, 2, 1100, 60, "M6"));

        Assert.False(ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 1), nameFilter: "K").IsMeasured);
    }

    // --- Task 3b round 3, T2: a Y whose two arms leave from one runway node ----------------------

    [Fact]
    public void The_forward_arm_of_a_Y_whose_arms_share_one_runway_node_is_its_sibling()
    {
        // The KMIA 08R Z / ULWB 33 shape. Both arms of "Y" leave the runway from J (1000,0): the backward
        // arm through (975,20) to (960,40), the forward arm through (1030,28) - already past the 25 m
        // half-width - to (1045,45). A connector along 40-45 m joins their outer ends.
        var g = Build(
            Seg(1000, 0, 975, 20, "Y"), Seg(975, 20, 960, 40, "Y"),
            Seg(1000, 0, 1030, 28, "Y"), Seg(1030, 28, 1045, 45, "Y"),
            Seg(960, 40, 1000, 42, "Y"), Seg(1000, 42, 1045, 45, "Y"));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 975, 20), nameFilter: "Y");
        Assert.Equal(NodeAt(g, 1000, 0), backward.JunctionNodeId);
        Assert.True(backward.IsTurnaround);

        var sibling = ExitBranch.FindForwardSibling(g, Axis, backward, "Y");

        Assert.NotNull(sibling);
        Assert.Equal(NodeAt(g, 1000, 0), sibling!.JunctionNodeId);
        Assert.Equal(NodeAt(g, 1030, 28), sibling.Path[1]);   // out along the forward arm, not the backward one
        Assert.False(sibling.IsTurnaround);
    }

    [Fact]
    public void A_sibling_that_leaves_the_pavement_forward_and_hooks_back_beyond_the_edge_is_accepted()
    {
        // Review Minor 2, pinning S2 inside the sibling check. M6's backward arm meets the stem at
        // (1000,45); its unnamed forward arm leaves the runway at 34 degrees to (960,28), past the 25 m
        // half-width, then hooks back 138 degrees to (950,37), past the clear line, before joining the
        // stem. Judged by its turn to the clear line (138) it was rejected; it leaves the pavement at 34.
        var g = Build(
            Seg(918, 0, 960, 28), Seg(960, 28, 950, 37), Seg(950, 37, 1000, 45),
            Seg(1072, 1, 1030, 16, "M6"), Seg(1030, 16, 1015, 32, "M6"), Seg(1015, 32, 1000, 45, "M6"),
            Seg(1072, 1, 1104, -1, "M6"), Seg(1000, 45, 1000, 85, "M6"));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1030, 16), nameFilter: "M6");
        Assert.True(backward.IsTurnaround);

        var sibling = ExitBranch.FindForwardSibling(g, Axis, backward, "M6");

        Assert.NotNull(sibling);
        Assert.Equal(NodeAt(g, 918, 0), sibling!.JunctionNodeId);
        Assert.True(sibling.TurnToClearDeg > RolloutExitGate.TurnaroundAboveDeg);
        Assert.InRange(sibling.TurnToLeaveDeg, 32.0, 36.0);
        Assert.False(sibling.IsTurnaround);
    }

    // --- PR #252 review fixes (2026-09-26) ---------------------------------------------------------

    [Fact]
    public void A_two_metre_jog_where_the_lead_in_meets_the_centreline_does_not_set_the_branch_angle()
    {
        // KPIT 28L F5 (fs2024, nodes 1888-1873): a 2.1 m row turning 50.8° joins the exit's first 56 m
        // segment to its centreline node; every other segment turns at most 21.4°. Read edge by edge the
        // jog made this rapid exit "Normal 50.8°" — too fast above 30 kt, no 900 ft call, no early handoff.
        // Along / lateral from the fs2024 rows, lateral right = -north in this frame.
        var g = Build(
            Seg(2015.7, -0.4, 2017.1, -2.0, "F5"),
            Seg(2017.1, -2.0, 2073.2, -1.8, "F5"),
            Seg(2073.2, -1.8, 2105.5, -3.7, "F5"),
            Seg(2105.5, -3.7, 2145.0, -7.9, "F5"),
            Seg(2145.0, -7.9, 2192.4, -16.8, "F5"),
            Seg(2192.4, -16.8, 2237.4, -29.7, "F5"),
            Seg(2237.4, -29.7, 2285.1, -48.5, "F5"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 2015.7, -0.4), null, "F5");
        Assert.True(b.IsMeasured);
        Assert.False(b.IsTurnaround);
        Assert.InRange(b.TurnToClearDeg, 20.5, 22.5);   // the real curve, never the jog's 50.8°
        Assert.InRange(b.TurnToLeaveDeg, 15.0, 17.0);
    }

    [Fact]
    public void A_node_whose_own_arm_is_blocked_is_never_measured_on_the_other_side()
    {
        // Review V3-c: "X" crosses at J(1000,0) and runs 60 m out on the LEFT; on the RIGHT, J-C(995,20)-
        // D(995,30) is "X" and D-E(995,70) is "Y". C's own arm stops at D (the name changes), and measured
        // from the junction without a side it took the left arm's angle, bearing and side "Left" for a node
        // 20 m right of the centreline. Its own side has no way off on its own taxiway: unmeasured, so the
        // exit keeps the producer's own reading (C->D, right).
        var g = Build(Seg(1000, 0, 1000, 60, "X"), Seg(1000, 0, 995, -20, "X"),
            Seg(995, -20, 995, -30, "X"), Seg(995, -30, 995, -70, "Y"));
        var c = ExitBranch.Analyze(g, Axis, NodeAt(g, 995, -20), null, "X");
        Assert.False(c.IsMeasured);
    }

    [Fact]
    public void A_node_already_off_the_pavement_is_never_read_as_leaving_backward()
    {
        // 0D7 27 (fs2024, nodes 1-10): a taxi line drawn along a 7.6 m half-width runway humps out to a
        // hold-short node 15.9 m right, where a parking lead-in leaves at 91°. The walk in chose the
        // hump's downfield side by 0.5 m of lateral and read 178° back from there - a turnaround, which
        // cost the runway its only exit. Nothing of its own clears the runway: unmeasured, as before.
        var axis = new RunwayAxis(0.0, 0.0, 90.0, 7.6);
        var g = Build(
            Seg(287.6, -2.3, 292.2, -5.2), Seg(292.2, -5.2, 312.1, -5.5), Seg(312.1, -5.5, 319.2, -10.0),
            Seg(319.2, -10.0, 323.1, -15.9), Seg(323.1, -15.9, 328.1, -9.5), Seg(328.1, -9.5, 335.2, -5.6),
            Seg(335.2, -5.6, 353.1, -4.9), Seg(353.1, -4.9, 359.5, -1.5),
            new TaxiPath
            {
                Type = "P", Width = 98.0, Name = "",
                StartLat = -15.9 / M_PER_DEG, StartLon = 323.1 / M_PER_DEG,
                EndLat = -39.7 / M_PER_DEG, EndLon = 322.7 / M_PER_DEG,
            });
        var b = ExitBranch.Analyze(g, axis, NodeAt(g, 323.1, -15.9), null, "");
        Assert.False(b.IsMeasured);
    }

    [Fact]
    public void A_branch_off_the_pavement_never_walks_back_over_the_runway()
    {
        // GMMN 17L A (fs2024, nodes 660-699, 275, 274): the lead line leaves LEFT and meets taxiway A 27 m out,
        // where A carries on left to 150 m AND crosses back over the runway to the right. The crossing was
        // 4.8 m shorter, so the branch cleared on the RIGHT - its clear node, corridor node and bearing, and
        // "turn right" for an exit that turns off left. North = LEFT in this frame.
        var g = Build(
            Seg(1000, 0, 1065, 0, "A"), Seg(1065, 0, 1077, 7, "A"), Seg(1077, 7, 1085, 27, "A"),
            Seg(1085, 27, 1086, 160, "A"),
            Seg(1085, 27, 1086, -3, "A"), Seg(1086, -3, 1085, -100));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), null, "A");
        Assert.True(b.IsMeasured);
        Assert.False(b.IsTurnaround);
        Assert.True(g.Nodes[b.ClearNodeId].Latitude > 0);      // cleared on its own, left, side
        Assert.True(g.Nodes[b.CorridorNodeId].Latitude > 0);
    }

    [Fact]
    public void The_corridor_node_lies_along_the_clear_nodes_own_arm()
    {
        // KACK 24 A (fs2024, nodes 238-172, 291): the exit cleared on its LEFT arm while the first node past
        // the corridor line was on a RIGHT arm reached sooner, and the bearing's chord to it said "Right" for
        // an exit to the left. North = LEFT in this frame.
        var g = Build(
            Seg(1000, 0, 1075, 17, "A"), Seg(1075, 17, 1094, 36, "A"), Seg(1094, 36, 1180, 39, "A"),
            Seg(1180, 39, 1190, 50, "A"),
            Seg(1000, 0, 1100, -10, "A"), Seg(1100, -10, 1110, -55, "A"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), null, "A");
        Assert.True(b.IsMeasured);
        Assert.True(g.Nodes[b.ClearNodeId].Latitude > 0);
        Assert.True(g.Nodes[b.CorridorNodeId].Latitude > 0);   // on the clear node's own, left, arm
    }

    [Fact]
    public void A_short_last_stretch_is_read_with_the_stretch_before_it()
    {
        // A 90° exit whose clear node sits 2.5 m past a jog that, read alone, turns 99° (longer than
        // the graph's 1.5 m node merge, shorter than a stroke).
        var g = Build(Seg(1000, 0, 1000, -34.5, "K"), Seg(1000, -34.5, 999.6, -37.0, "K"));
        var b = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0), null, "K");
        Assert.True(b.IsMeasured);
        Assert.InRange(b.TurnToClearDeg, 89.0, 91.0);
    }

    [Fact]
    public void A_sibling_arm_named_only_beyond_its_own_clear_point_is_rejected()
    {
        // A second, unrelated arm reaches the runway at (945,0): unnamed from the runway up through
        // (945,20) to (945,45) — its own first node beyond the 35 m clear boundary — then NAMED "B4"
        // out to (945,65), then an unnamed bridge back to (915,80) and finally to Z9's own clear node
        // (915,85). The near-runway portion (runway .. first-beyond-clear node) is entirely unnamed,
        // so a check truncated at the clear boundary sees nothing wrong; only checking the WHOLE arm
        // out to the point the search actually started from (its "start") crosses the "B4" edge.
        var g = Build(
            Seg(1000, 0, 915, 85, "Z9"),
            Seg(915, 85, 915, 80, ""),
            Seg(915, 80, 945, 65, ""),
            Seg(945, 65, 945, 45, "B4"),
            Seg(945, 45, 945, 20, ""),
            Seg(945, 20, 945, 0, ""));
        var backward = ExitBranch.Analyze(g, Axis, NodeAt(g, 1000, 0));
        Assert.True(backward.IsTurnaround);
        Assert.Null(ExitBranch.FindForwardSibling(g, Axis, backward, "Z9"));
    }
}
