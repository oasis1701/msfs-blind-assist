# PMDG 737 First Officer: set pressurization FLT ALT / LAND ALT from SimBrief

**Date:** 2026-07-01
**Branch:** `feature/777-first-officer` (depends on the unmerged FO subsystem there)
**Follows:** PR #120 (accessible Flight Altitude / Landing Altitude pressurization controls)

## Goal

The 737 First Officer sets the pressurization panel's flight altitude (SimBrief cruise)
and landing altitude (SimBrief destination field elevation) itself, instead of leaving
them as captain reminders. Descent auto-verifies the landing altitude. Loose-end
checklist items with readable state become auto-detecting.

**Scope decisions (user-confirmed):**
- **777: untouched.** Its pressurization is automatic (FMC landing altitude); the SDK
  exposes no landing-altitude value and no direct-set event, so manual-knob dialing
  would be blind ticks — out of scope.
- **Descent: auto-verify only.** No new flow step; LAND ALT is set at preflight and
  only verified in descent, matching real 737 procedure.

## 1. Data path (SimBrief → evaluator)

- `IFoStateEvaluator` gains `void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt)`.
- `FirstOfficerForm.ApplyFlightPlanThresholds` (already pushes transition alt + takeoff
  flaps) parses `ofp.InitialAltitude` → cruise and `ofp.DestElevation` → landing
  elevation. Each parses independently; an unparseable field stays null.
- The **777 evaluator implements it as a no-op** (the `SetEngineN2` precedent).
- The **737 evaluator rounds and clamps at storage time** so every consumer reads one
  consistent value equal to what the cockpit window will show:
  - Flight altitude: nearest 500 ft, clamped 0–42,000.
  - Landing altitude: nearest 50 ft, clamped 0–14,000.
  - (PR #120's panel path rounds DOWN to mirror the knob; here nearest-rounding happens
    at storage, so the value sent is already a step multiple and the event-side
    round-down is a no-op — the two paths cannot disagree.)

## 2. Flow engine: `TargetValueProvider`

- `FlowStep<TState>` gains optional `Func<TState, int?> TargetValueProvider`.
- In `FlowManager.ExecuteStepAsync`'s `SetSwitch` case, a non-null provider is resolved
  against `_state` immediately before dispatch:
  - **Value** → sent as the event parameter via the existing `Simple` dispatch (which
    already passes arbitrary int targets; no executor change needed on the flow path).
  - **Null** (no SimBrief) → the step is **quietly skipped**: no announcement (the
    "Already set:" wording would be wrong), `StepCompleted` semantics as a skip, flow
    continues. The fallback reminder step (below) does the talking.

## 3. Preflight flow

Replace `Captain("PF_PRESS", "Set flight and landing altitudes on the pressurization panel.")`
with three steps at the same position:

1. **"Flight altitude: set"** — `EVT_OH_PRESS_FLT_ALT_SET`, `TargetValueProvider` =
   planned cruise; `SkipCondition` = FLT ALT window already matches the plan
   (announces "Already set: …", which is correct for that case).
2. **"Landing altitude: set"** — `EVT_OH_PRESS_LAND_ALT_SET`, provider = destination
   elevation, same shape. Standard 350 ms `PostActionDelayMs` keeps the two CDA writes
   in separate sim frames (same-frame coalescing rule).
3. **Captain reminder fallback** — today's wording, `SkipCondition` = at least one
   planned value available (it fires only when BOTH are missing, per §5). Without
   SimBrief, flow behavior is identical to today.

PR #120's background monitors (`AIR_FltAltWindow` / `AIR_LandAltWindow`) announce the
resulting values ("Flight altitude 36000 feet") automatically — no new announcement code.

## 4. Checklists

Auto-detect via **synthetic evaluator fields** (the `FO_ENG1_N2` precedent), computed in
the 737 `AircraftStateEvaluator.GetValue`:

- `FO_PRESS_ALTS_MATCH` — 1 when a plan exists AND both windows match the stored plan
  within one knob step (FLT ±500, LAND ±50); else 0.
- `FO_PRESS_LAND_ALT_MATCH` — 1 when the landing window alone matches; else 0.

Item changes (all in `PMDG737ChecklistDefinitions`):

| Item | Today | Becomes |
|---|---|---|
| `PF_PRESS` (PREFLIGHT) | Reminder | `AutoAsync`: tick fires a new executor convenience `SetPressurizationAltitudesAsync(state)` (both sets, spaced writes behind the dispatch gate — `FireSpacedAsync` pattern); auto-ticks off `FO_PRESS_ALTS_MATCH`. No plan → degrades to manual-tick reminder (action no-ops). |
| `DC_PRESS` (DESCENT_CL) | Reminder | Action-free `Auto` off `FO_PRESS_LAND_ALT_MATCH` (preserves the `*_CL` action-free invariant; manual tick still allowed). |
| `PFC_PRESS` (PREFLIGHT_CL) | Reminder | Action-free `Auto` off `AIR_PressurizationModeSelector == 0` (AUTO) — the state was always readable. |

`RevertBehavior` for the converted items follows their group siblings.

## 5. Error handling

- Unparseable SimBrief field → that altitude stays null → its flow step quietly skips;
  the captain fallback fires only when **both** values are missing (partial-plan edge is
  negligible; whatever was available still gets set).
- No SimConnect / executor unavailable → existing flow failure paths apply unchanged.

## 6. Verification

- No automated test project (SimConnect-driven app). Build `MSFSBlindAssist.sln` x64.
- Add **Part H** to `docs/pmdg-737-first-officer-test-plan.md`: SimBrief-loaded preflight
  flow sets both values (spoken window announcements match the OFP), no-SimBrief fallback
  reminder, checklist tick-fires-set, preflight auto-tick, descent auto-verify,
  PFC_PRESS mode-selector auto-tick.
- Update CLAUDE.md's First Officer section: new synthetic keys, `TargetValueProvider`
  quiet-skip semantics, the "descent verifies, preflight sets" rule, 777 untouched.
