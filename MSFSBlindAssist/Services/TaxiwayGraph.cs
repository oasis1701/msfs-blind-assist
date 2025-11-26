using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Builds and manages the taxiway graph for an airport
/// </summary>
public class TaxiwayGraph
{
    private readonly Dictionary<string, TaxiwayNode> _nodes;
    private readonly List<TaxiwaySegment> _segments;
    private readonly List<(string RunwayName, double Latitude, double Longitude, double Heading)> _runwayEnds;

    private const double COORDINATE_EPSILON = 0.000005; // ~0.5 meters for coordinate matching
    private const double NM_TO_FEET = 6076.12;
    private const double HOLD_SHORT_RUNWAY_MATCH_FEET = 500.0; // Max distance to associate HSND with runway

    public string AirportIcao { get; }
    public int SegmentCount => _segments.Count;
    public int NodeCount => _nodes.Count;

    public IReadOnlyList<TaxiwaySegment> Segments => _segments;
    public IReadOnlyCollection<TaxiwayNode> Nodes => _nodes.Values;

    public TaxiwayGraph(string airportIcao)
    {
        AirportIcao = airportIcao;
        _nodes = new Dictionary<string, TaxiwayNode>();
        _segments = new List<TaxiwaySegment>();
        _runwayEnds = new List<(string, double, double, double)>();
    }

    /// <summary>
    /// Builds the taxiway graph from database records
    /// </summary>
    public static TaxiwayGraph? Build(string databasePath, string airportIcao)
    {
        var provider = new TaxiwayDatabaseProvider(databasePath);

        // Get airport ID
        int? airportId = provider.GetAirportId(airportIcao);
        if (!airportId.HasValue)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Airport not found: {airportIcao}");
            return null;
        }

        // Get taxi path records
        var taxiPaths = provider.GetTaxiPaths(airportId.Value);
        if (taxiPaths.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] No taxi paths found for {airportIcao}");
            return null;
        }

        // Get runway ends for hold short identification
        var runwayEnds = provider.GetRunwayEnds(airportId.Value);

        var graph = new TaxiwayGraph(airportIcao);
        graph._runwayEnds.AddRange(runwayEnds);

        // Build nodes and segments
        foreach (var path in taxiPaths)
        {
            // Get or create start node
            var startNode = graph.GetOrCreateNode(
                path.StartLaty, path.StartLonx,
                ParseNodeType(path.StartType));

            // Get or create end node
            var endNode = graph.GetOrCreateNode(
                path.EndLaty, path.EndLonx,
                ParseNodeType(path.EndType));

            // Create segment
            var segment = new TaxiwaySegment
            {
                Id = path.Id,
                StartNode = startNode,
                EndNode = endNode,
                Name = string.IsNullOrEmpty(path.Name) ? null : path.Name,
                Width = path.Width,
                Surface = path.Surface,
                SegmentType = path.Type,
                Heading = NavigationCalculator.CalculateBearing(
                    startNode.Latitude, startNode.Longitude,
                    endNode.Latitude, endNode.Longitude),
                Length = NavigationCalculator.CalculateDistance(
                    startNode.Latitude, startNode.Longitude,
                    endNode.Latitude, endNode.Longitude) * NM_TO_FEET
            };

            // Connect segment to nodes
            startNode.ConnectedSegments.Add(segment);
            endNode.ConnectedSegments.Add(segment);

            graph._segments.Add(segment);
        }

        // Associate hold short nodes with runways
        graph.AssociateHoldShortNodes();

        // Associate parking nodes with parking spot data
        graph.AssociateParkingNodes(provider, airportId.Value);

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Built graph for {airportIcao}: {graph.NodeCount} nodes, {graph.SegmentCount} segments");

        return graph;
    }

    /// <summary>
    /// Gets or creates a node at the specified coordinates
    /// </summary>
    private TaxiwayNode GetOrCreateNode(double latitude, double longitude, TaxiwayNodeType type)
    {
        // Round coordinates for consistent key generation
        string key = GetNodeKey(latitude, longitude);

        if (_nodes.TryGetValue(key, out var existingNode))
        {
            // Upgrade node type if needed (HoldShort > Parking > Normal)
            if (type > existingNode.Type)
            {
                existingNode.Type = type;
            }
            return existingNode;
        }

        var newNode = new TaxiwayNode(latitude, longitude, type);
        _nodes[key] = newNode;
        return newNode;
    }

    /// <summary>
    /// Generates a consistent key for node lookup
    /// </summary>
    private static string GetNodeKey(double latitude, double longitude)
    {
        // Round to ~0.5 meter precision for matching
        double roundedLat = Math.Round(latitude / COORDINATE_EPSILON) * COORDINATE_EPSILON;
        double roundedLon = Math.Round(longitude / COORDINATE_EPSILON) * COORDINATE_EPSILON;
        return $"{roundedLat:F6},{roundedLon:F6}";
    }

    /// <summary>
    /// Parses node type from database string
    /// </summary>
    private static TaxiwayNodeType ParseNodeType(string? typeStr)
    {
        return typeStr?.ToUpperInvariant() switch
        {
            "HSND" => TaxiwayNodeType.HoldShort,
            "P" => TaxiwayNodeType.Parking,
            _ => TaxiwayNodeType.Normal
        };
    }

    /// <summary>
    /// Associates hold short nodes with their nearest runway
    /// </summary>
    private void AssociateHoldShortNodes()
    {
        var holdShortNodes = _nodes.Values.Where(n => n.Type == TaxiwayNodeType.HoldShort).ToList();

        foreach (var node in holdShortNodes)
        {
            string? nearestRunway = null;
            double nearestDistance = double.MaxValue;

            foreach (var (runwayName, lat, lon, _) in _runwayEnds)
            {
                double distanceFeet = NavigationCalculator.CalculateDistance(
                    node.Latitude, node.Longitude, lat, lon) * NM_TO_FEET;

                if (distanceFeet < nearestDistance && distanceFeet < HOLD_SHORT_RUNWAY_MATCH_FEET)
                {
                    nearestDistance = distanceFeet;
                    nearestRunway = runwayName;
                }
            }

            node.HoldShortRunway = nearestRunway;

            if (nearestRunway != null)
            {
                System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Hold short node at ({node.Latitude:F6}, {node.Longitude:F6}) → Runway {nearestRunway} ({nearestDistance:F0}ft)");
            }
        }
    }

    /// <summary>
    /// Associates parking nodes with their parking spot names from the database
    /// </summary>
    private void AssociateParkingNodes(TaxiwayDatabaseProvider provider, int airportId)
    {
        const double PARKING_MATCH_FEET = 100.0; // Max distance to match parking spot to node

        var parkingNodes = _nodes.Values.Where(n => n.Type == TaxiwayNodeType.Parking).ToList();
        if (parkingNodes.Count == 0)
            return;

        var parkingSpots = provider.GetParkingSpots(airportId);
        if (parkingSpots.Count == 0)
            return;

        int matchCount = 0;
        foreach (var node in parkingNodes)
        {
            string? bestName = null;
            bool bestHasJetway = false;
            double bestDistance = double.MaxValue;

            foreach (var (lat, lon, name, _, hasJetway) in parkingSpots)
            {
                double distanceFeet = NavigationCalculator.CalculateDistance(
                    node.Latitude, node.Longitude, lat, lon) * NM_TO_FEET;

                if (distanceFeet < bestDistance && distanceFeet < PARKING_MATCH_FEET)
                {
                    bestDistance = distanceFeet;
                    bestName = name;
                    bestHasJetway = hasJetway;
                }
            }

            if (bestName != null)
            {
                node.ParkingSpotName = bestName;
                node.HasJetway = bestHasJetway;
                matchCount++;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Associated {matchCount}/{parkingNodes.Count} parking nodes with spot names");
    }

    /// <summary>
    /// Finds the nearest segment to the given position that aligns with the heading
    /// </summary>
    /// <param name="latitude">Aircraft latitude</param>
    /// <param name="longitude">Aircraft longitude</param>
    /// <param name="heading">Aircraft heading (optional, for disambiguation)</param>
    /// <param name="maxDistanceFeet">Maximum search distance</param>
    /// <returns>Nearest segment and distance, or null if none found</returns>
    public (TaxiwaySegment Segment, double DistanceFeet, double CrossTrackFeet)? FindNearestSegment(
        double latitude, double longitude,
        double? heading = null,
        double maxDistanceFeet = 200.0)
    {
        TaxiwaySegment? bestSegment = null;
        double bestScore = double.MaxValue;
        double bestDistance = 0;
        double bestCrossTrack = 0;

        foreach (var segment in _segments)
        {
            // Calculate perpendicular distance to segment line
            double crossTrackNM = CalculateDistanceToSegment(
                latitude, longitude, segment);
            double crossTrackFeet = Math.Abs(crossTrackNM * NM_TO_FEET);

            if (crossTrackFeet > maxDistanceFeet)
                continue;

            // Calculate score (distance + heading penalty)
            double score = crossTrackFeet;

            if (heading.HasValue)
            {
                // Add penalty for misaligned heading
                double headingDiff = GetHeadingDifference(heading.Value, segment.Heading);
                // Allow either direction along the segment
                double reverseHeadingDiff = GetHeadingDifference(heading.Value, (segment.Heading + 180) % 360);
                double alignmentDiff = Math.Min(headingDiff, reverseHeadingDiff);

                // Penalize if heading differs by more than 45 degrees
                if (alignmentDiff > 45)
                {
                    score += alignmentDiff * 2; // Heavy penalty for perpendicular segments
                }
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestSegment = segment;
                bestDistance = crossTrackFeet;

                // Get cross-track sign relative to segment direction
                double crossTrackSign = Math.Sign(GetCrossTrackSign(latitude, longitude, segment));

                // If aircraft is traveling opposite to segment direction, flip the sign
                // so left/right are relative to pilot's perspective
                if (heading.HasValue)
                {
                    double headingDiff = GetHeadingDifference(heading.Value, segment.Heading);
                    if (headingDiff > 90)
                    {
                        crossTrackSign = -crossTrackSign;
                    }
                }

                bestCrossTrack = crossTrackNM * NM_TO_FEET * crossTrackSign;
            }
        }

        if (bestSegment == null)
            return null;

        return (bestSegment, bestDistance, bestCrossTrack);
    }

    /// <summary>
    /// Finds all nearby segments grouped by taxiway name, with both directions for each.
    /// Used for initial segment selection and re-locking.
    /// </summary>
    /// <param name="latitude">Aircraft latitude</param>
    /// <param name="longitude">Aircraft longitude</param>
    /// <param name="aircraftHeading">Aircraft heading for relative direction calculation</param>
    /// <param name="maxDistanceFeet">Maximum search distance</param>
    /// <returns>List of segment options sorted by relative direction (ahead first) then distance</returns>
    public List<SegmentOption> FindNearbySegments(
        double latitude, double longitude,
        double aircraftHeading,
        double maxDistanceFeet = 200.0)
    {
        // Step 1: Find all segments within range and group by taxiway identity
        var segmentsByTaxiway = new Dictionary<string, (TaxiwaySegment Segment, double DistanceFeet, string DisplayName)>();

        foreach (var segment in _segments)
        {
            // Calculate perpendicular distance to segment
            double crossTrackNM = CalculateDistanceToSegment(latitude, longitude, segment);
            double distanceFeet = Math.Abs(crossTrackNM * NM_TO_FEET);

            if (distanceFeet > maxDistanceFeet)
                continue;

            // Generate a key for grouping (taxiway name or connector destination)
            string taxiwayKey;
            string displayName;

            if (segment.HasName)
            {
                taxiwayKey = $"taxiway_{segment.Name}";
                displayName = $"Taxiway {segment.Name}";
            }
            else
            {
                // For connectors, use destination names from both ends
                var destFromStart = GetDestinationNameWithHeading(segment, segment.StartNode);
                var destFromEnd = GetDestinationNameWithHeading(segment, segment.EndNode);
                // Use a combined key to group similar connectors
                taxiwayKey = $"connector_{destFromStart}_{destFromEnd}";
                displayName = destFromStart; // Will be refined per-direction below
            }

            // Keep the closest segment for each taxiway
            if (!segmentsByTaxiway.ContainsKey(taxiwayKey) ||
                distanceFeet < segmentsByTaxiway[taxiwayKey].DistanceFeet)
            {
                segmentsByTaxiway[taxiwayKey] = (segment, distanceFeet, displayName);
            }
        }

        // Step 2: Generate two options per taxiway (both directions)
        var options = new List<SegmentOption>();

        foreach (var (key, (segment, distanceFeet, displayName)) in segmentsByTaxiway)
        {
            // Direction 1: StartNode → EndNode (segment.Heading)
            double heading1 = segment.Heading;
            var targetNode1 = segment.EndNode;
            string name1 = segment.HasName
                ? $"Taxiway {segment.Name}"
                : GetDestinationNameWithHeading(segment, segment.StartNode);
            string relativeDir1 = GetRelativeDirection(aircraftHeading, heading1);

            options.Add(new SegmentOption
            {
                Segment = segment,
                TaxiwayName = name1,
                Heading = heading1,
                DistanceFeet = distanceFeet,
                RelativeDirection = relativeDir1,
                TargetNode = targetNode1
            });

            // Direction 2: EndNode → StartNode (opposite heading)
            double heading2 = (segment.Heading + 180) % 360;
            var targetNode2 = segment.StartNode;
            string name2 = segment.HasName
                ? $"Taxiway {segment.Name}"
                : GetDestinationNameWithHeading(segment, segment.EndNode);
            string relativeDir2 = GetRelativeDirection(aircraftHeading, heading2);

            options.Add(new SegmentOption
            {
                Segment = segment,
                TaxiwayName = name2,
                Heading = heading2,
                DistanceFeet = distanceFeet,
                RelativeDirection = relativeDir2,
                TargetNode = targetNode2
            });
        }

        // Step 3: Sort options - "ahead" first, then by distance
        options.Sort((a, b) =>
        {
            // Priority: ahead > ahead-left/ahead-right > left/right > behind
            int priorityA = GetDirectionPriority(a.RelativeDirection);
            int priorityB = GetDirectionPriority(b.RelativeDirection);

            if (priorityA != priorityB)
                return priorityA.CompareTo(priorityB);

            return a.DistanceFeet.CompareTo(b.DistanceFeet);
        });

        return options;
    }

    /// <summary>
    /// Gets the relative direction description based on heading difference
    /// </summary>
    private static string GetRelativeDirection(double aircraftHeading, double targetHeading)
    {
        double diff = targetHeading - aircraftHeading;
        while (diff > 180) diff -= 360;
        while (diff < -180) diff += 360;

        // Determine direction based on angle
        if (Math.Abs(diff) <= 30)
            return "ahead";
        if (Math.Abs(diff) >= 150)
            return "behind";
        if (diff > 30 && diff < 90)
            return "ahead right";
        if (diff >= 90 && diff < 150)
            return "to your right";
        if (diff < -30 && diff > -90)
            return "ahead left";
        if (diff <= -90 && diff > -150)
            return "to your left";

        return diff > 0 ? "to your right" : "to your left";
    }

    /// <summary>
    /// Gets sort priority for relative direction (lower = first)
    /// </summary>
    private static int GetDirectionPriority(string relativeDirection)
    {
        return relativeDirection switch
        {
            "ahead" => 0,
            "ahead left" => 1,
            "ahead right" => 1,
            "to your left" => 2,
            "to your right" => 2,
            "behind" => 3,
            _ => 4
        };
    }

    /// <summary>
    /// Gets destination name with heading for a connector segment
    /// </summary>
    private string GetDestinationNameWithHeading(TaxiwaySegment segment, TaxiwayNode fromNode)
    {
        if (segment.HasName)
            return $"Taxiway {segment.Name}";

        // Get destinations using enhanced lookahead
        var destinations = GetAllDestinations(segment, fromNode);
        double heading = segment.GetHeadingFrom(fromNode);

        if (destinations.Count == 0)
            return $"Connector, heading {(int)heading:000}";

        string destStr = string.Join(" and ", destinations);
        return $"Connector to {destStr}, heading {(int)heading:000}";
    }

    /// <summary>
    /// Gets all reachable named destinations within lookahead depth
    /// </summary>
    private List<string> GetAllDestinations(TaxiwaySegment segment, TaxiwayNode fromNode)
    {
        var destinations = new HashSet<string>();
        var visited = new HashSet<TaxiwaySegment> { segment };
        var queue = new Queue<(TaxiwaySegment seg, TaxiwayNode node, int depth)>();

        var nextNode = segment.GetOtherNode(fromNode);
        if (nextNode != null)
        {
            // Check if immediate destination is parking
            string? parkingDest = GetParkingDestination(nextNode);
            if (parkingDest != null)
            {
                destinations.Add(parkingDest);
            }

            queue.Enqueue((segment, nextNode, 0));
        }

        while (queue.Count > 0)
        {
            var (currentSeg, currentNode, depth) = queue.Dequeue();

            if (depth > 5) // Max lookahead depth
                continue;

            foreach (var connectedSeg in currentNode.ConnectedSegments)
            {
                if (visited.Contains(connectedSeg))
                    continue;

                visited.Add(connectedSeg);

                if (connectedSeg.HasName)
                {
                    destinations.Add($"Taxiway {connectedSeg.Name}");
                    continue; // Don't continue past named taxiways
                }

                var otherNode = connectedSeg.GetOtherNode(currentNode);
                if (otherNode != null)
                {
                    // Check for parking
                    string? parkingDest = GetParkingDestination(otherNode);
                    if (parkingDest != null)
                    {
                        destinations.Add(parkingDest);
                    }

                    if (depth < 5)
                    {
                        queue.Enqueue((connectedSeg, otherNode, depth + 1));
                    }
                }
            }
        }

        return destinations.ToList();
    }

    /// <summary>
    /// Calculates cross-track error from a specific segment (public API for locked segment tracking)
    /// </summary>
    /// <param name="latitude">Aircraft latitude</param>
    /// <param name="longitude">Aircraft longitude</param>
    /// <param name="segment">The segment to calculate cross-track from</param>
    /// <param name="aircraftHeading">Aircraft heading (used to determine left/right relative to pilot)</param>
    /// <returns>Cross-track error in feet (positive = right, negative = left from pilot perspective)</returns>
    public double CalculateCrossTrackFromSegment(
        double latitude, double longitude,
        TaxiwaySegment segment,
        double aircraftHeading)
    {
        // Calculate perpendicular distance (always positive)
        double crossTrackNM = CalculateDistanceToSegment(latitude, longitude, segment);
        double crossTrackFeet = Math.Abs(crossTrackNM * NM_TO_FEET);

        // Get cross-track sign relative to segment direction
        double crossTrackSign = Math.Sign(GetCrossTrackSign(latitude, longitude, segment));

        // If aircraft is traveling opposite to segment direction, flip the sign
        // so left/right are relative to pilot's perspective
        double headingDiff = GetHeadingDifference(aircraftHeading, segment.Heading);
        if (headingDiff > 90)
        {
            crossTrackSign = -crossTrackSign;
        }

        return crossTrackFeet * crossTrackSign;
    }

    /// <summary>
    /// Calculates perpendicular distance from a point to a segment
    /// </summary>
    private static double CalculateDistanceToSegment(double lat, double lon, TaxiwaySegment segment)
    {
        return NavigationCalculator.CalculateDistanceToLocalizer(
            lat, lon,
            segment.StartNode.Latitude, segment.StartNode.Longitude,
            segment.Heading);
    }

    /// <summary>
    /// Determines the sign of cross-track error (positive = right of centerline)
    /// relative to the segment's intrinsic direction (StartNode→EndNode)
    /// </summary>
    private static double GetCrossTrackSign(double lat, double lon, TaxiwaySegment segment)
    {
        // CalculateCrossTrackError returns: positive = right of centerline, negative = left
        // (as documented in NavigationCalculator)
        return NavigationCalculator.CalculateCrossTrackError(
            lat, lon,
            segment.StartNode.Latitude, segment.StartNode.Longitude,
            segment.Heading);
    }

    /// <summary>
    /// Gets the absolute difference between two headings (0-180)
    /// </summary>
    private static double GetHeadingDifference(double heading1, double heading2)
    {
        double diff = Math.Abs(heading1 - heading2);
        if (diff > 180) diff = 360 - diff;
        return diff;
    }

    /// <summary>
    /// Gets junction options from a node
    /// </summary>
    public List<JunctionOption> GetJunctionOptions(
        TaxiwayNode node,
        TaxiwaySegment? currentSegment,
        double aircraftHeading)
    {
        var options = new List<JunctionOption>();

        foreach (var segment in node.ConnectedSegments)
        {
            // Skip the segment we came from
            if (segment == currentSegment)
                continue;

            // Get the heading when leaving this node via this segment
            double segmentHeading = segment.GetHeadingFrom(node);

            // Calculate relative turn angle
            double turnAngle = segmentHeading - aircraftHeading;
            while (turnAngle > 180) turnAngle -= 360;
            while (turnAngle < -180) turnAngle += 360;

            // Determine turn direction
            TurnDirection direction;
            if (Math.Abs(turnAngle) < 30)
                direction = TurnDirection.Straight;
            else if (turnAngle < 0)
                direction = TurnDirection.Left;
            else
                direction = TurnDirection.Right;

            // Get destination taxiway name (may require lookahead)
            string destinationName = GetDestinationName(segment, node);

            options.Add(new JunctionOption
            {
                Segment = segment,
                Direction = direction,
                TurnAngle = turnAngle,
                DestinationName = destinationName,
                HeadingAfterTurn = segmentHeading
            });
        }

        // Sort by turn angle (straight first, then left, then right)
        options.Sort((a, b) =>
        {
            if (a.Direction == TurnDirection.Straight && b.Direction != TurnDirection.Straight)
                return -1;
            if (b.Direction == TurnDirection.Straight && a.Direction != TurnDirection.Straight)
                return 1;
            return Math.Abs(a.TurnAngle).CompareTo(Math.Abs(b.TurnAngle));
        });

        return options;
    }

    /// <summary>
    /// Gets the destination taxiway name for a segment (uses lookahead if unnamed)
    /// </summary>
    private string GetDestinationName(TaxiwaySegment segment, TaxiwayNode fromNode)
    {
        if (segment.HasName)
            return $"taxiway {segment.Name}";

        // Check if the immediate destination is a parking dead-end
        var nextNode = segment.GetOtherNode(fromNode);
        if (nextNode != null)
        {
            string? parkingDest = GetParkingDestination(nextNode);
            if (parkingDest != null)
                return parkingDest;
        }

        // Lookahead to find a named taxiway or parking
        var visited = new HashSet<TaxiwaySegment> { segment };
        var queue = new Queue<(TaxiwaySegment seg, TaxiwayNode node, int depth)>();

        if (nextNode != null)
        {
            queue.Enqueue((segment, nextNode, 0));
        }

        while (queue.Count > 0)
        {
            var (currentSeg, currentNode, depth) = queue.Dequeue();

            if (depth > 5) // Max lookahead depth
                break;

            foreach (var connectedSeg in currentNode.ConnectedSegments)
            {
                if (visited.Contains(connectedSeg))
                    continue;

                visited.Add(connectedSeg);

                if (connectedSeg.HasName)
                    return $"connector to {connectedSeg.Name}";

                var otherNode = connectedSeg.GetOtherNode(currentNode);
                if (otherNode != null)
                {
                    // Check if this node leads to a parking spot
                    string? parkingDest = GetParkingDestination(otherNode);
                    if (parkingDest != null)
                        return parkingDest;

                    queue.Enqueue((connectedSeg, otherNode, depth + 1));
                }
            }
        }

        return "connector";
    }

    /// <summary>
    /// Gets the parking destination name if the node is a parking dead-end
    /// </summary>
    private static string? GetParkingDestination(TaxiwayNode node)
    {
        if (node.Type != TaxiwayNodeType.Parking)
            return null;

        // If it's a dead-end parking node with a name, return the destination
        if (node.IsDeadEnd && !string.IsNullOrEmpty(node.ParkingSpotName))
        {
            string dest = node.ParkingSpotName;
            if (node.HasJetway)
                dest += " with jetway";
            return dest;
        }

        // If it's a parking node without a specific name
        if (node.IsDeadEnd)
            return "parking";

        return null;
    }

    /// <summary>
    /// Gets distance from position to a node
    /// </summary>
    public double GetDistanceToNode(double latitude, double longitude, TaxiwayNode node)
    {
        return NavigationCalculator.CalculateDistance(
            latitude, longitude,
            node.Latitude, node.Longitude) * NM_TO_FEET;
    }

    /// <summary>
    /// Determines which node of a segment is ahead based on aircraft heading
    /// </summary>
    public TaxiwayNode GetAheadNode(TaxiwaySegment segment, double aircraftHeading)
    {
        double headingToStart = GetHeadingDifference(aircraftHeading, (segment.Heading + 180) % 360);
        double headingToEnd = GetHeadingDifference(aircraftHeading, segment.Heading);

        return headingToEnd < headingToStart ? segment.EndNode : segment.StartNode;
    }
}

/// <summary>
/// Turn direction at a junction
/// </summary>
public enum TurnDirection
{
    Straight,
    Left,
    Right
}

/// <summary>
/// Represents an option at a junction
/// </summary>
public class JunctionOption
{
    public required TaxiwaySegment Segment { get; set; }
    public TurnDirection Direction { get; set; }
    public double TurnAngle { get; set; }
    public required string DestinationName { get; set; }
    public double HeadingAfterTurn { get; set; }

    /// <summary>
    /// Gets display text for this option
    /// </summary>
    public string GetDisplayText()
    {
        string directionText = Direction switch
        {
            TurnDirection.Straight => "Continue on",
            TurnDirection.Left => "Turn left to",
            TurnDirection.Right => "Turn right to",
            _ => "Go to"
        };

        return $"{directionText} {DestinationName}";
    }

    public override string ToString() => GetDisplayText();
}
