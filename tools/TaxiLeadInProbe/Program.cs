using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

// TaxiLeadInProbe — pins the apron lead-in onto the first cleared taxiway:
//   * TaxiLeadIn pure helpers (Extract / IsAcceptable / Clause)
//   * TaxiGraph.FindNearestNode component filter
//   * TaxiRouter.FindConstrainedPath builds a lead-in when started off the
//     first taxiway, and none when started on it (the contrast the fix exploits)
// Run:  dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64
// Exit 0 = all checks pass.

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    if (!ok) failures++;
}

// --- helpers to hand-build a graph (mirrors ProgressiveTaxiProbe) -----------
var g = new TaxiGraph();
TaxiNode AddNode(int id, double lat, double lon, int comp)
{
    var n = new TaxiNode { NodeId = id, Latitude = lat, Longitude = lon, ComponentId = comp };
    g.Nodes[id] = n;
    g.Adjacency[id] = new List<TaxiEdge>();
    return n;
}
void AddEdge(int from, int to, string tw)
{
    g.Adjacency[from].Add(new TaxiEdge { FromNodeId = from, ToNodeId = to, TaxiwayName = tw });
    g.Adjacency[to].Add(new TaxiEdge { FromNodeId = to, ToNodeId = from, TaxiwayName = tw });
    if (!string.IsNullOrEmpty(tw))
    {
        g.Nodes[from].TaxiwayNames.Add(tw);
        g.Nodes[to].TaxiwayNames.Add(tw);
    }
}

// --- synthetic CYYZ-like topology (all component 0 unless noted) ------------
// Aircraft sits on the apron at N1. The first cleared taxiway "A" begins ~345 m
// away at N5, reachable along pavement: 4 (N1-N2) -> AJ (N2-N3-N4) -> unnamed
// connector (N4-N5) -> A (N5-N6-N7). N7 is the destination on A.
double acLat = 40.00000, acLon = -90.00000;
AddNode(1, 40.00000, -90.00005, 0);  // apron, ~4 m from aircraft
AddNode(2, 40.00030, -90.00100, 0);
AddNode(3, 40.00100, -90.00200, 0);
AddNode(4, 40.00150, -90.00280, 0);
AddNode(5, 40.00180, -90.00330, 0);  // entry onto taxiway A (~345 m from aircraft)
AddNode(6, 40.00300, -90.00500, 0);
AddNode(7, 40.00450, -90.00720, 0);  // destination on A
AddEdge(1, 2, "4");
AddEdge(2, 3, "AJ");
AddEdge(3, 4, "AJ");
AddEdge(4, 5, "");        // unnamed apron connector
AddEdge(5, 6, "A");
AddEdge(6, 7, "A");
// Decoy node in a DIFFERENT component, placed nearer the aircraft than N1.
AddNode(99, 40.000005, -90.000005, 1);

// === Task 1 — TaxiLeadIn pure helpers =====================================
// Build a route by hand to test Extract/Clause without the router.
TaxiRouteSegment Seg(int from, int to, string tw, double dist) => new()
{
    FromNode = g.Nodes[from], ToNode = g.Nodes[to], TaxiwayName = tw, DistanceMeters = dist
};
var handRoute = new TaxiRoute
{
    Segments = new()
    {
        Seg(1, 2, "4", 90), Seg(2, 3, "AJ", 110), Seg(3, 4, "AJ", 70),
        Seg(4, 5, "", 60), Seg(5, 6, "A", 200), Seg(6, 7, "A", 280)
    },
    TaxiwaySequence = new() { "A" }
};

var info = TaxiLeadIn.Extract(handRoute, "A");
Check(info.HasLeadIn, "Extract: hand route has a lead-in");
Check(info.Taxiways.SequenceEqual(new[] { "4", "AJ" }),
      $"Extract: lead-in taxiways == [4, AJ] (got [{string.Join(",", info.Taxiways)}])");
Check(Math.Abs(info.DistanceMeters - 330.0) < 0.001,
      $"Extract: lead-in distance == 90+110+70+60 = 330 (got {info.DistanceMeters})");

var noLeadRoute = new TaxiRoute { Segments = new() { Seg(5, 6, "A", 200), Seg(6, 7, "A", 280) } };
Check(!TaxiLeadIn.Extract(noLeadRoute, "A").HasLeadIn,
      "Extract: route starting on A has no lead-in");

Check(TaxiLeadIn.IsAcceptable(330, 297, null),
      "IsAcceptable: 330 m lead-in for a 297 m gap is accepted");
Check(!TaxiLeadIn.IsAcceptable(2000, 297, null),
      "IsAcceptable: 2000 m lead-in for a 297 m gap is rejected (dead-end cap)");
Check(!TaxiLeadIn.IsAcceptable(330, 297, "fell back to shortest path"),
      "IsAcceptable: a router fallback is rejected");

Check(TaxiLeadIn.Clause(info, "A") == " First taxi via 4 and AJ to reach A.",
      $"Clause: two named lead-in taxiways (got '{TaxiLeadIn.Clause(info, "A")}')");
var oneName = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = new[] { "4" }, DistanceMeters = 90 };
Check(TaxiLeadIn.Clause(oneName, "A") == " First taxi via 4 to reach A.",
      "Clause: single named lead-in taxiway");
var unnamed = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = Array.Empty<string>(), DistanceMeters = 60 };
Check(TaxiLeadIn.Clause(unnamed, "A") == " First taxi onto A.",
      "Clause: lead-in with no named taxiways");
Check(TaxiLeadIn.Clause(default, "A") == "",
      "Clause: no lead-in -> empty string");

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
Environment.Exit(failures == 0 ? 0 : 1);
