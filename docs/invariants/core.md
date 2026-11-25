# Rules for any file — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in CLAUDE.md.
The text is verbatim from CLAUDE.md as of `1f37801a` (CORE-18 from PR #160's as of `642f48c1`, where it was CORE-17); a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## CORE-1

- Always build the .sln or pass `-p:Platform=x64`; NEVER build the bare `.csproj` alone — it silently defaults to Platform=AnyCPU and writes to a different output folder (`bin\Debug\...`) than the x64 run path, so the running exe never updates. → CLAUDE.md

## CORE-2

- Keep `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`, `<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>` and `<SelfContained>false</SelfContained>` in `MSFSBlindAssist/MSFSBlindAssist.csproj`. Without the RuntimeIdentifier the build is portable and ships every platform's SQLite binaries. Without the `false`, a build writes to a `win-x64\` subfolder: not the folder the app runs from, not the folder release.yml zips, not the folder the PostBuild xcopy steps target, so a build can succeed while the app, the zip and the copies stay stale. `dotnet publish` writes to `win-x64\publish\`, a folder under the run path. A `win-x64\` folder holding built files predates 2026-08-25 and is stale. Build to, and check the timestamp in, the folder the app launches from.

History. The trap was first seen 2026-06-06, when the csproj had no RuntimeIdentifier: a plain build wrote to `net10.0-windows\`, and `-r win-x64` (or `dotnet publish -r win-x64`) wrote to a separate `net10.0-windows\win-x64\` tree a plain build never touched. Commit 70f28f06 (2026-08-25) pinned the RuntimeIdentifier to cut the shipped output, measured on a Release build at 86.2 MB to 21.2 MB (114 files to 82) and the zip at 41.3 MB to 8.7 MB, and added `AppendRuntimeIdentifierToOutputPath=false` so the output stayed in `bin\x64\<cfg>\net10.0-windows\`. The csproj's comment gives the same reasons.

Measured 2026-10-09, by evaluation only (`dotnet msbuild MSFSBlindAssist/MSFSBlindAssist.csproj -p:Configuration=<cfg> -p:Platform=x64 -getProperty:<name>`, no build):

- As committed, Debug: `OutputPath` is `bin\x64\Debug\net10.0-windows\`, `PublishDir` is `bin\x64\Debug\net10.0-windows\win-x64\publish\`, `RuntimeIdentifier` is `win-x64`. Release: `bin\x64\Release\net10.0-windows\` and `bin\x64\Release\net10.0-windows\win-x64\publish\`, with `AppendRuntimeIdentifierToOutputPath` false and `SelfContained` false.
- `-p:RuntimeIdentifier=win-x64` on the command line, which is what `-r win-x64` sets: the same `OutputPath` and `PublishDir`. So `-r win-x64` changes nothing any more.
- `-p:RuntimeIdentifier=` (the RuntimeIdentifier emptied): `OutputPath` unchanged, `PublishDir` `bin\x64\Debug\net10.0-windows\publish\`.
- `-p:AppendRuntimeIdentifierToOutputPath=true` (the `false` undone): `OutputPath` becomes `bin\x64\Debug\net10.0-windows\win-x64\`.

Corrected 2026-10-09: the rule said `-r win-x64` writes a separate `win-x64\` tree a plain build never touches; since 2026-08-25 the csproj pins the RuntimeIdentifier and the output path, so `-r win-x64` changes nothing, and the trap is removing the `false` (output moves into `win-x64\`) or the RuntimeIdentifier (a portable build). Evidence: `MSFSBlindAssist/MSFSBlindAssist.csproj` (commit 70f28f06) and the `-getProperty` measurements above.

## CORE-3

- The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe. → CLAUDE.md

## CORE-4

- `dotnet build MSFSBlindAssist.sln` builds only four of the `tools/` projects, `tools/PMDGDispatchTester`, `tools/ChangelogBuilder`, `tools/GuideBuilder` (added 2026-10-19 with the user guide, docs/user-guide.md) and `tools/LayoutBundle` (added 2026-10-31; it copies the layout files into a build). The other 17 `tools/*` projects build on their own, never as part of the solution: `Bbs757EfbProbe`, `b757-gen/PanelSweep`, `CDUTest`, `DecimalSweep`, `DistanceUnitsProbe`, `DockingProbe`, `ExitConsistencySweep`, `GsxAirplaneProbe`, `GsxOffsetProbe`, `GsxProfileProbe`, `IFlySdkProbe`, `LandingExitSweep`, `ProgressiveTaxiProbe`, `StandBridgeSweep`, `TaxiAugmentProbe`, `TaxiGuidanceProbe` and `VirtualPilot`. docs/development.md ("Build output and traps") names what each does.

Why it matters. Fourteen of the seventeen link production sources from `MSFSBlindAssist/` with `<Compile Include>`, or reference the app project (`ExitConsistencySweep`, `VirtualPilot`); `CDUTest` links none and copies the main app's `SimConnect.dll`, `Bbs757EfbProbe` links none and copies the app's 757 EFB agent script, and `DecimalSweep` links none: it reads the source files as text and the app's dependencies from its build output. A solution build never compiles them, so a change to a linked source can break one without any solution build showing it. Build the probe you rely on, after a change to a source it links.

Corrected 2026-10-09: the rule named only `tools/CDUTest` and probes "of its style", and CLAUDE.md listed four; 12 projects built on their own. Evidence: `ls tools/*/*.csproj` (14 projects) against the project list in `MSFSBlindAssist.sln`, which held only `PMDGDispatchTester` and `ChangelogBuilder` from `tools/`.

Corrected 2026-10-31: the count of 12 omitted `ExitConsistencySweep` and `VirtualPilot`, and the solution gained two projects, `MSFSBlindAssist.DisplayLayouts` and `tools/LayoutBundle`, nine in all. Evidence: `git ls-files 'tools/*/*.csproj'` lists 18 projects, 4 of them in `MSFSBlindAssist.sln`, so 14 build on their own; a scan of their project files finds an app `<Compile Include>` or project reference in every one but `CDUTest`.

Corrected 2026-11-14: the BlueBird 757 work added two projects that build on their own, `tools/Bbs757EfbProbe` and `tools/b757-gen/PanelSweep`, 16 in all. Evidence: `git ls-files 'tools/**/*.csproj'` lists 20 projects (PanelSweep sits one folder deeper, so `tools/*/*.csproj` misses it), 4 of them in `MSFSBlindAssist.sln`.

Corrected 2026-11-19: PR #349 added `tools/DecimalSweep` ([CORE-21]), which builds on its own, 17 in all. Evidence: `git ls-files 'tools/**/*.csproj'` lists 21 projects, 4 of them in `MSFSBlindAssist.sln`.

Moved from CLAUDE.md, 2026-11-25, unchanged: "The solution builds nine projects, the vPilot plugin and `MSFSBlindAssist.DisplayLayouts` among them; [CORE-4] says which `tools/*` projects build on their own."

## CORE-5

- Sim-facing paths are verified only against a live sim — describe an in-sim test plan in the PR; pure logic belongs in `tests/MSFSBlindAssist.Tests` (CI-enforced). → CLAUDE.md

Moved from CLAUDE.md, 2026-11-25, unchanged: "Sim-facing behavior cannot be unit-tested: when changing SimConnect/UI paths, build, then describe an in-sim test plan in the PR — the human owner of the repo runs it."

Extended 2026-11-25 (owner, 2026-11-25): sim results come from the PR's own build. Every sim result a PR reports for itself comes from that PR's own build: a commit pushed to its branch (its head, an earlier commit, or a merge of main into it), never a build with another open PR merged in, and never main alone. The PR names that build on its `Sim-tested on:` line. CLAUDE.md's line says "its own sim results", not "any sim result it reports", so a labelled "before" result on main stays allowed (below).

Why. Combined builds are fine to use. The owner, 2026-11-25: "It's certainly okay to produce combined builds as I know people want access to all the fixes and features before they ship". The problem is the write-up. The owner, the same day: "if someone runs a combined build, tests, and then writes something up evidence of this runs into the test results, which [skews] them and widens scope" (the owner's message had a typo there; the brackets mark the correction). Skews: the results show behaviour that comes from other PRs, so they do not show what this PR does on its own. Widens scope: the write-up ends up describing other PRs' work. The pattern, in general terms: a PR's live check quotes spoken text from a combined build that reads that way only because another open PR fixes the same output; built alone, the PR says something else.

Combined builds. A combined build is any build with another unmerged PR's commits merged in. Combined builds are allowed, for flying with every fix and feature before they ship and for hunting bugs, but their results are never any PR's evidence. Nobody polices what anyone runs on their own computer: the rule governs what a PR reports for itself.

The PR's own build. It is built from a commit pushed to the PR's branch. Merging main in first is allowed, as long as that merge is pushed. A build with uncommitted edits reports the commit it started from, which does not hold those edits, so test a committed, pushed state. A build of main alone is not this PR's result. A "before" result on main (the problem reproduced on main, then gone on the PR's build) is welcome in the test plan's text, labelled as main's; it is not this PR's result and never goes on the `Sim-tested on:` line. A merge of main that cannot be pushed (a reviewer testing someone else's fork) cannot be told from a combined build, so the check flags it. That is acceptable for an advisory check: the line then says what the build was ("a1b2c3d, the PR's head with main merged in locally"), the check still flags the hash, and the words tell the reviewer what was flown. Testing the PR's head as it is avoids the flag.

Work from the project's private repo. Work built there may land on main as one squashed PR (CORE-24's route). Its private builds' hashes are never on the public branch, and a private hash written in public text would point at the private repo. So such a PR re-tests on a build of its public branch, names that build on the line, and never writes a private build's hash there.

Evidence versus context. Other PRs may be named for context or coordination: to coordinate when both PRs edit the same paragraph, to say that something seen here was fixed elsewhere, to name a PR this one replaces, or to say where a regression came from. Their builds are never this PR's evidence. That is why the check never flags PR numbers in the text: naming another PR is often right, and flagging it would make the check noisy. A bug found on a combined build is reported on the PR (or a new issue) whose code is at fault, and checked again on that PR's own build before that PR cites it.

The `Sim-tested on:` line. The PR template's Test plan carries it, visible, under a comment that says what goes there. Read the build from Help, About, which shows it as "v0.0.0 (build a1b2c3d)": every build carries its commit, which SourceLink appends to the informational version, and `AppVersion.Describe` shows its first 7 characters. Measured 2026-11-25: a Debug build's exe carries the informational version `0.0.0+` followed by the commit's full 40 characters. `AppVersionTests.The_build_carries_its_commit` pins it, so a project-file change cannot quietly break the line. Copy the short hash, or the whole About text, onto the line. Several builds are allowed, one per line or comma-separated, each a build of this PR. Leave the line empty when nothing was tested in the sim; words with no hash ("not tested in the sim") say the same.

The check. The PR scope check (CORE-22 describes it) reads every `Sim-tested on:` line outside HTML comments, so the template's own example never counts; the label is also read when written "Sim tested on". Every token of 7 to 40 hexadecimal characters on those lines that holds at least one digit and at least one letter a-f, in either case, must be the start of a commit in `git rev-list origin/<base>..<PR head>`. The workflow takes the head from the pull request event through its environment, and fetches it when the checkout lacks it rather than flag every hash. A commit only on main is outside that range, so a build of main alone is flagged like a combined one. A value with no such token checks nothing. Otherwise the check names the token: not a commit on this PR's branch, so a combined build with other open PRs merged in, a merge that was not pushed, a build of main alone, or a commit a rebase replaced. Its message says to report this PR's results from its own build, to put a "before" result on main in the test plan's text, or to say on the line which build it was. It is advisory: not a required check, as its message says. Its limits: it cannot prove which build was really flown (someone who tests a combined build and writes the PR's own commit on the line is not caught; review covers that), and a short hash of only digits or only letters goes unchecked, since dates such as 20261125 and hex-only words such as "defaced" are not hashes.

## CORE-6

- `main` is protected — never commit directly to main; always branch + PR. → CLAUDE.md

Moved from CLAUDE.md, 2026-11-25, unchanged: "The `main` branch is protected. Always create a new branch for changes and open a pull request — never commit directly to main."

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
- The Fenix DCDU window (`FenixChannelDcduReadback`, `FenixChannelDcduForm`): the status after an answer key and an info line after a press, read from the DCDU itself about a second later, and the Bright and Dim buttons' level.

"<name> pressed" for an action with no readable state:

- Momentary pushbuttons: `BaseAircraftDefinition.PulseMomentaryLVar` pulses the L:var 1 then 0 and speaks "<name> pressed". The FBW A320 (and so the Headwind A330) calls it from the momentary L-var handler in `FlyByWireA320Definition.HandleUIVariableSet`, which covers both a `RenderAsButton` momentary button and an Idle/Activate action combo (its comment calls the combo "the screen-reader-preferred form for TEST/RESET/DEPLOY actions"); the A380's `_momentaryButtons` (Off/Activate combos) call it from `FlyByWireA380Definition.UiVariableSet.cs`.
- The A380's ECAM-CP keys (`A32NX_BTN_*`, made buttons at the owner's request in 2026-06) speak the same words through `FlyByWireA380Definition.PulseEcpKey`.
- The A320's master warning and master caution buttons and its rudder-trim nudge buttons (`RUDDER_TRIM_LEFT`, `RUDDER_TRIM_RIGHT`), in `FlyByWireA320Definition.HandleUIVariableSet`, and so the Headwind A330's.
- MainForm's fallback for a panel button no definition handles, at two sites in `MainForm.PanelBuilder.cs`.
- The A380 chronometer combos `A32NX_CHRONO_TOGGLE` and `A32NX_CHRONO_RST` ("Chronometer start stop", "Chronometer reset") in `FlyByWireA380Definition.UiVariableSet.cs`, and the HS787's transponder "Ident" button (`HS787_XpndrIdent` in `HorizonSim787Definition.UiAndHotkeys.cs`).
- The Fenix A320's press buttons with no light of their own (2026-10-22): GCS, Service Interphone Override, Avionics Compartment Light, CALLS Mech, All, Fwd, Aft, Mid and Exit, the clock's Chronometer Start/Stop, Chronometer Reset and Date, ISIS Baro Push, ISIS Reset, ECAM ALL, and the pedestal's Pedestal Video, through `ExecuteButtonTransition`'s `confirmPress` in `FenixA320Definition.cs` and `FenixA320Definition.Variants.cs`: once, as the press is sent. A Fenix press button with a light stays quiet, and its light speaks ([FNX-6]).

An action combo or button whose selected value is only "Activate" or "Reset" says nothing of what happened, so it speaks what the action did, once:

- A320, in `FlyByWireA320Definition.HandleUIVariableSet`: the rudder-trim reset combo (`A32NX_RUDDER_TRIM_RESET`, "Rudder trim reset"), the evacuation horn shut-off combo (`A32NX_EVAC_HORN_SHUTOFF`, "Evacuation horn silenced") and the "All Landing Lights On" and "All Landing Lights Off" buttons ("All landing lights on", "All landing lights retracted").
- A380, in `FlyByWireA380Definition.UiVariableSet.cs`: "Signal Cabin Ready" (`A380X_MSFSBA_SIGNAL_CABIN_READY`, "Cabin ready signalled") and the rudder-trim reset combo ("Rudder trim reset").

Not a press at all:

- `FbwEwdWindow` (`MSFSBlindAssist/Forms/FbwEwdWindow.cs`, shared by the A380 and the A32NX): F5 or the Refresh button speaks "E W D refreshed", because a refresh reconciles the text in place and changes nothing the reader would say. The first load and the 2 s timer refresh stay silent.
- A panel's status display (`MainForm.ProcessCmdKey` in `MainForm.Hotkeys.cs`): F5 speaks "Status display refreshed" (queued) for the same reason, from the list or from any control in the panel; its key auto-repeat refreshes nothing and says nothing. The 1 s tick and Ctrl+3's fill of an empty list stay silent.
- The Suspend Hotkeys menu item (`SuspendHotkeysMenuItem_Click`, `MainForm.MenuHandlers.cs`) speaks "Hotkeys suspended" or "Hotkeys resumed", so the pilot knows every hotkey went quiet or came back. A failed re-registration is an error and speaks a warning.

Counter-example. The thrust-lever detent press confirmations ("All thrust levers Idle", "Thrust lever 1 Climb") repeated the value of the combo the pilot had just set, which the screen reader had just read. They sat in the thrust-detent branch of `FlyByWireA320Definition.HandleUIVariableSet` (the Headwind A330 inherits it) and in `FlyByWireA380Definition.UiVariableSet.cs`, and were removed. The A380's background detent announcement in `FlyByWireA380Definition.SimVarUpdate.cs` stays: a combo pick only sends the axis command, so the announcement reads back that the levers actually reached the detent (a state read back after the press, which the reader cannot say), and it also covers a lever moved by other means (a sim key binding or a hardware throttle).

Changed 2026-10-09: the closed list ruled on 2026-10-08 missed four kinds of case within a day; the owner replaced it with this principle, and ruled the thrust-lever detent press confirmations a repeat (removed).

Moved from CLAUDE.md, 2026-11-25, unchanged: "Today's cases: the TFDi MD-11's once-after-settle press confirmation ([MD11-11]); the EFB shell's `announceChange` opt-in ([MD11-26]); the FBW A320, Headwind A330 and FBW A380 button read-back (`GetButtonStateMapping`, `HandleButtonStateAnnouncement`); the value-dialog toggle read-back (`ValueInputForm`, about 1.2 s after a press); the Fenix DCDU window's read-back (the status after an answer key, an info line after a press, the Bright and Dim level); "<name> pressed" for a momentary button or an Idle/Activate action combo with no readable state (`PulseMomentaryLVar`, the A380's `PulseEcpKey`, MainForm's unhandled-button fallback), and for the Fenix's press buttons with no light (`ExecuteButtonTransition`'s `confirmPress`: GCS, Service Interphone Override, Avionics Compartment Light, CALLS Mech/All/Fwd/Aft/Mid/Exit, the clock's Chronometer Start/Stop, Chronometer Reset and Date, ISIS Baro Push, ISIS Reset, ECAM ALL, Pedestal Video); the A380 FCU push/pull value read-out (`OnPanelButtonFired`); the FBW E/WD pop-out's and the panel status display's F5 refresh; the BlueBird 757 tablet window's press, tab, set and typed-field read-backs (`Bbs757EfbPressRunner`), and the menu's hotkey suspend and resume confirmations."

## CORE-8

- Combo double-announce suppression must be GLOBAL, never aircraft-gated: `_uiSetEcho`/`MarkUiSet` plus a wrap that sets `announcer.Suppressed` around `ProcessSimVarUpdate` for any var inside the echo window — gating the wrap to one aircraft (the old HS787-only gate) causes double-announces on every other def that self-announces from inside `ProcessSimVarUpdate`. → CLAUDE.md

A switch whose state comes back on a SEPARATE light names that light in `SimVarDefinition.UiEchoVariable`, and `MarkUiSet` marks it with the picked value too, so the light's change is the same echo, swallowed once (first users: the Fenix ACP reception latches, 2026-10; then Econ Flow and ACT Pump, 2026-10-24). The light must read back the value the switch was set to: its entry goes through the value-matched generic gate. A PUSHBUTTON whose press flips its own `StateVariable` light opts in with `SimVarDefinition.PressTogglesStateVariable` instead (the Fenix EFIS FD and LS buttons, 2026-10-24; the map option buttons, 2026-11-05): the panel's button click calls `MarkStateLightEchoForPress`, which marks the light with `StateVariableAfterPress(cached)`, the toggle of its cached value, through the same `MarkUiSet` and the same value-matched gate, so the other side through a linking option, the First Officer and a virtual-cockpit click still speak; nothing is marked while the light is unread. The A380 ND filter and the HS787 ground power predate it and keep their own echo windows: their read-back is a derived selection, not a copy of the picked value.

## CORE-9

- The echo-window suppression in the wrap around `ProcessSimVarUpdate` (CORE-8) must match on TIME only, never on value — a combo set can write a different encoding than the SDK reads back, so a value-compare silently misses the duplicate. The generic `_uiSetEcho` gate on the monitor path stays value-matched on purpose: it drops only a value matching what the user set inside the window, so a change from another source inside the window still speaks, and a definition whose read-back legitimately lands on a sibling encoding opts a variable out with `SimVarDefinition.UiEchoMatchesAnyValue` (PR #163). → CLAUDE.md

Corrected 2026-10-08: scoped to the wrap; the rule had read as covering the generic gate too, which matches on value by design. Evidence: `MainForm.OnSimVarUpdated` in `MainForm.Announcers.cs`, whose `uiEcho` test around `ProcessSimVarUpdate` checks only the `UiSetEchoSuppressMs` window, while the generic gate also compares the value unless `UiEchoMatchesAnyValue` is set.

## CORE-10

- Never blanket-suppress value-0 resting-state button labels in MainForm — use the opt-in `SuppressRestingButtonState` flag only; some 0-state labels (PMDG 777 "LNAV: Off", HS787 "Baro STD: QNH") are meaningful and must be spoken. → CLAUDE.md

Corrected 2026-10-09: the explanation (and two code comments) said only the FBW momentary-button helpers set the flag. Three families do: the FBW A320 and A380 (the A380's local `Btn`/`PressSilent`/`SeatBtn` helpers and inline defs, the A320's inline ones in `BuildVariables`), the iFly 737 (its `Btn` helper and `McpModeStyleWarning`, whose buttons have no readable resting state), and the TFDi MD-11 (every `Md11Kinds.Button` control `BuildControlVariable` makes). The rule is unchanged: the flag stays opt-in per definition, and MainForm never decides it. Evidence: `SuppressRestingButtonState = true` in `FlyByWireA320Definition.cs`, `FlyByWireA380Definition.cs`, `IFly737MAXDefinition.cs` (`Btn`, `McpModeStyleWarning`) and `TFDiMD11Definition.cs` (`BuildControlVariable`); the only readers are `MainForm.Announcers.cs` and `MainForm.PanelBuilder.cs`.

## CORE-11

- CRITICAL: set `IsConnected = true` BEFORE calling `SetupDataDefinitions()` in SimConnectManager — `StartContinuousMonitoring()` guards on `IsConnected == true`. → CLAUDE.md

## CORE-12

- CRITICAL: never use `TreeView` directly in forms — use `NativeAccessibleTreeView`; the .NET 9/10 UIA `TreeViewAccessibleObject` produces wrong NVDA navigation order. → CLAUDE.md

## CORE-13

- CRITICAL: never hardcode the FBWBA/MSFSBlindAssist database path — always go through `Database/DatabasePathResolver` (`ResolveExistingDatabasePath` for reads, `GetCanonicalDatabasePath` for writes). → CLAUDE.md

## CORE-14

- CRITICAL: every diagnostic log path must be resolved through `Utils/AppLogs.PathFor(...)` into `%APPDATA%\MSFSBlindAssist\logs` — never hand-build a log path. → CLAUDE.md

Corrected 2026-10-09: the rule had no exceptions, but two programs cannot reference the app's `AppLogs`. The vPilot plugin's log still resolves into the canonical logs folder ([VAT-6]); the updater's does not. `MSFSBlindAssistUpdater` has no project reference to the app, and its `Program.Main` writes a startup-arguments log at `Path.GetTempPath()` + `MSFSBlindAssist_Updater_Args.log`, a hand-built `%TEMP%` path that it also names in its invalid-arguments message box. Moving it into `%APPDATA%\MSFSBlindAssist\logs` would need the updater to carry its own copy of the path logic. Evidence: `MSFSBlindAssistUpdater/Program.cs` (the `logPath` and the message box) and `MSFSBlindAssistUpdater.csproj` (no `ProjectReference`).

## CORE-15

- Never hand-build a log write (`File.AppendAllText`/raw path) — every diagnostic log goes through `Utils/Logging/Log` (`Log.Debug/Info/Warn/Error(category,msg)` → debug.log, or `Log.Channel(name)` → named file); `AppLogs.PathFor` is the PATH layer only. → CLAUDE.md

Corrected 2026-10-09: names the two programs that cannot call `Log` and are exempt. The vPilot plugin writes its own `vpilot-plugin.log` ([VAT-6]: it runs in vPilot's .NET Framework process and cannot reference the app's logger). The updater, `MSFSBlindAssistUpdater`, has no reference to the app either, so `Program.Main` writes its startup-arguments log with `File.WriteAllText` to `Path.GetTempPath()` + `MSFSBlindAssist_Updater_Args.log`, and its invalid-arguments message box tells the user that path. Neither is a model for code inside the app, which always goes through `Log`. Evidence: `MSFSBlindAssistUpdater/Program.cs` (`logPath`) and `MSFSBlindAssistUpdater.csproj` (no `ProjectReference`).

## CORE-16

Added 2026-10 with the move to path-scoped rules, not carried over from CLAUDE.md. Claude Code loads a `.claude/rules/<area>.md` file when its Read, Write or Edit tool touches a file its `paths:` globs match; the docs say path-scoped rules "trigger when Claude uses the Read, Write, or Edit tool on a file matching the pattern, not on every tool use". Measured 2026-09-30 and 2026-10-01: a probe rule and `ground-traffic.md` were absent before a Read of a matching file and injected right after it, in the main session, in a general-purpose subagent and in an Explore subagent. The shell loads them only for some command shapes. Measured 2026-10-10 on Claude Code 2.1.294, in two fresh headless sessions started in a worktree and in a desktop-app session there: a Bash command that reads one file with `cat`, `cat -n`, `head`, `tail`, `sed -n` or `nl` loaded that file's rules, also beside an `echo` joined by `;` or `&&`, and also after a leading `cd` to the session's own folder written as the Bash tool writes it (`cd /c/…`, which Claude Code drops before running). Nothing loaded for `grep`, `wc` or `awk`, a pipe (`cat f | head`), any redirect (`> /dev/null`, `2>/dev/null`), two files in one `cat`, a `cd` to another folder or one written `cd C:/…`, PowerShell's `Get-Content` or `cat`, the Grep and Glob tools, a `cp` that wrote a covered path, or a `sed -i` the shell guard refused. So a change made through the shell sees none of the area's rules, and whether a shell read brings them depends on its exact shape; until 2026-10-10 this rule said a shell read never did. This is a live risk, not a corner case: in Claude Code's auto and bypass-permissions modes the harness tells the model it may read files with `cat`, `head` or `sed` instead of the Read tool (seen in both during review, 2026-10), which is why CORE-16 heads CLAUDE.md's "Everywhere else" list. Before this layout every rule sat in CLAUDE.md and was always loaded, so this is the one habit the layout asks for. The Edit tool requires a Read of the file first, which is what makes it safe. Re-measure after a Claude Code update: docs/development.md, "Measuring what loads", gives the method.

The rules hook (2026-10-08). Three gaps remained after the move, and `.claude/hooks/rules-hook.ps1`, registered in the committed `.claude/settings.json`, closes them. First, a subagent run with worktree isolation got no area rules at all: measured on 2026-10-08, one that read `Pmdg737DisplayReads.cs` and `TaxiGraph.cs` inside `.claude/worktrees/agent-<id>/` received no rule file, while a normal subagent reading the same two files received six, and the Claude Code docs say such a subagent doesn't load the `.claude/rules/` at its worktree's root. The hook's `read` mode adds that file's rule files after a Read (filtered to agent worktrees) or a Write there; Edit needs no hook, since Claude Code requires a Read first. Second, a review that reads `git diff`, `git show` or `gh pr diff` output loaded nothing; the `diff` mode adds the rule files for the changed paths. Claude Code shows the model only about 10,000 characters of a hook's output (longer output is saved to a file behind a 2 KB preview), so both modes show whole rule files within 9,000 characters and name the rest for Claude to Read (CCT-5). Third, a shell write loaded nothing; the `shell-guard` mode refuses, on the commands it starts on (`sed`, `perl`, `tee`, `cat`, `echo`, `printf`, `python`, `python3`, `py`; in PowerShell `Set-Content`, `Add-Content`, `Out-File`, `python`, `python3`, `py`), `sed -i`, `perl -i`, `tee`, an output redirect (also written without a space, `echo x>f`) and the three cmdlets aimed at a covered file; a Python write sink whose target resolves from literals (CCT-7); and a write through a path held in a variable the same command set to a literal (`f=<path> && sed -i … "$f"`, `$p = '<path>'`). It points to Read and then Edit or Write. It does not see `cp`, `mv`, git itself, a redirect from a command it does not start on (`awk … > f`, `git show X:f > f`), a PowerShell .NET write (`[IO.File]::WriteAllText`), a script in another language (Node, `dotnet`), a Python path built at run time, Python run under another name (`python3.12`, `python.exe`, a full path) or fed through a pipe, or a variable it cannot follow: this rule's instruction still covers those. The `subagent-start` mode also gives the built-in Plan agent, which skips CLAUDE.md, its "Rules for any file" and "Before changing behaviour" sections and the path to Read for the rest. Each `git` or `gh` command, each filtered shell command and each Write costs about 0.2 to 0.5 s (PowerShell start-up, plus reading the rule files when a target is inside a checkout); normal Reads cost nothing. `MSFSBA_RULES_HOOK=off`, set before Claude Code starts, silences the hook. The hook's own rules are CCT-1 to CCT-7, and docs/development.md, "Claude Code hooks", describes each mode and the live checks. Changed 2026-10-09: the guard covers Python and literal path variables.

## CORE-17

Added 2026-10-14 (PR #303). Two traps misread decimals outside en-US. A bare `double.TryParse(s, out v)` parses in the PC's culture: under sv-SE "29.92" does not parse, and under de-DE it reads 2992, since "." is the de-DE thousands separator. `NumberStyles.Any` with the invariant culture takes "," as a thousands separator: a pilot's Mach "0,85" read 85, so the PMDG 737 and 777 speed dialogs sent 85 knots, an FPA "-2,5" read -25 fpm on the HS787 and PMDG 777, and ActiveSky's QNH "1014,999" (sent in the OS culture, seen live at ESGG under a Swedish Windows on 2026-10-12, #302) read 1014999 hPa.

`Utils/NumberParsing` holds the two parses. `TryParseTyped` is for what the pilot types: either decimal separator, a leading sign, and a possible thousands group refused, never guessed ("10,000", "1.500", "1.013,5"), so the box says the entry is invalid. A group is a lone separator before exactly three digits after a whole part of one or two digits: a whole part of 0 ("0,820", a Mach) or of three digits ("118,275", a COM frequency; as a group it would be 100,000 or more, which no typed field takes) is no group. `TryParseExternal` is for what another program writes (ActiveSky, the MobiFlight WASM): such programs never group, so a lone "," is the decimal (ActiveSky's "1014,999" has three decimals), "," with "." or whitespace inside the number is refused, and the U+2212 minus .NET 5+ writes for sv-SE reads as "-". Both refuse NaN and Infinity, which pass every `v < min || v > max` range check. Machine data already written invariant (SimBrief, GSX profiles, apt.dat, GeoNames, METAR) keeps its plain invariant `NumberStyles.Float` parse. `NumberParsingTests` runs every case under sv-SE, de-DE and en-US.

## CORE-18

Numbered CORE-17 on PR #160 until PR #303 merged first with that number (renumbered 2026-10-15). **CRITICAL:** Never set `TreeView.CheckBoxes = true` on a tree that mixes checkable items with plain headers. In a `TVS_CHECKBOXES` tree, any item that is currently selected or was ever expanded reports MSAA role CHECKBUTTON ("check box" in NVDA) even with its checkbox hidden via state image 0 — and the focused item is always selected, so NVDA announced every visited group header as "check box not checked" (probe-verified 2026-07-16; state-image scrubbing cannot fix the role). Use `NativeAccessibleTreeView.CheckboxStateImages = true` + `ShowCheckBox(node)` per checkable node instead (custom TVSIL_STATE image list, no TVS_CHECKBOXES): headers stay plain tree items, checkable nodes announce checked/not checked from the state image, `TreeNode.Checked`/Space/AfterCheck keep working. Mouse state-image clicks need a NodeMouseClick hit-test handler (the native control only self-toggles under TVS_CHECKBOXES). See the FirstOfficerForm checklist tree for the reference implementation.

## CORE-19

Added 2026-10-19 (PR #319). The pilot's guide lives in `guide/` in this repo; the website (msfsblindassist.com/guide/) and the Help menu's one-page copy are both built from it (docs/user-guide.md). So a PR that changes what a pilot presses, sees or hears (a hotkey, window, setting, spoken text, tone or aircraft) updates the guide in the same PR, like its code and its changelog fragment. When it cannot, or when nothing in the guide changes, its description says so on a line of its own:

- `User guide: needed: <chapter and section>`: the guide is now wrong or incomplete there and this PR does not fix it. These lines are the guide's backlog: search the PRs for "User guide: needed" to find them.
- `User guide: none, because <reason>`: the guide is still right, for example after a fix that restores what it already describes, or an internal change.

A PR that updates `guide/` needs no line. The guide on `main` describes `main`, preview features included: the website publishes the newest release's guide as the released edition and `main`'s as the preview edition, so nothing needs marking as "preview only". The PR template carries the line with both forms in a comment; a PR opened with `gh pr create --body` skips the template, so this rule is what puts the line there.

The check. `.github/workflows/user-guide-line.yml` runs `GuideBuilder check-pr` (`tools/GuideBuilder/PullRequestCheck.cs`, `UserGuideLine.cs` and `UserGuideImpact.cs`, covered by `UserGuideLineTests`) when a PR is opened, pushed to, reopened or its description edited. It asks only when the PR adds an `aircraft`, `feature` or `improvement` fragment or changes a file in `MSFSBlindAssist/HotkeyGuides/`: of the 221 fragments on `main` on 2026-10-19, 59 were in those categories and 125 were fixes, most of which restore behavior the guide already describes. It passes when the PR changes a file in `guide/` or answers with the line. It reads the line outside HTML comments, so the template's own examples never count, and refuses a placeholder left in (`<reason>`), a bare "none" or "needed", and any other text. It is advisory: not a required check, as its message says. The rule asks on every pilot-visible PR; the check covers the commonest cases and is not the definition.

Why a line to fill in, not a checkbox: most PRs here are written with Claude Code, and a checkbox can be ticked, or left in as boilerplate, without anyone deciding anything. A line that names the place still to fix or the reason none is needed records a decision a reviewer can check. The model is the Kubernetes PR template's required release-note block, with "NONE" as the explicit opt-out.

History. The first draft of this rule (2026-10-19, same PR) asked for a link to a pull request in the website's repo, where the guide then lived; the guide moved into this repo before it merged.

## CORE-20 (retired: never used on main; kept unused because a closed pull request claimed it and a commit message on main cites it for the rule now numbered CORE-21)

CORE-20 was never a rule on main and stays unused, because a pull request closed unmerged on 2026-11-24 claimed it and a commit message on main cites it for the decimal-format rule now numbered CORE-21, so reusing it would make that citation point at a different rule; the PR scope rules therefore start at CORE-22.

## CORE-21

Added 2026-11-13 (PR #349). A plain interpolated string formats in the PC's culture, so `$"COM1 active {value:F3}"` reads "COM1 active 118,300" on a German or Swedish Windows, `{mach:0.00}` "Mach 0,78", and `{feet:N0}` "12.345 ft", which a screen reader says as twelve point three four five. The app's text is English and CI runs under en-US, so nothing showed it: the sweep that added this rule found about 290 such phrases, in every aircraft's announcements, display values and system-display rows, in MainForm, the SimConnect readouts, the weather radar, the SimBrief planner and the EFB. Two were not speech at all: `WeatherService.BuildBbox` and `BuildWindsUrl` put coordinates into web addresses, so under a comma-decimal culture the bounding box became eight numbers and the winds address `latitude=48,3538`, and the radar got no PIREPs or winds aloft (SIGMETs come from two fixed addresses and were never affected). The PIREP box also had a fault of its own, in every culture: it was longitude first, while the aviationweather.gov Data API reads `bbox` latitude first ("lat0, lon0, lat1, lon1"), so it asked for another part of the globe; it is latitude first now and clamped at the poles and the date line (`WeatherServiceAddressCultureTests`).

The rule: a number with a decimal or a group separator that reaches the pilot, or another program, is formatted with the invariant culture, `FormattableString.Invariant($"…")` or `string.Create(CultureInfo.InvariantCulture, $"…")`, as `ActiveSkyFormatting`, `LocationPhrases` and `TaxiBriefingRenderer` already did (an invariant `N0` writes "12,345" everywhere). A whole number needs nothing (`{x:F0}`, `{x:000}`), and a log line may keep the PC's culture.

Two tests hold it. `DecimalFormatSourceTests` walks every source file of the four shipped projects (the app, `MSFSBlindAssist.DisplayLayouts`, the updater and the vPilot plugin) as C# and fails on a decimal format with no invariant call around it, naming the file and line; a log call (`Log.Debug`, a `LogChannel`, `RolloutDiag`, `DiagLog`) is its only exemption: a new log helper is added to its `LogHelpers` and a new logger's name to its `LoggerName`, never a speech helper, and `WriteLine`, `Error` and the like count only when called on a logger. It cannot see a double interpolated with no format at all (`$"{value}"`), a bare `G` or `R` (they write what no format writes) or a format held in a variable (`x.ToString(fmt)`), which is what `SpokenDecimalCultureTests` is for: it feeds every variable of every aircraft through `TryGetDisplayOverride`, `ProcessSimVarUpdate` and the system-display rows under de-DE and holds the result against en-US.

Not covered, on purpose: the minus sign of a phrase with only whole numbers. Under sv-SE a negative whole number is written with U+2212 ("−1500"), which screen readers say as minus, so such a phrase is left in the PC's culture; `Pmdg737McpAnnouncementTests` pins that for the 737's V/S. A phrase wrapped for its decimals writes every number in it with an ASCII minus, whole ones too (MainForm's generic "<name>: <value>" and "<name> set to <value>", the 737's generic announcements), which screen readers say the same way: under sv-SE both forms are heard. That holds for speech only: a negative number written into calculator code is formatted with the invariant culture even when it is whole (`FcuValueEntry.VerticalSpeedSetCode` for the FBW V/S window, the A32NX landing-elevation knob, the FBW catch-all L:var writes), because the RPN parser rejects U+2212; [VAR-5] covers the doubles.

A semantic sweep of the four shipped projects on 2026-11-19 (every interpolation hole, `ToString()` and string concatenation of a `double`, `float` or `decimal` with no format, read with the compiler's types) found thirteen such numbers the scan cannot see and the culture tests do not reach, in MainForm, the HS787 EICAS window, the visual-guidance callouts, the MD-11 V/S dialog and the FBW flaps and temperature read-backs; they are wrapped. The sweep is `tools/DecimalSweep` (docs/tooling.md, section 7): run by hand after adding a spoken or shown number, never by CI, since it reads types from the app's build output. It leaves out a log call, an invariant call and a hole that can only hold a whole number (`{Math.Round(nm)}`), and the tree is kept at "0 to review", so any line it prints is a new number. The parsing twin of this rule is [CORE-17].

## CORE-22

- One aircraft or one feature per branch and PR. Variants share one only when they are one maker's models of one family (the PMDG 737 series) or an add-on built from another's code with identical L:vars and methods (the Headwind A330 on the FBW A32NX). → CLAUDE.md

Added 2026-11-25, the first of the PR scope rules (CORE-22 to CORE-25, with CORE-5's own-build clause). The owner approved their design on 2026-11-18 and its amendment on 2026-11-25; each decision below carries its date.

Why. An aircraft branch can carry two aircraft of different families plus an unrelated sim-wide feature. Then none of its parts can be reviewed, flown or merged without the others: review and in-sim tests of one piece are blocked by the others, and a fault in any piece holds back all of them.

How to apply. Before starting a branch, name the one aircraft or the one feature it delivers, and keep everything else off it. The tests below decide what counts as one. The PR template's Scope section asks what the PR delivers, and carries the `Variants:` line as an example inside a comment.

Variants (owner, 2026-11-18). The owner's test: "the methodologies like L:vars and support have to be identical". Variants may share one branch and PR only when they are either:

- one maker's models of one aircraft family: the whole PMDG 737 series, or the Fenix A320 series; or
- an add-on built from another add-on's code whose L:vars and control methods are identical: the Headwind A330 on the FBW A32NX, "an exact replica".

A PR that carries variants says so in its description, on a line of its own: `Variants: <family>`.

Subclassing does not make a variant. One aircraft definition built as a subclass of another, of a different family, fails both tests when it drives the sim differently: for example, one aircraft through the sim's input events and the other through direct L:var writes, with a display reader of its own. The base class shares code, not the way each aircraft is driven, so each still needs its own review and its own in-sim test.

One feature. One feature is what a pilot would read as one changelog entry. A PR that needs fragments for two different things (two aircraft, or an aircraft plus an unrelated sim-wide feature) is two PRs. A feature that works across aircraft is one feature.

What aircraft support includes. An aircraft's definition, panels, hotkeys, hotkey guide, its own First Officer profile, docs, guide chapter, tests and tools: all of it is one aircraft, and it belongs on that aircraft's branch. A feature that is part of the aircraft's support is described in the aircraft's changelog fragment, not in a feature fragment of its own.

Fixes (owner, 2026-11-18). Fixes may share a PR when they are on one aircraft or one feature, so one review and one in-sim test covers them. Unrelated fixes across aircraft are separate PRs. A fix in code several aircraft share is one fix; CLAUDE.md's "Fix the aircraft the request is about" says when it covers the other aircraft in the same PR. Docs and tooling PRs are their own kind, separate from aircraft, feature and fix PRs.

Branches, not only PRs (owner, 2026-11-18). This rule, like CORE-23 to CORE-25, governs a feature branch of this repo from the moment it is created, whether or not a PR is open for it. The PR scope check sees only PRs; before one is opened, CLAUDE.md and the branch-creation warning (CCT-8) are what reach a contributor, and `CONTRIBUTING.md` and these full texts say so. The rules govern pull requests to this public repo. The project's private repo cuts its branches from public main and takes main in, so they carry CLAUDE.md and these lines; it keeps its own hub-and-branch model, and how these rules apply there is set in that repo.

The PR scope check. `.github/workflows/pr-scope.yml` runs `GuideBuilder check-scope` (`tools/GuideBuilder/ScopeCheck.cs` and `ScopeLines.cs`, covered by `PrScopeCheckTests` and `PrScopeWorkflowTests`) when a PR is opened, pushed to, reopened or its description edited. It uses the plain `pull_request` trigger only (UPD-10's full text forbids `pull_request_target`), and the description reaches its script only through the environment. It reads the `Variants:`, `Planned in:` and `Sim-tested on:` lines outside HTML comments, so the PR template's own examples never count, with or without a list marker or bold around the label; an empty value or a lone `<placeholder>` is no answer. It checks CORE-22, CORE-23, CORE-25 and CORE-5's build line, and when nothing is flagged it passes and says what it checked. It is advisory: not a required check, as its message says; a GitHub admin can make it required later. For this rule it flags a PR that:

- adds more than one aircraft code to the factory switch in `MSFSBlindAssist/MainForm.AircraftSwitch.cs` (arms of the form `"CODE" => new SomeDefinition(),`) with no `Variants:` line; reordered or reformatted arms add no aircraft;
- adds an `aircraft` changelog fragment together with a `feature` fragment, even with a `Variants:` line: a variant family explains several aircraft, not an aircraft plus a feature, so the message says to describe a feature that is part of the aircraft's support in the aircraft's fragment, or to split the PR;
- adds two `aircraft` fragments with no `Variants:` line.

The rule reaches further than the check: a second aircraft that adds no switch arm, or one fragment that describes two features, is still two PRs, and review applies the rule there.

## CORE-23

- A follow-up (work its parent's plan or PR description does not list) starts from main only after the parent merges; a PR into any branch but main says where that plan is on a `Planned in:` line. → CLAUDE.md

Added 2026-11-25 with CORE-22 (the PR scope rules).

Why. A new feature opened as a PR into another feature's unmerged branch (a stacked follow-up) cannot land before its parent does, and every change to the parent under review can break it.

How to apply. Work goes onto an unmerged branch only when that branch's plan or its PR description already lists it (planned scope). Anything else is a follow-up: it waits until its parent is in main, then starts from a freshly fetched main (CORE-24) as a PR into main.

Planned scope (owner, 2026-11-18). The owner chose planned scope over a strict main-only rule, which would have ended the planned multi-person branch: contributors pushing planned pieces straight to one unmerged branch, as for large aircraft rework. Such a branch stays allowed, and so does a PR into it, because its pieces are in its plan.

Where a plan lives (owner, 2026-11-18). A plan counts only where a reviewer, CI or another contributor can read it:

- a short scope list committed on the branch, `docs/<topic>/scope.md`: the stages the branch will deliver, a few lines, deleted before the final merge;
- the PR description;
- a plan kept in the project's private repo, cited as "the private repo's plan", never by name or link (CORE-25).

A staged branch whose stage plans live only in git-ignored files on one computer, with no PR yet, shows nobody that its later stages were planned: commit the scope list, or open the PR, before building later stages on it.

Who may add to a plan (owner, 2026-11-18). Once the branch is under way, a maintainer approves each addition to its plan, and the plan records it.

Not a follow-up: a fix or review change to an open PR's own scope. It goes to that PR, through its author, never as a PR stacked on it.

The `Planned in:` line. A PR whose base is not main says where its plan is, on a line of its own in the description: `Planned in: <link>`, or `Planned in: the private repo's plan`, never naming that repo. The PR template carries the line as an example inside a comment.

The check (CORE-22 describes the workflow). For this rule it flags:

- a PR whose base is not `main` and whose description has no `Planned in:` line (a lone `<placeholder>` is no answer; a `<https://…>` link is one);
- a PR into main stacked on another open PR into main: the two share commits that are not on main (a merge base of the two heads is not on main), as when a follow-up is retargeted to main while still carrying its parent's unmerged commits. The message says to rebase on fresh main, so the PR carries only its own commits, or to retarget it at the other PR's branch and add a `Planned in:` line.

What the stacked check compares: only other open PRs into main, and of those only the ones with a lower number, using each PR's head rather than its merge commit, and every merge base of the two heads. So only the newer PR of a stacked pair is flagged (a follow-up is opened after its parent), and the parent is not flagged, even after review fixes move its head on. A planned child PR into this PR's branch has another base, so it is never compared.

The check's reach, accepted when the check was designed. A `pull_request` workflow runs only when the PR's merge commit holds it. A PR between two branches cut before the check landed gets no check until one side takes in main; review covers it meanwhile.

Branches, not only PRs (owner, 2026-11-18). Like CORE-22, this rule governs a feature branch of this repo from the moment it is created, whether or not a PR is open for it. The project's private repo keeps its own branch model; how these rules apply there is set in that repo.

## CORE-24

- Start every branch from a freshly fetched main, never from a local experiment, another feature branch or uncommitted work; push commits as the work happens, never as bulk drops from a private copy (work built in the project's private repo, whose history stays there, excepted). → CLAUDE.md

Added 2026-11-25 with CORE-22 (the PR scope rules).

Why. The habit behind bundled branches, in the owner's words (2026-11-18): a contributor makes a local build to experiment, then branches and builds on that local build instead of switching to main first to start a new feature. The new branch then carries the experiment's other aircraft, tools and half-finished pieces. When the work was built in a larger private copy (which held still more aircraft) and moved over as a few bulk commits, the branch ends up citing files, tools and tests that are not there (CORE-25), and none of its parts can be reviewed, flown or merged without the others (CORE-22).

How to apply. Start from a freshly fetched main. In a clone of this repository:

```bash
git fetch origin
git switch -c <branch> origin/main
git push -u origin <branch>
```

In a clone of your own fork, add this repository once as `upstream`, start each branch from its main, and push the branch to your fork:

```bash
git remote add upstream https://github.com/oasis1701/msfs-blind-assist.git
git fetch upstream
git switch -c <branch> upstream/main
git push -u origin <branch>
```

A plain `git switch -c <branch>` starts from wherever the checkout stands, a local experiment or another feature branch, and carries its commits; uncommitted edits travel with it too. Commit or set aside other work first, and name the start point. Planned work on an unmerged branch starts from that branch instead (CORE-23).

Push as you go. Commit and push as the work happens, never as a few bulk commits dropped in from a private copy.

The private-repo route (owner, 2026-11-18). Work built on a branch of the project's private repo may land on public main as one squashed PR, because its history lives there and cannot be published. The PR says so, on the PR template's line asking where the work was built. A contributor's own local copy never qualifies. Such a PR re-tests on a build of its public branch and never writes a private build's hash (CORE-5).

Re-landing a bulk drop. When a branch already holds a bulk drop, re-land each piece from main as a series of small commits that each build and pass the tests, keeping the original author's credit (as the commit's author, or in a `Co-authored-by:` trailer) and not back-dating them. Rebuild from scratch only the parts that fail CORE-25 or a provenance check.

Why not from scratch. Rebuilding the work from scratch is not more ethical or secure in itself: review of the final code is what makes it safe, a rewrite made while reading the original is still the original's work, and it throws away in-sim measurement already done on the original.

The PR template and the warning. The PR template's Scope section asks where the work was built: on this branch from a freshly fetched main, or in the project's private repo. It is a line to fill in, not a checkbox: CORE-19's full text found that a checkbox records no decision. In a Claude Code session, the rules hook's `branch-check` mode (CCT-8, docs/invariants/claude-tooling.md) warns, and never blocks or asks, when a git command creates a branch that does not start at the main repository's main or when main has not been fetched recently, and tells a clone of a fork to add this repository as `upstream`.

How the rules reach a contributor. CLAUDE.md and the hook load from a contributor's own checkout, so contributors pull main once after the rules land. A session working from a stale local copy or a private experiment does not see them; only the PR-time layers reach it: `CONTRIBUTING.md` on the new-pull-request page, the PR template and the check.

Branches, not only PRs (owner, 2026-11-18). Like CORE-22, this rule governs a feature branch of this repo from the moment it is created, whether or not a PR is open for it. The project's private repo keeps its own branch model; how these rules apply there is set in that repo.

## CORE-25

- What a branch adds is reachable: each file, tool, type or test its code depends on or cites as proof is in this repo or the project's private repo (never named or linked in public text), and each generated or trained file rebuilds from committed tools and data, its source named. → CLAUDE.md

Added 2026-11-25 with CORE-22 (the PR scope rules).

Why. The pattern it stops, in general terms: a trained file whose training captures were never committed, and code citing capture tools, training tools and tests that do not exist, such as a README naming a capture tool and a test class that were never committed. Nobody can rebuild such a file, check where its data came from or run the tests it cites, so nobody can review it.

How to apply. Everything a branch adds can be rebuilt and checked: generated or trained files rebuild from tools and data committed to this repo or the project's private repo; each data source is named; and every file, tool, type or test the code depends on or cites as proof exists in one of the two.

Cited as proof, or one-off. These must exist:

- a test a comment says pins the code;
- a tool a README says regenerates a shipped file;
- a type or file the code uses.

A measurement script named only as the source of a recorded result is fine when the text marks it as one-off and not kept: the line says "one-off" and "not kept", for example "measured with a one-off sweep, not kept". That is the owner's practice since the lean CLAUDE.md work: one-off scripts stay out of the tree, and their output is cited.

It applies to what a branch adds. Main already holds things this rule would not allow for new work, and they stay; they are named here so no session "fixes" them in passing:

- about 20 one-off tools cited but deliberately never kept, among them `tools/AmdbExitSweep`, `LandingExitSideSweep`, `UnchartedCrossingProbe`, `LineupPointSweep` and `SignHoldProbe`, in the taxi code and in the runway-holds and runway-entry full texts;
- `EWDMessageLookupA380.cs`, generated by the git-ignored `tools/_fbw_ecam/` workspace (docs/development.md).

Vendored binaries. Third-party binaries vendored into the repo (the SimConnect and Tolk DLLs) are not "generated or trained": they name their source, and nothing rebuilds them.

Provenance: name where each data file came from. Reading a running add-on read-only (its module memory, its data channel, its files on disk) is established practice: the PMDG 737 and BlueBird 757 display readers and the Fenix channel work do it, with their sensitive details in the private repo. The line is never breaking a protection the add-on deliberately puts in place, such as encrypted files or blocked inspection (the line the project already holds where an add-on protects itself). Owner, 2026-11-18: the first wording, "never use anything that requires getting around an add-on's protection", was too strong for those cases.

Checkable. "Checkable" means checkable by maintainers: in this repo, or in the private repo for what is kept there, never only on one contributor's computer.

The project's private repo. It holds what the owner keeps out of public: add-on internals such as WASM module layouts and addresses, the layout generator, and closed-channel captures and tools. It counts as part of the project, unlike one contributor's private build, because:

- the owner controls who can read it;
- work there has its own pushed history that maintainers can review;
- public code reaches it only through a documented contract that it checks (the layout file format in `MSFSBlindAssist.DisplayLayouts`, `LayoutBundler` at release, `LayoutStore` at run time), and falls back out loud when the files are missing;
- public docs describe it without naming it.

What goes there is the owner's decision. The default: anything read from inside an add-on (memory, closed channels, its package files) is a candidate; what any pilot can see from outside (SimConnect, L:vars, input events, screenshots of a display) is public.

How public text refers to it: never by its name or a link, and its contents only as far as the public code's contract needs, as UPD-10 does. UPD-10 and the release workflows name the store's `layouts` branch, one full text cites "the recipe in the private repo", and the PMDG 737 work names `LayoutGenerator.exe`; that stays the line. Knowing the name would not let anyone in (GitHub shows a private repo as not found), but there is no reason to advertise it. The release workflows reach it only through the `LAYOUTS_REPO` secret (UPD-10). A commit hash from it is never written in public text either: it would point at the private repo (CORE-5). Public text says "the project's private repo", "the private repo" or "the layouts store".

The sanctioned private-repo work already on main (the PMDG 737 and BlueBird 757 memory readers' layout files, and the Fenix channel tools) is what this rule allows: it is never flagged as a violation, and sensitive material is never pushed public to satisfy this rule.

The check (CORE-22 describes the workflow). For this rule it reads the lines a PR adds under `MSFSBlindAssist/`, under `tests/`, or in a tool's README (`tools/<name>/README.md`), and flags:

- a `tools/<name>` path the PR's tree does not hold;
- a `<Name>Tests` name that no code line of a `.cs` file in the PR's tree holds (a type, a method, a property or field, a call). A comment does not count, so a dangling "pinned by …Tests" comment never vouches for itself.

A name the base branch's tree already mentions passes: a moved or split file shows its old lines as added, and main keeps citing one-off tools it never kept. A line that says "one-off" and "not kept", or names the "private repo" or the "layouts store", is not scanned. Docs outside a tool's README are not scanned; the rule still applies to them, and review covers them.

Branches, not only PRs (owner, 2026-11-18). Like CORE-22, this rule governs a feature branch of this repo from the moment it is created, whether or not a PR is open for it. The project's private repo keeps its own branch model; how these rules apply there is set in that repo.

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

**Resting-state button labels are suppressed ONLY via the opt-in `SimVarDefinition.SuppressRestingButtonState`** (set by the FBW momentary helpers `Btn`/`PressSilent`/`SeatBtn` + the inline cabin-ready/chrono defs, and, since the iFly 737 and TFDi MD-11 were added, by the iFly's `Btn`/`McpModeStyleWarning` and the MD-11's push-button controls in `BuildControlVariable`). Never blanket-suppress value-0 labels in MainForm: PMDG 777 MCP "LNAV: Off" and HS787 "Baro STD: QNH" are meaningful states a blind user needs (PR #85 finding M4).

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
