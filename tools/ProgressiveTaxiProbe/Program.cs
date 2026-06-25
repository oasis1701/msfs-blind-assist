using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

// ProgressiveTaxiProbe — asserts FindTaxiwayIntersectionNode and FindTaxiwayEndNode
// on a hand-built synthetic graph.
//
// Run:  dotnet run --project tools/ProgressiveTaxiProbe -p:Platform=x64
// Exit 0 = all checks pass.

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    if (!ok) failures++;
}

// ---------------------------------------------------------------------------
// Synthetic graph layout (component 0)
//
//  Node 1 ---[A]--- Node 2 ---[A]--- Node 3 ---[A]--- Node 4 ---[A]--- Node 5
//                                                                          |
//                                                                         [B]
//                                                                          |
//                                                                        Node 6
//
// Nodes 1..5 lie on a straight east-west line (lat 40.0, lon increasing 0.001
// per step ≈ 80 m apart). Node 5 also has an outgoing [B] edge south to node 6.
// Node 5 is therefore the A∩B intersection node.
//
// Component 1 (isolated)
//  Node 7 ---[C]--- Node 8 (different component — must not be returned for comp 0)
// ---------------------------------------------------------------------------

var g = new TaxiGraph();

// Helper: add a node to the graph
TaxiNode AddNode(int id, double lat, double lon, int comp)
{
    var n = new TaxiNode { NodeId = id, Latitude = lat, Longitude = lon, ComponentId = comp };
    g.Nodes[id] = n;
    g.Adjacency[id] = new List<TaxiEdge>();
    return n;
}

// Helper: add a directed edge (one direction only — we add both for undirected)
void AddEdge(int from, int to, string tw)
{
    if (!g.Adjacency.TryGetValue(from, out var list))
    {
        list = new List<TaxiEdge>();
        g.Adjacency[from] = list;
    }
    list.Add(new TaxiEdge { FromNodeId = from, ToNodeId = to, TaxiwayName = tw });
}

// Component 0 — nodes 1..6
double baseLat = 40.0;
AddNode(1, baseLat, -90.004, 0);
AddNode(2, baseLat, -90.003, 0);
AddNode(3, baseLat, -90.002, 0);
AddNode(4, baseLat, -90.001, 0);
AddNode(5, baseLat, -90.000, 0);  // intersection: on A and on B
AddNode(6, baseLat - 0.001, -90.000, 0);  // south of node 5, on B

// Taxiway A edges (both directions so intersection detection works regardless
// of which direction edges are stored in Adjacency)
AddEdge(1, 2, "A"); AddEdge(2, 1, "A");
AddEdge(2, 3, "A"); AddEdge(3, 2, "A");
AddEdge(3, 4, "A"); AddEdge(4, 3, "A");
AddEdge(4, 5, "A"); AddEdge(5, 4, "A");

// Taxiway B edge at node 5 (outgoing from node 5 to node 6)
AddEdge(5, 6, "B"); AddEdge(6, 5, "B");

// Component 1 — nodes 7, 8 (taxiway C, different component)
AddNode(7, 41.0, -91.0, 1);
AddNode(8, 41.0, -91.001, 1);
AddEdge(7, 8, "C"); AddEdge(8, 7, "C");

// ---------------------------------------------------------------------------
// Assert 1: A∩B in component 0 == node 5
// ---------------------------------------------------------------------------
int intersection = g.FindTaxiwayIntersectionNode("A", "B", 0);
Check(intersection == 5,
      $"A∩B in comp 0 == node 5 (got {intersection})");

// Assert 1b: case-insensitive taxiway name lookup
int intersectionLower = g.FindTaxiwayIntersectionNode("a", "b", 0);
Check(intersectionLower == 5,
      $"A∩B case-insensitive == node 5 (got {intersectionLower})");

// ---------------------------------------------------------------------------
// Assert 2: A∩C in component 0 == -1 (C is only in component 1)
// ---------------------------------------------------------------------------
int noIntersection = g.FindTaxiwayIntersectionNode("A", "C", 0);
Check(noIntersection == -1,
      $"A∩C in comp 0 == -1 (no C in comp 0, got {noIntersection})");

// Assert 2b: asking for comp 1 should NOT return a comp-0 node
int noIntersectionComp1 = g.FindTaxiwayIntersectionNode("A", "B", 1);
Check(noIntersectionComp1 == -1,
      $"A∩B in comp 1 == -1 (A and B are in comp 0 only, got {noIntersectionComp1})");

// ---------------------------------------------------------------------------
// Assert 3: FindTaxiwayEndNode from node 1 on taxiway A == node 5
//   (node 5 is the farthest node on A from node 1 in the same component)
// ---------------------------------------------------------------------------
int endNode = g.FindTaxiwayEndNode(1, "A");
Check(endNode == 5,
      $"end of A from node 1 == node 5 (got {endNode})");

// Assert 3b: from node 5 the farthest on A is node 1
int endFromFive = g.FindTaxiwayEndNode(5, "A");
Check(endFromFive == 1,
      $"end of A from node 5 == node 1 (got {endFromFive})");

// Assert 3c: unknown taxiway returns -1
int endUnknown = g.FindTaxiwayEndNode(1, "Z");
Check(endUnknown == -1,
      $"end of unknown taxiway Z == -1 (got {endUnknown})");

// Assert 3d: unknown fromNodeId returns -1
int endBadNode = g.FindTaxiwayEndNode(999, "A");
Check(endBadNode == -1,
      $"FindTaxiwayEndNode with nonexistent fromNodeId == -1 (got {endBadNode})");

// ---------------------------------------------------------------------------
// Hold-short runway association (TaxiGraph.MatchHoldShortRunwayName)
//
// Synthetic runway 15R/33L: a straight N-S centerline along lon -71.0100 from
// lat 42.3500 (33L threshold) to lat 42.3800 (15R threshold) — ~3.3 km long,
// half-width 30 m. This mirrors the KBOS case: a hold-short where a taxiway
// crosses the runway far from BOTH thresholds.
// ---------------------------------------------------------------------------
const double MATCH_M = 150.0; // mirror TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M
var rwy = new List<TaxiGraph.RunwayCenterline>
{
    new TaxiGraph.RunwayCenterline
    {
        Lat1 = 42.3800, Lon1 = -71.0100, Name1 = "15R", // primary end (north)
        Lat2 = 42.3500, Lon2 = -71.0100, Name2 = "33L", // opposite end (south)
        HalfWidthMeters = 30.0
    }
};

// Mid-runway node, ~60 m east of centerline, nearer the 15R (north) end.
// Far from both thresholds (would FAIL the old <500 m threshold test).
string? midName = TaxiGraph.MatchHoldShortRunwayName(42.3750, -71.00927, rwy, MATCH_M);
Check(midName == "15R", $"mid-runway hold-short names 15R (got '{midName ?? "null"}')");

// Node ~1.6 km east of the centerline (e.g. a real taxiway-only hold) — no match.
string? farName = TaxiGraph.MatchHoldShortRunwayName(42.3650, -70.9900, rwy, MATCH_M);
Check(farName == null, $"far node does not match a runway (got '{farName ?? "null"}')");

// Node ~2 km north of the 15R threshold (beyond the runway end). The clamped
// perpendicular distance measures to the endpoint, so it must NOT match.
string? beyondName = TaxiGraph.MatchHoldShortRunwayName(42.4000, -71.0100, rwy, MATCH_M);
Check(beyondName == null, $"node beyond runway end does not match (got '{beyondName ?? "null"}')");

// ---------------------------------------------------------------------------
// Apron lead-in onto the first cleared taxiway (TaxiLeadIn).
//
// Pure helper math only — TaxiLeadIn links Database.Models, NOT TaxiRouter, so
// these tests do not touch the shared taxi_router.log (the reason the old
// TaxiLeadInProbe was removed). They pin Extract / IsAcceptable / Clause.
// ---------------------------------------------------------------------------
static TaxiRoute Route(params (string tw, double m)[] segs)
{
    var r = new TaxiRoute();
    foreach (var (tw, m) in segs)
        r.Segments.Add(new TaxiRouteSegment { TaxiwayName = tw, DistanceMeters = m });
    return r;
}

// Lead-in = the run of segments before the first segment named after the first
// cleared taxiway. GA + GB apron lanes precede taxiway A.
var li = TaxiLeadIn.Extract(Route(("GA", 100), ("GB", 50), ("A", 200), ("A", 80)), "A");
Check(li.HasLeadIn && Math.Abs(li.DistanceMeters - 150) < 0.01
      && li.Taxiways.Count == 2 && li.Taxiways[0] == "GA" && li.Taxiways[1] == "GB",
      $"Extract: GA,GB lead-in before A (dist={li.DistanceMeters})");

// Route already starts on A → no lead-in.
var li2 = TaxiLeadIn.Extract(Route(("A", 200), ("B", 100)), "A");
Check(!li2.HasLeadIn && li2.DistanceMeters == 0 && li2.Taxiways.Count == 0,
      "Extract: starts on A → no lead-in");

// Unnamed apron connectors before A → distance counts, no taxiway names.
var li3 = TaxiLeadIn.Extract(Route(("", 60), ("", 40), ("A", 200)), "A");
Check(li3.HasLeadIn && Math.Abs(li3.DistanceMeters - 100) < 0.01 && li3.Taxiways.Count == 0,
      "Extract: unnamed connectors → distance but no names");

// Acceptance: reject on router fallback, accept within gap*2.5+300, reject beyond.
Check(!TaxiLeadIn.IsAcceptable(900, 297, "fell back to shortest path"),
      "IsAcceptable: router fallback reason → reject");
Check(TaxiLeadIn.IsAcceptable(900, 297, null),
      "IsAcceptable: 900 <= 297*2.5+300 → accept");
Check(!TaxiLeadIn.IsAcceptable(1100, 297, null),
      "IsAcceptable: 1100 > 1042 → reject (dead-end guard)");

// Spoken clause.
Check(TaxiLeadIn.Clause(li, "A") == " First taxi via GA and GB to reach A.",
      "Clause: two named taxiways");
Check(TaxiLeadIn.Clause(li3, "A") == " First taxi onto A.",
      "Clause: unnamed connectors only");
Check(TaxiLeadIn.Clause(li2, "A") == "",
      "Clause: no lead-in → empty");

// ---------------------------------------------------------------------------
// Edge-vs-runway crossing detection (TaxiGraph.EdgeCrossesRunwayStatic).
//
// A taxiway crosses a runway via an EDGE that spans the pavement; the edge's
// endpoint NODES sit OFF the runway on either side. A point-on-pavement test on
// the nodes therefore misses the crossing — which is exactly the KBOS bug:
// taxiway C plainly crosses runway 04L, but C's nearest node is 35 m from the
// 04L centerline (beyond half-width 25 m + 5 m), so no node lands on pavement.
//
// Real KBOS geometry (from fs2024 navdata):
//   04L/22R centerline: 04L (42.358017,-71.014366) → 22R (42.378273,-71.004539)
//   crossing C edge:    (42.363224,-71.012863) → (42.362663,-71.011658)
//                        both endpoints 79 m / 35 m off the centerline.
// ---------------------------------------------------------------------------
double rwy04Lat1 = 42.358017, rwy04Lon1 = -71.014366; // 04L threshold
double rwy04Lat2 = 42.378273, rwy04Lon2 = -71.004539; // 22R threshold
double cA_lat = 42.363224, cA_lon = -71.012863;       // C node, 79 m off centerline
double cB_lat = 42.362663, cB_lon = -71.011658;       // C node, 35 m off centerline
double rwy04HalfW = 164.0 * 0.3048 / 2.0;             // 25 m

// The crossing edge IS detected.
Check(TaxiGraph.EdgeCrossesRunwayStatic(cA_lat, cA_lon, cB_lat, cB_lon,
        rwy04Lat1, rwy04Lon1, rwy04Lat2, rwy04Lon2),
      "KBOS C edge crosses 04L (edge-intersection)");

// And neither endpoint NODE lands on the pavement (the old point test fails),
// proving the edge test catches a crossing the node test cannot.
double perpA = TaxiGraph.PerpendicularDistanceMetersStatic(
    cA_lat, cA_lon, rwy04Lat1, rwy04Lon1, rwy04Lat2, rwy04Lon2);
double perpB = TaxiGraph.PerpendicularDistanceMetersStatic(
    cB_lat, cB_lon, rwy04Lat1, rwy04Lon1, rwy04Lat2, rwy04Lon2);
Check(perpA > rwy04HalfW + 5.0 && perpB > rwy04HalfW + 5.0,
      $"KBOS C nodes are OFF 04L pavement (perpA={perpA:F1}, perpB={perpB:F1} > {rwy04HalfW + 5.0:F1}) — node test misses it");

// A C edge running well clear of 04L does NOT register a crossing (no false positive).
Check(!TaxiGraph.EdgeCrossesRunwayStatic(42.3700, -70.9950, 42.3690, -70.9940,
        rwy04Lat1, rwy04Lon1, rwy04Lat2, rwy04Lon2),
      "edge clear of 04L does not register a crossing");

// An edge that runs PARALLEL alongside the runway (same side, never crossing the
// centerline) does NOT register a crossing.
Check(!TaxiGraph.EdgeCrossesRunwayStatic(42.3600, -71.0200, 42.3720, -71.0170,
        rwy04Lat1, rwy04Lon1, rwy04Lat2, rwy04Lon2),
      "edge parallel to 04L (same side, west of centerline) does not register a crossing");

// ---------------------------------------------------------------------------
// Taxiway ALIAS resolution — the "test data port" for online-source names.
// Build a graph from ONE named taxiway "HAWKER" that an online source (OSM/apt.dat)
// also calls "B". Verifies the dropdown shows BOTH the canonical and the labeled alias,
// and that selecting/entering either routes to the canonical pavement.
// ---------------------------------------------------------------------------
Console.WriteLine("\n-- taxiway alias resolution --");
{
    var hawker = new TaxiPath
    {
        Type = "T", Name = "HAWKER", Width = 75,
        StartLat = 40.0, StartLon = -74.0, EndLat = 40.0, EndLon = -73.999,
        Aliases = new List<string> { "B" },
    };
    var ag = TaxiGraph.Build(
        new List<TaxiPath> { hawker },
        new List<ParkingSpot>(),
        new List<StartPosition>());

    var names = ag.GetAllTaxiwayNames();
    Check(names.Contains("HAWKER"), "alias: canonical 'HAWKER' is in the dropdown");
    Check(names.Contains("B (HAWKER)"), "alias: labeled alias 'B (HAWKER)' is in the dropdown");
    Check(ag.ResolveTaxiwayName("B (HAWKER)") == "HAWKER", "alias: picking 'B (HAWKER)' routes to HAWKER");
    Check(ag.ResolveTaxiwayName("B") == "HAWKER", "alias: bare 'B' (ATC clearance) routes to HAWKER");
    Check(ag.ResolveTaxiwayName("HAWKER") == "HAWKER", "alias: canonical 'HAWKER' unchanged");
    Check(ag.ResolveTaxiwayName("ZZ") == "ZZ", "alias: unknown name passes through unchanged");
}

// ---------------------------------------------------------------------------
// #4 — AMBIGUOUS bare alias: two DIFFERENT navdata taxiways both online-named "B"
// (and "B" is NOT itself a real taxiway). A bare "B" can't safely pick one pavement,
// so ResolveTaxiwayName must NOT guess — it returns "B" unchanged. The disambiguated
// labels still resolve. (Was: first-registered canonical silently won.)
// ---------------------------------------------------------------------------
Console.WriteLine("\n-- ambiguous bare alias --");
{
    var hawker = new TaxiPath { Type = "T", Name = "HAWKER", Width = 75,
        StartLat = 40.0, StartLon = -74.0, EndLat = 40.0, EndLon = -73.999, Aliases = new List<string> { "B" } };
    var foxtrot = new TaxiPath { Type = "T", Name = "FOXTROT", Width = 75,
        StartLat = 41.0, StartLon = -74.0, EndLat = 41.0, EndLon = -73.999, Aliases = new List<string> { "B" } };
    var ag = TaxiGraph.Build(new List<TaxiPath> { hawker, foxtrot },
        new List<ParkingSpot>(), new List<StartPosition>());

    Check(ag.ResolveTaxiwayName("B") == "B", "ambiguous: bare 'B' (two canonicals) does NOT guess — returns 'B' unchanged");
    Check(ag.ResolveTaxiwayName("B (HAWKER)") == "HAWKER", "ambiguous: disambiguated 'B (HAWKER)' still resolves");
    Check(ag.ResolveTaxiwayName("B (FOXTROT)") == "FOXTROT", "ambiguous: disambiguated 'B (FOXTROT)' still resolves");
    var nm = ag.GetAllTaxiwayNames();
    Check(nm.Contains("B (HAWKER)") && nm.Contains("B (FOXTROT)"), "ambiguous: both labeled aliases still in dropdown");
}

// ---------------------------------------------------------------------------
// #6 — collision-skip must use the build-time normalized alias, NOT a re-parse of
// the display label. A canonical name containing " (" breaks LastIndexOf(" ("):
// canonical "RAMP (NORTH)" aliased "K2" while "K2" is itself a REAL taxiway — the
// mislabeled "K2 (RAMP (NORTH))" must be SKIPPED from the dropdown (collision guard).
// ---------------------------------------------------------------------------
Console.WriteLine("\n-- alias collision skip with parens in canonical --");
{
    var ramp = new TaxiPath { Type = "T", Name = "RAMP (NORTH)", Width = 75,
        StartLat = 42.0, StartLon = -74.0, EndLat = 42.0, EndLon = -73.999, Aliases = new List<string> { "K2" } };
    var realK2 = new TaxiPath { Type = "T", Name = "K2", Width = 75,
        StartLat = 43.0, StartLon = -74.0, EndLat = 43.0, EndLon = -73.999 };
    var ag = TaxiGraph.Build(new List<TaxiPath> { ramp, realK2 },
        new List<ParkingSpot>(), new List<StartPosition>());

    var nm = ag.GetAllTaxiwayNames();
    Check(nm.Contains("K2"), "collision: real taxiway 'K2' is in the dropdown");
    Check(!nm.Contains("K2 (RAMP (NORTH))"), "collision: mislabeled 'K2 (RAMP (NORTH))' is SKIPPED (alias normalizes to real 'K2')");
}

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
Environment.Exit(failures == 0 ? 0 : 1);
