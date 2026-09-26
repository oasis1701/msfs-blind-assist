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
}
