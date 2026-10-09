# Rules for any file — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in CLAUDE.md.
The text is verbatim from CLAUDE.md as of `1f37801a` (CORE-17 from PR #160's as of `642f48c1`); a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

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

- NEVER announce button presses, combo/dropdown value changes, or any direct UI interaction in panel controls — screen readers already announce them; ONLY announce numeric input confirmations, validation errors, and background (non-user-triggered) state changes. The scoped exceptions are the TFDi MD-11's once-after-settle press confirmation, the EFB shell's `announceChange` opt-in (the paragraph under Screen Reader Announcements), the FBW button read-back and the momentary-press confirmation below — never a licence to announce presses elsewhere. → CLAUDE.md

The button read-back is the third exception, ruled a sanctioned one by the repo's owner on 2026-10-08 when the review of the rules split found it in the code. After a panel button click (`MainForm.PanelBuilder.cs`) or a hotkey press (`MainForm.Hotkeys.cs`) whose event a definition maps in `GetButtonStateMapping`, `MainForm.HandleButtonStateAnnouncement` waits 300 ms, force-reads the mapped state variable and speaks it once (`pendingStateAnnouncements`, consumed by `AnnounceVariableState` in `OnSimVarUpdated`). Only the FBW A320, the Headwind A330 (the A320's map plus its baro STD rows) and the FBW A380 map any button; every other definition returns an empty map. The reason is MD11-11's: the screen reader speaks the button, never what the press did (an autopilot that engaged or did not, a managed mode taken), and the read-back is the pilot's only confirmation of it. A new mapping is a deliberate choice for a button whose effect the pilot cannot otherwise hear, never a way to echo presses.

The momentary-press confirmation is the fourth exception, ruled a sanctioned one by the repo's owner on 2026-10-08 when the final review of that correction found it in the code. A momentary pushbutton with no readable state confirms its own press ONCE, as it is pressed: "<name> pressed" from `BaseAircraftDefinition.PulseMomentaryLVar`, the FBW A380's `PulseEcpKey` (its ECAM-CP keys, made buttons at the owner's request in 2026-06), the FBW A320's master warning/caution and rudder-trim buttons (and so the Headwind A330's), and MainForm's fallback for a panel button no definition handles (`MainForm.PanelBuilder.cs`); the A380 chrono's "Chronometer reset" and "Chronometer start stop" and the HS787's "Ident"; and the FBW A380's FCU push/pull panel buttons, which speak the resulting selected or managed value through `OnPanelButtonFired` exactly as their hotkeys do. The reason is the read-back's: such a button has no state the screen reader can speak, so nothing else tells the pilot the press registered. It covers a momentary button's own press only — never a combo, a latching control or a control that shows its state, which the screen reader already speaks.

Corrected 2026-10-08: the exception list names the third and fourth exceptions, and no longer calls the first two "the ONE scoped exception". Evidence: `MainForm.HandleButtonStateAnnouncement` in `MainForm.Announcers.cs`, its callers in `MainForm.PanelBuilder.cs` and `MainForm.Hotkeys.cs`, and the `GetButtonStateMapping` overrides of `FlyByWireA320Definition`, `HeadwindA330Definition` and `FlyByWireA380Definition`; for the fourth, `BaseAircraftDefinition.PulseMomentaryLVar`, `FlyByWireA380Definition.PulseEcpKey`, the "pressed" fallbacks in `MainForm.PanelBuilder.cs` and `FlyByWireA380Definition.OnPanelButtonFired`.

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

The rules hook (2026-10-08). Three gaps remained after the move, and `.claude/hooks/rules-hook.ps1`, registered in the committed `.claude/settings.json`, closes them. First, a subagent run with worktree isolation got no area rules at all: measured on 2026-10-08, one that read `Pmdg737DisplayReads.cs` and `TaxiGraph.cs` inside `.claude/worktrees/agent-<id>/` received no rule file, while a normal subagent reading the same two files received six, and the Claude Code docs say such a subagent doesn't load the `.claude/rules/` at its worktree's root. The hook's `read` mode adds that file's rule files after a Read (filtered to agent worktrees) or a Write there; Edit needs no hook, since Claude Code requires a Read first. Second, a review that reads `git diff`, `git show` or `gh pr diff` output loaded nothing; the `diff` mode adds the rule files for the changed paths. Claude Code shows the model only about 10,000 characters of a hook's output (longer output is saved to a file behind a 2 KB preview), so both modes show whole rule files within 9,000 characters and name the rest for Claude to Read (CCT-5). Third, a shell write loaded nothing; the `shell-guard` mode refuses `sed -i`, `perl -i`, `tee`, output redirects, and `Set-Content`, `Add-Content` or `Out-File` aimed at a covered file, pointing to Read and then Edit or Write. It cannot see a write a script makes (Python, `dotnet`), `cp`, `mv`, git itself, or a PowerShell `>` outside those cmdlets: this rule's instruction still covers those. The `subagent-start` mode also gives the built-in Plan agent, which skips CLAUDE.md, its "Rules for any file" and "Before changing behaviour" sections and the path to Read for the rest. Each `git` or `gh` command, each filtered shell command and each Write costs about 0.2 to 0.5 s (PowerShell start-up, plus reading the rule files when a target is inside a checkout); normal Reads cost nothing. `MSFSBA_RULES_HOOK=off`, set before Claude Code starts, silences the hook. The hook's own rules are CCT-1 to CCT-4, and docs/development.md, "Claude Code hooks", describes each mode and the live checks.

## CORE-17

**CRITICAL:** Never set `TreeView.CheckBoxes = true` on a tree that mixes checkable items with plain headers. In a `TVS_CHECKBOXES` tree, any item that is currently selected or was ever expanded reports MSAA role CHECKBUTTON ("check box" in NVDA) even with its checkbox hidden via state image 0 — and the focused item is always selected, so NVDA announced every visited group header as "check box not checked" (probe-verified 2026-07-16; state-image scrubbing cannot fix the role). Use `NativeAccessibleTreeView.CheckboxStateImages = true` + `ShowCheckBox(node)` per checkable node instead (custom TVSIL_STATE image list, no TVS_CHECKBOXES): headers stay plain tree items, checkable nodes announce checked/not checked from the state image, `TreeNode.Checked`/Space/AfterCheck keep working. Mouse state-image clicks need a NodeMouseClick hit-test handler (the native control only self-toggles under TVS_CHECKBOXES). See the FirstOfficerForm checklist tree for the reference implementation.

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

`Utils/AppLogs` remains the PATH authority underneath `Log` — it is not bypassed, just no longer called directly for writing. Every diagnostic log still lives in ONE folder, `%APPDATA%\MSFSBlindAssist\logs` (Roaming), and every log path is still resolved through `Utils/AppLogs.PathFor("name.log")` (now internally, by `Log.Channel`) — never hand-build a log path. Historically logs were scattered (taxi/rollout logs in the Roaming root `%APPDATA%\MSFSBlindAssist\`, the startup log in `%TEMP%`, GSX/docking logs in `%LOCALAPPDATA%\MSFSBlindAssist\logs`), which made "send me your logs" support unanswerable. They were first unified under Local, then moved to Roaming so EVERYTHING the app owns — settings, databases, AND logs — lives in one `%APPDATA%\MSFSBlindAssist` tree. `AppLogs.MigrateLegacyLogs()` (called once at startup in `Program.Main`) best-effort sweeps BOTH legacy locations (Roaming-root `*.log` files AND the entire former Local logs folder) into the canonical folder and removes the Local folder once empty. Current files: `debug.log` (the new app-wide `Log.Debug/Info/Warn/Error` sink), `taxi_guidance.log`, `taxi_router.log`, `landing_exit.log`, `takeoff_assist.log`, `gsx.log`, `gsx-gate-select.log`, `docking.log`, `docking-aircraft.log`, `sayintentions.log`, `center_pumps.log`, `startup.log` (truncated per launch), `input_events.txt`. The tester instruction is always: **Windows+R → `%APPDATA%\MSFSBlindAssist\logs`** (or just `%APPDATA%\MSFSBlindAssist` to grab settings + databases + logs in one go).
