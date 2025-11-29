using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Builds taxi routes from aircraft position through specified taxiways to a destination
/// </summary>
public class TaxiRouteBuilder
{
    private readonly TaxiwayGraph _graph;
    private const double NM_TO_FEET = 6076.12;

    public TaxiRouteBuilder(TaxiwayGraph graph)
    {
        _graph = graph;
    }

    /// <summary>
    /// Builds a route from the current aircraft position through the specified taxiways to a runway hold short
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude</param>
    /// <param name="aircraftLon">Aircraft longitude</param>
    /// <param name="aircraftHeading">Aircraft heading</param>
    /// <param name="taxiwayNames">Ordered list of taxiway names to follow</param>
    /// <param name="runwayName">Destination runway name</param>
    /// <returns>Route result with built route or error message</returns>
    public TaxiRouteResult BuildRouteToRunway(
        double aircraftLat, double aircraftLon, double aircraftHeading,
        List<string> taxiwayNames, string runwayName)
    {
        // Find the destination hold short node
        var destinationNode = _graph.FindHoldShortByRunway(runwayName);
        if (destinationNode == null)
            return TaxiRouteResult.Failed($"Runway {runwayName} not found at this airport");

        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Destination node for runway {runwayName}: ({destinationNode.Latitude:F6}, {destinationNode.Longitude:F6}), HoldShortRunway={destinationNode.HoldShortRunway}");

        return BuildRoute(aircraftLat, aircraftLon, aircraftHeading, taxiwayNames,
            destinationNode, $"Runway {runwayName}");
    }

    /// <summary>
    /// Builds a route from the current aircraft position through the specified taxiways to a gate/parking spot
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude</param>
    /// <param name="aircraftLon">Aircraft longitude</param>
    /// <param name="aircraftHeading">Aircraft heading</param>
    /// <param name="taxiwayNames">Ordered list of taxiway names to follow</param>
    /// <param name="gateName">Destination gate/parking spot name</param>
    /// <returns>Route result with built route or error message</returns>
    public TaxiRouteResult BuildRouteToGate(
        double aircraftLat, double aircraftLon, double aircraftHeading,
        List<string> taxiwayNames, string gateName)
    {
        // Find the destination parking node
        var destinationNode = _graph.FindParkingByName(gateName);
        if (destinationNode == null)
            return TaxiRouteResult.Failed($"Gate {gateName} not found at this airport");

        string description = gateName;
        if (destinationNode.HasJetway)
            description += " (with jetway)";

        return BuildRoute(aircraftLat, aircraftLon, aircraftHeading, taxiwayNames,
            destinationNode, description);
    }

    /// <summary>
    /// Core route building logic
    /// </summary>
    private TaxiRouteResult BuildRoute(
        double aircraftLat, double aircraftLon, double aircraftHeading,
        List<string> taxiwayNames, TaxiwayNode destinationNode, string destinationDescription)
    {
        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Building route to {destinationDescription} via {string.Join(", ", taxiwayNames)}");

        // Validate taxiway names
        var validationResult = ValidateTaxiwayNames(taxiwayNames);
        if (!validationResult.Success)
            return validationResult;

        if (taxiwayNames.Count == 0)
            return TaxiRouteResult.Failed("No taxiways specified");

        // Step 1: Find starting point and path to first taxiway (handles connectors from gate)
        var startResult = FindStartingPoint(aircraftLat, aircraftLon, aircraftHeading, taxiwayNames[0]);
        if (startResult == null)
            return TaxiRouteResult.Failed($"Cannot find a path to taxiway {taxiwayNames[0]} from current position");

        var (startNode, pathToFirstTaxiway, entryNode, startHeading) = startResult.Value;
        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Starting from node at ({startNode.Latitude:F6}, {startNode.Longitude:F6}), entry to {taxiwayNames[0]} at ({entryNode.Latitude:F6}, {entryNode.Longitude:F6})");

        // Step 2: Build path through each taxiway in sequence
        var allSegments = new List<(TaxiwaySegment Segment, TaxiwayNode FromNode, TaxiwayNode ToNode)>();

        // Prepend initial connector segments (path from current position to first taxiway)
        var initialSegments = DetermineSegmentDirections(pathToFirstTaxiway, startNode);
        allSegments.AddRange(initialSegments);
        var currentNode = initialSegments.Count > 0 ? initialSegments[^1].ToNode : startNode;

        // Now currentNode should be at the entry point of the first taxiway
        currentNode = entryNode;

        for (int i = 0; i < taxiwayNames.Count; i++)
        {
            string currentTaxiway = taxiwayNames[i];
            string? nextTaxiway = i + 1 < taxiwayNames.Count ? taxiwayNames[i + 1] : null;

            // Find the exit point from current taxiway
            TaxiwayNode exitNode;
            if (nextTaxiway != null)
            {
                // Need to find where current taxiway connects to next taxiway
                var connectionResult = FindTaxiwayConnection(currentNode, currentTaxiway, nextTaxiway);
                if (connectionResult == null)
                    return TaxiRouteResult.Failed($"Cannot find a path from taxiway {currentTaxiway} to taxiway {nextTaxiway}");

                exitNode = connectionResult.Value.ConnectionNode;
                // Add the path segments with correct direction
                var connectionSegments = DetermineSegmentDirections(connectionResult.Value.Path, currentNode);
                allSegments.AddRange(connectionSegments);
                currentNode = connectionSegments.Count > 0 ? connectionSegments[^1].ToNode : exitNode;
            }
            else
            {
                // Last taxiway - find path to destination
                var pathToDest = FindPathToDestination(currentNode, currentTaxiway, destinationNode);
                if (pathToDest == null)
                    return TaxiRouteResult.Failed($"Cannot reach {destinationDescription} from taxiway {currentTaxiway}");

                // Add the path segments with correct direction
                var destSegments = DetermineSegmentDirections(pathToDest, currentNode);
                allSegments.AddRange(destSegments);
                currentNode = destSegments.Count > 0 ? destSegments[^1].ToNode : destinationNode;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Path has {allSegments.Count} segments");

        // Step 3: Convert segments to waypoints
        var route = new TaxiRoute
        {
            DestinationNode = destinationNode,
            DestinationDescription = destinationDescription,
            SelectedTaxiways = new List<string>(taxiwayNames)
        };

        double previousHeading = aircraftHeading;
        string? previousTaxiwayName = null;

        // Add waypoint for aircraft's current segment (so guidance starts from where aircraft IS)
        // This prevents "through walls" guidance when aircraft is on a different segment than the route starts
        var nearestResult = _graph.FindNearestSegment(aircraftLat, aircraftLon, aircraftHeading);
        if (nearestResult != null)
        {
            var nearestSegment = nearestResult.Value.Segment;
            var targetNode = _graph.GetAheadNode(nearestSegment, aircraftHeading);

            // Only add starting waypoint if we're not already on the first segment of the path
            // (avoid duplicate if aircraft is exactly where the path starts)
            bool isOnFirstPathSegment = allSegments.Count > 0 && allSegments[0].Segment == nearestSegment;

            if (!isOnFirstPathSegment)
            {
                var otherNode = nearestSegment.GetOtherNode(targetNode) ?? nearestSegment.StartNode;
                double segmentHeading = nearestSegment.GetHeadingFrom(otherNode);

                double distanceToTarget = NavigationCalculator.CalculateDistance(
                    aircraftLat, aircraftLon, targetNode.Latitude, targetNode.Longitude);

                var startingWaypoint = new TaxiRouteWaypoint
                {
                    Segment = nearestSegment,
                    TargetNode = targetNode,
                    Type = WaypointType.Normal,
                    TurnDirection = TurnDirection.Straight,
                    TurnAngle = 0,
                    Heading = segmentHeading,
                    ApproachAnnouncement = "Approaching connector",
                    PassAnnouncement = "",
                    DistanceFromPreviousFeet = distanceToTarget
                };
                route.Waypoints.Add(startingWaypoint);
                previousHeading = segmentHeading;
                previousTaxiwayName = nearestSegment.Name;

                System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Added starting waypoint on {nearestSegment.Name ?? "connector"}, {distanceToTarget:F0}ft to node");
            }
        }

        for (int i = 0; i < allSegments.Count; i++)
        {
            var (segment, fromNode, toNode) = allSegments[i];
            double segmentHeading = segment.GetHeadingFrom(fromNode);

            // Calculate turn direction and angle
            double turnAngle = segmentHeading - previousHeading;
            while (turnAngle > 180) turnAngle -= 360;
            while (turnAngle < -180) turnAngle += 360;

            TurnDirection turnDirection;
            if (Math.Abs(turnAngle) < 30)
                turnDirection = TurnDirection.Straight;
            else if (turnAngle < 0)
                turnDirection = TurnDirection.Left;
            else
                turnDirection = TurnDirection.Right;

            // Determine waypoint type
            WaypointType waypointType = WaypointType.Normal;
            bool isLastWaypoint = i == allSegments.Count - 1;

            if (isLastWaypoint)
            {
                waypointType = WaypointType.Destination;
            }
            else if (toNode.Type == TaxiwayNodeType.HoldShort)
            {
                waypointType = WaypointType.HoldShort;
            }
            else if (segment.HasName && segment.Name != previousTaxiwayName)
            {
                waypointType = WaypointType.TaxiwayChange;
            }

            // Generate announcements
            string approachAnnouncement = GenerateApproachAnnouncement(
                turnDirection, turnAngle, segment, toNode, waypointType, destinationDescription);
            string passAnnouncement = GeneratePassAnnouncement(
                segment, toNode, waypointType, destinationDescription);

            var waypoint = new TaxiRouteWaypoint
            {
                Segment = segment,
                TargetNode = toNode,
                Type = waypointType,
                TurnDirection = turnDirection,
                TurnAngle = turnAngle,
                Heading = segmentHeading,
                ApproachAnnouncement = approachAnnouncement,
                PassAnnouncement = passAnnouncement,
                DistanceFromPreviousFeet = segment.Length
            };

            route.Waypoints.Add(waypoint);
            previousHeading = segmentHeading;
            previousTaxiwayName = segment.Name;
        }

        // Calculate total distance
        route.TotalDistanceFeet = route.Waypoints.Sum(w => w.DistanceFromPreviousFeet);

        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Route built successfully: {route.Waypoints.Count} waypoints, {route.TotalDistanceFeet:F0}ft total");

        return TaxiRouteResult.Succeeded(route);
    }

    /// <summary>
    /// Validates that all specified taxiway names exist in the graph
    /// </summary>
    private TaxiRouteResult ValidateTaxiwayNames(List<string> taxiwayNames)
    {
        var availableTaxiways = _graph.GetAllTaxiwayNames();

        foreach (var name in taxiwayNames)
        {
            if (!availableTaxiways.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return TaxiRouteResult.Failed($"Taxiway {name} not found at this airport");
            }
        }

        return TaxiRouteResult.Succeeded(null!); // Just using this for validation
    }

    /// <summary>
    /// Finds the starting point and path to the first taxiway.
    /// Handles the case where aircraft is on connectors before reaching the first named taxiway.
    /// </summary>
    private (TaxiwayNode StartNode, List<TaxiwaySegment> PathToFirstTaxiway, TaxiwayNode EntryNode, double Heading)? FindStartingPoint(
        double aircraftLat, double aircraftLon, double aircraftHeading, string firstTaxiway)
    {
        // Step 1: Find the nearest segment to aircraft (any segment, including connectors)
        var nearestResult = _graph.FindNearestSegment(aircraftLat, aircraftLon, aircraftHeading);
        if (nearestResult == null)
            return null;

        var nearestSegment = nearestResult.Value.Segment;
        var nearestNode = _graph.GetAheadNode(nearestSegment, aircraftHeading);

        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Nearest segment: {nearestSegment.Name ?? "connector"}, nearest node ahead at ({nearestNode.Latitude:F6}, {nearestNode.Longitude:F6})");

        // Step 2: Check if we're already on the first taxiway
        if (nearestSegment.Name == firstTaxiway)
        {
            // Already on the first taxiway - no initial path needed
            double heading = nearestSegment.GetHeadingFrom(nearestSegment.GetOtherNode(nearestNode) ?? nearestSegment.StartNode);
            System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Already on first taxiway {firstTaxiway}");
            return (nearestNode, new List<TaxiwaySegment>(), nearestNode, heading);
        }

        // Step 3: Get all nodes on the first taxiway
        var firstTaxiwayNodes = _graph.GetNodesOnTaxiway(firstTaxiway);
        if (firstTaxiwayNodes.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] No nodes found on taxiway {firstTaxiway}");
            return null;
        }

        // Step 4: Find shortest path from nearest node to any node on first taxiway
        List<TaxiwaySegment>? bestPath = null;
        TaxiwayNode? bestTargetNode = null;
        double bestDistance = double.MaxValue;

        foreach (var taxiwayNode in firstTaxiwayNodes)
        {
            var path = _graph.FindPath(nearestNode, taxiwayNode);
            if (path != null)
            {
                double distance = path.Sum(s => s.Length);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPath = path;
                    bestTargetNode = taxiwayNode;
                }
            }
        }

        if (bestPath == null || bestTargetNode == null)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] No path found from current position to taxiway {firstTaxiway}");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Found path to taxiway {firstTaxiway}: {bestPath.Count} segments, {bestDistance:F0}ft");

        // Step 5: Calculate heading for first segment
        double startHeading = bestPath.Count > 0
            ? bestPath[0].GetHeadingFrom(nearestNode)
            : aircraftHeading;

        return (nearestNode, bestPath, bestTargetNode, startHeading);
    }

    /// <summary>
    /// Finds the connection point between two taxiways
    /// </summary>
    private (TaxiwayNode ConnectionNode, List<TaxiwaySegment> Path)? FindTaxiwayConnection(
        TaxiwayNode currentNode, string currentTaxiway, string nextTaxiway)
    {
        // Get all nodes on both taxiways
        var currentTaxiwayNodes = _graph.GetNodesOnTaxiway(currentTaxiway);
        var nextTaxiwayNodes = _graph.GetNodesOnTaxiway(nextTaxiway);

        // Find intersection points (nodes that are on both taxiways)
        var directConnections = currentTaxiwayNodes.Intersect(nextTaxiwayNodes).ToList();

        if (directConnections.Count > 0)
        {
            // Find the closest direct connection from current node
            TaxiwayNode? bestConnection = null;
            List<TaxiwaySegment>? bestPath = null;
            double bestDistance = double.MaxValue;

            foreach (var connectionNode in directConnections)
            {
                var path = _graph.FindPath(currentNode, connectionNode);
                if (path != null)
                {
                    double distance = path.Sum(s => s.Length);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestConnection = connectionNode;
                        bestPath = path;
                    }
                }
            }

            if (bestConnection != null && bestPath != null)
            {
                System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Direct connection from {currentTaxiway} to {nextTaxiway} at node, distance {bestDistance:F0}ft");
                return (bestConnection, bestPath);
            }
        }

        // No direct connection - need to find path via connectors
        List<TaxiwaySegment>? shortestPath = null;
        TaxiwayNode? shortestPathEnd = null;
        double shortestDistance = double.MaxValue;

        foreach (var targetNode in nextTaxiwayNodes)
        {
            var path = _graph.FindPath(currentNode, targetNode);
            if (path != null)
            {
                double distance = path.Sum(s => s.Length);
                if (distance < shortestDistance)
                {
                    shortestDistance = distance;
                    shortestPath = path;
                    shortestPathEnd = targetNode;
                }
            }
        }

        if (shortestPath != null && shortestPathEnd != null)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Path via connectors from {currentTaxiway} to {nextTaxiway}, distance {shortestDistance:F0}ft");
            return (shortestPathEnd, shortestPath);
        }

        return null;
    }

    /// <summary>
    /// Finds path from current position on a taxiway to the destination node
    /// </summary>
    private List<TaxiwaySegment>? FindPathToDestination(
        TaxiwayNode currentNode, string currentTaxiway, TaxiwayNode destinationNode)
    {
        // Try direct path first
        var directPath = _graph.FindPath(currentNode, destinationNode);
        if (directPath != null)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiRouteBuilder] Direct path to destination found, {directPath.Count} segments");
            return directPath;
        }

        return null;
    }

    /// <summary>
    /// Checks if a node is already on our path
    /// </summary>
    private static bool IsOnPath(TaxiwayNode node, List<(TaxiwaySegment, TaxiwayNode, TaxiwayNode)> path)
    {
        return path.Any(p => p.Item2 == node || p.Item3 == node);
    }

    /// <summary>
    /// Checks if two nodes are directly connected
    /// </summary>
    private static bool IsConnectedTo(TaxiwayNode node1, TaxiwayNode node2)
    {
        return node1.ConnectedSegments.Any(s => s.IsConnectedTo(node2));
    }

    /// <summary>
    /// Determines the correct direction (fromNode, toNode) for each segment in a path.
    /// Uses shared nodes between consecutive segments to determine direction.
    /// </summary>
    /// <param name="segments">List of segments from Dijkstra pathfinding</param>
    /// <param name="startNode">The starting node of the path</param>
    /// <returns>List of tuples with segment and correct from/to nodes</returns>
    private static List<(TaxiwaySegment Segment, TaxiwayNode FromNode, TaxiwayNode ToNode)> DetermineSegmentDirections(
        List<TaxiwaySegment> segments, TaxiwayNode startNode)
    {
        var result = new List<(TaxiwaySegment Segment, TaxiwayNode FromNode, TaxiwayNode ToNode)>();

        if (segments.Count == 0)
            return result;

        // For first segment: find which node connects to startNode (or is startNode)
        var firstSeg = segments[0];
        TaxiwayNode firstFrom;
        if (firstSeg.StartNode == startNode)
            firstFrom = firstSeg.StartNode;
        else if (firstSeg.EndNode == startNode)
            firstFrom = firstSeg.EndNode;
        else if (startNode.ConnectedSegments.Any(s => s.IsConnectedTo(firstSeg.StartNode)))
            firstFrom = firstSeg.StartNode;
        else
            firstFrom = firstSeg.EndNode;

        var firstTo = firstSeg.GetOtherNode(firstFrom) ?? firstSeg.EndNode;
        result.Add((firstSeg, firstFrom, firstTo));

        // For subsequent segments: fromNode is the shared node with previous segment's toNode
        for (int i = 1; i < segments.Count; i++)
        {
            var seg = segments[i];
            var prevTo = result[i - 1].ToNode;

            TaxiwayNode fromNode;
            if (seg.StartNode == prevTo)
                fromNode = seg.StartNode;
            else if (seg.EndNode == prevTo)
                fromNode = seg.EndNode;
            else
            {
                // Fallback: if nodes don't match exactly, check connectivity
                fromNode = prevTo.ConnectedSegments.Contains(seg) && seg.IsConnectedTo(prevTo)
                    ? (seg.StartNode.ConnectedSegments.Any(s => s.IsConnectedTo(prevTo)) ? seg.StartNode : seg.EndNode)
                    : seg.StartNode;
            }

            var toNode = seg.GetOtherNode(fromNode) ?? seg.EndNode;
            result.Add((seg, fromNode, toNode));
        }

        return result;
    }

    /// <summary>
    /// Generates the approach announcement for a waypoint
    /// </summary>
    private static string GenerateApproachAnnouncement(
        TurnDirection turn, double turnAngle, TaxiwaySegment segment,
        TaxiwayNode targetNode, WaypointType type, string destinationDescription)
    {
        string turnText = turn switch
        {
            TurnDirection.Left => "Turn left",
            TurnDirection.Right => "Turn right",
            _ => "Continue"
        };

        return type switch
        {
            WaypointType.Destination when targetNode.Type == TaxiwayNodeType.HoldShort =>
                $"Hold short {targetNode.HoldShortRunway} ahead",
            WaypointType.Destination when targetNode.Type == TaxiwayNodeType.Parking =>
                $"Approaching {destinationDescription}",
            WaypointType.HoldShort =>
                $"Hold short runway {targetNode.HoldShortRunway} in 150 feet",
            WaypointType.TaxiwayChange =>
                $"{turnText} onto taxiway {segment.Name} in 150 feet",
            _ when turn == TurnDirection.Straight =>
                "", // No announcement for straight segments
            _ =>
                $"{turnText} in 150 feet"
        };
    }

    /// <summary>
    /// Generates the pass announcement for a waypoint
    /// </summary>
    private static string GeneratePassAnnouncement(
        TaxiwaySegment segment, TaxiwayNode targetNode,
        WaypointType type, string destinationDescription)
    {
        return type switch
        {
            WaypointType.Destination when targetNode.Type == TaxiwayNodeType.HoldShort =>
                $"Hold short {targetNode.HoldShortRunway}. Guidance complete.",
            WaypointType.Destination when targetNode.Type == TaxiwayNodeType.Parking =>
                $"Arrived at {destinationDescription}. Guidance complete.",
            WaypointType.HoldShort =>
                $"Hold short runway {targetNode.HoldShortRunway}",
            WaypointType.TaxiwayChange =>
                $"Now on taxiway {segment.Name}",
            _ => ""
        };
    }
}
