# First Officer APU-start gating — design

Date: 2026-07-06. Branch: `feature/first-officer`. Status: approved by user.

## Problem

The First Officer "Before Start" flows transfer electrical load from ground power to the APU,
but the four aircraft profiles gate that transfer inconsistently — and two of them unsafely:

| Aircraft | Today | Defect |
|---|---|---|
| PMDG 777 | Fixed 90 s blind `Wait` after APU START (`PMDG777FlowDefinitions.cs:213`) | No detection. GPU is disconnected after the timer regardless of whether the APU actually started. |
| PMDG 737 | `WaitForField` on `APU_Selector` springing START→ON (`PMDG737FlowDefinitions.cs:149`), default **Skip** policy | Spring-back = starter cutout, which happens while the APU is still spooling — before the generator is available. On timeout the flow continues and drops GPU anyway. |
| Fenix A320 | `WaitForField` on `I_OH_ELEC_APU_START_U` (AVAIL light), 180 s, default **Skip** (`FenixFlowDefinitions.cs:178`) | Correct signal, but a timeout (start failure) still proceeds to pulse external power off. |
| FBW A380 | Writes APU MASTER only (`FbwA380FlowDefinitions.cs:147`), then immediately switches all four EXT PWR PBs off | The START pushbutton (`A32NX_OVHD_APU_START_PB_IS_ON`) is never pressed, so the APU never starts; there is no AVAIL wait; external power is dropped onto batteries. Same master-only gap in After Landing (`:359`) and the checklist tick actions (`FbwA380ChecklistDefinitions.cs:184, 404`). |

## Decisions (user-approved)

1. **Timeout = abort.** Before Start APU waits use `FlowStepFailurePolicy.Stop` (the proven
   737 engine-start pattern, `FlowManager.cs:158-161`): on timeout the pilot hears
   *"Timed out waiting for: …"* then *"Before Start flow stopped. Unable to complete: …"* and
   ground power remains connected. Re-running the flow is safe — skip conditions no-op
   completed steps.
2. **Scope = everything**: all four Before Start flows, the 777 + A380 After Landing APU
   blocks, and the A380 checklist APU tick actions.
3. **After Landing waits stay Skip** — engines are running (no power-loss hazard) and a Stop
   would abort unrelated cleanup steps; the announced timeout is the pilot's cue.
4. **A380 Parking flow unchanged** — pilot-triggered after the After Landing outcome was
   announced.
5. **Verification = source-evidence + in-sim test plan** (project convention; no automated
   tests). Timeout paths reuse the already-proven Stop mechanism and are verified by review.

## Investigated facts the design rests on

- **777** `PMDG777XDataStruct.APURunning` (bool, struct line 712) is a direct "APU running"
  flag, already consumed by the System Display (`PMDG777Definition.SystemDisplay.cs:137`,
  `v > 0.5 ? "running" : "off"`). The FO evaluator reads any struct field by reflection via
  `GetValue` → `PMDG777DataManager.GetFieldValue` — **no registration needed**, NaN-gated
  until the first CDA snapshot (NaN fails any comparison → a wait just keeps waiting).
- **737** `PMDGNG3DataStruct` has `ELEC_annunAPU_GEN_OFF_BUS` (bool, line 293 — the blue
  "APU generator on line, not on a bus" light = *generator available*), `APU_EGTNeedle`
  (float, line 302), and `APU_Selector` (byte, line 475, `0 OFF / 1 ON / 2 START`). All
  reflection-readable, no registration needed.
- **Fenix** `I_OH_ELEC_APU_START_U` is registered `Continuous + IsAnnounced`
  (`FenixA320Definition.cs:5801`) → batch-cached at 1 Hz → readable by the evaluator today.
  The existing wait works; only the policy is wrong.
- **A380** `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` is registered `Continuous + IsAnnounced`
  via `ReadEnum` (`FlyByWireA380Definition.cs:356`) → batch-cached → readable with **no**
  evaluator/def changes. The START PB write via the FO executor
  (`FbwA380ActionExecutor.Set` → `ApplySilent` → `ApplyUIVariable`) hits the `A32NX_OVHD_`
  calc-path catch-all (`FlyByWireA380Definition.cs:4947`) and produces a single **level**
  write (`1 (>L:…)`) — the `button:true` flag on its `OnOff` registration is a documented
  no-op, the key is NOT in `_momentaryButtons`, so nothing pulses it back to 0. No in-repo
  evidence that FBW auto-clears the latched PB. AVAIL semantics: 1 = available
  (`tools/a380-simvars-catalog.md:90`).
- **FlowManager semantics**: `WaitForCondition` announces once ("Waiting for: …"), polls
  `_state.GetValue(field)` every 1 s up to `TimeoutSeconds` (`FlowManager.cs:236-254`).
  Stop → `FlowFailed` + immediate "… flow stopped. Unable to complete: …" and the flow ends.
  Skip → "Skipping: …" and the flow continues. `StartFlow` always restarts from step 0;
  resume-after-abort relies on per-step `SkipCondition`s.

## Per-aircraft design

### PMDG 777 (`PMDG777FlowDefinitions.cs`)

Before Start:
- Replace the fixed `Wait("BS_APU_WAIT", …, 90)` with
  `WaitForField("BS_APU_WAIT", "Waiting for the APU to start", "APURunning", v => v > 0.5, 120, onTimeout: Stop)`.
  (The 777 `WaitForField` helper must gain the optional `onTimeout` parameter the 737/Fenix
  helpers already have — today it hardcodes Skip, `PMDG777FlowDefinitions.cs:610-620`.)
- Add `Wait("BS_APU_SETTLE", "APU stabilising", 5)` after it (gen-breaker lag margin; the
  APU GEN switch was armed during Cockpit Prep and closes automatically on the 777).
- ON → 2 s → START sequence unchanged. GPU disconnect steps unchanged (now genuinely gated).

After Landing:
- Replace `WaitForField("AL_APU_RUNNING", …, "ELEC_APU_Selector", |v−1|<0.1, 90)` (unreliable
  per the Before Start comment) with the same `APURunning` wait, **Skip** policy, 120 s.

### PMDG 737 (`PMDG737FlowDefinitions.cs` + `PMDG737\AircraftStateEvaluator.cs`)

Evaluator:
- Add `ApuEgt() => GetValue("APU_EGTNeedle")` and redefine `IsApuRunning()` as
  `ApuEgt() >= ApuRunningEgt` with `public const double ApuRunningEgt = 100` (tunable,
  verified in-sim). **Audit existing `IsApuRunning()` callers first** — today it means
  "selector at ON", which is only the switch position; any caller that genuinely wants
  switch position should call `ApuSelector() == 1` instead.

Before Start:
1. `BS_APU_ON` / `BS_APU_DWELL` / `BS_APU_START` — as today, each gaining
   `Skip(…, s => s.IsApuRunning())` so a re-run never re-commands a running APU.
2. `BS_APU_WAIT` (selector spring-back, starter cutout) — keep, upgrade to **Stop**, 90 s.
   Naturally re-run-safe: with the APU already started the selector reads ON → passes
   instantly. Early progress checkpoint for the pilot.
3. **New generator gate** `BS_APU_GEN_AVAIL`:
   `WaitForField("Waiting for the APU generator", "ELEC_annunAPU_GEN_OFF_BUS", v => v > 0.5, 30, onTimeout: Skip)`
   — no SkipCondition. The blue GEN OFF BUS light illuminates only when the generator is up
   and able to take a bus, so in the normal case this is the true availability gate the
   transfer waits on. It is deliberately **Skip**, not Stop, because "light off" is
   ambiguous: it also reads 0 when the APU is *already powering the buses* (flow re-run
   after a completed transfer), and no stateless NG3 signal separates the two at the moment
   the step is reached (a `running && light off` SkipCondition was considered and rejected —
   that is exactly the state a fresh start is in right after starter cutout, so it would
   skip the gate in the normal case). Consequences: fresh start waits for the light
   (normally seconds after cutout); post-transfer re-run costs one announced 30 s timeout
   then continues through idempotent no-ops; the pathological "running but generator never
   available" fault (not spontaneously modelled by PMDG) gets an announced timeout before
   proceeding — the Stop-gated selector wait in step 2 has already proven a running APU by
   then. Test plan measures the real cutout→light gap to validate the 30 s budget.
4. `BS_APUGEN` transfer-button press — only now (unchanged content).
5. `BS_GPU_OFF` — unchanged (skip-gated on `!IsGpuOn()`).

After Landing: unchanged (starts the APU, doesn't wait — Shutdown's transfer happens minutes
later and the 737 After Landing was already judged fine).

### Fenix A320 (`FenixFlowDefinitions.cs`)

- `BS_APU_AVAIL` gains `onTimeout: FlowStepFailurePolicy.Stop`. One-line change; the AVAIL
  detection is already correct and readable. After Landing (`AL_APU_AVAIL`) stays Skip.

### FBW A380 (`FbwA380FlowDefinitions.cs` + `FbwA380ChecklistDefinitions.cs`)

Before Start APU block becomes (each step skip-gated on
`s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")` unless noted):
1. `BS_APU` — APU master ON (existing; skip stays master-based OR becomes AVAIL-based —
   master-based is fine since re-setting 1 is a no-op; keep existing skip).
2. `BS_APU_DWELL` — `Skip(Wait("Waiting before APU start", 3), s => AVAIL)` (flap/ECB dwell,
   Fenix pattern).
3. `BS_APU_START` — **new**: `SW("APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1)`, skip
   when AVAIL. Level calc write via the existing `A32NX_OVHD_` catch-all.
4. `BS_APU_AVAIL` — **new**:
   `WaitForField("Waiting for APU available", "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180, onTimeout: Stop)`.
5. `BS_APU_START_OFF` — **new, defensive**: `SW("A32NX_OVHD_APU_START_PB_IS_ON", 0)`, skip
   when the PB already reads 0 (needs `A32NX_OVHD_APU_START_PB_IS_ON` added to
   `FbwA380StateEvaluator.PollFields` for the skip to read it — it is Continuous-registered
   so it may already be cached; adding to PollFields is harmless belt-and-braces).
   Rationale: no repo evidence FBW auto-clears the latched PB, and a stale latched 1 could
   surprise-start the APU on a later master-ON. Silent-skip when FBW did clear it.
6. `BS_APUBLEED` (existing) — stays after the AVAIL wait.
7. `BS_GPU_OFF` (EXT PWR ×4 off) + Captain EFB-disconnect reminder — now AFTER the AVAIL
   wait (they currently run before any of this).
   Fuel pumps / beacon order relative to EXT PWR off: keep current relative order, just
   ensure everything sits downstream of the AVAIL gate.

After Landing (`AL_APU`): same block with **Skip**-policy AVAIL wait (Fenix parity), each
step skip-gated on AVAIL. Defensive START clear included.

Checklist tick actions (`BS_APU` :184, `AL_APU` :404): become
`master ON → await Task.Delay(3000) → START = 1` inside the async `CheckAction` lambda.
No inline AVAIL wait (checklists are pilot-paced; auto-detect condition stays master-based).

After Start flow (`AS_APU_OFF`) is unchanged — with the defensive START clear at the end of
each start block, master-off never coexists with a latched START=1.

## Announcements

No new announcement mechanisms. All speech comes from existing FlowManager paths:
"Waiting for: …", "Timed out waiting for: …", "{Flow} flow stopped. Unable to complete: …",
"Already set: …", "Skipping: …". Step labels above are the spoken text — keep them short and
concrete.

## Out of scope

- A380 Parking flow gating (pilot-triggered; see decision 4).
- 737/777 checklist APU items (no defect identified).
- Fenix After Landing (already correct).
- Any live SimConnect MCP probing (deferred to in-sim testing).

## Verification (in-sim, human)

Add a test-plan section per aircraft (append to the existing FO test-plan docs where one
exists; the 777 shares `docs/pmdg-737-first-officer-test-plan.md` conventions):
1. **Happy path**: cold & dark → power-up flow → Before Start; confirm the APU wait
   announces, the transfer/GPU-disconnect happens only after the aircraft's real
   availability signal, and the flow completes.
2. **Re-run idempotence**: immediately re-run Before Start with the APU on line; confirm
   no false abort, no re-commanded APU, "Already set"/skip behaviour throughout.
3. **737 EGT threshold**: read `APU_EGTNeedle` cold and running; confirm 100 separates them
   (tune the const if not). Also time starter cutout (selector spring-back) → blue
   GEN OFF BUS light; confirm it fits the 30 s budget of `BS_APU_GEN_AVAIL`.
4. **A380 specifics**: confirm the START press actually spools the APU from master-only
   state; observe whether FBW auto-clears the START PB (informs the defensive-clear skip);
   confirm EXT PWR drop happens only after "AVAIL".
5. **Timeout path**: verified by review (identical mechanism to the shipped 737
   engine-start Stop waits); optionally force one in-sim (e.g. A380 with APU fire pb pushed)
   if convenient.
