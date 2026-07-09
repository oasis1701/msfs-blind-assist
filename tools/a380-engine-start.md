# FlyByWire A380X (842) — Engine Start & Readout Control Reference

Investigated against the local FBW clone (`C:\Users\franc\Documents\development\fbw-aircraft`).
All paths below are absolute. Engines are numbered 1-4 (left-outboard 1 ... right-outboard 4).

> **⚠️ Path update (MSFS-2024-native modular rebuild, FBW #10758, 2026-06):** the cockpit
> behaviour XMLs moved out of the old `SimObjects\AirPlanes\FlyByWire_A380_842\model\behaviour\`
> tree into `SimObjects\AirPlanes\FlyByWire_A380X\attachments\flybywire\Part_Interior_Cockpit\model\behaviour\`.
> When a path below shows `FlyByWire_A380_842\model\behaviour\...`, read it as the new
> `FlyByWire_A380X\...\Part_Interior_Cockpit\model\behaviour\...`. (`runway.FLT` and other
> non-behaviour assets were redistributed by the modular split — locate them under the
> `FlyByWire_A380X` SimObject if the old path 404s.)

The A380X reuses the A32NX `A32NX_*` naming convention for engine L:vars (the engine
WASM module is shared logic), so the var families look "A32NX" but apply to all 4 engines.

---

## 1. ENG MASTER 1-4 ON/OFF mechanism

Same mechanism as the A320 — the master switch drives the **MSFS fuel-system valve** per engine,
and the FADEC reacts to the resulting `GENERAL ENG STARTER` state.

**To turn an ENG MASTER ON:**  send K event `FUELSYSTEM_VALVE_OPEN` with `EventParam = {N}` (N = valve id = engine number 1-4).
**To turn an ENG MASTER OFF:** send K event `FUELSYSTEM_VALVE_CLOSE` with `EventParam = {N}`.

The switch behaviour also toggles the starter (`K:TOGGLE_STARTER{N}`) to keep
`A:GENERAL ENG STARTER:{N}` consistent with the valve, but from an external app you only need
to drive the valve open/close — the model's Update loop reconciles the starter automatically.

Readback of current master/valve state: `A:FUELSYSTEM VALVE SWITCH:{N}` (Bool) or `A:GENERAL ENG STARTER:{N}` (Bool).

Source — template definition (the exact K events):
`...\SimObjects\AirPlanes\FlyByWire_A380_842\model\behaviour\legacy\generated\A32NX_Interior_Misc.xml`
lines 311-355, Template `FBW_ENGINE_Switch_Master_Template`:
```
LEFT_SINGLE_CODE:
  (A:FUELSYSTEM VALVE SWITCH:#VALVE_ID#, Bool) if{
      #VALVE_ID# (>K:FUELSYSTEM_VALVE_CLOSE)
      ... (>K:TOGGLE_STARTER#ID#) ...
  } els{
      #VALVE_ID# (>K:FUELSYSTEM_VALVE_OPEN)
      ... (>K:TOGGLE_STARTER#ID#) ...
  }
```
Source — switch nodes mapping VALVE_ID = ID = engine number 1-4:
`...\model\behaviour\pedestal\pedestal.xml` lines 94-149
(`SWITCH_ENGINES_ENG1..4`, each `<ID>=<VALVE_ID>` = 1..4).

---

## 2. Engine MODE selector (CRANK / NORM / IGN-START)

Pedestal knob `KNOB_ENGINES_MODE` (`ENGINE_COUNT = 4`), 3-position rotary.
Uses template `A32NX_ENGINE_MODE_SELECTOR_TEMPLATE`.

Positions / values (TURB ENG IGNITION SWITCH enum):
- **0 = CRANK**  (`ANIMTIP_0` = SET_CRANK)
- **1 = NORM**   (`ANIMTIP_1` = SET_NORM)
- **2 = IGN/START** (`ANIMTIP_2` = SET_IGN_START)

**To set the mode from an external app:** send K event `TURBINE_IGNITION_SWITCH_SET{N}`
with `EventParam = 0|1|2` for **each** engine N=1..4 (the A380 template fans the single knob
out to all 4 engines — it issues `TURBINE_IGNITION_SWITCH_SET1..4`).
Readback per engine: `A:TURB ENG IGNITION SWITCH EX1:{N}` (Enum 0/1/2).

A shared cockpit L:var also tracks knob position: `L:XMLVAR_ENG_MODE_SEL` (0/1/2)
(`SWITCH_POSITION_VAR` in the subtemplate). The authoritative per-engine value the FADEC
reads is the igniter enum above, set by `TURBINE_IGNITION_SWITCH_SET{N}`.

Source:
`...\model\behaviour\legacy\Airbus.xml` lines 703-769
(Template `A32NX_ENGINE_MODE_SELECTOR_TEMPLATE` / `_SUBTEMPLATE`):
```
CODE_POS_1: 1 (>K:TURBINE_IGNITION_SWITCH_SET#ENGINE_CURRENT#)   <- NORM
CODE_POS_2: 2 (>K:TURBINE_IGNITION_SWITCH_SET#ENGINE_CURRENT#)   <- IGN/START
SWITCH_POSITION_VAR: XMLVAR_ENG_MODE_SEL
STATEx_TEST: (A:TURB ENG IGNITION SWITCH EX1:#ENGINE_CURRENT#, Enum) x ==
```
Pedestal instantiation with `ENGINE_COUNT=4`:
`...\model\behaviour\pedestal\pedestal.xml` lines 167-183.

### Start sequence (how FADEC actually starts an engine)
FADEC state machine reads `engineIgniter` (= TURB ENG IGNITION SWITCH EX1 enum) and
`engineStarter` (= GENERAL ENG STARTER). To start: mode selector to **IGN/START (2)**, then
open the fuel valve / set master ON (drives starter on). State machine: `igniter==2 && starter` -> STARTING -> ON.
Source: `...\src\wasm\fadec_a380x\src\Fadec\EngineControl_A380X.cpp` lines 60-90, 315-390
(`engineStateMachine`); simvar bindings `GENERAL ENG STARTER:1..4` at
`...\src\wasm\fadec_a380x\src\Fadec\FadecSimData_A380X.hpp` lines 175-178.

---

## 3. Manual-start pushbuttons & FADEC

- Manual start P/B (one per engine, OVHD): write/read `L:A32NX_ENGMANSTART{1..4}_TOGGLE` (Bool, 0/1).
  There is also a guard-lock var `L:A32NX_ENGMANSTART{1..4}LOCK_TOGGLE` and an ALTN button
  `PUSH_OVHD_ENGMANSTARTALTN`.
  Source: `...\model\A380_COCKPIT.xml` lines 3590-3651 (`TOGGLE_SIMVAR = L:A32NX_ENGMANSTART{N}_TOGGLE`);
  defaults in `...\SimObjects\AirPlanes\FlyByWire_A380_842\runway.FLT` lines 316-323.

- **FADEC power status:** `L:A32NX_OVHD_FADEC_{ENG}` (ENG = 1,2,3,4) — powered state of each
  engine's FADEC, set by the OVHD FADEC pushbutton.
  Source: `...\fbw-a380x\docs\a380-simvars.md` lines 1288-1291 ("Engines ATA 70").

---

## 4. Per-engine readable parameter L:vars (engines 1-4)

All written by the A380X FADEC WASM module as `AUTO_READ_WRITE` named vars, indexed `:1` .. `:4`.
Source (authoritative): `...\src\wasm\fadec_a380x\src\Fadec\FadecSimData_A380X.hpp` lines 358-450.

| Parameter            | L:var family                  | Index | Notes |
|----------------------|-------------------------------|-------|-------|
| Engine state         | `A32NX_ENGINE_STATE:{1-4}`    | 1-4   | 0=OFF,1=ON,2=STARTING,3=SHUTTING,4=RESTARTING (per state machine) |
| N1 (%)               | `A32NX_ENGINE_N1:{1-4}`       | 1-4   | confirmed in EWD publisher line 80 |
| N2 (%)               | `A32NX_ENGINE_N2:{1-4}`       | 1-4   | |
| **N3 (%)**           | `A32NX_ENGINE_N3:{1-4}`       | 1-4   | EXISTS on A380 (3-spool Trent). N3 is the primary spool the FADEC tracks for start. |
| EGT (deg C)          | `A32NX_ENGINE_EGT:{1-4}`      | 1-4   | confirmed in EWD publisher line 79 |
| Fuel flow (kg/h)     | `A32NX_ENGINE_FF:{1-4}`       | 1-4   | also `A32NX_ENGINE_PRE_FF:{1-4}` |
| Fuel used (kg)       | `A32NX_FUEL_USED:{1-4}`       | 1-4   | note: family is `A32NX_FUEL_USED`, not `ENGINE_FUEL_USED` |
| Oil quantity         | `A32NX_ENGINE_OIL_QTY:{1-4}`  | 1-4   | plus `A32NX_ENGINE_OIL_TOTAL:{1-4}` |
| Start timer          | `A32NX_ENGINE_TIMER:{1-4}`    | 1-4   | |
| TLA (throttle angle) | `A32NX_AUTOTHRUST_TLA:{1-4}`  | 1-4   | thrust-lever angle, degrees |

### Oil TEMP / PRESS — NOT FBW L:vars
The A380X SD pages read oil temperature and pressure from **native MSFS A:vars**, not FBW L:vars:
- Oil pressure: `A:ENG OIL PRESSURE:{N}` (psi)
- Oil temperature: `A:GENERAL ENG OIL TEMPERATURE:{N}` (celsius)  [code-marked TODO]
Source: `...\src\systems\instruments\src\SD\Pages\Engine\elements\OilPressureGauge.tsx` line 13;
`...\SD\Pages\Engine\elements\EngineColumn.tsx` line 40.

---

## Uncertainties / flags

- **EGT/FF units:** L:vars hold the FBW-computed value (EGT deg C, FF kg/h). Verify scaling against
  the EWD page formatting if exact units matter; only N1 and EGT are explicitly confirmed via the
  EWD publisher (`EwdSimvarPublisher.tsx` lines 79-80). N2/N3/FF are confirmed to exist in the
  FADEC source but their consumers weren't individually traced.
- **Oil temp/press** are native A:vars (not L:vars) and the temp source is marked TODO in FBW code,
  so it may be the raw MSFS value rather than a modeled one.
- **MODE knob via single event:** there is no single "A380 mode selector" event; the model issues
  `TURBINE_IGNITION_SWITCH_SET{N}` for N=1..4. To replicate the knob from an external app, send the
  same value to all four engines. `L:XMLVAR_ENG_MODE_SEL` reflects knob position but writing it alone
  does NOT drive the FADEC — the per-engine igniter enum does.
- **Start from external app — recommended sequence:** (1) FADEC powered (`L:A32NX_OVHD_FADEC_{N}`),
  (2) set mode IGN/START: `TURBINE_IGNITION_SWITCH_SET{N}` param 2, (3) master ON:
  `FUELSYSTEM_VALVE_OPEN` param {N}. This was inferred from the FADEC state machine, not run in-sim — verify live.
