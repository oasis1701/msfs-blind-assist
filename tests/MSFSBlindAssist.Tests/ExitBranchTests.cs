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
