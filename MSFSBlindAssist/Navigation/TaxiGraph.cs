using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Represents an edge in the taxi graph connecting two nodes.
/// </summary>
public class TaxiEdge
{
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public double DistanceMeters { get; set; }
    public string TaxiwayName { get; set; } = "";
    public double BearingDegrees { get; set; }
    public double WidthFeet { get; set; }
    public string PathType { get; set; } = "";
}

/// <summary>
/// Builds and represents a taxi graph from navdatareader taxi_path data.
/// Each taxi_path row defines a centerline segment; endpoints within ~1m are merged into shared nodes.
/// </summary>
public class TaxiGraph
{
    // Merge threshold in METERS. Using a distance-based check keeps merging consistent
    // across latitudes — a degree-based check was asymmetric (NS vs EW) at high latitudes
    // like ENGM (60°N) / ENSB (78°N) and fragmented the graph at arctic airports.
    // 1.5 m is wide enough to absorb navdatareader coordinate rounding yet tight enough
    // to keep genuinely-separate endpoints apart (real taxi_path segments are >>5 m long).
    private const double MERGE_THRESHOLD_METERS = 1.5;
    private const int SPATIAL_HASH_PRECISION = 5; // decimal places for hash key

    public Dictionary<int, TaxiNode> Nodes { get; } = new();
    public Dictionary<int, List<TaxiEdge>> Adjacency { get; } = new();

    /// <summary>
    /// One physical runway as a centerline segment between its two thresholds,
    /// with a name for each direction. Built from the navdatareader `start`
    /// table at construction time. Used by DescribeLocation to detect
    /// "on runway X" mid-runway — the older edge-scan path required
    /// taxi_path.type starting with 'R', but no row in the navdatareader DB
    /// actually has that type, so without these pairs we could only report a
    /// runway when the aircraft was sitting within 50 m of a threshold node.
    /// </summary>
    public class RunwayCenterline
    {
        public double Lat1, Lon1;          // primary-end threshold
        public double Lat2, Lon2;          // opposite-end threshold
        public string Name1 = "";          // designator at primary end (e.g. "27L")
        public string Name2 = "";          // designator at opposite end (e.g. "09R")
        public double HeadingDeg1;         // heading from end 1 (0..360, true)
        public double HalfWidthMeters;     // centerline → edge tolerance
    }
    public List<RunwayCenterline> RunwayCenterlines { get; } = new();

    private int _nextNodeId = 0;
    private readonly Dictionary<string, List<int>> _spatialHash = new();
    private readonly Dictionary<string, List<int>> _taxiwayNodeIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the taxi graph asynchronously — useful at large airports where Build can take
    /// 200-500ms and would otherwise stall the UI thread. Runs on the thread pool.
    /// </summary>
    public static System.Threading.Tasks.Task<TaxiGraph> BuildAsync(
        List<TaxiPath> paths, List<ParkingSpot> parkingSpots, List<StartPosition> runwayStarts)
    {
        return System.Threading.Tasks.Task.Run(() => Build(paths, parkingSpots, runwayStarts));
    }

    /// <summary>
    /// Builds the taxi graph from raw taxi path data and parking spots.
    /// </summary>
    public static TaxiGraph Build(List<TaxiPath> paths, List<ParkingSpot> parkingSpots, List<StartPosition> runwayStarts)
    {
        var graph = new TaxiGraph();

        foreach (var path in paths)
        {
            // Defense-in-depth: trim here in case the path was constructed directly
            // (e.g. tests) bypassing the DB provider normalization.
            string name = path.Name?.Trim() ?? "";

            // Resolve start and end nodes (create if new, merge if close) — pass the
            // trimmed name so node.TaxiwayNames HashSet entries are canonical.
            int startNodeId = graph.ResolveNode(path.StartLat, path.StartLon, path.StartType, name);
            int endNodeId = graph.ResolveNode(path.EndLat, path.EndLon, path.EndType, name);

            if (startNodeId == endNodeId)
                continue; // degenerate segment

            // Calculate edge properties
            double distMeters = CalculateDistanceMeters(
                path.StartLat, path.StartLon, path.EndLat, path.EndLon);
            double bearing = NavigationCalculator.CalculateBearing(
                path.StartLat, path.StartLon, path.EndLat, path.EndLon);

            // Add bidirectional edges
            var fwdEdge = new TaxiEdge
            {
                FromNodeId = startNodeId,
                ToNodeId = endNodeId,
                DistanceMeters = distMeters,
                TaxiwayName = name,
                BearingDegrees = bearing,
                WidthFeet = path.Width,
                PathType = path.Type
            };
            var revEdge = new TaxiEdge
            {
                FromNodeId = endNodeId,
                ToNodeId = startNodeId,
                DistanceMeters = distMeters,
                TaxiwayName = name,
                BearingDegrees = (bearing + 180.0) % 360.0,
                WidthFeet = path.Width,
                PathType = path.Type
            };

            graph.AddEdge(fwdEdge);
            graph.AddEdge(revEdge);

            // Register taxiway name to nodes
            if (!string.IsNullOrEmpty(name))
            {
                graph.RegisterTaxiwayNode(name, startNodeId);
                graph.RegisterTaxiwayNode(name, endNodeId);
            }
        }

        // Mark parking nodes by matching to parking spot coordinates (spatial-hash FindNearestNode)
        foreach (var spot in parkingSpots)
        {
            var nearNode = graph.FindNearestNode(spot.Latitude, spot.Longitude);
            if (nearNode != null)
            {
                double dist = FastDistanceMeters(nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude);
                if (dist < 100) // within 100m
                {
                    nearNode.Type = TaxiNodeType.Parking;
                    string displayName = FormatParkingName(spot);
                    nearNode.ParkingName = displayName;
                }
            }
        }

        // Mark runway start nodes
        foreach (var start in runwayStarts)
        {
            var nearNode = graph.FindNearestNode(start.Latitude, start.Longitude);
            if (nearNode != null)
            {
                double dist = FastDistanceMeters(nearNode.Latitude, nearNode.Longitude, start.Latitude, start.Longitude);
                if (dist < 150)
                {
                    nearNode.ParkingName ??= $"Runway {start.RunwayName}";
                }
            }
        }

        // Build runway centerlines by pairing opposing runway-start positions.
        // Each physical runway has two `start` rows whose headings differ by 180°
        // (e.g. 27L heading ~270° pairs with 09R heading ~90°). We pair them up so
        // DescribeLocation can detect "on runway X" anywhere along the runway,
        // not just within 50 m of a threshold node — the previous edge-scan path
        // required taxi_path.type='R' which doesn't exist in the navdatareader DB.
        // Half-width defaults to 75 ft (≈23 m) when we can't infer it from a
        // nearby taxi_path edge — covers most Code C/D/E runways.
        const double DEFAULT_HALF_WIDTH_FT = 75.0;
        var paired = new HashSet<int>();
        for (int i = 0; i < runwayStarts.Count; i++)
        {
            if (paired.Contains(i)) continue;
            var a = runwayStarts[i];
            for (int j = i + 1; j < runwayStarts.Count; j++)
            {
                if (paired.Contains(j)) continue;
                var b = runwayStarts[j];
                // Reciprocal heading check (allow ±15° to absorb mag/true conventions).
                double hdgDelta = Math.Abs(NormalizeAngle(a.Heading - b.Heading + 180.0));
                if (hdgDelta > 15.0) continue;
                // Sanity: opposing thresholds should be 200 m – 6000 m apart
                // (smaller = same end, larger = different runway pair).
                double sep = FastDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
                if (sep < 200.0 || sep > 6000.0) continue;

                graph.RunwayCenterlines.Add(new RunwayCenterline
                {
                    Lat1 = a.Latitude, Lon1 = a.Longitude,
                    Lat2 = b.Latitude, Lon2 = b.Longitude,
                    Name1 = a.RunwayName,
                    Name2 = b.RunwayName,
                    HeadingDeg1 = a.Heading,
                    HalfWidthMeters = (DEFAULT_HALF_WIDTH_FT * 0.3048),
                });
                paired.Add(i);
                paired.Add(j);
                break;
            }
        }

        // Assign hold-short names using the node's taxiway designator and nearest runway.
        // The taxiway name on the hold-short node IS the holding point designator (e.g. NB1, A5).
        // The nearest runway tells us what we're holding short OF.
        foreach (var node in graph.Nodes.Values)
        {
            if (node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort)
            {
                // Find nearest runway for "holding short of" context
                string? nearestRunway = null;
                double nearestDist = double.MaxValue;

                foreach (var start in runwayStarts)
                {
                    double dist = FastDistanceMeters(node.Latitude, node.Longitude, start.Latitude, start.Longitude);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestRunway = start.RunwayName;
                    }
                }

                // The hold point's own name comes from its taxiway edges.
                // Prefer connector-style designators (A5, NB1, K12) over plain
                // main taxiway names (A, B, K) because the connector name is
                // the actual *holding point* designator pilots use with ATC.
                //
                // Ranking (best first):
                //   1. Name contains BOTH a letter and a digit (e.g., "A5", "NB1", "K12")
                //   2. Any non-empty name (longest-first — gives "MAIN" over "A")
                //   3. Fallback: empty
                string holdPointName = "";
                if (graph.Adjacency.TryGetValue(node.NodeId, out var edges))
                {
                    var distinctNames = new HashSet<string>();
                    foreach (var edge in edges)
                    {
                        if (!string.IsNullOrEmpty(edge.TaxiwayName))
                            distinctNames.Add(edge.TaxiwayName);
                    }

                    // Tier 1: names that look like connector/holding-point designators
                    string? connectorName = null;
                    foreach (var n in distinctNames)
                    {
                        bool hasLetter = false, hasDigit = false;
                        foreach (char c in n)
                        {
                            if (char.IsLetter(c)) hasLetter = true;
                            else if (char.IsDigit(c)) hasDigit = true;
                        }
                        if (hasLetter && hasDigit)
                        {
                            // Among connectors, prefer the shortest (A5 > KILO5A);
                            // ties broken by alpha order for determinism.
                            if (connectorName == null ||
                                n.Length < connectorName.Length ||
                                (n.Length == connectorName.Length &&
                                 string.Compare(n, connectorName, StringComparison.Ordinal) < 0))
                            {
                                connectorName = n;
                            }
                        }
                    }

                    if (connectorName != null)
                    {
                        holdPointName = connectorName;
                    }
                    else if (distinctNames.Count > 0)
                    {
                        // No connector pattern — fall back to any name.
                        // Prefer longer over single-letter (more specific).
                        string? best = null;
                        foreach (var n in distinctNames)
                        {
                            if (best == null ||
                                n.Length > best.Length ||
                                (n.Length == best.Length &&
                                 string.Compare(n, best, StringComparison.Ordinal) < 0))
                            {
                                best = n;
                            }
                        }
                        holdPointName = best ?? "";
                    }
                }

                if (nearestRunway != null && nearestDist < 500)
                {
                    if (!string.IsNullOrEmpty(holdPointName))
                        node.HoldShortName = $"{holdPointName}, Runway {nearestRunway}";
                    else
                        node.HoldShortName = $"Runway {nearestRunway}";
                }
                else if (!string.IsNullOrEmpty(holdPointName))
                {
                    node.HoldShortName = holdPointName;
                }
            }
        }

        return graph;
    }

    /// <summary>
    /// Resolves a coordinate to an existing node (if within merge threshold) or creates a new one.
    /// </summary>
    private int ResolveNode(double lat, double lon, string nodeType, string? taxiwayName)
    {
        string hashKey = GetSpatialHashKey(lat, lon);

        // Check nearby cells for existing nodes within merge threshold
        foreach (var key in GetNearbyCellKeys(lat, lon))
        {
            if (_spatialHash.TryGetValue(key, out var nodeIds))
            {
                foreach (int nodeId in nodeIds)
                {
                    var existing = Nodes[nodeId];
                    // Distance-based merge check — consistent NS and EW at any latitude.
                    if (FastDistanceMeters(existing.Latitude, existing.Longitude, lat, lon) < MERGE_THRESHOLD_METERS)
                    {
                        // Upgrade node type if this endpoint is hold-short
                        UpgradeNodeType(existing, nodeType);
                        if (!string.IsNullOrEmpty(taxiwayName))
                            existing.TaxiwayNames.Add(taxiwayName);
                        return nodeId;
                    }
                }
            }
        }

        // Create new node
        int newId = _nextNodeId++;
        var newNode = new TaxiNode
        {
            NodeId = newId,
            Latitude = lat,
            Longitude = lon,
            Type = MapNodeType(nodeType)
        };
        if (!string.IsNullOrEmpty(taxiwayName))
            newNode.TaxiwayNames.Add(taxiwayName);

        Nodes[newId] = newNode;
        Adjacency[newId] = new List<TaxiEdge>();

        if (!_spatialHash.ContainsKey(hashKey))
            _spatialHash[hashKey] = new List<int>();
        _spatialHash[hashKey].Add(newId);

        return newId;
    }

    private void AddEdge(TaxiEdge edge)
    {
        if (!Adjacency.ContainsKey(edge.FromNodeId))
            Adjacency[edge.FromNodeId] = new List<TaxiEdge>();

        // Avoid duplicate edges
        bool exists = false;
        foreach (var e in Adjacency[edge.FromNodeId])
        {
            if (e.ToNodeId == edge.ToNodeId && e.TaxiwayName == edge.TaxiwayName)
            {
                exists = true;
                break;
            }
        }
        if (!exists)
            Adjacency[edge.FromNodeId].Add(edge);
    }

    private void RegisterTaxiwayNode(string taxiwayName, int nodeId)
    {
        if (!_taxiwayNodeIndex.ContainsKey(taxiwayName))
            _taxiwayNodeIndex[taxiwayName] = new List<int>();
        if (!_taxiwayNodeIndex[taxiwayName].Contains(nodeId))
            _taxiwayNodeIndex[taxiwayName].Add(nodeId);
    }

    /// <summary>
    /// Finds the nearest graph node to a given position.
    /// Uses the spatial hash for fast local lookup (expanding ring if needed),
    /// falling back to a full scan only if no node is found within ~1km.
    /// </summary>
    public TaxiNode? FindNearestNode(double lat, double lon)
    {
        // Fast path: search the spatial hash with an expanding ring of cells.
        // Precision 5 = ~1.1m cells at equator. Rings 1, 3, 10, 30 cover up to ~330m cheaply.
        foreach (int ringRadius in new[] { 1, 3, 10, 30 })
        {
            TaxiNode? best = null;
            double bestDist = double.MaxValue;

            double step = Math.Pow(10, -SPATIAL_HASH_PRECISION);
            for (int dlat = -ringRadius; dlat <= ringRadius; dlat++)
            {
                for (int dlon = -ringRadius; dlon <= ringRadius; dlon++)
                {
                    string key = GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
                    if (_spatialHash.TryGetValue(key, out var nodeIds))
                    {
                        foreach (int nodeId in nodeIds)
                        {
                            var node = Nodes[nodeId];
                            double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                best = node;
                            }
                        }
                    }
                }
            }
            if (best != null) return best;
        }

        // Fallback: full scan (rare — only hit for coordinates outside all airport nodes)
        TaxiNode? fallback = null;
        double fallbackDist = double.MaxValue;
        foreach (var node in Nodes.Values)
        {
            double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (dist < fallbackDist)
            {
                fallbackDist = dist;
                fallback = node;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Finds the nearest graph node in the direction the aircraft is facing.
    /// Returns the closest node that is roughly ahead (within ±90 degrees of heading).
    /// Bounded by MAX_START_NODE_DISTANCE_M — if nothing ahead is within range,
    /// falls back to the overall nearest node (also distance-bounded), otherwise returns null.
    /// A null return means "no taxiway node near this position" — caller should
    /// report "no nearby taxiway" rather than silently snap to something far away.
    /// </summary>
    /// <summary>
    /// Returns the nearest graph node that has the given taxiway in its
    /// TaxiwayNames set, ignoring aircraft heading. Used when the pilot has
    /// specified a constrained taxiway sequence — even if the aircraft is
    /// pointed AWAY from the first taxiway after pushback, we want the
    /// route to start on that taxiway. The pilot will turn after pushback;
    /// route plotting shouldn't reject the taxiway because of the current
    /// transient heading. Returns null if no node within `maxDistanceM` has
    /// the named taxiway, in which case caller should fall back to the
    /// heading-aware FindNearestNodeInDirection.
    /// </summary>
    public TaxiNode? FindNearestNodeOnTaxiway(double lat, double lon, string taxiwayName, double maxDistanceM = 800.0)
    {
        if (string.IsNullOrEmpty(taxiwayName)) return null;

        TaxiNode? best = null;
        double bestDist = maxDistanceM;

        foreach (var node in Nodes.Values)
        {
            if (!node.TaxiwayNames.Contains(taxiwayName)) continue;
            double d = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (d < bestDist)
            {
                bestDist = d;
                best = node;
            }
        }

        return best;
    }

    public TaxiNode? FindNearestNodeInDirection(double lat, double lon, double headingDeg)
    {
        // Tiered caps: prefer ahead-of-aircraft nodes within 300m (the common case at
        // a gate pushback), widen to 800m before giving up. Small airports with a large
        // apron between parking and the taxi network routinely produce >300m gaps; 800m
        // still rejects "you're flying over the field" cases while keeping small strips usable.
        const double PREFERRED_DISTANCE_M = 300.0;
        const double MAX_START_NODE_DISTANCE_M = 800.0;

        TaxiNode? preferred = null;    double preferredScore = double.MaxValue;
        TaxiNode? extended = null;     double extendedScore = double.MaxValue;

        foreach (var node in Nodes.Values)
        {
            double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);
            if (dist < 5) continue;                         // skip nodes right under us
            if (dist > MAX_START_NODE_DISTANCE_M) continue; // too far to be "at" the airport

            double bearing = NavigationCalculator.CalculateBearing(lat, lon, node.Latitude, node.Longitude);
            double angleDiff = Math.Abs(NormalizeAngle(bearing - headingDeg));

            if (angleDiff > 90) continue; // behind us

            // Score: prioritize closeness, with slight penalty for off-heading
            double score = dist + (angleDiff * 0.5);

            if (dist <= PREFERRED_DISTANCE_M)
            {
                if (score < preferredScore)
                {
                    preferredScore = score;
                    preferred = node;
                }
            }
            else if (score < extendedScore)
            {
                extendedScore = score;
                extended = node;
            }
        }

        if (preferred != null) return preferred;
        if (extended != null) return extended;

        // Nothing ahead — try the overall nearest, but only if within the extended range
        var fallback = FindNearestNode(lat, lon);
        if (fallback == null) return null;
        double fallbackDist = FastDistanceMeters(lat, lon, fallback.Latitude, fallback.Longitude);
        return fallbackDist <= MAX_START_NODE_DISTANCE_M ? fallback : null;
    }

    /// <summary>
    /// Describes what the aircraft is currently on: "Gate A25", "Runway 22L",
    /// "Taxiway Bravo", or "" if nothing plausible is nearby.
    ///
    /// Priority order (more specific wins):
    ///   1. Parking node within 40 m (gate).
    ///   2. Runway edge within half-width+5 m perpendicular distance (on the runway surface).
    ///      Runway edges are those with PathType indicating a runway (first char 'R').
    ///   3. Runway threshold node within 50 m (near a runway start).
    ///   4. Taxiway edge within half-width+3 m perpendicular distance (on a named taxiway).
    ///   5. Nearest node's first taxiway name as a fallback (within 60 m).
    ///
    /// This does NOT depend on guidance being active — it's a pure query against the graph.
    /// Caller is responsible for prepending " at ICAO" if desired.
    /// </summary>
    public string DescribeLocation(double lat, double lon)
    {
        const double PARKING_RADIUS_M = 40.0;
        const double RUNWAY_THRESHOLD_RADIUS_M = 50.0;
        const double NODE_FALLBACK_RADIUS_M = 60.0;
        const double EDGE_SCAN_RADIUS_M = 120.0; // generous — runway surfaces are wide

        // --- Pass 1: scan nearby nodes via spatial hash ---
        TaxiNode? nearestParking = null;      double nearestParkingDist = double.MaxValue;
        TaxiNode? nearestRunwayThreshold = null; double nearestRunwayDist = double.MaxValue;
        TaxiNode? nearestAnyNode = null;      double nearestAnyDist = double.MaxValue;

        double step = Math.Pow(10, -SPATIAL_HASH_PRECISION);
        // ring radius 30 cells ~= 330 m at equator — covers EDGE_SCAN_RADIUS_M comfortably
        for (int dlat = -30; dlat <= 30; dlat++)
        {
            for (int dlon = -30; dlon <= 30; dlon++)
            {
                string key = GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
                if (!_spatialHash.TryGetValue(key, out var nodeIds)) continue;

                foreach (int nodeId in nodeIds)
                {
                    var node = Nodes[nodeId];
                    double dist = FastDistanceMeters(lat, lon, node.Latitude, node.Longitude);

                    if (dist < nearestAnyDist)
                    {
                        nearestAnyDist = dist;
                        nearestAnyNode = node;
                    }

                    if (node.Type == TaxiNodeType.Parking &&
                        !string.IsNullOrEmpty(node.ParkingName) &&
                        !node.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase) &&
                        dist < nearestParkingDist)
                    {
                        nearestParkingDist = dist;
                        nearestParking = node;
                    }

                    if (!string.IsNullOrEmpty(node.ParkingName) &&
                        node.ParkingName.StartsWith("Runway", StringComparison.OrdinalIgnoreCase) &&
                        dist < nearestRunwayDist)
                    {
                        nearestRunwayDist = dist;
                        nearestRunwayThreshold = node;
                    }
                }
            }
        }

        // Gate wins if close enough
        if (nearestParking != null && nearestParkingDist <= PARKING_RADIUS_M)
        {
            string raw = nearestParking.ParkingName!.Trim();
            // Parking name may already be "Gate A25" or just "A25" / "G 10" — prefix "Gate" once
            if (raw.StartsWith("Gate", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Parking", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Ramp", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Tie", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("Hangar", StringComparison.OrdinalIgnoreCase))
                return raw;
            return $"Gate {raw}";
        }

        // --- Pass 2: edge scan for on-runway / on-taxiway ---
        TaxiEdge? bestRunwayEdge = null;  double bestRunwayEdgePerp = double.MaxValue;
        TaxiEdge? bestTaxiwayEdge = null; double bestTaxiwayEdgePerp = double.MaxValue;

        // Collect candidate edges from adjacency of all nodes within EDGE_SCAN_RADIUS_M.
        // We dedupe by (from,to) pair since adjacency holds both directions.
        var visitedEdges = new HashSet<long>();

        foreach (var kvp in Adjacency)
        {
            int fromId = kvp.Key;
            var fromNode = Nodes[fromId];
            double fromDist = FastDistanceMeters(lat, lon, fromNode.Latitude, fromNode.Longitude);
            if (fromDist > EDGE_SCAN_RADIUS_M) continue;

            foreach (var edge in kvp.Value)
            {
                // Dedupe
                long edgeKey = Math.Min(edge.FromNodeId, edge.ToNodeId) * 1_000_000L + Math.Max(edge.FromNodeId, edge.ToNodeId);
                if (!visitedEdges.Add(edgeKey)) continue;

                var toNode = Nodes[edge.ToNodeId];
                double perp = PerpendicularDistanceMeters(
                    lat, lon,
                    fromNode.Latitude, fromNode.Longitude,
                    toNode.Latitude, toNode.Longitude);

                // Half-width in meters (width stored as feet)
                double halfWidthM = (edge.WidthFeet * 0.3048) * 0.5;

                bool isRunway = !string.IsNullOrEmpty(edge.PathType) &&
                                edge.PathType.StartsWith("R", StringComparison.OrdinalIgnoreCase);

                double tolerance = isRunway ? halfWidthM + 5.0 : halfWidthM + 3.0;
                if (tolerance < 5.0) tolerance = 5.0; // minimum tolerance if width is missing/zero

                if (perp <= tolerance)
                {
                    if (isRunway)
                    {
                        if (perp < bestRunwayEdgePerp)
                        {
                            bestRunwayEdgePerp = perp;
                            bestRunwayEdge = edge;
                        }
                    }
                    else if (!string.IsNullOrEmpty(edge.TaxiwayName))
                    {
                        if (perp < bestTaxiwayEdgePerp)
                        {
                            bestTaxiwayEdgePerp = perp;
                            bestTaxiwayEdge = edge;
                        }
                    }
                }
            }
        }

        // On runway surface (graph-edge path) wins over near-threshold-node.
        // NOTE: this branch only fires for DBs that store runway centerlines as
        // taxi_path.type='R' rows. The current navdatareader schema does NOT —
        // every taxi_path row is type T / PT / P. The runway-centerline scan
        // below covers the common case using start-table threshold pairs.
        if (bestRunwayEdge != null && !string.IsNullOrEmpty(bestRunwayEdge.TaxiwayName))
            return $"Runway {bestRunwayEdge.TaxiwayName}";

        // Runway centerline scan (works for the whole length, not just the
        // thresholds). Each RunwayCenterline is the segment between the two
        // opposing-end thresholds; if the aircraft is within half-width-plus-
        // a-bit of that segment, it's on the runway. Pick the runway end whose
        // heading is closer to the aircraft's bearing along the centerline so
        // we report the correct designator (27L vs 09R).
        foreach (var rwy in RunwayCenterlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            double tolerance = rwy.HalfWidthMeters + 5.0;
            if (perp > tolerance) continue;

            // Pick the directional name. For a stationary aircraft we can't use
            // its heading, so default to the end the aircraft is closer to:
            // it'll be lined up to take off in that direction. (For a rolling
            // aircraft this same convention happens to match the takeoff end.)
            double d1 = FastDistanceMeters(lat, lon, rwy.Lat1, rwy.Lon1);
            double d2 = FastDistanceMeters(lat, lon, rwy.Lat2, rwy.Lon2);
            string name = d1 <= d2 ? rwy.Name1 : rwy.Name2;
            if (string.IsNullOrEmpty(name)) name = rwy.Name1;
            return $"Runway {name}";
        }

        // Otherwise if we're near a runway threshold node
        if (nearestRunwayThreshold != null && nearestRunwayDist <= RUNWAY_THRESHOLD_RADIUS_M &&
            !string.IsNullOrEmpty(nearestRunwayThreshold.ParkingName))
            return nearestRunwayThreshold.ParkingName; // already "Runway 22L"

        // On taxiway
        if (bestTaxiwayEdge != null && !string.IsNullOrEmpty(bestTaxiwayEdge.TaxiwayName))
            return $"Taxiway {bestTaxiwayEdge.TaxiwayName}";

        // Fallback: nearest node's first taxiway name
        if (nearestAnyNode != null && nearestAnyDist <= NODE_FALLBACK_RADIUS_M &&
            nearestAnyNode.TaxiwayNames.Count > 0)
        {
            string name = nearestAnyNode.TaxiwayNames.First();
            return $"Near taxiway {name}";
        }

        return "";
    }

    /// <summary>
    /// Perpendicular distance (meters) from point (plat, plon) to segment (a→b).
    /// Uses equirectangular projection — accurate for taxiway-scale distances.
    /// Returns the distance to the nearest point on the segment (not the infinite line),
    /// so endpoints count when the foot of the perpendicular falls outside.
    /// </summary>
    /// <summary>
    /// Public wrapper for the internal perpendicular-distance calculation, so
    /// other components (e.g. TaxiGuidanceManager.WhichRunwayContains) can do
    /// runway-pavement membership tests without duplicating the projection math.
    /// </summary>
    public static double PerpendicularDistanceMetersStatic(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
        => PerpendicularDistanceMeters(plat, plon, alat, alon, blat, blon);

    private static double PerpendicularDistanceMeters(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (alat + blat) * 0.5 * (Math.PI / 180.0);
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);

        double ax = 0.0, ay = 0.0;
        double bx = (blon - alon) * metersPerDegLon;
        double by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon;
        double py = (plat - alat) * METERS_PER_DEG_LAT;

        double dx = bx - ax;
        double dy = by - ay;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9)
            return Math.Sqrt(px * px + py * py);

        double t = (px * dx + py * dy) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        double fx = t * dx;
        double fy = t * dy;
        double ex = px - fx;
        double ey = py - fy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    /// <summary>
    /// Gets all unique taxiway names in the graph, sorted.
    /// </summary>
    public List<string> GetAllTaxiwayNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edges in Adjacency.Values)
        {
            foreach (var edge in edges)
            {
                if (!string.IsNullOrEmpty(edge.TaxiwayName))
                    names.Add(edge.TaxiwayName);
            }
        }
        var sorted = names.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    /// <summary>
    /// Gets taxiway names sorted by distance from a given position, closest first.
    /// Only returns taxiways that have at least one node within the direction the aircraft is facing.
    /// </summary>
    public List<string> GetTaxiwayNamesSortedByDistance(double lat, double lon, double headingDeg)
    {
        var taxiwayDistances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _taxiwayNodeIndex)
        {
            string name = kvp.Key;
            double bestDist = double.MaxValue;

            foreach (int nodeId in kvp.Value)
            {
                var node = Nodes[nodeId];
                double dist = CalculateDistanceMeters(lat, lon, node.Latitude, node.Longitude);
                if (dist < bestDist)
                    bestDist = dist;
            }

            taxiwayDistances[name] = bestDist;
        }

        // Sort by distance
        return taxiwayDistances
            .OrderBy(kvp => kvp.Value)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Gets the closest taxiway name in the direction the aircraft is facing.
    /// Considers both distance and heading alignment.
    /// </summary>
    public string? GetClosestTaxiwayInDirection(double lat, double lon, double headingDeg)
    {
        string? bestName = null;
        double bestScore = double.MaxValue;

        foreach (var kvp in _taxiwayNodeIndex)
        {
            string name = kvp.Key;

            foreach (int nodeId in kvp.Value)
            {
                var node = Nodes[nodeId];
                double dist = CalculateDistanceMeters(lat, lon, node.Latitude, node.Longitude);
                if (dist < 10) continue; // too close, probably under us

                double bearing = NavigationCalculator.CalculateBearing(lat, lon, node.Latitude, node.Longitude);
                double angleDiff = Math.Abs(NormalizeAngle(bearing - headingDeg));

                if (angleDiff > 90) continue; // behind us

                // Weighted score: distance + angle penalty
                double score = dist + (angleDiff * 2.0);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestName = name;
                }
            }
        }

        return bestName;
    }

    /// <summary>
    /// Gets taxiway names that connect to a given taxiway (within a small number
    /// of named-taxiway crossings, freely traversing along the seed and unnamed
    /// connectors).
    /// </summary>
    public List<string> GetConnectedTaxiwayNames(string taxiwayName)
    {
        // 2 named-taxiway crossings is the right granularity for surfacing
        // adjacent taxiways: it catches direct neighbors (1 crossing) and
        // one step further (e.g., a parallel taxiway reached via an
        // intermediate connector taxiway). The router does the heavy lifting
        // for actual path-finding; this is just to prioritize relevant items
        // in the dropdown UI.
        return GetReachableTaxiwayNames(taxiwayName, maxCrossings: 2);
    }

    /// <summary>
    /// Returns named taxiways reachable from any node on <paramref name="taxiwayName"/>
    /// within at most <paramref name="maxCrossings"/> NAMED-TAXIWAY transitions.
    /// Walking along the seed taxiway and through unnamed connector edges is
    /// FREE (does not consume the crossing budget). Only crossing into a
    /// different named taxiway counts as a crossing.
    ///
    /// This matches how ATC clearances read: a clearance like "M5 M1 A L" is
    /// 3 crossings end-to-end. Each step of the clearance corresponds to one
    /// crossing in this metric, regardless of how many unnamed connector
    /// segments physically lie between the two named taxiways. Counting raw
    /// graph hops (the previous behavior) silently hid taxiways like M1 from
    /// the M5 dropdown at KSFO, where M5 and M1 are connected via 4-6
    /// unnamed connector segments rather than sharing a node.
    /// </summary>
    public List<string> GetReachableTaxiwayNames(string taxiwayName, int maxCrossings = 2)
    {
        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_taxiwayNodeIndex.TryGetValue(taxiwayName, out var seedNodes))
            return new List<string>();

        // BFS state: (nodeId, currentNamedTaxiway, crossingsUsed). The
        // currentNamedTaxiway carries the context of which named entity we're
        // logically on — staying on it (or walking unnamed connectors that
        // don't "name" anything) is free; only crossing INTO a different
        // named taxiway counts. visited is keyed on (nodeId, currentTaxiway)
        // so an intersection node can be revisited from different taxiway
        // contexts (each context may yield different reachability), bounded
        // by the small number of named taxiways crossing any one node.
        var visited = new HashSet<(int, string)>();
        var queue = new Queue<(int nodeId, string currentTaxiway, int crossings)>();
        foreach (int n in seedNodes)
        {
            if (visited.Add((n, taxiwayName)))
                queue.Enqueue((n, taxiwayName, 0));
        }

        while (queue.Count > 0)
        {
            var (current, currentTw, crossings) = queue.Dequeue();
            if (!Adjacency.TryGetValue(current, out var edges)) continue;

            foreach (var edge in edges)
            {
                string edgeName = edge.TaxiwayName ?? string.Empty;
                int nextCrossings;
                string nextTw;

                if (string.IsNullOrEmpty(edgeName))
                {
                    // Unnamed connector — stay logically on the current
                    // named taxiway and don't consume a crossing.
                    nextTw = currentTw;
                    nextCrossings = crossings;
                }
                else if (edgeName.Equals(currentTw, StringComparison.OrdinalIgnoreCase))
                {
                    // Walking along the same named taxiway — free.
                    nextTw = currentTw;
                    nextCrossings = crossings;
                }
                else
                {
                    // Crossing into a different named taxiway.
                    nextTw = edgeName;
                    nextCrossings = crossings + 1;
                }

                // Bound check applies to BOTH the enqueue and the collection
                // step: a name reached at nextCrossings > maxCrossings is
                // beyond the budget and must not appear in the result. (The
                // earlier version added the name BEFORE the bound check,
                // which silently widened the dropdown by one extra crossing
                // — a name at distance 3 would surface for maxCrossings=2.)
                if (nextCrossings > maxCrossings) continue;

                // Only collect distinct named taxiways (skip unnamed connectors
                // and skip self-references back to the seed).
                if (!string.IsNullOrEmpty(edgeName) &&
                    !edgeName.Equals(taxiwayName, StringComparison.OrdinalIgnoreCase) &&
                    !edgeName.Equals(currentTw, StringComparison.OrdinalIgnoreCase))
                {
                    collected.Add(edgeName);
                }

                if (!visited.Add((edge.ToNodeId, nextTw))) continue;
                queue.Enqueue((edge.ToNodeId, nextTw, nextCrossings));
            }
        }

        return collected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Gets the edge between two adjacent nodes on a specific taxiway, or any edge if taxiway is empty.
    /// </summary>
    public TaxiEdge? GetEdge(int fromNodeId, int toNodeId, string? preferredTaxiway = null)
    {
        if (!Adjacency.TryGetValue(fromNodeId, out var edges))
            return null;

        TaxiEdge? fallback = null;
        foreach (var edge in edges)
        {
            if (edge.ToNodeId != toNodeId) continue;
            if (!string.IsNullOrEmpty(preferredTaxiway) &&
                edge.TaxiwayName.Equals(preferredTaxiway, StringComparison.OrdinalIgnoreCase))
                return edge;
            fallback ??= edge;
        }
        return fallback;
    }

    #region Helpers

    private static string GetSpatialHashKey(double lat, double lon)
    {
        return $"{Math.Round(lat, SPATIAL_HASH_PRECISION)},{Math.Round(lon, SPATIAL_HASH_PRECISION)}";
    }

    private static IEnumerable<string> GetNearbyCellKeys(double lat, double lon)
    {
        double step = Math.Pow(10, -SPATIAL_HASH_PRECISION);
        for (int dlat = -1; dlat <= 1; dlat++)
        {
            for (int dlon = -1; dlon <= 1; dlon++)
            {
                yield return GetSpatialHashKey(lat + dlat * step, lon + dlon * step);
            }
        }
    }

    private static void UpgradeNodeType(TaxiNode node, string typeCode)
    {
        var newType = MapNodeType(typeCode);
        // Only upgrade: Normal → HoldShort → ILSHoldShort
        if (newType > node.Type)
            node.Type = newType;
    }

    private static TaxiNodeType MapNodeType(string typeCode)
    {
        return typeCode?.ToUpperInvariant() switch
        {
            "HS" or "HSND" => TaxiNodeType.HoldShort,
            "IHS" or "IHSND" => TaxiNodeType.ILSHoldShort,
            "P" => TaxiNodeType.Parking,
            _ => TaxiNodeType.Normal
        };
    }

    public static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        return NavigationCalculator.CalculateDistance(lat1, lon1, lat2, lon2) * 1852.0; // NM to meters
    }

    /// <summary>
    /// Fast equirectangular distance approximation in meters — accurate to &lt;1 cm at taxiway
    /// scale (&lt; a few km), ~2 orders of magnitude cheaper than Haversine. Use in per-frame hot
    /// paths (e.g. segment advance, look-ahead, incursion scan). Do NOT use for long distances.
    /// </summary>
    public static double FastDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latRad = (lat1 + lat2) * 0.5 * (Math.PI / 180.0);
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latRad);
        double dLat = (lat2 - lat1) * METERS_PER_DEG_LAT;
        double dLon = (lon2 - lon1) * metersPerDegLon;
        return Math.Sqrt(dLat * dLat + dLon * dLon);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    /// <summary>
    /// Formats a parking spot display name from its name, number, and suffix.
    /// </summary>
    public static string FormatParkingDisplayName(ParkingSpot spot)
    {
        string name = spot.Name;
        if (spot.Number > 0)
            name += $" {spot.Number}";
        if (!string.IsNullOrEmpty(spot.Suffix))
            name += spot.Suffix;
        return name.Trim();
    }

    private static string FormatParkingName(ParkingSpot spot) => FormatParkingDisplayName(spot);

    #endregion

    #region Landing Exit Planning

    /// <summary>
    /// Finds usable runway exit taxiways for the given landing runway. Projects every
    /// hold-short and ILS hold-short node onto the runway centerline; any node that
    /// lies within the runway footprint (half-width + 15 m lateral buffer) and between
    /// a minimum along-runway distance and the runway end is considered an exit.
    ///
    /// Returned list is sorted by along-runway distance from the landing threshold (nearest first).
    /// Useful exits typically start 1500 ft past the threshold (jet touchdown zone) and
    /// end ~500 ft before the runway end.
    ///
    /// "Threshold" = rwy.StartLat/Lon (primary end of the Runway record, which is the
    /// landing threshold for this runway direction — the DB returns one Runway per
    /// direction, each with its own StartLat representing its own threshold).
    /// </summary>
    public List<LandingExit> GetLandingExits(Runway rwy)
    {
        var exits = new List<LandingExit>();
        if (rwy == null || rwy.Length <= 0) return exits;

        // Runway axis — use true heading (rwy.Heading is true in the DB model).
        double rwyHeadingTrue = rwy.Heading;
        double cosH = Math.Cos(rwyHeadingTrue * Math.PI / 180.0);
        double sinH = Math.Sin(rwyHeadingTrue * Math.PI / 180.0);

        // Lateral tolerance: runway half-width + a buffer, because hold-short nodes
        // are usually painted a few meters inside the runway edge stripe. If the DB
        // lacks width (Width==0), fall back to 75 ft (23 m half-width → 60 ft total
        // which covers most Code C/D taxiway-runway intersections).
        double halfWidthFt = rwy.Width > 0 ? rwy.Width * 0.5 : 75.0;
        double lateralToleranceM = (halfWidthFt * 0.3048) + 15.0;

        double lengthM = rwy.Length * 0.3048;

        // Displaced threshold handling. rwy.ThresholdOffset is the distance (feet)
        // from the physical runway end (rwy.StartLat/Lon) to the painted landing
        // threshold. Pilots land past the displaced threshold — the aim point, the
        // touchdown zone markings, and therefore the "usable exit" math must all
        // be measured from the LANDING threshold, not the physical pavement end.
        // KJFK 13R has a ~2055 ft displaced threshold; KJFK 22R has ~3438 ft; EGLL
        // 27R has ~1004 ft. Without this, a "2000 ft from touchdown" exit at 13R
        // would actually be ~3000 ft before the touchdown aim point (i.e. behind
        // the aircraft) — unsafe and wrong. ~5,500 runway ends in a typical
        // navdatareader DB have non-zero offset; this matters at every major hub.
        double landingThresholdOffsetFt = rwy.ThresholdOffset;

        // Cutoffs: usable exits lie past the jet touchdown zone and before the runway end.
        // MIN_DIST_FT is a conservative floor (still captures very-early RETs at some
        // airports and "reject take-off" spots; also avoids false positives from
        // threshold hold-short lines). END_BUFFER_FT cuts off the last stretch so we
        // don't list turnoffs that require full-length backtrack.
        const double MIN_DIST_FT = 500.0;
        const double END_BUFFER_FT = 200.0;
        const double TOUCHDOWN_AIM_FT = 1000.0;  // typical jet aim point past landing threshold

        // Classification thresholds (angle between exit edge and runway axis).
        const double HIGH_SPEED_MAX_DEG = 50.0;   // RET geometry (≤50° off runway axis)
        const double NORMAL_MAX_DEG     = 110.0;  // beyond this → End
        const double END_RATIO          = 0.85;   // last 15% of runway → always End

        // Dedup window: exits within this along-runway distance that share a
        // taxiway name are collapsed to a single entry.
        const double DEDUP_WINDOW_FT = 50.0;

        double maxDistFt = rwy.Length - END_BUFFER_FT;
        if (maxDistFt < MIN_DIST_FT) return exits;

        // Universal fallback: many runways in real-world navdatareader DBs have
        // no HoldShort / ILSHoldShort nodes recorded at all — small airports,
        // renumbered runways, certain third-party scenery, and (notably) every
        // runway whose taxi_path rows just don't carry the HS/IHS markers.
        // Without a fallback, GetLandingExits returns an empty list for those
        // runways and a blind pilot has no way to plan an exit.
        //
        // Earlier versions of this fallback gated on "the node has a runway-type
        // edge (PathType starts with R)" — but in the schema this app actually
        // ships against, NO taxi_path row has PathType == "R" (runway centerlines
        // live in the separate runway/runway_end tables, not in taxi_path). So
        // that gate was dead code. The geometric fallback below is independent of
        // PathType labels: it finds Normal nodes that lie on the runway axis AND
        // have at least one named-taxiway edge that turns meaningfully off the
        // runway (≥ MIN_FALLBACK_EXIT_ANGLE_DEG). That excludes parallel taxiway
        // nodes (which lie close to the axis but only have edges parallel to it)
        // while still picking up real intersections.
        const double MIN_FALLBACK_EXIT_ANGLE_DEG = 20.0;

        bool hasHoldShortOnRunway = false;
        foreach (var n in Nodes.Values)
        {
            if (n.Type != TaxiNodeType.HoldShort && n.Type != TaxiNodeType.ILSHoldShort)
                continue;
            const double M_PER_DEG_LAT = 111132.0;
            double latR = (rwy.StartLat + n.Latitude) * 0.5 * Math.PI / 180.0;
            double mPerLon = M_PER_DEG_LAT * Math.Cos(latR);
            double dNn = (n.Latitude - rwy.StartLat) * M_PER_DEG_LAT;
            double dEn = (n.Longitude - rwy.StartLon) * mPerLon;
            double latM = dEn * cosH - dNn * sinH;
            double aM = dEn * sinH + dNn * cosH;
            if (Math.Abs(latM) <= lateralToleranceM && aM >= 0 && aM <= lengthM + 50.0)
            { hasHoldShortOnRunway = true; break; }
        }

        foreach (var node in Nodes.Values)
        {
            bool isHoldShortNode = node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort;
            bool isImplicitExitNode = false;
            if (!isHoldShortNode)
            {
                // Fallback gate: only consider Normal nodes when THIS runway has
                // no explicit hold-short nodes at all. Otherwise the HS/IHS data
                // is authoritative and we shouldn't muddy the list with implicit
                // junctions (which tend to dedupe awkwardly).
                if (hasHoldShortOnRunway) continue;
                if (node.Type != TaxiNodeType.Normal) continue;
                if (!Adjacency.TryGetValue(node.NodeId, out var ee)) continue;

                // Need at least one named-taxiway edge that turns off the runway
                // axis at a meaningful angle. Parallel taxiways (whose edges run
                // along the runway heading) are filtered out here.
                bool hasOffAxisNamedEdge = false;
                foreach (var ed in ee)
                {
                    if (string.IsNullOrEmpty(ed.TaxiwayName)) continue;
                    double rel = Math.Abs(NormalizeAngle(ed.BearingDegrees - rwyHeadingTrue));
                    // Fold to 0..90 so reverse direction (180°) counts as parallel.
                    double off = rel > 90.0 ? 180.0 - rel : rel;
                    if (off >= MIN_FALLBACK_EXIT_ANGLE_DEG)
                    { hasOffAxisNamedEdge = true; break; }
                }
                if (!hasOffAxisNamedEdge) continue;
                isImplicitExitNode = true;
            }
            if (!isHoldShortNode && !isImplicitExitNode) continue;

            // Convert node offset from threshold into local meters (equirectangular).
            const double METERS_PER_DEG_LAT = 111132.0;
            double latRad = (rwy.StartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
            double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latRad);
            double dN = (node.Latitude - rwy.StartLat) * METERS_PER_DEG_LAT;         // north
            double dE = (node.Longitude - rwy.StartLon) * metersPerDegLon;           // east

            // Project onto runway axis.
            // Runway bearing is measured from north going clockwise; unit vector = (sin H, cos H) in (E, N).
            // along = dE*sinH + dN*cosH  ← distance along runway in its flight direction
            // lateral = dE*cosH - dN*sinH ← perpendicular distance (sign indicates left/right)
            double alongM = dE * sinH + dN * cosH;
            double lateralM = dE * cosH - dN * sinH;

            if (Math.Abs(lateralM) > lateralToleranceM) continue;
            if (alongM < 0) continue; // behind the threshold
            if (alongM > lengthM + 50.0) continue; // past the far end

            double alongFt = alongM / 0.3048;
            // Distance measured from the LANDING (displaced) threshold, which is
            // where the aircraft actually touches down. MIN_DIST_FT (500 ft) is
            // the earliest usable exit after that point; END_BUFFER_FT still
            // caps against the physical end of pavement.
            double distFromLandingThresholdFt = alongFt - landingThresholdOffsetFt;
            if (distFromLandingThresholdFt < MIN_DIST_FT || alongFt > maxDistFt) continue;

            // Find the best taxiway name + exit angle. Walk the node's edges and pick
            // the edge that is NOT on the runway (taxiway continuation), preferring
            // a named path. Compute the angle between that edge and the runway.
            string taxiwayName = "";
            double exitAngle = 90.0; // default to perpendicular if nothing better found
            if (Adjacency.TryGetValue(node.NodeId, out var edges))
            {
                TaxiEdge? best = null;
                foreach (var e in edges)
                {
                    // Skip runway-type edges (the edge that lies on the runway centerline).
                    bool onRunway = string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase);
                    if (onRunway) continue;
                    if (string.IsNullOrEmpty(e.TaxiwayName)) continue;

                    if (best == null)
                    {
                        best = e;
                        continue;
                    }

                    // Prefer connector-style names (letter+digit) over bare main names.
                    // Same ranking spirit as hold-short naming (Feature #7).
                    bool bestHasDigit = HasLetterAndDigit(best.TaxiwayName);
                    bool curHasDigit  = HasLetterAndDigit(e.TaxiwayName);
                    if (curHasDigit && !bestHasDigit)
                        best = e;
                }

                if (best != null)
                {
                    taxiwayName = best.TaxiwayName;
                    // Raw relative angle in 0..180 (absolute value of normalized delta).
                    double rel = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));

                    // Is the edge pointing "backward" relative to the landing direction?
                    // rel > 90 means more than a right angle off the landing heading —
                    // i.e., the taxiway peels off toward the approach end rather than
                    // toward the departure end.
                    bool peelsBackward = rel > 90.0;

                    // Fold to 0..90 so "exitAngle" is magnitude of turn from runway axis.
                    exitAngle = peelsBackward ? 180.0 - rel : rel;

                    // Backward-peel nodes are almost always end-of-runway turnoffs
                    // (handled by the endRatio>0.85 check below). But if a backward
                    // peel appears mid-runway, we should NOT classify it as
                    // "High-speed" — exiting through it requires turning around,
                    // which is not a high-speed RET. Forcing exitAngle to an
                    // obtuse-looking value (>=100) pushes classification to "End"
                    // below regardless of the along-runway position.
                    if (peelsBackward && exitAngle < 50.0)
                    {
                        // Geometry says RET-angle but direction is backward —
                        // treat as end-style exit, not a high-speed. Forcing the
                        // angle above NORMAL_MAX_DEG pushes classification to
                        // "End" below regardless of along-runway position.
                        exitAngle = NORMAL_MAX_DEG + 20.0;
                    }
                }
            }

            // End-of-runway classification: if the exit is within the last 15% of the
            // runway, label it "End" regardless of angle — exiting there means rolling
            // out the full length.
            double endRatio = alongFt / rwy.Length;
            string exitType;
            if (endRatio > END_RATIO)
                exitType = "End";
            else if (exitAngle <= HIGH_SPEED_MAX_DEG)
                exitType = "High-speed";
            else if (exitAngle <= NORMAL_MAX_DEG)
                exitType = "Normal";
            else
                exitType = "End";

            exits.Add(new LandingExit
            {
                NodeId = node.NodeId,
                Latitude = node.Latitude,
                Longitude = node.Longitude,
                DistanceFromThresholdFeet = distFromLandingThresholdFt,
                DistanceFromTouchdownFeet = distFromLandingThresholdFt - TOUCHDOWN_AIM_FT,
                TaxiwayName = taxiwayName,
                ExitAngleDegrees = exitAngle,
                ExitType = exitType
            });
        }

        // Deduplicate exits that share the same taxiway name and are within 50 ft of
        // each other along the runway (happens when both sides of a taxiway intersection
        // produce a hold-short node). Keep the one with the smaller angle (better RET
        // candidate) or, if equal, the one closer to the threshold.
        exits.Sort((a, b) =>
        {
            int c = a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet);
            return c;
        });

        var deduped = new List<LandingExit>(exits.Count);
        foreach (var e in exits)
        {
            bool merged = false;
            for (int i = deduped.Count - 1; i >= 0; i--)
            {
                var d = deduped[i];
                if (Math.Abs(d.DistanceFromThresholdFeet - e.DistanceFromThresholdFeet) > DEDUP_WINDOW_FT)
                    break; // list is sorted; no further candidates within DEDUP_WINDOW_FT
                if (string.Equals(d.TaxiwayName, e.TaxiwayName, StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the one with smaller exit angle.
                    if (e.ExitAngleDegrees < d.ExitAngleDegrees)
                        deduped[i] = e;
                    merged = true;
                    break;
                }
            }
            if (!merged) deduped.Add(e);
        }

        return deduped;
    }

    private static bool HasLetterAndDigit(string s)
    {
        bool hasL = false, hasD = false;
        foreach (char c in s)
        {
            if (char.IsLetter(c)) hasL = true;
            else if (char.IsDigit(c)) hasD = true;
            if (hasL && hasD) return true;
        }
        return false;
    }

    #endregion
}
