using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Where a taxi route starts for an aircraft standing on a runway. Runway pavement is not a routable
/// corridor in the taxi graph, and the nearest graph node can be a parallel taxiway across the
/// infield. RJAA 34L, 2026-11-06: stopped between exits with the parallel 200 m to the side, the tone
/// aimed 482 m straight across the grass. A taxiway node on the runway's own pavement is a start the
/// aircraft can reach by rolling along that pavement.
/// </summary>
public static class RouteRunwayStart
{
    /// <summary>A node closer ahead than this is under the nose and cannot be steered to.</summary>
    public const double MinAheadMeters = 30.0;

    /// <summary>The share of the aircraft's motion that must lie along the runway, about 45 degrees.
    /// Below it the aircraft is crossing or turning off, and has no "ahead" along this runway.</summary>
    public const double MinAlignment = 0.7;

    /// <summary>How many exits route loading tries before it settles for the first. An exit on the other
    /// bank beside one already offered comes along without taking a place of its own.</summary>
    public const int MaxCandidates = 6;

    /// <summary>Nodes closer together than this along the runway belong to one exit's curve.</summary>
    public const double ExitSpacingMeters = 60.0;

    private const double ProbeMeters = 100.0;
    private const double MetersPerDegree = 111132.0;

    /// <summary>
    /// True for a plain taxi route. A runway destination has its own lineup logic, and landing-exit
    /// routes have their own anchor (<c>ExitPathStartAnchor</c>, <c>ExitOwnSide</c>).
    /// </summary>
    public static bool Applies(bool isRunwayDestination, string? startTaxiwayName, bool landingRolloutRoute)
        => !isRunwayDestination && string.IsNullOrEmpty(startTaxiwayName) && !landingRolloutRoute;

    /// <summary>The best candidate, or null when the aircraft is not rolling along a runway or that
    /// runway carries no usable node.</summary>
    public static TaxiNode? Pick(
        TaxiGraph graph, double lat, double lon, double headingTrue, string? firstCleared, int requiredComponentId)
        => Candidates(graph, lat, lon, headingTrue, firstCleared, requiredComponentId).FirstOrDefault();

    /// <summary>
    /// Start nodes on the runway under the aircraft, best first: the first cleared taxiway's nearest
    /// node ahead, then the nodes ahead by distance, then those behind. Nodes of one exit's curve
    /// collapse to its nearest, so the list offers different exits; exits on opposite banks at about
    /// the same distance are both offered. Empty when the aircraft is not rolling along a runway, or
    /// when the first cleared taxiway is right here, on this runway or at its edge.
    /// </summary>
    public static IReadOnlyList<TaxiNode> Candidates(
        TaxiGraph graph, double lat, double lon, double headingTrue, string? firstCleared, int requiredComponentId)
        => Candidates(graph, RunwayPavement.BuildShapes(graph.RunwayCenterlines), lat, lon, headingTrue,
            firstCleared, requiredComponentId);

    /// <summary>
    /// The first candidate whose route can be built and does not come back onto the runway, with that
    /// route, so route loading need not build it again. The exits ahead (the first
    /// <paramref name="aheadCount"/>) are tried before those behind, which would turn the aircraft round
    /// on the runway. Among the exits one way, a route that follows the clearance wins over one where the
    /// router fell back to a shortest path (<see cref="TaxiRoute.ConstrainedFallbackReason"/>), so a
    /// later exit that honours the clearance beats an earlier one that ignores it; with none following
    /// it, the first fallback. With nothing usable either way, null and the older start stands.
    /// </summary>
    public static (TaxiNode Node, TaxiRoute Route)? Choose(
        IReadOnlyList<TaxiNode> candidates, Func<TaxiNode, TaxiRoute?> build, Func<TaxiRoute, bool> returnsToRunway,
        int aheadCount = int.MaxValue)
    {
        int split = Math.Min(Math.Max(0, aheadCount), candidates.Count);
        return Best(candidates.Take(split)) ?? Best(candidates.Skip(split));

        (TaxiNode Node, TaxiRoute Route)? Best(IEnumerable<TaxiNode> group)
        {
            (TaxiNode Node, TaxiRoute Route)? fallback = null;
            foreach (var candidate in group)
            {
                var route = build(candidate);
                if (route is not { Segments.Count: > 0 } || returnsToRunway(route)) continue;
                if (route.ConstrainedFallbackReason == null) return (candidate, route);
                fallback ??= (candidate, route);
            }
            return fallback;
        }
    }

    /// <inheritdoc cref="Candidates(TaxiGraph, double, double, double, string?, int)"/>
    /// <param name="shapes">The graph's runway shapes, built once by the caller (<see cref="RunwayPavement.BuildShapes"/>).</param>
    public static IReadOnlyList<TaxiNode> Candidates(
        TaxiGraph graph, IReadOnlyList<RunwayShape> shapes, double lat, double lon, double headingTrue,
        string? firstCleared, int requiredComponentId)
        => Candidates(graph, shapes, lat, lon, headingTrue, firstCleared, requiredComponentId, out _);

    /// <inheritdoc cref="Candidates(TaxiGraph, IReadOnlyList{RunwayShape}, double, double, double, string?, int)"/>
    /// <param name="aheadCount">How many of the candidates lie ahead; the rest are behind.</param>
    public static IReadOnlyList<TaxiNode> Candidates(
        TaxiGraph graph, IReadOnlyList<RunwayShape> shapes, double lat, double lon, double headingTrue,
        string? firstCleared, int requiredComponentId, out int aheadCount)
    {
        aheadCount = 0;
        if (!TryFindRunway(shapes, lat, lon, headingTrue, out var runway, out double aircraftAlong, out double direction))
            return Array.Empty<TaxiNode>();

        // The roll must not cross a runway the aircraft is not already on: no stop can be placed on it.
        var otherRunways = shapes.Where(s => !s.Contains(lat, lon, 0.0)).ToList();

        TaxiNode? cleared = null;
        double clearedAt = double.MaxValue;
        var ahead = new List<(TaxiNode Node, double Metres)>();
        var behind = new List<(TaxiNode Node, double Metres)>();
        var onPavement = new HashSet<int>();

        foreach (var node in graph.Nodes.Values)
        {
            if (node.ComponentId != requiredComponentId || graph.IsBridgeOnlyStandStub(node.NodeId)) continue;

            var (along, lateral) = runway.Project(node.Latitude, node.Longitude);

            // The cleared taxiway is right here, on this runway or at its edge: the usual pre-snap
            // starts on it [RTE-23], and rolling to the next exit would only bring the aircraft back
            // along the parallel. A cleared parallel across the grass strip is not: the pre-snap would
            // aim straight over the grass at it.
            if (firstCleared != null && node.TaxiwayNames.Contains(firstCleared)
                && Math.Abs(lateral) <= runway.HalfWidthMeters + ClearedEdgeMarginMeters
                && TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude) <= TaxiLeadIn.TriggerMeters)
                return Array.Empty<TaxiNode>();

            if (!runway.ContainsAlongLateral(along, lateral, 0.0)) continue;
            if (RunwayPavement.SegmentTouchesPavement(lat, lon, node.Latitude, node.Longitude, otherRunways, out _)) continue;

            onPavement.Add(node.NodeId);
            double forward = (along - aircraftAlong) * direction;
            if (forward < MinAheadMeters)
            {
                behind.Add((node, Math.Abs(forward)));
                continue;
            }
            ahead.Add((node, forward));
            if (firstCleared != null && forward < clearedAt && node.TaxiwayNames.Contains(firstCleared))
            {
                clearedAt = forward;
                cleared = node;
            }
        }

        var banks = BanksOf(graph, runway, onPavement);
        var keptAhead = new List<(double Metres, int Banks)>();
        var entries = new List<(TaxiNode Node, bool Twin, bool Ahead)>();
        if (cleared != null)
        {
            entries.Add((cleared, false, true));
            keptAhead.Add((clearedAt, banks[cleared.NodeId]));
        }
        entries.AddRange(OneNodePerExit(ahead, banks, keptAhead).Select(e => (e.Node, e.Twin, true)));
        entries.AddRange(OneNodePerExit(behind, banks, new List<(double Metres, int Banks)>()).Select(e => (e.Node, e.Twin, false)));

        // MaxCandidates exits, counted as one bank's exits are; an exit on the other bank beside one
        // already offered comes along without taking a place, so splitting the banks never cuts the
        // reach along the runway short.
        var ordered = new List<TaxiNode>();
        int exits = 0;
        foreach (var (node, twin, isAhead) in entries)
        {
            if (!twin && exits++ == MaxCandidates) break;
            ordered.Add(node);
            if (isAhead) aheadCount++;
        }
        return ordered;
    }

    /// <summary>A first cleared taxiway's node this far past the runway edge is still at its edge:
    /// an exit drawn from the edge rather than the centreline. A parallel is further out.</summary>
    public const double ClearedEdgeMarginMeters = 15.0;

    private const int LeftBank = 1, RightBank = 2;

    // Which bank or banks each node on the pavement leads to: the sides of the runway its taxiways
    // leave by, shared across the nodes linked to it on the pavement (one exit's curve). 0 when it
    // leads off neither side (no off-pavement neighbour, or only beyond the runway's end).
    private static Dictionary<int, int> BanksOf(TaxiGraph graph, RunwayShape runway, HashSet<int> onPavement)
    {
        var own = new Dictionary<int, int>();
        foreach (int id in onPavement)
        {
            int bits = 0;
            if (graph.Adjacency.TryGetValue(id, out var edges))
                foreach (var edge in edges)
                {
                    if (onPavement.Contains(edge.ToNodeId) || !graph.Nodes.TryGetValue(edge.ToNodeId, out var next)) continue;
                    double lateral = runway.Project(next.Latitude, next.Longitude).Lateral;
                    if (Math.Abs(lateral) <= runway.HalfWidthMeters) continue;
                    bits |= lateral > 0 ? LeftBank : RightBank;
                }
            own[id] = bits;
        }

        var banks = new Dictionary<int, int>();
        foreach (int start in onPavement)
        {
            if (banks.ContainsKey(start)) continue;
            var group = new List<int> { start };
            var seen = new HashSet<int> { start };
            for (int i = 0; i < group.Count; i++)
                if (graph.Adjacency.TryGetValue(group[i], out var edges))
                    foreach (var edge in edges)
                        if (onPavement.Contains(edge.ToNodeId) && seen.Add(edge.ToNodeId)) group.Add(edge.ToNodeId);
            int bits = group.Aggregate(0, (acc, id) => acc | own[id]);
            foreach (int id in group) banks[id] = bits;
        }
        return banks;
    }

    /// <summary>
    /// True while the tone should aim at the route's first node rather than walk the route. The walk
    /// reads along the first segment, and when that leaves at an angle it lands up the exit, so a long
    /// roll aims beside the runway; a line between two points on the pavement stays on it.
    /// </summary>
    public static bool AimsAtStartNode(
        bool startedOnRunway, bool joinedRoute, int segmentIndex, double distanceToStartMeters, double lookAheadMeters)
        => startedOnRunway && !joinedRoute && segmentIndex == 0 && distanceToStartMeters > lookAheadMeters;

    /// <summary>
    /// True when the route leaves the runway under the aircraft and then comes back onto it. Starting
    /// on the far bank's side of an exit makes a route do that (KFLL 10L Q), and the hold pass then
    /// rightly stops the pilot short of the runway they have only just left.
    /// </summary>
    public static bool RouteReturnsToRunway(
        TaxiGraph graph, IReadOnlyList<TaxiRouteSegment> segments, double lat, double lon, double headingTrue)
        => RouteReturnsToRunway(RunwayPavement.BuildShapes(graph.RunwayCenterlines), segments, lat, lon, headingTrue);

    /// <inheritdoc cref="RouteReturnsToRunway(TaxiGraph, IReadOnlyList{TaxiRouteSegment}, double, double, double)"/>
    /// <param name="shapes">The graph's runway shapes, built once by the caller (<see cref="RunwayPavement.BuildShapes"/>).</param>
    public static bool RouteReturnsToRunway(
        IReadOnlyList<RunwayShape> shapes, IReadOnlyList<TaxiRouteSegment> segments, double lat, double lon, double headingTrue)
    {
        if (!TryFindRunway(shapes, lat, lon, headingTrue, out var runway, out _, out _)) return false;

        var nodes = RunwayRouteClassifier.NodesFrom(segments, 0, new RouteRunwayCrossings.AircraftPosition(lat, lon));
        return RunwayRouteClassifier.Classify(nodes, runway).Count > 0;
    }

    // Nearest first, skipping a node within ExitSpacingMeters of one already kept that leads to the
    // same bank: that is the same exit's curve. Exits on opposite banks at about the same distance are
    // two exits, and the far bank's route would come back over the runway: the second is a twin of the
    // first. A node leading to neither bank, or to both (a crossing), is the same exit as anything beside it.
    private static IEnumerable<(TaxiNode Node, bool Twin)> OneNodePerExit(
        IEnumerable<(TaxiNode Node, double Metres)> nodes, IReadOnlyDictionary<int, int> banks,
        List<(double Metres, int Banks)> kept)
    {
        foreach (var (node, metres) in nodes.OrderBy(n => n.Metres))
        {
            int bits = banks[node.NodeId];
            var beside = kept.Where(k => Math.Abs(metres - k.Metres) < ExitSpacingMeters).ToList();
            if (beside.Any(k => k.Banks == 0 || bits == 0 || (k.Banks & bits) != 0)) continue;
            kept.Add((metres, bits));
            yield return (node, beside.Count > 0);
        }
    }

    // Of the runways under the aircraft, the one best aligned with its heading. A closed runway is not
    // one: taxiways are drawn along closed runways, and the aircraft there is taxiing, not rolling to an
    // exit (the fs2020 navdata keeps start rows for 1,226 closed runway ends). It still blocks the roll
    // across it above, like any runway.
    private static bool TryFindRunway(
        IReadOnlyList<RunwayShape> shapes, double lat, double lon, double headingTrue,
        out RunwayShape runway, out double aircraftAlong, out double direction)
    {
        double radians = headingTrue * Math.PI / 180.0;
        double probeLat = lat + ProbeMeters * Math.Cos(radians) / MetersPerDegree;
        double probeLon = lon + ProbeMeters * Math.Sin(radians) / (MetersPerDegree * Math.Cos(lat * Math.PI / 180.0));

        RunwayShape? best = null;
        double bestAlignment = MinAlignment, bestAlong = 0.0, bestDirection = 0.0;

        foreach (var shape in shapes)
        {
            if (shape.Centerline.IsClosed || !shape.Contains(lat, lon, 0.0)) continue;

            double along = shape.Project(lat, lon).Along;
            double travelled = shape.Project(probeLat, probeLon).Along - along;
            double alignment = Math.Abs(travelled) / ProbeMeters;
            if (alignment < bestAlignment) continue;

            best = shape;
            bestAlignment = alignment;
            bestAlong = along;
            bestDirection = Math.Sign(travelled);
        }

        runway = best!;
        aircraftAlong = bestAlong;
        direction = bestDirection;
        return best != null;
    }
}
