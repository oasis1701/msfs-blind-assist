# Fenix v3 Fixes Implementation Plan (plan 02)

**Claimed by:** nobody yet · **Status:** not started

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair what Fenix v3 (3.0.0.7426) broke in the Fenix A320 definition and the older bugs the investigation found, and make the TO CONFIG check (panel button and First Officer) fail-safe against v3's delayed master warning/caution.

**Architecture:** Name fixes and panel fixes are edits to `FenixA320Definition.cs`, each pinned by a new `*FenixV3Fix*` test. The gravity gear becomes one sequenced control driven by a small testable walker (`FenixA320GravityGear.cs`). The TO CONFIG verdict moves from one cached read at 1.5 s to a shared watch in `FenixTakeoffConfigVerdict` that reads on-request `_READOUT` twins (the [A320-41] pattern) from the observed press until a measured verdict time; the panel and the First Officer both call it.

**Tech Stack:** C# 13 / .NET 10 WinForms, SimConnect (`SimConnectManager.SetLVar`, `ReadFreshAsync`), xUnit.

**Spec:** `docs/fenix-v3/spec.md` sections 1 and 2 (2.4 doc fixes belong to plan 11). Index and ownership: `docs/fenix-v3/plans/00-index.md`. Seams this plan builds on: `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions

My calls (the reviewer can change any of them):

1. **Renamed names get renamed keys** (`B_ELEC_BUS_POWER_DC_SERVICE`, `I_OH_COCKPIT_DOOR_VIDEO`): key equals the L:var again. A pilot's Ctrl+M mute is stored by key (`UserSettings.FenixDisabledMonitorVariables`), so the two old mutes must be carried by `SettingsManager.CarryRenamedMonitorMutes`; that method is outside this plan's lines, so it is a handoff to plan 11 (exact change below). Until it lands, a pilot who muted one of these rows in v2 hears it once and can mute it again.
2. **FenixQuartz: 13 of the 16 `FNX2PLD_*` definitions go now; the three `FNX2PLD_speedV1/VR/V2` stay** until plan 04 moves the First Officer to `N_MISC_PERF_TO_*`. Deleting them here breaks `FenixReadbackTests` (plan 04's file), which asserts they are registered. Plan 11 deletes the three after merging 08.
3. **No ISIS baro readout in this plan.** The dead `FNX2PLD_isisBaro` never spoke, so dropping it loses nothing. A real ISIS baro readout (`N_MIP_ISFD_BARO_HPA` 1013 / `_INCH` 2991.39 = inches x100 on the data service, read live today; the L:var encoding is unmeasured, and the unit follows the ISIS's own BARO button) is a new feature in plan 03's ISIS/standby area. Listed as a follow-up for plan 11.
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
- Owned files: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`; new `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs` (matches the a32nx-fenix.md glob `Aircraft/FenixA320*.cs`); `MSFSBlindAssist/FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs`; `MSFSBlindAssist/FirstOfficer/Fenix/FenixActionExecutor.cs` (the whole file for the parallel phase; plan 04 does not edit it, and this plan changes only the take-off config members and the constant they no longer use); `MSFSBlindAssist/Settings/SettingsManager.cs` ONLY the Fenix seed (lines 226-227 comment, 240); tests `SettingsSeedTests.cs`, `FirstOfficer/FenixTakeoffConfigVerdictTests.cs` and new `*FenixV3Fix*` / `*FenixTakeoffConfig*` files.
- Test command: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (add `--filter <ClassName>` for one class).

## Evidence read live on 2026-10-21 (Fenix A321 IAE, data service, read-only)

`S_HYD_GRAVITY_GEAR_EXTEND` 0; `S_HYD_GRAVITY_GEAR_HANDLE` `""` (model-only L:var); `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION` `""`. `S_OH_ELEC_GEN1` 1, `S_OH_ELEC_GEN1_LINE` 1. `S_OH_FLT_CTL_ELAC_1`, `_SEC_3`, `_FAC_2` all 1 (latched buttons). `I_MIP_AUTOLAND_{CAPT,FO}` and `_{CAPT,FO}_{U,L}`: all six exist, all 0. `I_OH_ELEC_COMMERCIAL_U` `""`, `_L` 0. `I_OH_DOOR_VIDEO` `""`, `I_OH_COCKPIT_DOOR_VIDEO` 0. `B_ELEC_BUS_POWER_DC_SERIVICE` `""`, `_SERVICE` true. `I_MIP_GEAR_1_L` 1 and `_1_U` 0 on the ground with the gear down. Package: the crank template `FNX32X_Interact_Emergency_Gear_Lever_Template` (`ModelBehaviorDefs/fnx32x/Templates/Interactions.xml` in `fnx-aircraft-a32x-common`) steps `S_HYD_GRAVITY_GEAR_EXTEND` by 0.05 between 0 and 3 at 10 Hz while held, shows the crank only while `S_HYD_GRAVITY_GEAR_HANDLE` > 0, and lets the handle move only while the crank is 0. `Cockpit_Behavior.xml` lights AUTOLAND from `I_MIP_AUTOLAND_{CAPT,FO}_U/_L` (upper, lower, or a "both" node). ELAC/SEC/FAC, GEN 1, GEN 2, GEN 1 LINE and COMMERCIAL are `FNX32X_Interact_Button_Latched_Template`; `S_ECAM_TO` is `..._Momentary_Template`.

## Review Focus

- A master light that comes on later than the old 1.5 s read must give "check", never "normal" (Task 7 test `A_warning_that_lights_late_is_still_caught`).
- A fresh read that times out at the verdict time must never give "normal" (Task 7 `Unread_lights_never_give_normal`).
- A press the watch never sees (First Officer gate busy, disconnect) gives "no result read"; a First Officer window closed mid-test speaks nothing (Task 7 `No_press_seen_is_no_result`, Task 8 source pin on `StillAttached`).
- Gravity gear picked again mid-crank, or with an unread crank, moves nothing (Task 5 `A_second_request_while_turning_is_refused`, `An_unread_crank_moves_nothing`).
- A partly turned crank is spoken "1.5", never "1,5", on a comma-decimal Windows (Task 5 `Partial_read_back_uses_a_point_under_sv_SE`).

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

- [ ] **Step 1: Owner sets up.** Fenix loaded (any variant), on the ground, both engines running, parking brake set, the app running a build of `main` (unchanged TO CONFIG code), Fenix aircraft profile.
- [ ] **Step 2: Bad config, three runs.** Owner: flap lever to 0 (Pedestal → Flight Controls → Flap lever: Up). Implementer starts `python -I fenix_probe.py --seconds 15 NAMES`; within 3 s the owner opens Pedestal → ECAM and presses **ECAM TO CONFIG**. Owner notes what the app says (today: "Takeoff config: check configuration." is right, "Takeoff config normal." is the bug). Between runs: press Main Instrument Panel → Warnings/Messages → **Master Warning Captain** and **Master Caution Captain** if lit, wait 5 s. Record per run: ms from `S_ECAM_TO=1` to the first lit master light, which light, ms from `S_ECAM_TO=0` (release) to the light going out (or "stays lit").
- [ ] **Step 3: Good config, one run.** Owner: flaps 1, pitch trim set, V-speeds entered. Same probe and press. Record any light (expected: none) and what the app said.
- [ ] **Step 4: Only if no run in Step 2 lit any light in 15 s.** With the owner's spoken go-ahead to write: flaps 0 again, `python -I fenix_probe.py --seconds 15 --pulse system.switches.S_ECAM_TO --hold 5 NAMES` (a 5 s press through the data service). Record as in Step 2.
- [ ] **Step 5: Fill the record.** `D` = the largest press-to-light time over the bad runs.

  | Item | Value |
  |---|---|
  | Bad runs: press→light ms (light) | ___ / ___ / ___ |
  | Light still lit after release? | yes / no |
  | Good run: any light? | ___ |
  | What the `main` build said (bad / good) | ___ / ___ |
  | `VerdictAfterMs` = D + 1000, rounded up to 100 | ___ |
  | `HoldMs` = 1500 if the light stays lit after release, else `VerdictAfterMs` | ___ |

- [ ] **Step 6: Decide.** Bad runs light a master light within 10 s and the good run lights nothing → continue (Tasks 7-8 use the two numbers). No light in any bad run (Steps 2 and 4) → STOP, ask owner question (a). A light on the good config → STOP, report to the owner. Tasks 2-6 do not depend on this and can proceed meanwhile.

### Task 2: v3 renames and the dead FenixQuartz definitions

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (1522-1530 `I_OH_ELEC_COMMERCIAL_U`; 5714-5722 `B_ELEC_BUS_POWER_DC_SERIVICE`; 5844 header comment; 5872-5975 the 13 `FNX2PLD_*` from `fcuSpd` to `clockEt`; 6370-6378 `FNX2PLD_isisBaro` and its "ISIS (1 variables)" header; 6717-6725 `I_OH_DOOR_VIDEO`)
- Modify: `MSFSBlindAssist/Settings/SettingsManager.cs:240` and the words "and the FenixQuartz chrono/ET counters" in its doc comment (226-227)
- Modify: `tests/MSFSBlindAssist.Tests/SettingsSeedTests.cs:214`
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixNamesTests.cs`

**Interfaces:** Produces the keys `B_ELEC_BUS_POWER_DC_SERVICE` and `I_OH_COCKPIT_DOOR_VIDEO` (used by plan 11's mute carry).

- [ ] **Step 1: Write the failing tests.** In `FenixV3FixNamesTests` (`Vars = new FenixA320Definition().GetVariables()`):
  - `V3_name_is_registered_under_its_own_key_and_announced` `[Theory]` over `B_ELEC_BUS_POWER_DC_SERVICE`, `I_OH_COCKPIT_DOOR_VIDEO`, `I_OH_ELEC_COMMERCIAL_L`: `Assert.Equal(name, Vars[name].Name)`, `Continuous`, `IsAnnounced`.
  - `No_variable_reads_a_name_v3_removed` `[Theory]` over `B_ELEC_BUS_POWER_DC_SERIVICE`, `I_OH_DOOR_VIDEO`, `I_OH_ELEC_COMMERCIAL_U`: `Assert.DoesNotContain(Vars, kv => kv.Key == gone || kv.Value.Name == gone)`.
  - `Only_the_takeoff_speed_FenixQuartz_names_remain`: the ordinal-sorted `Name`s starting `FNX2PLD_` equal `{ "FNX2PLD_speedV1", "FNX2PLD_speedV2", "FNX2PLD_speedVR" }` (comment: plan 04 moves the First Officer off them, plan 11 deletes them).
  - In `SettingsSeedTests`: `Fenix_DefaultSilentVars_AreAllRegisteredFenixVariables`: every entry of `ExpectedDefaultSilentVars` is a key of `new FenixA320Definition().GetVariables()`.
- [ ] **Step 2: Run them.** `--filter "FenixV3FixNamesTests|SettingsSeedTests"`. Expected: every DC SERVICE, door-video, `COMMERCIAL_U` and FenixQuartz case fails; the `COMMERCIAL_L` case and the seed fact pass for now.
- [ ] **Step 3: Implement.** Rename key and `Name` of the DC SERVICE and door-video entries (display names unchanged). Delete `I_OH_ELEC_COMMERCIAL_U`; leave `_L` as it is. Delete the 13 `FNX2PLD_*` entries and `FNX2PLD_isisBaro`; above the three speed entries put one comment: FenixQuartz (discontinued, never installed here), read only by the First Officer until plan 04, plan 11 deletes them. Fix the "(20 variables)" header count. Confirm with `rg -n FNX2PLD MSFSBlindAssist tests` that only the three speed definitions, the First Officer (plan 04's files) and the `MainForm.Hotkeys.cs:524` comment remain.
- [ ] **Step 4: Seed.** Run the seed fact: it now fails on `FNX2PLD_clockChr`. Delete line 240 of `SettingsManager.cs` and the matching line 214 of `ExpectedDefaultSilentVars` (now 7 entries); drop the FenixQuartz words from the doc comment. Existing users keep the two stale strings in their list: harmless, nothing matches them.
- [ ] **Step 6: Run the whole suite.** Expected: all green.
- [ ] **Step 7: Commit.** `fix(fenix): v3 renames (DC SERVICE, door video), drop COMMERCIAL upper and 13 dead FenixQuartz vars`

### Task 3: Generator 1 writes GEN 1

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (1641-1648 `["S_OH_ELEC_GEN1"]`, its `Name` and the "Uses GEN1_LINE internally" comment; handler 9425-9429)
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixGeneratorTests.cs`

**Interfaces:** Consumes `FoFbwUnclaimedEventKeyTests.MethodBody(string sourcePath, string methodName)` (internal, block bodies only) and `ClaudeContextBudgetTests.RepoRoot()`.

- [ ] **Step 1: Write the failing tests.**
  - `Generator_1_reads_its_own_switch`: `Assert.Equal("S_OH_ELEC_GEN1", Vars["S_OH_ELEC_GEN1"].Name)`; `Assert.Equal("Generator 1", Vars["S_OH_ELEC_GEN1"].DisplayName)`.
  - `No_two_plain_panel_writes_share_an_lvar`: on `MethodBody(<repo>/MSFSBlindAssist/Aircraft/FenixA320Definition.cs, "HandleUIVariableSet")`, collect `if \(varKey == "([^"]+)"\)\s*\{\s*simConnect\.SetLVar\("([^"]+)", value\);` pairs, group keys by written name; every group has one key, except the allow-listed `A_FC_THROTTLE_LEFT_INPUT` → {`A_FC_THROTTLE_LEFT_INPUT`, `A_FC_THROTTLE_BOTH_INPUT`} (reason: "Both" moves both levers). Today it fails with `S_OH_ELEC_GEN1_LINE` written by `S_OH_ELEC_GEN1` and `S_OH_ELEC_GEN1_LINE`.
  - `Plain_panel_writes_match_their_variable_name`: for the same pairs, `Vars[key].Name == written`, except `A_FC_THROTTLE_BOTH_INPUT`. Passes today; it fails if someone fixes the write and not the `Name`.
- [ ] **Step 2: Run.** `--filter FenixV3FixGeneratorTests`. Expected: 2 failures.
- [ ] **Step 3: Implement.** `Name = "S_OH_ELEC_GEN1"`, drop the comment; handler `simConnect.SetLVar("S_OH_ELEC_GEN1", value);`. Checked, no change: Generator 2, APU Generator (`S_OH_ELEC_APU_GENERATOR`), Emergency Gen 1 Line, and every other ELEC switch match the package's names; the First Officer never writes a generator; the GEN 1 lights `I_OH_ELEC_GEN1_U/_L` are already defined (1468-1485).
- [ ] **Step 4: Run the suite.** All green.
- [ ] **Step 5: Commit.** `fix(fenix): Generator 1 operates GEN 1, not the EMER ELEC GEN 1 LINE button`

### Task 4: Flight Control Computers panel

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (`GetPanelStructure` Overhead list 8509-8531: insert `"Flight Control Computers"` between `"GPWS"` 8528 and `"Engine"` 8529; `BuildPanelControls` 8791: the first `["Flight Controls"]` becomes `["Flight Control Computers"]`)
- Create: `tests/MSFSBlindAssist.Tests/FenixV3FixPanelTests.cs`

The first list's last element `"S_OH_FLT_CTL_FAC_2"` has no trailing comma; keep it so. (The DCDU Left list's `"S_DCDU1_PGDN",` before `// Print (1 button)` has its comma: checked, no defect.)

- [ ] **Step 1: Write the failing tests** (`def = new FenixA320Definition()`):
  - `Overhead_computers_have_their_own_panel`: `def.GetPanelControls()["Flight Control Computers"]` equals `S_OH_FLT_CTL_ELAC_1, _ELAC_2, _SEC_1, _SEC_2, _SEC_3, _FAC_1, _FAC_2` in that order.
  - `Overhead_lists_the_computers_after_GPWS`: in `def.GetPanelStructure()["Overhead"]`, index of `"Flight Control Computers"` == index of `"GPWS"` + 1.
  - `Pedestal_flight_controls_are_unchanged`: `GetPanelControls()["Flight Controls"]` has 14 entries, first `S_MIP_PARKING_BRAKE`, last `S_FC_FO_INST_DISCONNECT`.
  - `Every_panel_is_in_a_section`: every key of `GetPanelControls()` is in some `GetPanelStructure()` list.
  - `BuildPanelControls_names_each_panel_once` (source scan): in the definition source between `BuildPanelControls()` and `AddVariantPanelControls(panels)` (plan 01's seam), the names captured by `\["([^"]+)"\]\s*=\s*new List<string>` are distinct. A C# indexer initializer keeps only the last of two equal keys, which is how the overhead list vanished.
- [ ] **Step 2: Run.** `--filter FenixV3FixPanelTests`. Expected: the first two and the source scan fail.
- [ ] **Step 3: Implement** the two edits above; keep the comment `// Flight Control Computers (7 controls)`. The controls are latched pushbuttons in the package and read 1 live, so the existing Off/On combos and `SetLVar` handlers (10458-10506) are right.
- [ ] **Step 4: Run the suite: all green.**
- [ ] **Step 5: Commit.** `fix(fenix): overhead ELAC/SEC/FAC get their own Flight Control Computers panel`

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

- [ ] **Step 1: Write the failing tests** (fakes: `write` appends `(key, value)`; `readFresh` returns scripted values; `delay` returns `Task.CompletedTask`):
  - `CrankSteps_cover_three_turns_on_the_grid`: `CrankSteps(0, 3)` has 60 values, first 0.05, last 3.0; `CrankSteps(3, 0)` has 60, first 2.95, last 0.0; `CrankSteps(1.5, 3)` has 30, first 1.55; `CrankSteps(3, 3)` is empty.
  - `DescriptionKey_maps_only_the_ends` `[Theory]`: 0→0, 0.02→0, 3→3, 2.98→3, 1.5→1.5.
  - `Extend_from_stowed_raises_the_handle_then_cranks_to_3`: reads 0 then 3; writes[0] == (`HandleKey`, 1), then 60 `CrankKey` writes rising to 3.0; result `"Gravity gear: cranked 3 turns."`.
  - `Stow_from_extended_cranks_back_then_stows_the_handle`: reads 3 then 0; 60 crank writes falling to 0.0, last write (`HandleKey`, 0); result `"Gravity gear: crank stowed."`.
  - `Extend_from_a_partial_crank_continues_from_there`: reads 1.5 then 3; first crank write 1.55, 30 crank writes.
  - `An_unread_crank_moves_nothing`: read null → result `"Gravity gear: no read-back, nothing moved."`, no writes.
  - `A_second_request_while_turning_is_refused`: a `delay` that waits on a `TaskCompletionSource`; start `MoveAsync(true)`, then `MoveAsync(false)` returns `"Gravity gear: crank already turning."` and adds no write; complete the source, the first finishes.
  - `Partial_read_back_uses_a_point_under_sv_SE`: under `CultureInfo("sv-SE")`, `Describe(1.5)` == `"Gravity gear: crank at 1.5 turns."`.
  - `The_extension_control_is_stowed_or_extended` (definition): `Vars["S_HYD_GRAVITY_GEAR_EXTEND"]` has `ValueDescriptions` exactly {0: "Stowed", 3: "Extended"}, `DescriptionKeyFor(2.99)` == 3, `DescriptionKeyFor(1.5)` == 1.5; `GetPanelControls()["Cockpit Other"]` contains it and not `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION`.
- [ ] **Step 2: Run.** `--filter "FenixV3FixGravityGearTests|FenixV3FixNamesTests"`. Expected: compile failure (new types).
- [ ] **Step 3: Implement** `FenixA320GravityGear.cs` per Interfaces, with a class comment citing the template and the live `""` for the handle.
- [ ] **Step 4: Wire the definition.** Def 4947: `ValueDescriptions = {[0] = "Stowed", [3] = "Extended"}`, `ValueToDescriptionKey = FenixA320GravityGear.DescriptionKey` (OnRequest, display name unchanged). Delete def 4948, its "Cockpit Other" entry and both case labels 9382-9383. New branch: `if (varKey == FenixA320GravityGear.CrankKey) { _ = MoveGravityGearAsync(value >= FenixA320GravityGear.FullTurns / 2, simConnect, announcer); return true; }`. `private async Task MoveGravityGearAsync(bool extend, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer? announcer)` keeps one `FenixA320GravityGearWalker` per definition (field `_gravityGearWalker`), whose delegates call the manager from the latest call (field `_gravityGearSim`): `SetLVar(k, v)`, `ReadFreshAsync(k, FenixA320GravityGear.ReadTimeoutMs)`, `Task.Delay`. Speak the result: refusals and "no read-back" with `AnnounceImmediate`, the completion read-back with `Announce` (it lands about 6 s after the pick). Wrap in try/catch → `Log.Debug("Fenix", …)`.
- [ ] **Step 5: Run the suite: all green.**
- [ ] **Step 6: Commit.** `fix(fenix): gravity gear is one control that raises the handle and cranks three turns`
- [ ] **Step 7: Owner in sim (ground only: owner answer 2, no flight test).** Fenix on the ground, parked. Cockpit → Cockpit Other → **Gravity Gear Extension** → Extended. Expected after about 6 s: *"Gravity gear: cranked 3 turns."*, and a read-only data-service read of `system.switches.S_HYD_GRAVITY_GEAR_EXTEND` gives 3. Then choose Stowed: about 6 s later *"Gravity gear: crank stowed."* and the read gives 0. If the read does not reach 3: replace the per-step `SetLVar` with the template's own step through the calculator, `simConnect.ExecuteCalculatorCodeUnique("3 (L:S_HYD_GRAVITY_GEAR_EXTEND) 0.05 + min (>L:S_HYD_GRAVITY_GEAR_EXTEND)")` (stow: `"0 (L:S_HYD_GRAVITY_GEAR_EXTEND) 0.05 - max (>L:S_HYD_GRAVITY_GEAR_EXTEND)"`), and retest once; if that fails too, report to the owner. Whether 3 turns actually drops the gear in flight stays unproven: list it as a follow-up in the handoff.

### Task 6: AUTOLAND lights (measure, then follow)

**Files:** Modify `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (782-799, only the two `Name` values, if Step 2 says so); modify `tests/MSFSBlindAssist.Tests/FenixV3FixNamesTests.cs`.

- [ ] **Step 1: Owner in sim (ground, batteries on).** Implementer runs `python -I fenix_probe.py --seconds 20` with the six names `system.indicators.I_MIP_AUTOLAND_CAPT`, `_CAPT_U`, `_CAPT_L`, `I_MIP_AUTOLAND_FO`, `_FO_U`, `_FO_L` (full names). Owner: Overhead → Interior Lights → **Annunciator** → Test, wait 5 s, back to Bright. Record which names went to 1.
- [ ] **Step 2: Decide.** The unsplit names lit → no code change; note it here and skip to Step 5. Only split names lit → the lit name is the new `Name` (prefer `_U` when both `_U` and `_L` lit); keys `I_MIP_AUTOLAND_CAPT` / `I_MIP_AUTOLAND_FO` stay, so `S_MIP_AUTOLAND_*`'s `StateVariable` and Ctrl+M mutes keep working. Nothing lit → no change; add "AUTOLAND during a real autoland warning" to the handoff in-sim list and skip to Step 5.
- [ ] **Step 3: Test first.** `Autoland_lights_read_the_v3_name` `[Theory]` (`I_MIP_AUTOLAND_CAPT` → measured name, `I_MIP_AUTOLAND_FO` → measured name): `Vars[key].Name`; and `Vars["S_MIP_AUTOLAND_CAPT"].StateVariable == "I_MIP_AUTOLAND_CAPT"`. Run: fails.
- [ ] **Step 4: Change the two `Name`s** (display names unchanged). Run the suite: green. Commit `fix(fenix): AUTOLAND lights read the v3 split legend`.
- [ ] **Step 5: Owner check** (only after a change): Annunciator → Test. Expected among the lights: *"Glareshield Autoland Captain: On"* and *"Glareshield Autoland FO: On"*.

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

- [ ] **Step 1: Write the failing tests** (a fake clock that `delay` advances; a scripted reader per key keyed on the fake time; `verdictAfter` = 2.5 s, `pressWait` = 3 s):
  - `Decide` `[Theory]`: warning at 1.0 s among dark samples → check; caution only → caution; dark at 2.4 s only → no result; dark at 2.5 s → normal; (2.6 s, warning 0, caution null) → no result; empty → no result.
  - `A_warning_that_lights_late_is_still_caught`: edge at 0.4 s, warning lights at edge + 2.0 s → check.
  - `Normal_comes_only_after_the_verdict_time`: all dark → normal, and the fake clock at return ≥ edge + 2.5 s.
  - `No_press_seen_is_no_result`: button always 0 → no result, clock ≥ 3 s, no light read issued.
  - `A_button_already_held_needs_a_release_first`: button 1, 1, 0, 1 → t0 is the last 1; all dark → normal no earlier than that t0 + 2.5 s.
  - `A_warning_lit_before_the_press_is_check`: warning 1 throughout → check.
  - `Caution_waits_for_a_late_warning`: caution at edge + 0.5 s, warning at edge + 1.5 s → check.
  - `Unread_lights_never_give_normal`: light reads return null → no result.
  - `The_hold_is_never_shorter_than_v2s`: `FenixTakeoffConfigVerdict.HoldMs >= 1500`.
- [ ] **Step 2: Run.** `--filter FenixTakeoffConfigVerdictTests`. Expected: compile failure.
- [ ] **Step 3: Implement** per Interfaces. Rewrite the class comment: v3's FWC delays MASTER WARN/CAUT (Fenix V3 changelog), so one cached read at 1.5 s could say "normal" over a bad config; the verdict is now a fresh-read watch from the observed press, still fail-safe (any light = check, unread = no result, never a pre-press baseline).
- [ ] **Step 4: Run.** All `FenixTakeoffConfigVerdictTests` pass, the old seven included. Suite green.
- [ ] **Step 5: Commit.** `fix(fenix): TO CONFIG verdict watches fresh reads until the v3 warning delay has passed`

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

- [ ] **Step 1: Constants.** Set `HoldMs` and `VerdictAfterMs` from Task 1's record; delete the TODO. (Task 1 must be done and must not have stopped.)
- [ ] **Step 2: Write the failing tests** in `FenixTakeoffConfigWiringTests`:
  - `Each_twin_is_a_one_off_read_of_its_original` `[Theory]` over `FenixTakeoffConfigVerdict.FreshKeys`: ends with `FenixA320Definition.ReadoutSuffix`; same `Name` and `Units` as the original key; `OnRequest`; `ExcludeFromMonitorManager`; `FreshReadPolicy.RouteRequest(def, false) == VarRequestRoute.DataDefinitionId` (as `FenixFcuReadoutTests` does).
  - `A_twin_answer_is_consumed_silently` `[Theory]` over the same keys: `def.ProcessSimVarUpdate(key, 1, speech)` is true and `speech.All` is empty (`SpeechCapture`).
  - `Panel_button_runs_the_shared_watch`: `MethodBody(definition, "RunTakeoffConfigTestAsync")` contains `FenixTakeoffConfigVerdict.WatchAsync(` and `pressHoldMs: FenixTakeoffConfigVerdict.HoldMs`; the `S_ECAM_TO` branch of `MethodBody(definition, "HandleUIVariableSet")` contains `RunTakeoffConfigTestAsync(simConnect, announcer)`; the definition source no longer contains `FenixTakeoffConfigVerdict.MasterWarningKey)`.
  - `First_Officer_runs_the_shared_watch`: `MethodBody(executor, "TakeoffConfigTest")` contains `FenixTakeoffConfigVerdict.WatchAsync(` and `PulseAsync(FenixTakeoffConfigVerdict.ButtonKey, FenixTakeoffConfigVerdict.HoldMs)`; `MethodBody(executor, "AnnounceTakeoffConfigResult")` contains `StillAttached(sc)` and not `GetCachedVariableValue`.
- [ ] **Step 3: Run.** `--filter FenixTakeoffConfigWiringTests`. Expected: failures (no twins, old wiring).
- [ ] **Step 4: Definition.** The twin loop iterates `FcuReadoutVariables.Concat(TakeoffConfigReadoutVariables)`. In the readout `switch` add `case FenixTakeoffConfigVerdict.ButtonFreshKey or FenixTakeoffConfigVerdict.MasterWarningFreshKey or FenixTakeoffConfigVerdict.MasterCautionFreshKey: return true;` (the fresh-read waiter already has the value; nothing is spoken). The `S_ECAM_TO` branch becomes `_ = RunTakeoffConfigTestAsync(simConnect, announcer); return true;`. `RunTakeoffConfigTestAsync`: first `ExecuteButtonTransition("S_ECAM_TO", "ECAM TO CONFIG", simConnect, announcer, pressHoldMs: FenixTakeoffConfigVerdict.HoldMs)` (it writes the 0 at once, so the watch's first read sees the release), then `await FenixTakeoffConfigVerdict.WatchAsync(k => simConnect.ReadFreshAsync(k, FenixTakeoffConfigVerdict.ReadTimeoutMs), FenixTakeoffConfigVerdict.PanelPressWait)`, then `if (simConnect.IsConnected) announcer?.AnnounceImmediate(text)`; try/catch → `Log.Debug("Fenix", …)`. Delete the old constant and method, and `onHeld`.
- [ ] **Step 5: First Officer executor (this plan owns `FenixActionExecutor.cs` for the parallel phase; also delete the now-unused `TakeoffConfigTestHoldMs` and its comment, about :58-63).** `TakeoffConfigTest`: `var sc = Sc; if (sc is not { IsConnected: true }) return false;` start `var watch = FenixTakeoffConfigVerdict.WatchAsync(k => sc.ReadFreshAsync(k, FenixTakeoffConfigVerdict.ReadTimeoutMs), FenixTakeoffConfigVerdict.FirstOfficerPressWait);` (before the pulse: the gate may make the press wait, and the watch needs to see the button released first), `bool pressed = await PulseAsync(FenixTakeoffConfigVerdict.ButtonKey, FenixTakeoffConfigVerdict.HoldMs);`, `string verdict = await watch;`, `if (pressed) AnnounceTakeoffConfigResult(sc, verdict); return pressed;`. `AnnounceTakeoffConfigResult` speaks `verdict` with `AnnounceImmediate` only when `sc.IsConnected && StillAttached(sc)`; try/catch → `Log.Debug("FO", …)`. Update both doc comments (no "cached", no "1.5 s").
- [ ] **Step 6: Build the solution and run the suite: all green.**
- [ ] **Step 7: Commit.** `fix(fenix): panel and First Officer TO CONFIG read fresh twins through the shared watch`
- [ ] **Step 8: Owner in sim (ground, engines running, parking brake set).** (1) Flaps 0, Pedestal → ECAM → **ECAM TO CONFIG**: within `VerdictAfterMs` *"Takeoff config: check configuration."*. (2) Flaps 1, trim and V-speeds set, same button: after about `VerdictAfterMs` *"Takeoff config normal."* (3) First Officer window (Output Shift+C), Taxi checklist, tick **Takeoff config test** with flaps 0: *"Takeoff config: check configuration."* A wrong verdict in any of the three: STOP and report with `debug.log`.

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

- [ ] **Step 1: Check the Fenix plays no V-speed call of its own.** Search `<MSFS Community>\fnx-aircraft-320\SimObjects\Airplanes\FNX_32X\attachments\fnx\Part_Interior_Cockpit_CEO\sound\sound.xml` (and the common cockpit sound file) for a V1/rotate event. If the Fenix calls V1 itself, stop and ask the owner whether the app should still call it (the A380 note in TKO-5 is the precedent).
- [ ] **Step 2: Write the failing tests** (pattern of `A320TakeoffCalloutsTests.cs`, under de-DE): `Keys_name_the_fenix_speed_rows` (feed key and the three `N_MISC_PERF_TO_*` keys); `Definition_registers_the_feed` (`AIRSPEED INDICATED`, knots, `Continuous`, `IsAnnounced`, `ExcludeFromBatch`, `HighFrequency`, `ExcludeFromMonitorManager`; and `TakeoffCalloutFeedKey == FenixTakeoffCallouts.IasKey`); `Speed_rows_read_not_set_below_1` (`NotSetBelow == 1` on all three); `Takeoff_assist_has_no_fenix_vspeed_code` (source scan: `TakeoffAssistManager.cs` and `MainForm.Hotkeys.cs` no longer mention `FenixVSpeeds` or `N_MISC_PERF_TO_V1`).
- [ ] **Step 3: Run them; they fail.**
- [ ] **Step 4: Implement** exactly as the A320 does, with the Fenix's keys and its mute set `FenixDisabledMonitorVariablesSet`: the feed branch first in `ProcessSimVarUpdate` (one `AnnounceImmediate` of `Compose`, skipped while `announcer.Suppressed`), `SIM_ON_GROUND` and the three speed keys PEEKED (fed to the machine, never consumed, so "MCDU V1 Speed: 140" is still spoken as it is entered), the arm dropped in both resets, never the speeds. Then delete the old Fenix path from `TakeoffAssistManager` and `MainForm.Hotkeys`.
- [ ] **Step 5: Run the suite, including `TakeoffVSpeedCalloutsTests`, `TakeoffCalloutFeedTests` and `TakeoffCalloutKeysTests`; all green. Build.**
- [ ] **Step 6: Commit.** `feat(fenix): take-off V1, Rotate and V2 on the shared callout machine (TKO-5)`
- [ ] **Step 7: Owner in sim (with plan 04's takeoff-speeds check).** PERF TAKE OFF: enter V1 140, VR 142, V2 145; hear "MCDU V1 Speed: 140" etc. Take off with Takeoff Assist OFF: hear "V1", "Rotate", "V2" at those speeds (one utterance when two coincide). Clear a speed: hear "not set", not "0".

### Task 10: FCU vertical speed is silent while the window is dashed (collaborator report, 2026-10-21)

Why: in v3, `N_FCU_VS` carries the LIVE vertical speed even while the FCU V/S window is dashed (`B_FCU_VERTICALSPEED_DASHED` = 1, managed climb or descent); v2 held it still. `N_FCU_VS` is a plain `Continuous` + `IsAnnounced` row (FenixA320Definition.cs:655-662) with nothing gating it, so a managed climb speaks "FCU VERTICAL SPEED: 3400 … 1700 … 4100" every second or two. A collaborator's v3 test flight: dashed from 13:40, yet `N_FCU_VS` went 3400 → 1700 → 4100 → 3400 between 14:09 and 14:12. Speed, heading and altitude are unaffected; the Shift+V readout already checks the dashed flag (:12122-12125).

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (`ProcessSimVarUpdate` from :12100; a private `_fcuVsDashed` field). Never override `TryGetDisplayOverride` here: plan 01's hooks file owns that override (plan 05's clock readouts use it), and a second one would not compile. The Fenix panel display list (`GetPanelDisplayVariables`, :9329) was empty on 2026-10-21.
- Test: `tests/MSFSBlindAssist.Tests/FenixV3FixFcuVsTests.cs`

**Interfaces:**
- Produces: `private double? _fcuVsDashed` (last `B_FCU_VERTICALSPEED_DASHED` seen; null until read) and `internal static bool FcuVsIsSilenced(double? dashed) => dashed == 1;`.

- [ ] **Step 1: Check the whole of what you touch.** Find every place the Fenix shows or speaks `N_FCU_VS`: the panel display list (`GetPanelDisplayVariables`, :9329), the FCU/autopilot windows (`Forms/FenixA320/`), the V/S set path (`SetFCUVS`, :13103, which reads the cached `N_FCU_VS` as the starting point: a live value there while dashed changes the step count; note what it does today). Record the list in this task.
- [ ] **Step 2: Write the failing tests** (constructing the definition and announcer as `FenixFcuReadoutTests.cs` does): `Vs_is_silent_while_dashed`: after `ProcessSimVarUpdate("B_FCU_VERTICALSPEED_DASHED", 1, a)`, `ProcessSimVarUpdate("N_FCU_VS", 3400, a)` returns true (consumed) and nothing is spoken; `Vs_speaks_when_shown`: after the flag reads 0, `N_FCU_VS` 1500 is not consumed (returns false, so the generic announcement speaks it); `Vs_speaks_before_the_flag_is_read`: with no flag read yet, `N_FCU_VS` is not consumed; `Dashed_flag_still_announces`: `ProcessSimVarUpdate("B_FCU_VERTICALSPEED_DASHED", 1, a)` returns false (peeked, never consumed, so "FCU VERTICAL SPEED DISPLAY: Dashed" is still heard); `FcuVsIsSilenced` is true only for exactly 1 (`null`, 0, `double.NaN` → false).
- [ ] **Step 3: Run them; they fail.**
- [ ] **Step 4: Implement.** In `ProcessSimVarUpdate`, after the readout `switch` (:12131): `if (varName == "B_FCU_VERTICALSPEED_DASHED") _fcuVsDashed = value;` (no return); `if (varName == "N_FCU_VS" && FcuVsIsSilenced(_fcuVsDashed)) return true;`. Reset `_fcuVsDashed` to null where the definition resets its announcement baselines (reconnect, `OnSimContextReset`), so a stale "dashed" never silences a fresh flight. If Step 1 finds the V/S on a panel display after all, do not change the display here: hand it to plan 11 (it should show "Dashed" while `FcuVsIsSilenced`, through plan 01's display hook). If `SetFCUVS` starts from a live value while dashed, note it in the handoff for the owner (do not change the set path here).
- [ ] **Step 5: Run the suite; all green.**
- [ ] **Step 6: Commit.** `fix(fenix): FCU vertical speed is silent while its window is dashed (v3 sends the live V/S)`
- [ ] **Step 7: Owner or collaborator in sim.** A managed climb with the V/S window dashed: no "FCU VERTICAL SPEED" callouts for a minute. Pull the V/S knob: hear "FCU VERTICAL SPEED DISPLAY: Shown"; turn it: each new target is spoken. Push it: "…: Dashed", then silence again. Note the race: the first selected value at a pull may arrive just before the "Shown" flag and stay unspoken (the next turn speaks); acceptable, say so in the handoff.

### Task 11: Hand back

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` and the full suite: both green.
- [ ] **Step 2:** `git diff --name-only <base-sha>` lists only: `FenixA320Definition.cs`, `FenixA320GravityGear.cs`, `FenixTakeoffCallouts.cs`, `FenixTakeoffConfigVerdict.cs`, `FenixActionExecutor.cs` (two members and the unused hold constant), `TakeoffAssistManager.cs` (Fenix V-speed code removed), `MainForm.Hotkeys.cs` (its Fenix V-speed block), `SettingsManager.cs` (seed), `SettingsSeedTests.cs`, `FenixTakeoffConfigVerdictTests.cs`, and the new `FenixV3Fix*Tests.cs` (incl. `FenixV3FixFcuVsTests.cs`) / `FenixTakeoffConfigWiringTests.cs` / `FenixTakeoffCalloutsTests.cs`.
- [ ] **Step 3:** Fill Task 1's and Task 6's records in this file, tick the boxes, and hand the "Handoff to plan 11" section below to the integrator.

---

## Handoff to plan 11

### Pilot-facing changes (for the guide and changelog, `fix` category)

1. Fenix Overhead has a new **Flight Control Computers** panel: ELAC 1 and 2, SEC 1 to 3, FAC 1 and 2. They were never reachable before. Guide: `guide/airbus-aircraft.md` Fenix Overhead list (about line 237).
2. **Gravity Gear Extension** (Cockpit → Cockpit Other) is now one control, Stowed or Extended. Extended raises the handle and turns the crank three times (about six seconds), then BA says "Gravity gear: cranked 3 turns."; Stowed turns it back and stows the handle. The separate "Gravity Gear Crank" is gone. Guide line 252 ("gravity gear extension and its crank").
3. **TO CONFIG** (panel key and First Officer) now waits for the Fenix warning computer's own delay: "Takeoff config normal." comes only after both master lights stayed dark that long (about `VerdictAfterMs`); a bad config is announced as soon as the light comes on. It can also say "Takeoff config: caution. Check the ECAM." and "Takeoff config: no result read. Check the ECAM." Guide line 314.
4. **Generator 1** now switches GEN 1. It used to press the EMER ELEC GEN 1 LINE button.
5. **Take-off calls:** the Fenix now calls "V1", "Rotate" and "V2" from the speeds on PERF TAKE OFF, followed live (enter or change them any time before the roll), whether or not Takeoff Assist is on; each call is muted with its speed row in Ctrl+M; a cleared speed is spoken as "not set". Guide: the Fenix part of the take-off callouts section.
6. **FCU vertical speed:** no more callouts every second or two during a managed climb or descent; the V/S is spoken only while its window shows a selected value.
7. On Fenix v3 these are announced again: the DC SERVICE bus, the cockpit door video light, and (if Task 6 changed them) the AUTOLAND lights. The COMMERCIAL upper-legend row and 13 FenixQuartz rows leave the Ctrl+M Monitor Manager (they never spoke).

### Proposed rule lines (new Fenix rules go to `.claude/rules/fenix-data.md`, since a32nx-fenix.md is at its cap)

fenix-data.md's globs (plan 01) cover only the definition's partials, so plan 11 also adds the globs `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`, `MSFSBlindAssist/Aircraft/FenixA320GravityGear.cs`, `MSFSBlindAssist/FirstOfficer/Fenix/FenixTakeoffConfigVerdict.cs`, `tests/MSFSBlindAssist.Tests/**/*FenixV3Fix*.cs` and `tests/MSFSBlindAssist.Tests/**/*FenixTakeoffConfig*.cs`, then runs `ClaudeContextBudgetTests`.

```text
- [FNX-4] Fenix TO CONFIG verdicts (panel key and First Officer) come only from `FenixTakeoffConfigVerdict.WatchAsync`: fresh twin reads timed from the press it observed; "normal" only when both master lights read dark at or past `VerdictAfterMs` (v3 delays them). Never the cached 1 Hz lights, a fixed-time read or a pre-press baseline. Full: docs/invariants/fenix-data.md#fnx-4
- [FNX-5] The Fenix gravity gear is one control: `FenixA320GravityGearWalker` raises `S_HYD_GRAVITY_GEAR_HANDLE`, then steps `S_HYD_GRAVITY_GEAR_EXTEND` 0 to 3 in the cockpit template's 0.05 steps, and stows in reverse, handle last; never write 1 (a third of a turn) and never move it on an unread crank. Full: docs/invariants/fenix-data.md#fnx-5
```

Both are under 400 characters. Full text for `docs/invariants/fenix-data.md`: Task 1's record (FNX-4) and this plan's template evidence (FNX-5). If Task 5's owner check needed the calculator fallback, FNX-5 says so instead of "steps".

### Handoff requests (files plan 02 does not own)

1. `MSFSBlindAssist/Settings/SettingsManager.cs`: add `private static readonly (string Old, string New)[] FenixRenamedMonitorKeys = { ("B_ELEC_BUS_POWER_DC_SERIVICE", "B_ELEC_BUS_POWER_DC_SERVICE"), ("I_OH_DOOR_VIDEO", "I_OH_COCKPIT_DOOR_VIDEO") };` and `moved |= Carry(settings.FenixDisabledMonitorVariables, FenixRenamedMonitorKeys);` in `CarryRenamedMonitorMutes`; extend its doc comment (Fenix v3, 2026-10-21); test like `A380RenamedMonitorMuteTests`. Why: keep a pilot's Ctrl+M mute across the v3 rename (decision 1).
2. `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`, after plan 04 merges: delete the three `FNX2PLD_speedV1/VR/V2` definitions and their comment; `FenixV3FixNamesTests.Only_the_takeoff_speed_FenixQuartz_names_remain` then expects none. Why: plan 04 reads `N_MISC_PERF_TO_*` instead.
3. Note for plan 04: the First Officer reads none of the renamed or removed names except `FNX2PLD_speedV1/VR/V2` (`FenixChecklistDefinitions.cs:599-600`, `FenixStateEvaluator.cs:67`, `FenixReadbackTests.cs:220`): it must stop reading them. `TakeoffConfigTest` now takes up to `VerdictAfterMs` plus the press, not 1.5 s.
4. (The unused `TakeoffConfigTestHoldMs` is deleted in Task 8 Step 5.) `FenixChecklistDefinitions.cs:302`: the "holds 1.5 s" comment.
5. `MSFSBlindAssist/MainForm.Hotkeys.cs:524`: the comment naming `FNX2PLD_speedV1/VR` can go (no behaviour).
6. (Withdrawn: plan 01 no longer pins counts.)
7. Docs (spec 2.4 plus this plan): `docs/a32nx.md:71` (crank), `:89-91` (TO CONFIG: `onHeld`, 1.5 s, cached read are gone); `docs/first-officer.md:95-96`.
8. Follow-up, not built: an ISIS baro readout from `N_MIP_ISFD_BARO_HPA` / `_INCH` (decision 3), with plan 03's ISIS/standby work.
9. Task 9 (take-off calls): mirror TKO-5 onto the Fenix definition, as a "Mirrored from takeoff-and-callouts.md" line in a rule file that loads on `FenixA320Definition.cs` but not on takeoff-and-callouts.md's own files (run `ClaudeContextBudgetTests` for a double load, e.g. on `FenixTakeoffCalloutsTests.cs`), and add the Fenix to TKO-5's full text in `docs/invariants/takeoff-and-callouts.md`.

### In-sim checks still needed (owner, in this order)

1. TO CONFIG measurement (Task 1), then the three verdict checks (Task 8 Step 8).
2. Gravity gear on the ground (Task 5 Step 7: the crank reads 3, then 0). The real gear drop in flight stays unproven (follow-up).
3. AUTOLAND with the annunciator test (Task 6 Steps 1 and 5); a real autoland warning only if the test lit nothing.
4. One ground power-up pass: Overhead → Electrical → **Generator 1** Off then On with an engine running, hearing the "Electrical Generator 1 Upper/Lower" lights change; "ELEC BUS POWER DC SERVICE STATUS: Powered" at power-up; Overhead lists **Flight Control Computers**.
