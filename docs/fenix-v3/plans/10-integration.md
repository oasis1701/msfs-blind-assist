# Fenix v3 Integration Implementation Plan (plan 10)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring plans 02-09 together on `FenixV3`, apply every handoff they left, write the docs, rules, guide and changelog, run one short owner-in-sim pass, delete `docs/fenix-v3/`, and open the pull request to `main`.

**Architecture:** One person, after 02-09 are done on `FenixV3` (contributors push there directly; this is the only pull request). This plan owns every file the index assigns to "nobody before 10" (MainForm partials, `SettingsManager.cs` beyond plan 02's seed lines, shared tests, `docs/`, `guide/`, `changelog.d/`, `CLAUDE.md`, `.claude/rules/`), and edits other plans' files only for the handoff items listed here.

**Tech Stack:** C# / .NET 10, xUnit, Markdown, `gh`.

**Spec:** `docs/fenix-v3/spec.md`; index: `docs/fenix-v3/plans/00-index.md`; each plan's "Handoff to plan 10" section is the source of truth for its items (this plan lists them by file).

## Global Constraints

- CLAUDE.md in full: CORE-19 (guide in the same PR), the changelog fragment rule (`changelog.d/<pr>-<slug>.<category>.md`, `<pr>` read from `gh pr create`, never guessed), rule lines one per mechanism, at most 400 characters, `Full: docs/invariants/<stem>.md#<id>`, rule files at most 8,000 characters (`ClaudeContextBudgetTests`).
- Never push or open the PR without the owner's go-ahead in the session. Auto-merge is disabled on this repo: the owner merges.
- In-sim: few, prioritised checks with exact keys and phrases; each plan's own checks were run when that plan ran. Nothing here repeats them.
- Owner answers recorded in `00-index.md` (section "Owner answers", added by the coordinator) decide every conditional item below; an unanswered conditional item is left out and listed as a follow-up.

## Review Focus

- A pilot who muted a renamed row in v2 (DC SERVICE, door video) must not hear it again after the update (Task 2, `SettingsManager` carry test).
- A spoken frequency or altimeter value under de-DE must read with a dot, never a comma (Task 2, the two culture fixes, each with a de-DE test).
- The guide must never name a key the app does not register (Task 5: every key named is checked against `HotkeyManager.cs`).
- `ClaudeContextBudgetTests` stays green after all new rules (Task 3).
- Removing `docs/fenix-v3/` must not leave any doc or code comment pointing into it (Task 7 grep).

---

### Task 1: Confirm 02-09 are in

**Files:** none (read only).

- [ ] **Step 1:** Pull `FenixV3`. Every row of the claim table in `00-index.md` reads "done", and every plan's "Handoff to plan 10" section is filled in (in-sim results included).
- [ ] **Step 2: The one controlled overlap.** `FirstOfficer/Fenix/FenixActionExecutor.cs`: check plan 02's members (`TakeoffConfigTest`, `AnnounceTakeoffConfigResult`) and plan 08's changes are both present.
- [ ] **Step 3:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` and the full suite. Expected: green. (Pushes to `FenixV3` run no CI, so this is the first combined check.)

### Task 2: Code handoffs, by file

Each item: make the change, add or adjust its test, run the suite, commit per file group. Source plan in brackets.

**`MSFSBlindAssist/Settings/SettingsManager.cs`** [02 H1]
- [ ] Add `FenixRenamedMonitorKeys = { ("B_ELEC_BUS_POWER_DC_SERIVICE", "B_ELEC_BUS_POWER_DC_SERVICE"), ("I_OH_DOOR_VIDEO", "I_OH_COCKPIT_DOOR_VIDEO") }` and carry them in `CarryRenamedMonitorMutes`; test `FenixRenamedMonitorMuteTests` modelled on `A380RenamedMonitorMuteTests` (a muted old key is muted under the new key after load).

**`MSFSBlindAssist/Aircraft/FenixA320Definition.cs`**
- [ ] [02 H2] Delete the three `FNX2PLD_speedV1/VR/V2` definitions and their comment (plan 08 now reads `N_MISC_PERF_TO_*`); `FenixV3FixNamesTests.Only_the_takeoff_speed_FenixQuartz_names_remain` becomes "no FenixQuartz names remain".
- [ ] [09 H1] `protected override IReadOnlyList<AiDisplayRead> DisplayReads => FenixA320DisplayReads.For(Variant);` (about :13185) with plan 09's summary; replace the stale comment at about :13204 ("…dispatched from FenixA320DisplayReads by the base, on measured views.").
- [ ] [03 H6] `S_OH_ENG_N1_MODE_1/2` are registered twice in one initializer (about :4720-4735 and :5761-5778): keep one definition (the panel control; VAR-6, VAR-7), test that each name is registered once.
- [ ] [07, also seen] RMP frequency spoken with `{freqMHz:F3}` in the PC's culture (about :12154): invariant culture; de-DE test.
- [ ] [07, also seen] FD and LS pushbutton lights announced as "… FD Fault" / "… LS Fault" (about :1258-1275, :1321-1338): read the lights in the sim (owner) and name them by what they show; never infer a legend.
- [ ] [08 H4, owner's choice] `NotSetBelow = 1` on `N_MISC_PERF_TO_V1/VR/V2` (A320-10) if the owner agrees.
- [ ] [04 H2, conditional] Output O on the Fenix reads `aircraft.temperature.oat` through `DataService.ReadAsync` when plan 04's check 2 showed a 1 °C or larger difference from stock; else nothing.
- [ ] [07 R1, R3, R4, conditional on M1, M3, M4] FCU encoder sets with acceleration on; baro set with Linked EFIS; COM standby against the Fenix's channel spacing: apply only the ones plan 07's measurements showed misbehaving, as plan 07 describes them.
- [ ] [03 H1-H4, conditional on owner answers] menu/profile name (`AircraftName` and `MainForm.Designer.cs` menu text, never `AircraftCode`); hide empty panels in `GetPanelStructure`; CALLS MECH/ALL/FWD/AFT as press buttons; panel rebuild on `VariantChanged` (`MainForm.PanelBuilder.cs`).

**`MSFSBlindAssist/FirstOfficer/Fenix/`**
- [ ] [02 H4] Delete the unused `TakeoffConfigTestHoldMs` and its comment in `FenixActionExecutor.cs` (about :58-63) and the "holds 1.5 s" comment in `FenixChecklistDefinitions.cs` (about :302).
- [ ] [07 R2, conditional on M2] FD/LS sync: press one side and verify both (`FenixFlowDefinitions.cs` PF_FD1/PF_FD2, AP_LS1/AP_LS2, SD_LS1/SD_LS2; `FenixChecklistDefinitions.cs` about :378-385, :473-479).
- [ ] [08 H3, owner's choice] the read-only `FO_CABIN_READY` check as plan 08 describes it, with its parity entries and the three shared A320-family test edits.

**Other**
- [ ] [02 H5] `MainForm.Hotkeys.cs:524`: drop the comment naming `FNX2PLD_speedV1/VR`.
- [ ] [07, also seen] `Forms/FenixA320/FenixBaroWindow.cs:280` "Setting altimeter to {inHgValue:0.00}": invariant culture; de-DE test.
- [ ] [09 H5] `FenixVariant.With`: confirm `B_CONFIG_STDBY` 1 maps to `HasIsis == false` once plan 09's check read 1 on a round-gauge livery.
- [ ] [04 H4, conditional on owner Q1 = always-on] the Ctrl+M row for the engine running announcements (monitor manager, `UserSettings.FenixDisabledMonitorVariables`).
- [ ] [03 H5, after its in-sim check] `UiEchoVariable = "I_OH_PNEUMATIC_ECON_FLOW_L"` on ECON FLOW if its light only follows the switch; ACT PUMP labels from the read legends.

### Task 3: Rules and their full text

**Files:** `.claude/rules/fenix-data.md`, `docs/invariants/fenix-data.md`, `.claude/rules/first-officer-airbus.md`, `docs/invariants/first-officer-airbus.md`, `.claude/rules/ai-display.md`, `docs/invariants/ai-display.md`, `docs/invariants/a32nx-fenix.md` (A320-40 text), `CLAUDE.md`.

- [ ] **Step 1:** Collect the proposed lines from each plan's handoff: FNX lines from 02 (2), 03 (2), 04 (2), 05 (3), 06 (3), 07 (2, plus 1 if options editing was built), 09 (1); FOA-13 to FOA-15 from 08; AI-10 from 09. Number the FNX lines FNX-4 upward in merge order; drop any whose mechanism a test with a self-explaining name already pins (the CLAUDE.md "one mechanism per ID" rule).
- [ ] **Step 2:** Add each line and its `## ID` full text (from the plan's evidence and owner decisions, with dates and measurements).
- [ ] **Step 3:** If `fenix-data.md` passes 8,000 characters, split by subject: keep data service, variant and EFB rules in `fenix-data.md`; move the window rules (engines, cabin, failures, options) to a new `.claude/rules/fenix-windows.md` with globs `MSFSBlindAssist/Forms/Fenix/**`, `MSFSBlindAssist/Services/FenixEngine*.cs`, `FenixCabin*.cs`, `FenixFailure*.cs`, `FenixOption*.cs` and their tests, a `docs/invariants/fenix-windows.md`, and the CLAUDE.md row. IDs never change.
- [ ] **Step 4:** [09 H6] In `docs/invariants/a32nx-fenix.md` A320-40, replace "Not covered by this rule: the Fenix EFB window (…)" with "The Fenix EFB window uses the same address (FNX rule)."
- [ ] **Step 5:** Run `ClaudeContextBudgetTests` and the whole suite; follow any failure's message. Commit `docs(rules): Fenix v3 rules`.

### Task 4: Developer docs

**Files:** `docs/a32nx.md`, `docs/first-officer.md`, `docs/fenix-increment-decrement.md`.

- [ ] `docs/a32nx.md` Fenix sections, from the plans and their in-sim results: v3 data service (gateway, unknown name reads `""`, settings/FAP/failures APIs, names only from the bundles); variant detection and per-variant controls (03); gravity gear crank (02, about :71); TO CONFIG (02: the measured delay, `WatchAsync`; about :89-91); AI display reads on v3's cameras (09: the measured table, captain's PFD readable, about :647-700); MCDU key pulse (09 H7, only if its typing check changed it, about :96); Engines window (04); Cabin window and cabin-ready (05; music channels are per livery, not 15); Failures window and endpoints, AND/OR, `time`, Clear (06; the catalog is 549 on the A320 CFM, 555 on the A321 IAE); Fenix options window, labels regeneration (`option_labels.py`), M1-M4 results (07).
- [ ] `docs/first-officer.md`: about :802, the APU AVAIL lamp is `_U` (plus plan 08's v3 re-read); about :76, engine running from the Fenix's own N2 with the stock fallback; about :95-96, the TO CONFIG timing; the A321 pack-flow skip and the cabin-ready live value.
- [ ] `docs/fenix-increment-decrement.md`: drop the `E_PED_RMP*` encoder description the code no longer uses.
- [ ] Commit `docs(fenix): v3 developer docs`.

### Task 5: Pilot's guide and hotkey list (CORE-19)

**Files:** `guide/airbus-aircraft.md` (Fenix chapter), `guide/first-officer.md`, `guide/planning-your-flight.md`, the hotkey appendix, `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`.

- [ ] Fenix chapter, from each plan's "Pilot-facing changes": the "Fenix windows" key (input mode, Ctrl+Shift+F) and its Engines, Cabin, Failures and Fenix options windows; the Flight Control Computers panel; gravity gear; TO CONFIG wording; Generator 1; the A321, extra-tank and IAE controls and "Not fitted on this aircraft."; Alt+P/N/E/S/I on v3 (09 H3, about :306) and "Fenix 2.x: update the Fenix" if the owner chose v3-only; a short note that Fenix v3 needs Navigraph connected in its EFB for current navdata.
- [ ] `guide/first-officer.md`: plan 08's five pilot-facing lines.
- [ ] `guide/planning-your-flight.md:491`: plan 09's EFB message text (09 H4).
- [ ] `Fenix_A320_Hotkeys.txt` (about :33-38 for the reads, plus plan 01's Ctrl+Shift+F line) and the hotkey appendix.
- [ ] Check: every key the guide names exists in `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`; the guide builds (`tools/GuideBuilder`, as `user-guide-build.yml` does).
- [ ] Commit `docs(guide): Fenix v3`.

### Task 6: One short owner-in-sim pass (after Tasks 1-5)

Aircraft: a Fenix v3 A320 or A321 at a gate, cold and dark with ground power available, then engines running. Each line: key, then the exact phrase.

- [ ] 1. `[` then Ctrl+Shift+F: "Fenix windows" lists Engines, Cabin, Failures, Fenix options; each opens and Escape returns.
- [ ] 2. Overhead: "Flight Control Computers" is listed; Electrical: Generator 1 Off then On with an engine running changes GEN 1 (not EMER GEN 1 LINE).
- [ ] 3. A bad TO CONFIG (flaps up) from the panel key: "Takeoff config: check configuration."; set flaps 1: "Takeoff config normal." after the measured delay.
- [ ] 4. Alt+P: the AI describes the captain's PFD.
- [ ] 5. With a pre-update Ctrl+M mute on "ELEC BUS POWER DC SERVICE STATUS" (or a test settings file), the row stays muted.
- [ ] Record results in the PR description.

### Task 7: Remove the working documents

- [ ] `git rm -r docs/fenix-v3`; `git grep -n "docs/fenix-v3"` returns nothing outside `changelog.d` (fix any hit). Commit `docs: remove the Fenix v3 working documents`.

### Task 8: Pull request (owner go-ahead first)

- [ ] **Step 1:** Ask the owner to approve pushing `FenixV3`. Then push and `gh pr create --base main --head FenixV3` with: what changes for a pilot (memory: explain in user terms), the in-sim results, `User guide: updated: Part <n>, Fenix A320 family`, the follow-ups below, and the closing attribution line.
- [ ] **Step 2:** Read the PR number from `gh pr create`'s output. Add the fragments, written for a pilot: `changelog.d/<pr>-fenix-v3-fixes.fix.md` (plans 02, 08, 09), `changelog.d/<pr>-fenix-v3-windows.feature.md` (plans 01, 04, 05, 06, 07), `changelog.d/<pr>-fenix-v3-variants.improvement.md` (plan 03). Use the plans' draft lines. Commit and push.
- [ ] **Step 3:** CI green (tests, changelog, user-guide-line). The owner merges.

## Follow-ups (record in the PR, not built)

- Datalink: DCDU screen and CPDLC (Hoppie / SayIntentions / BeyondATC), AOC messages, printer (spec 4.6).
- An accessible "Fenix documents" window over `/fenix/documents` (09), if the EFB's Documents app proved hard to use.
- ISIS baro readout from `N_MIP_ISFD_BARO_HPA/_INCH` (02).
- Variant follow-ups: brake fans, cargo heat vs vent, GNADIRS IR ALIGN/FAULT lights, analogue standby baro knob, ACT refuel valves and FQI, A321 centre-tank legends (03); confirm `N_CONFIG_AIRCRAFT_TYPE` 2 on an A319.
- Failures "clear all" (06). Fenix options editing, if read-only was chosen (07). Speaking the detected variant (03 Q1), if declined.
- NEO: re-check the 43 CEO-only names when Fenix ships it (spec section 3).
- Registration/SELCAL decode, `FNX_FLT_LOADED` for the First Officer's in-flight start (spec 4.7).
