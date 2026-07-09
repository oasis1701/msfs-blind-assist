# Codebase Cleanup Phase 2 Implementation Plan (PR-4..PR-7)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the SAFE-bucket efficiency and consistency fixes from the cleanup spec (`docs/design/2026-07-07-codebase-cleanup-design.md`, findings register IDs cited per task) as four themed PRs, with the Phase-1 characterization suite (580 tests) as the safety net.

**Architecture:** Four independent branches off `main` (NOT stacked — a stacked base-merge auto-closes dependent PRs, learned in Phase 1): `perf/simconnect-hot-path`, `perf/services-hot-path`, `perf/navdb`, `chore/aircraft-forms-safe`. Executed and merged serially. Every change must be behavior-neutral: identical outputs/announcements/protocol, only less work per frame or consolidated code. Line numbers in the findings register predate PR-2's deletions — **locate every edit by symbol, not line number**.

**Tech Stack:** .NET 10 / C# 13, WinForms, xUnit, git + gh.

## Global Constraints

- `main` is protected; branch + PR each; NEVER push without Robin's explicit permission.
- Build: `dotnet build MSFSBlindAssist.sln -c Debug` (0 errors, 0 warnings expected — baseline is 0/0). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → **580/580** must stay green after every task.
- Behavior-neutral only: no spoken-output changes, no guidance-math changes, no SimConnect protocol changes, no threading-semantics changes beyond the named lock-compliance fixes. If an edit turns out to require a behavior choice, STOP → escalate.
- CLAUDE.md invariants apply everywhere (announcement rules, calc-path routing, invariant RPN formatting).
- The RISKY-bucket items (SC-12, SV-4's VG half, AC-2/4/5/6/7, AC-11 wording, FM-5, Thread.Sleep removals, JS agents) are PHASE 3 — do not touch them even when adjacent.
- Scope reduction vs spec, decided at planning: (a) the AC-15 if-chain→switch conversions are scoped to hoisting the per-frame `G_FORCE` branch to the top of the FBW A320 ladder only — full switch rewrites of 45-185-branch ladders in sim-verified aircraft defs are high transcription risk for medium gain and are deferred to per-aircraft feature work; (b) SV-10 (3s-tick LINQ micro, spec-marked optional) is skipped.

---

# PR-4: `perf/simconnect-hot-path` (branch off main)

### Task 4.1: SC-1 — per-frame dispatch reverse map + HighFrequency log gate

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.VarCache.cs` (the individual-var response path: the `variableDataDefinitions.FirstOrDefault(x => x.Value == requestId)` scan, the `ContainsKey`+indexer double lookup, the value-capturing `AddOrUpdate` closure, the unconditional per-fire `Log.Debug`)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs` (field declarations near `variableDataDefinitions`), `SimConnectManager.Setup.cs` (registration site that populates `variableDataDefinitions`), and any site that removes/clears definitions (aircraft switch) — the reverse map must be maintained at every write site.

**Interfaces:**
- Produces: `private readonly ConcurrentDictionary<int, string> requestIdToVarKey` — kept in exact sync with `variableDataDefinitions` (add on register, remove on clear/unregister; find ALL mutation sites by grepping `variableDataDefinitions`).

- [ ] **Step 1:** Read the dispatch path. Replace the `FirstOrDefault` scan with `requestIdToVarKey.TryGetValue(requestId, out var varKey)`; replace ContainsKey+indexer pairs with `TryGetValue`; replace the value-capturing `AddOrUpdate` with an overload/form that doesn't allocate a closure per call (`lastVariableValues[varKey] = value` is the existing simpler idiom elsewhere in the file — match it if semantics allow, i.e. no concurrent-update merge logic is actually used).
- [ ] **Step 2:** Gate the per-fire `Log.Debug("SimConnect", $"Firing SimVarUpdated for {varKey}...")` on `!varDef.HighFrequency` (read `SimVarDefinition` to confirm the flag name; `G_FORCE` in `BaseAircraftDefinition` sets it). The string interpolation must sit INSIDE the gate so no formatting happens for skipped lines.
- [ ] **Step 3:** Populate/maintain the reverse map at every `variableDataDefinitions` mutation site (grep them all; list each in your report).
- [ ] **Step 4:** Build + full suite green. Commit: `perf(simconnect): O(1) requestId lookup + gate per-frame dispatch logging`

### Task 4.2: SC-2 + SC-10 — PMDG box-once + cached FieldInfo

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs` (change-detection loop over `s_dataFields`; `GetFieldValue`), `MSFSBlindAssist/SimConnect/PMDGNG3DataManager.cs` (same + `GetStringFieldValue`)

- [ ] **Step 1:** In each manager's snapshot-diff loop, box each struct once before the loop and pass the boxes to `FieldInfo.GetValue`:

```csharp
object oldBox = _lastDataSnapshot;   // one boxing copy instead of one per field
object newBox = newData;
foreach (var field in s_dataFields) {
    var oldVal = field.GetValue(oldBox);
    var newVal = field.GetValue(newBox);
    ...
}
```

Apply the same box-once inside `CompareArrayField` if it re-boxes the parent struct per element (read it first).
- [ ] **Step 2:** In `GetFieldValue` (both managers) and `GetStringFieldValue` (NG3): build a `private static readonly Dictionary<string, FieldInfo> s_fieldsByName` once from `s_dataFields` (static ctor or lazy init) and use it instead of `typeof(...).GetField(name)` per call; box the snapshot once per call.
- [ ] **Step 3:** Build + full suite green (PmdgCduDecodeTests cover the NG3 decode paths). Commit: `perf(pmdg): box snapshots once per diff, cache FieldInfo lookups`

### Task 4.3: SC-3 (safe half) + SC-14 log item — LED-var lookup dictionary + quiet high-rate command sends

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs` (the two `variables.Values.FirstOrDefault(v => v.LedVariable == e.VariableName)` scan sites in the MobiFlight LVar/LED event handlers)
- Modify: `MSFSBlindAssist/SimConnect/MobiFlightWasmModule.cs` (`ProcessDefaultChannelLVarUpdate`'s per-lvar `Log.Debug`; `SendMFCommand`'s per-command `Log.Debug`)

- [ ] **Step 1:** Build a cached `Dictionary<string, SimVarDefinition> ledVarToDef` from the current aircraft's variables (populate where the aircraft's variable set is installed — find the `CurrentAircraft` change site; invalidate/rebuild there). Replace both `FirstOrDefault` scans with `TryGetValue`. Vars without `LedVariable` are simply absent from the map.
- [ ] **Step 2:** Remove (or gate to a compile-time-off verbosity) the per-lvar `Log.Debug` in `ProcessDefaultChannelLVarUpdate`. Do NOT add change-filtering — the re-fire semantics stay (spec: RISKY half deferred).
- [ ] **Step 3:** `SendMFCommand`: add a `bool quiet = false` parameter (default keeps current logging); pass `quiet: true` from the calc-path write route used per-frame (find the A380 seat-motor path per docs/a380x.md; if the routing is shared, gate on a caller-supplied flag from `ExecuteCalculatorCode`'s high-rate callers only if cleanly reachable — otherwise leave callers unchanged and only remove the redundant duplicate logging inside `SendMFCommand` itself if any; report what was feasible).
- [ ] **Step 4:** Build + full suite green. Commit: `perf(mobiflight): cached LED-var lookup, quiet high-rate command logging`

### Task 4.4: SC-7 — extract the duplicated ECAM block

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.VarCache.cs` (the ~50-line ECAM collection block duplicated between the individual-response path and the batch path — EWD code decode, `ecamStringData`/`ecamAnnouncementData` writes, 14-line modulo trigger, `ECAMDataEventArgs` construction, `AnnounceECAMChanges` call)

- [ ] **Step 1:** Diff the two blocks first (extract both to scratch, `diff` them). They were audited as verbatim-identical; if they have since drifted, STOP → escalate with the diff.
- [ ] **Step 2:** Extract one `private void ProcessEcamLine(string varKey, double value)` containing the block verbatim; call it from both sites. Zero logic edits — the diff for each site must be delete-block+one-call-line.
- [ ] **Step 3:** Build + full suite green. Commit: `refactor(simconnect): single ProcessEcamLine for individual + batch paths`

### Task 4.5: SC-9 (safe part) — port 0xA3/0xA4 arrows to the 777 + dedupe its two inline copies

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs` (the TWO inline cell-symbol switches — locate by searching for `0xA1`/`0xA2` or the `'<'`/`'>'` arms)
- Test: extend `tests/MSFSBlindAssist.Tests/PmdgCduDecodeTests.cs`

**Interfaces:**
- Consumes: `PMDGNG3DataManager.DecodeCellSymbol` (internal static, pinned by PmdgCduDecodeTests) as the reference semantics.

- [ ] **Step 1:** Extract the 777's duplicated inline switch into one `internal static char DecodeCellSymbol(byte b)` on `PMDG777DataManager`, matching the NG3's semantics INCLUDING the `0xA3 → '↑'` / `0xA4 → '↓'` arms the 777 currently lacks. Both former inline sites call it.
- [ ] **Step 2:** Add a test to `PmdgCduDecodeTests.cs` asserting the 777 and NG3 decoders agree on every byte 0x00-0xFF:

```csharp
[Fact]
public void Pmdg777_decoder_agrees_with_NG3_for_all_bytes()
{
    for (int b = 0; b <= 0xFF; b++)
        Assert.Equal(PMDGNG3DataManager.DecodeCellSymbol((byte)b),
                     PMDG777DataManager.DecodeCellSymbol((byte)b));
}
```

- [ ] **Step 3:** Build + full suite green. NOTE for the PR body: this is the one deliberate behavior DELTA in PR-4 — 777 CDU cells containing 0xA3/0xA4 now render arrows instead of spaces (restoring parity with the NG3; medium-confidence the 777 ever emits them). Commit: `fix(pmdg777): CDU decodes 0xA3/0xA4 arrows; single decoder shared by both former inline copies`

### Task 4.6: SC-11 — per-batch prebuilt arrays

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.VarCache.cs` (`ProcessContinuousBatch` or equivalent: the `foreach (var kvp in continuousVariableIndexMap)` that skips other batches' vars, the per-var `variables.TryGetValue` re-resolution, and the LINQ `Any`+closure on `OnlyAnnounceValueDescriptionMatches`), `SimConnectManager.Monitoring.cs` (`StartContinuousMonitoring`, where `continuousVariableIndexMap` is built)

- [ ] **Step 1:** At `StartContinuousMonitoring`, additionally build `private (string key, int index, SimVarDefinition def)[][] batchVarArrays` (one array per batch, 1-based or 0-based to match existing batch numbering — read it). Clear it wherever `continuousVariableIndexMap` is cleared.
- [ ] **Step 2:** Rewrite the batch delivery loop to iterate ONLY `batchVarArrays[batchNum]` — same visitation set, no per-var dictionary lookups. Keep `continuousVariableIndexMap` itself (other consumers may read it — grep; report).
- [ ] **Step 3:** Replace the `Any(...)` closure with a plain foreach over `ValueDescriptions.Keys`.
- [ ] **Step 4:** Build + full suite green. Commit: `perf(simconnect): prebuilt per-batch var arrays for 1 Hz batch delivery`

### Task 4.7: SC-13 + SC-14 remainder — registration ordering + disposal hygiene

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs` (`RegisterClientEvents`: move `eventIds[kvp.Key] = eventId;` inside the `try`, AFTER `MapClientEventToSimEvent` succeeds)
- Modify: `MSFSBlindAssist/SimConnect/MobiFlightWasmModule.cs` (declare `: IDisposable` — the `Dispose()` method already exists)
- Modify: `MSFSBlindAssist/Utils/SimulatorDetector.cs` (`DetectRunningSimulator`: dispose every `Process` from `GetProcessesByName` — wrap in try/finally or foreach-dispose after use)
- Modify: `MSFSBlindAssist/SimConnect/SimVarMonitor.cs` (`ProcessUpdate`: ContainsKey + double indexer → single `TryGetValue`)

- [ ] **Step 1:** Apply all four edits (each is mechanical; read each surrounding method first).
- [ ] **Step 2:** Build + full suite green (SimVarMonitor has no direct tests — the edit must be a pure lookup-idiom swap; reviewer verifies). Commit: `chore(simconnect): registration-ordering fix, IDisposable declaration, Process disposal, TryGetValue idiom`

### Task 4.8: PR-4 wrap (controller)

- [ ] Full build + suite; `git diff main --stat` review; ASK ROBIN to push + open PR (body: per-task summary, the Task 4.5 behavior-delta note, suggested in-sim smoke: PMDG 777 CDU pages, A32NX ECAM window announcements, aircraft switch, one MobiFlight-dependent panel).

---

# PR-5: `perf/services-hot-path` (branch off main)

### Task 5.1: SV-1 — runway-incursion check caching

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (`CheckRunwayIncursion` and the fields near its callers; the `RoutePoints()` reference-keyed cache idiom in the same file is the pattern to mirror)

- [ ] **Step 1:** Read `CheckRunwayIncursion` fully, plus `TaxiGraph` (`Nodes`, hold-short node types) and every site that mutates `_route`/`_currentSegmentIndex`.
- [ ] **Step 2:** Add (a) a per-graph cache of HS/ILS-HS nodes: `private List<TaxiNode>? _cachedHoldShortNodes; private TaxiGraph? _holdShortNodesGraph;` — rebuild when `_graph` reference changes (graph is immutable after Build); (b) a reference-keyed cache for the on-route hold-short set: key on `(object.ReferenceEquals(_route, cachedRoute) && _currentSegmentIndex == cachedIndex)`, mirroring `RoutePoints()`. Both caches live and are read INSIDE `_stateLock` (same as today's data) — no new lock.
- [ ] **Step 3:** The per-frame body then scans only `_cachedHoldShortNodes` (not all nodes) against the cached on-route set. The RESULTING WARNINGS must be identical: same candidate set, same distances, same cooldown behavior. Write the equivalence argument in your report (why the cached sets equal the recomputed ones at every mutation point — route recalc, segment advance, graph reload, StopGuidance).
- [ ] **Step 4:** Build + full suite green. Commit: `perf(taxi): cache hold-short node set + on-route set in runway-incursion check`

### Task 5.2: SV-2 + SV-3 — `_stateLock` compliance

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (`TryGetRunwayLineupReference`: wrap body in `lock (_stateLock)`; `ClearWhereAmICache`: take `_stateLock` around the two-field invalidation)
- Modify: `docs/taxi-guidance.md` (§Concurrency: the stale "unlocked by design — readers capture a local" sentence about the where-am-I cache → now locked like its twin `OnAirportDataUpdated`)

- [ ] **Step 1:** Apply both lock wraps (verify neither method takes any other lock — no ordering hazard; state that check in the report). **Step 2:** Doc edit. **Step 3:** Build + suite green. Commit: `fix(taxi): lock compliance for TryGetRunwayLineupReference + ClearWhereAmICache`

### Task 5.3: SV-4 (taxi half) — UtcNow standardization

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` + `TaxiGuidanceManager.Routing.cs` (every `DateTime.Now` stamp/compare pair: off-route persistence, segment-advance grace, recalc/speed/incursion cooldowns — enumerate with `grep -n "DateTime.Now" MSFSBlindAssist/Services/TaxiGuidanceManager*.cs`)

- [ ] **Step 1:** For each pair, swap BOTH the stamp and the comparison to `DateTime.UtcNow`. A pair is only safe to swap together — list each field + its read sites in the report. Do NOT touch `VisualGuidanceManager`, `HandFlyManager`, or `TakeoffAssistManager` (VG deltaTime is Phase-3; the others are out of this task's named scope — leave for consistency review there).
- [ ] **Step 2:** Build + suite green. Commit: `fix(taxi): monotonic-safe UtcNow for all taxi cooldown/grace timers`

### Task 5.4: SV-5 — settings snapshot per frame

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (`UpdatePosition`: two `SettingsManager.Current` reads inside `_stateLock` → one local snapshot taken ONCE at frame start), `MSFSBlindAssist/Services/DockingGuidanceManager.cs` (1 read/frame → snapshot), `MSFSBlindAssist/Services/GroundSpeedAnnouncer.cs`, `MSFSBlindAssist/Services/GroundTrafficMonitor.cs` (same treatment where reads are per-tick)

- [ ] **Step 1:** In each per-frame/per-tick method, hoist `var settings = SettingsManager.Current;` to the top and use the local. Do NOT cache across frames (live-apply from the settings dialog must keep working frame-to-frame — the snapshot is per-call only).
- [ ] **Step 2:** Build + suite green. Commit: `perf(services): one SettingsManager.Current acquisition per frame, not per use`

### Task 5.5: SV-8 + SV-6 — Reset outside lock + GSX tooltip change gate

**Files:**
- Modify: `MSFSBlindAssist/Settings/SettingsManager.cs` (`Reset()`: assign `_currentSettings` under `lock (_lock)`, call `Save(...)` AFTER releasing — mirror `Save`'s own comment about the invariant)
- Modify: `MSFSBlindAssist/Services/GsxService.cs` (the 1 s tooltip poll: skip the `File.ReadAllText`/parse when `File.GetLastWriteTimeUtc` AND length are unchanged since the previous tick; store the two prior values in fields; a missing file resets the memo)

- [ ] **Step 1:** Apply both. For SV-6: the skip must be provably equivalent — same-timestamp-and-length means the parse would produce the same string; dedup already happens post-parse today, so skipping earlier only saves work. State this in the report.
- [ ] **Step 2:** Build + suite green (SettingsSeedTests exercise SettingsManager statics). Commit: `perf(settings+gsx): Reset saves outside the static lock; tooltip poll skips unchanged file`

### Task 5.6: SV-7 — bless the rollout diagnostics (Robin's decision: KEEP)

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (~the RolloutDiag comment sites) and `TaxiGuidanceManager.Rollout.cs` (header comments)

- [ ] **Step 1:** Rewrite every comment that promises removal ("Removed after the root cause is identified", "entirely removed once we've identified the root cause of the RJAA bug") to state the instrumentation is PERMANENT diagnostic tooling for rollout issues (kept by owner decision 2026-07-07; rate-limited; async Log facade). No code changes.
- [ ] **Step 2:** Build + suite green. Commit: `docs(taxi): rollout diagnostics are permanent — comments stop promising removal`

### Task 5.7: PR-5 wrap (controller)

- [ ] Full build + suite; diff review; ASK ROBIN to push + open PR (suggested in-sim smoke: one full taxi with an incursion-warning scenario + hold-shorts, settings dialog OK mid-taxi, a GSX tooltip cycle).

---

# PR-6: `perf/navdb` (branch off main)

### Task 6.1: ND-1 — thread the connection through procedure-leg resolution

**Files:**
- Modify: `MSFSBlindAssist/Database/NavigationDatabaseProvider.cs` (`ResolveFixCoordinates`, `TryGetNavaidCoords`, `TryGetRunwayEndCoords`, `TryGetAirportCoords`, `ParseLegToWaypoint`, `GetWaypoint`, and the four leg-loop readers `GetApproachWaypoints`/`GetSIDWaypoints`/`GetSTARWaypoints`/`GetTransitionWaypoints`)

**Interfaces:**
- Produces: each helper gains a `SqliteConnection connection` first parameter (the already-open connection from the enclosing reader loop). Public API signatures of the provider itself must NOT change.

- [ ] **Step 1:** Read the call graph. Change the four leg-loop readers to pass their open connection down: `ParseLegToWaypoint(connection, ...)` → `ResolveFixCoordinates(connection, ...)` → `TryGet*Coords(connection, ...)`. Each helper creates a `SqliteCommand` on the passed connection instead of opening its own. `GetWaypoint`: add a connection-taking overload used internally; keep the existing public signature as a thin wrapper that opens one connection and delegates. Mirror the pattern already used at the "both on ONE connection" comment in this file and `LittleNavMapProvider.GetILSForRunway(connection, ...)`.
- [ ] **Step 2:** Also fix the ND-9 sub-item that this subsumes: the blank-fix-type "last resort" re-querying `vor`/`ndb` after the switch already tried them — restructure so each table is queried at most once per resolution (same results: the fallback order is unchanged, only duplicate queries removed).
- [ ] **Step 3:** Build + full suite green. Commit: `perf(navdb): one connection per procedure load instead of 1-5 opens per leg`

### Task 6.2: ND-5 — one GetLegs for the triplicated SQL

**Files:**
- Modify: `MSFSBlindAssist/Database/NavigationDatabaseProvider.cs` (`GetApproachWaypoints`/`GetSIDWaypoints`/`GetSTARWaypoints` — byte-identical except section/airway tagging; `GetTransitionWaypoints` differs deliberately by `is_missed` and stays separate)

- [ ] **Step 1:** Diff the three method bodies (scratch-extract + `diff`) to confirm identical-except-tagging; if drifted, STOP → escalate. Extract `private List<Waypoint> GetLegs(SqliteConnection connection, int approachId, FlightPlanSection section, string? airway)` holding the single SQL string; the three publics become one-line delegations. (Depends on Task 6.1's connection threading — do 6.1 first.)
- [ ] **Step 2:** Build + suite green. Commit: `refactor(navdb): single GetLegs SQL for approach/SID/STAR waypoint readers`

### Task 6.3: ND-6 — fold per-end ILS lookups into the runway query

**Files:**
- Modify: `MSFSBlindAssist/Database/LittleNavMapProvider.cs` (`GetRunways`' main query + `CreateRunwayFromReader`'s per-end `GetILSData` call; keep `GetILSForRunwayFallback` — the spatial fallback — untouched and still invoked for ends the JOIN leaves ILS-less)

- [ ] **Step 1:** Add two `LEFT JOIN ils` clauses (primary + secondary end) on `(ident, loc_airport_ident)` to the main runway query, aliasing every joined column explicitly (`p_ils_freq`, `s_ils_freq`, ... — the runway/runway_end ambiguity bug class in CLAUDE.md makes explicit aliasing mandatory). `CreateRunwayFromReader` consumes the joined columns; falls back to the spatial path only when the join produced NULLs.
- [ ] **Step 2:** Equivalence: the join must reproduce exactly what `GetILSData` returned (same table, same match keys — read `GetILSData` and mirror its WHERE semantics precisely, including any type/category filters). State the mapping in the report.
- [ ] **Step 3:** Build + suite green. Commit: `perf(navdb): LEFT JOIN ils into GetRunways; spatial fallback preserved`

### Task 6.4: ND-4 — expose the taxiway-node index

**Files:**
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs` (add `public IReadOnlyList<int> GetNodesOnTaxiway(string name)` backed by the private `_taxiwayNodeIndex`; empty list for unknown names)
- Modify: `MSFSBlindAssist/Navigation/TaxiRouter.cs` (the six full-graph `Adjacency`-scan sites: `AStarSearchStrict`'s `nodesOnTaxiway` rebuild, `FindNearestNodesOnTaxiway`, `FindExtremeNodeOnTaxiway`, `FindNearestNodeOnTaxiwayToTarget` (×2), `FindBestIntersection`, the gap-chain next-section scan)

- [ ] **Step 1:** Verify index equivalence FIRST: `_taxiwayNodeIndex` must contain exactly the nodes the scans find (both endpoints of every edge whose `TaxiwayName` matches, same name-comparison semantics — read `RegisterTaxiwayNode` and the scans' `.Equals(...)` comparison type). If the index's name normalization differs from any scan site's, STOP → escalate.
- [ ] **Step 2:** Per site: replace the scan ONLY where the consumption is order-independent (min-distance selection, set membership, candidate accumulation into a further-sorted structure). If any site's result depends on Adjacency iteration ORDER (first-match-wins without a comparison), leave that site on the old scan and flag it in the report.
- [ ] **Step 3:** Build + suite green. Commit: `perf(taxi-routing): dictionary-backed taxiway-node lookup replaces full-graph scans`

### Task 6.5: ND-7 + ND-8 + ND-9 remainder — branding, ring scan, minors

**Files:**
- Modify: `MSFSBlindAssist/Database/NavdataReaderBuilder.cs` (user-facing "FBWBA" strings in the locked-DB dialog → "MSFS Blind Assist"; the comment nearby; dispose the `Process[]` from `GetProcessesByName`)
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs` (`DescribeLocation` Pass 2: collect candidate node IDs from the same spatial-hash ring Pass 1 uses, scan only their adjacency lists; `RegisterTaxiwayNode`: add a `HashSet<int>` sidecar to kill the O(n) `List.Contains` during Build)
- Modify: `MSFSBlindAssist/Navigation/TaxiRouter.cs` (delete the stale duplicate `<summary>` on `FindExtremeNodeOnTaxiway`; fix the `bridgeCount` log line to count bridges actually on the final path OR rename the log wording to "bridge-edge relaxations" — pick the honest one-line fix)
- Modify: `MSFSBlindAssist/Navigation/NavigationCalculator.cs` (remove the two unused usings)

- [ ] **Step 1:** Apply all; each is mechanical. The Pass-2 ring change must produce the identical candidate superset within the 120 m radius (the ring covers ≥ the radius — verify the ring dimensions vs 120 m before assuming; if the ring is smaller, widen the ring query, never shrink results). **Step 2:** Build + suite green. Commit: `chore(navdb+taxi): branding, ring-scoped DescribeLocation, build-time HashSet, log accuracy, unused usings`

### Task 6.6: PR-6 wrap (controller)

- [ ] Full build + suite; diff review; ASK ROBIN to push + open PR (suggested in-sim smoke: EFB Shift+E procedure loading at a big airport — SID+STAR+approach+transitions — plus one taxi route calc with a multi-taxiway clearance, Where-Am-I, and an Airport Lookup runway with ILS).

---

# PR-7: `chore/aircraft-forms-safe` (branch off main)

### Task 7.1: AC-1 + AC-3 — the two one-line aircraft fixes

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` (`public new string CurrentFlightPhase => currentFlightPhase;` → `public override string? CurrentFlightPhase => currentFlightPhase;` — first verify the base member is `virtual string?` in `BaseAircraftDefinition`; adjust nullability to match exactly)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.UiVariableSet.cs` (`simConnect.ExecuteCalculatorCode($"{value} (>L:{knob})");` → `simConnect.ExecuteCalculatorCode($"{value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} (>L:{knob})");` for the `PRESS_MAN_ALT_SET`/`PRESS_MAN_VS_SET` branch)

- [ ] **Step 1:** Apply both. **Step 2:** Build + suite green. NOTE for PR body: AC-1 RESTORES a documented feature (the "MSFS BA - <phase>" window title on the FBW A320 starts updating again — it was dead via interface dispatch); Robin should see phase titles reappear in-sim. Commit: `fix(aircraft): A320 flight-phase override reaches interface dispatch; A380 pressurization RPN uses invariant formatting`

### Task 7.2: FM-1 — hotkey ID dedup

**Files:**
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` (`HOTKEY_HAND_FLY_MODE = 9075` and `HOTKEY_TRACK_FIX = 9076` — renumber BOTH to IDs unused anywhere in the file; keep `HOTKEY_FCU_SET_AUTOPILOT = 9075` and `HOTKEY_VISUAL_GUIDANCE = 9076` as-is so the older assignments keep their values)

- [ ] **Step 1:** Extract every `= 9\d+` constant in the file, pick two unused IDs (e.g. next free in the 9xxx range), renumber the two later constants. Verify uniqueness: `grep -oE "= 9[0-9]+" MSFSBlindAssist/Hotkeys/HotkeyManager.cs | sort | uniq -d` → empty.
- [ ] **Step 2:** Build + suite green. Commit: `fix(hotkeys): dedupe Win32 hotkey IDs 9075/9076`

### Task 7.3: FM-2 + FM-8 + FM-9 + FM-11 — thread-marshal hygiene

**Files:**
- Modify: `MSFSBlindAssist/Forms/ValueInputForm.cs` (the `Task.Delay(1200).ContinueWith` toggle refresh: wrap the `IsDisposed`/`IsHandleCreated` check + `Invoke` in `try { } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }` — mirror `HS787AutopilotWindow`'s corrected pattern)
- Modify: `MSFSBlindAssist/Forms/LocationInfoForm.cs` (`SafeInvoke`: blocking `Invoke` → `BeginInvoke` inside the existing catches)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (`CheckWeatherProximityAsync`: drop the redundant self-`Invoke` post-await — the WinForms sync context resumes on the UI thread — and add a `private bool _proximityCheckRunning` reentrancy latch set/cleared in try/finally around the async body; the timer-tick caller skips when latched)
- Modify: `MSFSBlindAssist/MainForm.MenuHandlers.cs` (the post-await `BeginInvoke(...)` redundant marshal → direct call, keeping the `IsHandleCreated && !IsDisposed` guard)

- [ ] **Step 1:** Apply all four (read each site fully first — the "redundant" claims must be re-verified: confirm each await genuinely resumes on the UI thread, i.e. no `ConfigureAwait(false)` upstream in the same method).
- [ ] **Step 2:** Build + suite green. Commit: `fix(forms): safe marshal idioms — try/catch races, BeginInvoke, reentrancy latch, drop redundant marshals`

### Task 7.4: FM-10 + JS-11 — path hygiene

**Files:**
- Modify: `MSFSBlindAssist/Database/DatabasePathResolver.cs` (add `public static string GetCanonicalDatabasesFolder()` returning the canonical databases DIRECTORY — derive from the existing `GetCanonicalDatabasePath` logic, no duplicated literals)
- Modify: `MSFSBlindAssist/Forms/DatabaseSettingsForm.cs` (the display-only inline `Path.Combine(AppData, CanonicalFolderName, "databases")` → the new helper)
- Modify: `tools/mcdu_run.ps1`, `tools/mcdu_tour.ps1`, `tools/mfd_import_and_scrape.ps1` (default `$AgentFile` hardcodes `C:/Users/franc/...` → repo-relative default derived from `$PSScriptRoot`)

- [ ] **Step 1:** Apply; the PS1 default must resolve to the same agent file path relative to the repo layout (read one script's usage to get the right relative target). **Step 2:** Build + suite green. Commit: `chore(paths): canonical databases-folder helper; repo-relative tool script defaults`

### Task 7.5: AC-9 — base-class GetVariables caching

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` (make `GetVariables()` a non-virtual cached method: `public Dictionary<string, SimVarDefinition> GetVariables() => _cachedVariables ??= BuildVariables();` + `protected abstract Dictionary<string, SimVarDefinition> BuildVariables();` — read `IAircraftDefinition` first: `GetVariables` stays the interface member)
- Modify: all six definitions (`FlyByWireA320Definition`, `FenixA320Definition`, `PMDG777Definition`, `PMDG737Definition`, `HorizonSim787Definition`, `FlyByWireA380Definition`): rename each `GetVariables` override to `BuildVariables` (protected), DELETE each local `_cachedVariables`/`_varCache` caching wrapper so the base cache is the only cache.

- [ ] **Step 1:** Read each def's current caching shape first (the A380's is named `_varCache` and its guard may sit elsewhere — unwind carefully). The transform per def: the method that BUILDS the dictionary becomes `BuildVariables`; every internal self-call to `GetVariables()` keeps working via the base cache.
- [ ] **Step 2:** Grep for any external caller that relied on re-building (`GetVariables(force...)`? — there is none per the audit, but verify).
- [ ] **Step 3:** Build + full suite green. Commit: `refactor(aircraft): single base-class GetVariables cache + abstract BuildVariables`

### Task 7.6: AC-10/11/12 — byte-identical helper consolidation

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` (receives the shared helpers), `FlyByWireA320Definition.cs`, `FlyByWireA380Definition.SimVarUpdate.cs`, `FlyByWireA380Definition.HotkeysAndMotion.cs`, `PMDG737Definition.cs`, `PMDG777Definition.cs`, `FenixA320Definition.cs`

**Scope — ONLY the audited byte-identical pairs** (each MUST be diff-verified identical before moving; if drifted, leave both copies and flag):
- FBW A320 ↔ A380: `DecodeArmedModes`, `CgMacPhrase`, `SetAltIncrement`
- PMDG 737 ↔ 777: `FormatEtaFromDistance`, `AnnounceDestFromSDK`, `AnnounceTODFromSDK`, `BuildSuppressedButtonKeys`
- Fenix ↔ FBW A320: `RequestFuelQuantity` (identical except log category — parameterize the category)
- A320-internal: consolidate the six `RequestSpeedGD/S/F/VFE/VLS/VS` bodies into one private table-driven helper WITHIN FlyByWireA320Definition (do NOT switch them to `SimConnectManager.RequestSingleValue` — that swap is the RISKY half, deferred)

- [ ] **Step 1:** For each pair: scratch-extract both bodies, `diff` → byte-identical (modulo whitespace/comments) or leave + flag. Move identical ones to `BaseAircraftDefinition` as `protected` members (static where they don't touch instance state); delete the copies; callers unchanged.
- [ ] **Step 2:** Where the helper needs per-aircraft data (log category, var keys), take it as a parameter — no virtual hooks, no behavior knobs.
- [ ] **Step 3:** Build + full suite green after EACH moved helper (cheap; catches signature slips early). Commit: `refactor(aircraft): consolidate byte-identical helpers into BaseAircraftDefinition`

### Task 7.7: AC-15 (scoped) + AC-16 leftovers — micro-efficiency + naming

**Files:**
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs` + consumers (the five `*DisabledMonitorVariables` `List<string>` per-event `.Contains` scans → cache a `HashSet<string>` snapshot per list, invalidated on settings save — find the save/apply path and rebuild there; the settings JSON shape must NOT change, the HashSet is a runtime sidecar)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.SimVarUpdate.cs` (ROW/ROP per-event tuple array → `private static readonly`; `DecodeArmedModes` caller's join→re-Split round-trip → return the parts directly; `StartsWith` without `StringComparison.Ordinal` on the per-event path → add it)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs` (hoist the per-frame `G_FORCE` branch to the TOP of the `ProcessSimVarUpdate` ladder — a pure reorder; verify no earlier branch could also match "G_FORCE" before moving; per-call `string[] detents` → static readonly; double ContainsKey+indexer lookups → TryGetValue)
- Modify: `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.HotkeysAndMotion.cs` (per-call detents array → static readonly)
- Modify: `MSFSBlindAssist/Aircraft/HorizonSim787Definition.SimVarUpdate.cs` (rename misleading `_previousFuelXfeedFwd` to match what it actually tracks — read the usage and pick the accurate name)
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (the ~500 inline `{[0]="Off",[1]="On"}` dictionaries → one `private static readonly Dictionary<int,string> OffOn` (+ a percent table) IF mechanical: only replace instances that are exactly identical; any variant wording stays inline)

- [ ] **Step 1:** Apply file-by-file, building between files. The `_doorDefs` linear scans and PMDG stale section numbering are explicitly SKIPPED (churn > value; note in PR body). **Step 2:** Full suite green. Commit: `perf(aircraft): hoist per-frame branches, static tables, HashSet monitor-var lookups`

### Task 7.8: PR-7 wrap (controller)

- [ ] Full build + suite; diff review; ASK ROBIN to push + open PR (suggested in-sim smoke: FBW A320 — window title shows flight phases again, G_FORCE-dependent hotkey readouts, FCU sets; A380 pressurization knob set on a comma-decimal locale machine if available; hand-fly + track-fix hotkeys both register; Fenix panel spot-check; Ctrl+M monitor-manager toggles still suppress).

---

## Execution notes

- Serial execution, one PR at a time, merge before starting the next branch (all branch off current `main`).
- Every implementer report must include the equivalence argument for its change (why outputs are identical), not just green builds.
- PR bodies must carry: per-task summary, the two deliberate behavior-restorations (AC-1 phase titles, SC-9 777 arrows), and the deferred/skipped items with reasons.

## Self-review record

- Spec coverage: PR-4 = SC-1,2,3(safe),7,9(safe),10,11,13,14 ✓; PR-5 = SV-1,2,3,4(taxi),5,6,7,8 ✓ (SV-10 skipped, documented); PR-6 = ND-1,4,5,6,7,8,9 ✓; PR-7 = AC-1,3,9,10/11/12(identical-only),15(scoped),16-leftovers, FM-1,2,8,9,10,11, JS-11 ✓. Deviations (AC-15 switch-conversion scope-down, SV-10 skip, `_doorDefs`/section-numbering skip) documented in Global Constraints + Task 7.7.
- Placeholders: none — every step names symbols, files, and the exact transform; code blocks where the shape is load-bearing.
- Type consistency: `GetNodesOnTaxiway` (6.4) consumed only within 6.4; `BuildVariables` (7.5) referenced only in 7.5; `ProcessEcamLine`/`DecodeCellSymbol` signatures defined where used.
