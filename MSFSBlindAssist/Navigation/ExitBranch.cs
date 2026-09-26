using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A runway's frame for measuring exit branches: the same equirectangular projection
/// <see cref="TaxiGraph.GetLandingExits"/> uses (111,132 m per degree of latitude, longitude scaled by
/// the cosine of the mean latitude), measured from the runway start along the landing heading.
/// Lateral is signed, POSITIVE = RIGHT of the landing direction.
/// </summary>
public readonly record struct RunwayAxis(double StartLat, double StartLon, double HeadingTrueDeg, double HalfWidthMetres)
{
    private const double MetresPerDegLat = 111132.0;

    /// <summary>
    /// The margin <c>GetLandingExits</c> adds to the half-width for its exit corridor: the rollout's own
    /// exit-node corridor margin (<see cref="RolloutExitGate.HandoffReachMarginM"/>), linked so the two can
    /// never drift - the derived-constant tripwire's 5 m gap lives between this and the clear margin.
    /// </summary>
    public const double CorridorMarginMetres = RolloutExitGate.HandoffReachMarginM;

    // Get-only (never init), so a `with` can never leave the cached trigonometry describing another heading.
    public double HeadingTrueDeg { get; } = HeadingTrueDeg;
    private readonly double _cosH = Math.Cos(HeadingTrueDeg * Math.PI / 180.0);
    private readonly double _sinH = Math.Sin(HeadingTrueDeg * Math.PI / 180.0);

    /// <summary>Off the runway: half-width + <see cref="RolloutExitGate.RunwayClearMarginM"/>, the codebase's one definition.</summary>
    public double ClearLateralMetres => HalfWidthMetres + RolloutExitGate.RunwayClearMarginM;

    /// <summary><c>GetLandingExits</c>' exit corridor: half-width + 15 m.</summary>
    public double CorridorLateralMetres => HalfWidthMetres + CorridorMarginMetres;

    /// <summary>The frame for <paramref name="rwy"/>, with <c>GetLandingExits</c>' own half-width rule (75 ft when missing).</summary>
    public static RunwayAxis For(Runway rwy)
    {
        double halfWidthFt = rwy.Width > 0 ? rwy.Width * 0.5 : 75.0;
        return new RunwayAxis(rwy.StartLat, rwy.StartLon, rwy.Heading, halfWidthFt * 0.3048);
    }

    public (double AlongMetres, double LateralMetres) Project(double lat, double lon)
    {
        double latRad = (StartLat + lat) * 0.5 * Math.PI / 180.0;
        double metresPerDegLon = MetresPerDegLat * Math.Cos(latRad);
        double dN = (lat - StartLat) * MetresPerDegLat;
        double dE = (lon - StartLon) * metresPerDegLon;
        return (dE * _sinH + dN * _cosH, dE * _cosH - dN * _sinH);
    }

    /// <summary>Heading of the step a→b relative to the landing heading, degrees, signed (+ right), (−180, 180].</summary>
    public double RelativeHeadingDeg(double latA, double lonA, double latB, double lonB)
    {
        var (alongA, lateralA) = Project(latA, lonA);
        var (alongB, lateralB) = Project(latB, lonB);
        return Math.Atan2(lateralB - lateralA, alongB - alongA) * 180.0 / Math.PI;
    }
}

/// <summary>One exit branch as measured by <see cref="ExitBranch.Analyze"/>.</summary>
/// <param name="JunctionNodeId">Where the branch meets the runway.</param>
/// <param name="ClearNodeId">First node beyond <see cref="RunwayAxis.ClearLateralMetres"/> along the branch; -1 = unmeasured.</param>
/// <param name="CorridorNodeId">First node beyond <see cref="RunwayAxis.CorridorLateralMetres"/>; -1 when none within reach.</param>
/// <param name="TurnToClearDeg">Sharpest turn from the landing heading along the whole of <paramref name="Path"/>,
/// 0–180°, read over strokes of at least <see cref="ExitBranch.MinStrokeMetres"/>. Sets the exit's angle
/// (capped at 90°).</param>
/// <param name="TurnToLeaveDeg">Sharpest turn from the landing heading along <paramref name="Path"/> up to and
/// including the first node beyond the runway half-width, read the same way: how the branch LEAVES the pavement,
/// 0–180°. Decides <see cref="IsTurnaround"/>.</param>
/// <param name="Path">Junction … clear node.</param>
public sealed record LandingExitBranch(
    int JunctionNodeId, int ClearNodeId, int CorridorNodeId, double TurnToClearDeg, double TurnToLeaveDeg,
    IReadOnlyList<int> Path)
{
    public bool IsMeasured => ClearNodeId >= 0;

    /// <summary>
    /// The branch leaves the runway pavement turning more than <see cref="RolloutExitGate.TurnaroundAboveDeg"/>.
    /// Judged on <see cref="TurnToLeaveDeg"/>, never on the turn to the clear line: a branch that leaves the
    /// pavement at 90° and hooks back only on the taxiway system beyond the edge is a usable exit (worldwide
    /// sweep, 2026-09-26: CYVR 26L D1, SNOL 30, MURU 06, O54 36 were lost as "turnarounds" that way).
    /// </summary>
    public bool IsTurnaround => IsMeasured && TurnToLeaveDeg > RolloutExitGate.TurnaroundAboveDeg;
}

/// <summary>
/// Measures a landing exit by its WHOLE branch — where its pavement leaves the runway, which way it
/// goes, and the sharpest turn needed to get clear — instead of by whichever short segment happens to
/// carry the taxiway name (KMEM 36L, 2026-09-26: the 18R arm of Y-shaped M6 was offered to a 36L
/// landing as "Normal 52°" because a 128° hairpin was folded to 52°, and 90° M5 was "High-speed 14°"
/// because its fillet's first segment is shallow). Graph-reading and deterministic; no SimConnect/UI.
/// </summary>
public static class ExitBranch
{
    /// <summary>Nodes within this of the centerline are ON it; the junction search walks back along this band.</summary>
    public const double CenterlineBandMetres = 5.0;
    /// <summary>Each inward step must bring the branch at least this much closer to the centerline.</summary>
    public const double InwardStepMinMetres = 0.25;
    public const int WalkMaxHops = 12;
    public const double WalkMaxMetres = 400.0;
    /// <summary>
    /// How far the walk back along the band (<see cref="WalkToJunction"/>'s second phase) may follow a
    /// lead-in line, measured along its own edges. A lead-in line runs from where the exit's pavement
    /// meets the band back to where its painted line starts - tens of metres (KMEM M5 18 m, M7 24 m).
    /// </summary>
    public const double BandWalkMaxMetres = 150.0;
    /// <summary>How far the outward search follows the branch (ExitPathLeavesCorridor's own bound).</summary>
    public const double OutwardMaxMetres = 600.0;
    /// <summary>
    /// How far <see cref="LeadsOntoRunway"/> walks inward. A taxiway that leads onto the runway gets there
    /// within tens of metres (the longest such walk in the fs2024 database, over every corridor node beyond a
    /// runway edge, is 85 m at LEGI 09/27); a parallel taxiway drifting toward the runway takes hundreds, or never does.
    /// </summary>
    public const double ReachWalkMaxMetres = 150.0;
    /// <summary>
    /// How wide a strip between a taxiway's pavement and the runway's may be and still count as the two
    /// touching - a seam in how the scenery was drawn, not a verge an aircraft crosses on grass. A judgement
    /// value, not a measurement (2026-09-26): the parallels the rescue scan must never offer leave 5.7 m of
    /// grass (NC12 26's A) and 7.8 m (SC41 33's B); EGDR 36's L misses touching by 0.2 m.
    /// </summary>
    public const double PavementSeamMetres = 2.0;
    /// <summary>How far past the clear point the sibling search looks for a Y-exit's other arm.</summary>
    public const double SiblingSearchMaxMetres = 150.0;
    /// <summary>
    /// The shortest stretch of a branch a heading is read over. Navdata joins many lead-in lines to
    /// their centreline node with a 2–4 m jog, and read edge by edge that jog set the whole branch's
    /// angle, type and bearing: KPIT 28L F5 (fs2024) was "Normal 50.8°" from a 2.1 m row on a rapid exit
    /// whose every other segment turns at most 21.4°, WSSS 20L MY6 81.3° from a 2.0 m row — too fast to
    /// turn above 30 kt, no 900 ft call, no early handoff. Consecutive edges are merged until each
    /// stretch is at least this long; a last stretch shorter than it joins the one before. Longer than
    /// the graph's own 1.5 m node merge, shorter than any real exit segment; checked over the whole
    /// database (docs/taxi-guidance.md, "Exits measured by branch").
    /// </summary>
    public const double MinStrokeMetres = 5.0;
    /// <summary>
    /// How far along the runway a Y exit's forward arm may meet it from its backward arm's junction.
    /// KMEM M6's two arms are 155 m apart; the spurious KMCI 01L "sibling" the sweep found was 384 m
    /// away, on the other side of the runway.
    /// </summary>
    public const double SiblingJunctionMaxMetres = 300.0;

    /// <summary>
    /// Measures the branch <paramref name="candidateNodeId"/> lies on. <paramref name="seedNeighborId"/>
    /// picks the side when the candidate IS the junction (a taxiway crossing the runway): the outward
    /// search's first hop is then only that neighbour. <paramref name="nameFilter"/>, when set, keeps
    /// the whole measurement on edges that are unnamed or carry that name - an empty name means unnamed
    /// edges only: the inward walk (<see cref="WalkToJunction"/>, both phases), so a candidate at a node
    /// it shares with another exit is never walked in along that exit's arm (KATL 26L: B4 was listed as
    /// a copy of E3), and the outward search for the clear node, so a candidate on a lead line another
    /// exit crosses is never measured out along that exit's arm (KMIA 08R: M7 was given M6's 90°
    /// crossing). A candidate whose own taxiway never reaches the runway, or never clears it, is
    /// unmeasured. Unset (the hold-short gate), nothing is filtered.
    /// </summary>
    public static LandingExitBranch Analyze(TaxiGraph graph, RunwayAxis axis, int candidateNodeId,
        int? seedNeighborId = null, string? nameFilter = null)
    {
        var inward = WalkToJunction(graph, axis, candidateNodeId, excluded: null, nameFilter);
        int junction = inward[0];
        // The inward walk never actually reached the runway pavement (a dead end short of it, or the
        // hop/distance budget ran out first) — this candidate's branch never meets the runway at all,
        // so it must be reported unmeasured rather than silently measured from wherever the walk gave up.
        if (Math.Abs(Lateral(graph, axis, junction)) > axis.HalfWidthMetres)
            return new LandingExitBranch(junction, -1, -1, 0.0, 0.0, inward);
        // Decided once, for this measurement and the junction fallback alike (LeavingBackwardShortOfClear).
        bool exitOnPavement = Math.Abs(Lateral(graph, axis, candidateNodeId)) <= axis.HalfWidthMetres;
        var branch = MeasureFrom(graph, axis, inward, junction == candidateNodeId ? seedNeighborId : null,
            nameFilter, exitOnPavement);
        if (junction == candidateNodeId && seedNeighborId.HasValue && (!branch.IsMeasured || branch.IsTurnaround))
            return ForwardHalf(graph, axis, inward, seedNeighborId.Value, nameFilter, exitOnPavement) ?? branch;
        if (branch.IsMeasured || junction == candidateNodeId) return branch;
        // Nothing leaves the runway from the candidate itself — it sits on a lead-in line beside the
        // junction. Measure the junction's own branch instead: on the candidate's own side of the runway
        // whenever the candidate has an arm of its own there (OwnSideToKeep).
        return MeasureFrom(graph, axis, new List<int> { junction }, null, nameFilter, exitOnPavement,
            OwnSideToKeep(graph, axis, candidateNodeId, nameFilter));
    }

    /// <summary>
    /// A crossing whose seeded half does not measure forward (a turnaround, or never clearing) is measured on
    /// its other half instead - a first hop from the candidate on an edge carrying the exit's own name, never
    /// an unnamed one (which could run along the runway to another exit's arm), whose branch clears on the
    /// OTHER side of the runway from the way the seed edge heads - returning the forward one that leaves the
    /// pavement least sharply (ties: the lower neighbour id), or null. The producers seed the edge their
    /// best-edge rule picks, and that rule reads each edge folded off the runway axis, so a crossing's backward
    /// half wins whenever it is the more nearly perpendicular: a kink of half a degree (forward 60.0°, back
    /// 119.5°) was enough to measure a usable exit as a 130° turnaround. Only a crossing: two arms of one name
    /// on the SAME side are a Y, whose forward arm is listed where it leaves the centreline (its own node, or
    /// FindForwardSibling's divergence node) - rescued here at the junction, KABQ 08 P was listed 353 ft
    /// before its turn-off, at a point where its lead line still runs 3.7° off the runway.
    /// </summary>
    private static LandingExitBranch? ForwardHalf(TaxiGraph graph, RunwayAxis axis, List<int> inward, int seed,
        string? nameFilter, bool exitOnPavement)
    {
        if (string.IsNullOrEmpty(nameFilter)) return null;
        var at = graph.Nodes[inward[^1]];
        var seedNode = graph.Nodes[seed];
        double seedLateral = Math.Sin(axis.RelativeHeadingDeg(at.Latitude, at.Longitude, seedNode.Latitude, seedNode.Longitude) * Math.PI / 180.0);
        if (Math.Abs(seedLateral) < 0.05) return null;   // along the runway: no side to cross from
        int seedSide = Math.Sign(seedLateral);
        LandingExitBranch? best = null;
        int bestHop = int.MaxValue;
        foreach (var e in Walkable(graph, inward[^1]))
        {
            if (e.ToNodeId == seed || !SameTaxiwayName(e.TaxiwayName, nameFilter)) continue;
            var half = MeasureFrom(graph, axis, inward, e.ToNodeId, nameFilter, exitOnPavement);
            if (!half.IsMeasured || half.IsTurnaround) continue;
            if (Math.Sign(Lateral(graph, axis, half.ClearNodeId)) != -seedSide) continue;
            if (best == null || half.TurnToLeaveDeg < best.TurnToLeaveDeg
                || (half.TurnToLeaveDeg == best.TurnToLeaveDeg && e.ToNodeId < bestHop))
            {
                best = half;
                bestHop = e.ToNodeId;
            }
        }
        return best;
    }

    /// <summary>
    /// The side (+1 right, -1 left) the junction fallback must stay on: the candidate's own, when it stands
    /// outside the centreline band and its own taxiway (<paramref name="nameFilter"/>) carries on further out
    /// on that side — an arm of its own that never cleared (a name change, a dead end, an arm leaving
    /// backward). Measured on the other side, it took a different arm's angle, bearing and side: WSSS 20L
    /// MY6's node 21.7 m RIGHT of the centreline, on the crossing's backward half, was listed "Left" with the
    /// forward half's jog while "turn now" steered at the node on the right (review V3-c; 97 such exits over
    /// the fs2024 database). 0 — either side — for a candidate in the band or at the start of a lead-in line
    /// (nothing of its own further out), where measuring the branch it joins is right (ENGM 01R B4).
    /// </summary>
    private static int OwnSideToKeep(TaxiGraph graph, RunwayAxis axis, int candidateNodeId, string? nameFilter)
    {
        double lateral = Lateral(graph, axis, candidateNodeId);
        if (Math.Abs(lateral) <= CenterlineBandMetres) return 0;
        int side = Math.Sign(lateral);
        foreach (var e in Walkable(graph, candidateNodeId))
        {
            if (!MatchesNameFilter(e, nameFilter)) continue;
            double next = Lateral(graph, axis, e.ToNodeId);
            if (Math.Sign(next) == side && Math.Abs(next) >= Math.Abs(lateral) + InwardStepMinMetres) return side;
        }
        return 0;
    }

    /// <summary>
    /// The other arm of a Y-shaped exit whose <paramref name="backward"/> arm is a turnaround: an arm that
    /// reaches the same off-runway point along different pavement - from a junction of its own or, where
    /// the Y's two arms leave the runway from one node, from the backward arm's junction - carries no
    /// other taxiway's name, and is itself not a turnaround (judged, like every branch, by how it leaves
    /// the pavement). Null when there is none.
    /// <para>The search floods out from the backward arm's clear node, on <paramref name="exitName"/>'s
    /// own taxiway (or unnamed pavement), through nodes on the SAME side of the runway as that clear
    /// node and outside the runway half-width only - it never crosses the runway (KMCI 01L, USTN 25:
    /// a same-named taxiway led it to a connector on the other side). The backward arm's own nodes
    /// are walled off only up to where the arm meets other pavement (three or more walkable
    /// neighbours, where a Y's two arms merge), so the flood can pass through a merge that lies
    /// inside the clear line onto the forward arm (VADE 26, KIXA 20). A sibling's junction must lie
    /// within <see cref="SiblingJunctionMaxMetres"/> along the runway of the backward arm's, and may be
    /// the backward arm's own junction - a Y whose two arms leave the runway from one node - as long as
    /// the sibling's path beyond it never reuses the backward arm's own nodes.</para>
    /// </summary>
    public static LandingExitBranch? FindForwardSibling(TaxiGraph graph, RunwayAxis axis, LandingExitBranch backward, string exitName)
    {
        if (!backward.IsTurnaround) return null;
        var own = OwnArmNodes(graph, backward.Path);
        int side = Math.Sign(Lateral(graph, axis, backward.ClearNodeId));
        double backwardAlong = Along(graph, axis, backward.JunctionNodeId);
        bool OnThisSideOffTheRunway(int nodeId)
        {
            double lateral = Lateral(graph, axis, nodeId);
            return Math.Sign(lateral) == side && Math.Abs(lateral) > axis.HalfWidthMetres;
        }

        // The arm the inward walk from `start` finds, when it is a valid sibling. The walk never enters
        // `wall`. Both the outward flood that finds candidate start nodes and this inward walk stay ON
        // exitName's own taxiway (or unnamed pavement): a physically-nearby but differently-named
        // taxiway is a different exit system, not this one's other arm, however close its own pavement
        // sits to this exit's clear point.
        LandingExitBranch? SiblingFrom(int start, ISet<int> wall)
        {
            var inward = WalkToJunction(graph, axis, start, wall, exitName);
            int junction = inward[0];
            // The walk never enters `wall`, so it cannot end on the backward arm's own nodes: the start
            // is the only one it can stand on (rejected here), and in the second pass the backward
            // junction is the one own node outside the wall - which is exactly what that pass allows.
            if (junction == start) return null;
            if (Math.Abs(Lateral(graph, axis, junction)) > axis.HalfWidthMetres) return null;
            if (Math.Abs(Along(graph, axis, junction) - backwardAlong) > SiblingJunctionMaxMetres) return null;
            // The WHOLE arm (junction..start) is on exitName's own taxiway or unnamed pavement: the walk and
            // the flood that chose `start` only follow such edges. (A separate whole-arm check once read one
            // row per node pair and rejected an arm followed on its own row beside another name's.)
            int clearIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.ClearLateralMetres);
            if (clearIdx < 0) return null;
            var path = inward.GetRange(0, clearIdx + 1);
            // The sibling must not itself be a turnaround - judged, like every branch, by how it
            // leaves the runway pavement (LandingExitBranch.IsTurnaround).
            double leave = TurnToLeave(graph, axis, path);
            if (leave > RolloutExitGate.TurnaroundAboveDeg) return null;
            double turn = SharpestTurn(graph, axis, path, 0, path.Count - 1);
            int corridorIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.CorridorLateralMetres);
            int corridor = corridorIdx >= 0 ? inward[corridorIdx] : backward.CorridorNodeId;
            return new LandingExitBranch(junction, path[^1], corridor, turn, leave, path);
        }

        // First pass: the backward arm's own nodes, its junction included, are walled off - a sibling
        // leaves the runway from a junction of its own. The flood's order is recorded for the second pass,
        // which reads the same nodes rather than flooding again.
        var flooded = new List<int>();
        foreach (int start in NodesOutwardFrom(graph, backward.ClearNodeId, own, exitName, OnThisSideOffTheRunway))
        {
            flooded.Add(start);
            var sibling = SiblingFrom(start, own);
            if (sibling != null) return sibling;
        }
        // Only when that finds nothing: a Y whose two arms leave the runway from ONE node (KMIA 08R Z,
        // ULWB 33). The walk may now end on the backward arm's junction, never on its other nodes.
        // Second, so that it only ever ADDS a sibling: tried first, the shared node - often a few feet
        // from an arm's own junction and nearer the centreline - drew walks away from real siblings.
        var ownBeyondJunction = new HashSet<int>(own);
        ownBeyondJunction.Remove(backward.JunctionNodeId);
        foreach (int start in flooded)
        {
            var sibling = SiblingFrom(start, ownBeyondJunction);
            if (sibling != null) return sibling;
        }
        return null;
    }

    /// <summary>
    /// Junction … start. Two phases.
    /// <para>Phase 1 steps toward the centerline (each step at least <see cref="InwardStepMinMetres"/>
    /// closer, the closest neighbour first) until inside the <see cref="CenterlineBandMetres"/> band.</para>
    /// <para>Phase 2 then walks BACK along the band (toward the landing threshold) to where the exit's
    /// lead-in line starts - and follows a SIMPLE line only, because a lead-in line is a simple chain
    /// until it meets other pavement. It may start only when the first in-band node has at most two
    /// walkable neighbours; it continues only from a node with exactly two; it may step INTO a node of
    /// any degree, and that node ends the walk and is the junction; and it follows at most
    /// <see cref="BandWalkMaxMetres"/> of band. So it never slides down a taxi path drawn along the
    /// centreline, or onto another exit's lead line (worldwide sweep, 2026-09-26: 1,591 exits were
    /// relocated more than 300 ft that way - KMIA 08R M5 by 1,080 ft, KDFW 17C P2 by 1,306 ft).
    /// "Walkable neighbours" are the distinct neighbours <c>Walkable</c> returns, whatever their name.</para>
    /// <para>Both phases share <see cref="WalkMaxHops"/> and <see cref="WalkMaxMetres"/>, never enter
    /// <paramref name="excluded"/>, and - when <paramref name="nameFilter"/> is set - only follow edges
    /// that are unnamed or carry that name (<see cref="SameTaxiwayName"/>; an empty
    /// filter means unnamed edges only). The refinement's walk stays on its exit's own taxiway that
    /// way, and the sibling search's own walk can never wander home via someone else's.</para>
    /// </summary>
    internal static List<int> WalkToJunction(TaxiGraph graph, RunwayAxis axis, int startNodeId, ISet<int>? excluded, string? nameFilter = null)
    {
        var path = new List<int> { startNodeId };
        var visited = new HashSet<int> { startNodeId };
        int current = startNodeId;
        double walked = 0.0;

        // Phase 1: steepest descent into the band.
        while (path.Count <= WalkMaxHops && Math.Abs(Lateral(graph, axis, current)) > CenterlineBandMetres)
        {
            double limit = Math.Abs(Lateral(graph, axis, current)) - InwardStepMinMetres;
            TaxiEdge? best = null;
            foreach (var e in Walkable(graph, current))
            {
                if (visited.Contains(e.ToNodeId) || (excluded != null && excluded.Contains(e.ToNodeId))) continue;
                if (!MatchesNameFilter(e, nameFilter)) continue;
                double lateral = Math.Abs(Lateral(graph, axis, e.ToNodeId));
                if (lateral <= limit) { best = e; limit = lateral; }
            }
            if (best == null || walked + best.DistanceMeters > WalkMaxMetres) break;
            walked += best.DistanceMeters;
            current = best.ToNodeId;
            path.Add(current);
            visited.Add(current);
        }

        // Phase 2: back along the band on a simple line only (see the summary).
        if (Math.Abs(Lateral(graph, axis, current)) <= CenterlineBandMetres
            && WalkableDegree(graph, current) <= 2)
        {
            double bandWalked = 0.0;
            while (path.Count <= WalkMaxHops)
            {
                double limit = Along(graph, axis, current) - InwardStepMinMetres;
                TaxiEdge? best = null;
                foreach (var e in Walkable(graph, current))
                {
                    if (visited.Contains(e.ToNodeId) || (excluded != null && excluded.Contains(e.ToNodeId))) continue;
                    if (!MatchesNameFilter(e, nameFilter)) continue;
                    if (Math.Abs(Lateral(graph, axis, e.ToNodeId)) > CenterlineBandMetres) continue;
                    double along = Along(graph, axis, e.ToNodeId);
                    if (along <= limit) { best = e; limit = along; }
                }
                if (best == null
                    || walked + best.DistanceMeters > WalkMaxMetres
                    || bandWalked + best.DistanceMeters > BandWalkMaxMetres) break;
                walked += best.DistanceMeters;
                bandWalked += best.DistanceMeters;
                current = best.ToNodeId;
                path.Add(current);
                visited.Add(current);
                // Where the chain meets other pavement (or ends), that node is the junction.
                if (WalkableDegree(graph, current) != 2) break;
            }
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Can an aircraft on the runway get to <paramref name="startNodeId"/> on paved ground - its taxiway a way
    /// off the runway, not a taxiway running beside it across the grass?
    /// <para>True at once where the node's own taxiway pavement reaches the runway's: no further beyond the
    /// runway edge than the taxiway's half-width and <see cref="PavementSeamMetres"/>
    /// (<see cref="TouchesRunwayPavement"/>; 4AK6 19's CC, 55 ft wide, 1.1 m beyond).</para>
    /// <para>Otherwise walks inward by steepest descent over walkable edges (never a stand lead-in or a stand
    /// bridge), each step at least <see cref="InwardStepMinMetres"/> closer to the centreline and angled more
    /// than <see cref="TaxiGraph.ParallelTaxiwayMaxDeg"/> off the axis - a step within that runs along the
    /// runway, not toward it - for up to <see cref="ReachWalkMaxMetres"/>. True when a step ends on the runway
    /// pavement (<see cref="RunwayAxis.HalfWidthMetres"/>), crosses the centreline or reaches a node whose
    /// taxiway pavement touches the runway's; the last step counts only as far as the pavement edge, so a long
    /// edge crossing the runway is not held against its length (LEMD 18R's Z7 crosses in one 166 m edge and is
    /// on the pavement after 34). True too when the walk stops short at a node NOT on a line along the runway
    /// (<see cref="RunsAlongRunway"/>): a connector's inner end, a fork, a taxiway stopped short of the runway
    /// edge (KTPA 10's N, its stub ending 4.3 m beyond the navdata edge).</para>
    /// <para>False when it stops on such a line - a parallel taxiway with grass between it and the runway,
    /// where it bends closest (NC12 26's A: 26 ft wide, 20.0 m out on a 10.4 m half-width) or where a loop to
    /// the apron leaves it (SC41 33's B: 20 ft wide, 27.7 m out on 16.9 m) - or when it runs out of
    /// <see cref="ReachWalkMaxMetres"/>.</para>
    /// </summary>
    internal static bool LeadsOntoRunway(TaxiGraph graph, RunwayAxis axis, int startNodeId)
    {
        if (!graph.Nodes.ContainsKey(startNodeId)) return false;
        double lateral = Lateral(graph, axis, startNodeId);
        double outward = Math.Abs(lateral);           // how far out on the start side; negative = across
        double side = Math.Sign(lateral);
        int current = startNodeId;
        double walked = 0.0;
        while (true)
        {
            if (outward <= axis.HalfWidthMetres || TouchesRunwayPavement(graph, axis, current, outward)) return true;
            // Strictly inward every step, so the walk can never revisit a node.
            TaxiEdge? best = null;
            double bestOutward = outward - InwardStepMinMetres;
            foreach (var e in Walkable(graph, current))
            {
                double next = side * Lateral(graph, axis, e.ToNodeId);
                if (next > bestOutward) continue;
                if (OffAxisDeg(Heading(graph, axis, current, e.ToNodeId)) <= TaxiGraph.ParallelTaxiwayMaxDeg) continue;
                best = e;
                bestOutward = next;
            }
            if (best == null) return !RunsAlongRunway(graph, axis, current);
            if (bestOutward <= axis.HalfWidthMetres)
            {
                double toPavement = best.DistanceMeters * (outward - axis.HalfWidthMetres) / (outward - bestOutward);
                return walked + toPavement <= ReachWalkMaxMetres;
            }
            walked += best.DistanceMeters;
            if (walked > ReachWalkMaxMetres) return false;
            current = best.ToNodeId;
            outward = bestOutward;
        }
    }

    /// <summary>
    /// Does the pavement of the taxiway at <paramref name="nodeId"/>, <paramref name="outwardMetres"/> from the
    /// centreline, reach the runway's? Its half-width - the widest walkable edge's navdata width, capped at
    /// <see cref="PavementTolerance.WidthCapFeet"/> - must close the gap to the runway edge, all but a
    /// <see cref="PavementSeamMetres"/> seam. A row with no width is no evidence of pavement, so it never touches.
    /// </summary>
    internal static bool TouchesRunwayPavement(TaxiGraph graph, RunwayAxis axis, int nodeId, double outwardMetres)
    {
        double widestFeet = 0.0;
        foreach (var e in Walkable(graph, nodeId))
            if (e.WidthFeet > widestFeet) widestFeet = e.WidthFeet;
        if (widestFeet <= 0.0) return false;
        double halfWidthMetres = Math.Min(widestFeet, PavementTolerance.WidthCapFeet) * 0.3048 * 0.5;
        return outwardMetres - axis.HalfWidthMetres - halfWidthMetres <= PavementSeamMetres;
    }

    /// <summary>
    /// Is <paramref name="nodeId"/> on a line running along the runway - a walkable edge each way within
    /// <see cref="TaxiGraph.MinFallbackExitAngleDeg"/> of the axis? One edge along the runway is not a line
    /// along it: a connector's inner end with a short link running on to its twin is not (KBUR 26's D).
    /// </summary>
    internal static bool RunsAlongRunway(TaxiGraph graph, RunwayAxis axis, int nodeId)
    {
        bool forward = false, backward = false;
        foreach (var e in Walkable(graph, nodeId))
        {
            double rel = Math.Abs(Heading(graph, axis, nodeId, e.ToNodeId));
            if (rel < TaxiGraph.MinFallbackExitAngleDeg) forward = true;
            else if (rel > 180.0 - TaxiGraph.MinFallbackExitAngleDeg) backward = true;
        }
        return forward && backward;
    }

    // A relative heading (degrees, signed) folded onto the angle it makes with the axis, 0-90.
    private static double OffAxisDeg(double relativeHeadingDeg)
    {
        double a = Math.Abs(relativeHeadingDeg);
        return a > 90.0 ? 180.0 - a : a;
    }

    private static LandingExitBranch MeasureFrom(
        TaxiGraph graph, RunwayAxis axis, List<int> inward, int? seedNeighborId, string? nameFilter,
        bool exitOnPavement, int requiredSide = 0)
    {
        int junction = inward[0];
        int candidate = inward[^1];
        var (clearNode, corridorNode, edgeNode, parents) =
            SearchOutward(graph, axis, candidate, new HashSet<int>(inward), seedNeighborId, nameFilter, requiredSide);

        // The path always reaches the candidate, so the exit's own node is on it. When the walk in has
        // already crossed the clear line (a hold-short node just outside it), the branch is measured to
        // the first node beyond it and the path runs on to the candidate; the review's V3-a shape lost
        // the candidate off the end of a path cut at the clear node, and its bearing was read from the
        // lead-in start instead - the lead line's chord, 38.7 degrees off the runway for a 90-degree exit.
        var path = new List<int>(inward);
        int clearIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.ClearLateralMetres);
        if (clearIdx < 0)
        {
            if (clearNode < 0)
                return exitOnPavement
                    ? LeavingBackwardShortOfClear(graph, axis, inward, edgeNode, corridorNode, parents)
                    : new LandingExitBranch(junction, -1, corridorNode, 0.0, 0.0, inward);
            path.AddRange(ChainFrom(parents, candidate, clearNode));
            clearIdx = path.Count - 1;
        }
        return new LandingExitBranch(junction, path[clearIdx], corridorNode,
            SharpestTurn(graph, axis, path, 0, clearIdx), TurnToLeave(graph, axis, path), path);
    }

    // A branch whose own taxiway ends short of the clear line has no clear node and is unmeasured - except
    // one that has already LEFT THE PAVEMENT turning back. A turnaround is judged where the branch leaves the
    // pavement (TurnToLeaveDeg), never at the clear line, so it is one whether or not its own name reaches
    // the clear line: WSSS 20L MY6's backward half leaves the pavement 168 degrees back and becomes A6/A7 a
    // metre later. Left unmeasured, its nodes kept the producer's own reading - a coin toss between an
    // 11.5-degree forward edge and an 11.4-degree backward one - which listed a node 12.5 m RIGHT of the
    // centreline as MY6's forward exit, where MY6 turns off to the LEFT; "turn now" steered at that node.
    // Reported to its first node beyond the half-width, which stands in for the clear node. Only while the
    // exit's own node is still ON the pavement (Analyze decides it), so the way off is the outward search's,
    // shortest first: from a node already off it, the walk in and the junction's way back out are picks
    // between neighbours - 0D7 27 (fs2024) took a hump's downfield side by 0.5 m and read 178° back, and the
    // junction's only way off ran up the same hump; 172 runway directions lost their only usable exit so.
    private static LandingExitBranch LeavingBackwardShortOfClear(
        TaxiGraph graph, RunwayAxis axis, List<int> inward, int edgeNode, int corridorNode, Dictionary<int, int> parents)
    {
        int junction = inward[0];
        var unmeasured = new LandingExitBranch(junction, -1, corridorNode, 0.0, 0.0, inward);
        if (edgeNode < 0) return unmeasured;
        var path = new List<int>(inward);
        path.AddRange(ChainFrom(parents, inward[^1], edgeNode));
        int edgeIdx = path.Count - 1;
        double leave = SharpestTurn(graph, axis, path, 0, edgeIdx);
        if (leave <= RolloutExitGate.TurnaroundAboveDeg) return unmeasured;
        return new LandingExitBranch(junction, path[edgeIdx], corridorNode, leave, leave, path);
    }

    // Dijkstra by path length from `from`, never entering `excluded` (the inward path) and - when
    // `nameFilter` is set - following only edges that are unnamed or carry that name, and - when
    // `requiredSide` is set - never leaving the centreline band on the other side of the runway; from off
    // the pavement, never back into the band or across to the runway's other side. Returns the
    // first node beyond the clear boundary, the first beyond the corridor boundary and the first beyond the
    // runway half-width (-1 when none in reach).
    private static (int Clear, int Corridor, int Edge, Dictionary<int, int> Parents) SearchOutward(
        TaxiGraph graph, RunwayAxis axis, int from, HashSet<int> excluded, int? seedNeighborId, string? nameFilter,
        int requiredSide = 0)
    {
        var parents = new Dictionary<int, int>();
        var best = new Dictionary<int, double> { [from] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        int clear = -1, corridor = -1, edge = -1;

        // Once off the pavement, a branch never comes back to the centreline or crosses to the runway's other
        // side - that is a taxiway crossing the runway, walked back over it. GMMN 17L A's crossing was 4.8 m
        // shorter than its own left arm, so the exit took its clear node, corridor and bearing from the RIGHT
        // and was told "turn right" for a turn-off to the left (66 branches over the fs2024 database left the
        // pavement and came back onto it before clearing, 56 clearing on the far side). The side is carried
        // along each path, so a wiggle back inside the edge cannot reopen the way across. On a strip narrower
        // than the band (0IN9 09: 2.1 m half-width) a path has left only once it is past the band too.
        var leftOn = new Dictionary<int, int>();
        double leftBeyond = Math.Max(axis.HalfWidthMetres, CenterlineBandMetres);
        int SideLeftOn(int at)
        {
            int inherited = leftOn.GetValueOrDefault(at);
            if (inherited != 0) return inherited;
            double l = Lateral(graph, axis, at);
            return Math.Abs(l) > leftBeyond ? Math.Sign(l) : 0;
        }
        bool ReturnsToRunway(int side, int to)
        {
            if (side == 0) return false;
            double there = Lateral(graph, axis, to);
            return Math.Abs(there) <= CenterlineBandMetres || Math.Sign(there) != side;
        }

        // The corridor node lies further along the CLEAR node's own path, never on another arm the search
        // happened to reach first: KACK 24 A cleared on its left arm while the first node past the corridor
        // line was on a right-hand one, and the bearing's chord to it said "Right" for an exit to the left.
        var throughClear = new HashSet<int>();

        int fromSide = SideLeftOn(from);
        foreach (var e in Walkable(graph, from))
        {
            if (excluded.Contains(e.ToNodeId)) continue;
            if (!MatchesNameFilter(e, nameFilter)) continue;
            if (seedNeighborId.HasValue && e.ToNodeId != seedNeighborId.Value) continue;
            if (!OnSide(graph, axis, e.ToNodeId, requiredSide)) continue;
            if (ReturnsToRunway(fromSide, e.ToNodeId)) continue;
            if (best.TryGetValue(e.ToNodeId, out double known) && known <= e.DistanceMeters) continue;
            best[e.ToNodeId] = e.DistanceMeters;
            parents[e.ToNodeId] = from;
            leftOn[e.ToNodeId] = fromSide;
            queue.Enqueue(e.ToNodeId, e.DistanceMeters);
        }

        var done = new HashSet<int> { from };
        while (queue.TryDequeue(out int node, out double dist))
        {
            if (!done.Add(node)) continue;
            double lateral = Math.Abs(Lateral(graph, axis, node));
            if (edge < 0 && lateral > axis.HalfWidthMetres) edge = node;
            if (clear < 0 && lateral > axis.ClearLateralMetres) { clear = node; throughClear.Add(node); }
            if (corridor < 0 && lateral > axis.CorridorLateralMetres && throughClear.Contains(node)) corridor = node;
            if (clear >= 0 && corridor >= 0) break;
            if (dist >= OutwardMaxMetres) continue;
            int side = SideLeftOn(node);
            bool onClearPath = throughClear.Contains(node);
            foreach (var e in Walkable(graph, node))
            {
                if (excluded.Contains(e.ToNodeId) || done.Contains(e.ToNodeId)) continue;
                if (!MatchesNameFilter(e, nameFilter)) continue;
                if (!OnSide(graph, axis, e.ToNodeId, requiredSide)) continue;
                if (ReturnsToRunway(side, e.ToNodeId)) continue;
                double next = dist + e.DistanceMeters;
                if (best.TryGetValue(e.ToNodeId, out double known) && known <= next) continue;
                best[e.ToNodeId] = next;
                parents[e.ToNodeId] = node;
                leftOn[e.ToNodeId] = side;
                if (onClearPath) throughClear.Add(e.ToNodeId); else throughClear.Remove(e.ToNodeId);
                queue.Enqueue(e.ToNodeId, next);
            }
        }
        return (clear, corridor, edge, parents);
    }

    // True when `side` is 0 (either), or the node lies inside the centreline band or on that side.
    private static bool OnSide(TaxiGraph graph, RunwayAxis axis, int nodeId, int side)
    {
        if (side == 0) return true;
        double lateral = Lateral(graph, axis, nodeId);
        return Math.Abs(lateral) <= CenterlineBandMetres || Math.Sign(lateral) == side;
    }

    /// <summary>
    /// The path indices a heading is read between AT <c>path[index]</c>: onward from it until at least
    /// <see cref="MinStrokeMetres"/> of path lies between them (or the path ends), or - <paramref name="into"/>
    /// - back from it by the same.
    /// </summary>
    internal static (int From, int To) StrokeAt(TaxiGraph graph, IReadOnlyList<int> path, int index, bool into)
    {
        if (into)
        {
            int k = index;
            double back = 0.0;
            while (k > 0 && back < MinStrokeMetres)
            {
                back += NodeDistance(graph, path[k - 1], path[k]);
                k--;
            }
            return (k, index);
        }
        int j = index;
        double on = 0.0;
        while (j < path.Count - 1 && on < MinStrokeMetres)
        {
            on += NodeDistance(graph, path[j], path[j + 1]);
            j++;
        }
        return (index, j);
    }

    // The nodes after `from` up to and including `to`, following Dijkstra parents.
    private static List<int> ChainFrom(Dictionary<int, int> parents, int from, int to)
    {
        var chain = new List<int>();
        for (int n = to; n != from; n = parents[n]) chain.Add(n);
        chain.Reverse();
        return chain;
    }

    // The backward arm's own nodes, walled off from the sibling search: its path from the junction up
    // to, but EXCLUDING, the first node after the junction with three or more walkable neighbours -
    // where the arm meets other pavement, on a Y exit the merge with the forward arm. The whole path
    // when there is no such node.
    private static HashSet<int> OwnArmNodes(TaxiGraph graph, IReadOnlyList<int> path)
    {
        var own = new HashSet<int>();
        for (int i = 0; i < path.Count; i++)
        {
            if (i >= 1 && WalkableDegree(graph, path[i]) >= 3) break;
            own.Add(path[i]);
        }
        return own;
    }

    // `start` and every node within SiblingSearchMaxMetres of it that avoids `own` and that `admit`
    // accepts, nearest first. When `nameFilter` is set, the flood only crosses edges that are unnamed or
    // carry that name — it can never leave onto a physically-nearby but differently-named taxiway to
    // find a "sibling" there.
    private static IEnumerable<int> NodesOutwardFrom(
        TaxiGraph graph, int start, HashSet<int> own, string? nameFilter, Func<int, bool> admit)
    {
        var best = new Dictionary<int, double> { [start] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(start, 0.0);
        var done = new HashSet<int>();
        while (queue.TryDequeue(out int node, out double dist))
        {
            if (!done.Add(node)) continue;
            yield return node;
            foreach (var e in Walkable(graph, node))
            {
                if (own.Contains(e.ToNodeId) || done.Contains(e.ToNodeId)) continue;
                if (!MatchesNameFilter(e, nameFilter)) continue;
                if (!admit(e.ToNodeId)) continue;
                double next = dist + e.DistanceMeters;
                if (next > SiblingSearchMaxMetres) continue;
                if (best.TryGetValue(e.ToNodeId, out double known) && known <= next) continue;
                best[e.ToNodeId] = next;
                queue.Enqueue(e.ToNodeId, next);
            }
        }
    }

    // True when `nameFilter` is unset, or `e` is unnamed, or `e` carries that name (SameTaxiwayName).
    private static bool MatchesNameFilter(TaxiEdge e, string? nameFilter)
    {
        if (nameFilter == null) return true;
        string name = e.TaxiwayName ?? "";
        return name.Length == 0 || SameTaxiwayName(name, nameFilter);
    }

    /// <summary>
    /// Two spellings of one taxiway name: equal letter for letter and digit for digit, ignoring case and
    /// everything else - "M5", "M-5" and "m 5" are one taxiway. With online taxiway names on (the default),
    /// an unnamed navdata row can be filled with an online spelling of a name navdata already uses beside
    /// it (TaxiGraph.Build folds case variants only), and an exact test stopped the branch at that row: it
    /// never cleared, and the exit kept the first-edge reading the branch exists to replace.
    /// </summary>
    internal static bool SameTaxiwayName(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (true)
        {
            while (i < a.Length && !char.IsLetterOrDigit(a[i])) i++;
            while (j < b.Length && !char.IsLetterOrDigit(b[j])) j++;
            if (i == a.Length || j == b.Length) return i == a.Length && j == b.Length;
            if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[j])) return false;
            i++;
            j++;
        }
    }

    /// <summary>
    /// The strokes of <c>path[from..to]</c> (inclusive node indices): consecutive edges merged until each
    /// run is at least <see cref="MinStrokeMetres"/> long, a last run shorter than that joining the one
    /// before it (or standing alone when it is the only one). Empty when <paramref name="from"/> is not
    /// before <paramref name="to"/>.
    /// </summary>
    internal static List<(int From, int To)> Strokes(TaxiGraph graph, IReadOnlyList<int> path, int from, int to)
    {
        var strokes = new List<(int From, int To)>();
        int i = from;
        while (i < to)
        {
            int j = i;
            double length = 0.0;
            while (j < to && length < MinStrokeMetres)
            {
                length += NodeDistance(graph, path[j], path[j + 1]);
                j++;
            }
            if (length < MinStrokeMetres && strokes.Count > 0) strokes[^1] = (strokes[^1].From, j);
            else strokes.Add((i, j));
            i = j;
        }
        return strokes;
    }

    /// <summary>The sharpest turn from the landing heading over the strokes of <c>path[from..to]</c>.</summary>
    internal static double SharpestTurn(TaxiGraph graph, RunwayAxis axis, IReadOnlyList<int> path, int from, int to)
    {
        double max = 0.0;
        foreach (var (a, b) in Strokes(graph, path, from, to))
            max = Math.Max(max, Math.Abs(Heading(graph, axis, path[a], path[b])));
        return max;
    }

    // The sharpest turn along `path` up to and including its first node beyond the runway half-width:
    // how the branch leaves the pavement (a measured path always reaches one - its clear node is beyond).
    private static double TurnToLeave(TaxiGraph graph, RunwayAxis axis, IReadOnlyList<int> path)
        => SharpestTurn(graph, axis, path, 0, LeaveIndex(graph, axis, path));

    // The first node of the path beyond the runway half-width (the last node when none is).
    private static int LeaveIndex(TaxiGraph graph, RunwayAxis axis, IReadOnlyList<int> path)
    {
        for (int i = 1; i < path.Count; i++)
            if (Math.Abs(Lateral(graph, axis, path[i])) > axis.HalfWidthMetres) return i;
        return path.Count - 1;
    }

    /// <summary>
    /// <paramref name="branch"/> as the aircraft meets it at the exit's own node - its turn to leave the
    /// runway pavement and its turn to the clear line, read like the branch's own over strokes of at least
    /// <see cref="MinStrokeMetres"/>, but from that node on: at or past the pavement edge, the stroke that
    /// crosses it. Null when the node is not on the branch's path (the junction fallback measured another
    /// node's branch).
    /// <para>The branch's own turns are read from its junction, which is only the band node the inward walk
    /// reached. Where that lies past the lead-in's own start, the path runs backward first, over pavement the
    /// aircraft never drives: SBGL 15 F's runs 44 m back along the centreline (178 degrees) before its lead-in
    /// leaves the runway at 47 and turns to 72; MYAS 29's junction lies 9 m past a 90-degree connector, joined
    /// to it by an 11 m link back at 142. Read from the junction, both are turnarounds. Met at the exit's own
    /// node, neither is.</para>
    /// </summary>
    internal static (double TurnToLeaveDeg, double TurnToClearDeg)? FromExitNode(
        TaxiGraph graph, RunwayAxis axis, LandingExitBranch branch, int exitNodeId)
    {
        if (!branch.IsMeasured) return null;
        var path = branch.Path;
        int at = -1;
        for (int i = 0; i < path.Count; i++)
            if (path[i] == exitNodeId) { at = i; break; }
        if (at < 0) return null;
        int clear = -1;
        for (int i = 0; i < path.Count; i++)
            if (path[i] == branch.ClearNodeId) { clear = i; break; }
        if (clear < 0) return null;
        return (TurnsFrom(graph, axis, path, at, LeaveIndex(graph, axis, path)),
                TurnsFrom(graph, axis, path, at, clear));
    }

    // The sharpest turn from the landing heading over the strokes of path[0..to] that end after `at` - the
    // stretch from path[at] on, or the last stroke when path[at] is at or past path[to].
    private static double TurnsFrom(TaxiGraph graph, RunwayAxis axis, IReadOnlyList<int> path, int at, int to)
    {
        int from = Math.Min(at, to - 1);
        double max = 0.0;
        foreach (var (a, b) in Strokes(graph, path, 0, to))
            if (b > from) max = Math.Max(max, Math.Abs(Heading(graph, axis, path[a], path[b])));
        return max;
    }

    private static double NodeDistance(TaxiGraph graph, int a, int b)
    {
        var x = graph.Nodes[a];
        var y = graph.Nodes[b];
        return TaxiGraph.FastDistanceMeters(x.Latitude, x.Longitude, y.Latitude, y.Longitude);
    }

    // Heading of the step a→b relative to the landing heading, degrees, signed (+ right).
    private static double Heading(TaxiGraph graph, RunwayAxis axis, int a, int b)
    {
        var x = graph.Nodes[a];
        var y = graph.Nodes[b];
        return axis.RelativeHeadingDeg(x.Latitude, x.Longitude, y.Latitude, y.Longitude);
    }

    // Never a fabricated stand bridge or a stand lead-in ("P"): neither is part of a way off the runway.
    private static IEnumerable<TaxiEdge> Walkable(TaxiGraph graph, int nodeId)
    {
        if (!graph.Adjacency.TryGetValue(nodeId, out var edges)) yield break;
        foreach (var e in edges)
        {
            if (TaxiGraph.IsStandBridge(e)) continue;
            if (TaxiGraph.IsParkingLeadIn(e)) continue;
            if (!graph.Nodes.ContainsKey(e.ToNodeId)) continue;
            yield return e;
        }
    }

    // The number of distinct neighbours Walkable reaches from `nodeId`, whatever their names.
    private static int WalkableDegree(TaxiGraph graph, int nodeId)
    {
        // Counted up to 3: every caller asks "at most two", "exactly two" or "three or more". Node ids are
        // positive (0 is TaxiGraph's "not set"), so -1 marks an empty slot.
        int first = -1, second = -1;
        foreach (var e in Walkable(graph, nodeId))
        {
            int n = e.ToNodeId;
            if (n == first || n == second) continue;
            if (first < 0) first = n;
            else if (second < 0) second = n;
            else return 3;
        }
        return second >= 0 ? 2 : first >= 0 ? 1 : 0;
    }

    private static double Lateral(TaxiGraph graph, RunwayAxis axis, int nodeId)
    {
        var n = graph.Nodes[nodeId];
        return axis.Project(n.Latitude, n.Longitude).LateralMetres;
    }

    private static double Along(TaxiGraph graph, RunwayAxis axis, int nodeId)
    {
        var n = graph.Nodes[nodeId];
        return axis.Project(n.Latitude, n.Longitude).AlongMetres;
    }
}
