# First Officer shutdown/secure tightening — design

**Date:** 2026-07-13
**Branch:** `feature/first-officer`
**Scope:** First Officer flows + checklists only (`MSFSBlindAssist/FirstOfficer/`). No executor, state-evaluator, or panel-control changes — every event / L:var / actuation pattern required already exists (the "off" patterns are already used in each aircraft's Before-Start flow).

## Motivation

Three user requests, applied consistently across aircraft:

1. **Transponder is set to standby too early.** Today several aircraft set XPDR → standby in the *After Landing* flow/checklist. The user wants that deferred to the *Shutdown* flow (transponder should keep replying while taxiing to the gate).
2. **Airbus LS switches are never turned off.** The A320/Fenix turn the EFIS **LS** (landing-system) pushbuttons ON at approach but never OFF. They should be switched OFF at *Shutdown*.
3. **Secure should fully power down.** The *Secure* phase should turn off **ground power** and the **APU**. Some aircraft already do this; others don't.

## Current state (as surveyed)

| Aircraft | XPDR→STBY today | LS turned off? | Secure: ext-pwr off? | Secure: APU off? |
|---|---|---|---|---|
| PMDG 777 | After Landing **and** Shutdown (duplicated) | n/a (Boeing) | ❌ | ✅ (`SEC_APU_OFF`) |
| PMDG 737 | Shutdown only ✅ | n/a | ❌ | ❌ (only a manual `EPD_PWR` reminder) |
| FBW A320 | After Landing (XPDR + TCAS) | ❌ (LS on at approach, never off) | ✅ (`SC_EXTPWR_OFF`) | ✅ (`SC_APUMASTER`) |
| Fenix A320 | After Landing (XPDR + TCAS) | ❌ | ✅ (`SC_EXTPWR_OFF`) | ✅ (`SC_APUMASTER`) |
| FBW A380 | *reminders only* — After Landing (`AL_TCAS`) + Parking (`PK_XPDR`/`PK_TCAS`) | ❌ (uses EFIS-mode, no LS button in flow) | ❌ | ❌ (Parking keeps APU on) |

Structural notes:
- **A380** has no Shutdown/Secure flow — **Parking** is its terminal flow, and its XPDR/TCAS are captain *reminders*, not automated actions.
- **A320/Fenix** already satisfy request 3.
- No characterization tests currently assert on any of these flow/checklist definitions.

## Decisions (confirmed with user)

- **A380 scope: minimal.** Add ground-power-off + APU-off to the A380 **Parking** flow; remove the redundant After-Landing `AL_TCAS` reminder; **no** LS step (A380 uses EFIS-mode).
- **Checklists kept in sync** with every flow change (the `Auto` checklist items also actuate, so leaving them behind would contradict the flow).
- **737 `EPD_PWR` reminder: removed** once Secure automates APU + ground-power off.

## Changes

### Change 1 — Transponder → standby: After Landing → Shutdown

- **PMDG 777** — delete the After-Landing transponder step from **both** the flow (`AL_XPNDR_STBY`, `SW(... "EVT_TCAS_MODE", 0)`) and the checklist (`AL_TRANSPONDER`, `SetTransponderMode(0)`). Shutdown already sets it (`SD_XPNDR_STBY` flow + `SD_XPNDR_STBY` checklist). Net: remove 2 after-landing steps, add nothing.
- **PMDG 737** — no change (already Shutdown-only: `SD_XPDR`).
- **FBW A320** — move from After-Landing flow to Shutdown flow: `AL_XPDR_STBY` (`A32NX_TRANSPONDER_MODE`→0) and its paired `AL_TCAS_STBY` (`A32NX_SWITCH_TCAS_POSITION`→0). Move the mirror checklist item `AL_XPDR_STBY` (`A32NX_TRANSPONDER_MODE`→0) from the AFTER_LANDING group to SHUTDOWN. Rename the moved ids to `SD_XPDR_STBY` / `SD_TCAS_STBY` so the flow step id, `CompletesChecklistItemId`, and checklist item id stay aligned and read cleanly under the Shutdown group. Place near the transponder-relevant end of Shutdown (after engine masters off, alongside the other cockpit-cleanup items).
- **Fenix A320** — identical shape to the A320 with Fenix vars: flow `AL_XPDR_STBY` (`S_XPDR_OPERATION`→0) + `AL_TCAS_STBY` (`S_XPDR_MODE`→0) move to Shutdown; checklist `AL_XPDR_STBY` (`S_XPDR_OPERATION`→0) moves AFTER_LANDING → SHUTDOWN; rename to `SD_`-prefixed ids.
- **FBW A380** — remove the After-Landing `AL_TCAS` captain reminder from the flow (and its mirror checklist item in `AFTER_LANDING_CL`, if present). Parking's `PK_XPDR`/`PK_TCAS` reminders are unchanged.

### Change 2 — Airbus LS switches OFF at Shutdown

- **FBW A320** — add to Shutdown flow + Shutdown checklist:
  - `SD_LS1` "LS captain: OFF" → `A32NX_EFIS_L_LS_BUTTON_IS_ON` = 0, skip-if-already-0.
  - `SD_LS2` "LS first officer: OFF" → `A32NX_EFIS_R_LS_BUTTON_IS_ON` = 0, skip-if-already-0.
  (Inverse of the existing `AP_LS1`/`AP_LS2` approach items.)
- **Fenix A320** — add to Shutdown flow + checklist, using the pulse actuator (base var, NOT `_PRESS`):
  - `SD_LS1` "LS captain: OFF" → pulse `S_FCU_EFIS1_LS` **only when** `I_FCU_EFIS1_LS` reads on (guard mirrors the approach item's on-guard, inverted).
  - `SD_LS2` "LS first officer: OFF" → pulse `S_FCU_EFIS2_LS` only when `I_FCU_EFIS2_LS` on.
- **A380** — none.

### Change 3 — Secure turns off ground power + APU

- **PMDG 777** — add ground-power-off to the Secure flow + Secure checklist, mirroring the Before-Start pattern (momentary push, per-GPU guard):
  - `SEC_GND_PWR_PRIM` momentary `EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH`, skip-if `!IsGpuPower1On()`.
  - `SEC_GND_PWR_SEC` momentary `EVT_OH_ELEC_GRD_PWR_SEC_SWITCH`, skip-if `!IsGpuPower2On()`.
  APU-off already present (`SEC_APU_OFF`). Order: after APU/battery handling is fine; ground-power push is independent.
- **PMDG 737** — add to the Secure flow + Secure checklist:
  - `SE_APU_OFF` "APU: OFF" → `EVT_OH_LIGHTS_APU_START` = 0 (selector 0=OFF), auto-detect from `APU_Selector` < 0.5.
  - `SE_GND_PWR_OFF` "Ground power: OFF" → `EVT_OH_ELEC_GRD_PWR_SWITCH` = 0, skip-if `!IsGpuOn()` (`FO_GPU_ON` composite).
  Remove the now-redundant `EPD_PWR` reminder from the `ELEC_POWER_DOWN` checklist group. No long blocking APU-cooldown wait (the FO flow must not stall for minutes; matches the 777's short-wait philosophy — no cooldown wait is added here since nothing downstream needs the APU stopped).
- **FBW A320 / Fenix** — already do both (`SC_APUMASTER` + `SC_EXTPWR_OFF`); no change.
- **FBW A380** — append to the **Parking** flow + `PARKING_CL` checklist (at the end, after cockpit-door):
  - `PK_EXTPWR_OFF` "External power: OFF" → all four `A32NX_OVHD_ELEC_EXT_PWR_{1..4}_PB_IS_ON` = 0 (Multi), skip when none are on (mirror the Before-Start off step's guard).
  - `PK_APU_OFF` "APU master: OFF" → `A32NX_OVHD_APU_MASTER_SW_PB_IS_ON` = 0, skip-if-already-0.
  Note: this fully secures the A380 (it falls back to battery — Parking does not turn batteries off). Intentional per the "secure = everything off" request.

## Invariants respected

- **Completion-latch / id alignment:** whenever a checklist item moves groups or is renamed, the flow step `Id`, its `CompletesChecklistItemId`, and the checklist item `Id` are updated together so they stay in agreement (see `first-officer.md`: a completed group with participation is completion-latched — do not desync).
- **Fenix pulses are full press-release** via the pulse dispatch table; LS uses `e.Pulse(...)`, never a direct `_PRESS` write, and is guarded so a pulse only fires when the indicator says the button is on (never toggles it back on).
- **Idempotent "off" steps:** every added off/disconnect step carries a skip-guard so re-running the flow no-ops, and so it does nothing when a GPU was never connected.
- **No new SimConnect definitions** — all events/L:vars confirmed already registered.
- **PMDG auto-flaps / autobrake invariants** untouched (not in scope).

## Testing

- **Pure-logic guardrail tests** (new, `tests/MSFSBlindAssist.Tests`): for each aircraft, assert the transponder-standby step is **absent** from the After-Landing group and **present** in the Shutdown group (A380: assert `AL_TCAS` reminder removed from After Landing). These lock in the intent cheaply and are the only unit-testable surface.
- **Build:** `dotnet build MSFSBlindAssist.sln -c Debug` (solution / x64 — never the bare csproj).
- **Sim-facing (in PR, human-run):** per aircraft — run After Landing and confirm the transponder is **still active** (not STBY); run Shutdown and confirm XPDR → STBY and (Airbus) both LS buttons OFF; run Secure/Parking and confirm APU OFF and ground power OFF (and that steps no-op cleanly when no GPU is connected).

## Implementation approach

Per the "use workflows" request: a workflow with **one agent per aircraft** (PMDG 777, PMDG 737, FBW A320, Fenix A320, FBW A380). Each agent edits only its own aircraft's flow-definition + checklist-definition files as a self-consistent unit; the aircraft are independent of one another (no shared files), so they parallelize cleanly. A final stage adds the guardrail tests, builds the solution, and runs `dotnet test`.

## Out of scope

- Any FMC/CDU programming (permanent user decision).
- A380 LS handling, batteries-off at A380 parking, and any change to the A320/Fenix Secure (already correct).
- Panel controls, executors, state evaluators.
