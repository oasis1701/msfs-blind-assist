# Fenix v3 Fixes Implementation Plan (plan 02)

**Claimed by:** oasis1701 · **Status:** done (2026-10-22: code, reviews and the owner's in-sim checks; see "In-sim checks" for what stays unproven)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair what Fenix v3 (3.0.0.7426) broke in the Fenix A320 definition and the older bugs the investigation found, and make the TO CONFIG check (panel button and First Officer) fail-safe against v3's delayed master warning/caution.

**Architecture:** Name fixes and panel fixes are edits to `FenixA320Definition.cs`, each pinned by a new `*FenixV3Fix*` test. The gravity gear becomes one sequenced control driven by a small testable walker (`FenixA320GravityGear.cs`). The TO CONFIG verdict moves from one cached read at 1.5 s to a shared watch in `FenixTakeoffConfigVerdict` that reads on-request `_READOUT` twins (the [A320-41] pattern) from the observed press until a measured verdict time; the panel and the First Officer both call it.

**Tech Stack:** C# 13 / .NET 10 WinForms, SimConnect (`SimConnectManager.SetLVar`, `ReadFreshAsync`), xUnit.

**Spec:** `docs/fenix-v3/spec.md` sections 1 and 2 (2.4 doc fixes belong to plan 12). Index and ownership: `docs/fenix-v3/plans/00-index.md`. Seams this plan builds on: `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions

My calls (the reviewer can change any of them):

1. **Renamed names get renamed keys** (`B_ELEC_BUS_POWER_DC_SERVICE`, `I_OH_COCKPIT_DOOR_VIDEO`): key equals the L:var again. A pilot's Ctrl+M mute is stored by key (`UserSettings.FenixDisabledMonitorVariables`), so the two old mutes must be carried by `SettingsManager.CarryRenamedMonitorMutes`; that method is outside this plan's lines, so it is a handoff to plan 12 (exact change below). Until it lands, a pilot who muted one of these rows in v2 hears it once and can mute it again.
2. **FenixQuartz: 13 of the 16 `FNX2PLD_*` definitions go now; the three `FNX2PLD_speedV1/VR/V2` stay** until plan 05 moves the First Officer to `N_MISC_PERF_TO_*`. Deleting them here breaks `FenixReadbackTests` (plan 05's file), which asserts they are registered. Plan 12 deletes the three after merging plan 05.
3. **No ISIS baro readout in this plan.** The dead `FNX2PLD_isisBaro` never spoke, so dropping it loses nothing. A real ISIS baro readout (`N_MIP_ISFD_BARO_HPA` 1013 / `_INCH` 2991.39 = inches x100 on the data service, read live today; the L:var encoding is unmeasured, and the unit follows the ISIS's own BARO button) is a new feature in plan 04's ISIS/standby area. Listed as a follow-up for plan 12.
4. **Gravity gear is one control, "Gravity Gear Extension": Stowed / Extended.** Extended raises the handle, then turns the crank to 3 turns in the cockpit template's own 0.05 steps; Stowed turns it back to 0, then stows the handle. "Gravity Gear Crank" is removed. Write method: `SetLVar` per step (the route every Fenix switch uses). The owner verifies it in flight (Task 5); the fallback if it does not extend the gear is in that task.
5. **The overhead ELAC/SEC/FAC panel is named "Flight Control Computers"**, the name the FBW A320 and A380 use, placed after "GPWS" in the Overhead section as the FBW A320 does.
6. **AUTOLAND: keys stay, only the L:var name follows the measurement** (Task 6), so the Autoland buttons' state label and any Ctrl+M mute keep working.
7. **TO CONFIG reads fresh through SimConnect twins, not the Fenix data service**, so the verdict never depends on the gateway being up. "Takeoff config normal." is said only when both master lights read dark at or after `VerdictAfterMs`, timed from the press the watch itself observed.
8. **Seam counts: resolved.** Plan 01's `FenixDefinitionSeamTests` no longer pins totals (it checks the seams are wired and that long-standing keys exist), so this plan never edits it.

Only the owner can answer:

- (a) If Task 1 shows v3 lights neither master light on a bad TO CONFIG, the app can never tell normal from bad. What should it say then? (My proposal, not built: "Takeoff config test done. No warning seen. Check the ECAM memo.")
- (b) ~~Airborne gravity-gear test~~ Answered: ground check only (index, owner answer 2).

## Global Constraints

- Fenix 3.0.0.7426. Names only from the Fenix package model, the data service (an unknown name reads `""`) or names already in the app. Never guess, never introspect the schema.
- Data service: `http://127.0.0.1:8083/graphql`, never `localhost` (A320-40). Probes are read-only. A write to the sim needs the owner's go-ahead in the session.
- Fenix pushbuttons are a full press-release pulse 0→1→0 (A320-3, A320-4). Never leave `S_ECAM_TO` held.
- Never infer a Fenix light's legend from its suffix or the real aircraft (FO-11): display names stay as they are.
- Speech (CORE-7): the screen reader already spoke the pick; the app speaks only a read-back, a refusal, or a verdict. Anything with a number is formatted invariant and tested under sv-SE.
- Line numbers are on `main` 2d6444ab1. Plan 01 shifts lines after `BuildVariables` by a few; find each spot by the quoted text.
- Owned files: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`; new `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs` (matches the a32nx-fenix.md glob `Aircraft/FenixA320*.cs`); `MSFSBlindAssist/FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs`; `MSFSBlindAssist/FirstOfficer/Fenix/FenixActionExecutor.cs` (the whole file for the parallel phase; plan 05 does not edit it, and this plan changes only the take-off config members, the constant they no longer use, and `SetBaroStdBoth` (Task 11)); `MSFSBlindAssist/Settings/SettingsManager.cs` ONLY the Fenix seed (lines 226-227 comment, 240); tests `SettingsSeedTests.cs`, `FirstOfficer/FenixTakeoffConfigVerdictTests.cs` and new `*FenixV3Fix*` / `*FenixTakeoffConfig*` files.
- Test command: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (add `--filter <ClassName>` for one class).

## Evidence read live on 2026-10-21 (Fenix A321 IAE, data service, read-only)

`S_HYD_GRAVITY_GEAR_EXTEND` 0; `S_HYD_GRAVITY_GEAR_HANDLE` `""` (model-only L:var); `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION` `""`. `S_OH_ELEC_GEN1` 1, `S_OH_ELEC_GEN1_LINE` 1. `S_OH_FLT_CTL_ELAC_1`, `_SEC_3`, `_FAC_2` all 1 (latched buttons). `I_MIP_AUTOLAND_{CAPT,FO}` and `_{CAPT,FO}_{U,L}`: all six exist, all 0. `I_OH_ELEC_COMMERCIAL_U` `""`, `_L` 0. `I_OH_DOOR_VIDEO` `""`, `I_OH_COCKPIT_DOOR_VIDEO` 0. `B_ELEC_BUS_POWER_DC_SERIVICE` `""`, `_SERVICE` true. `I_MIP_GEAR_1_L` 1 and `_1_U` 0 on the ground with the gear down. Package: the crank template `FNX32X_Interact_Emergency_Gear_Lever_Template` (`ModelBehaviorDefs/fnx32x/Templates/Interactions.xml` in `fnx-aircraft-a32x-common`) steps `S_HYD_GRAVITY_GEAR_EXTEND` by 0.05 between 0 and 3 at 10 Hz while held, shows the crank only while `S_HYD_GRAVITY_GEAR_HANDLE` > 0, and lets the handle move only while the crank is 0. `Cockpit_Behavior.xml` lights AUTOLAND from `I_MIP_AUTOLAND_{CAPT,FO}_U/_L` (upper, lower, or a "both" node). ELAC/SEC/FAC, GEN 1, GEN 2, GEN 1 LINE and COMMERCIAL are `FNX32X_Interact_Button_Latched_Template`; `S_ECAM_TO` is `..._Momentary_Template`.

## Review Focus

- A master light that comes on later than the old 1.5 s read must give "check", never "normal" (Task 7 test `A_warning_that_lights_late_is_still_caught`).
- A fresh read that times out at the verdict time must never give "normal" (Task 7 `Unread_lights_never_give_normal`).
- A press the watch never sees (First Officer gate busy, disconnect) gives "no result read"; a First Officer window closed mid-test speaks nothing (Task 7 `No_press_seen_is_no_result`, Task 8 source pin on `StillAttached`).
- Gravity gear picked again mid-crank, or with an unread crank, moves nothing (Task 5 `A_second_request_while_turning_is_refused`, `An_unread_crank_moves_nothing`).
- A partly turned crank is spoken "1.5", never "1,5", on a comma-decimal Windows (Task 5 `Partial_read_back_uses_a_point_under_sv_SE`).
- A knob step after an app restart or a virtual-cockpit turn must move one step the asked way, never by the difference (Task 11 `Counters_step_from_the_sims_value`; owner check 1 in Task 11 Step 7).

---

### Task 1: Measure TO CONFIG in v3 (owner in sim, no code)

**Files:** none in the repo. Save the probe below outside the repo (your temp folder) as `fenix_probe.py`; run with `python -I`.

**Interfaces:** Produces the two numbers Tasks 7-8 need: `HoldMs` and `VerdictAfterMs` (filled into the Measurement record below).

```python
"""Timeline of Fenix data-service names, printed on change. Read-only unless --pulse.
python -I fenix_probe.py --seconds 15 NAME [NAME ...]
python -I fenix_probe.py --seconds 15 --pulse system.switches.S_ECAM_TO --hold 5 NAME ...  (writes: owner's go-ahead first)"""
import argparse, json, time, urllib.request
URL = "http://127.0.0.1:8083/graphql"   # never localhost (A320-40)
def post(query, variables=None):
    body = json.dumps({"query": query, "variables": variables or {}}).encode()
    req = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=5) as r:
        return json.loads(r.read())
def write(name, value):
    post("mutation ($n: String!, $v: Int!) { dataRef { writeInt(name: $n, value: $v) } }", {"n": name, "v": value})
ap = argparse.ArgumentParser()
ap.add_argument("names", nargs="+"); ap.add_argument("--seconds", type=float, default=15)
ap.add_argument("--pulse"); ap.add_argument("--hold", type=float, default=1.5)
a = ap.parse_args()
q = "query { dataRef { " + " ".join(f"r{i}: dataRef(name: {json.dumps(n)}) {{ value }}" for i, n in enumerate(a.names)) + " } }"
t0, last, pulse = time.monotonic(), None, ([(2.0, 0), (2.2, 1), (2.2 + a.hold, 0)] if a.pulse else [])
while (t := time.monotonic() - t0) < a.seconds:
    while pulse and t >= pulse[0][0]:
        write(a.pulse, pulse[0][1]); print(f"{t*1000:8.0f} ms  WROTE {a.pulse} = {pulse[0][1]}"); pulse.pop(0)
    d = (post(q).get("data") or {}).get("dataRef") or {}
    row = tuple(json.dumps((d.get(f"r{i}") or {}).get("value")) for i in range(len(a.names)))
    if row != last:
        print(f"{t*1000:8.0f} ms  " + "  ".join(f"{n.split('.')[-1]}={v}" for n, v in zip(a.names, row))); last = row
    time.sleep(0.05)
```

`NAMES` below = `system.switches.S_ECAM_TO system.indicators.I_MIP_MASTER_WARNING_CAPT system.indicators.I_MIP_MASTER_CAUTION_CAPT`.

- [x] **Step 1: Owner sets up.** Fenix loaded (any variant), on the ground, both engines running, parking brake set, the app running a build of `main` (unchanged TO CONFIG code), Fenix aircraft profile.
- [x] **Step 2: Bad config, three runs.** Owner: flap lever to 0 (Pedestal → Flight Controls → Flap lever: Up). Implementer starts `python -I fenix_probe.py --seconds 15 NAMES`; within 3 s the owner opens Pedestal → ECAM and presses **ECAM TO CONFIG**. Owner notes what the app says (today: "Takeoff config: check configuration." is right, "Takeoff config normal." is the bug). Between runs: press Main Instrument Panel → Warnings/Messages → **Master Warning Captain** and **Master Caution Captain** if lit, wait 5 s. Record per run: ms from `S_ECAM_TO=1` to the first lit master light, which light, ms from `S_ECAM_TO=0` (release) to the light going out (or "stays lit").
- [x] **Step 3: Good config, one run.** Owner: flaps 1, pitch trim set, V-speeds entered. Same probe and press. Record any light (expected: none) and what the app said.
- [ ] **Step 4: Only if no run in Step 2 lit any light in 15 s.** With the owner's spoken go-ahead to write: flaps 0 again, `python -I fenix_probe.py --seconds 15 --pulse system.switches.S_ECAM_TO --hold 5 NAMES` (a 5 s press through the data service). Record as in Step 2.
- [x] **Step 5: Fill the record.** `D` = the largest press-to-light time over the bad runs.

  | Item | Value |
  |---|---|
  | Bad runs: press→light ms (light) | 1015 / 1009 / 809 (MASTER WARNING, captain and FO; a lit warning reads 2, not 1) |
  | Light still lit after release? | yes: the warning goes out about 1 s after release (+1017, +974, +905 ms); MASTER CAUTION follows (about 0.5 s after release) and stays lit until reset |
  | Good run: any light? | none (flaps 1, V1 130 / VR 135 / V2 135, trim set; watched 27 s after the press) |
  | What the `main` build said (bad / good) | "Takeoff config normal." in all three / "Takeoff config normal." |
  | `VerdictAfterMs` = D + 1000, rounded up to 100 | 2100 |
  | `HoldMs` = 1500 if the light stays lit after release, else `VerdictAfterMs` | 1500 |

  Measured 2026-10-21 on Fenix 3.0.0.7426 (A320, engines running, parking brake set), with the app built from `FenixV3` 4e8dcd777 (TO CONFIG code unchanged from `main`). The probe polled every 150-200 ms, so times are detection to detection. Decision (Step 6): bad runs light a master light within 10 s and the good run lights nothing, so Tasks 7-8 use 1500 / 2100.

- [x] **Step 6: Decide.** Bad runs light a master light within 10 s and the good run lights nothing → continue (Tasks 7-8 use the two numbers). No light in any bad run (Steps 2 and 4) → STOP, ask owner question (a). A light on the good config → STOP, report to the owner. Tasks 2-6 do not depend on this and can proceed meanwhile.

### Task 2: v3 renames and the dead FenixQuartz definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (1522-1530 `I_OH_ELEC_COMMERCIAL_U`; 5714-5722 `B_ELEC_BUS_POWER_DC_SERIVICE`; 5844 header comment; 5872-5975 the 13 `FNX2PLD_*` from `fcuSpd` to `clockEt`; 6370-6378 `FNX2PLD_isisBaro` and its "ISIS (1 variables)" header; 6717-6725 `I_OH_DOOR_VIDEO`)
- Modify: `MSFSBlindAssist/Settings/SettingsManager.cs:240` and the words "and the FenixQuartz chrono/ET counters" in its doc comment (226-227)
- Modify: `tests/MSFSBlindAssist.Tests/SettingsSeedTests.cs:214`
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixNamesTests.cs`

**Interfaces:** Produces the keys `B_ELEC_BUS_POWER_DC_SERVICE` and `I_OH_COCKPIT_DOOR_VIDEO` (used by plan 12's mute carry).

- [x] **Step 1: Write the failing tests.** In `FenixV3FixNamesTests` (`Vars = new FenixA320Definition().GetVariables()`):
  - `V3_name_is_registered_under_its_own_key_and_announced` `[Theory]` over `B_ELEC_BUS_POWER_DC_SERVICE`, `I_OH_COCKPIT_DOOR_VIDEO`, `I_OH_ELEC_COMMERCIAL_L`: `Assert.Equal(name, Vars[name].Name)`, `Continuous`, `IsAnnounced`.
  - `No_variable_reads_a_name_v3_removed` `[Theory]` over `B_ELEC_BUS_POWER_DC_SERIVICE`, `I_OH_DOOR_VIDEO`, `I_OH_ELEC_COMMERCIAL_U`: `Assert.DoesNotContain(Vars, kv => kv.Key == gone || kv.Value.Name == gone)`.
  - `Only_the_takeoff_speed_FenixQuartz_names_remain`: the ordinal-sorted `Name`s starting `FNX2PLD_` equal `{ "FNX2PLD_speedV1", "FNX2PLD_speedV2", "FNX2PLD_speedVR" }` (comment: plan 05 moves the First Officer off them, plan 12 deletes them).
  - In `SettingsSeedTests`: `Fenix_DefaultSilentVars_AreAllRegisteredFenixVariables`: every entry of `ExpectedDefaultSilentVars` is a key of `new FenixA320Definition().GetVariables()`.
- [x] **Step 2: Run them.** `--filter "FenixV3FixNamesTests|SettingsSeedTests"`. Expected: every DC SERVICE, door-video, `COMMERCIAL_U` and FenixQuartz case fails; the `COMMERCIAL_L` case and the seed fact pass for now.
- [x] **Step 3: Implement.** Rename key and `Name` of the DC SERVICE and door-video entries (display names unchanged). Delete `I_OH_ELEC_COMMERCIAL_U`; leave `_L` as it is. Delete the 13 `FNX2PLD_*` entries and `FNX2PLD_isisBaro`; above the three speed entries put one comment: FenixQuartz (discontinued, never installed here), read only by the First Officer until plan 05, plan 12 deletes them. Fix the "(20 variables)" header count. Confirm with `rg -n FNX2PLD MSFSBlindAssist tests` that only the three speed definitions, the First Officer (plan 05's files) and the `MainForm.Hotkeys.cs:524` comment remain.
- [x] **Step 4: Seed.** Run the seed fact: it now fails on `FNX2PLD_clockChr`. Delete line 240 of `SettingsManager.cs` and the matching line 214 of `ExpectedDefaultSilentVars` (now 7 entries); drop the FenixQuartz words from the doc comment. Existing users keep the two stale strings in their list: harmless, nothing matches them.
- [x] **Step 6: Run the whole suite.** Expected: all green.
- [x] **Step 7: Commit.** `fix(fenix): v3 renames (DC SERVICE, door video), drop COMMERCIAL upper and 13 dead FenixQuartz vars`

### Task 3: Generator 1 writes GEN 1

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (1641-1648 `["S_OH_ELEC_GEN1"]`, its `Name` and the "Uses GEN1_LINE internally" comment; handler 9425-9429)
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixGeneratorTests.cs`

**Interfaces:** Consumes `FoFbwUnclaimedEventKeyTests.MethodBody(string sourcePath, string methodName)` (internal, block bodies only) and `ClaudeContextBudgetTests.RepoRoot()`.

- [x] **Step 1: Write the failing tests.**
  - `Generator_1_reads_its_own_switch`: `Assert.Equal("S_OH_ELEC_GEN1", Vars["S_OH_ELEC_GEN1"].Name)`; `Assert.Equal("Generator 1", Vars["S_OH_ELEC_GEN1"].DisplayName)`.
  - `No_two_plain_panel_writes_share_an_lvar`: on `MethodBody(<repo>/MSFSBlindAssist/Aircraft/FenixA320Definition.cs, "HandleUIVariableSet")`, collect `if \(varKey == "([^"]+)"\)\s*\{\s*simConnect\.SetLVar\("([^"]+)", value\);` pairs, group keys by written name; every group has one key, except the allow-listed `A_FC_THROTTLE_LEFT_INPUT` → {`A_FC_THROTTLE_LEFT_INPUT`, `A_FC_THROTTLE_BOTH_INPUT`} (reason: "Both" moves both levers). Today it fails with `S_OH_ELEC_GEN1_LINE` written by `S_OH_ELEC_GEN1` and `S_OH_ELEC_GEN1_LINE`.
  - `Plain_panel_writes_match_their_variable_name`: for the same pairs, `Vars[key].Name == written`, except `A_FC_THROTTLE_BOTH_INPUT`. Passes today; it fails if someone fixes the write and not the `Name`.
- [x] **Step 2: Run.** `--filter FenixV3FixGeneratorTests`. Expected: 2 failures.
- [x] **Step 3: Implement.** `Name = "S_OH_ELEC_GEN1"`, drop the comment; handler `simConnect.SetLVar("S_OH_ELEC_GEN1", value);`. Checked, no change: Generator 2, APU Generator (`S_OH_ELEC_APU_GENERATOR`), Emergency Gen 1 Line, and every other ELEC switch match the package's names; the First Officer never writes a generator; the GEN 1 lights `I_OH_ELEC_GEN1_U/_L` are already defined (1468-1485).
- [x] **Step 4: Run the suite.** All green.
- [x] **Step 5: Commit.** `fix(fenix): Generator 1 operates GEN 1, not the EMER ELEC GEN 1 LINE button`

### Task 4: Flight Control Computers panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (`GetPanelStructure` Overhead list 8509-8531: insert `"Flight Control Computers"` between `"GPWS"` 8528 and `"Engine"` 8529; `BuildPanelControls` 8791: the first `["Flight Controls"]` becomes `["Flight Control Computers"]`)
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixPanelTests.cs`

The first list's last element `"S_OH_FLT_CTL_FAC_2"` has no trailing comma; keep it so. (The DCDU Left list's `"S_DCDU1_PGDN",` before `// Print (1 button)` has its comma: checked, no defect.)

- [x] **Step 1: Write the failing tests** (`def = new FenixA320Definition()`):
  - `Overhead_computers_have_their_own_panel`: `def.GetPanelControls()["Flight Control Computers"]` equals `S_OH_FLT_CTL_ELAC_1, _ELAC_2, _SEC_1, _SEC_2, _SEC_3, _FAC_1, _FAC_2` in that order.
  - `Overhead_lists_the_computers_after_GPWS`: in `def.GetPanelStructure()["Overhead"]`, index of `"Flight Control Computers"` == index of `"GPWS"` + 1.
  - `Pedestal_flight_controls_are_unchanged`: `GetPanelControls()["Flight Controls"]` has 14 entries, first `S_MIP_PARKING_BRAKE`, last `S_FC_FO_INST_DISCONNECT`.
  - `Every_panel_is_in_a_section`: every key of `GetPanelControls()` is in some `GetPanelStructure()` list.
  - `BuildPanelControls_names_each_panel_once` (source scan): in the definition source between `BuildPanelControls()` and `AddVariantPanelControls(panels)` (plan 01's seam), the names captured by `\["([^"]+)"\]\s*=\s*new List<string>` are distinct. A C# indexer initializer keeps only the last of two equal keys, which is how the overhead list vanished.
- [x] **Step 2: Run.** `--filter FenixV3FixPanelTests`. Expected: the first two and the source scan fail.
- [x] **Step 3: Implement** the two edits above; keep the comment `// Flight Control Computers (7 controls)`. The controls are latched pushbuttons in the package and read 1 live, so the existing Off/On combos and `SetLVar` handlers (10458-10506) are right.
- [x] **Step 4: Run the suite: all green.**
- [x] **Step 5: Commit.** `fix(fenix): overhead ELAC/SEC/FAC get their own Flight Control Computers panel`

### Task 5: Gravity gear, one sequenced control

**Files:**
- Create: `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs`
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (4947-4948 the two defs; 8936 "Cockpit Other"; 9382-9383 the two case labels; a new branch in `HandleUIVariableSet` right after that switch)
- Modify: `tests/MSFSBlindAssist.Tests/FenixV3FixNamesTests.cs` (add `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION` to `No_variable_reads_a_name_v3_removed`)
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixGravityGearTests.cs`

**Interfaces (produces):**
- `public static class FenixA320GravityGear`: `const string CrankKey = "S_HYD_GRAVITY_GEAR_EXTEND"`, `const string HandleKey = "S_HYD_GRAVITY_GEAR_HANDLE"`, `const double FullTurns = 3.0`, `const double StepTurns = 0.05`, `const int StepIntervalMs = 100` (the template's 10 Hz repeat), `const int HandleSettleMs = 300`, `const int ReadTimeoutMs = 1000`;
  `static IReadOnlyList<double> CrankSteps(double from, double to)`: grid values after `from` (rounded to the 0.05 grid) up to and including `to`, each `Math.Round(i * StepTurns, 2)`, clamped to [0, 3];
  `static double DescriptionKey(double crank)`: ≤ 0.025 → 0, ≥ 2.975 → 3, else `crank` (no description);
  `static string Describe(double? crank)`: null/NaN → `"Gravity gear: no read-back."`; ≥ 2.975 → `"Gravity gear: cranked 3 turns."`; ≤ 0.025 → `"Gravity gear: crank stowed."`; else `"Gravity gear: crank at {crank:0.0} turns."` (invariant).
- `public sealed class FenixA320GravityGearWalker(Action<string, double> write, Func<string, Task<double?>> readFresh, Func<TimeSpan, Task> delay)`: `bool IsRunning`; `Task<string> MoveAsync(bool extend)`.
  MoveAsync: if `IsRunning` → `"Gravity gear: crank already turning."`, no writes. Read `CrankKey`; unread → `"Gravity gear: no read-back, nothing moved."`, no writes. Extend: write `HandleKey` 1, delay `HandleSettleMs`, then each `CrankSteps(current, 3)` value to `CrankKey` with `StepIntervalMs` between. Stow: each `CrankSteps(current, 0)`, then write `HandleKey` 0 (the model lets the handle move only at crank 0). Finally read `CrankKey` again and return `Describe(it)`. No `ConfigureAwait(false)`: on the UI thread the writes stay there.

- [x] **Step 1: Write the failing tests** (fakes: `write` appends `(key, value)`; `readFresh` returns scripted values; `delay` returns `Task.CompletedTask`):
  - `CrankSteps_cover_three_turns_on_the_grid`: `CrankSteps(0, 3)` has 60 values, first 0.05, last 3.0; `CrankSteps(3, 0)` has 60, first 2.95, last 0.0; `CrankSteps(1.5, 3)` has 30, first 1.55; `CrankSteps(3, 3)` is empty.
  - `DescriptionKey_maps_only_the_ends` `[Theory]`: 0→0, 0.02→0, 3→3, 2.98→3, 1.5→1.5.
  - `Extend_from_stowed_raises_the_handle_then_cranks_to_3`: reads 0 then 3; writes[0] == (`HandleKey`, 1), then 60 `CrankKey` writes rising to 3.0; result `"Gravity gear: cranked 3 turns."`.
  - `Stow_from_extended_cranks_back_then_stows_the_handle`: reads 3 then 0; 60 crank writes falling to 0.0, last write (`HandleKey`, 0); result `"Gravity gear: crank stowed."`.
  - `Extend_from_a_partial_crank_continues_from_there`: reads 1.5 then 3; first crank write 1.55, 30 crank writes.
  - `An_unread_crank_moves_nothing`: read null → result `"Gravity gear: no read-back, nothing moved."`, no writes.
  - `A_second_request_while_turning_is_refused`: a `delay` that waits on a `TaskCompletionSource`; start `MoveAsync(true)`, then `MoveAsync(false)` returns `"Gravity gear: crank already turning."` and adds no write; complete the source, the first finishes.
  - `Partial_read_back_uses_a_point_under_sv_SE`: under `CultureInfo("sv-SE")`, `Describe(1.5)` == `"Gravity gear: crank at 1.5 turns."`.
  - `The_extension_control_is_stowed_or_extended` (definition): `Vars["S_HYD_GRAVITY_GEAR_EXTEND"]` has `ValueDescriptions` exactly {0: "Stowed", 3: "Extended"}, `DescriptionKeyFor(2.99)` == 3, `DescriptionKeyFor(1.5)` == 1.5; `GetPanelControls()["Cockpit Other"]` contains it and not `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION`.
- [x] **Step 2: Run.** `--filter "FenixV3FixGravityGearTests|FenixV3FixNamesTests"`. Expected: compile failure (new types).
- [x] **Step 3: Implement** `FenixA320GravityGear.cs` per Interfaces, with a class comment citing the template and the live `""` for the handle.
- [x] **Step 4: Wire the definition.** Def 4947: `ValueDescriptions = {[0] = "Stowed", [3] = "Extended"}`, `ValueToDescriptionKey = FenixA320GravityGear.DescriptionKey` (OnRequest, display name unchanged). Delete def 4948, its "Cockpit Other" entry and both case labels 9382-9383. New branch: `if (varKey == FenixA320GravityGear.CrankKey) { _ = MoveGravityGearAsync(value >= FenixA320GravityGear.FullTurns / 2, simConnect, announcer); return true; }`. `private async Task MoveGravityGearAsync(bool extend, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer? announcer)` keeps one `FenixA320GravityGearWalker` per definition (field `_gravityGearWalker`), whose delegates call the manager from the latest call (field `_gravityGearSim`): `SetLVar(k, v)`, `ReadFreshAsync(k, FenixA320GravityGear.ReadTimeoutMs)`, `Task.Delay`. Speak the result: refusals and "no read-back" with `AnnounceImmediate`, the completion read-back with `Announce` (it lands about 6 s after the pick). Wrap in try/catch → `Log.Debug("Fenix", …)`.
- [x] **Step 5: Run the suite: all green.**
- [x] **Step 6: Commit.** `fix(fenix): gravity gear is one control that raises the handle and cranks three turns`
- [ ] **Step 7: Owner in sim (ground only: owner answer 2, no flight test).** Fenix on the ground, parked. Cockpit → Cockpit Other → **Gravity Gear Extension** → Extended. Expected after about 6 s: *"Gravity gear: cranked 3 turns."*, and a read-only data-service read of `system.switches.S_HYD_GRAVITY_GEAR_EXTEND` gives 3. Then choose Stowed: about 6 s later *"Gravity gear: crank stowed."* and the read gives 0. If the read does not reach 3: replace the per-step `SetLVar` with the template's own step through the calculator, `simConnect.ExecuteCalculatorCodeUnique("3 (L:S_HYD_GRAVITY_GEAR_EXTEND) 0.05 + min (>L:S_HYD_GRAVITY_GEAR_EXTEND)")` (stow: `"0 (L:S_HYD_GRAVITY_GEAR_EXTEND) 0.05 - max (>L:S_HYD_GRAVITY_GEAR_EXTEND)"`), and retest once; if that fails too, report to the owner. Whether 3 turns actually drops the gear in flight stays unproven: list it as a follow-up in the handoff.

### Task 6: AUTOLAND lights (measure, then follow)

**Files:** Modify `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (782-799, only the two `Name` values, if Step 2 says so); modify `tests/MSFSBlindAssist.Tests/FenixV3FixNamesTests.cs`.

- [x] **Step 1: Owner in sim (ground, batteries on).** Implementer runs `python -I fenix_probe.py --seconds 20` with the six names `system.indicators.I_MIP_AUTOLAND_CAPT`, `_CAPT_U`, `_CAPT_L`, `I_MIP_AUTOLAND_FO`, `_FO_U`, `_FO_L` (full names). Owner: Overhead → Interior Lights → **Annunciator** → Test, wait 5 s, back to Bright. Record which names went to 1.
- [x] **Step 2: Decide.** The unsplit names lit → no code change; note it here and skip to Step 5. Only split names lit → the lit name is the new `Name` (prefer `_U` when both `_U` and `_L` lit); keys `I_MIP_AUTOLAND_CAPT` / `I_MIP_AUTOLAND_FO` stay, so `S_MIP_AUTOLAND_*`'s `StateVariable` and Ctrl+M mutes keep working. Nothing lit → no change; add "AUTOLAND during a real autoland warning" to the handoff in-sim list and skip to Step 5.
- [ ] **Step 3: Test first.** `Autoland_lights_read_the_v3_name` `[Theory]` (`I_MIP_AUTOLAND_CAPT` → measured name, `I_MIP_AUTOLAND_FO` → measured name): `Vars[key].Name`; and `Vars["S_MIP_AUTOLAND_CAPT"].StateVariable == "I_MIP_AUTOLAND_CAPT"`. Run: fails.
- [ ] **Step 4: Change the two `Name`s** (display names unchanged). Run the suite: green. Commit `fix(fenix): AUTOLAND lights read the v3 split legend`.
- [ ] **Step 5: Owner check** (only after a change): Annunciator → Test. Expected among the lights: *"Glareshield Autoland Captain: On"* and *"Glareshield Autoland FO: On"*.

**Record (2026-10-21, annunciator test, data-service probe):** all six names lit together for about 2.7 s (`I_MIP_AUTOLAND_CAPT`, `_CAPT_U`, `_CAPT_L`, `I_MIP_AUTOLAND_FO`, `_FO_U`, `_FO_L`; the FO pair about 110 ms before the captain's). The unsplit names light on v3, so Step 2 decided: no code change; Steps 3-5 do not apply. Whether a real autoland warning also lights the unsplit names stays an in-sim item (handoff).

### Task 7: TO CONFIG verdict over a time window (pure)

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs` (whole file, 26 lines)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixTakeoffConfigVerdictTests.cs` (the existing seven cases stay)

**Interfaces (produces):**
- `public readonly record struct TakeoffConfigSample(TimeSpan SincePress, double? Warning, double? Caution);`
- In `FenixTakeoffConfigVerdict`: keep `MasterWarningKey`, `MasterCautionKey`, `Text(double?, double?)`. Add `const string ButtonKey = "S_ECAM_TO"`, `const string ButtonFreshKey = "S_ECAM_TO_READOUT"`, `const string MasterWarningFreshKey = "I_MIP_MASTER_WARNING_CAPT_READOUT"`, `const string MasterCautionFreshKey = "I_MIP_MASTER_CAUTION_CAPT_READOUT"`, `static readonly IReadOnlyList<string> FreshKeys` (those three), `const int HoldMs` and `const int VerdictAfterMs` (Task 1's record; until it is filled, 1500 and 2500 with a `// TODO(Task 1)` that Task 8 Step 1 removes), `const int PollMs = 200`, `const int ReadTimeoutMs = 500`, `static readonly TimeSpan PanelPressWait = 3 s`, `FirstOfficerPressWait = 30 s`.
- `public static string Decide(IReadOnlyList<TakeoffConfigSample> samples, TimeSpan verdictAfter)`: any warning ≥ 0.5 → "check configuration"; else any caution ≥ 0.5 → "caution"; else any sample with `SincePress >= verdictAfter` and both lights read (not null, not NaN) and dark → "Takeoff config normal."; else "no result read". The four strings are the existing `Text` strings.
- `public static Task<string> WatchAsync(Func<string, Task<double?>> readFresh, TimeSpan pressWait)` (Stopwatch + `Task.Delay`, `VerdictAfterMs`) over `internal static Task<string> WatchAsync(Func<string, Task<double?>> readFresh, TimeSpan pressWait, TimeSpan verdictAfter, Func<TimeSpan> clock, Func<TimeSpan, Task> delay)`.

The watch, because its tests leave choices open:

```
edge: poll ButtonFreshKey every PollMs until pressWait; t0 = clock() taken BEFORE issuing the first read
      that returns >= 0.5 after an earlier read returned < 0.5. No edge → "no result read".
then: loop { t = clock() before the reads; w = read(MasterWarningFreshKey); c = read(MasterCautionFreshKey);
             add (t - t0, w, c);
             w >= 0.5 → return Decide (early: a warning outranks everything);
             t - t0 >= verdictAfter and w, c read → return Decide;
             clock() - t0 >= verdictAfter + 4 × PollMs → return Decide;
             delay(PollMs) }
```

A caution does not end the watch early: a later warning outranks it. Times are taken before the read, so a sample is never counted as later than it was.

- [x] **Step 1: Write the failing tests** (a fake clock that `delay` advances; a scripted reader per key keyed on the fake time; `verdictAfter` = 2.5 s, `pressWait` = 3 s):
  - `Decide` `[Theory]`: warning at 1.0 s among dark samples → check; caution only → caution; dark at 2.4 s only → no result; dark at 2.5 s → normal; (2.6 s, warning 0, caution null) → no result; empty → no result.
  - `A_warning_that_lights_late_is_still_caught`: edge at 0.4 s, warning lights at edge + 2.0 s → check.
  - `Normal_comes_only_after_the_verdict_time`: all dark → normal, and the fake clock at return ≥ edge + 2.5 s.
  - `No_press_seen_is_no_result`: button always 0 → no result, clock ≥ 3 s, no light read issued.
  - `A_button_already_held_needs_a_release_first`: button 1, 1, 0, 1 → t0 is the last 1; all dark → normal no earlier than that t0 + 2.5 s.
  - `A_warning_lit_before_the_press_is_check`: warning 1 throughout → check.
  - `Caution_waits_for_a_late_warning`: caution at edge + 0.5 s, warning at edge + 1.5 s → check.
  - `Unread_lights_never_give_normal`: light reads return null → no result.
  - `The_hold_is_never_shorter_than_v2s`: `FenixTakeoffConfigVerdict.HoldMs >= 1500`.
- [x] **Step 2: Run.** `--filter FenixTakeoffConfigVerdictTests`. Expected: compile failure.
- [x] **Step 3: Implement** per Interfaces. Rewrite the class comment: v3's FWC delays MASTER WARN/CAUT (Fenix V3 changelog), so one cached read at 1.5 s could say "normal" over a bad config; the verdict is now a fresh-read watch from the observed press, still fail-safe (any light = check, unread = no result, never a pre-press baseline).
- [x] **Step 4: Run.** All `FenixTakeoffConfigVerdictTests` pass, the old seven included. Suite green.
- [x] **Step 5: Commit.** `fix(fenix): TO CONFIG verdict watches fresh reads until the v3 warning delay has passed`

### Task 8: TO CONFIG wiring (definition twins, panel button, First Officer)

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs` (the two constants from Task 1)
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (twin loop 8485-8500; readout `switch` in `ProcessSimVarUpdate` 12108-12131; `S_ECAM_TO` branch 10994-11004; delete `TakeoffConfigTestHoldMs` 12609-12614 and `AnnounceTakeoffConfigResult` 12741-12773; `ExecuteButtonTransition` 12653-12739 loses its `onHeld` parameter, which had no other caller: check with `rg -n "onHeld:" MSFSBlindAssist/Aircraft/FenixA320Definition.cs`)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixActionExecutor.cs` (`TakeoffConfigTest` 215-224, `AnnounceTakeoffConfigResult` 226-244, and the now-unused `TakeoffConfigTestHoldMs` about :58-63; this plan owns the whole file for the parallel phase)
- Create: `tests/MSFSBlindAssist.Tests/FenixTakeoffConfigWiringTests.cs`

**Interfaces:**
- Consumes Task 7's `WatchAsync`, `FreshKeys`, `ButtonKey`, `HoldMs`, `ReadTimeoutMs`, `PanelPressWait`, `FirstOfficerPressWait`; `SimConnectManager.ReadFreshAsync(string varKey, int timeoutMs, CancellationToken ct = default)`.
- Produces on the definition: `internal static readonly IReadOnlyList<string> TakeoffConfigReadoutVariables = FenixTakeoffConfigVerdict.FreshKeys;` and `private async Task RunTakeoffConfigTestAsync(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer? announcer)` (block body).
- First Officer: `public async Task<bool> TakeoffConfigTest()` (block body) and `private void AnnounceTakeoffConfigResult(SimConnectManager sc, string verdict)`.

- [x] **Step 1: Constants.** Set `HoldMs` and `VerdictAfterMs` from Task 1's record; delete the TODO. (Task 1 must be done and must not have stopped.)
- [x] **Step 2: Write the failing tests** in `FenixTakeoffConfigWiringTests`:
  - `Each_twin_is_a_one_off_read_of_its_original` `[Theory]` over `FenixTakeoffConfigVerdict.FreshKeys`: ends with `FenixA320Definition.ReadoutSuffix`; same `Name` and `Units` as the original key; `OnRequest`; `ExcludeFromMonitorManager`; `FreshReadPolicy.RouteRequest(def, false) == VarRequestRoute.DataDefinitionId` (as `FenixFcuReadoutTests` does).
  - `A_twin_answer_is_consumed_silently` `[Theory]` over the same keys: `def.ProcessSimVarUpdate(key, 1, speech)` is true and `speech.All` is empty (`SpeechCapture`).
  - `Panel_button_runs_the_shared_watch`: `MethodBody(definition, "RunTakeoffConfigTestAsync")` contains `FenixTakeoffConfigVerdict.WatchAsync(` and `pressHoldMs: FenixTakeoffConfigVerdict.HoldMs`; the `S_ECAM_TO` branch of `MethodBody(definition, "HandleUIVariableSet")` contains `RunTakeoffConfigTestAsync(simConnect, announcer)`; the definition source no longer contains `FenixTakeoffConfigVerdict.MasterWarningKey)`.
  - `First_Officer_runs_the_shared_watch`: `MethodBody(executor, "TakeoffConfigTest")` contains `FenixTakeoffConfigVerdict.WatchAsync(` and `PulseAsync(FenixTakeoffConfigVerdict.ButtonKey, FenixTakeoffConfigVerdict.HoldMs)`; `MethodBody(executor, "AnnounceTakeoffConfigResult")` contains `StillAttached(sc)` and not `GetCachedVariableValue`.
- [x] **Step 3: Run.** `--filter FenixTakeoffConfigWiringTests`. Expected: failures (no twins, old wiring).
- [x] **Step 4: Definition.** The twin loop iterates `FcuReadoutVariables.Concat(TakeoffConfigReadoutVariables)`. In the readout `switch` add `case FenixTakeoffConfigVerdict.ButtonFreshKey or FenixTakeoffConfigVerdict.MasterWarningFreshKey or FenixTakeoffConfigVerdict.MasterCautionFreshKey: return true;` (the fresh-read waiter already has the value; nothing is spoken). The `S_ECAM_TO` branch becomes `_ = RunTakeoffConfigTestAsync(simConnect, announcer); return true;`. `RunTakeoffConfigTestAsync`: first `ExecuteButtonTransition("S_ECAM_TO", "ECAM TO CONFIG", simConnect, announcer, pressHoldMs: FenixTakeoffConfigVerdict.HoldMs)` (it writes the 0 at once, so the watch's first read sees the release), then `await FenixTakeoffConfigVerdict.WatchAsync(k => simConnect.ReadFreshAsync(k, FenixTakeoffConfigVerdict.ReadTimeoutMs), FenixTakeoffConfigVerdict.PanelPressWait)`, then `if (simConnect.IsConnected) announcer?.AnnounceImmediate(text)`; try/catch → `Log.Debug("Fenix", …)`. Delete the old constant and method, and `onHeld`.
- [x] **Step 5: First Officer executor (this plan owns `FenixActionExecutor.cs` for the parallel phase; also delete the now-unused `TakeoffConfigTestHoldMs` and its comment, about :58-63).** `TakeoffConfigTest`: `var sc = Sc; if (sc is not { IsConnected: true }) return false;` start `var watch = FenixTakeoffConfigVerdict.WatchAsync(k => sc.ReadFreshAsync(k, FenixTakeoffConfigVerdict.ReadTimeoutMs), FenixTakeoffConfigVerdict.FirstOfficerPressWait);` (before the pulse: the gate may make the press wait, and the watch needs to see the button released first), `bool pressed = await PulseAsync(FenixTakeoffConfigVerdict.ButtonKey, FenixTakeoffConfigVerdict.HoldMs);`, `string verdict = await watch;`, `if (pressed) AnnounceTakeoffConfigResult(sc, verdict); return pressed;`. `AnnounceTakeoffConfigResult` speaks `verdict` with `AnnounceImmediate` only when `sc.IsConnected && StillAttached(sc)`; try/catch → `Log.Debug("FO", …)`. Update both doc comments (no "cached", no "1.5 s").
- [x] **Step 6: Build the solution and run the suite: all green.**
- [x] **Step 7: Commit.** `fix(fenix): panel and First Officer TO CONFIG read fresh twins through the shared watch`
- [x] **Step 8: Owner in sim (ground, engines running, parking brake set).** (1) Flaps 0, Pedestal → ECAM → **ECAM TO CONFIG**: within `VerdictAfterMs` *"Takeoff config: check configuration."*. (2) Flaps 1, trim and V-speeds set, same button: after about `VerdictAfterMs` *"Takeoff config normal."* (3) First Officer window (Output Shift+C), Taxi checklist, tick **Takeoff config test** with flaps 0: *"Takeoff config: check configuration."* A wrong verdict in any of the three: STOP and report with `debug.log`.

### Task 9: Take-off V-speed callouts on the shared machine (owner request, 2026-10-21)

Why: the Fenix is the only airframe still calling V1/rotate from `TakeoffAssistManager`, which reads `N_MISC_PERF_TO_V1/VR` ONCE when Takeoff Assist turns on (`MainForm.Hotkeys.cs:521-533`, `TakeoffAssistManager.SetFenixVSpeeds` :588-595): speeds entered or changed after that, or Takeoff Assist left off, give no V1 or rotate call and nothing says why. The FBW A320/A380, MD-11 and iFly use the shared `TakeoffVSpeedCallouts` machine (TKO-5), which follows the speeds live and calls V1, Rotate and V2 whether or not Takeoff Assist is on. Move the Fenix onto it, built from the A320's (`A320TakeoffCallouts`; `FlyByWireA320Definition.cs:3006-3019`, :7017-7036, :8120-8148); owner answer 16 (`NotSetBelow`) lands here too, on the same rows.

**Files:**
- Create: `MSFSBlindAssist/Aircraft/FenixTakeoffCallouts.cs` (owned by plan 02)
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (the IAS feed definition beside the V-speed rows about :6548-6571, `NotSetBelow = 1` and `Units = "knots"` on `N_MISC_PERF_TO_V1/VR/V2`, the `TakeoffCalloutFeedKey` / `TakeoffCalloutFeedNeeded` / `IsMuteWrapExempt` overrides, the first branch of `ProcessSimVarUpdate`, the arm drop in `ResetAnnouncementBaselines` and `OnSimContextReset`)
- Modify: `MSFSBlindAssist/Services/TakeoffAssistManager.cs` (delete the Fenix-only fields :78-81, the V1/rotate blocks :560-580 and `SetFenixVSpeeds`/`ClearFenixVSpeeds` :583-605; the 80/100 knots calls stay) and `MSFSBlindAssist/MainForm.Hotkeys.cs` (the Fenix block :521-533 and `ClearFenixVSpeeds()` :551-552; plan 01's own lines in this file are already in, so this is sequential, not an overlap)
- Test: `tests/MSFSBlindAssist.Tests/FenixTakeoffCalloutsTests.cs`

**Interfaces:**
- Consumes: `TakeoffVSpeedCallouts` (`ProcessSample`, `NeedsSamples`, `Compose`), `TakeoffCalloutKeys` (`IsMuted`, `IsVSpeedKey`, `Feed`), `UserSettings.FenixDisabledMonitorVariablesSet`.
- Produces: `public static class FenixTakeoffCallouts` with `public const string IasKey = "FENIX_TAKEOFF_CALLOUT_IAS";`, `V1Key = "N_MISC_PERF_TO_V1"`, `VrKey = "N_MISC_PERF_TO_VR"`, `V2Key = "N_MISC_PERF_TO_V2"`, `public static readonly TakeoffCalloutKeys Keys`.

- [x] **Step 1: Check the Fenix plays no V-speed call of its own.** Search `<MSFS Community>\fnx-aircraft-320\SimObjects\Airplanes\FNX_32X\attachments\fnx\Part_Interior_Cockpit_CEO\sound\sound.xml` (and the common cockpit sound file) for a V1/rotate event. If the Fenix calls V1 itself, stop and ask the owner whether the app should still call it (the A380 note in TKO-5 is the precedent).
- [x] **Step 2: Write the failing tests** (pattern of `A320TakeoffCalloutsTests.cs`, under de-DE): `Keys_name_the_fenix_speed_rows` (feed key and the three `N_MISC_PERF_TO_*` keys); `Definition_registers_the_feed` (`AIRSPEED INDICATED`, knots, `Continuous`, `IsAnnounced`, `ExcludeFromBatch`, `HighFrequency`, `ExcludeFromMonitorManager`; and `TakeoffCalloutFeedKey == FenixTakeoffCallouts.IasKey`); `Speed_rows_read_not_set_below_1` (`NotSetBelow == 1` on all three); `Takeoff_assist_has_no_fenix_vspeed_code` (source scan: `TakeoffAssistManager.cs` and `MainForm.Hotkeys.cs` no longer mention `FenixVSpeeds` or `N_MISC_PERF_TO_V1`).
- [x] **Step 3: Run them; they fail.**
- [x] **Step 4: Implement** exactly as the A320 does, with the Fenix's keys and its mute set `FenixDisabledMonitorVariablesSet`: the feed branch first in `ProcessSimVarUpdate` (one `AnnounceImmediate` of `Compose`, skipped while `announcer.Suppressed`), `SIM_ON_GROUND` and the three speed keys PEEKED (fed to the machine, never consumed, so "MCDU V1 Speed: 140" is still spoken as it is entered), the arm dropped in both resets, never the speeds. Then delete the old Fenix path from `TakeoffAssistManager` and `MainForm.Hotkeys`.
- [x] **Step 5: Run the suite, including `TakeoffVSpeedCalloutsTests`, `TakeoffCalloutFeedTests` and `TakeoffCalloutKeysTests`; all green. Build.**
- [x] **Step 6: Commit.** `feat(fenix): take-off V1, Rotate and V2 on the shared callout machine (TKO-5)`
- [ ] **Step 7: Owner in sim (with plan 05's takeoff-speeds check).** PERF TAKE OFF: enter V1 140, VR 142, V2 145; hear "MCDU V1 Speed: 140" etc. Take off with Takeoff Assist OFF: hear "V1", "Rotate", "V2" at those speeds (one utterance when two coincide). Clear a speed: hear "not set", not "0".

### Task 10: FCU vertical speed is silent while the window is dashed (collaborator report, 2026-10-21)

Why: in v3, `N_FCU_VS` carries the LIVE vertical speed even while the FCU V/S window is dashed (`B_FCU_VERTICALSPEED_DASHED` = 1, managed climb or descent); v2 held it still. `N_FCU_VS` is a plain `Continuous` + `IsAnnounced` row (FenixA320Definition.cs:655-662) with nothing gating it, so a managed climb speaks "FCU VERTICAL SPEED: 3400 … 1700 … 4100" every second or two. A collaborator's v3 test flight: dashed from 13:40, yet `N_FCU_VS` went 3400 → 1700 → 4100 → 3400 between 14:09 and 14:12. Speed, heading and altitude are unaffected; the Shift+V readout already checks the dashed flag (:12122-12125).

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (`ProcessSimVarUpdate` from :12100; a private `_fcuVsDashed` field). Never override `TryGetDisplayOverride` here: plan 01's hooks file owns that override (plan 06's clock readouts use it), and a second one would not compile. The Fenix panel display list (`GetPanelDisplayVariables`, :9329) was empty on 2026-10-21.
- Test: `tests/MSFSBlindAssist.Tests/FenixV3FixFcuVsTests.cs`

**Interfaces:**
- Produces: `private double? _fcuVsDashed` (last `B_FCU_VERTICALSPEED_DASHED` seen; null until read) and `internal static bool FcuVsIsSilenced(double? dashed) => dashed == 1;`.

- [x] **Step 1: Check the whole of what you touch.** Find every place the Fenix shows or speaks `N_FCU_VS`: the panel display list (`GetPanelDisplayVariables`, :9329), the FCU/autopilot windows (`Forms/FenixA320/`), the V/S set path (`SetFCUVS`, :13103, which reads the cached `N_FCU_VS` as the starting point: a live value there while dashed changes the step count; note what it does today). Record the list in this task.
- [x] **Step 2: Write the failing tests** (constructing the definition and announcer as `FenixFcuReadoutTests.cs` does): `Vs_is_silent_while_dashed`: after `ProcessSimVarUpdate("B_FCU_VERTICALSPEED_DASHED", 1, a)`, `ProcessSimVarUpdate("N_FCU_VS", 3400, a)` returns true (consumed) and nothing is spoken; `Vs_speaks_when_shown`: after the flag reads 0, `N_FCU_VS` 1500 is not consumed (returns false, so the generic announcement speaks it); `Vs_speaks_before_the_flag_is_read`: with no flag read yet, `N_FCU_VS` is not consumed; `Dashed_flag_still_announces`: `ProcessSimVarUpdate("B_FCU_VERTICALSPEED_DASHED", 1, a)` returns false (peeked, never consumed, so "FCU VERTICAL SPEED DISPLAY: Dashed" is still heard); `FcuVsIsSilenced` is true only for exactly 1 (`null`, 0, `double.NaN` → false).
- [x] **Step 3: Run them; they fail.**
- [x] **Step 4: Implement.** In `ProcessSimVarUpdate`, after the readout `switch` (:12131): `if (varName == "B_FCU_VERTICALSPEED_DASHED") _fcuVsDashed = value;` (no return); `if (varName == "N_FCU_VS" && FcuVsIsSilenced(_fcuVsDashed)) return true;`. Reset `_fcuVsDashed` to null where the definition resets its announcement baselines (reconnect, `OnSimContextReset`), so a stale "dashed" never silences a fresh flight. If Step 1 finds the V/S on a panel display after all, do not change the display here: hand it to plan 12 (it should show "Dashed" while `FcuVsIsSilenced`, through plan 01's display hook). If `SetFCUVS` starts from a live value while dashed, note it in the handoff for the owner (do not change the set path here).
- [x] **Step 5: Run the suite; all green.**
- [x] **Step 6: Commit.** `fix(fenix): FCU vertical speed is silent while its window is dashed (v3 sends the live V/S)`
- [ ] **Step 7: Owner or collaborator in sim.** A managed climb with the V/S window dashed: no "FCU VERTICAL SPEED" callouts for a minute. Pull the V/S knob: hear "FCU VERTICAL SPEED DISPLAY: Shown"; turn it: each new target is spoken. Push it: "…: Dashed", then silence again. Note the race: the first selected value at a pull may arrive just before the "Shown" flag and stay unspoken (the next turn speaks); acceptable, say so in the handoff.

### Task 11: Panel controls that move differently from the cockpit (panel check and live measurements, 2026-10-21)

Why: `docs/fenix-v3/evidence/panel-sweep.md` compared all 443 panel keys with the v3 cockpit model; the uncertain ones were then measured live (A320-214 CFM on the ground, engines off, ground power on; every value restored), then measured again on a reloaded flight through a direct SimConnect connection (not the MCP) with the same results. Most of these predate v3.

**Measured live on 2026-10-21:**
- **Momentary buttons register a press-release pulse.** The ECAM DOOR page button `S_ECAM_DOOR` (model `Button_Momentary`, a counter: the cockpit adds 1 on press and 1 on release) pulsed 0, 1, 0 with holds of 200, 100 and 50 ms flipped `I_ECAM_DOOR` each time, in pairs; a 20 ms hold (about one frame) was missed. The bus tie `S_OH_ELEC_BUSTIE` behaved the same (`I_OH_ELEC_BUSTIE_L`), but toggling it disturbs ground power: never test with it. The app's 200 ms (`ButtonPressHoldMs`, A320-3) is safe on v3. A held 1, which today's "On" combos write, flips the function once whatever its state; 0 ("Off") does nothing.
- **Two lights never answer.** `S_OH_LIGHTING_AVIONICS_COMPT` and `S_OH_SVCE_INT_OVRD` took every press (the owner heard the clicks) but `I_OH_LIGHTING_AVIONICS_COMPT` and `I_OH_SVCE_INT_OVRD` stayed 0 on the ground with ground power, for the app's pulse and for the cockpit's own count alike: the Fenix does not light them here, so they are not used as these buttons' state.
- **Baro STD is a push/pull counter** (`Knob_PushPullButton`, button `S_FCU_EFIS1_BARO_STD`; HubHop: pull `++`, push `--`). Any increase selects STD, any decrease QNH; extra pulls stay STD and extra pushes stay QNH. The state is `I_FCU_EFIS1_QNH` (1 QNH, 0 STD; already registered and announced as "EFIS1 BARO STD Status"). With the counter at 2 after two cockpit pulls, the app's "STD" (a straight write of 1, a decrease) selected QNH.
- **FCU knob counters step by difference.** `E_FCU_SPEED` at 3 after three cockpit clicks (speed 100 → 103); the app's first "increase" writes its own count, 1: speed 103 → 101, down 2. The same happens after any app restart mid-flight: the app's count (`rmpCounters`) restarts at 0, the sim's does not.
- **Manual pressurization labels are reversed.** In MAN, `S_OH_PNEUMATIC_PRESS_MAN` 0 closed the outflow valve (`A320_Cabin_Pressure_Outflow_Anim` 100 → 91) and 2 opened it (91 → 95): 0 = DOWN, 2 = UP (v3's tooltips agree); ours say 0 Up, 2 Down.
- **Annunciator light labels are right:** 0 dims (`N_BRIGHTNESS_ANNUNCIATORS` 0.351), 1 bright (1), 2 test (master warning lit). v3's tooltip text (0 BRT, 1 DIM) is wrong; leave ours.
- **Thrust-lever reverse not measured:** with the engines off neither `GENERAL ENG THROTTLE LEVER POSITION:1` nor `A_FC_THROTTLE_LEFT` moved for `A_FC_THROTTLE_LEFT_INPUT` 0, 1, 2 (Step 1 measures it with the engines at idle).

**Decisions (my calls):**
1. **Momentary buttons become press buttons** (`RenderAsButton = true`, no `ValueDescriptions`, handler `ExecuteButtonTransition` on `value == 1`, `StateVariable` = their light where one is registered, as External Power does at :1657-1665), labels unchanged: `S_OH_ELEC_BUS_TIE` (writes `S_OH_ELEC_BUSTIE`; state `I_OH_ELEC_BUSTIE_L`), `S_OH_SVCE_INT_OVRD` and `S_OH_LIGHTING_AVIONICS_COMPT` (no state: their lights never lit), `S_PED_COCKPIT_DOOR_VIDEO` (Task 2's `I_OH_COCKPIT_DOOR_VIDEO`), `S_WR_GCS` (no light registered), `S_ASP_PA_SEND` (`I_ASP_PA_SEND`; today it writes `value` and never releases), and CALLS `S_OH_CALLS_MECH/ALL/FWD/AFT` (owner answer 9, formerly plan 06's handoff H3; not `S_OH_CALLS_EMER` or its cover).
2. **Every knob and push/pull counter steps from the sim's own value**: `internal static string CounterStepCode(string lvar, int steps)` gives `(L:<lvar>) <|steps|> + (>L:<lvar>)` or `… - …`, sent with `ExecuteCalculatorCodeUnique` (SIM-17) as `AdjustFcuPushPullCounter` already does. `IncrementCounter`, `DecrementCounter` and `JumpCounter` use it (so the typed-value jump in `SetFCUValueWithVerification` too) for `E_FCU_SPEED/HEADING/ALTITUDE/VS`, `E_FCU_EFIS1/2_BARO`, `E_MIP_ISFD_BARO` and `S_FCU_VERTICAL_SPEED`; `rmpCounters` goes.
3. **Baro STD becomes a pull and a push**, like the FCU knobs: the EFIS panels list `S_FCU_EFIS1_BARO_PULL` "EFIS Left Baro Pull (STD)" and `S_FCU_EFIS1_BARO_PUSH` "EFIS Left Baro Push (QNH)" (and the EFIS2 pair, "Right") in place of the `S_FCU_EFIS1/2_BARO_STD` combos: `RenderAsButton`, `OnRequest`, `Name` `S_FCU_EFISn_BARO_STD`, handler a `CounterStepCode` step of +1 or -1. The result is heard from the existing `I_FCU_EFISn_QNH` announcement. The First Officer's `SetBaroStdBoth(std)` steps each side +1 (STD) or -1 (QNH) the same way: idempotent, so it needs no prior read. The `S_FCU_EFISn_BARO_STD` definitions stay (plan 05 reads them until its Task 7).
4. **Spring-loaded three-position switches return to the middle** (model `Switch_3Position_Hold`; a written end stays held): picking an end writes it, then 1 after `ButtonPressHoldMs` (`PulseSpringSwitch`, a one-shot WinForms timer as in `ExecuteButtonTransition`, only while connected). Cargo Discharge 1 and 2 (`S_OH_CARGO_DISC_n_OLD_LAYOUT`): values from the model's tooltips, 0 "Agent 2", 1 "Rest", 2 "Agent 1" (today 0 Off / 1 On, so "Off" fires agent 2 and agent 1 cannot be chosen). DCDU Brightness (`S_DCDU1_DIM_BRT`, `S_DCDU2_DIM_BRT`): 0 "Dim", 1 "Mid", 2 "Bright", one step per pick. The combo keeps showing the picked end until the panel is reopened, as plan 06's ET Reset does. Manual pressurization keeps holding (the valve moves while held, which is what the pilot wants); only its labels change.
5. **Positions the model fixes:** Manual Vertical Speed 0 "Down", 1 "Mid", 2 "Up" (measured). ADIRS System Display (`S_OH_NAV_SYS_DISP`, GNADIRS only, model `Knob_Increment` 0..3): four positions 0 "Off", 1 "IR 1", 2 "IR 3", 3 "IR 2" (the real SYS DISP selector's order; Step 1 confirms if a GNADIRS livery is at hand). Weather radar Image Selector (`S_WR_MODE`, model 0..3 in steps of 1; today 0..1 in tenths): 0 "WX", 1 "WX+T", 2 "TURB", 3 "MAP" (the real selector's order; Step 1 confirms on the ND). The handlers write whole positions.
6. **N1 MODE is registered once** (formerly plan 06's handoff H6): `S_OH_ENG_N1_MODE_1/2` appear twice in one initializer (:4720-4735 and :5770-5778, the later wins). Keep one `OnRequest` definition with `OffOn` and the label pilots hear today, "Engine N1 Mode 1" / "2" (plan 06's lights take their labels from it).
7. **Thrust levers** (`A_FC_THROTTLE_LEFT/RIGHT/BOTH_INPUT`, model 0..5 continuous): if Step 1 shows 0 = full reverse and 1 = reverse idle, offer 0 "Full Reverse", 1 "Reverse Idle" and keep 2-5; otherwise leave them.
8. **ACP INT/RAD** (`S_ASP_INTRAD`): keep 0 INT, 1 OFF, 2 RAD (presets agree; v3's tooltips say the reverse); Step 1 checks it by ear.

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (the definitions named above, their panel lists, their `HandleUIVariableSet` branches, the counter helpers :12775-12870), `MSFSBlindAssist/FirstOfficer/Fenix/FenixActionExecutor.cs` (`SetBaroStdBoth` :270-278)
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixPanelControlsTests.cs`

**Interfaces (produces):** `internal static string CounterStepCode(string lvar, int steps)`; `private void PulseSpringSwitch(string lvar, double position, SimConnect.SimConnectManager simConnect)`; the keys `S_FCU_EFIS1_BARO_PULL`, `S_FCU_EFIS1_BARO_PUSH`, `S_FCU_EFIS2_BARO_PULL`, `S_FCU_EFIS2_BARO_PUSH`.

- [x] **Step 1: Owner in sim (measure; owner's go-ahead for each write).**
  - Thrust levers: engines at idle, parking brake set. Set `A_FC_THROTTLE_LEFT_INPUT` to 1 for 3 s and read `aircraft.engines.1.n1` and `L:A320_Engine_Reverser_Left`; then 0 for 3 s, read again; back to 2. Record which value gives full reverse.
  - Optional, a GNADIRS livery (or `adirs_type` GNADIRS for the session): turn `S_OH_NAV_SYS_DISP` 0 to 3 and note what the ADIRS display shows at each. Weather radar: `S_WR_MODE` 0 to 3, read the ND's radar mode (Alt+N, or a sighted helper).
  - INT/RAD: with vPilot or SayIntentions connected, hold `S_ASP_INTRAD` at 2 for 2 s: the radio transmits (RAD), or not.
  - Write the results into this task. If a label differs from decision 5 or 8, use the measured one.
- [x] **Step 2: Write the failing tests** in `FenixV3FixPanelControlsTests` (one definition instance, `GetVariables()`, `GetPanelControls()`; source scans with `FoFbwUnclaimedEventKeyTests.MethodBody` and `ClaudeContextBudgetTests.RepoRoot()`):
  - `Momentary_buttons_are_press_buttons`: each key in decision 1 is `RenderAsButton`, has no `ValueDescriptions`, and its `StateVariable` is the listed light (null for `S_WR_GCS`, `S_OH_SVCE_INT_OVRD` and `S_OH_LIGHTING_AVIONICS_COMPT`); in `HandleUIVariableSet`, each key's `if (varKey == "<key>")` block calls `ExecuteButtonTransition(` and not `SetLVar(`.
  - `Counters_step_from_the_sims_value`: `CounterStepCode("E_FCU_SPEED", 3)` == `"(L:E_FCU_SPEED) 3 + (>L:E_FCU_SPEED)"`, `(…, -2)` == `"(L:E_FCU_SPEED) 2 - (>L:E_FCU_SPEED)"`, both under `CultureInfo("sv-SE")`; the bodies of `IncrementCounter`, `DecrementCounter` and `JumpCounter` contain `CounterStepCode(` and `ExecuteCalculatorCodeUnique(`; the file no longer contains `rmpCounters`.
  - `Baro_std_is_a_pull_and_a_push`: the two EFIS panels list the four new keys and not `S_FCU_EFIS1_BARO_STD` / `S_FCU_EFIS2_BARO_STD`; the new keys are `RenderAsButton` with the labels in decision 3 and `Name` `S_FCU_EFISn_BARO_STD`; `I_FCU_EFIS1_QNH` and `_2_` stay `Continuous` and `IsAnnounced`; `SetBaroStdBoth`'s body contains `CounterStepCode(` and no `DispatchAsync("S_FCU_EFIS`.
  - `Spring_switches_return_to_the_middle`: Cargo Discharge 1/2 values exactly `{0:"Agent 2",1:"Rest",2:"Agent 1"}`, DCDU Brightness 1/2 `{0:"Dim",1:"Mid",2:"Bright"}`; their branches call `PulseSpringSwitch(`.
  - `Positions_match_the_cockpit`: Manual Vertical Speed `{0:"Down",1:"Mid",2:"Up"}`; System Display exactly four positions as decision 5; Image Selector exactly `{0:"WX",1:"WX+T",2:"TURB",3:"MAP"}`; Annunciator unchanged `{0:"Dim",1:"Bright",2:"Test"}` (pins the measurement).
  - `N1_mode_is_registered_once`: the definition source has `["S_OH_ENG_N1_MODE_1"]` and `["S_OH_ENG_N1_MODE_2"]` once each; each is `OnRequest` with labels "Engine N1 Mode 1" / "Engine N1 Mode 2".
- [x] **Step 3: Run them; they fail.** `--filter FenixV3FixPanelControlsTests`.
- [x] **Step 4: Implement decisions 1-7.** Keep every key name (pilots' Ctrl+M mutes are stored by key); only `S_FCU_EFISn_BARO_STD` leaves the panels, replaced by the pull/push keys.
- [x] **Step 5: Run the filter, then the whole suite; all pass. Build the solution.**
- [x] **Step 6: Commit.** `fix(fenix): momentary buttons press, counters step from the sim, baro pull/push, spring switches return, positions match the v3 cockpit`
- [ ] **Step 7: Owner in sim (few).** (1) Restart the app mid-flight, then FCU Speed increase once from the panel: the speed goes up 1. (2) Overhead > Calls: press Calls Fwd once; the cabin chime sounds once and nothing stays held (not the Bus Tie: toggling it disturbs ground power). (3) Pull the left baro knob twice in the virtual cockpit, then the app's "EFIS Left Baro Pull (STD)": hear "EFIS1 BARO STD Status: STD"; "Push (QNH)": QNH. (4) Pressurization in MAN: Manual Vertical Speed "Up" opens the outflow valve; back to Mid and AUTO.

### Task 12: Hand back

- [x] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` and the full suite: both green. (2026-10-22, rebased on plan 09: 14,558 passed.)
- [x] **Step 2:** `git diff --name-only <base-sha>` lists only: `FenixA320Definition.cs`, `FenixA320GravityGear.cs`, `FenixTakeoffCallouts.cs`, `FenixTakeoffConfigVerdict.cs`, `FenixActionExecutor.cs` (the take-off config members, the unused hold constant and `SetBaroStdBoth`), `TakeoffAssistManager.cs` (Fenix V-speed code removed), `MainForm.Hotkeys.cs` (its Fenix V-speed block), `SettingsManager.cs` (seed), `SettingsSeedTests.cs`, `FenixTakeoffConfigVerdictTests.cs`, and the new `FenixV3Fix*Tests.cs` (incl. `FenixV3FixFcuVsTests.cs`) / `FenixTakeoffConfigWiringTests.cs` / `FenixTakeoffCalloutsTests.cs`.
- [x] **Step 3:** Fill Task 1's and Task 6's records in this file, tick the boxes, and hand the "Handoff to plan 12" section below to the integrator.

---

## Handoff to plan 12

### Pilot-facing changes (for the guide and changelog, `fix` category)

1. Fenix Overhead has a new **Flight Control Computers** panel: ELAC 1 and 2, SEC 1 to 3, FAC 1 and 2. They were never reachable before. Guide: `guide/airbus-aircraft.md` Fenix Overhead list (about line 237).
2. **Gravity Gear Extension** (Cockpit → Cockpit Other) is now one control, Stowed or Extended. Extended raises the handle and turns the crank three times (about seven seconds), then BA says "Gravity gear: cranked 3 turns."; Stowed turns it back and stows the handle ("Gravity gear: crank stowed."). The control keeps your pick while the crank turns; a second pick before it finishes is refused aloud ("Gravity gear: crank already turning."). The separate "Gravity Gear Crank" is gone. Guide line 252 ("gravity gear extension and its crank").
3. **TO CONFIG** (panel key and First Officer) now waits for the Fenix warning computer's own delay: "Takeoff config normal." comes only after both master lights stayed dark that long (about `VerdictAfterMs`); a bad config is announced as soon as the light comes on. It can also say "Takeoff config: caution. Check the ECAM." and "Takeoff config: no result read. Check the ECAM." Guide line 314.
4. **Generator 1** now switches GEN 1. It used to press the EMER ELEC GEN 1 LINE button.
5. **Take-off calls:** the Fenix now calls "V1", "Rotate" and "V2" from the speeds on PERF TAKE OFF, followed live (enter or change them any time before the roll), whether or not Takeoff Assist is on; each call is muted with its speed row in Ctrl+M; a cleared speed is spoken as "not set". The Fenix's own warning computer also calls "V1" aloud (`FWC_V_ONE_L/_R` in its cockpit sound.xml), so V1 is heard twice: the owner chose to keep all three app calls (2026-10-22), as on the A380. Takeoff Assist no longer calls V1 or rotate on the Fenix (its 80 and 100 knots calls stay). Guide: the Fenix part of the take-off callouts section, and `guide/takeoff-climb-and-cruise.md:102`, `:132`, `:159`, which still say the Fenix calls come from Takeoff Assist.
6. **FCU vertical speed:** no more callouts every second or two during a managed climb or descent; the V/S is spoken only while its window shows a selected value, now with its sign ("FCU VERTICAL SPEED: +300", as a descent's "-300" and the FBW A320's V/S call-out; owner request 2026-10-22, `SimVarDefinition.SpokenNumberFormat`, opt-in and shared like handoff 23).
7. On Fenix v3 these are announced again: the DC SERVICE bus and the cockpit door video light (the AUTOLAND lights needed no change: Task 6). The COMMERCIAL upper-legend row and 13 FenixQuartz rows leave the Ctrl+M Monitor Manager (they never spoke).
8. **Panel buttons that press:** Bus Tie, Service Interphone Override, Avionics Compartment Light, Cockpit Door Video, the radar's GCS, ACP PA Transmit and CALLS Mech/All/Fwd/Aft are now buttons you press once, like the real ones; the result is heard from the button's light where it has one. Before, "On" flipped the function whatever its state and "Off" did nothing.
9. **FCU and baro knobs never jump:** after an app restart or a turn in the virtual cockpit, one step from the app moves one step the way you asked (it used to move by the difference, sometimes the wrong way).
10. **Baro STD** is now "EFIS Left Baro Pull (STD)" and "Push (QNH)" (and Right), like the FCU knobs; the First Officer's baro setting works the same way.
11. **Corrected panels:** Cargo Discharge picks Agent 1 or Agent 2 (before, "Off" fired agent 2); DCDU brightness takes one step per pick instead of ramping to the limit; Manual Vertical Speed Up and Down were reversed; ADIRS System Display has its four positions; the weather radar Image Selector reaches all four modes (both in the real selectors' order, not yet confirmed live). The thrust levers are unchanged: Task 11 Step 1 measured input 0 settling at near-idle reverse (N1 22.7), not full reverse.
12. **N1 MODE** (Engine N1 Mode 1 and 2) is registered once, as an on-request switch like every other Fenix switch: a change made outside the app is no longer announced by the switch itself, and its two Ctrl+M rows are gone (the duplicate that won was the announced copy). Plan 06's N1 MODE lights announce the state.
13. **Typed baro** (the baro dialog) leaves STD with a push step, so a typed QNH always ends in QNH, whatever the knob's count after cockpit pulls and pushes. The altimeter window's QNH/STD choice also pulls or pushes the knob instead of writing a fixed value.
14. **Master Warning** is spoken "On" when lit (a lit warning reads 2 on the data service and flashes 1/0 over SimConnect, measured 2026-10-22; it used to be spoken "Master Warning Captain: 2").
15. **Press buttons with no light** (GCS, Service Interphone Override, Avionics Compartment Light, CALLS Mech/All/Fwd/Aft, and the pedestal Cockpit Door Video under Cockpit Other) say "<name> pressed" once per press; buttons with a light stay quiet and the light speaks. The pedestal Cockpit Door Video was paired with the overhead VIDEO pb's light until the owner's check of 2026-10-22 (a press left that light unchanged, so it was silent and its label never moved).
16. **Spring-loaded switches** (Cargo Discharge, DCDU brightness): the control shows the middle again once the switch has sprung back, so the same end can be picked again (DCDU brightness one step at a time).
17. **ADR 1-3, IR 1-3 and Bus Tie read the right way round** (owner check, 2026-10-22): their light is the OFF legend, so a working ADR used to read "ADR 1: Off" and a switched-off one "On". The buttons now say "On" (Bus Tie "Auto") with the light dark and "Off" with it lit, and the Bus Tie light's own announcement says the same ("Electrical Bus Tie: Off"); so does the overhead Cockpit Door VIDEO pb's light ("OverheadMisc Cockpit Door Video: Off" when VIDEO is set Off, measured). The ADR and IR lights still announce as lamps ("OH ADIRS ADR1 Lower: On" when ADR 1 is switched off): handoff 22.
18. **Altimeter read-back** (the altimeter window's typed setting): "Both altimeters set: 1015, 29.97." when both sides read alike (both units kept, owner 2026-10-22), else each side named ("Captain altimeter 1015, 29.97. First officer altimeter 1014, 29.94.", "… still in STD", "… not read"). It used to read the captain's side alone as "Altimeter set: 1015, 29.97". Guide (CORE-19): the Fenix altimeter window in `guide/airbus-aircraft.md` (about :273) does not give the confirmation wording; the FBW one (about :106) does.

### Proposed rule lines (new Fenix rules go to `.claude/rules/fenix-data.md`, since a32nx-fenix.md is at its cap)

fenix-data.md's globs (plan 01) cover only the definition's partials, so plan 12 also adds the globs `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`, `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs`, `MSFSBlindAssist/FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs`, `tests/MSFSBlindAssist.Tests/**/*FenixV3Fix*.cs` and `tests/MSFSBlindAssist.Tests/**/*FenixTakeoffConfig*.cs`, then runs `ClaudeContextBudgetTests`.

```text
- [FNX-4] Fenix TO CONFIG verdicts (panel key and First Officer) come only from `FenixTakeoffConfigVerdict.WatchAsync`: fresh twin reads timed from the press it observed; "normal" only when both master lights read dark at or past `VerdictAfterMs` (v3 delays them). Never the cached 1 Hz lights, a fixed-time read or a pre-press baseline. Full: docs/invariants/fenix-data.md#fnx-4
- [FNX-5] The Fenix gravity gear is one control: `FenixA320GravityGearWalker` raises `S_HYD_GRAVITY_GEAR_HANDLE`, then steps `S_HYD_GRAVITY_GEAR_EXTEND` 0 to 3 in the cockpit template's 0.05 steps, and stows in reverse, handle last; never write 1 (a third of a turn) and never move it on an unread crank. Full: docs/invariants/fenix-data.md#fnx-5
- [FNX-?] Fenix knob and push/pull counters (`E_FCU_*`, `E_MIP_ISFD_BARO`, `S_FCU_VERTICAL_SPEED`, `S_FCU_EFISn_BARO_STD`) step from the sim's own value (`CounterStepCode`, read-modify-write), never an app-side count: the Fenix acts on the difference, so a count that missed a cockpit turn or an app restart steps the wrong way (+1 moved FCU speed -2, measured 2026-10-21). Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] A Fenix `Button_Momentary` control is a press button (`ExecuteButtonTransition`), never an Off/On combo: a held 1 flips the function once whatever its state and 0 does nothing (bus tie, measured 2026-10-21). A `Switch_3Position_Hold` end is written then released to the middle (`PulseSpringSwitch`). Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] A Fenix button whose `StateVariable` is an OFF legend (ADR, IR: `_L`; Bus Tie) sets `StateLitText = "Off"` and its dark text, and an announced OFF lamp speaks the same words; never infer which lamp a control owns or what it means: press it in the sim and watch which lights (FO-11; ADR 1, IR 1, door VIDEO measured 2026-10-22). Full: docs/invariants/fenix-data.md#fnx-?
```

All five are under 400 characters (the last two, Task 11's, with `?` for their numbers). Full text for `docs/invariants/fenix-data.md`: Task 1's record (FNX-4), this plan's template evidence (FNX-5) and Task 11's measurements (the last two). Task 5's owner check (2026-10-22) needed no calculator fallback: the per-step `SetLVar` moved the crank (SimConnect read-back of the L:vars: handle 0 → 1, crank 0 → 3 in 0.05 steps in about 7 s, back to 0, handle 0 last). FNX-5's full text should add two measured facts: the data service's `system.switches.S_HYD_GRAVITY_GEAR_EXTEND` reads whole numbers (1, 0, 1 on the way up, 0 at the end) and never 3, so the check is the L:var read-back, not the data service; and, as MD11-8 for the MD-11's walked controls, the crank's deliveries are consumed while a move runs and one forced read re-syncs the panel combo when it ends (without it the walker's first read snapped the combo back to Stowed: found in the owner's check).

### Handoff requests (files plan 02 does not own)

1. `MSFSBlindAssist/Settings/SettingsManager.cs`: add `private static readonly (string Old, string New)[] FenixRenamedMonitorKeys = { ("B_ELEC_BUS_POWER_DC_SERIVICE", "B_ELEC_BUS_POWER_DC_SERVICE"), ("I_OH_DOOR_VIDEO", "I_OH_COCKPIT_DOOR_VIDEO") };` and `moved |= Carry(settings.FenixDisabledMonitorVariables, FenixRenamedMonitorKeys);` in `CarryRenamedMonitorMutes`; extend its doc comment (Fenix v3, 2026-10-21); test like `A380RenamedMonitorMuteTests`. Why: keep a pilot's Ctrl+M mute across the v3 rename (decision 1).
2. `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`, after plan 05 merges: delete the three `FNX2PLD_speedV1/VR/V2` definitions and their comment; `FenixV3FixNamesTests.Only_the_takeoff_speed_FenixQuartz_names_remain` then expects none. Why: plan 05 reads `N_MISC_PERF_TO_*` instead.
3. Note for plan 05: the First Officer reads none of the renamed or removed names except `FNX2PLD_speedV1/VR/V2` (`FenixChecklistDefinitions.cs:599-600`, `FenixStateEvaluator.cs:67`, `FenixReadbackTests.cs:220`): it must stop reading them. `TakeoffConfigTest` now takes up to `VerdictAfterMs` plus the press, not 1.5 s.
4. (The unused `TakeoffConfigTestHoldMs` is deleted in Task 8 Step 5.) `FenixChecklistDefinitions.cs:302`: the "holds 1.5 s" comment.
5. `MSFSBlindAssist/MainForm.Hotkeys.cs:524`: the comment naming `FNX2PLD_speedV1/VR` can go (no behaviour).
6. (Withdrawn: plan 01 no longer pins counts.)
7. Docs (spec 2.4 plus this plan): `docs/a32nx.md:71` (crank), `:89-91` (TO CONFIG: `onHeld`, 1.5 s, cached read are gone); `docs/first-officer.md:95-96`.
8. Follow-up, not built: an ISIS baro readout from `N_MIP_ISFD_BARO_HPA` / `_INCH` (decision 3), with plan 04's ISIS/standby work.
9. Task 9 (take-off calls): mirror TKO-5 onto the Fenix definition, as a "Mirrored from takeoff-and-callouts.md" line in a rule file that loads on `FenixA320Definition.cs` but not on takeoff-and-callouts.md's own files (run `ClaudeContextBudgetTests` for a double load, e.g. on `FenixTakeoffCalloutsTests.cs`), and add the Fenix to TKO-5's full text in `docs/invariants/takeoff-and-callouts.md`.
10. `docs/invariants/a32nx-fenix.md`, A320-3 and A320-4 full text: add the v3 measurement (Task 11): on 3.0.0.7426 a press-release pulse registers with holds of 50-200 ms and is missed at 20 ms (about one frame), so the 200 ms `ButtonPressHoldMs` stands.
11. Note for plan 05 (its Task 7): the First Officer's baro STD state comes from `I_FCU_EFISn_QNH` (0 STD, 1 QNH), never the `S_FCU_EFISn_BARO_STD` counter, which is any number after cockpit pulls and pushes. Now urgent: `FenixStateEvaluator.BaroText` (`FenixStateEvaluator.cs:179-180`) reads the raw counter as `std > 0.5`, and with this plan's relative steps the counter can be 0 while STD is selected. Tester note until plan 05's Task 7 lands: after using the app's Pull/Push or the altimeter window, do not trust the First Officer's "Baro reference" read-back (Cockpit Prep, Approach); check the "EFIS1 BARO STD Status" announcement instead.
12. DONE in plan 02's final fix wave (2026-10-22, an ownership exception: no plan owns this form and plan 02's relative steps broke it), kept here for the record. `MSFSBlindAssist/Forms/FenixA320/FenixBaroWindow.cs:179-180`: replace the absolute `SetLVar("S_FCU_EFISn_BARO_STD", isStd ? 1 : 0)` with a relative step, `simConnect.ExecuteCalculatorCodeUnique(FenixA320Definition.CounterStepCode($"S_FCU_EFIS{n}_BARO_STD", isStd ? 1 : -1))` (`CounterStepCode` is `internal static`, same assembly). Why: the counter acts on the difference, and the panel's Pull/Push and the First Officer now step it, so an absolute write can select the opposite mode (pull twice → counter 2 → "STD" writes 1, a decrease → QNH). Check: pull twice from the panel, then pick STD in the baro window: it stays STD.
13. Docs: `docs/fenix-increment-decrement.md` (about :30-108 and :256) and `docs/QUICK-REFERENCE.md` (about :217-230) still teach the removed `rmpCounters` absolute count (measured wrong 2026-10-21): rewrite to `CounterStepCode` relative steps through `ExecuteCalculatorCodeUnique`. CLAUDE.md's "Fenix rotary encoders" row points at the first. Stale mentions: `FenixActionExecutor.cs:20` (class doc) and the comment at `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFcuAltitudePushTests.cs:19`.
14. Guide (CORE-19): the Fenix FCU and baro knobs and the baro Pull/Push now step through the MobiFlight calculator channel (the FCU push/pull already did), so they need the MobiFlight WASM module like those.
15. Plan 12 Task 2's file list: Task 9 also changed one line of `tests/MSFSBlindAssist.Tests/TakeoffCalloutFeedTests.cs` (it pinned the Fenix's null take-off feed key, which Task 9 replaces).
16. `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs:29` (plan 01, comment only): "Start runs from the first ProcessSimVarUpdate that instance receives" — the take-off IAS feed branch now returns first, so the start can come up to about a second later; plans 08 and 09 should not rely on the very first update.
17. Item 5 is done already: Task 9 removed the whole Fenix V-speed block of `MainForm.Hotkeys.cs`, comment included.
18. Owner decision (Task 10 Step 1): `SetFCUVS` (typed V/S) counts its steps from the cached `N_FCU_VS`, which is the LIVE V/S while the window is dashed; left unchanged. Two accepted races: the first selected value at a V/S pull may arrive before "Shown" and stay unspoken (the next turn speaks), and after a connect one live V/S may be spoken before the dashed flag arrives. (Task 10's plan text reset the dashed flag on reconnect and flight load; the final review found that wiped the seeded flag, against `IAircraftDefinition`'s reset contract, so it now resets only when the variable cache is cleared.)
19. CLAUDE.md's CORE-7 list of today's "<name> pressed" cases gains the Fenix's stateless press buttons (GCS, SVCE INT OVRD, AVIONICS COMPT, CALLS Mech/All/Fwd/Aft).
20. Follow-ups, not built: other Fenix `ExecuteButtonTransition` buttons with no light are still silent on press (CORE-7 says "<name> pressed"); a Pull when already STD (or Push when already QNH) is silent, since the read-back is the QNH light changing; the gravity-gear walker and the panel TO CONFIG watch can outlive an aircraft switch by a few seconds.
21. `MSFSBlindAssist/Forms/FenixA320/FenixBaroWindow.cs` (pre-existing, found in the final re-review): its Mode combo's handler is `SelectedIndexChanged`, attached (:87) before the combo is placed on the form (:142); `MainForm.PanelBuilder.cs:862-867` records that this event can fire again when the handle is created, so opening the window could send one step (with the combo defaulting to STD when `I_FCU_EFIS1_QNH` is unread, a pull to STD). Move it to `SelectionChangeCommitted`, as the panel builder did. Also :282 speaks `{inHgValue:0.00}` in the PC's culture ("29,92" on a comma-decimal Windows): format it invariant, with a sv-SE test.
22. Owner decision pending (asked 2026-10-22): the ADR 1-3 and IR 1-3 lamps (`I_OH_NAV_ADRn_L`, `I_OH_NAV_IRn_SWITCH_L`, Continuous + IsAnnounced, `OffOn`) announce as lamps, "OH ADIRS ADR1 Lower: On" when ADR 1 is switched off: true of the light, unhelpful to the pilot. If renamed (for example "ADR 1" with {0 On, 1 Off}), keep the keys, so Ctrl+M mutes stay, and keep VAR-1 (no two controls of one panel share a name).
23. `SimVarDefinition.StateLitText` / `StateDarkText` (plan 02, `SimVarDefinitions.cs`) are shared and opt-in: only the Fenix sets them; every other aircraft's `StateVariable` button keeps "On" / "Off".

### In-sim checks (owner; all DONE 2026-10-22 on builds 2-4, the unproven items are follow-ups)

1. DONE: TO CONFIG measurement (Task 1, 2026-10-21), then the three verdict checks (Task 8 Step 8, 2026-10-22): flaps 0 → "Takeoff config: check configuration." (warning lit within about 1 s), flaps 1 → "Takeoff config normal.", First Officer Taxi "Takeoff config test" with flaps 0 → the failure verdict. The panel's 200 ms press gap never beat the watch's first read in five presses.
2. DONE (2026-10-22, build 2): gravity gear on the ground, the move (see the FNX-5 note above) and the fixed combo: Extended stays Extended, "cranked 3 turns" (crank 0 to 3 in 6.4 s), Stowed, "crank stowed". The real gear drop in flight stays unproven (follow-up).
3. DONE: AUTOLAND with the annunciator test (Task 6, no change needed). A real autoland warning lighting the unsplit names stays unproven.
4. DONE (2026-10-22): **Generator 1** Off then On with an engine running (`S_OH_ELEC_GEN1` 0 then 1, GEN 1 OFF light on then off, GEN 1 LINE untouched); Overhead lists **Flight Control Computers** after GPWS; "ELEC BUS POWER DC SERVICE STATUS: Powered" at a power-up (`B_ELEC_BUS_POWER_DC_SERVICE` 0 → 1).
5. Task 11 Step 1 DONE (thrust levers: no change). Not measured: ADIRS SYS DISP order (needs a GNADIRS livery), radar Image Selector modes (needs the ND read), INT/RAD (needs vPilot or SayIntentions). DONE on build 2 (2026-10-22): Step 7 (2) Calls Forward (one chime, "pressed" once, released after 200 ms), (3) baro Pull/Push from a counter of 2 (both sides), (4) manual pressurization (Down closes the outflow valve, Up opens it); DCDU brightness (one step per Bright, back to Mid); the altimeter window's QNH/STD after Pulls; a typed QNH with only the first officer's side in STD (that side leaves STD; with both in STD the window hides the value box, so that case cannot arise); GCS "pressed" once; master warning "On" (then "check configuration"). FOUND AND FIXED (Fix task F9): Bus Tie, ADR 1-3 and IR 1-3 read inverted (OFF legends; ADR 1 and IR 1 measured by a press, Bus Tie from the Fenix's XML until pressed); the pedestal Cockpit Door Video was paired with the overhead VIDEO pb's light (measured: VIDEO 0 lights it; a pedestal press leaves it unchanged). DONE on build 3: the labels read On / Auto with their lights dark; the altimeter window opened in QNH right after connecting stays QNH (handoff 21). DONE on build 4 (in flight): the pedestal Cockpit Door Video says "Cockpit Door Video pressed" once; Bus Tie pressed lights its OFF legend ("Off"), pressed again puts it out ("Auto"), which settles its legend by measurement; "Both altimeters set: …". Accepted by the owner: Bus Tie's state is heard twice after a press (NVDA reads the focused button's new label, then the lamp announces), against CORE-7's "once"; the owner judged it fine (2026-10-22), so no change, but a later pass could mark the lamp in the UI-set echo gate after a press.
6. One flight: Task 9 Step 7: the MCDU read-back is DONE ("MCDU V1 Speed" etc. as entered, 2026-10-22); take off with Takeoff Assist ON, as every pilot does (the calls no longer depend on it) → "V1", "Rotate", "V2" once each from the app, plus the Fenix's own aural "V1"; "clear a speed" cannot be done by the pilot (the Fenix MCDU refuses CLR on V1, as the real FMGS: unit tests only); listen whether the Fenix clears the speeds at liftoff (if it does, "V2" may be lost and "not set" spoken after liftoff). Task 10 Step 7 (connect the app with the V/S window ALREADY dashed, then a managed climb stays silent; pull → "Shown", turns spoken; push → "Dashed"), Task 11 Step 7 (1) (restart the app mid-flight, FCU speed +1 once → up 1). ALL DONE 2026-10-22: V1, Rotate, V2 each called once with Takeoff Assist on, and the Fenix keeps the speeds after liftoff (no "not set"); after a restart in a managed climb with the V/S already dashed, no V/S call-outs; pull "Shown", turns spoken, and the dash back (re-engaging the climb with the ALT knob: on the Airbus a V/S push levels off rather than dashing) "Dashed"; FCU Speed Inc after the restart moved the knob one step from the sim's own count (with the speed managed the Fenix preselected 252 for its 10 s, then went back to managed: the owner accepted it as the Airbus's behaviour). Also from this flight, the owner's request: a selected V/S is spoken signed ("+300"), pilot-facing change 6.
