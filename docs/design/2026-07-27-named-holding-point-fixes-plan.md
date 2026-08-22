# Named Holding Point Review Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the seven surviving review findings from PR #164 — a truthful designated-snap flag, kind normalization, an unreachable-hold-point guard, a combo that repopulates on airport change, a bounded re-resolve, diagnostics, and docs — without moving the snap policy.

**Architecture:** Two production files change. `Navigation/NamedHoldingPointResolver.cs` is pure logic covered by xUnit; `Forms/TaxiAssistForm.cs` is UI/sim-facing and verified by build + the existing suite + an in-sim plan. The resolver's *routing behaviour* must be provably unchanged: the flag fix splits reporting from ranking so duplicate-name selection cannot move.

**Tech Stack:** .NET 10, C# 13, Windows Forms, xUnit.

## Global Constraints

- Build the **solution**, never the bare `.csproj`: `dotnet build MSFSBlindAssist.sln -c Debug`. A bare csproj build silently writes to `bin\Debug\` instead of the x64 run path.
- Test command: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- The exe is file-locked while MSFSBA is running (MSB3021) — close the app before building.
- Branch is `feat/named-holding-points`, already checked out from the PR head. It belongs to fork `blindflightsimmer` (`maintainerCanModify: true`); pushes go to `fork feat/named-holding-points` and nowhere else.
- **Do not change** `DESIGNATED_SNAP_M` (15.0), `MAX_SNAP_M` (30.0), the parking-node exclusion, the plain-node fallback, or the drop rule. These were measured; see the design doc.
- All logging goes through `MSFSBlindAssist.Utils.Logging.Log` — never `File.AppendAllText` or a hand-built path.
- Announcements: only validation errors and background state changes. Never announce a combo selection.

---

### Task 1: Truthful `SnappedToDesignatedNode` without moving ranking

**Files:**
- Modify: `MSFSBlindAssist/Navigation/NamedHoldingPointResolver.cs`
- Test: `tests/MSFSBlindAssist.Tests/NamedHoldingPointResolverTests.cs`

**Interfaces:**
- Consumes: `TaxiGraph`, `TaxiNode`, `TaxiNodeType` from `MSFSBlindAssist.Database.Models`.
- Produces: `NamedHoldingPoint.SnappedToDesignatedNode` (public, now describes the chosen node) and `NamedHoldingPoint.WonDesignatedPreference` (**internal**, the ≤15 m preference winner — the only thing `Beats` reads). Task 5 reads `SnappedToDesignatedNode` for its log line.

- [ ] **Step 1: Add both tests**

Append to `tests/MSFSBlindAssist.Tests/NamedHoldingPointResolverTests.cs`, before the closing brace of the class:

```csharp
    [Fact]
    public void Resolve_reports_a_designated_node_chosen_via_the_plain_path_as_designated()
    {
        // The only node within MAX_SNAP_M is an HS node at 20 m — outside the 15 m
        // designated PREFERENCE, so it is selected through the plain fallback. The
        // flag describes the NODE, so it must still read designated.
        var graph = BuildGraph(Edge(20, 0, "HSND", 80, 0, ""));

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("N2E", LatN(0), LonE(0), "runway") });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 20, 0).NodeId, hp.NodeId);
        Assert.True(hp.SnappedToDesignatedNode);
    }

    [Fact]
    public void Resolve_duplicate_ranking_ignores_a_designated_node_beyond_the_preference_radius()
    {
        // Regression pin: this must pass BEFORE and AFTER the flag fix. Same name
        // twice — one occurrence sees only a designated node at 20 m (outside the
        // 15 m preference), the other a plain node at 18 m. The nearer plain node
        // must win: a designated node that far out can be a DIFFERENT hold line
        // (measured — EDDF M15 sits 91 m off its own point).
        var graph = BuildGraph(
            Edge(20, 0, "HSND", 80, 0, ""),
            Edge(18, 200, "", 80, 200, ""));

        var result = NamedHoldingPointResolver.Resolve(graph, new[]
        {
            ("A4", LatN(0), LonE(0), "runway"),
            ("A4", LatN(0), LonE(200), "runway"),
        });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 18, 200).NodeId, hp.NodeId);
        Assert.False(hp.SnappedToDesignatedNode);
    }
```

- [ ] **Step 2: Run both tests to see the split**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~NamedHoldingPointResolverTests"
```

Expected: `Resolve_reports_a_designated_node_chosen_via_the_plain_path_as_designated` **FAILS** (`Assert.True() Failure` — the flag currently means "won the preference"). `Resolve_duplicate_ranking_ignores_a_designated_node_beyond_the_preference_radius` **PASSES** — that is the point of it; it pins behaviour that must not move.

- [ ] **Step 3: Split the flag from the ranking key**

In `NamedHoldingPointResolver.cs`, replace the `SnappedToDesignatedNode` property declaration:

```csharp
    /// <summary>The resolved node is a scenery-designated hold-short node (HS/IHS).</summary>
    public bool SnappedToDesignatedNode { get; init; }
```

with:

```csharp
    /// <summary>The resolved node is a scenery-designated hold-short node (HS/IHS).</summary>
    public bool SnappedToDesignatedNode { get; init; }

    /// <summary>
    /// This candidate won the ≤<see cref="NamedHoldingPointResolver.DESIGNATED_SNAP_M"/>
    /// designated preference. NOT the same as <see cref="SnappedToDesignatedNode"/>, which
    /// merely describes the chosen node: duplicate-name ranking keys on THIS, so a designated
    /// node picked through the plain fallback (beyond the preference radius) never outranks a
    /// nearer plain node. A designated node that far out can be an entirely different hold
    /// line — measured at EDDF, where M15's nearest HS node beyond 15 m sits 91 m away.
    /// </summary>
    internal bool WonDesignatedPreference { get; init; }
```

- [ ] **Step 4: Populate both from the chosen node**

Replace these lines in `Resolve`:

```csharp
            var chosen = designated ?? plain;
            if (chosen == null) continue;   // nothing within MAX_SNAP_M — drop, never misplace
            bool viaDesignated = designated != null;
            double chosenD = viaDesignated ? designatedD : plainD;

            var candidate = new NamedHoldingPoint
            {
                Name = name,
                Kind = kind ?? "",
                NodeId = chosen.NodeId,
                Latitude = chosen.Latitude,
                Longitude = chosen.Longitude,
                SnapDistanceMeters = chosenD,
                SnappedToDesignatedNode = viaDesignated,
            };
```

with:

```csharp
            var chosen = designated ?? plain;
            if (chosen == null) continue;   // nothing within MAX_SNAP_M — drop, never misplace
            bool wonPreference = designated != null;
            double chosenD = wonPreference ? designatedD : plainD;

            var candidate = new NamedHoldingPoint
            {
                Name = name,
                Kind = kind ?? "",
                NodeId = chosen.NodeId,
                Latitude = chosen.Latitude,
                Longitude = chosen.Longitude,
                SnapDistanceMeters = chosenD,
                SnappedToDesignatedNode = chosen.Type == TaxiNodeType.HoldShort
                                       || chosen.Type == TaxiNodeType.ILSHoldShort,
                WonDesignatedPreference = wonPreference,
            };
```

- [ ] **Step 5: Point `Beats` at the ranking key**

Replace:

```csharp
    // Duplicate-name ranking: a designated-node snap always beats a plain-node
    // snap (the painted line beats a nearby centerline vertex); within the same
    // class the smaller snap distance wins.
    private static bool Beats(NamedHoldingPoint a, NamedHoldingPoint b)
    {
        if (a.SnappedToDesignatedNode != b.SnappedToDesignatedNode)
            return a.SnappedToDesignatedNode;
        return a.SnapDistanceMeters < b.SnapDistanceMeters;
    }
```

with:

```csharp
    // Duplicate-name ranking: winning the ≤DESIGNATED_SNAP_M preference always beats
    // a fallback snap (the painted line beats a nearby centerline vertex); within the
    // same class the smaller snap distance wins. Deliberately keyed on
    // WonDesignatedPreference, NOT SnappedToDesignatedNode — see that property.
    private static bool Beats(NamedHoldingPoint a, NamedHoldingPoint b)
    {
        if (a.WonDesignatedPreference != b.WonDesignatedPreference)
            return a.WonDesignatedPreference;
        return a.SnapDistanceMeters < b.SnapDistanceMeters;
    }
```

- [ ] **Step 6: Run the resolver tests**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~NamedHoldingPointResolverTests"
```

Expected: PASS, all cases (the 8 pre-existing + the 2 new).

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Navigation/NamedHoldingPointResolver.cs tests/MSFSBlindAssist.Tests/NamedHoldingPointResolverTests.cs
git commit -m "fix(taxi): SnappedToDesignatedNode describes the node, not the snap path

Split the diagnostic flag from the duplicate-name ranking key so making it
truthful cannot move which node a repeated name resolves to."
```

---

### Task 2: `Kind` normalization and the parking-exclusion pin

**Files:**
- Modify: `MSFSBlindAssist/Navigation/NamedHoldingPointResolver.cs`
- Test: `tests/MSFSBlindAssist.Tests/NamedHoldingPointResolverTests.cs`

**Interfaces:**
- Consumes: `NamedHoldingPoint.Kind`, `NamedHoldingPoint.DisplayLabel` from Task 1's file.
- Produces: `DisplayLabel` tolerant of casing and surrounding whitespace. The label strings themselves are unchanged (`"N2E (runway hold)"`, `"A11 (ILS hold)"`, `"A11 (intermediate hold)"`, bare `Name`), so Task 3's `DisplayLabel ==` lookup in the form stays valid.

- [ ] **Step 1: Add both tests**

Append to `NamedHoldingPointResolverTests.cs`, before the closing brace of the class:

```csharp
    [Fact]
    public void Resolve_never_snaps_to_a_parking_node()
    {
        // Characterization pin for an untested safety rule: a stand connector is not
        // a holding point. The parking node at 3 m must be skipped in favour of the
        // plain taxiway node at 12 m.
        var graph = BuildGraph(
            Edge(3, 0, "P", 3, 60, "P", name: "STAND"),
            Edge(12, 0, "", 80, 0, ""));

        var result = NamedHoldingPointResolver.Resolve(
            graph, new[] { ("VIKAS", LatN(0), LonE(0), "intermediate") });

        var hp = Assert.Single(result);
        Assert.Equal(NodeNear(graph, 12, 0).NodeId, hp.NodeId);
    }

    [Theory]
    [InlineData("RUNWAY", "N2E (runway hold)")]
    [InlineData("ils", "A11 (ILS hold)")]
    [InlineData("  intermediate  ", "A11 (intermediate hold)")]
    public void DisplayLabel_tolerates_kind_casing_and_whitespace(string kind, string expected)
    {
        string name = expected.Split(' ')[0];
        var hp = new NamedHoldingPoint { Name = name, Kind = kind };
        Assert.Equal(expected, hp.DisplayLabel);
    }
```

- [ ] **Step 2: Run them to see the split**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~NamedHoldingPointResolverTests"
```

Expected: `Resolve_never_snaps_to_a_parking_node` **PASSES** (it pins existing behaviour). All three `DisplayLabel_tolerates_kind_casing_and_whitespace` cases **FAIL** — the current `switch` matches `Kind` exactly, so each returns the bare name.

- [ ] **Step 3: Make `DisplayLabel` tolerant**

Replace:

```csharp
    public string DisplayLabel => Kind switch
    {
        "runway"       => $"{Name} (runway hold)",
        "ILS"          => $"{Name} (ILS hold)",
        "intermediate" => $"{Name} (intermediate hold)",
        _              => Name,
    };
```

with:

```csharp
    public string DisplayLabel
    {
        get
        {
            // Normalized at read time as well as on construction: OSM is hand-edited
            // and the property is also built directly in tests.
            string suffix = Kind.Trim().ToLowerInvariant() switch
            {
                "runway"       => " (runway hold)",
                "ils"          => " (ILS hold)",
                "intermediate" => " (intermediate hold)",
                _              => "",
            };
            return Name + suffix;
        }
    }
```

- [ ] **Step 4: Trim the stored `Kind`**

In `Resolve`, change the candidate initializer line:

```csharp
                Kind = kind ?? "",
```

to:

```csharp
                Kind = (kind ?? "").Trim(),
```

- [ ] **Step 5: Run the resolver tests**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~NamedHoldingPointResolverTests"
```

Expected: PASS, all cases.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/NamedHoldingPointResolver.cs tests/MSFSBlindAssist.Tests/NamedHoldingPointResolverTests.cs
git commit -m "fix(taxi): tolerate holding_position:type casing, pin the parking exclusion"
```

---

### Task 3: Refuse an unreachable named holding point

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (the `case 4:` block and the `destNode < 0` message composer inside `OnCalculateClicked`)

**Interfaces:**
- Consumes: `destComponentId` (already computed above the switch as `startNode.ComponentId`), `_graph.Nodes`, `TaxiNode.ComponentId`, `NamedHoldingPoint.NodeId`/`.Name` from Task 1.
- Produces: no new API.

- [ ] **Step 1: Add the component guard**

In `OnCalculateClicked`, replace these two lines of `case 4:`:

```csharp
                    destNode = holdPoint.NodeId;
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldAtNamedPoint, holdPoint.Name);
```

with:

```csharp
                    // This is the only terminator whose target is resolved purely by
                    // NAME against the whole graph — every other one derives it from the
                    // aircraft node or the cleared taxiway, so it cannot land off the
                    // aircraft's component. Disconnected taxiway islands are routine
                    // (LOWW/KJFK 6 components, EHAM 4, GCLP's 13-node S5 island), and
                    // LoadRoute picks its start node in the DESTINATION's component with
                    // no distance bound — routing to an island would silently start the
                    // route where the aircraft is not.
                    if (!_graph.Nodes.TryGetValue(holdPoint.NodeId, out var holdNode)
                        || holdNode.ComponentId != destComponentId)
                    {
                        string unreachable =
                            $"Cannot taxi to {holdPoint.Name} from your position. Check your entry.";
                        _announcer.AnnounceImmediate(unreachable);
                        lblStatus.Text = unreachable;
                        return;
                    }
                    destNode = holdPoint.NodeId;
                    term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldAtNamedPoint, holdPoint.Name);
```

- [ ] **Step 2: Cover terminator type 4 in the not-found message**

Replace:

```csharp
                string what = terminatorTypeIndex == 1 ? $"taxiway {taxiwayTarget}"
                    : terminatorTypeIndex == 3 ? $"the end of taxiway {lastTaxiway}"
                    : pinnedCross ? $"taxiway {taxiwayTarget} crossing runway {runwayTarget}"
                    : $"runway {runwayTarget}";
```

with:

```csharp
                string what = terminatorTypeIndex == 1 ? $"taxiway {taxiwayTarget}"
                    : terminatorTypeIndex == 3 ? $"the end of taxiway {lastTaxiway}"
                    : terminatorTypeIndex == 4 ? $"holding point {term.Target}"
                    : pinnedCross ? $"taxiway {taxiwayTarget} crossing runway {runwayTarget}"
                    : $"runway {runwayTarget}";
```

(Type 4 returns early today, so this branch is unreachable — it exists so the composer cannot render `runway ` with an empty target if that early return is ever relaxed. `term` is definitely assigned by this point: every switch arm either assigns it or returns.)

- [ ] **Step 3: Build**

Run:

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: `Build succeeded`, 0 errors. If MSB3021 appears, MSFSBA is running — close it and rebuild.

- [ ] **Step 4: Run the full suite**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "fix(taxi): refuse a named holding point on an unreachable graph island

Every other progressive terminator derives its target from the aircraft node
or the cleared taxiway; this one resolves by name against the whole graph, so
it can land on a disconnected island and start the route off-aircraft."
```

---

### Task 4: Repopulate the combo on airport change, and bound the re-resolve

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (field block near `_namedHoldingPoints`, `LoadAirportData`, `ResolveNamedHoldingPoints`, `PopulateTerminatorHoldPointList`)

**Interfaces:**
- Consumes: `AugmentingAirportDataProvider.GetNamedHoldingPoints(icao)`, `NamedHoldingPointResolver.Resolve`.
- Produces: field `private bool _namedHoldingPointsResolved` — Task 5 relies on `ResolveNamedHoldingPoints` running at most once per airport load when raw data is present.

- [ ] **Step 1: Add the latch field**

Directly after the `private List<NamedHoldingPoint> _namedHoldingPoints = new();` declaration, add:

```csharp
    // True once ResolveNamedHoldingPoints has actually SEEN online holding-point data
    // for the loaded airport — whether or not any of it resolved. Gates the retry in
    // PopulateTerminatorHoldPointList: while the async fetch hasn't landed the raw list
    // is empty and retrying costs nothing (the resolve early-returns before scanning),
    // but once raw data has arrived the O(points × nodes) scan must not repeat on every
    // dropdown open and every taxiway row add/remove — which is what happens today at an
    // airport whose points all fail the MAX_SNAP_M test.
    private bool _namedHoldingPointsResolved;
```

- [ ] **Step 2: Clear the latch per airport load**

In `LoadAirportData`, replace:

```csharp
        _namedHoldingPoints = new List<NamedHoldingPoint>();
        cmbTerminatorHoldPoint.Items.Clear();
```

with:

```csharp
        _namedHoldingPoints = new List<NamedHoldingPoint>();
        _namedHoldingPointsResolved = false;
        cmbTerminatorHoldPoint.Items.Clear();
```

- [ ] **Step 3: Set the latch when raw data was present**

In `ResolveNamedHoldingPoints`, replace:

```csharp
        var raw = aug.GetNamedHoldingPoints(_currentIcao);
        if (raw.Count == 0) return;
        _namedHoldingPoints = NamedHoldingPointResolver.Resolve(_graph, raw);
```

with:

```csharp
        var raw = aug.GetNamedHoldingPoints(_currentIcao);
        // Leave the latch clear on an empty source: the online fetch is async, so this
        // is "not yet", not "none" — and retrying is free, we returned before scanning.
        if (raw.Count == 0) return;
        _namedHoldingPoints = NamedHoldingPointResolver.Resolve(_graph, raw);
        _namedHoldingPointsResolved = true;
```

- [ ] **Step 4: Gate the retry on the latch**

In `PopulateTerminatorHoldPointList`, replace:

```csharp
        if (_namedHoldingPoints.Count == 0)
            ResolveNamedHoldingPoints();
```

with:

```csharp
        if (!_namedHoldingPointsResolved)
            ResolveNamedHoldingPoints();
```

- [ ] **Step 5: Refill the combo after an airport load**

In `LoadAirportData`, replace:

```csharp
        ResolveNamedHoldingPoints();
```

with:

```csharp
        ResolveNamedHoldingPoints();
        // Refill the combo too — LoadAirportData cleared its Items above and nothing else
        // repopulates it on an airport change (RefreshTerminatorRow only runs on
        // terminator-type / destination-type / taxiway-row changes). Without this the
        // combo reads as EMPTY to a screen reader until the dropdown is opened, and
        // arrowing a DropDownList does not raise DropDown.
        PopulateTerminatorHoldPointList();
```

- [ ] **Step 6: Update the two stale doc comments**

In `ResolveNamedHoldingPoints`'s XML doc, replace:

```csharp
    /// Cheap (one pass over the graph per point), so it simply recomputes on
    /// demand — the online fetch is async and may land after the airport loads.
```

with:

```csharp
    /// One pass over the graph per point, run at most once per airport load once the
    /// online source has data (_namedHoldingPointsResolved); until then the async fetch
    /// may still be in flight, so an empty source leaves the latch clear and the combo
    /// retries on demand.
```

In `PopulateTerminatorHoldPointList`'s XML doc, replace:

```csharp
    /// selection by label when possible. Re-resolves when the list is empty so a
    /// background online fetch that landed after form open still surfaces. Safe
```

with:

```csharp
    /// selection by label when possible. Re-resolves until the online source has been
    /// seen, so a background fetch that landed after form open still surfaces. Safe
```

- [ ] **Step 7: Build**

Run:

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 8: Run the full suite**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: PASS, 0 failed.

- [ ] **Step 9: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "fix(taxi): repopulate the holding-point combo on airport change, bound the re-resolve

LoadAirportData cleared the combo's Items and never refilled them, so after an
ICAO change the combo read as empty to a screen reader until the dropdown was
opened. The retry is now latched on having seen online data rather than on an
empty result, so an all-dropped airport stops rescanning the graph."
```

---

### Task 5: `taxi_router` diagnostics for the resolve

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (channel field near `_dockingAircraftLog`, and the tail of `ResolveNamedHoldingPoints`)

**Interfaces:**
- Consumes: `Log.Channel(string)` → `LogChannel` with `.Info(string)` / `.Debug(string)`. `Log.Channel` is cached by name (`Channels.GetOrAdd`), so this returns the same instance `TaxiRouter` already holds — no second writer.
- Produces: no new API.

- [ ] **Step 1: Add the channel field**

Directly after the existing `private static readonly LogChannel _dockingAircraftLog = Log.Channel("docking-aircraft");` declaration, add:

```csharp
    private static readonly LogChannel _taxiRouterLog = Log.Channel("taxi_router");
```

- [ ] **Step 2: Log the resolve outcome**

At the end of `ResolveNamedHoldingPoints`, after the `_namedHoldingPointsResolved = true;` line added in Task 4, append:

```csharp

        // Field diagnostics: a "why isn't VIKAS in my list?" report is otherwise
        // unanswerable without an ad-hoc probe against the user's navdata.
        var resolvedNames = new HashSet<string>(
            _namedHoldingPoints.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var rawNames = raw.Select(p => (p.Name ?? "").Trim())
                          .Where(n => n.Length > 0)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList();
        var dropped = rawNames.Where(n => !resolvedNames.Contains(n))
                              .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                              .ToList();
        _taxiRouterLog.Info(
            $"{_currentIcao}: named holding points raw={raw.Count} distinct={rawNames.Count} " +
            $"resolved={_namedHoldingPoints.Count}" +
            (dropped.Count > 0 ? $" dropped={string.Join(", ", dropped)}" : ""));
        foreach (var hp in _namedHoldingPoints)
            _taxiRouterLog.Debug(
                $"  {hp.Name} -> node {hp.NodeId}, {hp.SnapDistanceMeters:F1} m, " +
                $"designated={hp.SnappedToDesignatedNode}, kind={(hp.Kind.Length > 0 ? hp.Kind : "-")}");
```

- [ ] **Step 3: Build**

Run:

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Run the full suite**

Run:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): log named-holding-point resolve outcome to taxi_router.log"
```

---

### Task 6: Record the decisions in the docs

**Files:**
- Modify: `docs/taxi-guidance.md` (the "Progressive Taxi 'Hold at named holding point' terminator" bullet)
- Modify: `CLAUDE.md` (the "Taxi guidance" invariant list)

**Interfaces:** none — documentation only.

- [ ] **Step 1: Extend the taxi-guidance bullet**

In `docs/taxi-guidance.md`, find the bullet beginning `- **Progressive Taxi "Hold at named holding point" terminator (2026-07 — EGLL VIKAS ask).**` and append these two sentences to the end of it, before the newline:

```
 **The snap radii are MEASURED, not guessed — do not tune them.** Probed 2026-07-27 against the owner's fs2024 navdata joined with live Overpass data at EGLL/EDDF/LOWW/LFPG/EHAM/KJFK: requiring a designated node for runway/ILS kinds loses 14 real points at EDDF and 3 at EHAM while gaining nothing (at EGLL every runway/ILS point already snaps designated); rejecting any snap that moves the target runway-ward rejects CORRECT designated nodes, because navdata's HS node routinely sits up to 14 m runway-ward of OSM's painted line (EDDF designated snaps 53 → 31, LOWW 22 → 6); and widening `DESIGNATED_SNAP_M` to the full `MAX_SNAP_M` leaves coverage identical but makes 4 of 7 changed points jump onto a DIFFERENT hold line — EDDF M15 (a runway hold 218 m from the centerline) lands on an HS node 23.7 m away that sits 126 m out, i.e. ~91 m runway-ward. The 15 m preference is tight so it can only pick the hold line the point actually sits on; the 30 m cap bounds worst-case runway-ward movement (26.5 m observed, all at intermediate/untagged holds far from any runway). **Reachability:** the resolved node is checked against the aircraft's `ComponentId` at Calculate time and refused with "Cannot taxi to X from your position." — this is the only terminator whose target is found by NAME across the whole graph, so unlike the others it can land on a disconnected island (LOWW/KJFK 6 components, EHAM 4, GCLP's 13-node S5 island), and `LoadRoute` snaps its start node into the DESTINATION's component with no distance bound. `SnappedToDesignatedNode` describes the chosen NODE; duplicate-name ranking keys on a separate internal "won the ≤15 m preference" flag so the two can never be conflated. Resolve outcomes (raw/distinct/resolved/dropped, plus per-point snap distance) go to `taxi_router.log`.
```

- [ ] **Step 2: Add the CLAUDE.md invariant**

In `CLAUDE.md`, in the `### Taxi guidance` invariant list, insert this bullet directly after the line beginning `- Do NOT implement OSM \`holding_position\` hold-short sharpening`:

```markdown
- Never tune `NamedHoldingPointResolver`'s snap radii: don't widen `DESIGNATED_SNAP_M` (15 m) toward `MAX_SNAP_M` (30 m), don't require a designated node for runway/ILS kinds, and don't add a "never snap runway-ward" guard — all three were probed against real navdata + live OSM at six airports and are worse (widening makes EDDF M15 jump ~91 m onto a different hold line; the guard rejects correct designated nodes because navdata's HS node sits up to 14 m runway-ward of OSM's painted line). → [taxi-guidance.md](docs/taxi-guidance.md)
```

- [ ] **Step 3: Verify both files still render as a single list item / bullet**

Run:

```bash
git diff --stat
```

Expected: `CLAUDE.md` and `docs/taxi-guidance.md` modified; no other files. Confirm with `git diff docs/taxi-guidance.md` that the appended text stayed on the SAME line as the existing bullet (that file's convention is one long line per bullet).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md docs/taxi-guidance.md
git commit -m "docs(taxi): record the measured named-holding-point snap policy and the reachability guard"
```

---

### Task 7: Verify the resolver's routing behaviour did not move

**Files:** none modified — verification only.

**Interfaces:** none.

- [ ] **Step 1: Full clean build and suite**

Run:

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Then:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: `Build succeeded` 0 errors; tests PASS with **0 failed**. The total should be
**1393** — the 1387 the PR reported, plus 2 facts from Task 1 and 1 fact + 3 theory cases
from Task 2 (xUnit counts each `InlineData` separately). A different total means a test was
lost or duplicated; 0 failed is the hard gate.

- [ ] **Step 2: Confirm the flag change moved no targets**

The concern is that Task 1 changed a property `Beats` used to read. The regression pin
`Resolve_duplicate_ranking_ignores_a_designated_node_beyond_the_preference_radius` covers
the case that could move; confirm it and the two pre-existing duplicate tests all pass:

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Resolve_collapses|FullyQualifiedName~Resolve_duplicate_ranking"
```

Expected: 3 passed, 0 failed.

- [ ] **Step 3: Push to the PR branch**

Confirm the head first — pushing to the wrong remote has happened before:

```bash
gh pr view 164 --json headRepositoryOwner,headRefName,maintainerCanModify
```

Expected: `blindflightsimmer`, `feat/named-holding-points`, `true`. Then:

```bash
git push fork feat/named-holding-points
```

- [ ] **Step 4: Add the in-sim test plan to the PR**

Post a comment on PR #164 with the additions to the existing in-sim plan:

1. At EGLL with terminator type already set to "Hold at named holding point", type a different ICAO into the airport box and let it load — the holding-point combo must be populated (or show the none-available sentinel) **without** opening its dropdown.
2. At an airport with named holds, pick one and Calculate — a normal progressive route ending with *"Hold at VIKAS. Set a new route when cleared."*, unchanged from the original plan.
3. Check `%APPDATA%\MSFSBlindAssist\logs\taxi_router.log` after loading EGLL — one `named holding points raw=… distinct=… resolved=…` line plus one line per resolved point.

---

## Self-Review

**Spec coverage:** truthful flag → Task 1. Kind normalization → Task 2. Parking pin → Task 2. Component guard → Task 3. Dead branch → Task 3. Combo repopulate → Task 4. Re-resolve latch → Task 4. Diagnostics → Task 5. Docs (taxi-guidance + CLAUDE.md) → Task 6. Verification → Task 7. Snap policy unchanged — enforced by the Global Constraints and pinned by Task 1 Step 2 and Task 7 Step 2.

**Type consistency:** `WonDesignatedPreference` is introduced in Task 1 Step 3 and read only in Task 1 Step 5 (`Beats`) — internal, so the cross-assembly test project never touches it. `SnappedToDesignatedNode` stays public and is read in Task 1's tests and Task 5's log line. `DisplayLabel`'s output strings are unchanged by Task 2, so Task 3's `hp.DisplayLabel == label` lookup and the `NO_NAMED_HOLD_POINTS` sentinel comparison remain correct. `_namedHoldingPointsResolved` is declared in Task 4 Step 1 and read in Steps 3–4; Task 5 Step 2 appends after the Step 3 assignment.
