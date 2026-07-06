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

The solution contains three projects: `MSFSBlindAssist` (main app), `MSFSBlindAssistUpdater` (small WinForms auto-update helper), and `tools/PMDGDispatchTester` (a console diagnostic REPL for probing which PMDG NG3 dispatch shape a switch accepts against a live sim — e.g. used to confirm the 737 fire-handle UNLOCK→TOP sequence). The tester compiles the main app's `SimConnect/PMDGNG3DataStruct.cs` via a **linked** `<Compile>` (not a copy) so its CDA layout can never drift. `dotnet build MSFSBlindAssist.sln` builds all three. A second standalone probe, `tools/CDUTest`, fires a single CDA-write or TransmitClientEvent at one chosen PMDG event (used to prove the NG3 CDU keys need TransmitClientEvent, not the CDA write); it builds on its own (`dotnet build tools/CDUTest`), not as part of the solution.

## Testing

No automated test project exists. Verification is done by running the app against a live sim (MSFS 2020 or 2024). When making changes, build, then describe an in-sim test plan in the PR — the human owner of the repo runs it. Don't add unit tests speculatively; this is a SimConnect-driven UI app where most code paths only execute against the real simulator.

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

**CRITICAL:** In SimConnectManager.cs, set `IsConnected = true` BEFORE calling `SetupDataDefinitions()`. Required for `StartContinuousMonitoring()` to execute properly (has guard clause requiring `IsConnected == true`). See SimConnectManager.cs:251

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

### Diagnostic Logs — ONE folder, via `Utils/AppLogs`

**CRITICAL:** Every diagnostic log lives in `%APPDATA%\MSFSBlindAssist\logs` (Roaming) and every log path MUST be resolved through `Utils/AppLogs.PathFor("name.log")` — never hand-build a log path. Historically logs were scattered (taxi/rollout logs in the Roaming root `%APPDATA%\MSFSBlindAssist\`, the startup log in `%TEMP%`, GSX/docking logs in `%LOCALAPPDATA%\MSFSBlindAssist\logs`), which made "send me your logs" support unanswerable. They were first unified under Local, then moved to Roaming so EVERYTHING the app owns — settings, databases, AND logs — lives in one `%APPDATA%\MSFSBlindAssist` tree. `AppLogs.MigrateLegacyLogs()` (called once at startup in `Program.Main`) best-effort sweeps BOTH legacy locations (Roaming-root `*.log` files AND the entire former Local logs folder) into the canonical folder and removes the Local folder once empty. Current files: `taxi_guidance.log`, `taxi_router.log`, `landing_exit.log`, `takeoff_assist.log`, `gsx-gate-select.log`, `docking.log`, `docking-aircraft.log`, `startup.log` (truncated per launch), `input_events.txt`. The tester instruction is always: **Windows+R → `%APPDATA%\MSFSBlindAssist\logs`** (or just `%APPDATA%\MSFSBlindAssist` to grab settings + databases + logs in one go).

### Flight-Planning EFB & Instrument-Procedure Data (Shift+E)

**Feature:** `Forms/ElectronicFlightBagForm.cs` — the screen-reader flight-plan builder (Departure / SID / STAR / Arrival / Approach / Airport Lookup tabs) + SimBrief import + Gemini route description. Procedure data comes from `Database/NavigationDatabaseProvider.cs` (the navdatareader `approach` / `approach_leg` / `transition` / `transition_leg` tables — SIDs are `suffix='D'`, STARs `suffix='A'`, approaches the rest), assembled by `Navigation/FlightPlanManager.cs`. **Navigraph navdata, when present in the user's DB, is used here automatically** — there is ONE merged DB read by every provider; nothing branches on data source.

**Key rules when touching this code (invariants behind the 2026-06 bugfix pass):**
- **Never drop a fix-less leg.** ARINC 424 path/terminator legs `CA`/`VA`/`CI`/`VI`/`VM`/`FM`/`CD`/`VD`/`CR`/`VR` legitimately have an empty `fix_ident` (~14 % of legs) — they carry the **initial climb of most SIDs and the heading legs of most missed approaches**. `ParseLegToWaypoint` must NOT `return null` on empty `fix_ident`; it synthesizes a readable maneuver label via `BuildManeuverLabel` (e.g. *"Climb heading 071° to 600 feet"*) from the leg `type` + course + altitude. The leg's path/terminator code lives in `approach_leg.type` (read into `WaypointFix.Type`), NOT `arinc_descr_code` (which this DB only fills with A/F/M/B fix-role letters).
- **Resolve fix coordinates across all fix tables, not just `waypoint`.** Navaid (`fix_type='V'`/`'N'`), runway-threshold (`'R'`, idents like `RW06L`) and airport (`'A'`) fixes are NOT in the `waypoint` table → a waypoint-only lookup left them at (0,0) and corrupted distance/bearing. `ResolveFixCoordinates` falls back to `vor`/`ndb`/`runway_end`/`airport` by fix_type. `approach_leg.fix_lonx`/`fix_laty` are NULL in this navdata build — do NOT rely on them. `FlightPlanManager.UpdateAircraftPosition` skips distance/bearing for any waypoint still at (0,0) — maneuver legs have no position.
- **Circling approaches (VOR-A, NDB-A…) share `suffix='A'` with STARs.** Distinguish them by **the presence of a missed-approach leg** (`EXISTS … approach_leg.is_missed=1`): `GetApproaches` INCLUDES suffix-A rows that have a missed leg (they're circling approaches); `GetSTARs`/`GetSTARsForRunway` EXCLUDE them. Without this, circling approaches were missing from the Approach list and polluting the STAR list.
- **"ALL"-runway SID/STAR loads the runway-INDEPENDENT body**, not an arbitrary runway's legs. The `GetSIDsForRunway`/`GetSTARsForRunway` "ALL" branch picks, per procedure name, the `approach_id` that is runway-independent under BOTH columns — empty `runway_name` AND a non-`RW…` `arinc_name` — then runway_name-less rows, then `MIN(approach_id)`. Keying on `runway_name` alone degenerated back to a random runway transition's legs at arinc-tagged airports (OMDB: `runway_name` NULL on every row).
- **Transition + procedure share their boundary fix** — `FlightPlanManager.AppendWaypoints` drops the duplicate first/last fix when concatenating, else it appears twice with a spurious 0 NM leg.
- **Airport Lookup**: the runway list needs a `SelectedIndexChanged` handler (selecting a runway must repopulate the runway-info box; it's not Load-button-only), and `GetRunwayDetailedInfo`'s `SELECT r.*, re.*` aliases the runway-END columns (`re.heading AS end_heading`, `end_altitude`/`end_lonx`/`end_laty`) — the bare `heading`/`altitude`/`lonx`/`laty` are ambiguous between `runway` and `runway_end` and resolve to the runway-CENTER (primary-end) value, so a secondary end like "24L" showed the reciprocal's 180°-off heading.
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

**Every L:var write that reaches `SetLVar` is routed through the MobiFlight calculator path (`ExecuteCalculatorCode("{v} (>L:{var})")`) when MobiFlight is connected** — NOT the native `AddToDataDefinition` + `SetDataOnSimObject` write (which is unreliable for many add-on L:vars and silently reverts FBW vars a frame later). The routing lives in `SetLVar` (`SimConnectManager.cs` ~3896) and is gated:
- **`CalcPathVerified`** must be true (the end-to-end nonce probe — `IsMobiFlightConnected` is true even with NO WASM module installed, so it must never gate writes) — otherwise it falls through to the data-def write so users without the WASM module still work; FBW installs verify within ~3 s of detection.
- **Plain L:var names only** — a name containing a **space or colon** is a stock SimVar shape (`TRANSPONDER STATE:1`, `INTERACTIVE POINT OPEN:0`) and is left on the data-def path; `SetLVar` always prepends `L:` so a real caller never passes such a name through the calc branch.

**So "with MobiFlight connected, does everything use the calc path except PMDG?" — essentially YES for L:var writes, with the precise scope being:**
- **Routed through the calc path:** every plain-L:var write — panel combos/buttons (MainForm's `if (!handled) SetLVar(...)` fallback, ~MainForm.cs:5179/5217/5323/5484), the FBW per-prefix catch-alls, and every aircraft def's `SetLVar(key,value)` call (Fenix combos, etc.).
- **NOT routed (use their own mechanism regardless of MobiFlight):** **PMDG** (writes via CDA `SetClientData`/`SendPMDGEvent` — never calls `SetLVar`), **K-events** (`SendEvent`/`TransmitClientEvent`), **H-events**, and **stock SimVars** (space/colon names, left on data-def). HS787 control writes are K/H-events, also unaffected.

**`SendEvent` H:/dotted calc-path gate + pre-connection queue.** The H:/dotted FBW event classes (e.g. `H:A380X_EFIS_CP_BARO_PUSH_1`, `A32NX.FCU_AP_1_PUSH`) prefer the MobiFlight calc path. `SendEvent` splits them: **H: events** go to the MobiFlight channel whenever `IsMobiFlightConnected` (queued during the brief connect window — they have no other transport on any branch). **Dotted events** prefer the calc path only once **`CalcPathVerified`**; while the probe is still running they are queued in the bounded `pendingCalcEvents` (cap `MaxPendingCalcEvents = 64`), then flushed via calc on `MarkCalcPathVerified` or via the legacy `MapClientEventToSimEvent` + `TransmitClientEvent` transport on `MarkCalcPathProbeConcluded` (module absent, or a non-FBW aircraft that can't probe — MainForm concludes immediately for those). The queue is cleared on MobiFlight teardown so events never carry across a disconnect/aircraft swap. `FireCalcEvent` is the shared single-event dispatcher used by both the live path and the flush.

**Catch-all standardization across aircraft — the verdict (verified, do NOT "fix" the Fenix):** the per-control explicit cases in `FenixA320Definition.HandleUIVariableSet` are **fine, not a gap**. MainForm ALREADY provides the effective catch-all: when `HandleUIVariableSet` returns false, the combo/button paths call `SetLVar(varKey, value)` (now calc-routed). So a plain Fenix L:var combo works with NO explicit case at all. The explicit Fenix cases that *matter* are the ones doing MORE than a plain write — button transitions (`ExecuteButtonTransition`, 0→1 pulse), COM frequency (validate + Hz + `SendEvent`), and encoder increment/decrement counters — and those **cannot** be replaced by a blanket `SetLVar` catch-all without breaking. A single cross-aircraft catch-all is impossible: **PMDG** (CDA struct offsets, inversions, momentary params) and **HS787** (K/H-event tables) don't write L:vars at all, so a string-keyed `SetLVar` catch-all is meaningless for them. The standard already exists at the MainForm `SetLVar`-fallback layer; each def only adds explicit cases for non-plain-write controls.

**RPN number formatting must be invariant fixed-point** — `value.ToString("0.################", InvariantCulture)` (or `"0.###"`-style) — NEVER default `{0}` formatting or `$"{double}"` interpolation: the former emits scientific notation for small/large magnitudes (`1E-05`) and the latter uses CurrentCulture (`87,5` on comma-decimal locales); the MSFS RPN parser rejects both. Same rule for every `ExecuteCalculatorCode` call that embeds a computed double (the A380 temp-selector was bitten).

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
- **Working with FCU/MCP/display systems** → [Architecture](docs/architecture.md)
- **Adding or modifying hotkeys** → [Hotkey System](docs/hotkey-system.md)
- **Fenix rotary encoders (RMP, FCU)** → [Fenix Increment/Decrement](docs/fenix-increment-decrement.md)
- **Tuning visual guidance PID controller** → [Visual Guidance](docs/visual-guidance.md)
- **Working on taxi guidance (graph, router, tone, form)** → [Taxi Guidance](docs/taxi-guidance.md)
- **Working on PMDG 737-800 panels, CDU, NG3 data struct** → [PMDG 737-800](docs/pmdg-737.md)
- **Understanding variable patterns** → [Variable System](docs/variable-system.md)
- **API reference** → [Aircraft Definitions](docs/aircraft-definitions.md)
- **Dependencies and key files** → [Development](docs/development.md)

**Available documentation:**
- **[Quick Reference](docs/QUICK-REFERENCE.md)** - Common patterns and workflows (read first for most tasks)
- **[Architecture](docs/architecture.md)** - Core components, multi-aircraft system, FCU architecture
- **[Adding Features](docs/adding-features.md)** - Step-by-step workflows for common development tasks
- **[Variable System](docs/variable-system.md)** - Three patterns for managing variables (Panel, Monitoring, Hotkey)
- **[Fenix Increment/Decrement](docs/fenix-increment-decrement.md)** - Counter-based pattern for Fenix rotary encoders
- **[Visual Guidance](docs/visual-guidance.md)** - PID controller tuning and ground track monitoring
- **[Taxi Guidance](docs/taxi-guidance.md)** - Turn-by-turn taxi assistance, steering tone, ATC-constrained routing
- **[PMDG 737-800](docs/pmdg-737.md)** - NG3 SDK patterns, two-CDU convention, FIRE_HandlePos ordering, EFB gating
- **[Aircraft Definitions](docs/aircraft-definitions.md)** - Multi-aircraft dictionary system API reference
- **[Hotkey System](docs/hotkey-system.md)** - Dual-mode hotkeys and multi-aircraft routing
- **[Development](docs/development.md)** - Dependencies, key files, development notes
- **[Developer Tooling Guide](docs/tooling.md)** - Coherent debugger (`:19999`) probes/scrapers/drivers in `tools/`, how to run each, and crash diagnosis

## Gemini AI (display reading + scene/route description)

Details: [docs/gemini.md](docs/gemini.md).

## Technology Stack

.NET 10 (C# 13), Windows Forms, SimConnect SDK (MSFS), SQLite, NVDA/Tolk (screen readers)
