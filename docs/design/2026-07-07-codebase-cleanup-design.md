# Codebase Cleanup Sweep — Design (2026-07-07)

## Context

A seven-domain parallel audit of the codebase (SimConnect core + Utils, aircraft
definitions, services/guidance, Forms/UI, Navigation/Database, JS Coherent agents,
docs accuracy) was run on 2026-07-07, focused on two lenses chosen by Robin:

1. **Consistency & pattern drift** — code violating the project's own established
   patterns, or the same problem solved with divergent copies.
2. **Efficiency / hot-path performance** — per-frame allocations, repeated work,
   blocking I/O, lock contention.

Out of scope this round (explicitly): correctness bug-hunting as a primary lens,
structural decomposition of god files, `MSFSBlindAssistUpdater`, `tools/`.

This sweep follows the early-July 2026 cleanup (logging facade, dead-code pass,
`tests/MSFSBlindAssist.Tests` + CI, unified settings dialog) and targets what that
pass did not cover.

**Baseline finding: the documented invariants overwhelmingly hold.** No logging-facade
bypasses, no DatabasePathResolver bypasses (one display-only re-encode), no raw
TreeViews, `_uiSetEcho` intact, docking/taxi safety invariants verified, the two METAR
decoder copies have NOT drifted. The findings below are the exceptions.

## Decisions made during design (Robin, 2026-07-07)

- **Risk posture:** two buckets. SAFE (provably behavior-neutral) ships first;
  RISKY (behavior-affecting) gets per-PR in-sim/NVDA test plans Robin runs before merge.
- **Tests:** written as part of the plan, as a safety net BEFORE touching the code
  they cover.
- **PR slicing:** themed PRs, 9 total, 3 phases. All work on branches → PRs (main
  is protected).
- **Rollout diagnostics** (TaxiGuidanceManager RolloutDiag): KEEP; update the
  comments that promise removal.
- **PMDG 737/777 readout wording drift:** HARMONIZE (risky bucket; Robin verifies
  by ear).
- **HS787 transponder Ident announce:** INTENTIONAL; keep, add a code comment
  marking it deliberate.
- **TaxiAssistForm validation errors:** normalize Calculate-path validation errors
  to `AnnounceImmediate` (risky; NVDA spot-check).

## Findings register

IDs are used by the implementation plan. Severity within each domain is descending.
Bucket: SAFE = provably behavior-neutral; RISKY = needs in-sim/NVDA verification.

### SC — SimConnect core + Utils

| ID | Finding | Bucket |
|----|---------|--------|
| SC-1 | Per-frame individual-var dispatch (G_FORCE at SIM_FRAME rate): `variableDataDefinitions.FirstOrDefault(x => x.Value == requestId)` O(n) scan + closure per event, `ContainsKey`+indexer double lookup, value-capturing `AddOrUpdate` closure, and an unconditional `Log.Debug` interpolated line per fired update (`SimConnectManager.VarCache.cs:22,119,151`). Fix: reverse `ConcurrentDictionary<int,string>` requestId→varKey; `TryGetValue`; gate the per-fire log for `HighFrequency` vars. | SAFE |
| SC-2 | PMDG change detection boxes the full multi-KB CDA struct once per field, twice, per 1 Hz diff (`PMDG777DataManager.cs:239-255`, `PMDGNG3DataManager.cs:409-439`). Fix: box each snapshot once, pass the boxes to `FieldInfo.GetValue`. | SAFE |
| SC-3 | Every MobiFlight LVar/LED event does a LINQ scan over the full aircraft variable dictionary (`SimConnectManager.cs:799-801,828-830`); the default channel re-fires all 64 slots unfiltered with a `Log.Debug` per lvar (`MobiFlightWasmModule.cs:515-543`, subscribed `FLAG.DEFAULT` at :232). Fix: cached ledVar→def dictionary per aircraft + drop the per-lvar log (SAFE); change-filtering before raising is RISKY (consumers may rely on re-fires) — defer. | SAFE / RISKY split |
| SC-4 | Dead subsystem: `EFBBridgeServer` (871 lines, never constructed), `HS787SimBriefForm` (field declared, disposed, never assigned), stale comment in `CoherentDebuggerClient.cs:9-11` claiming HS787 still uses it. Live members to preserve: `EFBStateUpdateEventArgs`, `IMcduBridge`. | SAFE |
| SC-5 | Dead struct: 1000-field `GenericBatch` unused (only `GenericBatch1`–`5` referenced); `docs/variable-system.md` still documents the retired single-batch design. | SAFE |
| SC-6 | Dead API cluster: `RequestLVarValue` (no-op body), `RequestSpecificLVar` (zero callers, hash-collision request-ID scheme), `GetAircraftPosition` duplicate (`Monitoring.cs:385-402`), `pendingRequests` dictionary. EXCEPTION: the 400-499 dispatch branch (`Dispatch.cs:53-108`) has `ecamMasterWarning/-Caution/-Stall` side-writes — verify the A32NX ECAM window in-sim before removing (RISKY). | SAFE except 400-499 branch |
| SC-7 | 50-line ECAM collection block duplicated verbatim between individual-response and batch paths (`VarCache.cs:44-104` vs `:399-453`). Fix: extract one `ProcessEcamLine` helper. | SAFE |
| SC-8 | Dead condition in batch fire-gate: `!lastVariableValues.ContainsKey(varKey)` always false (`VarCache.cs:476`; :473 writes the key first). | SAFE |
| SC-9 | PMDG 777 vs NG3 manager duplication with drift: NG3's `DecodeCellSymbol` maps 0xA3/0xA4 arrows; the 777's two inline copies render them as spaces (777:610-616, 644-647). Port the arrow decode + factor the 777's own two copies (SAFE); full shared-base refactor deferred (RISKY). | SAFE (port) |
| SC-10 | `GetFieldValue` re-runs `typeof(...).GetField(name)` reflection per call in both PMDG managers. Fix: static `Dictionary<string,FieldInfo>` from `s_dataFields`; box snapshot once. | SAFE |
| SC-11 | Each batch delivery iterates the entire `continuousVariableIndexMap` (all 5 batches) and re-resolves every varDef (`VarCache.cs:366-391`); LINQ `Any`+closure per changed detent value (:485). Fix: per-batch prebuilt `(key,index,def)` arrays at `StartContinuousMonitoring`. | SAFE |
| SC-12 | 13 near-identical hotkey readout methods each clear + re-register a static data def with a 50 ms `DoEvents` pump per press (`DataRequests.cs:187-527`, `Setup.cs:537-585`). Fix: register fixed defs once; table-driven helper. RISKY: the clear-first pattern was a crash-avoidance measure — needs in-sim regression on hotkey readouts + aircraft switch. | RISKY |
| SC-13 | `RegisterClientEvents` records the event ID before `MapClientEventToSimEvent` succeeds (`Setup.cs:765-778`) — failed mapping leaves a stale entry that later transmits on an unmapped ID. Fix: move insert inside the try, after the map. | SAFE |
| SC-14 | Minor: `MobiFlightWasmModule` has `Dispose()` without `: IDisposable`; `SimulatorDetector` never disposes `Process[]` (leak per 5 s reconnect attempt; same pattern `NavdataReaderBuilder.cs:478`); `SimVarMonitor.ProcessUpdate` ContainsKey+double indexer; `SendMFCommand` logs per command (per-frame for seat motors) — add quiet overload for high-rate callers. | SAFE |

### AC — Aircraft definitions

| ID | Finding | Bucket |
|----|---------|--------|
| AC-1 | `CurrentFlightPhase` declared `public new` instead of `override` (`FlyByWireA320Definition.cs:31`) — interface dispatch sees the base's `null`; the "MSFS BA - X phase" window title never fires. Fix: `override`. Verified in source. | SAFE (restores documented behavior; note in PR test plan) |
| AC-2 | PMDG 777 switch dispatch lacks the 737's `IsReady` gate (`PMDG777Definition.cs:5837-6098` vs `PMDG737Definition.cs:4224-4777`) — pre-snapshot `GetFieldValue` returns the 0.0 sentinel; set-to-0 silently swallowed. Fix: mirror the 737's "Switch not ready" pattern. | RISKY |
| AC-3 | Raw culture-sensitive `$"{value} (>L:{knob})"` RPN for `PRESS_MAN_ALT_SET`/`PRESS_MAN_VS_SET` (`FlyByWireA380Definition.UiVariableSet.cs:300`) — violates the invariant-formatting rule the same file fixes 10 lines up. Fix: `value.ToString("0.###", CultureInfo.InvariantCulture)`. Verified in source. | SAFE |
| AC-4 | `AUTOBRAKE_MODE` combo announces its own selection (`FlyByWireA320Definition.cs:8011`) — combo echo the rules forbid; can triple-announce with the separately-keyed `A32NX_AUTOBRAKES_ARMED_MODE` monitor; `ValueDescriptions[value]` throws on unmapped value. | RISKY |
| AC-5 | `AnnounceImmediate` for ECP LED state changes (`FlyByWireA320Definition.cs:7509`) — state changes should queue. | RISKY (low) |
| AC-6 | HS787 `HS787_APMaster`/`HS787_ATStatus` fire toggle events unconditionally from state-target combos (`HorizonSim787Definition.UiAndHotkeys.cs:31-35,65-69`) — re-selecting current value inverts the system; siblings are state-aware. | RISKY |
| AC-7 | A380 RMP press/release H-events lack the mandated `{seq} 0 *` uniquifier (`FlyByWireA380Definition.Rmp.cs:62,70`) vs compliant `SendRmpKey` (:28). Safe today only because press/release alternate. | RISKY (low) |
| AC-8 | Fenix "Generator 1" and "Emergency Gen 1 Line" combos alias the same LVar `S_OH_ELEC_GEN1` (`FenixA320Definition.cs:1585-1592` vs :1683-1690); comment says deliberate but they are different physical switches. Needs live check per troubleshooting playbook before touching. | RISKY (investigate only) |
| AC-9 | `GetVariables()` caching boilerplate re-implemented in all six defs (naming drift: `_cachedVariables` vs `_varCache`). Fix: base-class cached `GetVariables()` + abstract `BuildVariables()`. | SAFE |
| AC-10 | FBW A320↔A380 duplicated helpers: byte-identical `DecodeArmedModes`, `CgMacPhrase`, `SetAltIncrement`; same-scaffold FCU setters, `RequestAutopilotStates`, TCAS RA timer scaffolding, doors block (A380 already drifted), preset narration, flaps/gear readout, `WeightUser`. Consolidate byte-identical ones (SAFE); parameterized FCU merge is RISKY. `FireFCUButton`/`BaroPhrase` genuinely diverge — rename to prevent copy-paste accidents. | SAFE (byte-identical) / deferred (rest) |
| AC-11 | PMDG 737↔777 duplicated helpers: `FormatEtaFromDistance`, `AnnounceDestFromSDK`, `AnnounceTODFromSDK`, `BuildSuppressedButtonKeys`, altimeter formatting (inlined 4×), squawk BCD, COM handlers, four dialogs. Divergences: COM baseline sentinel idiom, 737's `TryParseSpeedInput` (M-prefix) never ported to 777, readout wording drift (harmonize — Robin's decision), `PMDGEnhancedDistanceMode` honored only by the 777. | SAFE (identical) / RISKY (wording + TryParseSpeedInput port) |
| AC-12 | Fenix↔A320 `RequestFuelQuantity` byte-identical; 5 Fenix + 6 A320 hand-rolled single-value request bodies duplicate `RequestSingleValue` boilerplate and use raw `ClearDataDefinition` instead of `SafelyClearDataDefinition`. Consolidating the A320 six-pack is SAFE; switching to the shared helper is RISKY (datum/clear subtleties). | split |
| AC-13 | Tracked-window helper adoption incomplete: Fenix's six `ShowFenix*Window` methods construct a new form per press, no reuse guard, no swap-time disposal (windows survive aircraft swap holding the discarded def); HS787's own windows hand-roll the pattern the base extracted from them. A base `virtual OnAircraftSwappedOut()` would close the leak class. | RISKY (deferred — lifecycle) |
| AC-14 | HS787 ~150 copy-pasted "previous-value + transition announce" blocks; flaps detent table duplicated; inHg→hPa implemented 3× with 3 output shapes. Fenix: 5 copy-pasted FCU pending pairs; `Increment/DecrementCounter` copy `JumpCounter(±1)`. A320: 6 FCU pending pairs; LAND-capability ARINC decode copy-pasted 3× with drifted speech ("LAND3 dual" vs "LAND 3 dual"). Table-driven consolidation transcribing phrases verbatim. | Deferred (large); LAND-speech drift fix in wording-harmonization PR |
| AC-15 | Efficiency micro: `*DisabledMonitorVariables` are `List<string>` scanned per event (`UserSettings.cs:184-217`; ~20 call sites) → cache HashSet snapshot; 777/A320/HS787 sequential if-chains vs 737's `switch` (G_FORCE falls through the A320's full ladder per frame); A380 ROW/ROP per-event tuple-array alloc (`SimVarUpdate.cs:416-424`); `DecodeArmedModes` join→Split round-trip; `Thread.Sleep(100/50)` on the UI thread in A320 COM/FCU-alt set paths (:7692, :8345 — RISKY, write ordering); `Math.Pow` in `UnpackSixBit`; `StartsWith` without Ordinal; per-call detents arrays; double dictionary lookups; `_doorDefs` linear scans. | SAFE except Thread.Sleep |
| AC-16 | Dead/cosmetic: HS787 seven unreachable duplicate switch cases + dead `HS787_Battery` map entry + misleading `_previousFuelXfeedFwd` name; A380 dead `bool button` param; PMDG stale section numbering; Fenix mixed field naming + ~500 inline `{Off,On}` dictionaries → shared static tables. | SAFE |
| AC-17 | Out-of-scope observation (no verdict per playbook): HS787 `Dialogs.cs:339/357` sends `KOHLSMAN_SET` as inHg×16 where the stock event is documented as millibars×16 — needs a live check. | Investigate |

### SV — Services / guidance

| ID | Finding | Bucket |
|----|---------|--------|
| SV-1 | `CheckRunwayIncursion` at ~30 Hz inside `_stateLock`: fresh HashSet of on-route hold-short nodes + linear scan of ALL graph nodes per frame (`TaxiGuidanceManager.cs:2040-2111`); early-out only during post-warning cooldown. Fix: cache HS/IHS node list per graph (immutable after Build) + reference-keyed on-route set (mirrors `RoutePoints()` idiom). Cache must invalidate on recalc/advance. | SAFE (verify invalidation) |
| SV-2 | `TryGetRunwayLineupReference` (public) reads `_state`/lineup fields with no `_stateLock` (`TaxiGuidanceManager.cs:958-986`) — violates the documented lock rule; all sibling accessors comply. Fix: wrap in lock. | SAFE |
| SV-3 | `ClearWhereAmICache` writes the cache pair lock-free while its twin `OnAirportDataUpdated` documents and takes the lock (:925-929 vs :939-950); the "unlocked by design" doc rationale is stale. Fix: take the lock + update docs/taxi-guidance.md §Concurrency. | SAFE |
| SV-4 | Mixed `DateTime.Now`/`UtcNow` time bases in per-frame guidance timing (taxi cooldowns, off-route persistence, recalc/speed/incursion cooldowns on wall clock; VisualGuidanceManager derives PD `deltaTime` from `DateTime.Now` — DST/clock-sync hazard + per-frame timezone conversion). Fix: standardize on UtcNow. Taxi cooldowns SAFE; VG `deltaTime` swap RISKY (touches guidance math input — verify an approach in-sim). | split |
| SV-5 | `SettingsManager.Current` (static lock) read 2×/frame inside `_stateLock` in taxi `UpdatePosition` (:1197-1198), 1×/frame in docking, plus GroundSpeed/Traffic — an options-dialog Save mid-taxi stalls the position frame behind the JSON serialize. Fix: snapshot once per frame or cache fields refreshed on settings-saved. | SAFE |
| SV-6 | GSX tooltip poll re-reads + re-parses `status.html` every 1 s tick on the UI thread regardless of change (`GsxService.cs:1107-1194`). Fix: skip on unchanged LastWriteTimeUtc/length. | SAFE |
| SV-7 | Rollout diagnostics comments promise removal ("removed after root cause identified") but ship on main. DECISION: keep, rewrite comments. Also stale `Debug.WriteLine` mention in `GsxGateSelector.cs:149` doc comment. | SAFE |
| SV-8 | `SettingsManager.Reset()` calls `Save` inside `lock (_lock)` (`SettingsManager.cs:229-237`) — the file write executes under the static lock, violating the invariant the comment at :176-186 protects. Fix: assign under lock, Save after release. | SAFE |
| SV-9 | Doc staleness: taxi-guidance.md attributes ground-traffic distance to `DistanceFormatter.FromFeet`; code correctly uses private `FormatDistance` on the independent toggle. One-line doc fix. | SAFE (docs) |
| SV-10 | Micro: LINQ allocs on the 3 s traffic tick (`GroundTrafficMonitor.cs:178-220`). Optional. | SAFE |

### FM — Forms / MainForm / Hotkeys / Settings

| ID | Finding | Bucket |
|----|---------|--------|
| FM-1 | Duplicate Win32 hotkey IDs: 9075 (`HOTKEY_FCU_SET_AUTOPILOT` + `HOTKEY_HAND_FLY_MODE`), 9076 (`HOTKEY_VISUAL_GUIDANCE` + `HOTKEY_TRACK_FIX`) (`HotkeyManager.cs:75,94,95,109`). Masked by mode exclusivity today. Fix: renumber to unused IDs (all other 60+ verified unique). Verified in source. | SAFE |
| FM-2 | `ValueInputForm` toggle refresh uses the known-bad TOCTOU marshal idiom — `ContinueWith` on threadpool, `IsHandleCreated` check, no try/catch (`ValueInputForm.cs:146-149`); `HS787AutopilotWindow.cs:222-234` has the corrected pattern. | SAFE |
| FM-3 | `SettingsManager.Reset()` — see SV-8 (same item, discovered by both agents). | SAFE |
| FM-4 | `DestinationRunwayForm`/`RunwayTeleportForm` are ~290-line copy-paste twins (entire ICAO→runway-list pipeline byte-identical except button text + accept action). Fix: shared picker base preserving accessible strings byte-for-byte. | SAFE (strings verbatim) |
| FM-5 | TaxiAssistForm validation errors split between `AnnounceImmediate` (3 sites) and queued `Announce` (10 sites) for the same class of message. DECISION: normalize to immediate; NVDA spot-check. | RISKY |
| FM-6 | Sync SQLite on the UI thread in `TextChanged` (4th ICAO char) — EFB tabs, runway/gate pickers. Once-per-ICAO, correct gate; accept unless stutter reported. NOT planned. | Deferred |
| FM-7 | Dead: empty `WaveTypeCombo_SelectedIndexChanged` (`HandFlyPanel.cs:575-577` + wiring :151). | SAFE |
| FM-8 | `LocationInfoForm.SafeInvoke` uses blocking `Invoke` (correct catches) instead of the standard `SafeBeginInvoke` idiom (:157-184). | SAFE |
| FM-9 | Weather proximity checker: redundant self-`Invoke` post-await on the UI thread + no reentrancy latch on the fire-and-forget tick (`MainForm.Announcers.cs:1871-2015`). | SAFE |
| FM-10 | `DatabaseSettingsForm.cs:56` re-encodes the `"databases"` path segment inline (display-only). Fix: expose canonical databases folder from `DatabasePathResolver`. | SAFE |
| FM-11 | `MainForm.MenuHandlers.cs:74-75` redundant bare marshal post-await that mimics the known-bad idiom. | SAFE |

### ND — Navigation / Database

| ID | Finding | Bucket |
|----|---------|--------|
| ND-1 | N+1 + connection-per-query in procedure-leg coordinate resolution: every leg opens 1-5 fresh non-pooled SQLite connections (`NavigationDatabaseProvider.cs:853-950` via `ParseLegToWaypoint` from the four leg-loop readers; `GetWaypoint` too) — ~50-150 real file opens per SID+STAR+approach load. Fix: thread the already-open connection through (pattern exists at :263 and in LittleNavMapProvider). | SAFE |
| ND-2 | `GetLandingExits` fallback pass (`TaxiGraph.cs:2085-2191`) drifted from the main loop (:1754-1928): missing the equal-angle lateral-side tiebreak, still 5° (not 20°) apron-override threshold, missing the `apronAngle > currentAngleFwd` guard CLAUDE.md says is required together with the other. Fix: extract shared helper. Fallback's own comment claims "Same targeted apron-bearing override" while no longer being the same. | RISKY (rollout exit bearings; in-sim verify) |
| ND-3 | `AirportDatabase.cs` (~570 lines) dead: zero external references, legacy self-created `airports.db` path, only remaining `SELECT *`/`SELECT r.*` of the warned bug class. Delete. | SAFE |
| ND-4 | TaxiRouter re-derives "nodes on taxiway X" by full-graph scans in six sites (O(N×V·E) per constrained route) while `TaxiGraph._taxiwayNodeIndex` already holds the index privately. Fix: expose `GetNodesOnTaxiway(name)`; verify route outputs unchanged (candidate enumeration order). One-shot path, so polish. | SAFE-leaning (verify) |
| ND-5 | Triplicated procedure-leg SQL (`GetApproachWaypoints`/`GetSIDWaypoints`/`GetSTARWaypoints` byte-identical except tagging; :602-725). Fix: one private `GetLegs(...)`. | SAFE |
| ND-6 | `GetRunways` bounded N+1: per-end ILS query + 2-query fallback (`LittleNavMapProvider.cs:137-142`, :649, :668). Fix: LEFT JOIN both ends; keep spatial fallback. Low urgency. | SAFE |
| ND-7 | User-facing "FBWBA" branding in the locked-DB dialog (`NavdataReaderBuilder.cs:97-98` + comment :93). | SAFE |
| ND-8 | `DescribeLocation` Pass 2 scans the whole adjacency map despite the spatial hash (`TaxiGraph.cs:807-855`). Hotkey-only today. | SAFE (low) |
| ND-9 | Minor: stale duplicate `<summary>` on `FindExtremeNodeOnTaxiway`; `bridgeCount` counts relaxations not bridges (log accuracy); `RegisterTaxiwayNode` O(n²) `List.Contains` during Build → HashSet sidecar; blank-fix-type re-queries vor/ndb twice (subsumed by ND-1); `Process[]` not disposed (`NavdataReaderBuilder.cs:478`); unused usings in `NavigationCalculator.cs`. | SAFE |

### JS — Coherent agents (Resources/*.js)

| ID | Finding | Bucket |
|----|---------|--------|
| JS-1 | A380 `flightInfo()` still checks `ident` before `mcduIdent` (`coherent-a380-agent.js:1993`) — the exact T/D-vs-step-descent bug the A32NX agent fixed with a comment (`coherent-a32nx-flightinfo.js:50`). Fix: mirror the order. | RISKY (live A380 flight w/ step alt) |
| JS-2 | flyPad `setValue` actuates without a `disabledFor` guard (toggle branch + click fallback, `coherent-flypad-agent.js:2376-2418`) contradicting the agent's own rule and `clickElement`'s behavior. | RISKY |
| JS-3 | flyPad full-tree O(n²) scrape per 600 ms poll: per-element `isVisible` (style+rect), `containsInteractive` re-classifies whole subtrees, pass-2 re-classify, 8-ancestor walks per text leaf; no unchanged-page short-circuit (C# hash runs after the scrape). Fix: per-scrape node memoization + gating. | RISKY (live NVDA verify) |
| JS-4 | PMDG EFB `collect()` two full-tree traversals + layout reads per 600 ms with no dirty gate (`coherent-pmdg-efb-agent.js:385-395`). Fix: MutationObserver flag or cheap fingerprint → "unchanged" token. Must not miss value-only mutations. | RISKY |
| JS-5 | A380 `comboSelectedValue` two full-subtree scans with rect reads per dropdown per 350 ms (:1079-1110); ARRIVAL page = 10+ tree scans/poll. Fix: shared (text,rect) leaf index per `enumerateLines` pass. | RISKY |
| JS-6 | Dead file: `pmdg-efb-accessibility-bridge.js` (2,372 lines, retired HTTP bridge, not in csproj copy list, `EFBModPackageManager.Install` has zero callers). Delete file + dead Install/Update halves; keep `BridgeJsFileName` + `Remove`/`IsInstalled` for `LegacyEfbBridgeCleanup`. | SAFE |
| JS-7 | flyPad `labelFor` local `var lower` shadows the module `lower()` helper — documented landmine with an in-code warning comment (:444, :397-400). Fix: rename to `baseLower`. jsdom tests cover labelFor. | SAFE |
| JS-8 | Dead symbols: A380 `GRID_WIDTH`/`MAX_BODY_ROWS`/`KEY_FIRE_DELAY_MS`, `A.elementLabel`, `A.ensureKccuKeyboardOn` (zero call sites); flyPad write-only `A._elements`. | SAFE |
| JS-9 | `isVisible` exception-default drift: a380/ewd return true on throw; flypad/pmdg return false; display agent deliberately geometric (SVG). Fix: comment the intentional ones; align the a380 default or document it. | RISKY (visibility ripples) |
| JS-10 | Minor: `activeUnit` re-implements `settingsObj()` fallback inline; double `var w` declaration in `collect()`; nbsp normalization only in PMDG `txt`; Fuel-page O(n²) `buildFuelLines`; `buildPerfLines`+`buildLabeledFields` duplicate querySelectorAll. | mixed (see plan) |
| JS-11 | `tools/mcdu_run.ps1`, `mcdu_tour.ps1`, `mfd_import_and_scrape.ps1` default `$AgentFile` to a contributor's hardcoded `C:/Users/franc/...` path. Fix: repo-relative default. | SAFE |

### DOC — Documentation accuracy

| ID | Finding |
|----|---------|
| DOC-1 | CLAUDE.md: "No automated test project exists" false (tests + CI since 2026-07-06); "three projects" → four; the invariants bullet repeating the stale claim. Rewrite Testing section: pure-logic xUnit suite + CI; sim-facing behavior still needs in-sim plans. |
| DOC-2 | CLAUDE.md:78 + docs/development.md:100: `SimConnectManager.cs:251` citation stale (code ~:628). Cite the method, not the line. |
| DOC-3 | docs/tooling.md §8 + docs/development.md: `StartupLogger`/`%TEMP%` crash-diagnosis claims — class deleted; now `Log.Channel("startup", truncateOnLaunch: true)` → `%APPDATA%\MSFSBlindAssist\logs\startup.log`. Actively misleads support. |
| DOC-4 | docs/adding-features.md:202, docs/architecture.md:202, docs/fenix-increment-decrement.md (4 sites): samples teach `Debug.WriteLine` → update to `Log.Error`. |
| DOC-5 | Renamed settings forms: taxi-guidance.md → `Forms/Settings/TaxiGuidancePanel.cs`; visual-guidance.md (2 sites) → `HandFlyPanel.cs`; gemini.md → `GeminiPanel.cs`. (gsx.md's `GsxSettingsForm.cs` still exists — leave.) |
| DOC-6 | docs/flypad.md: `ShowFBWA380EFBDialog` → `ShowFbwEfbDialog()` (`MainForm.Dialogs.cs:454`). |
| DOC-7 | docs/variable-system.md Pattern 2: rewrite single-`GenericBatch` description to the 5×300 multi-batch design (pairs with SC-5). |
| DOC-8 | docs/visual-guidance.md:16 SIM_ON_GROUND pointer → `MainForm.Announcers.cs:~585`. |
| DOC-9 | docs/taxi-guidance.md:229 ground-traffic distance mechanism (pairs with SV-9). |
| DOC-10 | docs/pmdg-737.md:168 EFB title logic lives in `MainForm.Dialogs.cs:485`, not FbwEfbForm. |
| DOC-11 | EFBBridgeServer cross-doc contradiction (pmdg-737.md + hs787.md say deleted; flypad.md says retained; reality: dead-but-present). Resolved by SC-4's deletion — align all three docs to "deleted". |
| DOC-12 | docs/gsx.md:52 "not xUnit" → "probes + xUnit characterization tests". |
| DOC-13 | docs/a32nx-feature-parity-todo.md: stale TODO doc, open items already implemented; header admits superseded. Archive or delete. |
| DOC-14 | README.md: optional one-liner about the test project/CI for contributors. |

## Test coverage plan

Tests compile via `<ProjectReference>` to the main app (x64, xUnit, `InternalsVisibleTo` in place). Current 11 files cover Arinc429Word, DistanceFormatter/Milestones, DockingGeometry, GateAliasResolver, GsxOffset/AircraftIdMap, GuidanceGeometry, Log*, RouteRunwayCrossings, StandId.

**Wave 1 (PR-3, this plan)** — pins code later PRs touch + safety invariants; all pure logic, some need `private`→`internal` promotion:

| Target | Guards |
|--------|--------|
| `SimConnectManager.ExtractIcaoFromAtcModel` | 3-tier resolution, known-bad outputs ("NG3","CEO") |
| `ConvertMHzToBcd16Hz` | BCD encode; latent float-truncation hazard (122.800) |
| METAR precip decoder — BOTH copies against one vector set | mechanizes the CLAUDE.md keep-in-sync rule; "no precip → None" |
| `ColdTemperatureCorrectionForm.CorrectedAltitude` | EUROCONTROL formula; never-correct-downward-when-warm; round-up-to-10 ft |
| `GsxMenuClassifier` | count-suffix-before-IsBack ordering; `IsBackUp` vs "◀Previous Page"; ForbiddenAction never matches |
| `GsxService.TextRules` | port `tools/GsxTextProbe` asserts to xUnit (CI never runs the probe) |
| `TaxiGuidanceManager.MathUtils` | `ComputeTurnVerbalFromHeading`, `NormalizeAngle`, `RunwayDesignatorsMatch`, perpendicular distance |
| `PMDGNG3DataManager.DecodeCellSymbol` + `ToDouble` | pins the decode before SC-9's 777 port |
| `EWDMessageLookup` (both variants): `CleanANSICodes`/`GetMessagePriority` | safety-relevant ECAM announcement feed |
| `RunwayFrame` | cross-track/along sign conventions, displaced thresholds |
| `SettingsManager` seeds/migrations | `SeedTakeoffAssistToneConvention` mapping, idempotence |

**Wave 2 (deferred; prerequisite for the deferred risky work)**: `HoldShortNodeResolver` (KSFO D reciprocal case), `GetLandingExits` synthetic-graph fixture (prerequisite for ND-2), `TaxiRouter` fixture (GCLP component filter, bridge cap, no-Euclidean-fallback), `NavigationCalculator`, `GsxNavdataMerger`/`GsxPyOffsetEvaluator`/`GsxParkingNameEnum`, `TaxiDataMerger` anti-grass invariants, `AptDatParser`, `FbwMcduFormat`, `DisplayText.SetPreserveCaret`/`DisplayList.UpdateInPlace`, hotkey-list filtering, TCAS parsing, aircraft-def formatters (post-dedup), JS jsdom additions (`flightInfo` ident matching — would have caught JS-1 — OANS arinc/geometry, EWD/ECL token maps, DCDU slot mapping).

## PR plan

All branches off `main`, merged via PR. Order within phases matters; phases are sequential.

### Phase 1 — Foundations

- **PR-1 `docs/cleanup-accuracy`** — DOC-1..10, DOC-12..14, SV-9. No code. (DOC-11 ships with PR-2 so the docs and the deletion land together.)
- **PR-2 `chore/dead-code-removal`** — SC-4 (EFBBridgeServer + HS787SimBriefForm + comment), SC-5 (GenericBatch struct), SC-6 (except 400-499 branch), SC-8, ND-3 (AirportDatabase), JS-6 (bridge JS + dead patcher halves), JS-8, FM-7, AC-16 dead items, stale comments (SV-7's GsxGateSelector line), DOC-11 (align the three EFBBridgeServer doc mentions to "deleted"). Build must stay green; grep-verified zero references per deletion.
- **PR-3 `test/characterization-wave-1`** — the Wave 1 table above. Includes the `private`→`internal` promotions + `InternalsVisibleTo` already present.

### Phase 2 — Safe fixes

- **PR-4 `perf/simconnect-hot-path`** — SC-1, SC-2, SC-3 (SAFE half), SC-7, SC-9 (arrow port + 777 self-dedup), SC-10, SC-11, SC-13, SC-14.
- **PR-5 `perf/services-hot-path`** — SV-1, SV-2, SV-3, SV-4 (taxi-cooldown half), SV-5, SV-6, SV-7 (comment rewrite), SV-8/FM-3, SV-10 (optional).
- **PR-6 `perf/navdb`** — ND-1, ND-4, ND-5, ND-6, ND-7, ND-8, ND-9.
- **PR-7 `chore/aircraft-forms-safe`** — AC-1, AC-3, AC-9, AC-10 (byte-identical only + renames), AC-11 (identical only), AC-12 (A320 six-pack consolidation only), AC-15 (all but Thread.Sleep), FM-1, FM-2, FM-4, FM-8, FM-9, FM-10, FM-11, JS-7, JS-11, HS787 Ident "deliberate" comment.

### Phase 3 — Risky fixes (each PR ships with an explicit in-sim/NVDA test checklist Robin runs before merge)

- **PR-8 `fix/js-agents`** — JS-1, JS-2, JS-3, JS-4, JS-5, JS-9, JS-10 remainder. jsdom tests added first where the harness reaches (flightInfo table test). Test plan: per-page live verification (A380 F-PLN with step alt for T/D; flyPad Ground/Fuel/Settings pages under NVDA; PMDG EFB pages incl. value-only mutations for the dirty gate).
- **PR-9 `fix/behavioral-cleanup`** — AC-2 (777 IsReady), AC-4 (autobrake announce), AC-5, AC-6 (HS787 state-aware combos), AC-7 (RMP uniquifier), AC-11 wording harmonization + TryParseSpeedInput port + LAND-speech drift, AC-15 Thread.Sleep removal, FM-5 (validation announce normalization), SV-4 (VG deltaTime), SC-12 (hotkey readout defs), SC-6 400-499 branch removal (after ECAM in-sim check). May split further at implementation time if the test session gets too big.

### Deferred (documented, not planned)

- Cross-aircraft structural dedup at scale: AC-10 parameterized FCU merge, AC-11 full dialog dedup, AC-13 window-lifecycle unification (`OnAircraftSwappedOut()`), AC-14 table-driven readout scaffolding, SC-9 PMDG shared base class. Rationale: high churn in sim-verified code; best done per-aircraft alongside feature work.
- ND-2 `GetLandingExits` fallback consolidation — needs the Wave 2 synthetic-graph fixture first; then its own PR with rollout in-sim tests.
- SC-3 change-filter half (suppressing redundant default-channel LVar re-fires) — consumers may rely on re-fires; low residual value once the SAFE lookup-dictionary half lands.
- FM-6 (sync DB on 4th ICAO char) — accept unless stutter is reported.
- AC-8 (Fenix GEN1 aliasing) and AC-17 (HS787 KOHLSMAN units) — live investigation items, not code changes.
- Test Wave 2.

## Error handling / verification approach

- Every PR: `dotnet build MSFSBlindAssist.sln -c Debug` green + `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` green (CI enforces on PR).
- Dead-code PRs additionally grep-verify zero remaining references per deleted symbol.
- SAFE-bucket efficiency PRs must be semantics-preserving refactors; where output ordering could shift (ND-4), compare route outputs on recorded inputs before/after.
- RISKY PRs each carry a written in-sim/NVDA test checklist in the PR description; merge waits for Robin's pass.
