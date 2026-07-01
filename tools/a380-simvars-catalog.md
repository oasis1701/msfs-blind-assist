# FlyByWire A380X – Cockpit Variable Catalog (Accessibility Integration)

Authoritative catalog of cockpit variables (L:vars, H:events, K:events) for the FlyByWire A380X,
organized by cockpit AREA / PANEL in roughly **overhead → glareshield → instrument panel → pedestal → displays** order.

**Scope notes**

- The **FCU is intentionally EXCLUDED** (out of scope) per request.
- Sources: local clone `docs/a380-simvars.md`, the model-behaviour XML under
  `SimObjects/AirPlanes/FlyByWire_A380X/attachments/flybywire/Part_Interior_Cockpit/model/behaviour/**`
  (the MSFS-2024-native modular rebuild #10758 relocated these from the old
  `SimObjects/AirPlanes/FlyByWire_A380_842/model/behaviour/**`) and `model/A380_COCKPIT.xml`,
  and the FBW docs site (`a380x-flight-deck-api`, `a380x-systems-api`).
- Prefix legend per entry: `A380X_` = A380-specific FBW var, `A32NX_` = var reused from the A32NX
  codebase (same name carried over to the A380), `FBW_` = framework/shared FBW var, bare names
  (e.g. `LIGHT_NAV`, `LIGHT_POTENTIOMETER:n`, `XMLVAR_*`, `K:*`) = stock MSFS SimVars/events.
- **Type/units**: bool = 0/1; enum = discrete integer states; number = continuous; `Arinc429Word<T>`
  = ARINC 429 labeled word (read with the FBW Arinc429 helper, carries an SSM validity flag).
- R = read-only (status/output), R/W = writable (the control input you toggle for accessibility).
- `{NUM}`/`{ID}` etc. are placeholders; expand per the listed range. The A380 has **4 engines**,
  **4 main-gear bogies**, and a much larger ELEC/HYD/FUEL architecture than the A320 — A380-specific
  items are flagged **[A380-SPECIFIC]**.

A prefix-split summary is given at the end.

---

## OVERHEAD PANEL

### OVHD – Annunciator / Integral Light test (top of overhead)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A380X_OVHD_ANN_LT_POSITION` | enum | 0=TEST, 1=BRT, 2=DIM | R/W | ANN LT switch state (annunciator light test/bright/dim). |
| `A32NX_OVHD_INTLT_ANN` | enum | 0=TEST, 1=BRT, 2=DIM | R/W | Internal annunciator-light mode (drives all PB integral lights). |
| `A380X_OVHD_INTLT_ANN` | enum | 0=TEST, 1=BRT, 2=DIM | R/W | A380X duplicate of the above used by some overhead nodes. **[A380-SPECIFIC]** |

### ELEC – Batteries / Generators / Bus tie / Ext power (ELECTRICAL ATA 24)

Overhead pushbutton states (the controls you toggle):

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_ELEC_BAT_{ID}_PB_IS_AUTO` | bool | 0/1 | R/W | Battery PB AUTO. `{ID}` = 1, 2, ESS, APU. **4 batteries [A380-SPECIFIC]** (A320 has 2). |
| `A32NX_OVHD_ELEC_BAT_{ID}_PB_HAS_FAULT` | bool | 0/1 | R | Battery PB FAULT light. `{ID}` = 1, 2, ESS, APU. |
| `A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO` | bool | 0/1 | R/W | BUS TIE PB AUTO. |
| `A32NX_OVHD_ELEC_BUS_TIE_PB_HAS_FAULT` | bool | 0/1 | R | BUS TIE FAULT. |
| `A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL` | bool | 0/1 | R/W | AC ESS FEED PB NORMAL (vs ALTN). |
| `A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT` | bool | 0/1 | R | AC ESS FEED FAULT. |
| `A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO` | bool | 0/1 | R/W | GALY & CAB (galley/commercial) PB AUTO. |
| `A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT` | bool | 0/1 | R | GALY & CAB FAULT. |
| `A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON` | bool | 0/1 | R/W | COMMERCIAL PB on. |
| `A32NX_OVHD_ELEC_IDG_{NUM}_PB_IS_RELEASED` | bool | 0→1 | R/W | IDG disconnect PB; irreversible release. `{NUM}` = 1–4. **4 IDGs [A380-SPECIFIC]**. |
| `A32NX_OVHD_ELEC_IDG_{NUM}_PB_IS_DISC` | bool | 0/1 | R | IDG mechanically disconnected. `{NUM}` = 1–4. |
| `A32NX_OVHD_ELEC_IDG_{NUM}_PB_HAS_FAULT` | bool | 0/1 | R | IDG FAULT. `{NUM}` = 1–4. |
| `A32NX_OVHD_ELEC_ENG_GEN_{NUM}_PB_HAS_FAULT` | bool | 0/1 | R | Engine generator PB FAULT. `{NUM}` = 1–4. **4 gens [A380-SPECIFIC]**. |
| `A32NX_OVHD_ELEC_EXT_PWR_{ID}_PB_IS_ON` | bool | 0/1 | R/W | External power PB on. `{ID}` = 1–4. **4 ext-power inputs [A380-SPECIFIC]**. |
| `A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON` | bool | 0/1 | R/W | EMER ELEC GEN 1 LINE PB on. |
| `A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_HAS_FAULT` | bool | 0/1 | R | EMER ELEC GEN 1 LINE FAULT. |
| `A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED` | bool | 0/1 | R/W | RAT & EMER GEN manual deploy PB pressed. |
| `A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT` | bool | 0/1 | R | RAT & EMER GEN FAULT. |
| `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` | bool | 0/1 | R | APU AVAIL light (also APU section). |
| `A380X_OVHD_ELEC_BAT_SELECTOR_KNOB` | enum | 0=ESS,1=APU,2=OFF,3=BAT1,4=BAT2 | R/W | Battery voltage display knob. Maps to `A32NX_ELEC_BAT_{idx}_POTENTIAL` (idx ESS=3,APU=4,OFF=0,BAT1=1,BAT2=2). **[A380-SPECIFIC]** |

ELEC system status outputs (SD/ECAM data, mostly R):

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_ELEC_{NAME}_BUS_IS_POWERED` | bool | 0/1 | R | Bus powered. NAME ∈ AC_1..AC_4, AC_ESS, AC_ESS_SCHED, AC_247XP, DC_1, DC_2, DC_ESS, DC_247PP, DC_HOT_1..4, DC_GND_FLT_SVC. **4 AC buses + 4 hot buses [A380-SPECIFIC]**. |
| `A32NX_ELEC_{NAME}_POTENTIAL` | volts | — | R | Element voltage. NAME ∈ APU_GEN_1/2, ENG_GEN_1..4, EXT_PWR, STAT_INV, EMER_GEN, TR_1..4 (TR_3=ESS, TR_4=APU), BAT_1..4 (BAT_3=ESS, BAT_4=APU). |
| `A32NX_ELEC_{NAME}_POTENTIAL_NORMAL` | bool | 0/1 | R | Voltage in normal range (same NAME set). |
| `A32NX_ELEC_{NAME}_FREQUENCY` | hertz | — | R | AC frequency. NAME ∈ APU_GEN_1/2, ENG_GEN_1..4, EXT_PWR, STAT_INV, EMER_GEN. |
| `A32NX_ELEC_{NAME}_FREQUENCY_NORMAL` | bool | 0/1 | R | Frequency normal (same set). |
| `A32NX_ELEC_{NAME}_LOAD` | percent | — | R | Generator load. NAME ∈ APU_GEN_1/2, ENG_GEN_1..4. |
| `A32NX_ELEC_{NAME}_LOAD_NORMAL` | bool | 0/1 | R | Load normal (same set). |
| `A32NX_ELEC_{NAME}_CURRENT` | ampere | — | R | Current. NAME ∈ TR_1..4, BAT_1..4 (negative = discharging). |
| `A32NX_ELEC_{NAME}_CURRENT_NORMAL` | bool | 0/1 | R | Current normal (same set). |
| `A32NX_ELEC_ENG_GEN_{NUM}_IDG_OIL_OUTLET_TEMPERATURE` | celsius | — | R | IDG oil outlet temp. `{NUM}` = 1–4. |
| `A32NX_ELEC_ENG_GEN_{NUM}_IDG_IS_CONNECTED` | bool | 0/1 | R | IDG connected. `{NUM}` = 1–4. |
| `A32NX_ELEC_CONTACTOR_{NAME}_IS_CLOSED` | bool | 0/1 | R | Contactor closed; large A380 set (e.g. 990XU1..4 engine gen line, 990XG1..4 ext-pwr, 980XU1..6 bus-tie, 990PB1/2 battery, 5XE emer-gen). See simvars doc for full list. **[A380-SPECIFIC architecture]** |
| `L:A32NX_EXT_PWR_AVAIL:{NUM}` | bool | 0/1 | R | Ground power available. `{NUM}` = 1–4. **[A380-SPECIFIC]** |

### APU (AUXILIARY POWER UNIT ATA 49)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_APU_MASTER_SW_PB_IS_ON` | bool | 0/1 | R/W | APU MASTER SW on. |
| `A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT` | bool | 0/1 | R | APU MASTER FAULT. |
| `A32NX_OVHD_APU_START_PB_IS_ON` | bool | 0/1 | R/W | APU START PB on. |
| `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` | bool | 0/1 | R | APU AVAIL light. |
| `A32NX_APU_N2` | Arinc429Word<percent> | 0–100 | R | APU N2 RPM percentage. |
| `A32NX_APU_FUEL_USED` | Arinc429Word<mass> | kg | R | APU fuel used. |
| `A32NX_APU_AUTOEXITING_TEST_ON` | bool | 0/1 | R/W | APU auto-exiting test active. |
| `A32NX_APU_AUTOEXITING_TEST_OK` | bool | 0/1 | R | APU auto-exiting test OK result. |
| `A32NX_APU_AUTOEXITING_RESET` | bool | 0/1 | R/W | APU auto-exiting reset. |

### FUEL (FUEL ATA 28)

Overhead fuel-panel controls (transfer PBs are A380-specific; A380 has outer/mid/inner/trim tanks):

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A380X_OVHD_FUEL_OUTRTK_XFR_PB_IS_AUTO` | bool | 0/1 | R/W | OUTR TK transfer PB AUTO. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_MIDTK_XFR_PB_IS_AUTO` | bool | 0/1 | R/W | MID TK transfer PB AUTO. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_INRTK_XFR_PB_IS_AUTO` | bool | 0/1 | R/W | INR TK transfer PB AUTO. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_TRIMTK_XFR_PB_IS_AUTO` | bool | 0/1 | R/W | TRIM TK transfer PB AUTO. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_EMER_OUTR_XFR_PB_IS_ON` | bool | 0/1 | R/W | EMER OUTR TK transfer PB on. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_JETTISON_ARM_PB_IS_ON` | bool | 0/1 | R/W | Fuel JETTISON ARM PB. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_JETTISON_ACTIVE_PB_IS_ON` | bool | 0/1 | R/W | Fuel JETTISON ACTIVE PB. **[A380-SPECIFIC]** |
| `A380X_OVHD_FUEL_JETTISON_IS_OPEN` | bool | 0/1 | R | Jettison valve open. **[A380-SPECIFIC]** |
| `XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED_Pressed` | bool | 0/1 | R | Crossfeed 1 momentary PB pressed. |
| `XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED{2,3,4}_Pressed` | bool | 0/1 | R | Crossfeed 2–4 momentary PBs. **4 crossfeeds [A380-SPECIFIC]** |

Fuel quantity / FQMS outputs:

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_TOTAL_FUEL_QUANTITY` | number (kg) | — | R | Total physical fuel mass. |
| `A32NX_TOTAL_FUEL_VOLUME` | number (gal) | — | R | Total physical fuel volume. |
| `A32NX_FQMS_TOTAL_FUEL_ON_BOARD` | Arinc429Word<kg> | — | R | FQMS total fuel on board. |
| `A32NX_FQMS_GROSS_WEIGHT` | Arinc429Word<kg> | — | R | Aircraft gross weight. |
| `A32NX_FQMS_CENTER_OF_GRAVITY_MAC` | Arinc429Word<percent> | — | R | CG as %MAC. |
| `A32NX_FQMS_{TANK}_TANK_QUANTITY` | Arinc429Word<kg> | — | R | Per-tank quantity. TANK ∈ FEED_1..4, LEFT/RIGHT_OUTER/MID/INNER, TRIM. **11 tanks [A380-SPECIFIC]** |
| `A32NX_FQDC_{ID}_{TANK}_TANK_QUANTITY` | Arinc429Word<kg> | — | R | AGP gauging value. `{ID}` = 1,2; TANK set as above. |
| `A32NX_FQMS_{SIDE}_FUEL_PUMP_RUNNING_WORD` | Arinc429Word<discrete> | bits | R | Pump-running bits. `{SIDE}` = LEFT/RIGHT. |
| `A32NX_FQMS_STATUS_WORD` | Arinc429Word<discrete> | bits | R | FQMS status (bit 11 FMS data unavail, 12 FMS data disagrees). |

### HYDRAULICS (Green / Yellow systems; engine + electric pumps)

A380 uses Green/Yellow hydraulic systems with electric pumps (EPUMP) and engine-driven pumps A/B per engine:

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_HYD_ENG_{NUM}A_PUMP_PB_IS_AUTO` | bool | 0/1 | R/W | Engine `{NUM}` pump A PB AUTO. `{NUM}` = 1–4. **[A380-SPECIFIC]** |
| `A32NX_OVHD_HYD_ENG_{NUM}B_PUMP_PB_IS_AUTO` | bool | 0/1 | R/W | Engine `{NUM}` pump B PB AUTO. `{NUM}` = 1–4. |
| `A32NX_OVHD_HYD_ENG_{NUM}{A,B}_PUMP_PB_HAS_FAULT` | bool | 0/1 | R | Engine pump A/B FAULT. |
| `A32NX_OVHD_HYD_ENG_{NUM}AB_PUMP_DISC_PB_IS_AUTO` | bool | 0/1 | R/W | Engine `{NUM}` A+B pump disconnect PB AUTO (not disconnected). |
| `A32NX_OVHD_HYD_ENG_{NUM}AB_PUMP_DISC_PB_HAS_FAULT` | bool | 0/1 | R | Pump-disconnect PB FAULT. |
| `A32NX_HYD_ENG_{NUM}AB_PUMP_DISC` | bool | 0/1 | R | Disconnected-pump feedback signal. |
| `A32NX_OVHD_HYD_EPUMP{G,Y}{A,B}_ON_PB_IS_AUTO` | bool | 0/1 | R/W | Green/Yellow electric pump A/B ON PB AUTO. **[A380-SPECIFIC]** |
| `A32NX_OVHD_HYD_EPUMP{G,Y}{A,B}_ON_PB_HAS_FAULT` | bool | 0/1 | R | EPUMP ON PB FAULT. |
| `A32NX_OVHD_HYD_EPUMP{G,Y}{A,B}_OFF_PB_IS_AUTO` | bool | 0/1 | R/W | EPUMP OFF PB AUTO. |
| `A32NX_OVHD_HYD_EPUMP{G,Y}{A,B}_OFF_PB_HAS_FAULT` | bool | 0/1 | R | EPUMP OFF PB FAULT. |
| `A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO` / `_PB_HAS_FAULT` | bool | 0/1 | R/W / R | Additional electric pump B control/fault. |
| `A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO` / `_PB_HAS_FAULT` | bool | 0/1 | R/W / R | Yellow electric pump control/fault. |
| `A32NX_OVHD_HYD_PTU_PB_IS_AUTO` | bool | 0/1 | R/W | PTU PB AUTO. |
| `A32NX_OVHD_HYD_PTU_PB_HAS_FAULT` | bool | 0/1 | R | PTU FAULT. |
| `A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED` | bool | 0/1 | R/W | RAT manual deploy PB pressed. |
| `A32NX_HYD_BRAKE_ALTN_{LEFT,RIGHT,ACC}_PRESS` | psi | — | R | Alternate-brake hydraulic pressures. |
| `A32NX_HYD_{SURFACE}_DEFLECTION` | number | — | R | Flight-surface hydraulic deflections (ailerons inboard/middle/outboard L/R, elevators, upper/lower rudder, spoilers 1–8 L/R). **[A380-SPECIFIC surface count]** |

### AIR / PNEUMATIC / BLEED (BLEED AIR ATA 36) + COND (AIR COND ATA 21)

Overhead bleed/air-cond controls:

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON` | bool | 0/1 | R/W | APU BLEED PB on. |
| `A32NX_OVHD_PNEU_APU_BLEED_PB_HAS_FAULT` | bool | 0/1 | R | APU BLEED FAULT. |
| `A32NX_OVHD_PNEU_ENG_{ID}_BLEED_PB_IS_AUTO` | bool | 0/1 | R/W | Engine `{ID}` BLEED PB AUTO. `{ID}` = 1–4. **4 engine bleeds [A380-SPECIFIC]** |
| `A32NX_OVHD_PNEU_ENG_{ID}_BLEED_PB_HAS_FAULT` | bool | 0/1 | R | Engine bleed FAULT. |
| `A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION` | enum | 0=CLOSE,1=AUTO,2=OPEN | R/W | Crossbleed valve selector. |
| `A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION` | enum | 0=MAN,1=LO,2=NORM,3=HI | R/W | Pack flow selector. |
| `A32NX_OVHD_COND_PACK_{NUM}_PB_IS_ON` | bool | 0/1 | R/W | Pack `{NUM}` PB on. `{NUM}` = 1,2. |
| `A32NX_OVHD_COND_PACK_{NUM}_PB_HAS_FAULT` | bool | 0/1 | R | Pack FAULT. |
| `A32NX_OVHD_COND_HOT_AIR_{IDX}_PB_IS_ON` | bool | 0/1 | R/W | HOT AIR `{IDX}` PB on. `{IDX}` = 1,2. |
| `A32NX_OVHD_COND_HOT_AIR_{IDX}_PB_HAS_FAULT` | bool | 0/1 | R | HOT AIR FAULT. |
| `A32NX_OVHD_COND_RAM_AIR_PB_IS_ON` | bool | 0/1 | R/W | RAM AIR PB on. |
| `A32NX_OVHD_COND_{ID}_SELECTOR_KNOB` | number | 0–300 (°C = v*0.04+18) | R/W | Temp selector. `{ID}` = CKPT, CABIN. |
| `A32NX_PNEU_ENG_{NUM}_INTERMEDIATE_TRANSDUCER_PRESSURE` | psi | -1 if no output | R | Intermediate-pressure transducer. `{NUM}` = 1–4. |
| `A32NX_COND_{ID}_TEMP` | °C | — | R | Zone temp. ID ∈ CKPT, MAIN_DECK_1..8, UPPER_DECK_1..7, CARGO_FWD, CARGO_BULK. **2-deck cabin [A380-SPECIFIC]** |
| `A32NX_COND_{ID}_DUCT_TEMP` | °C | — | R | Duct temp (same ID set). |
| `A32NX_COND_{ID}_TRIM_AIR_VALVE_POSITION` | percent | 0–100 | R | Trim-air valve opening (same ID set). |
| `A32NX_COND_PACK_{ID}_OUTLET_TEMPERATURE` | °C | — | R | Pack outlet temp. `{ID}` = 1,2. |
| `A32NX_COND_PURS_SEL_TEMPERATURE` | °C | — | R | Purser-selected cabin temp (set via EFB). |
| `A32NX_COND_CPIOM_B{ID}_{APP}_DISCRETE_WORD` | Arinc429Word<discrete> | bits | R | CPIOM B AGS/TCS/VCS/CPCS discrete words. `{ID}` = 1–4. **[A380-SPECIFIC]** |

### PRESSURIZATION / CABIN (ATA 21)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO` | bool | 0/1 | R/W | Manual cabin-altitude PB AUTO. |
| `A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB` | feet | — | R/W | Manually selected cabin target altitude. |
| `A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO` | bool | 0/1 | R/W | Manual cabin-V/S PB AUTO. |
| `A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB` | fpm | — | R/W | Manually selected cabin V/S. |
| `A32NX_OVHD_PRESS_DITCHING_PB_IS_ON` | bool | 0/1 | R/W | DITCHING PB on. |
| `A32NX_PRESS_CABIN_ALTITUDE_{CPIOM}` | Arinc429Word<feet> | — | R | Cabin altitude. `{CPIOM}` = B1–B4. |
| `A32NX_PRESS_CABIN_ALTITUDE_TARGET_{CPIOM}` | Arinc429Word<feet> | — | R | Target cabin altitude (B1–B4). |
| `A32NX_PRESS_CABIN_VS_{CPIOM}` | Arinc429Word<fpm> | — | R | Cabin V/S (B1–B4). |
| `A32NX_PRESS_CABIN_VS_TARGET_{CPIOM}` | Arinc429Word<fpm> | — | R | Target cabin V/S (B1–B4). |
| `A32NX_PRESS_CABIN_DELTA_PRESSURE_{CPIOM}` | Arinc429Word<psi> | — | R | Cabin/exterior delta-P (B1–B4). |
| `A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE` | psi | — | R | Analog delta-P from CPC1 manual partition. |
| `A32NX_PRESS_OUTFLOW_VALVE_{NUM}_OPEN_PERCENTAGE_ANIM` | percent | 0–100 | R | Outflow-valve opening (animation). `{NUM}` = 1–4. **4 OFVs [A380-SPECIFIC]** |
| `A32NX_PRESS_OCSM_{ID}_CHANNEL_{CH}_FAILURE` | bool | 0/1 | R | OCSM channel failure. `{ID}` = 1–4, `{CH}` = 1,2. |
| `A32NX_PRESS_OCSM_{ID}_AUTO_PARTITION_FAILURE` | bool | 0/1 | R | OCSM auto-control failure. `{ID}` = 1–4. |

### VENTILATION (cabin/avionics fans)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON` | bool | 0/1 | R/W | Cabin fans PB on. |
| `A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON` | bool | 0/1 | R/W | Air-extract override PB on. |
| `A32NX_VENTILATION_BLOWER_TOGGLE` | bool | 0/1 | R/W | Avionics blower toggle. |
| `A32NX_VENTILATION_BLOWER_FAULT` | bool | 0/1 | R | Blower fault. |
| `A32NX_VENTILATION_EXTRACT_TOGGLE` | bool | 0/1 | R/W | Avionics extract toggle. |
| `A32NX_VENTILATION_EXTRACT_FAULT` | bool | 0/1 | R | Extract fault. |
| `A32NX_VENTILATION_CABFANS_TOGGLE` | bool | 0/1 | R/W | Cabin-fans toggle (legacy). |
| `A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN` | bool | 0/1 | R | Overpressure relief valve open. |
| `A32NX_VENT_{ID}_VCM_CHANNEL_{CH}_FAILURE` | bool | 0/1 | R | VCM channel failure. `{ID}` = FWD/AFT, `{CH}` = 1,2. |

### CARGO AIR-COND / HEAT

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_CARGO_AIR_{ID}_SELECTOR_KNOB` | number | 0–300 (°C = v*0.0667+5) | R/W | Cargo temp selector. `{ID}` = FWD, BULK. |
| `A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{ID}_PB_IS_ON` | bool | 0/1 | R/W | Cargo isolation-valve PB on. `{ID}` = FWD, BULK. |
| `A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{ID}_PB_HAS_FAULT` | bool | 0/1 | R | Isolation-valve FAULT. |
| `A32NX_OVHD_CARGO_AIR_HEATER_PB_IS_ON` | bool | 0/1 | R/W | Bulk cargo heater PB on. |
| `A32NX_OVHD_CARGO_AIR_HEATER_PB_HAS_FAULT` | bool | 0/1 | R | Cargo heater FAULT. |

### ANTI-ICE / PROBE HEAT

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_MAN_PITOT_HEAT` | bool | 0/1 | R/W | Probe/window heat manual control. |
| `STRUCTURAL_DEICE_SWITCH` | bool | 0/1 | R/W | Wing anti-ice (stock SimVar). |
| `XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED` | bool | 0/1 | R/W | WING anti-ice momentary PB. |
| `ENG_ANTI_ICE:{NUM}` | bool | 0/1 | R/W | Engine anti-ice (stock SimVar). `{NUM}` = 1–4. **4 engines [A380-SPECIFIC]** |
| `XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG{NUM}_PRESSED` | bool | 0/1 | R/W | ENG anti-ice momentary PB. `{NUM}` = 1–4. |
| `A32NX_ICING_STATE_ICING_STICK_INDICATOR` | bool | 0/1 | R | Icing-condition indicator. |

### FIRE (FIRE & SMOKE ATA 26)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_FIRE_BUTTON_ENG{NUM}` | bool | 0/1 | R/W | Engine fire PB released. `{NUM}` = 1–4. **4 engines [A380-SPECIFIC]** |
| `A32NX_FIRE_BUTTON_APU` | bool | 0/1 | R/W | APU fire PB released. |
| `A32NX_FIRE_GUARD_ENG{NUM}` / `A32NX_FIRE_GUARD_APU1` | bool | 0/1 | R/W | Fire PB guard. |
| `A32NX_OVHD_FIRE_AGENT_{BOTTLE}_{ZONE}_{NUM}_IS_PRESSED` | bool | 0/1 | R/W | Agent-discharge PB (momentary). BOTTLE 1/2, ZONE APU/ENG, NUM 1–4 (APU uses 1_APU_1). |
| `A32NX_OVHD_FIRE_SQUIB_{BOTTLE}_{ZONE}_{NUM}_IS_ARMED` | bool | 0/1 | R | Squib armed. |
| `A32NX_OVHD_FIRE_SQUIB_{BOTTLE}_{ZONE}_{NUM}_IS_DISCHARGED` | bool | 0/1 | R | Agent discharged. |
| `A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED` | bool | 0/1 | R/W | Fire test PB pressed. |
| `A32NX_FIRE_DETECTED_ENG{NUM}` | bool | 0/1 | R | Fire detected on engine. `{NUM}` = 1–4. |
| `A32NX_FIRE_DETECTED_{ZONE}` / `A32NX_{ZONE}_ON_FIRE` | bool | 0/1 | R | Fire detected. `{ZONE}` = APU, MLG. |
| `A32NX_FIRE_FDU_DISCRETE_WORD` | Arinc429Word<discrete> | bits | R | FDU discrete (per-engine/APU/MLG fire & loop faults; 4 engine loops). **[A380-SPECIFIC]** |

### OXYGEN

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `PUSH_OVHD_OXYGEN_CREW` | enum | 0=AUTO, 1=OFF | R/W | Crew oxygen supply PB. |
| `A32NX_OXYGEN_MASKS_DEPLOYED` | bool | 0/1 | R/W | PAX oxygen masks deployed. |
| `A32NX_OXYGEN_PASSENGER_LIGHT_ON` | bool | 0/1 | R/W | PAX oxygen indicator light. |
| `A32NX_OXYGEN_TMR_RESET` | bool | 0/1 | R/W | Oxygen timer reset PB. |
| `A32NX_OXYGEN_TMR_RESET_FAULT` | bool | 0/1 | R | Oxygen timer-reset FAULT. |

### CALLS / EVAC / SIGNS

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_CALLS_EMER_ON` | bool | 0/1 | R/W | EMER (CALLS) PB on. |
| `A32NX_EVAC_COMMAND_TOGGLE` | bool | 0/1 | R/W | EVAC COMMAND toggle. |
| `A32NX_EVAC_CAPT_TOGGLE` | bool | 0/1 | R/W | EVAC CAPT/PURS selector toggle. |
| `XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position` | enum | 0=ON,1=AUTO,2=OFF | R/W | SEAT BELTS sign switch. |
| `XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position` | enum | 0=ON,1=AUTO,2=OFF | R/W | NO SMOKING sign switch. |
| `XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position` | enum | 0=ON,1=AUTO,2=OFF | R/W | EMER EXIT LT switch. |
| `K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE` | event | — | W | Stock seat-belt sign toggle event. |

### RCDR (Recorder) / Misc overhead

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_RCDR_GROUND_CONTROL_ON` | bool | 0/1 | R/W | CVR/recorder GND CTL on. |
| `A32NX_ELT_ON` | bool | 0/1 | R/W | ELT switch on. |
| `A32NX_ELT_TEST_RESET` | bool | 0/1 | R/W | ELT test/reset. |
| `A32NX_DFDR_EVENT_ON` | bool | 0/1 | R/W | DFDR EVENT PB (momentary, resets to 0). |
| `A32NX_ACMS_TRIGGER_ON` | bool | 0/1 | R/W | ACMS trigger (momentary). |
| `A32NX_AVIONICS_COMPLT_ON` | bool | 0/1 | R/W | Avionics compartment light. |
| `A32NX_RAIN_REPELLENT_{LEFT,RIGHT}_ON` | bool | 0/1 | R/W | Rain-repellent (wiper area). |
| `A32NX_SVGEINT_OVRD_ON` | bool | 0/1 | R/W | SVGE INT override. |
| `A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE` | bool | 0/1 | R/W | Cockpit-door video toggle. |
| `A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE` | bool | 0/1 | R/W | NSS data-to-avionics toggle. **[A380-SPECIFIC]** |
| `A32NX_NSS_MASTER_OFF` | bool | 0/1 | R/W | NSS master off. **[A380-SPECIFIC]** |
| `A380X_OVHD_STORM_LT` | bool | 0/1 | R/W | STORM (storm light) switch. **[A380-SPECIFIC]** |
| `A380X_REMOTE_CB_CTRL` | bool | 0/1 | R/W | Remote circuit-breaker control. **[A380-SPECIFIC]** |

### WIPERS

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `CIRCUIT_SWITCH_ON:141` | bool | 0/1 | R/W | Wiper LEFT on/off (stock circuit). |
| `CIRCUIT_SWITCH_ON:143` | bool | 0/1 | R/W | Wiper RIGHT on/off (stock circuit). |
| `K:ELECTRICAL_CIRCUIT_POWER_SETTING_SET:141` | event | 0/75/100 | W | Wiper LEFT speed (0=off, 75=slow, 100=fast). |
| `K:ELECTRICAL_CIRCUIT_POWER_SETTING_SET:143` | event | 0/75/100 | W | Wiper RIGHT speed. |

### ADIRS

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_ADIRS_IR_{NUM}_MODE_SELECTOR_KNOB` | enum | 0=OFF,1=NAV,2=ATT | R/W | IR mode selector. `{NUM}` = 1–3. |
| `A32NX_OVHD_ADIRS_IR_{NUM}_PB_IS_ON` | bool | 0/1 | R/W | IR PB on. |
| `A32NX_OVHD_ADIRS_IR_{NUM}_PB_HAS_FAULT` | bool | 0/1 | R | IR FAULT. |
| `A32NX_OVHD_ADIRS_ADR_{NUM}_PB_IS_ON` | bool | 0/1 | R/W | ADR PB on. |
| `A32NX_OVHD_ADIRS_ADR_{NUM}_PB_HAS_FAULT` | bool | 0/1 | R | ADR FAULT. |
| `A32NX_OVHD_ADIRS_ON_BAT_IS_ILLUMINATED` | bool | 0/1 | R | ADIRS ON BAT light. |
| `A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME` | seconds | — | R | Remaining alignment time. |

### FLIGHT-CONTROL computers (overhead F/CTL)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_PRIM_{ID}_PUSHBUTTON_PRESSED` | bool | 0/1 | R/W | PRIM computer PB. `{ID}` = 1–3 (PRIM replaces ELAC/SEC arch). **[A380-SPECIFIC]** |
| `A32NX_PRIM_{ID}_HEALTHY` | bool | 0/1 | R | PRIM healthy. |
| `A32NX_SEC_{ID}_PUSHBUTTON_PRESSED` | bool | 0/1 | R/W | SEC computer PB. `{ID}` = 1–3. **[A380-SPECIFIC]** |
| `A32NX_SEC_{ID}_HEALTHY` | bool | 0/1 | R | SEC healthy. |

### ENGINE start / FADEC (overhead)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_OVHD_FADEC_{ENG}` | bool | 0/1 | R/W | FADEC ground-power PB per engine. `{ENG}` = 1–4. **4 FADECs [A380-SPECIFIC]** |
| `A32NX_ENGMANSTART{NUM}_TOGGLE` | bool | 0/1 | R/W | Manual start toggle per engine. `{NUM}` = 1–4. |
| `A32NX_ENGMANSTARTALTN_TOGGLE` | bool | 0/1 | R/W | Alternate manual-start toggle. |

---

## GLARESHIELD

(FCU excluded.)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `PUSH_AUTOPILOT_MASTERWARN_{SIDE}` | bool | 0/1 | R/W | MASTER WARN PB. `{SIDE}` = L, R. |
| `PUSH_AUTOPILOT_MASTERCAUT_{SIDE}` | bool | 0/1 | R/W | MASTER CAUT PB. `{SIDE}` = L, R. |
| `A32NX_MASTER_WARNING` | bool | 0/1 | R | Master-warning active. |
| `A32NX_MASTER_CAUTION` | bool | 0/1 | R | Master-caution active. |
| `A32NX_AUTOPILOT_AUTOLAND_WARNING` | bool | 0/1 | R | AUTO LAND warning light. |
| `A32NX_DCDU_ATC_MSG_ACK` | bool | 0/1 | R/W | ATC MSG acknowledge PB. |
| `A32NX_DCDU_ATC_MSG_WAITING` | bool | 0/1 | R | ATC MSG waiting indicator. |
| `A380X_SWITCH_OIT_SIDE_LEFT` | enum | 0=NSS AVNCS,1=NSS FLT OPS | R/W | CAPT OIT side switch. **[A380-SPECIFIC]** |
| `A380X_SWITCH_OIT_SIDE_RIGHT` | enum | 0=NSS AVNCS,1=NSS FLT OPS | R/W | F/O OIT side switch. **[A380-SPECIFIC]** |

### EFIS Control Panel (EFIS CP — part of glareshield; NOT the FCU)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A380X_EFIS_{SIDE}_LS_BUTTON_IS_ON` | bool | 0/1 | R/W | LS PB. `{SIDE}` = L, R. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_VV_BUTTON_IS_ON` | bool | 0/1 | R/W | VV PB. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_CSTR_BUTTON_IS_ON` | bool | 0/1 | R/W | CSTR (constraints) PB. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_ARPT_BUTTON_IS_ON` | bool | 0/1 | R/W | ARPT PB. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_TRAF_BUTTON_IS_ON` | bool | 0/1 | R/W | TRAF PB. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_ACTIVE_FILTER` | enum | 1=WPT,2=VORD,3=NDB | R/W | Active waypoint filter. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_ACTIVE_OVERLAY` | enum | 0=WX/OFF,1=TERR (page lists 0=OFF,1=WX,2=TERR) | R/W | WX/TERR overlay. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_NAVAID_{N}_BUTTON_IS_ON` | enum | 0=OFF,1=ADF,2=VOR | R/W | Navaid 1/2 selector. `{N}` = 1,2. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_BARO_PRESELECTED` | number (hPa/inHg) | 0 when not displayed | R | Preselected QNH in STD mode. Not for systems use. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_ND_MODE` | enum | 0=ROSE ILS,1=ROSE VOR,2=ROSE NAV,3=ARC,4=PLAN | R/W | ND mode. **[A380-SPECIFIC]** |
| `A380X_EFIS_{SIDE}_ND_RANGE` | enum | 0=ZOOM,1=10,2=20,3=40,4=80,5=160,6=320,7=640 | R/W | ND range. **[A380-SPECIFIC]** |
| `A32NX_EFIS_{SIDE}_OANS_RANGE` | enum | 0=MAX … 4=MIN | R/W | OANS range (requires ND_RANGE=0). |
| `A32NX_EFIS_{SIDE}_ND_MODE` / `A32NX_EFIS_{SIDE}_ND_RANGE` | enum | as above | R | Legacy A32NX-named mirror of ND mode/range (read). |
| `A32NX_EFIS_{SIDE}_OPTION` | enum | filter option | R/W | Legacy EFIS option var. |
| `A32NX_EFIS_TERR_{SIDE}_ACTIVE` | bool | 0/1 | R | Terrain overlay active. |
| `H:A380X_EFIS_CP_BARO_PULL_{ALTIMETER_INDEX}` | event | — | W | Baro knob pull (STD). **[A380-SPECIFIC]** |
| `H:A380X_EFIS_CP_BARO_PUSH_{ALTIMETER_INDEX}` | event | — | W | Baro knob push (QNH). **[A380-SPECIFIC]** |
| `H:A32NX_EFIS_{SIDE}_CHRONO_PUSHED` | event | — | W | EFIS chrono push (`{SIDE}` = L/R). |

---

## MAIN INSTRUMENT PANEL (MIP)

### Gear / Autobrake / Anti-skid

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_GEAR_HANDLE_POSITION` | number | 0.0–1.0 | R/W | Gear lever (0=up, 1=down). |
| `A32NX_GEAR_LEVER_LOCKED` | bool | 0/1 | R | Gear lever locked. |
| `A32NX_GEAR_{LEFT,RIGHT,CENTER}_POSITION` | number | 0–100 | R | Gear strut position. **Body+wing gear [A380-SPECIFIC]** |
| `A32NX_GEAR_{NUM}_TILT_POSITION` | number | — | R | Bogie tilt per main-gear unit. `{NUM}` = 1–4. **[A380-SPECIFIC]** |
| `A32NX_GEAR_DOOR_{LEFT,RIGHT,CENTER}_POSITION` | number | — | R | Gear-door position. |
| `A32NX_SECONDARY_GEAR_DOOR_{LEFT,RIGHT}_POSITION` | number | — | R | Secondary (body-gear) door position. **[A380-SPECIFIC]** |
| `A32NX_LGCIU_{ID}_{SIDE}_GEAR_DOWNLOCKED` | bool | 0/1 | R | LGCIU downlock. `{ID}` = 1,2; `{SIDE}` = LEFT/RIGHT. |
| `A32NX_LG_GRVTY_MASTER_SWITCH_GUARD` | bool | 0/1 | R | Gravity-extension master guard. |
| `A32NX_LG_GRVTY_SWITCH_GUARD_{NUM}` | bool | 0/1 | R/W | Gravity-extension guard. `{NUM}` = 1,2. |
| `A32NX_LG_GRVTY_SWITCH_POS` | enum | 0=RESET,1=OFF,2=DOWN | R/W | Gravity gear-extension switch. |
| `A32NX_AUTOBRAKES_SELECTED_MODE` | enum | 0=DISARM,1=BTV,2=LOW,3=L2,4=L3,5=HIGH | R/W | Autobrake selector. **BTV/L2/L3 [A380-SPECIFIC]** |
| `A32NX_AUTOBRAKES_ARMED_MODE` | enum | 0–5 as above, 6=RTO | R | Actual armed autobrake mode. |
| `A32NX_AUTOBRAKES_DISARM_KNOB_REQ` | bool | 0/1 | R | Autobrake solenoid disarm request. |
| `A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED` | bool | 0/1 | R/W | RTO autobrake arm PB. |
| `A32NX_AUTOBRAKES_RTO_ARMED` | bool | 0/1 | R | RTO armed. |
| `ANTISKID_BRAKES_ACTIVE` | bool | 0/1 | R/W | Anti-skid active (stock SimVar). |
| `A32NX_BTV_STATE` | enum | 0=DISABLED,1=ARMED,2=ROT OPT,3=DECEL,4=END OF BRAKING | R | Brake-To-Vacate state. **[A380-SPECIFIC]** |
| `A32NX_BRAKE_TEMPERATURE_{1..16}` | celsius | — | R | Main-wheel brake temps. **16 brakes [A380-SPECIFIC]** |
| `A32NX_REPORTED_BRAKE_TEMPERATURE_{1..16}` | celsius | — | R | Sensor-reported brake temps. |
| `A32NX_NOSE_WHEEL_{LEFT,RIGHT}_ANIM_ANGLE` | degrees | — | R | Nose-wheel angle (wheel axis). |

### Switching / Clock / ISIS

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_ATT_HDG_SWITCHING_KNOB` | enum | 0=CAPT,1=NORM,2=F/O | R/W | ATT/HDG source switch. |
| `A32NX_AIR_DATA_SWITCHING_KNOB` | enum | 0=CAPT,1=NORM,2=F/O | R/W | Air-data source switch. |
| `A32NX_EIS_DMC_SWITCHING_KNOB` | enum | 0=CAPT,1=NORM,2=F/O | R/W | EIS/DMC source switch. |
| `A32NX_CHRONO_ET_SWITCH_POS` | enum | 0=RUN,1=STOP,2=RESET | R/W | Elapsed-time switch. |
| `H:A32NX_CHRONO_TOGGLE` / `_RST` / `_DATE` / `_ET_POS_CHANGED` | event | — | W | Clock chrono control events. |
| `A32NX_ISIS_{BTN}_ACTIVE` | bool | 0/1 | R | ISIS button state. `{BTN}` = LS, BUGS. |
| `H:A32NX_ISIS_{BTN}_PRESSED` / `_RELEASED` | event | — | W | ISIS button press/release. |
| `H:A32NX_ISIS_KNOB_CLOCKWISE` / `_ANTI_CLOCKWISE` / `_PRESSED` | event | — | W | ISIS baro knob events. |
| `A32NX_ISIS_BARO_MODE` / `A32NX_ISIS_BARO_UNIT_INHG` | enum/bool | — | R/W | ISIS baro mode/unit. |

---

## PEDESTAL

### THRUST levers / ENG MASTER / mode selector

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_AUTOTHRUST_TLA:{NUM}` | degrees | TLA | R | Thrust-lever angle per engine. `{NUM}` = 1–4. **4 thrust levers [A380-SPECIFIC]** |
| `A32NX_3D_THROTTLE_LEVER_POSITION_{ID}` | number | — | R | 3D throttle-lever animation. `{ID}` = 1–4. |
| `A32NX_AUTOTHRUST_DISCONNECT` | bool | 0/1 | R | A/THR instinctive-disconnect status. |
| `A32NX_AUTOTHRUST_STATUS` | enum | 0=Disengaged,1=Armed,2=Active | R | A/THR status. |
| `A32NX_REVERSER_{NUM}_POSITION` | number | — | R | Reverser position (inboard engines 2,3). |
| `TURB_ENG_IGNITION_SWITCH_EX1:{NUM}` | enum | 0=CRANK,1=NORM,2=IGN START | R | Engine mode selector readback. `{NUM}` = 1–4. |
| ENG MASTER switches | node `SWITCH_ENGINES_ENG{1..4}` | — | R/W | ENG MASTER 1–4 (drive stock fuel-valve/mixture; no dedicated FBW L:var — read via `GENERAL ENG ...`). **4 masters [A380-SPECIFIC]** |
| Engine mode KNOB | node `KNOB_ENGINES_MODE` | 0=CRANK,.5=NORM,1=IGN/START | R/W | Engine start-mode selector. |

### FLAPS / SPEED BRAKE / TRIM / PARK BRAKE

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_FLAPS_HANDLE_INDEX` | enum | 0=UP,1=1,2=2,3=3,4=FULL | R | Flap-lever detent. |
| `A32NX_FLAPS_HANDLE_PERCENT` | number | 0.0–1.0 (0.25 steps) | R | Flap-lever percent. |
| `A32NX_FLAPS_CONF_INDEX` | enum | 0=Conf0,1=Conf1,2=Conf1F,3=Conf2,4=Conf2S,5=Conf3,6=Conf4 | R | Computed flap config (don't use in systems). |
| `A32NX_SPOILERS_HANDLE_POSITION` | number | 0.0–1.0 | R | Speed-brake handle. |
| `A32NX_SPOILERS_ARMED` | bool | 0/1 | R | Ground-spoiler armed. |
| `A32NX_PARK_BRAKE_LEVER_POS` | bool/number | 0–1 | R/W | Parking-brake lever. |
| `XMLVAR_RudderTrim` | enum | 0–2 | R/W | Rudder-trim selector (node `KNOB_RUDDERTRIM`). |
| Pitch-trim switch | node `SWITCH_TRIM_PITCH` | K:ELEV_TRIM_DN/UP | R/W | Manual pitch-trim rocker. |
| `A32NX_LEFT/RIGHT_BRAKE_PEDAL_INPUT` | number | 0–100 | R | Brake-pedal input. |
| `A32NX_TILLER_HANDLE_POSITION` | number | -1.0–1.0 | R | Nosewheel tiller. |

### ECAM Control Panel (ECP) / SD page (ECAM ATA 31)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_ECAM_SD_CURRENT_PAGE_INDEX` | enum | -1=None,0=ENG,1=APU,2=BLEED,3=COND,4=PRESS,5=DOOR,6=EL/AC,7=EL/DC,8=FUEL,9=WHEEL,10=HYD,11=F/CTL,12=C/B,13=CRZ,14=STS,15=VIDEO | R/W | SD page selected on ECP. |
| `A32NX_BTN_{NAME}` | bool | 0=not pressed,1=pressed (momentary) | R/W | ECP buttons. NAME ∈ ALL, ABNPROC, CHECK_LH, CHECK_RH, CL, CLR, CLR2, DOWN, EMERCANC, MORE, RCL, TOCONFIG, UP. |
| `A32NX_SD_MORE_SHOWN` | bool | 0/1 | R | SD MORE page shown. |
| `H:A32NX_SD_PAGE_CHANGED` / `_REQUEST_MORE` / `_STS_NEXT_PAGE` | event | — | W | SD navigation events. |
| `A32NX_FWS_ECP_FAILED` | bool | 0/1 | R | ECP failed. |

### WEATHER RADAR / SURV / TCAS (pedestal)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_RADAR_MULTISCAN_AUTO` | bool | 0/1 | R/W | WXR multiscan AUTO (INOP). |
| `A32NX_RADAR_GCS_AUTO` | bool | 0/1 | R/W | WXR GCS AUTO (INOP). |
| `A32NX_SWITCH_RADAR_PWS_Position` | enum | 0/1 | R/W | Predictive-windshear switch (INOP). |
| WXR mode knob / sys switch | nodes `KNOB_RADAR_MODE`, `SWITCH_RADAR_SYS` | — | R/W | WXR mode and on/off. |
| SURV TCAS/XPDR/GS/WXR-TAWS buttons | nodes `PUSH_SURV_*` (TCAS_ABV/BLW/TAONLY, GS_MODE, WXR_TAWS_SYS1/2, XPDR_TCAS_SYS1/2) | — | R/W | A380 SURV-panel pushbuttons. **[A380-SPECIFIC]** |

### TRANSPONDER / ATC (driven via MFD SURV, but K-events apply)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `K:XPNDR_SET` | event | BCD code | W | Set transponder squawk. |
| `K:XPNDR_IDENT_ON` | event | — | W | Transponder IDENT. |

### RMP – Radio Management Panel (COMMUNICATIONS ATA 23) **[A380-SPECIFIC layout; 3 RMPs]**

Placeholders: `{rmp_index}` = 1,2,3; `{vhf_index}` = 1,2,3; `{hf_index}`/`{tel_index}` per radio.

State / synced frequencies:

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A380X_RMP_{rmp_index}_STATE` | enum | 0=OffFailed,1=OffStandby,2=On,3=OnFailed | R | RMP power/op state. |
| `FBW_RMP_FREQUENCY_ACTIVE_{vhf_index}` | BCD32 freq | MHz | R | Synced active VHF frequency. |
| `FBW_RMP_FREQUENCY_STANDBY_{vhf_index}` | BCD32 freq | MHz | R | Synced standby VHF frequency. |
| `FBW_RMP_MODE_ACTIVE_{vhf_index}` | enum | 0=Frequency,1=Data,2=Emergency | R | Active frequency mode. |
| `FBW_RMP_MODE_STANDBY_{vhf_index}` | enum | 0=Frequency,1=Data,2=Emergency | R | Standby frequency mode. |
| `FBW_RMP{rmp_index}_PRIMARY_VHF{vhf_index}_FREQUENCY` | Arinc429 BCD | — | R | Primary-bus freq to VHF radios. |
| `FBW_RMP{rmp_index}_BACKUP_VHF{vhf_index}_FREQUENCY` | Arinc429 BCD | — | R | Backup-bus freq to VHF radios. |
| `FBW_VHF{vhf_index}_BUS_A` | bool | 0/1 | R | Hardwired bus-select discrete. |
| `FBW_VHF{vhf_index}_FREQUENCY` | Arinc429 BCD | — | R | Tuned freq from VHF radios. |

Per-channel TX / RX / CALL / VOL (channels: VHF, HF, TEL, INT, CAB, PA, NAV — most R indicators):

| Variable | Type | R/W | Description |
|---|---|---|---|
| `A380X_RMP_{rmp_index}_{CH}_TX[_{idx}]` | bool | R | Channel transmit on/off (CH ∈ VHF/HF/TEL/INT/CAB/PA). |
| `A380X_RMP_{rmp_index}_{CH}_RX[_{idx}]` | bool | R | Channel reception state. |
| `A380X_RMP_{rmp_index}_{CH}_RX_SWITCH[_{idx}]` | bool | R | Physical reception switch state. |
| `A380X_RMP_{rmp_index}_{CH}_CALL[_{idx}]` | bool | R | Incoming-call indicator. |
| `A380X_RMP_{rmp_index}_{CH}_VOL[_{idx}]` | percent | R | Volume-knob position (0–100). |
| `A380X_RMP_{rmp_index}_NAV_SELECT` | enum | R | Selected navaid for audio: 0=ADF1,1=ADF2,2=LS,3=VOR1,4=VOR2,5=MKR. |
| `A380X_RMP_{rmp_index}_NAV_FILTER` | bool | R | Voice filter (ident-morse filter). |
| `A380X_RMP_{rmp_index}_STBY_NAV` | bool | R | Standby radio-nav on/off. |
| `A380X_RMP_{rmp_index}_{GREEN,RED,RST}_LED` | bool | R | RMP LEDs (GREEN=powered/standby, RED=fail, RST=reset available). |
| `A380X_RMP_{rmp_index}_MECH_TX` | bool | R | MECH transmit indicator. |
| `H:A32NX_RMP_LEFT_TOGGLE_SWITCH` | event | W | RMP left on/off toggle. |

### KCCU / Keyboard (pedestal)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_KCCU_{SIDE}_KBD_ON_OFF` | bool | 0/1 | R/W | KCCU keyboard on/off. `{SIDE}` = L/R. **[A380-SPECIFIC]** |
| `A32NX_KCCU_{SIDE}_CCD_ON_OFF` | bool | 0/1 | R/W | KCCU cursor-control on/off. **[A380-SPECIFIC]** |
| `H:A380X_LAPTOP_KEYBOARD_{GROUP}_{KEY}` | event | — | W | OIT/laptop keyboard keys. **[A380-SPECIFIC]** |
| `A380X_SWITCH_LAPTOP_POWER_{LEFT,RIGHT}` | bool | 0/1 | R/W | Laptop power switches. **[A380-SPECIFIC]** |

### Cockpit door / Misc pedestal

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_COCKPIT_DOOR_LOCKED` | bool | 0/1 | R/W | Cockpit-door lock. |
| `A32NX_CABIN_READY` | bool | 0/1 | R/W | Cabin-ready signal. |
| `A32NX_CARGOSMOKE_{FWD,AFT}_DISCHARGED` | bool | 0/1 | R | Cargo-smoke agent discharged. |
| `A32NX_CARGOSMOKE_DISCH{1,2}LOCK_TOGGLE` | bool | 0/1 | R/W | Cargo-smoke discharge guard. |

---

## DISPLAYS (PFD / ND / EWD / SD / MFD / OIT)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A380X_MFD_{SIDE}_ACTIVE_PAGE` | string | URI (e.g. `fms/active/init`) | R | Active MFD page. `{SIDE}` = L/R. **[A380-SPECIFIC]** |
| `A32NX_FMS_PAX_NUMBER` | number | — | R | PAX number from FMS FUEL&LOAD. |
| `A32NX_FMS_SWITCHING_KNOB` | enum | 0=BOTH ON 2,1=NORM,2=BOTH ON 1 | R/W | FMS switching knob. |
| `A380X_FMS_DEST_EFOB_BELOW_MIN` | bool | 0/1 | R | FMS predicted dest fuel below min. **[A380-SPECIFIC]** |
| `A32NX_SPEEDS_MANAGED_SHORT_TERM_PFD` | number | — | R | Short-term managed speed on PFD. |
| `A32NX_CDS_CAN_BUS_{S}_{B}_AVAIL` | bool | 0/1 | R | CDS CAN bus available. `{S}`=1(capt)/2(F/O), `{B}`=1/2. |
| `A32NX_CDS_CAN_BUS_{S}_{B}_FAILURE` | bool | 0/1 | R/W | CDS CAN bus simulated failure. |
| `A32NX_ECAM_FAILURE_ACTIVE` | bool | 0/1 | R | ECAM failure active. |

### Display & integral lighting potentiometers (stock `LIGHT_POTENTIOMETER:n`, 0–100, R)

| Index | Surface | Index | Surface |
|---|---|---|---|
| 7 | Ambient/dome | 83 | Main-panel flood |
| 10 | Table light CAPT | 84 | Glareshield integral |
| 11 | Table light F/O | 85 | Cockpit integral (pedestal decals) |
| 76 | Pedestal flood | 87 | Glareshield LCD |
| 88 | PFD CAPT | 89 | ND CAPT |
| 90 | PFD F/O | 91 | ND F/O |
| 92 | ECAM/EWD (warning display) | 93 | SD (system display) |
| 94 | WX/Terr CAPT | 95 | WX/Terr F/O |
| 96 | Reading lights | 98 | MFD CAPT |
| 99 | MFD F/O | | |

`H:A380X_LIGHTING_KNOB_*_INCREASE/_DECREASE` events adjust the per-display knobs (L_PFD/ND/MFD/OIT,
R_PFD/ND/MFD, PANEL, PEDESTAL, LIGHT_GENERIC). Lighting presets: `A32NX_LIGHTING_PRESET_LOAD` /
`A32NX_LIGHTING_PRESET_SAVE` (number, >0 triggers, resets to 0).

### EXTERIOR LIGHTS (stock SimVars, EXT LT panel on overhead)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `LIGHT_NAV` | bool | 0/1 | R | Navigation lights. |
| `LIGHT_BEACON` | bool | 0/1 | R | Beacon. |
| `LIGHT_STROBE` | bool | 0/1 | R | Strobe. |
| `STROBE_0_AUTO` | bool | 0/1 | R/W | Strobe AUTO-mode enable. |
| `LIGHT_WING` | bool | 0/1 | R | Wing/scan lights. |
| `LIGHT_LOGO` | enum | 0–2 | R | Logo lights. |
| `LIGHT_TAXI:2` | bool | 0/1 | R | Taxi/runway-turnoff lights. |
| `A380X_OVHD_EXTLT_STBY_COMPASS_ICE_IND_SWITCH_POS` | enum | 0/1 | R/W | Ice-indicator / standby-compass light selector. **[A380-SPECIFIC]** |

(Exterior light *switches* are stock `K:` toggle events — `TOGGLE_NAV_LIGHTS`, `TOGGLE_BEACON_LIGHTS`,
`STROBES_TOGGLE`, `TOGGLE_WING_LIGHTS`, `LANDING_LIGHTS_TOGGLE`, `TOGGLE_TAXI_LIGHTS`, `TOGGLE_LOGO_LIGHTS`.)

---

## AIRCRAFT / EFB / PRESET helpers (not a cockpit panel, useful for automation)

| Variable | Type | Range / Enum | R/W | Description |
|---|---|---|---|---|
| `A32NX_AIRCRAFT_PRESET_LOAD` | enum | 1=Cold&Dark,2=Powered,3=Pushback,4=Taxi,5=Takeoff (0 cancels) | R/W | Load aircraft state preset (resets to 0). |
| `A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS` | number | 0.0–1.0 | R | Preset-load progress. |
| `A32NX_AIRCRAFT_PRESET_LOAD_EXPEDITE` | bool | 0/1 | R/W | Expedite preset loading. |
| `A32NX_EFB_BRIGHTNESS` | percent | 0–100 | R/W | EFB brightness. |
| `A32NX_PUSHBACK_SYSTEM_ENABLED` | bool | 0/1 | R/W | Pushback system enable. |
| `A32NX_PUSHBACK_SPD_FACTOR` | number | -1.0–1.0 | R/W | Pushback speed factor. |
| `A32NX_PUSHBACK_HDG_FACTOR` | number | -1.0–1.0 | R/W | Pushback turn factor. |
| `FBW_PILOT_SEAT` | enum | 0=Left,1=Right | R | Occupied seat. |
| `A380X_PILOT_IN_FO_SEAT` | bool | 0/1 | R | Pilot in F/O seat. **[A380-SPECIFIC]** |

---

## A32NX_ vs A380X_ prefix split — summary

- **The overwhelming majority of cockpit system vars reuse the `A32NX_` prefix** even on the A380X. This
  includes essentially all of ELEC, APU, FUEL (the FQMS/FQDC/quantity vars), HYD, BLEED/PNEU, COND,
  PRESSURIZATION, VENTILATION, ANTI-ICE, FIRE, OXYGEN, CALLS/EVAC, RCDR, ADIRS, the F/CTL PRIM/SEC
  pushbuttons, ENG start/FADEC, gear/autobrake/anti-skid, ECAM control panel, switching panel, and the
  pushback/preset helpers. FBW kept the historical `A32NX_` namespace and simply extended the index
  ranges (e.g. 1–4 engines/gens/IDGs/bleeds/FADECs, 11 fuel tanks, 16 brakes, 4 outflow valves, CPIOM
  B1–B4) rather than renaming them.

- **`A380X_` is reserved for genuinely new A380 hardware/logic that has no A320 analog**: the RMP radio
  panel (`A380X_RMP_*`), the EFIS-CP buttons and ND mode/range (`A380X_EFIS_*`), the multi-tank fuel
  transfer/jettison PBs (`A380X_OVHD_FUEL_*`), the MFD page URIs (`A380X_MFD_*`), the battery display
  knob (`A380X_OVHD_ELEC_BAT_SELECTOR_KNOB`), the ANN-LT/INT-LT enums (`A380X_OVHD_ANN_LT_POSITION`,
  `A380X_OVHD_INTLT_ANN`), storm light, remote CB, OIT side switches, laptop/keyboard, the standby-compass
  ice-indicator switch, and `A380X_FMS_DEST_EFOB_BELOW_MIN` / `A380X_PILOT_IN_FO_SEAT`.

- **`FBW_` prefix** is used for cross-aircraft framework signals — notably the RMP frequency/mode buses
  (`FBW_RMP_*`, `FBW_VHF*`) and `FBW_PILOT_SEAT`.

- **Stock MSFS SimVars/events** (no FBW prefix) carry the exterior lights (`LIGHT_NAV/BEACON/STROBE/
  WING/LOGO/TAXI`), all lighting potentiometers (`LIGHT_POTENTIOMETER:n`), wipers (`CIRCUIT_SWITCH_ON:n`),
  signs (`XMLVAR_SWITCH_OVHD_INTLT_*`), engine ignition/anti-ice (`TURB_ENG_IGNITION_SWITCH_EX1`,
  `ENG_ANTI_ICE`), anti-skid, transponder (`K:XPNDR_*`), and trim K-events.

- **Things genuinely A380-specific with NO A320 analog** (flagged **[A380-SPECIFIC]** inline): 4 engines
  (and their 4 thrust levers, gens, IDGs, FADECs, bleeds, fire loops); the Green/Yellow hydraulic
  architecture with EPUMP G/Y A/B; the 11-tank fuel system with trim-tank transfer and fuel jettison;
  the 4-battery / 4-ext-power / 6-bus-tie-contactor electrical architecture; the 2-deck (MAIN/UPPER)
  cabin conditioning with up to 8/7 zones; 4 outflow valves and CPIOM B1–B4 pressurization; the PRIM/SEC
  flight-control computer scheme (vs ELAC/SEC/FAC); BTV (Brake-To-Vacate) with body+wing main gear,
  16 brakes and bogie tilt; the 3-RMP radio suite; the EFIS-CP/ND glass; the NSS/OIT/KCCU avionics; and
  the MFD page-URI model.
