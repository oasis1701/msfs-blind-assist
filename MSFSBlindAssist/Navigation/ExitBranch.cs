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
/// <param name="TurnToClearDeg">Sharpest turn from the landing heading along <paramref name="Path"/>, 0–180°.</param>
/// <param name="Path">Junction … clear node.</param>
public sealed record LandingExitBranch(
    int JunctionNodeId, int ClearNodeId, int CorridorNodeId, double TurnToClearDeg, IReadOnlyList<int> Path)
{
    public bool IsMeasured => ClearNodeId >= 0;
    public bool IsTurnaround => IsMeasured && TurnToClearDeg > RolloutExitGate.TurnaroundAboveDeg;
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
    /// <summary>How far the outward search follows the branch (ExitPathLeavesCorridor's own bound).</summary>
    public const double OutwardMaxMetres = 600.0;
    /// <summary>How far past the clear point the sibling search looks for a Y-exit's other arm.</summary>
    public const double SiblingSearchMaxMetres = 150.0;

    /// <summary>
    /// Measures the branch <paramref name="candidateNodeId"/> lies on. <paramref name="seedNeighborId"/>
    /// picks the side when the candidate IS the junction (a taxiway crossing the runway): the outward
    /// search's first hop is then only that neighbour.
    /// </summary>
    public static LandingExitBranch Analyze(TaxiGraph graph, RunwayAxis axis, int candidateNodeId, int? seedNeighborId = null)
    {
        var inward = WalkToJunction(graph, axis, candidateNodeId, excluded: null);
        int junction = inward[0];
        var branch = MeasureFrom(graph, axis, inward, junction == candidateNodeId ? seedNeighborId : null);
        if (branch.IsMeasured || junction == candidateNodeId) return branch;
        // Nothing leaves the runway from the candidate itself — it sits on a lead-in line beside the
        // junction. Measure the junction's own branch instead.
        return MeasureFrom(graph, axis, new List<int> { junction }, null);
    }

    /// <summary>
    /// The other arm of a Y-shaped exit whose <paramref name="backward"/> arm is a turnaround: an arm that
    /// reaches the same off-runway point from a different junction, carries no other taxiway's name, and
    /// is itself not a turnaround. Null when there is none.
    /// </summary>
    public static LandingExitBranch? FindForwardSibling(TaxiGraph graph, RunwayAxis axis, LandingExitBranch backward, string exitName)
    {
        if (!backward.IsTurnaround) return null;
        var own = new HashSet<int>(backward.Path);
        foreach (int start in NodesOutwardFrom(graph, backward.ClearNodeId, own))
        {
            var inward = WalkToJunction(graph, axis, start, own);
            int junction = inward[0];
            if (junction == start || own.Contains(junction)) continue;
            if (Math.Abs(Lateral(graph, axis, junction)) > axis.HalfWidthMetres) continue;
            int clearIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.ClearLateralMetres);
            if (clearIdx < 0) continue;
            var path = inward.GetRange(0, clearIdx + 1);
            if (!IsNamedLike(graph, path, exitName)) continue;
            double turn = TurnAlong(graph, axis, path);
            if (turn > RolloutExitGate.TurnaroundAboveDeg) continue;
            int corridorIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.CorridorLateralMetres);
            int corridor = corridorIdx >= 0 ? inward[corridorIdx] : backward.CorridorNodeId;
            return new LandingExitBranch(junction, path[^1], corridor, turn, path);
        }
        return null;
    }

    /// <summary>
    /// Junction … start. Steps toward the centerline (each step at least <see cref="InwardStepMinMetres"/>
    /// closer, the closest neighbour first) until inside the band, then walks BACK along the band
    /// (toward the landing threshold) to where the lead-in line starts. Never enters
    /// <paramref name="excluded"/>.
    /// </summary>
    internal static List<int> WalkToJunction(TaxiGraph graph, RunwayAxis axis, int startNodeId, ISet<int>? excluded)
    {
        var path = new List<int> { startNodeId };
        var visited = new HashSet<int> { startNodeId };
        int current = startNodeId;
        double walked = 0.0;

        while (path.Count <= WalkMaxHops && Math.Abs(Lateral(graph, axis, current)) > CenterlineBandMetres)
        {
            double limit = Math.Abs(Lateral(graph, axis, current)) - InwardStepMinMetres;
            TaxiEdge? best = null;
            foreach (var e in Walkable(graph, current))
            {
                if (visited.Contains(e.ToNodeId) || (excluded != null && excluded.Contains(e.ToNodeId))) continue;
                double lateral = Math.Abs(Lateral(graph, axis, e.ToNodeId));
                if (lateral <= limit) { best = e; limit = lateral; }
            }
            if (best == null || walked + best.DistanceMeters > WalkMaxMetres) break;
            walked += best.DistanceMeters;
            current = best.ToNodeId;
            path.Add(current);
            visited.Add(current);
        }

        if (Math.Abs(Lateral(graph, axis, current)) <= CenterlineBandMetres)
        {
            while (path.Count <= WalkMaxHops)
            {
                double limit = Along(graph, axis, current) - InwardStepMinMetres;
                TaxiEdge? best = null;
                foreach (var e in Walkable(graph, current))
                {
                    if (visited.Contains(e.ToNodeId) || (excluded != null && excluded.Contains(e.ToNodeId))) continue;
                    if (Math.Abs(Lateral(graph, axis, e.ToNodeId)) > CenterlineBandMetres) continue;
                    double along = Along(graph, axis, e.ToNodeId);
                    if (along <= limit) { best = e; limit = along; }
                }
                if (best == null || walked + best.DistanceMeters > WalkMaxMetres) break;
                walked += best.DistanceMeters;
                current = best.ToNodeId;
                path.Add(current);
                visited.Add(current);
            }
        }

        path.Reverse();
        return path;
    }

    private static LandingExitBranch MeasureFrom(TaxiGraph graph, RunwayAxis axis, List<int> inward, int? seedNeighborId)
    {
        int junction = inward[0];
        int candidate = inward[^1];
        var (clearNode, corridorNode, parents) =
            SearchOutward(graph, axis, candidate, new HashSet<int>(inward), seedNeighborId);

        int clearIdx = inward.FindIndex(n => Math.Abs(Lateral(graph, axis, n)) > axis.ClearLateralMetres);
        List<int> path;
        if (clearIdx >= 0)
        {
            path = inward.GetRange(0, clearIdx + 1);
        }
        else
        {
            if (clearNode < 0) return new LandingExitBranch(junction, -1, corridorNode, 0.0, inward);
            path = new List<int>(inward);
            path.AddRange(ChainFrom(parents, candidate, clearNode));
        }
        return new LandingExitBranch(junction, path[^1], corridorNode, TurnAlong(graph, axis, path), path);
    }

    // Dijkstra by path length from `from`, never entering `excluded` (the inward path). Returns the first
    // node beyond the clear boundary and the first beyond the corridor boundary (-1 when none in reach).
    private static (int Clear, int Corridor, Dictionary<int, int> Parents) SearchOutward(
        TaxiGraph graph, RunwayAxis axis, int from, HashSet<int> excluded, int? seedNeighborId)
    {
        var parents = new Dictionary<int, int>();
        var best = new Dictionary<int, double> { [from] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        int clear = -1, corridor = -1;

        foreach (var e in Walkable(graph, from))
        {
            if (excluded.Contains(e.ToNodeId)) continue;
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

    // `start` and every node within SiblingSearchMaxMetres of it that avoids `own`, nearest first.
    private static IEnumerable<int> NodesOutwardFrom(TaxiGraph graph, int start, HashSet<int> own)
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
                double next = dist + e.DistanceMeters;
                if (next > SiblingSearchMaxMetres) continue;
                if (best.TryGetValue(e.ToNodeId, out double known) && known <= next) continue;
                best[e.ToNodeId] = next;
                queue.Enqueue(e.ToNodeId, next);
            }
        }
    }

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
