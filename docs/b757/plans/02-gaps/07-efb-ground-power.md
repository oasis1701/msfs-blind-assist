# BlueBird 757 EFB Ground Power Implementation Plan (plan 07, early: essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** From the 757 EFB window (Shift+T) a pilot can connect and disconnect ground power, and hears ground power come and go from any source (BlueBird's automatic GPU, the EFB, a preset).

**Architecture:** BlueBird's tablet is drawn by its WASM: no text can be read from it. The window taps the Ground Services page's GPU tile (measured: it toggles the cart) through the existing EFB client and agent, only after reading `L:BBS_757_GPU_AC_PWR_CONNECTED` and only when it differs from the wish ([B757-2]'s pattern), then judges the result from that variable. A disconnect is refused while external power is on line.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#11), 4 (R62); `docs/b757.md` "The 757 EFB window"; rule B757-2.

## Global Constraints

- Owned files (then passed to plan 11): `SimConnect/BBS757/Bbs757EfbLayout.cs`, `CoherentBbs757EfbClient.cs`, `Bbs757WorkFolder.cs`, `Bbs757Loadsheet.cs`, `Bbs757LoadWatch.cs`, `Bbs757GroundPower.cs` (new), `Forms/BBS757/BBS757EfbForm*.cs`, `Resources/coherent-bbs757-efb-agent.js`, `Aircraft/BBS757Definition.Efb.cs` (new), `Aircraft/BBS757Definition.LoadWatch.cs`, `MainForm.BBS757.cs` (after plan 01); tests `Bbs757EfbTests.cs`, `Bbs757LoadWatchTests.cs`, `Bbs757LoadsheetTests.cs`, `Bbs757GroundPowerTests.cs` (new).
- Measured (session 8): Home (19,62), Aircraft (320,190), GROUND SERVICES (128,368), GPU tile (1005,231); one tap toggled `BBS_757_GPU_AC_PWR_CONNECTED`, `_CART_VISIBLE`, `_PWR_NOT_IN_USE` and the EXT PWR AVAIL light, a second tap restored them within 4 s.
- External power on line: `L:B757_OVHD_EXT_PWR_BUTTON_ON` 1, read under this plan's own OnRequest key `BBS757_EFB_EXT_PWR_ON` (plan 06 registers the same L:var as an announced row; the two keys never both stream, [VAR-7]), so this plan does not wait for plan 06.
- Variables added: 2 (`Bbs757SharedKeys.GpuConnected`, `BBS757_EFB_EXT_PWR_ON`).

## Review Focus

- The ground power state cannot be read: nothing is tapped and the window says so (Task 1 test).
- A connect while connected never taps (the tile toggles), Task 1 test.
- A disconnect while EXT PWR is on line is refused before any tap (Task 1 test).
- A run that started but did not report finishing is judged from the variable, never run again (Task 2).
- BlueBird's automatic GPU connecting at flight load is the first reading: silent (Task 3 test).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/07-efb-ground-power.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 07`, with the `Plan: 07` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: Ground power logic, pure

**Files:** Create `SimConnect/BBS757/Bbs757GroundPower.cs`; modify `Bbs757EfbLayout.cs`; test `Bbs757GroundPowerTests.cs`.

**Interfaces:**
- `Bbs757EfbLayout`: `public static readonly EfbPoint GpuTile = new(1005, 231);` and `ToggleGroundPower()` = `Home`, `AircraftIcon`, `AircraftMenuGroundServices`, `GpuTile`, each `NavigateDelayMs` (as `ToggleChocks()`).
  ```csharp
  public static class Bbs757GroundPower
  {
      public const string ConnectedVar = "BBS_757_GPU_AC_PWR_CONNECTED";
      public static string? Refusal(bool connect, double? connected, double? extPwrOn);   // null = tap
      public static string Result(bool connect, double? before, double? after);
      public static string? ChangePhrase(double v);   // 1 "Ground power connected", 0 "Ground power disconnected"
      public static DateTime? LastWindowRunUtc { get; set; }   // Task 3's echo stamp
  }
  ```
  `Refusal` (exact): connected unread → "The ground power cannot be read, so nothing was tapped."; connect at 1 → "Ground power is already connected."; disconnect at 0 → "Ground power is already disconnected."; disconnect with `extPwrOn` 1 → "Ground power is on line. Press EXT PWR on the Electrical panel to take it off, with the APU or the engines powering the aircraft, then try again."; disconnect with `extPwrOn` unread → "External power cannot be read, so nothing was tapped." `Result`: wish met → "Ground power connected. Press EXT PWR on the Electrical panel to put it on line." / "Ground power disconnected."; unchanged → "BlueBird did not connect the ground power." / "… disconnect …"; unread → "The ground power cannot be read now."

- [x] **Steps 1-5** (tests `Refusals`, `Taps_only_when_different`, `Results`, `Sequence_ends_on_the_gpu_tile`); commit `feat(b757): EFB ground power logic`.

### Task 2: Ground power buttons in today's window

**Files:** `Forms/BBS757/BBS757EfbForm.cs`; test `Bbs757EfbTests.cs`.

- "Connect ground power", "Disconnect ground power" after "Set chocks", run exactly as the chocks buttons run (one run at a time, reachability, read `Bbs757SharedKeys.GpuConnected` and `Bbs757SharedKeys.ExtPwrOn` fresh, `Refusal`, the sequence, read every 500 ms for up to 6 s, `Result` spoken and in the status box; a run that did not report finishing is judged from the variable). Stamp `LastWindowRunUtc` before tapping.
- [x] **Steps 1-5** (tests `Connect_taps_once_and_reports`, `Connect_when_connected_taps_nothing`, `Disconnect_refused_while_on_line`); commit `feat(b757): connect and disconnect ground power from the 757 EFB window`.

### Task 3: Ground power announced and on the Electrical panel

**Files:** Create `Aircraft/BBS757Definition.Efb.cs` (plan 01's `AddEfbVariables`, `AddEfbPanels`, `ProcessEfbUpdate`, `FormatEfbDisplay`); test `Bbs757GroundPowerTests.cs`.

- `Bbs757SharedKeys.GpuConnected` = LVar `BBS_757_GPU_AC_PWR_CONNECTED`, Continuous, `IsAnnounced`, status row "Ground Power" (Connected / Not connected) in the "Electrical" display, a Ctrl+M row. `ProcessEfbUpdate`: `_changes.Observe(key, ChangePhrase(v), NowMs())`, except within 8 s of `LastWindowRunUtc` (the window's own run: rebaseline silently).
- [x] **Steps 1-5** (tests `Ground_power_row_on_electrical`, `Change_announced_after_silent_first`, `Window_run_not_said_twice`); commit `feat(b757): ground power announced and on the Electrical panel`.

## In-sim checks (owner)

1. At the gate, APU running, EXT PWR not on line: Shift+T, Disconnect ground power → "Ground power disconnected." (EXT PWR AVAIL goes out); Connect ground power → "Ground power connected. Press EXT PWR…"; Connect again → "Ground power is already connected."
2. Press EXT PWR on the Electrical panel (AC from the GPU), then Disconnect ground power → refused. Meanwhile a
   snapshot (`docs/b757/tools/live/snap.sh`) records `L:B757_L_BUS_PWR_SRC` and `L:B757_R_BUS_PWR_SRC`, for R62's "no
   other source powers the buses" (the refusal does not test that yet: it refuses whenever EXT PWR is on line).
3. (added after the whole-plan review, 2026-10-26) Load a flight at the gate with BlueBird's AUTOMATIC GPU/JETWAY on:
   nothing is said about ground power, and the Electrical panel's status display reads Ground Power: Connected. This is
   the only real proof of the last Review Focus line: the tests can only show the first reading is silent.
4. (added, 2026-10-26) A change made outside the window: before the 757 EFB window is first opened in the session (so
   the app does not hold the EFB view yet), Claude taps BlueBird's GPU tile with
   `docs/b757/tools/live/efb-tap.sh 19,62 320,190 128,368 1005,231`: "Ground power connected" (or "disconnected") is
   said once. Then mute Ground Power in Ctrl+M and tap again: nothing is said, and the Electrical status display shows
   the change.

## Handoff to plan 16

- **Pilot-facing:** "757 EFB window: Connect and Disconnect ground power (it reads BlueBird's ground power first and refuses to disconnect while external power is on line); ground power coming or going is announced and shown on the Electrical panel." Guide: 757 EFB; Ctrl+M list. Changelog `feature`.
- **Hotkey guide:** the 757 EFB (Shift+T) list.
- **Rule change:** extend B757-2 with the GPU tile: "… and the Ground Services GPU tile (1005,231), which toggles `L:BBS_757_GPU_AC_PWR_CONNECTED` (`Bbs757GroundPower.Refusal`)", and its full text with session 8's measurement and the 2026-10-26 one (connected 0.30 s and disconnected 0.34 s after the tap, EXT PWR AVAIL 0.17 s after it).
- **The pages** (found while working): `HotkeyGuides/BBS757_Hotkeys.txt` (the 757 EFB list), `guide/hotkey-reference.md` (its 757 EFB section), `guide/boeing-aircraft-and-the-md-11.md` (the 757 EFB window), and `docs/b757.md` "The 757 EFB window" (a bullet after "Remove chocks / Set chocks", and its "What it reads" list: `L:BBS_757_GPU_AC_PWR_CONNECTED` and `L:B757_OVHD_EXT_PWR_BUTTON_ON`, read fresh).
- **Required with plan 06:** plan 06's EXT PWR AVAIL row (`Bbs757SharedKeys.ExtPwrAvail`) changes about 0.17 s after every GPU tap. Plan 07's echo arms and ends that key too (`BBS757Definition.ArmGroundPowerEcho`, `SettleGroundPowerEcho`), which works if plan 06 speaks that row through `_changes` under that key: test it with both rows present (one window run, one utterance). Rule too whether a ground power change from elsewhere keeps both "Ground power connected" and "External power available" (R66 lists both).
- **Two keys on one L:var:** plan 07 reads `L:B757_OVHD_EXT_PWR_BUTTON_ON` under its own OnRequest key `BBS757_EFB_EXT_PWR_ON` (`Bbs757GroundPower.ExtPwrOnKey`); plan 06 streams it as `Bbs757SharedKeys.ExtPwrOn` ([VAR-7] allows both: only one streams). Once both have landed, the window may read plan 06's key and plan 07's can go.
- **The display's panel:** the Ground Power row joins the map's "Electrical" panel by name (`BBS757Definition.GroundPowerPanel`); a regenerated map that renames that panel (plan 02, then 12) drops the row from it with no test failing. If plan 06 declares its own constant for the same panel, one of the two can go.
- **For plan 11**, which takes these files: read a toggle's state after the EFB agent answers (`_efb.ReadPageAsync` first, as `UpdateNavDataAsync` does), not before the agent wait and the navigation taps (today 5 to 11 s pass between the read and the GPU tap, so a change in between is toggled back; the read-back still says the truth); optionally have the agent check the L:var just before the last tap; make `GroundPowerAsync`'s outcome arms explicit; extract the shared finite-and-above-0.5 reader when a fourth copy appears (`Bbs757GroundPower.Flag`, `Bbs757EfbLayout.ChocksInPlace`, `MainForm.ReadBbs757OnGroundAsync`). Plan 11's Task 5 says a window run's echo is silenced "as plan 07's ground power is (its `LastWindowRunUtc` stamp)": that now means `ArmGroundPowerEcho` before the taps and `SettleGroundPowerEcho` after (no stamp exists; see "Changed from the plan"). From plan 07's in-sim run (Robin: "will be fixed in plan 11"): the window's status box keeps its last result across a flight load (after a reload it still said the ground power was on line); drop a result from an earlier tablet page (the client's page clock, `PageLoadedMs`, changes with each flight load).

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed and pushed to `kn4iee/B757` (2026-10-26); in-sim checks 1, 2 and 4 passed (2026-10-26); check 3 passed at the owner's word (2026-11-15); nothing waits on the owner
- **Claimed by:** Robin Kipp (robin24), 2026-10-26
- **Worked by:** Robin Kipp with Claude, with subagents (superpowers:subagent-driven-development): the controller (Claude Opus 5.5) claimed the plan, measured it live first and ruled on the plan's conflicts; each task had a Sonnet 5.5 implementer and a Sonnet 5.5 task reviewer (one fix round, for Task 3); then one whole-plan review (Opus 5.5) and one fix wave with its re-review
- **Started:** 2026-10-26, `B757` at ecc077271 (the early plans' base cf1dfce94 plus everything pushed since, up to the claim of plan 04)
- **Landed:** 2026-10-26; rebased onto `kn4iee/B757` at 6ca855761 (plans 03 and 05 landed meanwhile; no file in common); last code commit 899f35fe1. `git log --grep "^Plan: 07"` lists the plan's commits.
- **Tests at landing:** full suite passed: 17,350 after the last rebase (17,339 after the first, onto bf2a0e181; 17,054 before it; 16,944 at the claim); `MSFSBlindAssist.sln` builds with no new warning (its warnings are all in other areas' files)
- **Tasks:** all done; the plan's Task 3 ran before its Task 2 (see the rulings).
  - Task 0, the claim: 85ac53bdb.
  - Task 1, ground power logic: c113b4af2.
  - Task 3, announced and on the Electrical panel: f4e7c6eb7, fix round bfa7efd0c (the echo ends at the window's settle; a plan-specific panel constant; no test on the generated map's panel names).
  - Task 2, the window's buttons: f04b858c5.
  - Whole-plan review (no Critical; one Important, plan 06's EXT PWR AVAIL row, handled): 899f35fe1 (the echo covers that row too; a 2500 ms timeout for the batch row's reads; a truthful echo comment; a debug line per run; the muted-row test no longer uses another plan's phrase; the panel constant's value pinned).
- **Waiting on the owner:** nothing. Check 3 (the silent first reading of an automatic GPU on a fresh flight load, which the 2026-10-26 run below could not complete) had been run by the owner later and passed; he said so on 2026-11-15 ("the remaining plan 02 and 07 checks were also already tested previously and can be marked done").
- **In-sim results:** 2026-10-26, Robin, the Release build of a6743e9ed (deployed to `C:\MSFSBA-B757`), the B752 at a KSEA gate, BBS Nest running.
  - Check 4 passed. Before the EFB window was first opened, Claude tapped the GPU tile from outside the app (`efb-tap.sh`'s sequence through the app's agent): a disconnect was said once, "Ground power disconnected", and nothing else (the connect tapped just before was missed while NVDA was busy, so it does not count). With Ground Power muted in Ctrl+M, a connect said nothing, and the Electrical status display read Connected.
  - Check 1 passed. Disconnect, Connect, then Connect again: each said once ("Ground power disconnected.", "Ground power connected. Press EXT PWR on the Electrical panel to put it on line.", "Ground power is already connected."), nothing said twice; EXT PWR then put the GPU on line and powered the aircraft. `debug.log`: disconnect Result, connect Result, connect Already.
  - Check 2 passed. With EXT PWR on line (APU and engines off), Disconnect was refused at once and nothing was tapped (the GPU stayed connected). `L:B757_L_BUS_PWR_SRC` and `_R_` read 3 on external power (0 cold and dark at the session's start; 4 after the reload, with `B757_OVHD_BATTERY_BUTTON` 0). `debug.log`: disconnect Refused.
  - Check 3 not completed. After a flight reload at the gate with AUTOMATIC GPU/JETWAY on (`auto_connect_ground_services_on_init` true), nothing was said about ground power, and the status display read Not connected, which matched the sim: BlueBird had not connected its GPU (`BBS_757_GPU_AC_PWR_CONNECTED` 0, no cart, EXT PWR AVAIL off), still not six minutes later with its tablet up. The tablet canvas read 0 × 0 for the first minutes after the reload (the EFB agent answered NOT_READY), then came up when the window was opened.
  - Found, left to plan 11 (Robin's ruling): the window's status box keeps its last result across a flight load. After the reload it still read "Ground power is on line. Press EXT PWR…", no longer true. NVDA does not read the box when the window opens.

  Before the work, on 2026-10-26, the controller ran the plan's taps live through the app's EFB agent (B752 cold and dark at the gate, MSFSBA closed, BBS Nest running, with Robin's go-ahead): Home, Aircraft, GROUND SERVICES, GPU, 1.5 s apart, connected the ground power (`BBS_757_GPU_AC_PWR_CONNECTED` 0 → 1 0.30 s after the GPU tap; EXT PWR AVAIL lit 0.17 s after it; `_CART_VISIBLE` and `_PWR_NOT_IN_USE` 1, `_GPU_DOOR` 100), and the same taps again disconnected it (1 → 0 in 0.34 s). A snapshot diff before and after showed only drift, and the aircraft was left as found.
- **Changed from the plan:**
  - EXT PWR is read under plan 07's own OnRequest key `BBS757_EFB_EXT_PWR_ON` (`Bbs757GroundPower.ExtPwrOnKey`, L:var `ExtPwrOnVar`), as the Global Constraints say, not `Bbs757SharedKeys.ExtPwrOn` as Task 2's text says: that is plan 06's key, unregistered until 06 lands.
  - No `Bbs757GroundPower.LastWindowRunUtc`. The window's own run is silenced by the definition's change tracker: `BBS757Definition.ArmGroundPowerEcho()` before the taps (capped at `Bbs757GroundPower.WindowRunMaxMs`, 60 s) and `SettleGroundPowerEcho(after)` when the run is over (it rebaselines to what the window read and ends the echo, as `SettleOwnWriteEcho` does: no 8 s window), reached through two seams on `Bbs757LoadIo` wired in `MainForm.BBS757.cs`. The echo also covers `Bbs757SharedKeys.ExtPwrAvail` (see the handoff).
  - The run's flow is `Bbs757GroundPower.SwitchAsync` over `Bbs757GroundPowerIo` (delegates), so the plan's Task 2 tests run with no window; the window wires it and speaks its `Bbs757GroundPowerOutcome`.
  - The panel constant is `BBS757Definition.GroundPowerPanel` (value "Electrical"), not `ElectricalPanel`: plan 06's partial names its panel constants that way, and a second member of one name would break the build.
  - Added: `Bbs757EfbRunKind.GroundPower`; `ToggleGroundPower()` in the sequence lists the existing tests check; a muted-row test (the index's convention); de-DE and sv-SE tests; one debug line per run; in-sim checks 3 and 4.
  - The row's panel is tested only through an in-memory map, never the generated map's names (the index's convention).
- **Rulings made while working:** (question; answer; who decided. Claude's rulings are the owner's to reverse.)
  - Build plan 07 as written, or fold it into plan 11's EFB rework? Measure it first: the plan's points and variables held live and the work was small, so it was built; Robin ("If it's a simple task, let's bang it out and close out the plan as written").
  - Tap BlueBird's EFB live to check the plan? Yes, once each way, left as found; Robin's go-ahead ("you can access the EFB"), Claude.
  - Which EXT PWR key does the window read? Plan 07's own OnRequest key (the Global Constraints), not plan 06's; Claude.
  - Task 2 before Task 3? Task 3 first: the window reads what the definition registers and calls its echo; Claude.
  - A static `LastWindowRunUtc` stamp? Replaced by the definition's tracker echo, armed before the taps and settled in a `finally`: a static is shared by every definition and by parallel test classes, runs on a wall clock the definition cannot inject, and a single 8 s stamp is outlived by a slow run; Claude.
  - Keep an 8 s echo after the settle? No (found in Task 3's review): after a readable settle the same state is no change anyway, and the tail hid real late changes; Claude.
  - `ElectricalPanel`? Renamed `GroundPowerPanel`: plan 06 works in parallel on the partial whose panel constants follow that naming; Claude.
  - The brief asked for a test on the generated map's "Electrical" panel: the index's "tests never depend on another plan's map labels" wins; Claude.
  - Commit trailers? Each names the model that wrote the code from Task 3 on; c113b4af2 names Opus though Sonnet wrote it (left as it is, as plan 01 did); Claude.
  - Plan 06's EXT PWR AVAIL row would double every window run once 06 lands (whole-plan review): the echo arms and ends that shared key too, now; plan 16 tests it with both rows; Claude.
  - The ground power reads' timeout: 2500 ms (a batch row answers with the next 1 Hz delivery), as the window's ground read; Claude.
  - The status box's stale result after a flight load (found in the in-sim run): fix now in plan 07, or in plan 11? Plan 11; Robin ("Leave as-is, will be fixed in plan 11").
