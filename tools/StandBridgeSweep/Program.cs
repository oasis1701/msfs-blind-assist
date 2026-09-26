// StandBridgeSweep — re-measures PR #235's stand-bridge headline figures against the real
// navdata database, over the REAL production TaxiGraph.Build / RunwayShape / RunwayPavement
// (linked, not reimplemented — see the .csproj header comment). This is a MEASUREMENT tool
// only: it reads the database read-only and writes nothing back to it or to production code.
//
// Usage: StandBridgeSweep.exe [databasePath] [maxAirports]
//   databasePath  defaults to the pilot's fs2024 database, resolved as the app resolves it
//                 (NavdataSweepLoader.DefaultDatabasePath: canonical, then the legacy FBWBA folder)
//   maxAirports   optional cap for a quick smoke run; omit (or 0) to sweep every airport

using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

string dbPath = args.Length > 0 ? args[0] : NavdataSweepLoader.DefaultDatabasePath;
int maxAirports = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 0;

var overallSw = Stopwatch.StartNew();
Console.WriteLine($"Database: {dbPath}");
if (!File.Exists(dbPath))
{
    Console.WriteLine("ERROR: database file does not exist.");
    return 1;
}

using var conn = NavdataSweepLoader.OpenReadOnly(dbPath);

// The bulk load, shared with tools/LandingExitSweep (tools/Shared/NavdataSweepLoader.cs, linked).
var navdata = NavdataSweepLoader.Load(conn);
var airportLabel = navdata.AirportLabel;
var pathsByAirport = navdata.PathsByAirport;
var parkingByAirport = navdata.ParkingByAirport;
var startsByAirport = navdata.StartsByAirport;
var runwaysByAirport = navdata.RunwaysByAirport;
var sw = Stopwatch.StartNew();

// ---------------------------------------------------------------------------------------
// Reflection handles for two TaxiGraph private members, invoked (never reimplemented) on the
// already-built graph so two of the identity-dependent checks run the REAL production code:
//
//  - ResolveNode(lat, lon, type, taxiwayName): the exact same node-merge lookup Build's own
//    endpoint-identity loop calls. Re-invoking it, AFTER Build has finished, with the same
//    (lat, lon) a taxi_path row originally carried is safe and exact — it cannot create a new
//    node (Build already created every node these coordinates could resolve to) and always
//    re-hits the same spatial-hash bucket, so it returns the IDENTICAL node id Build's own
//    RecordEndpointIdentity call captured. This replaces an earlier FindNearestNode + 1.5m
//    proximity approximation, which was found (via the cross-check below) to sometimes pick a
//    different node than ResolveNode would at a close boundary between two nodes — the
//    approximation is retired rather than trusted silently. The taxiwayName argument is passed
//    as "" because it is provably irrelevant to which node id is returned (see ResolveNode's
//    body: it only ever feeds a TaxiwayNames.Add side effect, already applied identically
//    during the real Build, never the merge/lookup decision).
//  - MarkStandLeadInChains(HashSet<int>): the real chain-walk, fed the EXACT standNodes set
//    ResolveNode above reconstructs (not an approximation), so its answer is the real one Build
//    itself would have computed for BridgeOrphanParkingIslands to filter against.
// ---------------------------------------------------------------------------------------
var resolveNodeMethod = typeof(TaxiGraph).GetMethod(
    "ResolveNode", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("ResolveNode not found via reflection — TaxiGraph.cs signature changed?");
var markStandLeadInChainsMethod = typeof(TaxiGraph).GetMethod(
    "MarkStandLeadInChains", BindingFlags.NonPublic | BindingFlags.Instance)
    ?? throw new InvalidOperationException("MarkStandLeadInChains not found via reflection — TaxiGraph.cs signature changed?");

var stats = new SweepStats();
var airportIds = pathsByAirport.Keys.ToList();
airportIds.Sort();
if (maxAirports > 0 && airportIds.Count > maxAirports)
    airportIds = airportIds.Take(maxAirports).ToList();

Console.WriteLine();
Console.WriteLine($"Sweeping {airportIds.Count} airports with taxi_path rows...");
sw.Restart();

int processed = 0;
foreach (var apId in airportIds)
{
    var paths = pathsByAirport[apId];
    var parkingSpots = parkingByAirport.TryGetValue(apId, out var ps) ? ps : new List<ParkingSpot>();
    var starts = startsByAirport.TryGetValue(apId, out var ss) ? ss : new List<StartPosition>();
    var runways = runwaysByAirport.TryGetValue(apId, out var rr) ? rr : new List<Runway>();
    var (icao, ident) = airportLabel.TryGetValue(apId, out var lbl) ? lbl : ("", "");
    string label = string.IsNullOrEmpty(icao) ? ident : icao;

    TaxiGraph graph;
    try
    {
        graph = TaxiGraph.Build(paths, parkingSpots, starts, runways);
    }
    catch (Exception ex)
    {
        stats.BuildFailures.Add((apId, label, ex.Message));
        continue;
    }

    AnalyzeAirport(apId, label, graph, paths, parkingSpots, stats, resolveNodeMethod, markStandLeadInChainsMethod);

    processed++;
    if (processed % 2000 == 0)
        Console.WriteLine($"  ... {processed}/{airportIds.Count} ({sw.ElapsedMilliseconds} ms elapsed)");
}

Console.WriteLine($"Sweep complete: {processed} airports built ({sw.ElapsedMilliseconds} ms, {stats.BuildFailures.Count} build failures)");
Console.WriteLine();

// Per-airport bridge-count CSV (item 8: diffed externally against a same-shape CSV built from
// the pre-review-fix geometry, commit c1403ce1, to find airports whose bridge count moved).
{
    string csvPath = Path.Combine(AppContext.BaseDirectory, "new_bridge_counts.csv");
    using var csv = new StreamWriter(csvPath);
    csv.WriteLine("airport_id,label,bridge_count");
    foreach (var g in stats.BridgeDetails.GroupBy(d => (d.AirportId, d.Label)).OrderBy(g => g.Key.AirportId))
        csv.WriteLine($"{g.Key.AirportId},{g.Key.Label},{g.Count()}");
    Console.WriteLine($"Per-airport bridge-count CSV written: {csvPath}");
}

// ---------------------------------------------------------------------------------------
// ZBAT / OMDB spot checks
// ---------------------------------------------------------------------------------------
Console.WriteLine("=== ZBAT check ===");
foreach (var apId in FindAirportIds(airportLabel, "ZBAT"))
{
    if (!pathsByAirport.TryGetValue(apId, out var paths)) { Console.WriteLine($"  airport_id {apId}: no taxi_path rows"); continue; }
    var parkingSpots = parkingByAirport.TryGetValue(apId, out var ps) ? ps : new List<ParkingSpot>();
    var starts = startsByAirport.TryGetValue(apId, out var ss) ? ss : new List<StartPosition>();
    var runways = runwaysByAirport.TryGetValue(apId, out var rr) ? rr : new List<Runway>();
    Console.WriteLine($"  airport_id {apId}: runway rows(both ends)={runways.Count}, widths(ft)=[{string.Join(",", runways.Select(x => x.Width).Distinct())}]");
    var graph = TaxiGraph.Build(paths, parkingSpots, starts, runways);
    int bridgeCount = CountDistinctBridges(graph);
    int mainCount = graph.Nodes.Values.Count(n => n.ComponentId == graph.MainComponentId);
    var shapes = RunwayPavement.BuildShapes(graph.RunwayCenterlines);
    int mainOnPavement = graph.Nodes.Values.Count(n => n.ComponentId == graph.MainComponentId
        && RunwayPavement.IsOnPavement(n.Latitude, n.Longitude, shapes));
    Console.WriteLine($"  bridges built: {bridgeCount}");
    Console.WriteLine($"  total nodes: {graph.Nodes.Count}; main component size: {mainCount} nodes; of those, on-pavement: {mainOnPavement}");
    Console.WriteLine($"  runway centerlines: {graph.RunwayCenterlines.Count}, shapes (non-degenerate): {shapes.Count}");
    foreach (var shape in shapes)
    {
        Console.WriteLine($"    shape {shape.Name1}/{shape.Name2}: usesPavement={shape.UsesPavement}, halfWidthM={shape.HalfWidthMeters:F2}, lengthM={shape.LengthMeters:F1}, extent=[{shape.ExtentMinMeters:F1},{shape.ExtentMaxMeters:F1}]");
    }
    // Per-node lateral/along distance to the (single, presumably) shape, to see the actual spread.
    if (shapes.Count > 0)
    {
        var shape0 = shapes[0];
        var mainNodes = graph.Nodes.Values.Where(n => n.ComponentId == graph.MainComponentId).ToList();
        var proj = mainNodes.Select(n => shape0.Project(n.Latitude, n.Longitude)).ToList();
        Console.WriteLine($"    main-component nodes vs shape0: along range=[{proj.Min(p => p.Along):F1},{proj.Max(p => p.Along):F1}], |lateral| range=[{proj.Min(p => Math.Abs(p.Lateral)):F1},{proj.Max(p => Math.Abs(p.Lateral)):F1}]");
    }
}

Console.WriteLine();
Console.WriteLine("=== OMDB C 51L check ===");
foreach (var apId in FindAirportIds(airportLabel, "OMDB"))
{
    if (!pathsByAirport.TryGetValue(apId, out var paths)) { Console.WriteLine($"  airport_id {apId}: no taxi_path rows"); continue; }
    var parkingSpots = parkingByAirport.TryGetValue(apId, out var ps) ? ps : new List<ParkingSpot>();
    var starts = startsByAirport.TryGetValue(apId, out var ss) ? ss : new List<StartPosition>();
    var runways = runwaysByAirport.TryGetValue(apId, out var rr) ? rr : new List<Runway>();
    var graph = TaxiGraph.Build(paths, parkingSpots, starts, runways);

    var target = parkingSpots.FirstOrDefault(s => TaxiGraph.FormatParkingDisplayName(s).Equals("C 51L", StringComparison.OrdinalIgnoreCase));
    if (target == null)
    {
        Console.WriteLine($"  airport_id {apId}: no parking spot displays as \"C 51L\" (have {parkingSpots.Count} spots; sample names: {string.Join(",", parkingSpots.Select(TaxiGraph.FormatParkingDisplayName).Where(n => n.StartsWith("C ")).Take(10))})");
        continue;
    }
    var node = graph.FindNearestNode(target.Latitude, target.Longitude);
    double snapDist = node == null ? -1 : TaxiGraph.FastDistanceMeters(node.Latitude, node.Longitude, target.Latitude, target.Longitude);
    bool isBridged = node != null && graph.Adjacency.TryGetValue(node.NodeId, out var edges) && edges.Any(TaxiGraph.IsStandBridge);
    bool inMain = node != null && node.ComponentId == graph.MainComponentId;
    int islandSize = node != null ? graph.Nodes.Values.Count(n => n.ComponentId == node.ComponentId) : -1;
    Console.WriteLine($"  airport_id {apId}: node={(node?.NodeId.ToString() ?? "null")}, snapDistM={snapDist:F1}, nodeParkingName=\"{node?.ParkingName}\", componentId={(node?.ComponentId.ToString() ?? "n/a")} (size {islandSize}), mainComponentId={graph.MainComponentId}, inMainComponent={inMain}, touchedByStandBridgeEdge={isBridged}");
}

Console.WriteLine();
Console.WriteLine("=== SUMMARY ===");
stats.PrintSummary(overallSw.Elapsed);

return 0;

// =========================================================================================
// Local functions
// =========================================================================================

static IEnumerable<int> FindAirportIds(Dictionary<int, (string Icao, string Ident)> labels, string code)
{
    foreach (var kv in labels)
        if (string.Equals(kv.Value.Icao, code, StringComparison.OrdinalIgnoreCase)
            || string.Equals(kv.Value.Ident, code, StringComparison.OrdinalIgnoreCase))
            yield return kv.Key;
}

static int CountDistinctBridges(TaxiGraph graph)
{
    var seen = new HashSet<(int, int)>();
    foreach (var edges in graph.Adjacency.Values)
        foreach (var e in edges)
            if (TaxiGraph.IsStandBridge(e))
                seen.Add((Math.Min(e.FromNodeId, e.ToNodeId), Math.Max(e.FromNodeId, e.ToNodeId)));
    return seen.Count;
}

// Generic BFS + main-component tie-break over a TaxiGraph's own public Nodes/Adjacency.
// This is NOT a reimplementation of any TaxiGraph business rule — it is the same
// well-defined graph-connectivity computation TaxiGraph.AssignConnectedComponents /
// ComputeMainComponentId perform (verbatim tie-break rule: largest component by node
// count, ties broken on the member node with the smallest (latitude, longitude) pair —
// copied from TaxiGraph.cs's own doc comment on ComputeMainComponentId, since that method
// is private and cannot be reused directly). It exists ONLY because Build does not expose
// the PRE-bridge component partition; the POST-bridge case (excludeEdge: null) is a pure
// cross-check and is asserted to equal graph.MainComponentId for every airport swept (see
// AnalyzeAirport) as evidence this replica is faithful before trusting its pre-bridge answer.
static (Dictionary<int, int> ComponentOf, int MainComponentId) ComputeComponents(
    TaxiGraph graph, Func<TaxiEdge, bool>? excludeEdge)
{
    var componentOf = new Dictionary<int, int>();
    int next = 0;
    var queue = new Queue<int>();
    foreach (var node in graph.Nodes.Values)
    {
        if (componentOf.ContainsKey(node.NodeId)) continue;
        int cid = next++;
        componentOf[node.NodeId] = cid;
        queue.Enqueue(node.NodeId);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (!graph.Adjacency.TryGetValue(cur, out var edges)) continue;
            foreach (var e in edges)
            {
                if (excludeEdge != null && excludeEdge(e)) continue;
                if (!componentOf.ContainsKey(e.ToNodeId))
                {
                    componentOf[e.ToNodeId] = cid;
                    queue.Enqueue(e.ToNodeId);
                }
            }
        }
    }

    var sizes = new Dictionary<int, int>();
    foreach (var cid in componentOf.Values)
        sizes[cid] = sizes.GetValueOrDefault(cid) + 1;
    int mainSize = -1;
    foreach (var s in sizes.Values) if (s > mainSize) mainSize = s;

    int mainId = -1;
    double bestLat = double.MaxValue, bestLon = double.MaxValue;
    foreach (var node in graph.Nodes.Values)
    {
        int cid = componentOf[node.NodeId];
        if (sizes[cid] != mainSize) continue;
        if (node.Latitude > bestLat || (node.Latitude == bestLat && node.Longitude >= bestLon)) continue;
        bestLat = node.Latitude; bestLon = node.Longitude; mainId = cid;
    }
    return (componentOf, mainId);
}

// EXACT reconstruction of Build's own RecordEndpointIdentity, by reflectively re-invoking the
// real private ResolveNode for every taxi_path row endpoint in the same order Build used (see
// the reflection setup comment above for why this is exact, not an approximation) and applying
// RecordEndpointIdentity's own switch (MapNodeType's mapping, copied verbatim — "HS"/"HSND" ->
// HoldShort, "IHS"/"IHSND" -> ILSHoldShort, "P" -> Parking; trivial data classification, not
// graph-building logic) to the returned node id. This is deliberately NOT TaxiNode.Type, which
// the later parking-spot proximity pass (and, within the taxi_path loop itself, UpgradeNodeType's
// single highest-wins field) can make lie about a node also being a hold-short — exactly why
// CLAUDE.md says not to use it here.
static (HashSet<int> StandNodes, HashSet<int> HoldShortNodes) ReconstructNavdataIdentity(
    TaxiGraph graph, List<TaxiPath> paths, MethodInfo resolveNode)
{
    var standNodes = new HashSet<int>();
    var holdShortNodes = new HashSet<int>();

    void Record(string type, double lat, double lon)
    {
        int nodeId = (int)resolveNode.Invoke(graph, new object?[] { lat, lon, type ?? "", "" })!;
        switch ((type ?? "").ToUpperInvariant())
        {
            case "P": standNodes.Add(nodeId); break;
            case "HS": case "HSND": case "IHS": case "IHSND": holdShortNodes.Add(nodeId); break;
        }
    }

    foreach (var p in paths)
    {
        Record(p.StartType, p.StartLat, p.StartLon);
        Record(p.EndType, p.EndLat, p.EndLon);
    }
    return (standNodes, holdShortNodes);
}

void AnalyzeAirport(
    int apId, string label, TaxiGraph graph, List<TaxiPath> paths, List<ParkingSpot> parkingSpots,
    SweepStats s, MethodInfo resolveNode, MethodInfo markStandLeadInChains)
{
    // ---- bridge inventory for this airport ----
    var bridgeEdgePairs = new HashSet<(int A, int B)>();
    foreach (var edges in graph.Adjacency.Values)
        foreach (var e in edges)
            if (TaxiGraph.IsStandBridge(e))
                bridgeEdgePairs.Add((Math.Min(e.FromNodeId, e.ToNodeId), Math.Max(e.FromNodeId, e.ToNodeId)));

    if (bridgeEdgePairs.Count == 0)
    {
        // Still run the (cheap) reachability accounting below for airports with parking,
        // since item 5 needs the full population, not just bridged airports.
    }
    else
    {
        s.AirportsWithBridges.Add(apId);
    }
    s.TotalBridges += bridgeEdgePairs.Count;

    if (bridgeEdgePairs.Count > 0)
    {
        // Pre-bridge components, to label island vs. main endpoint per bridge and to answer
        // item 5 (reachability before bridging). Cross-checked below against the graph's own
        // post-bridge MainComponentId via the same generic algorithm run with no exclusion.
        var (preComponentOf, preMainId) = ComputeComponents(graph, e => TaxiGraph.IsStandBridge(e));
        var (postComponentOf, postMainId) = ComputeComponents(graph, null);
        if (postMainId == -1 || graph.MainComponentId == -1
            || postComponentOf.Count == 0
            || postComponentOf.Values.Count(c => c == postMainId) != graph.Nodes.Values.Count(n => n.ComponentId == graph.MainComponentId))
        {
            s.MainComponentCrossCheckMismatches.Add(apId);
        }

        var shapes = RunwayPavement.BuildShapes(graph.RunwayCenterlines);
        var (standNodes, holdShortNodes) = ReconstructNavdataIdentity(graph, paths, resolveNode);
        HashSet<int>? leadInChainNodes = null;
        try
        {
            // BridgeOrphanParkingIslands calls the real MarkStandLeadInChains exactly ONCE,
            // before its own bridging loop adds any StandBridgePathType edge — so the walk it
            // filters IsEligibleMainEndpoint against never sees a bridge edge. Our reflective
            // call runs AFTER Build has finished (every bridge already added), so without this
            // strip/restore the walk would see the just-added bridge at a candidate's own
            // node — changing that node's DEGREE (e.g. a pre-bridge dead end becomes a 2-neighbour
            // pass-through, or a pre-bridge chain node becomes a 3-neighbour junction) relative
            // to what the real internal call saw, and marking (or failing to mark) nodes the
            // production decision never considered. Confirmed as the actual cause, not a
            // hypothesis: with the strip/restore removed, this harness reported 28 bridges
            // "ending on a lead-in chain" against a filter that structurally cannot admit one;
            // restoring the exact pre-bridge adjacency below is what makes this check trustworthy.
            var removedBridgeEdges = new Dictionary<int, List<TaxiEdge>>();
            foreach (var kv in graph.Adjacency)
            {
                var bridgeEdges = kv.Value.Where(TaxiGraph.IsStandBridge).ToList();
                if (bridgeEdges.Count == 0) continue;
                removedBridgeEdges[kv.Key] = bridgeEdges;
                kv.Value.RemoveAll(TaxiGraph.IsStandBridge);
            }
            try
            {
                leadInChainNodes = (HashSet<int>?)markStandLeadInChains.Invoke(graph, new object[] { standNodes });
            }
            finally
            {
                foreach (var kv in removedBridgeEdges)
                    graph.Adjacency[kv.Key].AddRange(kv.Value);
            }
        }
        catch (Exception ex)
        {
            s.LeadInChainReflectionFailures.Add((apId, label, ex.Message));
        }

        foreach (var (a, b) in bridgeEdgePairs)
        {
            var nodeA = graph.Nodes[a];
            var nodeB = graph.Nodes[b];

            bool aIsMain = preComponentOf.TryGetValue(a, out var ca) && ca == preMainId;
            bool bIsMain = preComponentOf.TryGetValue(b, out var cb) && cb == preMainId;

            TaxiNode mainNode, islandNode;
            if (aIsMain && !bIsMain) { mainNode = nodeA; islandNode = nodeB; }
            else if (bIsMain && !aIsMain) { mainNode = nodeB; islandNode = nodeA; }
            else
            {
                s.AmbiguousBridgeEndpoints.Add((apId, label, a, b, ca, cb, preMainId));
                continue;
            }

            // 2. touches runway pavement (the exact check ChooseBridgePair used to admit it)
            bool touchesPavement = RunwayPavement.SegmentTouchesPavement(
                islandNode.Latitude, islandNode.Longitude, mainNode.Latitude, mainNode.Longitude,
                shapes, out _);
            if (touchesPavement) s.BridgesTouchingPavement++;

            // 3. ends on a hold-short node / a stand (navdata endpoint-type identity, not TaxiNode.Type)
            bool mainIsHoldShort = holdShortNodes.Contains(mainNode.NodeId);
            bool islandIsHoldShort = holdShortNodes.Contains(islandNode.NodeId);
            bool eitherIsStand = standNodes.Contains(mainNode.NodeId) || standNodes.Contains(islandNode.NodeId);
            if (mainIsHoldShort) s.BridgesNetworkEndOnHoldShort++;
            if (islandIsHoldShort) s.BridgesIslandEndOnHoldShort++;
            if (eitherIsStand) s.BridgesEndingOnStand++;

            // "ending part-way up another stand's lead-in chain" — real production walk via
            // reflection (see ReconstructNavdataIdentity's doc for why the INPUT is ours but
            // the WALK is not).
            if (leadInChainNodes != null && leadInChainNodes.Contains(mainNode.NodeId))
                s.BridgesEndingOnLeadInChain++;

            // 4. network end within 150 m of a runway centreline (current/authoritative shape)
            double bestDist = double.MaxValue;
            foreach (var shape in shapes)
            {
                double d = TaxiGraph.PerpendicularDistanceMetersStatic(
                    mainNode.Latitude, mainNode.Longitude, shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2);
                if (d < bestDist) bestDist = d;
            }
            if (bestDist <= 150.0) s.NetworkEndsNear150mOfCenterline++;

            s.BridgeDetails.Add(new BridgeDetail(apId, label, islandNode.NodeId, mainNode.NodeId,
                touchesPavement, mainIsHoldShort, islandIsHoldShort, eitherIsStand,
                leadInChainNodes != null && leadInChainNodes.Contains(mainNode.NodeId), bestDist));
        }
    }

    // ---- item 5: stand reachability by the main component, before vs after bridging ----
    if (parkingSpots.Count > 0)
    {
        var (preComponentOf, preMainId) = bridgeEdgePairs.Count > 0
            ? ComputeComponents(graph, e => TaxiGraph.IsStandBridge(e))
            : (graph.Nodes.Values.ToDictionary(n => n.NodeId, n => n.ComponentId), graph.MainComponentId);

        foreach (var spot in parkingSpots)
        {
            var node = graph.FindNearestNode(spot.Latitude, spot.Longitude);
            if (node == null) continue;
            double dist = TaxiGraph.FastDistanceMeters(node.Latitude, node.Longitude, spot.Latitude, spot.Longitude);
            if (dist >= 100.0) continue; // not matched to any node — outside Build's own "is this a stand" model

            s.TotalMatchedStands++;
            bool preReachable = preComponentOf.TryGetValue(node.NodeId, out var pc) && pc == preMainId;
            bool postReachable = node.ComponentId == graph.MainComponentId;

            if (!preReachable)
            {
                s.StandsUnreachablePreBridge++;
                s.AirportsWithUnreachableStands.Add(apId);
                if (postReachable) s.StandsBroughtOnByBridges++;
            }
        }
    }
}

// =========================================================================================
// Types
// =========================================================================================

record BridgeDetail(int AirportId, string Label, int IslandNodeId, int MainNodeId,
    bool TouchesPavement, bool MainIsHoldShort, bool IslandIsHoldShort, bool EitherIsStand,
    bool MainOnLeadInChain, double NearestCenterlineMeters);

class SweepStats
{
    public HashSet<int> AirportsWithBridges { get; } = new();
    public int TotalBridges;
    public int BridgesTouchingPavement;
    public int BridgesNetworkEndOnHoldShort;
    public int BridgesIslandEndOnHoldShort;
    public int BridgesEndingOnStand;
    public int BridgesEndingOnLeadInChain;
    public int NetworkEndsNear150mOfCenterline;

    public int TotalMatchedStands;
    public int StandsUnreachablePreBridge;
    public int StandsBroughtOnByBridges;
    public HashSet<int> AirportsWithUnreachableStands { get; } = new();

    public List<BridgeDetail> BridgeDetails { get; } = new();
    public List<(int ApId, string Label, string Message)> BuildFailures { get; } = new();
    public List<(int ApId, string Label, string Message)> LeadInChainReflectionFailures { get; } = new();
    public List<(int ApId, string Label, int A, int B, int Ca, int Cb, int PreMain)> AmbiguousBridgeEndpoints { get; } = new();
    public HashSet<int> MainComponentCrossCheckMismatches { get; } = new();

    public void PrintSummary(TimeSpan elapsed)
    {
        Console.WriteLine($"Elapsed: {elapsed}");
        Console.WriteLine($"Total bridges built: {TotalBridges}");
        Console.WriteLine($"Distinct airports with >=1 bridge: {AirportsWithBridges.Count}");
        Console.WriteLine($"Bridges touching runway pavement: {BridgesTouchingPavement}");
        Console.WriteLine($"Bridges with network end on a hold-short node: {BridgesNetworkEndOnHoldShort}");
        Console.WriteLine($"Bridges with island end on a hold-short node: {BridgesIslandEndOnHoldShort}");
        Console.WriteLine($"Bridges ending on a stand (either end): {BridgesEndingOnStand}");
        Console.WriteLine($"Bridges with network end part-way up another stand's lead-in chain: {BridgesEndingOnLeadInChain}");
        Console.WriteLine($"Network ends within 150 m of a runway centreline: {NetworkEndsNear150mOfCenterline}");
        Console.WriteLine();
        Console.WriteLine($"Matched stands (ParkingSpot within 100m of a node) swept: {TotalMatchedStands}");
        Console.WriteLine($"  unreachable by main component BEFORE bridging: {StandsUnreachablePreBridge} at {AirportsWithUnreachableStands.Count} airports");
        Console.WriteLine($"  of those, brought onto the main component BY bridging: {StandsBroughtOnByBridges}");
        Console.WriteLine();
        Console.WriteLine($"Build failures: {BuildFailures.Count}");
        foreach (var f in BuildFailures.Take(10)) Console.WriteLine($"  {f.Label} (airport_id {f.ApId}): {f.Message}");
        Console.WriteLine($"MarkStandLeadInChains reflection failures: {LeadInChainReflectionFailures.Count}");
        foreach (var f in LeadInChainReflectionFailures.Take(10)) Console.WriteLine($"  {f.Label} (airport_id {f.ApId}): {f.Message}");
        Console.WriteLine($"Ambiguous bridge endpoint classifications (neither/both side pre-bridge-main): {AmbiguousBridgeEndpoints.Count}");
        foreach (var a in AmbiguousBridgeEndpoints.Take(10))
            Console.WriteLine($"  {a.Label} (airport_id {a.ApId}): nodes {a.A}/{a.B}, components {a.Ca}/{a.Cb}, preMain {a.PreMain}");
        Console.WriteLine($"Main-component cross-check mismatches (my BFS replica vs graph.MainComponentId): {MainComponentCrossCheckMismatches.Count}");

        Console.WriteLine();
        Console.WriteLine("Airports with bridges (label, count):");
        var byAirport = BridgeDetails.GroupBy(d => (d.AirportId, d.Label)).OrderByDescending(g => g.Count());
        foreach (var g in byAirport.Take(30))
            Console.WriteLine($"  {g.Key.Label} (airport_id {g.Key.AirportId}): {g.Count()} bridge(s)");
        if (byAirport.Count() > 30) Console.WriteLine($"  ... and {byAirport.Count() - 30} more airports");
    }
}
