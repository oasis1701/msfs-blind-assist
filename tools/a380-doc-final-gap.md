# A380X Doc Final Gap

Exhaustive top-to-bottom scrape of BOTH FlyByWire A380X API doc pages
(`a380x-flight-deck-api` and `a380x-systems-api`), cross-referenced against
every variable/event string registered in `FlyByWireA380Definition.GetVariables()`.

Every documented row that is NOT already registered in our A380 definition is
listed below, organized by the doc's own section order (top to bottom),
flight-deck page first, then systems page. Already-registered rows are omitted.

Each entry: exact name | type | R/W | one-line purpose | confidence tag.
- **High** = clearly useful pilot control/readout we should add.
- **Low** = internal/debug/ARINC429-data-word/duplicate/already-covered-by-equivalent.

Three buckets are separated explicitly at the bottom:
A) WORTH ADDING (plain control/readout)
B) ARINC429 DATA WORDS (need the SD decoder; do not surface raw)
C) SKIP (debug/internal/FCU-already-done/duplicate/stock-event-we-already-cover)

NOTE on "already covered": where the doc lists a stock MSFS event or SimVar
(e.g. GEAR_UP, FLAPS_SET, AP_* events, KOHLSMAN_*, ANTISKID_BRAKES_TOGGLE) that
we deliberately drive through the L:var / custom-event path instead, it's tagged
Low/skip. Where the doc gives a NEW readout we genuinely lack, it's High.

================================================================================
# PAGE 1 — FLIGHT DECK API
================================================================================

## OVERHEAD

### ADIRS Panel
(all 6 rows already registered) — none missing.

### AIR Panel
- `XMLVAR_Momentary_PUSH_OVHD_AIRCOND_APUBLEED_Pressed` | bool (button) | R/W | APU bleed pushbutton physical position. | Low (we drive APU bleed via A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON; duplicate)
- `BLEED AIR APU` | SimVar bool | R | Stock APU bleed-air state. | Low (duplicate of our PB var)
- `ENGINE_BLEED_AIR_SOURCE_TOGGLE` | MSFS event | — | Toggle bleed source. | Low (skip; we use AUTO PB vars)
- `BLEED AIR ENGINE:{NUM}` | SimVar bool | R | Stock per-engine bleed-air state. | Low (duplicate of A32NX_OVHD_PNEU_ENG_{n}_BLEED_PB_IS_AUTO)
- `XMLVAR_Momentary_PUSH_OVHD_AIRCOND_ENG{NUM}BLEED_Pressed` | bool (button) | R/W | Per-engine bleed pushbutton physical position. | Low (duplicate of our AUTO PB)
- `A32NX_COND_PACK_{NUM}_FLOW_VALVE_1_IS_OPEN` | bool | R | Pack 1/2 flow valve 1 open. | **High** (pack-valve status readout we lack)
- `A32NX_COND_PACK_{NUM}_FLOW_VALVE_2_IS_OPEN` | bool | R | Pack 1/2 flow valve 2 open. | **High** (pack-valve status readout we lack)
- `A32NX_COND_CKPT_DUCT_TEMP` | celsius | R | Cockpit duct outlet temperature. | **High** (duct-temp readout we lack)
- `A32NX_OVHD_COND_FWD_SELECTOR_KNOB` | number (0-400) | R/W | FWD cabin temp selector (we register CABIN/CKPT/CARGO knobs but not this FWD one). | **High**
- `A32NX_COND_{DECK}_DECK_{NUM}_DUCT_TEMP` | celsius | R | Per-deck duct outlet temps (main 1-8 / upper 1-7). | **High** (duct-temp readouts; we have zone temps but not duct)
- `A32NX_OVHD_COND_RAM_AIR_PB_IS_ON_LOCK` | bool (guard) | R | Ram air switch guard. | Low (switch-guard internal)
- `A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON_LOCK` | bool (guard) | R | Air-extract switch guard. | Low (switch-guard internal)

### ANTI ICE Panel
- `TOGGLE_STRUCTURAL_DEICE` | MSFS event | — | Stock structural de-ice toggle. | Low (skip; A380 has no structural deice control we surface)
- `STRUCTURAL DEICE SWITCH` | SimVar bool | R/W | Stock structural de-ice state. | Low (skip)
- `XMLVAR_MOMENTARY_PUSH_OVHD_PROBESWINDOW_PRESSED` | bool (button) | R/W | Probe/window heat pushbutton physical state. | Low (we already drive A32NX_MAN_PITOT_HEAT)

### APU Panel
(all 4 rows registered) — none missing. (We have MASTER_SW_PB_IS_ON, START_PB_IS_ON, START_PB_IS_AVAILABLE, MASTER_SW_PB_HAS_FAULT.)

### CALLS Panel
- `A32NX_CALLS_EMER_ON_LOCK` | bool (guard) | R | Emergency-call switch guard. | Low (switch-guard internal)

### ELEC Panel
- `TOGGLE_ALTERNATOR:{NUM}` | MSFS event | — | Stock per-engine generator toggle. | Low (skip; A380 gen control is via PB faults/IDG)
- `GENERAL ENG MASTER ALTERNATOR:{NUM}` | SimVar bool | R/W | Stock engine generator on/off. | **High** (per-engine generator on/off readout/control — we have ENG_GEN fault + IDG-connected but not the gen switch itself)
- `APU_GENERATOR_SWITCH_TOGGLE` | MSFS event | — | Stock APU gen toggle. | Low (skip)
- `APU_GENERATOR_SWITCH_SET` | MSFS event | — | Stock APU gen set. | Low (skip)
- `APU GENERATOR SWITCH` | SimVar bool | R/W | APU generator on/off. | **High** (APU gen line control/readout we lack)
- `A32NX_OVHD_ELEC_APU_GEN_{NUM}_PB_HAS_FAULT` | bool | R | APU generator 1/2 fault. | **High** (APU-gen fault readout we lack; A380 has 2 APU gens)
- `A32NX_OVHD_ELEC_BUS_TIE_PB_HAS_FAULT` | bool | R | Bus-tie fault. | **High** (fault readout we lack; we register the PB but not its fault)
- `A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL_LOCK` | bool (guard) | R | AC ESS feed switch guard. | Low (switch-guard internal)
- `A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_AUTO` | bool | R/W | Commercial PB AUTO (doc lists _IS_AUTO; we registered _IS_ON). | Low (likely doc/skin variance — we already cover Commercial via _IS_ON; verify spelling, not a new control)
- `A32NX_OVHD_ELEC_COMMERCIAL_PB_HAS_FAULT` | bool | R | Commercial PB fault. | **High** (fault readout we lack)

### ENG START Panel
- `TURB ENG IGNITION SWITCH EX1:{NUM}` | MSFS var int(0-2) | R | Stock per-engine ignition-switch position readback. | **High** (readback of engine-mode position per engine — we write XMLVAR_ENG_MODE_SEL but have no readback)
- `TURBINE_IGNITION_SWITCH_SET{NUM}` | MSFS event | — | Per-engine ignition set (we already SEND this in HandleUIVariableSet). | Low (already used internally)

### ENG FADEC Panel
- `A32NX_OVHD_FADEC_{NUM}_LOCK` | bool (guard) | R | FADEC ground-power switch guard. | Low (switch-guard internal)

### EXT LT Panel
- `LIGHTING_STROBE_0` | input event int(0-2) | R/W | Strobe OFF/AUTO/ON as a settable input event. | **High** (lets us set the 3-position strobe directly — we only have STROBES_TOGGLE + STROBE_0_AUTO)
- `STROBES_SET` / `STROBES_ON` / `STROBES_OFF` | input events | — | Strobe set/on/off (no AUTO). | Low (skip; STROBES_TOGGLE + STROBE_0_AUTO cover it)
- `LIGHTING_BEACON_0` | input event | R/W | Beacon as settable input event. | Low (we have TOGGLE_BEACON_LIGHTS + LIGHT BEACON)
- `BEACON_LIGHTS_SET/ON/OFF` | input events | — | Beacon set/on/off. | Low (skip; toggle covered)
- `LIGHTING_WING_0` | input event | R/W | Wing light input event. | Low (toggle covered)
- `WING_LIGHTS_SET/ON/OFF` | input events | — | Wing set/on/off. | Low (skip)
- `LIGHTING_NAV_0` | input event | R/W | Nav light input event. | Low (toggle covered)
- `NAV_LIGHTS_SET/ON/OFF` | input events | — | Nav set/on/off. | Low (skip)
- `LIGHTING_LOGO_0` | input event | R/W | Logo light input event. | Low (toggle covered)
- `LOGO_LIGHTS_SET/ON/OFF` | input events | — | Logo set/on/off. | Low (skip)
- `LANDING_TAXI_2` | input event int(0-1) | R/W | Taxi-light input event. | Low (we have TOGGLE_TAXI_LIGHTS + LIGHT TAXI:2)
- `LIGHTING_LANDING_2` | input event int(0-1) | R/W | Landing light 2 OFF/ON. | Low (we have LANDING_LIGHTS_TOGGLE + LIGHT LANDING)
- `LIGHTING_LANDING_1` | input event int(0-2) | R/W | Landing/taxi selector: 0=T.O,1=TAXI,2=OFF. | **High** (this is the actual 3-position nose landing/taxi light selector — gives a real settable position we lack)

### FIRE Panel
- `A32NX_FIRE_GUARD_APU1` | bool | R/W | APU fire pushbutton guard. | Low (switch-guard; but settable — borderline, keep Low)
- `A32NX_FIRE_BUTTON_APU1` | bool | R/W | (doc spells APU button as _APU1; we registered _APU). | Low (spelling variance of one we have)
- `A32NX_FIRE_SQUIB_1_APU_1_IS_ARMED` | bool | R | APU agent squib armed (we have APU _IS_DISCHARGED but not _IS_ARMED). | **High** (APU squib-armed readout we lack)
- `A32NX_OVHD_FIRE_AGENT_1_APU_1_IS_PRESSED` | bool | R/W | APU agent discharge pushbutton pressed. | **High** (APU agent-discharge control/readout we lack)
- `A32NX_OVHD_FIRE_AGENT_1_APU_1_IS_DISCHARGED` | bool | R | APU agent discharged (we DO have this). | Low (already registered)
- `A32NX_FIRE_GUARD_ENG{NUM}` | bool | R/W | Per-engine fire pushbutton guard. | Low (switch-guard)
- `A32NX_OVHD_FIRE_AGENT_{NUM}_ENG_{NUM}_IS_PRESSED` | bool | R/W | Per-engine agent 1/2 discharge pushbutton pressed. | **High** (engine fire-agent discharge control/readout we lack — we have squib armed/discharged but not the AGENT press)

### F/CTL Panel
(PRIM/SEC pushbuttons registered) — none missing.

### FUEL Panel
- `ELECTRICAL_BUS_TO_CIRCUIT_CONNECTION_TOGGLE` | MSFS event | — | Fuel-pump bus toggle. | Low (skip; internal)
- `FUELSYSTEM_VALVE_TOGGLE` | MSFS event | — | Crossfeed valve toggle. | Low (we drive crossfeed via XMLVAR push vars)
- `A380X_OVHD_FUEL_OUTTTK_XFR_PB_IS_AUTO_LOCK` | bool (guard) | R | Outer-tank transfer switch guard. | Low (switch-guard)
- `A380X_OVHD_FUEL_MIDTK_XFR_PB_IS_AUTO_LOCK` | bool (guard) | R | Mid-tank transfer guard. | Low
- `A380X_OVHD_FUEL_INRTK_XFR_PB_IS_AUTO_LOCK` | bool (guard) | R | Inner-tank transfer guard. | Low
- `A380X_OVHD_FUEL_TRIMTK_XFR_PB_IS_AUTO_LOCK` | bool (guard) | R | Trim-tank transfer guard. | Low

### HYD Panel
- `A32NX_OVHD_HYD_ENG_1AB_PUMP_DISC_PB_IS_AUTO_LOCK` | bool (guard) | R | Pump-disc switch guard. | Low
- `A32NX_OVHD_HYD_ENG_{ENG}AB_PUMP_DISC_PB_IS_AUTO` | bool | R/W | Engine pump-disconnect pushbutton AUTO. | **High** (the actual pump-disconnect control — we have its FAULT + DISC feedback but not the settable AUTO PB)
- `A32NX_OVHD_HYD_EPUMP{G|Y}{A|B}_ON_PB_IS_AUTO_LOCK` | bool (guard) | R | Electric-pump ON switch guard. | Low
- `A32NX_OVHD_HYD_EPUMP{G|Y}{A|B}_ON_PB_HAS_FAULT` | bool | R | Electric-pump ON fault. | **High** (epump fault readout we lack)
- `A32NX_OVHD_HYD_EPUMP{G|Y}{A|B}_OFF_PB_IS_AUTO` | bool | R/W | Electric-pump OFF pushbutton. | Low (paired OFF half of the ON PB we already have; borderline — keep Low)
- `A32NX_OVHD_HYD_EPUMP{G|Y}{A|B}_OFF_PB_HAS_FAULT` | bool | R | Electric-pump OFF fault. | Low (duplicate-ish of ON fault)

### INT Panel
(STBY_COMPASS_ICE_IND switch + INTLT_ANN registered) — none missing.

### OXYGEN Panel
(MASKS_DEPLOYED, PASSENGER_LIGHT_ON, PUSH_OVHD_OXYGEN_CREW registered) — none missing.

### RCDR Panel
(GROUND_CONTROL_ON registered) — none missing.

### Reading Lights Panel
- `LIGHT POTENTIOMETER:96` | MSFS var int(0-100) | R | Reading-light brightness. | Low (brightness pot; not a useful blind-pilot readout)

### SIGNS Panel
- `CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE` | MSFS event | — | Stock seatbelt toggle. | Low (we drive SEATBELT via XMLVAR position)
- `CABIN SEATBELTS ALERT SWITCH` | SimVar bool | R | Stock seatbelt state. | Low (duplicate)

### WIPER Panel
(CIRCUIT SWITCH ON:141/143 + the circuit events registered/used) — none missing.

## GLARESHIELD

### EFIS Control Panel
- `KOHLSMAN SETTING MB:1` | MSFS var int(948-1084) | R | Capt baro in hPa (stock). | Low (we have A32NX_FCU_LEFT_EIS_BARO_HPA)
- `KOHLSMAN SETTING HG:1` | MSFS var | R | Capt baro in inHg (stock). | Low (duplicate)
- `KOHLSMAN_INC` / `KOHLSMAN_DEC` | MSFS events | — | Baro inc/dec. | Low (skip)
- `XMLVAR_Baro_Selector_HPA_1` | bool | R/W | Capt baro unit Hg/hPa (doc name). | Low (we use A32NX_FCU_EFIS_L_BARO_IS_INHG)
- `A32NX_EFIS_{SIDE}_OANS_RANGE` | int(0-4) | R/W | OANS (airport map) range selector. | **High** (genuinely new ND/OANS range control we lack)
- `A380X_EFIS_{SIDE}_WPT_BUTTON_IS_ON` | bool | R/W | Waypoint filter button. | **High** (per-filter button; we collapsed filters into ACTIVE_FILTER combo but lack discrete WPT button — see note: ACTIVE_FILTER covers it, so borderline). Tag **Low** — covered by A380X_EFIS_{side}_ACTIVE_FILTER.
- `A380X_EFIS_{SIDE}_VORD_BUTTON_IS_ON` | bool | R/W | VOR/DME filter button. | Low (covered by ACTIVE_FILTER)
- `A380X_EFIS_{SIDE}_NDB_BUTTON_IS_ON` | bool | R/W | NDB filter button. | Low (covered by ACTIVE_FILTER)

### FCU Panel
- ALL FCU rows (A32NX.FCU_*_INC/DEC/SET/PUSH/PULL, AP_* stock events, AUTOPILOT ALTITUDE LOCK VAR:3, AUTOPILOT MANAGED SPEED IN MACH, A32NX.FCU_ALT_INCREMENT_*, H:A320_Neo_FCU_TRUEMAG_PUSH, etc.) | various | | FCU control surface. | Low (FCU-already-done — handled by the dedicated FCU dialog/readout integration; explicitly out of scope per definition header)

### Glareshield Side Panel
- `H:A32NX_EFIS_{SIDE}_CHRONO_PUSHED` | HTML event | — | EFIS chrono pushbutton. | Low (HTML event; chrono already via ET switch)
- (MASTERCAUT/MASTERWARN per side, AUTOLAND_WARNING registered) — none missing. NOTE doc typo `PUSH_AUTOPILOT_MASTERAWARN_{SIDE}` = our MASTERWARN.

### Lighting Knobs
- `LIGHT POTENTIOMETER:84/87/10/11` | MSFS var int(0-100) | R | Glareshield/integral brightness pots. | Low (brightness; not surfaced)

## INSTRUMENT PANEL

### Autobrake, Gear Lever and Gear Annunciation
- `GEAR_UP` / `GEAR_DOWN` | MSFS events | — | Stock gear. | Low (we drive A32NX_GEAR_HANDLE_POSITION)
- `GEAR HANDLE POSITION` | SimVar bool | R/W | Stock gear handle. | Low (duplicate of our L:var)
- `GEAR LEFT/CENTER/RIGHT POSITION` | SimVar int(0-100) | R | Per-strut gear extension percent. | **High** (gear-transit/position readouts we lack — we only have downlock booleans)
- `A32NX_AUTOBRAKES_RTO_ARMED` | bool | R | RTO autobrake armed feedback. | **High** (RTO-armed readout we lack; we have the press PB only)
- `A32NX_AUTOBRAKES_DISARM_KNOB_REQ` | bool | R | Autobrake knob solenoid reset-to-DISARM request. | Low (internal state)
- `ANTISKID_BRAKES_TOGGLE` | MSFS event | — | Stock anti-skid toggle. | Low (we read ANTISKID BRAKES ACTIVE)

### Clock
- `H:A32NX_CHRONO_RST` / `_TOGGLE` / `_DATE` | HTML events | — | Chrono reset/toggle/date. | Low (HTML events; chrono via ET switch already)

### Display Unit Control Panel
- `LIGHT POTENTIOMETER:88/89/94/98/90/91/95/99` | MSFS var int(0-100) | R | DU brightness pots. | Low (brightness)

### Integrated Standby Instrument System (ISIS)
- `H:A32NX_ISIS_PLUS/MINUS/BUGS/LS/RST/KNOB_*` (12 HTML events) | HTML events | — | ISIS button presses/knob. | Low (HTML events; we expose ISIS state L:vars already)

### Landing Gear Gravity Panel
- `A32NX_LG_GRVTY_SWITCH_GUARD_1` | bool | R/W | Gravity-extension guard 1. | Low (switch-guard)
- `A32NX_LG_GRVTY_SWITCH_GUARD_2` | bool | R/W | Gravity-extension guard 2. | Low (switch-guard)
- (MASTER_SWITCH_GUARD, GRVTY_SWITCH_POS registered) — none missing.

### Switching Panel
(ATT_HDG, AIR_DATA, EIS_DMC switching knobs registered) — none missing.

## PEDESTAL

### CKPT DOOR Panel
(COCKPIT_DOOR_LOCKED registered) — none missing.

### Cockpit Lighting Panel
- `LIGHT POTENTIOMETER:85/83/76/7` | MSFS var int(0-100) | R | Pedestal/flood brightness pots. | Low (brightness)

### ECAM Control Panel
- `LIGHT POTENTIOMETER:92/93` | MSFS var int(0-100) | R | ECAM CP brightness pots. | Low (brightness)
- (all A32NX_BTN_* + SD page index registered) — none missing.

### ENG MASTER Panel
- `FUELSYSTEM VALVE SWITCH:1` | SimVar bool | R | Engine 1 master fuel-valve switch. | **High** (per-engine master/fuel-valve readback we lack — we only SEND open/close events)
- `FUELSYSTEM VALVE SWITCH:2` | SimVar bool | R | Engine 2 master fuel-valve switch. | **High** (and by extension :3 :4 — readback of master switch state)

### FLAPS Panel
- `FLAPS_SET/UP/1/2/3/DOWN/INCR/DECR` | MSFS events | — | Stock flap controls. | Low (we read A32NX_FLAPS_HANDLE_INDEX; no flap set control exposed by design)
- `A32NX_FLAPS_HANDLE_PERCENT` | number(0-1) | R | Flap handle percent. | Low (we have the index)
- `FLAPS HANDLE INDEX` | SimVar int(0-5) | R | Stock flap index. | Low (duplicate)
- `FLAPS HANDLE PERCENT` | SimVar number | R | Stock flap percent. | Low (duplicate)

### Flight Data Recording System Panel
- `A32NX_ACMS_TRIGGER_ON` | bool (momentary) | R/W | ACMS event trigger. | Low (we have DFDR_EVENT_ON; ACMS is niche — borderline, keep Low)

### KCCU Panel
- `H:A32NX_KCCU_{SIDE}_{KEY}` | HTML events (30+ keys) | — | KCCU keypad keys. | Low (HTML keypad; we expose KBD/CCD on-off, KCCU text entry is via bridged MCDU)

### PARK BRK Panel
(PARK_BRAKE_LEVER_POS registered) — none missing.

### PITCH TRIM Panel
- `ELEV_TRIM_UP` / `ELEV_TRIM_DN` | MSFS events | — | Pitch trim. | Low (skip; FBW-managed trim)
- `ELEVATOR TRIM INDICATOR` | SimVar number | R | Pitch trim indicator. | **High** (pitch-trim position readout we lack — useful for takeoff config)
- `ELEVATOR TRIM POSITION` | SimVar number (radians) | R | Pitch trim position. | Low (duplicate of indicator)
- `ELEVATOR TRIM PCT` | SimVar int | R | Pitch trim percent. | Low (duplicate)

### RUDDER TRIM Panel
- `RUDDER TRIM PCT` | SimVar number(-1..1) | R | Rudder trim percent. | **High** (rudder-trim readout we lack)
- `RUDDER TRIM` | SimVar number (radians) | R | Rudder trim. | Low (duplicate)
- `RUDDER_TRIM_RESET` | MSFS event | — | Reset rudder trim. | Low (skip)
- `XMLVAR_RudderTrim` | int(0-2) | R/W | Rudder trim reset switch position. | Low (skip; niche)

### SPEED BRAKE Panel
- `SPOILER SET` | MSFS event | — | Stock spoiler set. | Low (skip)
- `SPOILERS_ARM_TOGGLE` | MSFS event | — | Arm spoilers toggle. | Low (we read A32NX_SPOILERS_ARMED)
- `SPOILERS ARMED` | SimVar bool | W | Stock spoilers-armed. | Low (duplicate)
- (A32NX_SPOILERS_HANDLE_POSITION, A32NX_SPOILERS_ARMED registered) — none missing.

### SURV (WXR, TCAS) Panel
- "Not yet implemented" in the doc. — nothing to add. (We already have A32NX_SWITCH_RADAR_PWS_Position, MULTISCAN, GCS.)

### Thrust Lever
- `THROTTLE{NUM}_AXIS_SET_EX1` | MSFS event | — | Throttle axis set. | Low (skip; axis)
- `A32NX_AUTOTHRUST_DISCONNECT` | bool | R | Autothrust disconnect state. | Low (we have AUTOTHRUST_STATUS + the disconnect event)

## SIDE STICK
- `AILERON_SET` / `AILERON POSITION` | event / SimVar | | Aileron axis/position. | Low (skip; axis)
- `ELEVATOR_SET` / `ELEVATOR POSITION` | event / SimVar | | Elevator axis/position. | Low (skip; axis)
- `A32NX_PRIORITY_TAKEOVER:1` | bool | R | Capt sidestick priority takeover. | **High** (priority-takeover annunciation — dual-input warning relevant to a blind FO/Capt)
- `A32NX_PRIORITY_TAKEOVER:2` | bool | R | F/O sidestick priority takeover. | **High**

## TILLER
- (Refers to separate doc.) — nothing concrete.

## RUDDER PEDALS
- `RUDDER_SET` / `RUDDER POSITION` | event / SimVar | | Rudder axis/position. | Low (skip; axis)
- `A32NX_LEFT_BRAKE_PEDAL_INPUT` | int(0-100) | R | Left toe-brake input. | Low (input axis; not a useful readout)
- `A32NX_RIGHT_BRAKE_PEDAL_INPUT` | int(1-100) | R | Right toe-brake input. | Low (input axis)
- `AXIS_LEFT_BRAKE_SET` / `AXIS_RIGHT_BRAKE_SET` | events | — | Brake axes. | Low (skip)

## flyPad EFB
- `A32NX_EFB_POWER` | HTML event | — | EFB power toggle. | Low (EFB is driven via bridged EFB client, not L:vars)
- `A32NX_EFB_BRIGHTNESS` | int(0-100) | R/W | EFB brightness. | Low (EFB bridge)
- `A32NX_EFB_USING_AUTOBRIGHTNESS` | bool | R/W | EFB autobrightness. | Low (EFB bridge)
- `A32NX_EFB_CHECKLIST_COMPLETE_ITEM` | bool | R/W | EFB checklist item. | Low (EFB bridge)
- `A32NX_LOAD_LIGHTING_PRESET` | int(1-8) | R/W | Load lighting preset. | Low (niche; A32NX_AIRCRAFT_PRESET_LOAD already covered for aircraft presets)
- `A32NX_SAVE_LIGHTING_PRESET` | int(1-8) | R/W | Save lighting preset. | Low (niche)
- `A32NX_LOAD_AIRCRAFT_PRESET` | int(1-5) | R/W | Load aircraft preset (doc name). | Low (we register A32NX_AIRCRAFT_PRESET_LOAD — equivalent)
- `A32NX_LOAD_AIRCRAFT_PRESET_PROGRESS` | number(0-1) | R | Preset load progress (doc name). | Low (we register A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS — equivalent)

================================================================================
# PAGE 2 — SYSTEMS API
================================================================================

## Uncategorized
- `A32NX_NOSE_WHEEL_LEFT_ANIM_ANGLE` | degrees | R | Left nose-wheel angle (wheel axis). | Low (animation/internal)
- `A32NX_NOSE_WHEEL_RIGHT_ANIM_ANGLE` | degrees | R | Right nose-wheel angle. | Low (animation/internal)
- `A32NX_REPORTED_BRAKE_TEMPERATURE_{1-16}` | celsius | R | Sensor-reported brake temps. | Low (we already surface A32NX_BRAKE_TEMPERATURE_{1-16})
- `A32NX_LIGHTING_PRESET_LOAD` | number | R/W | Load lighting preset. | Low (niche)
- `A32NX_LIGHTING_PRESET_SAVE` | number | R/W | Save lighting preset. | Low (niche)
- `A32NX_AIRCRAFT_PRESET_LOAD_EXPEDITE` | bool | R/W | Expedite preset load. | Low (we have the preset load + progress; expedite is a nicety — keep Low)
- (A32NX_AIRCRAFT_PRESET_LOAD, _PROGRESS, PUSHBACK_*, generic _PB_* templates, BAT_SELECTOR_KNOB registered) — none missing.

## Air Conditioning / Pressurisation / Ventilation (ATA 21)
- `A32NX_COND_CPIOM_B{id}_AGS_DISCRETE_WORD` | ARINC429 | R | AGS app discrete word. | **ARINC429**
- `A32NX_COND_CPIOM_B{id}_TCS_DISCRETE_WORD` | ARINC429 | R | TCS app discrete word. | **ARINC429**
- `A32NX_COND_CPIOM_B{id}_VCS_DISCRETE_WORD` | ARINC429 | R | VCS app discrete word. | **ARINC429**
- `A32NX_COND_CPIOM_B{id}_CPCS_DISCRETE_WORD` | ARINC429 | R | CPCS app discrete word. | **ARINC429**
- `A32NX_COND_FDAC_{id1}_CHANNEL_FAILURE` | bool | R | FDAC channel failure (doc generic form; we register per-channel _CHANNEL_1/2). | Low (covered)
- `A32NX_COND_PURS_SEL_TEMPERATURE` | celsius | R | Purser-selected cabin temp (FAP). | **High** (purser temp-target readout we lack)
- `A32NX_COND_PACK_{id}_OUTLET_TEMPERATURE` | celsius | R | Pack outlet temperature. | **High** (pack outlet temp readout we lack)
- `A32NX_COND_{id}_TRIM_AIR_VALVE_POSITION` | percent | R | Trim-air valve opening per zone. | **High** (trim-air valve position readout we lack)
- `A32NX_COND_TADD_CHANNEL_{id}_FAILURE` | bool | R | TADD channel failure. | **High** (trim-air controller fault readout we lack)
- `A32NX_PRESS_CABIN_ALTITUDE_{cpiom_id}` | ARINC429Word | R | Cabin altitude. | **ARINC429** (note: this is the GENUINE cabin-altitude readout but ARINC429-encoded; needs decoder — High value but in the ARINC429 bucket)
- `A32NX_PRESS_CABIN_ALTITUDE_TARGET_{cpiom_id}` | ARINC429Word | R | Target cabin altitude. | **ARINC429**
- `A32NX_PRESS_CABIN_VS_{cpiom_id}` | ARINC429Word | R | Cabin vertical speed. | **ARINC429** (high-value once decoded)
- `A32NX_PRESS_CABIN_VS_TARGET_{cpiom_id}` | ARINC429Word | R | Target cabin V/S. | **ARINC429**
- `A32NX_PRESS_CABIN_DELTA_PRESSURE_{cpiom_id}` | ARINC429Word | R | Cabin delta-P (ARINC). | **ARINC429** (we already surface the analog A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE in PSI)
- (A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE, OCSM channel/auto-partition failures, OVHD COND/CARGO/PRESS/VENT PB vars registered) — none missing among non-ARINC.

## Auto Flight System (ATA 22)
- `A380X_MFD_{side}_ACTIVE_PAGE` | string | R | URI of active MFD page (e.g. fms/active/init). | **High** (lets us announce which MFD page is up — genuinely useful, plain string)
- `A32NX_SPEEDS_MANAGED_SHORT_TERM_PFD` | number | R | Short-term managed speed on PFD. | **High** (managed-speed readout we lack)
- (A32NX_FMS_PAX_NUMBER registered) — none missing.

## Flight Management System (ATA 22)
- (A32NX_FMS_SWITCHING_KNOB, A380X_FMS_DEST_EFOB_BELOW_MIN registered) — none missing.

## Communications (ATA 23)
- `FBW_RMP_MODE_ACTIVE_{vhf_index}` | enum | R | Active freq mode (Frequency/Data/Emergency) per VHF. | **High** (RMP mode readout we lack — we have active/standby freq but not mode)
- `FBW_RMP_MODE_STANDBY_{vhf_index}` | enum | R | Standby freq mode per VHF. | **High**
- (A380X_RMP_{n}_STATE, FBW_RMP_FREQUENCY_ACTIVE/STANDBY registered) — none missing.

## Electrical (ATA 24)
- `A32NX_ELEC_CONTACTOR_{name}_IS_CLOSED` | bool | R | Contactor closed state. | Low (internal — many contactors; SD/debug detail)
- `A32NX_ELEC_{name}_POTENTIAL_NORMAL` | bool | R | Potential within normal range. | **High** (per-element normal/abnormal flag — useful electrical-health readout)
- `A32NX_ELEC_{name}_FREQUENCY` | hertz | R | AC element frequency. | **High** (gen frequency readout we lack)
- `A32NX_ELEC_{name}_FREQUENCY_NORMAL` | bool | R | Frequency normal flag. | **High**
- `A32NX_ELEC_{name}_LOAD` | percent | R | Generator load percent. | **High** (gen-load readout we lack)
- `A32NX_ELEC_{name}_LOAD_NORMAL` | bool | R | Load normal flag. | **High**
- `A32NX_ELEC_{name}_CURRENT` | ampere | R | Element current. | **High** (current readout we lack)
- `A32NX_ELEC_{name}_CURRENT_NORMAL` | bool | R | Current normal flag. | **High**
- `A32NX_ELEC_ENG_GEN_{number}_IDG_OIL_OUTLET_TEMPERATURE` | celsius | R | IDG oil outlet temp. | **High** (IDG oil-temp readout we lack)
- (A32NX_ELEC_{name}_POTENTIAL — we register BAT_{n}_POTENTIAL only; the generic POTENTIAL covers gens/buses too. The bus-powered + IDG-connected flags are registered.)

## Fire and Smoke Protection (ATA 26)
- `A32NX_FIRE_FDU_DISCRETE_WORD` | ARINC429 | R | Fire Detection Unit discrete word. | **ARINC429**
- (zone ON_FIRE, DETECTED_ENG/zone, AGENT_PRESSED [see flight-deck High items above], SQUIB armed/discharged, FIRE_BUTTON, TEST registered for engines/MLG/APU as applicable) — non-ARINC engine/APU AGENT/SQUIB gaps already captured under flight-deck FIRE section.

## Flight Controls (ATA 27)
- `A32NX_{side}_FLAPS_POSITION_PERCENT` | percent | R | Left/right flap position percent. | **High** (real flap-surface position readout we lack — we only have handle index)
- `A32NX_{side}_SLATS_POSITION_PERCENT` | percent | R | Left/right slat position percent. | **High** (slat-surface position readout we lack)
- `A32NX_{side}_FLAPS_ANGLE` | degrees | R | Actual flap angle. | **High** (flap-angle readout)
- `A32NX_{side}_SLATS_ANGLE` | degrees | R | Actual slat angle. | **High** (slat-angle readout)
- `A32NX_FCDC_{number}_DISCRETE_WORD_1` | ARINC429 | R | Flight-control law/fault word. | **ARINC429**
- `A32NX_FCDC_{number}_FG_DISCRETE_WORD_4` | ARINC429 | R | Flight-guidance AP/FD word. | **ARINC429**
- `A32NX_FCDC_{number}_FG_DISCRETE_WORD_8` | ARINC429 | R | Flight-guidance fault/inop word. | **ARINC429**
- `A32NX_SFCC_{number}_SLAT_FLAP_ACTUAL_POSITION_WORD` | ARINC429 | R | Slat/flap position word. | **ARINC429**
- `A32NX_FLAPS_CONF_INDEX` | number | R | Desired flap config index ("DO NOT USE IN SYSTEMS"). | Low (doc warns against using)

## Fuel (ATA 28)
- `A32NX_FQMS_STATUS_WORD` | ARINC429 | R | FQMS status word. | **ARINC429**
- `A32NX_FQMS_TOTAL_FUEL_ON_BOARD` | ARINC429 | R | Total fuel on board. | **ARINC429** (high value once decoded; we surface A32NX_TOTAL_FUEL_QUANTITY in kg already)
- `A32NX_FQMS_GROSS_WEIGHT` | ARINC429 | R | Aircraft gross weight. | **ARINC429** (high value once decoded — GW readout)
- `A32NX_FQMS_CENTER_OF_GRAVITY_MAC` | ARINC429 | R | CG %MAC. | **ARINC429** (high value once decoded — CG readout)
- `A32NX_FQMS_{tank}_TANK_QUANTITY` | ARINC429 | R | Per-tank fuel quantity. | **ARINC429**
- `A32NX_FQMS_{side}_FUEL_PUMP_RUNNING_WORD` | ARINC429 | R | Fuel-pump running word. | **ARINC429**
- `A32NX_FQDC_{id}_{tank}_TANK_QUANTITY` | ARINC429 | R | Per-tank quantity (AGP). | **ARINC429**
- (A32NX_TOTAL_FUEL_QUANTITY, A32NX_TOTAL_FUEL_VOLUME registered) — none missing among non-ARINC.

## Indicating-Recording (ATA 31)
- `A32NX_CDS_CAN_BUS_1_1_AVAIL` ... `2_2_AVAIL` (4) | bool | R | CDS CAN-bus availability. | Low (internal display-bus health; SD/debug)
- `A32NX_CDS_CAN_BUS_*_FAILURE` (4) | bool | R | CDS CAN-bus failure (sim). | Low (internal)
- `A32NX_CDS_CAN_BUS_1_1_RECEIVED` | bool | R | CDS bus received-message flag. | Low (internal)
- `A32NX_CDS_CAN_BUS_1_1 / 1_2 / 2_1 / 2_2` | ArincWord852 | R | CDS CAN-bus data words. | **ARINC429** (ArincWord852 — data words)

## ECAM Control Panel (ATA 31)
- (A32NX_BTN_{button}, A32NX_ECAM_SD_CURRENT_PAGE_INDEX registered) — none missing.

## EFIS Control Panel (ATA 31)
- (LS/VV/CSTR/ACTIVE_FILTER/ACTIVE_OVERLAY/ARPT/TRAF/BARO_PRESELECTED registered) — none missing.

## Landing Gear (ATA 32)
- (A32NX_BTV_STATE registered) — none missing.

## Bleed Air (ATA 36)
- `A32NX_PNEU_ENG_{number}_INTERMEDIATE_TRANSDUCER_PRESSURE` | PSI | R | Engine bleed intermediate pressure (-1 if no output). | **High** (bleed-pressure readout we lack)

## Integrated Modular Avionics (ATA 42)
- `A32NX_AFDX__REACHABLE` | bool | R | AFDX switch reachability. | Low (internal avionics-network)
- `A32NX_AFDX_SWITCH__FAILURE` | bool | R | AFDX switch failure. | Low (internal)
- `A32NX_AFDX_SWITCH__AVAIL` | bool | R | AFDX switch available. | Low (internal)
- `A32NX_CPIOM__FAILURE` | bool | R | CPIOM failure. | Low (internal)
- `A32NX_CPIOM__AVAIL` | bool | R | CPIOM available. | Low (internal)
- `A32NX_IOM__FAILURE` | bool | R | IOM failure. | Low (internal)
- `A32NX_IOM__AVAIL` | bool | R | IOM available. | Low (internal)

## Auxiliary Power Unit (ATA 49)
- `A32NX_APU_N2` | ARINC429Word<Percent> | R | APU N2 RPM percent. | **ARINC429** (we surface A32NX_APU_N_RAW already)
- `A32NX_APU_FUEL_USED` | ARINC429Word<Mass> | R | APU fuel used (kg). | **ARINC429**

## Engines (ATA 70)
- (A32NX_OVHD_FADEC_{ENG} registered) — none missing.

## Hydraulics
- `A32NX_OVHD_HYD_ENG_{ENG}AB_PUMP_DISC_PB_IS_AUTO` | bool | R(/W) | Pump-disc PB AUTO. | **High** (same item flagged in flight-deck HYD section — the settable control)
- (PUMP_DISC_PB_HAS_FAULT, HYD_ENG_{ENG}AB_PUMP_DISC registered) — none missing.

## Sound Variables
- `A380X_SOUND_COCKPIT_WINDOW_RATIO` | number(0-1) | R | Cockpit-window open ratio. | Low (sound/animation)

## Autobrakes
- `A32NX_AUTOBRAKES_DISARM_KNOB_REQ` | bool | R | Knob reset-to-DISARM request. | Low (internal — also in flight-deck list)
- (SELECTED_MODE, ARMED_MODE, RTO_ARM_IS_PRESSED registered; RTO_ARMED flagged High in flight-deck section) — none new here.

## Non-Systems Related
- `FBW_PILOT_SEAT` | enum | R | Which seat occupied (0=Left,1=Right). | Low (cosmetic; also A380X_PILOT_IN_FO_SEAT below)
- (A32NX_EXT_PWR_AVAIL:{n} registered) — none missing.

## Developer Input Events — RMP (B:A380X_{rmp_prefix}_*) — ~40 rows
- All `B:A380X_{rmp_prefix}_*_PB / _KNOB / _PUSH / _SELECTOR / _COVER` input events.
  | Input events | R/W | RMP keypad/knob/selector hardware events. | Low (the RMP is managed via the bridged screen + we drive radios through stock COM events; these granular keypad events are out of scope)

## A380X Private Local Vars — Communications (L:A380X_RMP_{n}_* ~40 rows + FBW_* bus words)
- All `L:A380X_RMP_{n}_{CAB|HF|INT|PA|NAV|TEL|VHF|MECH}_{CALL|RX|RX_SWITCH|TX|VOL|...}`, `_GREEN_LED`, `_RED_LED`, `_RST_LED`, `_NAV_SELECT`, `_NAV_FILTER`, `_STBY_NAV`.
  | bool/enum/percent | R | RMP per-channel call/reception/transmit/volume/LED internal state. | Low (internal RMP UI state — "private local vars")
- `FBW_RMP{n}_PRIMARY_VHF{v}_FREQUENCY` | ARINC429 BCD | R | Primary bus freq output RMP→VHF. | **ARINC429** (Low — also internal; we read FBW_RMP_FREQUENCY_ACTIVE/STANDBY)
- `FBW_RMP{n}_BACKUP_VHF{v}_FREQUENCY` | ARINC429 BCD | R | Backup bus freq output. | **ARINC429** (Low/internal)
- `FBW_VHF{v}_BUS_A` | bool | R | Hardwired bus-select discrete to VHF. | Low (internal)
- `FBW_VHF{v}_FREQUENCY` | ARINC429 BCD | R | Tuned freq from VHF radios. | **ARINC429** (Low/internal — we have the active/standby plain freqs)

## Sim Specific
- `A380X_PILOT_IN_FO_SEAT` | bool | R | Whether pilot sits on FO half of cockpit. | Low (cosmetic)

================================================================================
# BUCKETS
================================================================================

## A) WORTH ADDING — plain control/readout (High confidence)

ELEC
- GENERAL ENG MASTER ALTERNATOR:{1-4} (engine generator on/off)
- APU GENERATOR SWITCH (APU gen on/off)
- A32NX_OVHD_ELEC_APU_GEN_{1-2}_PB_HAS_FAULT
- A32NX_OVHD_ELEC_BUS_TIE_PB_HAS_FAULT
- A32NX_OVHD_ELEC_COMMERCIAL_PB_HAS_FAULT
- A32NX_ELEC_{name}_POTENTIAL_NORMAL
- A32NX_ELEC_{name}_FREQUENCY
- A32NX_ELEC_{name}_FREQUENCY_NORMAL
- A32NX_ELEC_{name}_LOAD
- A32NX_ELEC_{name}_LOAD_NORMAL
- A32NX_ELEC_{name}_CURRENT
- A32NX_ELEC_{name}_CURRENT_NORMAL
- A32NX_ELEC_ENG_GEN_{number}_IDG_OIL_OUTLET_TEMPERATURE

AIR / COND
- A32NX_COND_PACK_{1-2}_FLOW_VALVE_1_IS_OPEN
- A32NX_COND_PACK_{1-2}_FLOW_VALVE_2_IS_OPEN
- A32NX_COND_CKPT_DUCT_TEMP
- A32NX_OVHD_COND_FWD_SELECTOR_KNOB
- A32NX_COND_{DECK}_DECK_{NUM}_DUCT_TEMP
- A32NX_COND_PURS_SEL_TEMPERATURE
- A32NX_COND_PACK_{id}_OUTLET_TEMPERATURE
- A32NX_COND_{id}_TRIM_AIR_VALVE_POSITION
- A32NX_COND_TADD_CHANNEL_{id}_FAILURE

BLEED
- A32NX_PNEU_ENG_{number}_INTERMEDIATE_TRANSDUCER_PRESSURE

HYD
- A32NX_OVHD_HYD_ENG_{ENG}AB_PUMP_DISC_PB_IS_AUTO
- A32NX_OVHD_HYD_EPUMP{G|Y}{A|B}_ON_PB_HAS_FAULT

ENG START / MASTER
- TURB ENG IGNITION SWITCH EX1:{1-4} (engine-mode position readback)
- FUELSYSTEM VALVE SWITCH:{1-4} (engine master/fuel-valve readback)

FIRE
- A32NX_FIRE_SQUIB_1_APU_1_IS_ARMED
- A32NX_OVHD_FIRE_AGENT_1_APU_1_IS_PRESSED
- A32NX_OVHD_FIRE_AGENT_{1-2}_ENG_{1-4}_IS_PRESSED

LIGHTING
- LIGHTING_STROBE_0 (3-position strobe set)
- LIGHTING_LANDING_1 (T.O / TAXI / OFF selector)

GEAR / INSTRUMENT
- GEAR LEFT POSITION
- GEAR CENTER POSITION
- GEAR RIGHT POSITION
- A32NX_AUTOBRAKES_RTO_ARMED

EFIS
- A32NX_EFIS_{SIDE}_OANS_RANGE

TRIM
- ELEVATOR TRIM INDICATOR
- RUDDER TRIM PCT

SIDE STICK
- A32NX_PRIORITY_TAKEOVER:1
- A32NX_PRIORITY_TAKEOVER:2

FLIGHT CONTROLS (surfaces)
- A32NX_{side}_FLAPS_POSITION_PERCENT
- A32NX_{side}_SLATS_POSITION_PERCENT
- A32NX_{side}_FLAPS_ANGLE
- A32NX_{side}_SLATS_ANGLE

AUTOFLIGHT / FMS
- A380X_MFD_{side}_ACTIVE_PAGE
- A32NX_SPEEDS_MANAGED_SHORT_TERM_PFD

COMMS (RMP)
- FBW_RMP_MODE_ACTIVE_{vhf_index}
- FBW_RMP_MODE_STANDBY_{vhf_index}

## B) ARINC429 DATA WORDS — need the SD decoder (do not surface raw)

- A32NX_COND_CPIOM_B{id}_AGS_DISCRETE_WORD
- A32NX_COND_CPIOM_B{id}_TCS_DISCRETE_WORD
- A32NX_COND_CPIOM_B{id}_VCS_DISCRETE_WORD
- A32NX_COND_CPIOM_B{id}_CPCS_DISCRETE_WORD
- A32NX_PRESS_CABIN_ALTITUDE_{cpiom_id}
- A32NX_PRESS_CABIN_ALTITUDE_TARGET_{cpiom_id}
- A32NX_PRESS_CABIN_VS_{cpiom_id}
- A32NX_PRESS_CABIN_VS_TARGET_{cpiom_id}
- A32NX_PRESS_CABIN_DELTA_PRESSURE_{cpiom_id}
- A32NX_FIRE_FDU_DISCRETE_WORD
- A32NX_FCDC_{number}_DISCRETE_WORD_1
- A32NX_FCDC_{number}_FG_DISCRETE_WORD_4
- A32NX_FCDC_{number}_FG_DISCRETE_WORD_8
- A32NX_SFCC_{number}_SLAT_FLAP_ACTUAL_POSITION_WORD
- A32NX_FQMS_STATUS_WORD
- A32NX_FQMS_TOTAL_FUEL_ON_BOARD
- A32NX_FQMS_GROSS_WEIGHT          (high-value once decoded: GW)
- A32NX_FQMS_CENTER_OF_GRAVITY_MAC (high-value once decoded: CG)
- A32NX_FQMS_{tank}_TANK_QUANTITY
- A32NX_FQMS_{side}_FUEL_PUMP_RUNNING_WORD
- A32NX_FQDC_{id}_{tank}_TANK_QUANTITY
- A32NX_CDS_CAN_BUS_1_1 / 1_2 / 2_1 / 2_2  (ArincWord852 data words)
- A32NX_APU_N2  (ARINC429Word<Percent>)
- A32NX_APU_FUEL_USED  (ARINC429Word<Mass>)
- FBW_RMP{n}_PRIMARY_VHF{v}_FREQUENCY  (ARINC429 BCD, internal)
- FBW_RMP{n}_BACKUP_VHF{v}_FREQUENCY   (ARINC429 BCD, internal)
- FBW_VHF{v}_FREQUENCY                  (ARINC429 BCD, internal)

## C) SKIP — debug / internal / switch-guard / FCU-already-done / duplicate / stock-event-we-cover

- All `*_LOCK` switch guards (RAM_AIR, VENT_AIR_EXTRACT, AC_ESS_FEED, FADEC, FUEL XFR ×4, HYD pump-disc/epump, CALLS_EMER, ELEC IDG).
- All `A32NX_FIRE_GUARD_*` and `A32NX_LG_GRVTY_SWITCH_GUARD_1/2` guards.
- All `LIGHT POTENTIOMETER:*` brightness pots (~25 rows).
- All `H:*` HTML events (CHRONO, ISIS ×12, KCCU keypad, EFIS chrono, A320_Neo_FCU_TRUEMAG, EFB power).
- All entire FCU panel rows (A32NX.FCU_*_INC/DEC/SET/PUSH/PULL, AP_* stock events, ALT LOCK VAR:3, MANAGED SPEED IN MACH, ALT_INCREMENT_*) — FCU handled by dedicated dialog/readout integration.
- Stock event/SimVar duplicates of L:var controls we already drive: ENGINE_BLEED_AIR_SOURCE_TOGGLE, BLEED AIR APU, BLEED AIR ENGINE:{n}, XMLVAR_Momentary_PUSH_OVHD_AIRCOND_*BLEED_Pressed, TOGGLE_STRUCTURAL_DEICE, STRUCTURAL DEICE SWITCH, XMLVAR_MOMENTARY_PUSH_OVHD_PROBESWINDOW_PRESSED, CABIN_SEATBELTS_ALERT_SWITCH(_TOGGLE), GEAR_UP/DOWN, GEAR HANDLE POSITION, ANTISKID_BRAKES_TOGGLE, FLAPS_* events + FLAPS HANDLE INDEX/PERCENT + A32NX_FLAPS_HANDLE_PERCENT, SPOILER SET / SPOILERS_ARM_TOGGLE / SPOILERS ARMED, KOHLSMAN_* + KOHLSMAN SETTING MB/HG + XMLVAR_Baro_Selector_HPA_1, TOGGLE_ALTERNATOR, APU_GENERATOR_SWITCH_TOGGLE/SET, STROBES_SET/ON/OFF, all *_SET/_ON/_OFF light input events, LANDING_TAXI_2, LIGHTING_LANDING_2, LIGHTING_BEACON/WING/NAV/LOGO_0.
- EFIS discrete filter buttons (WPT/VORD/NDB_BUTTON_IS_ON) — covered by A380X_EFIS_{side}_ACTIVE_FILTER combo.
- Axis events/positions: AILERON/ELEVATOR/RUDDER _SET + POSITION, THROTTLE{n}_AXIS_SET_EX1, AXIS_LEFT/RIGHT_BRAKE_SET, A32NX_LEFT/RIGHT_BRAKE_PEDAL_INPUT.
- Trim duplicates: ELEVATOR TRIM POSITION/PCT, RUDDER TRIM (radians), RUDDER_TRIM_RESET, XMLVAR_RudderTrim, ELEV_TRIM_UP/DN.
- IMA internals: A32NX_AFDX_*, A32NX_CPIOM_*, A32NX_IOM_* (avail/failure/reachable).
- CDS CAN-bus AVAIL/FAILURE/RECEIVED booleans.
- ELEC contactor closed flags (A32NX_ELEC_CONTACTOR_{name}_IS_CLOSED).
- Animation/sound/cosmetic: A32NX_NOSE_WHEEL_*_ANIM_ANGLE, A380X_SOUND_COCKPIT_WINDOW_RATIO, FBW_PILOT_SEAT, A380X_PILOT_IN_FO_SEAT, FBW_VHF{v}_BUS_A.
- Preset niceties: A32NX_LIGHTING_PRESET_LOAD/SAVE, A32NX_LOAD/SAVE_LIGHTING_PRESET, A32NX_AIRCRAFT_PRESET_LOAD_EXPEDITE, A32NX_LOAD_AIRCRAFT_PRESET(_PROGRESS) [equivalents already registered], A32NX_ACMS_TRIGGER_ON, A32NX_FLAPS_CONF_INDEX (doc says do-not-use), A32NX_REPORTED_BRAKE_TEMPERATURE_{1-16} (duplicate of BRAKE_TEMPERATURE), A32NX_AUTOBRAKES_DISARM_KNOB_REQ, A32NX_AUTOTHRUST_DISCONNECT.
- All RMP `B:A380X_{rmp_prefix}_*` developer input events and `L:A380X_RMP_{n}_*` private LED/call/RX/TX/VOL state (RMP driven via bridge + stock COM).
- Doc spelling/skin variants of vars we already register: A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_AUTO (we use _IS_ON), A32NX_FIRE_BUTTON_APU1 (we use _APU), A32NX_COND_FDAC_{id1}_CHANNEL_FAILURE (we use per-channel form).
