# Rules for any file — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in CLAUDE.md.
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## CORE-1

- Always build the .sln or pass `-p:Platform=x64`; NEVER build the bare `.csproj` alone — it silently defaults to Platform=AnyCPU and writes to a different output folder (`bin\Debug\...`) than the x64 run path, so the running exe never updates. → CLAUDE.md

## CORE-2

- RID-subfolder gotcha: `-r win-x64` (or `dotnet publish -r win-x64`) writes to a SEPARATE `net10.0-windows\win-x64\` tree that a plain `.sln` build never touches — always build/verify the exact folder the app launches from. → CLAUDE.md

## CORE-3

- The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe. → CLAUDE.md

## CORE-4

- `tools/CDUTest` and `tools/CDUTest`-style standalone probes build on their own, NOT as part of the solution. → CLAUDE.md

## CORE-5

- Sim-facing paths are verified only against a live sim — describe an in-sim test plan in the PR; pure logic belongs in `tests/MSFSBlindAssist.Tests` (CI-enforced). → CLAUDE.md

## CORE-6

- `main` is protected — never commit directly to main; always branch + PR. → CLAUDE.md

## CORE-7

- NEVER announce button presses, combo/dropdown value changes, or any direct UI interaction in panel controls — screen readers already announce them; ONLY announce numeric input confirmations, validation errors, and background (non-user-triggered) state changes. The ONE scoped exception is the TFDi MD-11's once-after-settle press confirmation and the EFB shell's `announceChange` opt-in (the paragraph under Screen Reader Announcements) — never a licence to announce presses elsewhere. → CLAUDE.md

## CORE-8

- Combo double-announce suppression must be GLOBAL, never aircraft-gated: `_uiSetEcho`/`MarkUiSet` plus a wrap that sets `announcer.Suppressed` around `ProcessSimVarUpdate` for any var inside the echo window — gating the wrap to one aircraft (the old HS787-only gate) causes double-announces on every other def that self-announces from inside `ProcessSimVarUpdate`. → CLAUDE.md

## CORE-9

- The echo-window suppression must match on TIME only, never on value — a combo set can write a different encoding than the SDK reads back, so a value-compare silently misses the duplicate. → CLAUDE.md

## CORE-10

- Never blanket-suppress value-0 resting-state button labels in MainForm — use the opt-in `SuppressRestingButtonState` flag only; some 0-state labels (PMDG 777 "LNAV: Off", HS787 "Baro STD: QNH") are meaningful and must be spoken. → CLAUDE.md

## CORE-11

- CRITICAL: set `IsConnected = true` BEFORE calling `SetupDataDefinitions()` in SimConnectManager — `StartContinuousMonitoring()` guards on `IsConnected == true`. → CLAUDE.md

## CORE-12

- CRITICAL: never use `TreeView` directly in forms — use `NativeAccessibleTreeView`; the .NET 9/10 UIA `TreeViewAccessibleObject` produces wrong NVDA navigation order. → CLAUDE.md

## CORE-13

- CRITICAL: never hardcode the FBWBA/MSFSBlindAssist database path — always go through `Database/DatabasePathResolver` (`ResolveExistingDatabasePath` for reads, `GetCanonicalDatabasePath` for writes). → CLAUDE.md

## CORE-14

- CRITICAL: every diagnostic log path must be resolved through `Utils/AppLogs.PathFor(...)` into `%APPDATA%\MSFSBlindAssist\logs` — never hand-build a log path. → CLAUDE.md

## CORE-15

- Never hand-build a log write (`File.AppendAllText`/raw path) — every diagnostic log goes through `Utils/Logging/Log` (`Log.Debug/Info/Warn/Error(category,msg)` → debug.log, or `Log.Channel(name)` → named file); `AppLogs.PathFor` is the PATH layer only. → CLAUDE.md

## Background: the former CLAUDE.md core sections

These sections stood in CLAUDE.md's core until 2026-10; CLAUDE.md now keeps the rules as CORE one-liners. Kept here word for word.

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

**One scoped exception — the TFDi MD-11.** Its cockpit panel press is confirmed ONCE, after the aircraft settles, with the control's resulting state ("External Power: On"), because nothing the aircraft exposes confirms a panel press: its PFD, ND, EAD, SD and ISFD are drawn inside its WASM module with no DOM and no export, and what IS readable or writable from outside — the MCDU's exported text, the exported L:vars (the FCP windows' `MD11_AFS_*`, V-speeds, altimeters, minimums) and the `MD11_EXTCTL_*` typed-value inputs — says nothing about what a panel button did. The legend lamps and latch behind a button are the only confirmation a blind pilot has, and an inert press (a generator with the engines off) must be heard as inert. The press's own lamp echo is swallowed until that confirmation speaks — the TFDi MD-11 invariant that begins "A lamp change speaks its OWNER's composed state". The EFB shell's post-press announce is the second, narrower opt-in: a changed label on the control just pressed is spoken only where the reader flagged that element `announceChange: true` — controls whose label carries their own NEW state (the MD-11 EFB's stepper arrows, tiles and state-tile action buttons), never a label the screen reader has just read (the TFDi MD-11 invariant on stepper option lists). Neither is a licence to announce presses anywhere else.

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

`NavdataReaderBuilder.GetDefaultDatabasePath` delegates to the resolver and is safe for reads. The MS Store package name for FS2024 is `Microsoft.Limitless_8wekyb3d8bbwe` (not "FlightSimulator2024"); FS2020 is `Microsoft.FlightSimulator_8wekyb3d8bbwe`. Both live in `Database/MsfsPackagesLocator.cs`, the reader of `UserCfg.opt`'s `InstalledPackagesPath`, to which `NavdataReaderBuilder.GetMSFSBasePath` now delegates. That reader opens the file `FileShare.ReadWrite | FileShare.Delete` and closes it before its first `Directory.Exists` — it is the SIMULATOR's own config, and since the scenery census it is read while the simulator is running, where a reader that permits no writer can make the simulator's own write fail.

### Diagnostic Logs — ONE folder, via `Utils/AppLogs`, ALL writes via `Utils/Logging/Log`

**CRITICAL:** `Utils/Logging/Log` (namespace `MSFSBlindAssist.Utils.Logging`) is the single logging entry point — never hand-build a log write (no raw `File.AppendAllText`/`StreamWriter` against a log path). Two ways to log: `Log.Debug/Info/Warn/Error(category, message)` writes to the app-wide `debug.log` (this is where all former `Debug.WriteLine` trace now goes, and unlike `Debug.WriteLine` it persists in Release builds, not just under a debugger); `Log.Channel("name")` (e.g. `Log.Channel("taxi_guidance")`, `Log.Channel("startup", truncateOnLaunch: true)`) returns a `LogChannel` that writes the dedicated `name.log`. Every line is formatted uniformly as `yyyy-MM-dd HH:mm:ss.fff [LEVEL] [category] message` (local time), written by a single async background writer (never inline/blocking file I/O on the calling thread), with size-based rotation (5 MB × 3 backups) per file. `Log.Init()`/`Log.Shutdown()` are wired in `Program.Main`.

`Utils/AppLogs` remains the PATH authority underneath `Log` — it is not bypassed, just no longer called directly for writing. Every diagnostic log still lives in ONE folder, `%APPDATA%\MSFSBlindAssist\logs` (Roaming), and every log path is still resolved through `Utils/AppLogs.PathFor("name.log")` (now internally, by `Log.Channel`) — never hand-build a log path. Historically logs were scattered (taxi/rollout logs in the Roaming root `%APPDATA%\MSFSBlindAssist\`, the startup log in `%TEMP%`, GSX/docking logs in `%LOCALAPPDATA%\MSFSBlindAssist\logs`), which made "send me your logs" support unanswerable. They were first unified under Local, then moved to Roaming so EVERYTHING the app owns — settings, databases, AND logs — lives in one `%APPDATA%\MSFSBlindAssist` tree. `AppLogs.MigrateLegacyLogs()` (called once at startup in `Program.Main`) best-effort sweeps BOTH legacy locations (Roaming-root `*.log` files AND the entire former Local logs folder) into the canonical folder and removes the Local folder once empty. Current files: `debug.log` (the new app-wide `Log.Debug/Info/Warn/Error` sink), `taxi_guidance.log`, `taxi_router.log`, `landing_exit.log`, `takeoff_assist.log`, `gsx.log`, `gsx-gate-select.log`, `docking.log`, `docking-aircraft.log`, `sayintentions.log`, `startup.log` (truncated per launch), `input_events.txt`. The tester instruction is always: **Windows+R → `%APPDATA%\MSFSBlindAssist\logs`** (or just `%APPDATA%\MSFSBlindAssist` to grab settings + databases + logs in one go).
