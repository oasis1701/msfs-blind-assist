# PR #127 Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the three open PR #127 review findings — combined standstill announcement, one intersection entry per meeting point, and full-length-entrance filtering at displaced thresholds.

**Architecture:** All geometry work lands in `TaxiGraph.GetRunwayIntersections` (along-track clustering + optional lineup-point filter, TDD against the existing synthetic-equator suite). The form gets two small changes: pass the lineup point, and compose the post-Calculate standstill speech as one utterance.

**Tech Stack:** C# 13 / .NET 10, xUnit (`tests/MSFSBlindAssist.Tests`), WinForms.

**Spec:** `docs/design/2026-07-10-pr127-intersection-departure-review-fixes-design.md`

## Global Constraints

- Branch: `feat/intersection-departures` (PR #127 head, pushes go to the `fork` remote — verified `branch.feat/intersection-departures.remote = fork`).
- Build the solution or pass `-p:Platform=x64`; NEVER `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj` alone.
- Test command: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- Every commit message ends with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Screen-reader rules: no announcements for direct UI interactions; `AnnounceImmediate` only for validation/confirmation speech already designed in.
- Constants fixed by the approved design: `CLUSTER_GAP_M = 100.0`, `FULL_LENGTH_MARGIN_M = 50.0`, mid-runway sanity clamp (`lineupAlong > totalLen / 2` → ignore filter).

---

### Task 1: Along-track clustering in GetRunwayIntersections (finding 4)

**Files:**
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs` (method `GetRunwayIntersections`, ~line 1125)
- Test: `tests/MSFSBlindAssist.Tests/RunwayIntersectionTests.cs`

**Interfaces:**
- Consumes: existing `ProjectOntoCenterline`, `_taxiwayNodeIndex`, `Nodes`, `FastDistanceMeters`.
- Produces: `GetRunwayIntersections` now returns one `RunwayIntersection` per MEETING POINT (cluster), still sorted threshold-first. Signature unchanged in this task.

- [ ] **Step 1: Update the one existing test whose premise changes, and add the two-branch test (both must fail)**

In `RunwayIntersectionTests.cs`, REPLACE the test `Closest_node_to_the_centerline_wins_within_a_taxiway` (its two R nodes are 100.02 m apart — exactly astride the new cluster gap; its intent, min-perp selection, now applies WITHIN a cluster, so move the nodes to 66.7 m apart):

```csharp
    [Fact]
    public void Closest_node_to_the_centerline_wins_within_a_meeting_point()
    {
        // Taxiway R has two qualifying nodes 66.7 m apart along the runway —
        // one dense polyline entrance, i.e. ONE meeting point (cluster): a node
        // 20 m off the centerline at 1200 m along, and a node 2 m off at
        // 1266.7 m along. One entry, and it is the 2 m node (minimum
        // perpendicular distance), not the first-along one.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0108, EndLat = 20.0 * DEG_PER_M, EndLon = 0.0108, Name = "R" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0114, EndLat = 2.0 * DEG_PER_M,  EndLon = 0.0114, Name = "R" },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var result = Departing09(g);

        var r = Assert.Single(result);
        Assert.Equal("R", r.TaxiwayName);
        Assert.Equal(0.0114 * M_PER_DEG, r.AlongMetersFromThreshold, 1);
    }
```

ADD below it:

```csharp
    [Fact]
    public void A_taxiway_meeting_the_runway_twice_yields_one_entry_per_meeting_point()
    {
        // Taxiway V reaches the pavement at 1000 m and again at 1300 m — the
        // paired high-speed-exit shape sharing one name (real navdata: KORD Y4
        // meets 04R/22L at 1361 m AND 1684 m). The 300 m along-track gap
        // (> CLUSTER_GAP_M 100) makes these two distinct meeting points, each
        // offered as its own entry, threshold-first.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.009,  EndLat = 0, EndLon = 0.009,  Name = "V" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0117, EndLat = 0, EndLon = 0.0117, Name = "V" },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var result = Departing09(g);

        Assert.Equal(2, result.Count);
        Assert.All(result, ix => Assert.Equal("V", ix.TaxiwayName));
        Assert.Equal(0.009 * M_PER_DEG,  result[0].AlongMetersFromThreshold, 1);
        Assert.Equal(0.0117 * M_PER_DEG, result[1].AlongMetersFromThreshold, 1);
    }
```

Also update the file-header comment bullet `- one entry per taxiway name: the qualifying node CLOSEST to the centerline` to:

```
//   - one entry per MEETING POINT: qualifying nodes on the same taxiway are
//     clustered by along-track gap (>100 m starts a new cluster); each
//     cluster contributes its node closest to the centerline
```

- [ ] **Step 2: Run the two tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RunwayIntersection"`
Expected: `A_taxiway_meeting_the_runway_twice...` FAILS (count 1, expected 2). `Closest_node...meeting_point` PASSES already (min-perp logic exists) — that is fine; the two-branch test is the red driver.

- [ ] **Step 3: Implement clustering**

In `TaxiGraph.cs`, replace the body of the `foreach (var kv in _taxiwayNodeIndex)` loop in `GetRunwayIntersections` (the single `bestNode` pick) with collect-sort-cluster-emit, and update the method's XML doc:

In the `<summary>`, replace the sentence `For each named taxiway we keep the single node closest to the runway centerline; if that node sits within the runway half-width (i.e. the taxiway actually reaches the pavement) it becomes an intersection, with its distance measured from the DEPARTURE threshold (so "remaining" is the runway ahead in the takeoff direction).` with:

```
    /// Qualifying nodes on the same taxiway are clustered by along-track
    /// distance (a gap > 100 m starts a new cluster — paired high-speed-exit
    /// branches sharing one name sit 105-555 m apart in fs2024 navdata, while
    /// polyline nodes along a single entrance are far denser). Each cluster is
    /// ONE meeting point and contributes its node closest to the centerline,
    /// with distance measured from the DEPARTURE threshold (so "remaining" is
    /// the runway ahead in the takeoff direction).
```

Replace the loop body:

```csharp
        // Two qualifying nodes on the SAME taxiway further apart than this along
        // the runway are distinct meeting points (the paired branches of a
        // high-speed exit sharing one name). Over-splitting is benign — both
        // points are genuinely on the runway and the labels carry distances;
        // under-splitting hides a real branch, which is the failure that matters.
        const double CLUSTER_GAP_M = 100.0;

        foreach (var kv in _taxiwayNodeIndex)
        {
            string twName = kv.Key;
            if (string.IsNullOrEmpty(twName)) continue;

            // All qualifying nodes on this taxiway. Gating HERE — not after
            // picking a best node — means a taxiway's near-threshold connector
            // node (along < MIN_ALONG_M) or a far-end nub can't shadow a genuine
            // mid-field entrance further down the same taxiway.
            var candidates = new List<(double along, double perp, int nodeId, double lat, double lon)>();
            foreach (int nid in kv.Value)
            {
                if (!Nodes.TryGetValue(nid, out var n)) continue;
                var (perp, along, projLat, projLon) =
                    ProjectOntoCenterline(n.Latitude, n.Longitude, thrLat, thrLon, farLat, farLon);
                if (along < MIN_ALONG_M || along > totalLen) continue;
                if (totalLen - along < MIN_REMAINING_M) continue;
                if (perp > maxPerp) continue;
                candidates.Add((along, perp, nid, projLat, projLon));
            }
            if (candidates.Count == 0) continue;
            candidates.Sort((a, b) => a.along.CompareTo(b.along));

            // Walk the sorted candidates; a >CLUSTER_GAP_M along-track gap closes
            // the current cluster. Emit each cluster's min-perpendicular node.
            int clusterStart = 0;
            for (int i = 1; i <= candidates.Count; i++)
            {
                if (i < candidates.Count &&
                    candidates[i].along - candidates[i - 1].along <= CLUSTER_GAP_M)
                    continue;

                var best = candidates[clusterStart];
                for (int j = clusterStart + 1; j < i; j++)
                    if (candidates[j].perp < best.perp) best = candidates[j];

                result.Add(new RunwayIntersection
                {
                    TaxiwayName = twName,
                    NodeId = best.nodeId,
                    Latitude = best.lat,
                    Longitude = best.lon,
                    AlongMetersFromThreshold = best.along,
                    RemainingMeters = totalLen - best.along,
                });
                clusterStart = i;
            }
        }
```

(The trailing `result.Sort(...)` and `return result;` stay as they are. The comment above `MIN_ALONG_M`/`MIN_REMAINING_M` and the `maxPerp` slop comment stay.)

- [ ] **Step 4: Run the RunwayIntersection suite — all pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RunwayIntersection"`
Expected: PASS, 9 tests (8 previous + 1 new; 1 replaced in place).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/TaxiGraph.cs tests/MSFSBlindAssist.Tests/RunwayIntersectionTests.cs
git commit -m "feat(taxi): one intersection entry per runway meeting point

A taxiway can meet a runway at two well-separated points (paired
high-speed-exit branches sharing one name: KORD Y4 meets 04R/22L at
1361 m AND 1684 m; 23 such taxiways across ten majors in fs2024).
Cluster qualifying nodes by along-track gap (>100 m = new meeting
point) and emit each cluster's closest-to-centerline node, instead of
one arbitrary entry per taxiway name. Labels already carry distances,
so same-named entries stay distinguishable in the picker.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Optional lineup-point filter (finding 5)

**Files:**
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs` (`GetRunwayIntersections` signature + min-along)
- Test: `tests/MSFSBlindAssist.Tests/RunwayIntersectionTests.cs`

**Interfaces:**
- Produces: `public List<RunwayIntersection> GetRunwayIntersections(double thrLat, double thrLon, double farLat, double farLon, double halfWidthMeters, double? lineupLat = null, double? lineupLon = null)` — Task 3 passes the two new arguments from the form.

- [ ] **Step 1: Write the three failing tests**

Add to `RunwayIntersectionTests.cs`:

```csharp
    // --- Full-length entrance filter (displaced thresholds) -----------------------

    // Shared fixture for the lineup-point filter: FL serves the full-length
    // lineup point at 300 m down the physical pavement (displaced-threshold
    // shape, e.g. KJFK 22R's ~1 km displacement), NEAR sits 33 m past it
    // (inside the 50 m margin), MID is a genuine mid-field shortcut.
    private static TaxiGraph BuildDisplacedGraph()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0027, EndLat = 0, EndLon = 0.0027, Name = "FL" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.003,  EndLat = 0, EndLon = 0.003,  Name = "NEAR" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.009,  EndLat = 0, EndLon = 0.009,  Name = "MID" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void Lineup_point_filters_the_full_length_entrance_and_its_margin()
    {
        // Lineup point at 300 m along: FL (300 m) and NEAR (333 m ≤ 300+50)
        // are the normal full-length entrance, not shortcuts; MID survives.
        var result = BuildDisplacedGraph().GetRunwayIntersections(
            0, 0, 0, FarLon, HalfWidthM, lineupLat: 0.0, lineupLon: 0.0027);

        var only = Assert.Single(result);
        Assert.Equal("MID", only.TaxiwayName);
    }

    [Fact]
    public void Omitted_lineup_point_keeps_all_entries()
    {
        var result = Departing09(BuildDisplacedGraph());

        Assert.Equal(new[] { "FL", "NEAR", "MID" },
            result.Select(ix => ix.TaxiwayName).ToArray());
    }

    [Fact]
    public void Lineup_point_beyond_mid_runway_is_treated_as_corrupt_and_ignored()
    {
        // A start row projecting past the runway midpoint (2000 m of 3000 m)
        // can't be a real full-length lineup point — the filter must switch
        // off rather than empty the list.
        var result = BuildDisplacedGraph().GetRunwayIntersections(
            0, 0, 0, FarLon, HalfWidthM, lineupLat: 0.0, lineupLon: 0.018);

        Assert.Equal(3, result.Count);
    }
```

- [ ] **Step 2: Run them to verify the first fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RunwayIntersection"`
Expected: compile error (no 7-argument overload). That IS the red state for all three.

- [ ] **Step 3: Implement the filter**

In `TaxiGraph.cs`:

1. Change the signature:

```csharp
    public List<RunwayIntersection> GetRunwayIntersections(
        double thrLat, double thrLon, double farLat, double farLon, double halfWidthMeters,
        double? lineupLat = null, double? lineupLon = null)
```

2. Add the two params to the XML doc after the `halfWidthMeters` param:

```csharp
    /// <param name="lineupLat">Optional: latitude of the full-length lineup point
    /// (start-table row). When given, meeting points at or before it (+50 m) are
    /// dropped — they are the NORMAL departure entrance, not an intersection
    /// shortcut. Matters at displaced-threshold runways (KJFK 22R: ~1 km displaced),
    /// where that entrance otherwise lists as a bogus "intersection".</param>
    /// <param name="lineupLon">Optional: longitude of the full-length lineup point.</param>
```

3. After the `const double MIN_REMAINING_M = 45.0;` line, add:

```csharp
        // Meeting points at or before the full-length lineup point are the normal
        // departure entrance, not a shortcut; 50 m of margin absorbs connector
        // geometry around the lineup spot.
        const double FULL_LENGTH_MARGIN_M = 50.0;

        double minAlong = MIN_ALONG_M;
        if (lineupLat.HasValue && lineupLon.HasValue)
        {
            var (_, lineupAlong, _, _) = ProjectOntoCenterline(
                lineupLat.Value, lineupLon.Value, thrLat, thrLon, farLat, farLon);
            // Sanity clamp: a lineup point past mid-runway is a corrupt start
            // row — ignore the filter rather than emptying the list.
            if (lineupAlong > 0 && lineupAlong <= totalLen / 2.0)
                minAlong = Math.Max(minAlong, lineupAlong + FULL_LENGTH_MARGIN_M);
        }
```

4. In the candidate gate inside the loop, change `if (along < MIN_ALONG_M || along > totalLen) continue;` to:

```csharp
                if (along < minAlong || along > totalLen) continue;
```

- [ ] **Step 4: Run the RunwayIntersection suite — all pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RunwayIntersection"`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/TaxiGraph.cs tests/MSFSBlindAssist.Tests/RunwayIntersectionTests.cs
git commit -m "feat(taxi): filter the full-length entrance out of the intersection list

GetRunwayIntersections optionally takes the start-table lineup point and
drops meeting points at or before it (+50 m margin) — at displaced-
threshold runways (KJFK 22R: ~1 km) the normal full-length entrance
otherwise lists as a bogus ~1,000 m 'intersection'. A lineup point
projecting past mid-runway is treated as a corrupt start row and
ignored rather than emptying the list.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Form passes the lineup point; duplicate-guard comment (finding 5)

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (`PopulateIntersections`, ~line 1363)

**Interfaces:**
- Consumes: Task 2's `GetRunwayIntersections(..., double? lineupLat, double? lineupLon)`; existing `_destinationThresholdMap` (`Dictionary<string, (double lat, double lon)>`, keyed by the destination display name, holding the start-table lineup point).

- [ ] **Step 1: Pass the lineup point**

In `PopulateIntersections`, replace:

```csharp
        foreach (var ix in _graph.GetRunwayIntersections(
                     rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM))
```

with:

```csharp
        // The start-table lineup point (same one full-length departures line up
        // at) lets the enumeration drop the normal full-length entrance — only
        // genuine shortcuts past it are offered (displaced-threshold fix).
        double? lineupLat = null, lineupLon = null;
        if (_destinationThresholdMap.TryGetValue(destName, out var lineup))
        {
            lineupLat = lineup.lat;
            lineupLon = lineup.lon;
        }

        foreach (var ix in _graph.GetRunwayIntersections(
                     rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
                     lineupLat, lineupLon))
```

- [ ] **Step 2: Update the duplicate-label guard comment**

Replace:

```csharp
            // Guard against a duplicate label (two same-named runs) shadowing an entry.
```

with:

```csharp
            // Same-named taxiways now legitimately produce MULTIPLE entries (one
            // per meeting point), distinguished by the distances in the label.
            // This guard only fires if display rounding collapses two close
            // meeting points to identical text — then the first (closer to the
            // threshold) wins and the duplicate is dropped rather than shadowed.
```

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): pass the lineup point into the intersection enumeration

PopulateIntersections hands GetRunwayIntersections the start-table
lineup point from _destinationThresholdMap so the normal full-length
entrance is filtered from the intersection picker; the duplicate-label
guard comment now explains its real (rounding-collision) role under
per-meeting-point entries.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Combined standstill announcement (finding 3)

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (`OnCalculateClicked`, ~lines 2310-2331)

**Interfaces:**
- Consumes: existing `_guidanceManager.LastRouteReachWarning`, `intersection` local, `_announcer.AnnounceImmediate`.

- [ ] **Step 1: Replace the two announcements with one composed utterance**

Replace this block (both the reach-warning `if` and the intersection `if`, keeping the explanatory intent in the new comment):

```csharp
        // Speak the unreachable-runway warning LAST — after StartGuidance, whose
        // first-taxiway callout would otherwise stomp it. The warning was showing
        // in the box but never heard at Calculate (in-sim 2026-06-13) because it
        // was spoken before StartGuidance. As the final standstill announcement
        // it's heard in full; the box (route summary) still shows it too.
        // (No-op for Progressive Taxi: LastRouteReachWarning is only set for
        // runway destinations, and progressive legs never set a lineup target.)
        if (!string.IsNullOrEmpty(_guidanceManager.LastRouteReachWarning))
            _announcer.AnnounceImmediate(_guidanceManager.LastRouteReachWarning);

        // Intersection departure: confirm the entry point + runway remaining
        // ahead. Spoken last so StartGuidance's first-taxiway callout doesn't
        // stomp it (same reasoning as the reach warning above).
        if (intersection != null)
        {
            string rwyLabel = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
                ? "runway " + destName.Substring(7).Trim()
                : destName;
            _announcer.AnnounceImmediate(
                $"Intersection {intersection.TaxiwayName} departure, {rwyLabel}. " +
                $"About {DistanceFormatter.FromMetres(intersection.RemainingMeters)} of runway ahead.");
        }
```

with:

```csharp
        // Post-StartGuidance standstill speech — ONE utterance. It must come
        // after StartGuidance (whose first-taxiway callout would otherwise stomp
        // it: the reach warning was showing in the box but never heard at
        // Calculate, in-sim 2026-06-13, when spoken before StartGuidance), and
        // it must be a SINGLE AnnounceImmediate: consecutive calls stomp each
        // other, so the intersection confirmation and the reach warning are
        // joined, warning last so the safety-relevant text ends the utterance.
        // (No-op for Progressive Taxi: LastRouteReachWarning is only set for
        // runway destinations, and progressive legs never set a lineup target.)
        var standstillParts = new List<string>();
        if (intersection != null)
        {
            string rwyLabel = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
                ? "runway " + destName.Substring(7).Trim()
                : destName;
            standstillParts.Add(
                $"Intersection {intersection.TaxiwayName} departure, {rwyLabel}. " +
                $"About {DistanceFormatter.FromMetres(intersection.RemainingMeters)} of runway ahead.");
        }
        if (!string.IsNullOrEmpty(_guidanceManager.LastRouteReachWarning))
            standstillParts.Add(_guidanceManager.LastRouteReachWarning);
        if (standstillParts.Count > 0)
            _announcer.AnnounceImmediate(string.Join(" ", standstillParts));
```

- [ ] **Step 2: Build + full test suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` then `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: build 0 errors; all tests pass (1056 = 1052 + 4 net-new).

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "fix(taxi): compose the Calculate standstill speech as one utterance

The intersection-departure confirmation and LastRouteReachWarning were
two back-to-back AnnounceImmediate calls, so the second cut the first
off. Join them into a single announcement (warning last, so the
safety-relevant text ends the utterance), preserving the after-
StartGuidance placement.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: PR body update, push, CI green

**Files:**
- Modify: PR #127 body (via `gh pr edit`), scratchpad copy at `<scratchpad>/pr127-body.md`

- [ ] **Step 1: Update the PR body**

In the scratchpad `pr127-body.md`: under "How it works", extend the `TaxiGraph.GetRunwayIntersections` bullet with the clustering + lineup-filter behavior; update the Tests paragraph to the new test count; append two items to the in-sim test plan:

```
7. **Paired-exit taxiway** (e.g. KORD 22L, taxiway Y4): the picker lists BOTH same-named meeting points with distinct remaining/from-threshold distances, and routing goes to whichever is selected.
8. **Displaced threshold, full-length entrance** (e.g. KJFK 22R, ~1 km displaced): the taxiway serving the normal full-length lineup point is NOT listed; only genuine shortcuts past it appear.
```

And a "Review fixes (2026-07-10)" section noting: combined standstill announcement, one entry per meeting point, full-length entrance filter (link the design doc path).

Then: `gh pr edit 127 --body-file <scratchpad>/pr127-body.md`

- [ ] **Step 2: Push and verify CI**

```bash
git push fork feat/intersection-departures
gh pr checks 127 --watch --interval 15
```

Expected: push succeeds; "Pure-logic tests (xUnit)" = pass; `gh pr view 127 --json mergeable` = MERGEABLE.
