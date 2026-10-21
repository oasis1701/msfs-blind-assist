# Fenix panel controls against the v3 cockpit model (2026-10-21)

Every key in `FenixA320Definition.BuildPanelControls()` (443 unique keys, 445 entries) compared with the v3 cockpit
behaviour XML (`fnx-aircraft-a32x-common\…\Part_Interior_Cockpit\model\Cockpit_Behavior.xml`, the per-part
`*_Behavior.xml` files and `ModelBehaviorDefs\fnx32x\Templates`), read only, plus HubHop's Fenix presets for history.
388 keys match the model; 5 are not model controls (4 stock SimConnect names: `XPNDR_SET`, `COM STANDBY FREQUENCY:1-3`;
1 the known `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION`). Most of the mismatches below predate v3 (the 2022-2023 HubHop presets
show the same model).

How a Fenix momentary button works: each click adds 1 on press and 1 on release, an odd value means "held down". Our
press-release pulse (0, 1, 0; A320-3) matches it. A combo that writes 0 or 1 instead means 1 = "hold down", 0 = "let go".

## Wrong today

| Panel | Key | Ours | Model | What the pilot gets | Confidence |
|---|---|---|---|---|---|
| Cargo Smoke | `S_OH_CARGO_DISC_1_OLD_LAYOUT`, `_2_` | combo 0 Off / 1 On | `Switch_3Position_Hold`: 0 = AGENT2, 1 = rest, 2 = AGENT1 (ANIMTIP) | "Off" fires agent 2 and holds the switch there; "On" is the rest position; agent 1 cannot be chosen | certain (model read again 2026-10-21) |
| ADIRS | `S_OH_NAV_SYS_DISP` | 6 options 0-5, "IR 1" … "ADR 3" | `Knob_Increment` 0..3 (real panel OFF/1/3/2) | options 4 and 5 are off the knob and the labels are wrong (the control is GNADIRS-only, plan 06) | certain (model read again); exact order: in sim |
| Weather Radar | `S_WR_MODE` | 0..1 in 0.1 steps, "0".."10" | `Knob_Increment` 0..3: WX, WX+T, WX+T+HZD, MAP | only WX and WX+T reachable; the other values fall between detents | certain (model read again) |
| ACP | `S_ASP_PA_SEND` | button, handler `SetLVar(value)` (always 1) | `Button_Momentary` | one press leaves PA held for good; the app cannot release it (breaks A320-3) | certain (model and handler read again) |
| Electrical, Maintenance, Cockpit Other, Weather Radar | `S_OH_ELEC_BUS_TIE` (writes `S_OH_ELEC_BUSTIE`), `S_OH_SVCE_INT_OVRD`, `S_OH_LIGHTING_AVIONICS_COMPT`, `S_PED_COCKPIT_DOOR_VIDEO`, `S_WR_GCS` | Off/On combos | momentary; the system flips the function per press and shows it in an `I_` light | "On" presses and holds (flips the function whatever its state); "Off" only lets go. The CALLS pattern (plan 06 H3) | certain from the model; effect: in sim |
| DCDU Left, Cockpit Other | `S_DCDU1_DIM_BRT`, `S_DCDU2_DIM_BRT` | Dim/Mid/Bright | spring-loaded both ways (0 decrease, 2 increase) | "Dim" or "Bright" stays held, ramping to the limit until "Mid" | certain from the model; effect: in sim |
| Flight Controls | `A_FC_THROTTLE_LEFT/RIGHT/BOTH_INPUT` | "Reverse" = 1 | 0..5 (a preset: 0 full reverse, 1 idle reverse) | only idle reverse | likely |

## Check in the sim

| Panel | Key | Question |
|---|---|---|
| EFIS L/R | `S_FCU_EFIS1_BARO_STD`, `_2_` (`SetFCUBaro` writes 0/1) | the model is a push/pull counter (push −1, pull +1): after a push or pull in the virtual cockpit, do STD/QNH from the app still work? |
| FCU, EFIS, ISIS | `E_FCU_SPEED/HEADING/ALTITUDE/VS`, `E_FCU_EFIS{1,2}_BARO`, `E_MIP_ISFD_BARO` (INC/DEC), `S_FCU_VERTICAL_SPEED_PUSH/PULL` | relative counters written from a counter the app starts at 0: after a knob turn outside the app, does the first app step jump? V/S push/pull is the one FCU push/pull not on `AdjustFcuPushPullCounter` |
| Interior Lights | `S_OH_IN_LT_ANN_LT` | v3 tooltips say 0 BRT, 1 DIM, 2 TEST; ours 0 Dim, 1 Bright (2023 presets agree with ours) |
| Pressurization | `S_OH_PNEUMATIC_PRESS_MAN` | v3 names say 0 DOWN, 2 UP; ours the reverse (2022 presets and the click direction agree with ours) |
| ACP | `S_ASP_INTRAD` | v3 names say 0 RAD, 2 INT; ours 0 INT, 2 RAD (presets agree with ours); RAD is spring-loaded, so ours holds a transmit until OFF |

## Matching by design (no change)

23 hold-and-release combos on momentary buttons (IDG 1/2, EMER GEN TEST and MAN ON, the fire tests and agents,
recorder ERASE/TEST, oxygen MASK MAN ON, evac horn shutoff, wiper repellents, cargo smoke test, ELT test, APU
auto-extinguish RESET/TEST, standby attitude cage): "On" holds, "Off" lets go. The four seat switches (holding is
intended). Brightness knobs in 0.05 steps where the model uses 0.1 (harmless). Window blinds (tooltips disagree with
each other, both agree with our labels).

Already known and planned (plan 02): the gravity gear crank, the duplicate Flight Controls panel, Generator 1, the
three renamed names; CALLS and PACK FLOW (plan 06).
