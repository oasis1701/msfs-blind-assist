using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A* pathfinding on the taxi graph.
/// Supports both unconstrained (shortest path) and constrained (follow specific taxiway sequence) routing.
/// </summary>
public class TaxiRouter
{
    private readonly TaxiGraph _graph;
    private static readonly LogChannel _log = MSFSBlindAssist.Utils.Logging.Log.Channel("taxi_router");

    private static void Log(string message)
    {
        try { _log.Info(message); }
        catch { /* ignore logging failures */ }
    }

    public TaxiRouter(TaxiGraph graph)
    {
        _graph = graph;
    }

    /// <summary>
    /// Nodes no search may expand into or pick as an entry or target (a Turn pick closes the
    /// charted way it declined). Empty by default, which leaves every search unchanged.
    /// </summary>
    internal HashSet<int> BlockedNodes { get; } = new();

    /// <summary>
    /// Finds the shortest path between two nodes with no taxiway constraints.
    /// </summary>
    public TaxiRoute? FindShortestPath(int startNodeId, int endNodeId)
    {
        var path = AStarSearch(startNodeId, endNodeId, null);
        if (path == null) return null;

        return BuildRoute(path);
    }

    /// <summary>
    /// Joins two CONTIGUOUS legs (the first must END on the node the second STARTS from)
    /// into one route. Rebuilt from the combined node list rather than by appending segment
    /// lists, so cumulative/remaining distances and the turn angle AT THE JOIN are computed
    /// across the seam instead of carried over from two independently-built legs (an appended
    /// second leg would restart its cumulative distances at zero and read the join as
    /// "straight" whatever the actual turn).
    /// <para>Returns null when either leg is empty or they do not meet — callers keep their
    /// original route.</para>
    /// </summary>
    public TaxiRoute? Concatenate(TaxiRoute first, TaxiRoute second)
    {
        var ids = NodeIdsOf(first);
        var tail = NodeIdsOf(second);
        if (ids.Count < 2 || tail.Count < 2) return null;
        if (ids[^1] != tail[0]) return null;

        for (int i = 1; i < tail.Count; i++)
            ids.Add(tail[i]);

        return BuildRoute(ids);
    }

    /// <summary>Node ids a route visits, in order (start node followed by every segment end).</summary>
    private static List<int> NodeIdsOf(TaxiRoute route)
    {
        var ids = new List<int>();
        if (route.Segments.Count == 0) return ids;
        ids.Add(route.Segments[0].FromNode.NodeId);
        foreach (var seg in route.Segments)
            ids.Add(seg.ToNode.NodeId);
        return ids;
    }

    /// <summary>
    /// Finds a path that follows the specified taxiway sequence.
    /// Falls back to shortest path if the constrained route fails, with reason stored in route.
    /// </summary>
    /// <param name="destinationIsRunway">
    /// True only when the destination is a RUNWAY. The "honor the last cleared taxiway as a
    /// hold-short terminus" logic (skip the final bypass leg when the last taxiway branches off the
    /// destination) is correct hold-short behaviour for a runway, but WRONG for a gate/parking
    /// destination — there the route must continue ONTO the gate via the final connector leg. So the
    /// terminus skip is gated on this flag; gate and progressive-taxi destinations route normally.
    /// </param>
    public TaxiRoute? FindConstrainedPath(int startNodeId, int endNodeId, List<string> taxiwaySequence,
        bool destinationIsRunway = false)
    {
        // A clearance that returns to a taxiway it just left ("A, C, A") can be honoured two ways: let
        // the short leg shrink to nothing at the junction it was entered by (the historical router), or
        // leave it at a DIFFERENT junction (FindBestDetourEntry + the entry exclusion). Neither wins
        // everywhere — honouring cut OTBH 16L "A, C, A" from 9.1 to 2.9 km and ZPPP "A, B, A" from 6.2
        // to 3.9, but made KABQ's alternating "P, M, P, M" loop (2.7 → 4.0 km) — so both are built and
        // the honoured one taken unless it is more than 25 % + 100 m longer (a loop); a route that fell
        // back to shortest path loses to one that did not (VirtualPilot 2026-09-18, 100 airports).
        bool hasRepeat = false;
        if (taxiwaySequence != null)
            for (int i = 0; i + 2 < taxiwaySequence.Count && !hasRepeat; i++)
                hasRepeat = string.Equals(taxiwaySequence[i], taxiwaySequence[i + 2], StringComparison.OrdinalIgnoreCase);
        var plain = BuildConstrainedPath(startNodeId, endNodeId, taxiwaySequence!, destinationIsRunway, honourRepeats: false);
        if (!hasRepeat) return plain;
        var honoured = BuildConstrainedPath(startNodeId, endNodeId, taxiwaySequence!, destinationIsRunway, honourRepeats: true);
        if (honoured == null) return plain;
        if (plain == null) return honoured;
        bool plainFellBack = plain.ConstrainedFallbackReason != null, honouredFellBack = honoured.ConstrainedFallbackReason != null;
        // Honour the clearance as given unless that makes it loop: a genuine bypass ("A, B, A" around
        // part of A) is legitimately a little longer and must still use B.
        // A plain route that already DRIVES every cleared taxiway in order satisfies the clearance —
        // the repeated leg is just a short piece along the way — and honouring it again only adds a
        // detour (KPIT "V, D, V": +385 m). Otherwise the honoured route wins unless it loops.
        var pick = plainFellBack != honouredFellBack
            ? (plainFellBack ? honoured : plain)
            : VisitsInOrder(plain, taxiwaySequence!) ? plain
            : (honoured.TotalDistanceMeters <= plain.TotalDistanceMeters * REPEAT_HONOUR_MAX_RATIO + REPEAT_HONOUR_MAX_PAD_M ? honoured : plain);
        Log($"Repeated taxiway in clearance: plain {plain.TotalDistanceMeters:F0} m, honoured {honoured.TotalDistanceMeters:F0} m -> {(ReferenceEquals(pick, honoured) ? "honoured" : "plain")}");
        return pick;
    }

    /// <summary>
    /// One reading of the clearance. Each bridge leg is refined as it is joined to the route: it joins
    /// the next cleared taxiway where it first reaches it (<see cref="JoinWhereTheBridgeFirstReaches"/>),
    /// and an out-and-back or loop where it meets the route is taken out (<see cref="AppendBridgeLeg"/>).
    /// When a leg was refined, the route is built again with every bridge leg as scored, and that one
    /// is kept where the refining was not needed (<see cref="KeepAsScored"/>). An earlier join moves
    /// every junction picked after it: where a later taxiway is drawn in pieces the new junction can
    /// have no leg along it (KNZY "B2, A3, A": the join landed on the A3/A junction, from which A has
    /// none, and the route fell back), and between two bridges a short taxiway can be left at the node
    /// it was joined at (KABQ "R, Q, P, M, B": Q no longer driven, 35 m longer, over E).
    /// </summary>
    private TaxiRoute? BuildConstrainedPath(int startNodeId, int endNodeId, List<string> taxiwaySequence,
        bool destinationIsRunway, bool honourRepeats)
    {
        var route = FindConstrainedPathCore(startNodeId, endNodeId, taxiwaySequence, destinationIsRunway, honourRepeats,
            refineBridgeLegs: true, out int refined);
        if (refined == 0 || route == null) return route;
        var asScored = FindConstrainedPathCore(startNodeId, endNodeId, taxiwaySequence, destinationIsRunway, honourRepeats,
            refineBridgeLegs: false, out _);
        if (asScored == null) return route;
        bool keep = route.ConstrainedFallbackReason != null
            ? asScored.ConstrainedFallbackReason == null
            : KeepAsScored(route, asScored, taxiwaySequence, endNodeId);
        Log($"Bridge legs refined: {route.TotalDistanceMeters:F0} m{(route.ConstrainedFallbackReason != null ? " (fell back)" : "")}, " +
            $"as scored {asScored.TotalDistanceMeters:F0} m -> {(keep ? "as scored" : "refined")}");
        return keep ? asScored : route;
    }

    /// <summary>
    /// The route built with each bridge leg as scored stands, over the refined one, only where it
    /// needed no refining: it follows the clearance, passes no node twice (an out-and-back or a loop,
    /// <see cref="PassesANodeTwice"/>), and drives every cleared taxiway the refined route drives.
    /// Driving more of them, it stands within the limit a repeated taxiway is honoured within (25 % +
    /// 100 m longer), even where it then ends short of the destination: a last cleared taxiway leading
    /// away is the end of the route ([RTE-17]), and the refined route had not driven it (EBBR "E4, T"
    /// to 01: T never driven, 1.7 km of other taxiways to the runway). Driving the same ones, it stands
    /// only if it reaches the destination whenever the refined one does (else it is shorter by
    /// stopping short), and has no more metres of taxiways the clearance never named and no more
    /// metres in all.
    /// </summary>
    private static bool KeepAsScored(TaxiRoute refined, TaxiRoute asScored, List<string> clearance, int endNodeId)
    {
        if (asScored.ConstrainedFallbackReason != null || PassesANodeTwice(NodeIdsOf(asScored))) return false;
        var cleared = new HashSet<string>(clearance, StringComparer.OrdinalIgnoreCase);
        HashSet<string> Driven(TaxiRoute r) =>
            new(r.Segments.Select(s => s.TaxiwayName).Where(cleared.Contains), StringComparer.OrdinalIgnoreCase);
        double Uncleared(TaxiRoute r) =>
            r.Segments.Where(s => s.TaxiwayName.Length > 0 && !cleared.Contains(s.TaxiwayName)).Sum(s => s.DistanceMeters);
        var byRefined = Driven(refined);
        var byScored = Driven(asScored);
        if (!byScored.IsSupersetOf(byRefined)) return false;
        if (byScored.Count > byRefined.Count)
            return asScored.TotalDistanceMeters <= refined.TotalDistanceMeters * REPEAT_HONOUR_MAX_RATIO + REPEAT_HONOUR_MAX_PAD_M;
        if (refined.Segments[^1].ToNode.NodeId == endNodeId && asScored.Segments[^1].ToNode.NodeId != endNodeId) return false;
        return Uncleared(asScored) <= Uncleared(refined) + 1.0
            && asScored.TotalDistanceMeters <= refined.TotalDistanceMeters + 1.0;
    }

    private TaxiRoute? FindConstrainedPathCore(int startNodeId, int endNodeId, List<string> taxiwaySequence,
        bool destinationIsRunway, bool honourRepeats, bool refineBridgeLegs, out int bridgeLegsRefined)
    {
        bridgeLegsRefined = 0;
        // The last leg joined to the route was a bridge: the next leg's seam is a bridge's too.
        bool afterBridge = false;
        // Where the leg the next bridge leaves from begins in the route (AppendBridgeLeg's floor).
        int bridgeFloor = 0;
        // Append a session header. Size-capping/rotation is now handled by the
        // shared LogWriter (5 MB cap, 3-file retention) rather than a hand-rolled
        // per-call truncate, so a recalc can never wipe a prior build's entry
        // (the old truncate-per-run blinded debugging of the KIAH 2026-06-10 6 km loop).
        Log("=== Constrained Route ===");

        var startN = _graph.Nodes[startNodeId];
        var endN = _graph.Nodes[endNodeId];
        Log($"Start: node {startNodeId} ({startN.Latitude:F6}, {startN.Longitude:F6})");
        Log($"End: node {endNodeId} ({endN.Latitude:F6}, {endN.Longitude:F6})");
        Log($"Taxiway sequence: {string.Join(" → ", taxiwaySequence)}");
        Log($"Graph: {_graph.Nodes.Count} nodes, {_graph.Adjacency.Values.Sum(e => e.Count)} edges");

        if (taxiwaySequence == null || taxiwaySequence.Count == 0)
            return FindShortestPath(startNodeId, endNodeId);

        // Precompute graph distances from the final destination once per
        // FindConstrainedPath run. Used by FindBestIntersection at every
        // taxiway-transition step to score candidate intersection nodes
        // by their graph cost to the destination (not Euclidean, which
        // silently picks dead-ends). Hoisted out of the per-call site
        // because finalDestId is invariant across the entire run — saves
        // O(N) Dijkstra runs on long clearances.
        var distFromFinalDest = ComputeGraphDistancesFrom(endNodeId);

        var fullPath = new List<int>();
        int currentNode = startNodeId;

        // Step 1: Get onto the first taxiway - try multiple entry points.
        // distFromFinalDest makes the ranking score the whole route through each
        // candidate rather than just the hop onto it, so an entry that would force
        // an immediate reversal (a stub off the junction) loses to the junction.
        var candidateEntries = FindNearestNodesOnTaxiway(
            startNodeId, taxiwaySequence[0], 10, distFromFinalDest);
        if (candidateEntries.Count == 0)
        {
            string reason = $"No nodes found on taxiway '{taxiwaySequence[0]}'";
            Log($"FALLBACK: {reason}");
            return FallbackShortest(startNodeId, endNodeId, reason);
        }

        // Set when the LAST cleared taxiway is honored as the route terminus (it holds short of /
        // branches off the destination); the final "to destination" leg below is then skipped so it
        // can't bypass the cleared taxiway. Declared here because a SINGLE-taxiway sequence is itself
        // the last taxiway and sets this in the step-1 path below.
        bool lastTaxiwayTerminal = false;
        // The junction that terminal leg is entered by (see OnwardLegOverAnotherRunway).
        int terminalEntryNode = -1;

        // Determine the target for the first taxiway
        string? secondTaxiway = taxiwaySequence.Count > 1 ? taxiwaySequence[1] : null;
        int firstTarget;
        bool firstBridgedAcrossRunway = false;
        int firstBridgeEntryOnSecond = -1;
        if (secondTaxiway != null)
        {
            firstTarget = honourRepeats && taxiwaySequence.Count > 2
                          && string.Equals(taxiwaySequence[2], taxiwaySequence[0], StringComparison.OrdinalIgnoreCase)
                ? FindBestDetourEntry(startNodeId, distFromFinalDest, taxiwaySequence[0], secondTaxiway)
                : -1;
            if (firstTarget == -1)
                firstTarget = FindBestIntersection(startNodeId, endNodeId, distFromFinalDest, taxiwaySequence[0], secondTaxiway);
            if (firstTarget == -1)
            {
                // Same fallback as the inner loop: try a runway bridge before
                // bailing to whole-route shortest path. Covers ATC clearances
                // where the FIRST hop is across a runway (rare but possible —
                // e.g., entering from a connector that crosses a runway to
                // reach the named departure taxiway).
                var (exitNode, entryNode) = FindRunwayBridge(taxiwaySequence[0], secondTaxiway, startNodeId, distFromFinalDest);
                if (exitNode != -1 && entryNode != -1)
                {
                    firstTarget = exitNode;
                    firstBridgedAcrossRunway = true;
                    firstBridgeEntryOnSecond = entryNode;
                    Log($"Step 1: bridging '{taxiwaySequence[0]}' → '{secondTaxiway}' via runway crossing");
                }
                else
                {
                    string reason = $"No intersection between '{taxiwaySequence[0]}' and '{secondTaxiway}'";
                    Log($"FALLBACK: {reason}");
                    return FallbackShortest(startNodeId, endNodeId, reason);
                }
            }
        }
        else
        {
            firstTarget = FindNearestNodeOnTaxiwayToTarget(endNodeId, taxiwaySequence[0], distFromFinalDest);
            if (firstTarget == -1) firstTarget = endNodeId;

            // A SINGLE cleared taxiway is also the LAST. If its nearest-to-destination node is the
            // start node (no traversal — the destination node sits back near where we entered), force
            // traversal to its FAR end and make it the terminus, skipping the bypass leg. Otherwise
            // the step no-ops and the final unconstrained leg deviates off the cleared taxiway — the
            // aircraft, correctly on it, then reads as off-route (LFPG recalc "R1" to 26R: R1's
            // nearest-to-26R node is the R/R1 junction it starts on).
            // Runway destinations ONLY — for a gate this degenerate case means the gate sits back
            // near the entry, and the route must still continue onto the gate via the final leg
            // (routing to the taxiway's far end would strand it short of the stand).
            // The far end is picked as for a last taxiway of several (FarEndOfLastTaxiway): the
            // farthest node alone drove a link whose name also runs back to the apron down that arm.
            // Never from a node already ON the destination runway ([RTE-33], as the last taxiway of
            // several): "routing along it" from there drives away from the runway the aircraft is on
            // (KLAS "D" to 01R, loaded on 01R at D: 3.3 km away across 08L, for 2 m to the lineup point).
            if (destinationIsRunway && firstTarget == startNodeId
                && !IsOnDestinationRunway(startNodeId, endNodeId))
            {
                int far = FarEndOfLastTaxiway(taxiwaySequence[0], startNodeId, endNodeId);
                if (far != -1)
                {
                    firstTarget = far;
                    lastTaxiwayTerminal = true;
                    Log($"Single taxiway '{taxiwaySequence[0]}' branches off the destination — " +
                        $"routing along it to {far} (no bypass leg)");
                }
            }
        }

        // Try each candidate entry point
        bool entryFound = false;
        var failedEntries = new List<string>();

        foreach (int entryNode in candidateEntries)
        {
            List<int>? approachPath = null;
            if (entryNode != startNodeId)
            {
                approachPath = AStarSearch(startNodeId, entryNode, null);
                if (approachPath == null)
                {
                    failedEntries.Add($"node {entryNode}: unreachable");
                    continue;
                }
            }

            if (entryNode != firstTarget)
            {
                // Route along the taxiway, bridging disconnected sections if needed
                var taxiwayPath = AStarSearchWithGapChaining(entryNode, firstTarget, taxiwaySequence[0]);
                if (taxiwayPath == null)
                {
                    failedEntries.Add($"node {entryNode}: no path on '{taxiwaySequence[0]}' to intersection {firstTarget}");
                    continue;
                }

                if (approachPath != null)
                    fullPath.AddRange(approachPath);

                int startIdx = (fullPath.Count > 0 && taxiwayPath.Count > 0 && fullPath[^1] == taxiwayPath[0]) ? 1 : 0;
                for (int j = startIdx; j < taxiwayPath.Count; j++)
                    fullPath.Add(taxiwayPath[j]);
            }
            else
            {
                if (approachPath != null)
                    fullPath.AddRange(approachPath);
            }

            currentNode = firstTarget;
            entryFound = true;
            if (lastTaxiwayTerminal) terminalEntryNode = entryNode;
            // Where the first taxiway's own leg begins: the entry, after the approach to it.
            bridgeFloor = approachPath != null ? approachPath.Count - 1 : 0;
            Log($"Step 1 OK: '{taxiwaySequence[0]}' via node {entryNode} -> {firstTarget}");

            // A SINGLE cleared taxiway joined at the very node the route would leave it by is not
            // driven at all: the final leg goes on from the junction another way ("K4 is not on the
            // way from here"). When that taxiway is a link over ANOTHER runway towards the
            // destination, the clearance means that crossing: drive it and go on from its far end
            // (OMDB "K4" to 30R, 2026-11-23). Anything else keeps the route it had.
            // The same when the aircraft is already ON that taxiway past the junction (the entry is
            // the start node, between the junction and the far end): the route would run back down
            // the taxiway to the junction. The leg is judged whole, from the junction, and driven
            // from where the aircraft is.
            // The router is not told which way the aircraft faces, and "past the junction" is every
            // aircraft on the taxiway that is not at its exit, an aircraft heading FOR that exit
            // included. So the way on is taken only while the route it makes stays near the plain
            // one (OnwardLegKeepsTheRouteShort): a far end behind such an aircraft, with a link
            // there over the same runway, made 4.7 km of 2.8 (review of 2026-11-25).
            bool judgedFromTheAircraft = false;
            if (secondTaxiway == null && destinationIsRunway && !lastTaxiwayTerminal
                && (entryNode == firstTarget || entryNode == startNodeId)
                && !IsOnDestinationRunway(firstTarget, endNodeId))
            {
                string who = $"Single taxiway '{taxiwaySequence[0]}' " +
                    (entryNode == firstTarget ? $"(joined at {firstTarget}, the node the way on leaves it by)"
                                              : $"(the aircraft is on it at {entryNode}, the way on leaves it at {firstTarget})");
                int far = FarEndOfLastTaxiway(taxiwaySequence[0], firstTarget, endNodeId);
                var along = far != -1 ? AStarSearchWithGapChaining(firstTarget, far, taxiwaySequence[0]) : null;
                int from = along == null ? -1 : entryNode == firstTarget ? 0 : along.IndexOf(entryNode);
                if (far == -1 || along == null)
                    Log($"{who}: not driven on, it has no leg from {firstTarget} to a far end");
                else if (from == -1)
                    Log($"{who}: not judged from the aircraft, which is not between {firstTarget} and the far end {far}");
                else if (PassesANodeTwice(along.Skip(from)))
                    Log($"{who}: not driven on, its leg to the far end {far} passes a node twice (a gap in the taxiway chained through its own nodes)");
                else
                {
                    judgedFromTheAircraft = entryNode != firstTarget;
                    // The approach to the junction, when the route has one.
                    var approach = entryNode == firstTarget ? fullPath.ToList() : new List<int>();
                    var judged = approach.Concat(along.Skip(approach.Count > 0 ? 1 : 0)).ToList();
                    // The way on never leaves the taxiway behind the aircraft that is on it.
                    int judgedLegStart = judged.Count - along.Count;
                    var onward = OnwardLegOverAnotherRunway(judged, judgedLegStart, endNodeId, taxiwaySequence[0], who,
                        earliestLeave: judgedLegStart + from);
                    if (onward != null)
                    {
                        var made = GoOn(approach.Concat(along.Skip(approach.Count > 0 ? 1 : from)).ToList(), onward);
                        if (OnwardLegKeepsTheRouteShort(made, PlainWayOn(fullPath, firstTarget, endNodeId), who))
                        {
                            fullPath.Clear();
                            fullPath.AddRange(made);
                            currentNode = endNodeId;
                            Log($"{who} leads over another runway: driven to {onward[0]}, " +
                                $"then on to the destination ({onward.Count} nodes)");
                        }
                    }
                }
            }

            // The same taxiway JOINED SHORT of that junction and driven up to it. Where its name
            // runs on past the junction to the apron, the route joins it at the apron end, the
            // junction is still its node nearest the destination, and the way on leaves it there
            // for another crossing of the very runway it goes on over: the pilot was cleared over
            // that runway on THIS taxiway. (OMDB "K4" to 30R, 2026-11-24, live: the online names
            // carry K4 down to Z, and the route read "via K4, then K, K9 ... crossing runway 12R
            // at K9"; with navdata's own names K4 starts at K and the case above drove it.)
            // Only when the way on meets a runway this taxiway also goes on to meet: a taxiway
            // left for a way on that crosses nothing of its own keeps the route it had.
            // And the taxiway joined BEYOND that junction, on the far end's side: a fillet from the
            // crossing taxiway meets its line past the junction, the step then drives it back DOWN
            // to the junction, and the leg from there to the far end runs over ground just driven
            // (OMDB "K4" from the west apron on navdata's own names, replayed 2026-11-25: left for
            // K and K9 without a word in the log). The route is cut where it first touches that leg
            // and goes on up it from there.
            if (currentNode != endNodeId && !judgedFromTheAircraft
                && secondTaxiway == null && destinationIsRunway && !lastTaxiwayTerminal
                && entryNode != firstTarget
                && !IsOnDestinationRunway(firstTarget, endNodeId))
            {
                string who = $"Single taxiway '{taxiwaySequence[0]}' (joined at {entryNode}, the way on leaves it at {firstTarget})";
                int far = FarEndOfLastTaxiway(taxiwaySequence[0], firstTarget, endNodeId);
                var along = far != -1 ? AStarSearchWithGapChaining(firstTarget, far, taxiwaySequence[0]) : null;
                var wayOn = AStarSearch(firstTarget, endNodeId, null);
                if (far == -1 || along == null || along.Count < 2)
                    Log($"{who}: not driven on, it has no leg from {firstTarget} to a far end");
                else if (wayOn == null)
                    Log($"{who}: not driven on, no way on from {firstTarget} to compare it with");
                else
                {
                    var legIndex = new Dictionary<int, int>();
                    for (int j = 0; j < along.Count; j++) legIndex[along[j]] = j;
                    // Where the route so far first touches the leg: the junction when the taxiway was
                    // joined short of it, a node past it when it was joined on the far end's side.
                    int touch = fullPath.FindIndex(legIndex.ContainsKey);
                    var soFar = fullPath.Take(touch + 1).ToList();
                    var rest = along.Skip(legIndex[fullPath[touch]] + 1).ToList();
                    var driven = new HashSet<int>(soFar);
                    if (rest.Any(driven.Contains))
                        Log($"{who}: not driven on, its leg to the far end {far} runs over ground the route has driven");
                    else if (PassesANodeTwice(rest))
                        Log($"{who}: not driven on, its leg to the far end {far} passes a node twice (a gap in the taxiway chained through its own nodes)");
                    else
                    {
                        var judged = soFar.Concat(rest).ToList();
                        int legStart = soFar.LastIndexOf(entryNode);
                        // The way on leaves the leg past where the route joined it, never at that node.
                        var onward = OnwardLegOverAnotherRunway(judged, legStart == -1 ? touch : legStart, endNodeId,
                            taxiwaySequence[0], who, earliestLeave: soFar.Count);
                        if (onward != null && !LeadsOverARunwayTheWayOnCrosses(
                                along.Take(along.IndexOf(onward[0]) + 1).ToList(), onward, wayOn, endNodeId))
                            Log($"{who}: not driven on, the way on from {firstTarget} crosses no runway this taxiway leads over");
                        else if (onward != null)
                        {
                            var made = GoOn(judged, onward);
                            if (OnwardLegKeepsTheRouteShort(made, PlainWayOn(fullPath, firstTarget, endNodeId), who))
                            {
                                fullPath.Clear();
                                fullPath.AddRange(made);
                                currentNode = endNodeId;
                                Log($"{who} goes on over a runway the way on crossed elsewhere: driven to {onward[0]}, " +
                                    $"then on to the destination ({onward.Count} nodes)");
                            }
                        }
                    }
                }
            }
            break;
        }

        if (!entryFound)
        {
            string reason = $"No viable entry to '{taxiwaySequence[0]}'. Tried {candidateEntries.Count}: {string.Join("; ", failedEntries)}";
            Log($"FALLBACK: {reason}");
            return FallbackShortest(startNodeId, endNodeId, reason);
        }

        // If step 1 used a runway bridge to taxiwaySequence[1], do the actual
        // bridge crossing now that we've reached the exit node on taxiway[0].
        // The free A* path picks up the hold-short → runway-pavement →
        // hold-short chain naturally.
        if (firstBridgedAcrossRunway && firstBridgeEntryOnSecond != -1)
        {
            var bridge = AStarSearch(currentNode, firstBridgeEntryOnSecond, null);
            if (bridge == null)
            {
                string reason = $"Step 1 bridge from '{taxiwaySequence[0]}' to '{secondTaxiway}' unreachable";
                Log($"FALLBACK: {reason}");
                return FallbackShortest(startNodeId, endNodeId, reason);
            }
            if (refineBridgeLegs && JoinWhereTheBridgeFirstReaches(ref bridge, secondTaxiway!, "Step 1")) bridgeLegsRefined++;
            AppendBridgeLeg(fullPath, bridge, refineBridgeLegs, bridgeFloor, "Step 1", ref bridgeLegsRefined);
            currentNode = bridge[^1];
            afterBridge = true;
            Log($"Step 1 bridge OK: {bridge.Count} nodes across runway");
        }

        // Step 2: For remaining taxiways in sequence
        for (int i = 1; i < taxiwaySequence.Count; i++)
        {
            string currentTaxiway = taxiwaySequence[i];
            string? nextTaxiway = (i + 1 < taxiwaySequence.Count) ? taxiwaySequence[i + 1] : null;

            int targetNode;
            bool bridgedAcrossRunway = false;
            int bridgeEntryOnNext = -1;
            // Where this taxiway's leg begins: the floor for a bridge leaving it.
            int legStart = Math.Max(0, fullPath.Count - 1);
            if (nextTaxiway != null)
            {
                // "via A, C, A": leaving C back onto the taxiway the route just came off can never be
                // at the junction it entered C by — that gives C zero length, and the rest of the
                // clearance is then routed from the wrong place (OTBH 16L "A, C, A": C dropped, and
                // the last A leg ran 6 km to A's far end; VirtualPilot 2026-09-18). Exclude the entry
                // junction when the taxiway repeats; with no other junction, the old answer stands.
                bool returnsToPrevious = honourRepeats && string.Equals(nextTaxiway, taxiwaySequence[i - 1], StringComparison.OrdinalIgnoreCase);
                bool detourFollows = honourRepeats && i + 2 < taxiwaySequence.Count
                    && string.Equals(taxiwaySequence[i + 2], currentTaxiway, StringComparison.OrdinalIgnoreCase);
                targetNode = returnsToPrevious
                    ? FindBestIntersection(currentNode, endNodeId, distFromFinalDest, currentTaxiway, nextTaxiway, excludeNodeId: currentNode)
                    : detourFollows
                        ? FindBestDetourEntry(currentNode, distFromFinalDest, currentTaxiway, nextTaxiway)
                        : -1;
                if (targetNode == -1)
                    targetNode = FindBestIntersection(currentNode, endNodeId, distFromFinalDest, currentTaxiway, nextTaxiway);
                if (targetNode == -1)
                {
                    // No shared node. Common cause: an ATC clearance like
                    // "K14 hold short 30L M17" where K14 ends at the 30L
                    // hold-short on its side and M17 starts at the 30L
                    // hold-short on the opposite side — they don't share a
                    // graph node, but a free A* between them naturally
                    // crosses the runway. Try a runway bridge before
                    // bailing to whole-route shortest path: this preserves
                    // the user's clearance for every taxiway except the
                    // crossing itself.
                    var (exitNode, entryNode) = FindRunwayBridge(currentTaxiway, nextTaxiway, currentNode, distFromFinalDest);
                    if (exitNode != -1 && entryNode != -1)
                    {
                        targetNode = exitNode;
                        bridgedAcrossRunway = true;
                        bridgeEntryOnNext = entryNode;
                        Log($"Step {i + 1}: bridging '{currentTaxiway}' → '{nextTaxiway}' via runway crossing");
                    }
                    else
                    {
                        string reason = $"Step {i + 1}: No intersection between '{currentTaxiway}' and '{nextTaxiway}'";
                        Log($"FALLBACK: {reason}");
                        return FallbackShortest(startNodeId, endNodeId, reason);
                    }
                }
            }
            else
            {
                targetNode = FindNearestNodeOnTaxiwayToTarget(endNodeId, currentTaxiway, distFromFinalDest);
                if (targetNode == -1) targetNode = endNodeId;

                // Honor the LAST cleared taxiway. FindNearestNodeOnTaxiwayToTarget picks the node on
                // it with the smallest GRAPH distance to the destination. When the last taxiway
                // branches AWAY from a destination the PRIOR taxiway already reaches, that node is the
                // ENTRY junction we just arrived at (targetNode == currentNode): every node on the
                // last taxiway can only reach the destination by going BACK through the entry. The
                // step would then no-op (continue, below) and the final unconstrained leg would
                // bypass the cleared taxiway straight onto the runway with NO hold short. Live case:
                // EIDW "…N, N2" to 28R — taxiway N runs to the 28R threshold (4 m), N2 is a ~450 m
                // connector that only rejoins through its junction with N, so N2 was silently dropped
                // and the aircraft was guided down N onto the runway. Instead, force traversal ALONG
                // the last taxiway to the node geographically nearest the runway (its hold-short end)
                // and make THAT the route terminus — do not append the bypass leg.
                // Runway destinations ONLY (see the destinationIsRunway note on this method): for a
                // gate this skip would strand the route at the taxiway end instead of continuing onto
                // the stand via the final connector leg.
                // Not when the route has already REACHED the destination runway — the node itself or
                // the same runway's pavement: a runway bridge into this last taxiway can land on the
                // destination node (ZPPP 04L "A, B, A"), a junction can sit on the runway (OTBH 16L
                // "A, C, A"), and "routing along it" then drove away to the taxiway's far end — 2.3
                // and 6 km of detour (VirtualPilot 2026-09-18). This rule is for a clearance that stops
                // SHORT of the runway.
                if (destinationIsRunway && targetNode == currentNode && taxiwaySequence.Count > 1
                    && !IsOnDestinationRunway(currentNode, endNodeId))
                {
                    int holdEnd = FarEndOfLastTaxiway(currentTaxiway, currentNode, endNodeId);
                    if (holdEnd != -1)
                    {
                        targetNode = holdEnd;
                        lastTaxiwayTerminal = true;
                        terminalEntryNode = currentNode;
                        Log($"Last taxiway '{currentTaxiway}' branches off the destination — " +
                            $"routing along it to {holdEnd} (no bypass leg)");
                    }
                }
            }

            if (currentNode == targetNode && !bridgedAcrossRunway) continue;

            if (currentNode != targetNode)
            {
                // Route along the taxiway, bridging disconnected sections if needed
                var segment = AStarSearchWithGapChaining(currentNode, targetNode, currentTaxiway);
                if (segment == null)
                {
                    string reason = $"Step {i + 1}: No path on '{currentTaxiway}' from {currentNode} to {targetNode}";
                    Log($"FALLBACK: {reason}");
                    return FallbackShortest(startNodeId, endNodeId, reason);
                }

                Log($"Step {i + 1} OK: '{currentTaxiway}' {currentNode}->{targetNode} ({segment.Count} nodes)");

                if (afterBridge)
                    AppendBridgeLeg(fullPath, segment, refineBridgeLegs, bridgeFloor, $"Step {i + 1}", ref bridgeLegsRefined);
                else
                {
                    int si = (fullPath.Count > 0 && segment.Count > 0 && fullPath[^1] == segment[0]) ? 1 : 0;
                    for (int j = si; j < segment.Count; j++)
                        fullPath.Add(segment[j]);
                }
                afterBridge = false;

                currentNode = targetNode;
            }

            // After traversing currentTaxiway to its exit, if we're bridging
            // across a runway to nextTaxiway, free-A* through the crossing.
            // The free A* uses no taxiway constraint, so it picks up the
            // hold-short / runway-pavement / hold-short chain naturally.
            if (bridgedAcrossRunway)
            {
                var bridge = AStarSearch(currentNode, bridgeEntryOnNext, null);
                if (bridge == null)
                {
                    string reason = $"Step {i + 1}: Bridge across runway from '{currentTaxiway}' node {currentNode} to '{nextTaxiway}' node {bridgeEntryOnNext} unreachable";
                    Log($"FALLBACK: {reason}");
                    return FallbackShortest(startNodeId, endNodeId, reason);
                }
                if (refineBridgeLegs && JoinWhereTheBridgeFirstReaches(ref bridge, nextTaxiway!, $"Step {i + 1}")) bridgeLegsRefined++;
                // A taxiway with no leg of its own between two bridges keeps the first bridge's floor:
                // the two bridges make one seam, and the leg before them is the last one driven.
                if (!afterBridge) bridgeFloor = legStart;
                AppendBridgeLeg(fullPath, bridge, refineBridgeLegs, bridgeFloor, $"Step {i + 1}", ref bridgeLegsRefined);
                currentNode = bridge[^1];
                afterBridge = true;
                Log($"Step {i + 1} bridge OK: {bridge.Count} nodes across runway");
            }
        }

        // Step 3: Route from last taxiway to destination — UNLESS the last cleared taxiway was
        // honored as the terminus (it holds short of the runway; appending an unconstrained leg
        // here would bypass it onto the runway — see the last-taxiway branch above).
        //
        // A terminus is only right where the clearance really ends: at the destination runway, or on
        // a taxiway with no way on. When the last cleared taxiway instead leads over ANOTHER runway
        // towards the destination (OMDB "K, K7" to 30R, 2026-11-23: K7 crosses 12R/30L, and a
        // crossing further east is the shorter way, so K7 read as a branch), the route goes on from
        // its far end. Ending it there held the pilot at the wrong runway's line under the
        // destination's name.
        if (lastTaxiwayTerminal && currentNode != endNodeId)
        {
            string who = $"Last taxiway '{taxiwaySequence[^1]}'";
            int legStart = terminalEntryNode == -1 ? -1 : fullPath.LastIndexOf(terminalEntryNode);
            if (legStart == -1) Log($"{who}: stays the end of the route, the junction it was entered by is not on the route");
            var onward = legStart == -1 ? null
                : OnwardLegOverAnotherRunway(fullPath, legStart, endNodeId, taxiwaySequence[^1], who);
            var made = onward == null ? null : GoOn(fullPath, onward);
            // The plain way to the destination: the route as far as that junction, then the shortest way on.
            if (made != null && OnwardLegKeepsTheRouteShort(made,
                    PlainWayOn(fullPath.Take(legStart + 1).ToList(), fullPath[legStart], endNodeId), who))
            {
                fullPath.Clear();
                fullPath.AddRange(made);
                Log($"{who} leads over another runway: on from {onward![0]} to the destination ({onward.Count} nodes)");
                currentNode = endNodeId;
                lastTaxiwayTerminal = false;
            }
            else if (onward != null)
            {
                // The plain way it was measured against is NOT what the pilot then gets: a last
                // cleared taxiway is never dropped ([RTE-17]), so the route still ends at its far
                // end, beside the other runway, with the load warning that it stops short. The
                // limit stays all the same: on the fs2024 navdata the ways on it refuses here add
                // a median of 1.2 km to a lone taxiway and 3.2 km to a last of two, up to 12.6 km
                // (clearance sweep of 2026-11-26).
                Log($"{who}: stays the end of the route at {currentNode}, its way on was not taken");
            }
        }

        if (currentNode != endNodeId && !lastTaxiwayTerminal)
        {
            var finalLeg = AStarSearch(currentNode, endNodeId, null);
            if (finalLeg == null)
            {
                string reason = $"No path from last taxiway node {currentNode} to destination {endNodeId}";
                Log($"FALLBACK: {reason}");
                return FallbackShortest(startNodeId, endNodeId, reason);
            }

            if (afterBridge)
                AppendBridgeLeg(fullPath, finalLeg, refineBridgeLegs, bridgeFloor, "Final leg", ref bridgeLegsRefined);
            else
            {
                int si = (fullPath.Count > 0 && finalLeg.Count > 0 && fullPath[^1] == finalLeg[0]) ? 1 : 0;
                for (int j = si; j < finalLeg.Count; j++)
                    fullPath.Add(finalLeg[j]);
            }
        }

        if (fullPath.Count < 2)
        {
            string reason = $"Constrained path too short ({fullPath.Count} nodes)";
            Log($"FALLBACK: {reason}");
            return FallbackShortest(startNodeId, endNodeId, reason);
        }

        Log($"Constrained path SUCCESS: {fullPath.Count} nodes");
        return BuildRoute(fullPath);
    }

    /// <summary>
    /// Falls back to shortest path and records the reason for failure.
    /// </summary>
    private TaxiRoute? FallbackShortest(int startNodeId, int endNodeId, string reason)
    {
        var route = FindShortestPath(startNodeId, endNodeId);
        if (route != null)
            route.ConstrainedFallbackReason = reason;
        return route;
    }

    /// <summary>
    /// Ranks the nodes on a given taxiway as candidate ENTRY points from
    /// <paramref name="fromNodeId"/>, best first, and returns the top
    /// <paramref name="maxResults"/>.
    ///
    /// When <paramref name="distToDestination"/> is supplied (Dijkstra costs from the
    /// final destination) the score is the TOTAL route cost through the candidate —
    /// (start → entry) + (entry → destination) — not the distance to the entry alone.
    /// Without it the ranking degrades to the historical entry-distance-only rule.
    /// </summary>
    private List<int> FindNearestNodesOnTaxiway(
        int fromNodeId, string taxiwayName, int maxResults,
        Dictionary<int, double>? distToDestination = null)
    {
        var fromNode = _graph.Nodes[fromNodeId];
        int fromComponent = fromNode.ComponentId;

        // Rank by GRAPH distance, never Euclidean — the same rule
        // FindNearestNodeOnTaxiwayToTarget already follows for taxiway EXITS
        // (KDEN M4). Straight-line distance treats a node the aircraft would
        // have to taxi PAST the real junction to reach as equally close, and
        // when it wins by a hair the route enters the cleared taxiway at the
        // wrong end and immediately doubles back.
        //
        // Motivating defect (EVRA, 2026-08-07, clearance "C then P"): taxiway C
        // runs east-west with its junction onto F in the middle. From the
        // aircraft's position the C/F junction was 454 m away in a straight line
        // and C's west (runway-hold) end 453 m — one metre closer, so the west
        // end was chosen. The route then ran the pilot 111 m west past the
        // junction to that dead end and 111 m straight back east along C: a 222 m
        // out-and-back on an otherwise correct clearance. By graph distance the
        // junction wins by the 111 m it actually is nearer, which is the point on
        // C the aircraft genuinely reaches first.
        //
        // Entry distance ALONE is still not enough, because a taxiway's nearest
        // point can be a stub the route has to reverse out of. Score by the TOTAL
        // route cost through the candidate instead: (start → entry) + (entry →
        // destination). Every node on the direct path ties at the minimum (the
        // sum is just the through-route length), so the ThenBy picks the FIRST
        // of them the aircraft reaches — the same answer entry-distance gave for
        // the EVRA case — while a stub that costs a there-and-back detour is
        // ranked behind by exactly the detour it adds.
        //
        // Motivating defect (LOWS, 2026-08-16, progressive taxi "L"): from the
        // 15 vacate point, L's nearest node by BOTH Euclidean (64 m) and graph
        // distance (80 m) was a 15 m dead-end stub hanging off the south junction;
        // the north junction was 107 m. Entering at the stub made the route open
        // with a 15 m leg at 167° and a 170° hairpin onto 337°, and the pilot spent
        // 55 s turning round in it. By total cost the north junction wins (107 + Y
        // against 186 + Y for the stub) and the route runs straight up L.
        var distFromStart = ComputeGraphDistancesFrom(fromNodeId);
        var candidates = new List<(int nodeId, double totalCost, double distToEntry)>();

        foreach (int nodeId in _graph.GetNodesOnTaxiway(taxiwayName))
        {
            var node = _graph.Nodes[nodeId];
            if (node.ComponentId != fromComponent) continue;
            if (BlockedNodes.Count > 0 && BlockedNodes.Contains(nodeId)) continue;

            // Unreachable in the graph despite sharing a component id (an edge
            // direction quirk) — keep the candidate but rank it behind every
            // reachable one, ordered among its peers by straight-line distance.
            // The offset must be a finite constant, not double.MaxValue: adding
            // a few hundred metres to MaxValue is a no-op in floating point, so
            // every unreachable candidate would tie and lose its ordering.
            const double UNREACHABLE_RANK_BASE = 1e9;
            double distToEntry = distFromStart.TryGetValue(nodeId, out double graphDist)
                ? graphDist
                : UNREACHABLE_RANK_BASE + TaxiGraph.CalculateDistanceMeters(
                    fromNode.Latitude, fromNode.Longitude, node.Latitude, node.Longitude);

            // Onward cost to the destination. Same unreachable treatment: a
            // candidate the destination cannot be reached from is kept (the
            // caller still tries the list in order) but ranked last.
            double onward = 0.0;
            if (distToDestination != null
                && !distToDestination.TryGetValue(nodeId, out onward))
            {
                onward = UNREACHABLE_RANK_BASE;
            }

            candidates.Add((nodeId, distToEntry + onward, distToEntry));
        }

        // Bucket the total cost before the tie-break: nodes on one through-path
        // sum the SAME edges in different orders, so their totals differ only by
        // floating-point noise and an exact comparison would order them at random.
        return candidates
            .OrderBy(c => Math.Round(c.totalCost / ENTRY_COST_TIE_M))
            .ThenBy(c => c.distToEntry)
            .Take(maxResults)
            .Select(c => c.nodeId)
            .ToList();
    }

    /// <summary>
    /// Bucket width (metres) for the entry-candidate total-cost comparison. Two
    /// candidates within this of each other count as equal-cost and are separated
    /// by which one the aircraft reaches first.
    /// </summary>
    private const double ENTRY_COST_TIE_M = 5.0;

    /// <summary>
    /// Picks the single best node on <paramref name="taxiwayName"/> to enter from
    /// <paramref name="fromNodeId"/> when routing to <paramref name="destinationNodeId"/>,
    /// by the same total-route-cost rule <see cref="FindNearestNodesOnTaxiway"/> uses.
    /// Returns -1 when either node is missing from the graph or the taxiway has no
    /// candidate in the start node's component.
    ///
    /// Exists so the route-start pre-snap in TaxiGuidanceManager.LoadRoute picks the
    /// same entry the router would, instead of the Euclidean-nearest node: the
    /// pre-snap runs BEFORE the router and overrides it (the chosen node becomes the
    /// A* start), so a stub picked here could not be recovered from downstream.
    /// </summary>
    internal int FindBestEntryNodeOnTaxiway(int fromNodeId, string taxiwayName, int destinationNodeId)
    {
        if (string.IsNullOrEmpty(taxiwayName)) return -1;
        if (!_graph.Nodes.ContainsKey(fromNodeId)) return -1;
        if (!_graph.Nodes.ContainsKey(destinationNodeId)) return -1;

        var distToDest = ComputeGraphDistancesFrom(destinationNodeId);
        var best = FindNearestNodesOnTaxiway(fromNodeId, taxiwayName, 1, distToDest);
        return best.Count > 0 ? best[0] : -1;
    }

    /// <summary>
    /// Returns the node on <paramref name="taxiwayName"/> (within <paramref name="requiredComponent"/>)
    /// whose geographic distance to (<paramref name="refLat"/>, <paramref name="refLon"/>) is minimal
    /// (<paramref name="farthest"/> = false) or maximal (true), or -1 if none. The two callers honor a
    /// last cleared taxiway that branches off the destination: NEAREST-to-the-runway-position picks
    /// its hold-short end; FARTHEST-from-the-entry traverses it when the nearest-to-destination node
    /// degenerates to the entry. Euclidean is correct here precisely because that taxiway does NOT
    /// lead onto the destination in the graph, so graph distance degenerately picks the entry.
    /// </summary>
    /// <summary>
    /// Returns the node on <paramref name="taxiwayName"/> (within <paramref name="requiredComponent"/>)
    /// whose geographic distance to (<paramref name="refLat"/>, <paramref name="refLon"/>) is minimal
    /// (<paramref name="farthest"/> = false) or maximal (true), or -1 if none. The two callers honor a
    /// last cleared taxiway that branches off the destination: NEAREST-to-the-runway-position picks
    /// its hold-short end; FARTHEST-from-the-entry traverses it when the nearest-to-destination node
    /// degenerates to the entry. Euclidean is correct here precisely because that taxiway does NOT
    /// lead onto the destination in the graph, so graph distance degenerately picks the entry.
    /// </summary>
    private int FindExtremeNodeOnTaxiway(string taxiwayName, double refLat, double refLon,
        int requiredComponent, bool farthest, Func<TaxiNode, bool>? skip = null)
    {
        int best = -1;
        double bestM = farthest ? -1 : double.MaxValue;
        foreach (int nodeId in _graph.GetNodesOnTaxiway(taxiwayName))
        {
            var node = _graph.Nodes[nodeId];
            if (node.ComponentId != requiredComponent) continue;
            if (BlockedNodes.Count > 0 && BlockedNodes.Contains(nodeId)) continue;
            if (skip != null && skip(node)) continue;
            double d = TaxiGraph.CalculateDistanceMeters(node.Latitude, node.Longitude, refLat, refLon);
            if (farthest ? d > bestM : d < bestM) { bestM = d; best = nodeId; }
        }
        return best;
    }

    /// <summary>Node on the taxiway geographically NEAREST the given position (-1 if none).</summary>
    private int FindNodeOnTaxiwayNearestPosition(string taxiwayName, double lat, double lon, int requiredComponent,
        Func<TaxiNode, bool>? skip = null)
        => FindExtremeNodeOnTaxiway(taxiwayName, lat, lon, requiredComponent, farthest: false, skip);

    /// <summary>Node on the taxiway geographically FARTHEST from the given node (-1 if none / node unknown).</summary>
    private int FindNodeOnTaxiwayFarthestFromNode(string taxiwayName, int fromNodeId, int requiredComponent)
        => _graph.Nodes.TryGetValue(fromNodeId, out var from)
            ? FindExtremeNodeOnTaxiway(taxiwayName, from.Latitude, from.Longitude, requiredComponent, farthest: true)
            : -1;

    /// <summary>
    /// Where a last cleared taxiway that branches off the destination is driven TO from
    /// <paramref name="entryNodeId"/>, or -1 when it has no other node.
    /// First the node nearest the destination POSITION: correct when the taxiway ends AT the runway
    /// (EIDW "…N, N2" to 28R → N2's hold-short end). If that is still the entry, the destination node
    /// sits back near the entry rather than at the taxiway's far end (LFPG "…R, R1" to 26R: the 26R
    /// node is closer to the R/R1 junction than to R1's far end), so the node FARTHEST along the
    /// taxiway from the entry is taken and the taxiway is still traversed.
    /// The nearest pick skips the end of a fillet beside the junction: a node where this taxiway's
    /// name ENDS (one edge of it) on another taxiway the entry is on, the one the route came along.
    /// Taking it ended the route a few metres along that other taxiway, and the cleared one was never
    /// driven (the LFPG R1 failure the farthest-node fallback exists for). Only where the name ends: a
    /// node this taxiway runs on through stays a pick (LFPG "B" and KORD "A", where a link meets the
    /// taxiway twice). And only while the far end the skip leaves is a connector's, within
    /// <see cref="FILLET_SKIP_MAX_FAR_END_M"/> of the entry: past it the taxiway is a long one (OMDB
    /// "M" from M4: its farthest node is 4.6 km off, the far side of the airport), so the fillet
    /// pick stands as it was, whether the skip left the farthest node or some other node that far
    /// away.
    /// A node where the name ends is skipped whatever the name does between it and the entry. The
    /// other end of a simple link that leaves a taxiway and rejoins it within
    /// <see cref="FILLET_END_MAX_M"/> is skipped too, and such a link is then driven short of its
    /// end. Telling the two apart by the name's own shape (a fillet joins a branch of the name, a
    /// link has none) was measured and REJECTED: a pair of fillet arms with no branch of the name
    /// between them reads as a link too. Of the 290 routes it changed, 184 drove more of the
    /// cleared taxiway, by a median of 15 m, while 103 drove less, 12 came to pass a node twice,
    /// and 18 went on from the other arm's end a median 5.1 km longer (clearance sweep of
    /// 2026-11-26, the 100 busiest fs2024 airports).
    /// </summary>
    private int FarEndOfLastTaxiway(string taxiwayName, int entryNodeId, int endNodeId)
    {
        var entry = _graph.Nodes[entryNodeId];
        int comp = entry.ComponentId;
        var destNode = _graph.Nodes[endNodeId];
        bool BesideTheEntry(TaxiNode n) => n.NodeId != entryNodeId
            && TaxiGraph.CalculateDistanceMeters(entry.Latitude, entry.Longitude, n.Latitude, n.Longitude) <= FILLET_END_MAX_M
            && _graph.Adjacency.TryGetValue(n.NodeId, out var edges)
            && edges.Count(e => e.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase)) == 1
            && n.TaxiwayNames.Any(t => !t.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase) && entry.TaxiwayNames.Contains(t));
        int unskipped = FindNodeOnTaxiwayNearestPosition(taxiwayName, destNode.Latitude, destNode.Longitude, comp);
        int far = FindNodeOnTaxiwayNearestPosition(taxiwayName, destNode.Latitude, destNode.Longitude, comp, BesideTheEntry);
        if (far == entryNodeId || far == -1)
            far = FindNodeOnTaxiwayFarthestFromNode(taxiwayName, entryNodeId, comp);
        // The bound, on whatever the skip left. With nothing skipped the two picks are one node (or
        // the unskipped one is the entry), and the pick stands however far it is, as before.
        if (far != -1 && far != unskipped && unskipped != entryNodeId && unskipped != -1
            && TaxiGraph.CalculateDistanceMeters(entry.Latitude, entry.Longitude,
                   _graph.Nodes[far].Latitude, _graph.Nodes[far].Longitude) > FILLET_SKIP_MAX_FAR_END_M)
            far = unskipped;
        return far == entryNodeId ? -1 : far;
    }

    // How far from the junction the far end of a last cleared taxiway may be for a fillet beside the
    // junction to be skipped (FarEndOfLastTaxiway). Measured over the 100 busiest fs2024 airports,
    // lone and two-taxiway clearances, plain navdata (2026-11-26): unbounded, the skip made 69 routes
    // 0.5 to 4.4 km longer, each driving a long taxiway to its far side; bounded, none.
    private const double FILLET_SKIP_MAX_FAR_END_M = 1000.0;

    // How far from the junction a node may be to count as the end of a fillet beside it. Farther, a
    // node where the taxiway's name ends on a taxiway the junction is also on is the taxiway's real
    // far end (WSSS "D5": 371 m, which the skip cut to 19 m).
    private const double FILLET_END_MAX_M = 150.0;

    /// <summary>
    /// The way on to the destination from the far end of a last cleared taxiway, when that taxiway
    /// leads over ANOTHER runway: the shortest path from the last node of
    /// <paramref name="drivenSoFar"/> (the route up to that far end) that does not come back along
    /// the last taxiway's own leg (from <paramref name="lastLegStart"/>), accepted only when it is
    /// all NEW ground, through no node the route has already used, and the taxiway leads over
    /// another runway: either the way on crosses another runway before it meets the destination's
    /// own, its pavement within <see cref="OTHER_RUNWAY_MAX_WALK_M"/> of that far end, or the last
    /// leg ITSELF crossed one that close to its far end (a taxiway that keeps its name over the
    /// runway: its far end is already on the other side).
    /// Null in every other case, which keeps the taxiway the route's terminus and says why in the
    /// log: no way on but back (EIDW N2), a way on over no other runway or only the long way round
    /// to one (PHNL 04L, warned by RunwayReachGate), a far end on the destination runway itself, a
    /// destination node off every runway's pavement, or a way on that meets one other runway
    /// TWICE (a stub to another runway's hold, with the destination on the near side: over that
    /// runway and back at the next link is all new ground, and the hold is a few metres from the
    /// pavement, so the walk bound never sees it).
    /// A destination node on two runways' pavement (a threshold at an intersection) counts both as
    /// the destination's: the router is not told which was selected, and guessing the first one
    /// listed could drop an EIDW-shaped terminus.
    /// Both halves of the new-ground test were measured (VirtualPilot A/B, 100 airports,
    /// 2026-11-24). Without it, EGKK "…J, G" to 08R: the route comes OVER 08L onto G, G's far end is
    /// back at 08L, and the way on went over 08L twice more. CLOSING the whole route so far to the
    /// search instead of refusing forces a way round: RKTP "E, C" to 03R grew from 4.2 to 7.5 km.
    /// The way on may LEAVE THE LEG SHORT of its far end, and the path returned then starts at the
    /// node it leaves by (callers join the two with <see cref="GoOn"/>). A taxiway's name runs on
    /// round the curves it meets a runway by, so its far end is often the tip of the curve that
    /// points at the destination, on the pavement; from there the only way on follows the curve
    /// onto the centerline and comes back to the curve of the taxiway opposite (OMDB "K, K7" to
    /// 30R on navdata's own names: 430 m and a reversal on 12R/30L, where the link straight over
    /// from the node the curves part at is 117 m). When the way on from the far end wanders on
    /// the runway it leads over like that (<see cref="WandersOn"/>), every node of the leg up to
    /// <see cref="LEAVE_BEFORE_FAR_END_MAX_M"/> back is tried in its place, judged by the same
    /// rules, and the shortest way on that does not wander is taken, provided it reaches a runway
    /// over no OTHER taxiway (<see cref="ReachesARunwayOverNoOtherTaxiway"/>): the crossing is
    /// still the cleared taxiway's own. <paramref name="earliestLeave"/> is the first node of
    /// <paramref name="drivenSoFar"/> it may leave by, for an aircraft already on the taxiway.
    /// </summary>
    private List<int>? OnwardLegOverAnotherRunway(
        IReadOnlyList<int> drivenSoFar, int lastLegStart, int endNodeId, string taxiwayName, string context,
        int earliestLeave = -1)
    {
        var fromFarEnd = WayOnFromTheLastNode(drivenSoFar, lastLegStart, endNodeId, context, null, logRefusals: true,
            out var ledOver);
        if (fromFarEnd == null || ledOver == null) return fromFarEnd;
        string? wanders = WandersOn(drivenSoFar.Skip(lastLegStart).Concat(fromFarEnd.Skip(1)).ToList(), ledOver);
        if (wanders == null) return fromFarEnd;

        // Each node of the leg up to LEAVE_BEFORE_FAR_END_MAX_M back, the node the leg starts at
        // included (an aircraft standing at the end of the taxiway has the crossing straight
        // ahead: OMDB "K14"), with the rest of the leg closed to the search.
        List<int>? best = null;
        double bestMeters = double.MaxValue, back = 0.0;
        int bestLeave = -1;
        for (int leave = drivenSoFar.Count - 2; leave >= Math.Max(lastLegStart, earliestLeave); leave--)
        {
            back += PathMeters(new[] { drivenSoFar[leave], drivenSoFar[leave + 1] });
            if (back > LEAVE_BEFORE_FAR_END_MAX_M) break;
            var kept = drivenSoFar.Take(leave + 1).ToList();
            var wayOn = WayOnFromTheLastNode(kept, lastLegStart, endNodeId, context,
                drivenSoFar.Skip(leave + 1), logRefusals: false, out var over, legMayBeEmpty: true);
            if (wayOn == null || over == null) continue;
            var route = kept.Skip(lastLegStart).Concat(wayOn.Skip(1)).ToList();
            double meters = PathMeters(route);
            if (meters >= bestMeters || WandersOn(route, over) != null
                || !ReachesARunwayOverNoOtherTaxiway(wayOn, taxiwayName))
                continue;
            best = wayOn;
            bestMeters = meters;
            bestLeave = leave;
        }
        if (best == null)
        {
            // Still taken. Where the navdata has no link straight over (two high-speed exits facing
            // each other: EDDF "M21" to 36, KLAX "A, A7" to 24R), nothing here is a crossing.
            // Refusing the way on was measured and is NOT done: the route then ends at the far end,
            // which is on that runway's pavement, and guidance stops the aircraft there under the
            // destination's name ([RTE-17]'s full text has the counts and the replays). Said in the
            // log so such a route can be found.
            Log($"{context}: the way on from the far end {wanders}, and no node of the taxiway up to " +
                $"{LEAVE_BEFORE_FAR_END_MAX_M:F0} m short of it has one that crosses it instead");
            return fromFarEnd;
        }
        Log($"{context}: goes on from {drivenSoFar[bestLeave]}, " +
            $"{PathMeters(drivenSoFar.Skip(bestLeave).ToList()):F0} m short of its far end {drivenSoFar[^1]}: " +
            $"the way on from the far end {wanders}");
        return best;
    }

    // How far short of its far end a last cleared taxiway may be left for a way on that does not
    // wander on the runway it leads over (OnwardLegOverAnotherRunway). Measured on the clearance
    // sweep of 2026-11-26 (299,989 routes, the 100 busiest fs2024 airports): at 150 m, 268 routes
    // change, 252 of them shorter by a median of 102 m; at 400 m, 100 more changed, 17 of those
    // longer by a median of 1.2 km, leaving the taxiway up to 440 m early.
    private const double LEAVE_BEFORE_FAR_END_MAX_M = 150.0;

    // A turn sharper than this at a node on a runway's pavement is a turn back on that runway.
    // The curves a taxiway meets a runway by turn up to 90 degrees; the three reversals measured
    // (OMDB K7 and K14, EDDF M14) are 175, 169 and 154.
    private const double TURN_BACK_ON_RUNWAY_MIN_DEG = 135.0;

    /// <summary>
    /// Whether a way on reaches a runway's pavement over nothing but unnamed links and
    /// <paramref name="taxiwayName"/> itself: it leaves the cleared taxiway for the runway, not for
    /// another taxiway that crosses somewhere else.
    /// </summary>
    private bool ReachesARunwayOverNoOtherTaxiway(IReadOnlyList<int> wayOn, string taxiwayName)
    {
        // Where the way on first comes onto pavement, sampled along its edges: a crossing is often
        // one edge from hold side to hold side with no node on the runway.
        var met = RunwaysMet(wayOn, 0, out _);
        if (met.Count == 0) return false;
        double ontoPavementAt = met.Min(m => m.FromMeters);
        double walked = 0.0;
        for (int i = 0; i + 1 < wayOn.Count; i++)
        {
            // The edge that itself goes onto the pavement is the crossing, whatever it is called:
            // the link over the runway often carries the name of the taxiway on the far side.
            walked += PathMeters(new[] { wayOn[i], wayOn[i + 1] });
            if (walked >= ontoPavementAt) return true;
            string name = _graph.Adjacency.TryGetValue(wayOn[i], out var edges)
                ? edges.FirstOrDefault(e => e.ToNodeId == wayOn[i + 1])?.TaxiwayName ?? "" : "";
            if (name.Length > 0 && !name.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return false;
    }

    /// <summary>
    /// What a path does on <paramref name="runway"/> that a crossing of it does not, as the words
    /// the log says it in, or null: it DRIVES ALONG the runway (a stretch of more than
    /// <see cref="TurnPickApplier.AlongRunwayLimitMeters"/> with both ends of every edge on its
    /// pavement, the measure and the figure a Turn pick is held to, [TURN-7]: a crossing puts 45
    /// to 60 m of route there), or it TURNS BACK on it (a turn sharper than
    /// <see cref="TURN_BACK_ON_RUNWAY_MIN_DEG"/> at a node on the pavement).
    /// </summary>
    private string? WandersOn(IReadOnlyList<int> path, TaxiGraph.RunwayCenterline runway)
    {
        var shape = RunwayShape.For(runway);
        bool On(int id) => shape.Contains(_graph.Nodes[id].Latitude, _graph.Nodes[id].Longitude, 0.0);
        double run = 0.0, longest = 0.0;
        bool fromOn = false;
        for (int i = 0; i < path.Count; i++)
        {
            bool on = On(path[i]);
            run = i > 0 && fromOn && on ? run + PathMeters(new[] { path[i - 1], path[i] }) : 0.0;
            if (run > longest) longest = run;
            if (on && i > 0 && i + 1 < path.Count
                && TurnDegrees(path[i - 1], path[i], path[i + 1]) > TURN_BACK_ON_RUNWAY_MIN_DEG)
                return $"turns back on runway {runway.Name1}/{runway.Name2} at {path[i]}";
            fromOn = on;
        }
        return longest > TurnPickApplier.AlongRunwayLimitMeters
            ? $"drives {longest:F0} m along runway {runway.Name1}/{runway.Name2}"
            : null;
    }

    /// <summary>The turn made at <paramref name="at"/>, in degrees; 0 where either edge is too short to have a direction.</summary>
    private double TurnDegrees(int from, int at, int to)
    {
        var a = _graph.Nodes[from];
        var b = _graph.Nodes[at];
        var c = _graph.Nodes[to];
        if (TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) < 1.0
            || TaxiGraph.CalculateDistanceMeters(b.Latitude, b.Longitude, c.Latitude, c.Longitude) < 1.0)
            return 0.0;
        return Math.Abs(NormalizeAngle(
            NavigationCalculator.CalculateBearing(b.Latitude, b.Longitude, c.Latitude, c.Longitude)
            - NavigationCalculator.CalculateBearing(a.Latitude, a.Longitude, b.Latitude, b.Longitude)));
    }

    /// <summary>
    /// <see cref="OnwardLegOverAnotherRunway"/>'s way on from the LAST node of
    /// <paramref name="drivenSoFar"/>, with the rest of the last leg and
    /// <paramref name="alsoClosed"/> closed to the search, or null with the reason in the log.
    /// </summary>
    private List<int>? WayOnFromTheLastNode(
        IReadOnlyList<int> drivenSoFar, int lastLegStart, int endNodeId, string context,
        IEnumerable<int>? alsoClosed, bool logRefusals, out TaxiGraph.RunwayCenterline? ledOver, bool legMayBeEmpty = false)
    {
        ledOver = null;
        // The caller says which taxiway and which case this is: the same refusals are written for a
        // last taxiway of several, which then stays the end of the route, and for a single one,
        // which is then left the plain way.
        List<int>? Refuse(string why)
        {
            if (logRefusals) Log($"{context}: no way on over another runway, {why}");
            return null;
        }

        if (drivenSoFar.Count < 1 || lastLegStart < 0 || lastLegStart > drivenSoFar.Count - 1
            || (!legMayBeEmpty && lastLegStart == drivenSoFar.Count - 1))
            return Refuse($"no last leg to judge ({drivenSoFar.Count} nodes, leg from index {lastLegStart})");
        if (!_graph.Nodes.TryGetValue(endNodeId, out var dest))
            return Refuse($"destination node {endNodeId} is not in the graph");
        var destRunways = DestinationRunways(dest);
        if (destRunways.Count == 0)
            return Refuse($"destination node {endNodeId} is on no runway's pavement, nor on a centreline just past its end");
        bool IsDestination(TaxiGraph.RunwayCenterline r) => IsOneOf(destRunways, r);

        var lastLeg = new HashSet<int>(drivenSoFar.Skip(lastLegStart).Take(drivenSoFar.Count - 1 - lastLegStart));
        if (alsoClosed != null) lastLeg.UnionWith(alsoClosed);
        var onward = AStarSearch(drivenSoFar[^1], endNodeId, null, lastLeg);
        if (onward == null || onward.Count < 2)
            return Refuse($"no path from {drivenSoFar[^1]} that does not come back along the last taxiway");
        var used = new HashSet<int>(drivenSoFar);
        for (int i = 1; i < onward.Count; i++)
            if (used.Contains(onward[i]))
                return Refuse($"the way on reuses node {onward[i]} of the route so far");

        // The last leg and the way on, walked as one path, so a crossing inside the leg is seen and
        // a far-end node standing on pavement is one meeting, not two.
        var walked = drivenSoFar.Skip(lastLegStart).Concat(onward.Skip(1)).ToList();
        var met = RunwaysMet(walked, drivenSoFar.Count - 1 - lastLegStart, out double farMeters);
        if (met.Any(m => IsDestination(m.Runway) && m.FromMeters <= farMeters && m.ToMeters >= farMeters))
            return Refuse("the last taxiway ends on the destination runway");
        var others = met.Where(m => !IsDestination(m.Runway)).ToList();
        for (int i = 0; i < others.Count; i++)
            for (int j = i + 1; j < others.Count; j++)
                if (ReferenceEquals(others[i].Runway, others[j].Runway))
                    return Refuse($"it meets one other runway twice, {others[i].FromMeters - farMeters:F0} m " +
                                  $"and {others[j].FromMeters - farMeters:F0} m from the far end (over it and back)");

        // The first other runway must be CROSSED. RunwaysMet merges one continuous stretch of
        // pavement into a single meeting, so a way on that goes onto another runway at a node on
        // it and leaves on the SAME side (two stubs meeting on the runway, a pair of holding-bay
        // links at a threshold) is one meeting, inside the walk bound, and was taken: "K, K4"
        // over the next runway's edge and back by K5 (review of 2026-11-25). The classifier
        // separates an entry from a crossing, and merges the two centrelines of an airport whose
        // navdata carries two scenery layers (KBOS).
        // Every other runway on the way, not only the first: a way on that crosses the runway the
        // taxiway leads over and then runs onto a third one and back off it is no better (KMSP
        // "W" to 22, lone-clearance sweep of 2026-11-25). And the destination's own runway is met
        // once, at the end.
        var passages = PassagesOf(walked);
        var entered = passages.FirstOrDefault(p => !IsDestination(p.Runway) && p.Kind != RunwayEventKind.Crossing);
        if (entered != null)
            return Refuse($"it goes onto runway {entered.Runway.Name1}/{entered.Runway.Name2} and leaves it on the side it came from (an entry, not a crossing)");
        // Counted per runway BY NAME: where the navdata carries two centrelines for one runway, the
        // classifier keeps a passage under either copy.
        var atDestination = passages.Where(p => IsDestination(p.Runway)).ToList();
        for (int i = 0; i < atDestination.Count; i++)
            for (int j = i + 1; j < atDestination.Count; j++)
                if (RunwayRouteClassifier.SameRunway(atDestination[i].Runway, atDestination[j].Runway))
                    return Refuse($"it meets the destination runway {atDestination[i].Runway.Name1}/{atDestination[i].Runway.Name2} before its end");

        // And a runway the taxiway leads over is one the path goes over (GoesOver): the pavement walk
        // alone also sees a corner the path only cuts.
        bool GoesOver(TaxiGraph.RunwayCenterline r) => this.GoesOver(walked, passages, r);
        int crossedByTheLeg = others.FindIndex(m => m.FromMeters < farMeters && farMeters - m.ToMeters <= OTHER_RUNWAY_MAX_WALK_M
                                                    && GoesOver(m.Runway));
        if (crossedByTheLeg != -1)
        {
            ledOver = others[crossedByTheLeg].Runway;
            return onward;
        }
        int next = met.FindIndex(m => m.ToMeters >= farMeters && (IsDestination(m.Runway) || GoesOver(m.Runway)));
        if (next == -1) return Refuse("the way on crosses no runway");
        if (IsDestination(met[next].Runway)) return Refuse("the way on meets the destination's own runway first");
        double walkedMeters = Math.Max(0.0, met[next].FromMeters - farMeters);
        if (walkedMeters > OTHER_RUNWAY_MAX_WALK_M)
            return Refuse($"the first other runway is {walkedMeters:F0} m on, past the {OTHER_RUNWAY_MAX_WALK_M:F0} m bound");
        ledOver = met[next].Runway;
        return onward;
    }

    /// <summary>
    /// Whether the runway a cleared taxiway leads over is one the plain way on crosses as well,
    /// somewhere else. <paramref name="along"/> is the taxiway from the node the way on leaves it
    /// by to its far end, <paramref name="onward"/> the way on from that far end, and the runway it
    /// leads over is any other than the destination's own that the two meet up to
    /// <see cref="OTHER_RUNWAY_MAX_WALK_M"/> past the far end, the same reach
    /// <see cref="OnwardLegOverAnotherRunway"/> accepts a far end by. <paramref name="wayOn"/> is the
    /// route's own way to the destination from the node it would leave the taxiway by.
    /// Both must GO OVER that runway (<see cref="GoesOver"/>), as the onward leg must: pavement that
    /// either only cuts a corner of is no runway it leads over, and counting it took the pilot over
    /// a runway the plain way never crossed.
    /// </summary>
    internal bool LeadsOverARunwayTheWayOnCrosses(
        IReadOnlyList<int> along, IReadOnlyList<int> onward, IReadOnlyList<int> wayOn, int endNodeId)
    {
        if (!_graph.Nodes.TryGetValue(endNodeId, out var dest)) return false;
        var destRunways = DestinationRunways(dest);
        var walked = along.Concat(onward.Skip(1)).ToList();
        var met = RunwaysMet(walked, along.Count - 1, out double farMeters);
        var onWayOn = RunwaysMet(wayOn, 0, out _);
        var walkedPassages = PassagesOf(walked);
        var wayOnPassages = PassagesOf(wayOn);
        foreach (var m in met)
        {
            if (m.FromMeters > farMeters + OTHER_RUNWAY_MAX_WALK_M) continue;
            if (IsOneOf(destRunways, m.Runway)) continue;
            if (!GoesOver(walked, walkedPassages, m.Runway)) continue;
            if (onWayOn.Any(w => ReferenceEquals(w.Runway, m.Runway)) && GoesOver(wayOn, wayOnPassages, m.Runway))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a path goes over a runway: the classifier calls one of its passages a crossing of it,
    /// or the path stands on its pavement at a node (a path that STARTS on a runway and leaves it,
    /// as a last leg does at EBBR B8/B9 and EDDF L3: the classifier gives such a path no passage).
    /// <see cref="RunwaysMet"/> also sees pavement a path never stands on or crosses: an edge cutting
    /// a corner of it with both ends clear on one side, a path that changes side only beyond the
    /// runway's end. The classifier gives those no passage either, and they are no runway the path
    /// goes over. <paramref name="passages"/> is <see cref="PassagesOf"/> the same path.
    /// </summary>
    private bool GoesOver(IReadOnlyList<int> path, List<RunwayPassage> passages, TaxiGraph.RunwayCenterline r)
    {
        if (passages.Any(p => p.Kind == RunwayEventKind.Crossing && RunwayRouteClassifier.SameRunway(p.Runway, r)))
            return true;
        var shape = RunwayShape.For(r);
        return path.Any(id => shape.Contains(_graph.Nodes[id].Latitude, _graph.Nodes[id].Longitude, 0.0));
    }

    private List<RunwayPassage> PassagesOf(IReadOnlyList<int> path)
        => RunwayRouteClassifier.ClassifyAll(
            path.Select(id => (TaxiNode?)_graph.Nodes[id]).ToList(), _graph.RunwayCenterlines.Where(r => r != null));

    /// <summary>
    /// Whether a centreline is one of the given runways, BY NAME (<see cref="RunwayRouteClassifier.SameRunway"/>):
    /// an airport whose navdata carries two scenery layers (KBOS) has two centrelines per runway, and
    /// where their pavement differs a node can stand on one copy only. Matched by reference, the
    /// other copy of the destination's own runway read as another runway.
    /// </summary>
    private static bool IsOneOf(List<TaxiGraph.RunwayCenterline> runways, TaxiGraph.RunwayCenterline r)
        => runways.Any(d => RunwayRouteClassifier.SameRunway(d, r));

    // How far past its last node a taxiway may still be said to lead onto a runway. The ONE
    // figure TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M also reads: 99.16 % of the runway-owned
    // hold nodes in the fs2020 navdata are within 400 m of taxiing from their runway's pavement
    // (p50 54 m, p90 130 m). Beyond it the other runway is somewhere the route wanders to once its
    // way back is closed, not where the cleared taxiway goes.
    private const double OTHER_RUNWAY_MAX_WALK_M = RunwayReachGate.MaxWalkMeters;

    // How far past a runway's recorded end a destination node on its centreline still belongs to
    // it: the ONE figure TaxiGuidanceManager.ENTRY_STUB_BEYOND_END_M also reads ([STR-20]).
    private const double DESTINATION_BEYOND_END_M = RunwayReachGate.StubBeyondEndMeters;

    /// <summary>
    /// The runways a destination node belongs to: every one whose pavement it stands on (a threshold
    /// at an intersection is on two, and both count), else every one whose centreline it lies on up
    /// to <see cref="DESTINATION_BEYOND_END_M"/> past the recorded end: an entry stub may end there
    /// ([STR-20], Taxi2Gate LFPG 26R), on no pavement, and its runway is found the way
    /// TaxiGuidanceManager.RunwaysOnEntryStub finds it. The ONE answer
    /// <see cref="OnwardLegOverAnotherRunway"/> and <see cref="LeadsOverARunwayTheWayOnCrosses"/>
    /// both read, so neither counts the destination's own runway as another.
    /// </summary>
    private List<TaxiGraph.RunwayCenterline> DestinationRunways(TaxiNode dest)
    {
        var onPavement = _graph.RunwayCenterlines
            .Where(r => r != null && RunwayShape.For(r).Contains(dest.Latitude, dest.Longitude, 0.0)).ToList();
        if (onPavement.Count > 0) return onPavement;
        return _graph.RunwayCenterlines.Where(r =>
        {
            if (r == null) return false;
            var shape = RunwayShape.For(r);
            if (shape.IsDegenerate) return false;
            var (along, lateral) = shape.Project(dest.Latitude, dest.Longitude);
            return Math.Abs(lateral) <= shape.HalfWidthMeters
                && along >= shape.ExtentMinMeters - DESTINATION_BEYOND_END_M
                && along <= shape.ExtentMaxMeters + DESTINATION_BEYOND_END_M;
        }).ToList();
    }

    /// <summary>
    /// The route as it stands, then the shortest way on from <paramref name="fromNodeId"/>: what the
    /// pilot would be given without an onward leg. Null when there is no such way.
    /// </summary>
    private List<int>? PlainWayOn(IReadOnlyList<int> routeSoFar, int fromNodeId, int endNodeId)
    {
        var wayOn = AStarSearch(fromNodeId, endNodeId, null);
        if (wayOn == null) return null;
        var plain = new List<int>(routeSoFar);
        plain.AddRange(wayOn.Skip(plain.Count > 0 && plain[^1] == wayOn[0] ? 1 : 0));
        return plain;
    }

    /// <summary>
    /// Whether an accepted onward leg leaves the route near the length of the plain one: at most
    /// <see cref="REPEAT_HONOUR_MAX_RATIO"/> times it plus <see cref="REPEAT_HONOUR_MAX_PAD_M"/>, the
    /// limit a repeated taxiway is honoured within, and for the same reason (a clearance is
    /// followed where that costs a little, never where it makes a loop). Without it a taxiway that
    /// runs on past its best exit to a link over the same runway was driven there however far
    /// (6.2 km for 2.8 on the review's field), and a lone taxiway the aircraft is on was driven
    /// AWAY from its exit (4.7 km for 2.8): the router does not know which way the aircraft faces.
    /// It also refuses a way on whose route makes a runway ENTRY the plain route does not.
    /// With no plain way at all there is nothing to compare, and the leg stands.
    /// </summary>
    private bool OnwardLegKeepsTheRouteShort(IReadOnlyList<int> withOnwardLeg, IReadOnlyList<int>? plain, string context)
    {
        if (plain == null) return true;
        double made = PathMeters(withOnwardLeg), shortest = PathMeters(plain);
        if (made > shortest * REPEAT_HONOUR_MAX_RATIO + REPEAT_HONOUR_MAX_PAD_M)
        {
            Log($"{context}: way on over another runway not taken, it makes the route {made:F0} m against {shortest:F0} m " +
                $"the plain way (limit {REPEAT_HONOUR_MAX_RATIO:F2} x + {REPEAT_HONOUR_MAX_PAD_M:F0} m)");
            return false;
        }
        // Nor may it add a runway ENTRY (onto a runway and back off the same side) the plain route
        // does not make. OnwardLegOverAnotherRunway judges the last leg and the way on by
        // themselves, and a leg that STARTS on a runway's pavement shows it no passage there;
        // over the whole route it is one (OMDB "N5" to 12R, LEBL "UB" to 24R: lone-clearance
        // sweep of 2026-11-25).
        int madeEntries = RunwayEntries(withOnwardLeg), plainEntries = RunwayEntries(plain);
        if (madeEntries > plainEntries)
        {
            Log($"{context}: way on over another runway not taken, the route it makes goes onto a runway and back off " +
                $"the same side {madeEntries} time(s) against {plainEntries} the plain way");
            return false;
        }
        return true;
    }

    /// <summary>
    /// The route so far, cut at the node a way on leaves it by (its last node, or one short of it:
    /// <see cref="OnwardLegOverAnotherRunway"/>), then that way on.
    /// </summary>
    private static List<int> GoOn(IReadOnlyList<int> soFar, IReadOnlyList<int> wayOn)
    {
        int at = soFar.Count - 1;
        while (at > 0 && soFar[at] != wayOn[0]) at--;
        var route = soFar.Take(at + 1).ToList();
        route.AddRange(wayOn.Skip(1));
        return route;
    }

    private int RunwayEntries(IReadOnlyList<int> path)
        => RunwayRouteClassifier.ClassifyAll(
                path.Select(id => (TaxiNode?)_graph.Nodes[id]).ToList(), _graph.RunwayCenterlines.Where(r => r != null))
            .Count(p => p.Kind == RunwayEventKind.Entry);

    // AStarSearchWithGapChaining may bridge a gap in a taxiway through nodes it then comes back to
    // (LEMD "LA": LA, A13, LA, M13, LB, LA). A leg like that is a loop, not a taxiway driven on.
    private static bool PassesANodeTwice(IEnumerable<int> path)
    {
        var seen = new HashSet<int>();
        return path.Any(n => !seen.Add(n));
    }

    private double PathMeters(IReadOnlyList<int> path)
    {
        double metres = 0.0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var a = _graph.Nodes[path[i]];
            var b = _graph.Nodes[path[i + 1]];
            metres += TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
        }
        return metres;
    }

    /// <summary>
    /// Every meeting of the path with a runway's pavement, in the order met, as metres walked from
    /// the path's first node (<c>FromMeters</c> where it comes onto the pavement, <c>ToMeters</c>
    /// the last sample still on it). Sampled along every edge: a navdata crossing is often ONE edge
    /// from hold side to hold side with no node on the pavement. Each runway is followed on its
    /// own, so where two overlap neither hides the other. <paramref name="markMeters"/> is the
    /// metres walked at the node <paramref name="markIndex"/>.
    /// </summary>
    private List<(TaxiGraph.RunwayCenterline Runway, double FromMeters, double ToMeters)> RunwaysMet(
        IReadOnlyList<int> path, int markIndex, out double markMeters)
    {
        const double SAMPLE_METERS = 10.0;
        var runways = _graph.RunwayCenterlines.Where(r => r != null).ToList();
        var shapes = runways.Select(RunwayShape.For).ToList();
        var met = new List<(TaxiGraph.RunwayCenterline Runway, double FromMeters, double ToMeters)>();
        // Per runway: the index of its open meeting in `met`, plus one; 0 while the path is off it.
        var open = new int[runways.Count];
        void Sample(double lat, double lon, double atMeters)
        {
            for (int r = 0; r < runways.Count; r++)
            {
                if (!shapes[r].Contains(lat, lon, 0.0)) { open[r] = 0; continue; }
                if (open[r] == 0) { met.Add((runways[r], atMeters, atMeters)); open[r] = met.Count; }
                else met[open[r] - 1] = (runways[r], met[open[r] - 1].FromMeters, atMeters);
            }
        }

        markMeters = 0.0;
        double walkedMeters = 0.0;
        for (int i = 0; i < path.Count; i++)
        {
            var a = _graph.Nodes[path[i]];
            if (i == markIndex) markMeters = walkedMeters;
            Sample(a.Latitude, a.Longitude, walkedMeters);
            if (i + 1 == path.Count) break;
            var b = _graph.Nodes[path[i + 1]];
            double edgeMeters = TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            int steps = (int)(edgeMeters / SAMPLE_METERS);
            for (int k = 1; k <= steps; k++)
            {
                double t = (double)k / (steps + 1);
                Sample(a.Latitude + (b.Latitude - a.Latitude) * t, a.Longitude + (b.Longitude - a.Longitude) * t,
                    walkedMeters + edgeMeters * t);
            }
            walkedMeters += edgeMeters;
        }
        return met;
    }

    private int FindNearestNodeOnTaxiwayToTarget(
        int targetNodeId, string taxiwayName,
        Dictionary<int, double>? precomputedDistFromTarget = null)
    {
        var targetNode = _graph.Nodes[targetNodeId];
        int targetComponent = targetNode.ComponentId;
        int bestNode = -1;
        double bestDist = double.MaxValue;

        // When the caller has already run Dijkstra-from-target (e.g. the
        // FindConstrainedPath's hoisted distFromFinalDest for the same
        // endNodeId), reuse it. Each Dijkstra is ~50 ms on a 2-3k-node
        // airport graph; on the constrained-routing single-taxiway and
        // last-step paths this avoids running it 1-2 redundant times per
        // route build.
        var distFromTarget = precomputedDistFromTarget
            ?? ComputeGraphDistancesFrom(targetNodeId);

        foreach (int nodeId in _graph.GetNodesOnTaxiway(taxiwayName))
        {
            var node = _graph.Nodes[nodeId];
            if (node.ComponentId != targetComponent) continue;
            if (BlockedNodes.Count > 0 && BlockedNodes.Contains(nodeId)) continue;

            if (!distFromTarget.TryGetValue(nodeId, out double gDist))
                continue; // unreachable from destination (shouldn't happen given component filter, but defensive)

            if (gDist < bestDist)
            {
                bestDist = gDist;
                bestNode = nodeId;
            }
        }

        // Defensive fallback: if Dijkstra found no taxiway node (e.g., the
        // taxiway exists in the graph but every one of its nodes is across
        // a component split that the ComponentId filter missed), retry with
        // Euclidean. This is the pre-fix behaviour and keeps the old "any
        // answer is better than no answer" property on malformed graphs.
        if (bestNode == -1)
        {
            foreach (int nodeId in _graph.GetNodesOnTaxiway(taxiwayName))
            {
                var node = _graph.Nodes[nodeId];
                if (node.ComponentId != targetComponent) continue;
                if (BlockedNodes.Count > 0 && BlockedNodes.Contains(nodeId)) continue;

                double euclid = TaxiGraph.CalculateDistanceMeters(
                    targetNode.Latitude, targetNode.Longitude, node.Latitude, node.Longitude);

                if (euclid < bestDist)
                {
                    bestDist = euclid;
                    bestNode = nodeId;
                }
            }
        }

        return bestNode;
    }

    /// <summary>
    /// When two consecutive taxiways in an ATC clearance don't share a graph
    /// node (typically because they sit on opposite sides of a runway crossing —
    /// e.g., K14 → 30L → M17 at OMDB), find the closest pair of nodes between
    /// them. Caller can then route along the current taxiway to the exit node,
    /// bridge with a free A* across the runway, and resume the constrained
    /// sequence at the entry node on the next taxiway.
    ///
    /// MAX_BRIDGE_METERS bounds how far the bridge can be: a runway crossing
    /// is typically &lt;100 m wide; allowing a much larger gap would let the
    /// router silently route through unrelated airport infrastructure when an
    /// ATC clearance is genuinely wrong. 400 m (raised from 200 m with the
    /// total-cost scoring below, #76) gives slack for unusual layouts
    /// (extra-wide RWY + shoulder + pavement margins, a handoff point further
    /// along a looping taxiway) without enabling silent half-airport jumps [RTE-24].
    /// </summary>
    private (int exitOnCurrent, int entryOnNext) FindRunwayBridge(
        string currentTaxiway,
        string nextTaxiway,
        int currentNodeId,
        Dictionary<int, double> distFromFinalDest)
    {
        // With total-cost scoring, a large bridge gap is naturally penalised:
        // the entryDist (cost to reach the exit node on currentTaxiway) plus
        // the destDist (cost from the entry node on nextTaxiway to the
        // destination) will be huge if the bridge is the wrong one. The old
        // Euclidean-only guard (200 m) was the sole filter and caused a
        // pathological case at KDEN BN→G: the closest BN node to G is the
        // western tip of BN's south loop arm (~73 m gap) but reaching it
        // requires traversing the entire BN hairpin loop (~600 m detour),
        // while the correct handoff point on BN's northern arm is ~330 m
        // from G (gap exceeds the old 200 m cap) but reachable directly.
        // Raising the cap to 400 m and scoring by total route cost picks the
        // northern-arm handoff because its entryDist is much shorter.
        const double MAX_BRIDGE_METERS = 400.0;

        // Dijkstra from the current node: gives graph cost to reach each
        // candidate exit on currentTaxiway. Paired with the caller-supplied
        // distFromFinalDest (graph cost from each candidate entry on
        // nextTaxiway to the destination), this scores every candidate bridge
        // by total route length, not just gap width.
        var distFromEntry = ComputeGraphDistancesFrom(currentNodeId);

        int bestExit = -1, bestEntry = -1;
        double bestScore = double.MaxValue;

        // O(N×M) over the two taxiways' nodes — cheap at hub scale.
        foreach (var nA in _graph.Nodes.Values)
        {
            if (!nA.TaxiwayNames.Contains(currentTaxiway)) continue;
            if (BlockedNodes.Count > 0 && BlockedNodes.Contains(nA.NodeId)) continue;
            if (!distFromEntry.TryGetValue(nA.NodeId, out double entryDist)) continue;

            foreach (var nB in _graph.Nodes.Values)
            {
                if (!nB.TaxiwayNames.Contains(nextTaxiway)) continue;
                if (BlockedNodes.Count > 0 && BlockedNodes.Contains(nB.NodeId)) continue;
                if (!distFromFinalDest.TryGetValue(nB.NodeId, out double destDist)) continue;

                double bridgeGap = TaxiGraph.CalculateDistanceMeters(
                    nA.Latitude, nA.Longitude, nB.Latitude, nB.Longitude);
                if (bridgeGap > MAX_BRIDGE_METERS) continue;

                double score = entryDist + bridgeGap + destDist;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestExit = nA.NodeId;
                    bestEntry = nB.NodeId;
                }
            }
        }

        if (bestExit != -1)
        {
            double gap = TaxiGraph.CalculateDistanceMeters(
                _graph.Nodes[bestExit].Latitude, _graph.Nodes[bestExit].Longitude,
                _graph.Nodes[bestEntry].Latitude, _graph.Nodes[bestEntry].Longitude);
            Log($"Runway bridge candidate: '{currentTaxiway}' node {bestExit} → '{nextTaxiway}' node {bestEntry}, gap {gap:F1} m, total score {bestScore:F0} m");
        }

        return (bestExit, bestEntry);
    }

    /// <summary>
    /// The free leg of a bridge (<see cref="FindRunwayBridge"/>), cut at the first node where it
    /// reaches the next cleared taxiway, so the route joins that taxiway there and drives it on
    /// under the clearance. The node the bridge was scored for can lie beyond: the score takes the
    /// straight-line gap and the cost on to the destination by ANY path, so a dead end of the
    /// taxiway with a short way on of its own wins, and the free leg then drives the cleared
    /// taxiway, and any cross-link it likes, out to it. KEWR "A11, A12, A, AA" (2026-11-26): A11
    /// and A12 meet only through an unnamed link; the bridge picked the end of a curved A12 branch,
    /// the free leg ran along A12, over F2 (never cleared) and out along the branch, and the A12
    /// leg came straight back the same way. The leg is cut only where the cleared taxiway itself
    /// leads from that node to the scored one (<see cref="AStarSearchStrict"/>: its own edges and
    /// the short hops onto them, never a gap chained through other taxiways, which can run out and
    /// back; HECA "A2, A1", where A1 is drawn in two pieces). A node of the same name on another
    /// piece is not a join, and the next node of the name is tried. True when the leg was cut.
    /// </summary>
    private bool JoinWhereTheBridgeFirstReaches(ref List<int> bridge, string nextTaxiway, string step)
    {
        int scored = bridge[^1];
        for (int k = 1; k < bridge.Count - 1; k++)
        {
            if (!_graph.Nodes.TryGetValue(bridge[k], out var node) || !node.TaxiwayNames.Contains(nextTaxiway)) continue;
            if (AStarSearchStrict(bridge[k], scored, nextTaxiway) == null)
            {
                Log($"{step} bridge passes '{nextTaxiway}' node {bridge[k]}, which does not lead along it to {scored}: not joined there");
                continue;
            }
            Log($"{step} bridge reaches '{nextTaxiway}' at node {bridge[k]}, {bridge.Count - 1 - k} nodes before {scored}: joined there");
            bridge = bridge.Take(k + 1).ToList();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Joins a leg that begins where the route stands, at a seam a bridge leg makes (the bridge
    /// itself, or the leg after it). With <paramref name="unwind"/>, the route never goes out and
    /// back there. First, while the leg goes straight back over the edge the route arrived by, both
    /// drop that edge: a bridge leaves from a node its score chose, and where the bridge before it
    /// joined a short taxiway early, that node is the join itself, so the next bridge left straight
    /// back the way the route came (RJTT "R, E3, E, E2, C2": out to E's node and back over E3, out
    /// along a 20 m stub to E2's and back). Then, a leg that comes back to a node the route reached at
    /// or after <paramref name="floor"/> (the start of the leg the bridge leaves from, or of the bridge
    /// before the leg after it) cuts the loop out: the score can pick an exit beyond the turn-off with
    /// a turning loop at the tip, and the route drove the cleared taxiway out to it and back over the
    /// same nodes (OIII "B, D": 268 m east along B to a loop by runway 11R, and back west). Neither
    /// reaches below the floor, so the legs of earlier cleared taxiways, and the approach to the first,
    /// stay as they were: unwound past the first taxiway's entry, a route that went out to it and
    /// straight back no longer touched it at all, the start check that leaves such a taxiway out
    /// ([RTE-39]) no longer saw it, and the lead-in was refused (ZJHK "S2, B, B10" from Gate 12: the
    /// route then began on S2, 430 m away in a straight line). The route still ends where the leg
    /// does, and only gets shorter.
    /// </summary>
    private void AppendBridgeLeg(List<int> path, List<int> leg, bool unwind, int floor, string step, ref int refined)
    {
        int k = (path.Count > 0 && leg.Count > 0 && path[^1] == leg[0]) ? 1 : 0;
        floor = Math.Min(Math.Max(0, floor), Math.Max(0, path.Count - 1));
        int unwound = 0;
        while (unwind && k == 1 + unwound && path.Count - 1 > floor && k < leg.Count && path[^2] == leg[k])
        {
            path.RemoveAt(path.Count - 1);
            k++;
            unwound++;
        }
        int cut = 0;
        for (int j = k; j < leg.Count; j++)
        {
            int again = -1;
            if (unwind)
                for (int r = path.Count - 1; r >= floor; r--)
                    if (path[r] == leg[j]) { again = r; break; }
            if (again == -1)
            {
                path.Add(leg[j]);
                continue;
            }
            cut += path.Count - 1 - again;
            path.RemoveRange(again + 1, path.Count - 1 - again);
        }
        if (unwound > 0 || cut > 0)
        {
            refined++;
            Log($"{step}: where the bridge leg meets the route, {unwound} edge{(unwound == 1 ? "" : "s")} of out-and-back " +
                $"and {cut} of loop taken out");
        }
    }

    /// <summary>
    /// Finds the best intersection node between two taxiways.
    /// Picks the candidate that minimizes total GRAPH distance:
    /// dist_from_entry[candidate] + dist_from_destination[candidate].
    /// Uses Dijkstra-based costs (precomputed in FindConstrainedPath for the
    /// destination side; computed per call for the entry side) rather than
    /// Euclidean — see FindNearestNodeOnTaxiwayToTarget for why (dead-end
    /// intersection nodes that are closer in straight-line distance to
    /// either endpoint can require a long graph backtrack to escape).
    /// </summary>
    /// <summary>Every node the two taxiways share (see FindBestIntersection for the fallback rule).</summary>
    private List<int> IntersectionNodes(string taxiway1, string taxiway2)
    {
        var onTaxiway1 = new HashSet<int>(_graph.GetNodesOnTaxiway(taxiway1));
        var onTaxiway2 = new HashSet<int>(_graph.GetNodesOnTaxiway(taxiway2));
        var intersections = new List<int>();
        foreach (var node in _graph.Nodes.Values)
        {
            if (BlockedNodes.Count > 0 && BlockedNodes.Contains(node.NodeId)) continue;
            bool hasTaxiway1 = onTaxiway1.Contains(node.NodeId) || node.TaxiwayNames.Contains(taxiway1);
            bool hasTaxiway2 = onTaxiway2.Contains(node.NodeId) || node.TaxiwayNames.Contains(taxiway2);
            if (hasTaxiway1 && hasTaxiway2)
                intersections.Add(node.NodeId);
        }
        return intersections;
    }

    /// <summary>
    /// For a clearance "X, Y, X" (a short Y bypass or connector between two stretches of X): the
    /// X→Y junction J1 minimising entry→J1 + J1→J2 + J2→destination over the Y→X junctions J2 ≠ J1.
    /// Choosing J1 alone by entry + destination cost picks Y's FAR junction (it is nearer the
    /// destination), so Y is then run backwards and X forwards again — a loop (LEBL 24R "T, T2, T",
    /// +740 m; VirtualPilot 2026-09-18). -1 when fewer than two junctions exist or none is reachable.
    /// </summary>
    private int FindBestDetourEntry(int currentNodeId, Dictionary<int, double> distFromFinalDest, string x, string y)
    {
        var junctions = IntersectionNodes(x, y);
        if (junctions.Count < 2 || junctions.Count > 12) return -1;
        // Junctions on runway pavement take no part unless the route is already on the runway there:
        // honouring the repeat by turning on a runway is worse than the old zero-length Y (OTBH).
        if (!OnRunwayPavement(currentNodeId))
            junctions = junctions.Where(j => !OnRunwayPavement(j)).ToList();
        if (junctions.Count < 2) return -1;
        var fromEntry = ComputeGraphDistancesFrom(currentNodeId);
        int best = -1;
        double bestScore = double.MaxValue;
        foreach (int j1 in junctions)
        {
            if (!fromEntry.TryGetValue(j1, out double e)) continue;
            var fromJ1 = ComputeGraphDistancesFrom(j1);
            foreach (int j2 in junctions)
            {
                if (j2 == j1) continue;
                if (!fromJ1.TryGetValue(j2, out double d12) || !distFromFinalDest.TryGetValue(j2, out double dd)) continue;
                double score = e + d12 + dd;
                if (score < bestScore) { bestScore = score; best = j1; }
            }
        }
        return best;
    }

    /// <summary>The node IS the destination, or stands on the destination node's runway pavement.</summary>
    private bool IsOnDestinationRunway(int nodeId, int endNodeId)
    {
        if (nodeId == endNodeId) return true;
        if (!_graph.Nodes.TryGetValue(nodeId, out var n) || !_graph.Nodes.TryGetValue(endNodeId, out var e)) return false;
        var destRunway = RouteRunwayCrossings.RunwayUnder(_graph.RunwayCenterlines, e.Latitude, e.Longitude);
        return destRunway != null
            && ReferenceEquals(destRunway, RouteRunwayCrossings.RunwayUnder(_graph.RunwayCenterlines, n.Latitude, n.Longitude));
    }

    /// <summary>Does the route's own taxiway naming contain the clearance, in order (gaps allowed)?</summary>
    internal static bool VisitsInOrder(TaxiRoute route, List<string> clearance)
    {
        int k = 0;
        foreach (var seg in route.Segments)
            if (k < clearance.Count && string.Equals(seg.TaxiwayName, clearance[k], StringComparison.OrdinalIgnoreCase))
                k++;
        return k == clearance.Count;
    }

    private const double REPEAT_HONOUR_MAX_RATIO = 1.25;
    private const double REPEAT_HONOUR_MAX_PAD_M = 100.0;

    private bool OnRunwayPavement(int nodeId)
        => _graph.Nodes.TryGetValue(nodeId, out var n)
           && RouteRunwayCrossings.RunwayUnder(_graph.RunwayCenterlines, n.Latitude, n.Longitude) != null;

    internal int FindBestIntersection(int currentNodeId, int finalDestId, Dictionary<int, double> distFromFinalDest, string taxiway1, string taxiway2,
        int excludeNodeId = -1)
    {
        // Hybrid: the O(E)-per-node edge scan is replaced by an O(1) index-set lookup
        // (proven exactly equal in content — TaxiGraph.RegisterTaxiwayNode is called
        // for a node iff a real, non-degenerate edge with that TaxiwayName touches it).
        // The node.TaxiwayNames.Contains fallback is INTENTIONALLY kept: a zero-length
        // ("degenerate") taxi_path segment resolves both endpoints to the same node and
        // still adds the taxiway name to that node's TaxiwayNames set (TaxiGraph.Build),
        // but is skipped before RegisterTaxiwayNode runs — so the index alone would
        // silently drop those nodes here. Dropping the fallback would change which
        // intersection candidates are found, which this routing code must not risk.
        var intersections = IntersectionNodes(taxiway1, taxiway2);

        if (intersections.Count == 0)
            return -1;

        // Graph-distance lookup from entry. Same motivation as
        // FindNearestNodeOnTaxiwayToTarget — dead-end candidate intersections
        // are rejected because their graph cost round-trips back through the
        // only neighbour. The destination-side Dijkstra is precomputed once
        // per FindConstrainedPath run and passed in via distFromFinalDest;
        // the entry side advances along the route, so it's per-call here.
        var distFromEntry = ComputeGraphDistancesFrom(currentNodeId);

        int bestNode = -1;
        double bestScore = double.MaxValue;

        // The alternative to the excluded entry junction must not be on runway pavement unless the
        // entry itself is: honouring "A, C, A" by turning around on a runway (OTBH gate to gate: the
        // only other A/C junction is on 16L) is worse than the zero-length C it replaces.
        bool avoidRunway = excludeNodeId != -1 && !OnRunwayPavement(excludeNodeId);
        foreach (int nodeId in intersections)
        {
            if (nodeId == excludeNodeId) continue;
            if (avoidRunway && OnRunwayPavement(nodeId)) continue;
            if (!distFromEntry.TryGetValue(nodeId, out double entryDist))
                continue; // unreachable from entry
            if (!distFromFinalDest.TryGetValue(nodeId, out double destDist))
                continue; // unreachable from destination
            double score = entryDist + destDist;
            if (score < bestScore)
            {
                bestScore = score;
                bestNode = nodeId;
            }
        }

        // Returning -1 when every candidate is graph-unreachable from
        // entry or destination is intentional — unlike FindNearestNodeOnTaxiwayToTarget,
        // we do NOT fall back to Euclidean here. The old Euclidean picker
        // would return a node A* then couldn't reach, producing a silently
        // wrong constrained path. Callers' -1 path (FindRunwayBridge then
        // shortest-path fallback) is the correct recovery.
        return bestNode;
    }

    /// <summary>
    /// A* search that follows the specified taxiway, with gap-bridging.
    /// Navdata often splits a single real-world taxiway into disconnected graph sections
    /// (endpoints 5-30m apart that don't merge). This search primarily uses taxiway edges
    /// but allows short hops through non-taxiway edges (≤50m) when both endpoints
    /// have edges on the required taxiway — bridging the data gaps without leaving the taxiway.
    /// </summary>
    private List<int>? AStarSearchStrict(int startId, int goalId, string requiredTaxiway)
    {
        if (!_graph.Nodes.ContainsKey(startId) || !_graph.Nodes.ContainsKey(goalId))
        {
            Log($"Strict: node missing - start {startId} exists={_graph.Nodes.ContainsKey(startId)}, goal {goalId} exists={_graph.Nodes.ContainsKey(goalId)}");
            return null;
        }

        // Pre-build set of nodes that have at least one edge on the required taxiway
        var nodesOnTaxiway = new HashSet<int>(_graph.GetNodesOnTaxiway(requiredTaxiway));

        Log($"Strict '{requiredTaxiway}': {nodesOnTaxiway.Count} nodes on taxiway, start {startId} on={nodesOnTaxiway.Contains(startId)}, goal {goalId} on={nodesOnTaxiway.Contains(goalId)}");

        var goalNode = _graph.Nodes[goalId];

        var openSet = new PriorityQueue<int, double>();
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, double>();
        var closedSet = new HashSet<int>();

        gScore[startId] = 0;
        openSet.Enqueue(startId, Heuristic(startId, goalNode));

        int iterations = 0;
        int bridgeCount = 0;
        const int MAX_ITERATIONS = 500000;
        const double MAX_BRIDGE_DISTANCE_M = 50.0;

        while (openSet.Count > 0 && iterations < MAX_ITERATIONS)
        {
            iterations++;
            int current = openSet.Dequeue();

            if (current == goalId)
            {
                // bridgeCount counts every bridge-edge RELAXATION during the search (a candidate
                // improvement considered, not necessarily kept) — it is not the number of bridges
                // on the final reconstructed path, so the log wording says "relaxations" honestly.
                Log($"Strict '{requiredTaxiway}': FOUND path in {iterations} iterations, {closedSet.Count} nodes explored, {bridgeCount} bridge-edge relaxations");
                return ReconstructPath(cameFrom, current);
            }

            if (closedSet.Contains(current))
                continue;
            closedSet.Add(current);

            if (!_graph.Adjacency.TryGetValue(current, out var edges))
                continue;

            double currentG = gScore.GetValueOrDefault(current, double.MaxValue);

            foreach (var edge in edges)
            {
                if (closedSet.Contains(edge.ToNodeId))
                    continue;
                if (BlockedNodes.Count > 0 && BlockedNodes.Contains(edge.ToNodeId))
                    continue;

                bool isOnTaxiway = edge.TaxiwayName.Equals(requiredTaxiway, StringComparison.OrdinalIgnoreCase);

                if (!isOnTaxiway)
                {
                    // Allow short bridge hops: the destination node must also be on the taxiway
                    // and the edge must be short (≤50m) — these are navdata gaps, not real detours
                    if (edge.DistanceMeters > MAX_BRIDGE_DISTANCE_M)
                        continue;
                    if (!nodesOnTaxiway.Contains(edge.ToNodeId))
                        continue;
                }

                double cost = edge.DistanceMeters;
                if (!isOnTaxiway)
                {
                    // Small penalty to prefer real taxiway edges over bridges
                    cost *= 1.5;
                    bridgeCount++;
                }

                double tentativeG = currentG + cost;

                if (tentativeG < gScore.GetValueOrDefault(edge.ToNodeId, double.MaxValue))
                {
                    cameFrom[edge.ToNodeId] = current;
                    gScore[edge.ToNodeId] = tentativeG;
                    double h = Heuristic(edge.ToNodeId, goalNode);
                    openSet.Enqueue(edge.ToNodeId, tentativeG + h);
                }
            }
        }

        Log($"Strict '{requiredTaxiway}': FAILED after {iterations} iterations, {closedSet.Count} nodes explored (max={MAX_ITERATIONS})");
        return null;
    }

    /// <summary>
    /// Routes along a taxiway, chaining through disconnected sections.
    /// Navdata often has gaps where a real-world taxiway is split into separate graph components.
    /// This method: 1) tries strict A*, 2) if it fails, finds the closest reachable node to the goal,
    /// 3) unconstrained-hops to the next section of the taxiway, 4) repeats until goal is reached.
    /// </summary>
    internal List<int>? AStarSearchWithGapChaining(int startId, int goalId, string requiredTaxiway)
    {
        // Try strict first — works when the taxiway is one connected component
        var direct = AStarSearchStrict(startId, goalId, requiredTaxiway);
        if (direct != null)
            return direct;

        Log($"GapChain '{requiredTaxiway}': strict failed, attempting gap chaining {startId} → {goalId}");

        // A blocked node cuts ITS taxiway where it stands, and no hop may step over it: see
        // HopStepsOverABlock. A navdata gap elsewhere on the same taxiway is chained as always (KBOS C
        // has 400 m with no name 900 m past its blocked H junction; refusing every gap on a taxiway
        // that carries a block refused that left turn outright).
        List<TaxiNode>? blocksHere = null;
        HashSet<int>? behindBlocks = null;
        if (BlockedNodes.Count > 0)
        {
            foreach (int id in _graph.GetNodesOnTaxiway(requiredTaxiway))
                if (BlockedNodes.Contains(id)) (blocksHere ??= new List<TaxiNode>()).Add(_graph.Nodes[id]);
            if (blocksHere != null) behindBlocks = SectionsBehindBlocks(startId, requiredTaxiway, blocksHere);
        }

        // Chain through disconnected sections
        var fullPath = new List<int>();
        int currentNode = startId;
        int maxChainSteps = 10; // safety limit

        for (int step = 0; step < maxChainSteps; step++)
        {
            // BFS: find all nodes reachable from currentNode on this taxiway
            var reachable = BfsReachable(currentNode, requiredTaxiway);

            if (reachable.Contains(goalId))
            {
                // Goal is reachable — do strict search to reach it
                var seg = AStarSearchStrict(currentNode, goalId, requiredTaxiway);
                if (seg != null)
                {
                    int si = (fullPath.Count > 0 && seg.Count > 0 && fullPath[^1] == seg[0]) ? 1 : 0;
                    for (int j = si; j < seg.Count; j++)
                        fullPath.Add(seg[j]);
                    Log($"GapChain '{requiredTaxiway}': step {step + 1} reached goal, total {fullPath.Count} nodes");
                    return fullPath;
                }
                break; // shouldn't happen, but safety
            }

            // Goal not reachable. Find the reachable node closest to the goal.
            var goalNode = _graph.Nodes[goalId];
            int bestReachable = -1;
            double bestDist = double.MaxValue;
            foreach (int nodeId in reachable)
            {
                var n = _graph.Nodes[nodeId];
                double d = TaxiGraph.CalculateDistanceMeters(n.Latitude, n.Longitude, goalNode.Latitude, goalNode.Longitude);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestReachable = nodeId;
                }
            }

            if (bestReachable == -1)
            {
                Log($"GapChain '{requiredTaxiway}': no reachable nodes from {currentNode}");
                break;
            }

            // Route strictly to the edge of this section
            if (bestReachable != currentNode)
            {
                var seg = AStarSearchStrict(currentNode, bestReachable, requiredTaxiway);
                if (seg != null)
                {
                    int si = (fullPath.Count > 0 && seg.Count > 0 && fullPath[^1] == seg[0]) ? 1 : 0;
                    for (int j = si; j < seg.Count; j++)
                        fullPath.Add(seg[j]);
                }
                else
                {
                    // Can't even reach the edge of our own section
                    Log($"GapChain '{requiredTaxiway}': can't reach edge node {bestReachable}");
                    break;
                }
            }

            // Now find the nearest node on the NEXT section of the same taxiway
            // that isn't in our current reachable set
            int nextSectionNode = -1;
            double nextDist = double.MaxValue;
            var edgeNode = _graph.Nodes[bestReachable];

            foreach (int nodeId in _graph.GetNodesOnTaxiway(requiredTaxiway))
            {
                if (reachable.Contains(nodeId)) continue;
                if (blocksHere != null && HopStepsOverABlock(edgeNode, nodeId, blocksHere, behindBlocks!)) continue;

                var n = _graph.Nodes[nodeId];
                double d = TaxiGraph.CalculateDistanceMeters(edgeNode.Latitude, edgeNode.Longitude, n.Latitude, n.Longitude);
                if (d < nextDist)
                {
                    nextDist = d;
                    nextSectionNode = nodeId;
                }
            }

            if (nextSectionNode == -1 || nextDist > 500.0)
            {
                Log($"GapChain '{requiredTaxiway}': no next section found (closest={nextDist:F0}m)");
                break;
            }

            // Unconstrained hop from edge of current section to start of next section
            var hop = AStarSearch(bestReachable, nextSectionNode, null);
            if (hop == null)
            {
                Log($"GapChain '{requiredTaxiway}': can't hop from {bestReachable} to {nextSectionNode} ({nextDist:F0}m)");
                break;
            }

            Log($"GapChain '{requiredTaxiway}': step {step + 1} bridged gap {bestReachable} → {nextSectionNode} ({nextDist:F0}m, {hop.Count} nodes)");

            int hi = (fullPath.Count > 0 && hop.Count > 0 && fullPath[^1] == hop[0]) ? 1 : 0;
            for (int j = hi; j < hop.Count; j++)
                fullPath.Add(hop[j]);

            currentNode = nextSectionNode;
        }

        if (fullPath.Count < 2)
        {
            Log($"GapChain '{requiredTaxiway}': FAILED, path too short ({fullPath.Count} nodes)");
            return null;
        }

        Log($"GapChain '{requiredTaxiway}': partial path {fullPath.Count} nodes (may not have reached goal)");
        return null; // only return if we reached the goal (handled above)
    }

    /// <summary>
    /// Every node of the taxiway in a section that touches a blocked node from a side the chain did
    /// not start on: the taxiway as it runs on behind the block.
    /// </summary>
    private HashSet<int> SectionsBehindBlocks(int startId, string taxiwayName, List<TaxiNode> blocksOnTaxiway)
    {
        var own = BfsReachable(startId, taxiwayName);
        var behind = new HashSet<int>();
        foreach (var block in blocksOnTaxiway)
        {
            if (!_graph.Adjacency.TryGetValue(block.NodeId, out var edges)) continue;
            foreach (var edge in edges)
            {
                if (!edge.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
                int next = edge.ToNodeId;
                if (BlockedNodes.Contains(next) || own.Contains(next) || behind.Contains(next)) continue;
                behind.UnionWith(BfsReachable(next, taxiwayName));
            }
        }
        return behind;
    }

    /// <summary>
    /// Would a gap-chaining hop from <paramref name="from"/> to this node of the same taxiway step
    /// over a block? It would if the node is blocked, if it lies in a section running on behind a
    /// block (<see cref="SectionsBehindBlocks"/>), or if a blocked node of the taxiway stands between
    /// the two (seen from the block they lie more than 90 degrees apart, and the block lies near the
    /// line of the hop), which also covers a section behind the block that a navdata gap of its own
    /// detaches from it. "Near" is <see cref="HopBlockCorridorMinMeters"/> or a quarter of the hop,
    /// whichever is more: a block well off to the side (a pick's whole other arm is blocked, and a
    /// curving taxiway puts some of it inside the hop's circle) does not stand between them.
    /// </summary>
    private bool HopStepsOverABlock(TaxiNode from, int toNodeId, List<TaxiNode> blocksOnTaxiway, HashSet<int> behindBlocks)
    {
        if (BlockedNodes.Contains(toNodeId) || behindBlocks.Contains(toNodeId)) return true;
        var to = _graph.Nodes[toNodeId];
        double corridorM = Math.Max(HopBlockCorridorMinMeters, HopBlockCorridorFraction *
            TaxiGraph.FastDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude));
        foreach (var block in blocksOnTaxiway)
        {
            double east = Math.Cos(block.Latitude * Math.PI / 180.0);
            double fromX = (from.Longitude - block.Longitude) * east, fromY = from.Latitude - block.Latitude;
            double toX = (to.Longitude - block.Longitude) * east, toY = to.Latitude - block.Latitude;
            if (fromX * toX + fromY * toY < 0 &&
                GuidanceGeometry.PerpendicularDistanceToSegmentMeters(block.Latitude, block.Longitude,
                    from.Latitude, from.Longitude, to.Latitude, to.Longitude) <= corridorM)
                return true;
        }
        return false;
    }

    /// <summary>The floor of the corridor round a gap hop inside which a blocked node stands between its ends.</summary>
    internal const double HopBlockCorridorMinMeters = 50.0;
    /// <summary>The corridor round a gap hop, as a fraction of the hop's length, when that is wider than the floor.</summary>
    internal const double HopBlockCorridorFraction = 0.25;

    /// <summary>
    /// BFS to find all nodes reachable from startId using only edges on the given taxiway.
    /// </summary>
    private HashSet<int> BfsReachable(int startId, string taxiwayName)
    {
        var visited = new HashSet<int> { startId };
        var queue = new Queue<int>();
        queue.Enqueue(startId);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!_graph.Adjacency.TryGetValue(current, out var edges))
                continue;

            foreach (var edge in edges)
            {
                if (!edge.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (BlockedNodes.Count > 0 && BlockedNodes.Contains(edge.ToNodeId))
                    continue;
                if (visited.Add(edge.ToNodeId))
                    queue.Enqueue(edge.ToNodeId);
            }
        }

        return visited;
    }

    /// <summary>
    /// Single-source shortest-paths via Dijkstra. Returns a map of node IDs to
    /// the shortest graph distance (in metres) from <paramref name="sourceId"/>
    /// to that node. Unreachable nodes are absent from the map; the source
    /// itself has cost 0. The graph is treated as undirected because every
    /// taxi_path is inserted as two reverse edges in TaxiGraph.Build, so
    /// forward Dijkstra from the destination yields cost-to-destination for
    /// every node in the same connected component.
    ///
    /// Used by FindNearestNodeOnTaxiwayToTarget to pick the taxiway exit
    /// that actually leads to the destination by the shortest graph path,
    /// not by straight-line distance. The Euclidean version silently picked
    /// dead-end endpoints (KDEN M4 northern endpoint: closer to gate A 60
    /// in a straight line, but a graph dead-end requiring 1+ km of backtrack
    /// to escape).
    ///
    /// Cost: O((V + E) log V). Typical airport graph: 2k-3k nodes, 5k-8k
    /// edges — runs in well under 50 ms on warm CPU. Called once per recalc,
    /// so the cost is dwarfed by the recalc cooldown (15 s).
    /// </summary>
    internal Dictionary<int, double> ComputeGraphDistancesFrom(int sourceId)
    {
        var dist = new Dictionary<int, double>();
        if (!_graph.Nodes.ContainsKey(sourceId)) return dist;

        var visited = new HashSet<int>();
        var pq = new PriorityQueue<int, double>();
        dist[sourceId] = 0.0;
        pq.Enqueue(sourceId, 0.0);

        int iterations = 0;
        const int MAX_ITERATIONS = 200000;

        while (pq.Count > 0 && iterations < MAX_ITERATIONS)
        {
            iterations++;
            int current = pq.Dequeue();
            if (!visited.Add(current)) continue;

            double currentDist = dist[current];

            if (!_graph.Adjacency.TryGetValue(current, out var edges)) continue;

            foreach (var edge in edges)
            {
                if (visited.Contains(edge.ToNodeId)) continue;
                if (BlockedNodes.Count > 0 && BlockedNodes.Contains(edge.ToNodeId)) continue;
                double tentative = currentDist + edge.DistanceMeters;
                if (tentative < dist.GetValueOrDefault(edge.ToNodeId, double.MaxValue))
                {
                    dist[edge.ToNodeId] = tentative;
                    pq.Enqueue(edge.ToNodeId, tentative);
                }
            }
        }

        return dist;
    }

    /// <summary>
    /// A* search with optional taxiway preference (soft constraint).
    /// When preferredTaxiway is set, non-preferred edges cost 10x more.
    /// </summary>
    private List<int>? AStarSearch(int startId, int goalId, string? preferredTaxiway, HashSet<int>? alsoBlocked = null)
    {
        if (!_graph.Nodes.ContainsKey(startId) || !_graph.Nodes.ContainsKey(goalId))
            return null;

        var goalNode = _graph.Nodes[goalId];

        var openSet = new PriorityQueue<int, double>();
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, double>();
        var closedSet = new HashSet<int>();

        gScore[startId] = 0;
        openSet.Enqueue(startId, Heuristic(startId, goalNode));

        int iterations = 0;
        const int MAX_ITERATIONS = 100000;

        while (openSet.Count > 0 && iterations < MAX_ITERATIONS)
        {
            iterations++;
            int current = openSet.Dequeue();

            if (current == goalId)
                return ReconstructPath(cameFrom, current);

            if (closedSet.Contains(current))
                continue;
            closedSet.Add(current);

            if (!_graph.Adjacency.TryGetValue(current, out var edges))
                continue;

            double currentG = gScore.GetValueOrDefault(current, double.MaxValue);

            foreach (var edge in edges)
            {
                if (closedSet.Contains(edge.ToNodeId))
                    continue;
                if (BlockedNodes.Count > 0 && BlockedNodes.Contains(edge.ToNodeId))
                    continue;
                if (alsoBlocked != null && alsoBlocked.Contains(edge.ToNodeId))
                    continue;

                double cost = edge.DistanceMeters;
                if (!string.IsNullOrEmpty(preferredTaxiway) &&
                    !edge.TaxiwayName.Equals(preferredTaxiway, StringComparison.OrdinalIgnoreCase))
                {
                    cost *= 10.0;
                }

                double tentativeG = currentG + cost;

                if (tentativeG < gScore.GetValueOrDefault(edge.ToNodeId, double.MaxValue))
                {
                    cameFrom[edge.ToNodeId] = current;
                    gScore[edge.ToNodeId] = tentativeG;
                    double h = Heuristic(edge.ToNodeId, goalNode);
                    openSet.Enqueue(edge.ToNodeId, tentativeG + h);
                }
            }
        }

        return null;
    }

    private double Heuristic(int nodeId, TaxiNode goal)
    {
        var node = _graph.Nodes[nodeId];
        return TaxiGraph.CalculateDistanceMeters(node.Latitude, node.Longitude, goal.Latitude, goal.Longitude);
    }

    private static List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
    {
        var path = new List<int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Builds a TaxiRoute from a sequence of node IDs.
    /// </summary>
    private TaxiRoute BuildRoute(List<int> nodeIds)
    {
        var route = new TaxiRoute();

        for (int i = 0; i < nodeIds.Count - 1; i++)
        {
            int fromId = nodeIds[i];
            int toId = nodeIds[i + 1];
            var edge = _graph.GetEdge(fromId, toId);
            if (edge == null) continue;
            AppendWithTurn(route, EdgeSegment(_graph.Nodes[fromId], _graph.Nodes[toId], edge));
        }

        SetRouteDistances(route);
        return route;
    }

    /// <summary>A route segment along one graph edge (IsHoldShortPoint is set only by the hold-short passes).</summary>
    private static TaxiRouteSegment EdgeSegment(TaxiNode fromNode, TaxiNode toNode, TaxiEdge edge) => new()
    {
        FromNode = fromNode,
        ToNode = toNode,
        DistanceMeters = edge.DistanceMeters,
        TaxiwayName = edge.TaxiwayName,
        BearingDegrees = edge.BearingDegrees,
        PathWidth = edge.WidthFeet,
        HoldShortRunway = toNode.HoldShortName,
    };

    /// <summary>Appends a segment, with its turn from the one before it (the first stays straight).</summary>
    private static void AppendWithTurn(TaxiRoute route, TaxiRouteSegment segment)
    {
        if (route.Segments.Count > 0)
        {
            segment.TurnAngleDegrees = NormalizeAngle(segment.BearingDegrees - route.Segments[^1].BearingDegrees);
            segment.TurnDirection = GetTurnDirection(segment.TurnAngleDegrees);
        }
        route.Segments.Add(segment);
    }

    /// <summary>Cumulative and remaining distances of every segment, and the route's total.</summary>
    private static void SetRouteDistances(TaxiRoute route)
    {
        double cumulative = 0;
        foreach (var s in route.Segments)
        {
            cumulative += s.DistanceMeters;
            s.CumulativeDistanceMeters = cumulative;
        }
        foreach (var s in route.Segments)
            s.RemainingDistanceMeters = cumulative - s.CumulativeDistanceMeters + s.DistanceMeters;
        route.TotalDistanceMeters = cumulative;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UNCHARTED APRON CROSSING — routing to a destination on a DISCONNECTED
    // component of the taxi network.
    //
    // Some sceneries split an airport's taxi paths into islands separated by
    // real, drivable pavement that simply has no taxi paths drawn (default LMML
    // 2026-08-31, live: the P/N/NW aprons are a 503-node island 192 m of bare
    // apron away from the B..G network an arriving aircraft is on — ATC clears
    // stands over there exactly as in the real world, where the pavement is
    // continuous). No sequence of charted taxiways can cross such a gap, and the
    // silent gap-bridges (BridgeSameNamedGaps ≤40 m, BridgeTinyDeadEndGaps ≤6 m)
    // rightly refuse it. This path instead builds BOTH charted halves normally
    // and joins them with ONE explicit synthetic segment (IsUncharted = true)
    // that guidance announces out loud — the same design language as the
    // backtrack departure's geometric steer, never a silent shortcut.
    //
    // Safety gates: crossing gap capped (UNCHARTED_CROSSING_MAX_M); the straight
    // line must stay clear of every runway pavement corridor (the caller passes
    // TaxiGraph.LineIsClearOfRunwayCorridors); both charted halves are built by
    // the normal router (A* never runs across the gap).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Longest synthetic crossing this router will build. Beyond this the
    /// "pavement" between two islands is more likely grass/buildings than apron.
    /// Sized on the motivating airport, measured against the live DB with
    /// tools/UnchartedCrossingProbe: LMML's NARROWEST waist (193 m, taxiway F's
    /// end to the P apron) is correctly refused by the runway-corridor check —
    /// F's end node sits ~5 m off the 13/31 centreline, so that line clips the
    /// runway — and the safe crossing (the diagonal the stranded pilot actually
    /// drove on 2026-08-31, parallel-taxiway end → P-apron corner, clear of both
    /// runways by >90 m) is ~430 m. A 250 m cap refused the exact case the
    /// feature exists for.</summary>
    public const double UNCHARTED_CROSSING_MAX_M = 450.0;

    /// <summary>Score weight on the uncharted gap when picking the crossing pair —
    /// a metre of uncharted apron costs 3× a metre of charted taxiway, so the
    /// narrowest usable waist wins unless it forces a huge charted detour.</summary>
    private const double UNCHARTED_GAP_WEIGHT = 3.0;

    /// <summary>Max candidate pairs to run the runway-corridor line test on
    /// (ascending score). Sized generously: at LMML the best-scored ~dozens of
    /// pairs all funnel through the runway-adjacent F-end waist and fail the
    /// corridor check before the first clear diagonal pair ranks in — if none of
    /// the best 120 is clear, the gap genuinely sits across a runway.</summary>
    private const int UNCHARTED_LINE_CHECK_CAP = 120;

    /// <summary>Two components whose nodes come within this of each other effectively
    /// MEET. If every such near-touching line is runway-blocked, the components
    /// connect via runway pavement (GA sceneries routinely join the two sides of the
    /// field only through the runway) and no synthetic crossing is fabricated — a
    /// "clear" line elsewhere would be an overland diagonal across what is usually
    /// grass. Whole-DB sweep 2026-08-31: 408 of 1,569 crossing-possible airports were
    /// this shape (00CA: islands 1 m apart at the runway, first clear line 175 m of
    /// open ground). Runway backtaxi is the honest way across at such airports.</summary>
    private const double NEAR_TOUCH_M = 50.0;
    private const int NEAR_TOUCH_CHECK_CAP = 20;

    internal sealed record ComponentCrossing(int ExitNodeId, int EntryNodeId, double GapMeters);

    /// <summary>
    /// Finds the best pair of nodes to bridge between the start node's component and
    /// the destination node's component: minimises graph-distance-from-start +
    /// weighted gap + graph-distance-to-destination, gap capped, straight line
    /// verified clear of runway corridors via <paramref name="lineIsClear"/>.
    /// </summary>
    internal ComponentCrossing? FindBestComponentCrossing(
        int startNodeId, int endNodeId, double maxGapMeters,
        Func<double, double, double, double, bool> lineIsClear)
    {
        if (!_graph.Nodes.ContainsKey(startNodeId) || !_graph.Nodes.ContainsKey(endNodeId))
            return null;
        int compA = _graph.Nodes[startNodeId].ComponentId;
        int compB = _graph.Nodes[endNodeId].ComponentId;
        if (compA == compB) return null;

        var distFromStart = ComputeGraphDistancesFrom(startNodeId);
        var distFromDest = ComputeGraphDistancesFrom(endNodeId);

        // Reachable nodes per side, with bounding boxes for the prefilter below
        // (huge airports can have thousands of nodes per component — only nodes
        // within the gap cap of the OTHER side's extent can ever pair).
        var aNodes = new List<TaxiNode>();
        var bNodes = new List<TaxiNode>();
        double aMinLat = double.MaxValue, aMaxLat = double.MinValue,
               aMinLon = double.MaxValue, aMaxLon = double.MinValue;
        double bMinLat = double.MaxValue, bMaxLat = double.MinValue,
               bMinLon = double.MaxValue, bMaxLon = double.MinValue;
        foreach (var n in _graph.Nodes.Values)
        {
            if (n.ComponentId == compA && distFromStart.ContainsKey(n.NodeId))
            {
                aNodes.Add(n);
                if (n.Latitude < aMinLat) aMinLat = n.Latitude;
                if (n.Latitude > aMaxLat) aMaxLat = n.Latitude;
                if (n.Longitude < aMinLon) aMinLon = n.Longitude;
                if (n.Longitude > aMaxLon) aMaxLon = n.Longitude;
            }
            else if (n.ComponentId == compB && distFromDest.ContainsKey(n.NodeId))
            {
                bNodes.Add(n);
                if (n.Latitude < bMinLat) bMinLat = n.Latitude;
                if (n.Latitude > bMaxLat) bMaxLat = n.Latitude;
                if (n.Longitude < bMinLon) bMinLon = n.Longitude;
                if (n.Longitude > bMaxLon) bMaxLon = n.Longitude;
            }
        }
        if (aNodes.Count == 0 || bNodes.Count == 0) return null;

        double midLatRad = (aMinLat + aMaxLat + bMinLat + bMaxLat) * 0.25 * Math.PI / 180.0;
        double latPad = maxGapMeters / 111132.0;
        double lonPad = maxGapMeters / Math.Max(1.0, 111132.0 * Math.Cos(midLatRad));

        var aNear = aNodes.Where(n =>
            n.Latitude >= bMinLat - latPad && n.Latitude <= bMaxLat + latPad &&
            n.Longitude >= bMinLon - lonPad && n.Longitude <= bMaxLon + lonPad).ToList();
        var bNear = bNodes.Where(n =>
            n.Latitude >= aMinLat - latPad && n.Latitude <= aMaxLat + latPad &&
            n.Longitude >= aMinLon - lonPad && n.Longitude <= aMaxLon + lonPad).ToList();
        if (aNear.Count == 0 || bNear.Count == 0) return null;

        var candidates = new List<(double score, double gap, TaxiNode a, TaxiNode b)>();
        var nearTouch = new List<(double gap, TaxiNode a, TaxiNode b)>();
        foreach (var a in aNear)
        {
            foreach (var b in bNear)
            {
                double gap = TaxiGraph.FastDistanceMeters(
                    a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (gap > maxGapMeters) continue;
                if (gap < NEAR_TOUCH_M) nearTouch.Add((gap, a, b));
                double score = distFromStart[a.NodeId] + gap * UNCHARTED_GAP_WEIGHT
                             + distFromDest[b.NodeId];
                candidates.Add((score, gap, a, b));
            }
        }
        if (candidates.Count == 0) return null;

        // Near-touching-at-a-runway gate (see NEAR_TOUCH_M): components that come
        // within touching distance connect there in the real world. If every such
        // line is runway-blocked, refuse the whole crossing rather than fabricate
        // a longer diagonal around the runway; if any is clear, it is a genuine
        // tiny modelling gap and ranks fine on its own.
        if (nearTouch.Count > 0)
        {
            nearTouch.Sort((x, y) => x.gap.CompareTo(y.gap));
            bool anyNearTouchClear = false;
            int ntChecks = 0;
            foreach (var (_, a, b) in nearTouch)
            {
                if (++ntChecks > NEAR_TOUCH_CHECK_CAP) break;
                if (lineIsClear(a.Latitude, a.Longitude, b.Latitude, b.Longitude))
                {
                    anyNearTouchClear = true;
                    break;
                }
            }
            if (!anyNearTouchClear)
            {
                Log($"Uncharted crossing refused: components meet within {NEAR_TOUCH_M:F0} m " +
                    $"at a runway ({nearTouch.Count} near-touch pairs, none runway-clear) — " +
                    "they connect via runway pavement, not open apron");
                return null;
            }
        }

        candidates.Sort((x, y) => x.score.CompareTo(y.score));

        int checks = 0;
        foreach (var (_, gap, a, b) in candidates)
        {
            if (++checks > UNCHARTED_LINE_CHECK_CAP) break;
            if (lineIsClear(a.Latitude, a.Longitude, b.Latitude, b.Longitude))
            {
                Log($"Uncharted crossing: node {a.NodeId} -> {b.NodeId}, gap {gap:F0} m " +
                    $"(checked {checks} of {candidates.Count} candidate pairs)");
                return new ComponentCrossing(a.NodeId, b.NodeId, gap);
            }
        }
        Log($"Uncharted crossing: no runway-clear pair among best {checks} of {candidates.Count} candidates");
        return null;
    }

    /// <summary>
    /// Splits a cleared taxiway sequence between the two sides of a component
    /// crossing: names stay on the NEAR side until the first name that exists ONLY
    /// on the far component, from which point everything goes to the far side (an
    /// ATC clearance is ordered, so once it names far-side pavement it never comes
    /// back). Names in neither component stay where the walk puts them and are
    /// reported by the per-leg constrained router exactly as today.
    /// </summary>
    internal static (List<string> nearSide, List<string> farSide) PartitionSequenceByComponent(
        TaxiGraph graph, List<string> sequence, int nearComponentId, int farComponentId)
    {
        var near = new List<string>();
        var far = new List<string>();
        bool switched = false;
        foreach (string name in sequence)
        {
            bool inNear = false, inFar = false;
            foreach (int id in graph.GetNodesOnTaxiway(name))
            {
                int c = graph.Nodes[id].ComponentId;
                if (c == nearComponentId) inNear = true;
                else if (c == farComponentId) inFar = true;
                if (inNear && inFar) break;
            }
            if (!switched && inFar && !inNear) switched = true;
            if (switched) far.Add(name); else near.Add(name);
        }
        return (near, far);
    }

    /// <summary>
    /// Builds a route from <paramref name="startNodeId"/> to a destination on a
    /// DIFFERENT connected component: charted leg on the start side, one synthetic
    /// <see cref="TaxiRouteSegment.IsUncharted"/> segment across the gap, charted
    /// leg on the destination side. Null when no gated crossing exists or either
    /// charted leg cannot build. A per-leg constrained fallback is carried up in
    /// <see cref="TaxiRoute.ConstrainedFallbackReason"/> so the existing warning speaks.
    /// </summary>
    public TaxiRoute? FindCrossComponentPath(
        int startNodeId, int endNodeId, List<string>? taxiwaySequence,
        bool destinationIsRunway, double maxGapMeters,
        Func<double, double, double, double, bool> lineIsClear,
        out double crossingGapMeters)
    {
        crossingGapMeters = 0;
        var crossing = FindBestComponentCrossing(startNodeId, endNodeId, maxGapMeters, lineIsClear);
        if (crossing == null) return null;

        List<string>? leg1Seq = null, leg2Seq = null;
        if (taxiwaySequence is { Count: > 0 })
        {
            (leg1Seq, leg2Seq) = PartitionSequenceByComponent(
                _graph, taxiwaySequence,
                _graph.Nodes[startNodeId].ComponentId, _graph.Nodes[endNodeId].ComponentId);
        }

        TaxiRoute? leg1 = null;
        if (crossing.ExitNodeId != startNodeId)
        {
            leg1 = leg1Seq is { Count: > 0 }
                ? FindConstrainedPath(startNodeId, crossing.ExitNodeId, leg1Seq,
                    destinationIsRunway: false)
                : FindShortestPath(startNodeId, crossing.ExitNodeId);
            if (leg1 == null || leg1.Segments.Count == 0)
            {
                Log("Uncharted crossing: near-side leg failed to build");
                return null;
            }
        }

        TaxiRoute? leg2 = null;
        if (crossing.EntryNodeId != endNodeId)
        {
            leg2 = leg2Seq is { Count: > 0 }
                ? FindConstrainedPath(crossing.EntryNodeId, endNodeId, leg2Seq,
                    destinationIsRunway: destinationIsRunway)
                : FindShortestPath(crossing.EntryNodeId, endNodeId);
            if (leg2 == null || leg2.Segments.Count == 0)
            {
                Log("Uncharted crossing: far-side leg failed to build");
                return null;
            }
        }

        var route = StitchWithCrossing(leg1, crossing, leg2);
        route.ConstrainedFallbackReason =
            leg1?.ConstrainedFallbackReason ?? leg2?.ConstrainedFallbackReason;
        crossingGapMeters = crossing.GapMeters;
        Log($"Uncharted crossing route: {route.Segments.Count} segments, " +
            $"{route.TotalDistanceMeters:F0} m total incl. {crossing.GapMeters:F0} m uncharted");
        return route;
    }

    /// <summary>Concatenates near leg + synthetic crossing segment + far leg, recomputing
    /// turn info at both joins and the cumulative/remaining distances across the whole.</summary>
    private TaxiRoute StitchWithCrossing(TaxiRoute? leg1, ComponentCrossing crossing, TaxiRoute? leg2)
    {
        var route = new TaxiRoute();
        var a = _graph.Nodes[crossing.ExitNodeId];
        var b = _graph.Nodes[crossing.EntryNodeId];
        double crossingBearing = NavigationCalculator.CalculateBearing(
            a.Latitude, a.Longitude, b.Latitude, b.Longitude);

        if (leg1 != null)
            route.Segments.AddRange(leg1.Segments);

        double turnAngle = 0;
        string turnDir = "straight";
        if (route.Segments.Count > 0)
        {
            turnAngle = NormalizeAngle(crossingBearing - route.Segments[^1].BearingDegrees);
            turnDir = GetTurnDirection(turnAngle);
        }
        route.Segments.Add(new TaxiRouteSegment
        {
            FromNode = a,
            ToNode = b,
            DistanceMeters = crossing.GapMeters,
            // Deliberately UNNAMED: the generic turn callouts then say "turn left in
            // 90 metres" with no bogus taxiway name, and the dedicated entering-
            // uncharted announcement carries the explanation.
            TaxiwayName = "",
            BearingDegrees = crossingBearing,
            TurnAngleDegrees = turnAngle,
            TurnDirection = turnDir,
            PathWidth = 0,
            IsUncharted = true,
        });

        if (leg2 != null && leg2.Segments.Count > 0)
        {
            // BuildRoute started leg2 fresh, so its first segment carries no turn
            // relative to the crossing bearing — recompute it.
            var first = leg2.Segments[0];
            first.TurnAngleDegrees = NormalizeAngle(first.BearingDegrees - crossingBearing);
            first.TurnDirection = GetTurnDirection(first.TurnAngleDegrees);
            route.Segments.AddRange(leg2.Segments);
        }

        SetRouteDistances(route);
        return route;
    }

    /// <summary>
    /// Builds a route through <paramref name="nodeIds"/> in order, like <see cref="BuildRoute"/>, except
    /// that every (From, To) pair in <paramref name="offChartPairs"/> that has no graph edge becomes a
    /// synthetic straight leg (<see cref="TaxiRouteSegment.IsOffChartTurn"/>, matched in that direction
    /// only). Any other pair with no edge returns null. Turns and distances run over the whole list.
    /// </summary>
    internal TaxiRoute? BuildRouteThrough(IReadOnlyList<int> nodeIds,
        IReadOnlyCollection<(int From, int To)>? offChartPairs = null)
    {
        var route = new TaxiRoute();

        for (int i = 0; i < nodeIds.Count - 1; i++)
        {
            int fromId = nodeIds[i];
            int toId = nodeIds[i + 1];
            if (!_graph.Nodes.TryGetValue(fromId, out var fromNode) || !_graph.Nodes.TryGetValue(toId, out var toNode))
                return null;

            TaxiRouteSegment segment;
            var edge = _graph.GetEdge(fromId, toId);
            if (edge != null)
            {
                segment = EdgeSegment(fromNode, toNode, edge);
            }
            else if (offChartPairs != null && offChartPairs.Contains((fromId, toId)))
            {
                segment = new TaxiRouteSegment
                {
                    FromNode = fromNode,
                    ToNode = toNode,
                    DistanceMeters = TaxiGraph.CalculateDistanceMeters(
                        fromNode.Latitude, fromNode.Longitude, toNode.Latitude, toNode.Longitude),
                    TaxiwayName = "",
                    BearingDegrees = NavigationCalculator.CalculateBearing(
                        fromNode.Latitude, fromNode.Longitude, toNode.Latitude, toNode.Longitude),
                    PathWidth = 0,
                    HoldShortRunway = toNode.HoldShortName,
                    IsOffChartTurn = true,
                };
            }
            else
            {
                return null;
            }

            AppendWithTurn(route, segment);
        }

        SetRouteDistances(route);
        return route;
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    internal static string GetTurnDirection(double angle)
    {
        double absAngle = Math.Abs(angle);
        if (absAngle < 20) return "straight";
        if (absAngle < 60) return angle < 0 ? "slight left" : "slight right";
        // For BOTH normal turns (60–120°) and sharp turns (≥120°), return base
        // direction without a "sharp" prefix. Callers that announce a sharp
        // turn (CheckUpcomingAnnouncements) detect it via TurnAngleDegrees ≥
        // SHARP_TURN_ANGLE_DEG and prepend "sharp " themselves. Returning
        // "sharp left" / "sharp right" here was producing "sharp sharp right"
        // double-prefix in the spoken callouts (Bug observed on OMDB
        // K15A→bridge transitions).
        return angle < 0 ? "left" : "right";
    }
}
