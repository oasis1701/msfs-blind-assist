# A380X Read-Only Fault / Status L:Vars — Per-Panel Catalog

On-demand (NOT auto-announced) readouts for MSFS Blind Assist, A380X.
All vars are `L:` local vars. **Bool** unless noted. Index expansions shown inline.

Sources:
- `fbw-a380x/docs/a380-simvars.md` (read fully) — cited as **[doc]**
- `fbw-a380x/src/wasm/systems/a380_systems/src/**.rs` (Rust systems, `get_identifier`/`write_by_name`) — cited as **[rust]**
- `fbw-a380x/src/systems/instruments/src/**` (TS publishers / SD pages) — cited as **[ts]**

**ARINC429 marking**: vars read via `Arinc429Word<...>` / `useArinc429Var` are marked **[ARINC429]** — EXCLUDE from this batch. All others are **[plain]** (Bool/Number/Enum) — include.

> Note: A380 flight control computers are **3× PRIM + 3× SEC** (not A320 ELAC/SEC). Anti-Ice has no dedicated FBW L:var fault — it uses MSFS A: vars (`ENG ANTI ICE:n`, `STRUCTURAL DEICE SWITCH`) and ECAM, so the Anti Ice panel yields 0 plain FBW status L:vars here.

---

## ELEC (Electrical ATA 24)

Plain:
- `A32NX_OVHD_ELEC_BAT_{1,2,ESS,APU}_PB_HAS_FAULT` (4) [doc]
- `A32NX_OVHD_ELEC_IDG_{1,2,3,4}_PB_HAS_FAULT` (4) [doc]
- `A32NX_OVHD_ELEC_ENG_GEN_{1,2,3,4}_PB_HAS_FAULT` (4) [doc][rust]
- `A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT` (1) [doc][rust]
- `A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT` (1) [doc]
- `A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT` (1) [doc][rust]
- `A32NX_OVHD_ELEC_IDG_{1,2,3,4}_PB_IS_DISC` (4) [doc]
- `A32NX_ELEC_ENG_GEN_{1,2,3,4}_IDG_IS_CONNECTED` (4) [doc]
- `A32NX_ELEC_{AC_1,AC_2,AC_3,AC_4,AC_ESS,AC_ESS_SCHED,AC_247XP,DC_1,DC_2,DC_ESS,DC_247PP,DC_HOT_1,DC_HOT_2,DC_HOT_3,DC_HOT_4,DC_GND_FLT_SVC}_BUS_IS_POWERED` (16) [doc]
- `A32NX_ELEC_{...}_POTENTIAL_NORMAL` (18: APU_GEN_1/2, ENG_GEN_1-4, EXT_PWR, STAT_INV, EMER_GEN, TR_1-4, BAT_1-4) [doc]
- `A32NX_ELEC_{...}_FREQUENCY_NORMAL` (9: APU_GEN_1/2, ENG_GEN_1-4, EXT_PWR, STAT_INV, EMER_GEN) [doc]
- `A32NX_ELEC_{...}_LOAD_NORMAL` (6: APU_GEN_1/2, ENG_GEN_1-4) [doc]
- `A32NX_ELEC_{...}_CURRENT_NORMAL` (8: TR_1-4, BAT_1-4) [doc]
- `A32NX_EXT_PWR_AVAIL:{1,2,3,4}` (4) [doc][rust]
- `A32NX_ELEC_CONTACTOR_{name}_IS_CLOSED` (≈48 named contactors) [doc] — bulk; usually not surfaced individually but available.

**ELEC plain count (excluding the 48 raw contactors): 92**  (incl. contactors: 140)

---

## APU (Auxiliary Power Unit ATA 49)

Plain:
- `A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT` (1) [ts]
- `A32NX_OVHD_APU_MASTER_SW_PB_IS_ON` (1) [ts]
- `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` (1) [ts] — APU AVAIL light
- `A32NX_OVHD_APU_START_PB_IS_ON` (1) [ts]
- `A32NX_APU_LOW_FUEL_PRESSURE_FAULT` (1) [ts]
- `A32NX_APU_FLAP_OPEN_PERCENTAGE` — Number [ts]
- `A32NX_APU_EGT_WARNING` — Number (warn threshold) [ts]
- `A32NX_APU_BLEED_AIR_VALVE_OPEN` (1) [ts]
- `A32NX_APU_AUTOEXITING_TEST_OK` / `_TEST_ON` / `_RESET` (3) [ts]
- `A32NX_FIRE_DETECTED_APU` (1) — fire (also under Fire) [doc][rust]

ARINC429 (EXCLUDE): `A32NX_APU_N` , `A32NX_APU_N2`, `A32NX_APU_EGT`, `A32NX_APU_FUEL_USED`.
Note: doc lists `A32NX_APU_N2` as Arinc429; instruments read `A32NX_APU_N` via `useArinc429Var` → ARINC. `A32NX_APU_N_RAW` is the underlying Number if a plain N is needed.

**APU plain count: 11**

---

## Fuel (ATA 28)

Plain:
- `A32NX_TOTAL_FUEL_QUANTITY` — Number kg [doc]
- `A32NX_TOTAL_FUEL_VOLUME` — Number gal [doc]
- `A32NX_COND...`/n/a

ARINC429 (EXCLUDE): `A32NX_FQMS_STATUS_WORD`, `A32NX_FQMS_TOTAL_FUEL_ON_BOARD`, `A32NX_FQMS_GROSS_WEIGHT`, `A32NX_FQMS_CENTER_OF_GRAVITY_MAC`, `A32NX_FQMS_{tank}_TANK_QUANTITY` (11 tanks), `A32NX_FQMS_{LEFT,RIGHT}_FUEL_PUMP_RUNNING_WORD`, `A32NX_FQDC_{1,2}_{tank}_TANK_QUANTITY`. [doc] — pump-running status is bit-packed in ARINC words (separate effort).

**Fuel plain count: 2**

---

## Hydraulics

Plain:
- `A32NX_OVHD_HYD_ENG_{1,2,3,4}AB_PUMP_DISC_PB_HAS_FAULT` (4) [doc]
- `A32NX_OVHD_HYD_ENG_{1,2,3,4}AB_PUMP_DISC_PB_IS_AUTO` (4) [doc]
- `A32NX_HYD_ENG_{1,2,3,4}AB_PUMP_DISC` (4) [doc][rust]
- `A32NX_OVHD_HYD_ENG_{1,3}A_PUMP_PB_HAS_FAULT` (per rust test refs) [rust]
- `A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE_SWITCH` (1) [ts]
- `A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE_SWITCH` (1) [ts]

**Hydraulics plain count: 16**

---

## Bleed Air (ATA 36)

Plain:
- `A32NX_OVHD_PNEU_ENG_{1,2,3,4}_BLEED_PB_HAS_FAULT` (4) [rust]
- `A32NX_OVHD_PNEU_ENG_{1,2,3,4}_BLEED_PB_IS_ON` (4) [rust]
- `A32NX_OVHD_PNEU_APU_BLEED_PB_HAS_FAULT` (1) [ts]
- `A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON` (1) [ts]
- `A32NX_PNEU_XBLEED_VALVE_{L,C,R}_OPEN` (3) [ts]
- `A32NX_APU_BLEED_AIR_VALVE_OPEN` (1) [ts] (shared w/ APU)

ARINC429/Number sensor (EXCLUDE per request — numeric pressure not a fault, optional): `A32NX_PNEU_ENG_{number}_INTERMEDIATE_TRANSDUCER_PRESSURE` [doc], `A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE` [ts] — these are plain Number psi if you want pressure readouts.

**Bleed Air plain count (fault/status bools): 14**  (+2 optional plain pressure Numbers)

---

## Air Conditioning (ATA 21)

Plain:
- `A32NX_OVHD_COND_PACK_{1,2}_PB_HAS_FAULT` (2) [rust]
- `A32NX_OVHD_COND_HOT_AIR_{1,2}_PB_HAS_FAULT` (2) [doc]
- `A32NX_OVHD_COND_HOT_AIR_{1,2}_PB_IS_ON` (2) [doc]
- `A32NX_OVHD_COND_RAM_AIR_PB_IS_ON` (1) [doc]
- `A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{FWD,BULK}_PB_HAS_FAULT` (2) [doc]
- `A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{FWD,BULK}_PB_IS_ON` (2) [doc]
- `A32NX_OVHD_CARGO_AIR_HEATER_PB_HAS_FAULT` (1) [doc]
- `A32NX_OVHD_CARGO_AIR_HEATER_PB_IS_ON` (1) [doc]
- `A32NX_COND_FDAC_{1,2}_CHANNEL_{1,2}_FAILURE` (4) [doc]
- `A32NX_COND_TADD_CHANNEL_{1,2}_FAILURE` (2) [doc]
- (temps `A32NX_COND_{zone}_TEMP` etc. are plain Number readouts, not faults — available if desired)

ARINC429 (EXCLUDE): `A32NX_COND_CPIOM_B{1-4}_AGS/TCS/VCS/CPCS_DISCRETE_WORD` (16 words; contain pack-operating, fan-fault, INOP bits). [doc]

**Air Conditioning plain count: 19**

---

## Pressurization (part of ATA 21)

Plain:
- `A32NX_PRESS_OCSM_{1,2,3,4}_CHANNEL_{1,2}_FAILURE` (8) [doc]
- `A32NX_PRESS_OCSM_{1,2,3,4}_AUTO_PARTITION_FAILURE` (4) [doc]
- `A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO` (1) [doc]
- `A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO` (1) [doc]
- `A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE` — Number psi [doc]
- `A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB` — Number feet [doc]
- `A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB` — Number fpm [doc]

ARINC429 (EXCLUDE): `A32NX_PRESS_CABIN_ALTITUDE_{B1-B4}`, `_TARGET_{B1-B4}`, `A32NX_PRESS_CABIN_VS_{B1-B4}`, `_TARGET_{B1-B4}`, `A32NX_PRESS_CABIN_DELTA_PRESSURE_{B1-B4}`; CPCS warn bits live in `COND_CPIOM_B*_CPCS_DISCRETE_WORD`. [doc]

**Pressurization plain count: 16**

---

## Ventilation (part of ATA 21)

Plain:
- `A32NX_VENT_{FWD,AFT}_VCM_CHANNEL_{1,2}_FAILURE` (4) [doc]
- `A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN` (1) [doc]
- `A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON` (1) [doc]

ARINC429 (EXCLUDE): VCS fan-fault/heater-fault bits in `COND_CPIOM_B*_VCS_DISCRETE_WORD`. [doc]

**Ventilation plain count: 6**

---

## Anti Ice

No dedicated FBW L:var fault/status on A380 (uses MSFS A: vars + ECAM).
Available status (MSFS A: vars, not L:):
- `A:ENG ANTI ICE:{1,2,3,4}` — Bool [ts]
- `A:STRUCTURAL DEICE SWITCH` (wing anti-ice) — Bool [ts]

**Anti Ice plain FBW-L:var count: 0**  (2 MSFS A: vars usable)

---

## Fire (ATA 26)

Plain:
- `A32NX_FIRE_DETECTED_ENG{1,2,3,4}` (4) [doc][rust]
- `A32NX_FIRE_DETECTED_APU` (1) [doc][rust]
- `A32NX_FIRE_DETECTED_MLG` (1) [doc][rust]
- `A32NX_APU_ON_FIRE` (1) [doc][rust]
- `A32NX_MLG_ON_FIRE` (1) [doc][rust]
- `A32NX_ENG_{1,2,3,4}_ON_FIRE` (4) [rust]
- `A32NX_FIRE_BUTTON_ENG{1,2,3,4}` (4) — PB released [doc][rust]
- `A32NX_FIRE_BUTTON_APU` (1) [doc]
- `A32NX_OVHD_FIRE_SQUIB_{1,2}_{ENG_1..4,APU_1}_IS_ARMED` (10) [doc][rust]
- `A32NX_OVHD_FIRE_SQUIB_{1,2}_{ENG_1..4,APU_1}_IS_DISCHARGED` (10) [doc][rust]
- `A32NX_OVHD_FIRE_AGENT_{1,2}_{ENG_1..4,APU_1}_IS_PRESSED` (10) [doc]
- `A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED` (1) [doc][rust]

ARINC429 (EXCLUDE): `A32NX_FIRE_FDU_DISCRETE_WORD` (loop A/B faults per zone). [doc]

**Fire plain count: 58**

---

## ADIRS (Inertial / Air Data)

Plain:
- `A32NX_ADIRS_ADIRU_{1,2,3}_STATE` — Enum (0 Off / 1 Aligning / 2 Aligned) [ts]
- `A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME` — Number seconds [common, already used by app]
- `A32NX_OVHD_ADIRS_IR_{1,2,3}_PB_IS_ON` style / `A32NX_OVHD_ADIRS_*` knob/PB — see EFIS/overhead [ts]

ARINC429 (EXCLUDE): all `A32NX_ADIRS_ADR_{1,2,3}_*` (airspeed, altitude, SAT/TAT, mach, AoA, pressures) and `A32NX_ADIRS_IR_{1,2,3}_*` (heading, attitude, position, GS, VS, wind, MAINT_WORD). [ts]

**ADIRS plain count: ~4** (3 ADIRU_STATE + alignment time; overhead PB/knob extra)

---

## Flight Control Computers (ATA 27)

Plain (A380 = 3 PRIM + 3 SEC):
- `A32NX_PRIM_{1,2,3}_HEALTHY` (3) [ts]
- `A32NX_SEC_{1,2,3}_HEALTHY` (3) [ts]

ARINC429 (EXCLUDE): `A32NX_FCDC_{1,2}_DISCRETE_WORD_1` (PRIM/SEC/law/fault bits), `A32NX_FCDC_{1,2}_FG_DISCRETE_WORD_4/8`, `A32NX_SFCC_{1,2}_SLAT_FLAP_ACTUAL_POSITION_WORD` (slat/flap fault & jam bits). [doc][ts]
Plain flap selection: `A32NX_FLAPS_CONF_INDEX` (Number, debug only), `A32NX_FLAPS_HANDLE_INDEX` (Number) [doc][ts].

**Flight Control Computers plain count: 6** (+ FLAPS_HANDLE_INDEX if desired)

---

## Gear (ATA 32)

Plain:
- `A32NX_LGCIU_{1,2}_{LEFT,RIGHT}_GEAR_DOWNLOCKED` (4) [a380 source]
- `A32NX_LGCIU_{1,2}_NOSE_GEAR_COMPRESSED` (2) [ts]
- `A32NX_LGCIU_1_{LEFT,RIGHT}_GEAR_COMPRESSED` (2) [ts]
- `A32NX_GEAR_HANDLE_POSITION` — Number 0..1 [common]
- `A32NX_GEAR_{LEFT,RIGHT,CENTER}_POSITION` — Number 0..1 [common]
- `A32NX_SECONDARY_GEAR_DOOR_{side}_POSITION` — Number [rust]
- `A32NX_PARK_BRAKE_LEVER_POS` (1) [ts]

ARINC429 (EXCLUDE): `A32NX_LGCIU_{1,2}_DISCRETE_WORD_1/2` (downlock/uplock bits — but plain `*_GEAR_DOWNLOCKED` bools above cover the need). [ts]
Note: nose-gear DOWNLOCKED bool not exposed separately on A380 (only L/R + nose compressed); use LGCIU discrete word bit for nose downlock if required (ARINC).

**Gear plain count: ~12**

---

## Autobrake

Plain:
- `A32NX_AUTOBRAKES_SELECTED_MODE` — Number (0 DISARM..5 HIGH) [doc]
- `A32NX_AUTOBRAKES_ARMED_MODE` — Number (0 DISARM..6 RTO) [doc]
- `A32NX_AUTOBRAKES_DISARM_KNOB_REQ` (1) [doc]
- `A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED` (1) [doc]
- `A32NX_BTV_STATE` — Enum (0 DISABLED..4 END OF BRAKING) [doc]
- `A32NX_AUTOBRAKE_INSTINCTIVE_DISCONNECT` (1) [rust]
- Brake temps: `A32NX_BRAKE_TEMPERATURE_{1..16}` / `A32NX_REPORTED_BRAKE_TEMPERATURE_{1..16}` — Number °C [doc]

**Autobrake plain count: 6** (+32 brake-temp Numbers if surfacing temps)

---

## Per-Panel Plain Count Summary

| Panel | Plain fault/status vars |
|---|---|
| ELEC | 92 (140 incl. 48 contactors) |
| APU | 11 |
| Fuel | 2 |
| Hydraulics | 16 |
| Bleed Air | 14 (+2 opt. pressure) |
| Air Conditioning | 19 |
| Pressurization | 16 |
| Ventilation | 6 |
| Anti Ice | 0 (2 MSFS A: vars) |
| Fire | 58 |
| ADIRS | ~4 |
| Flight Control Computers | 6 |
| Gear | ~12 |
| Autobrake | 6 (+32 brake temps) |

---

## A32NX "Common" Vars Present on A380 but OMITTED from a380-simvars.md

(Verified used by A380 instrument publishers — fbw-common-style shared vars. All plain unless noted.)

**High value:**
- `A32NX_PRIM_{1,2,3}_HEALTHY`, `A32NX_SEC_{1,2,3}_HEALTHY` — FCC health (Bool). [SD Fctl ComputerIndication.tsx, OIT]
- `A32NX_FWS1_IS_HEALTHY`, `A32NX_FWS2_IS_HEALTHY` — FWС health (Bool). [EwdSimvarPublisher]
- `A32NX_FWC_FLIGHT_PHASE` — Enum; `A32NX_FMGC_FLIGHT_PHASE` — Enum. [PFD/EWD/MFD publishers]
- `A32NX_LGCIU_{1,2}_{LEFT,RIGHT}_GEAR_DOWNLOCKED` (Bool), `A32NX_LGCIU_{1,2}_NOSE_GEAR_COMPRESSED`, `A32NX_LGCIU_1_{LEFT,RIGHT}_GEAR_COMPRESSED` (Bool). [PFD/PseudoFwc]
- `A32NX_GEAR_HANDLE_POSITION`, `A32NX_GEAR_{LEFT,RIGHT,CENTER}_POSITION` (Number 0..1). [common]
- `A32NX_ADIRS_ADIRU_{1,2,3}_STATE` (Enum: Off/Aligning/Aligned), `A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME` (Number s). [IRS pages]
- `A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE_SWITCH`, `A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE_SWITCH` (Bool). [PseudoFwc/OIT/PFD]
- `A32NX_PARK_BRAKE_LEVER_POS` (Bool). [OIT]

**Engine / thrust (numeric readouts):**
- `A32NX_ENGINE_STATE:{1-4}` (Enum), `A32NX_ENGINE_N1:{1-4}`, `A32NX_ENGINE_N2:{1-4}`, `A32NX_ENGINE_EGT:{1-4}`, `A32NX_ENGINE_FF:{1-4}` (Number). [EWD/SD publishers]
- `A32NX_AUTOTHRUST_STATUS` (Enum), `A32NX_AUTOTHRUST_MODE` (Enum), `A32NX_AUTOTHRUST_TLA:{1-4}` (Number), `A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE` (Number). [PFD/EWD]
- `A32NX_REVERSER_{1-4}_DEPLOYED`, `A32NX_REVERSER_{1-4}_DEPLOYING` (Bool). [EWD]

**FMA / AP (for FMA readout panel if added):**
- `A32NX_FMA_LATERAL_MODE`, `A32NX_FMA_VERTICAL_MODE`, `A32NX_FMA_LATERAL_ARMED`, `A32NX_FMA_VERTICAL_ARMED`, `A32NX_FMA_MODE_REVERSION`, `A32NX_FMA_SPEED_PROTECTION_MODE` (Number/Enum). [PFD]
- `A32NX_AUTOPILOT_1_ACTIVE`, `A32NX_AUTOPILOT_2_ACTIVE` (Bool). [PFD]

**Most valuable to add first (per the request):** the FCC health (`PRIM/SEC_{n}_HEALTHY`), FWС health (`FWS{1,2}_IS_HEALTHY`), gear downlock (`LGCIU_*_GEAR_DOWNLOCKED`), `FWC_FLIGHT_PHASE`, `ADIRS_ADIRU_*_STATE` + `REMAINING_IR_ALIGNMENT_TIME`, and the HYD section pressure switches — all plain, all present on the A380, none in a380-simvars.md.

> Caveat: the fbw-common Rust crate (`fbw-common/src/wasm/systems`) is NOT checked out in this clone (referenced via Cargo path but absent). Common-var names above were verified from the A380 **instrument** source where they are actually consumed, which guarantees they exist on the A380; exact value types confirmed from the publisher `SimVarValueType` and `useArinc429Var` usage.
