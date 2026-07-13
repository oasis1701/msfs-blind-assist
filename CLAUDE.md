# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Project Overview

MSFS Blind Assist - C# Windows Forms accessibility application for Microsoft Flight Simulator. Multi-aircraft support (FlyByWire A320, Fenix A320, extensible). SimConnect integration, screen reader optimized (NVDA/JAWS). .NET 10, Windows Forms, SQLite.

## Build Commands

```bash
dotnet build MSFSBlindAssist.sln -c Debug
dotnet build MSFSBlindAssist.sln -c Release
```

**Output (the run path):** `MSFSBlindAssist\bin\x64\{Debug|Release}\net10.0-windows\` — a plain `dotnet build` (the `.sln`, or the `.csproj` with `-p:Platform=x64`) writes HERE. There is **NO `win-x64\` subfolder** unless you build with an explicit `-r win-x64`; see the RID-subfolder gotcha below.

**⚠️ ALWAYS build the SOLUTION (or pass `-p:Platform=x64`), NEVER `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj` alone.** The csproj declares `<Platforms>x64</Platforms>` only, but a bare `dotnet build` on the .csproj still defaults to **Platform=AnyCPU** and writes the exe to **`bin\Debug\net10.0-windows\`** — a DIFFERENT folder from the one the app runs from (**`bin\x64\Debug\net10.0-windows\`**). The build will say "Build succeeded" while the running x64 exe stays frozen at its old timestamp, so changes silently never reach the user (burned a whole session on this — every "compile-check" went to bin\Debug and the user's x64 exe never updated). Correct commands: `dotnet build MSFSBlindAssist.sln -c Debug` (the .sln maps to Debug|x64) or `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj -c Debug -p:Platform=x64`. To verify a build actually landed, check the LastWriteTime of `bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` is "now". The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe the user will run.

**RID-subfolder gotcha (a SECOND build-path trap, seen 2026-06-06):** the csproj has no `<RuntimeIdentifier>`, so a plain `dotnet build` produces the non-RID run path above and creates **no** `win-x64\` subfolder. But building with an explicit **`-r win-x64`** (or `dotnet publish -r win-x64`) writes to a SEPARATE tree, `...\net10.0-windows\win-x64\`, that a plain build NEVER touches. So a stale `win-x64\MSFSBlindAssist.dll` can sit next to a fresh non-RID `net10.0-windows\MSFSBlindAssist.dll` and mislead you into thinking the build didn't land. **Build to — and verify the timestamp in — the folder the app actually launches from.** By default that is the non-RID `bin\x64\Debug\net10.0-windows\` (which the `.sln` build updates). Only if you deliberately run a `win-x64\` RID tree must you pass `-r win-x64` so C# changes reach it (a plain `.sln` build will not). NOTE: the JS agents under `Resources\` are copied to whichever tree you build, so when in doubt build BOTH (plain + `-r win-x64`), or just confirm your launch path.

**Prerequisites:** MSFS_SDK environment variable, .NET 10 SDK

The solution contains four projects: `MSFSBlindAssist` (main app), `MSFSBlindAssistUpdater` (small WinForms auto-update helper), `tools/PMDGDispatchTester` (a console diagnostic REPL for probing which PMDG NG3 dispatch shape a switch accepts against a live sim — e.g. used to confirm the 737 fire-handle UNLOCK→TOP sequence), and `tests/MSFSBlindAssist.Tests` (the pure-logic xUnit suite run by CI). The tester compiles the main app's `SimConnect/PMDGNG3DataStruct.cs` via a **linked** `<Compile>` (not a copy) so its CDA layout can never drift. `dotnet build MSFSBlindAssist.sln` builds all four. A second standalone probe, `tools/CDUTest`, fires a single CDA-write or TransmitClientEvent at one chosen PMDG event (used to prove the NG3 CDU keys need TransmitClientEvent, not the CDA write); it builds on its own (`dotnet build tools/CDUTest`), not as part of the solution.

## Testing

Pure-logic code is covered by an xUnit characterization suite at `tests/MSFSBlindAssist.Tests`
(run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`).
CI (`.github/workflows/tests.yml`) runs it on every PR and on pushes to main.
Sim-facing behavior cannot be unit-tested: when changing SimConnect/UI paths, build, then
describe an in-sim test plan in the PR — the human owner of the repo runs it. New pure logic
(formatters, parsers, geometry, classifiers) should get characterization tests; don't add
speculative tests for sim-driven paths.

## Git Workflow

The `main` branch is protected. Always create a new branch for changes and open a pull request — never commit directly to main.

## CRITICAL Rules (Always Follow)

### Screen Reader Announcements

**CRITICAL:** Screen readers automatically announce ALL UI control interactions.

**NEVER announce:**
- Button presses in panel controls
- Combo box/dropdown value changes
- Any direct user interaction with UI elements

**ONLY announce:**
- Numeric input confirmations (user needs exact value feedback)
- Error conditions (validation failures)
- Background state changes (not directly triggered by user)

**Why:** Screen readers already announce UI interactions. Redundant announcements = poor UX.

**Combo double-announce suppression is GLOBAL (`_uiSetEcho`, MainForm).** When the user changes a panel **combo**, the screen reader already speaks the selection — so MSFSBA must not also announce the resulting SimVar change. Every combo-set path calls `MarkUiSet(varKey, value)` (records `_uiSetEcho[varKey]` + a tick), and `OnSimVarUpdated` suppresses the duplicate two ways: (1) the generic `_uiSetEcho` gate for vars announced on the generic monitor path, AND (2) **a wrap that sets `announcer.Suppressed` around the `ProcessSimVarUpdate` call** for any var inside the echo window — because a def that auto-announces from INSIDE `ProcessSimVarUpdate` (PMDG APU selector + the Boris Audio Works soundpack switches, HS787, A380, …) returns `true` and exits BEFORE the generic gate ever runs. **The wrap was HS787-gated and is now ALL-aircraft (2026-06 fix)** — that gate-miss is exactly why the PMDG APU selector + the whole Boris panel double-announced. The wrap matches on the **time window only, not the value** (a combo set can write a different encoding than the SDK reads back — event position vs struct field, 0/1 vs 0/100 — so a value compare silently misses). So: a def that announces its own state from `ProcessSimVarUpdate` needs NO per-control echo flag for combo sets — the global wrap covers it; only background (non-UI) changes still announce.

**Resting-state button labels are suppressed ONLY via the opt-in `SimVarDefinition.SuppressRestingButtonState`** (set by the FBW momentary helpers `Btn`/`PressSilent`/`SeatBtn` + the inline cabin-ready/chrono defs). Never blanket-suppress value-0 labels in MainForm: PMDG 777 MCP "LNAV: Off" and HS787 "Baro STD: QNH" are meaningful states a blind user needs (PR #85 finding M4).

### SimConnect Connection Timing

**CRITICAL:** In SimConnectManager.cs, set `IsConnected = true` BEFORE calling `SetupDataDefinitions()`. Required for `StartContinuousMonitoring()` to execute properly (has guard clause requiring `IsConnected == true`). See SimConnectManager.Connect() in SimConnect/SimConnectManager.cs

### Accessible TreeView Controls

**CRITICAL:** Never use `TreeView` directly in forms. Use `NativeAccessibleTreeView` (`Controls/NativeAccessibleTreeView.cs`) instead. The framework's UIA-based `TreeViewAccessibleObject` (introduced in .NET 9, still the default in .NET 10) produces incorrect navigation order in NVDA — items appear out of sequence, focus jumps between unrelated nodes. `NativeAccessibleTreeView` bypasses the framework UIA implementation and falls back to the native Win32 SysTreeView32 MSAA proxy, which works reliably (NVDA-verified on the .NET 10 build, 2026-07).

**Pattern for tree views with detail data:**
- Parent nodes show summary text only — no child nodes pre-populated
- Add a dummy child `new TreeNode("Loading...") { Tag = "placeholder" }` so the expand indicator (+) appears
- Handle `BeforeExpand` to lazily populate real child nodes on demand, checking for the placeholder first
- Store the data index in `parent.Tag` so the expand handler can look up the data
- Leaf nodes (e.g. airport endpoints with no detail) get no placeholder and no expand indicator

This lazy-loading pattern keeps the tree lightweight (fewer total nodes) and avoids accessibility edge cases.

### Database Paths

**CRITICAL:** Never hardcode `Path.Combine(..., "FBWBA", "databases", ...)` or `Path.Combine(..., "MSFSBlindAssist", "databases", ...)`. The user's DB may live at *either* location for historical reasons (the app was renamed). All code must go through `Database/DatabasePathResolver.cs`:

- `ResolveExistingDatabasePath(simVer)` — for **reads** (canonical first, legacy fallback). Used by `DatabaseSelector`, `MainForm`, `ElectronicFlightBagForm` lookups, etc.
- `GetCanonicalDatabasePath(simVer)` — for **writes** (always `MSFSBlindAssist\databases\`). Used only by the build target in `DatabaseBuildProgressForm`.

`NavdataReaderBuilder.GetDefaultDatabasePath` delegates to the resolver and is safe for reads. The MS Store package name for FS2024 is `Microsoft.Limitless_8wekyb3d8bbwe` (not "FlightSimulator2024"); FS2020 is `Microsoft.FlightSimulator_8wekyb3d8bbwe`. Both are referenced in `NavdataReaderBuilder.GetMSFSBasePath` when resolving `UserCfg.opt` for the scenery base path.

### Diagnostic Logs — ONE folder, via `Utils/AppLogs`, ALL writes via `Utils/Logging/Log`

**CRITICAL:** `Utils/Logging/Log` (namespace `MSFSBlindAssist.Utils.Logging`) is the single logging entry point — never hand-build a log write (no raw `File.AppendAllText`/`StreamWriter` against a log path). Two ways to log: `Log.Debug/Info/Warn/Error(category, message)` writes to the app-wide `debug.log` (this is where all former `Debug.WriteLine` trace now goes, and unlike `Debug.WriteLine` it persists in Release builds, not just under a debugger); `Log.Channel("name")` (e.g. `Log.Channel("taxi_guidance")`, `Log.Channel("startup", truncateOnLaunch: true)`) returns a `LogChannel` that writes the dedicated `name.log`. Every line is formatted uniformly as `yyyy-MM-dd HH:mm:ss.fff [LEVEL] [category] message` (local time), written by a single async background writer (never inline/blocking file I/O on the calling thread), with size-based rotation (5 MB × 3 backups) per file. `Log.Init()`/`Log.Shutdown()` are wired in `Program.Main`.

`Utils/AppLogs` remains the PATH authority underneath `Log` — it is not bypassed, just no longer called directly for writing. Every diagnostic log still lives in ONE folder, `%APPDATA%\MSFSBlindAssist\logs` (Roaming), and every log path is still resolved through `Utils/AppLogs.PathFor("name.log")` (now internally, by `Log.Channel`) — never hand-build a log path. Historically logs were scattered (taxi/rollout logs in the Roaming root `%APPDATA%\MSFSBlindAssist\`, the startup log in `%TEMP%`, GSX/docking logs in `%LOCALAPPDATA%\MSFSBlindAssist\logs`), which made "send me your logs" support unanswerable. They were first unified under Local, then moved to Roaming so EVERYTHING the app owns — settings, databases, AND logs — lives in one `%APPDATA%\MSFSBlindAssist` tree. `AppLogs.MigrateLegacyLogs()` (called once at startup in `Program.Main`) best-effort sweeps BOTH legacy locations (Roaming-root `*.log` files AND the entire former Local logs folder) into the canonical folder and removes the Local folder once empty. Current files: `debug.log` (the new app-wide `Log.Debug/Info/Warn/Error` sink), `taxi_guidance.log`, `taxi_router.log`, `landing_exit.log`, `takeoff_assist.log`, `gsx-gate-select.log`, `docking.log`, `docking-aircraft.log`, `startup.log` (truncated per launch), `input_events.txt`. The tester instruction is always: **Windows+R → `%APPDATA%\MSFSBlindAssist\logs`** (or just `%APPDATA%\MSFSBlindAssist` to grab settings + databases + logs in one go).

### Flight-Planning EFB & Instrument-Procedure Data (Shift+E)

**Feature:** `Forms/ElectronicFlightBagForm.cs` — the screen-reader flight-plan builder (Departure / SID / STAR / Arrival / Approach / Airport Lookup tabs) + SimBrief import + Gemini route description. Procedure data comes from `Database/NavigationDatabaseProvider.cs` (the navdatareader `approach` / `approach_leg` / `transition` / `transition_leg` tables — SIDs are `suffix='D'`, STARs `suffix='A'`, approaches the rest), assembled by `Navigation/FlightPlanManager.cs`. **Navigraph navdata, when present in the user's DB, is used here automatically** — there is ONE merged DB read by every provider; nothing branches on data source.

**Key rules when touching this code (invariants behind the 2026-06 bugfix pass):**
- **Never drop a fix-less leg.** ARINC 424 path/terminator legs `CA`/`VA`/`CI`/`VI`/`VM`/`FM`/`CD`/`VD`/`CR`/`VR` legitimately have an empty `fix_ident` (~14 % of legs) — they carry the **initial climb of most SIDs and the heading legs of most missed approaches**. `ParseLegToWaypoint` must NOT `return null` on empty `fix_ident`; it synthesizes a readable maneuver label via `BuildManeuverLabel` (e.g. *"Climb heading 071° to 600 feet"*) from the leg `type` + course + altitude. The leg's path/terminator code lives in `approach_leg.type` (read into `WaypointFix.Type`), NOT `arinc_descr_code` (which this DB only fills with A/F/M/B fix-role letters).
- **Resolve fix coordinates across all fix tables, not just `waypoint`.** Navaid (`fix_type='V'`/`'N'`), runway-threshold (`'R'`, idents like `RW06L`) and airport (`'A'`) fixes are NOT in the `waypoint` table → a waypoint-only lookup left them at (0,0) and corrupted distance/bearing. `ResolveFixCoordinates` falls back to `vor`/`ndb`/`runway_end`/`airport` by fix_type. `approach_leg.fix_lonx`/`fix_laty` are NULL in this navdata build — do NOT rely on them. `FlightPlanManager.UpdateAircraftPosition` skips distance/bearing for any waypoint still at (0,0) — maneuver legs have no position.
- **Circling approaches (VOR-A, NDB-A…) share `suffix='A'` with STARs.** Distinguish them by **the presence of a missed-approach leg** (`EXISTS … approach_leg.is_missed=1`): `GetApproaches` INCLUDES suffix-A rows that have a missed leg (they're circling approaches); `GetSTARs`/`GetSTARsForRunway` EXCLUDE them. Without this, circling approaches were missing from the Approach list and polluting the STAR list.
- **"ALL"-runway SID/STAR loads the runway-INDEPENDENT body**, not an arbitrary runway's legs. The `GetSIDsForRunway`/`GetSTARsForRunway` "ALL" branch picks, per procedure name, the `approach_id` that is runway-independent under BOTH columns — empty `runway_name` AND a non-`RW…` `arinc_name` — then runway_name-less rows, then `MIN(approach_id)`. Keying on `runway_name` alone degenerated back to a random runway transition's legs at arinc-tagged airports (OMDB: `runway_name` NULL on every row).
- **Transition + procedure share their boundary fix** — `FlightPlanManager.AppendWaypoints` drops the duplicate first/last fix when concatenating, else it appears twice with a spurious 0 NM leg.
- **Airport Lookup**: the runway list needs a `SelectedIndexChanged` handler (selecting a runway must repopulate the runway-info box; it's not Load-button-only), and `GetRunwayDetailedInfo`'s `SELECT r.*, re.*` aliases the runway-END columns (`re.heading AS end_heading`, `end_altitude`/`end_lonx`/`end_laty`) and reads them by those explicit `end_*` names — the bare `heading`/`altitude`/`lonx`/`laty` are ambiguous between `runway` and `runway_end`. Microsoft.Data.Sqlite resolves such a duplicate to the LAST occurrence (`re`, the runway-end — verified by probe, NOT the first/`r` value earlier assumed), but the code must not depend on that positional accident; the explicit aliases keep the read unambiguous and provider-independent. Dropping an alias makes `reader["end_heading"]` throw "no such column" (the failure `RunwayInfoAliasingTests` pins), not silently return a center value.
- **Minimums (DA/MDA/visibility) are NOT in navdata** — not in CIFP, MSFS, or even Navigraph DFD; they live only on the visual plate. The EFB cannot show them; this is a data-source fact, not a fixable bug.

### Taxi Guidance

Details: [docs/taxi-guidance.md](docs/taxi-guidance.md).

### GSX Gate Integration, Docking Guidance & Distance Units

Details: [docs/gsx.md](docs/gsx.md) — user-facing usage in the main sections, developer internals (gate selection, docking geometry, distance units) under "Developer internals".

### Visual Landing Guidance (dual-tone)

Details: [docs/visual-guidance.md](docs/visual-guidance.md).

### Multi-Aircraft Architecture

**Core interfaces:**
- **IAircraftDefinition** - Contract for all aircraft
- **BaseAircraftDefinition** - Recommended base class (provides hotkey routing, caching, helpers)
- **FlyByWireA320Definition** - Reference implementation

**Each aircraft defines:**
- `GetVariables()` - All simulator variables
- `GetPanelStructure()` - Section/panel hierarchy
- `BuildPanelControls()` - Panel-to-variables mapping (cached automatically by base class)
- `GetHotkeyVariableMap()` - Simple hotkey action → event name mappings
- `HandleHotkeyAction()` - Custom hotkey logic (optional override)

## Invariants (do not revert)

Every bullet below is a condensed guardrail ("do NOT / NEVER / CRITICAL / gotcha / ⚠️" or an emphatic correction) pulled from this codebase's history. Each points at the doc that carries the full story — read that doc before touching the flagged code, but the one-liner alone is enough to avoid re-introducing a known-bad "fix". Grouped by area; "→ CLAUDE.md" means the full rule already lives above in this file.

### Build / project (→ CLAUDE.md — stays in the lean core)

- Always build the .sln or pass `-p:Platform=x64`; NEVER build the bare `.csproj` alone — it silently defaults to Platform=AnyCPU and writes to a different output folder (`bin\Debug\...`) than the x64 run path, so the running exe never updates. → CLAUDE.md
- RID-subfolder gotcha: `-r win-x64` (or `dotnet publish -r win-x64`) writes to a SEPARATE `net10.0-windows\win-x64\` tree that a plain `.sln` build never touches — always build/verify the exact folder the app launches from. → CLAUDE.md
- The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe. → CLAUDE.md
- `tools/CDUTest` and `tools/CDUTest`-style standalone probes build on their own, NOT as part of the solution. → CLAUDE.md
- Sim-facing paths are verified only against a live sim — describe an in-sim test plan in the PR; pure logic belongs in `tests/MSFSBlindAssist.Tests` (CI-enforced). → CLAUDE.md
- `main` is protected — never commit directly to main; always branch + PR. → CLAUDE.md

### Screen-reader announcements (→ CLAUDE.md — stays as full prose in the lean core)

- NEVER announce button presses, combo/dropdown value changes, or any direct UI interaction in panel controls — screen readers already announce them; ONLY announce numeric input confirmations, validation errors, and background (non-user-triggered) state changes. → CLAUDE.md
- Combo double-announce suppression must be GLOBAL, never aircraft-gated: `_uiSetEcho`/`MarkUiSet` plus a wrap that sets `announcer.Suppressed` around `ProcessSimVarUpdate` for any var inside the echo window — gating the wrap to one aircraft (the old HS787-only gate) causes double-announces on every other def that self-announces from inside `ProcessSimVarUpdate`. → CLAUDE.md
- The echo-window suppression must match on TIME only, never on value — a combo set can write a different encoding than the SDK reads back, so a value-compare silently misses the duplicate. → CLAUDE.md
- Never blanket-suppress value-0 resting-state button labels in MainForm — use the opt-in `SuppressRestingButtonState` flag only; some 0-state labels (PMDG 777 "LNAV: Off", HS787 "Baro STD: QNH") are meaningful and must be spoken. → CLAUDE.md

### Core SimConnect / framework (→ [architecture.md](docs/architecture.md))

- CRITICAL: set `IsConnected = true` BEFORE calling `SetupDataDefinitions()` in SimConnectManager — `StartContinuousMonitoring()` guards on `IsConnected == true`. → CLAUDE.md
- CRITICAL: never use `TreeView` directly in forms — use `NativeAccessibleTreeView`; the .NET 9/10 UIA `TreeViewAccessibleObject` produces wrong NVDA navigation order. → CLAUDE.md
- CRITICAL: never hardcode the FBWBA/MSFSBlindAssist database path — always go through `Database/DatabasePathResolver` (`ResolveExistingDatabasePath` for reads, `GetCanonicalDatabasePath` for writes). → CLAUDE.md
- CRITICAL: every diagnostic log path must be resolved through `Utils/AppLogs.PathFor(...)` into `%APPDATA%\MSFSBlindAssist\logs` — never hand-build a log path. → CLAUDE.md
- Never hand-build a log write (`File.AppendAllText`/raw path) — every diagnostic log goes through `Utils/Logging/Log` (`Log.Debug/Info/Warn/Error(category,msg)` → debug.log, or `Log.Channel(name)` → named file); `AppLogs.PathFor` is the PATH layer only. → CLAUDE.md
- Never register a name containing a space or colon as an L:var — those are stock SimVars (force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection entirely). → [architecture.md](docs/architecture.md)
- The SimConnect data-definition budget is 1000 per connection (resets per aircraft switch) — never register a var as BOTH an individual def AND a batch-covered def; a Continuous+IsAnnounced var must skip its individual registration and read from the shared batch cache. → [architecture.md](docs/architecture.md)
- `SetupDataDefinitions` must register bulk/batch vars LAST, after the fixed/critical defs (AIRCRAFT_INFO/ATC/position) — so a def-count overflow degrades gracefully instead of stranding aircraft detection. → [architecture.md](docs/architecture.md)
- Watch `registration.log`'s `approxTotalDefs` — never let a new var addition push a connection's total definitions near/over 1000 without splitting to a second SimConnect connection. → [architecture.md](docs/architecture.md)
- `RequestVariable(key, forceUpdate:true)` must also work for batch-covered (no-individual-def) vars — `ProcessContinuousBatch` must consult `forceUpdateVariables` too, or a forced re-read of an unchanged batch value silently no-ops. → [architecture.md](docs/architecture.md)
- `SetLVar`'s MobiFlight calc-path routing must gate on `CalcPathVerified`, never on bare `IsMobiFlightConnected` — that flag is true even with no WASM module installed. → [architecture.md](docs/architecture.md)
- A name containing a space or colon (e.g. `TRANSPONDER STATE:1`) is a stock SimVar shape and must stay on the data-def write path — never route it through the L:var calc path. → [architecture.md](docs/architecture.md)
- Every `ExecuteCalculatorCode` call embedding a computed double must use invariant fixed-point formatting — never default `{0}`/`$"{double}"` interpolation; both can emit scientific notation or comma-decimal output the MSFS RPN parser rejects. → [architecture.md](docs/architecture.md)
- H: events must always go to the MobiFlight channel whenever `IsMobiFlightConnected`; dotted events must wait for `CalcPathVerified` (queued, bounded, flushed on verify or probe-conclude) — never fire a dotted event before the probe concludes. → [architecture.md](docs/architecture.md)
- When adding a Continuous+IsAnnounced background-monitoring variable, do NOT also add it to `BuildPanelControls()` — batched monitoring registration is automatic. → [architecture.md](docs/architecture.md)
- The status-display auto-refresh repaint must be a LEADING-edge one-shot coalesce, never a restart-per-push trailing debounce — a trailing debounce starves under high-frequency PFD/ISIS streams (hand-fly posts per SIM_FRAME). → [architecture.md](docs/architecture.md)
- `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first — relying on stale cached `displayValues` alone goes stale for any def that returns `true` from `ProcessSimVarUpdate` (A32NX COM freqs, A380 EFIS baro, HS787 flight data all skip the generic write). → [architecture.md](docs/architecture.md)
- The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary on every call. → [architecture.md](docs/architecture.md)

### Universal variable/control troubleshooting playbook (→ [troubleshooting-playbook.md](docs/troubleshooting-playbook.md) — explicitly applies to every aircraft)

- Never conclude "doesn't work" from the MCP `set_lvar` tool (native data-def write) — it is unreliable for many FBW/add-on L:vars; always test writes via the calculator/MobiFlight path. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- A control can be fully working with NO audible/visible feedback — confirm by READ-BACK after ~1-2s, never by "I can't tell if it did anything." → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- Read back the DOWNSTREAM effect (the stock simvar/system output), not just the L:var you wrote — a dead output-mirror L:var holds a write but drives nothing, and conversely a genuine mirror L:var can falsely "pass" a stickiness test if written to its own already-current value. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- Test every state of a multi-position control, not just 0→1 — a control can stick at one value and revert at another. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- The reliable existence test for an add-on control is its own SOURCE (cockpit XML/behavior template + a systems-side reader) — the write-stick test alone passes even on a nonexistent variable. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- Never assume a DOM/WebView control needs the visible widget you'd click — trace what the real cockpit hardware input actually writes underneath (e.g. an ECL driven by push-button L:var pulses, not DOM clicks; an MFD driven by keypress events, not a cursor). → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- Coherent GT (Chromium 49) allows only ONE inspector socket per page for ANY aircraft using it — never open a second client against a view another client already holds; share the connection. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- A `_PRESSED`/`_Pressed`-style XMLVAR is almost always a model-only press-ANIMATION flag, not the real actuator — find the real K-event or state var instead of pulsing it. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)

### Flight-Planning EFB & instrument-procedure data (Shift+E) (→ [architecture.md](docs/architecture.md))

- Never drop a fix-less leg in `ParseLegToWaypoint` — empty-`fix_ident` ARINC 424 legs legitimately carry SID climb-outs and missed-approach heading legs; synthesize a maneuver label instead of returning null. → [architecture.md](docs/architecture.md)
- Resolve fix coordinates across ALL fix tables (vor/ndb/runway_end/airport), not just `waypoint`, or navaid/runway/airport fixes stay at (0,0) and corrupt distance/bearing; `approach_leg.fix_lonx`/`fix_laty` are NULL in this navdata build — do not rely on them. → [architecture.md](docs/architecture.md)
- Circling approaches share `suffix='A'` with STARs — distinguish by presence of a missed-approach leg; `GetSTARs` must exclude them or they pollute the STAR list. → [architecture.md](docs/architecture.md)
- "ALL"-runway SID/STAR must load the runway-INDEPENDENT procedure body (empty runway_name AND non-RW arinc_name) — keying on `runway_name` alone degenerates to a random runway transition's legs. → [architecture.md](docs/architecture.md)
- `FlightPlanManager.AppendWaypoints` must drop the duplicate shared boundary fix between a transition and its procedure, or a spurious 0 NM leg appears. → [architecture.md](docs/architecture.md)
- `GetRunwayDetailedInfo` must explicitly alias the `runway_end` columns (`re.heading AS end_heading`, etc.) and read them by those `end_*` names — the bare columns are ambiguous between `runway`/`runway_end`; Microsoft.Data.Sqlite resolves the duplicate to the LAST occurrence (`re`, verified by probe — not the primary-end value once assumed), but the code must not rely on that positional accident. → [architecture.md](docs/architecture.md)
- Minimums (DA/MDA/visibility) are not present in navdata at all (not CIFP, MSFS, or Navigraph DFD) — this is a data-source limitation, not a fixable bug; don't try to source them from the DB. → [architecture.md](docs/architecture.md)

### Taxi guidance (→ [taxi-guidance.md](docs/taxi-guidance.md))

- No airport-specific hardcoding — every taxiway/parking/runway name must flow through from the user's DB unchanged. → [taxi-guidance.md](docs/taxi-guidance.md)
- Takeoff Assist's `Toggle(off)` must unconditionally clear the runway reference — within-session preservation let a turnaround flight silently reuse flight 1's runway on flight 2's CTRL+T; the teleport dialog path still sets the reference unconditionally so teleport always wins. → [taxi-guidance.md](docs/taxi-guidance.md)
- Where-Am-I's runway-detection fallback must use a strict half-width tolerance (no +5m fudge) and stay gated on `_lastOnGround`. → [taxi-guidance.md](docs/taxi-guidance.md)
- Auto-activate-Takeoff-Assist-on-lineup is a one-shot latch (`_autoActivateFired`) and must NOT reset on lineup drift-out — re-engaging after a deliberate manual deactivation would surprise the pilot. → [taxi-guidance.md](docs/taxi-guidance.md)
- Never announce runway info (length/surface/ILS) from taxi guidance — out of scope. → [taxi-guidance.md](docs/taxi-guidance.md)
- `WAYPOINT_CAPTURE_RADIUS_M` (25m) must skip the last route segment or it preempts the gate arrival radius / parking countdown. → [taxi-guidance.md](docs/taxi-guidance.md)
- Steering tone must stay stereo-pan only — never add frequency/volume modulation to the taxi/lineup tone (pulse mode's on/off volume toggle is the one deliberate exception). → [taxi-guidance.md](docs/taxi-guidance.md)
- Taxiing tone target must be the continuous arc-length walk (`GuidanceGeometry.WalkTarget`) — a turn/no-turn branch reintroduces one-frame target jumps (hard pan-flips); clamp `t` at the upper bound only, never the lower (clamping low teleports the walk start ~25m at every capture). → [taxi-guidance.md](docs/taxi-guidance.md)
- "Straighten." must fire per sustained-yaw episode — never gate it on per-junction `TurnAngleDegrees`; navdata splits real 90° turns into many small micro-bends. → [taxi-guidance.md](docs/taxi-guidance.md)
- Runway lineup must use explicit thresholds (`UpdateHeadingErrorWithThresholds`, 0.5°/1°/15°) — do NOT call the width-scaled tone overload here; its `MIN_SCALE` clamp leaves pilots 3° off heading with no audio cue. → [taxi-guidance.md](docs/taxi-guidance.md)
- Lineup-aligned hysteresis (enter <1°/<10ft, exit >2°/>20ft) are fixed literals in `UpdateLineup` — do not loosen back toward the old 2°/5°–15ft/30ft deadband. → [taxi-guidance.md](docs/taxi-guidance.md)
- Lineup pulse mode must key on BOTH heading error AND cross-track — cross-track can be huge while intercept-angle saturation reads heading error as ~zero; dropping the cross-track branch leaves the pilot with no cue to move forward. → [taxi-guidance.md](docs/taxi-guidance.md)
- Runway lineup steering must stay intercept-angle-based — never reintroduce a bearing-to-threshold blend; once past the threshold, bearing-to-threshold sits on the ±180° wrap and produces chaotic sign flips. → [taxi-guidance.md](docs/taxi-guidance.md)
- Every `LiningUp` state entry must reset the heading-error smoother, or the taxi-phase low-pass residual leaks into the lineup tone and can steer the pilot off the runway. → [taxi-guidance.md](docs/taxi-guidance.md)
- No feet-quantity verbal cues for spatial/cross-track guidance — a blind pilot has no reference for "42 feet left"; the tone is the cross-track instrument (heading numbers are fine, every pilot has a heading instrument). → [taxi-guidance.md](docs/taxi-guidance.md)
- `TaxiGuidanceManager._stateLock` must be acquired by any new public method touching `_route`/`_state`/`_currentSegmentIndex`. → [taxi-guidance.md](docs/taxi-guidance.md)
- Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons (breaks on long segments). → [taxi-guidance.md](docs/taxi-guidance.md)
- Off-route auto-recalc must stay gated on the route-joined latch (`_hasJoinedRoute`) — without it, the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance before the pilot has joined it. → [taxi-guidance.md](docs/taxi-guidance.md)
- An accepted recalc must announce the new taxiway sequence by name ("Route changed. Now via X, Y…") — never the old generic "Recalculating… Taxiway X." wording. → [taxi-guidance.md](docs/taxi-guidance.md)
- Hold-short-to-runway association must be by nearest runway CENTERLINE, not threshold distance — the old <500m-to-threshold test mislabeled crossings far from either threshold with the taxiway name instead of the runway. → [taxi-guidance.md](docs/taxi-guidance.md)
- A user "end of taxiway" hold-short label must never be touched/overwritten by the crossing-label self-heal logic. → [taxi-guidance.md](docs/taxi-guidance.md)
- The Progressive Taxi terminator block is self-contained with its OWN runway/taxiway combos — never reuse the per-row "Hold short of runway" combo for the terminator target; that per-row combo must be hidden (and reset) in Progressive Taxi mode so a stale pick can't leak into the route. → [taxi-guidance.md](docs/taxi-guidance.md)
- Never restore an "already used taxiway" filter on the Add-Taxiway dropdown — ATC clearances legitimately reuse a taxiway across a runway crossing (KBOS pattern); only the immediately-previous taxiway is conditionally hidden. → [taxi-guidance.md](docs/taxi-guidance.md)
- "Where Am I" is ground-only by design (gated on `_lastOnGround`); runway detection must use `TaxiGraph.RunwayCenterlines`, not `taxi_path.type='R'` edges (the DB has none). → [taxi-guidance.md](docs/taxi-guidance.md)
- Landing Exit Planner's `SIM_ON_GROUND` handler must always use `RequestAircraftPositionAsync`, never trust `LastKnownPosition` — that cache can be stale from a prior mode and silently fails the GS≥40kt "real landing" gate. → [taxi-guidance.md](docs/taxi-guidance.md)
- `SetExit(..., currentlyAirborne)` must be armed from the actual air/ground state, never unconditionally true — unconditional true would false-trigger the exit plan during a high-speed taxi or rejected takeoff. → [taxi-guidance.md](docs/taxi-guidance.md)
- Rollout handoff to Taxiing must require `turnBegun || (atTaxiSpeed && nearExit)` — do NOT relax `nearExit` back to a speed-only condition; a long runway drops below 30kt thousands of feet before the planned exit. → [taxi-guidance.md](docs/taxi-guidance.md)
- Never remove the `ROLLOUT_TURN_MAX_GS_KTS` (90kt) speed cap on `turnBegun` — above 90kt a heading deviation is touchdown yaw/crosswind crab, not a real exit turn (KJFK 22L false-trigger). → [taxi-guidance.md](docs/taxi-guidance.md)
- `EnterRunwayEndCountdown` must null `_route`/`_destinationNodeId` on overshoot-with-no-exit, or `TryRecalculateRoute` routes back across the runway to the already-passed exit. → [taxi-guidance.md](docs/taxi-guidance.md)
- `TryEarlyExitHandoff` must fire ONLY for High-speed exits (angle <50°) — never restore it for Normal/End exits; it caused a 90°-exit tone to hard-pan 300ft early with no verbal cue (EGNX miss). → [taxi-guidance.md](docs/taxi-guidance.md)
- Every handoff to Taxiing must always re-route from the live aircraft position via the extension-node logic — never revert to the ApronNodeId-only re-route (Normal/End exits have ApronNodeId==NodeId and would keep the bogus initial route). → [taxi-guidance.md](docs/taxi-guidance.md)
- The post-high-speed-exit `ExitBearingTrue` pan floor must release via the opposite-SIGN test only, never magnitude-vs-floor, and never restore the unconditional `Math.Max/Min` clamp — magnitude gating reintroduces the wrong-side hard-pan reversal seen at CYVR. → [taxi-guidance.md](docs/taxi-guidance.md)
- Both override guards (apron forward-direction AND `apronAngle > currentAngleFwd`) are required together in the implicit-exit shallow-angle override — dropping either regresses the exit bearing. → [taxi-guidance.md](docs/taxi-guidance.md)
- `ExitAngleDegrees` is intentionally left unchanged by the shallow-angle override — touching it cascades into `ExitType` classification and the overshoot-margin formula, each with separate regression risk. → [taxi-guidance.md](docs/taxi-guidance.md)
- The `exitedLaterally` handoff trigger must use the combined gate (lateral + dist/hdgDelta/pastExit), never bare lateral distance — bare lateral fires too eagerly during the rollout's silent-tone phase (EDDB false trigger). → [taxi-guidance.md](docs/taxi-guidance.md)
- The passive-handoff `exitedLaterallyPH` check must NOT be gated the same way as the trigger — different semantics; gating it delays clearing the overshoot monitor. → [taxi-guidance.md](docs/taxi-guidance.md)
- `_rolloutRunway` must stay cached through `EnterRunwayEndCountdown` — the countdown needs it; full silence on a missed-last-exit is unsafe for a blind pilot. → [taxi-guidance.md](docs/taxi-guidance.md)
- `LoadRoute`/`TryRecalculateRoute` must filter start-node candidates to the destination's `ComponentId` — without the connected-component filter, A* can't reach an isolated taxiway island (GCLP S5) and route calc silently fails. → [taxi-guidance.md](docs/taxi-guidance.md)
- Node ID 0 is a permanent "not set" sentinel in `TaxiGraph` — never reuse it as a real node id. → [taxi-guidance.md](docs/taxi-guidance.md)
- Taxiway exit/intersection picking must use GRAPH distance (Dijkstra from the destination), never Euclidean — Euclidean silently fails on a graph dead-end (KDEN case). → [taxi-guidance.md](docs/taxi-guidance.md)
- `FindBestIntersection` has no Euclidean fallback by design — returning -1 (unreachable) on a malformed graph is intentional; don't add one back (the old Euclidean path produced silently wrong routes). → [taxi-guidance.md](docs/taxi-guidance.md)
- The last cleared taxiway must be honored as the route terminus when it branches off the destination — never let the final unconstrained leg silently drop the cleared last taxiway (EIDW N2, LFPG R1 cases). → [taxi-guidance.md](docs/taxi-guidance.md)
- The post-recalc sanity gate needs BOTH the length-blowup indicator AND the backwards-bearing indicator, OR'd (not AND'd) — either alone misses real dead-end-backtrack recalcs. → [taxi-guidance.md](docs/taxi-guidance.md)
- The initial-load sanity advisory must compare against the PRE-truncation `fullRouteMeters`, not the truncated total — truncation can hide a genuine backtrack detour (EHAM 18L case). → [taxi-guidance.md](docs/taxi-guidance.md)
- `TryRecalculateRoute` must fall back to shortest path (never the full original clearance sequence) when the aircraft isn't near any sequence taxiway — reapplying the full sequence routes the pilot backwards through the whole clearance. → [taxi-guidance.md](docs/taxi-guidance.md)
- `BeginLandingRolloutNoGraph` must defensively null `_route` at entry regardless of the takeoff path, so the handoff-failure fallback invariant always holds. → [taxi-guidance.md](docs/taxi-guidance.md)
- `RetargetLandingExit` must cascade through every downfield exit before giving up — one bad `LoadRoute` on the first candidate must not skip good later exits (YSSY case). → [taxi-guidance.md](docs/taxi-guidance.md)
- The undershoot retarget scan needs a speed-proportional minimum lead distance, not "nearest within 1000ft" — the naive version retargeted to an exit impossible to make at the aircraft's speed. → [taxi-guidance.md](docs/taxi-guidance.md)
- The rollout announce latches (e.g. `_rolloutApproach900Announced`) must be reset at all four reset sites (`BeginLandingRollout`, `BeginLandingRolloutNoGraph`, `EnterRunwayEndCountdown`, `StopGuidance`). → [taxi-guidance.md](docs/taxi-guidance.md)
- `GroundTrafficMonitor.SuppressCheck` must silence Caution/Warning callouts during the takeoff roll, when no taxi route is active, and during LandingRollout — but the forward-arc filter (120°) must never gate Awareness pings, and the Alt+G manual summary hotkey must stay ungated. → [taxi-guidance.md](docs/taxi-guidance.md)
- `TaxiAssistForm.OnCalculateClicked` must refresh the aircraft position from `LastKnownPosition` immediately before building the route, or the route starts from a stale pre-pushback position and off-routes on frame one. → [taxi-guidance.md](docs/taxi-guidance.md)
- Never scan `%APPDATA%\HiFi\` for the ActiveSky settings-file port — recursive directory enumeration there blocks the UI thread for many seconds. → [weather.md](docs/weather.md)
- A "METAR says no precipitation" result must render as "None," never fall through to the next weather source — only a wholly-missing METAR should trigger fallback. → [weather.md](docs/weather.md)
- Turbulence ≤25 (the AS calm-weather baseline) must be hidden entirely, never shown as a raw alarming number. → [weather.md](docs/weather.md)
- Don't move the WMO/ICAO weather-token decoder out of `WeatherRadarForm.ParsePrecipFromMetar` without updating its duplicate in the weather monitor — keep both copies in sync. → [weather.md](docs/weather.md)
- All three precipitation readouts — the Weather Radar, the ActiveSky decoded-weather monitor, and the Alt+W auto-announce — MUST derive precip from the SAME source precedence (closest-station METAR first, position METAR second, SimConnect bitmask last) so they never contradict; the difference the user reported (radar rain vs decoded "none") was two features reading different METARs. → [weather.md](docs/weather.md)
- The ActiveSky precip auto-announce must NOT repeat an unchanged phrase: compare the decoded precip phrase trimmed + case-insensitive, and speak only on start / stop / a genuinely different phrase ("light rain" → "light rain" stays silent). → [weather.md](docs/weather.md)
- ActiveSky integration is strictly OPT-IN (`UserSettings.ActiveSkyEnabled`, Weather settings tab, default OFF) — no AS probe may ever run on an unopted path: the liveness probe has a ~1.2 s floor when AS is absent, which every non-AS user paid per output+I press before the switch existed. The gate is CENTRAL, inside `ActiveSkyClient.IsRunningAsync` (returns false instantly, `LastStatus = "disabled in settings"`); every call site degrades to its no-AS path through it, and the `ActiveSkyWeatherMonitor` additionally runs only when `ActiveSkyWeatherMonitor.ShouldRun` says so (`ActiveSkyEnabled` AND `WeatherAutoAnnounceEnabled`) — the AS switch alone must never start the spoken weather updates. → [weather.md](docs/weather.md)
- Wind truth is PER-ENGINE, keyed on the ActiveSky switch: with AS enabled, the Alt+W precip auto-announce AND the output+I wind come from ActiveSky (station/position METAR for precip; AS ambient wind + surface gust for wind — the SimConnect `AMBIENT PRECIP STATE` bitmask sticks and the ambient wind can diverge under AS wind smoothing); with the switch off, SimConnect is authoritative and correct. Neither source is "the" truth — never hard-pick one for both populations. → [weather.md](docs/weather.md)
- The Cold Temperature Correction math must be transcribed VERBATIM from FlyByWire's EUROCONTROL formula (including the redundant term and round-up-to-10ft) and must never correct a published altitude downward on a warm temperature. → [taxi-guidance.md](docs/taxi-guidance.md)
- No-op recalc suppression: a recalculated route identical to the current remaining sequence must skip the "Route changed" callout, latch resets, and tone re-slew entirely — otherwise a sharp corner-cut trips a spurious "Route changed" on an unchanged route (LFPG case). → [taxi-guidance.md](docs/taxi-guidance.md)
- Do NOT remove the first-taxiway pre-snap — it's the LEPA anchoring fix; the pavement lead-in only replaces it when the first cleared taxiway is far (>75 m from the aircraft). → [taxi-guidance.md](docs/taxi-guidance.md)
- GroundSpeedAnnouncer is mode-independent (every on-ground phase: taxi, takeoff roll, landing rollout) — do NOT move it back into a per-mode manager like TaxiGuidanceManager; it stopped the instant takeoff-assist/touchdown took over when it lived there. → [taxi-guidance.md](docs/taxi-guidance.md)
- The 1,000-ft altitude callout (`AltitudeCalloutAnnouncer`) must fire AT the boundary (the band change IS the crossing), not ~80 ft late; and it announces the thousand CROSSED (same value climbing or descending), not the band entered. Re-crossing the LAST-announced thousand is ALWAYS silent with NO distance window (turbulent-cruise excursions step >80 ft between samples, so a window can't be trusted — user ruling: never re-announce); it re-arms only when a different thousand announces or on the ground/teleport reset (which must clear the announced-thousand latch too, or the next flight's climb-out swallows it). → [taxi-guidance.md](docs/taxi-guidance.md)
- Never disable the auto-inserted runway-crossing hold-shorts — VATSIM controllers expect a hold-short at every crossed runway, and silently rolling across an active runway is a runway-incursion risk (FAA AIM 4-3-18 / ICAO Doc 4444). → [taxi-guidance.md](docs/taxi-guidance.md)
- `ApplyUserRunwayHoldShorts` must scan forward from the FIRST run of segments tagged with the named taxiway, not the LAST — and its geometry test must be reciprocal-aware (resolve to the `RunwayCenterline` once, test the edge-crossing against its endpoints) or a same-named taxiway continuing past a crossing (KSFO D) silently rejects the user's correct hold-short pick. → [taxi-guidance.md](docs/taxi-guidance.md)
- TaxiSteeringTone must reset audio-modulation state (`_pulseActive`) in both `Start()` and `Stop()` — never trust caller-side cleanup; a leaked pulse state pulses the next route's taxiing tone at 3Hz. → [taxi-guidance.md](docs/taxi-guidance.md)
- TaxiSteeringTone must refresh volume on every sounding frame, not only in pulse mode — a pulse→continuous transition can otherwise leave the tone stuck at zero volume until an unrelated state change. → [taxi-guidance.md](docs/taxi-guidance.md)
- Verbal turn direction must be computed from the aircraft's current heading (`ComputeTurnVerbalFromHeading`), never the route's static `TurnDirection` — off-axis (post-pushback, after a wide turn) the actual turn can be the opposite direction and the static cue contradicts the (correct) tone. → [taxi-guidance.md](docs/taxi-guidance.md)
- Runway-destination lineup must anchor on the `start` table (`GetRunwayStarts`), never `Runway.StartLat/StartLon` directly — the latter is the pavement edge, hundreds of metres off the lineup point at displaced-threshold runways, and routes the aircraft to a node off the runway. → [taxi-guidance.md](docs/taxi-guidance.md)
- The `FindRunwayBridge` 200m cap must not be raised — it prevents a silent half-airport jump when an ATC clearance is genuinely wrong (those must fall back to shortest path with a log line). → [taxi-guidance.md](docs/taxi-guidance.md)
- Taxi-data augmentation: navdata is AUTHORITATIVE — an existing navdata taxiway/gate name is never overwritten, online names only fill UNNAMED segments, and online-only geometry is IGNORED (never steer on an offset online line). → [taxi-guidance.md](docs/taxi-guidance.md)
- Taxi-data augmentation is anti-grass: online data NEVER sets a gate Name/position and NEVER adds a selectable gate — gate identity is authoritative from GSX/navdata, online contributes searchable aliases only. → [taxi-guidance.md](docs/taxi-guidance.md)
- Do NOT implement OSM `holding_position` hold-short sharpening without an explicit sim-verified ask — augmentation stays NAME-only and must never touch the tuned, safety-critical hold-short placement. → [taxi-guidance.md](docs/taxi-guidance.md)

### GSX gate integration, docking guidance & distance units (→ [gsx.md](docs/gsx.md))

- Domain boundary with PR #84: this codebase owns POSITIONING only (gate/stand selection, docking geometry, deice positioning) — never add live service-state logic here, and docking must never read service vars. → [gsx.md](docs/gsx.md)
- `GsxNavdataMerger` must never cross-concourse-borrow coordinates — a navdata candidate may only donate coordinates when its normalized concourse matches the GSX gate's; otherwise drop the spot (a mislabeled coordinate on another pier is worse than omission). → [gsx.md](docs/gsx.md)
- GSX gate spot-position priority is `this_parking_pos` → navdata → stop position as LAST resort — the GSX stop position is a VDGS nose-stop reference, not an aircraft-datum location; using it as the spot position teleports the datum into the stand. → [gsx.md](docs/gsx.md)
- The `.py` per-aircraft stop offset must apply to ALL non-deice gates including `.ini` gates — skipping it for `.ini` gates left every `.ini`-airport 777 parking ~5m short. → [gsx.md](docs/gsx.md)
- `GsxOffset.Zero` must be a strict no-op (skip the shift entirely) — any resolver miss at any layer must degrade to Zero, never throw or half-apply. → [gsx.md](docs/gsx.md)
- `GsxGateSelector` must never choose a `ForbiddenAction` (WARP/Follow-Me/reposition/towing) menu entry — only a positively-identified `SafeServicingAction`. → [gsx.md](docs/gsx.md)
- `BackOutAsync` must use `IsBackUp` (`IsBack && !IsNext`), never the raw back-pattern match — the raw pattern also matches the "◀Previous Page" pagination entry and gets the search stuck in a submenu. → [gsx.md](docs/gsx.md)
- The count-suffix Category check ("(N suitable parkings)") must run BEFORE both `IsBack` and leaf matching in `GsxMenuClassifier` — reordering breaks group-header detection and desyncs `BackOutAsync`. → [gsx.md](docs/gsx.md)
- `SelectGateAsync` needs its `Interlocked` reentrancy latch — two overlapping Calculate clicks can interleave two DFS traversals on one live GSX menu and press arbitrary wrong entries. → [gsx.md](docs/gsx.md)
- Docking's forward distance math must stay datum-aligned — never reintroduce the per-aircraft `gsx.cfg` longitudinal door offset into the stop math; it describes door position on the airframe, not a stop offset, and parked a B777 ~26m short when subtracted. → [gsx.md](docs/gsx.md)
- The docking lateral cue must use the heading-error angle, never `CalculateCrossTrackError` — that assumes the aircraft is ahead of the reference and yields ±180° garbage when docking from behind. → [gsx.md](docs/gsx.md)
- `DockingGeometry.ClampStopToOccupancy` must never be removed or simplified to clamp on `gatedistancethreshold` unconditionally, and must clamp only the `.py`-shifted stop, never the navdata base point — it must remain a no-op for deice pads, navdata-only gates, already-inside-circle datums, and VDGS-reliant gates whose stop sits beyond the threshold. → [gsx.md](docs/gsx.md)
- Docking's lateral tone must use the runway-lineup PRECISION profile (`UpdateHeadingErrorWithThresholds`), never the width-scaled overload — its MIN_SCALE clamp is far too loose for parking. → [gsx.md](docs/gsx.md)
- `DockingCompleted` must fire `taxiGuidanceManager.StopGuidance()` exactly once (event raised outside the docking lock), or taxi guidance can be left stuck in LiningUp after parking. → [gsx.md](docs/gsx.md)
- Arrival ownership must stay ENGAGE-LATCHED (docking `IsActive` = Docking or Stopped state), never widened back to gate-set semantics — the old semantics left a pilot in total verbal silence when docking never engages (approach outside cone, navdata heading error). → [gsx.md](docs/gsx.md)
- Docking's stop tolerance and beep plateau must not regress: `StopToleranceMetres` stays 0.3m and `BeepNearMetres` must equal it (no plateau) — a plateau makes 2m-to-stop sound identical to the stop itself and pilots park short. → [gsx.md](docs/gsx.md)
- The solid "docked" tone must hold through the Stopped state until guidance ends or the aircraft moves away, but must NOT hold after an OVERSHOOT stop — a "docked" marker over a bad park misleads. → [gsx.md](docs/gsx.md)
- Docking's taxi-away disengage must use ABSOLUTE distance, never along-track — along-track goes negative once the stop is behind the aircraft and can never trip for a forward taxi-out. → [gsx.md](docs/gsx.md)
- Disabling docking (or losing the gate) mid-approach must fully `ResetLocked`, not just go silent — leaving `_state` latched at Docking/Stopped keeps `IsActive` true forever and mutes taxi's steering tone with no lateral cue. → [gsx.md](docs/gsx.md)
- Takeoff-assist activation and `LandingRollout` entry must both call `SetDestinationGate(null)` — a stale departure gate could otherwise keep docking `IsActive` latched on landing and mute the rollout steering tone. → [gsx.md](docs/gsx.md)
- The runway-style stopped-misaligned pulse must NEVER be re-added to gate lineup — precision parking is docking's job; pulsing 3Hz at a correctly-parked pilot demanding precision to a possibly-offset navdata point is a misfeature. → [gsx.md](docs/gsx.md)
- MainForm must call `taxiGuidanceManager.SetSteeringToneSuppressed(dockingGuidanceManager.IsActive)` every frame so only one steering tone ever plays — taxi and docking must never pan simultaneously. → [gsx.md](docs/gsx.md)
- Hot-path perf invariants must not regress: docking far-field telemetry/lineup math stays gated to <150m or engaged; hold-short/parking/exit-approach/runway-end callout paths must early-out once their latches have fired; `TaxiAssistForm`'s gate list must stay cached in memory per ICAO (never re-query per keystroke); `SettingsManager.Save` must write the file OUTSIDE its static lock. → [gsx.md](docs/gsx.md)
- SimConnect's `PLANE_HEADING_DEGREES_TRUE`/`_MAGNETIC` are returned in RADIANS despite the name — always multiply by 57.2958 before using as degrees (lat/lon are already in degrees). → [gsx.md](docs/gsx.md)
- `DistanceFormatter` is a DISPLAY layer only — never use it for guidance thresholds; those must stay unit-native (metric) internally. `GroundTrafficUseMetres` is a separate, independent toggle from `GroundDistanceUnit` — never fold them together. → [gsx.md](docs/gsx.md)

### Visual landing guidance (dual-tone) (→ [visual-guidance.md](docs/visual-guidance.md))

- Visual guidance must NOT require HandFly mode — never reintroduce a `!handFlyManager.IsActive` gate; that produced a confusing three-tone overlap. → [visual-guidance.md](docs/visual-guidance.md)
- HandFly's tone must auto-mute while VG is active (`SuppressAudio`/`ResumeAudio`) — VG's two tones share HandFly's Hz/pan mapping, so all three together is acoustically incoherent. → [visual-guidance.md](docs/visual-guidance.md)
- The quick-access hotkey set must stay reference-counted/shared between HandFly and VG — never split it back into per-mode key sets, which caused a double-register conflict. → [visual-guidance.md](docs/visual-guidance.md)
- There is no single-tone VG mode — never reintroduce a flag to gate off the current tone; the dual-tone is the design. → [visual-guidance.md](docs/visual-guidance.md)
- Always route `cachedBank` through `VisualGuidanceManager.StandardBank()` before any tone/bank-error use — SimConnect's `PLANE_BANK_DEGREES` is left-positive but the tone API is right-positive. → [visual-guidance.md](docs/visual-guidance.md)
- `VisualGuidanceManager.Initialize` must stay idempotent (calls `Stop()` first) — don't remove that guard. → [visual-guidance.md](docs/visual-guidance.md)
- Never re-add a `Start()` call inside `Initialize` — tone start must stay deferred to the first `ProcessUpdate` or a brief fused-tone glitch reappears. → [visual-guidance.md](docs/visual-guidance.md)
- The follower (current) tone must only start if the desired tone started — don't reorder `StartTonesIfNeeded`'s try blocks. → [visual-guidance.md](docs/visual-guidance.md)
- VG auto-deactivation on the airborne→on-ground edge must not be gated on GS or any other condition — landings of any speed must trigger it. → [visual-guidance.md](docs/visual-guidance.md)
- Never split VG's lateral and vertical guidance into separate tones — the matching idiom needs one oscillator per role (desired + current). → [visual-guidance.md](docs/visual-guidance.md)
- Desired and current tone waveforms must stay different (triangle + sine) — identical waveforms at a matched state phase-cancel exactly when the pilot most needs the difference audible. → [visual-guidance.md](docs/visual-guidance.md)
- Never bake aircraft-specific VG numbers back into `VisualGuidanceManager` as consts — they belong on `IAircraftDefinition.GetVisualGuidanceProfile()`. → [visual-guidance.md](docs/visual-guidance.md)
- `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` are applied in different code paths (glideslope error vs. phase detection) and were measured separately — never collapse them into one shared constant. → [visual-guidance.md](docs/visual-guidance.md)
- The `MAX_DESCENT_RATE_FPM` safety clamp must stay dynamic (`min(-1500, natural×1.3)`) so a legitimate steep-approach descent rate (e.g. a 5.5° ILS) is never clipped. → [visual-guidance.md](docs/visual-guidance.md)
- The pitch PID's `fpmError`/`fpmErrorRate` coefficients must stay POSITIVE (same sign as the error) — never reintroduce the old leading-minus; it produced wrong-direction guidance masked by tight-tracking autopilot tests. → [visual-guidance.md](docs/visual-guidance.md)
- PID math, phase machine, and lateral arc-capture logic must stay untouched by tone work — VG's failure mode must always be "missing audible reference," never "wrong steering command." → [visual-guidance.md](docs/visual-guidance.md)
- VG's manual-query grace window must only suppress the two chatty per-second callouts (bank guidance, centerline deviation) — phase changes and distance callouts must still fire during a manual hotkey readout. → [visual-guidance.md](docs/visual-guidance.md)

### PMDG 777 (→ [pmdg-777.md](docs/pmdg-777.md))

- Continuous knobs (brightness, temperature, EFIS baro/mins) cannot be controlled via the PMDG SDK event with a position parameter — do not add them to panels via that path (the cockpit-model-L:var-IS-the-input exception applies only to a few named knobs, e.g. shoulder heaters). → [pmdg-777.md](docs/pmdg-777.md)
- Fuel control levers are the inverted exception — CDA parameter 1=Cutoff/0=Run, not the usual on/off convention. → [pmdg-777.md](docs/pmdg-777.md)
- Ground power switches (`ELEC_ExtPwr`) are momentary — always send parameter 1 regardless of target state. → [pmdg-777.md](docs/pmdg-777.md)
- The PMDG 777 foot-heater combo and the Boris Audio Works hydraulic-pump-model combo drive the SAME physical knob — only expose the Boris combo; do not re-add a separate foot-heater control. → [pmdg-777.md](docs/pmdg-777.md)
- Crew seats are NOT adjustable on the PMDG 777 — no event/struct field/L:var/animation exists; don't go hunting for seat-motion vars. → [pmdg-777.md](docs/pmdg-777.md)
- CDU buttons must send parameter 1 (pressed) via CDA — parameter 0 ALSO registers as a press, not a release, so never rely on 0 to mean "no press." → [pmdg-777.md](docs/pmdg-777.md)
- The CDU array index convention is `0=Captain/1=F.O./2=Observer` for every crew-position array including CDU data areas — the dropdown is Left/Center/Right and needs the `DataCDUIndex` remap (`1→2, 2→1`); the event-prefix switch uses the raw dropdown index and must NOT be remapped. → [pmdg-777.md](docs/pmdg-777.md)
- `EVT_MCP_VS_SWITCH` is engage/disengage; `EVT_MCP_VS_FPA_SWITCH` is the VS↔FPA display toggle — the SDK names are misleading (confirmed only by live testing); don't swap them back based on the names. → [pmdg-777.md](docs/pmdg-777.md)
- PMDG-broadcast System Display reads must gate on `IPMDGDataManager.IsReady` — before the first CDA snapshot, `GetFieldValue` returns 0.0 for every field, which must render as `--`, never as "every door open"/"0 lb". → [pmdg-777.md](docs/pmdg-777.md)
- MainForm's PMDG panel-populate loop must only force-read `Type == PMDGVar` controls — force-reading a non-PMDG control (e.g. an LVar combo) via `GetFieldValue` returns the "unknown field" 0.0 sentinel and silently resets it on every panel re-show. → [pmdg-777.md](docs/pmdg-777.md)

### PMDG 737-800 NG3 (→ [pmdg-737.md](docs/pmdg-737.md))

- Two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (LVL_CHG/HDG_SEL/VOR_LOC), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test — see docs/pmdg-737.md for the full gotcha list. → [pmdg-737.md](docs/pmdg-737.md)

### PMDG EFB (Coherent debugger) (→ [pmdg-efb.md](docs/pmdg-efb.md))

- No injection, no HTTP bridge, no HTML patching, and no sim restart for the PMDG EFB — it is driven purely over the Coherent GT remote debugger; never reintroduce a Community-folder mod for it. → [pmdg-efb.md](docs/pmdg-efb.md)
- `collect()` must drop truly-anonymous buttons (no text/icon/title/aria/id/row-pair) — post-SimBrief-load leaflet overlay buttons would otherwise flood the readout as "(button)". → [pmdg-efb.md](docs/pmdg-efb.md)
- Single-character glyph text must be dropped BEFORE the same-row merge — the Take-Off page's per-character runway-designator spans would otherwise combine into garbage if merged first. → [pmdg-efb.md](docs/pmdg-efb.md)
- A field label must contain letters — a value-display label (e.g. a weather temperature "28") must never be mistaken for a field label. → [pmdg-efb.md](docs/pmdg-efb.md)
- `.opt-output`/`.groundops_ui_outputlabel` value cells must be excluded from the generic measurement-value path — a value already owned by a dedicated output pass must never ALSO emit as an orphan duplicate line. → [pmdg-efb.md](docs/pmdg-efb.md)
- Alert/confirmation cards must be captured as ONE assertive-announced item, and the card's own heading/message subtree must be skipped in the main collect loop — a dynamically-injected alert must be app-side `AnnounceImmediate`d since WebView2 aria-live isn't reliable for it. → [pmdg-efb.md](docs/pmdg-efb.md)
- Unit toggles must read the LIVE `el.checked` state, never the lagging `Settings` object or a `::after` CSS caption — the current build's toggles are textless and `Settings` only commits on Save Preferences. → [pmdg-efb.md](docs/pmdg-efb.md)
- `window.Settings` is ALWAYS false on the live PMDG view (never mirrored to `window`) — any code reading it must fall back to the bare global, never a `window.Settings`-only check (that silently no-ops live). → [pmdg-efb.md](docs/pmdg-efb.md)
- Never query the `ils` table by `ident` alone — always scope by airport (+runway); anything unscoped must go through `GetILSForRunway` so it is spatially validated (498 idents are shared across airports and 213 fs2024 rows are orphaned join columns). → [pmdg-efb.md](docs/pmdg-efb.md)

### FlyByWire A380X (→ [a380x.md](docs/a380x.md))

- Never assume a Coherent UI element needs the KCCU cursor or a DOM click without checking what the real cockpit input drives underneath — the MFD uses `InputField` keypress, the ECL is driven by ECP L:var pulses, neither needs the KCCU cursor. → [a380x.md](docs/a380x.md)
- The annunciator/integral LT-TEST knob is render-only — never synthesize a spoken narration of what the bulbs would show; only announce the knob's own position and let real per-system fault lights announce genuine faults. → [a380x.md](docs/a380x.md)
- Every A380 panel control must render as a COMBO, not a hardware button, EXCEPT true one-shot momentary actions (ECAM-CP keys, chrono, calls, ATC ack) — a control that must show ongoing state must never be a plain button (the reverted APU-start-PB and Fire-Test cases). → [a380x.md](docs/a380x.md)
- Every Coherent client's `EnsureConnected` must re-install the agent on a still-open socket instead of reconnecting, and must Abort+Dispose any existing socket BEFORE `ConnectAsync` — skipping either orphans a healthy old socket and permanently loses the page for the process. → [a380x.md](docs/a380x.md)
- Any public on-demand scrape method competing with a background `RunLoop` must be serialized by its own connect-lock (`_connectLock`) — the existing `_sendLock` only covers `SendAsync`, not connection setup, and doesn't close the race. → [a380x.md](docs/a380x.md)
- Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists — the SD Upper-E/WD fallback must go through the one always-on monitor socket. → [a380x.md](docs/a380x.md)
- The ECL must share the EWD monitor's existing Coherent socket, never open its own — a second inspector connection to the same page is rejected. → [a380x.md](docs/a380x.md)
- Every form marshaling a background bridge-push to the UI thread must wrap `BeginInvoke` in try/catch(InvalidOperationException) (`SafeBeginInvoke`) — an `IsHandleCreated` check alone races a concurrent handle-destroy on aircraft swap/window close. → [a380x.md](docs/a380x.md)
- Never re-add the OANS forced-render/zoom/visibility DOM scrape path — it exhausted host commit memory and froze the whole machine; OANS must stay DATA-ONLY (JS instance reads + `btvUtils` method calls, never a canvas draw/scrape). → [a380x.md](docs/a380x.md)
- Never loop `armExit`/`armRunway` over the full OANS exit list — each accepted arm triggers a canvas redraw, and a full sweep exhausts memory; only arm the 1-2 same-named candidate features. → [a380x.md](docs/a380x.md)
- OANS `loadAirportMap(icao)` must be called DIRECTLY, never only via the `oans_display_airport` bus event — under perf-hide mode the bus handler only sets the ICAO and skips the actual load. → [a380x.md](docs/a380x.md)
- Never implement the FBW flyPad pushback controls — Robin's team uses GSX for pushback; this is a permanent decision. → [a380x.md](docs/a380x.md)
- Never treat the Surveillance pedestal panel as a working feature — FBW's own docs say it's not yet implemented; transponder AUTO-mode and squawk are the only real controls, reachable via the MFD SURV page. → [a380x.md](docs/a380x.md)
- Before assuming an announced ARINC var can't be injection-tested, check whether it has a per-frame writer (Rust systems/rendering instrument) — only vars the writer leaves alone are injectable; real verification of writer-owned vars needs a live scenario. → [a380x.md](docs/a380x.md)
- FBW named AC/DC buses and batteries publish as `A32NX_ELEC_{rawBusName}_BUS_IS_POWERED` with the raw bus id — never invent a descriptive name (e.g. `_AC_EHA_...`); confirm the id in the Rust source. → [a380x.md](docs/a380x.md)
- Every A380 RMP calc-path write must be made unique per call with a `{seq} 0 *` prefix — MobiFlight's command channel coalesces two consecutive IDENTICAL calc strings, silently dropping a repeated-digit keystroke or a double-press of the same LSK/ADK. → [a380x.md](docs/a380x.md)
- Every DCDU H-event fire must be similarly sequence-uniquified — the WILCO→SEND two-step press on the same slot would otherwise silently drop the second press to the coalescing bug. → [a380x.md](docs/a380x.md)
- Seat-motor writes must use a per-frame UNIQUE calc string (`<seq> 0 *` prefix) — identical strings are registered+fired only once by MobiFlight, which is why a naive write only ticked the seat motor once instead of sustaining it. → [a380x.md](docs/a380x.md)
- Never poll a combo's backing var and snap its displayed value on a periodic re-read for a synthetic motor var that idles at 0 — that causes a spurious restart/stop/announce loop; use a `RenderAsButton` toggle instead so state only changes on the click edge. → [a380x.md](docs/a380x.md)
- A380 FCU baro polarity is PUSH=STD/PULL=QNH — the OPPOSITE of the A32NX's PULL=STD/PUSH=QNH; never harmonize the two conventions. → [a380x.md](docs/a380x.md)
- KCCU H-events do not reach the MFD via `Coherent.trigger`/`SimVar.SetSimVarValue` from the external debugger — they must be published on the MFD's own msfs-sdk EventBus (`bus.pub('hEvent', ...)`); required for F-PLN scrolling and any KCCU-driven navigation. → [a380x.md](docs/a380x.md)
- `fireKey` must fire each KCCU H-event ONCE, never twice — firing it via both `Coherent.trigger` AND `SimVar.SetSimVarValue` caused double/erratic F-PLN paging. → [a380x.md](docs/a380x.md)
- Never widen the flyPad Dashboard's column-first read order to other EFB pages without evidence a specific page is jumbled — a blind global split would break single-column pages. → [a380x.md](docs/a380x.md)
- Never key a settings-page unit-toggle detection on a universal "checked=metric" assumption — direction differs per toggle id; use the per-id `UNIT_PAIRS` map. → [a380x.md](docs/a380x.md)
- `buildSettingsLines` must return null (defer to the generic pass) when it finds no recognizable control in a region, rather than rendering an owned-but-blank page — this is the safety net for layouts the builder doesn't recognize. → [a380x.md](docs/a380x.md)
- Door-tile precise names must never trust the FBW enum DIGIT alone — it can be wrong (index 9 "Main4Right" is actually Main Door 5 Right); always parse the handler comment's enum NAME, falling back to column-based Left/Right only when the enum can't be parsed. → [a380x.md](docs/a380x.md)
- `A.DOOR_NAMES` (flyPad agent) must be kept in sync with each aircraft def's `_doorDefs` table — the flyPad label and the spoken door name must agree. → [a380x.md](docs/a380x.md)
- The metric-weight toggle must never write `SetStoredData` directly and expect it to propagate — the ONLY reliable aircraft-side write is the real EFB "US Units" toggle; MSFSBA's Units button is a LOCAL read-out preference only, kept separate from the last-known aircraft value so the button and the live monitor don't fight. → [a380x.md](docs/a380x.md)
- Every FBW unit/feature with an observable effect must be wired into MSFSBA's OWN read-outs — MSFSBA bypasses the cockpit displays, so a display-only conversion never reaches the blind pilot unless MSFSBA applies it itself. → [a380x.md](docs/a380x.md)
- Never regress the A380 ROW/ROP and BTV rollout distance call-outs — they're safety call-outs for a blind pilot during landing rollout; they can only be verified in a real scenario since their writers are per-frame and injection is unreliable. → [a380x.md](docs/a380x.md)
- The `_baroInHgL/R` unit tracking must key off `XMLVAR_Baro_Selector_HPA_{1,2}`, never `A32NX_FCU_EFIS_*_BARO_IS_INHG` — the IS_INHG var is stuck/dead on this build. → [a380x.md](docs/a380x.md)
- Never re-add the RMP "Radios" panel using stock COM standby-set/swap events on the A380 — the FBW A380 ignores them entirely; all tuning is RMP-only. → [a380x.md](docs/a380x.md)
- The RMP VHF standby readback must be computed from the TYPED digit entry, never from a single polled scrape — the FBW autocomplete settles over several frames after the last keystroke. → [a380x.md](docs/a380x.md)
- RMP row selection must be authoritative from the manual `Ctrl+1/2/3` selection, synced from the scrape only on the FIRST poll — a per-poll sync races the LSK-select registration lag and resets a fresh selection back to row 0. → [a380x.md](docs/a380x.md)
- RMP squawk must always be set via the stock `XPNDR_SET` event, independent of which RMP page is displayed — the page-switch+keypad+auto-validate chain was unreliable to drive externally. → [a380x.md](docs/a380x.md)
- The RMP announce (`Apply`) must marshal to the UI thread — `_announcer.Announce` silently fails off the UI thread while the dedup key still updates, masking future announcements. → [a380x.md](docs/a380x.md)
- Every A380 form holding a Coherent client or the def instance must be disposed in `SwitchAircraft`'s cleanup — a hide-on-close form (RMP) needs teardown in `Dispose(bool)`, since `Close()` is cancelled by the hide guard and `Form.Dispose()` skips `OnFormClosed`. → [a380x.md](docs/a380x.md)
- Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown) — otherwise seat/slider motor timers keep writing L:vars into the new aircraft for seconds after the swap. → [a380x.md](docs/a380x.md)
- The A380 EWD scrape must silently baseline on first connect — only failures appearing AFTER connect should announce, matching every other MSFSBA monitor (avoids re-reading the whole screen on reconnect). → [a380x.md](docs/a380x.md)
- Never leave only the unindexed TCAS RA-guidance vars registered — the fly-to/avoid V/S bands exist ONLY as the `:1`/`:2` indexed L:vars; the unindexed names are never written by FBW. → [a380x.md](docs/a380x.md)
- The TCAS RA-guidance compose must be DEFERRED (~800ms), never synchronous off the state edge — FBW resets the V/S band vars only in TCAS STBY (not on clear-of-conflict), so a synchronous compose at RA-onset can speak the previous RA's stale sense. → [a380x.md](docs/a380x.md)
- The TCAS `VSPEED_GREEN/RED:1/:2` + `RA_RATE_TO_MAINTAIN` L:vars MUST be registered `Units="number"`, NEVER a velocity unit — FBW writes them unitless-but-already-in-fpm, so a "feet per minute" data-def read multiplies by 196.85 (assumes native m/s) and speaks garbage RA numbers (1500 → "295276"); the fpm label is hardcoded in the compose/display, not derived from Units. → [troubleshooting-playbook.md](docs/troubleshooting-playbook.md)
- Any ANNOUNCED enum var whose raw value is ARINC-large (≥2^32) must be decoded via `Arinc429Word` before comparing to its `ValueDescriptions` — otherwise the generic announcer speaks the raw multi-billion word. → [a380x.md](docs/a380x.md)
- Annunciators must be stripped from panel DISPLAY variable sets — a pilot navigates a panel to operate controls, not to scan "X Fault: Normal" rows; numeric/analog and 3+-state status fields are kept. → [a380x.md](docs/a380x.md)
- When a status has BOTH a PB-light L:var and an ECAM memo for the same condition, the L:var must be `ReadEnumQuiet` and the memo must be the single call-out — never double-announce the same condition from two sources. → [a380x.md](docs/a380x.md)
- Master Warning/Caution acknowledge must pulse the EXACT L:var name the glareshield XML uses — the A380's is misspelled `MASTERAWARN` (extra A); using the plausible-but-wrong spelling is a silent no-op that never clears the aural. → [a380x.md](docs/a380x.md)
- The generic ARINC429 auto-decoder hook must run in `MainForm.UpdateDisplayText` AFTER `TryGetDisplayOverride` and only for vars `ProcessSimVarUpdate` didn't already handle — ad-hoc per-var decoders (baro, minimums, rudder trim) must win, and a var must never be decoded twice. → [a380x.md](docs/a380x.md)
- #103 breakthrough: A380 overhead PBs (PACK/HOT AIR/ENGINE BLEED/CABIN+AIR-EXTRACT FANS/HYD engine+electric pumps/ELEC bus-tie/galley/HYD PTU/emergency-exit sign) ARE all settable via the calculator path — the earlier "computed outputs that revert" verdict was WRONG (an artifact of testing with the unreliable data-def `set_lvar` write); `XMLVAR_` sign combos now route through the OVHD calc catch-all too. Always test an FBW L:var write with the calculator path, never `set_lvar`. → [a380x.md](docs/a380x.md)
- Multi-position cockpit switches stay MULTI-position combos, never split into easier On/Off controls: the Nose light is one 3-position T.O./Taxi/Off combo (state = `LIGHTING_LANDING_1`, actuated by indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET`), and Seat Belts is 3-position ON/AUTO/OFF (`XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position` — On/Off drive the stock `CABIN SEATBELTS ALERT SWITCH` via its toggle, AUTO is left to the FBW 500 ms Update). A blind pilot gets the same access a sighted pilot has. → [a380x.md](docs/a380x.md)
- Wing anti-ice must write the var the real cockpit button writes (`A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION`), NOT the stock `STRUCTURAL DEICE SWITCH` the cockpit never touches (they diverged). The A380 wing-anti-ice PNEUMATIC isn't modelled in the FBW build — no input drives `_SYSTEM_ON` — so the switch is faithful but the flow won't engage yet; engine anti-ice (`ANTI_ICE_SET_ENGn`) and probe heat (auto) both work. → [a380x.md](docs/a380x.md)
- SD-page row var registration classifies by FBW PREFIX, not by space/colon: a colon-INDEXED FBW L:var (`A32NX_FUEL_USED:n`) is a real L:var, not a stock SimVar — the old "any colon = SimVar" rule registered it as a nonexistent stock SimVar that read 0, blanking the SD Fuel/Cruise Engine fuel-used rows. Stock names (no FBW prefix) still register as SimVar. → [a380x.md](docs/a380x.md)
- Frequency readouts need explicit formatting: the RMP panel's stock `COM ACTIVE/STANDBY FREQUENCY:n` (MHz) needs a "0.000 MHz" display override or a whole-MHz freq drops its fraction to a bare "137"; ND ADF/VOR need kHz/MHz unit labels (an ADF "890" is a correct 890 kHz, just ambiguous). → [a380x.md](docs/a380x.md)
- Flight Director control is `K:TOGGLE_FLIGHT_DIRECTOR` with the SIDE as the param (1=Capt/2=F/O) + stock `AUTOPILOT FLIGHT DIRECTOR ACTIVE:n` state — `A32NX_FCU_EFIS_L/R_FD_ACTIVE` is DEAD (reads 0 while the FD is on); don't switch the combos back to it. → [a380x.md](docs/a380x.md)
- Approach minimums read the PLAIN-feet L:vars the MFD PERF page writes (`AIRLINER_MINIMUM_DESCENT_ALTITUDE` = baro MDA, `AIRLINER_DECISION_HEIGHT` = radio DH; unset MDA≤0 / DH<0), NOT the ARINC429 `A32NX_FM1/FM2_*` words — those are NCD (read 2³² → "Not set") until the FMC decides the aircraft is in approach range, so a set minimum showed "Not set" at cruise. → [a380x.md](docs/a380x.md)
- "Passengers on Board" sums the per-station `A32NX_PAX_<st>_DESIRED` seat bitmasks (the planned/target load the flyPad headline + GSX `FSDT_GSX_NUMPASSENGERS` report), NOT the boarded `A32NX_PAX_<st>` set — the boarded bitmask lags and, under GSX-driven boarding, settles below target and stays there (popcount is exact, max 50 seats/station < 2⁵³; it's the wrong quantity, not a math bug). → [a380x.md](docs/a380x.md)
- Takeoff trim reads the ARINC429 word `A32NX_FM1_TO_PITCH_TRIM` (what the FMC writes + the FWS reads), NOT the dead bare `A32NX_TO_PITCH_TRIM` (reads 0 → "Not computed"), and it's a PERCENT (takeoff CG %MAC — the PERF "THS FOR" field), NOT degrees (FWS compares it to the CG percent; THS degrees only span ~−0.2…+5.8). FBW packs even BNR words as float32-of-value + ssm<<32, so `Arinc429Word.Value` is correct. → [a380x.md](docs/a380x.md)
- Wipers are 3-position OFF/SLOW/FAST per side (Capt = circuit 141, F/O = 143, independent): OFF = `CIRCUIT SWITCH ON` off; SLOW = switch on + `CIRCUIT POWER SETTING` 75%; FAST = switch on + power 100%. The power setting PERSISTS at its default (100%) while the switch is off, so the position needs BOTH vars (switch-first — power alone would misread a cold-start OFF as FAST); expose as a synthetic 3-position combo (not On/Off). → [a380x.md](docs/a380x.md)

### flyPad EFB (shared A320/A380) (→ [flypad.md](docs/flypad.md))

- A control with no native `<input>`/click handler that can only be driven by an L:var write must NOT be wired through the injected agent — `SimVar.SetSimVarValue` silently no-ops from inside an injected agent function (Coherent restriction); expose it as an app-side panel control instead. → [flypad.md](docs/flypad.md)
- Never render the flyPad scrape as native WinForms controls — only a WebView2 HTML document gives NVDA full browse mode; headings and static text are otherwise unreachable. → [flypad.md](docs/flypad.md)
- Never wipe and rebuild the flyPad WebView2 DOM every poll (`innerHTML` replace) — that steals screen-reader focus; use the keyed in-place reconcile only. → [flypad.md](docs/flypad.md)
- The flyPad reconcile key must be element CONTENT, not the scrape idx — idx is unstable across pages/sub-tabs and re-stamped from 1 every scrape, causing cross-tab node collisions. → [flypad.md](docs/flypad.md)
- The reconcile key must strip dynamic state suffixes (`(active)`/`(called)`/`(selected)`) — keying on the full label destroys+rebuilds the node the instant its state changes, throwing NVDA's focus off the control the user just activated. → [flypad.md](docs/flypad.md)
- Wheel Chocks/Safety Cones are READ-ONLY status (no click handler in FBW source) — never make them clickable; there is no setter, they only turn green when ground equipment is actually placed. → [flypad.md](docs/flypad.md)
- The Ground Payload/Fuel builders must own (suppress) every non-actionable section child, the CG-chart card, and all tooltip nodes — a flat positional scrape otherwise flattens the W&B table/fuel schematic into unreadable fragments. → [flypad.md](docs/flypad.md)
- `setValue` on a flyPad SimpleInput must commit via a synthetic Enter keydown/keyup + blur — FBW commits on Enter/blur, not on React onChange; a plain value-set silently fails to reach the sim. → [flypad.md](docs/flypad.md)
- Suppress the "Fill … from SimBrief" controls' caption tooltip and icon button (never the value input) on the Ground Payload/Fuel pages — the user imports via the Dashboard instead. → [flypad.md](docs/flypad.md)

### HorizonSim 787 (→ [hs787.md](docs/hs787.md))

- The HS787 EFB is intentionally REMOVED — its perf-page inputs cannot be driven externally (a custom MSFS keyboard mechanism rejects both programmatic value-set and synthetic keyboard events); never re-attempt driving it without a different input mechanism. → [hs787.md](docs/hs787.md)
- The realistic "TIME TO ALIGN" IRS state must be read from the ND/PFD `.time-to-align` DOM element, never inferred from `WT_IRS_POS_SET_N` alone — that L:var means "position accepted," not "alignment complete." → [hs787.md](docs/hs787.md)
- The WT787 `boeing-mfd-button` component reacts to MOUSE events — `clickElement` must dispatch the full pointerdown/mousedown/pointerup/mouseup/click sequence, or CDU page-key navigation silently does nothing (LSKs alone still work, masking the bug). → [hs787.md](docs/hs787.md)
- Each `Resources\coherent-hs787-*-agent.js` needs its OWN explicit `<None Update=...>` csproj entry — the build does NOT wildcard-copy `Resources\`. → [hs787.md](docs/hs787.md)
- The Ctrl+M monitor-manager disabled-var gate must WRAP `ProcessSimVarUpdate` in `announcer.Suppressed` for the HS787, never rely on the generic post-return gate alone — the HS787 announces ~100 of its vars from INSIDE `ProcessSimVarUpdate`, which returns true and skips the generic gate entirely; apply the same wrap to any future aircraft with the same self-announcing pattern. → [hs787.md](docs/hs787.md)
- A display-only Flight Data sub-panel must have a (even empty) `BuildPanelControls` entry, or MainForm's panel-build logic early-returns and the panel renders nothing. → [hs787.md](docs/hs787.md)
- Before concluding an HS787 readout is "dead," confirm the aircraft isn't in a genuine failure state — several "dead" readouts were actually correct values during a fuel-exhaustion failure. → [hs787.md](docs/hs787.md)
- APU generator control must use the un-indexed `APU_GENERATOR_SWITCH_SET` (ganged both gens) — the per-index `APU_GEN1/2_SWITCH_SET` events are no-ops. → [hs787.md](docs/hs787.md)
- Autobrake control must step `INCREASE/DECREASE_AUTOBRAKE_CONTROL` toward the target and read `AUTO BRAKE SWITCH CB` — `SET_AUTOBRAKE_CONTROL` is a no-op and `AUTOBRAKE CONTROL SWITCH POSITION` is stuck at 0. → [hs787.md](docs/hs787.md)
- Strobe light control must use `STROBES_SET`, not `STROBE_LIGHTS_SET` (a no-op on the Asobo-template lighting). → [hs787.md](docs/hs787.md)
- The Community-folder HS787 bridge (HTML patching + injected JS) is fully removed on both sims — never reinstate the installer; the Coherent transport needs no Community mod and no sim restart. → [hs787.md](docs/hs787.md)

### FlyByWire A32NX / Fenix (→ [a32nx.md](docs/a32nx.md))

- Every Fenix panel pushbutton must go through a full PRESS-RELEASE pulse (0→1→0), never press-only — a press-only pulse leaves the button held down for the whole session (root cause of the stuck TO CONFIG / stuck ECAM STATUS bug that re-fired the takeoff-config test after touchdown). → [a32nx.md](docs/a32nx.md)
- Do not revert `ExecuteButtonTransition` to the press-only form — the release is safe/correct for all ~150 Fenix buttons (systems latch on the 0→1 rising edge into a separate indicator var; the release never loses state). → [a32nx.md](docs/a32nx.md)
- The A32NX Flight Director control vars are `A32NX_FCU_EFIS_{L,R}_FD_ACTIVE`, NOT `TOGGLE_FLIGHT_DIRECTOR`/`A320_Neo_FCU_FD_n_PUSH`/`A380X_EFIS_L_FD_BUTTON_IS_ON` — those alternates genuinely fail; don't re-test the wrong vars and re-conclude "uncontrollable." → [a32nx.md](docs/a32nx.md)
- Never write A32NX overhead L:vars via the unreliable data-def `SetLVar` path when testing — the earlier "PACK/HOT AIR/BLEED/etc. are computed outputs that revert" verdict was an artifact of testing with the wrong write path; the calculator path sticks for all of them. → [a32nx.md](docs/a32nx.md)
- The RMP audio/volume `A32NX_RMP_{L,R}_VHF{n}_VOLUME` L:vars do not exist in dev FBW — do not re-add the ACP volume combos; the physical ACP is unmodeled. → [a32nx.md](docs/a32nx.md)
- The A32NX predicted-takeoff-pitch-trim sign is INVERTED vs the A380 (`-ths`, negative = nose up) — never copy a sign convention between the two FMSes without checking the writer. → [a32nx.md](docs/a32nx.md)
- FUEL MODE SEL junction options are 1-based (`t+1`, not `t`) — sending the raw toggle value selects the wrong option despite the L:var/light appearing to follow. → [a32nx.md](docs/a32nx.md)
- The evacuation-horn shut-off L:var write is ONE-WAY — never pulse it back to 0, that resumes the horn. → [a32nx.md](docs/a32nx.md)
- Momentary FBW L:var button pulses must be sent as TWO SEPARATE calc calls, never a single same-frame `1 (>L:X) 0 (>L:X)` string — the Rust sampler doesn't see a same-tick pulse. → [a32nx.md](docs/a32nx.md)
- The A32NX DCDU display must be a ListBox, not a multiline TextBox — a right-aligned key label read on a separate braille line from its leading key number in a TextBox. → [a32nx.md](docs/a32nx.md)
- DCDU soft-key slot must be mapped by POSITION (a Y-threshold), never by simple L/R order — an empty-state key can be alone on a side yet still live on the second slot. → [a32nx.md](docs/a32nx.md)
- DCDU page-scroll direction must be DOWN=forward everywhere — the answer keys stay inactive until the pilot has paged to the end of a multi-page uplink, so an inverted direction silently blocks answering. → [a32nx.md](docs/a32nx.md)
- Per-DCDU-key ACTIVE flags must be checked before firing — an inactive key must say "not available yet," never falsely confirm a press. → [a32nx.md](docs/a32nx.md)
- A32NX FCU baro STD/QNH polarity is PULL=STD/PUSH=QNH — the opposite of the A380's PUSH=STD/PULL=QNH; never harmonize them. → [a32nx.md](docs/a32nx.md)
- The A32NX autobrake set must use the MobiFlight calculator path, never `SetLVar` (data-def write is unreliable for this FBW L:var). → [a32nx.md](docs/a32nx.md)
- Never re-fold the A380's metric-ALTITUDE (MTRS) feature into the A32NX — the real A320 has no MTRS button (A330+/A380 only). → [a32nx.md](docs/a32nx.md)
- A32NX approach minimums read the plain-feet `AIRLINER_*` L:vars, never the `A32NX_FM1_*` ARINC words — those are NCD until near-destination, so a gate-entered MDA/DH read "Not set" (same bug + fix as the A380). → [a32nx.md](docs/a32nx.md)
- A32NX wing anti-ice writes `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION` (the Rust input), never `_SYSTEM_SELECTED` — that is a Rust per-frame OUTPUT and any write reverts (<2 s, any phase; the old "holds in flight" note was a mis-test). → [a32nx.md](docs/a32nx.md)
- A32NX nose/landing lights are driven by the indexed stock events in the FBW template's verbatim RPN form `<value> <index> r (>K:2:LANDING_LIGHTS_SET/TAXI_LIGHTS_SET)` — the `LIGHTING_LANDING_x` L:vars drive nothing (nose holds-but-dead, wing template-owned/reverts). (RPN `r` swaps the top two stack entries, so this form is stack-equivalent to index-first no-`r`; the old "index-first is a NO-OP" claim was a test confound — keep the template-verbatim form, don't rewrite others without live re-verification.) → [a32nx.md](docs/a32nx.md)
- A32NX wipers are circuits 77 (Capt) / 80 (F/O) — not the A380's 141/143 — and the live OFF/SLOW/FAST position needs BOTH circuit switch AND power (power rests at 100% while off; the switch bool alone can't read back FAST); `XMLVAR_A320_WiperSwitch_*` does not exist in FBW. → [a32nx.md](docs/a32nx.md)
- A32NX seat belts is genuinely 2-position ON/OFF in the FBW model (no AUTO — unlike the A380); don't "fix" it to 3-position. → [a32nx.md](docs/a32nx.md)
- A32NX "Passengers on Board" sums the `A32NX_PAX_{A..D}_DESIRED` planned bitmasks, not the lagging boarded set (same lesson as the A380 pax fix). → [a32nx.md](docs/a32nx.md)

### Gemini AI (→ [gemini.md](docs/gemini.md))

- Never reinstate a silent multi-model fallback for Gemini calls — the model used must be exactly `UserSettings.GeminiModel`; a silent fallback hides which model produced a response (explicitly rejected by the user). → [gemini.md](docs/gemini.md)
- Do NOT send `thinkingConfig`/`thinkingBudget` to Gemini — `thinkingBudget` is deprecated/invalid on Gemini 3.x models (`thinking_level` is used instead) and can error if both are set. → [gemini.md](docs/gemini.md)
- The Gemini HTTP timeout catch must NOT gate on `ex.CancellationToken.IsCancellationRequested` (false on a modern .NET HttpClient timeout) — catch `TaskCanceledException` as the timeout instead. → [gemini.md](docs/gemini.md)

## Quick Reference

### Adding Panel Control
1. Add to aircraft's `GetVariables()` with `UpdateFrequency.OnRequest`
2. Add variable key to `BuildPanelControls()` under appropriate panel
3. Test - automatic registration and UI generation

### Adding Background Monitoring
1. Add to `GetVariables()` with `UpdateFrequency.Continuous` + `IsAnnounced = true`
2. Do NOT add to `BuildPanelControls()` - batched monitoring is automatic
3. Change detection and announcements are automatic (supports 1000 variables)

### Adding New Aircraft
1. Create class inheriting `BaseAircraftDefinition`
2. Override: `GetVariables()`, `GetPanelStructure()`, `BuildPanelControls()`
3. Add menu item in `MainForm.Designer.cs` + click handler
4. Add to `LoadAircraftFromCode()` switch statement
5. Use `FlyByWireA320Definition.cs` as template

### Variable Types
- **K:EVENT** - Standard MSFS events (via SimConnect TransmitClientEvent)
- **L:VARIABLE** - Local variables (reading aircraft state)
- **H:EVENT** - Hardware events (via MobiFlight WASM module)
- **PMDGVar** - PMDG SDK variables (read via Client Data Area broadcast)

### `SimConnectManager.SetLVar` — GLOBAL MobiFlight calc-path routing (2026-06)

Every L:var write is routed through the MobiFlight calculator path when connected (gated on `CalcPathVerified`), never the native data-def write. Full routing rules, the H:/dotted event queue, and the RPN invariant-formatting rule: [docs/architecture.md](docs/architecture.md).

### Fenix A320 cockpit controls — new "Cockpit" panel section (2026-06)

Details: [docs/a32nx.md](docs/a32nx.md).

### Fenix monitor manager (Ctrl+M) — now DYNAMIC + clock counters default-off (2026-06)

Details: [docs/a32nx.md](docs/a32nx.md).

### Fenix momentary buttons are a full PRESS-RELEASE, not press-only (2026-07 — stuck-button fix)

Details: [docs/a32nx.md](docs/a32nx.md).

### PMDG 777 Specific Patterns

Details: [docs/pmdg-777.md](docs/pmdg-777.md).

### PMDG 737-800 NG3 Specific Patterns

Details: [docs/pmdg-737.md](docs/pmdg-737.md). Key gotchas: two CDUs (no observer), no FPA mode, annunciator names differ from 777 (LVL_CHG / HDG_SEL / VOR_LOC), DU selectors have "reverse sequence for FO", fire handles need an active fire to test, the 737 EFB has full parity with the 777 (Dashboard / Preferences / Navdata / Performance / Ground Ops / W&B / Manuals) via the shared `FbwEfbForm` over the Coherent debugger (`CoherentPmdgEfbClient` + `coherent-pmdg-efb-agent.js`), opened with Shift+T — the EFB app is byte-identical across all four 737 variants and the 777, so one shared in-page agent serves them all (NO Community-folder package; the retired `zzz-pmdg-efb-accessibility` is auto-removed by `LegacyEfbBridgeCleanup`).

### PMDG EFB (Coherent debugger)

Details: [docs/pmdg-efb.md](docs/pmdg-efb.md).

### FlyByWire A380X Specific Patterns

Details + all A380X invariants: [docs/a380x.md](docs/a380x.md).

## VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK — UNIVERSAL (read this before saying "X doesn't work")

Details: [docs/troubleshooting-playbook.md](docs/troubleshooting-playbook.md).

### HorizonSim 787-9 CDU (Coherent debugger — the HTTP bridge was RETIRED 2026-06-19; the EFB was REMOVED 2026-06)

Details: [docs/hs787.md](docs/hs787.md).

### FlyByWire Accessible flyPad EFB (A320 + A380 — ONE shared flyPad)

Details: [docs/flypad.md](docs/flypad.md).

### FlyByWire A32NX Panel Parity (Phase 3)

Details: [docs/a32nx.md](docs/a32nx.md).

### FlyByWire A32NX Accessible MCDU

Details: [docs/a32nx.md](docs/a32nx.md).

## Detailed Documentation

**Claude: Read these docs only when the task specifically requires them.**

**When to read detailed docs:**
- **Debugging the A380/A32NX live (Coherent debugger, probes, scrapers, crash diagnosis)** → [Developer Tooling Guide](docs/tooling.md)
- **Adding complex features or workflows** → [Adding Features](docs/adding-features.md), [Quick Reference](docs/QUICK-REFERENCE.md)
- **Implementing new aircraft** → [Architecture](docs/architecture.md), [Adding Features](docs/adding-features.md)
- **Working with FCU/MCP/display systems, the SimConnect data-def ceiling, or MobiFlight calc-path routing** → [Architecture](docs/architecture.md)
- **Adding or modifying hotkeys** → [Hotkey System](docs/hotkey-system.md)
- **Fenix rotary encoders (RMP, FCU)** → [Fenix Increment/Decrement](docs/fenix-increment-decrement.md)
- **Tuning visual guidance PID controller** → [Visual Guidance](docs/visual-guidance.md)
- **Working on taxi guidance (graph, router, tone, form)** → [Taxi Guidance](docs/taxi-guidance.md)
- **Working on GSX gate selection, docking guidance, or the metres/feet distance toggle** → [GSX Integration](docs/gsx.md)
- **Working on ActiveSky integration, the weather radar, METAR readouts, or weather auto-announcements** → [Weather](docs/weather.md)
- **Working on PMDG 737-800 panels, CDU, NG3 data struct** → [PMDG 737-800](docs/pmdg-737.md)
- **Working on the PMDG 777 (CDA switches, CDU array indexing, System Display synoptic pages)** → [PMDG 777](docs/pmdg-777.md)
- **Working on the PMDG or HS787 EFB (Coherent debugger scrape agent, shared `FbwEfbForm`)** → [PMDG EFB](docs/pmdg-efb.md)
- **Working on the FlyByWire A380X (MFD/MCDU, OANS/BTV, RMP, ECAM/EWD, checklists, flyPad specifics)** → [FlyByWire A380X](docs/a380x.md)
- **Working on the FlyByWire A32NX or Fenix A320 (panel parity, MCDU, DCDU, cockpit controls, monitor manager)** → [FlyByWire A32NX / Fenix](docs/a32nx.md)
- **Working on the shared flyPad EFB (A320 + A380 ground services, settings, dashboard reading order)** → [flyPad EFB](docs/flypad.md)
- **Working on the HorizonSim 787-9 (CDU/IRS/EICAS over the Coherent debugger)** → [HorizonSim 787](docs/hs787.md)
- **A control "doesn't work" and you're about to declare it broken, computed-output, or unsettable** → [Troubleshooting Playbook](docs/troubleshooting-playbook.md) (read this FIRST — most "broken" verdicts turn out wrong)
- **Working on Gemini AI display reading, scene description, or route briefing** → [Gemini AI](docs/gemini.md)
- **Understanding variable patterns** → [Variable System](docs/variable-system.md)
- **API reference** → [Aircraft Definitions](docs/aircraft-definitions.md)
- **Dependencies and key files** → [Development](docs/development.md)

**Available documentation:**
- **[Quick Reference](docs/QUICK-REFERENCE.md)** - Common patterns and workflows (read first for most tasks)
- **[Architecture](docs/architecture.md)** - Core components, multi-aircraft system, FCU architecture, SimConnect data-def ceiling, MobiFlight calc-path routing
- **[Adding Features](docs/adding-features.md)** - Step-by-step workflows for common development tasks
- **[Variable System](docs/variable-system.md)** - Three patterns for managing variables (Panel, Monitoring, Hotkey)
- **[Fenix Increment/Decrement](docs/fenix-increment-decrement.md)** - Counter-based pattern for Fenix rotary encoders
- **[Visual Guidance](docs/visual-guidance.md)** - PID controller tuning and ground track monitoring
- **[Taxi Guidance](docs/taxi-guidance.md)** - Turn-by-turn taxi assistance, steering tone, ATC-constrained routing
- **[GSX Integration](docs/gsx.md)** - GSX gate selection, docking guidance, distance units; developer internals (gate DFS, docking geometry) under "Developer internals"
- **[Weather](docs/weather.md)** - ActiveSky opt-in gate, SimConnect fallbacks, per-engine wind truth, precip source precedence, decoded-weather monitor lifecycle
- **[PMDG 737-800](docs/pmdg-737.md)** - NG3 SDK patterns, two-CDU convention, FIRE_HandlePos ordering, EFB gating
- **[PMDG 777](docs/pmdg-777.md)** - CDA switch patterns, fuel-lever/ground-power exceptions, CDU crew-index convention, System Display
- **[PMDG EFB](docs/pmdg-efb.md)** - Coherent-debugger EFB scrape agent shared by the PMDG 737/777 (and HS787's CDU transport pattern)
- **[FlyByWire A380X](docs/a380x.md)** - MCDU/MFD, OANS/BTV, RMP, ECAM/EWD, checklists, flyPad-specific A380 notes, and all A380X invariants
- **[FlyByWire A32NX / Fenix](docs/a32nx.md)** - A32NX panel parity, MCDU, DCDU, Fenix cockpit controls, Fenix monitor manager, momentary-button press-release fix
- **[flyPad EFB](docs/flypad.md)** - Shared FlyByWire A320/A380 flyPad accessibility architecture (WebView2 shell, ground services, settings, dashboard)
- **[HorizonSim 787](docs/hs787.md)** - 787-9 CDU/IRS/EICAS over the Coherent debugger, community-folder-bridge retirement
- **[Troubleshooting Playbook](docs/troubleshooting-playbook.md)** - Universal variable/control troubleshooting method — read before declaring any control "broken"
- **[Gemini AI](docs/gemini.md)** - Model selection, retry/backoff, and API-parameter gotchas for the AI display-reading and route-briefing features
- **[Aircraft Definitions](docs/aircraft-definitions.md)** - Multi-aircraft dictionary system API reference
- **[Hotkey System](docs/hotkey-system.md)** - Dual-mode hotkeys and multi-aircraft routing
- **[Development](docs/development.md)** - Dependencies, key files, development notes
- **[Developer Tooling Guide](docs/tooling.md)** - Coherent debugger (`:19999`) probes/scrapers/drivers in `tools/`, how to run each, and crash diagnosis

## Gemini AI (display reading + scene/route description)

Details: [docs/gemini.md](docs/gemini.md).

## Technology Stack

.NET 10 (C# 13), Windows Forms, SimConnect SDK (MSFS), SQLite, NVDA/Tolk (screen readers)
