# Rules for any file — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in CLAUDE.md.
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

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

- Never repeat what the screen reader just said (a panel control's press, label or new combo value). A direct interaction is confirmed once, and only with what the reader cannot say: a state read back after the press, or "<name> pressed" for an action with no readable state. Numeric input confirmations, validation errors and background (non-user-triggered) state changes always speak.

Why. The screen reader already speaks every control the pilot reaches or presses: its name, its role and, for a combo, the value just chosen. Speaking that again is noise. What the reader cannot say is what a press DID (an autopilot that engaged or did not, a managed mode taken) or, for a control with no state to read, that the press registered at all. A confirmation may carry only that. The test for a new announcement in a panel control: does it tell the pilot something the screen reader did not? If not, leave it out. Adding a case is a deliberate choice for an effect the pilot cannot otherwise hear, never a way to echo a press.

Today's cases, each with its code. They show what the principle allows; the test above decides a new one, so the list is not closed.

A state read back after the press:

- The TFDi MD-11's once-after-settle press confirmation ([MD11-11]). Nothing the aircraft exposes confirms a panel press, so once it settles the definition speaks the control's resulting state ("External Power: On") and swallows the press's own lamp echo until then. Code: the press feedback in `TFDiMD11Definition.State.cs` and `Md11AnnouncementGate.Feedback`.
- The EFB shell's `announceChange` opt-in ([MD11-26]). A pressed control's changed label is spoken only where the reader flagged that element `announceChange: true`: controls whose label carries their own NEW state (the MD-11 EFB's stepper arrows, tile button and state tile's action button). Code: `Forms/FBWA380/FbwEfbForm.cs` (the shell's page script speaks it), `Resources/coherent-md11-efb-agent.js` (sets the flag), `SimConnect/CoherentPmdgEfbClient.cs` (passes it on).
- The button read-back of the FBW A320, Headwind A330 and FBW A380. After a panel button click (`MainForm.PanelBuilder.cs`) or a hotkey press (`MainForm.Hotkeys.cs`) whose event a definition maps in `GetButtonStateMapping`, `MainForm.HandleButtonStateAnnouncement` (`MainForm.Announcers.cs`) waits 300 ms, force-reads the mapped state variable and speaks it once (`pendingStateAnnouncements`, consumed by `AnnounceVariableState` in `OnSimVarUpdated`). Only these three map any button (the A330 is the A320's map plus its baro STD rows); every other definition returns an empty map. A new mapping is the owner's deliberate choice for a button whose effect the pilot cannot otherwise hear.
- The value-dialog toggle read-back. In `ValueInputForm` (`ToggleButtonDef`), 1.2 s after a toggle button is pressed the dialog refreshes every toggle's label and speaks the pressed toggle's new state with `AnnounceImmediate`, unless the toggle's `SuppressStateAnnounce` says the aircraft refused the press (the announce would cut off the refusal). The PMDG 777 and 737, HS787, iFly 737 and MD-11 dialogs use it.
- The A380 FCU push/pull panel buttons speak the resulting selected or managed value through `FlyByWireA380Definition.OnPanelButtonFired` (`FlyByWireA380Definition.HotkeysAndMotion.cs`), exactly as their hotkeys do.

"<name> pressed" for an action with no readable state:

- Momentary pushbuttons: `BaseAircraftDefinition.PulseMomentaryLVar` pulses the L:var 1 then 0 and speaks "<name> pressed". The FBW A320 (and so the Headwind A330) calls it from the momentary L-var handler in `FlyByWireA320Definition.HandleUIVariableSet`, which covers both a `RenderAsButton` momentary button and an Idle/Activate action combo (its comment calls the combo "the screen-reader-preferred form for TEST/RESET/DEPLOY actions"); the A380's `_momentaryButtons` (Off/Activate combos) call it from `FlyByWireA380Definition.UiVariableSet.cs`.
- The A380's ECAM-CP keys (`A32NX_BTN_*`, made buttons at the owner's request in 2026-06) speak the same words through `FlyByWireA380Definition.PulseEcpKey`.
- The A320's master warning and master caution buttons and its rudder-trim nudge buttons (`RUDDER_TRIM_LEFT`, `RUDDER_TRIM_RIGHT`), in `FlyByWireA320Definition.HandleUIVariableSet`, and so the Headwind A330's.
- MainForm's fallback for a panel button no definition handles, at two sites in `MainForm.PanelBuilder.cs`.
- The A380 chronometer combos `A32NX_CHRONO_TOGGLE` and `A32NX_CHRONO_RST` ("Chronometer start stop", "Chronometer reset") in `FlyByWireA380Definition.UiVariableSet.cs`, and the HS787's transponder "Ident" button (`HS787_XpndrIdent` in `HorizonSim787Definition.UiAndHotkeys.cs`).

An action combo or button whose selected value is only "Activate" or "Reset" says nothing of what happened, so it speaks what the action did, once:

- A320, in `FlyByWireA320Definition.HandleUIVariableSet`: the rudder-trim reset combo (`A32NX_RUDDER_TRIM_RESET`, "Rudder trim reset"), the evacuation horn shut-off combo (`A32NX_EVAC_HORN_SHUTOFF`, "Evacuation horn silenced") and the "All Landing Lights On" and "All Landing Lights Off" buttons ("All landing lights on", "All landing lights retracted").
- A380, in `FlyByWireA380Definition.UiVariableSet.cs`: "Signal Cabin Ready" (`A380X_MSFSBA_SIGNAL_CABIN_READY`, "Cabin ready signalled") and the rudder-trim reset combo ("Rudder trim reset").

Not a press at all:

- `FbwEwdWindow` (`MSFSBlindAssist/Forms/FbwEwdWindow.cs`, shared by the A380 and the A32NX): F5 or the Refresh button speaks "E W D refreshed", because a refresh reconciles the text in place and changes nothing the reader would say. The first load and the 2 s timer refresh stay silent.
- The Suspend Hotkeys menu item (`SuspendHotkeysMenuItem_Click`, `MainForm.MenuHandlers.cs`) speaks "Hotkeys suspended" or "Hotkeys resumed", so the pilot knows every hotkey went quiet or came back. A failed re-registration is an error and speaks a warning.

Counter-example. The thrust-lever detent press confirmations ("All thrust levers Idle", "Thrust lever 1 Climb") repeated the value of the combo the pilot had just set, which the screen reader had just read. They sat in the thrust-detent branch of `FlyByWireA320Definition.HandleUIVariableSet` (the Headwind A330 inherits it) and in `FlyByWireA380Definition.UiVariableSet.cs`, and were removed. The A380's background detent announcement in `FlyByWireA380Definition.SimVarUpdate.cs` stays: a combo pick only sends the axis command, so the announcement reads back that the levers actually reached the detent (a state read back after the press, which the reader cannot say), and it also covers a lever moved by hotkey or hardware.

Changed 2026-10-09: the closed list ruled on 2026-10-08 missed four kinds of case within a day; the owner replaced it with this principle, and ruled the thrust-lever detent press confirmations a repeat (removed).

## CORE-8

- Combo double-announce suppression must be GLOBAL, never aircraft-gated: `_uiSetEcho`/`MarkUiSet` plus a wrap that sets `announcer.Suppressed` around `ProcessSimVarUpdate` for any var inside the echo window — gating the wrap to one aircraft (the old HS787-only gate) causes double-announces on every other def that self-announces from inside `ProcessSimVarUpdate`. → CLAUDE.md

## CORE-9

- The echo-window suppression in the wrap around `ProcessSimVarUpdate` (CORE-8) must match on TIME only, never on value — a combo set can write a different encoding than the SDK reads back, so a value-compare silently misses the duplicate. The generic `_uiSetEcho` gate on the monitor path stays value-matched on purpose: it drops only a value matching what the user set inside the window, so a change from another source inside the window still speaks, and a definition whose read-back legitimately lands on a sibling encoding opts a variable out with `SimVarDefinition.UiEchoMatchesAnyValue` (PR #163). → CLAUDE.md

Corrected 2026-10-08: scoped to the wrap; the rule had read as covering the generic gate too, which matches on value by design. Evidence: `MainForm.OnSimVarUpdated` in `MainForm.Announcers.cs`, whose `uiEcho` test around `ProcessSimVarUpdate` checks only the `UiSetEchoSuppressMs` window, while the generic gate also compares the value unless `UiEchoMatchesAnyValue` is set.

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

## CORE-16

Added 2026-10 with the move to path-scoped rules, not carried over from CLAUDE.md. Claude Code loads a `.claude/rules/<area>.md` file only when its Read, Write or Edit tool touches a file its `paths:` globs match; the docs say path-scoped rules "trigger when Claude uses the Read, Write, or Edit tool on a file matching the pattern, not on every tool use". Measured 2026-09-30 and 2026-10-01: a probe rule and `ground-traffic.md` were absent before a Read of a matching file and injected right after it, in the main session, in a general-purpose subagent and in an Explore subagent. Reading or editing through the shell (`cat`, `sed`, `rg`) or the Grep tool loads nothing, so a change made that way sees none of the area's rules. This is a live risk, not a corner case: in Claude Code's auto and bypass-permissions modes the harness tells the model it may read files with `cat`, `head` or `sed` instead of the Read tool (seen in both during review, 2026-10), which is why CORE-16 heads CLAUDE.md's "Everywhere else" list. Before this layout every rule sat in CLAUDE.md and was always loaded, so this is the one habit the layout asks for. The Edit tool requires a Read of the file first, which is what makes it safe.

The rules hook (2026-10-08). Three gaps remained after the move, and `.claude/hooks/rules-hook.ps1`, registered in the committed `.claude/settings.json`, closes them. First, a subagent run with worktree isolation got no area rules at all: measured on 2026-10-08, one that read `Pmdg737DisplayReads.cs` and `TaxiGraph.cs` inside `.claude/worktrees/agent-<id>/` received no rule file, while a normal subagent reading the same two files received six, and the Claude Code docs say such a subagent doesn't load the `.claude/rules/` at its worktree's root. The hook's `read` mode adds that file's rule files after a Read (filtered to agent worktrees) or a Write there; Edit needs no hook, since Claude Code requires a Read first. Second, a review that reads `git diff`, `git show` or `gh pr diff` output loaded nothing; the `diff` mode adds the rule files for the changed paths. Claude Code shows the model only about 10,000 characters of a hook's output (longer output is saved to a file behind a 2 KB preview), so both modes show whole rule files within 9,000 characters and name the rest for Claude to Read (CCT-5). Third, a shell write loaded nothing; the `shell-guard` mode refuses, on the commands it starts on (`sed`, `perl`, `tee`, `cat`, `echo`, `printf`, `python`, `python3`, `py`; in PowerShell `Set-Content`, `Add-Content`, `Out-File`, `python`, `python3`, `py`), `sed -i`, `perl -i`, `tee`, an output redirect (also written without a space, `echo x>f`) and the three cmdlets aimed at a covered file; a Python write sink whose target resolves from literals (CCT-7); and a write through a path held in a variable the same command set to a literal (`f=<path> && sed -i … "$f"`, `$p = '<path>'`). It points to Read and then Edit or Write. It does not see `cp`, `mv`, git itself, a redirect from a command it does not start on (`awk … > f`, `git show X:f > f`), a PowerShell .NET write (`[IO.File]::WriteAllText`), a script in another language (Node, `dotnet`), a Python path built at run time, Python run under another name (`python3.12`, `python.exe`, a full path) or fed through a pipe, or a variable it cannot follow: this rule's instruction still covers those. The `subagent-start` mode also gives the built-in Plan agent, which skips CLAUDE.md, its "Rules for any file" and "Before changing behaviour" sections and the path to Read for the rest. Each `git` or `gh` command, each filtered shell command and each Write costs about 0.2 to 0.5 s (PowerShell start-up, plus reading the rule files when a target is inside a checkout); normal Reads cost nothing. `MSFSBA_RULES_HOOK=off`, set before Claude Code starts, silences the hook. The hook's own rules are CCT-1 to CCT-7, and docs/development.md, "Claude Code hooks", describes each mode and the live checks. Changed 2026-10-09: the guard covers Python and literal path variables.

## Background: the former CLAUDE.md core sections

These sections stood in CLAUDE.md's core until 2026-10; CLAUDE.md now keeps the rules as CORE one-liners. Kept here word for word; where the Screen Reader Announcements text below differs from CORE-7 above, CORE-7 governs.

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
