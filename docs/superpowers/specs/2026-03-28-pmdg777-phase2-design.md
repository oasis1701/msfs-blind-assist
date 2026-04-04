# PMDG 777 Phase 2: Bug Fixes, Missing Systems & Monitoring Enhancements

**Date:** 2026-03-28
**Branch:** feature/pmdg-777
**Scope:** Fix existing bugs/misconfigurations, add high-priority missing systems, enhance monitoring

## Overview

The PMDG 777 implementation has ~680 variables across 28 panels with ~230 events. This phase fixes functional bugs, corrects misconfigurations, and adds flight-critical missing systems: engine fire handles, doors, FMC data, aileron/rudder trim, and monitoring enhancements.

## Implementation Order

1. Bug fixes (Section 1)
2. Misconfigurations (Section 2)
3. Missing events for existing panels (Section 3)
4. Engine fire handles (Section 4)
5. Doors panel (Section 5)
6. FMC data (Section 6)
7. Aileron/rudder trim (Section 7)
8. Monitoring enhancements (Section 8)

---

## Section 1: Bug Fixes

### 1.1 MCP Monitoring Variables Not Registered

**Problem:** `MCP_IASBlank`, `MCP_VertSpeedBlank`, and `MCP_FPA` are handled in `ProcessSimVarUpdate` and hotkey readouts but never declared in `GetPMDGVariables()`. The announcement pipeline never fires for them.

**Fix:** Add three PMDGVar entries with `UpdateFrequency.Continuous`, `IsAnnounced = true`. No panel placement — background monitoring only.

### 1.2 AIR_AirCondReset Missing Event Mapping

**Problem:** Button exists in Air Conditioning panel, event `EVT_OH_AIRCOND_RESET_SWITCH` (69773) is in `EventIds`, but no entry in `_simpleEventMap`. Pressing it does nothing.

**Fix:** Add `["AIR_AirCondReset"] = "EVT_OH_AIRCOND_RESET_SWITCH"` to `_simpleEventMap`.

### 1.3 Forward Panel Source Selectors Missing Events

**Problem:** `ISP_Nav_L/R`, `ISP_DsplCtrl_L/R/C`, `ISP_AirDataAtt_L/R` have no event mappings. Controls render but can't be changed.

**Fix:** Add 7 events to `EventIds`:
- `EVT_FWD_NAV_SOURCE_L` (69800), `EVT_FWD_NAV_SOURCE_R` (69908)
- `EVT_FWD_DSPL_CTRL_SOURCE_L` (69801), `EVT_FWD_DSPL_CTRL_SOURCE_R` (69909)
- `EVT_FWD_AIR_DATA_ATT_SOURCE_L` (69802), `EVT_FWD_AIR_DATA_ATT_SOURCE_R` (69910)
- Center display control if applicable

Add corresponding `_simpleEventMap` entries. These are multi-position selectors — use existing wheel-step logic.

### 1.4 CDU Center Brightness Missing Event

**Problem:** `CDU_BrtKnob_C` has no event mapping. Only L and R brightness knobs work.

**Fix:** Add `EVT_CDU_C_BRITENESS` to `EventIds` and `_simpleEventMap`.

### 1.5 Warning Reset Duplicate Panel Placement

**Problem:** `WARN_Reset_L/R` appears in both MCP panel and Warning panel.

**Fix:** Remove from MCP panel controls. Keep only in Warning panel.

---

## Section 2: Misconfiguration Fixes

### 2.1 Inverted ValueDescriptions

**ENG_StartSelector_L/R:** SDK defines 0=START, 1=NORM. Implementation has 0=Norm, 1=Start. Fix: swap to match SDK.

### 2.2 Wrong Value Counts

**BRAKES_AutobrakeSelector:** Implementation has 7 positions (0-6), SDK only has 6 (0-5). Fix: remove position 6, rename position 5 to "Max Auto". Final: 0=RTO, 1=Off, 2=Disarm, 3=1, 4=2, 5=Max Auto.

### 2.3 Wrong Labels

- **LTS_IndLightsTest:** Position 0 is "Test" not "Off" (SDK: 0=TEST, 1=BRT, 2=DIM)
- **FUEL_FuelToRemainSelector:** Position 1 is "Neutral" not "Off"

### 2.4 Wrong Event Mapping

**LTS_MasterBrightSw:** Currently mapped to `EVT_OH_PANEL_LIGHT_CONTROL` (the overhead panel knob). Should map to `EVT_OH_MASTER_BRIGHT_PUSH` (72433). Add this event to `EventIds` if missing.

### 2.5 Missing ValueDescriptions

Add proper labels to variables currently showing raw numbers:

- **DSP_InbdDspl_L:** 0=ND, 1=NAV, 2=MFD, 3=EICAS
- **DSP_InbdDspl_R:** 0=EICAS, 1=MFD, 2=ND, 3=PFD
- **CHR_TimeDateSelector_L/R:** 0=UTC, 1=MAN
- **CHR_SetSelector_L/R:** 0=RUN, 1=HLDY, 2=MM, 3=HD
- **CHR_ETSelector_L/R:** 0=RESET, 1=HLD, 2=RUN
- **AIR_TempSelectorFlightDeck/Cabin:** Key positions: 0=Cold, 35=Neutral, 60=Warm, 70=Manual
- **AIR_CargoTempFwd/Aft:** 0=Off, 1=Low, 2=High
- **AIR_LdgAltSelector:** 0=Decr, 1=Neutral, 2=Incr
- **FCTL_Speedbrake:** Expand: 0=Down, 25=Armed, 50+=Deployed

### 2.6 Missing Chronometer Events

Add ~10 chronometer events to `EventIds` and `_simpleEventMap`:
- `EVT_CHRONO_L_CHR` (69803), `EVT_CHRONO_L_TIME_DATE_SELECT` (69804)
- `EVT_CHRONO_L_TIME_DATE_PUSH` (70353), `EVT_CHRONO_L_ET` (69805)
- `EVT_CHRONO_L_SET` (69806)
- `EVT_CHRONO_R_CHR` (69911), `EVT_CHRONO_R_TIME_DATE_SELECT` (69912)
- `EVT_CHRONO_R_TIME_DATE_PUSH` (72434), `EVT_CHRONO_R_ET` (69913)
- `EVT_CHRONO_R_SET` (69914)

---

## Section 3: Missing Events for Existing Panels

### 3.1 EFIS FPV/MTRS Buttons

Add 4 new momentary buttons to EFIS Captain and FO panels:
- `EFIS_FPV_Capt` / `EFIS_FPV_FO` — Flight Path Vector toggle
- `EFIS_MTRS_Capt` / `EFIS_MTRS_FO` — Meters toggle

Events: `EVT_EFIS_CPT_FPV` (69825), `EVT_EFIS_CPT_MTRS` (69826), `EVT_EFIS_FO_FPV` (69892), `EVT_EFIS_FO_MTRS` (69893).

Properties: `RenderAsButton = true`, `IsMomentary = true`, `UpdateFrequency.Never`.

Add corresponding data struct fields as continuous monitoring variables if present in SDK.

### 3.2 TCAS Panel Events

Add missing events to `EventIds`:
- `EVT_TCAS_ALTSOURCE` (70375)
- `EVT_TCAS_KNOB_L_OUTER` (70376), `EVT_TCAS_KNOB_L_INNER` (70377)
- `EVT_TCAS_IDENT` (70378)
- `EVT_TCAS_KNOB_R_OUTER` (70379), `EVT_TCAS_KNOB_R_INNER` (70380)
- `EVT_TCAS_MODE` (70381)
- `EVT_TCAS_TEST` (73123)
- `EVT_TCAS_XPNDR` (70383)

Verify existing XPDR variables map to correct TCAS events. Fix any mismatches.

### 3.3 Standby Instrument Events

Add missing ISFD/standby events:
- `EVT_STANDBY_ASI_KNOB` (69940), `EVT_STANDBY_ASI_KNOB_PUSH` (72712)
- `EVT_STANDBY_ALTIMETER_KNOB` (69943), `EVT_STANDBY_ALTIMETER_KNOB_PUSH` (72742)

Map to existing ISFD variables or add new variables as needed.

---

## Section 4: Engine Fire Handles (New Pedestal Panel)

### 4.1 Panel Structure

Add "Engine Fire" sub-panel under "Pedestal" section in `GetPanelStructure()`.

### 4.2 Variables

**Switches:**
- `FIRE_EngineHandle_1` / `_2` — Multi-position fire handles. ValueDescriptions: 0=Normal, 1=Pull, 2=Disch Left, 3=Disch Right. `UpdateFrequency.Continuous`.
- `FIRE_EngineHandleUnlock_1` / `_2` — Momentary unlock buttons. `RenderAsButton = true`, `IsMomentary = true`, `UpdateFrequency.Never`.

**Annunciators (continuous, announced):**
- `FIRE_annunENG_BTL_DISCH_1` / `_2` — Bottle discharge lights per engine.
- `FIRE_EngineHandleIsUnlocked_1` / `_2` — Handle unlock state.

### 4.3 Events

Use existing `EventIds` entries for fire handles. Add `_guardedMap` entries (unlock + pull is a guarded sequence, similar to APU handle pattern).

### 4.4 Announcement Logic

Add to `ProcessSimVarUpdate`: "Engine 1 fire handle pulled", "Engine 2 bottle discharged".

---

## Section 5: Doors Panel (New System)

### 5.1 Panel Structure

Add "Doors" as a new top-level section in `GetPanelStructure()` with a single "Door Status" panel.

### 5.2 Variables

From SDK `DOOR_state[16]` — create 16 individual PMDGVar entries. Door naming follows 777 layout (verify exact index-to-door mapping from SDK struct or in-sim testing):

- `DOOR_Entry_1L`, `DOOR_Entry_1R`, `DOOR_Entry_2L`, `DOOR_Entry_2R`
- `DOOR_Entry_3L`, `DOOR_Entry_3R`, `DOOR_Entry_4L`, `DOOR_Entry_4R`
- `DOOR_Entry_5L`, `DOOR_Entry_5R`
- `DOOR_Cargo_Fwd`, `DOOR_Cargo_Aft`, `DOOR_Cargo_Bulk`
- Plus remaining indices for any additional doors (777 variant dependent)

Plus: `DOOR_CockpitDoor` — cockpit door state.

All with `UpdateFrequency.Continuous`, `IsAnnounced = true`.

### 5.3 ValueDescriptions

Each door: `[0] = "Closed", [1] = "Open"`. Expand if SDK provides additional states (armed, transit).

### 5.4 Panel Controls

All doors in "Door Status" panel in `BuildPanelControls()`. Read-only — no events.

---

## Section 6: FMC Data

### 6.1 Continuous Monitoring (announced on change)

- `FMC_V1` (unsigned short, knots) — announce "V1 148 knots"
- `FMC_VR` (unsigned short, knots) — announce "VR 156 knots"
- `FMC_V2` (unsigned short, knots) — announce "V2 162 knots"
- `FMC_CruiseAlt` (unsigned int, feet) — announce "Cruise altitude 35000 feet"

All with `UpdateFrequency.Continuous`, `IsAnnounced = true`. No panel placement — background monitoring only.

### 6.2 ProcessSimVarUpdate

Suppress announcements when value is 0 (not yet set). Format with units.

### 6.3 Hotkey Readouts

Add three `HotkeyAction` entries:

- **ReadDistanceToTOD:** Read `FMC_DistanceToTOD` (float, nm). "52 miles to top of descent" or "Top of descent not available".
- **ReadDistanceToDest:** Read `FMC_DistanceToDest` (float, nm). "284 miles to destination".
- **ReadThrustLimitMode:** Read `FMC_ThrustLimitMode` (0-16). Map: 0=None, 1=TO, 2=TO 1, 3=TO 2, 4=D-TO, 5=D-TO 1, 6=D-TO 2, 7=CLB, 8=CLB 1, 9=CLB 2, 10=CRZ, 11=CON, etc.

---

## Section 7: Aileron/Rudder Trim

### 7.1 Variables

Add to "Control Stand" pedestal panel:

- `FCTL_AileronTrim` — 3-position switch. ValueDescriptions: 0=Left Wing Down, 1=Neutral, 2=Right Wing Down. Event: `EVT_FCTL_AILERON_TRIM` (70359).
- `FCTL_RudderTrim` — 3-position knob. ValueDescriptions: 0=Nose Left, 1=Neutral, 2=Nose Right. Event: `EVT_FCTL_RUDDER_TRIM` (70360).

Both multi-position selectors using existing wheel-step logic.

### 7.2 Rudder Trim Cancel

Verify existing `FCTL_RudderTrimCancel` button has correct event mapping. Add `EVT_FCTL_RUDDER_TRIM_CANCEL` to `EventIds` if missing.

---

## Section 8: Monitoring Enhancements

### 8.1 APU Running State

Add `APURunning` — continuous monitoring, announced. "APU running" / "APU shut down".

### 8.2 IRS Aligned

Add `IRS_aligned` — continuous monitoring, announced. "IRS aligned" on transition to true. Critical for knowing when navigation is ready.

### 8.3 Engine Start Valve

Add `ENG_StartValve_1` / `_2` — continuous monitoring annunciators. "Engine 1 start valve open" / "closed".

### 8.4 Fuel Quantity

Add `FUEL_QtyCenter`, `FUEL_QtyLeft`, `FUEL_QtyRight`, `FUEL_QtyAux` — continuous monitoring. Float values in pounds. No panel placement (already accessible via ReadFuelQuantity hotkey). Enables future low-fuel threshold announcements.

### 8.5 Wheel Chocks and Ground Connections

Add `WheelChocksSet` and `GroundConnAvailable` — continuous monitoring, announced. State change announcements for pre-departure awareness.

### 8.6 Brake Pressure

Add `BRAKES_BrakePressNeedle` (0-100 maps to 0-4000 PSI) — `UpdateFrequency.OnRequest` in Brakes panel. Custom formatting in `ProcessSimVarUpdate` to show PSI.

---

## Deferred to Future Phases

- Weather Radar panel
- Audio Control Panels (ACP) — Captain, FO, Observer
- Radio Control Panels — COM1/2/3 frequency tuning
- Data Link — ACARS accept/cancel/reject
- Sidewall panels — heaters, display brightness
- CDU Right and Center key events (if separate CDU forms needed)
- COMM receiver switches (bitmask decoding)
