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
  - A complete example of every pattern, not a template to copy (its button read-back mappings and press confirmations would come with it); start a new aircraft from `tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs`
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

- **`Database/NavdataReaderBuilder.cs`**: builds the airport database with the navdatareader tool (FS2020 and FS2024)
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
- **`Forms/Settings/AnnouncementsPanel.cs`**: the Announcements settings panel (announcement mode, nearest-city interval, seconds in time readouts, 1,000-foot altitude crossings)
- **`Controls/AccessiblePanel.cs`**: Accessible navigation control

### Input Management

- **`Hotkeys/HotkeyManager.cs`**: Global hotkey registration and processing

## Development Notes

- Project targets .NET 10 (`net10.0-windows`)
- Uses modern SDK-style project format
- Platform: x64 (`Platforms`/`PlatformTarget`), with `RuntimeIdentifier` win-x64 pinned and the output kept at the run path — see the RID-subfolder gotcha, CLAUDE.md [CORE-2] and [Build output and traps](#build-output-and-traps) below
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
> not Coherent tooling — see [Build output and traps](#build-output-and-traps) below. Leave them untouched.

### Crashes & diagnosis

Global exception handlers (`Program.cs` → `InstallGlobalExceptionHandlers`) catch UI-thread
faults (recovered, app keeps running), background-thread faults (logged, CLR still terminates),
and unobserved task exceptions. Startup diagnostics are wired through `Log.Channel("startup", truncateOnLaunch: true)`
which writes to `%APPDATA%\MSFSBlindAssist\logs\startup.log`, so **managed** crashes leave a stack trace there.
A crash with **no** logged exception is almost certainly **native** (WebView2 / Coherent / SimConnect) — check Windows Event Viewer →
Application for the faulting module. Full procedure in [tooling.md §8](tooling.md#8-crash-diagnosis).

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

Moved here from CLAUDE.md (2026-10), word for word except the project count, which said five before the vPilot plugin was counted; CLAUDE.md keeps the commands and the three traps as one-line rules. Corrected 2026-10-09: the RID-subfolder gotcha and the list of standalone tools below, to match the csproj and `tools/` (CORE-2, CORE-4).

**Output (the run path):** `MSFSBlindAssist\bin\x64\{Debug|Release}\net10.0-windows\` — a plain `dotnet build` (the `.sln`, or the `.csproj` with `-p:Platform=x64`) writes HERE. The csproj pins the output there, so there is **NO `win-x64\` subfolder** for a build, with or without `-r win-x64`; see the RID-subfolder gotcha below.

**⚠️ ALWAYS build the SOLUTION (or pass `-p:Platform=x64`), NEVER `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj` alone.** The csproj declares `<Platforms>x64</Platforms>` only, but a bare `dotnet build` on the .csproj still defaults to **Platform=AnyCPU** and writes the exe to **`bin\Debug\net10.0-windows\`** — a DIFFERENT folder from the one the app runs from (**`bin\x64\Debug\net10.0-windows\`**). The build will say "Build succeeded" while the running x64 exe stays frozen at its old timestamp, so changes silently never reach the user (burned a whole session on this — every "compile-check" went to bin\Debug and the user's x64 exe never updated). Correct commands: `dotnet build MSFSBlindAssist.sln -c Debug` (the .sln maps to Debug|x64) or `dotnet build MSFSBlindAssist\MSFSBlindAssist.csproj -c Debug -p:Platform=x64`. To verify a build actually landed, check the LastWriteTime of `bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` is "now". The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe the user will run.

**RID-subfolder gotcha (a SECOND build-path trap, first seen 2026-06-06, changed 2026-08-25):** since commit 70f28f06, `MSFSBlindAssist.csproj` sets `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>` and `<SelfContained>false</SelfContained>`. So a plain `dotnet build` and a build with `-r win-x64` both write to the run path above and create **no** `win-x64\` subfolder, and `dotnet publish` writes to `...\net10.0-windows\win-x64\publish\`, a folder under the run path. (Measured 2026-10-09 with `dotnet msbuild ... -getProperty:OutputPath -getProperty:PublishDir`; the figures are in docs/invariants/core.md, CORE-2.) A build has one output tree, so there is no second tree to build.

The trap is now removing a setting. Without `AppendRuntimeIdentifierToOutputPath=false` the build goes to `...\net10.0-windows\win-x64\`, which is not the folder the app runs from, release.yml zips or the PostBuild xcopy steps target, so it can succeed while all three stay stale. Without the `RuntimeIdentifier` the build is portable and ships every platform's SQLite binaries (86.2 MB against 21.2 MB on a Release build, 70f28f06). A `win-x64\` folder holding built files, such as a `MSFSBlindAssist.dll` beside the run path's, was left by a build from before 2026-08-25 and is stale: do not launch from it. **Build to, and verify the timestamp in, the folder the app actually launches from.**

**Prerequisites:** MSFS_SDK environment variable, .NET 10 SDK

The solution contains six projects: `MSFSBlindAssist` (main app), `MSFSBlindAssistUpdater` (small WinForms auto-update helper), `tools/PMDGDispatchTester` (a console diagnostic REPL for probing which PMDG NG3 dispatch shape a switch accepts against a live sim — e.g. used to confirm the 737 fire-handle UNLOCK→TOP sequence), `tools/ChangelogBuilder` (the release-notes builder that turns `changelog.d/` fragments into the GitHub release body; its parsing/rendering logic is covered by the xUnit suite), `tests/MSFSBlindAssist.Tests` (the pure-logic xUnit suite run by CI), and `plugins/MSFSBlindAssist.VPilotPlugin` (the vPilot plugin that passes VATSIM events to the app over a named pipe; see [vatsim.md](vatsim.md)). The tester compiles the main app's `SimConnect/PMDGNG3DataStruct.cs` via a **linked** `<Compile>` (not a copy) so its CDA layout can never drift. `dotnet build MSFSBlindAssist.sln` builds all six.

The other 12 `tools/*` projects build on their own, never as part of the solution (CORE-4; `ls tools/*/*.csproj` lists 14, and the solution holds only `PMDGDispatchTester` and `ChangelogBuilder` of them, checked 2026-10-09). Eleven link production sources from `MSFSBlindAssist/` with `<Compile Include>`, so they test the app's own code, not a copy. A solution build never compiles them, so build the one you rely on after a change to a source it links. Each is a standalone executable:

- `tools/CDUTest`: fires a single CDA-write or TransmitClientEvent at one chosen PMDG event (used to prove the NG3 CDU keys need TransmitClientEvent, not the CDA write). It links no source; it copies the main app's `SimConnect.dll`. Build: `dotnet build tools/CDUTest`.
- `tools/DistanceUnitsProbe`: asserts `DistanceFormatter` in its metres and feet modes, and the distance milestones.
- `tools/DockingProbe`: asserts `DockingGeometry` (along-track distance, stop and overshoot, beep interval).
- `tools/GsxAirplaneProbe`: checks `GsxAirplaneProfile.ParseDoorOffset` on a literal snippet and `BuildMap` over the real GSX airplane folders and MSFS packages.
- `tools/GsxOffsetProbe`: verifies the GSX `.py` per-aircraft stop-offset evaluator against the real installed GSX profiles (`--sweep` adds the all-profiles guard).
- `tools/GsxProfileProbe`: `GsxProfileParser` over the installed GSX profiles (`--all` sweeps every one for parse and map errors), and a hand-built matcher check.
- `tools/IFlySdkProbe`: dumps the iFly shared-memory block live; it links the generated offset files, so it can never drift.
- `tools/LandingExitSweep`: writes every runway direction's `GetLandingExits` list as CSV over the production `TaxiGraph` and diffs two runs into a markdown report, the whole-database before/after for any change to how landing exits are measured ([tooling.md §7](tooling.md#7-pre-existing-tools-and-other-standalone-non-coherent-probes-out-of-scope--do-not-fold-in)).
- `tools/ProgressiveTaxiProbe`: asserts `TaxiGraph` helpers (`FindTaxiwayIntersectionNode`, `FindTaxiwayEndNode`, `MatchHoldShortRunwayName`, runway-crossing tests) on hand-built synthetic graphs.
- `tools/StandBridgeSweep`: sweeps a real navdata database and reports the PR #235 stand-bridge figures (bridge count, distinct airports touched, and the four safety invariants: never on or across runway pavement, never ending on a hold-short node, a stand, or another stand's lead-in chain) by linking the production `TaxiGraph`/`RunwayPavement`/`RunwayShape` sources. Re-run it (`dotnet build tools/StandBridgeSweep`) before trusting any change to the bridging rule.
- `tools/TaxiAugmentProbe`: asserts the online taxi-data augmentation (the OSM and apt.dat parsers, `TaxiDataMerger`, `StandId`, `GateAliasResolver`) against the fixtures beside it.
- `tools/TaxiGuidanceProbe`: asserts that `GuidanceGeometry.WalkTarget`, the steering look-ahead, is continuous, and that the curve scan finds the KATL 2026-06-10 micro-segment curve.

Both sweeps load the database through the one linked `tools/Shared/NavdataSweepLoader.cs`.

## Claude Code hooks

The committed `.claude/settings.json` runs `.claude/hooks/rules-hook.ps1` under Windows PowerShell in every Claude Code session in this repository. It brings area rules where Claude Code's own path-scoped loading does not reach, and refuses shell edits that would go around it (CORE-16). Its own rules are CCT-1 to CCT-7, in `.claude/rules/claude-tooling.md`.

The modes, and what starts each:

- `read`: after a Read inside `.claude/worktrees/agent-*` (filter `Read(//**/.claude/worktrees/agent-*/**)`), and after every Write or NotebookEdit, since no filter matches a Write; outside agent worktrees it exits at once. A subagent run with worktree isolation gets no area rules from Claude Code, so this adds the file's rule files, once per subagent: whole rule files within about 9,000 characters, then the rest named for Claude to Read (CCT-5).
- `diff`: after every `git` or `gh` command in the Bash and PowerShell tools; it acts only on `git diff`, `git show` (also after `git -C <dir>`) and `gh pr diff`. It adds the rule files for the changed paths the same way. It is registered as `Bash(git *)` rather than `Bash(git diff*)` because a filter naming more than the command also runs on every command holding `$VAR` or `$()`.
- `shell-guard`: before a Bash command using `sed`, `perl`, `tee`, `cat`, `echo`, `printf`, `python`, `python3` or `py`, and before a PowerShell `Set-Content`, `Add-Content`, `Out-File`, `python`, `python3` or `py`. It refuses the command when it writes a file some rule file covers, and points to Read and then Edit or Write. It follows a path held in a variable the same command set to a literal (`f=<path> && sed -i … "$f"`). For Python it reads the code the command runs (`-c`, a heredoc or a bash here-string (`<<<`), a `<` stdin file, a script file, or a script the same command wrote with a heredoc) and refuses only a write sink whose path it can resolve from literals (CCT-7).
- `subagent-start`: when a subagent starts. The built-in Plan agent skips CLAUDE.md, so it gets the "Rules for any file" and "Before changing behaviour" sections of the CLAUDE.md in the checkout it runs in, and the path to Read for the rest. A subagent working in a `.claude/worktrees/<name>` folder that is neither an `agent-*` folder nor the session's own is told to load its rules itself. The own-worktree test compares the folder in the hook input's `transcript_path` with the worktree's path, never `CLAUDE_PROJECT_DIR` (CCT-6).
- `session-start`: after a compaction. It forgets which rule files the hook added, so they can be added again.

**Worktree sessions.** A desktop-app session in `.claude/worktrees/<name>` runs the main checkout's copy of the script, because `CLAUDE_PROJECT_DIR` is the main checkout there. Try a hook change by running the worktree's script by hand, or in a `claude -p` started in the worktree.

**Listing a change's rules.** The same script lists the rule files that load for any paths, which is useful when reviewing a change or planning one:

```
git diff --name-only main... | powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/rules-hook.ps1 for -Stdin
powershell -NoProfile -ExecutionPolicy Bypass -File .claude/hooks/rules-hook.ps1 for MSFSBlindAssist/Navigation/TaxiGraph.cs
```

**Switching it off.** Set the environment variable `MSFSBA_RULES_HOOK=off` before starting Claude Code, and every hook mode does nothing. Use it only to rule the hook out while diagnosing a problem.

**Limits.**
- Claude Code shows the model only about 10,000 characters of a hook's output; anything longer is saved to a file and the model sees a 2 KB preview. So the hook shows whole rule files within 9,000 characters and names the rest by path, and Claude has to Read those itself (CCT-5).
- The shell guard refuses only a write it has matched to a covered file. It leaves allowed `cp`, `mv`, git itself, a redirect from a command it does not start on (`awk … > f`, `git show X:f > f`), PowerShell .NET writes (`[IO.File]::WriteAllText`), a script in another language (Node, `dotnet`), a Python path built at run time (`os.walk`, globs, f-strings, `Path(...) / 'x'`, string concatenation), an interpreter named anything but exactly `python`, `python3` or `py` (`python3.12`, `python.exe`, a full path), and code piped into Python (`cat <<EOF | python -`). CORE-16's instruction still covers those.
- The guard reads a variable as the command wrote it: one changed by `unset`, `read`, `f+=`, `for`, `declare` or `local` keeps its earlier literal value in the guard's eyes, so it can refuse a write that would not happen or miss one that would, and a single-quoted `'$f'` is read like `"$f"`.
- The Python analysis has a 3-second deadline and allows whatever it has not analysed by then. A missing or broken `.claude/hooks/python-writes.ps1` costs only that analysis (CCT-1).
- Each `git` or `gh` command, each filtered shell command and each Write costs about 0.2 to 0.5 s: PowerShell start-up, plus reading the rule files when a target is inside a checkout. A Python command costs up to about 0.75 s (measured 2026-10-09: `python -c "print(1)"` about 0.4 s, a few-KB heredoc with a write sink about 0.7 s), and filtered shell commands are about 50 ms slower than before the Python analysis (the first-call compile of the larger parser). Normal Reads never start the hook.
- Claude Code and the hook do not know what the other has added, so a rule file can arrive twice: when Claude Reads a file and then runs `git diff` over it, or the other way round. It is harmless; it costs context.
- A change to `.claude/settings.json` can reach a session that is already running, so test a change in a fresh session.

**Live checks (CCT-3).** After changing a hook's matcher or `if` filter, check in a fresh session (a headless `claude -p` from the checkout is enough) that:

1. A subagent run with worktree isolation that reads `MSFSBlindAssist/Aircraft/Pmdg737DisplayReads.cs` and `MSFSBlindAssist/Navigation/TaxiGraph.cs` gets `pmdg-737`, `variable-definitions`, `gsx-stands-docking`, `landing-exits`, `rollout-geometry`, `runway-holds-call-sites`, `taxi-graph`, `taxi-position` and `taxi-routing`, each shown in full or named to Read, and a Write of a new file under `MSFSBlindAssist/Aircraft/MD11/` adds `md11`. Ask it to quote the last rule ID of each file shown in full: a 2 KB preview would not contain it (CCT-5).
2. The main conversation reading the same two files gets each rule file once, from Claude Code, with no hook header.
3. `sed -i` and `cat > file <<EOF` aimed at a covered file are refused with the CORE-16 message, and the same aimed at a scratch file run. A Python heredoc that writes a covered file is refused and one that only reads it runs.
4. `git show <commit> -- MSFSBlindAssist/Navigation/TaxiGraph.cs`, and `git -C <checkout> diff --name-only` over that commit, add the taxi rule files.
5. A Plan agent can quote CORE-7, from CLAUDE.md's "Rules for any file", and is told to Read CLAUDE.md for the rest.
6. A normal Read in the main conversation never starts the `read` hook. To see this, register a logging copy of the hook under the same filter with `--settings`.
7. A subagent in the session's own worktree gets no instruction, and a logging SubagentStart hook (registered with `--settings`, as in check 6) shows its `transcript_path` key matches the worktree. A subagent in another non-`agent-*` worktree is told to load its rules itself. In a `claude -p` started in the worktree this checks the `transcript_path` key format; only a desktop-app worktree session (where `CLAUDE_PROJECT_DIR` is the main checkout) shows the old false warning.
8. A filter naming a redirect (`Bash(cat >*)`) still matches nothing, so the broad shell-guard filters are still needed; and a command holding `$VAR` but no git command does not start the diff hook.


