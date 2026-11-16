# BlueBird 757 gaps: plan index, order, file ownership and parallel workflow

**Spec:** `docs/b757/spec-gaps.md` (read it first; section 4 holds the rulings, section 8 the owner's answers).
Background: `docs/b757/spec.md`, `docs/b757.md`, rules `.claude/rules/b757.md`.
**Branch:** `B757` (kn4iee's fork). Every plan lands on this one branch; one pull request to `main` at the end
(plan 16; or two, if the owner wants the early plans released first). Contributors rebase onto `B757` and push
straight to it ("Workflow for one plan" below: the suite runs after the rebase, since pushes to a non-main branch get
no CI). No per-plan pull requests, changelog fragments or "User guide:" lines: plan 16 writes them.
**Where these documents live:** `docs/b757/` on `B757`. Plan 16 deletes the folder before the pull request merges.

## Order: fixes and essentials first, new features later (owner, ruling R68)

- **01, first and alone:** the foundation (seams, test files split, the CDU service, one fix).
- **02-08, early: fixes and essentials.** Fixes first inside each plan. They run in parallel and never share a file.
- **09-15, later: new features.** Each starts when the early plans it takes files from have landed (column "After");
  they run in parallel with each other and never share a file.
- **16, last:** integration.

| # | Plan | Kind | After |
|---|---|---|---|
| 01 | `01-foundation.md`: seams, test files split by owner, the definition owning the tablet client, `Bbs757CduPages` and `Bbs757CduTitle`, shared keys, guard fields, the snapshot tool; fix: no glideslope call-out from the sim's NAV 1 | foundation | — |
| 02 | `02-control-map.md`: fixes (position names, labels and kinds, guarded rows, chrono, controls the cockpit lacks, a replay test); essentials (gear lever with OFF, flap lever, altitude intervene per a flight check) | early | 01 |
| 03 | `03-panel-writes.md`: fixes (guarded writes for emergency lights and stab trim cutouts, fire handles relock); essential (landing altitude) | early | 01 |
| 04 | `04-radios.md`: fix (output N reads the 757's radios); essentials (COM panel and announcements, transfer, Ctrl+N, ILS per a flight check) | early | 01 |
| 05 | `05-mcp.md`: essentials (Alt keys, Speed Intervene, blank speed box, Mach, MCP and autoflight announcements, altitude intervene) | early | 01 |
| 06 | `06-readouts-announcements.md`: fix (EXT PWR reads back); essentials (flap speeds, D and Shift+D, autoland status and the other essential announcements) | early | 01 |
| 07 | `07-efb-ground-power.md`: essential (connect and disconnect ground power, announced) | early | 01 |
| 08 | `08-cdu-fixes.md`: fix (the CDU window keeps the cursor on the pilot's line); faster scratchpad typing at a measured key rate, a missed key said | early | 01 |
| 09 | `09-cdu-presentation.md`: the CDU window for speech and braille | later | 08 |
| 10 | `10-first-officer.md`: checklists, then actions, then flows | later | 02, 03, 06 |
| 11 | `11-efb-rework.md`: the reworked EFB window, every ground service, doors, pushback, presets, settings | later | 07 |
| 12 | `12-panels-remaining.md`: the remaining controls, trims and spring knobs, the live sweep | later | 02, 03 |
| 13 | `13-cockpit-announcements.md`: every meaningful light and switch announced from any source; flight modes if found | later | 01-08 (it changes the tablet client the early plans use) |
| 14 | `14-autopilot-window-and-readouts.md`: Ctrl+P parity, squawk/TCAS/altimeter changes, Alt+I | later | 05, 06 |
| 15 | `15-thrust-levers.md`: thrust levers from the keyboard, for pilots without a hardware throttle (optional) | later | 01 |
| 16 | `16-integration.md`: handoffs, docs, guide, rules, changelog, in-sim pass, pull request, delete `docs/b757/` | last | all |

## File ownership

Work only in the files your plan owns. A change you need elsewhere goes in your plan's "Handoff to plan 16" (file,
change, why). "A → B" means plan B takes the file over once A has landed; never two plans at once. Paths are under
`MSFSBlindAssist/` unless they start with `docs/`, `tools/`, `tests/`, `guide/`, `changelog.d/` or `.claude/`.

| File or pattern | Owner |
|---|---|
| `Aircraft/BBS757Definition.cs`, `Aircraft/BBS757Definition.Hooks.cs` (new), `Aircraft/BBS757Definition.Altimeter.cs`, `Forms/BBS757/BBS757MonitorManagerForm.cs` (takes `MonitorRows()`) | 01, then frozen |
| `Aircraft/BBS757Definition.Interaction.cs` | 01 (seam calls) → 03 |
| `Aircraft/BBS757Definition.Readouts.cs`, `Aircraft/BBS757/Bbs757Readouts.cs`, `Bbs757McpLights.cs`, `Bbs757FlapSpeeds.cs` (new), `Bbs757Prog.cs` (new), `Bbs757Annunciators.cs` (new) | 01 (display seam calls, Readouts only) → 06 → 14 |
| `Aircraft/BBS757/Bbs757DisplayReads.cs`, and in `Services/GeminiService.cs` only a new `Standby757` display type and its prompt | 14 |
| `Aircraft/BaseAircraftDefinition.cs` (the glideslope gate only) | 01 |
| `SimConnect/BBS757/CoherentBbs757TabletClient.cs` | 01 → 08 (the per-key gap for faster typing) → 13 |
| `Resources/coherent-bbs757-tablet-agent.js` | 01 → 13 |
| `SimConnect/BBS757/Bbs757CduPages.cs` (new), `Bbs757CduTitle.cs` (new), `Aircraft/BBS757/Bbs757SharedKeys.cs` (new), `docs/b757/tools/**` | 01, then frozen |
| `Aircraft/BBS757/Bbs757ControlMap.cs` | 01 (guard fields) → 02 → 12 |
| `tools/b757-gen/**`, `Resources/bbs757_control_map.json` | 02 → 12 |
| `Aircraft/BBS757/Bbs757Write.cs`, `Bbs757Rpn.cs`, `Aircraft/BBS757Definition.Overhead.cs` (new) | 03 → 12 |
| `Aircraft/BBS757/Bbs757Guarded.cs` (new), `Bbs757LandingAltitude.cs` (new) | 03 |
| `Aircraft/BBS757/Bbs757SpringControls.cs` (new) | 12 |
| `Aircraft/BBS757Definition.Radios.cs` (new), `Aircraft/BBS757/Bbs757Radios.cs` (new), `Bbs757NavRad.cs` (new), `Forms/BBS757/BBS757NavRadiosForm.cs` (new) | 04 |
| `Aircraft/BBS757/Bbs757Mcp.cs`, `Bbs757McpWindowLayout.cs`, `Bbs757Mach.cs` (new), `Bbs757AutoflightAnnouncements.cs` (new), `Aircraft/BBS757Definition.Mcp.cs` | 05 |
| `Aircraft/BBS757/Bbs757AutopilotLayout.cs`, `Aircraft/BBS757Definition.Autopilot.cs`, `Forms/BBS757/BBS757AutopilotWindow.cs` | 05 → 14 |
| `SimConnect/BBS757/Bbs757EfbLayout.cs`, `CoherentBbs757EfbClient.cs`, `Bbs757WorkFolder.cs`, `Bbs757Loadsheet.cs`, `Bbs757LoadWatch.cs`, `Bbs757GroundPower.cs` (new), `Forms/BBS757/BBS757EfbForm*.cs`, `Resources/coherent-bbs757-efb-agent.js`, `Aircraft/BBS757Definition.Efb.cs` (new), `Aircraft/BBS757Definition.LoadWatch.cs`, `MainForm.BBS757.cs` | 01 (`MainForm.BBS757.cs` only) → 07 → 11 |
| `SimConnect/BBS757/Bbs757GroundServices.cs`, `Bbs757EfbSettings.cs`, `Bbs757EfbBridge.cs` (new); additive changes to `Forms/FBWA380/FbwEfbForm.cs` | 11 |
| `Forms/BBS757/BBS757CduForm*.cs`, `SimConnect/BBS757/Bbs757CduFrame.cs`, `Bbs757CduKeys.cs`, `Bbs757CduRows.cs` (new), `Bbs757CduTyping.cs` (new) | 08 → 09 |
| `SimConnect/BBS757/Bbs757CduMarkers.cs` (new), `Aircraft/BBS757Definition.CduRows.cs` (new) | 09 |
| `FirstOfficer/BBS757/**` (new), `Aircraft/BBS757Definition.FirstOfficer.cs` (new), `Forms/FirstOfficer/FirstOfficerWindows.cs` (its 757 row), the cross-profile FO tests the 757 joins, the `paths:` list of `.claude/rules/b757.md` | 10 |
| `Aircraft/BBS757Definition.Announcements.cs` (new), `Aircraft/BBS757/Bbs757CockpitAnnouncements.cs` (new), `Bbs757LightTable.cs` (new) | 13 |
| `Aircraft/BBS757Definition.Thrust.cs` (new), `Aircraft/BBS757/Bbs757Thrust.cs` (new) | 15 |
| `docs/b757/evidence/flight-NN.md` (one per plan that measures in flight) | plan NN |
| `HotkeyGuides/BBS757_Hotkeys.txt`, `docs/b757.md`, `guide/**`, `docs/invariants/b757.md`, `.claude/rules/b757.md` (rule lines), `changelog.d/**`, `CLAUDE.md`, `docs/b757/evidence/live-findings.md` | 16 |
| `Aircraft/BBS757/Bbs757ChangeTracker.cs`, `Bbs757SimIo.cs`, `Bbs757Nest.cs`, `Bbs757Squawk.cs`, `Bbs757Altimeters.cs`, `Forms/BBS757/BBS757AltimeterForm.cs`, `MainForm*.cs` other than `MainForm.BBS757.cs`, `Hotkeys/**`, `Services/**` (except plan 14's one entry), `Forms/ValueInputForm.cs`, `Forms/NavRadiosForm.cs`, `Settings/**` | nobody: frozen; handoff requests to 16 |
| `docs/b757/plans/02-gaps/NN-*.md` | plan NN: its own file, its execution record included |
| `docs/b757/plans/02-gaps/00-index.md` | 01 (the "Base commits" line), then frozen |
| `docs/b757/spec-gaps.md`, `docs/b757/spec.md` | nobody: frozen; a ruling made while working goes in the plan's execution record |
| `MSFSBlindAssist/MSFSBlindAssist.csproj`, `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj` | nobody: frozen. New test files, `Fixtures/*.json` and the two 757 agents need no new line (SDK globbing, wildcards, existing entries); only plan 11 may add one resource line if its bridge needs a new file |

**Tests** follow their code: `Bbs757DefinitionTests.cs` 01 (loosened), then frozen; `Bbs757SeamsTests.cs`,
`Bbs757CduPagesTests.cs`, `Bbs757GlideslopeGateTests.cs` 01; `CoherentBbs757TabletClientTests.cs` 01 → 08 → 13;
`Bbs757ControlMapTests.cs` and `tools/b757-gen/test_generate_b757_map.py` 02 → 12; `Bbs757WriteTests.cs`,
`Bbs757RpnTests.cs` 03 → 12; `Bbs757GuardedTests.cs`, `Bbs757LandingAltitudeTests.cs` 03; `Bbs757SpringControlsTests.cs`
12; `Bbs757RadiosTests.cs`, `Bbs757NavRadTests.cs` 04; `Bbs757McpTests.cs`, `Bbs757MachTests.cs`,
`Bbs757AutoflightAnnouncementsTests.cs` 05; `Bbs757McpWindowTests.cs` (made by 01) 05 → 14; `Bbs757ReadoutsTests.cs`,
`Bbs757FlightReadinessTests.cs`, `Bbs757FlapSpeedsTests.cs`, `Bbs757ProgTests.cs`, `Bbs757AnnunciatorsTests.cs` 06 → 14;
`Bbs757DisplayReadsTests.cs` 14; `Bbs757EfbTests.cs`, `Bbs757LoadWatchTests.cs`, `Bbs757LoadsheetTests.cs`,
`Bbs757GroundPowerTests.cs` 07 → 11; `Bbs757GroundServicesTests.cs`, `Bbs757EfbSettingsTests.cs`, `Bbs757EfbBridgeTests.cs`
11; `Bbs757CduFrameTests.cs`, `Bbs757CduKeysTests.cs`, `Bbs757CduRowsTests.cs`, `Bbs757CduTypingTests.cs` 08 → 09; `Bbs757CduMarkersTests.cs` 09;
`tests/.../FirstOfficer/BBS757/**` and the FO cross-profile tests 10; `Bbs757CockpitAnnouncementsTests.cs`,
`Bbs757LightTableTests.cs`, `Fixtures/bbs757-lights.json` 13; `Bbs757ThrustTests.cs` 15; the rest frozen. No test,
code or CI step may read a file under `docs/b757/`: plan 16 deletes it (copy what a test needs into a fixture). Test names keep `Bbs757` so the b757 rule
glob loads.

## Measuring in flight (owner, ruling R69)

No measurement plan: the owner flies during the work. A task that needs a flight says "[in flight]"; its plan records
the result in its own `docs/b757/evidence/flight-NN.md`. Plan 01 commits the L:var snapshot tool
(`docs/b757/tools/lvar-snapshot/`): all 2,173 BlueBird variables in one Coherent call, diffed before and after an
action. Session 8's working scripts (snapshot, key and L:var presses with a diff, CDU read, EFB taps, the ND mirror,
window capture) are in `docs/b757/tools/live/` with a README. Who measures what: spec section 6.

## Who is on which plan

Each plan's own "Execution record" (at its end) holds its claim and status, so claiming never edits a shared line.
Plan 01 runs first and alone; plans 02-16 are each claimed by their Task 0, before any other task: check the remote
copy, claim on your branch, push that one commit to `B757`. A claim counts only once it is on the remote `B757`: one
that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is
theirs. One person per plan. To see where every plan stands on the remote: `git fetch <remote>`, then
`git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/`.
A later plan may start when the plans in its "After" column say landed.

## Workflow for one plan

1. Fetch `B757` and branch from it at the base commit for your plan's kind (bottom of this file):
   `git switch -c B757-pNN-<slug> <base-sha>`. An agent works in a worktree inside the repo
   (`.claude/worktrees/b757-pNN`), says so before creating it, and removes it when done (`dotnet build-server shutdown`
   first). This step is part of the plan's Task 0 (plans 02-16), which then pushes the claim before any other task.
2. Build once before testing: `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`. Close MSFSBA first if it
   runs (CORE-3), or test with `--artifacts-path` ("Running a plan with subagents").
3. Work the tasks in order (TDD). Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
   The generator (plans 02, 12): `python -I -m unittest tools/b757-gen/test_generate_b757_map.py`. Every commit ends
   with a `Plan: NN` trailer line (next to any Co-Authored-By line), so `git log --grep "^Plan: NN"` lists the plan's
   commits.
4. Push whenever a task is done and green (others then build on it sooner): `git fetch`, then
   `git rebase <remote>/B757` (your commits replay on top of what others pushed; files are disjoint and your claim is
   already on `B757`, so the rebase does not stop; in Task 0, a stop on the claim lines means someone claimed first).
5. After the rebase, on the combined code: build and full suite green (this run is the only check that the plans work
   together); `git diff --name-only <remote>/B757...HEAD` lists only files you own.
6. `git push <remote> HEAD:B757`. If it is rejected (someone pushed meanwhile), go back to step 4. Never force-push
   `B757`. Update your execution record in the same push; when the plan is done, fill in its "Handoff to plan 16" and
   set **Status** to landed (or waiting on owner, naming what waits).
7. Steps for the owner need MSFS 2024 with the BlueBird 757 and BBS Nest running. Leave them unticked if you lack
   them. Never write to the sim without the owner's go-ahead (reads are fine).

## Running a plan with subagents

Plans 01-15 are written for superpowers:subagent-driven-development (a fresh implementer per task, a reviewer after
each); plan 16 is docs and owner steps, worked with superpowers:executing-plans.

- One worktree per plan, inside the repo (`.claude/worktrees/b757-pNN`). In a fresh worktree, build the vPilot plugin
  once first (`dotnet build plugins/MSFSBlindAssist.VPilotPlugin/MSFSBlindAssist.VPilotPlugin.csproj -c Debug`), or
  the test build fails with MSB3030.
- Test with `--artifacts-path <worktree>/bin/b757-pNN` so parallel builds and an open MSFSBA never collide. The folder
  must be inside the repo or worktree, never the scratchpad or `%TEMP%`: about 28 tests find `MSFSBlindAssist.sln` by
  walking up from the test DLL and fail otherwise.
- Each implementer gets the task's text, the plan's Global Constraints and Review Focus, the spec sections the plan
  cites and this index's "Shared conventions", and works only in the task's files. Where a task says "Steps 1-5" with
  test names, those are the usual five: write the named tests, run them failing, implement, full suite, commit with the
  given message.
- Task 0 [controller], the claim, is the controller's own: it creates the plan's worktree and branch and pushes the
  claim before Task 1 is dispatched, and is never given to an implementer.
- The controller never dispatches an [owner], [owner present] or [in flight] task, an "owner-in-sim" step, an
  "In-sim checks (owner)" list or any step that writes to the sim: it lists them under "Waiting on the owner" in the
  execution record and works the rest. What needs the owner or builds on the owner's result: plan 01 Task 7's live run
  (a saved snapshot pair will do); plan 02 Task 8; plan 03 Task 4; plan 04 Tasks 4 and 5 (their later steps branch on
  step 1's reading) and Task 6; plan 05 Tasks 3 and 5; plan 08 Task 2's optional loaded-sim repeat (step 1 measured 2026-10-26: 100 ms hold, 100 ms gap); plan 09 Task 4; plan 10 Task 3's sim check; plan 11 Task 1, and
  Tasks 2-5 for the tiles it measures; plan 12 Task 2's run and Task 3; plan 13 Task 1, then Task 6; plan 15 Task 3.
- Two plans at once: separate worktrees and separate scratch folders; never two agents on one plan or one file.
- When the plan's tasks are done: one whole-plan review checks the ownership diff (step 5), that every Review Focus
  line has its test, and that the handoff and execution record are filled in.

## Records: who worked a plan and how it went

- **Each plan's "Execution record":** claim and status, who worked it (and whether with subagents), start and landing
  dates and commits, the test result at landing, tasks left, what waits on the owner, in-sim results, what changed from
  the plan and the rulings made while working.
- **Each plan's "Handoff to plan 16":** what changes for a pilot, proposed rule lines, requests for frozen files.
- **`docs/b757/evidence/flight-NN.md`:** what the owner's flights measured for that plan.
- **Commits:** the `Plan: NN` trailer (step 3) gives every commit with its author and date.
- **Plan 16** compiles the records into the pull request description and names the last commit holding `docs/b757/`
  before deleting it; the pull request merges with its history (`gh pr merge --merge`), so the records stay reachable.

## Shared conventions

- CLAUDE.md applies in full. Read a file before changing it (CORE-16); the b757 rules are B757-1 to B757-4.
- Every 757 write is a calculator write through `ExecuteCalculatorCodeUnique` after `CalcWriteCanLand` (refused aloud
  as "{label} unavailable" when false); RPN numbers invariant, fixed point (VAR-5). Every write path calls plan 01's
  `OnOwnWrite(node)` first.
- What is spoken is the value read back; a background change speaks once after a silent first reading; the app's
  own write is not said twice (CORE-7; `Bbs757ChangeTracker`'s echo window).
- Every new announced row has a Ctrl+M row; a silently consumed row sets `ExcludeFromMonitorManager` ([VAR-9]);
  announcing from `ProcessSimVarUpdate` relies on MainForm's mute wrap ([VAR-8]); `ProcessSimVarUpdate` gets no
  `SimConnectManager`, so keep another row's last delivered value yourself.
- Ctrl+M, in one place: its rows are `BBS757Definition.MonitorRows()` (plan 01). A Continuous, `IsAnnounced` variable
  not marked `ExcludeFromMonitorManager` gets its row automatically. Speech made inside `ProcessSimVarUpdate` for
  that variable's own row is muted by MainForm's wrap. Speech made anywhere else (a timer, a batch hook, a Coherent
  client's event: a resting MCP value, the altimeter, CDU messages, the cockpit watcher) checks
  `BBS757DisabledMonitorVariablesSet` for its key itself; a branch that speaks another row's call-out goes in
  `IsMuteWrapExempt` and checks that row. Speech that is no variable's gets a row through a monitor-row hook
  (`AddCduMonitorRows`, `AddCockpitMonitorRows`). Each announcing plan has a test that a muted row says nothing.
- A continuous row is delivered only when it changes (about once a second), so "once it rests" is a timer, never "the
  same value delivered twice": plan 05's `Bbs757RestingValue`.
- A status row whose number needs formatting (frequencies, feet) formats through its plan's display seam (plan 01).
- Typed numbers through `NumberParsing.TryParseTyped` (CORE-17); anything spoken or formatted gets a de-DE test.
- Status text in windows is a read-only `TextBox` (VAT-13); background pushes marshal through a try/catch
  `SafeBeginInvoke` (A380C-6).
- A toggle on BlueBird's EFB or panel is pressed only after its state is read and differs from the wish ([B757-2]'s
  pattern); an unread state presses nothing.
- Never read MCP values from stock autopilot variables or NAV frequencies from stock NAV variables on the 757.
- Tests never depend on another plan's map labels: build in-memory controls.
- The total registered variables stay under 900 (`Bbs757DefinitionTests`; about 500 today). Each plan states how many
  it adds; plan 13 adds none.

## Evidence

`docs/b757/evidence/live-findings.md` (session 8 is this investigation), `docs/b757/evidence/controls/`,
`docs/b757/evidence/package/`, each plan's `flight-NN.md`. BlueBird's variable list:
<https://support.bluebirdsim.com/docs/simvars>.

## Base commits

- Early plans (02-08) start from the commit plan 01 records here: `cf1dfce94` (plan 01 with its record, merged with
  `origin/main` at f5ead55c2).
- Later plans (09-15) start from `B757` once their "After" plans have landed; record the commit you started from in
  your plan's execution record ("Started").

## Display readers (plans 23-28, added 2026-11-08)

The 757's displays read from `BBS752.wasm` memory, as the PMDG 737's are (`docs/wasm-memory.md`). The owner's decisions
for the series (S1-S17) are in plan 23's "The series"; each plan restates the ones it relies on. Plans 17-22 (the EFB)
have no rows above; their files are named in their own Global Constraints.

| # | Plan | After |
|---|---|---|
| 23 | `23-displays-fma.md`: the shared monitor, the session in the definition, the seams, the shared window; the FMA spoken and listed in Ctrl+P | 22 |
| 24 | `24-displays-upper-eicas.md`: EICAS messages spoken; Alt+E window | 23 |
| 25 | `25-displays-lower-eicas.md`: the lower EICAS's pages; status messages on STATUS; Alt+S window | 23 |
| 26 | `26-displays-ehsi-map.md`: the EHSI in MAP and PLAN; Alt+N window; Ctrl+W | 23 |
| 27 | `27-displays-adi.md` (optional): the rest of the ADI; Alt+P window, the AI read its fallback | 23 |
| 28 | `28-displays-ehsi-modes-tcas.md`: the EHSI's VOR and ILS modes, TCAS, the map options | 26 |

24, 25, 26 and 27 run in parallel once 23 has landed; 28 takes over 26's files once 26 has landed.

| File or pattern | Owner |
|---|---|
| `Services/BBS757/IBbs757DisplayReader.cs`, `Bbs757DisplayMonitor.cs`, `Bbs757DisplaySpeech.cs`, `Bbs757DisplayFilesNotice.cs`, `Bbs757DisplayEnvironment.cs`, `Bbs757DisplayRegistry.cs`, `Bbs757Fma*.cs`, `Aircraft/BBS757Definition.Displays.cs`, `Forms/BBS757/BBS757DisplayForm.cs`, `Forms/BBS757/BBS757AutopilotWindow.cs`, `Aircraft/BBS757Definition.Autopilot.cs`, `MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757FmaLayout.cs`, `MainForm.BBS757.cs`, the display lines in `MainForm.cs` and `MainForm.AircraftSwitch.cs`, the registry calls in `BBS757Definition.cs` and `BBS757Definition.Interaction.cs` | 23, then frozen (a later plan's need goes in its handoff) |
| `Services/BBS757/Bbs757UpperEicas*.cs`, `DisplayLayouts/Bbs757/Bbs757UpperEicasLayout.cs`, `Aircraft/BBS757Definition.UpperEicas.cs` | 24 |
| `Services/BBS757/Bbs757LowerEicas*.cs`, `DisplayLayouts/Bbs757/Bbs757LowerEicasLayout.cs`, `Aircraft/BBS757Definition.LowerEicas.cs` | 25 |
| `Services/BBS757/Bbs757Ehsi*.cs`, `DisplayLayouts/Bbs757/Bbs757EhsiLayout.cs`, `Aircraft/BBS757Definition.Ehsi.cs` | 26 → 28 |
| `Services/BBS757/Bbs757Adi*.cs`, `DisplayLayouts/Bbs757/Bbs757AdiLayout.cs`, `Aircraft/BBS757Definition.Adi.cs` | 27 |
| `docs/b757/evidence/displays-<display>.md` | its plan |
| Private repo: `toolkit/MSFSBlindAssist.WasmToolkit/Bbs757/Bbs757<Display>Recipe.cs`, its fixture and tests | its plan |

Tests follow their code (`Bbs757<Display>*Tests.cs`). **Shared lines** (one each per plan, never a whole file):
`Bbs757Module.Description`'s reader list and `Bbs757LayoutDescriptions.cs` (a method per reader) in the app repo;
`Bbs757Recipes.All` and `GeneratorVersion.Current` in the private repo; the rule files' new lines.

**Landing a display plan:**

1. Add your reader at the end of `Bbs757Module.Description`'s readers and your recipe at the end of `Bbs757Recipes.All`.
2. At landing, rebase onto `B757` (a conflict on the shared lines keeps both lines, yours last) and the private branch
   onto its head, take the next `GeneratorVersion.Current` (one higher than the head's), regenerate the 757 layout with
   every reader then on `B757`, and run the layout tests and both suites.
3. The layout store refuses a file lacking any reader ([WASM-26]): once a new reader is on `B757`, every 757 reader,
   the EFB's included, is unavailable until the regenerated layout is installed. Deploy each build with its layout,
   and publish the layout (`toolkit/scripts/publish-layouts.ps1 -Source` that file only) together with the push, both
   at the owner's word.
