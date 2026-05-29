# A32NX-documented controls/readouts MISSING from the A380X definition

**Purpose.** Cross-reference of controls/readouts documented for the **A32NX** (FBW docs site
flight-deck / systems API, and the GitHub `a320-simvars.md`) **plus** A380-specific items already
mined into `a380-simvars-catalog.md`, against what `FlyByWireA380Definition.cs` actually registers
in `GetVariables()`. Each item below is **NOT yet in the A380 definition** and is **plausibly shared**
(same `A32NX_` naming, or a stock SimVar the A380 reuses, on a system the A380 has).

**Method.** "Have" set = every key/Name registered in `GetVariables()` (lines 60–741 of the .cs) and
its panel mappings. "Documented" set = the three A32NX doc fetches + the A380 catalog. Gap = documented
controls/useful readouts the .cs does not register.

**Excluded** (per scope): the FCU/AFS (already ported, lines 619–686); ELAC/FAC (A380 uses PRIM/SEC,
already registered); pure ARINC429 SD/FMGC/ADIRS/FQMS data words (separate effort); raw EWD/SD message
code lines (already handled via lookup). Engine-parameter and per-bus readouts already present are not
re-listed.

**Confidence legend.** **High** = same var name confirmed in the A380 catalog or a stock SimVar the
A380 uses. **Medium** = A32NX-documented `A32NX_`/`XMLVAR_` var on a system the A380 has, very likely
carried over but not explicitly seen in the A380 catalog. **Low** = A320-architecture-flavored; confirm
it exists on the A380 dev build before relying on it.

Sources cited per item: **[FD]** flight-deck/systems API fetch, **[GH]** GitHub a320-simvars.md fetch,
**[CAT]** a380-simvars-catalog.md (already-mined A380 vars).

---

## CLOCK / CHRONO  (Instrument panel — "Source Switching" panel; only ET switch present today)

The code registers only `A32NX_CHRONO_ET_SWITCH_POS`. The chrono readouts and control events are missing.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_CHRONO_ELAPSED_TIME` | L:var number (s) | R | Clock **CHR** chrono elapsed time, −1 if empty. Surface as a "Chronometer" readout (format mm:ss). | High — [GH][FD]. Shared. |
| `A32NX_CHRONO_ET_ELAPSED_TIME` | L:var number (s) | R | Clock **ET** elapsed time, −1 if empty. Pairs with the ET switch already present. | High — [GH][FD]. Shared. |
| `H:A32NX_CHRONO_TOGGLE` | H:event | W | Start/stop the CHR chronometer (the CHRONO button). | Medium — [CAT] lists `H:A32NX_CHRONO_*`. |
| `H:A32NX_CHRONO_RST` | H:event | W | Reset the chronometer. | Medium — [CAT]. |
| `H:A32NX_CHRONO_DATE` | H:event | W | Clock DATE button. | Medium — [CAT]. |
| `H:A32NX_EFIS_{L,R}_CHRONO_PUSHED` | H:event | W | EFIS-CP chrono push per side (ND chrono). | Medium — [CAT] glareshield. |

---

## ISIS / Standby instrument  (Instrument panel — section header exists, NO vars registered)

The class comment claims ISIS coverage but `GetVariables()` registers **nothing** for it.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_ISIS_LS_ACTIVE` | L:var bool | R | ISIS LS scales shown. Toggled by `H:A32NX_ISIS_LS_PRESSED`. | High — [GH][FD][CAT]. |
| `A32NX_ISIS_BUGS_ACTIVE` | L:var bool | R | ISIS bugs page shown. Toggled by `H:A32NX_ISIS_BUGS_PRESSED`. | High — [GH][FD][CAT]. |
| `H:A32NX_ISIS_LS_PRESSED` / `_RELEASED` | H:event | W | Press/release ISIS LS button. | Medium — [CAT]. |
| `H:A32NX_ISIS_BUGS_PRESSED` / `_RELEASED` | H:event | W | Press/release ISIS BUGS button. | Medium — [CAT]. |
| `H:A32NX_ISIS_KNOB_CLOCKWISE` / `_ANTI_CLOCKWISE` / `_PRESSED` | H:event | W | ISIS baro knob (set standby altimeter). | Medium — [CAT]. |
| `A32NX_ISIS_BARO_MODE` | L:var enum | R/W | ISIS baro mode (STD vs set). | Medium — [CAT]. |
| `A32NX_ISIS_BARO_UNIT_INHG` | L:var bool | R/W | ISIS baro unit (inHg vs hPa). | Medium — [CAT]. |
| `A32NX_ISIS_BUGS_ALT_VALUE:{0,1}` / `_ACTIVE:{0,1}` | L:var feet / bool | R/W | ISIS altitude bugs (2). | Low — [GH][FD]. Niche. |
| `A32NX_ISIS_BUGS_SPD_VALUE:{0..3}` / `_ACTIVE:{0..3}` | L:var kts / bool | R/W | ISIS speed bugs (4). | Low — [GH][FD]. Niche. |

---

## BRAKES  (Instrument panel — "Autobrake" panel)

Brake **temps** (1–16) and autobrake selector are present. Missing the brake-fan control + status.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_BRAKE_FAN_BTN_PRESSED` | L:var bool | R/W | Brake-fan button. Useful pilot control (cool hot brakes before gear-up). | High — [GH][FD]. Shared system. |
| `A32NX_BRAKE_FAN_RUNNING` | L:var bool | R | Brake fan running status (announce). | High — [FD]. |
| `A32NX_BRAKES_HOT` | L:var bool | R | Brakes hot (>300 °C) — good auto-announce candidate. | High — [FD]. |
| `A32NX_HYD_BRAKE_ALTN_{LEFT,RIGHT,ACC}_PRESS` | L:var psi | R | Alternate-brake / accumulator pressure (parking-brake reserve). | High — [CAT] lists for A380. |

---

## GEAR  (Instrument panel — "Gear" panel)

Gear handle + gravity switch present. Missing handle-lock + gravity guards.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_GEAR_LEVER_LOCKED` | L:var bool | R | Gear lever locked (can't raise on ground). Announce. | High — [CAT]. |
| `A32NX_LG_GRVTY_MASTER_SWITCH_GUARD` | L:var bool | R | Gravity-extension master guard. | High — [CAT]. |
| `A32NX_LG_GRVTY_SWITCH_GUARD_{1,2}` | L:var bool | R/W | Gravity-extension guards (lift before turning the switch). | Medium — [CAT]. |

---

## PRESSURIZATION  (Overhead — "Pressurization" panel)

Manual PBs + ditching present. Missing the manual selector knobs and landing-elev knob.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB` | L:var feet | R/W | Manually-selected cabin target altitude (used when MAN ALTITUDE not AUTO). | High — [CAT]. |
| `A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB` | L:var fpm | R/W | Manually-selected cabin V/S. | High — [CAT]. |
| `A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE` | L:var psi | R | Cabin delta-P (already added — verify; if not, add). | High — present in code. |
| `A32NX_OVHD_PRESS_LDG_ELEV_KNOB` | L:var feet | R/W | Landing-elevation knob. **Low confidence on A380** — A380 is fully automatic; A320-only knob. | Low — [GH][FD]. Likely A320-only. |

---

## FUEL  (Overhead — "Fuel" panel)

Transfer/jettison PBs present. Missing the crossfeed PBs (A380 has 4) and jettison-valve status.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED_Pressed` | L:var bool | R/W | Crossfeed 1 momentary PB. | High — [CAT]. |
| `XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED{2,3,4}_Pressed` | L:var bool | R/W | Crossfeed 2–4 PBs (A380 4-crossfeed). | High — [CAT] A380-specific. |
| `A380X_OVHD_FUEL_JETTISON_IS_OPEN` | L:var bool | R | Jettison valve open status (announce during jettison). | High — [CAT]. |
| `A32NX_TOTAL_FUEL_VOLUME` | L:var gal | R | Total fuel volume (companion to the kg readout present). | Medium — [CAT]. |

---

## HYDRAULICS  (Overhead — "Hydraulics" panel)

Engine/electric pump PBs + RAT present. Missing the PTU PB and EPUMP OFF/extra controls.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_OVHD_HYD_PTU_PB_IS_AUTO` | L:var bool | R/W | PTU PB AUTO. | High — [CAT]. |
| `A32NX_OVHD_HYD_PTU_PB_HAS_FAULT` | L:var bool | R | PTU FAULT light. | High — [CAT]. |
| `A32NX_OVHD_HYD_EPUMP{G,Y}{A,B}_OFF_PB_IS_AUTO` | L:var bool | R/W | Green/Yellow electric-pump OFF PBs (paired with the ON PBs already present). | Medium — [CAT]. |

---

## VENTILATION  (Overhead — "Ventilation" panel)

Cabin-fans + air-extract PBs present. Missing the avionics blower/extract toggles.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_VENTILATION_BLOWER_TOGGLE` | L:var bool | R/W | Avionics-ventilation blower. | High — [CAT][FD]. |
| `A32NX_VENTILATION_EXTRACT_TOGGLE` | L:var bool | R/W | Avionics-ventilation extract. | High — [CAT][FD]. |
| `A32NX_VENTILATION_BLOWER_FAULT` / `_EXTRACT_FAULT` | L:var bool | R | Blower / extract fault (announce). | High — [CAT][FD]. |

---

## ANTI-ICE / PROBE HEAT  (Overhead — "Anti Ice" panel)

Engine/wing PBs + manual pitot heat present. Missing the wing-anti-ice status readouts.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON` | L:var bool | R | Wing anti-ice actually flowing (vs selected) — announce. | High — [FD]. |
| `A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT` | L:var bool | R | Wing anti-ice fault. | High — [FD]. |
| `A32NX_PITOT_HEAT_AUTO` | L:var bool | R | Pitot heat in AUTO (status of probe heat). | Medium — [GH][FD]. |

---

## OXYGEN  (Overhead — "Oxygen" panel)

Crew O2, mask deploy, timer reset present. Missing the timer-reset fault.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_OXYGEN_TMR_RESET_FAULT` | L:var bool | R | Oxygen timer-reset FAULT light. | High — [CAT][FD]. |

---

## CALLS / EVAC / CABIN  (Overhead — "Calls" panel; pedestal "Cockpit Door")

Emergency call + EVAC command/capt present. Missing the EVAC horn cutout and door slides.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_EVAC_COMMAND_FAULT` | L:var bool | R | EVAC command fault. | High — [FD]. |
| `PUSH_OVHD_EVAC_HORN` | L:var bool | R/W | EVAC horn cutout PB (silence the horn). | Medium — [GH][FD]. |
| `A32NX_SLIDES_ARMED` | L:var bool | R | Door slides armed (cabin-secure state) — announce. | High — [GH][FD]. Shared. |
| `A32NX_NO_SMOKING_MEMO` | L:var bool | R | NO SMOKING memo active. | Low — derived memo, niche. |

---

## RCDR / MISC OVERHEAD  (Overhead — "Recorder and Misc" panel)

Ground-control, ELT, avionics light, storm light, door video present. Missing several test/misc PBs.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_RCDR_TEST` | L:var bool | R/W | CVR/recorder test PB. | High — [GH][CAT]. |
| `A32NX_ELT_TEST_RESET` | L:var bool | R/W | ELT test/reset PB. | High — [GH][CAT]. |
| `A32NX_DFDR_EVENT_ON` | L:var bool | R/W | DFDR EVENT PB (momentary). | High — [CAT]. |
| `A32NX_CREW_HEAD_SET` | L:var bool | R/W | CVR crew headset PB. | Medium — [GH]. |
| `A32NX_SVGEINT_OVRD_ON` | L:var bool | R/W | SVGE INT override PB. | Medium — [GH][CAT]. |
| `A32NX_RAIN_REPELLENT_{LEFT,RIGHT}_ON` | L:var bool | R/W | Rain-repellent (windshield) — pilot control in rain. | High — [GH][CAT]. |
| `A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE` | L:var bool | R/W | NSS data-to-avionics toggle. | High — [CAT] A380-specific. |
| `A32NX_NSS_MASTER_OFF` | L:var bool | R/W | NSS master off. | High — [CAT] A380-specific. |
| `A380X_REMOTE_CB_CTRL` | L:var bool | R/W | Remote circuit-breaker control. | Medium — [CAT] A380-specific. |
| `A32NX_DLS_ON` | L:var bool | R/W | Data-loading selector on. | Low — [GH]. Niche. |

---

## WIPERS  (Overhead — NOT currently a panel; add a "Wipers" entry)

No wiper control of any kind is registered. Stock circuit SimVars on the A380 (per catalog).

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `K:ELECTRICAL_CIRCUIT_POWER_SETTING_SET:141` | K:event (0/75/100) | W | Wiper LEFT speed (0=off, 75=slow, 100=fast). | High — [CAT]. |
| `K:ELECTRICAL_CIRCUIT_POWER_SETTING_SET:143` | K:event (0/75/100) | W | Wiper RIGHT speed. | High — [CAT]. |
| `CIRCUIT_SWITCH_ON:141` / `:143` | SimVar bool | R | Wiper L/R on-state readback. | High — [CAT]. |

---

## SIGNS  (Overhead — "Signs" panel)

The three sign switches are present. One stock event is missing as an alternative.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE` | K:event | W | Stock seat-belt sign toggle (alternative to the XMLVAR switch). | Medium — [CAT]. Optional. |

---

## EFIS CONTROL PANEL  (Glareshield — "EFIS Control Panel" panel)

LS/VV/CSTR/ARPT/TRAF/navaid/ND-mode/range present. Missing the waypoint filter + overlay selectors.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A380X_EFIS_{L,R}_ACTIVE_FILTER` | enum (1=WPT,2=VORD,3=NDB) | R/W | Active waypoint/navaid symbol filter on the ND. | High — [CAT] A380-specific. |
| `A380X_EFIS_{L,R}_ACTIVE_OVERLAY` | enum (0=OFF/WX,1=WX,2=TERR) | R/W | WX / TERR overlay select. | High — [CAT] A380-specific. |
| `A32NX_EFIS_{L,R}_OANS_RANGE` | enum (0=MAX..4=MIN) | R/W | OANS (airport map) range; active when ND_RANGE=0/ZOOM. | Medium — [CAT]. |
| `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` | L:var bool | R/W | Baro unit inHg vs hPa per side. | Medium — [GH][FD]. |

---

## ECAM CONTROL PANEL  (Pedestal — "ECAM Control Panel" panel)

SD page selector + most buttons present. Missing the CHECK L/H, CHECK R/H buttons & SD status.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_BTN_CHECK_LH` | L:var bool | R/W | ECP **CHECK L/H** button (A380 checklist L). Code has ALL/ABNPROC/CL/CLR/CLR2/RCL/TOCONFIG/EMERCANC/UP/DOWN/MORE but not CHECK_LH/RH. | High — [CAT]. |
| `A32NX_BTN_CHECK_RH` | L:var bool | R/W | ECP **CHECK R/H** button. | High — [CAT]. |
| `A32NX_SD_MORE_SHOWN` | L:var bool | R | SD MORE page shown. | Medium — [CAT]. |
| `A32NX_FWS_ECP_FAILED` | L:var bool | R | ECP failed status. | Medium — [CAT]. |

---

## WEATHER RADAR / SURV  (Pedestal — "Weather Radar" panel)

PWS / multiscan / GCS present. The A380 SURV panel pushbuttons (the real TCAS/XPDR controls) are missing.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| WXR mode knob / sys switch (`KNOB_RADAR_MODE`, `SWITCH_RADAR_SYS`) | node | R/W | WXR mode (OFF/STBY/TST/ON) and system select. Need the underlying L:var/H:event from the dev build. | Low — [CAT] lists nodes, var names unconfirmed. |
| SURV pushbuttons `PUSH_SURV_TCAS_ABV/BLW/TAONLY`, `PUSH_SURV_GS_MODE`, `PUSH_SURV_WXR_TAWS_SYS{1,2}`, `PUSH_SURV_XPDR_TCAS_SYS{1,2}` | nodes | R/W | A380 SURV-panel pushbuttons (TCAS above/below/TA-only, GPWS G/S mode, WXR/TAWS & XPDR/TCAS system select). | Low — [CAT] nodes; confirm L:var/H:event names live. A380-specific. |

---

## TRANSPONDER / ATC  (Pedestal — "Transponder" panel)

XPNDR set/ident/mode present. The DCDU (datalink) ack is on the glareshield and missing.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_DCDU_ATC_MSG_ACK` | L:var bool | R/W | ATC datalink MSG acknowledge PB (glareshield). | Medium — [CAT]. |
| `A32NX_DCDU_ATC_MSG_WAITING` | L:var bool | R | ATC MSG waiting indicator — announce. | Medium — [CAT]. |

---

## RMP / AUDIO  (Pedestal — "RMP" panel)

RMP state + synced VHF freqs present. The per-channel TX/RX/CALL and nav-audio selectors are missing
(mostly read-only indicators useful for a blind pilot to know what they're transmitting on).

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A380X_RMP_{1,2,3}_{VHF,HF,TEL,INT,CAB,PA}_TX` | bool | R | Which channel is selected for transmit — announce. | High — [CAT] A380-specific. |
| `A380X_RMP_{1,2,3}_{CH}_CALL` | bool | R | Incoming-call indicator per channel — announce. | High — [CAT]. |
| `A380X_RMP_{1,2,3}_NAV_SELECT` | enum (0=ADF1..5=MKR) | R | Selected navaid for audio ident. | Medium — [CAT]. |
| `A380X_RMP_{1,2,3}_MECH_TX` | bool | R | MECH (interphone) transmit indicator. | Medium — [CAT]. |
| `H:A32NX_RMP_LEFT_TOGGLE_SWITCH` | H:event | W | RMP on/off toggle. | Medium — [CAT]. |

---

## CARGO SMOKE  (Pedestal — NOT currently a panel; could fold into "Fire" or new "Cargo Smoke")

No cargo-smoke control/status registered. (Engine/APU fire is covered; cargo smoke is separate.)

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_CARGOSMOKE_{FWD,AFT}_DISCHARGED` | L:var bool | R | Cargo-bay smoke agent discharged — announce. | High — [CAT][GH]. |
| `A32NX_CARGOSMOKE_DISCH{1,2}LOCK_TOGGLE` | L:var bool | R/W | Cargo-smoke discharge guard/lock. | Medium — [CAT]. |

---

## DISPLAYS / FMS SWITCHING  (Displays — "Status" panel)

A/THR status, PAX, ECAM-failure present. The FMS source-switching knob is missing.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_FMS_SWITCHING_KNOB` | enum (0=BOTH ON 2,1=NORM,2=BOTH ON 1) | R/W | FMS switching knob (which FMS feeds both sides). | High — [CAT] A380. |
| `A380X_FMS_DEST_EFOB_BELOW_MIN` | bool | R | FMS predicted destination fuel below minimum — announce. | High — [CAT] A380-specific. |
| `A32NX_FWC_FLIGHT_PHASE` | enum (0–10+) | R | FWC flight phase (finer than the FMGC phase already shown). | Low — [FD]. Optional. |

---

## V-SPEEDS / PERF readouts  (Displays — could add a "Speeds" panel)

None of the FBW-computed reference speeds are surfaced. These are high-value for a blind pilot who
can't see the PFD speed tape bugs.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_SPEEDS_VLS` | L:var kts | R | Lowest selectable speed (VLS). | High — [FD]. Shared FBW autoflight code. |
| `A32NX_SPEEDS_VAPP` | L:var kts | R | Approach speed (Vapp). | High — [FD]. |
| `A32NX_SPEEDS_GD` | L:var kts | R | Green-dot speed. | High — [FD]. |
| `A32NX_SPEEDS_F` / `A32NX_SPEEDS_S` | L:var kts | R | F-speed / S-speed (flap/slat retraction). | High — [FD]. |
| `A32NX_SPEEDS_VS` | L:var kts | R | Stall speed. | Medium — [FD]. |

---

## LIGHTING  (Overhead — "Interior Lighting")

ANN-LT and INT-LT enums present. Lighting presets (a quick "set the whole cockpit" control) are missing.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_LIGHTING_PRESET_LOAD` | L:var number (>0 triggers) | R/W | Load a saved lighting preset. | High — [GH][FD][CAT]. |
| `A32NX_LIGHTING_PRESET_SAVE` | L:var number (>0 triggers) | R/W | Save current lighting as preset. | Medium — [GH][FD][CAT]. |
| `A380X_OVHD_EXTLT_STBY_COMPASS_ICE_IND_SWITCH_POS` | enum 0/1 | R/W | Standby-compass / ice-indicator light selector. | Medium — [CAT] A380-specific. |
| `STROBE_0_AUTO` | SimVar bool | R/W | Strobe AUTO-mode enable (the A380 strobe has an AUTO position). | Medium — [CAT]. |

---

## AUTOMATION HELPERS  (not a cockpit panel, but very high-value for a blind pilot)

These let the user set the whole aircraft state or push back without sighted interaction.

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_AIRCRAFT_PRESET_LOAD` | enum 1–5 | R/W | Load state preset: 1 Cold&Dark, 2 Powered, 3 Pushback, 4 Taxi, 5 Takeoff (0 cancels). | High — [GH][FD][CAT]. |
| `A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS` | number 0.0–1.0 | R | Preset-load progress (announce % / done). | High — [CAT]. |
| `A32NX_AIRCRAFT_PRESET_LOAD_EXPEDITE` | bool | R/W | Expedite the preset load. | Medium — [CAT]. |
| `A32NX_PUSHBACK_SYSTEM_ENABLED` | bool | R/W | Enable/disable the internal pushback system. | High — [GH][FD][CAT]. |
| `A32NX_PUSHBACK_SPD_FACTOR` | number −1..1 | R/W | Pushback speed (−1 full back .. +1 full fwd). | High — [CAT]. |
| `A32NX_PUSHBACK_HDG_FACTOR` | number −1..1 | R/W | Pushback turn (−1 left .. +1 right). | High — [CAT]. |
| `A32NX_EFB_BRIGHTNESS` | percent | R/W | EFB tablet brightness. | Low — [CAT]. Niche. |
| `FBW_PILOT_SEAT` / `A380X_PILOT_IN_FO_SEAT` | enum / bool | R | Which seat is occupied. | Low — [CAT]. |

---

## KCCU / OIT KEYBOARD  (Pedestal — A380-specific; likely out of useful scope for now)

| Var / event | Type | R/W | What it does | Confidence / notes |
|---|---|---|---|---|
| `A32NX_KCCU_{L,R}_KBD_ON_OFF` | bool | R/W | KCCU keyboard on/off. | Medium — [CAT] A380-specific. |
| `A32NX_KCCU_{L,R}_CCD_ON_OFF` | bool | R/W | KCCU cursor-control device on/off. | Medium — [CAT] A380-specific. |
| `A380X_SWITCH_LAPTOP_POWER_{LEFT,RIGHT}` | bool | R/W | OIT laptop power switches. | Low — [CAT]. |

---

## Per-panel gap counts

| Panel / system | Gap items |
|---|---|
| Clock / Chrono | 6 |
| ISIS | 9 |
| Brakes | 4 |
| Gear | 3 |
| Pressurization | 4 |
| Fuel | 4 |
| Hydraulics | 3 |
| Ventilation | 4 |
| Anti-ice | 3 |
| Oxygen | 1 |
| Calls / EVAC / Cabin | 4 |
| RCDR / Misc overhead | 10 |
| Wipers | 4 |
| Signs | 1 |
| EFIS Control Panel | 4 |
| ECAM Control Panel | 4 |
| Weather Radar / SURV | 2 (groups) |
| Transponder / ATC (DCDU) | 2 |
| RMP / Audio | 5 (groups) |
| Cargo Smoke | 2 |
| Displays / FMS switching | 3 |
| V-speeds / Perf | 5 |
| Lighting | 4 |
| Automation helpers | 8 |
| KCCU / OIT keyboard | 3 |
| **Total** | **~102 individual vars/events (some are index/side groups)** |
