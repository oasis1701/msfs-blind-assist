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
    /// Finds the shortest path between two nodes with no taxiway constraints.
    /// </summary>
    public TaxiRoute? FindShortestPath(int startNodeId, int endNodeId)
    {
        var path = AStarSearch(startNodeId, endNodeId, null);
        if (path == null) return null;

        return BuildRoute(path);
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

        // Step 1: Get onto the first taxiway - try multiple entry points
        var candidateEntries = FindNearestNodesOnTaxiway(startNodeId, taxiwaySequence[0], 10);
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

        // Determine the target for the first taxiway
        string? secondTaxiway = taxiwaySequence.Count > 1 ? taxiwaySequence[1] : null;
        int firstTarget;
        bool firstBridgedAcrossRunway = false;
        int firstBridgeEntryOnSecond = -1;
        if (secondTaxiway != null)
        {
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
            if (destinationIsRunway && firstTarget == startNodeId)
            {
                int far = FindNodeOnTaxiwayFarthestFromNode(
                    taxiwaySequence[0], startNodeId, _graph.Nodes[startNodeId].ComponentId);
                if (far != -1 && far != startNodeId)
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
            Log($"Step 1 OK: '{taxiwaySequence[0]}' via node {entryNode} -> {firstTarget}");
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
            int sb = (fullPath.Count > 0 && bridge.Count > 0 && fullPath[^1] == bridge[0]) ? 1 : 0;
            for (int j = sb; j < bridge.Count; j++)
                fullPath.Add(bridge[j]);
            currentNode = firstBridgeEntryOnSecond;
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
            if (nextTaxiway != null)
            {
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
                if (destinationIsRunway && targetNode == currentNode && taxiwaySequence.Count > 1)
                {
                    int comp = _graph.Nodes[currentNode].ComponentId;
                    var destNode = _graph.Nodes[endNodeId];
                    // First try the node nearest the destination POSITION — correct when the last
                    // taxiway ends AT the runway (EIDW "…N, N2" to 28R → N2's hold-short end).
                    int holdEnd = FindNodeOnTaxiwayNearestPosition(
                        currentTaxiway, destNode.Latitude, destNode.Longitude, comp);
                    // If that is STILL the entry, the destination node sits back near the entry rather
                    // than at the taxiway's far end (LFPG "…R, R1" to 26R: the 26R node is closer to the
                    // R/R1 junction than to R1's far end). Fall back to the node FARTHEST along the
                    // cleared taxiway from the entry so it is still traversed — otherwise the step
                    // no-ops and the unconstrained final leg deviates off the clearance.
                    if (holdEnd == currentNode || holdEnd == -1)
                        holdEnd = FindNodeOnTaxiwayFarthestFromNode(currentTaxiway, currentNode, comp);
                    if (holdEnd != -1 && holdEnd != currentNode)
                    {
                        targetNode = holdEnd;
                        lastTaxiwayTerminal = true;
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

                int si = (fullPath.Count > 0 && segment.Count > 0 && fullPath[^1] == segment[0]) ? 1 : 0;
                for (int j = si; j < segment.Count; j++)
                    fullPath.Add(segment[j]);

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
                int sb = (fullPath.Count > 0 && bridge.Count > 0 && fullPath[^1] == bridge[0]) ? 1 : 0;
                for (int j = sb; j < bridge.Count; j++)
                    fullPath.Add(bridge[j]);
                currentNode = bridgeEntryOnNext;
                Log($"Step {i + 1} bridge OK: {bridge.Count} nodes across runway");
            }
        }

        // Step 3: Route from last taxiway to destination — UNLESS the last cleared taxiway was
        // honored as the terminus (it holds short of the runway; appending an unconstrained leg
        // here would bypass it onto the runway — see the last-taxiway branch above).
        if (currentNode != endNodeId && !lastTaxiwayTerminal)
        {
            var finalLeg = AStarSearch(currentNode, endNodeId, null);
            if (finalLeg == null)
            {
                string reason = $"No path from last taxiway node {currentNode} to destination {endNodeId}";
                Log($"FALLBACK: {reason}");
                return FallbackShortest(startNodeId, endNodeId, reason);
            }

            int si = (fullPath.Count > 0 && finalLeg.Count > 0 && fullPath[^1] == finalLeg[0]) ? 1 : 0;
            for (int j = si; j < finalLeg.Count; j++)
                fullPath.Add(finalLeg[j]);
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
    /// Finds the N nearest nodes on a given taxiway to a starting node.
    /// </summary>
    private List<int> FindNearestNodesOnTaxiway(int fromNodeId, string taxiwayName, int maxResults)
    {
        var fromNode = _graph.Nodes[fromNodeId];
        int fromComponent = fromNode.ComponentId;
        var candidates = new List<(int nodeId, double dist)>();

        foreach (int nodeId in _graph.GetNodesOnTaxiway(taxiwayName))
        {
            var node = _graph.Nodes[nodeId];
            if (node.ComponentId != fromComponent) continue;

            double dist = TaxiGraph.CalculateDistanceMeters(
                fromNode.Latitude, fromNode.Longitude, node.Latitude, node.Longitude);

            candidates.Add((nodeId, dist));
        }

        return candidates
            .OrderBy(c => c.dist)
            .Take(maxResults)
            .Select(c => c.nodeId)
            .ToList();
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
    private int FindExtremeNodeOnTaxiway(string taxiwayName, double refLat, double refLon,
        int requiredComponent, bool farthest)
    {
        int best = -1;
        double bestM = farthest ? -1 : double.MaxValue;
        foreach (int nodeId in _graph.GetNodesOnTaxiway(taxiwayName))
        {
            var node = _graph.Nodes[nodeId];
            if (node.ComponentId != requiredComponent) continue;
            double d = TaxiGraph.CalculateDistanceMeters(node.Latitude, node.Longitude, refLat, refLon);
            if (farthest ? d > bestM : d < bestM) { bestM = d; best = nodeId; }
        }
        return best;
    }

    /// <summary>Node on the taxiway geographically NEAREST the given position (-1 if none).</summary>
    private int FindNodeOnTaxiwayNearestPosition(string taxiwayName, double lat, double lon, int requiredComponent)
        => FindExtremeNodeOnTaxiway(taxiwayName, lat, lon, requiredComponent, farthest: false);

    /// <summary>Node on the taxiway geographically FARTHEST from the given node (-1 if none / node unknown).</summary>
    private int FindNodeOnTaxiwayFarthestFromNode(string taxiwayName, int fromNodeId, int requiredComponent)
        => _graph.Nodes.TryGetValue(fromNodeId, out var from)
            ? FindExtremeNodeOnTaxiway(taxiwayName, from.Latitude, from.Longitude, requiredComponent, farthest: true)
            : -1;

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
    /// ATC clearance is genuinely wrong. 200 m gives slack for unusual layouts
    /// (extra-wide RWY + shoulder + pavement margins) without enabling
    /// silent half-airport jumps.
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
            if (!distFromEntry.TryGetValue(nA.NodeId, out double entryDist)) continue;

            foreach (var nB in _graph.Nodes.Values)
            {
                if (!nB.TaxiwayNames.Contains(nextTaxiway)) continue;
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
    /// Finds the best intersection node between two taxiways.
    /// Picks the candidate that minimizes total GRAPH distance:
    /// dist_from_entry[candidate] + dist_from_destination[candidate].
    /// Uses Dijkstra-based costs (precomputed in FindConstrainedPath for the
    /// destination side; computed per call for the entry side) rather than
    /// Euclidean — see FindNearestNodeOnTaxiwayToTarget for why (dead-end
    /// intersection nodes that are closer in straight-line distance to
    /// either endpoint can require a long graph backtrack to escape).
    /// </summary>
    private int FindBestIntersection(int currentNodeId, int finalDestId, Dictionary<int, double> distFromFinalDest, string taxiway1, string taxiway2)
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
        var onTaxiway1 = new HashSet<int>(_graph.GetNodesOnTaxiway(taxiway1));
        var onTaxiway2 = new HashSet<int>(_graph.GetNodesOnTaxiway(taxiway2));

        var intersections = new List<int>();
        foreach (var node in _graph.Nodes.Values)
        {
            bool hasTaxiway1 = onTaxiway1.Contains(node.NodeId) || node.TaxiwayNames.Contains(taxiway1);
            bool hasTaxiway2 = onTaxiway2.Contains(node.NodeId) || node.TaxiwayNames.Contains(taxiway2);

            if (hasTaxiway1 && hasTaxiway2)
                intersections.Add(node.NodeId);
        }

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

        foreach (int nodeId in intersections)
        {
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
    private List<int>? AStarSearchWithGapChaining(int startId, int goalId, string requiredTaxiway)
    {
        // Try strict first — works when the taxiway is one connected component
        var direct = AStarSearchStrict(startId, goalId, requiredTaxiway);
        if (direct != null)
            return direct;

        Log($"GapChain '{requiredTaxiway}': strict failed, attempting gap chaining {startId} → {goalId}");

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
    private Dictionary<int, double> ComputeGraphDistancesFrom(int sourceId)
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
    private List<int>? AStarSearch(int startId, int goalId, string? preferredTaxiway)
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
        double cumulative = 0;

        for (int i = 0; i < nodeIds.Count - 1; i++)
        {
            int fromId = nodeIds[i];
            int toId = nodeIds[i + 1];
            var fromNode = _graph.Nodes[fromId];
            var toNode = _graph.Nodes[toId];

            var edge = _graph.GetEdge(fromId, toId);
            if (edge == null) continue;

            double turnAngle = 0;
            string turnDir = "straight";
            if (i > 0 && route.Segments.Count > 0)
            {
                var prevSeg = route.Segments[^1];
                double prevBearing = prevSeg.BearingDegrees;
                double currentBearing = edge.BearingDegrees;
                turnAngle = NormalizeAngle(currentBearing - prevBearing);
                turnDir = GetTurnDirection(turnAngle);
            }

            cumulative += edge.DistanceMeters;

            var segment = new TaxiRouteSegment
            {
                FromNode = fromNode,
                ToNode = toNode,
                DistanceMeters = edge.DistanceMeters,
                CumulativeDistanceMeters = cumulative,
                TaxiwayName = edge.TaxiwayName,
                BearingDegrees = edge.BearingDegrees,
                TurnAngleDegrees = turnAngle,
                TurnDirection = turnDir,
                PathWidth = edge.WidthFeet,
                IsHoldShortPoint = false,  // Only set by user's hold-short checkboxes
                HoldShortRunway = toNode.HoldShortName
            };

            route.Segments.Add(segment);
        }

        // Set remaining distances
        for (int i = 0; i < route.Segments.Count; i++)
        {
            route.Segments[i].RemainingDistanceMeters = cumulative - route.Segments[i].CumulativeDistanceMeters + route.Segments[i].DistanceMeters;
        }

        route.TotalDistanceMeters = cumulative;
        return route;
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    private static string GetTurnDirection(double angle)
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
