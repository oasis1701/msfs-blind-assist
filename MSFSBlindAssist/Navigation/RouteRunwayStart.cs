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

    /// <summary>How many exits route loading tries before it settles for the first.</summary>
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
    /// collapse to its nearest, so the list offers different exits. Empty when the aircraft is not
    /// rolling along a runway.
    /// </summary>
    public static IReadOnlyList<TaxiNode> Candidates(
        TaxiGraph graph, double lat, double lon, double headingTrue, string? firstCleared, int requiredComponentId)
    {
        var shapes = RunwayPavement.BuildShapes(graph.RunwayCenterlines);
        if (!TryFindRunway(shapes, lat, lon, headingTrue, out var runway, out double aircraftAlong, out double direction))
            return Array.Empty<TaxiNode>();

        // The roll must not cross a runway the aircraft is not already on: no stop can be placed on it.
        var otherRunways = shapes.Where(s => !s.Contains(lat, lon, 0.0)).ToList();

        TaxiNode? cleared = null;
        double clearedAt = double.MaxValue;
        var ahead = new List<(TaxiNode Node, double Metres)>();
        var behind = new List<(TaxiNode Node, double Metres)>();

        foreach (var node in graph.Nodes.Values)
        {
            if (node.ComponentId != requiredComponentId || graph.IsBridgeOnlyStandStub(node.NodeId)) continue;

            // The cleared taxiway is right here: the usual pre-snap starts on it [RTE-23], and rolling
            // to the next exit would only bring the aircraft back along the parallel.
            if (firstCleared != null && node.TaxiwayNames.Contains(firstCleared)
                && TaxiGraph.FastDistanceMeters(lat, lon, node.Latitude, node.Longitude) <= TaxiLeadIn.TriggerMeters)
                return Array.Empty<TaxiNode>();

            var (along, lateral) = runway.Project(node.Latitude, node.Longitude);
            if (!runway.ContainsAlongLateral(along, lateral, 0.0)) continue;
            if (RunwayPavement.SegmentTouchesPavement(lat, lon, node.Latitude, node.Longitude, otherRunways, out _)) continue;

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

        var ordered = new List<TaxiNode>(MaxCandidates);
        if (cleared != null) ordered.Add(cleared);
        ordered.AddRange(OneNodePerExit(ahead).Where(n => n != cleared));
        ordered.AddRange(OneNodePerExit(behind));
        return ordered.Take(MaxCandidates).ToList();
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
    {
        var shapes = RunwayPavement.BuildShapes(graph.RunwayCenterlines);
        if (!TryFindRunway(shapes, lat, lon, headingTrue, out var runway, out _, out _)) return false;

        var nodes = RunwayRouteClassifier.NodesFrom(segments, 0, new RouteRunwayCrossings.AircraftPosition(lat, lon));
        return RunwayRouteClassifier.Classify(nodes, runway).Count > 0;
    }

    private static IEnumerable<TaxiNode> OneNodePerExit(IEnumerable<(TaxiNode Node, double Metres)> nodes)
    {
        double last = double.NegativeInfinity;
        foreach (var (node, metres) in nodes.OrderBy(n => n.Metres))
        {
            if (metres - last < ExitSpacingMeters) continue;
            last = metres;
            yield return node;
        }
    }

    // Of the runways under the aircraft, the one best aligned with its heading.
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
            if (!shape.Contains(lat, lon, 0.0)) continue;

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
