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

    /// <summary>The margin <c>GetLandingExits</c> adds to the half-width for its exit corridor.</summary>
    public const double CorridorMarginMetres = 15.0;

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
        double h = HeadingTrueDeg * Math.PI / 180.0;
        double cosH = Math.Cos(h), sinH = Math.Sin(h);
        return (dE * sinH + dN * cosH, dE * cosH - dN * sinH);
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
/// 0–180°. Sets the exit's angle (capped at 90°).</param>
/// <param name="TurnToLeaveDeg">Sharpest turn from the landing heading along <paramref name="Path"/> up to and
/// including the first node beyond the runway half-width: how the branch LEAVES the pavement, 0–180°. Decides
/// <see cref="IsTurnaround"/>.</param>
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
    /// <summary>How far past the clear point the sibling search looks for a Y-exit's other arm.</summary>
    public const double SiblingSearchMaxMetres = 150.0;
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
        var branch = MeasureFrom(graph, axis, inward, junction == candidateNodeId ? seedNeighborId : null, nameFilter);
        if (branch.IsMeasured || junction == candidateNodeId) return branch;
        // Nothing leaves the runway from the candidate itself — it sits on a lead-in line beside the
        // junction. Measure the junction's own branch instead.
        return MeasureFrom(graph, axis, new List<int> { junction }, null, nameFilter);
    }

    /// <summary>
    /// The other arm of a Y-shaped exit whose <paramref name="backward"/> arm is a turnaround: an arm that
    /// reaches the same off-runway point from a different junction, carries no other taxiway's name, and
    /// is itself not a turnaround. Null when there is none.
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
        // The inward walk may END on the backward arm's own junction - a Y whose two arms leave the
        // runway from one node (KMIA 08R Z, ULWB 33) - but never uses the backward arm's other nodes.
        var ownBeyondJunction = new HashSet<int>(own);
        ownBeyondJunction.Remove(backward.JunctionNodeId);
        int side = Math.Sign(Lateral(graph, axis, backward.ClearNodeId));
        double backwardAlong = Along(graph, axis, backward.JunctionNodeId);
        bool OnThisSideOffTheRunway(int nodeId)
        {
            double lateral = Lateral(graph, axis, nodeId);
            return Math.Sign(lateral) == side && Math.Abs(lateral) > axis.HalfWidthMetres;
        }
        // Both the outward flood that finds candidate start nodes and the inward walk that measures
        // each one stay ON exitName's own taxiway (or unnamed pavement): a physically-nearby but
        // differently-named taxiway is a different exit system, not this one's other arm, however
        // close its own pavement sits to this exit's clear point.
        foreach (int start in NodesOutwardFrom(graph, backward.ClearNodeId, own, exitName, OnThisSideOffTheRunway))
        {
            var inward = WalkToJunction(graph, axis, start, ownBeyondJunction, exitName);
            int junction = inward[0];
            if (junction == start) continue;
            if (junction != backward.JunctionNodeId && own.Contains(junction)) continue;
            if (Math.Abs(Lateral(graph, axis, junction)) > axis.HalfWidthMetres) continue;
            if (Math.Abs(Along(graph, axis, junction) - backwardAlong) > SiblingJunctionMaxMetres) continue;
            // Covers the WHOLE arm (junction..start), not just junction..clear — a name change beyond
            // the clear point (still inside `inward`, out toward `start`) belongs to a different
            // taxiway just as much as one before it, even though it plays no part in `path` below.
            if (!IsNamedLike(graph, inward, exitName)) continue;
            int clearIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.ClearLateralMetres);
            if (clearIdx < 0) continue;
            var path = inward.GetRange(0, clearIdx + 1);
            // The sibling must not itself be a turnaround - judged, like every branch, by how it
            // leaves the runway pavement (LandingExitBranch.IsTurnaround).
            double leave = TurnToLeave(graph, axis, path);
            if (leave > RolloutExitGate.TurnaroundAboveDeg) continue;
            double turn = TurnAlong(graph, axis, path);
            int corridorIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.CorridorLateralMetres);
            int corridor = corridorIdx >= 0 ? inward[corridorIdx] : backward.CorridorNodeId;
            return new LandingExitBranch(junction, path[^1], corridor, turn, leave, path);
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
    /// that are unnamed or carry that name (<see cref="StringComparison.OrdinalIgnoreCase"/>; an empty
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

    private static LandingExitBranch MeasureFrom(
        TaxiGraph graph, RunwayAxis axis, List<int> inward, int? seedNeighborId, string? nameFilter)
    {
        int junction = inward[0];
        int candidate = inward[^1];
        var (clearNode, corridorNode, parents) =
            SearchOutward(graph, axis, candidate, new HashSet<int>(inward), seedNeighborId, nameFilter);

        int clearIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.ClearLateralMetres);
        List<int> path;
        if (clearIdx >= 0)
        {
            path = inward.GetRange(0, clearIdx + 1);
        }
        else
        {
            if (clearNode < 0) return new LandingExitBranch(junction, -1, corridorNode, 0.0, 0.0, inward);
            path = new List<int>(inward);
            path.AddRange(ChainFrom(parents, candidate, clearNode));
        }
        return new LandingExitBranch(junction, path[^1], corridorNode,
            TurnAlong(graph, axis, path), TurnToLeave(graph, axis, path), path);
    }

    // Dijkstra by path length from `from`, never entering `excluded` (the inward path) and - when
    // `nameFilter` is set - following only edges that are unnamed or carry that name. Returns the first
    // node beyond the clear boundary and the first beyond the corridor boundary (-1 when none in reach).
    private static (int Clear, int Corridor, Dictionary<int, int> Parents) SearchOutward(
        TaxiGraph graph, RunwayAxis axis, int from, HashSet<int> excluded, int? seedNeighborId, string? nameFilter)
    {
        var parents = new Dictionary<int, int>();
        var best = new Dictionary<int, double> { [from] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        int clear = -1, corridor = -1;

        foreach (var e in Walkable(graph, from))
        {
            if (excluded.Contains(e.ToNodeId)) continue;
            if (!MatchesNameFilter(e, nameFilter)) continue;
            if (seedNeighborId.HasValue && e.ToNodeId != seedNeighborId.Value) continue;
            if (best.TryGetValue(e.ToNodeId, out double known) && known <= e.DistanceMeters) continue;
            best[e.ToNodeId] = e.DistanceMeters;
            parents[e.ToNodeId] = from;
            queue.Enqueue(e.ToNodeId, e.DistanceMeters);
        }

        var done = new HashSet<int> { from };
        while (queue.TryDequeue(out int node, out double dist))
        {
            if (!done.Add(node)) continue;
            double lateral = Math.Abs(Lateral(graph, axis, node));
            if (clear < 0 && lateral > axis.ClearLateralMetres) clear = node;
            if (corridor < 0 && lateral > axis.CorridorLateralMetres) corridor = node;
            if (clear >= 0 && corridor >= 0) break;
            if (dist >= OutwardMaxMetres) continue;
            foreach (var e in Walkable(graph, node))
            {
                if (excluded.Contains(e.ToNodeId) || done.Contains(e.ToNodeId)) continue;
                if (!MatchesNameFilter(e, nameFilter)) continue;
                double next = dist + e.DistanceMeters;
                if (best.TryGetValue(e.ToNodeId, out double known) && known <= next) continue;
                best[e.ToNodeId] = next;
                parents[e.ToNodeId] = node;
                queue.Enqueue(e.ToNodeId, next);
            }
        }
        return (clear, corridor, parents);
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

    // True when `nameFilter` is unset, or `e` is unnamed, or `e` carries exactly that name.
    private static bool MatchesNameFilter(TaxiEdge e, string? nameFilter)
    {
        if (nameFilter == null) return true;
        string name = e.TaxiwayName ?? "";
        return name.Length == 0 || string.Equals(name, nameFilter, StringComparison.OrdinalIgnoreCase);
    }

    // Whole-arm name check: every edge from path[0] to path[^1] must be unnamed or carry exitName.
    private static bool IsNamedLike(TaxiGraph graph, List<int> path, string exitName)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            string edgeName = EdgeName(graph, path[i], path[i + 1]);
            if (edgeName.Length > 0 && !string.Equals(edgeName, exitName ?? "", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static string EdgeName(TaxiGraph graph, int from, int to)
    {
        if (graph.Adjacency.TryGetValue(from, out var edges))
            foreach (var e in edges)
                if (e.ToNodeId == to) return e.TaxiwayName ?? "";
        return "";
    }

    private static double TurnAlong(TaxiGraph graph, RunwayAxis axis, IReadOnlyList<int> path)
    {
        double max = 0.0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var a = graph.Nodes[path[i]];
            var b = graph.Nodes[path[i + 1]];
            max = Math.Max(max, Math.Abs(axis.RelativeHeadingDeg(a.Latitude, a.Longitude, b.Latitude, b.Longitude)));
        }
        return max;
    }

    // The sharpest turn along `path` up to and including its first node beyond the runway half-width:
    // how the branch leaves the pavement (a measured path always reaches one - its clear node is beyond).
    private static double TurnToLeave(TaxiGraph graph, RunwayAxis axis, IReadOnlyList<int> path)
    {
        double max = 0.0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var a = graph.Nodes[path[i]];
            var b = graph.Nodes[path[i + 1]];
            max = Math.Max(max, Math.Abs(axis.RelativeHeadingDeg(a.Latitude, a.Longitude, b.Latitude, b.Longitude)));
            if (Math.Abs(Lateral(graph, axis, path[i + 1])) > axis.HalfWidthMetres) break;
        }
        return max;
    }

    // Never a fabricated stand bridge or a stand lead-in ("P"): neither is part of a way off the runway.
    private static IEnumerable<TaxiEdge> Walkable(TaxiGraph graph, int nodeId)
    {
        if (!graph.Adjacency.TryGetValue(nodeId, out var edges)) yield break;
        foreach (var e in edges)
        {
            if (TaxiGraph.IsStandBridge(e)) continue;
            if (string.Equals(e.PathType, "P", StringComparison.OrdinalIgnoreCase)) continue;
            if (!graph.Nodes.ContainsKey(e.ToNodeId)) continue;
            yield return e;
        }
    }

    // The number of distinct neighbours Walkable reaches from `nodeId`, whatever their names.
    private static int WalkableDegree(TaxiGraph graph, int nodeId)
    {
        var neighbours = new HashSet<int>();
        foreach (var e in Walkable(graph, nodeId)) neighbours.Add(e.ToNodeId);
        return neighbours.Count;
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
