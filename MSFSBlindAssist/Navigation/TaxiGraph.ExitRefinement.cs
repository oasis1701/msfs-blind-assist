using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

// The refinement step every LandingExit producer applies — GetLandingExits' main and fallback passes
// and FindDownfieldExits — so an exit is judged by its whole branch (ExitBranch), not by whichever
// short segment carries the taxiway name. docs/taxi-guidance.md, "Exits measured by branch".
public partial class TaxiGraph
{
    /// <summary>
    /// Refines a candidate exit by its branch. Unmeasured branches (nothing clears the runway within
    /// reach) leave the exit exactly as it was, so thin navdata can never lose an exit here.
    /// A turnaround is replaced by its forward sibling when one exists; otherwise it is recorded as a
    /// turnaround (130°, "End") — or dropped when <paramref name="dropTurnarounds"/> (the rescue scan).
    /// A forward branch gets its sharpest turn to clear (capped at 90°) and, unless
    /// <paramref name="keepNode"/> (hold-short-anchored exits), moves to its junction.
    /// </summary>
    private LandingExit? RefineExitByBranch(
        LandingExit exit, int? seedNeighborId, bool keepNode, bool dropTurnarounds,
        Runway rwy, RunwayAxis axis, double minDistanceFromThresholdFeet, out bool measured)
    {
        var branch = ExitBranch.Analyze(this, axis, exit.NodeId, seedNeighborId);
        measured = branch.IsMeasured;
        if (!measured) return exit;

        if (branch.IsTurnaround)
        {
            var sibling = ExitBranch.FindForwardSibling(this, axis, branch, exit.TaxiwayName);
            if (sibling != null)
                return ExitAtJunction(sibling, exit.TaxiwayName, exit.ApronNodeId, rwy, axis, minDistanceFromThresholdFeet);
            if (dropTurnarounds) return null;
            exit.ExitAngleDegrees = RolloutExitGate.TurnaroundExitAngleDeg;
            exit.ExitType = "End";
            return exit;
        }

        double angle = Math.Min(branch.TurnToClearDeg, RolloutExitGate.MaxUsableExitTurnDeg);
        if (keepNode)
        {
            exit.ExitAngleDegrees = angle;
            double alongFt = axis.Project(exit.Latitude, exit.Longitude).AlongMetres / 0.3048;
            exit.ExitType = ClassifyExit(angle, alongFt, rwy.Length);
            return exit;
        }
        return ExitAtJunction(branch, exit.TaxiwayName, exit.ApronNodeId, rwy, axis, minDistanceFromThresholdFeet);
    }

    /// <summary>
    /// The exit at <paramref name="branch"/>'s junction, or null when that junction fails the same
    /// distance rules every exit does (under 500 ft past the landing threshold, within 50 ft of the
    /// pavement end, or not beyond <paramref name="minDistanceFromThresholdFeet"/>).
    /// </summary>
    private LandingExit? ExitAtJunction(
        LandingExitBranch branch, string name, int fallbackApronNodeId,
        Runway rwy, RunwayAxis axis, double minDistanceFromThresholdFeet)
    {
        const double MIN_DIST_FT = 500.0;
        const double END_BUFFER_FT = 50.0;
        const double TOUCHDOWN_AIM_FT = 1000.0;

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
    // forward (≤ 110°). Due north is stored as 360 so 0 keeps meaning "unknown".
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
            if (chordRel <= 110.0 && chordRel > firstRel) bearing = chord;
        }
        return bearing == 0.0 ? 360.0 : bearing;
    }

    // The existing classification thresholds (HIGH_SPEED_MAX_DEG 50, NORMAL_MAX_DEG 110, END_RATIO 0.85).
    private static string ClassifyExit(double angleDeg, double alongFt, double runwayLengthFt)
    {
        if (runwayLengthFt > 0 && alongFt / runwayLengthFt > 0.85) return "End";
        if (angleDeg <= 50.0) return "High-speed";
        if (angleDeg <= 110.0) return "Normal";
        return "End";
    }
}
