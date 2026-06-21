# Taxi Pavement Lead-In Onto First Cleared Taxiway — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the first ATC-cleared taxiway is far from the aircraft, route a pavement-following lead-in onto it (instead of a straight beeline across the apron/grass) and name that lead-in in the route summary.

**Architecture:** The router (`TaxiRouter.FindConstrainedPath`) already builds a pavement lead-in (`AStarSearch(start, entry)`) when the start node is *off* the first taxiway. `LoadRoute` currently bypasses this by pre-snapping the start *onto* the first taxiway. The fix: when the first-taxiway node is far (> 75 m), start the constrained route from the aircraft's nearest in-component graph node so the router builds the lead-in; accept it only if the clearance was honoured and the lead-in isn't a dead-end detour, else fall back to today's behaviour and say so. A new pure helper `TaxiLeadIn` extracts/validates/describes the lead-in and is pinned by a console probe.

**Tech Stack:** C# 13 / .NET 9. No xUnit in this repo — verification is via console probes (`tools/*Probe`, linked-source projects) that assert on public methods over synthetic graphs, plus an in-sim test plan run by the repo owner.

---

## File Structure

- **Create** `MSFSBlindAssist/Navigation/TaxiLeadIn.cs` — pure static helper: constants, `LeadInInfo`, `Extract`, `IsAcceptable`, `Clause`. No dependencies beyond `Database.Models`. Linkable into a probe.
- **Modify** `MSFSBlindAssist/Navigation/TaxiGraph.cs` — add optional `requiredComponentId` to `FindNearestNode`.
- **Modify** `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` — `LoadRoute` start-node decision + acceptance/fallback; `BuildRouteSummary` lead-in clause; summary-assembly fallback notice.
- **Create** `tools/TaxiLeadInProbe/` (`TaxiLeadInProbe.csproj`, `Program.cs`, `SimConnectStub.cs`) — pins `TaxiLeadIn`, the `FindNearestNode` component filter, and the router's lead-in behaviour on a synthetic CYYZ-like graph.
- **Modify** `CLAUDE.md` and `docs/taxi-guidance.md` — document the lead-in behaviour.

### Why a new pure helper

`TaxiGuidanceManager` cannot be linked into a probe (it drags in audio, settings, SimConnect). `TaxiRouter`/`TaxiGraph` can. Extracting the lead-in logic into `TaxiLeadIn` (pure, `Database.Models`-only) makes the decision logic unit-testable in the probe; the thin glue in `LoadRoute`/`BuildRouteSummary` is verified by compile + the router-contrast probe + the in-sim plan (the repo's standard for SimConnect-driven paths).

---

## Task 1: `TaxiLeadIn` helper + probe scaffold

**Files:**
- Create: `MSFSBlindAssist/Navigation/TaxiLeadIn.cs`
- Create: `tools/TaxiLeadInProbe/TaxiLeadInProbe.csproj`
- Create: `tools/TaxiLeadInProbe/SimConnectStub.cs`
- Create: `tools/TaxiLeadInProbe/Program.cs`

- [ ] **Step 1: Create the probe project file**

`tools/TaxiLeadInProbe/TaxiLeadInProbe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Platforms>x64</Platforms>
  </PropertyGroup>
  <ItemGroup>
    <!-- Navigation under test -->
    <Compile Include="..\..\MSFSBlindAssist\Navigation\TaxiGraph.cs"             Link="Linked\TaxiGraph.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Navigation\TaxiRouter.cs"            Link="Linked\TaxiRouter.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Navigation\TaxiLeadIn.cs"            Link="Linked\TaxiLeadIn.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Navigation\NavigationCalculator.cs"  Link="Linked\NavigationCalculator.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Navigation\LandingExit.cs"           Link="Linked\LandingExit.cs" />
    <!-- Database models -->
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\TaxiNode.cs"         Link="Linked\TaxiNode.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\TaxiPath.cs"         Link="Linked\TaxiPath.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\TaxiRoute.cs"        Link="Linked\TaxiRoute.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\ParkingSpot.cs"      Link="Linked\ParkingSpot.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\GateSource.cs"       Link="Linked\GateSource.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\StartPosition.cs"    Link="Linked\StartPosition.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Database\Models\Runway.cs"           Link="Linked\Runway.cs" />
    <!-- Utils -->
    <Compile Include="..\..\MSFSBlindAssist\Utils\AppLogs.cs"                    Link="Linked\AppLogs.cs" />
    <!-- Services / Settings (transitively needed by linked files) -->
    <Compile Include="..\..\MSFSBlindAssist\Services\DistanceFormatter.cs"       Link="Linked\DistanceFormatter.cs" />
    <Compile Include="..\..\MSFSBlindAssist\Settings\DistanceUnit.cs"            Link="Linked\DistanceUnit.cs" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the SimConnect stub**

`tools/TaxiLeadInProbe/SimConnectStub.cs` (satisfies the spurious `using MSFSBlindAssist.SimConnect;` in `NavigationCalculator.cs`):

```csharp
// Empty namespace so the spurious 'using MSFSBlindAssist.SimConnect;' in
// NavigationCalculator.cs compiles without the real SimConnect assembly.
namespace MSFSBlindAssist.SimConnect { }
```

- [ ] **Step 3: Write the failing probe (Task 1 assertions only)**

`tools/TaxiLeadInProbe/Program.cs`:

```csharp
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
```

- [ ] **Step 4: Run the probe to verify it fails to compile**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: FAIL — `TaxiLeadIn` does not exist yet (compile error `CS0103`/`CS0246`).

- [ ] **Step 5: Implement `TaxiLeadIn`**

`MSFSBlindAssist/Navigation/TaxiLeadIn.cs`:

```csharp
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Pure helpers for the apron "lead-in" onto the first ATC-cleared taxiway.
///
/// When the first cleared taxiway is far from the aircraft, <c>LoadRoute</c>
/// starts the constrained route from the aircraft's nearest graph node so the
/// router builds a pavement-following lead-in onto that taxiway (apron taxilanes)
/// instead of beelining across the apron/grass. These helpers extract that
/// lead-in from the built route, decide whether it's acceptable, and phrase it
/// for the route summary. Kept dependency-light (Database.Models only) so the
/// taxi probe can link and pin them.
/// </summary>
public static class TaxiLeadIn
{
    /// <summary>
    /// Distance (m) from the aircraft to the nearest node ON the first cleared
    /// taxiway, beyond which a pavement lead-in is routed instead of a beeline.
    /// Typical gate-to-its-taxiway gaps are well under this; CYYZ GB/GC -> A was
    /// 297 m.
    /// </summary>
    public const double TriggerMeters = 75.0;

    /// <summary>Lead-in is rejected if its distance exceeds gap * Ratio + Pad
    /// (dead-end / loop guard — mirrors RECALC_LENGTH_BLOWUP_*).</summary>
    public const double MaxRatio = 2.5;
    public const double MaxPadMeters = 300.0;

    public readonly struct LeadInInfo
    {
        public bool HasLeadIn { get; init; }
        public IReadOnlyList<string> Taxiways { get; init; }
        public double DistanceMeters { get; init; }
    }

    /// <summary>
    /// The lead-in is the leading run of segments before the first segment whose
    /// <see cref="TaxiRouteSegment.TaxiwayName"/> equals the first cleared taxiway.
    /// Returns the distinct named taxiways in order (unnamed apron connectors
    /// contribute distance but no name) and the total lead-in distance.
    /// </summary>
    public static LeadInInfo Extract(TaxiRoute route, string firstClearedTaxiway)
    {
        var names = new List<string>();
        double dist = 0;
        bool any = false;
        foreach (var seg in route.Segments)
        {
            if (seg.TaxiwayName.Equals(firstClearedTaxiway, StringComparison.OrdinalIgnoreCase))
                break;
            any = true;
            dist += seg.DistanceMeters;
            if (!string.IsNullOrEmpty(seg.TaxiwayName) &&
                (names.Count == 0 || !names[^1].Equals(seg.TaxiwayName, StringComparison.OrdinalIgnoreCase)))
                names.Add(seg.TaxiwayName);
        }
        return new LeadInInfo { HasLeadIn = any, Taxiways = names, DistanceMeters = dist };
    }

    /// <summary>
    /// Accept an entry-node-based route's lead-in only if the router honoured the
    /// clearance (no shortest-path fallback) AND the lead-in isn't a dead-end
    /// detour (within gap * <see cref="MaxRatio"/> + <see cref="MaxPadMeters"/>).
    /// </summary>
    public static bool IsAcceptable(double leadInDistanceMeters, double gapMeters, string? fallbackReason)
    {
        if (!string.IsNullOrEmpty(fallbackReason)) return false;
        return leadInDistanceMeters <= gapMeters * MaxRatio + MaxPadMeters;
    }

    /// <summary>
    /// Spoken/boxed clause inserted after the "via ..." list when a lead-in was
    /// added. Leads with a space so it appends cleanly. Empty when no lead-in.
    /// </summary>
    public static string Clause(LeadInInfo info, string firstClearedTaxiway)
    {
        if (!info.HasLeadIn) return "";
        if (info.Taxiways.Count == 0)
            return $" First taxi onto {firstClearedTaxiway}.";
        string list = info.Taxiways.Count == 1
            ? info.Taxiways[0]
            : string.Join(", ", info.Taxiways.Take(info.Taxiways.Count - 1)) + " and " + info.Taxiways[^1];
        return $" First taxi via {list} to reach {firstClearedTaxiway}.";
    }
}
```

- [ ] **Step 6: Run the probe to verify Task 1 checks pass**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: all PASS, `ALL CHECKS PASSED`, exit 0. (If the build reports a missing linked type, add that source file to the csproj `<ItemGroup>` and re-run — `TaxiRouter` only references `TaxiGraph`, `TaxiNode`, `TaxiRoute`/`TaxiRouteSegment`, `AppLogs`, all listed.)

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Navigation/TaxiLeadIn.cs tools/TaxiLeadInProbe/
git commit -m "feat(taxi): TaxiLeadIn helper + probe scaffold for apron lead-in"
```

---

## Task 2: `FindNearestNode` component filter

**Files:**
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs` (`FindNearestNode`)
- Modify: `tools/TaxiLeadInProbe/Program.cs` (add assertions)

- [ ] **Step 1: Add the failing probe assertions**

In `tools/TaxiLeadInProbe/Program.cs`, insert before the final `Console.WriteLine(...)` summary line:

```csharp
// === Task 2 — FindNearestNode component filter ============================
// Unfiltered: the comp-1 decoy (node 99) is physically closest to the aircraft.
Check(g.FindNearestNode(acLat, acLon)!.NodeId == 99,
      "FindNearestNode (no filter): returns the closest node regardless of component");
// Filtered to component 0: must skip the decoy and return N1.
Check(g.FindNearestNode(acLat, acLon, requiredComponentId: 0)!.NodeId == 1,
      "FindNearestNode (component 0): skips the closer out-of-component decoy, returns N1");
```

- [ ] **Step 2: Run the probe to verify the new checks fail**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: FAIL to compile — `FindNearestNode` has no `requiredComponentId` parameter (`CS1739`).

- [ ] **Step 3: Implement the component filter**

In `MSFSBlindAssist/Navigation/TaxiGraph.cs`, change the `FindNearestNode` signature and add the filter. Replace the method body's signature line and both candidate loops.

Signature (was `public TaxiNode? FindNearestNode(double lat, double lon)`):

```csharp
    /// <summary>
    /// Finds the nearest graph node to a given position. When
    /// <paramref name="requiredComponentId"/> is set, only nodes in that
    /// connected component are considered (the spatial-hash ring and the
    /// full-scan fallback both honour it) — used to keep an aircraft's start
    /// node in the destination's component.
    /// </summary>
    public TaxiNode? FindNearestNode(double lat, double lon, int? requiredComponentId = null)
```

Inside the spatial-hash ring loop, change the inner node loop to skip out-of-component nodes:

```csharp
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
```

In the full-scan fallback loop, add the same guard:

```csharp
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
```

- [ ] **Step 4: Run the probe to verify all checks pass**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: all PASS. Existing callers (`TaxiGraph.Build` parking/start matching) pass no `requiredComponentId`, so they are unchanged.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/TaxiGraph.cs tools/TaxiLeadInProbe/Program.cs
git commit -m "feat(taxi): component-filter option on FindNearestNode"
```

---

## Task 3: Pin the router lead-in behaviour (the contrast the fix relies on)

**Files:**
- Modify: `tools/TaxiLeadInProbe/Program.cs` (add assertions)

This pins existing `FindConstrainedPath` behaviour so a future refactor can't silently remove the lead-in the fix depends on. No production code changes.

- [ ] **Step 1: Add the probe assertions**

In `tools/TaxiLeadInProbe/Program.cs`, insert before the final summary line:

```csharp
// === Task 3 — router builds a lead-in only when started OFF the taxiway =====
var router = new TaxiRouter(g);

// Started from the aircraft's nearest node (N1, on the apron): the router routes
// the pavement lead-in 4 -> AJ -> connector onto A.
var routeFromApron = router.FindConstrainedPath(1, 7, new List<string> { "A" });
Check(routeFromApron != null && routeFromApron.Segments.Count > 0,
      "router: constrained path from apron node N1 builds");
var li = TaxiLeadIn.Extract(routeFromApron!, "A");
Check(li.HasLeadIn && li.Taxiways.SequenceEqual(new[] { "4", "AJ" }),
      $"router: lead-in from N1 follows [4, AJ] (got [{string.Join(",", li.Taxiways)}])");
// First segment starts AT the aircraft's node, not ~345 m away.
double firstSegFromAcM = TaxiGraph.FastDistanceMeters(
    acLat, acLon, routeFromApron!.Segments[0].FromNode.Latitude, routeFromApron.Segments[0].FromNode.Longitude);
Check(firstSegFromAcM < 20.0,
      $"router: first segment starts at the aircraft (got {firstSegFromAcM:F0} m, beeline would be ~345 m)");

// Started ON taxiway A (today's pre-snap, N5): no lead-in.
var routeFromA = router.FindConstrainedPath(5, 7, new List<string> { "A" });
Check(routeFromA != null && !TaxiLeadIn.Extract(routeFromA!, "A").HasLeadIn,
      "router: constrained path started on A has no lead-in (the bug's pre-snap path)");
```

- [ ] **Step 2: Run the probe to verify the new checks pass**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: all PASS. (These assert *existing* router behaviour, so they pass without production changes — they are regression pins.)

- [ ] **Step 3: Commit**

```bash
git add tools/TaxiLeadInProbe/Program.cs
git commit -m "test(taxi): pin router lead-in vs pre-snap contrast"
```

---

## Task 4: Wire the lead-in decision into `LoadRoute`

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (`LoadRoute` start-node block)

No automated test (LoadRoute pulls in audio/SimConnect and cannot be linked into a probe). Verified by: (a) the Task 3 router-contrast probe, (b) a clean solution build, (c) the in-sim plan in Task 7. Reasoning: starting `FindConstrainedPath` from the aircraft's nearest in-component node is exactly the Task-3-pinned path that produces the lead-in.

- [ ] **Step 1: Replace the start-node + route-build block**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, find this exact block in `LoadRoute` (immediately after the `int destComponentId = _graph.Nodes[destinationNodeId].ComponentId;` line):

```csharp
            TaxiNode? startNode = null;
            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
            {
                startNode = _graph.FindNearestNodeOnTaxiway(
                    aircraftLat, aircraftLon, taxiwaySequence[0],
                    requiredComponentId: destComponentId);
            }
            if (startNode == null)
            {
                startNode = _graph.FindNearestNodeInDirection(
                    aircraftLat, aircraftLon, aircraftHeading,
                    requiredComponentId: destComponentId);
            }
            if (startNode == null)
                return "Could not find a nearby taxiway node.";

            // Calculate route
            var router = new TaxiRouter(_graph);
            TaxiRoute? route;

            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
                route = router.FindConstrainedPath(startNode.NodeId, destinationNodeId, taxiwaySequence);
            else
                route = router.FindShortestPath(startNode.NodeId, destinationNodeId);

            if (route == null || route.Segments.Count == 0)
                return "Could not calculate a route to the destination.";
```

Replace it with:

```csharp
            // Start-node selection. With a constrained taxiway sequence we prefer
            // a node ON the first cleared taxiway (heading-irrelevant) so the route
            // anchors on the clearance regardless of post-pushback orientation
            // (the LEPA fix). BUT when that taxiway is FAR from the aircraft
            // (a gate across an apron from a main parallel — CYYZ GB/GC -> A was
            // 297 m), pre-snapping onto it makes the guidance beeline across the
            // apron/grass. In that case start from the aircraft's nearest
            // in-component graph node so FindConstrainedPath builds a
            // pavement-following lead-in onto the taxiway (its Step-1 AStarSearch).
            string? firstCleared = (taxiwaySequence is { Count: > 0 }) ? taxiwaySequence[0] : null;
            TaxiNode? firstTwNode = firstCleared != null
                ? _graph.FindNearestNodeOnTaxiway(
                    aircraftLat, aircraftLon, firstCleared, requiredComponentId: destComponentId)
                : null;

            bool attemptLeadIn = false;
            double leadInGap = 0;
            TaxiNode? startNode;
            if (firstTwNode != null)
            {
                leadInGap = TaxiGraph.FastDistanceMeters(
                    aircraftLat, aircraftLon, firstTwNode.Latitude, firstTwNode.Longitude);
                if (leadInGap > TaxiLeadIn.TriggerMeters)
                {
                    var entryNode = _graph.FindNearestNode(
                        aircraftLat, aircraftLon, requiredComponentId: destComponentId);
                    if (entryNode != null && entryNode.NodeId != firstTwNode.NodeId)
                    {
                        startNode = entryNode;
                        attemptLeadIn = true;
                    }
                    else
                    {
                        startNode = firstTwNode;
                    }
                }
                else
                {
                    startNode = firstTwNode;  // common case: gate on/near its taxiway
                }
            }
            else
            {
                startNode = _graph.FindNearestNodeInDirection(
                    aircraftLat, aircraftLon, aircraftHeading,
                    requiredComponentId: destComponentId);
            }
            if (startNode == null)
                return "Could not find a nearby taxiway node.";

            // Calculate route
            var router = new TaxiRouter(_graph);
            TaxiRoute? route;

            if (taxiwaySequence != null && taxiwaySequence.Count > 0)
                route = router.FindConstrainedPath(startNode.NodeId, destinationNodeId, taxiwaySequence);
            else
                route = router.FindShortestPath(startNode.NodeId, destinationNodeId);

            // Lead-in acceptance. If we started from the aircraft's nearest node to
            // get a pavement lead-in, accept it only when the router honoured the
            // clearance AND the lead-in is not a dead-end detour. Otherwise rebuild
            // from the on-taxiway node (today's behaviour) and note it in the
            // summary so the pilot knows the lead-in wasn't computed.
            TaxiLeadIn.LeadInInfo leadIn = default;
            bool leadInFallback = false;
            if (attemptLeadIn)
            {
                if (route is { Segments.Count: > 0 } &&
                    TaxiLeadIn.IsAcceptable(
                        (leadIn = TaxiLeadIn.Extract(route, firstCleared!)).DistanceMeters,
                        leadInGap, route.ConstrainedFallbackReason))
                {
                    // accepted — leadIn is set
                }
                else
                {
                    route = router.FindConstrainedPath(
                        firstTwNode!.NodeId, destinationNodeId, taxiwaySequence!);
                    leadIn = default;
                    leadInFallback = true;
                }
            }

            if (route == null || route.Segments.Count == 0)
                return "Could not calculate a route to the destination.";
```

> Note: the inline assignment `(leadIn = TaxiLeadIn.Extract(...)).DistanceMeters` sets `leadIn` and reads its distance in one expression; on rejection it is reset to `default`. If you prefer clarity over brevity, split it into two statements — behaviour is identical.

- [ ] **Step 2: Build the solution to verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`. (`TaxiLeadIn` is in the main project; no new reference needed.) If MSB3021 (exe locked), close the running app first.

- [ ] **Step 3: Re-run the probe (no regression)**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: all PASS.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs
git commit -m "feat(taxi): route a pavement lead-in when first cleared taxiway is far"
```

---

## Task 5: Lead-in clause + fallback notice in the route summary

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (`BuildRouteSummary` + the summary-assembly site in `LoadRoute`)

- [ ] **Step 1: Add the lead-in parameters to `BuildRouteSummary`**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, change the signature (was `private string BuildRouteSummary(TaxiRoute route, bool isRunwayDestination)`):

```csharp
    private string BuildRouteSummary(
        TaxiRoute route, bool isRunwayDestination,
        TaxiLeadIn.LeadInInfo leadIn, string? firstClearedTaxiway)
```

Then change the final `return` of `BuildRouteSummary` (was
`return $"Route to {route.DestinationName}{taxiwayStr}. {distStr}{holdStr}.{fallbackStr}";`) to insert the lead-in clause after the via-list sentence:

```csharp
        string leadInStr = (firstClearedTaxiway != null)
            ? TaxiLeadIn.Clause(leadIn, firstClearedTaxiway)
            : "";

        return $"Route to {route.DestinationName}{taxiwayStr}.{leadInStr} {distStr}{holdStr}.{fallbackStr}";
```

- [ ] **Step 2: Update the single call site + add the fallback notice**

In `LoadRoute`, find the summary-assembly block:

```csharp
                string summary = BuildRouteSummary(route, isRunwayDestination);
                if (!string.IsNullOrEmpty(runwayHoldShortWarning))
                    summary = summary + " " + runwayHoldShortWarning;
                // The length advisory goes FIRST, not last. ...
                if (!string.IsNullOrEmpty(constrainedLengthWarning))
                    summary = constrainedLengthWarning + " " + summary;
```

Replace the first line and add the fallback prepend after the `constrainedLengthWarning` block:

```csharp
                string summary = BuildRouteSummary(route, isRunwayDestination, leadIn, firstCleared);
                if (!string.IsNullOrEmpty(runwayHoldShortWarning))
                    summary = summary + " " + runwayHoldShortWarning;
                // The length advisory goes FIRST, not last. ...
                if (!string.IsNullOrEmpty(constrainedLengthWarning))
                    summary = constrainedLengthWarning + " " + summary;
                // If a pavement lead-in onto the first cleared taxiway was attempted
                // but couldn't be built (out-of-component / dead-end), the route fell
                // back to starting on that taxiway. Prepend a notice so it is heard
                // before the first tactical callout can interrupt the queued summary.
                if (leadInFallback && firstCleared != null)
                    summary = $"Could not compute a path onto taxiway {firstCleared} " +
                              $"along the apron; route starts on {firstCleared}. " + summary;
```

(`leadIn`, `leadInFallback`, and `firstCleared` are in scope from Task 4 — they are declared earlier in the same `try` block.)

- [ ] **Step 3: Build the solution to verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`. The summary now reads e.g. `"Route to Runway 23 via A, H. First taxi via 4 and AJ to reach A. 1.6 km."`

- [ ] **Step 4: Re-run the probe (Clause wording already pinned in Task 1)**

Run: `dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64`
Expected: all PASS (the exact clause strings asserted in Task 1 match what `BuildRouteSummary` now emits).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs
git commit -m "feat(taxi): name the apron lead-in (and fallback) in the route summary"
```

---

## Task 6: Confirm no spurious length warning + verify `_lastAnnouncedTaxiway`

**Files:**
- Read-only review of `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`

These are the two "confirm during implementation" risks from the spec. No code change expected; if either fails, note it and stop for guidance.

- [ ] **Step 1: Review the constrained-length advisory interaction**

Read the `constrainedLengthWarning` block in `LoadRoute` (the `if (taxiwaySequence is { Count: > 0 } && string.IsNullOrEmpty(route.ConstrainedFallbackReason))` block, ~30 lines above the summary site). Confirm: its `direct` baseline is `FindShortestPath` from `FindNearestNodeInDirection(aircraft, …)` (an aircraft-anchored start), so both `route.TotalDistanceMeters` (now including the lead-in) and `direct` include an aircraft-to-network leg — the ratio comparison stays fair. No change needed. Verify the actual numbers in the in-sim test (Task 7): the CYYZ A,H route (~1.6 km incl. lead-in) must NOT trip `CONSTRAINED_WARN_RATIO` (2×) + 500 m vs. the direct path.

- [ ] **Step 2: Verify `_lastAnnouncedTaxiway` reflects the lead-in's first named taxiway**

Read `StartGuidance` (the "Announce the first *named* taxiway" loop, ~line 1470). Confirm the `foreach (var seg in _route.Segments)` loop that finds `firstNamedTaxiway` also assigns it to `_lastAnnouncedTaxiway` (the field the initial U-turn cue at `:2070` reads). With the lead-in, segment 0 is the lead-in's first segment, so `firstNamedTaxiway` becomes the lead-in taxiway (e.g. `4`) — the initial cue, if it fires, now names the real first turn. If `StartGuidance` does NOT set `_lastAnnouncedTaxiway` there, note it: the cue would name a stale taxiway, but this is pre-existing behaviour and out of scope — flag for a follow-up rather than fixing here.

- [ ] **Step 3: No commit** (review only). Record findings in the PR description.

---

## Task 7: Documentation + full verification

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/taxi-guidance.md`

- [ ] **Step 1: Add a CLAUDE.md bullet**

In `CLAUDE.md`, under the `### Taxi Guidance` "Key rules" bullet list, add:

```markdown
- **Pavement lead-in onto the first cleared taxiway (large gap only).** When a user taxiway sequence is given and the nearest node ON the first cleared taxiway is more than `TaxiLeadIn.TriggerMeters` (75 m) from the aircraft, `LoadRoute` starts the constrained route from the aircraft's nearest *in-component* graph node (`FindNearestNode(..., requiredComponentId: destComponentId)`) instead of pre-snapping onto the taxiway. This lets `TaxiRouter.FindConstrainedPath` build its pavement-following lead-in (the Step-1 `AStarSearch`) onto the taxiway — apron taxilanes — rather than beelining across the apron/grass (CYYZ GB/GC → A: a 297 m beeline + 180° pivot, "in the grass crossing AJ", 2026-06-17). The lead-in is **accepted only** when the router honoured the clearance (`ConstrainedFallbackReason == null`) AND its distance is within `gap × 2.5 + 300 m` (`TaxiLeadIn.IsAcceptable`, a dead-end guard); otherwise the route is rebuilt from the on-taxiway node (today's behaviour) and the summary is prepended with *"Could not compute a path onto taxiway X along the apron; route starts on X."* On success the summary names the lead-in: *"Route to Runway 23 via A, H. First taxi via 4 and AJ to reach A. …"* (`TaxiLeadIn.Clause`). The ≤ 75 m common case (gate on/near its taxiway) is byte-for-byte unchanged, and `TryRecalculateRoute`/unconstrained routes are untouched. Pure-logic + router-contrast coverage: `tools/TaxiLeadInProbe`. Do NOT remove the pre-snap for the common case — it is the LEPA anchoring fix; the lead-in only replaces it when the first taxiway is far.
```

- [ ] **Step 2: Add a section to docs/taxi-guidance.md**

Append a short subsection to `docs/taxi-guidance.md` describing the lead-in: the trigger (75 m), the mechanism (start from nearest in-component node → router builds the `AStarSearch` lead-in), the acceptance test, the summary wording, and the probe. Keep it ~10–15 lines, consistent with the file's existing style.

- [ ] **Step 3: Full solution build + probe run**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
dotnet run --project tools/TaxiLeadInProbe -p:Platform=x64
```
Expected: `Build succeeded`; probe `ALL CHECKS PASSED`, exit 0. Verify the freshly built exe timestamp under `MSFSBlindAssist\bin\x64\Debug\net9.0-windows\` is current (per CLAUDE.md build-path note).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md docs/taxi-guidance.md
git commit -m "docs(taxi): document the pavement lead-in onto the first cleared taxiway"
```

- [ ] **Step 5: In-sim test plan (for the repo owner; include in the PR)**

1. **CYYZ repro (primary):** at a GB/GC stand, get/enter a clearance "runway 23 via A, H", Calculate. Expect: the first cue routes onto an apron taxiway (e.g. taxiway 4 / AJ) following pavement — NOT "Taxiway A is behind you" with a beeline across grass. The route summary should say "…via A, H. First taxi via 4 and AJ to reach A. …".
2. **Common case unchanged:** a gate whose first cleared taxiway is right outside (gap ≤ 75 m). Expect: identical to before — no "First taxi via …" clause, route starts on the cleared taxiway.
3. **Length advisory:** confirm the CYYZ route does NOT spuriously announce the "route via … is far longer than direct" warning.
4. **Fallback notice (best-effort):** if you can find a stand whose apron has no graph path onto the cleared taxiway, confirm it falls back to today's routing AND speaks "Could not compute a path onto taxiway X along the apron…".

---

## Self-Review

**Spec coverage:**
- Conditional trigger ("only when far") → Task 4 (`leadInGap > TaxiLeadIn.TriggerMeters`). ✓
- Reuse router approach (start from nearest in-component node) → Task 4 + Task 3 pin. ✓
- Component-filtered nearest node → Task 2. ✓
- Acceptance test (no fallback + length cap) → `TaxiLeadIn.IsAcceptable`, Task 1 + Task 4. ✓
- Silent fallback to today + notify in summary/announcement → Task 4 (rebuild) + Task 5 (prepended notice; summary is spoken + boxed). ✓
- Separate lead-in clause in summary → `TaxiLeadIn.Clause` + Task 5. ✓
- Only `LoadRoute` (start of taxiing); recalc/unconstrained untouched → Task 4 scope. ✓
- Initial-cue interaction + length-warning risk → Task 6. ✓
- Probe verification (lead-in exists, first segment near aircraft, summary clause, regression: no-lead-in case) → Tasks 1–3. ✓
- Constants `TriggerMeters=75`, `MaxRatio=2.5`, `MaxPadMeters=300` → Task 1. ✓

**Placeholder scan:** none — every step has concrete code/commands.

**Type consistency:** `TaxiLeadIn.LeadInInfo` (`HasLeadIn`/`Taxiways`/`DistanceMeters`), `Extract`/`IsAcceptable`/`Clause`, `TriggerMeters`/`MaxRatio`/`MaxPadMeters`, and `FindNearestNode(lat, lon, requiredComponentId)` are used identically across Tasks 1, 2, 4, 5. `BuildRouteSummary(route, isRunwayDestination, leadIn, firstClearedTaxiway)` — the new 4-arg form — is defined in Task 5 and called once (same task). ✓
