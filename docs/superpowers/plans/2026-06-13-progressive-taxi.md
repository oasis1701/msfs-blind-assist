# Progressive Taxi Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the one-shot "Cross Runway" taxi destination with a "Progressive Taxi" mode that routes to an intermediate terminator (hold short of a runway, hold short of a taxiway, after crossing a runway, or end of last taxiway), announces the hold/end, and stops cleanly so the pilot can set the next cleared leg.

**Architecture:** Reuse the existing `TaxiAssistForm` sequence rows, `TaxiGuidanceManager.LoadRoute`, the constrained router, per-row hold-shorts, and the current Cross-Runway far-side node math. The form resolves the terminator to a destination node (as it already does for Cross Runway) and passes a `ProgressiveTerminator` descriptor to `LoadRoute`. The manager adds a terminal `ProgressiveHold` state that fires instead of the gate/runway arrival flow — no lineup, no Takeoff-Assist, no unreachable-runway safety net.

**Tech Stack:** .NET 9, Windows Forms, SimConnect. No unit-test project (per `CLAUDE.md`): pure-geometry helpers are verified with a console **probe** under `tools/` (the established `TaxiGuidanceProbe` pattern); UI and guidance state changes are verified **in-sim** with a written test plan. Frequent commits.

**Spec:** `docs/superpowers/specs/2026-06-13-progressive-taxi-design.md`

**Branch:** `feature/progressive-taxi` (already created off `main`).

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `MSFSBlindAssist/Navigation/ProgressiveTerminator.cs` | The terminator value type (type enum + target designator + end-announcement text) | Create |
| `MSFSBlindAssist/Navigation/TaxiGraph.cs` | Add `FindTaxiwayIntersectionNode` (hold-short-of-taxiway) and `FindTaxiwayEndNode` (end-of-taxiway) helpers; expose the far-side computation already used by the form | Modify |
| `MSFSBlindAssist/Forms/TaxiAssistForm.cs` | Dropdown rename, last-row terminator control, hide gate/runway picker in progressive mode, resolve the terminator node + pass the descriptor in `OnCalculateClicked` | Modify |
| `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` | `LoadRoute` progressive param, suppress hold-short on the cleared crossing, `ProgressiveHold` state + `HandleProgressiveArrival` + announcements, bypass lineup/Takeoff-Assist/safety-net | Modify |
| `tools/ProgressiveTaxiProbe/ProgressiveTaxiProbe.csproj` + `Program.cs` | Console probe asserting the two new graph helpers against synthetic + replica graphs | Create |

**Decomposition rationale:** the terminator type is a tiny standalone value object (Task 1). The two new graph queries are pure functions on `TaxiGraph` and are probe-testable in isolation (Task 2). `LoadRoute` wiring (Task 3) and the terminal state (Task 4) are separable manager changes. The form (Task 5) is UI-only and depends on Tasks 1–4 being in place. Cleanup (Task 6) removes the dead Cross-Runway path.

---

## Task 1: Terminator value type

**Files:**
- Create: `MSFSBlindAssist/Navigation/ProgressiveTerminator.cs`

- [ ] **Step 1: Create the terminator type**

```csharp
namespace MSFSBlindAssist.Navigation;

/// <summary>
/// How a Progressive Taxi leg ends. The pilot picks this on the last taxiway
/// row of the planner; <see cref="Target"/> names the runway or taxiway it
/// refers to (empty for <see cref="ProgressiveTerminatorType.EndOfTaxiway"/>).
/// </summary>
public enum ProgressiveTerminatorType
{
    HoldShortRunway,
    HoldShortTaxiway,
    AfterCrossingRunway,
    EndOfTaxiway
}

/// <summary>
/// Immutable descriptor of a Progressive Taxi leg's endpoint. The form resolves
/// it to a destination graph node before calling LoadRoute; the manager keeps it
/// to drive the terminal "progressive hold" announcement and to suppress the
/// auto hold-short on a cleared crossing (AfterCrossingRunway only).
/// </summary>
public sealed class ProgressiveTerminator
{
    public ProgressiveTerminatorType Type { get; }
    public string Target { get; }   // runway ("09") or taxiway ("C") designator; "" for EndOfTaxiway

    public ProgressiveTerminator(ProgressiveTerminatorType type, string target)
    {
        Type = type;
        Target = target ?? "";
    }

    /// <summary>The spoken end-of-leg callout (see spec §4).</summary>
    public string EndAnnouncement() => Type switch
    {
        ProgressiveTerminatorType.HoldShortRunway =>
            $"Hold short of runway {Target}. Set a new route when cleared.",
        ProgressiveTerminatorType.HoldShortTaxiway =>
            $"Hold short of taxiway {Target}. Set a new route when cleared.",
        ProgressiveTerminatorType.AfterCrossingRunway =>
            $"Across runway {Target}. Hold position. Set a new route when cleared.",
        _ =>
            "Route ended. Hold position. Set a new route when cleared.",
    };

    /// <summary>Runway designator whose auto hold-short must be suppressed (cleared crossing), or null.</summary>
    public string? ClearedCrossingRunway =>
        Type == ProgressiveTerminatorType.AfterCrossingRunway ? Target : null;
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, `0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Navigation/ProgressiveTerminator.cs
git commit -m "feat(taxi): add ProgressiveTerminator value type"
```

---

## Task 2: Graph helpers + probe

**Files:**
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs`
- Create: `tools/ProgressiveTaxiProbe/ProgressiveTaxiProbe.csproj`
- Create: `tools/ProgressiveTaxiProbe/Program.cs`

> Note: the existing far-side node computation currently lives in `TaxiAssistForm` as `FindFarSideRunwayNode()` (used by Cross Runway). Step 1 moves the reusable core onto `TaxiGraph` so both the form's resolution and the probe can call it; the form keeps a thin wrapper. Read `TaxiAssistForm.FindFarSideRunwayNode` first and port its body verbatim (no behavior change).

- [ ] **Step 1: Add `FindTaxiwayIntersectionNode` to `TaxiGraph`**

Add this public method to `TaxiGraph` (hold-short-of-taxiway terminator). It returns the node on `fromTaxiway` nearest to where it meets `toTaxiway`, restricted to the destination connected component:

```csharp
/// <summary>
/// Returns the node on <paramref name="fromTaxiway"/> that sits at (or nearest to)
/// its intersection with <paramref name="toTaxiway"/> — i.e. a node that has at
/// least one edge named fromTaxiway AND at least one edge named toTaxiway. Used
/// by Progressive Taxi's "hold short of taxiway" terminator. Returns -1 if the
/// two taxiways do not meet in the given component.
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
```

- [ ] **Step 2: Add `FindTaxiwayEndNode` to `TaxiGraph`**

End-of-last-taxiway terminator: the node on `taxiway` farthest (by graph distance) from `fromNodeId`, within the component. Uses the existing `ComputeGraphDistancesFrom` if available on `TaxiRouter`; if that is router-private, compute farthest by the existing `FindNearestNodesOnTaxiway`-style scan but pick the MAX straight-line from `fromNodeId` as a defensive fallback. Prefer graph distance:

```csharp
/// <summary>
/// Returns the node on <paramref name="taxiway"/> in the same component as
/// <paramref name="fromNodeId"/> that is farthest along the taxiway from it
/// (the "far end"), for Progressive Taxi's "end of last taxiway" terminator.
/// Returns -1 if the taxiway has no node in the component.
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
```

> If `FastDistanceMeters` / `Adjacency` / `Nodes` member names differ, match the existing `TaxiGraph` API (read the class header first). The two helpers must use the same component-filter idiom the router already uses.

- [ ] **Step 3: Create the probe project**

`tools/ProgressiveTaxiProbe/ProgressiveTaxiProbe.csproj` (mirror an existing probe csproj, e.g. `tools/DockingProbe`, including the linked `<Compile>` references it uses to pull in `Navigation/TaxiGraph.cs` and dependencies):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Platforms>x64</Platforms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <!-- Link the graph sources (copy the <Compile Include=...> set DockingProbe uses
       for its linked sources; add Navigation/TaxiGraph.cs + Navigation/TaxiNode.cs
       + any model types they reference). -->
</Project>
```

- [ ] **Step 4: Write the probe asserts (these are the "failing tests")**

`tools/ProgressiveTaxiProbe/Program.cs`: build a small synthetic graph with two named taxiways `A` and `B` that share one node (the intersection), and a third taxiway `C` that does not touch `A`. Assert:
- `FindTaxiwayIntersectionNode("A","B",comp)` returns the shared node.
- `FindTaxiwayIntersectionNode("A","C",comp)` returns -1.
- `FindTaxiwayEndNode(startOnA, "A")` returns the far end of `A`.

```csharp
// Pseudocode shape — fill with the real TaxiGraph construction API.
int pass = 0, fail = 0;
void Check(string name, bool ok) { if (ok) { pass++; Console.WriteLine($"PASS {name}"); } else { fail++; Console.WriteLine($"FAIL {name}"); } }

var g = BuildSyntheticGraph(); // A and B share node 5; C is separate
int comp = g.Nodes[5].ComponentId;
Check("A∩B == node 5", g.FindTaxiwayIntersectionNode("A", "B", comp) == 5);
Check("A∩C == -1", g.FindTaxiwayIntersectionNode("A", "C", comp) == -1);
Check("end of A from node 1 == node 9", g.FindTaxiwayEndNode(1, "A") == 9);

Console.WriteLine($"\n{pass} passed, {fail} failed");
Environment.Exit(fail == 0 ? 0 : 1);
```

- [ ] **Step 5: Run the probe — expect FAIL first if helpers stubbed, then PASS**

Run: `dotnet run --project tools/ProgressiveTaxiProbe -p:Platform=x64`
Expected after Steps 1–2 implemented: `3 passed, 0 failed`, exit 0.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/TaxiGraph.cs tools/ProgressiveTaxiProbe/
git commit -m "feat(taxi): graph helpers for progressive terminators + probe"
```

---

## Task 3: `LoadRoute` — accept the terminator, suppress cleared crossing

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`

- [ ] **Step 1: Add a field to hold the active terminator**

Near the lineup-state fields (after `_isRunwayLineup` / `_hasLineupTarget`), add:

```csharp
// Non-null while a Progressive Taxi leg is active. Drives the terminal
// "progressive hold" end-state + announcement and suppresses the auto
// hold-short on a cleared crossing. Cleared on every LoadRoute (set fresh
// below) and StopGuidance.
private Navigation.ProgressiveTerminator? _progressiveTerminator;
```

- [ ] **Step 2: Add a `progressiveTerminator` parameter to `LoadRoute`**

Append a new optional parameter to the `LoadRoute` signature (after `userRunwayHoldShorts`):

```csharp
        Dictionary<int, string>? userRunwayHoldShorts = null,
        Navigation.ProgressiveTerminator? progressiveTerminator = null)
```

Inside `LoadRoute`, immediately after `_isRunwayLineup = isRunwayDestination;` set:

```csharp
            _progressiveTerminator = progressiveTerminator;
```

A progressive leg is NOT a runway lineup and NOT a gate, so the caller passes `isRunwayDestination: false` and no lineup-target data — `_hasLineupTarget` stays false, which already routes `HandleArrival` to the "no lineup data — just stop" branch (Task 4 intercepts it before that).

- [ ] **Step 3: Suppress the auto hold-short on the cleared crossing**

`InsertRunwayCrossingHoldShorts(route, destinationName)` tags every crossed runway. For `AfterCrossingRunway`, the terminator runway is cleared, so its tag must be removed. After the existing `InsertRunwayCrossingHoldShorts(...)` call in `LoadRoute`, add:

```csharp
            // Progressive "after crossing" terminator: the pilot is cleared to
            // cross the terminator runway, so strip the auto hold-short for it
            // (other crossings keep their safety hold — spec Decision 2).
            if (_progressiveTerminator?.ClearedCrossingRunway is string clearedRwy)
            {
                foreach (var seg in route.Segments)
                {
                    if (seg.IsHoldShortPoint && !string.IsNullOrEmpty(seg.HoldShortRunway) &&
                        RunwayDesignatorsMatch(seg.HoldShortRunway, clearedRwy))
                    {
                        seg.IsHoldShortPoint = false;
                        seg.HoldShortRunway = "";
                    }
                }
            }
```

- [ ] **Step 4: Add the reciprocal-aware designator match helper**

`HoldShortRunway` is stored like `"runway 09"` and the terminator target like `"09"`; a crossing may be tagged by either reciprocal end. Add a private helper (reuse the reciprocal logic already in `ApplyUserRunwayHoldShorts` if a shared helper exists — search first; otherwise):

```csharp
private static bool RunwayDesignatorsMatch(string tagged, string target)
{
    // tagged e.g. "runway 09" or "09"; target e.g. "09". Compare the bare
    // designator and its reciprocal (09<->27, with L/R/C suffix swap).
    string a = tagged.Replace("runway", "", StringComparison.OrdinalIgnoreCase).Trim();
    string b = target.Trim();
    if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
    return Reciprocal(a).Equals(b, StringComparison.OrdinalIgnoreCase);
}
```

> If a reciprocal helper already exists (it is used by `ApplyUserRunwayHoldShorts` / the runway-centerline matching), call that instead of adding `Reciprocal`. Do not duplicate it.

- [ ] **Step 5: Reset `_progressiveTerminator` in StopGuidance**

In `StopGuidance`, next to `_hasLineupTarget = false;`, add `_progressiveTerminator = null;`.

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs
git commit -m "feat(taxi): LoadRoute accepts progressive terminator; clear cleared-crossing hold-short"
```

---

## Task 4: Terminal `ProgressiveHold` state + announcement

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`

- [ ] **Step 1: Add the state**

In `enum TaxiGuidanceState` add a value after `Arrived`:

```csharp
    /// <summary>
    /// A Progressive Taxi leg has reached its terminator (hold short of a
    /// runway/taxiway, just past a cleared crossing, or end of taxiway). The
    /// tone is off and the aircraft holds; the pilot sets the next leg. No
    /// lineup, no Takeoff-Assist, no docking.
    /// </summary>
    ProgressiveHold,
```

- [ ] **Step 2: Intercept arrival for progressive routes**

At the very top of `HandleArrival()` (before the `_hasLineupTarget && _isRunwayLineup` runway branch), add:

```csharp
        if (_progressiveTerminator is { } term)
        {
            _steeringTone.Stop();
            SetState(TaxiGuidanceState.ProgressiveHold);
            AnnounceInstruction(term.EndAnnouncement());
            return;
        }
```

This runs before the runway/gate/lineup branches, so a progressive leg never enters `LiningUp` or fires `RequestTakeoffAssistAutoActivate`, and never triggers docking arrival.

- [ ] **Step 3: Guard the per-frame update**

Confirm `UpdatePosition`'s state switch has no handler that runs in `ProgressiveHold` (it should fall through to a no-op like `Arrived`). If there is an `Arrived`/default early-return, include `ProgressiveHold` in it so no tone/recalc fires while held. Read the `UpdatePosition` state dispatch and add `ProgressiveHold` wherever `Arrived` is handled as "terminal, do nothing."

- [ ] **Step 4: Confirm the unreachable-runway safety net is not reached**

The route-reach warning (`LoadRoute`) is gated on `isRunwayDestination && _hasLineupTarget`; progressive passes both false, so it is skipped. The lineup bailout lives in the `_isRunwayLineup` branch of `UpdateLineup`, which progressive never enters. No change needed — note it in the commit body.

- [ ] **Step 5: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs
git commit -m "feat(taxi): terminal ProgressiveHold state + end announcement"
```

---

## Task 5: `TaxiAssistForm` — Progressive Taxi UI + resolution

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs`

> Index map today: `cmbDestType` 0=Runway, 1=Gate, 2=Cross Runway, 3=Deice. After this task index 2 becomes **Progressive Taxi**.

- [ ] **Step 1: Rename the dropdown item**

Change the `cmbDestType.Items.AddRange(...)` line:

```csharp
cmbDestType.Items.AddRange(new object[] { "Runway", "Gate / Parking", "Progressive Taxi", "Deice Area" });
```

Update `cmbDestType.AccessibleDescription` to mention "progressive taxi (route to a hold short or across a runway)".

- [ ] **Step 2: Add the last-row terminator control**

The terminator lives on the current last taxiway row. Add to the per-row control tuple (`_additionalTaxiways` and the first-row fields) a terminator **type combo** (`(none) / Hold short of runway / Hold short of taxiway / After crossing runway / End of last taxiway`) and reuse the existing per-row "Hold short of runway" combo as the **runway target**; add a **taxiway target** combo for the taxiway case. Only show the terminator type combo on the row that is currently last, and only when `cmbDestType.SelectedIndex == 2`. Add a helper `RefreshTerminatorRow()` that hides the terminator combo on all rows and shows it on the last row; call it from add-row, remove-row, and `OnDestTypeChanged`.

> Keep the control creation consistent with the existing row-building code (mnemonics must stay unique — see the mnemonic plan comment near the constructor). Wire each combo's `AccessibleName`/`AccessibleDescription`.

- [ ] **Step 3: Show/hide pickers in `OnDestTypeChanged`**

In `OnDestTypeChanged`, for `SelectedIndex == 2` (Progressive Taxi): hide `cmbDestination` (the gate/runway destination picker) and its label, call `RefreshTerminatorRow()`. For other indices: hide the terminator combos, restore `cmbDestination`. Mirror how the existing gate (`isGate`) branch toggles visibility.

- [ ] **Step 4: Populate terminator target lists**

When Progressive Taxi is selected (and on airport load), populate the runway-target combos from the same non-closed runway list Cross Runway used (the `_crossRunwayMap` population at the old index-2 branch — keep building `_crossRunwayMap`, it is still used for the after-crossing far-side resolution) and populate the taxiway-target combo from `_graph.GetAllTaxiwayNames()` (or the connected-taxiway list already used by the Add-Taxiway combo).

- [ ] **Step 5: Resolve the terminator node in `OnCalculateClicked`**

In the `cmbDestType.SelectedIndex == 2` branch of `OnCalculateClicked`, read the last row's terminator type + target, compute the destination node and build a `ProgressiveTerminator`, then call `LoadRoute(...)` with `isRunwayDestination: false`, no lineup-target args, and `progressiveTerminator:`. Resolution per type (use the last taxiway name = the last row's taxiway):

```csharp
string lastTaxiway = /* last row's selected taxiway name */;
int destNode = -1;
ProgressiveTerminator term;
switch (terminatorType)
{
    case ProgressiveTerminatorType.HoldShortRunway:
        // Route to the runway, like the per-row "hold short of runway" already does:
        // pass the runway in userRunwayHoldShorts on the last sequence index and use
        // the far-side-of-approach node OR the runway-centerline node as destNode.
        // Reuse ApplyUserRunwayHoldShorts geometry via the existing runway-hold-short path.
        destNode = ResolveHoldShortRunwayNode(lastTaxiway, target);   // see note
        term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortRunway, target);
        break;
    case ProgressiveTerminatorType.HoldShortTaxiway:
        destNode = _graph.FindTaxiwayIntersectionNode(lastTaxiway, target, destComponentId);
        term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, target);
        break;
    case ProgressiveTerminatorType.AfterCrossingRunway:
        destNode = FindFarSideRunwayNode(_crossRunwayMap[$"Runway {target}"]); // existing method
        term = new ProgressiveTerminator(ProgressiveTerminatorType.AfterCrossingRunway, target);
        break;
    default: // EndOfTaxiway
        destNode = _graph.FindTaxiwayEndNode(startNode.NodeId, lastTaxiway);
        term = new ProgressiveTerminator(ProgressiveTerminatorType.EndOfTaxiway, "");
        break;
}
if (destNode < 0) { /* announce "Could not find <target> from <lastTaxiway>. Check your entry." and return */ }
```

> `ResolveHoldShortRunwayNode`: simplest correct option is to route to the **far-side node** (`FindFarSideRunwayNode`) as the destination AND set `userRunwayHoldShorts[lastIndex] = target`, so the existing `ApplyUserRunwayHoldShorts` truncates the route to the hold line before the runway. Verify against `ApplyUserRunwayHoldShorts` behavior when reading the manager; if it already truncates to the hold-short, the destination node only needs to be on/just past the runway so the crossing is detected. Document the exact choice in the commit.

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): Progressive Taxi mode UI + terminator resolution"
```

---

## Task 6: Remove standalone Cross Runway remnants

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs`

- [ ] **Step 1: Audit for the old Cross-Runway-only assumptions**

Search `TaxiAssistForm.cs` for comments/branches that treat index 2 as "Cross Runway" as a *final destination* (e.g. the old `cmbDestination.SelectedIndex = 0` cross-runway path that listed runways in `cmbDestination`). Remove the dead listing path — under Progressive Taxi the destination picker is hidden and the runway list now feeds the terminator target combo (Task 5 Step 4). Keep `_crossRunwayMap` and `FindFarSideRunwayNode` (now used by the after-crossing terminator).

- [ ] **Step 2: Update comments / mnemonic plan**

Update the destination-type comments and the tab-order/mnemonic plan comment to reflect Progressive Taxi and the terminator controls.

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "refactor(taxi): fold Cross Runway into Progressive Taxi; drop dead path"
```

---

## Task 7: In-sim verification

**Files:** none (test execution + notes).

- [ ] **Step 1: Build the run exe (close MSFS first)**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` and confirm `bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe` timestamp is current.

- [ ] **Step 2: Execute the in-sim test plan (PHNL or any airport with a runway crossing + taxiway intersections)**

For each terminator, open the planner → Progressive Taxi → enter taxiways → set the last-row terminator → Calculate → taxi:
  - **Hold short of runway:** ends at the hold line; hears "Hold short of runway X. Set a new route when cleared."; tone stops; no lineup tone, no "Lined up", no Takeoff-Assist activation.
  - **Hold short of taxiway:** ends at the intersection; hears "Hold short of taxiway Y…".
  - **After crossing runway:** rolls across the chosen runway WITHOUT a hold-short for it, ends on the far side; hears "Across runway X. Hold position…". A *different* runway crossed earlier still stops with a hold-short + Continue.
  - **End of last taxiway:** ends at the taxiway end; hears "Route ended. Hold position…".
  - **Continuation:** from the held position, open the planner again → Runway or Gate → Calculate → normal guidance resumes and finishes (arrival/lineup as usual).
  - **Mismatch:** pick a terminator target the last taxiway doesn't reach → hear the "Could not find … check your entry" message; no silent mis-route.

- [ ] **Step 3: Capture the log**

Confirm `%APPDATA%\MSFSBlindAssist\logs\taxi_guidance.log` shows the route ending in the `ProgressiveHold` path (no `seg=-1` lineup rows, no lineup target). Note results in the PR.

- [ ] **Step 4: Commit any tuning fixes, then open the PR**

```bash
git add -A
git commit -m "test(taxi): progressive taxi in-sim verification notes/tuning"
```

---

## Self-Review

**Spec coverage:**
- Mode replaces Cross Runway in dropdown → Task 5 Step 1, Task 6.
- Four terminator types → Task 1 (model), Task 2 (taxiway/end helpers), Task 5 (resolution incl. after-crossing reuse).
- Last row carries terminator → Task 5 Step 2.
- Reuse rows/hold-shorts/LoadRoute/router/far-side math → Tasks 3, 5.
- Intermediate crossings keep safety hold; cleared crossing suppressed → Task 3 Step 3 (Decision 2).
- Terminal hold state, no lineup/Takeoff-Assist/safety-net → Task 4.
- End announcements (exact wording) → Task 1 `EndAnnouncement()`.
- Continuation from position → existing position-refresh (Task 7 Step 2 verifies).
- Mismatch handling → Task 5 Step 5 guard.

**Placeholder scan:** resolution helpers reference real methods; the two `> Note` blocks flag "read the existing API first" rather than leaving code blank — acceptable because the exact member names must be confirmed against live code at execution time, and the algorithm is fully specified. `ResolveHoldShortRunwayNode` strategy is specified with a concrete reuse path.

**Type consistency:** `ProgressiveTerminator` / `ProgressiveTerminatorType` names, `EndAnnouncement()`, `ClearedCrossingRunway`, `_progressiveTerminator`, `FindTaxiwayIntersectionNode`, `FindTaxiwayEndNode`, `FindFarSideRunwayNode`, `ProgressiveHold` are used consistently across tasks.

**Note for executor:** confirm `TaxiGraph` member names (`Adjacency`, `Nodes`, `FastDistanceMeters`, `GetAllTaxiwayNames`) and the existing reciprocal-runway helper before writing Tasks 2–3; reuse rather than duplicate.
