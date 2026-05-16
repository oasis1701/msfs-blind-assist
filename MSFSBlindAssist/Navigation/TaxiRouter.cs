using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A* pathfinding on the taxi graph.
/// Supports both unconstrained (shortest path) and constrained (follow specific taxiway sequence) routing.
/// </summary>
public class TaxiRouter
{
    private readonly TaxiGraph _graph;
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MSFSBlindAssist", "taxi_router.log");

    private static void Log(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
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
    public TaxiRoute? FindConstrainedPath(int startNodeId, int endNodeId, List<string> taxiwaySequence)
    {
        // Clear log for fresh run
        try { File.WriteAllText(LogPath, $"=== Constrained Route {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"); } catch { }

        var startN = _graph.Nodes[startNodeId];
        var endN = _graph.Nodes[endNodeId];
        Log($"Start: node {startNodeId} ({startN.Latitude:F6}, {startN.Longitude:F6})");
        Log($"End: node {endNodeId} ({endN.Latitude:F6}, {endN.Longitude:F6})");
        Log($"Taxiway sequence: {string.Join(" → ", taxiwaySequence)}");
        Log($"Graph: {_graph.Nodes.Count} nodes, {_graph.Adjacency.Values.Sum(e => e.Count)} edges");

        if (taxiwaySequence == null || taxiwaySequence.Count == 0)
            return FindShortestPath(startNodeId, endNodeId);

        var fullPath = new List<int>();
        int currentNode = startNodeId;

        // Step 1: Get onto the first taxiway - try multiple entry points
        var candidateEntries = FindNearestNodesOnTaxiway(startNodeId, taxiwaySequence[0], 10);
        if (candidateEntries.Count == 0)
        {
            string reason = $"No nodes found on taxiway '{taxiwaySequence[0]}'";
            Log($"[TaxiRouter] FALLBACK: {reason}");
            return FallbackShortest(startNodeId, endNodeId, reason);
        }

        // Determine the target for the first taxiway
        string? secondTaxiway = taxiwaySequence.Count > 1 ? taxiwaySequence[1] : null;
        int firstTarget;
        bool firstBridgedAcrossRunway = false;
        int firstBridgeEntryOnSecond = -1;
        if (secondTaxiway != null)
        {
            firstTarget = FindBestIntersection(startNodeId, endNodeId, taxiwaySequence[0], secondTaxiway);
            if (firstTarget == -1)
            {
                // Same fallback as the inner loop: try a runway bridge before
                // bailing to whole-route shortest path. Covers ATC clearances
                // where the FIRST hop is across a runway (rare but possible —
                // e.g., entering from a connector that crosses a runway to
                // reach the named departure taxiway).
                var (exitNode, entryNode) = FindRunwayBridge(taxiwaySequence[0], secondTaxiway);
                if (exitNode != -1 && entryNode != -1)
                {
                    firstTarget = exitNode;
                    firstBridgedAcrossRunway = true;
                    firstBridgeEntryOnSecond = entryNode;
                    Log($"[TaxiRouter] Step 1: bridging '{taxiwaySequence[0]}' → '{secondTaxiway}' via runway crossing");
                }
                else
                {
                    string reason = $"No intersection between '{taxiwaySequence[0]}' and '{secondTaxiway}'";
                    Log($"[TaxiRouter] FALLBACK: {reason}");
                    return FallbackShortest(startNodeId, endNodeId, reason);
                }
            }
        }
        else
        {
            firstTarget = FindNearestNodeOnTaxiwayToTarget(endNodeId, taxiwaySequence[0]);
            if (firstTarget == -1) firstTarget = endNodeId;
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
            Log($"[TaxiRouter] Step 1 OK: '{taxiwaySequence[0]}' via node {entryNode} -> {firstTarget}");
            break;
        }

        if (!entryFound)
        {
            string reason = $"No viable entry to '{taxiwaySequence[0]}'. Tried {candidateEntries.Count}: {string.Join("; ", failedEntries)}";
            Log($"[TaxiRouter] FALLBACK: {reason}");
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
                Log($"[TaxiRouter] FALLBACK: {reason}");
                return FallbackShortest(startNodeId, endNodeId, reason);
            }
            int sb = (fullPath.Count > 0 && bridge.Count > 0 && fullPath[^1] == bridge[0]) ? 1 : 0;
            for (int j = sb; j < bridge.Count; j++)
                fullPath.Add(bridge[j]);
            currentNode = firstBridgeEntryOnSecond;
            Log($"[TaxiRouter] Step 1 bridge OK: {bridge.Count} nodes across runway");
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
                targetNode = FindBestIntersection(currentNode, endNodeId, currentTaxiway, nextTaxiway);
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
                    var (exitNode, entryNode) = FindRunwayBridge(currentTaxiway, nextTaxiway);
                    if (exitNode != -1 && entryNode != -1)
                    {
                        targetNode = exitNode;
                        bridgedAcrossRunway = true;
                        bridgeEntryOnNext = entryNode;
                        Log($"[TaxiRouter] Step {i + 1}: bridging '{currentTaxiway}' → '{nextTaxiway}' via runway crossing");
                    }
                    else
                    {
                        string reason = $"Step {i + 1}: No intersection between '{currentTaxiway}' and '{nextTaxiway}'";
                        Log($"[TaxiRouter] FALLBACK: {reason}");
                        return FallbackShortest(startNodeId, endNodeId, reason);
                    }
                }
            }
            else
            {
                targetNode = FindNearestNodeOnTaxiwayToTarget(endNodeId, currentTaxiway);
                if (targetNode == -1) targetNode = endNodeId;
            }

            if (currentNode == targetNode && !bridgedAcrossRunway) continue;

            if (currentNode != targetNode)
            {
                // Route along the taxiway, bridging disconnected sections if needed
                var segment = AStarSearchWithGapChaining(currentNode, targetNode, currentTaxiway);
                if (segment == null)
                {
                    string reason = $"Step {i + 1}: No path on '{currentTaxiway}' from {currentNode} to {targetNode}";
                    Log($"[TaxiRouter] FALLBACK: {reason}");
                    return FallbackShortest(startNodeId, endNodeId, reason);
                }

                Log($"[TaxiRouter] Step {i + 1} OK: '{currentTaxiway}' {currentNode}->{targetNode} ({segment.Count} nodes)");

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
                    Log($"[TaxiRouter] FALLBACK: {reason}");
                    return FallbackShortest(startNodeId, endNodeId, reason);
                }
                int sb = (fullPath.Count > 0 && bridge.Count > 0 && fullPath[^1] == bridge[0]) ? 1 : 0;
                for (int j = sb; j < bridge.Count; j++)
                    fullPath.Add(bridge[j]);
                currentNode = bridgeEntryOnNext;
                Log($"[TaxiRouter] Step {i + 1} bridge OK: {bridge.Count} nodes across runway");
            }
        }

        // Step 3: Route from last taxiway to destination
        if (currentNode != endNodeId)
        {
            var finalLeg = AStarSearch(currentNode, endNodeId, null);
            if (finalLeg == null)
            {
                string reason = $"No path from last taxiway node {currentNode} to destination {endNodeId}";
                Log($"[TaxiRouter] FALLBACK: {reason}");
                return FallbackShortest(startNodeId, endNodeId, reason);
            }

            int si = (fullPath.Count > 0 && finalLeg.Count > 0 && fullPath[^1] == finalLeg[0]) ? 1 : 0;
            for (int j = si; j < finalLeg.Count; j++)
                fullPath.Add(finalLeg[j]);
        }

        if (fullPath.Count < 2)
        {
            string reason = $"Constrained path too short ({fullPath.Count} nodes)";
            Log($"[TaxiRouter] FALLBACK: {reason}");
            return FallbackShortest(startNodeId, endNodeId, reason);
        }

        Log($"[TaxiRouter] Constrained path SUCCESS: {fullPath.Count} nodes");
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
    /// Finds the nearest node on a given taxiway to a starting node.
    /// </summary>
    private int FindNearestNodeOnTaxiway(int fromNodeId, string taxiwayName)
    {
        var fromNode = _graph.Nodes[fromNodeId];
        int fromComponent = fromNode.ComponentId;
        int bestNode = -1;
        double bestDist = double.MaxValue;

        foreach (var kvp in _graph.Adjacency)
        {
            int nodeId = kvp.Key;
            bool onTaxiway = kvp.Value.Any(e =>
                e.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase));

            if (!onTaxiway) continue;

            var node = _graph.Nodes[nodeId];
            if (node.ComponentId != fromComponent) continue;

            double dist = TaxiGraph.CalculateDistanceMeters(
                fromNode.Latitude, fromNode.Longitude, node.Latitude, node.Longitude);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestNode = nodeId;
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Finds the N nearest nodes on a given taxiway to a starting node.
    /// </summary>
    private List<int> FindNearestNodesOnTaxiway(int fromNodeId, string taxiwayName, int maxResults)
    {
        var fromNode = _graph.Nodes[fromNodeId];
        int fromComponent = fromNode.ComponentId;
        var candidates = new List<(int nodeId, double dist)>();

        foreach (var kvp in _graph.Adjacency)
        {
            int nodeId = kvp.Key;
            bool onTaxiway = kvp.Value.Any(e =>
                e.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase));

            if (!onTaxiway) continue;

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
    /// Finds the node on a named taxiway whose shortest graph path to the
    /// destination is minimal. Uses Dijkstra cost-from-destination rather
    /// than Euclidean distance to dodge the dead-end-endpoint trap (KDEN
    /// M4 northern endpoint sits closer to gate A 60 in a straight line,
    /// but is a graph dead-end: the path forward from it round-trips back
    /// down M4 plus the real route around the airport, ~1 km longer than
    /// picking the southern endpoint).
    ///
    /// Falls back to Euclidean distance only when the target is unreachable
    /// in the graph or no taxiway node sits in the destination's connected
    /// component — both shouldn't happen during normal operation but keep
    /// the helper defensive.
    /// </summary>
    private int FindNearestNodeOnTaxiwayToTarget(int targetNodeId, string taxiwayName)
    {
        var targetNode = _graph.Nodes[targetNodeId];
        int targetComponent = targetNode.ComponentId;
        int bestNode = -1;
        double bestDist = double.MaxValue;

        // Run Dijkstra once from the destination. Costs are valid for every
        // node in the same connected component; cross-component nodes are
        // unreachable and absent from the map.
        var distFromTarget = ComputeGraphDistancesFrom(targetNodeId);

        foreach (var kvp in _graph.Adjacency)
        {
            int nodeId = kvp.Key;
            bool onTaxiway = kvp.Value.Any(e =>
                e.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase));

            if (!onTaxiway) continue;

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
            foreach (var kvp in _graph.Adjacency)
            {
                int nodeId = kvp.Key;
                bool onTaxiway = kvp.Value.Any(e =>
                    e.TaxiwayName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase));
                if (!onTaxiway) continue;

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
    /// Finds the best intersection node between two taxiways.
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
    private (int exitOnCurrent, int entryOnNext) FindRunwayBridge(string currentTaxiway, string nextTaxiway)
    {
        const double MAX_BRIDGE_METERS = 200.0;

        int bestExit = -1, bestEntry = -1;
        double bestDist = double.MaxValue;

        // O(N×M) over the two taxiways' nodes. At a major hub each named
        // taxiway has tens of nodes, so this is well under 10K ops — cheap.
        foreach (var nA in _graph.Nodes.Values)
        {
            if (!nA.TaxiwayNames.Contains(currentTaxiway)) continue;
            foreach (var nB in _graph.Nodes.Values)
            {
                if (!nB.TaxiwayNames.Contains(nextTaxiway)) continue;
                double d = TaxiGraph.CalculateDistanceMeters(
                    nA.Latitude, nA.Longitude, nB.Latitude, nB.Longitude);
                if (d < bestDist && d <= MAX_BRIDGE_METERS)
                {
                    bestDist = d;
                    bestExit = nA.NodeId;
                    bestEntry = nB.NodeId;
                }
            }
        }

        if (bestExit != -1)
            Log($"[TaxiRouter] Runway bridge candidate: '{currentTaxiway}' node {bestExit} → '{nextTaxiway}' node {bestEntry}, gap {bestDist:F1} m");

        return (bestExit, bestEntry);
    }

    /// Picks the one that minimizes total path (current→intersection + intersection→dest).
    /// </summary>
    private int FindBestIntersection(int currentNodeId, int finalDestId, string taxiway1, string taxiway2)
    {
        var currentNode = _graph.Nodes[currentNodeId];
        var destNode = _graph.Nodes[finalDestId];

        var intersections = new List<int>();
        foreach (var kvp in _graph.Adjacency)
        {
            int nodeId = kvp.Key;
            bool hasTaxiway1 = false;
            bool hasTaxiway2 = false;

            foreach (var edge in kvp.Value)
            {
                if (edge.TaxiwayName.Equals(taxiway1, StringComparison.OrdinalIgnoreCase))
                    hasTaxiway1 = true;
                if (edge.TaxiwayName.Equals(taxiway2, StringComparison.OrdinalIgnoreCase))
                    hasTaxiway2 = true;
            }

            var node = _graph.Nodes[nodeId];
            if (node.TaxiwayNames.Contains(taxiway1)) hasTaxiway1 = true;
            if (node.TaxiwayNames.Contains(taxiway2)) hasTaxiway2 = true;

            if (hasTaxiway1 && hasTaxiway2)
                intersections.Add(nodeId);
        }

        if (intersections.Count == 0)
            return -1;

        int bestNode = -1;
        double bestScore = double.MaxValue;

        foreach (int nodeId in intersections)
        {
            var node = _graph.Nodes[nodeId];
            double distFromCurrent = TaxiGraph.CalculateDistanceMeters(
                currentNode.Latitude, currentNode.Longitude, node.Latitude, node.Longitude);
            double distToDest = TaxiGraph.CalculateDistanceMeters(
                node.Latitude, node.Longitude, destNode.Latitude, destNode.Longitude);
            double score = distFromCurrent + distToDest;

            if (score < bestScore)
            {
                bestScore = score;
                bestNode = nodeId;
            }
        }

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
        var nodesOnTaxiway = new HashSet<int>();
        foreach (var kvp in _graph.Adjacency)
        {
            if (kvp.Value.Any(e => e.TaxiwayName.Equals(requiredTaxiway, StringComparison.OrdinalIgnoreCase)))
                nodesOnTaxiway.Add(kvp.Key);
        }

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
                Log($"Strict '{requiredTaxiway}': FOUND path in {iterations} iterations, {closedSet.Count} nodes explored, {bridgeCount} bridges used");
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

            foreach (var kvp in _graph.Adjacency)
            {
                int nodeId = kvp.Key;
                if (reachable.Contains(nodeId)) continue;
                if (!kvp.Value.Any(e => e.TaxiwayName.Equals(requiredTaxiway, StringComparison.OrdinalIgnoreCase)))
                    continue;

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
