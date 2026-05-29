# A380X Definition — Correctness Audit

Cross-check of every var/event registered in
`MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs` against the FBW A380X
docs (`a380-simvars.md`, flight-deck-api, systems-api), the A32NX systems-api,
and the local FBW source (`fbw-a380x/src/**`). The existing reference notes
(a380-simvars-catalog.md, a380-fcu-vars.md, a380-engine-start.md,
a380-fault-status-vars.md, a380-sd-pages.md, a380-doc-final-gap.md) were used
as the first pass and re-verified against source for the high-risk items.

Verification axes: (1) name spelled right / exists; (2) ReadEnum/Sel value→label
mapping matches the source enum; (3) units; (4) event names exist.

---

## (A) WRONG — definite errors, with the exact fix

### A1. ND Mode var has the wrong prefix
`A380X_EFIS_{L|R}_ND_MODE` does **not exist**. The cockpit behaviour XML and
the fbw-sdk publisher write/read `A32NX_EFIS_{side}_ND_MODE`
(`legacy/AirlinerCommon.xml`; `A380X_` form found nowhere in the tree).
- `A380X_EFIS_L_ND_MODE` -> `A32NX_EFIS_L_ND_MODE`
- `A380X_EFIS_R_ND_MODE` -> `A32NX_EFIS_R_ND_MODE`
(Enum 0=Rose ILS,1=Rose VOR,2=Rose Nav,3=Arc,4=Plan is correct.)

### A2. ND Range var has the wrong prefix
Same as A1 — the var is `A32NX_EFIS_{side}_ND_RANGE`, not `A380X_`.
- `A380X_EFIS_L_ND_RANGE` -> `A32NX_EFIS_L_ND_RANGE`
- `A380X_EFIS_R_ND_RANGE` -> `A32NX_EFIS_R_ND_RANGE`

### A3. EFIS Navaid 1/2 var name is wrong (and wrong prefix)
Registered as `A380X_EFIS_{side}_NAVAID_{n}_BUTTON_IS_ON`; the real var is
`A32NX_EFIS_{side}_NAVAID_{n}_MODE` (efis-cp.xml: `A32NX_EFIS_#SIDE#_NAVAID_1_MODE`).
The `_BUTTON_IS_ON` form does not exist. Enum 0=Off,1=ADF,2=VOR is correct.
- `A380X_EFIS_L_NAVAID_1_BUTTON_IS_ON` -> `A32NX_EFIS_L_NAVAID_1_MODE`
- `A380X_EFIS_L_NAVAID_2_BUTTON_IS_ON` -> `A32NX_EFIS_L_NAVAID_2_MODE`
- `A380X_EFIS_R_NAVAID_1_BUTTON_IS_ON` -> `A32NX_EFIS_R_NAVAID_1_MODE`
- `A380X_EFIS_R_NAVAID_2_BUTTON_IS_ON` -> `A32NX_EFIS_R_NAVAID_2_MODE`

### A4. Engine state enum: values 3 and 4 are swapped
FADEC source `EngineControl_A380X.h` enum: `OFF=0, ON=1, STARTING=2,
RESTARTING=3, SHUTTING=4`. The def maps `[3]="Shutting Down", [4]="Restarting"`.
- `A32NX_ENGINE_STATE:{n}`: wrong enum, should be `[3]="Restarting", [4]="Shutting Down"`.

### A5. Predictive Windshear (PWS) enum reversed
`pedestal.xml` SWITCH_RADAR_PWS: `ANIMTIP_0 = AUTO`, `ANIMTIP_1 = OFF`. The def
maps `[0]="Off", [1]="Auto"`.
- `A32NX_SWITCH_RADAR_PWS_Position`: wrong enum, should be `[0]="Auto", [1]="Off"`.

### A6. EFIS baro-unit var does not exist on the A380
`A32NX_FCU_EFIS_{L|R}_BARO_IS_INHG` is an A32NX-only var; it is absent from the
entire A380 tree. The A380 unit selector is `XMLVAR_Baro_Selector_HPA_{1|2}`
(BaroManager.ts), with **inverted sense** (1=hPa, 0=inHg) — so a literal port
must flip the labels too.
- `A32NX_FCU_EFIS_L_BARO_IS_INHG` -> `XMLVAR_Baro_Selector_HPA_1` (Sel: `[0]="inHg", [1]="hPa"`)
- `A32NX_FCU_EFIS_R_BARO_IS_INHG` -> `XMLVAR_Baro_Selector_HPA_2` (Sel: `[0]="inHg", [1]="hPa"`)

### A7. Battery Display Selector knob enum is wrong
Catalog/simvars doc: `A380X_OVHD_ELEC_BAT_SELECTOR_KNOB` = `0=ESS, 1=APU,
2=OFF, 3=BAT1, 4=BAT2`. The def maps `[0]="Off",[1]="APU",[2]="Off (2)",
[3]="Battery 1",[4]="Battery 2"` (position 0 mislabeled, position 2 mislabeled).
- `A380X_OVHD_ELEC_BAT_SELECTOR_KNOB`: wrong enum, should be `[0]="ESS",[1]="APU",[2]="Off",[3]="Battery 1",[4]="Battery 2"`.

### A8. Wiper speed-set event is unindexed in the comment but must carry circuit id
The wiper combo is written via `HandleUIVariableSet`. The model issues
`K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET` with the **circuit id as the
parameter** (`A32NX_Interior_Misc.xml`: `#CIRCUIT_ID# (>K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET)`),
i.e. it is the indexed `:2:` event variant, param = 141 (left) / 143 (right),
value 0/75/100 set separately. The registration itself (`WIPER_LEFT/RIGHT` combo
+ `CIRCUIT SWITCH ON:141/143` readback) is correct; the fix is to ensure the
write path uses event id `ELECTRICAL_CIRCUIT_POWER_SETTING_SET` with the
`:2:` (indexed) form and circuit-id param 141/143 — confirm `HandleUIVariableSet`
sends `141`/`143` as the parameter, not as the `:n` event suffix. (Flagging here
because the file comment "ELECTRICAL_CIRCUIT_POWER_SETTING_SET:141/143" implies
the colon-suffix form; the model uses the `:2:` masked form + param.)

---

## (B) UNCERTAIN — plausible but unconfirmed; verify in-sim

1. **`A380X_EFIS_CP_BARO_PUSH_{1,2}` / `_PULL_{1,2}` (H-events)** — inferred from
   the FBW input catalog + local BaroManager behaviour, but NOT present in
   `a380x-input-events.md`. Exact token and whether a `_SET_` form exists is
   unconfirmed (a380-fcu-vars.md flags this). Verify the spelling drives the knob.

2. **`A32NX.FCU_TO_AP_HDG_PUSH/PULL` and `A32NX.FCU_TO_AP_VS_PULL`** — the in-sim
   FCU managers fire the `_TO_AP_` variants, but the public API documents the
   plain `A32NX.FCU_HDG_PUSH/PULL` / `A32NX.FCU_VS_PULL`. Both likely work;
   confirm the autoflight reacts to the `_TO_AP_` form actually registered.

3. **`A32NX_EVAC_CAPT_TOGGLE` labels** — cockpit comment reads "CAPT & PURS /
   CAPT" (a CAPT vs CAPT&PURS selector). The def labels `[0]="Purser",
   [1]="Capt and Purser"`. Position 0 is probably "Capt" (not "Purser"); the
   real A380 EVAC command selector positions are CAPT / CAPT & PURS. Verify
   which value is which and relabel (likely `[0]="Capt", [1]="Capt and Purser"`).

4. **`A32NX_EFIS_{L|R}_OANS_RANGE` enum labels** — code drives the value 0..4
   with 4 = default/largest (FLT files seed 4; the knob `--`/`++` walks it and
   clamps to 4). The def labels `[0]="Max",[1]="1",[2]="2",[3]="3",[4]="Min"`.
   The numeric→physical-NM meaning is not pinned down in source; confirm the
   0↔Max / 4↔Min direction (it may be inverted).

5. **`A380X_EFIS_{side}_ACTIVE_OVERLAY` labels** — efis-cp.xml toggles values
   {0,1,2}; preset XML uses 0 and 2; the simvars doc is itself inconsistent
   ("0=WX/OFF,1=TERR" vs "0=OFF,1=WX,2=TERR"). The def uses
   `[0]="Off",[1]="Weather",[2]="Terrain"`. Plausible but confirm 1=WX vs TERR
   in-sim (overlay button code `==1` / `==2 ? 2*` suggests WX=1, TERR=2 — likely
   correct, but doc disagreement warrants a check).

6. **`A32NX_APU_N_RAW`** ("APU N", percent) — used as the plain APU N readout
   because `A32NX_APU_N`/`_N2` are ARINC429. `_N_RAW` is asserted in
   a380-fault-status-vars.md but not directly confirmed in source this pass;
   verify it reads a sane 0–100.

7. **`A380X_EFIS_{side}_BARO_PRESELECTED`** units — registered as a plain Read
   with no unit; value is hPa OR inHg depending on unit selector and 0 when not
   shown (simvars doc: "Not for systems use"). Confirm it reads usefully.

8. **`FBW_RMP_FREQUENCY_ACTIVE/STANDBY_{n}`** — BCD32-encoded per the catalog;
   registered as plain Read. May need BCD decoding to display a real MHz value
   rather than a raw integer. Verify the displayed value.

9. **`A32NX_FM1_MINIMUM_DESCENT_ALTITUDE` / `A32NX_FM1_DECISION_HEIGHT`** —
   ported from A32NX FM; not located in the A380 source this pass. Marked
   best-effort in the file. Verify they exist/populate on the A380.

---

## (C) OK

Count: **~410** registered names verified correct (names exist for the A380 as
A380X_/A32NX_/FBW_/stock, enums match source, units sane). Not listed per the
brief. Spot-confirmed against source this pass: ENG MASTER fuel-valve events
(`FUELSYSTEM_VALVE_OPEN/CLOSE` + param), `TURBINE_IGNITION_SWITCH_SET{n}` fan-out,
engine-mode `XMLVAR_ENG_MODE_SEL`, `A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION`
(0=Close,1=Auto,2=Open), `A32NX_AUTOBRAKES_SELECTED_MODE` (0=Disarm,1=BTV,2=Low,
3=L2,4=L3,5=High) + ARMED_MODE (+6=RTO), `A32NX_OVHD_INTLT_ANN` /
`A380X_OVHD_ANN_LT_POSITION` (0=Test,1=Bright,2=Dim — source tests ==0 for TEST;
default 1), `A32NX_COND_FDAC_{n}_CHANNEL_{ch}_FAILURE`, `A32NX_COND_TADD_*`
(present though not registered), wiper circuits 141/143, APU squib
`A32NX_OVHD_FIRE_SQUIB_1_APU_1_IS_DISCHARGED`, `A380X_RMP_{n}_STATE`
(0=OffFailed,1=OffStandby,2=On,3=OnFailed), SD page index map, all ELEC
bus/fault/voltage vars, all FCU value/managed/mode readout vars per
a380-fcu-vars.md, COM swap/standby stock events.
