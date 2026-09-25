# Flysimware Learjet 35A — the variable surface

Companion to [learjet35a.md](learjet35a.md). Two halves: the **names** (what exists and how it
groups, from the package XML) and the **measurements** (units, encodings and behaviour, read
live at EGNX with the aircraft powered from the GPU and then through a full engine start).
Everything in the measured sections was read or written against the running aircraft; nothing
is inferred from a name.

## Method — names

```bash
PKG="$LOCALAPPDATA/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/LocalCache/Packages/Community/flysimware-aircraft-learjet-35a"
grep -rhoE '\(>?L:[A-Za-z0-9_:. #-]+[,)]' "$PKG/ModelBehaviorDefs" "$PKG/SimObjects/Airplanes/flysimware_LEARJET_35A/model" \
     "$PKG/SimObjects/Airplanes/flysimware_LEARJET_35A/panel" | sed -E 's/^\(>?L://; s/[,)]$//' | sort -u   # 377 from XML
grep -hoE 'L:[A-Za-z0-9_:. -]+' "$PKG"/html_ui/Pages/VCockpit/Instruments/Generic/*/*/*.js | sed 's/^L://' | sort -u   # 93 from the JS
```

413 distinct names; entries containing `#…#` are template parameters, not real variables.

⚠️ The live enumerator cannot list these: `msfs_list_lvars` (MobiFlight `MF.LVars.List`) caps at
1000 names and on this machine is crowded out by GSX and leftover DA40 variables — only 20
`GENERIC_LEAR_*` names of the 130-odd the aircraft registers appeared. Enumerate from the XML;
read by name. ⚠️ And do not read through the MCP's MobiFlight read path at all: on 2026-09-07 it
returned 0 for `L:GENERIC_LEAR_BATT1` while a Coherent read (`SimVar.GetSimVarValue` inside the
`LEAR35ATABLET` view) said 1 and the main bus was at 28 V. Every measurement below was read
through Coherent (`tools/coherent-eval.ps1 -Title LEAR35ATABLET`).

## The interaction surface

`tools/lj35-gen/enumerate-controls.ps1` lists every `<UseTemplate>` of a vendor switch, knob,
lever or button template with its `NODE_ID` and tooltip: **225 nodes** in v1.8.1.
`Lj35InteractionSurfaceTests` names each one with the MSFSBA control that covers it or the
reason it is left out, and fails when a vendor update adds or removes one.

## Transports — measured

| Template | Variable shape | Write that works | Notes |
|---|---|---|---|
| `Flysimware_GENERIC_SWITCH_Template` | `L:GENERIC_<node>` 0/1 | `SetLVar` (calculator path) | Sticks; drives the downstream effect (cabin light → `LIGHT CABIN:n`, jet pump → `FUELSYSTEM PUMP ACTIVE:n`, AP master → circuits 24/61). The B: input event `(>B:GENERIC_<node>_Set)` also works and additionally fires `H:GENERIC_<node>`. |
| `Flysimware_3Way_Momentary_Switch_Template` | `L:GENERIC_Momentary_<node>` 0/1/2 | `SetLVar` | Labels are the template's `TT_VALUE_0/1/2`. `RESET_OFF_RESET2`/`ON_OFF_RESET` types spring back from the momentary detent. |
| `Flysimware_Knob_ROTARY_Template` | `L:XMLVAR_<node>_Position` 0..N-1 | `SetLVar` | `NUM_STATES` and `TT_VALUE_n` in Interior.xml give the positions. |
| `Flysimware_GENERIC_FINITE_KNOB_Template` | `L:XMLVAR_<node>_Position` 0..100 (dimmers) or `L:GENERIC_<node>` 0..`ANIM_LENGTH` | `SetLVar` | |
| `Flysimware_GENERIC_SIMVAR_TOGGLE/SET_SWITCH_Template` | the stock SimVar | the stock K: event, conditionally | |
| GNS 530 / 430 | Coherent DOM | `H:AS530_<Key>` / `H:AS430_<Key>` via the Coherent socket | `ENT_Push`, `RightLargeKnob_Right` … verified. |
| GTX 345 | Coherent DOM + `TRANSPONDER STATE:1` | `H:Transponder{ON,STBY,…,0..9}` via the socket; squawk `K:XPNDR_SET` | Dark until `TRANSPONDER STATE:1 > 0`. |

## Measured, panel by panel

Each panel task appends its rows here: `Control | Variable | Encoding / behaviour (measured)`.

### Engine Start

| Control | Variable | Encoding / behaviour (measured 2026-09-07) |
|---|---|---|
| Battery 1 / 2 | `L:GENERIC_LEAR_BATT1/2` | 0/1. Write 1 → `A:BATTERY CONNECTION ON:1/2` 1 within a frame; battery bus 25.4 V. |
| Emergency battery | `L:GENERIC_Momentary_LEAR_SW_EMER_BATT` | 0 Emergency, 1 Standby, 2 Off (read 2 with the aircraft as delivered). |
| GPU | `L:EFB_PUSH_GPU` | 0/1. Write 1 → `EXTERNAL POWER ON:1` 1, main bus 28 V. Auto-clears on generator on line / brake off / GS > 1 / door open (Electrical.xml `GROUND_POWER_UNIT_VIS`). |
| Inverters | `L:GENERIC_LEAR_INV_PRI_1`, `_SEC_1` | 0/1 → `BUS CONNECTION ON:8` / `:9` (with bus-lookup 2 / 3). |
| Tie breakers | `L:GENERIC_BREAKER_{AC,MAIN,ESS}_TIE` | init 1 (closed). |
| Starter / generator | `L:GENERIC_Momentary_LEAR_STARTER_L/R` | 0 Generator, 1 Off, 2 Start. Write 2 (fuel computer on, lever at cut-off): `GENERAL ENG STARTER:1` 1 and `TURB ENG IGNITION SWITCH:1` 1 within 5 s; N2 climbs 13.6 → 19.9 → 22.9 → 24.1 → 25.2 % over 40 s and holds there — the starter-only ceiling; no light-off with the thrust lever at 0 %. |
| Thrust lever cut-off | `L:GENERIC_LEAR_SHUTOFF1/2` | 0 run / 1 cut-off; the model forces 0 when the lever is above 5 %. |
| Fuel computers | `L:GENERIC_LEAR_SW_COMPLEFT_1`, `_COMPRIGHT_1` | 0/1 (read 1 as delivered). |

### FC-530 Autopilot (measured 2026-09-07, engine running, AP master circuit on)

| Control | Write | Read back |
|---|---|---|
| Heading bug | `270 (>K:HEADING_BUG_SET)` | `AUTOPILOT HEADING LOCK DIR` 270 |
| Heading hold | `(>K:AP_PANEL_HEADING_HOLD)` | `AUTOPILOT HEADING LOCK` 1 |
| V/S target | `1500 (>K:AP_VS_VAR_SET_ENGLISH)` | `AUTOPILOT VERTICAL HOLD VAR` 1500 |
| Alerter | `L:ALERTER_DIGITAL` = 15000 | reads 15000; `AUTOPILOT ALTITUDE LOCK VAR:1` follows once `L:MODE_ALTSEL` is 1 |
| AP master circuit | `L:GENERIC_LEAR_SW_AUTOPILOT` = 1 | `CIRCUIT ON:24` and `:61` 1 |

### Engine start (measured 2026-09-07, left engine)

`L:GENERIC_LEAR_SHUTOFF1` 1 + `L:GENERIC_Momentary_LEAR_STARTER_L` 2 → `GENERAL ENG STARTER:1` 1,
N2 cranking. `L:GENERIC_LEAR_SHUTOFF1` 0 with the lever at 0 % → combustion within 12 s, N2 52 %,
ITT 645 °C, FF 149 pph. Starter flag self-clears; switch L:var stays 2. Switch to 0 (Generator) →
`GENERAL ENG MASTER ALTERNATOR:1` 1, 28 V, `EXTERNAL POWER ON:1` 0.

### Flaps and gear

`FLAPS NUM HANDLE POSITIONS` 3; `FLAPS_2` → `FLAPS HANDLE INDEX` 2, both flaps 20°; `FLAPS_UP` → 0.
`GEAR HANDLE POSITION` bool (1 down as delivered).

### GNS 530 (VCockpit13 - AS530)

The generic row agent (`coherent-display-agent.js`) installs and scrapes the page;
`SimVar.SetSimVarValue('H:AS530_ENT_Push','number',1)` inside the view advanced the self-test
page. Power needs the avionics master (`K:AVIONICS_MASTER_SET` 1 with the batteries and GPU on).
