// Hold-short-anchored landing exits and the hold-short gate, judged by their whole branch
// (ExitBranch, via TaxiGraph.RefineExitByBranch). The KMEM 36L fixture cannot reach these paths:
// its four hold-short nodes sit 83-85 m out, beyond the 40 m corridor, so every KMEM exit is a
// geometry-found one.
//
// Fixture frame, as in ExitBranchTests: a due-east runway on the equator, threshold at (0,0),
// 164 ft wide (half-width 25.0 m, clear boundary 35.0 m, corridor 40.0 m). Along-runway metres =
// longitude * 111132 and NORTH metres = latitude * 111132 (north = LEFT of the landing direction).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitHoldShortBranchTests
{
    private const double M_PER_DEG = 111132.0;

    private static Runway Runway09(double lengthM) => new()
    {
        RunwayID = "09", StartLat = 0.0, StartLon = 0.0, Heading = 90.0,
        Length = lengthM / 0.3048, Width = 164.0, ThresholdOffset = 0.0,
    };

    private static TaxiPath Seg(double a1, double n1, double a2, double n2, string name = "", string startType = "", string endType = "") => new()
    {
        Type = "T", Width = 98.0, Name = name, StartType = startType, EndType = endType,
        StartLat = n1 / M_PER_DEG, StartLon = a1 / M_PER_DEG,
        EndLat = n2 / M_PER_DEG, EndLon = a2 / M_PER_DEG,
    };

    private static TaxiGraph Build(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static TaxiNode NodeAt(TaxiGraph g, double along, double north)
        => g.Nodes.Values.OrderBy(n =>
               Math.Abs(n.Longitude * M_PER_DEG - along) + Math.Abs(n.Latitude * M_PER_DEG - north))
           .First();

    // A 1,200 m runway (3,937 ft; its last 15% starts at 1,020 m) in hold-short mode, every hold-short
    // node 36 m out, inside the corridor:
    //   B  a plain 90-degree exit at 400 m;
    //   C  a stub peeling BACK 119 degrees from 700 m, with no other arm;
    //   A  a curved fillet from a junction at 1,000 m (83% of the runway) whose segments turn 11, 41
    //      and 72 degrees to its hold-short node at 1,041 m (87%), then straight out at 90 degrees.
    private static TaxiGraph HoldShortRunway() => Build(
        Seg(400, 0, 400, 36, "B", endType: "HSND"), Seg(400, 36, 400, 80, "B"),
        Seg(700, 0, 680, 36, "C", endType: "HSND"), Seg(680, 36, 640, 80, "C"),
        Seg(1000, 0, 1020, 4, "A"), Seg(1020, 4, 1035, 17, "A"),
        Seg(1035, 17, 1041, 36, "A", endType: "HSND"), Seg(1041, 36, 1041, 80, "A"));

    [Fact]
    public void A_hold_short_exit_keeps_its_node_takes_its_branch_turn_and_is_typed_where_it_stands()
    {
        var g = HoldShortRunway();
        var a = g.GetLandingExits(Runway09(1200.0)).Single(e => e.TaxiwayName == "A");
        var hold = NodeAt(g, 1041, 36);

        Assert.Equal(hold.NodeId, a.NodeId);
        Assert.Equal(hold.Latitude, a.Latitude);
        Assert.Equal(hold.Longitude, a.Longitude);
        // The fillet's sharpest turn to clear (72.5 degrees) - not the hold-short node's own
        // 90-degree edge, which is what the node alone would have said.
        Assert.InRange(a.ExitAngleDegrees, 71.5, 73.5);
        // Typed at the hold-short node, 87% down the runway; typed at its junction (83%) it
        // would read Normal.
        Assert.Equal("End", a.ExitType);
    }

    [Fact]
    public void A_hold_short_turnaround_with_no_forward_arm_is_recorded_as_a_130_degree_end_exit()
    {
        var g = HoldShortRunway();
        var c = g.GetLandingExits(Runway09(1200.0)).Single(e => e.TaxiwayName == "C");

        Assert.Equal(NodeAt(g, 680, 36).NodeId, c.NodeId);
        Assert.Equal(RolloutExitGate.TurnaroundExitAngleDeg, c.ExitAngleDegrees);
        Assert.Equal("End", c.ExitType);
    }

    [Fact]
    public void A_hold_short_node_on_a_Y_stem_keeps_the_runway_in_hold_short_mode()
    {
        // The only hold-short node inside the corridor sits 38 m out on the stem of Y-shaped exit
        // "Y". Its backward arm (named, junction downfield at 2,050 m, a 158-degree turn) is nearer
        // the centreline, so the unseeded inward walk takes it; the forward arm (unnamed, junction at
        // 1,930 m) is its sibling. D is an unmarked 90-degree taxiway at 1,000 m.
        // The observable difference: in hold-short mode the unmarked taxiway D is not listed; had the
        // gate judged the stem by its backward arm alone, the runway would have fallen back to
        // geometry mode and listed D as well.
        var g = Build(
            Seg(2000, 38, 2000, 90, "Y", startType: "HSND"),
            Seg(2050, 0, 2030, 8, "Y"), Seg(2030, 8, 2000, 38, "Y"),
            Seg(1930, 0, 1965, 12), Seg(1965, 12, 2000, 38),
            Seg(1000, 0, 1000, 90, "D"));

        var exits = g.GetLandingExits(Runway09(3000.0));

        Assert.Equal(new[] { "Y" }, exits.Select(e => e.TaxiwayName).ToArray());
    }
}
