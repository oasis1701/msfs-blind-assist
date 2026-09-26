using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

// The refinement step every LandingExit producer applies — GetLandingExits' main and fallback passes
// and FindDownfieldExits — so an exit is judged by its whole branch (ExitBranch), not by whichever
// short segment carries the taxiway name. docs/taxi-guidance.md, "Exits measured by branch".
public partial class TaxiGraph
{
    /// <summary>
    /// Refines a candidate exit by its branch. The whole measurement stays on the exit's own taxiway
    /// (<see cref="ExitBranch.Analyze"/>'s name filter; unnamed edges only for an unnamed exit): the
    /// inward walk, so a node shared with another exit is never walked in along that exit's arm (KATL
    /// 26L: B4 was listed as a copy of E3), and the outward search, so a lead line another exit crosses
    /// is never measured out along that exit's arm (KMIA 08R: M7 took M6's 90° crossing).
    /// <para>Unmeasured branches (the exit's own taxiway never reaches or never clears the runway within
    /// reach) leave the exit exactly as it was, so thin navdata can never lose an exit here.</para>
    /// <para>A FORWARD exit keeps its own node - NodeId, position and distances unchanged, for every
    /// producer - and takes the branch's angle (its sharpest turn to clear, capped at 90°), its type
    /// (classified where the exit stands) and its bearing and side (<see cref="BranchExitBearing"/>;
    /// the producer's bearing can be a lead line's or a hold-short node's BACKWARD edge, and after "turn
    /// now" the rollout steers a Normal exit by its bearing). It is never moved to its junction: the
    /// worldwide sweep (2026-09-26) found lead-in starts up to 150 m before the turn-off (KMIA 08R Z),
    /// which put "turn now" hundreds of feet early, and moving exits back pushed distinct same-name
    /// turnoffs inside the coverage window.</para>
    /// <para>A TURNAROUND (<see cref="LandingExitBranch.IsTurnaround"/>, judged by how the branch leaves
    /// the runway pavement) is replaced by its forward sibling when one exists and the sibling's
    /// junction passes the distance rules (<see cref="ExitAtJunction"/>) - the one case in which an exit
    /// moves. Otherwise it is recorded as the turnaround it is (130°, "End") at its own node - never
    /// dropped from the planner list (dropping it emptied 111 runway directions' lists, e.g. 0KS5 09) -
    /// except that <paramref name="dropTurnarounds"/> (the rescue scan) drops it.</para>
    /// </summary>
    private LandingExit? RefineExitByBranch(
        LandingExit exit, int? seedNeighborId, bool dropTurnarounds,
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
        double alongFt = axis.Project(exit.Latitude, exit.Longitude).AlongMetres / 0.3048;
        exit.ExitAngleDegrees = angle;
        exit.ExitType = ClassifyExit(angle, alongFt, rwy.Length);
        exit.ExitBearingTrue = BranchExitBearing(branch, rwy.Heading);
        exit.ExitSide = ExitSideFor(exit.ExitBearingTrue, rwy.Heading);
        return exit;
    }

    /// <summary>
    /// The exit at a forward SIBLING's junction - the one case in which the refinement moves an exit (a
    /// turnaround replaced by its Y's other arm) - or null when that junction fails the same distance
    /// rules every exit does (closer than MIN_DIST_FT, 500 ft, past the landing threshold, within
    /// END_BUFFER_FT, 50 ft, of the pavement end, or not beyond
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
            ExitSide = ExitSideFor(bearing, rwy.Heading),
        };
    }

    // "Right"/"Left" of the landing heading for an ExitBearingTrue, "" for the 0.0 "unknown" sentinel -
    // the producers' own side rule.
    private static string ExitSideFor(double bearingTrue, double rwyHeadingTrue)
        => bearingTrue != 0.0
            ? (NormalizeAngle((bearingTrue == 360.0 ? 0.0 : bearingTrue) - rwyHeadingTrue) >= 0 ? "Right" : "Left")
            : "";

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
