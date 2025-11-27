using System.Linq;
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

        int junctionCount = graph._nodes.Values.Count(n => n.IsJunction);
        int deadEndCount = graph._nodes.Values.Count(n => n.IsDeadEnd);
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Built graph for {airportIcao}: {graph.NodeCount} nodes ({junctionCount} junctions, {deadEndCount} dead-ends), {graph.SegmentCount} segments");

        // Debug: Log all junctions (nodes with 3+ connections)
        foreach (var node in graph._nodes.Values.Where(n => n.IsJunction))
        {
            var segmentNames = string.Join(", ", node.ConnectedSegments.Select(s => s.Name ?? "unnamed"));
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Junction at ({node.Latitude:F6}, {node.Longitude:F6}): {node.ConnectedSegments.Count} connections [{segmentNames}]");
        }

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
    /// Associates hold short nodes with their nearest runway using perpendicular distance to centerline.
    /// Assigns the name of the closest threshold (e.g., "13" or "31", not "13/31").
    /// </summary>
    private void AssociateHoldShortNodes()
    {
        var holdShortNodes = _nodes.Values.Where(n => n.Type == TaxiwayNodeType.HoldShort).ToList();
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] AssociateHoldShortNodes: {holdShortNodes.Count} hold short nodes to process");

        // Build runway centerlines from pairs of thresholds
        var runwayCenterlines = BuildRunwayCenterlines();
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Built {runwayCenterlines.Count} runway centerlines:");
        foreach (var (sName, eName, sLat, sLon, eLat, eLon) in runwayCenterlines)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   {sName}/{eName}: ({sLat:F6}, {sLon:F6}) to ({eLat:F6}, {eLon:F6})");
        }

        foreach (var node in holdShortNodes)
        {
            string? nearestRunway = null;
            double nearestDistance = double.MaxValue;

            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Evaluating hold short at ({node.Latitude:F6}, {node.Longitude:F6}):");

            foreach (var (startName, endName, startLat, startLon, endLat, endLon) in runwayCenterlines)
            {
                // Calculate perpendicular distance from node to runway centerline
                double perpDistFeet = CalculatePerpendicularDistance(
                    node.Latitude, node.Longitude,
                    startLat, startLon, endLat, endLon);

                // Check if within runway bounds (not past either threshold)
                bool withinBounds = IsPointWithinSegmentBounds(
                    node.Latitude, node.Longitude,
                    startLat, startLon, endLat, endLon);

                double distToStart = CalculateDistanceFeet(node.Latitude, node.Longitude, startLat, startLon);
                double distToEnd = CalculateDistanceFeet(node.Latitude, node.Longitude, endLat, endLon);

                System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   vs {startName}/{endName}: perpDist={perpDistFeet:F0}ft (max={HOLD_SHORT_RUNWAY_MATCH_FEET}), withinBounds={withinBounds}, distTo{startName}={distToStart:F0}ft, distTo{endName}={distToEnd:F0}ft");

                if (perpDistFeet < nearestDistance &&
                    perpDistFeet < HOLD_SHORT_RUNWAY_MATCH_FEET &&
                    withinBounds)
                {
                    nearestDistance = perpDistFeet;
                    nearestRunway = distToStart < distToEnd ? startName : endName;
                    System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]     -> MATCHED to {nearestRunway}");
                }
            }

            node.HoldShortRunway = nearestRunway;

            if (nearestRunway != null)
            {
                System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Hold short ASSIGNED: ({node.Latitude:F6}, {node.Longitude:F6}) -> Runway {nearestRunway}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] Hold short UNASSIGNED: ({node.Latitude:F6}, {node.Longitude:F6}) - no matching runway");
            }
        }
    }

    /// <summary>
    /// Calculates distance between two points in feet
    /// </summary>
    private static double CalculateDistanceFeet(double lat1, double lon1, double lat2, double lon2)
    {
        const double LAT_TO_FEET = 364000.0;
        const double LON_TO_FEET = 288000.0;

        double dLat = (lat2 - lat1) * LAT_TO_FEET;
        double dLon = (lon2 - lon1) * LON_TO_FEET;
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    /// <summary>
    /// Builds runway centerlines by pairing opposite runway ends.
    /// Returns both runway names separately so we can assign the closest one to hold shorts.
    /// </summary>
    private List<(string StartName, string EndName, double StartLat, double StartLon, double EndLat, double EndLon)> BuildRunwayCenterlines()
    {
        var centerlines = new List<(string, string, double, double, double, double)>();
        var processed = new HashSet<string>();

        foreach (var (name, lat, lon, heading) in _runwayEnds)
        {
            if (processed.Contains(name)) continue;

            // Find opposite runway end (heading differs by ~180°)
            var opposite = _runwayEnds.FirstOrDefault(r =>
                r.RunwayName != name &&
                Math.Abs(Math.Abs(r.Heading - heading) - 180) < 10);

            if (opposite != default)
            {
                // Return both names separately (e.g., "04" and "22", not "04/22")
                centerlines.Add((name, opposite.RunwayName, lat, lon, opposite.Latitude, opposite.Longitude));
                processed.Add(name);
                processed.Add(opposite.RunwayName);
            }
        }

        return centerlines;
    }

    /// <summary>
    /// Calculates perpendicular distance from a point to a line segment (in feet)
    /// </summary>
    private static double CalculatePerpendicularDistance(
        double pointLat, double pointLon,
        double lineLat1, double lineLon1, double lineLat2, double lineLon2)
    {
        // Convert to approximate feet for calculation
        const double LAT_TO_FEET = 364000.0;  // ~364000 ft per degree latitude
        const double LON_TO_FEET = 288000.0;  // ~288000 ft per degree longitude at mid-latitudes

        double px = (pointLon - lineLon1) * LON_TO_FEET;
        double py = (pointLat - lineLat1) * LAT_TO_FEET;
        double lx = (lineLon2 - lineLon1) * LON_TO_FEET;
        double ly = (lineLat2 - lineLat1) * LAT_TO_FEET;

        double lineLen = Math.Sqrt(lx * lx + ly * ly);
        if (lineLen < 0.001) return double.MaxValue;

        // Perpendicular distance = |cross product| / |line length|
        double cross = Math.Abs(lx * py - ly * px);
        return cross / lineLen;
    }

    /// <summary>
    /// Checks if a point's projection falls within the line segment bounds (with buffer)
    /// </summary>
    private static bool IsPointWithinSegmentBounds(
        double pointLat, double pointLon,
        double lineLat1, double lineLon1, double lineLat2, double lineLon2)
    {
        // Project point onto line and check if projection is between endpoints
        const double LAT_TO_FEET = 364000.0;
        const double LON_TO_FEET = 288000.0;

        double px = (pointLon - lineLon1) * LON_TO_FEET;
        double py = (pointLat - lineLat1) * LAT_TO_FEET;
        double lx = (lineLon2 - lineLon1) * LON_TO_FEET;
        double ly = (lineLat2 - lineLat1) * LAT_TO_FEET;

        double lineLenSq = lx * lx + ly * ly;
        if (lineLenSq < 0.001) return false;

        // t = dot(point-start, line) / |line|^2
        double t = (px * lx + py * ly) / lineLenSq;

        // Allow some buffer past the ends (500ft / runway length)
        double buffer = 500.0 / Math.Sqrt(lineLenSq);
        return t >= -buffer && t <= 1.0 + buffer;
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
    /// Finds accessible segments based on graph connectivity, not just distance.
    /// Uses the closest segment and nearest junction to determine valid options.
    /// </summary>
    /// <param name="latitude">Aircraft latitude</param>
    /// <param name="longitude">Aircraft longitude</param>
    /// <param name="aircraftHeading">Aircraft heading for relative direction calculation</param>
    /// <param name="maxDistanceFeet">Maximum search distance</param>
    /// <returns>List of segment options based on graph connectivity</returns>
    public List<SegmentOption> FindAccessibleSegments(
        double latitude, double longitude,
        double aircraftHeading,
        double maxDistanceFeet = 200.0)
    {
        var options = new List<SegmentOption>();

        // Step 1: Find the closest segment to the aircraft
        var nearestResult = FindNearestSegment(latitude, longitude, aircraftHeading, maxDistanceFeet);
        if (nearestResult == null)
        {
            System.Diagnostics.Debug.WriteLine("[TaxiwayGraph] FindAccessibleSegments: No segment found within range");
            return options;
        }

        var (closestSegment, distanceFeet, _) = nearestResult.Value;
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] FindAccessibleSegments: Closest segment '{closestSegment.Name ?? "unnamed"}' at {distanceFeet:F1}ft");

        // Step 2: Find the nearest node (could be a junction)
        var nearestNode = FindNearestNode(latitude, longitude, maxDistanceFeet);

        // Step 3: Check if we're near a junction (node with 3+ connections)
        const double JUNCTION_PROXIMITY_FEET = 75.0;
        if (nearestNode != null)
        {
            double distanceToNode = GetDistanceToNode(latitude, longitude, nearestNode);
            bool isNearJunction = nearestNode.IsJunction && distanceToNode <= JUNCTION_PROXIMITY_FEET;

            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] FindAccessibleSegments: Nearest node at {distanceToNode:F1}ft, IsJunction={nearestNode.IsJunction}, Connections={nearestNode.ConnectedSegments.Count}");

            if (isNearJunction)
            {
                // Near a junction - show all connected segments from this node
                System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] FindAccessibleSegments: Using junction mode with {nearestNode.ConnectedSegments.Count} connected segments");

                foreach (var segment in nearestNode.ConnectedSegments)
                {
                    // Get heading when leaving this junction via this segment
                    double segmentHeading = segment.GetHeadingFrom(nearestNode);
                    var targetNode = segment.GetOtherNode(nearestNode);
                    if (targetNode == null) continue;

                    string name = segment.HasName
                        ? $"Taxiway {segment.Name}"
                        : GetDestinationNameWithHeading(segment, nearestNode);
                    string relativeDir = GetRelativeDirection(aircraftHeading, segmentHeading);

                    options.Add(new SegmentOption
                    {
                        Segment = segment,
                        TaxiwayName = name,
                        Heading = segmentHeading,
                        DistanceFeet = distanceToNode,
                        RelativeDirection = relativeDir,
                        TargetNode = targetNode
                    });
                }
            }
            else
            {
                // Not near a junction - show two options for the closest segment
                options = GenerateSegmentDirectionOptions(closestSegment, distanceFeet, aircraftHeading);
            }
        }
        else
        {
            // No node found nearby - show two options for the closest segment
            options = GenerateSegmentDirectionOptions(closestSegment, distanceFeet, aircraftHeading);
        }

        // Sort options - "ahead" first, then by distance
        options.Sort((a, b) =>
        {
            int priorityA = GetDirectionPriority(a.RelativeDirection);
            int priorityB = GetDirectionPriority(b.RelativeDirection);

            if (priorityA != priorityB)
                return priorityA.CompareTo(priorityB);

            return a.DistanceFeet.CompareTo(b.DistanceFeet);
        });

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] FindAccessibleSegments: Returning {options.Count} options");
        return options;
    }

    /// <summary>
    /// Generates two options for a segment (one for each direction)
    /// </summary>
    private List<SegmentOption> GenerateSegmentDirectionOptions(
        TaxiwaySegment segment, double distanceFeet, double aircraftHeading)
    {
        var options = new List<SegmentOption>();

        // Direction 1: StartNode → EndNode
        double heading1 = segment.Heading;
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
            TargetNode = segment.EndNode
        });

        // Direction 2: EndNode → StartNode
        double heading2 = (segment.Heading + 180) % 360;
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
            TargetNode = segment.StartNode
        });

        return options;
    }

    /// <summary>
    /// Finds the nearest node to the given position
    /// </summary>
    public TaxiwayNode? FindNearestNode(double latitude, double longitude, double maxDistanceFeet = 200.0)
    {
        TaxiwayNode? nearestNode = null;
        double nearestDistance = double.MaxValue;

        foreach (var node in _nodes.Values)
        {
            double distanceFeet = GetDistanceToNode(latitude, longitude, node);

            if (distanceFeet < nearestDistance && distanceFeet <= maxDistanceFeet)
            {
                nearestDistance = distanceFeet;
                nearestNode = node;
            }
        }

        return nearestNode;
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
        // Calculate perpendicular distance to infinite line (for active tracking)
        // Uses infinite line to allow guidance past segment endpoints during junction transitions
        double crossTrackNM = CalculatePerpendicularDistanceToLine(latitude, longitude, segment);
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
    /// Calculates distance from a point to a FINITE segment (not infinite line).
    /// Returns perpendicular distance if projection falls within segment,
    /// otherwise returns distance to nearest endpoint.
    /// </summary>
    private static double CalculateDistanceToSegment(double lat, double lon, TaxiwaySegment segment)
    {
        // Calculate along-track and cross-track distances
        var (alongTrackNM, crossTrackNM) = CalculateTrackDistances(
            lat, lon,
            segment.StartNode.Latitude, segment.StartNode.Longitude,
            segment.Heading);

        double segmentLengthNM = segment.Length / NM_TO_FEET;

        // Check if perpendicular intercept falls within segment bounds
        if (alongTrackNM >= 0 && alongTrackNM <= segmentLengthNM)
        {
            // Within segment - return perpendicular distance
            return Math.Abs(crossTrackNM);
        }
        else if (alongTrackNM < 0)
        {
            // Before start node - return distance to start
            return NavigationCalculator.CalculateDistance(
                lat, lon,
                segment.StartNode.Latitude, segment.StartNode.Longitude);
        }
        else
        {
            // Past end node - return distance to end
            return NavigationCalculator.CalculateDistance(
                lat, lon,
                segment.EndNode.Latitude, segment.EndNode.Longitude);
        }
    }

    /// <summary>
    /// Calculates both along-track and cross-track distances from a point to an infinite line.
    /// Along-track: distance along the line from origin to perpendicular intercept (positive = ahead)
    /// Cross-track: perpendicular distance from line (positive = right of line direction)
    /// </summary>
    private static (double alongTrackNM, double crossTrackNM) CalculateTrackDistances(
        double pointLat, double pointLon,
        double originLat, double originLon,
        double lineHeading)
    {
        // Calculate bearing and distance from origin to point
        double bearingToPoint = NavigationCalculator.CalculateBearing(
            originLat, originLon, pointLat, pointLon);
        double distanceNM = NavigationCalculator.CalculateDistance(
            originLat, originLon, pointLat, pointLon);

        // Angular difference between line heading and bearing to point
        double angleDiff = (bearingToPoint - lineHeading) * Math.PI / 180.0;

        // Along-track distance (projection onto line)
        double alongTrackNM = distanceNM * Math.Cos(angleDiff);

        // Cross-track distance (perpendicular from line)
        double crossTrackNM = distanceNM * Math.Sin(angleDiff);

        return (alongTrackNM, crossTrackNM);
    }

    /// <summary>
    /// Calculates perpendicular distance to an infinite line extending from segment.
    /// Used for active tracking where we want centerline extension behavior.
    /// </summary>
    private static double CalculatePerpendicularDistanceToLine(double lat, double lon, TaxiwaySegment segment)
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
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] GetJunctionOptions: Node at ({node.Latitude:F6}, {node.Longitude:F6}) has {node.ConnectedSegments.Count} connected segments, excluding incoming '{currentSegment?.Name ?? "unnamed"}'");

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

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] GetJunctionOptions: Generated {options.Count} options");

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

    #region Route Building Methods

    /// <summary>
    /// Finds the shortest path between two nodes using Dijkstra's algorithm
    /// </summary>
    /// <param name="start">Starting node</param>
    /// <param name="end">Destination node</param>
    /// <returns>List of segments forming the path, or null if no path exists</returns>
    public List<TaxiwaySegment>? FindPath(TaxiwayNode start, TaxiwayNode end)
    {
        if (start == end)
            return new List<TaxiwaySegment>();

        var distances = new Dictionary<TaxiwayNode, double>();
        var previous = new Dictionary<TaxiwayNode, (TaxiwayNode Node, TaxiwaySegment Segment)>();
        var queue = new PriorityQueue<TaxiwayNode, double>();

        // Initialize distances
        foreach (var node in _nodes.Values)
            distances[node] = double.MaxValue;

        distances[start] = 0;
        queue.Enqueue(start, 0);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current == end)
                break;

            // Skip if we've found a better path already
            if (distances[current] == double.MaxValue)
                continue;

            foreach (var segment in current.ConnectedSegments)
            {
                var neighbor = segment.GetOtherNode(current);
                if (neighbor == null)
                    continue;

                double newDist = distances[current] + segment.Length;
                if (newDist < distances[neighbor])
                {
                    distances[neighbor] = newDist;
                    previous[neighbor] = (current, segment);
                    queue.Enqueue(neighbor, newDist);
                }
            }
        }

        // Check if we found a path
        if (!previous.ContainsKey(end))
            return null;

        // Reconstruct path
        var path = new List<TaxiwaySegment>();
        var currentNode = end;
        while (previous.ContainsKey(currentNode))
        {
            var (prevNode, segment) = previous[currentNode];
            path.Insert(0, segment);
            currentNode = prevNode;
        }

        return path;
    }

    /// <summary>
    /// Finds path from start to end, constrained to pass through segments with the specified taxiway name
    /// </summary>
    /// <param name="start">Starting node</param>
    /// <param name="end">Destination node</param>
    /// <param name="requiredTaxiwayName">Taxiway name that path must include (or null for any path)</param>
    /// <returns>List of segments forming the path, or null if no path exists</returns>
    public List<TaxiwaySegment>? FindPathViaTaxiway(TaxiwayNode start, TaxiwayNode end, string? requiredTaxiwayName)
    {
        if (requiredTaxiwayName == null)
            return FindPath(start, end);

        // Find all segments on the required taxiway
        var taxiwaySegments = _segments.Where(s => s.Name == requiredTaxiwayName).ToList();
        if (taxiwaySegments.Count == 0)
            return null;

        // Find shortest path that goes through any segment on the taxiway
        List<TaxiwaySegment>? bestPath = null;
        double bestDistance = double.MaxValue;

        // Get all unique nodes on this taxiway
        var taxiwayNodes = new HashSet<TaxiwayNode>();
        foreach (var seg in taxiwaySegments)
        {
            taxiwayNodes.Add(seg.StartNode);
            taxiwayNodes.Add(seg.EndNode);
        }

        foreach (var taxiwayNode in taxiwayNodes)
        {
            // Path: start → taxiwayNode → end
            var pathToTaxiway = FindPath(start, taxiwayNode);
            var pathFromTaxiway = FindPath(taxiwayNode, end);

            if (pathToTaxiway != null && pathFromTaxiway != null)
            {
                double totalDist = pathToTaxiway.Sum(s => s.Length) + pathFromTaxiway.Sum(s => s.Length);
                if (totalDist < bestDistance)
                {
                    bestDistance = totalDist;
                    bestPath = pathToTaxiway.Concat(pathFromTaxiway).ToList();
                }
            }
        }

        return bestPath;
    }

    /// <summary>
    /// Gets all unique taxiway names in the graph, sorted alphabetically
    /// </summary>
    public List<string> GetAllTaxiwayNames()
    {
        return _segments
            .Where(s => s.HasName)
            .Select(s => s.Name!)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets all HoldShort nodes with their associated runway names
    /// </summary>
    public List<(TaxiwayNode Node, string RunwayName)> GetAllHoldShortNodes()
    {
        return _nodes.Values
            .Where(n => n.Type == TaxiwayNodeType.HoldShort && !string.IsNullOrEmpty(n.HoldShortRunway))
            .Select(n => (n, n.HoldShortRunway!))
            .OrderBy(x => x.Item2)
            .ToList();
    }

    /// <summary>
    /// Gets unique runway names from hold short nodes
    /// </summary>
    public List<string> GetAllRunwayNames()
    {
        return _nodes.Values
            .Where(n => n.Type == TaxiwayNodeType.HoldShort && !string.IsNullOrEmpty(n.HoldShortRunway))
            .Select(n => n.HoldShortRunway!)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>
    /// Gets all Parking nodes with their spot names
    /// </summary>
    public List<(TaxiwayNode Node, string SpotName, bool HasJetway)> GetAllParkingNodes()
    {
        return _nodes.Values
            .Where(n => n.Type == TaxiwayNodeType.Parking && !string.IsNullOrEmpty(n.ParkingSpotName))
            .Select(n => (n, n.ParkingSpotName!, n.HasJetway))
            .OrderBy(x => x.Item2)
            .ToList();
    }

    /// <summary>
    /// Finds all segments belonging to a specific taxiway
    /// </summary>
    public List<TaxiwaySegment> FindSegmentsOnTaxiway(string taxiwayName)
    {
        return _segments
            .Where(s => s.Name == taxiwayName)
            .ToList();
    }

    /// <summary>
    /// Finds the closest segment on a specific taxiway to the given position
    /// </summary>
    /// <param name="latitude">Aircraft latitude</param>
    /// <param name="longitude">Aircraft longitude</param>
    /// <param name="taxiwayName">Name of the taxiway to search</param>
    /// <returns>Closest segment on that taxiway and distance, or null if taxiway not found</returns>
    public (TaxiwaySegment Segment, double DistanceFeet, TaxiwayNode ClosestNode)? FindNearestSegmentOnTaxiway(
        double latitude, double longitude, string taxiwayName)
    {
        var taxiwaySegments = FindSegmentsOnTaxiway(taxiwayName);
        if (taxiwaySegments.Count == 0)
            return null;

        TaxiwaySegment? bestSegment = null;
        double bestDistance = double.MaxValue;
        TaxiwayNode? bestNode = null;

        foreach (var segment in taxiwaySegments)
        {
            // Calculate distance to segment
            double crossTrackNM = CalculateDistanceToSegment(latitude, longitude, segment);
            double distanceFeet = Math.Abs(crossTrackNM * NM_TO_FEET);

            if (distanceFeet < bestDistance)
            {
                bestDistance = distanceFeet;
                bestSegment = segment;

                // Determine which node is closer
                double distToStart = NavigationCalculator.CalculateDistance(
                    latitude, longitude, segment.StartNode.Latitude, segment.StartNode.Longitude) * NM_TO_FEET;
                double distToEnd = NavigationCalculator.CalculateDistance(
                    latitude, longitude, segment.EndNode.Latitude, segment.EndNode.Longitude) * NM_TO_FEET;

                bestNode = distToStart < distToEnd ? segment.StartNode : segment.EndNode;
            }
        }

        if (bestSegment == null || bestNode == null)
            return null;

        return (bestSegment, bestDistance, bestNode);
    }

    /// <summary>
    /// Finds the nearest hold short node for a specific runway
    /// </summary>
    public (TaxiwayNode Node, double DistanceFeet)? FindNearestHoldShort(
        double latitude, double longitude, string runwayName)
    {
        var holdShorts = GetAllHoldShortNodes()
            .Where(h => h.RunwayName == runwayName)
            .ToList();

        if (holdShorts.Count == 0)
            return null;

        TaxiwayNode? bestNode = null;
        double bestDistance = double.MaxValue;

        foreach (var (node, _) in holdShorts)
        {
            double dist = GetDistanceToNode(latitude, longitude, node);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestNode = node;
            }
        }

        if (bestNode == null)
            return null;

        return (bestNode, bestDistance);
    }

    /// <summary>
    /// Finds a parking node by name
    /// </summary>
    public TaxiwayNode? FindParkingByName(string spotName)
    {
        return _nodes.Values
            .FirstOrDefault(n => n.Type == TaxiwayNodeType.Parking &&
                                 n.ParkingSpotName == spotName);
    }

    /// <summary>
    /// Finds a hold short node by runway name (returns closest to runway threshold if multiple exist)
    /// </summary>
    public TaxiwayNode? FindHoldShortByRunway(string runwayName)
    {
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph] FindHoldShortByRunway('{runwayName}')");

        var holdShorts = _nodes.Values
            .Where(n => n.Type == TaxiwayNodeType.HoldShort &&
                        n.HoldShortRunway == runwayName)
            .ToList();

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   Found {holdShorts.Count} hold shorts for runway '{runwayName}'");

        if (holdShorts.Count == 0)
            return null;

        if (holdShorts.Count == 1)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   Single hold short at ({holdShorts[0].Latitude:F6}, {holdShorts[0].Longitude:F6})");
            return holdShorts[0];
        }

        // Multiple hold shorts with same name - find the one closest to the runway threshold
        var runwayEnd = _runwayEnds.FirstOrDefault(r => r.RunwayName == runwayName);
        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   Looking up runway end '{runwayName}' in _runwayEnds ({_runwayEnds.Count} total)");

        if (runwayEnd == default)
        {
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   WARNING: Runway end '{runwayName}' NOT FOUND - returning first hold short");
            return holdShorts.FirstOrDefault();
        }

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   Runway end '{runwayName}' at ({runwayEnd.Latitude:F6}, {runwayEnd.Longitude:F6})");

        // Log distances for each hold short
        foreach (var hs in holdShorts)
        {
            var dist = CalculateDistanceFeet(hs.Latitude, hs.Longitude, runwayEnd.Latitude, runwayEnd.Longitude);
            System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   Hold short ({hs.Latitude:F6}, {hs.Longitude:F6}) -> {dist:F0}ft from threshold");
        }

        // Return the hold short closest to the runway threshold (departure position)
        var selected = holdShorts
            .OrderBy(hs => CalculateDistanceFeet(hs.Latitude, hs.Longitude, runwayEnd.Latitude, runwayEnd.Longitude))
            .First();

        System.Diagnostics.Debug.WriteLine($"[TaxiwayGraph]   SELECTED: ({selected.Latitude:F6}, {selected.Longitude:F6})");
        return selected;
    }

    /// <summary>
    /// Checks if two taxiways are connected (share at least one node)
    /// </summary>
    public bool AreTaxiwaysConnected(string taxiway1, string taxiway2)
    {
        var nodes1 = new HashSet<TaxiwayNode>();
        var nodes2 = new HashSet<TaxiwayNode>();

        foreach (var seg in _segments)
        {
            if (seg.Name == taxiway1)
            {
                nodes1.Add(seg.StartNode);
                nodes1.Add(seg.EndNode);
            }
            if (seg.Name == taxiway2)
            {
                nodes2.Add(seg.StartNode);
                nodes2.Add(seg.EndNode);
            }
        }

        return nodes1.Overlaps(nodes2);
    }

    /// <summary>
    /// Gets all nodes that are on a specific taxiway
    /// </summary>
    public HashSet<TaxiwayNode> GetNodesOnTaxiway(string taxiwayName)
    {
        var nodes = new HashSet<TaxiwayNode>();
        foreach (var seg in _segments.Where(s => s.Name == taxiwayName))
        {
            nodes.Add(seg.StartNode);
            nodes.Add(seg.EndNode);
        }
        return nodes;
    }

    #endregion
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
