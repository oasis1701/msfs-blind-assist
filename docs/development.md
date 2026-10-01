# Development Reference

This document contains development notes, key files, and dependencies for MSFS Blind Assist.

## Key Files to Understand

### Aircraft Definitions (Multi-Aircraft Support)

- **`Aircraft/IAircraftDefinition.cs`**: Interface defining contract for all aircraft implementations
  - Defines all required methods: GetVariables(), GetPanelStructure(), GetPanelControls() (public API), GetButtonStateMapping(), HandleHotkeyAction()
  - FCU control type methods and aircraft metadata properties

- **`Aircraft/BaseAircraftDefinition.cs`**: **Recommended base class** for new aircraft implementations
  - Provides default hotkey handling via GetHotkeyVariableMap() and HandleHotkeyAction()
  - Includes ShowFCUInputDialog() helper method for standard input dialogs
  - Automatic button state announcements for mapped actions
  - **Panel controls caching**: Implements GetPanelControls() with lazy initialization - override BuildPanelControls() to define panels
  - Reduces boilerplate code significantly

- **`Aircraft/FlyByWireA320Definition.cs`**: Complete A320 definition (367 variables, 24 panels, all mappings)
  - **Reference implementation** - Use as template when adding new aircraft
  - Contains all variables, panel structures, control mappings, and hotkey handlers for A320
  - Demonstrates both simple variable mapping and custom dialog handling

### Main Application

- **`MainForm.cs`**: Primary UI logic, dynamic aircraft instance (`currentAircraft` field), aircraft switching (`SwitchAircraft()` method)
- **`MainForm.Designer.cs`**: Aircraft menu definition and menu items
- **`Program.cs`**: Application entry point and initialization

### SimConnect Integration

- **`SimConnect/SimVarDefinitions.cs`**: **Base enums and classes ONLY** (no aircraft-specific data)
  - Contains: `SimVarType`, `UpdateFrequency` enums, `SimVarDefinition` class definition
  - **Does NOT contain:** Variable dictionaries (moved to aircraft definition classes)

- **`SimConnect/SimConnectManager.cs`**: Core simulator communication, `CurrentAircraft` property for dynamic variable access, teleport functionality
- **`SimConnect/SimVarMonitor.cs`**: State change monitoring and announcements

### Accessibility

- **`Accessibility/ScreenReaderAnnouncer.cs`**: Multi-method screen reader integration
- **`Accessibility/NvdaControllerWrapper.cs`**: Direct NVDA integration
- **`Accessibility/TolkWrapper.cs`**: Universal screen reader support

### Database System

- **`Database/DatabaseBuilder.cs`**: BGL file processing for airport data
- **`Database/Models/Airport.cs`**: Airport data model
- **`Database/Models/Runway.cs`**: Runway data model
- **`Database/Models/ParkingSpot.cs`**: Gate/parking data model
- **`Database/Models/TaxiPath.cs`**: Taxi path data model (start/end coords, name, type)
- **`Database/Models/TaxiNode.cs`**: Graph node (Normal / HoldShort / ILSHoldShort / Parking)
- **`Database/Models/TaxiRoute.cs`**: Route with segments, hold-shorts, lineup target
- **`Database/Models/StartPosition.cs`**: Runway start position data model
- **`Database/LittleNavMapProvider.cs`**: `GetTaxiPaths()` / `GetRunwayStarts()` queries + taxiway name normalization + parking abbreviation mapping

### Taxi Guidance

- **`Navigation/TaxiGraph.cs`**: Builds the airport taxi graph from navdatareader rows (spatial hash node merge, taxiway name index)
- **`Navigation/TaxiRouter.cs`**: ATC-constrained A* pathfinding — follows the user-entered taxiway sequence in order, falls back to shortest path only when the sequence doesn't connect
- **`Navigation/RunwayCenterlineTracker.cs`**: Shared cross-track math used by taxi lineup AND takeoff-assist
- **`Services/TaxiGuidanceManager.cs`**: Real-time state machine, position tracking, announcements, re-routing
- **`Services/TaxiSteeringTone.cs`**: Stereo-panned steering tone with hysteresis + min sustain + low-pass smoothing
- **`Forms/TaxiAssistForm.cs`**: Route entry UI (destination combo, filtered taxiway ComboBoxes, hold-short checkboxes)
- **`Forms/Settings/TaxiGuidancePanel.cs`**: User settings (waveform, volume, crossing announcements)

See [Taxi Guidance](taxi-guidance.md) for the full feature reference.

### Access GSX

- **`Services/GsxService.cs`**: GSX menu/tooltip bridge, active-service tracking, announcement throttling, invoice/receipt parsing, and GSX settings persistence
- **`Forms/AccessGSXForm.cs`**: Accessible GSX menu, tooltip, active-services selector, keyboard shortcuts, and settings launcher
- **`Forms/GsxSettingsForm.cs`**: Screen-reader-friendly GSX settings editor built from GSX's published settings metadata

See [Access GSX](gsx.md) for the full feature reference.

### User Interface

- **`Forms/RunwayTeleportForm.cs`**: Runway selection dialog
- **`Forms/GateTeleportForm.cs`**: Gate selection dialog
- **`Forms/AnnouncementSettingsForm.cs`**: Tabbed announcement settings (mode, nearest city interval, weather/SIGMET/PIREP auto-announce)
- **`Controls/AccessiblePanel.cs`**: Accessible navigation control

### Input Management

- **`Hotkeys/HotkeyManager.cs`**: Global hotkey registration and processing

## Development Notes

- Project targets .NET 10 (`net10.0-windows`)
- Uses modern SDK-style project format
- Platform: x64 (`Platforms`/`PlatformTarget`; no `RuntimeIdentifier` — see the RID-subfolder gotcha in CLAUDE.md)
- Uses Microsoft Flight Simulator SimConnect SDK
- Post-build event copies SimConnect.dll to output directory
- SimConnect.cfg configuration file is copied to output for connection settings
- Application requires x64 build for proper SimConnect operation
- C# 13 with nullable reference types enabled
- **IMPORTANT - SimConnect Connection Timing:** `IsConnected = true` must be set immediately after SimConnect constructor, BEFORE calling `SetupDataDefinitions()`. This ensures `StartContinuousMonitoring()` can execute properly (it has a guard clause requiring `IsConnected == true`). See SimConnectManager.Connect() in SimConnect/SimConnectManager.cs

### A380/A32NX live-debugging tools (`tools/`)

The FlyByWire jets expose their MCDU/flyPad/cockpit displays as Coherent GT views on
the sim's remote inspector (`http://127.0.0.1:19999`). **The full catalogue — every
tool, how to run it, the shared transport, and crash diagnosis — is in
[Developer Tooling Guide](tooling.md), with an index at [`tools/README.md`](../tools/README.md).**
The essentials:

- **`tools/coherent-eval.ps1`** — the canonical entry point. Run a JS expression inside
  any Coherent view by title-needle (ids shuffle every session, so never hardcode them).
  Read/write any L:var (`SimVar.GetSimVarValue`/`SetSimVarValue`), scrape/click any
  cockpit DOM, or inject an in-page agent (`-PreFile`) and call it. The header lists every
  view title-needle (A380X_MFD / A380X_ND_1 / A380X_EWD / A380X_SYSTEMSHOST / ISISlegacy /
  "- EFB" / …). No Developer Mode needed — the sim opens port 19999 itself.
- **`tools/_probe/`** — ~42 worked probe scripts (one feature each) + `README.md` explaining
  the IIFE-returns-a-string pattern. Copy one, tweak the var/page, run via `coherent-eval.ps1`.
- **Drivers** (`mcdu_*`, `fp_*`, `sd-page-tour.ps1`, `mfd_import_and_scrape.ps1`, `fcu/`),
  **Node projects** (`fbw-mcdu-probe/`, `flypad-shell-test/`, `efb-dom-tool.js`), and the
  superseded **bootstrap probes** (`probe-*.ps1`, `prove-coherent-scrape.ps1`,
  `test-coherent-ws.ps1`) — all detailed in [tooling.md](tooling.md).

The `tools/*.md` files (e.g. `a380-simvars-catalog.md`, `a380-fcu-vars.md`,
`a380-sd-pages.md`) are the reference catalogues mined from the FBW source. The
`tools/_fbw_ecam/` workspace (downloaded FBW source + generators) stays gitignored;
its product ships as `MSFSBlindAssist/SimConnect/EWDMessageLookupA380.cs`.

> `tools/CDUTest` and `tools/PMDGDispatchTester` are **pre-existing PMDG console apps**,
> not Coherent tooling — see [CLAUDE.md](../CLAUDE.md) → Build Commands. Leave them untouched.

### Crashes & diagnosis

Global exception handlers (`Program.cs` → `InstallGlobalExceptionHandlers`) catch UI-thread
faults (recovered, app keeps running), background-thread faults (logged, CLR still terminates),
and unobserved task exceptions. Startup diagnostics are wired through `Log.Channel("startup", truncateOnLaunch: true)`
which writes to `%APPDATA%\MSFSBlindAssist\logs\startup.log`, so **managed** crashes leave a stack trace there.
A crash with **no** logged exception is almost certainly **native** (WebView2 / Coherent / SimConnect) — check Windows Event Viewer →
Application for the faulting module. Full procedure in [tooling.md §8](tooling.md).

## Dependencies

- **Microsoft.FlightSimulator.SimConnect** (from MSFS SDK)
- **System.Windows.Forms** (.NET 10)
- **Microsoft.Data.Sqlite** (version 10.0.x) - Airport database functionality
- **SQLitePCLRaw.bundle_e_sqlite3** (version 3.0.x) - direct reference lifting the transitive
  bundle above the vulnerable 2.1.11 (CVE-2025-6965); keep it >= the version
  Microsoft.Data.Sqlite would otherwise pull
- **Newtonsoft.Json** (version 13.0.3) - JSON serialization
- **System.Speech** (version 10.0.x) - Text-to-speech fallback
- **Microsoft.Extensions.Configuration** (version 10.0.x) - Configuration management
- **Microsoft.Extensions.Configuration.Json** (version 10.0.x) - JSON configuration provider
- **Microsoft.Extensions.Configuration.Binder** (version 10.0.x) - Configuration binding
- **NVDA Controller Client** (included) - Direct NVDA integration
- **Tolk wrapper** (included) - Universal screen reader support

## Settings System

Settings are now stored in JSON format at:
- **Location:** `%APPDATA%\MSFSBlindAssist\settings.json`
- **Format:** JSON (replaces legacy .NET Framework user settings)
- **Managed by:** `Settings/SettingsManager.cs` and `Settings/UserSettings.cs`

### Aircraft Persistence

- `UserSettings.LastAircraft`: Stores the aircraft code of the last selected aircraft (e.g., "A320")
- Application automatically loads this aircraft on startup
- Updated when user switches aircraft via the Aircraft menu

**Note:** Users upgrading from .NET Framework 4.8.1 will need to reconfigure their settings.


## Build output and traps

Moved here from CLAUDE.md (2026-10), word for word; CLAUDE.md keeps the commands and the three traps as one-line rules.

**Output (the run path):** `MSFSBlindAssist\bin\x64\{Debug|Release}\net10.0-windows\` — a plain `dotnet build` (the `.sln`, or the `.csproj` with `-p:Platform=x64`) writes HERE. There is **NO `win-x64\` subfolder** unless you build with an explicit `-r win-x64`; see the RID-subfolder gotcha below.

**⚠️ ALWAYS build the SOLUTION (or pass `-p:Platform=x64`), NEVER `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj` alone.** The csproj declares `<Platforms>x64</Platforms>` only, but a bare `dotnet build` on the .csproj still defaults to **Platform=AnyCPU** and writes the exe to **`bin\Debug\net10.0-windows\`** — a DIFFERENT folder from the one the app runs from (**`bin\x64\Debug\net10.0-windows\`**). The build will say "Build succeeded" while the running x64 exe stays frozen at its old timestamp, so changes silently never reach the user (burned a whole session on this — every "compile-check" went to bin\Debug and the user's x64 exe never updated). Correct commands: `dotnet build MSFSBlindAssist.sln -c Debug` (the .sln maps to Debug|x64) or `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj -c Debug -p:Platform=x64`. To verify a build actually landed, check the LastWriteTime of `bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` is "now". The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe the user will run.

**RID-subfolder gotcha (a SECOND build-path trap, seen 2026-06-06):** the csproj has no `<RuntimeIdentifier>`, so a plain `dotnet build` produces the non-RID run path above and creates **no** `win-x64\` subfolder. But building with an explicit **`-r win-x64`** (or `dotnet publish -r win-x64`) writes to a SEPARATE tree, `...\net10.0-windows\win-x64\`, that a plain build NEVER touches. So a stale `win-x64\MSFSBlindAssist.dll` can sit next to a fresh non-RID `net10.0-windows\MSFSBlindAssist.dll` and mislead you into thinking the build didn't land. **Build to — and verify the timestamp in — the folder the app actually launches from.** By default that is the non-RID `bin\x64\Debug\net10.0-windows\` (which the `.sln` build updates). Only if you deliberately run a `win-x64\` RID tree must you pass `-r win-x64` so C# changes reach it (a plain `.sln` build will not). NOTE: the JS agents under `Resources\` are copied to whichever tree you build, so when in doubt build BOTH (plain + `-r win-x64`), or just confirm your launch path.

**Prerequisites:** MSFS_SDK environment variable, .NET 10 SDK

The solution contains five projects: `MSFSBlindAssist` (main app), `MSFSBlindAssistUpdater` (small WinForms auto-update helper), `tools/PMDGDispatchTester` (a console diagnostic REPL for probing which PMDG NG3 dispatch shape a switch accepts against a live sim — e.g. used to confirm the 737 fire-handle UNLOCK→TOP sequence), `tools/ChangelogBuilder` (the release-notes builder that turns `changelog.d/` fragments into the GitHub release body; its parsing/rendering logic is covered by the xUnit suite), and `tests/MSFSBlindAssist.Tests` (the pure-logic xUnit suite run by CI). The tester compiles the main app's `SimConnect/PMDGNG3DataStruct.cs` via a **linked** `<Compile>` (not a copy) so its CDA layout can never drift. `dotnet build MSFSBlindAssist.sln` builds all five. A second standalone probe, `tools/CDUTest`, fires a single CDA-write or TransmitClientEvent at one chosen PMDG event (used to prove the NG3 CDU keys need TransmitClientEvent, not the CDA write); it builds on its own (`dotnet build tools/CDUTest`), not as part of the solution. A third standalone probe, `tools/IFlySdkProbe`, dumps the iFly shared-memory block live (links the generated offset files so it can never drift); it also builds on its own, not as part of the solution. A fourth standalone probe, `tools/StandBridgeSweep`, sweeps a real navdata database and reports the PR #235 stand-bridge figures (bridge count, distinct airports touched, and the four safety invariants — never on or across runway pavement, never ending on a hold-short node, a stand, or another stand's lead-in chain) by linking the production `TaxiGraph`/`RunwayPavement`/`RunwayShape` sources rather than reimplementing their logic, so its numbers can never drift from what the app actually builds; re-run it (`dotnet build tools/StandBridgeSweep`) before trusting any change to the bridging rule — it also builds on its own, not as part of the solution. A fifth, `tools/LandingExitSweep`, writes every runway direction's `GetLandingExits` list as CSV over the production `TaxiGraph` and diffs two runs into a markdown report — the whole-database before/after for any change to how landing exits are measured (`docs/tooling.md` §7); it builds on its own too. Both sweeps load the database through the one linked `tools/Shared/NavdataSweepLoader.cs`.


