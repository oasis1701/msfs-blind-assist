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

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
Environment.Exit(failures == 0 ? 0 : 1);
