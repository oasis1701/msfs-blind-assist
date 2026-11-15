# BlueBird 757 Control Map Implementation Plan (plan 02, early: fixes and essentials)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Fixes first: every 757 panel control reads with its real name and positions, the wrong labels and kinds are fixed, the emergency lights and the stab trim cutouts are guarded controls, the chronometers step the right way, controls the cockpit lacks are gone, and a test replays every control's click code. Then the essentials: the gear lever (with its OFF detent) and the flap lever on the panels, and Altitude Intervene kept or dropped per a flight check. (The remaining controls and the live sweep are plan 12, which takes these files over.)

**Architecture:** Curation and generator logic in `tools/b757-gen` (Python 3 stdlib) regenerate the embedded `MSFSBlindAssist/Resources/bbs757_control_map.json`; the C# side changes only to read it and in the map's own tests. The evidence for every name is BlueBird's published variable list (<https://support.bluebirdsim.com/docs/simvars>) or the click code itself, cited in the curation's `_notes`.

**Tech Stack:** Python 3 (`python -I`), unittest, C# xUnit.

**Spec:** `docs/b757/spec-gaps.md` sections 1 (#4, #5, #7), 3.3, 3.6, 3.7, 4 (R64, R65, R68); `docs/b757.md` "The control map".

## Global Constraints

- Needs the BlueBird package installed (the generator reads it): a contributor without it cannot regenerate the map.
- Never hand-edit `bbs757_control_map.json`: change `tools/b757-gen/curation.json` or the generator, then run `python -I tools/b757-gen/generate_b757_map.py`.
- `positions`, `labels` and the new keys are keyed by **node** (the control's `node`, e.g. `B757_MCP_AP_HDG_BNKLIM`), never its state var.
- A label never moves a control: names apply before `panel_order` / `control_order`.
- Panel names unique across sections; labels unique within a panel ([VAR-1]).
- `Bbs757RpnTests.Every_string_a_757_control_can_produce_stays_under_900_characters` keeps passing.
- Owned files (then passed to plan 12): `tools/b757-gen/**`, `Resources/bbs757_control_map.json`, `Aircraft/BBS757/Bbs757ControlMap.cs` (after plan 01), `tests/.../Bbs757ControlMapTests.cs`, `docs/b757/evidence/flight-02.md`.

## Review Focus

- No guarded control carries the vendor's gated click code (the old stab trim press began with the extractor's `[if …]` note), Task 3 test.
- Positions follow the control's real values in order, including non-contiguous ones (start selectors 1, 3, 4, 5, 6; warning indicators 0 and 11; chrono ET whatever the click code reaches), Task 1 test.
- A label fix never duplicates a label in its panel (the hydraulic pumps share one), Task 2 test.
- A variant suffix never doubles ("L Fuel Control (PW)" stays), Task 5 test.
- The gear lever's UP must never be sent on the ground by a test or the sweep (Task 7's in-sim check is airborne; plan 12's sweep denies it).

---

### Task 0 [controller]: Claim this plan

**Who:** whoever runs the plan, before any other task; with subagents, the controller itself, never an implementer. A claim counts only once it is on the remote `B757`: one that exists only on your machine claims nothing, and if someone else's claim reaches `B757` first, the plan is theirs.

- [x] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/B757 -- docs/b757/plans/02-gaps/02-control-map.md`. Anything but "not started" and "none" means the plan is taken: stop.
- [x] **Step 2: Branch and claim.** Branch as in the index's workflow step 1. In this file's "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the commit you branched from. Commit only that change: `docs(b757): claim plan 02`, with the `Plan: 02` trailer.
- [x] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/B757`, `git push <remote> HEAD:B757`. If the rebase stops on these lines, someone claimed the plan first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has gone through.

### Task 1: Name every position BlueBird names

**Files:** `tools/b757-gen/curation.json` (`positions`, `_notes`), the map; tests `tools/b757-gen/test_generate_b757_map.py`, `tests/MSFSBlindAssist.Tests/Bbs757ControlMapTests.cs`.

**Interfaces:** position labels by node (value → label), in the control's value order:
- `B757_IRS_CENTRE_KNOB`: 0 L, 1 C, 2 R. `B757_IRS_LARGE_KNOB`: 0 TK/GS, 1 PPOS, 2 WIND, 3 HDG/STS.
- `B757_OVHD_WIPER_SWITCH`: 0 OFF, 1 LOW, 2 HIGH.
- `B757_OVHD_SEATBELTS_SWITCH`, `B757_OVHD_NO_SMOKING_SWITCH`: 0 OFF, 1 AUTO, 2 ON.
- `B757_OVHD_MODE_SELECT_SWITCH`: 0 AUTO 1, 1 AUTO 2, 2 MAN.
- `B757_OVHD_SEL_L_PACK_SWITCH`, `B757_OVHD_SEL_R_PACK_SWITCH`: 0 OFF, 1 AUTO, 2 STBY N, 3 STBY C, 4 STBY W.
- `B757_CABIN_CEILING`: 0 Off, 1 33%, 2 66%, 3 100%. `B757_CABIN_WINDOW`, `B757_CABIN_ENTRY`: 0 Off, 1 50%, 2 100%.
- `B757_OVHD_WARNING_IND_SWITCH`: 0 DIM, 11 BRT.
- `B757_MCP_AP_HDG_BNKLIM` (Bank Limit, state `BBS757_BANK_LIMITER`): 0 AUTO, 1 5, 2 10, 3 15, 4 20, 5 25.
- `B757_MAIN_knob_gears_altn_flaps`: 0 NORM, 1 UP, 2 1, 3 5, 4 15, 5 20, 6 25, 7 30.
- `B757_MAIN_M_Gau_STBY_GYRO_SELECTOR`: 0 OFF, 1 ILS, 2 BCRS.
- `B757_MAIN_CHRONO_ET_SWITCH`, `B757_MAIN_CHRONO_ET_SWITCH_FO` (state `BBS_CHRONO_LEFT_SWITCH[_FO]`): 0 RUN, 1 HOLD, RESET at the value the click code reaches past HOLD (BlueBird's list says 3 with 2 unused; the cockpit tooltip says 2: read the generated cw/ccw and name what it lands on; a value the code never reaches is left out).
- `B757_MAIN_CHRONO_SET_SWITCH`, `B757_MAIN_CHRONO_SET_SWITCH_FO` (state `BBS_CHRONO_RIGHT_SWITCH[_FO]`): 0 RUN, 1 HOLD, 2 Slow slew, 3 Fast slew.
- `B757_OVHD_ENG_START_SWITCH` (Ignition): 0 1, 1 2, 2 BOTH.
- `B757_OVHD_L_ENG_START_SWITCH`, `B757_OVHD_R_ENG_START_SWITCH`: 1 GND, 3 AUTO, 4 OFF, 5 CONT, 6 FLT.
- `B757_PED_TCAS_MODE_KNOB`: 0 STBY, 1 ALT RPTG OFF, 2 XPNDR, 3 TA ONLY, 4 TA/RA.
- `B757_EICAS_LWR_THRUST-REF_KNOB`: 0 L, 1 BOTH, 2 R. `B757_ECAM_LRAUTO_KNOB`: 0 L, 1 AUTO, 2 R.
- `B757_VC_P61_SWITCH_2` (Flight Recorder, state `BBS757_FLT_REC_SWITCH`): 0 ON, 1 AUTO, 2 TEST.
- The P61 test switches `B757_VC_P61_SWITCH_3` … `_10` (find each node's label in the map): 1 Neutral on all; 0 / 2 per BlueBird's list: Equip Cooling "Upper test" / "Lower test"; Yaw Damp "Upper test" / "Lower test"; Wing AI Window Heat "Wing anti-ice test" / "Probe heat test"; Ground Prox Hyd Gen "Ground proximity test" / "Hydraulic generator test"; TO and Land Config "Takeoff config test" / "Landing config test"; Air Data Stall Left and Right "Air data test" / "Stall warning test". Duct Leak Fuel Qty: 1 Neutral, 0 and 2 numbered.
- `_notes`: "Position names from BlueBird's simvar list (support.bluebirdsim.com/docs/simvars, values checked against source 2026-10-21), read 2026-10-25."

- [x] **Step 1: Write the failing tests:** Python `test_bluebird_position_names` (every node above, exact `(value, label)` list). C# `Positions_have_bluebirds_names` (Mode Select AUTO 1 / AUTO 2 / MAN; left start selector values 1, 3, 4, 5, 6 = GND, AUTO, OFF, CONT, FLT; TCAS value 2 XPNDR; alternate flaps value 7 "30") and `No_position_reads_Position_n_except_the_unpublished` (only the Duct Leak Fuel Qty test switch keeps "Position n").
- [x] **Step 2: Run** `python -I -m unittest tools/b757-gen/test_generate_b757_map.py` and `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter Bbs757ControlMapTests`. Expected: FAIL.
- [x] **Step 3: Edit the curation**, regenerate.
- [x] **Step 4: Run both.** Expected: PASS.
- [x] **Step 5: Commit** `fix(b757): every position BlueBird names, named`.

### Task 2: Wrong labels and kinds

**Files:** as Task 1.

**Interfaces** (node → label / kind / positions):
- `B757_OVHD_SWITCH_GAURD` → "Emergency Lights Guard" (Closed / Open).
- The four hydraulic electric pumps (read each node's XML name and BlueBird's list) → "C1 ELEC Pump", "C2 ELEC Pump", "L ELEC Pump", "R ELEC Pump"; engine pumps stay "L ENG HYD Pumps", "R ENG HYD Pumps"; Hydraulics `control_order` keeps R, C, L for the electric pumps.
- "Xfeed Pumps FWD / Aft" → "Crossfeed FWD", "Crossfeed Aft".
- `B757_GAURD_11_SWITCH`, `B757_GAURD_12_SWITCH` → the names of the switches they cover (from the XML), else "Fire Guard 1" / "Fire Guard 2" with a `_notes` line.
- The Autopilot Disengage Bar → 0 Up, 1 Down. Thrust Ref Pull → 0 In, 1 Pulled.
- `B757_MCP_AP_SPD_HOLD_SW` ("AP SPD Hold") → "Speed Intervene".
- The APU fire handle → kind `toggle` on its pulled-state L:var (the engine handles' pattern), positions In / Pulled, so the generic path reads it before pressing (today a second press stows it).
- The 14 audio mic selects → kind `button` (each press selects that microphone and clears the others; "Off" is unreachable).

- [x] **Steps 1-5** as Task 1 (tests `test_label_corrections`, `test_labels_unique_per_panel`; C# `Labels_name_what_the_control_is`, `Apu_fire_handle_shows_its_state`); commit `fix(b757): labels and kinds that match the cockpit`.

### Task 3: Guarded controls

**Files:** `tools/b757-gen/generate_b757_map.py`, `curation.json` (new key `guarded`; `include_nodes`, `node_panels`, `control_order`), the map; tests as Task 1.

**Interfaces:**
- Consumes plan 01's `Bbs757Control.Guard`, `GuardClosed`, `CloseSets` (JSON `guard`, `guard_closed`, `close_sets`).
- `guarded`: node → `{ "state_var", "positions": [labels], "guard", "guard_closed", "close_sets" }`; emitted as kind `switch` with its state var, positions and guard fields and **no** `press`, `release`, `cw`, `ccw` (plan 03's writer opens the guard and writes the state var). The guard keeps its own row.
- Rows: `B757_OVHD_PASS_OXY_Toggle_Switch_CLICK` → "Emergency Lights", panel "Overhead miscellaneous", first in its `control_order`, the guard right after; 0 OFF, 1 ARMED, 2 ON; guard `B757_OVHD_SWITCH_GAURD`, closed 0, close sets 1. `B757_Stab_trim_C_toggle_switch_CLICK` → "Stab Trim C Cutout", 0 NORMAL, 1 CUTOUT, guard `B757_AISLE_STAND_STAB_1_GAURD_SWITCH`, closed 0, close sets 0; `B757_Stab_trim_R_toggle_switch_CLICK` → "Stab Trim R Cutout", guard `B757_AISLE_STAND_STAB_2_GAURD_SWITCH`.
- A check: any control whose press, release, cw or ccw starts with `[` and is not in `guarded` raises `CurationError`.

- [x] **Steps 1-5** (tests `test_guarded_controls`, `test_gate_note_never_reaches_click_code`, `test_gated_control_outside_guarded_stops_the_run`; C# `Guarded_controls_carry_their_guard`); commit `fix(b757): emergency lights and stab trim cutouts as guarded controls`. Until plan 03's writer lands (an early plan like this one), a guarded pick finds no click code, writes the state var, and BlueBird's guard refuses it: the combo snaps back. Nothing is worse than before.

### Task 4: Chronometer ET steps the right way

> **Superseded by ruling R10 (execution record): built as `direct_write`, not `reverse_knobs`** (commit `fix(b757): the chronometer ET switch goes straight to the pick`), because swapping cw and ccw fixes the direction but not the steps: a step down from RESET still lands on 2, which is no position, and the app then refuses every later pick, while the vendor's click writes nothing but that one L:var, so writing the picked value directly loses nothing. The planned text below is kept as written.

**Files:** generator, `curation.json` (new key `reverse_knobs`: nodes whose clockwise code lowers the value), the map; Python tests.

- `reverse_knobs`: `B757_MAIN_CHRONO_ET_SWITCH`, `B757_MAIN_CHRONO_ET_SWITCH_FO`: the emitted `cw` is the vendor's anticlockwise code and `ccw` its clockwise code, so `Bbs757Rpn.StepTo` (cw goes up) lands where it aims; a `reverse_knobs` node whose emitted `cw` still contains `--` raises `CurationError`.
- [x] **Steps 1-5** (test `test_chrono_et_cw_raises_the_value`); commit `fix(b757): chronometer ET steps toward the pick`.

### Task 5: Only controls the cockpit has

**Files:** generator (reads the glTF node names of `BBS_752_PW_INTERIOR.gltf` and `BBS_752_RR_INTERIOR.gltf`; stop with `CurationError` if either is missing), the map; tests as Task 1.

- `def cockpit_presence(node: str, pw: set[str], rr: set[str]) -> str` → `"both" | "pw" | "rr" | "none"`. A control in neither is dropped and reported (expected: the ADF switches, WX R Test, the P6 display and event buttons, guards 13-15, the P61 panel guard, `B757_EICAS_COMPUTER_KNOB`). In one model only → " (PW)" / " (RR)" appended unless the label already ends with it. Curation `keep_without_node` keeps a control BlueBird's list proves real (empty unless needed).
- [x] **Steps 1-5** (tests `test_cockpit_presence`, `test_variant_suffix_not_doubled`, `test_absent_controls_dropped`; C# `No_control_without_a_cockpit_node`); commit `fix(b757): the map holds only controls the cockpit has`.

### Task 6: Replay every control's click code in a test

**Files:** Create `tools/b757-gen/rpn_replay.py` (a small RPN interpreter for the subset the cockpit code uses: numbers, `(L:…)` / `(>L:…)` with optional units, `(>K:…)` recorded and ignored, `!`, `+`, `-`, `++`, `--`, `min`, `max`, `==`, `<`, `>`, `if{ } els{ }`, `d`, `%`; `O:` and `E:` reads as 0, `O:` writes recorded); test `tools/b757-gen/test_generate_b757_map.py`.

- `def replay(code: str, lvars: dict[str, float]) -> dict[str, float]`.
- Test `test_every_control_replays_to_its_positions`, over the generated map: for each toggle, guard and select, pressing from each position lands on another listed position of its state var; for each switch with cw / ccw, each step from each position lands on the next or previous listed position or stays at an end; no press, cw or ccw fails to parse; guarded controls are skipped (they have no code). A control that fails is reported by node with the code, and the test fails.
- [x] **Steps 1-5**; commit `test(b757): replay every panel control's click code`.

### Task 7: Gear lever and flap lever on the panels

**Files:** `curation.json` (`include_nodes`, `node_panels`, `positions`, `control_order`), the map; tests as Task 1.

**Interfaces:**
- "Gear Lever" (node `B757_MAIN_Landing_Gear_Y`), state var `B757_GEAR_HANDLE_POS` (BlueBird: "for hardware, use B757_GEAR_HANDLE_POS detents"), positions 0 UP, 0.5 OFF, 1 DOWN, no click code (the generic path writes the state var), first in the Forward panel's "Landing gear" panel. The after-takeoff checklist's "gear UP, then OFF" needs OFF, which the simulator's gear keys cannot select.
- "Flap Lever" (node of the flap handle, XML 32845), state var `B757_FC_FLAP_LEVER`, positions 0 UP, 17 1, 41 5, 67 15, 95 20, 125 25, 150 30 (BlueBird's list), no click code, in the Pedestal's "Control stand" panel after Speedbrake Up.

- [x] **Steps 1-5** (C# `Gear_and_flap_levers_are_on_the_panels`); commit `feat(b757): gear lever with its OFF detent and the flap lever on the panels`.
- In-sim (owner, airborne after takeoff only): Gear Lever UP, then OFF; Shift+G says gear up; Flap Lever 5 → "Flaps 5".

### Task 8 [in flight]: Altitude Intervene, or not

- In flight (owner, VNAV climb or descent with an altitude constraint ahead on LEGS): press the map's "ALT SEL Push" (`BBS757_ALTSEL_CLICK`) and see whether the constraint goes or VNAV changes level; snapshot before and after (`docs/b757/tools/lvar-snapshot`). Record in `docs/b757/evidence/flight-02.md`.
- Works: label it "Altitude Intervene" (C# test). Does nothing: `exclude_nodes` with a `_notes` line citing `flight-02.md`, a test that it is absent, and a line in this plan's handoff for plans 05 and 16.
- [ ] **Steps 1-5**; commit `fix(b757): altitude selector push per the flight check`.

## In-sim checks (owner)

Run them in this order, with the 757 loaded and BBS Nest running. Checks 2, 3 and 7 hold the plan's own two (the fixes; the essentials, as in Task 7); the others were added when the plan landed.

1. **First, on the ground, on a saved flight** (reload afterwards if anything moves): pick Gear Lever UP. The gear must stay down and the lever must read DOWN again: BlueBird's gear lock has to hold (ruling R13, handoff). If the gear retracts, stop the other gear checks (7): plan 12 then has to refuse a pick other than DOWN while the aircraft is on the ground (handoff).
2. **The guarded switches** (moved here from plan 03's Task 4: they need this plan's rows). Overhead miscellaneous "Emergency Lights" reads ARMED with its guard Closed (preset Before start); pick OFF (the guard opens), ARMED (the guard closes), ON, ARMED. Stab Trim C Cutout: CUTOUT, then NORMAL, on the ground only.
3. **The fixes.** Mode Select "AUTO 1"; TCAS Mode cycles STBY, ALT RPTG OFF, XPNDR, TA ONLY, TA/RA and back; the electric pumps read C1, C2, L and R ELEC Pump and switch the right pumps (Alt+S's hydraulic page).
4. **Chronometer ET,** captain and first officer: pick RUN, HOLD and RESET in turn; each goes straight to the pick.
5. **Flight deck door:** pick Deny, then Unlocked. The switch springs back to Auto by itself (BlueBird's own update, within a second or two). Note whether Deny took.
6. **Clicks with a vendor slip,** whose handling by the sim is unmeasured: a fuel pump or crossfeed button pressed from the app switches its pump or valve (8 of these buttons carry a slip in their press); the APU fire handle pulls and stows (its press has a dangling `and`, described in the curation's `_notes`).
7. **The essentials, airborne after takeoff only:** Gear Lever UP, then OFF (Shift+G says gear up); Flap Lever 5 says "Flaps 5". Check that the gear lever's combo reads OFF when you come back to it, and that no pick is announced twice (the readouts' own call-out and the combo's read-back: see the handoff, `LinkedStatusKey`).
8. **Task 8 [in flight]:** the ALT SEL push in a VNAV climb or descent with an altitude constraint ahead (Task 8).

## Handoff to plan 16

- **Pilot-facing** (changelog lines; plan 03's `fix` line already says the emergency lights and the stab trim cutouts work from their panels with their guards opened and closed by the app, so fold the two instead of saying it twice):
  - `fix`: "The 757's panels name every switch position the way BlueBird does (seat belts AUTO, TCAS XPNDR and TA ONLY, engine start CONT and FLT, pack STBY settings, bank limit, alternate flaps and more), and the hydraulic electric pumps, the crossfeed valves, Speed Intervene and the switch guards carry their right names."
  - `fix`: "757: the emergency lights and the stab trim cutouts are guarded switches on their panels, the chronometer ET and flight deck door switches go straight to the position you pick, the controls the 757's cockpit does not have (the ADF panel among them) are gone, and the four controls only the Rolls-Royce cockpit has say (RR)."
  - `feature`: "757: the gear lever, with the OFF detent the simulator's gear keys cannot select, is on the Landing gear panel (UP, OFF, DOWN), and the flap lever (UP, 1, 5, 15, 20, 25, 30) is on the Control stand."
- **docs/b757.md:** "The control map" needs these, all from the code and its curation notes:
  - Curation keys: `kinds` (node → toggle or button: a momentary button re-read as a toggle, a select as a button; the rules decide first, so an entry never brings back a control they drop), `guarded` (a switch whose vendor click is gated on its guard: no click code, plan 03's writer opens and closes the guard), `direct_write` (a switch whose vendor click the map leaves out, written through its state var; the generator checks that the click writes nothing but that var and O: variables, and lets an O: write through because the sim's calculator cannot set a component's O: variables, which is inferred from plan 03's fire handle relock and unmeasured on the door), `keep_without_node` (empty today) and `levers` (drag handles the rules leave out, the gear and flap levers; a lever under an excluded banner also needs `include_nodes` and a `node_panels` rule).
  - The cockpit-presence rule: the generator reads the node names of the two standard interior models (the package ships six, which agree on every control node), leaves out a control no cockpit has, and puts `(PW)` or `(RR)` on a control only one has unless its label already says so. `drop_reason` is the one place every drop decision is made, on the row as the extractor read it, so no curation key rescues a dropped control. The gate-note check: any control outside `guarded` whose press, release, cw or ccw carries the extractor's `[if …]` note stops the run.
  - The extractor keeps a private `_component_vars` per row (the L: variables the whole component reads and writes), which is how a `levers` state var is checked. The replay test (`rpn_replay.py`, `ClickCodeReplay`): every click code must parse, and a toggle, guard, select or stepping switch is replayed from each of its positions and must land where the map says; `PRECONDITIONS` holds the six fire controls whose click acts only while another L: variable allows it (each entry must be needed), `VENDOR_SLIPS` the nine controls whose vendor click no RPN parser accepts (each must still fail to parse, and the rest of its click must work); the interpreter assumes a pop from an empty stack gives 0, which is unmeasured.
  - BlueBird's variable list (support.bluebirdsim.com/docs/simvars, values checked against source 2026-10-21) is the naming source for positions, each cited in the curation's `_notes`. The stale paragraph at `docs/b757.md:62-68` ("Positions are named only where evidence backs the name ... the rest read 'Position n'. Ruling R32 ...") is no longer true: only the Duct Leak Fuel Qty test switch (`B757_VC_P61_SWITCH_5`, values 0 and 2) still reads "Position n". Do not carry over spec 3.7's line that every switch step moves one listed position: the replay test found the flight deck door switch writes absolute values. The map now holds 405 controls (424 before this plan).
- **The guide** (`guide/boeing-aircraft-and-the-md-11.md`): line 405 still lists an ADF panel and a Control stand without the Flap Lever (it is now Parking Brake, Speedbrake Armed, Down and Up, Flap Lever, TO/GA and A/T Disconnect), and the Landing gear panel gains the Gear Lever (UP, OFF, DOWN); line 416 says a guarded switch jumps back, which is no longer true once plans 02 and 03 are both in: BA opens and closes the guard of the emergency lights and the stab trim cutouts for you (the guard keeps its own row).
- **Proposed rule line:** `- [B757-6] A guarded 757 control (`guarded` in the curation, `Bbs757Control.Guard` in the app) carries no click code: its vendor click is gated on the guard, so the generator emits only the state var and the guard fields and stops on any `[if …]` gate note left in a press, release, cw or ccw. Full: docs/invariants/b757.md#b757-6`
- **A second proposed rule line,** for `direct_write` (its number is the next free one after plan 04's B757-15; plan 16 numbers them all): `- [B757-16] A 757 control whose click code the map leaves out (`direct_write` in the curation) is written through its state var, so its vendor click may write only that var and `O:` variables; another L:var, a K: or H: event or a gate note would be lost, and the generator (`check_direct_write`) stops on any of them. Full: docs/invariants/b757.md#b757-16`. The full text names the controls it exists for (the chronometer ET levers, whose vendor code can step onto 2, which is no position; the flight deck door switch, whose codes write 2 and 0 whatever it reads) and why an `O:` write is let through (component-local variables the sim's calculator cannot set: inferred from plan 03's fire handle relock, not measured on the door).
- **Removed controls** (22, from Task 5's run report; the map went from 425 controls to 403; no node of them is in either interior model, so no pilot could find the control):

  | Node | Label | Section / panel it was on |
  |---|---|---|
  | `B757_EICAS_COMPUTER_KNOB` | EICAS Computer | Pedestal / EICAS control |
  | `B757_PED_ADF_TONE_SWITCH` | ADF Tone | Pedestal / ADF |
  | `B757_PED_ADF_L_TEST_SWITCH` | ADF L Test | Pedestal / ADF |
  | `B757_PED_ADF_R_TEST_SWITCH` | ADF R Test | Pedestal / ADF |
  | `B757_PED_ADF_L_SWITCH` | ADF L | Pedestal / ADF |
  | `B757_PED_ADF_R_SWITCH` | ADF R | Pedestal / ADF |
  | `B757_PED_WX_R_TEST_BUTTON` | WX R Test | Pedestal / Weather radar right |
  | `B757_P6_DISPLAY_1_EADI_BUTTON` | P6 Display 1 EADI | Other / P61 panel |
  | `B757_P6_DISPLAY_1_EHSI_BUTTON` | P6 Display 1 EHSI | Other / P61 panel |
  | `B757_P6_DISPLAY_1_EICAS_BUTTON` | P6 Display 1 EICAS | Other / P61 panel |
  | `B757_P6_DISPLAY_2_EICAS_BUTTON` | P6 Display 2 EICAS | Other / P61 panel |
  | `B757_P6_DISPLAY_2_EADI_BUTTON` | P6 Display 2 EADI | Other / P61 panel |
  | `B757_P6_DISPLAY_2_EHSI_BUTTON` | P6 Display 2 EHSI | Other / P61 panel |
  | `B757_P6_EVENT_AUTO_TEST_SWITCH_BUTTON` | P6 Event Auto Test | Other / P61 panel |
  | `B757_P6_EVENT_MAN_TEST_SWITCH_BUTTON` | P6 Event Man Test | Other / P61 panel |
  | `B757_P6_EVENT_MAIN_TEST_SWITCH_BUTTON` | P6 Event Main Test | Other / P61 panel |
  | `B757_P6_EVENT_REC_TEST_SWITCH_BUTTON` | P6 Event Rec Test | Other / P61 panel |
  | `B757_P6_EVENT_ERASE_TEST_SWITCH_BUTTON` | P6 Event Erase Test | Other / P61 panel |
  | `B757_GAURD_13_SWITCH` | Left Guard | Other / P61 panel |
  | `B757_GAURD_14_SWITCH` | Center Guard | Other / P61 panel |
  | `B757_GAURD_15_SWITCH` | Right Guard | Other / P61 panel |
  | `B757_P61_PANEL_SWITCH_GAURD` | P61 Panel Guard | Other / P61 panel |

  The ADF panel held only its five switches (its tuning knobs are dropped as infinite knobs, as before), so the panel went too. Four controls only the RR cockpit has now end in "(RR)": `B757_OVHD_L_ELEC-ENG-CONT_BUTTON` "L ELEC ENG Cont" and `B757_OVHD_R_ELEC-ENG-CONT_BUTTON` "R ELEC ENG Cont" (Overhead / Engine control and yaw damper), `B757_GAURD_1_SWITCH` "Elec Eng Cont 1 Guard" and `B757_GAURD_2_SWITCH` "Elec Eng Cont 2 Guard" (Overhead / Switch guards); the four fuel control switches already ended in (PW) or (RR) and are unchanged. `keep_without_node` is empty: BlueBird's list has a row for 20 of the 22 (not for the EICAS Computer knob or the WX R test button), but each is a variable the XML declares, and a row does not say the control is in the cockpit. If a flight shows one of them does something, one `keep_without_node` line brings it back.
- **Owner decisions to record:** the 14 audio mic selects are buttons now, as this plan's Task 2 said (a press selects that microphone and clears the others, so Off was unreachable), and so they no longer show which microphone is selected; the PMDG 737 shows a selected-mic selector instead. Options: a per-panel selector (plan 12), or On and Off positions on the buttons with a read-back (that needs the write path, plan 12). The owner decides. The 22 removals above and ruling R13 below are also the owner's to overturn.
- **The gear lever on the ground (ruling R13):** the app writes `L:B757_GEAR_HANDLE_POS` directly. BlueBird documents for hardware writers of that variable that "Gear lock and aircraft logic apply", and the cockpit's own drag moves the lever only while airborne (XML:25092-25093); whether the lock holds against a direct write on the ground is unmeasured. The owner's first in-sim check is a pick of Gear Lever UP on the ground, on a saved flight. If it retracts the gear, plan 12 refuses a pick other than DOWN while SIM ON GROUND through plan 01's `HandleOverheadSet` seam, and a rule line follows.
- **For plan 12** (it takes over `tools/b757-gen/**`, the map, `Bbs757ControlMap.cs` and `Bbs757ControlMapTests.cs`):
  - Hand-typed facts the generator does not verify: `guard_closed` against the guard row's Closed value; the gear lever's state var, which is checked only as one of its component's variables (it is not told from its siblings) and whose positions are unconstrained; the positions on `direct_write` rows; and explicit position pairs that are malformed or out of order, which give a `KeyError` instead of a `CurationError`.
  - Plan 12's planned `direct_positions` has the shape of `levers` (node → state var and positions with their values, no click code) or of `direct_write`: generalize one of them rather than add a parallel key. Today `levers` takes in only a row the rules drop as a lever, and for no other reason.
  - Size: the generator is about 1,330 lines and its tests about 4,190. Before more keys are added they want a declarative table of which keys exclude each other, and the tests split by mechanism (`Guarded.shipped`, `DirectWrite.shipped` and `shipped_for` repeat each other).
  - The flight deck door as a spring-loaded control (a momentary Deny or Unlock that rests on Auto). The Crossfeed FWD and Aft labels in a Fuel-panel pass: they share a leading phrase, which VAR-1 would put second (unique today). The 14 mic buttons keep labels made from vendor node names ("Audio 1 Mic L Light pressed"), which the same pass can shorten.
  - Open minor findings of the task reviews and the final review, not fixed in this plan (function names, because line numbers have moved):
    - Repeated nodes: `guarded_control` and `lever_control` run on every row of a repeated node before the repeat is dropped, and the `kinds` "does nothing" check also runs on every row of a node (no real node triggers either); the stale-entry error for `kinds` stops at the first node instead of listing all.
    - Duplicated code: `lever_control` and `guarded_control` share a preamble, `valid_lever` and `valid_guarded` share `.pair`, `EXCLUSIONS` repeats `drop_reason`'s literals, pair-form `guarded` positions are never used, and `build_map` keeps a third node-keyed table list.
    - `check_direct_write` covers writes and the extractor's `[if …]` note but not an inline gate that reads another L: variable (`click_vars` could).
    - The `cockpit=None` default of `drop_reason`, `convert_row` and `build_map` silently turns the presence rule off (`main` always passes the cockpit); `find_cockpit_model` needs exactly one model file of each name under SimObjects.
    - Wording: the docstring's `guarded` paragraph and the curation's `guarded` note do not say a guarded entry never overrides the cockpit rule; the `keep_without_node` note's "leave out for another reason" is imprecise for a guarded take-in.
    - The replay: the run report does not name a dropped `O:` write; `rpn_replay._variable` drops units wholesale (it allows bool, enum, number and second(s)); the replay stands in for a parse check (expose `parse()`); the precondition citations are never read and the slip pins lack XML lines; `_component_vars` misses an Update's variables for a component expanded inside a template (`extract_controls.py`).
    - Python tests: fixtures and helpers repeated in `test_generate_b757_map.py` (the Task 1 fixtures, three end-to-end cockpit tests with about ten lines of scaffolding each, `write_chrono_package` beside `write_gated_package`); the 22 and 8 node lists pinned in four places; `control()` shadows a local name; `test_each_label_and_kind_correction_has_its_evidence_in_the_notes` only checks that a node is mentioned; the cockpit contract test's `outcome()` maps every `CurationError` to "stops the run"; no test pins the refusal of an A: write beside the state var; the whole-map replay test does not assert that its cycle and skip groups are non-empty.
    - C# tests: the door and chronometer tests repeat a local `Writes` helper and a culture loop; the de-DE and sv-SE loop in `Chronometer_ET_goes_straight_to_the_pick` cannot fail for the values 1 and 3; `No_control_without_a_cockpit_node` pins today's list (its name is the plan's); `The_control_stand_holds_the_six_controls_taken_in_by_node_and_the_flap_lever` reads as if the flap lever were not taken in by node.
    - A bisect through Task 6's replay-test commit meets a red generator test until the door switch commit after it (ruling R14).
- **Plan 14:** `Bbs757McpWindowLayout.cs:38-42`: the `Overrides` entry that captions Speed Intervene is now redundant, because the map says "Speed Intervene".
- **Plan 16:** the curation's `_notes` cite files under `docs/b757/` that plan 16 deletes (`evidence/live-findings.md`, `spec-gaps.md` and, since this plan's final fix wave, `evidence/flight-03.md`): fold those facts into `docs/b757.md` or the rules' full text.
- **For plans 06 and 14** (the owners of `BBS757Definition.Readouts.cs`): `LinkedStatusKey` does not link the two levers, so after a pick the readouts' own call-out ("Flaps 5", the gear call-outs) follows the combo's read-back. If in-sim check 7 hears a pick twice ([CORE-7]), link them there.
- **For plan 10:** the map now holds the gear lever (`B757_MAIN_Landing_Gear_Y`, state var `B757_GEAR_HANDLE_POS`: 0 UP, 0.5 OFF, 1 DOWN) and the flap lever (`BBS757_lever_flaps`, state var `B757_FC_FLAP_LEVER`: 0 UP, 17 "1", 41 "5", 67 "15", 95 "20", 125 "25", 150 "30"), both written through their state var, and 22 controls fewer than before. No 757 code looks a control up by label.
- **From the in-sim run (`evidence/flight-02.md`, "Findings for other plans"):** for plan 12, the spring-loaded positions read back as their rest position 1.5 s after the pick (chrono ET RESET reads HOLD, the door's Deny and Unlocked read Auto), so a "did not move" for a pick that does not read back would be wrong there; for plans 06 and 14, the stock gear handle has no OFF, so Shift+G and gear call-outs that should say OFF read `L:B757_GEAR_HANDLE_POS`; for plans 03 and 12, the APU fire handle pulls only within 1.5 s of its unlock (the cockpit's own timer), tight for a screen reader user; for plans 06 and 07, EXT PWR was refused by BlueBird while the APU FAULT was latched after a fire-handle shutdown (the press code ran); for plan 12, the clock buttons (CHR "Clock Button" and the glareshield "757 Capt Clock" and "757 FO Clock") want a longer hold than the app's 200 ms, clearer labels, and the F/O glareshield button out of the Captain chronometer panel; for plans 13 and 14, the chronograph (`BBS_CHRONO_ELAPSED_TIME[_FO]`, seconds) is readable, so a readout would make BlueBird's timer usable without sight.
- **Pending (Task 8):** whether the ALT SEL push (`BBS757_ALTSEL_CLICK`) is kept as "Altitude Intervene" or dropped depends on the owner's flight. When it is known, this handoff gains a line for plans 05 and 16, as Task 8 says.

## Execution record

Kept in this file only, so no other plan's push ever touches it. Task 0 claims the plan (**Status**, **Claimed by**,
**Started**); fill in the rest as you go and when the plan lands (index, "Records").

- **Status:** landed (2026-10-26): Tasks 1-7 done, in-sim checks 1-7 passed on the ground (2026-10-27); Task 8 closed without code by Daniel's ruling (2026-10-29, plan 05's record; recorded here 2026-11-13); the airborne gear check and the app's own words for the picks passed at the owner's word (2026-11-15); nothing waits on the owner
- **Claimed by:** Robin Kipp (robin24), 2026-10-26
- **Worked by:** Robin Kipp with Claude (superpowers:subagent-driven-development: a Sonnet implementer and a Sonnet task reviewer per task, an Opus whole-plan review, then a Sonnet implementer for the final fix wave and this record)
- **Started:** 2026-10-26, from `cf1dfce94`
- **Landed:** 2026-10-26. The plan's commits on `B757` are the ones named under Tasks, from the claim to the final fix wave, and the record commit last; `git log --grep "^Plan: 02"` lists them. They were rebased onto `kn4iee/B757` at `5fa654ce6` before the push; `git diff --name-only` against it lists only the plan's own files (`tools/b757-gen/**`, the map, `Bbs757ControlMap.cs`, `Bbs757ControlMapTests.cs` and this file).
- **Tests at landing:** the full suite passed: 17,751 tests, 0 failed, on the final fix wave rebased onto `14944413c` (the only commit since, `5fa654ce6`, changes one plan file) (17,750 on the last task commit, before the final wave's general test; 17,003 at Task 7 before the rebase), with no new warning from the plan's files. The generator tests passed too: 238, as `python -I tools/b757-gen/test_generate_b757_map.py` and in CI's form `python -m unittest discover -s tools/b757-gen -p "test_*.py"`; `Bbs757ControlMapTests` has 130. A regeneration of the map from the installed package (0.1.0) is byte-identical to the committed one.
- **Tasks:** Tasks 1-7 done, each reviewed by a task reviewer: Tasks 1, 3, 6 and 7 passed at once, Tasks 2, 4 and 5 took one fix round each. Task 8 is left: it needs the owner's flight. The map went from 424 controls to 425 (Task 3), 403 (Task 5) and 405 (Task 7).
  - Task 0, the claim: 6734d0e09 (pushed to `kn4iee/B757` before Task 1).
  - Task 1, the position names: eb9320100.
  - Task 2, labels and kinds: abb287b5d, fix round 81c0f1035 (a curated `kinds` entry never rescues a control the rules drop).
  - Task 3, guarded controls: e6d03400a.
  - Task 4, the chronometer ET switch: 7b04f0533 (`direct_write`, ruling R10), fix round 685da68ab (a `direct_write` control must write only its own state: the premise is now checked).
  - Task 5, only controls the cockpit has: 2b77692d1, fix round e1665a0bc (a `guarded` entry could still keep a control no cockpit has).
  - Task 6, the replay test: 918cea525, then c156f6a93 (ruling R12: the flight deck door switch, which the replay found).
  - Task 7, the gear lever and the flap lever: d3054c54f.
  - Final review (Opus; no Critical, three Important) and its fix wave: e3dc2863a (the gear lever note, the guarded switches' note, a general test that every `Guard` names a guard row, wording), then this record.
  - Task 8 [in flight], Altitude Intervene or not: closed without code. The ALT SEL push changed nothing in a VNAV climb on 2026-10-28 (`flight-13.md`, "For other plans") and in a VNAV step climb on 2026-10-29 (Daniel), and none of BlueBird's v1.01 manuals describes altitude intervention. Daniel's ruling (plan 05's record): the panel keeps "ALT SEL Push" (it is not excluded from the map), no window gets it, and the guide says to step climb on the CDU's CRZ page (plan 16).
- **Waiting on the owner:** nothing. The two checks that were open (airborne, Gear Lever UP then OFF with Shift+G, which was changed after the 2026-10-28 flight to read BlueBird's own lever, OFF included: plan 06's record; and, with the app running, what it says for these picks) had been run by the owner earlier and passed; he said so on 2026-11-15 ("the remaining plan 02 and 07 checks were also already tested previously and can be marked done").
- **In-sim results:** `docs/b757/evidence/flight-02.md`. Check 1 passed (owner): the gear stays down with the lever UP on the ground, so ruling R13 holds. Checks 2-7 passed on the ground on 2026-10-27, run by Claude with the owner's go-ahead: the app's exact calculator calls through MobiFlight, BlueBird's L:vars read back after the app's read-back delay, the app itself not running. Emergency lights and stab trim C cutout, Mode Select, TCAS Mode, the four electric pumps, chrono ET (RESET springs back to HOLD), the flight deck door (springs back to Auto in about 0.9 s), the slipped fuel pump buttons, the APU fire handle, Gear Lever OFF and DOWN, Flap Lever 5 and UP all land; the door switch is cosmetic in BlueBird (its systems module never reads it), so its short hold does not matter; the ET count is internal to BlueBird's module (no variable), and `BBS_CHRONO_ELAPSED_TIME` is the chronograph, cycled start, stop, reset by the clock's CHR button or the glareshield CLOCK button, which BlueBird registers from a 200 ms hold (the app's hold is 200 ms).
- **Changed from the plan:**
  - Task 4 (ruling R10): `direct_write` instead of `reverse_knobs`, and RESET is 3 with 2 left out of the positions (Task 1: BlueBird's list says "3 with 2 unused", and the generator stops on a list that names RESET at 2). The test `test_chrono_et_cw_raises_the_value` became `test_chrono_et_writes_its_state_directly`, with the C# `The_chronometer_ET_lever_keeps_no_click_code_and_names_RESET_at_3` and `Chronometer_ET_goes_straight_to_the_pick`; the plan's other test names are kept.
  - Task 6 found that the flight deck door switch writes absolute values (a step to Auto from either end lands on the other end), so ruling R12 made it a `direct_write` control too, and `direct_write`'s check lets `O:` writes through. That check is also an addition (Task 4's fix round, because the premise was unchecked): the vendor click must exist, carry no gate note and write only the state var and O: variables, and the node may not also be under `kinds` or `guarded`.
  - Curation keys: `guarded` and `keep_without_node` as planned; `kinds` (Task 2: the APU fire handle is a toggle on its pulled state, the 14 mic selects are buttons), `direct_write` (Task 4) and `levers` (Task 7: the gear and flap levers are drag handles the rules leave out, and `include_nodes` cannot take in a `lever`) are additions. A `kinds` entry only re-reads a momentary button as a toggle or a select as a button, and `drop_reason` decides first, on the row as the extractor read it, so no curation key rescues a control the rules drop (Task 2's fix round).
  - The extractor keeps a private `_component_vars` per row (Task 7): the gear lever's state var is in no code of its own node, so the generator checks a `levers` entry against the L: variables of the lever's whole component. The extractor's own outputs are byte-identical.
  - `rpn_replay.py` (Task 6) also understands `and`, `or`, `(A:…)` reads (as 0) and `(>H:…)` events, and the replay test also checks a switch with a press only (the two engine fire handles cycle forward), parses release codes and fails on a control shape no check covers. It needs two tables: `PRECONDITIONS` (six fire controls whose click acts only while another L: variable allows it) and `VENDOR_SLIPS` (nine controls whose vendor click no RPN parser accepts: eight pump buttons whose press has a set with no variable type, and the first officer's right ADF knob, whose press ends in a stray `}`).
  - Task 5: the 22 removals were exactly the expected list, and the ADF panel went with them (it held only five of the 22), so its `node_panels` rule and `panel_order` entry were removed too. `keep_without_node` stays empty. Four controls only the RR cockpit has got "(RR)"; the four fuel control labels already said (PW) or (RR).
  - Task 7: the flap lever's node is `BBS757_lever_flaps` (the plan's "XML 32845" is the template line of its component, which starts at XML:32844). The Review Focus line that the gear lever's UP is never sent on the ground by a test or the sweep still holds; the owner's first in-sim check is the one deliberate try (ruling R13).
  - Task 3's interim ("a guarded pick finds no click code, writes the state var, and BlueBird's guard refuses it") did not last: plan 03's guarded writer was on `B757` before this plan landed, so a pick opens or closes the guard. Ruling R11 lapsed with it.
  - The generator test command is `python -I tools/b757-gen/test_generate_b757_map.py` (ruling R2); the plan's `python -I -m unittest tools/b757-gen/...` form fails.
  - The final fix wave changed curation notes (the gear lever's lock and the owner's first check; the stab trim write-through and plan 03's writer; the APU fire handle's dangling `and`; the six interior models), added a general test that every `Guard` names a guard row, and fixed wording in `rpn_replay.py`, `Bbs757ControlMap.cs`, the generator's cockpit comments and one test comment. The map and the generator's behavior did not change.
- **Rulings made while working** (question; answer; who decided. Claude was the controller and made all of these; the owner may reverse any):
  - R1. Where does the plan run? In the existing worktree `.claude/worktrees/clone-b757-fork-branch-aa0dcf` (branch `B757-p02-control-map`), not a new `b757-p02` one: it is inside the repo and already dedicated to the B757 work; Claude.
  - R2. How are the generator tests run? As `python -I tools/b757-gen/test_generate_b757_map.py` (CI's discovery form works too): the plan's `python -I -m unittest tools/b757-gen/...` fails with "No module named 'tools'" on Python 3.14; Claude.
  - R3. How does `positions` name a control whose values are not 0, 1, 2 ...? The list form stays: the generator lays the names onto the control's own values in ascending order (the warning indicator's 0 and 11), and explicit `{value, label}` pairs cover a value the click code never rests on (the start selectors never write 2; the chronometer ET lever skips 2). No format change was needed; Claude.
  - R4. Where the plan keys a row by its L:var (the emergency lights), what keys it? The control's cockpit node, with the L:var as `state_var`; a row whose node is in neither interior model would go under `keep_without_node` with a note citing BlueBird's list. None needed it: the six guarded and guard nodes and both levers are in both models; Claude.
  - R5. Task 1's "no Position n" test and Task 5's drop of the second EICAS Computer knob contradict in order? The test exempts the knob until Task 5 drops it, and Task 5 removes the exemption; Claude.
  - R6. What if a change breaks a test file another plan owns? The implementer stops and reports and the controller rules (the index's ownership table). None broke: the (PW) and (RR) suffix never doubles, so `Bbs757DefinitionTests`' five Engines labels hold; Claude.
  - R7. What kind are the gear and flap levers? `switch` with a state var and no click code, so `Bbs757Write.ToPosition` writes the state var; Claude.
  - R8. Push after every task, as the index allows? Once, after the final whole-plan review (rebase, full suite, ownership diff): no plan builds on plan 02's files before it lands, and the final review sees everything first; Claude.
  - R9. Which trailers do the commits carry? `Plan: 02` and a `Co-Authored-By` line naming the model that wrote the commit (Claude Sonnet 5.5); Claude.
  - R10. How do the chronometer ET switches step? `direct_write`, not the planned `reverse_knobs`: a swap of cw and ccw fixes the direction, but a step down from RESET still lands on 2, which is no position, and the vendor's click writes nothing but that one variable, so writing the picked value directly loses nothing; Claude.
  - R11. The stab trim cutouts write through with their guard closed until plan 03's writer lands (their template has no revert, unlike the emergency lights switch's: XML:1447-1455 against XML:1529-1535)? Accepted for this plan: before Task 3 they did nothing, and after plan 03 the app opens the guard itself. It lapsed once plan 03's guarded writer landed on `B757`; Claude.
  - R12. The flight deck door switch, whose absolute codes send a step to Auto from either end to the other end? `direct_write` too, and the `direct_write` check lets an `O:` write through, because the sim's calculator cannot set a component's O: variables (inferred from plan 03's relock symptom, spec 3.7 #6, not measured): the app's replay of the click never performed `(>O:UpdateTime)` either. If the calculator can set them, a Deny hold could end sooner than BlueBird's own (in-sim check 5). Presenting it as a spring-loaded control is plan 12's; Claude.
  - R13. Gear Lever UP on the ground, with only BlueBird's documented gear lock between the pick and a retracted gear? Keep UP, OFF and DOWN as planned. The owner's first in-sim check is a pick of UP on the ground, on a saved flight; if the lock does not hold, plan 12 refuses a pick other than DOWN while SIM ON GROUND. The owner is told before he flies this build. The app already lets any combo commit a consequential switch (the fuel control switches alike), and the plan's essential is the OFF detent; Claude.
  - R14. Squash or reorder the red commit (Task 6's replay test, red until the door switch fix)? No: the per-task commit structure stays as reviewed, and a bisect meets one commit where the generator test fails; Claude.
  - Which findings of the final review are fixed before the push? The record, Task 4's text and the handoff (Important 1); the gear lever note, the handoff and the first in-sim check (Important 2); the owner decision on the mic selects in the handoff (Important 3); and the text-only items (the chronometer comment, the replay docstring, the APU `and`, the stab trim note, the loader comment, the interior model comments, one general test). Everything else went to plan 12's handoff or was dropped; Claude.
