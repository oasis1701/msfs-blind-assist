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
    /// (classified where the exit stands) and its bearing and side, measured where the exit stands
    /// (<see cref="BranchExitBearing"/> at <see cref="PlaceOf"/>; the producer's bearing can be a
    /// lead line's or a hold-short node's BACKWARD edge, and after "turn now" the rollout steers a
    /// Normal exit by its bearing). It is never moved to its junction: that is often its lead-in start,
    /// up to the 150 m of lead line R1 follows before the turn-off (worldwide sweep, 2026-09-26: KMIA
    /// 08R Z at 2,073 ft for a RET leaving the centreline at 2,369), which put "turn now" early, and
    /// moving exits back pushed distinct same-name turnoffs inside the coverage window.</para>
    /// <para>A TURNAROUND (<see cref="LandingExitBranch.IsTurnaround"/>, judged by how the branch leaves
    /// the runway pavement) is replaced by its forward sibling when one exists and the sibling's
    /// divergence node passes the distance rules (<see cref="SiblingExit"/>) - the one case in which an
    /// exit moves, and then to where the sibling arm leaves the centreline, not to its lead-in start.
    /// Otherwise it is recorded as the turnaround it is (130°, "End") at its own node - never
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
                : SiblingExit(sibling, exit.TaxiwayName, exit.ApronNodeId, rwy, axis, minDistanceFromThresholdFeet);
            if (atSibling != null) return atSibling;
            if (dropTurnarounds) return null;
            exit.ExitAngleDegrees = RolloutExitGate.TurnaroundExitAngleDeg;
            exit.ExitType = "End";
            return exit;
        }

        double angle = Math.Min(branch.TurnToClearDeg, RolloutExitGate.MaxUsableExitTurnDeg);
        double alongFt = axis.Project(exit.Latitude, exit.Longitude).AlongMetres / 0.3048;
        var place = PlaceOf(branch, exit.NodeId, axis);
        exit.ExitAngleDegrees = angle;
        exit.DivergenceAngleDegrees = DivergenceAt(branch, axis, place, angle);
        exit.ExitType = ClassifyExit(angle, alongFt, rwy.Length);
        exit.ExitBearingTrue = BranchExitBearing(branch, rwy.Heading, place);
        exit.ExitSide = ExitSideFor(exit.ExitBearingTrue, rwy.Heading);
        return exit;
    }

    // Where a kept exit STANDS on its branch - its own node's index in the path, which always reaches the
    // candidate (ExitBranch.Analyze) - and which way its heading is read there: onward from it while it is
    // on the runway pavement, INTO it once it is off it. From the junction instead - often a lead-in start up
    // to 150 m behind the node - the stretch onward is the lead line's own, and the bearing collapsed to the
    // lead line's shallow chord (review, 2026-09-26: a 90° exit read -31°, KMEM M8 +43° for about +54°), and
    // after "turn now" the rollout steers a Normal exit by it. A node already OFF the pavement was reached
    // turning off it: the stretch onward can run back along a parallel taxiway (OI19 11: a hold-short node
    // reached turning 91° right read 180°, and its spoken side flipped to Left). A node that is not on the
    // path at all - the junction fallback measured the branch its lead-in line joins - stands where that
    // branch leaves the centreline (DivergenceIndex), never at the junction.
    private (int Index, bool Into) PlaceOf(LandingExitBranch branch, int nodeId, RunwayAxis axis)
    {
        for (int i = 0; i < branch.Path.Count; i++)
        {
            if (branch.Path[i] != nodeId) continue;
            var n = Nodes[nodeId];
            bool offPavement = Math.Abs(axis.Project(n.Latitude, n.Longitude).LateralMetres) > axis.HalfWidthMetres;
            return (i, i > 0 && (offPavement || i == branch.Path.Count - 1));
        }
        return (DivergenceIndex(branch, axis), false);
    }

    /// <summary>
    /// The exit on a forward SIBLING - the one case in which the refinement moves an exit (a turnaround
    /// replaced by its Y's other arm). It lands at the sibling's DIVERGENCE node, where the arm leaves
    /// the centreline band (<see cref="DivergenceIndex"/>), never at its junction: the junction is the
    /// arm's lead-in start, at the far end of the up to 150 m of lead line R1 follows, which put "turn
    /// now" that much early (worldwide sweep, 2026-09-26: KMIA 08R Z at 2,073 ft, its lead-line start, for
    /// a RET leaving the centreline at 2,369). Null when that node fails the same
    /// distance rules every exit does (closer than MIN_DIST_FT, 500 ft, past the landing threshold,
    /// within END_BUFFER_FT, 50 ft, of the pavement end, or not beyond
    /// <paramref name="minDistanceFromThresholdFeet"/>).
    /// </summary>
    private LandingExit? SiblingExit(
        LandingExitBranch branch, string name, int fallbackApronNodeId,
        Runway rwy, RunwayAxis axis, double minDistanceFromThresholdFeet)
    {
        int at = DivergenceIndex(branch, axis);
        if (!Nodes.TryGetValue(branch.Path[at], out var node)) return null;
        double alongFt = axis.Project(node.Latitude, node.Longitude).AlongMetres / 0.3048;
        double distFromThresholdFt = alongFt - rwy.ThresholdOffset;
        if (distFromThresholdFt < MIN_DIST_FT || alongFt > rwy.Length - END_BUFFER_FT) return null;
        if (distFromThresholdFt <= minDistanceFromThresholdFeet) return null;

        double angle = Math.Min(branch.TurnToClearDeg, RolloutExitGate.MaxUsableExitTurnDeg);
        double bearing = BranchExitBearing(branch, rwy.Heading, (at, false));
        return new LandingExit
        {
            NodeId = node.NodeId,
            ApronNodeId = branch.CorridorNodeId > 0 ? branch.CorridorNodeId : fallbackApronNodeId,
            Latitude = node.Latitude,
            Longitude = node.Longitude,
            DistanceFromThresholdFeet = distFromThresholdFt,
            DistanceFromTouchdownFeet = distFromThresholdFt - TOUCHDOWN_AIM_FT,
            TaxiwayName = name,
            ExitAngleDegrees = angle,
            DivergenceAngleDegrees = DivergenceAt(branch, axis, (at, false), angle),
            ExitBearingTrue = bearing,
            ExitType = ClassifyExit(angle, alongFt, rwy.Length),
            ExitSide = ExitSideFor(bearing, rwy.Heading),
        };
    }

    // How steeply the branch leaves the exit's own node (LandingExit.DivergenceAngleDegrees): the heading
    // of its stroke where the exit stands, never above the branch's own angle.
    private double DivergenceAt(LandingExitBranch branch, RunwayAxis axis, (int Index, bool Into) place, double angle)
    {
        var (from, to) = ExitBranch.StrokeAt(this, branch.Path, place.Index, place.Into);
        if (from == to) return angle;
        var a = Nodes[branch.Path[from]];
        var b = Nodes[branch.Path[to]];
        return Math.Min(Math.Abs(axis.RelativeHeadingDeg(a.Latitude, a.Longitude, b.Latitude, b.Longitude)), angle);
    }

    // "Right"/"Left" of the landing heading for an ExitBearingTrue, "" for the 0.0 "unknown" sentinel -
    // the producers' own side rule.
    private static string ExitSideFor(double bearingTrue, double rwyHeadingTrue)
        => bearingTrue != 0.0
            ? (NormalizeAngle((bearingTrue == 360.0 ? 0.0 : bearingTrue) - rwyHeadingTrue) >= 0 ? "Right" : "Left")
            : "";

    // Index in the branch's path of its DIVERGENCE node: the last node, counting from the junction
    // outward, before the path first leaves the ExitBranch.CenterlineBandMetres band - where the arm
    // leaves the centreline. The junction itself when the next node is already out of the band (KMEM
    // M6's 36L arm) or the junction is out of it.
    private int DivergenceIndex(LandingExitBranch branch, RunwayAxis axis)
    {
        for (int i = 0; i < branch.Path.Count; i++)
        {
            var n = Nodes[branch.Path[i]];
            if (Math.Abs(axis.Project(n.Latitude, n.Longitude).LateralMetres) > ExitBranch.CenterlineBandMetres)
                return Math.Max(0, i - 1);
        }
        return 0;
    }

    // ExitBearingTrue by the existing rule, evaluated where the exit stands (PlaceOf; a sibling's divergence
    // node for a swap): the branch's heading over at least ExitBranch.MinStrokeMetres there - never a
    // single 2-4 m jog, which steered the tone 53-81 degrees off a ~20-degree exit after "turn now" (KPIT
    // 28L F5, WSSS 20L MY6) - replaced by the chord from there to the corridor node when that heading is
    // under 20° and the chord is wider and forward (≤ NORMAL_MAX_DEG, 110°, the producers' own apron-override
    // guard). A branch with no corridor node within reach aims at its clear node instead, which every
    // measured branch has: KSFB 36 C runs 67 m 0.1° right of the runway before turning off left, and with
    // nothing to aim at its side was that 0.1°. Due north is stored as 360 so 0 keeps meaning "unknown".
    private double BranchExitBearing(LandingExitBranch branch, double rwyHeadingTrue, (int Index, bool Into) place)
    {
        var (from, to) = ExitBranch.StrokeAt(this, branch.Path, place.Index, place.Into);
        if (from == to) return 0.0;
        var a = Nodes[branch.Path[from]];
        var b = Nodes[branch.Path[to]];
        double first = NavigationCalculator.CalculateBearing(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        double firstRel = Math.Abs(NormalizeAngle(first - rwyHeadingTrue));
        double bearing = first;
        int aimAt = branch.CorridorNodeId > 0 ? branch.CorridorNodeId : branch.ClearNodeId;
        if (firstRel < 20.0 && aimAt > 0 && Nodes.TryGetValue(aimAt, out var k))
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
    // The ONE best-edge rule every producer uses (GetLandingExits' main and fallback passes,
    // FindDownfieldExits). Of a node's named, non-runway edges: connector-style names (letter and digit) over
    // bare main names - the same ranking spirit as hold-short naming - then the edge turning most off the
    // runway axis, folded to 0-90° so a reverse row counts as parallel (without it a parallel-running named
    // edge was chosen over the perpendicular exit edge: EGCC AF/AG on 23R read about 0°). Two rows folding to
    // the same angle (within 0.01°) are one taxiway's forward and reverse edges: the one taking the aircraft
    // further off the runway on the node's own side wins (lateralM: + right, - left), or, with the node within
    // 1 m of the centreline, the forward one. The chosen edge's far node is the producer's seed, the half a
    // crossing is measured on; ExitBranch.Analyze tries the other half when that half is not forward.
    private static TaxiEdge? BestExitEdge(IEnumerable<TaxiEdge> edges, double rwyHeadingTrue, double lateralM)
    {
        TaxiEdge? best = null;
        foreach (var e in edges)
        {
            if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(e.TaxiwayName)) continue;
            if (best == null) { best = e; continue; }
            bool bestHasDigit = HasLetterAndDigit(best.TaxiwayName);
            bool curHasDigit = HasLetterAndDigit(e.TaxiwayName);
            if (curHasDigit && !bestHasDigit) { best = e; continue; }
            if (curHasDigit != bestHasDigit) continue;
            double bestRel = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));
            double bestOff = bestRel > 90.0 ? 180.0 - bestRel : bestRel;
            double curRel = Math.Abs(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue));
            double curOff = curRel > 90.0 ? 180.0 - curRel : curRel;
            if (curOff > bestOff + 0.01) { best = e; continue; }
            if (Math.Abs(curOff - bestOff) > 0.01) continue;
            // For two rows of a line drawn ALONG the runway the lateral sign below is noise (sin of ±0.1°), but
            // do not hand such ties to the hemisphere rule: a hold-short node on such a line decides whether the
            // runway's list is built in hold-short mode at all (hsOnlyEnds reads this producer reading), and
            // reading it forward cut KFCM 10L from seven exits to two (whole-database sweep, 2026-09-26).
            if (Math.Abs(lateralM) > 1.0)
            {
                double bestLat = Math.Sin(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue) * Math.PI / 180.0);
                double curLat = Math.Sin(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue) * Math.PI / 180.0);
                if (Math.Sign(curLat) == Math.Sign(lateralM) && Math.Sign(bestLat) != Math.Sign(lateralM)) best = e;
            }
            else if (curRel <= 90.0 && bestRel > 90.0)
            {
                best = e;
            }
        }
        return best;
    }
}
