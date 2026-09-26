using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

// The refinement step every LandingExit producer applies — GetLandingExits' main and fallback passes
// and FindDownfieldExits — so an exit is judged by its whole branch (ExitBranch), not by whichever
// short segment carries the taxiway name. docs/taxi-guidance.md, "Exits measured by branch".
public partial class TaxiGraph
{
    /// <summary>
    /// Refines a candidate exit by its branch. The inward walk that finds the branch stays on the
    /// exit's own taxiway (<see cref="ExitBranch.Analyze"/>'s name filter; unnamed edges only for an
    /// unnamed exit), so a node shared with another exit is never measured along that exit's arm
    /// (KATL 26L: B4 was listed as a copy of E3); a candidate whose own taxiway never reaches the
    /// runway is unmeasured.
    /// <para>Unmeasured branches (nothing clears the runway within reach) leave the exit exactly as it
    /// was, so thin navdata can never lose an exit here.</para>
    /// <para>A turnaround is replaced by its forward sibling when one exists and the sibling's junction
    /// passes the distance rules (<see cref="ExitAtJunction"/>); that substitution relocates the exit
    /// even when <paramref name="keepNode"/> is set. Otherwise it is recorded as the turnaround it is
    /// (130°, "End") at its own node - never dropped from the planner list (worldwide sweep,
    /// 2026-09-26: dropping it emptied 111 runway directions' lists, e.g. 0KS5 09) - except that
    /// <paramref name="dropTurnarounds"/> (the rescue scan) drops it.</para>
    /// <para>A forward branch gets its sharpest turn to clear (capped at 90°) and, unless
    /// <paramref name="keepNode"/> (hold-short-anchored exits), moves to its junction. Relocation
    /// never removes an exit: where the junction fails a distance rule the exit's own node passed
    /// (MIN_DIST_FT, END_BUFFER_FT, or the rescue scan's
    /// <paramref name="minDistanceFromThresholdFeet"/>), the exit keeps its own node with the
    /// refined angle and type - the keepNode behaviour (KMTC 19 B and LSGL 18 L, whose lead-ins
    /// start under 500 ft, were dropped).</para>
    /// </summary>
    private LandingExit? RefineExitByBranch(
        LandingExit exit, int? seedNeighborId, bool keepNode, bool dropTurnarounds,
        Runway rwy, RunwayAxis axis, double minDistanceFromThresholdFeet, out bool measured)
    {
        var branch = ExitBranch.Analyze(this, axis, exit.NodeId, seedNeighborId, exit.TaxiwayName ?? "");
        measured = branch.IsMeasured;
        if (!measured) return exit;

        if (branch.IsTurnaround)
        {
            var sibling = ExitBranch.FindForwardSibling(this, axis, branch, exit.TaxiwayName);
            var atSibling = sibling == null ? null
                : ExitAtJunction(sibling, exit.TaxiwayName, exit.ApronNodeId, rwy, axis, minDistanceFromThresholdFeet);
            if (atSibling != null) return atSibling;
            if (dropTurnarounds) return null;
            exit.ExitAngleDegrees = RolloutExitGate.TurnaroundExitAngleDeg;
            exit.ExitType = "End";
            return exit;
        }

        double angle = Math.Min(branch.TurnToClearDeg, RolloutExitGate.MaxUsableExitTurnDeg);
        if (!keepNode)
        {
            var atJunction = ExitAtJunction(branch, exit.TaxiwayName, exit.ApronNodeId, rwy, axis, minDistanceFromThresholdFeet);
            if (atJunction != null) return atJunction;
            // The junction fails a distance rule the exit's own node passed: keep the node.
        }
        exit.ExitAngleDegrees = angle;
        double alongFt = axis.Project(exit.Latitude, exit.Longitude).AlongMetres / 0.3048;
        exit.ExitType = ClassifyExit(angle, alongFt, rwy.Length);
        return exit;
    }

    /// <summary>
    /// The exit at <paramref name="branch"/>'s junction, or null when that junction fails the same
    /// distance rules every exit does (closer than MIN_DIST_FT, 500 ft, past the landing threshold,
    /// within END_BUFFER_FT, 50 ft, of the pavement end, or not beyond
    /// <paramref name="minDistanceFromThresholdFeet"/>).
    /// </summary>
    private LandingExit? ExitAtJunction(
        LandingExitBranch branch, string name, int fallbackApronNodeId,
        Runway rwy, RunwayAxis axis, double minDistanceFromThresholdFeet)
    {
        if (!Nodes.TryGetValue(branch.JunctionNodeId, out var junction)) return null;
        double alongFt = axis.Project(junction.Latitude, junction.Longitude).AlongMetres / 0.3048;
        double distFromThresholdFt = alongFt - rwy.ThresholdOffset;
        if (distFromThresholdFt < MIN_DIST_FT || alongFt > rwy.Length - END_BUFFER_FT) return null;
        if (distFromThresholdFt <= minDistanceFromThresholdFeet) return null;

        double angle = Math.Min(branch.TurnToClearDeg, RolloutExitGate.MaxUsableExitTurnDeg);
        double bearing = BranchExitBearing(branch, rwy.Heading);
        return new LandingExit
        {
            NodeId = junction.NodeId,
            ApronNodeId = branch.CorridorNodeId > 0 ? branch.CorridorNodeId : fallbackApronNodeId,
            Latitude = junction.Latitude,
            Longitude = junction.Longitude,
            DistanceFromThresholdFeet = distFromThresholdFt,
            DistanceFromTouchdownFeet = distFromThresholdFt - TOUCHDOWN_AIM_FT,
            TaxiwayName = name,
            ExitAngleDegrees = angle,
            ExitBearingTrue = bearing,
            ExitType = ClassifyExit(angle, alongFt, rwy.Length),
            ExitSide = bearing != 0.0
                ? (NormalizeAngle((bearing == 360.0 ? 0.0 : bearing) - rwy.Heading) >= 0 ? "Right" : "Left")
                : "",
        };
    }

    // ExitBearingTrue by the existing rule, evaluated at the junction: the branch's first edge, replaced
    // by the junction→corridor-node chord when the first edge is under 20° and the chord is wider and
    // forward (≤ NORMAL_MAX_DEG, 110°, the producers' own apron-override guard). Due north is stored
    // as 360 so 0 keeps meaning "unknown".
    private double BranchExitBearing(LandingExitBranch branch, double rwyHeadingTrue)
    {
        if (branch.Path.Count < 2) return 0.0;
        var a = Nodes[branch.Path[0]];
        var b = Nodes[branch.Path[1]];
        double first = NavigationCalculator.CalculateBearing(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        double firstRel = Math.Abs(NormalizeAngle(first - rwyHeadingTrue));
        double bearing = first;
        if (firstRel < 20.0 && branch.CorridorNodeId > 0 && Nodes.TryGetValue(branch.CorridorNodeId, out var k))
        {
            double chord = NavigationCalculator.CalculateBearing(a.Latitude, a.Longitude, k.Latitude, k.Longitude);
            double chordRel = Math.Abs(NormalizeAngle(chord - rwyHeadingTrue));
            if (chordRel <= NORMAL_MAX_DEG && chordRel > firstRel) bearing = chord;
        }
        return bearing == 0.0 ? 360.0 : bearing;
    }

    // The ONE exit classification rule, used by every producer (GetLandingExits' main and fallback
    // passes, FindDownfieldExits, and the refinement above) with the class-scope thresholds: past
    // END_RATIO of the runway is always End; otherwise High-speed up to HIGH_SPEED_MAX_DEG, Normal up
    // to NORMAL_MAX_DEG, End beyond. The Length guard is moot for the producers, which all return
    // early on Length <= 0.
    private static string ClassifyExit(double angleDeg, double alongFt, double runwayLengthFt)
    {
        if (runwayLengthFt > 0 && alongFt / runwayLengthFt > END_RATIO) return "End";
        if (angleDeg <= HIGH_SPEED_MAX_DEG) return "High-speed";
        if (angleDeg <= NORMAL_MAX_DEG) return "Normal";
        return "End";
    }
}
