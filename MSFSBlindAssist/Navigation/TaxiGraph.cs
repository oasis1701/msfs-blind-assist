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

    /// <summary>
    /// A taxiway that meets a runway partway down its length — an intersection
    /// (a.k.a. intersection-departure) point. Enumerated by
    /// <see cref="GetRunwayIntersections"/> so the Taxi form can offer "depart
    /// from taxiway W" instead of taxiing to the full-length threshold.
    /// </summary>
    public class RunwayIntersection
    {
        public string TaxiwayName = "";
        public int NodeId;                      // graph node where the taxiway meets the runway
        public double Latitude, Longitude;      // that node projected ONTO the centerline
        public double AlongMetersFromThreshold; // from the named runway's takeoff-end threshold
        public double RemainingMeters;          // runway ahead in the takeoff direction
    }

    // Max perpendicular distance (metres) from a hold-short node to a runway
    // centerline for the node to be named after that runway. Above a CAT III /
    // code-F holding-position setback (~107 m) so real hold lines match, below
    // major-airport parallel-runway spacing so a node binds to its OWN runway.
    private const double HOLDSHORT_RUNWAY_MATCH_M = 150.0;

    // Start at 1 so node ID 0 is a permanent "not set" sentinel. TaxiGuidanceManager
    // uses _destinationNodeId = 0 to mark a cleared route (e.g. after
    // EnterRunwayEndCountdown). If _nextNodeId were 0, the first real node would
    // collide with that sentinel — ContainsKey(0) would return true and the
    // recalc path could try to route to it.
    private int _nextNodeId = 1;
    private readonly Dictionary<string, List<int>> _spatialHash = new();
    private readonly Dictionary<string, List<int>> _taxiwayNodeIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a normalized alias (see TaxiDataMerger.NormalizeTaxiwayName) to the canonical
    /// taxiway name stored in the graph. Populated during Build from TaxiPath.Aliases.
    /// Used by ResolveTaxiwayName so pilots can enter alternative names (e.g. "K" for
    /// a navdata taxiway named "HAWKER") and still find the correct route.
    /// </summary>
    public Dictionary<string, string> TaxiwayAliasToCanonical { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Self-describing dropdown labels for online-source aliases — maps the display string
    /// (e.g. "B (HAWKER)") to the canonical navdata name ("HAWKER"). Surfaced in the taxiway
    /// dropdowns so a screen-reader user hears "B, HAWKER" and knows it's the SAME pavement;
    /// selecting one resolves to the canonical via ResolveTaxiwayName (exact match on the label).
    /// </summary>
    public Dictionary<string, string> AliasDisplayToCanonical { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Display label (e.g. "B (HAWKER)") → the normalized alias form ("B") captured at Build time.
    /// GetAllTaxiwayNames' collision skip reads THIS instead of re-parsing the label with
    /// LastIndexOf(" (") — a canonical name that itself contains " (" (e.g. "RAMP (NORTH)") would
    /// otherwise split at the wrong paren and mis-classify the alias.
    /// </summary>
    private readonly Dictionary<string, string> _aliasDisplayToNormalized = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalized forms of every REAL navdata taxiway name. An alias is never allowed to remap a
    /// name that is itself a real taxiway (that would misroute a legitimate clearance), so
    /// ResolveTaxiwayName / GetAllTaxiwayNames consult this set as a guard.
    /// </summary>
    private readonly HashSet<string> _normalizedRealNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalized bare aliases that map to MORE THAN ONE distinct canonical name (two different
    /// navdata taxiways online-named the same thing). A bare such alias can't safely pick one
    /// pavement, so ResolveTaxiwayName refuses to resolve it (returns the entered text unchanged) —
    /// a miss is safer than guessing. The disambiguated labels ("B (HAWKER)" / "B (FOXTROT)") still
    /// resolve via AliasDisplayToCanonical.
    /// </summary>
    private readonly HashSet<string> _ambiguousNormalizedAliases = new(StringComparer.OrdinalIgnoreCase);

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
                // Track the normalized real name so an alias can never remap a genuine taxiway.
                graph._normalizedRealNames.Add(
                    MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger.NormalizeTaxiwayName(name));
            }

            // Register any online-source aliases discovered by TaxiDataMerger.
            // Only affects COMPARISON / routing — never changes what is stored in the graph.
            if (path.Aliases != null && path.Aliases.Count > 0 && !string.IsNullOrEmpty(name))
            {
                foreach (var alias in path.Aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    string normalizedAlias = MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger
                        .NormalizeTaxiwayName(alias);
                    if (string.IsNullOrEmpty(normalizedAlias)) continue;

                    // Self-describing label "ALIAS (CANONICAL)" for the dropdown so the pilot can
                    // find + select the ATC/real name and hear which pavement it is. ALWAYS register
                    // the label (one per distinct canonical), so when two taxiways share an online
                    // name BOTH "B (HAWKER)" and "B (FOXTROT)" are selectable; resolution is exact on
                    // the label string. Also store the normalized alias for GetAllTaxiwayNames' skip.
                    string label = $"{alias.Trim()} ({name})";
                    graph.AliasDisplayToCanonical[label] = name;
                    graph._aliasDisplayToNormalized[label] = normalizedAlias;

                    // Bare-alias → canonical map, with ambiguity detection. A second DIFFERENT
                    // canonical for the same normalized alias makes the bare form ambiguous: remove
                    // it and never re-add (ResolveTaxiwayName then leaves a bare "B" unresolved).
                    if (graph._ambiguousNormalizedAliases.Contains(normalizedAlias))
                    {
                        // already ambiguous — labels stay, bare form stays unresolved
                    }
                    else if (graph.TaxiwayAliasToCanonical.TryGetValue(normalizedAlias, out var existing))
                    {
                        if (!string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                        {
                            graph._ambiguousNormalizedAliases.Add(normalizedAlias);
                            graph.TaxiwayAliasToCanonical.Remove(normalizedAlias);
                        }
                        // same canonical (another segment of the same taxiway) — keep as-is
                    }
                    else
                    {
                        graph.TaxiwayAliasToCanonical[normalizedAlias] = name;
                    }
                }
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

                // Primary: associate by nearest runway CENTERLINE (length-invariant),
                // so a mid-runway crossing of a long runway is named after the runway,
                // not the taxiway. Format leads with the runway (the safety cue),
                // appending the holding-point/taxiway designator when present.
                string? centerlineRwy = MatchHoldShortRunwayName(
                    node.Latitude, node.Longitude, graph.RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M);
                if (centerlineRwy != null)
                {
                    node.HoldShortName = !string.IsNullOrEmpty(holdPointName)
                        ? $"runway {centerlineRwy} at {holdPointName}"
                        : $"runway {centerlineRwy}";
                }
                // Fallback (no centerlines built, or none within tolerance): the
                // existing threshold-distance heuristic, unchanged — preserves
                // today's output for sparse navdata without reciprocal pairs.
                else if (nearestRunway != null && nearestDist < 500)
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

        // Compute connected components so start-node selectors can filter by
        // reachability to a known destination. Runs after all edges and node
        // upgrades are in place.
        graph.AssignConnectedComponents();

        return graph;
    }

    /// <summary>
    /// Assigns each node a ComponentId so callers can filter start-node candidates
    /// by reachability. Runs once at Build time after all edges are added. BFS over
    /// Adjacency; nodes in the same connected component share an integer ID
    /// starting at 0.
    ///
    /// Motivating defect: fs2024 navdata at GCLP models taxiway S5 as a 13-node
    /// island with no connection to any other taxiway at either terminus. A pilot
    /// touching down on 03L near S5 would have the start-node picker snap to an
    /// S5 node, and A* could never reach the chosen exit (in the main 1075-node
    /// component). With component IDs, the caller filters the start-node search
    /// to nodes co-component with the destination — the picker skips S5 and finds
    /// a reachable node on R3 instead.
    /// </summary>
    private void AssignConnectedComponents()
    {
        int nextComponentId = 0;
        var queue = new Queue<int>();

        foreach (var startNode in Nodes.Values)
        {
            if (startNode.ComponentId != -1) continue;
            int componentId = nextComponentId++;
            startNode.ComponentId = componentId;
            queue.Enqueue(startNode.NodeId);

            while (queue.Count > 0)
            {
                int currentId = queue.Dequeue();
                if (!Adjacency.TryGetValue(currentId, out var edges)) continue;
                foreach (var edge in edges)
                {
                    var neighbor = Nodes[edge.ToNodeId];
                    if (neighbor.ComponentId == -1)
                    {
                        neighbor.ComponentId = componentId;
                        queue.Enqueue(neighbor.NodeId);
                    }
                }
            }
        }
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
    /// Finds the nearest graph node to a given position. When
    /// <paramref name="requiredComponentId"/> is set, only nodes in that
    /// connected component are considered (the spatial-hash ring and the
    /// full-scan fallback both honour it) — used to keep an aircraft's start
    /// node in the destination's component.
    /// </summary>
    public TaxiNode? FindNearestNode(double lat, double lon, int? requiredComponentId = null)
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
                            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                                continue;
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
            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                continue;
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
    /// Finds the nearest graph node lying on a named taxiway. Heading-independent.
    /// Use case: snapping a route start onto the user's first ATC-cleared taxiway
    /// regardless of aircraft orientation (e.g. immediately after pushback). Caller
    /// can pass <paramref name="requiredComponentId"/> to restrict candidates to a
    /// connected component (typically the destination's) so isolated-island
    /// taxiways are skipped — see <see cref="FindNearestNodeInDirection"/>. Returns
    /// null if no node on <paramref name="taxiwayName"/> lies within
    /// <paramref name="maxDistanceM"/> of the position (and within the requested
    /// component if set).
    /// </summary>
    public TaxiNode? FindNearestNodeOnTaxiway(
        double lat, double lon, string taxiwayName,
        double maxDistanceM = 800.0,
        int? requiredComponentId = null)
    {
        if (string.IsNullOrEmpty(taxiwayName)) return null;

        TaxiNode? best = null;
        double bestDist = maxDistanceM;

        foreach (var node in Nodes.Values)
        {
            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                continue;
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

    /// <summary>
    /// Finds the nearest graph node in the direction the aircraft is facing.
    /// Returns the closest node that is roughly ahead (within ±90° of heading),
    /// bounded by MAX_START_NODE_DISTANCE_M — if nothing ahead is within range,
    /// falls back to the overall nearest node (also distance-bounded), otherwise
    /// returns null. A null return means "no taxiway node near this position" —
    /// caller should report "no nearby taxiway" rather than silently snap to
    /// something far away. Caller can pass <paramref name="requiredComponentId"/>
    /// to restrict candidates (including the fallback) to a connected component
    /// (typically the destination's) so isolated-island taxiways are skipped —
    /// see <see cref="FindNearestNodeOnTaxiway"/>.
    /// </summary>
    public TaxiNode? FindNearestNodeInDirection(
        double lat, double lon, double headingDeg,
        int? requiredComponentId = null)
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
            // Component filter: when a destination is known, candidate start nodes
            // must be in the same connected component or A* will fail. Defends
            // against navdata defects where a nearby taxiway is an isolated island
            // (e.g. GCLP S5 in fs2024 — 13 nodes, 0 external connections).
            if (requiredComponentId.HasValue && node.ComponentId != requiredComponentId.Value)
                continue;

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
        // AND (when filtering) in the requested component.
        var fallback = FindNearestNode(lat, lon);
        if (fallback == null) return null;
        if (requiredComponentId.HasValue && fallback.ComponentId != requiredComponentId.Value)
            return null;
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
    /// Detects which runway the aircraft is sitting on, using the same half-width
    /// tolerance as DescribeLocation but exposing structured data for callers that
    /// need geometry (threshold lat/lon, true heading, designator) rather than a
    /// spoken string. Uses the aircraft's true heading to pick the correct
    /// reciprocal designator (e.g. 27L vs 09R) — the "threshold" is the upwind
    /// end of the runway, i.e. the end the aircraft is taking off FROM.
    /// </summary>
    /// <param name="lat">Aircraft latitude (degrees).</param>
    /// <param name="lon">Aircraft longitude (degrees).</param>
    /// <param name="aircraftHeadingTrue">
    /// Aircraft true heading in degrees. Used to pick the reciprocal designator.
    /// </param>
    /// <param name="runwayId">
    /// Out: runway designator (e.g. "27L"), no "Runway " prefix.
    /// </param>
    /// <param name="thresholdLat">Out: latitude of the upwind threshold.</param>
    /// <param name="thresholdLon">Out: longitude of the upwind threshold.</param>
    /// <param name="runwayHeadingTrue">
    /// Out: true heading of the runway in the takeoff direction (degrees, 0..360).
    /// </param>
    /// <returns>
    /// True if the aircraft is within half-width of a runway centerline. False
    /// if the aircraft is not on any runway in this graph's RunwayCenterlines list.
    /// </returns>
    public bool TryGetRunwayAtPosition(
        double lat, double lon, double aircraftHeadingTrue,
        out string runwayId,
        out double thresholdLat, out double thresholdLon,
        out double runwayHeadingTrue)
    {
        runwayId = "";
        thresholdLat = 0; thresholdLon = 0;
        runwayHeadingTrue = 0;

        foreach (var rwy in RunwayCenterlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            // Strict half-width (no +5 m tolerance). Stricter than DescribeLocation
            // because takeoff-assist centerline math depends on the chosen runway
            // actually being the one under the aircraft — a 5 m fudge could
            // mis-attribute when the aircraft is sitting on a high-speed exit
            // immediately adjacent to a runway.
            if (perp > rwy.HalfWidthMeters) continue;

            // Pick the end whose takeoff heading is closer to the aircraft's
            // heading. End 1's takeoff heading is HeadingDeg1; end 2's is
            // HeadingDeg1 + 180 (mod 360).
            double hdg1 = NormalizeHeading(rwy.HeadingDeg1);
            double hdg2 = NormalizeHeading(rwy.HeadingDeg1 + 180.0);
            double diff1 = Math.Abs(NormalizeAngle(aircraftHeadingTrue - hdg1));
            double diff2 = Math.Abs(NormalizeAngle(aircraftHeadingTrue - hdg2));

            if (diff1 <= diff2)
            {
                runwayId = rwy.Name1;
                thresholdLat = rwy.Lat1;
                thresholdLon = rwy.Lon1;
                runwayHeadingTrue = hdg1;
            }
            else
            {
                runwayId = rwy.Name2;
                thresholdLat = rwy.Lat2;
                thresholdLon = rwy.Lon2;
                runwayHeadingTrue = hdg2;
            }

            // Fallback if the chosen end has an empty Name (shouldn't happen
            // in well-formed navdata, but defensive — an empty designator
            // would propagate into the spoken callout). When we fall over to
            // the other end's name, also re-point the threshold + heading to
            // that other end so the geometry stays consistent with the name.
            // If both names are empty, leave the geometry on the originally
            // chosen end — the empty runwayId will be the caller's signal
            // that data is malformed, but threshold + heading remain valid
            // approximations.
            if (string.IsNullOrEmpty(runwayId))
            {
                if (rwy.Name1.Length > 0)
                {
                    runwayId = rwy.Name1;
                    thresholdLat = rwy.Lat1;
                    thresholdLon = rwy.Lon1;
                    runwayHeadingTrue = hdg1;
                }
                else if (rwy.Name2.Length > 0)
                {
                    runwayId = rwy.Name2;
                    thresholdLat = rwy.Lat2;
                    thresholdLon = rwy.Lon2;
                    runwayHeadingTrue = hdg2;
                }
            }

            return true;
        }

        return false;
    }

    private static double NormalizeHeading(double deg)
    {
        deg = deg % 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    /// <summary>
    /// Enumerates the taxiways that meet a runway partway along its length — the
    /// valid intersection-departure points. For each named taxiway we keep the
    /// single node closest to the runway centerline; if that node sits within the
    /// runway half-width (i.e. the taxiway actually reaches the pavement) it
    /// becomes an intersection, with its distance measured from the DEPARTURE
    /// threshold (so "remaining" is the runway ahead in the takeoff direction).
    /// Sorted threshold-first. The threshold connector itself and the far-end
    /// nubs are filtered out.
    ///
    /// Parallel taxiways that run alongside the runway (e.g. a full-length
    /// parallel) never have a node within half-width, so they're correctly
    /// excluded — only taxiways that genuinely enter the runway are offered.
    ///
    /// The centerline is passed as explicit PHYSICAL geometry
    /// (<paramref name="thrLat"/>..<paramref name="farLon"/>, from the runway_end
    /// thresholds via the <c>Runway</c> model) — NOT the <see cref="RunwayCenterline"/>
    /// list, which is built from the <c>start</c> table and can sit hundreds of
    /// metres inside the pavement at displaced-threshold runways (that would make
    /// "remaining" badly understate the real runway length).
    /// </summary>
    /// <param name="thrLat">Departure-end threshold latitude (takeoff direction origin).</param>
    /// <param name="thrLon">Departure-end threshold longitude.</param>
    /// <param name="farLat">Opposite-end (rollout-end) latitude.</param>
    /// <param name="farLon">Opposite-end longitude.</param>
    /// <param name="halfWidthMeters">Runway half-width; a node must be within this of the centerline to count.</param>
    public List<RunwayIntersection> GetRunwayIntersections(
        double thrLat, double thrLon, double farLat, double farLon, double halfWidthMeters)
    {
        var result = new List<RunwayIntersection>();

        double totalLen = FastDistanceMeters(thrLat, thrLon, farLat, farLon);
        if (totalLen < 1.0) return result;

        // Small tolerance above the stored half-width to absorb navdata rounding
        // at the runway edge (the node is often exactly on the centerline, but a
        // few metres of slop keeps a wide runway's entrance node in).
        double maxPerp = halfWidthMeters + 5.0;
        const double MIN_ALONG_M = 15.0;      // exclude the threshold connector itself
        const double MIN_REMAINING_M = 45.0;  // exclude far-end nubs (~150 ft left)

        foreach (var kv in _taxiwayNodeIndex)
        {
            string twName = kv.Key;
            if (string.IsNullOrEmpty(twName)) continue;

            int bestNode = -1;
            double bestPerp = double.MaxValue, bestAlong = 0, bestLat = 0, bestLon = 0;
            foreach (int nid in kv.Value)
            {
                if (!Nodes.TryGetValue(nid, out var n)) continue;
                var (perp, along, projLat, projLon) =
                    ProjectOntoCenterline(n.Latitude, n.Longitude, thrLat, thrLon, farLat, farLon);
                // Only nodes that are themselves a valid intersection point compete
                // for "closest to the centerline": within the pavement band, past
                // the threshold connector, and with usable runway remaining. Gating
                // HERE — not after picking a single best node — means a taxiway's
                // near-threshold connector node (along < MIN_ALONG_M) or a far-end
                // nub can't shadow a genuine mid-field entrance further down the
                // same taxiway and drop the whole taxiway from the list.
                if (along < MIN_ALONG_M || along > totalLen) continue;
                if (totalLen - along < MIN_REMAINING_M) continue;
                if (perp > maxPerp) continue;
                if (perp < bestPerp)
                {
                    bestPerp = perp; bestNode = nid; bestAlong = along;
                    bestLat = projLat; bestLon = projLon;
                }
            }

            if (bestNode < 0) continue;
            double remaining = totalLen - bestAlong;

            result.Add(new RunwayIntersection
            {
                TaxiwayName = twName,
                NodeId = bestNode,
                Latitude = bestLat,
                Longitude = bestLon,
                AlongMetersFromThreshold = bestAlong,
                RemainingMeters = remaining,
            });
        }

        result.Sort((a, b) => a.AlongMetersFromThreshold.CompareTo(b.AlongMetersFromThreshold));
        return result;
    }

    /// <summary>
    /// Projects a point onto the runway centerline (a→b). Returns the
    /// perpendicular distance, the signed along-track distance from a (metres),
    /// and the foot-of-perpendicular point in lat/lon. Equirectangular frame —
    /// accurate at runway scale. Unlike <see cref="PerpendicularDistanceMeters"/>
    /// the along-track value is NOT clamped, so callers can reject points beyond
    /// the thresholds.
    /// </summary>
    private static (double perp, double along, double projLat, double projLon) ProjectOntoCenterline(
        double plat, double plon, double alat, double alon, double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos((alat + blat) * 0.5 * (Math.PI / 180.0));

        double bx = (blon - alon) * metersPerDegLon, by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon, py = (plat - alat) * METERS_PER_DEG_LAT;

        double lenSq = bx * bx + by * by;
        if (lenSq < 1e-9)
            return (Math.Sqrt(px * px + py * py), 0.0, alat, alon);

        double t = (px * bx + py * by) / lenSq;
        double fx = t * bx, fy = t * by;
        double ex = px - fx, ey = py - fy;
        double perp = Math.Sqrt(ex * ex + ey * ey);
        double along = t * Math.Sqrt(lenSq);
        double projLat = alat + t * (blat - alat);
        double projLon = alon + t * (blon - alon);
        return (perp, along, projLat, projLon);
    }

    /// <summary>
    /// Runway designator a hold-short node holds short of, found by the NEAREST
    /// runway centerline (full length, perpendicular distance clamped to the
    /// threshold endpoints). Length-invariant: a hold-short where a taxiway
    /// crosses a long runway far from either threshold is still matched — unlike a
    /// distance-to-threshold test. Returns the closer-end designator (same
    /// convention as DescribeLocation / WhichRunwayContains), or null when no
    /// centerline is within <paramref name="maxMatchMeters"/> (the caller then
    /// falls back to the threshold heuristic). Public static for probe coverage.
    /// </summary>
    public static string? MatchHoldShortRunwayName(
        double lat, double lon,
        IReadOnlyList<RunwayCenterline> centerlines,
        double maxMatchMeters)
    {
        string? best = null;
        double bestPerp = double.MaxValue;
        foreach (var rwy in centerlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            if (perp > maxMatchMeters || perp >= bestPerp) continue;

            // Closer-end designator (same convention as DescribeLocation): the end
            // the aircraft is nearer is the one it would line up to depart from.
            double d1 = FastDistanceMeters(lat, lon, rwy.Lat1, rwy.Lon1);
            double d2 = FastDistanceMeters(lat, lon, rwy.Lat2, rwy.Lon2);
            string name = d1 <= d2 ? rwy.Name1 : rwy.Name2;
            if (string.IsNullOrEmpty(name)) name = rwy.Name1;
            if (string.IsNullOrEmpty(name)) continue; // unnamed centerline — skip

            best = name;
            bestPerp = perp;
        }
        return best;
    }

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

    /// <summary>
    /// Perpendicular distance (meters) from point (plat, plon) to segment (a→b).
    /// Uses equirectangular projection — accurate for taxiway-scale distances.
    /// Returns the distance to the nearest point on the segment (not the infinite line),
    /// so endpoints count when the foot of the perpendicular falls outside.
    /// </summary>
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
    /// True when the taxi edge (a→b) crosses the runway centerline (t1→t2)
    /// between the thresholds — a proper segment-segment intersection.
    ///
    /// This is the CORRECT "does the route cross this runway" test. A taxiway
    /// crosses a runway via an EDGE that spans the pavement, with its endpoint
    /// NODES sitting OFF the runway on either side — so a "is a node ON the
    /// pavement?" test (perpendicular distance ≤ half-width) silently misses the
    /// crossing whenever the flanking nodes are more than ~half-width+5 m from
    /// the centerline. KBOS taxiway C over runway 04L is the motivating case:
    /// C plainly crosses 04L, but C's nearest node is 35 m from the 04L
    /// centerline (half-width is 25 m), so the node test found nothing and no
    /// hold-short was inserted — even though the route clearly traverses the
    /// runway. The edge-intersection test catches it regardless of node spacing.
    ///
    /// "Proper" (strict opposite-sides) intersection by design: a taxiway that
    /// merely touches a threshold endpoint or runs parallel alongside the runway
    /// is NOT flagged, avoiding false hold-shorts.
    /// </summary>
    public static bool EdgeCrossesRunwayStatic(
        double aLat, double aLon, double bLat, double bLon,
        double t1Lat, double t1Lon, double t2Lat, double t2Lon)
    {
        // Project the four points to a local planar frame (origin = a, x=east,
        // y=north, metres) — equirectangular, accurate at taxiway/runway scale.
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon =
            METERS_PER_DEG_LAT * Math.Cos((aLat + bLat) * 0.5 * (Math.PI / 180.0));

        double p1x = 0.0, p1y = 0.0;
        double p2x = (bLon - aLon) * metersPerDegLon, p2y = (bLat - aLat) * METERS_PER_DEG_LAT;
        double p3x = (t1Lon - aLon) * metersPerDegLon, p3y = (t1Lat - aLat) * METERS_PER_DEG_LAT;
        double p4x = (t2Lon - aLon) * metersPerDegLon, p4y = (t2Lat - aLat) * METERS_PER_DEG_LAT;

        // Orientation of (b)/(t1)/(t2) relative to the two directed lines.
        double d1 = Orient(p3x, p3y, p4x, p4y, p1x, p1y); // p1 vs runway line
        double d2 = Orient(p3x, p3y, p4x, p4y, p2x, p2y); // p2 vs runway line
        double d3 = Orient(p1x, p1y, p2x, p2y, p3x, p3y); // t1 vs edge line
        double d4 = Orient(p1x, p1y, p2x, p2y, p4x, p4y); // t2 vs edge line

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));

        // Signed area (z of cross product) of (o→a)×(o→b): >0 left, <0 right.
        static double Orient(double ox, double oy, double ax, double ay, double bx, double by)
            => (ax - ox) * (by - oy) - (ay - oy) * (bx - ox);
    }

    /// <summary>
    /// Gets all unique taxiway names in the graph, sorted, including alias names so
    /// pilots can type an alternative name and still see it in dropdowns.
    /// Alias names are the human-readable forms collected from online sources.
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

        // Include alias display names (the human-readable online-source forms, e.g. "B" for a
        // navdata "HAWKER") so a pilot can SELECT the ATC/real name from the dropdown — the combo
        // is DropDownList (no free text), so an alias the pilot can't select would be useless.
        // Selecting an alias resolves to the canonical name at route time (ResolveTaxiwayName).
        // Skip any alias whose normalized form collides with a real taxiway name: that real name
        // is ALREADY in the list and ResolveTaxiwayName routes the bare name to the REAL taxiway
        // (collision guard), so surfacing e.g. "Z (K)" would be a mislabeled duplicate of the real
        // taxiway Z that, if selected, mis-routes to K. These collisions are common at rich,
        // junction-dense airports (OMDB: ~130 such labels) where a navdata segment's midpoint
        // matches a DIFFERENT-named crossing online segment. The normalized alias is captured at
        // Build time in _aliasDisplayToNormalized — do NOT re-derive it by parsing the label, since
        // a canonical name containing " (" (e.g. "RAMP (NORTH)") splits at the wrong paren.
        foreach (var display in AliasDisplayToCanonical.Keys)
        {
            string normAlias = _aliasDisplayToNormalized.TryGetValue(display, out var na) ? na : "";
            if (!string.IsNullOrEmpty(normAlias) && _normalizedRealNames.Contains(normAlias))
                continue;
            names.Add(display);
        }

        var sorted = names.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    /// <summary>
    /// Resolves an entered taxiway name to the canonical navdata name via the alias map.
    /// If <paramref name="entered"/> (normalized) is a known alias, returns the canonical
    /// navdata name stored in the graph. Otherwise returns <paramref name="entered"/> unchanged.
    /// Comparison is normalized (case-insensitive, spaces stripped, TWY/TAXIWAY prefix stripped).
    /// </summary>
    public string ResolveTaxiwayName(string entered)
    {
        if (string.IsNullOrWhiteSpace(entered))
            return entered;

        // Exact match on a labeled dropdown alias ("B (HAWKER)") → canonical navdata name.
        if (AliasDisplayToCanonical.TryGetValue(entered.Trim(), out string? byLabel))
            return byLabel;

        string normalized = MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger
            .NormalizeTaxiwayName(entered);

        // Collision guard: if the entered name IS a real taxiway, never remap it to an alias
        // canonical — a legitimate "B" clearance must route to the real "B", not to whatever
        // online source happened to also call "B" by another name.
        if (!string.IsNullOrEmpty(normalized) && !_normalizedRealNames.Contains(normalized) &&
            TaxiwayAliasToCanonical.TryGetValue(normalized, out string? canonical))
        {
            return canonical;
        }

        return entered;
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
    /// Returns the node on <paramref name="fromTaxiway"/> that also has an outgoing edge on
    /// <paramref name="toTaxiway"/> within the given connected component — i.e. the intersection
    /// node where the two taxiways meet.  Used by the progressive-taxi terminator to locate
    /// "hold short of taxiway Y while on taxiway X".
    /// Returns -1 when no such node exists in the component.
    /// </summary>
    public int FindTaxiwayIntersectionNode(string fromTaxiway, string toTaxiway, int requiredComponentId)
    {
        foreach (var kvp in Adjacency)
        {
            int nodeId = kvp.Key;
            if (!Nodes.TryGetValue(nodeId, out var node) || node.ComponentId != requiredComponentId)
                continue;
            bool onFrom = false, onTo = false;
            foreach (var e in kvp.Value)
            {
                if (e.TaxiwayName.Equals(fromTaxiway, StringComparison.OrdinalIgnoreCase)) onFrom = true;
                else if (e.TaxiwayName.Equals(toTaxiway, StringComparison.OrdinalIgnoreCase)) onTo = true;
            }
            if (onFrom && onTo) return nodeId;
        }
        return -1;
    }

    /// <summary>
    /// Returns the node on <paramref name="taxiway"/> (in the same connected component as
    /// <paramref name="fromNodeId"/>) that is farthest — by straight-line distance — from
    /// <paramref name="fromNodeId"/>.  Used by the progressive-taxi terminator to locate
    /// the "end of taxiway" destination.
    /// Returns -1 when <paramref name="fromNodeId"/> is not found or no node on
    /// <paramref name="taxiway"/> exists in the component.
    /// </summary>
    public int FindTaxiwayEndNode(int fromNodeId, string taxiway)
    {
        if (!Nodes.TryGetValue(fromNodeId, out var from)) return -1;
        int comp = from.ComponentId;
        int best = -1;
        double bestDist = -1;
        foreach (var kvp in Adjacency)
        {
            int nodeId = kvp.Key;
            if (!Nodes.TryGetValue(nodeId, out var node) || node.ComponentId != comp) continue;
            if (!kvp.Value.Any(e => e.TaxiwayName.Equals(taxiway, StringComparison.OrdinalIgnoreCase))) continue;
            double d = FastDistanceMeters(from.Latitude, from.Longitude, node.Latitude, node.Longitude);
            if (d > bestDist) { bestDist = d; best = nodeId; }
        }
        return best;
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
        // threshold hold-short lines). END_BUFFER_FT is a small margin against nodes
        // literally on the runway-end markings; the geometric corridor + named-edge
        // filters are the real protection, so 50 ft is enough (200 ft was excluding
        // legitimate end-of-runway vacate exits like S7 at EIDW 28L).
        const double MIN_DIST_FT = 500.0;
        const double END_BUFFER_FT = 50.0;
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
            if (Math.Abs(latM) > lateralToleranceM || aM < 0 || aM > lengthM + 50.0)
                continue;
            // Node is geometrically within this runway's corridor. Also require that it
            // has at least one named edge whose exit angle is meaningful for this landing
            // direction (≤ NORMAL_MAX_DEG after applying the same backward-RET override
            // used in the main exit-angle computation). Without this check, a RET that is
            // designed for the OPPOSITE runway direction (e.g. N4 at EIDW — a 28R RET
            // whose node physically lies inside the 10L corridor) would set
            // hasHoldShortOnRunway=true, blocking the Normal-node fallback from finding
            // the real 10L exits (N1/N2/N3), leaving only that backward RET.
            if (!Adjacency.TryGetValue(n.NodeId, out var hsEdges)) continue;
            bool hasForwardExit = false;
            foreach (var he in hsEdges)
            {
                if (string.IsNullOrEmpty(he.TaxiwayName)) continue;
                double relAngle = Math.Abs(NormalizeAngle(he.BearingDegrees - rwyHeadingTrue));
                bool peelsBack = relAngle > 90.0;
                double ea = peelsBack ? 180.0 - relAngle : relAngle;
                if (peelsBack && ea < 50.0) ea = NORMAL_MAX_DEG + 20.0;
                if (ea <= NORMAL_MAX_DEG) { hasForwardExit = true; break; }
            }
            if (hasForwardExit) { hasHoldShortOnRunway = true; break; }
        }

        foreach (var node in Nodes.Values)
        {
            bool isHoldShortNode = node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort;
            bool isImplicitExitNode = false;
            // For fallback implicit exits: node ID of the first point outside the corridor.
            // Set by ExitPathLeavesCorridor; used as ApronNodeId for tone re-routing.
            int implicitApronNodeId = -1;
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
                // Secondary gate: some scenery packages (e.g. LVFR LEMD) model RETs as
                // smooth curves whose individual PT-type path segments all run nearly
                // parallel to the runway (< 20° off-axis), so the angle test above
                // misses them. A truly parallel holding taxiway stays within the lateral
                // corridor for its entire length; a real RET must eventually leave the
                // corridor to reach the apron. Follow named edges up to 600 m and accept
                // the node if the path demonstrably exits the runway strip.
                // Also captures the corridor-exit node ID for ApronNodeId (re-routing).
                if (!hasOffAxisNamedEdge)
                {
                    implicitApronNodeId = ExitPathLeavesCorridor(node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
                    if (implicitApronNodeId < 0) continue;
                    hasOffAxisNamedEdge = true;
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
            double exitBearingTrue = 0.0; // true bearing of best exit edge; 0 = not found
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
                    {
                        best = e;
                    }
                    else if (curHasDigit == bestHasDigit)
                    {
                        // Same name priority — prefer the edge that turns most off-axis
                        // from the runway. Adjacency-list ordering is not guaranteed, so
                        // without this tie-break a parallel-running named edge can be
                        // chosen over the actual perpendicular exit edge, producing an
                        // exit angle of ~0° for exits like EGCC AF/AG on 23R.
                        double bestRel = Math.Abs(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue));
                        double bestOff = bestRel > 90.0 ? 180.0 - bestRel : bestRel;
                        double curRel  = Math.Abs(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue));
                        double curOff  = curRel > 90.0 ? 180.0 - curRel : curRel;
                        if (curOff > bestOff + 0.01)
                        {
                            best = e;
                        }
                        else if (Math.Abs(curOff - bestOff) <= 0.01)
                        {
                            // Equal off-axis angle (within float tolerance): the adjacency
                            // list contains both the forward exit edge and the reverse edge
                            // of the same taxiway segment. Both fold to the same `off`, so
                            // first-encountered was winning non-deterministically.
                            // Wrong edge → wrong ExitBearingTrue → FindExitExtensionNode
                            // routes backward → permanent max-pan tone for any exit angle.
                            //
                            // Primary tiebreak — lateral direction.
                            //   Use the junction node's signed lateral offset from the
                            //   runway centreline (lateralM: + = right, - = left, already
                            //   computed above). The correct exit edge moves the aircraft
                            //   further off-runway on the SAME side as the junction; the
                            //   reverse edge heads toward the opposite apron or back across
                            //   the runway. lateralComponent = sin(NormalizeAngle(bearing −
                            //   rwyHeading)) gives the signed lateral movement of an edge.
                            //   This criterion is geometrically correct for ALL exit angles:
                            //     7°  exit: correct edge lat≈+0.12, reverse lat≈-0.12
                            //     90° exit: correct edge lat=±1.00, reverse lat=∓1.00
                            //     100° exit: correct edge lat≈±0.98, reverse lat≈∓0.98
                            //   (hemisphere alone would mis-pick the reverse edge for
                            //   obtuse exits 90°–180° where the correct edge is in the
                            //   "backward" hemisphere by the rel≤90 criterion.)
                            //
                            // Fallback tiebreak — hemisphere.
                            //   Applied only when the junction is within 1 m of the
                            //   centreline and lateral direction can't discriminate.
                            //   Forward-hemisphere edges (rel ≤ 90°) beat backward edges;
                            //   correct for acute exits, ambiguous for obtuse exits on the
                            //   centreline (an inherently rare degenerate case).
                            if (Math.Abs(lateralM) > 1.0)
                            {
                                double bestLatComp = Math.Sin(NormalizeAngle(best.BearingDegrees - rwyHeadingTrue) * Math.PI / 180.0);
                                double curLatComp  = Math.Sin(NormalizeAngle(e.BearingDegrees   - rwyHeadingTrue) * Math.PI / 180.0);
                                bool curMatchesSide  = Math.Sign(curLatComp)  == Math.Sign(lateralM);
                                bool bestMatchesSide = Math.Sign(bestLatComp) == Math.Sign(lateralM);
                                if (curMatchesSide && !bestMatchesSide)
                                    best = e;
                            }
                            else
                            {
                                // Junction near centreline: fall back to hemisphere.
                                bool bestForward = bestRel <= 90.0;
                                bool curForward  = curRel  <= 90.0;
                                if (curForward && !bestForward)
                                    best = e;
                            }
                        }
                    }
                }

                if (best != null)
                {
                    taxiwayName = best.TaxiwayName;
                    // Store 360.0 for due-north edges so 0.0 stays unambiguous as "not found".
                    exitBearingTrue = best.BearingDegrees == 0.0 ? 360.0 : best.BearingDegrees;
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

            // For implicit (non-HS) exits whose first named edge is nearly parallel to
            // the runway (< 20°), the edge bearing gives an inadequate pan cue. The BFS
            // apron node — first node found outside the corridor — captures the actual
            // exit direction after the arc, so use node→apron bearing instead when it
            // gives a wider (more useful) angle.
            //
            // Guards (both required):
            //   (a) apronAngle <= NORMAL_MAX_DEG (110°) — forward direction only. A
            //       backward apron-bearing (BFS exited toward the approach end) would
            //       pan the pilot the wrong way.
            //   (b) apronAngle > currentAngleFwd — only override when apron is MORE
            //       off-axis than the existing first-edge bearing. Mirrors the HS-style
            //       override guard at the next block. Without this, an exit whose stub
            //       points further off-runway than its eventual apron node (a curved-
            //       back-toward-centreline shape) would have its bearing narrowed by
            //       the override — a regression vs. the first-edge value.
            //
            // Threshold widened from 5° → 20° so EDDB-style implicit
            // exits with shallow first-edge stubs (e.g. EDDB 24L M3 at 6.9°, LGAV 03R
            // D8/D9 at 7.6°) are covered. Symmetric with the HS-style override gate at
            // the next block (also < 20°). EIDW S5 and other hold-short shallow exits
            // are unaffected — they use the parallel HS-style branch (`isHoldShortNode`
            // gate). EGNX 27/M (90° normal) is above 20°, also unaffected.
            if (!isHoldShortNode && exitAngle < 20.0
                && implicitApronNodeId > 0
                && Nodes.TryGetValue(implicitApronNodeId, out var apronTaxiNode))
            {
                const double MPD_BRG = 111132.0;
                double latRb = (node.Latitude + apronTaxiNode.Latitude) * 0.5 * Math.PI / 180.0;
                double mPLb = MPD_BRG * Math.Cos(latRb);
                double dNb = (apronTaxiNode.Latitude - node.Latitude) * MPD_BRG;
                double dEb = (apronTaxiNode.Longitude - node.Longitude) * mPLb;
                double apronBrg = Math.Atan2(dEb, dNb) * 180.0 / Math.PI;
                if (apronBrg < 0) apronBrg += 360.0;
                double apronAngle = Math.Abs(NormalizeAngle(apronBrg - rwyHeadingTrue));
                // Compare against current exitBearingTrue. 0.0 means no bearing found
                // (sentinel) → treat as -1 so any forward-direction apron wins.
                double currentAngleFwd = exitBearingTrue != 0.0
                    ? Math.Abs(NormalizeAngle(exitBearingTrue - rwyHeadingTrue))
                    : -1.0;
                if (apronAngle <= NORMAL_MAX_DEG && apronAngle > currentAngleFwd)
                    exitBearingTrue = apronBrg == 0.0 ? 360.0 : apronBrg;
            }

            // For HS/IHS exits with shallow angle (<20°), the hold-short marker may sit at
            // the start of a curved RET whose individual segments each run nearly parallel
            // to the runway. The first-edge ExitBearingTrue in those cases is close to
            // runway heading → near-zero tone blend during rollout. Run the same corridor-
            // BFS used for Normal-node implicit exits to find the first node outside the
            // runway strip. Two benefits:
            //   (a) ExitBearingTrue is overridden with the node→apron bearing when it gives
            //       a wider (more useful) angle — clearer pan cue at the RET turn point.
            //   (b) ApronNodeId is set to that node → the Taxiing-handoff re-route fires,
            //       giving A* guidance through the actual curve rather than the apron-network
            //       route computed at touchdown.
            // Threshold 20°: captures all real ICAO Cat E RETs; avoids BFS overhead on
            // standard 60-90° exits where the first-edge bearing is already adequate.
            int hsApronNodeId = -1;
            if (isHoldShortNode && exitAngle < 20.0)
            {
                int bfsResult = ExitPathLeavesCorridor(node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
                if (bfsResult > 0 && Nodes.TryGetValue(bfsResult, out var hsApronNode))
                {
                    hsApronNodeId = bfsResult;
                    const double MPD_BRG_HS = 111132.0;
                    double latRh = (node.Latitude + hsApronNode.Latitude) * 0.5 * Math.PI / 180.0;
                    double mPLh = MPD_BRG_HS * Math.Cos(latRh);
                    double dNh = (hsApronNode.Latitude - node.Latitude) * MPD_BRG_HS;
                    double dEh = (hsApronNode.Longitude - node.Longitude) * mPLh;
                    double hsApronBrg = Math.Atan2(dEh, dNh) * 180.0 / Math.PI;
                    if (hsApronBrg < 0) hsApronBrg += 360.0;
                    double apronAngle = Math.Abs(NormalizeAngle(hsApronBrg - rwyHeadingTrue));
                    // Compare against current ExitBearingTrue. 0.0 means no bearing found
                    // (sentinel) → treat as -1 so any forward-direction apron wins.
                    double currentAngleFwd = exitBearingTrue != 0.0
                        ? Math.Abs(NormalizeAngle(exitBearingTrue - rwyHeadingTrue))
                        : -1.0;
                    if (apronAngle <= NORMAL_MAX_DEG && apronAngle > currentAngleFwd)
                        exitBearingTrue = hsApronBrg == 0.0 ? 360.0 : hsApronBrg;
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
                // HS/IHS exits: normally the hold-short bar is at the junction (apron side).
                // Exception: shallow HS exits on curved RETs — BFS found a corridor-exit node
                // further along the curve (hsApronNodeId > 0). That node is used instead so
                // the Taxiing-handoff re-route drives A* through the actual curve geometry.
                // Fallback Normal exits: BFS result stored in implicitApronNodeId.
                ApronNodeId = isHoldShortNode
                    ? (hsApronNodeId > 0 ? hsApronNodeId : node.NodeId)
                    : implicitApronNodeId,
                Latitude = node.Latitude,
                Longitude = node.Longitude,
                DistanceFromThresholdFeet = distFromLandingThresholdFt,
                DistanceFromTouchdownFeet = distFromLandingThresholdFt - TOUCHDOWN_AIM_FT,
                TaxiwayName = taxiwayName,
                ExitAngleDegrees = exitAngle,
                ExitBearingTrue = exitBearingTrue,
                ExitType = exitType,
                ExitSide = exitBearingTrue != 0.0
                    ? (NormalizeAngle((exitBearingTrue == 360.0 ? 0.0 : exitBearingTrue) - rwyHeadingTrue) >= 0 ? "Right" : "Left")
                    : ""
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

        // High-speed RET dedup: a curved RET whose navdata has multiple HS/IHS nodes
        // along its arc (common in third-party scenery) generates one High-speed entry
        // per node, all more than 50 ft apart — the window above doesn't catch them.
        // The threshold-nearest node is the RET entry point; interior curve nodes are not
        // meaningful separate choices. Normal and End exits keep the 50 ft window only —
        // a Normal taxiway crossing the runway at 90° twice is a legitimate pair.
        {
            var hsSeenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hsDedupedList = new List<LandingExit>(deduped.Count);
            foreach (var e in deduped)
            {
                if (e.ExitType != "High-speed" || string.IsNullOrEmpty(e.TaxiwayName))
                {
                    hsDedupedList.Add(e);
                    continue;
                }
                if (hsSeenNames.Add(e.TaxiwayName)) hsDedupedList.Add(e);
            }
            deduped = hsDedupedList;
        }

        // Fallback-mode extra dedup: curved RETs (e.g. LVFR LEMD) generate many Normal
        // nodes along the same exit curve — all pass ExitPathLeavesCorridor but span
        // hundreds of feet of runway, far beyond the 50 ft window above. When no
        // HS/IHS nodes exist for this runway, keep only the first (threshold-nearest)
        // occurrence per named taxiway. That entry-point node is what matters; interior
        // curve nodes are not meaningful exit choices.
        //
        // Also handles the "HS-only-ends" case: when HS mode yielded only End-type exits,
        // the hold-short data isn't providing useful pre-end exits for this landing direction.
        // Typical cause: a curved RET HSND node designed for the opposite runway direction
        // (e.g. N4 at EIDW — a 28R rapid exit whose node lies inside the 10L corridor) is
        // the only HS node in range, but its exit angle is backward/End for 10L landings.
        // In that case we run a second pass collecting Normal-node fallback exits, merge them
        // with the HS End exits, and return the combined deduplicated list.
        bool hsOnlyEnds = hasHoldShortOnRunway && deduped.Count > 0
            && deduped.TrueForAll(e => e.ExitType == "End");

        // HS nodes exist in corridor but every one failed the distance filter
        // (too close to threshold or beyond END_BUFFER). Treat the same as
        // hsOnlyEnds — run the Normal-node fallback to find usable exits.
        bool hsYieldedNothing = hasHoldShortOnRunway && deduped.Count == 0;

        if (!hasHoldShortOnRunway || hsOnlyEnds || hsYieldedNothing)
        {
            if (hsOnlyEnds || hsYieldedNothing)
            {
                var fallbackExits = new List<LandingExit>();
                foreach (var node in Nodes.Values)
                {
                    if (node.Type != TaxiNodeType.Normal) continue;
                    if (!Adjacency.TryGetValue(node.NodeId, out var ee)) continue;

                    bool hasOffAxis = false;
                    int apronNode = -1;
                    foreach (var ed in ee)
                    {
                        if (string.IsNullOrEmpty(ed.TaxiwayName)) continue;
                        double rel = Math.Abs(NormalizeAngle(ed.BearingDegrees - rwyHeadingTrue));
                        double off = rel > 90.0 ? 180.0 - rel : rel;
                        if (off >= MIN_FALLBACK_EXIT_ANGLE_DEG) { hasOffAxis = true; break; }
                    }
                    if (!hasOffAxis)
                    {
                        apronNode = ExitPathLeavesCorridor(node.NodeId, rwy.StartLat, rwy.StartLon, cosH, sinH, lateralToleranceM);
                        if (apronNode < 0) continue;
                        hasOffAxis = true;
                    }
                    if (!hasOffAxis) continue;

                    const double MPD2 = 111132.0;
                    double latR2 = (rwy.StartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
                    double mPL2 = MPD2 * Math.Cos(latR2);
                    double dN2 = (node.Latitude - rwy.StartLat) * MPD2;
                    double dE2 = (node.Longitude - rwy.StartLon) * mPL2;
                    double aM2 = dE2 * sinH + dN2 * cosH;
                    double lM2 = dE2 * cosH - dN2 * sinH;
                    if (Math.Abs(lM2) > lateralToleranceM || aM2 < 0 || aM2 > lengthM + 50.0) continue;
                    double aFt2 = aM2 / 0.3048;
                    double dft2 = aFt2 - landingThresholdOffsetFt;
                    if (dft2 < MIN_DIST_FT || aFt2 > maxDistFt) continue;

                    string txName2 = "";
                    double angle2 = 90.0;
                    TaxiEdge? best2 = null;
                    double best2Brg = 0.0; // 0 = not found; due-north stored as 360
                    foreach (var e in ee)
                    {
                        if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)
                            || string.IsNullOrEmpty(e.TaxiwayName)) continue;
                        if (best2 == null) { best2 = e; continue; }
                        bool b2hd = HasLetterAndDigit(best2.TaxiwayName);
                        bool ehd  = HasLetterAndDigit(e.TaxiwayName);
                        if (ehd && !b2hd)
                        {
                            best2 = e;
                        }
                        else if (ehd == b2hd)
                        {
                            double b2r = Math.Abs(NormalizeAngle(best2.BearingDegrees - rwyHeadingTrue));
                            double b2o = b2r > 90.0 ? 180.0 - b2r : b2r;
                            double er  = Math.Abs(NormalizeAngle(e.BearingDegrees - rwyHeadingTrue));
                            double eo  = er > 90.0 ? 180.0 - er : er;
                            if (eo > b2o) best2 = e;
                        }
                    }
                    if (best2 != null)
                    {
                        txName2 = best2.TaxiwayName;
                        best2Brg = best2.BearingDegrees == 0.0 ? 360.0 : best2.BearingDegrees;
                        double rel2 = Math.Abs(NormalizeAngle(best2.BearingDegrees - rwyHeadingTrue));
                        bool pb2 = rel2 > 90.0;
                        angle2 = pb2 ? 180.0 - rel2 : rel2;
                        if (pb2 && angle2 < 50.0) angle2 = NORMAL_MAX_DEG + 20.0;
                    }
                    // Same targeted apron-bearing override: only for near-parallel first
                    // edges (< 5°) and only when the apron is in the forward direction.
                    if (angle2 < 5.0 && apronNode > 0
                        && Nodes.TryGetValue(apronNode, out var apronTaxiNode2))
                    {
                        const double MPD_BRG2 = 111132.0;
                        double latRc = (node.Latitude + apronTaxiNode2.Latitude) * 0.5 * Math.PI / 180.0;
                        double mPLc = MPD_BRG2 * Math.Cos(latRc);
                        double dNc = (apronTaxiNode2.Latitude - node.Latitude) * MPD_BRG2;
                        double dEc = (apronTaxiNode2.Longitude - node.Longitude) * mPLc;
                        double apronBrg2 = Math.Atan2(dEc, dNc) * 180.0 / Math.PI;
                        if (apronBrg2 < 0) apronBrg2 += 360.0;
                        if (Math.Abs(NormalizeAngle(apronBrg2 - rwyHeadingTrue)) <= NORMAL_MAX_DEG)
                            best2Brg = apronBrg2 == 0.0 ? 360.0 : apronBrg2;
                    }

                    double er2 = aFt2 / rwy.Length;
                    string et2 = er2 > END_RATIO ? "End"
                        : angle2 <= HIGH_SPEED_MAX_DEG ? "High-speed"
                        : angle2 <= NORMAL_MAX_DEG ? "Normal"
                        : "End";

                    fallbackExits.Add(new LandingExit
                    {
                        NodeId = node.NodeId,
                        ApronNodeId = apronNode,
                        Latitude = node.Latitude,
                        Longitude = node.Longitude,
                        DistanceFromThresholdFeet = dft2,
                        DistanceFromTouchdownFeet = dft2 - TOUCHDOWN_AIM_FT,
                        TaxiwayName = txName2,
                        ExitAngleDegrees = angle2,
                        ExitBearingTrue = best2Brg,
                        ExitType = et2,
                        ExitSide = best2Brg != 0.0
                            ? (NormalizeAngle((best2Brg == 360.0 ? 0.0 : best2Brg) - rwyHeadingTrue) >= 0 ? "Right" : "Left")
                            : ""
                    });
                }

                if (fallbackExits.Count > 0)
                {
                    var merged = new List<LandingExit>(deduped.Count + fallbackExits.Count);
                    merged.AddRange(deduped);
                    merged.AddRange(fallbackExits);
                    merged.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));
                    deduped = new List<LandingExit>(merged.Count);
                    foreach (var e in merged)
                    {
                        bool wasMerged = false;
                        for (int i = deduped.Count - 1; i >= 0; i--)
                        {
                            var d = deduped[i];
                            if (Math.Abs(d.DistanceFromThresholdFeet - e.DistanceFromThresholdFeet) > DEDUP_WINDOW_FT) break;
                            if (string.Equals(d.TaxiwayName, e.TaxiwayName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (e.ExitAngleDegrees < d.ExitAngleDegrees) deduped[i] = e;
                                wasMerged = true; break;
                            }
                        }
                        if (!wasMerged) deduped.Add(e);
                    }
                }
            }

            // Name dedup: keep only first (threshold-nearest) occurrence per taxiway name.
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dedupedFallback = new List<LandingExit>(deduped.Count);
            foreach (var e in deduped)
            {
                if (string.IsNullOrEmpty(e.TaxiwayName)) { dedupedFallback.Add(e); continue; }
                if (seenNames.Add(e.TaxiwayName)) dedupedFallback.Add(e);
            }
            return dedupedFallback;
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

    // BFS from startNodeId. Returns the node ID of the first reachable node that lies
    // outside the runway lateral corridor (|lateral| > lateralToleranceM) within
    // MAX_RET_SEARCH_M metres, or -1 if none found.
    // Used to detect smooth-curve RETs whose individual segments each fall below the
    // MIN_FALLBACK_EXIT_ANGLE_DEG threshold yet still exit the runway.
    //
    // The returned node ID is used as ApronNodeId on the LandingExit so the
    // LandingRollout → Taxiing handoff can re-route from the pilot's live position
    // to that corridor-exit point, giving correct tone guidance through the curve.
    //
    // Seeding: only named-taxiway edges from the start node (the node must be a real
    // taxiway junction, not just an unnamed runway surface waypoint).
    // Traversal: all edges — named and unnamed — so the BFS can cross unnamed connector
    // segments that some scenery packages insert between the named RET portions.
    //
    // A truly parallel taxiway that never leaves the runway strip returns -1. A real
    // RET — however shallow the departure angle — returns its first corridor-exit node.
    private int ExitPathLeavesCorridor(
        int startNodeId,
        double rwyStartLat, double rwyStartLon,
        double cosH, double sinH,
        double lateralToleranceM)
    {
        const double MAX_RET_SEARCH_M = 600.0;
        const double METERS_PER_DEG_LAT = 111132.0;

        if (!Adjacency.TryGetValue(startNodeId, out var initEdges)) return -1;

        // Require at least one named adjacent edge — node must be a taxiway junction.
        bool hasNamedStart = false;
        foreach (var e in initEdges)
            if (!string.IsNullOrEmpty(e.TaxiwayName)) { hasNamedStart = true; break; }
        if (!hasNamedStart) return -1;

        var visited = new HashSet<int> { startNodeId };
        var queue = new Queue<(int nodeId, double dist)>();

        // Seed from named edges only.
        foreach (var e in initEdges)
        {
            if (!string.IsNullOrEmpty(e.TaxiwayName))
                queue.Enqueue((e.ToNodeId, e.DistanceMeters));
        }

        while (queue.Count > 0)
        {
            var (nodeId, dist) = queue.Dequeue();
            if (visited.Contains(nodeId)) continue;
            visited.Add(nodeId);

            if (!Nodes.TryGetValue(nodeId, out var node)) continue;

            double latR = (rwyStartLat + node.Latitude) * 0.5 * Math.PI / 180.0;
            double mPerLon = METERS_PER_DEG_LAT * Math.Cos(latR);
            double dN = (node.Latitude - rwyStartLat) * METERS_PER_DEG_LAT;
            double dE = (node.Longitude - rwyStartLon) * mPerLon;
            double lateralM = Math.Abs(dE * cosH - dN * sinH);

            if (lateralM > lateralToleranceM) return nodeId;

            if (dist >= MAX_RET_SEARCH_M) continue;

            if (!Adjacency.TryGetValue(nodeId, out var edges)) continue;
            foreach (var e in edges)
            {
                // Follow all edges (named and unnamed) so unnamed connector segments
                // between the named portions of a RET don't break the chain.
                if (visited.Contains(e.ToNodeId)) continue;
                double newDist = dist + e.DistanceMeters;
                if (newDist <= MAX_RET_SEARCH_M)
                    queue.Enqueue((e.ToNodeId, newDist));
            }
        }
        return -1;
    }

    #endregion
}
