using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Cross runway 23R at P1": from a node on ONE side of a runway (a painted holding point, or
/// the last node of a taxiway before it meets the pavement), follows the taxi network straight
/// across and returns where the aircraft is properly on the OTHER side.
///
/// <para>Motivating case (EGCC 23R at P1, 2026-09-14). The crossing is one continuous stretch of
/// pavement that changes NAME halfway: P1 (hold node, 137 m north) → a node on the centreline →
/// DZ1 (hold node, 137 m south). No node beyond the runway carries the name P, so
/// "the far-side node on the ATC-named taxiway" — the only crossing pin Progressive Taxi had —
/// could never be found, and the holding point P1 was not offered at all. This resolver answers
/// by GEOMETRY and graph connectivity instead of by name.</para>
///
/// <para>What counts as a crossing, and the guards that keep a NON-crossing from passing:</para>
/// <list type="bullet">
/// <item>The start is off the pavement and within <see cref="MaxStartLateralM"/> of the axis.</item>
/// <item>On the start's side the path may never move more than <see cref="NearSideSlackM"/> further
/// out than the start — so it cannot run back along a parallel taxiway to a different connector.</item>
/// <item>Its along-track drift is bounded by <see cref="BaseAlongDriftM"/> plus the lateral distance
/// covered, so an angled crossing still qualifies but a neighbouring crossing hundreds of metres
/// away does not.</item>
/// <item>An edge that stays on the pavement while running ALONG the runway is never taken — a
/// runway entry at a threshold must not "cross" by taxiing down the runway to a real crossing.</item>
/// <item>The point where the path passes the centreline must lie within the runway's length
/// (±<see cref="AlongBufferM"/>): passing the EXTENDED centreline beyond a threshold is not crossing.</item>
/// <item>Never onto another runway's pavement, never back to the start's side once across.</item>
/// </list>
///
/// <para>The answer is "properly across": the first far-side node at the holding distance
/// (<see cref="RunwayVacateResolver.VacatedClearanceMetres"/>, or the legacy setback when larger),
/// or the far side's own hold line when that comes first — then, where the far side's painted
/// line for this runway is a short way further on, one hop past it so the tail is clear of it too.
/// Stopping between the runway and the far hold line leaves the aircraft in the area the line protects.</para>
///
/// <para>Pure (graph + runway in, node id out) — unit-tested on synthetic graphs.</para>
/// </summary>
public static class RunwayCrossingResolver
{
    /// <summary>A start further than this from the centreline is not "at" the runway.</summary>
    public const double MaxStartLateralM = 250.0;

    /// <summary>Along-track tolerance past either runway end for the centreline crossing point.</summary>
    public const double AlongBufferM = 50.0;

    private const double BaseAlongDriftM = 150.0;
    private const double NearSideSlackM = 30.0;
    private const double MaxPathM = 800.0;
    private const double OffPavementMarginM = 5.0;
    private const double FarHoldLookaheadM = 150.0;
    private const double PastHoldMarginM = 60.0;
    private const double PastHoldMaxStepM = 150.0;
    private const int MaxLookaheadHops = 8;

    /// <summary>A centreline whose both endpoints lie this close to the runway's axis is the runway itself.</summary>
    private const double SameRunwayLateralM = 30.0;

    /// <param name="StartNodeId">The node the crossing was resolved from.</param>
    /// <param name="FarNodeId">Where the aircraft is properly across.</param>
    /// <param name="CrossingAlongM">Along-track metres (from the runway start) where the path passes the centreline.</param>
    /// <param name="PathMeters">Graph distance from the start to the first qualifying far-side node.</param>
    /// <param name="NearSideNodeIds">Off-pavement nodes the path passes on the START's side before
    /// reaching the runway (start excluded) — lets a caller see that a hold further back crosses
    /// only THROUGH a nearer painted line, and so is not itself the crossing point.</param>
    public readonly record struct Crossing(
        int StartNodeId, int FarNodeId, double CrossingAlongM, double PathMeters,
        IReadOnlyList<int> NearSideNodeIds);

    /// <summary>True when the path passes a scenery hold-short node on the start's side.</summary>
    public static bool PassesNearSideHold(TaxiGraph graph, Crossing crossing)
        => crossing.NearSideNodeIds.Any(id => graph.Nodes.TryGetValue(id, out var n) && IsHold(n));

    /// <summary>
    /// Resolves the crossing from <paramref name="startNodeId"/>, or null when the taxi network
    /// offers no genuine crossing of <paramref name="runway"/> from there.
    /// </summary>
    public static Crossing? FindAcross(TaxiGraph? graph, Runway? runway, int startNodeId)
    {
        if (graph == null || runway == null) return null;
        if (!graph.Nodes.TryGetValue(startNodeId, out var start)) return null;

        var frame = RunwayFrame.For(runway, runway.StartLat);
        double offPavement = HoldShortNodeResolver.TrueHalfWidthMetres(runway) + OffPavementMarginM;
        double target = Math.Max(HoldShortNodeResolver.LegacySetbackMetres(runway),
                                 RunwayVacateResolver.VacatedClearanceMetres);

        double ct0 = frame.SignedCrossTrack(start.Latitude, start.Longitude);
        double along0 = frame.Along(start.Latitude, start.Longitude);
        if (Math.Abs(ct0) < offPavement || Math.Abs(ct0) > MaxStartLateralM) return null;
        int side = Math.Sign(ct0);

        var otherRunways = graph.RunwayCenterlines
            .Where(cl => !(Math.Abs(frame.SignedCrossTrack(cl.Lat1, cl.Lon1)) <= SameRunwayLateralM
                        && Math.Abs(frame.SignedCrossTrack(cl.Lat2, cl.Lon2)) <= SameRunwayLateralM))
            .ToList();

        var dist = new Dictionary<int, double> { [startNodeId] = 0.0 };
        var prev = new Dictionary<int, int>();
        var crossedAt = new Dictionary<int, double>();   // present once the best path to a node has crossed
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(startNodeId, 0.0);

        while (queue.TryDequeue(out int cur, out double d))
        {
            if (d > dist.GetValueOrDefault(cur, double.MaxValue)) continue;
            var n = graph.Nodes[cur];
            double ct = frame.SignedCrossTrack(n.Latitude, n.Longitude);
            double along = frame.Along(n.Latitude, n.Longitude);

            if (crossedAt.TryGetValue(cur, out double crossAlong))
            {
                double farProgress = -side * ct;
                bool ownHold = IsHold(n) && HoldBelongsTo(n, runway);
                bool otherHold = IsHold(n) && !ownHold;
                if (farProgress >= target || (IsHold(n) && farProgress >= offPavement))
                {
                    int cameFrom = prev.GetValueOrDefault(cur);
                    int settled;
                    if (ownHold)
                        settled = HopPastHold(graph, frame, side, cur, cameFrom, otherRunways);
                    else if (otherHold)
                        // Another runway's line: the node before it is where to stop, provided
                        // that node is itself clear of this runway's pavement; otherwise the
                        // line is the best available and the next leg's hold-short owns it.
                        settled = graph.Nodes.TryGetValue(cameFrom, out var before)
                                  && FarProgress(before, frame, side) >= offPavement
                            ? cameFrom : cur;
                    else
                        settled = LookaheadPastOwnHold(graph, frame, side, cur, cameFrom, runway, otherRunways);
                    var nearSide = new List<int>();
                    for (int id = cur; prev.TryGetValue(id, out int p); id = p)
                    {
                        if (p == startNodeId) break;
                        var pn = graph.Nodes[p];
                        if (side * frame.SignedCrossTrack(pn.Latitude, pn.Longitude) > offPavement)
                            nearSide.Add(p);
                    }
                    nearSide.Reverse();
                    return new Crossing(startNodeId, settled, crossAlong, d, nearSide);
                }
            }

            if (!graph.Adjacency.TryGetValue(cur, out var edges)) continue;
            foreach (var e in edges)
            {
                if (IsRunwayPath(e)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var cand)) continue;
                if (cand.Type == TaxiNodeType.Parking) continue;

                double cct = frame.SignedCrossTrack(cand.Latitude, cand.Longitude);
                double calong = frame.Along(cand.Latitude, cand.Longitude);

                // Never wander further out on the start's side than the start itself.
                if (side * cct > side * ct0 + NearSideSlackM) continue;
                // Nor further out on the far side than any runway hold could sit.
                if (-side * cct > MaxStartLateralM + NearSideSlackM) continue;
                // A crossing goes ACROSS: drift along the runway is bounded by the lateral covered.
                if (Math.Abs(calong - along0) > BaseAlongDriftM + Math.Abs(cct - ct0)) continue;
                // Staying on the pavement while running along it is taxiing down the runway.
                if (Math.Abs(ct) <= offPavement && Math.Abs(cct) <= offPavement
                    && Math.Abs(calong - along) > Math.Max(30.0, 1.5 * Math.Abs(cct - ct))) continue;
                if (OnOtherRunway(cand, otherRunways)) continue;

                double? crossing = crossedAt.TryGetValue(cur, out double ca) ? ca : null;
                if (crossing == null && side * ct > 0 && side * cct <= 0)
                {
                    double t = ct / (ct - cct);
                    double at = along + t * (calong - along);
                    if (at < -AlongBufferM || at > frame.LengthM + AlongBufferM) continue;
                    crossing = at;
                }
                // Once across, never back to the start's side.
                if (crossing != null && side * cct > 0.5) continue;

                double nd = d + e.DistanceMeters;
                if (nd > MaxPathM) continue;
                if (nd >= dist.GetValueOrDefault(e.ToNodeId, double.MaxValue)) continue;

                dist[e.ToNodeId] = nd;
                prev[e.ToNodeId] = cur;
                if (crossing != null) crossedAt[e.ToNodeId] = crossing.Value;
                else crossedAt.Remove(e.ToNodeId);
                queue.Enqueue(e.ToNodeId, nd);
            }
        }

        return null;
    }

    /// <summary>
    /// From the first far-side node at holding distance, keep going outward a short way when this
    /// runway's own painted line on the far side is just ahead, and settle past it.
    /// </summary>
    private static int LookaheadPastOwnHold(
        TaxiGraph graph, RunwayFrame frame, int side, int node, int cameFrom, Runway runway,
        List<TaxiGraph.RunwayCenterline> otherRunways)
    {
        int cur = node, from = cameFrom;
        double walked = 0.0;
        var seen = new HashSet<int> { node };
        for (int hop = 0; hop < MaxLookaheadHops; hop++)
        {
            if (!graph.Adjacency.TryGetValue(cur, out var edges)) break;
            double curProgress = FarProgress(graph.Nodes[cur], frame, side);
            TaxiEdge? pick = null;
            double pickProgress = curProgress + 0.5;
            foreach (var e in edges)
            {
                if (e.ToNodeId == from || seen.Contains(e.ToNodeId) || IsRunwayPath(e)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var cand)) continue;
                if (cand.Type == TaxiNodeType.Parking || OnOtherRunway(cand, otherRunways)) continue;
                double p = FarProgress(cand, frame, side);
                if (p > pickProgress) { pick = e; pickProgress = p; }
            }
            if (pick == null || walked + pick.DistanceMeters > FarHoldLookaheadM) break;
            if (pickProgress > MaxStartLateralM + NearSideSlackM) break;

            walked += pick.DistanceMeters;
            from = cur;
            cur = pick.ToNodeId;
            seen.Add(cur);

            var cn = graph.Nodes[cur];
            if (IsHold(cn))
                return HoldBelongsTo(cn, runway)
                    ? HopPastHold(graph, frame, side, cur, from, otherRunways)
                    : from;   // another runway's line: settle before it, never on or past it
        }
        return node;   // no own hold line within reach — the holding-distance node stands
    }

    /// <summary>
    /// One hop outward past a hold line so the whole airframe clears it: the most outward
    /// neighbour within <see cref="PastHoldMarginM"/>, else the nearest outward neighbour within
    /// <see cref="PastHoldMaxStepM"/>. A crossing that ends ON the line leaves the tail over it —
    /// EDDF 25C L16 → M28, where the next node is 70-odd metres beyond the paint. Never onto a hold
    /// line or parking, and never onto another runway; with nothing eligible it stays on the line.
    /// </summary>
    private static int HopPastHold(
        TaxiGraph graph, RunwayFrame frame, int side, int hold, int cameFrom,
        List<TaxiGraph.RunwayCenterline> otherRunways)
    {
        if (!graph.Adjacency.TryGetValue(hold, out var edges)) return hold;
        double holdProgress = FarProgress(graph.Nodes[hold], frame, side);
        int shortPick = hold; double shortBest = holdProgress;
        int longPick = hold; double longDist = double.MaxValue;
        foreach (var e in edges)
        {
            if (e.ToNodeId == cameFrom || e.DistanceMeters > PastHoldMaxStepM || IsRunwayPath(e)) continue;
            if (!graph.Nodes.TryGetValue(e.ToNodeId, out var cand)) continue;
            if (cand.Type == TaxiNodeType.Parking || IsHold(cand) || OnOtherRunway(cand, otherRunways)) continue;
            double p = FarProgress(cand, frame, side);
            if (p <= holdProgress + 0.5) continue;
            if (e.DistanceMeters <= PastHoldMarginM)
            {
                if (p > shortBest) { shortBest = p; shortPick = e.ToNodeId; }
            }
            else if (e.DistanceMeters < longDist)
            {
                longDist = e.DistanceMeters; longPick = e.ToNodeId;
            }
        }
        return shortPick != hold ? shortPick : longPick;
    }

    private static double FarProgress(TaxiNode n, RunwayFrame frame, int side)
        => -side * frame.SignedCrossTrack(n.Latitude, n.Longitude);

    private static bool IsRunwayPath(TaxiEdge e)
        => string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase);

    private static bool IsHold(TaxiNode n)
        => n.Type == TaxiNodeType.HoldShort || n.Type == TaxiNodeType.ILSHoldShort;

    /// <summary>An unnamed hold, or one named for this runway (either end), belongs to it.</summary>
    private static bool HoldBelongsTo(TaxiNode n, Runway runway)
    {
        string? des = RouteRunwayCrossings.ExtractRunwayDesignator(n.HoldShortName);
        if (des == null) return true;
        string mine = RouteRunwayCrossings.NormalizeDesignator((runway.RunwayID ?? "").Trim());
        if (mine.Length == 0) return true;
        return des.Equals(mine, StringComparison.OrdinalIgnoreCase)
            || RouteRunwayCrossings.Reciprocal(des).Equals(mine, StringComparison.OrdinalIgnoreCase);
    }

    private static bool OnOtherRunway(TaxiNode n, List<TaxiGraph.RunwayCenterline> otherRunways)
    {
        foreach (var cl in otherRunways)
        {
            double half = cl.PavementHalfWidthMeters > 0 ? cl.PavementHalfWidthMeters : cl.HalfWidthMeters;
            if (TaxiGraph.PerpendicularDistanceMetersStatic(
                    n.Latitude, n.Longitude, cl.Lat1, cl.Lon1, cl.Lat2, cl.Lon2) <= half)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Every taxiway name that genuinely carries a crossing of <paramref name="runway"/>: an edge
    /// that spans the centreline, AND — the EGCC P/DZ shape — a taxiway that ENDS on a node sitting
    /// on the centreline while another taxiway continues from that node to the far side. The old
    /// sign-change test alone saw only the far-side half (the P1 → centreline edge lies wholly on
    /// one side, 0.4 m short of the axis), so it offered DZ and never P.
    /// </summary>
    public static List<string> TaxiwaysCrossing(TaxiGraph? graph, Runway? runway)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (graph == null || runway == null) return new List<string>();

        var frame = RunwayFrame.For(runway, runway.StartLat);
        double offPavement = HoldShortNodeResolver.TrueHalfWidthMetres(runway) + OffPavementMarginM;
        const double ClusterAlongM = 60.0;

        bool InRunwayLength(double along) => along >= -AlongBufferM && along <= frame.LengthM + AlongBufferM;

        // 1. Edges spanning the centreline (the original rule, unchanged).
        foreach (var edges in graph.Adjacency.Values)
        {
            foreach (var edge in edges)
            {
                if (string.IsNullOrEmpty(edge.TaxiwayName) || names.Contains(edge.TaxiwayName)) continue;
                if (!graph.Nodes.TryGetValue(edge.FromNodeId, out var a)) continue;
                if (!graph.Nodes.TryGetValue(edge.ToNodeId, out var b)) continue;
                double ctA = frame.SignedCrossTrack(a.Latitude, a.Longitude);
                double ctB = frame.SignedCrossTrack(b.Latitude, b.Longitude);
                if (Math.Sign(ctA) == Math.Sign(ctB)) continue;
                double alongMid = (frame.Along(a.Latitude, a.Longitude) + frame.Along(b.Latitude, b.Longitude)) / 2.0;
                if (!InRunwayLength(alongMid)) continue;
                names.Add(edge.TaxiwayName);
            }
        }

        // 2. Pavement junctions: nodes ON the runway (plus pavement neighbours a short way
        //    along) with taxiways leaving to BOTH sides — every one of those taxiways crosses.
        foreach (var n in graph.Nodes.Values)
        {
            double ct = frame.SignedCrossTrack(n.Latitude, n.Longitude);
            if (Math.Abs(ct) > offPavement) continue;
            double along = frame.Along(n.Latitude, n.Longitude);
            if (!InRunwayLength(along)) continue;

            var left = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var right = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<int>();
            var seen = new HashSet<int> { n.NodeId };
            stack.Push(n.NodeId);
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                if (!graph.Adjacency.TryGetValue(id, out var edges)) continue;
                foreach (var e in edges)
                {
                    if (IsRunwayPath(e) || !graph.Nodes.TryGetValue(e.ToNodeId, out var m)) continue;
                    double mct = frame.SignedCrossTrack(m.Latitude, m.Longitude);
                    if (Math.Abs(mct) > offPavement)
                    {
                        if (string.IsNullOrEmpty(e.TaxiwayName)) continue;
                        (mct > 0 ? left : right).Add(e.TaxiwayName);
                    }
                    else if (Math.Abs(frame.Along(m.Latitude, m.Longitude) - along) <= ClusterAlongM
                             && seen.Add(m.NodeId))
                    {
                        stack.Push(m.NodeId);
                    }
                }
            }
            if (left.Count > 0 && right.Count > 0)
            {
                names.UnionWith(left);
                names.UnionWith(right);
            }
        }

        var list = names.ToList();
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Nodes on <paramref name="taxiwayName"/> from which that taxiway reaches the runway on the
    /// side with sign <paramref name="side"/> (+1 left / −1 right of the runway heading; 0 = either):
    /// the off-pavement end of every such edge that spans the centreline or runs onto the pavement.
    /// These are the starts <see cref="FindAcross"/> resolves an ATC-named crossing taxiway from.
    /// </summary>
    public static List<int> TaxiwayCrossingStarts(TaxiGraph? graph, Runway? runway, string taxiwayName, int side)
    {
        var starts = new List<int>();
        if (graph == null || runway == null || string.IsNullOrEmpty(taxiwayName)) return starts;

        var frame = RunwayFrame.For(runway, runway.StartLat);
        double offPavement = HoldShortNodeResolver.TrueHalfWidthMetres(runway) + OffPavementMarginM;
        var seen = new HashSet<int>();

        foreach (var edges in graph.Adjacency.Values)
        {
            foreach (var e in edges)
            {
                if (!string.Equals(e.TaxiwayName, taxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!graph.Nodes.TryGetValue(e.FromNodeId, out var a)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var b)) continue;
                double ctA = frame.SignedCrossTrack(a.Latitude, a.Longitude);
                double ctB = frame.SignedCrossTrack(b.Latitude, b.Longitude);
                // `a` is the outer end, off the pavement; `b` is on the pavement or beyond the axis.
                if (Math.Abs(ctA) < offPavement || Math.Abs(ctA) > MaxStartLateralM) continue;
                if (side != 0 && Math.Sign(ctA) != side) continue;
                bool reachesPavement = Math.Abs(ctB) <= offPavement || Math.Sign(ctB) != Math.Sign(ctA);
                if (!reachesPavement) continue;
                if (seen.Add(a.NodeId)) starts.Add(a.NodeId);
            }
        }
        return starts;
    }
}
