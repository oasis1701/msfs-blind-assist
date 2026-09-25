# Skyward Citation Sovereign+ (C680) — the variable surface

Companion to [citation680.md](citation680.md). Two halves: the **names** (what exists and how
it groups, from the package XML) and the **measurements** (encodings and behaviour, read live
at the gate with the aircraft powered up through its own switches). Everything in the measured
sections was read or written against the running aircraft; nothing is inferred from a name.

## Method — names

```bash
PKG="$LOCALAPPDATA/Packages/Microsoft.Limitless_8wekyb3d8bbwe/LocalCache/Packages/Community/skyward-cessna-citation-c680"
grep -aohE 'L:[A-Za-z0-9_:. -]+' "$PKG"/SimObjects/Airplanes/cessna-citation-c680/model/*.xml \
     "$PKG"/html_ui/Pages/VCockpit/Instruments/C680/*/*.js \
     "$PKG"/SimObjects/Airplanes/cessna-citation-c680/panel/Instruments/G3000/Plugins/*.js | sed -E 's/[ ,)].*$//' | sort -u   # 792 names
```

⚠️ The MobiFlight enumerator cannot list these: GSX's thousand-plus L:vars crowd them out of its
1000-name cap. Enumerate from the XML; read by name, through a Coherent view
(`tools/coherent-eval.ps1 -Title WTG3000_PFD_1 -ExprFile …`).

## Transports — measured 2026-09-09

| Control kind | Variable | Write that works | Notes |
|---|---|---|---|
| Vendor push button / switch | `L:SW_SOV_<name>` 0/1 | `SetLVar` (calculator path) | Sticks and drives the downstream effect (`SW_SOV_AI_PITOT_L` → `PITOT HEAT SWITCH:1`). |
| BATT buttons | `L:SW_SOV_ELEC_BATT_1/2` | `SetLVar` | ⚠️ NOT the stock B:ELECTRICAL_Battery_n events: those set `ELECTRICAL MASTER BATTERY:n` (25.4 V) but the plugin's own electrical model closes nothing from them — avionics stayed dark until the L:vars were written, then `SW_SOV_AVIONICS_POWER_ACTIVE` went 1 and the PFD's `#Electricity` state "on". |
| AVN, ELEC, TRU, INTERIOR, EMER LTS | `L:SW_SOV_ELEC_AVN_1/2`, `_ELEC_L/R`, `_TRU_1/2`, `_CABIN_PWR`, `SW_SOV_SAFETY_LIGHTS_POSITION` | `SetLVar` | |
| Stock main-bus SimVars | `ELECTRICAL MAIN BUS VOLTAGE:n`, `BATTERY BUS VOLTAGE`, `AVIONICS BUS VOLTAGE` | read-only | ⚠️ Read 0 V with the aircraft fully lit: the plugin does not write them. Bus health comes from `SW_SOV_*` flags and the EIS electrical group, not from these. |
| APU knob | `L:XMLVAR_APU_StarterKnob_Pos` 0 Off / 1 On / 2 Start | knob 1; then 2 + `(>K:APU_STARTER)`; back to 1 after 1 s | RPM 17 → 63 → 100 % within 15 s. The knob L:var write sticks. |
| Generators (L, R, APU) | `LINE CONNECTION ON:409` / `:86` / `:639` (the plugin's own bus lines: L GEN bus 2 → L main, R GEN bus 2 → R main, APU bus 2 → L emer) | `1 1 (>K:2:ALTERNATOR_SET)`, `2 1 (>K:2:ALTERNATOR_SET)`, `3 1 (>K:2:APU_GENERATOR_SWITCH_SET)` (0 for off) | ⚠️ The vendor plugin INTERCEPTS these events with passthrough off and keeps the switch position in a JS subject, so the stock `ELECTRICAL GENERATOR SWITCH:n`, `APU GENERATOR SWITCH`, `APU GENERATOR ACTIVE`, `APU VOLTS` and `ELECTRICAL GENALT BUS *` SimVars never move (measured: all constant while the APU generator went 105 A → 0.1 A → 105 A). The only readable state is the bus line the plugin closes: `LINE CONNECTION ON:639` with `ELECTRICAL GENERATOR AMPS:3`. The plugin puts the APU generator on line only with `APU PCT RPM` > 99.5, the LEFT generator off line (its line 409 open) and no engine start in progress; with the engines running the APU GEN switch does nothing, by the aircraft's own rule. External power is line 637, the bus tie line 473, the TRUs lines 479 / 810. The `L:SW_SOV_*_GEN_CONN` mirrors are the 2020 path and stay 0 on 2024. |
| Standby power | `ELECTRICAL MASTER BATTERY:3` | `3 (>K:TOGGLE_MASTER_BATTERY)` | The stock STBY battery template; `L:XMLVAR_BATTERYSTBY_SWITCHSTATE` writes do not stick (the template owns it). Battery 3 read 1 at load. |
| Beacon / nav | `LIGHT BEACON`, `LIGHT NAV` | `K:TOGGLE_BEACON_LIGHTS` / `K:TOGGLE_NAV_LIGHTS` | Beacon 1 → 0 → 1 measured. |
| Lighting knobs | `L:LIGHTING_Knob_Panel_raw` … 0–100 | `SetLVar` | 90 → 40 → 90 measured. |
| Run/stop | `GENERAL ENG FUEL VALVE:n` | `(>B:FUEL_RunStop_n_Toggle)` | 1 → 0 → 1 measured (assessment). |
| Autopilot values | stock | `HEADING_BUG_SET`, `AP_ALT_VAR_SET_ENGLISH`, `XPNDR_SET` | measured (assessment). |
| GTC touchscreens | Coherent DOM | mousedown/mouseup/click on `.touch-button` / `.bg-img-touch-button`; knobs `H:AS3000_TSC_Vertical_n_RightKnob_{Small,Large}_{INC,DEC}`, `RightKnob_Push`, `MiddleKnob_{INC,DEC,Push}`, `Joystick_*` | Origin KSAT entered, COM1 122.50 set, squawk 1200 set, knob stepped 122.50 → 122.525 → 122.55 (assessment). |
| Vendor EFB | Coherent DOM | `.click()` on `input[type=checkbox]` / `.settings-selector-btn` | chocks 0 → 1 → 0, Meter Overlay 0 → 1 → 0 (assessment). |
| Bus tie / ext power | `L:SW_SOV_ELEC_BUS_TIE_EMIS`, `L:SW_SOV_EXT_GEN_CONN` | `(>H:SW_SOV_ELEC_BUS_TIE)` / `(>H:SW_SOV_ELEC_EXT_PWR)` | The plugin's handler returns early ON THE GROUND for bus tie; the tie closed itself when the APU generator came on line (`SW_SOV_BUS_TIE_CONN` 1, CAS "BUS TIE CLOSED"). |

## CAS — the PFD list (measured with the aircraft lit)

Container `.full-cas-display-2-list`, rows `.cas-display-2-msg`; a visible row carries
`cas-display-2-msg-visible` and its severity as `cas-display-2-msg-advisory` (caution / warning
by the same pattern, matching the scroll-bar shading classes `cas-scroll-bar-shading-caution` /
`-warning`); an empty slot is `display: none`. Seen at the gate on batteries and APU: NO TAKEOFF,
CONTROL LOCK ON, ENGINE SHUTDOWN L (advisory), earlier BUS TIE CLOSED and, with the batteries
off, PARK BRAKE ON, P/S HEAT ON, FUEL LEVEL LOW L-R, FUEL BST PUMP ON L-R.

## Measured, panel by panel

Each panel task appends its rows here: `Control | Variable | Encoding / behaviour (measured)`.

### Electrical (2026-09-09, cold and dark at the gate, KTYQ)

| Control | Variable | Measured |
|---|---|---|
| Left / Right BATT | `L:SW_SOV_ELEC_BATT_1/2` | 0 at load. Write 1 → avionics power active, PFD/MFD/GTC lit within a second. `ELECTRICAL BATTERY VOLTAGE:1` 25.4 V. |
| STBY PWR | `ELECTRICAL MASTER BATTERY:3` | 1 at load. `L:STBY_PWR_LED_AMBER` 1 with the avionics dark, 0 once powered; `_GREEN` 0 throughout. |
| AVN L/R | `L:SW_SOV_ELEC_AVN_1/2` | 0 at load; 1 written; `SW_SOV_AVIONICS_POWER_ACTIVE` 1 only once the batteries' L:vars were 1 too. |
| ELEC L/R | `L:SW_SOV_ELEC_ELEC_L/R` | 1 (Norm) at load. |
| APU GEN | `APU GENERATOR SWITCH` | see Transports. |
| BUS TIE | `L:SW_SOV_BUS_TIE_CONN` 1, `L:SW_SOV_ELEC_BUS_TIE_EMIS` 1 | closed automatically with the APU generator. |
| INTERIOR | `L:SW_SOV_ELEC_CABIN_PWR` | 1 written, sticks. |

### APU

| Control | Variable | Measured |
|---|---|---|
| APU knob | `L:XMLVAR_APU_StarterKnob_Pos` | see Transports; `APU PCT RPM` 100 held for 20 s. `APU SWITCH` 1. |
| APU BLEED AIR | `L:SW_SOV_APU_BLEED` | 1 written, sticks; `L:ELECTRICAL_APU_Bleed` stayed 0 (the vendor's bleed logic uses its own duct flags). |

### Exterior lights and anti-ice

| Control | Variable | Measured |
|---|---|---|
| TAXI | `L:SW_SOV_LIGHTS_TAXI` → `LIGHT TAXI` | followed with the aircraft powered (2026-09-09). |
| Left PITOT/STATIC | `L:SW_SOV_AI_PITOT_L` → `PITOT HEAT SWITCH:1` | 1 → 0 → 1 followed (assessment, batteries only). |

### Right Tilt (2026-09-10, engines running at the gate)

| Control | Variable | Measured |
|---|---|---|
| PRESS SOURCE knob | `L:SW_SOV_PRESS_SRC` | 2 at load; 3 written and read back, 2 restored. 5 positions. |
| L / R BLEED AIR knobs | `L:SW_SOV_L_BLEED_AIR`, `_R_` | 2 at load; 3 written and read back. 4 positions. |
| CABIN ALT switch | `L:SW_SOV_CABIN_ALT_SWITCH_POS` | -1 / 0 / 1 (the model's STATE tests), 0 at rest. |
| Pressurization rate knob | `L:SW_SOV_PRESSURIZATION_RATE` | 50 at load. `SW_SOV_CABIN_ALT` 764 ft, ducts and ECS active with the APU bleed on. |
| AUX HYD PUMP | `CIRCUIT ON:120` ← `120 (>K:ELECTRICAL_CIRCUIT_TOGGLE)` | 0 → 1 → 0; `SW_SOV_HYD_AUX_PUMP` followed 0 → 1 → 0. `SW_SOV_HYD_PRESSURE` 3000, `_RESERVOIR` 265. |
| BOOST PUMP L | `L:SW_SOV_FUEL_PUMP_1_ON_SWITCH` | 1 → `FUELSYSTEM PUMP ACTIVE:1` 1 → 0 restored. |
| CROSSFEED knob | `L:SW_SOV_FUEL_TRANSFER` | 1 = Off (valve 1 closed). 0 = Left Tank, 2 = Right Tank: the crossfeed valve (`FUELSYSTEM VALVE OPEN:1`) opens to 100 percent and the named side's boost pump runs (position 2: pump 2 active, 35 gph through lines 2 and 9). |
| PASS OXY knob | `L:Mask_Selector_Position` | 1 at load; 0 written and read back. |
| ELT | `ELT ACTIVATED` ← `N (>B:SAFETY_ELT_1_Set)` | `(>B:SAFETY_ELT_1_Toggle)` took it 0 → 1 (transmitting); `0 (>B:SAFETY_ELT_1_Set)` back to 0. No L:var carries the switch position (`L:XMLVAR_ELT_STATE` does not exist). |
| Temperatures | `L:SW_SOV_CKPT_TMP_CUR`, `_CABIN_TMP_CUR` | 21.0 each; `_TMP_SEL` 0; fans 178 / 293. Oxygen gauges 12,726,220 (raw); two bottles fitted. |

### ⚠️ Reading trap — MobiFlight's per-client registration cap

Every distinct expression `execute_calculator_code` READS is registered as a MobiFlight
string-simvar slot on the client; the module caps them, and once the cap is hit every NEW
expression silently returns 0 while old expressions keep answering (measured 2026-09-10: a
dozen fresh reads of live L:vars all returned 0, the identical earlier expression still
returned 1111, and a fresh `(L:SW_SOV_ELEC_BATT_1) 1 +` returned 0 with the battery on).
Writes are unaffected. Reconnecting the client clears it. For bulk reads use the Coherent
debugger (`tools/coherent-eval.ps1 -Title WTG3000_MFD -ExprFile …` with
`SimVar.GetSimVarValue`), which has no such cap and gave every value above.

### Glareshield (2026-09-10, engines running at the gate)

| Control | Variable | Measured |
|---|---|---|
| FD L / R | `AUTOPILOT FLIGHT DIRECTOR ACTIVE:1/2` ← `1 (>K:TOGGLE_FLIGHT_DIRECTOR)` (side as the parameter) | 0 → 1 → 0 on side 1, side 2 untouched. |
| YD | `AUTOPILOT YAW DAMPER` ← `(>K:YAW_DAMPER_TOGGLE)` | 0 → 1 → 0. |
| Heading bug / altitude preselect | `AUTOPILOT HEADING LOCK DIR`, `AUTOPILOT ALTITUDE LOCK VAR` ← `HEADING_BUG_SET`, `AP_ALT_VAR_SET_ENGLISH` | 250 and 15000 read back. |
| AP DISC (yoke) | `L:SW_SOV_AUTOPILOT_Push_Disconnect_1/2_Pressed` | The vendor plugin maps these as its `ap_disc` inputs AND intercepts `AUTOPILOT_OFF` / `AUTOPILOT_DISENGAGE_*` (passthrough off): pulse the L:var and fire `AUTOPILOT_OFF`. |
| AT / AT DISC / TO/GA | `K:AUTO_THROTTLE_ARM` (arms, or deactivates when armed/on), `K:AUTO_THROTTLE_DISCONNECT`, `K:AUTO_THROTTLE_TO_GA` | all intercepted by the plugin's FMS speed manager; status published as `L:SW_SOV_Autothrottle_Status` 0 Off / 1 Disconnected / 2 Armed / 3 On (its `STATUS_SIMVAR_ENUM_MAP`). `L:SW_Sovereign_Autothrottle_Status` also exists in the names but is not the one written. |
| VNAV | `(>H:AS1000_VNAV_TOGGLE)` | the only VNAV key in the G3000's loaded sources; no state var (`L:XMLVAR_VNAVButtonValue` does not exist here) — the armed mode reads on the PFD FMA. |
| Bank limit | — | `1 (>K:AP_MAX_BANK_SET)` and `(>K:AP_MAX_BANK_INC)` both left `AUTOPILOT MAX BANK ID` 0 / 30°; the GMC's half-bank lamp is not published. Not exposed. |
| Speed target | `AUTOPILOT AIRSPEED HOLD VAR` / `AUTOPILOT MACH HOLD VAR` ← `1 (>L:XMLVAR_SpeedIsManuallySet) N (>K:AP_SPD_VAR_SET)` (Mach: hundredths to `AP_MACH_VAR_SET`) | ⚠️ Measured airborne 2026-09-10 (FL286, AP in NAV, target stuck at 80): `250 (>K:AP_SPD_VAR_SET)` and `_SET_EX1` and the indexed `1 250 (>K:2:AP_SPD_VAR_SET)` all left 80; `(>K:AP_SPD_VAR_INC)` moved it to 81. The G3000v2's `FmsSpeedManager` (mfd.js) intercepts `AP_SPD_VAR_SET`, `_SET_EX1`, `AP_SPD_VAR_DEC`, `AP_MACH_VAR_SET`, `_SET_EX1`, `AP_MACH_VAR_DEC` and the four `AP_MANAGED_SPEED_IN_MACH_*` keys with passthrough off and re-fires them ONLY while its `ap_selected_speed_is_manual` topic is true — that topic is `L:XMLVAR_SpeedIsManuallySet` (msfssdk.js). With the flag at 1 the same set landed 200 (Mach var followed to 0.61). INC is not intercepted, which is why the knob works. Every typed target now sets the flag first, as pressing the speed knob does on the real GTC; the flag is the "Speed Source" switch (FMS / Manual). |
| IAS / Mach | `AUTOPILOT MANAGED SPEED IN MACH` | `AP_MANAGED_SPEED_IN_MACH_ON` (the event the sources name) left it 0 on the ground. Readout only; the touchscreen speed-bug page owns the units. |
| CWS | `L:SW_SOV_AUTOPILOT_CVS` | the yoke buttons toggle it (`! (>L:…)` in the model), so it is a switch, not a pulse. |
| MASTER WARNING / CAUTION | `MASTER WARNING ACTIVE`, `MASTER CAUTION ACTIVE` (+ `… ACKNOWLEDGED`) ← `K:MASTER_WARNING_ACKNOWLEDGE` / `K:MASTER_CAUTION_ACKNOWLEDGE` | the model's own push templates; the `XMLVAR_WARNING_n` names in the model are its O: animation vars, not L:vars. All 0 with no fault present. |
| Fire covers | `L:SAFETY_Push_Extinguisher_1_Cover` | 0 → 1 → 0. |
| Standby QNH unit | `L:SW_SOV_GH3900_QNH` | 0 → 1 → 0; `KOHLSMAN SETTING MB:3` 1013.25. `SW_SOV_GH3900_BL` 0.8 at load. |

### Pedestal (2026-09-10, engines running at the gate — nothing that moves a flight control, brake or the gear was written, by the owner's rule)

| Control | Variable | Measured / source |
|---|---|---|
| SEAT BELTS, PAX SAFETY | `L:SW_SOV_PASSENGER_SEAT_BELT` (1 at load), `L:SW_SOV_PASSENGER_SAFETY_PUSH` | 1 → 0 → 1 and 0 → 1 → 0 written and read back. The stock `CABIN SEATBELTS ALERT SWITCH` stays 0 throughout: not mirrored. |
| Cabin overhead lights | `L:SW_SOV_LIGHT_OVERHEAD`, `_BRT` 0.8 | 0 → 1 lit `LIGHT CABIN` 1; restored. `PAX_MUSIC_VOL` 80. |
| Speedbrake lever | `L:SW_SOV_HANDLING_SPOILER_LEVER` percent | the model's click code steps this L:var in 2 percent steps and the plugin drives the spoilers from it (0.75 factor); `SPOILERS HANDLE POSITION` is the downstream read. 0 at rest. |
| Park brake | `L:SW_SOV_PARKING_BRAKE` (1) → `BRAKE PARKING POSITION` (1) | the click code toggles the L:var; not exercised. |
| Gear lever | `L:SW_SOV_LANDING_GEAR_LEVER` 1 = down | the click code toggles it under the model's own conditions; MSFSBA refuses Up while `SIM ON GROUND`. `SW_SOV_HANDLING_GEAR_LOCKED:1..3` 1, `GEAR_POWER_ON` 1. |
| Flaps | stock `FLAPS HANDLE INDEX` 0..3 (`FLAPS NUM HANDLE POSITIONS` 3) ← `FLAPS_UP/1/2/DOWN` | the stock `ASOBO_HANDLING_Lever_Flaps_Template`; not exercised. |
| Control disconnect handle | `L:SW_SOV_CSF_Disconnect` 0..3 | `ASOBO_GT_Switch_4States`; the axis flags are `Skyward_Ailerons_Disconnected`, `Skyward_Elevator_L/R_Disconnected`. |
| Control lock | `L:SW_SOV_CONTROL_LOCK` 1 at the gate (CAS "CONTROL LOCK ON"). | |
| Anti-skid | `CIRCUIT ON:126` 1. | |
| Passenger briefing | `L:Takeoff_English` … `Landing_Spanish` (ten trigger L:vars) | not a selector; not exposed. |

### Avionics and circuit breakers (2026-09-10)

| Control | Variable | Measured / source |
|---|---|---|
| Radios | `COM ACTIVE/STANDBY FREQUENCY:n` (124.850), `NAV ACTIVE FREQUENCY:n` (110.50), `ADF ACTIVE FREQUENCY:1` (890 kHz) | stock; the COM standby set and swap were proven in the assessment. ADF set not exposed: the `ADF_COMPLETE_SET` BCD layout is unverified. |
| Squawk | `TRANSPONDER CODE:1` BCO16 (25669 = 0x6445 = squawk 6445) ← `XPNDR_SET` with the code as hex digits | proven in the assessment (1200). |
| Transponder mode | `TRANSPONDER STATE:1` 4 = Alt | ⚠️ `1 (>K:XPNDR_STATE_SET)` left it at 4 — not settable by event on this avionics; the GTC transponder page sets it. Readout only. |
| Baro | `KOHLSMAN SETTING MB:1/2/3` 1013.25 ← `2 16320 (>K:2:KOHLSMAN_SET)` | index 2 went to 1020.0 with index 1 untouched, restored with 16212. RPN order for a two-parameter K event: INDEX first, VALUE last (the last pushed is the event's value), the same order the model uses for `3 1 (>K:2:APU_GENERATOR_SWITCH_SET)`. |
| Circuit breakers | `L:CB_<name>` + `'<line>'_n (>K:ELECTRICAL_LINE_BREAKER_TOGGLE)` | 111 breakers generated from `SOV_Circuit_Breakers.xml` by `tools/c680-gen/gen-breakers.js` (`CB_J` has no line and is skipped). `CB_FLIGHT_HOUR_METER` pulled with the model's own click code: L:var 0 → 1, pushed back. |
| Display reversion | `L:INSTRUMENT_Push_DisplayReverse_1/2` | pulses (the model's push buttons). `L:SW_SOV_CONFIG_Baro_Sync` is the EFB's baro-sync setting. |
| FMS rows | `GPS WP DISTANCE`, `GPS WP ETE`, `GPS ETE`, `GPS FLIGHT PLAN TOTAL DISTANCE`, `GPS IS ACTIVE FLIGHT PLAN` | stock. `GPS WP NEXT ID` is a string SimVar the app cannot register — the touchscreen window reads the ident. |

### Cabin, ground and simulation (2026-09-10)

| Control | Variable | Measured / source |
|---|---|---|
| Ground equipment | `L:Static_Gear_Chock_L/R/C` (1 at the gate), `Static_Pitot_1..3` (1), `Static_AOA_L/R` (1), `Static_Engine_Cover_L/R` (0), `Static_Wing_L/R_1..6`, `Static_Wing_T_1..6` (0), `gpu_cart`, `SW_SOV_HYDRAULICS_Ground_Active`, `Oxygen_Tank_L_Fill_Active`, `SW_SOV_Water_Waste`, `Fuel_truck_Visible`, `VIP_Vehicles`, `SW_SOV_Carpet`, `SW_SOV_Static_Windshield_Cover` | each an L:var the EFB Services card toggles (chocks proven from the EFB in the assessment). ⚠️ Engine covers written 1 with the engines RUNNING reverted to 0 within 2 s — the aircraft refuses them on a running engine (its own rule; try again engines off). |
| Water | `L:SW_SOV_Water_Quantity_clean` 7.5, `_Blue_Juice` 7.5, `_Waste_Purity` 0, `Water_tank_sink_lid` 0 → 1 → 0 | |
| Doors and panels | `L:SW_SOV_PANELS_*` (all 0 at the gate), `BAT_DISC_L/R_2`, `SW_SOV_CAS_Door_open`, `SW_SOV_MAIN_DOOR_LEAK` | plain toggles in the model. |
| Payload | `PAYLOAD STATION COUNT` 16; stations 1 and 2 190 lb (crew), the rest 0; `TOTAL WEIGHT` 24155, `EMPTY WEIGHT` 17831, `MAX GROSS WEIGHT` 30775 | station names are string SimVars SimConnect cannot deliver here ("does not recognise SimVar PAYLOAD STATION NAME"). |
| Fuel load | `FUELSYSTEM TANK LEVEL:1/2` percent of 850 gal | proven 2026-09-09 (55 percent). |
| EFB options | `L:SW_SOV_REALISTIC_PB` 1, `SW_SOV_Wifi_available` 1, the `SW_SOV_CONFIG_*` and `SW_SOV_PAX_VIP_*` L:vars 0 | ⚠️ `SW_SOV_ATC_SOURCE` is NOT an L:var: the plugin reads it with `GetStoredData` (a datastore key), so the ACARS provider is set only on the EFB Settings page. The ANC enum order is not in the names; read it off the EFB page. |

### Touchscreens — the GTC agent (2026-09-10, `Resources/coherent-gtc-agent.js`)

| Fact | Measured |
|---|---|
| Page title | `.gtc-view-title` carries `show-title-N` for the slot in front; the title is `.gtc-view-title-inner-text.title-N` inside it (the bar keeps one slot per open view, so "Speed Bugs" and "PFD Home" are both present while Speed Bugs is up). |
| Buttons | `.touch-button` / `.bg-img-touch-button`, `touch-button-disabled`, `touch-button-hidden`; a label is its visible leaf texts joined with spaces ("COM1 124.850"). The frequency buttons hold hidden per-digit entry spans. |
| Knob labels | `.label-bar-label.dual-knob` ("COM1 Freq Push:1-2 Hold:") and `.label-bar-label.center-knob` ("Pilot COM1 Volume"). |
| Press | mousedown / mouseup / click at the button centre: `press("Speed Bugs")` → title Speed Bugs, `press("Home")` → PFD Home, both through the agent. |
| Knobs | `H:AS3000_TSC_Vertical_<n>_<Name>` from inside the view (n from the view title); the 2026-09-09 assessment stepped COM1 standby 122.50 → 122.525. |
| Units | GTC 1 pilot PFD, 2 left MFD, 3 right MFD, 4 copilot PFD (`C680Seat.GtcIndexFor`). One inspector socket per view: the window owns its client and disposes it on close. |
| Bars | Every page carries the radio bar (`.gtc-nav-com-top-bar`: Audio & Radios, Intercom, COM1/2 with standbys, MIC, MON) and the button bar (`.button-bar`: XPDR mode, squawk, Back, Home, MSG, INIT on Home, Full/Half). The agent lists them after the page's own buttons under "Radio bar:" / "Bottom bar:"; Ctrl+R in the window hides them. |
| Slide timing | 700 ms after a page press the PREVIOUS view's buttons still read as visible (all sixteen MFD Home buttons ahead of the new page's own, 2026-09-10 walk); several seconds later they are gone. The window waits 900 ms after a press. Not yet measured: the exact transition end, and which class the GTC puts on the view that has slid away (the durable fix is to scrape only the active `.gtc-view`). |
| MFD Home | Map / Traffic / Weather are PANE selectors, not pages: the first press puts that display in the touchscreen's half and relabels the button "Map Settings" / "Traffic Settings" / "Weather Selection"; the second press opens those settings. TAWS opens TAWS Settings (TAWS Inhibit, GPWS Inhibit). |
| MFD pages walked (2026-09-10, airborne) | Direct To (waypoint, BRG/DIS, Activate); Active Flight Plan (one button per leg plus a "_.__ ° ___ KT" FPA/speed button per leg, altitude cells as bare "FT" text — needs per-row grouping); Procedures (Departure / Arrival / Approach / Activate …); Charts (Navigraph list); Aircraft Systems (Synoptics tab: Summary … Cabin Pressure; Controls tab); Checklist (the vendor's normal procedures); Services (Position Reports, ACARS; Music / Contacts / Telephone / SMS disabled); Utilities (Minimums, Trip Stats, Timer, GPS Status, Initialization, Setup); ATC Datalink (Logon Setup: Facility, Flight ID, airports); PERF (Takeoff Data, Landing Data, Speed Bugs, Weight and Fuel, Flap Speeds); Waypoint Info; Nearest (Airport, INT, VOR, NDB, User); INIT (Crew Profile, Reset / Accept Initialization, Weight and Fuel, Takeoff Data, System Tests); MSG (Notifications: CPDLC, ACARS, Telephone); Audio & Radios (volume percentages read as orphan text — needs pairing); Transponder (Auto, TA Only, Altitude Reporting, On, Standby, Flight ID). |
| PFD Home | Nav Source (⚠️ CYCLES FMS → LOC1 → … on every press — never press it blind while the AP is in NAV), Bearing 1 / 2 (cycle OFF → NAV1 → …), OBS, Speed Bugs (Vapp / Vref with All On / All Off), Timers (Time, Up / Down, Reset, Start), Minimums (a digit keypad with BKSP and no Enter, Minimums Baro toggle), PFD Map Settings, PFD Settings (Single Cue, Flight Path Marker, Horizon Heading, SVT, Wind, AoA). |
| ACARS | Services → ACARS: Link Status (VHF - Digital, SATCOM), D-ATIS, Messages, Flight Plan Request, Departure Clearance, Oceanic (TWIP, Weather, Report Settings, ACARS Enabled, SATCOM Enabled disabled). The vendor's own EFB setup guide (Settings → 3rd Party Options) says the SimBrief / Navigraph flight plans "will be found within the Flight Plan Request page" once the account is logged in on the EFB. Its rows are not yet walked. |

### CAS monitor and engine strip (2026-09-10, `Resources/coherent-c680-cas-agent.js`, `coherent-c680-mfd-agent.js`)

| Fact | Measured |
|---|---|
| CAS rows | `.full-cas-display-2-list .cas-display-2-msg`; live rows carry `cas-display-2-msg-visible` and `-warning` / `-caution` / `-advisory`; a fresh caution also `-new` (colour rgb(10,10,0) mid-flash) and an acknowledged one `-acked`; empty slots are `display:none`. Seen with engines running: FUEL IMBALANCE and PARK BRAKE LOW PRESS (cautions), PARK BRAKE ON, APU BLD VALVE CLOSED, NO TAKEOFF, CONTROL LOCK ON, P/S COLD L-R-STBY (advisories). |
| Engine strip | `.engine-gauges`: `.arc-gauge.n1-gauge` / `.itt-gauge` (one per engine) with `.arc-gauge-digital-readout`, titles as free `.gauge-title` ("N1%", "ITT°C"); secondary rows `.sec-eng-data-row` with `.sec-eng-data-title` / `-value` (N2%, FUEL PPH, OIL PSI, OIL°C); system groups `*-section` each with an `.eis-title-text` (TRIM, FUEL QTY, FLAPS, GEAR, APU, HYDRAULICS, ELECTRICAL), read as visible leaves in reading order. |
| One socket | The PFD 1 CAS monitor and the MFD reader each own their view's single inspector socket; the definition holds the MFD client so the engine strip and the synoptic reader share it. |

### MFD panes and synoptics (2026-09-10)

| Fact | Measured |
|---|---|
| Panes | `.display-pane.display-pane-left` / `-right` (`display-pane-half` when split), title `.display-pane-title-text` ("Navigation Map", "Traffic Map", "Electrical"), content `.display-pane-content`. GTC 2 drives the left half, GTC 3 the right. |
| Synoptic pages | The MFD touchscreen's Aircraft Systems page: Summary, Hydraulics, Fuel, Electrical, Systems Test, Cabin Management, Exterior Lights, Temp, Propulsion, Cabin Pressure (Maintenance disabled); Checklist and Services sit on MFD Home. |
| Electrical pane text | read in reading order: "L AVN / L INT / R INT / R AVN", "L ELEC / R ELEC", "28 V L WSHLD R WSHLD 28 V", "45 A 55 A", "L GEN R GEN L AC R AC", "L TRU R TRU", "BUS TIE CLSD", "29 V L BATT R BATT", "EXT PWR", "APU GEN 21 21 °C" — the synoptic's own words, as the Alt+S window lists them. |

### Vendor EFB (2026-09-10, `Resources/coherent-c680-efb-agent.js`)

| Fact | Measured |
|---|---|
| Pages | `#menu-bar .menu-item[data-page]`: home, ground (Services), flight, checklists, settings; the active page is `#page-container .page.active`. |
| Tabs | `.sub-nav-item` (active one carries `active`): Services — Access, Ground, Payload, Electrical, Hydraulics, Vanity Service, O2 / N2; Flight — Flight Plan (OFP), Flight Plan Charts, Charts; Checklists — Normal (Short), Normal, Abnormal, Emergency; Settings — Configuration, Simulation, Audio, In Flight, 3rd Party Options. |
| Services Ground | 13 `.ground-service-card`s, each with a checkbox: Antenna Covers, Engine Covers, Pitot Tube Covers, AOA Sensor Covers, Wheel Chocks, Ground Power Unit, Hydraulics Cart, Oxygen Cart, Lavatory Service Cart, Fuel Truck, VIP Vehicle, Red Carpet. |
| Settings Configuration | `.settings-option-row` with `.settings-option-title` and a checkbox or `.settings-selector-btn` choices (active one `active`): WiFi System No WiFi / Ku-Band / Starlink, 2nd Oxygen Bottle, Auto Brightness, Weight / Liquid / Temperature Units, Registration Type, Display Style, Distance Unit, Temperature Unit, Backlight Mode, QNH Unit, Meter Overlay, Turn Indicator, Ground Speed Indicator, Test Kit, Calibration Kit. |
| Acting | a card checkbox or selector button answers `.click()`; a page or tab needs the mousedown/mouseup/click triple. The window acts by ROW INDEX from its last scrape (`act(i)`), so a stale list is re-read before any press. |

EFB walk (2026-09-10, airborne, every page and tab): Home is a dashboard (clock, ROUTE EGNX / LOWW, time 03:01, AIRCRAFT HB-SOV C680+, FLIGHT GS / ALT, WEATHER, FUEL KG) whose labels and values fall on separate rows — needs column pairing. Services → Access is NOT card markup: door titles ("Left Avionics | Right Avionics"), bare "off" checkbox rows and "[Electrical]" category badges come out separately — needs its own builder. Payload: TOT fuel, weights, CG, a "[0] kg" cargo entry and "SimBrief Import: [Fuel] [Payload]" buttons. Electrical / Hydraulics / Vanity / O2-N2 are readouts with FILL / REFILL / SERVICE buttons; the O2 gauges leak their tick labels (0, 5, 10, 15, 20, 1264) as rows. Flight: "[Fetch SimBrief OFP]" (the EFB's own OFP view), "[Fetch SimBrief Weather]", Navigraph Charts sign-in. Checklists: Normal (Short) 155 rows, Normal 639, Abnormal 3109, Emergency 2558 — the section list (a left column) and the items interleave by y, so the reading order is jumbled; needs the two columns separated. Settings tabs read well ("Label: A* / B" selectors, "Label: on/off" toggles); In Flight has Pause at Top of Descent and Sim Rate.

Settings account and key cards (3rd Party Options): the SimBrief User ID is a BUTTON (`.simbrief-id-button`, shown as "*") that opens a keypad popup (`.payload-keyboard-overlay` outside the page container: header "Set SimBrief User ID", a `.keyboard-display`, `.keyboard-button`s 0-9, Clear, Cancel, Set ID); the Navigraph account and Hoppie key cards use the same card markup with buttons ("Log In / Go to Charts", "Insert Key here"). The window lists a popup's rows in place of the page while it is up, and typed digits press its keys.
