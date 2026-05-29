# A380X FCU / AFS Control Panel variable mapping (vs A32NX FCU)

Reference for porting the A320 FCU accessibility integration to the A380X.

## TL;DR — how the A380 FCU differs architecturally

- The A380 "FCU" is a **self-contained TypeScript instrument** (`fbw-a380x/src/systems/instruments/src/FCU/`), not the A32NX glass FCU. Its knobs/buttons are still driven by the **legacy `H:A320_Neo_FCU_*` H-events**, and the managers translate those into **`K:A32NX.FCU_*` key events** plus they **write the SAME `L:A32NX_*` display L:vars the A320 uses** — NOT the A320's ARINC-style `A32NX_FCU_AFS_DISPLAY_*` words. Those `A32NX_FCU_AFS_DISPLAY_*` vars do **not exist** on the A380.
- The A380 **does NOT publish per-button FCU light L:vars** (`A32NX_FCU_*_LIGHT_ON`). Those don't exist in the A380 tree at all. AP/ATHR/LOC/APPR state must be read from the **FG / FMA status L:vars** instead.
- EFIS-CP **baro is completely re-architected**: there is no `A32NX_FCU_EFIS_L_*` family. Baro mode/unit are read from `XMLVAR_*` lvars and emitted as **ARINC EIS discrete words** (`A32NX_FCU_LEFT_EIS_*`). The only A380X-prefixed baro var is the preselect readout.

Sources cited per row: **[FCU-src]** = the FCU instrument managers/components; **[simvars]** = `fbw-a380x/docs/a380-simvars.md`; **[api]** = flybywiresim.com a380x flight-deck-api page; **[input-events]** = `fbw-a380x/docs/a380x-input-events.md`.

---

## Events (knobs / pushbuttons)

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX.FCU_HDG_SET` | `A32NX.FCU_HDG_SET` (via H-event `A320_Neo_FCU_HDG_SET` → reads `L:A320_Neo_FCU_HDG_SET_DATA`) | SAME key event. HDG SET path confirmed [FCU-src HeadingManager.onEvent]. `A32NX.FCU_HDG_SET` listed [api]. |
| `A32NX.FCU_HDG_PUSH` | **`A32NX.FCU_TO_AP_HDG_PUSH`** | A380-SPECIFIC. HeadingManager fires `K:A32NX.FCU_TO_AP_HDG_PUSH` on push [FCU-src HeadingManager.onPush]. (`A32NX.FCU_HDG_PUSH` also documented [api] as the generic API, but the in-sim FCU uses the `_TO_AP_` variant.) |
| `A32NX.FCU_HDG_PULL` | **`A32NX.FCU_TO_AP_HDG_PULL`** | A380-SPECIFIC. [FCU-src HeadingManager.onPull]. |
| `A32NX.FCU_SPD_SET` | `A32NX.FCU_SPD_SET` | SAME [api]. In-sim path: H-event `A320_Neo_FCU_SPEED_SET` → `L:A320_Neo_FCU_SPEED_SET_DATA` [FCU-src SpeedManager]. |
| `A32NX.FCU_SPD_PUSH` / `_PULL` | `A32NX.FCU_SPD_PUSH` / `A32NX.FCU_SPD_PULL` | SAME [api]. (FCU SpeedManager handles push/pull internally via `K:SPEED_SLOT_INDEX_SET`, but the documented stable events keep the A32NX names.) |
| `A32NX.FCU_ALT_SET` | `A32NX.FCU_ALT_SET` | SAME [api]. |
| `A32NX.FCU_ALT_PUSH` | `A32NX.FCU_ALT_PUSH` | SAME. AltitudeManager fires `K:A32NX.FCU_ALT_PUSH` (+`K:ALTITUDE_SLOT_INDEX_SET 2`) [FCU-src AltitudeManager.onHEvent]. |
| `A32NX.FCU_ALT_PULL` | `A32NX.FCU_ALT_PULL` | SAME [FCU-src AltitudeManager.onHEvent]. |
| `A32NX.FCU_ALT_INCREMENT_SET` | `A32NX.FCU_ALT_INCREMENT_SET` (also `A32NX.FCU_ALT_INCREMENT_TOGGLE`) | SAME [api]. Selector value var `L:XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT` (100..1000) [api]. |
| `A32NX.FCU_VS_SET` | `A32NX.FCU_VS_SET` | SAME [api]. In-sim: H-event `A320_Neo_FCU_VS_SET` → `L:A320_Neo_FCU_VS_SET_DATA` [FCU-src VerticalSpeedManager]. |
| `A32NX.FCU_VS_PUSH` | `A32NX.FCU_VS_PUSH` | SAME (documented) [api]. Note: the in-sim VerticalSpeedManager handles VS PUSH internally; only PULL fires a key event. |
| `A32NX.FCU_VS_PULL` | **`A32NX.FCU_TO_AP_VS_PULL`** | A380-SPECIFIC. VerticalSpeedManager fires `K:A32NX.FCU_TO_AP_VS_PULL` [FCU-src VerticalSpeedManager.onPull]. (Generic `A32NX.FCU_VS_PULL` documented [api].) |
| `A32NX.FCU_EXPED_PUSH` | `A32NX.FCU_EXPED_PUSH` | SAME. A380 **HAS EXPED** — AutopilotManager fires `K:A32NX.FCU_EXPED_PUSH` [FCU-src AutopilotManager.onEvent]. Active state from `L:A32NX_FMA_EXPEDITE_MODE`. |
| `A32NX.FCU_APPR_PUSH` | `A32NX.FCU_APPR_PUSH` | SAME [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_LOC_PUSH` | `A32NX.FCU_LOC_PUSH` | SAME [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_AP_1_PUSH` | `A32NX.FCU_AP_1_PUSH` | SAME. A380 **has separate AP1/AP2** — AutopilotManager fires `K:A32NX.FCU_AP_1_PUSH` [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_AP_2_PUSH` | `A32NX.FCU_AP_2_PUSH` | SAME [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_ATHR_PUSH` | `A32NX.FCU_ATHR_PUSH` | SAME [api]. |
| `A32NX.FCU_AP_DISCONNECT_PUSH` | `A32NX.FCU_AP_DISCONNECT_PUSH` | SAME [api]. |
| `A32NX.FCU_ATHR_DISCONNECT_PUSH` | `A32NX.FCU_ATHR_DISCONNECT_PUSH` | SAME [api]. |
| `A32NX.FCU_SPD_MACH_TOGGLE_PUSH` | `A32NX.FCU_SPD_MACH_TOGGLE_PUSH` | SAME [api]. In-sim toggles via `K:AP_MANAGED_SPEED_IN_MACH_ON/OFF` [FCU-src SpeedManager.onSwitchSpeedMach]. |
| `A32NX.FCU_TRK_FPA_TOGGLE_PUSH` | `A32NX.FCU_TRK_FPA_TOGGLE_PUSH` | SAME [api]. |

## EFIS-CP events (FD / baro)

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX.FCU_EFIS_L_FD_PUSH` (+ `_R_`) | **NO `A32NX.FCU_EFIS_*` event on A380.** FD is toggled via standard `TOGGLE_FLIGHT_DIRECTOR` [api]. | NO direct A380 analog with that name. FD-on state read from `AUTOPILOT FLIGHT DIRECTOR ACTIVE` [api]. |
| `A32NX.FCU_EFIS_L_BARO_SET/PUSH/PULL` (+ `_R_`) | **A380X-prefixed input events.** Per the FBW API catalog the A380 baro knob uses `H:A380X_EFIS_CP_BARO_PULL_{1\|2}` / `H:A380X_EFIS_CP_BARO_PUSH_{1\|2}` (ALTIMETER_INDEX 1=Capt/L, 2=F/O/R). | A380-SPECIFIC. NOTE: these H-events are referenced in the FBW input catalog; the local FCU `BaroManager` consumes them internally (onPush=STD, onPull=QNH/last, onRotate=±). They are **NOT** in `a380x-input-events.md` (which currently only documents RMP), so treat the exact `_PUSH_/_PULL_/_SET_` spelling as **UNCERTAIN — verify in-sim**. |

---

## Display value L:vars (SPD / HDG / ALT / VS) — the A380 uses A32NX_* names, NOT AFS_DISPLAY

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE` | **`L:A32NX_AUTOPILOT_SPEED_SELECTED`** (knots, or mach×100 when in mach) | NO `AFS_DISPLAY` var. A380 writes/reads `A32NX_AUTOPILOT_SPEED_SELECTED` [FCU-src SpeedManager, api]. |
| `A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE` | **`L:A32NX_AUTOPILOT_HEADING_SELECTED`** (degrees; also raw `L:A32NX_FCU_HEADING_SELECTED`) | NO `AFS_DISPLAY` var. [FCU-src HeadingManager, api]. |
| `A32NX_FCU_AFS_DISPLAY_ALT_VALUE` | **`AUTOPILOT ALTITUDE LOCK VAR:3`** (MSFS simvar, feet) | NO `AFS_DISPLAY` var. A380 ALT display reads `AUTOPILOT ALTITUDE LOCK VAR:3` [FCU-src Altitude.tsx, api]. |
| `A32NX_FCU_AFS_DISPLAY_VS_VALUE` | **`L:A32NX_AUTOPILOT_VS_SELECTED`** (fpm) and **`L:A32NX_AUTOPILOT_FPA_SELECTED`** (deg, FPA mode) | NO `AFS_DISPLAY` var. [FCU-src VerticalSpeedManager, api]. |

## Managed / dot / dashes indicators

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED` | **`L:A32NX_FCU_SPD_MANAGED_DOT`** (managed dot) and **`L:A32NX_FCU_SPD_MANAGED_DASHES`** (dashes) | A380-named lvars [FCU-src SpeedManager.refresh]. |
| `A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED` | **`L:A32NX_FCU_HDG_MANAGED_DASHES`** (dashes). Selected-shown flag: `L:A320_FCU_SHOW_SELECTED_HEADING`. | No explicit HDG "dot" lvar written by A380 FCU; managed shown via dashes [FCU-src HeadingManager.refresh]. |
| `A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED` (ALT) | **`L:A32NX_FCU_ALT_MANAGED`** | A380-named lvar [FCU-src AltitudeManager.init]. |
| (VS managed) | **`L:A32NX_FCU_VS_MANAGED`** | A380-named lvar [FCU-src VerticalSpeedManager.refresh]. |
| `A32NX_FCU_AFS_DISPLAY_MACH_MODE` | **`AUTOPILOT MANAGED SPEED IN MACH`** (MSFS bool) | NO `AFS_DISPLAY` var. Mach-mode read from MSFS simvar [FCU-src SpeedManager, api]. |
| `A32NX_TRK_FPA_MODE_ACTIVE` | `L:A32NX_TRK_FPA_MODE_ACTIVE` | SAME — reuses A32NX name [FCU-src FcuPublisher, multiple managers, api]. |

---

## Mode / light indicators (AP / ATHR / LOC / APPR / EXPED / FD)

The A380 has **no `A32NX_FCU_*_LIGHT_ON` lvars**. Read engagement/mode status from FG/FMA vars instead:

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX_FCU_AP_1_LIGHT_ON` | **`L:A32NX_AUTOPILOT_1_ACTIVE`** | No light lvar; use FG status (0/1) [api]. |
| `A32NX_FCU_AP_2_LIGHT_ON` | **`L:A32NX_AUTOPILOT_2_ACTIVE`** | No light lvar; use FG status (0/1) [api]. |
| `A32NX_FCU_ATHR_LIGHT_ON` | **`L:A32NX_AUTOTHRUST_STATUS`** (0=off,1=armed,2=active) | No light lvar; use ATHR status [api]. |
| `A32NX_FCU_LOC_LIGHT_ON` | **`L:A32NX_FCU_LOC_MODE_ACTIVE`** | Reuses A32NX name; published by FCU [FCU-src FcuPublisher, api]. |
| `A32NX_FCU_APPR_LIGHT_ON` | **`L:A32NX_FCU_APPR_MODE_ACTIVE`** | Reuses A32NX name; published by FCU [FCU-src FcuPublisher, api]. |
| `A32NX_FCU_EXPED_LIGHT_ON` | **`L:A32NX_FMA_EXPEDITE_MODE`** (==1 when active) | No light lvar; use FMA expedite flag [FCU-src SpeedManager]. |
| `A32NX_FCU_EFIS_L_FD_LIGHT_ON` | **`AUTOPILOT FLIGHT DIRECTOR ACTIVE`** (per-index via `:1`/`:2`) | No A380 FD light lvar; use MSFS FD-active simvar [api]. |

---

## EFIS-CP baro (per side)

The A320 `A32NX_FCU_EFIS_L_*` baro family does **not exist** on the A380. Mapping:

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX_FCU_EFIS_L_BARO_IS_INHG` (unit) | **`L:XMLVAR_Baro_Selector_HPA_1`** (0=Hg/inHg, 1=hPa) — note inverted sense vs A320 | Unit selector read by BaroManager [FCU-src BaroManager], doc [api]. Also surfaced as bit 11 of `L:A32NX_FCU_LEFT_EIS_DISCRETE_WORD_1` / `_RIGHT_` (bit set = inHg) [FCU-src OutputBusManager]. |
| `A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE` (QNH/STD label) | **Baro mode** via `L:XMLVAR_Baro1_Mode` (0=QFE,1=QNH,2=STD) [api]; FCU emits STD/QNH as bits 28/29 of `L:A32NX_FCU_LEFT_EIS_DISCRETE_WORD_2` (and `_RIGHT_`) | No `FCU_EFIS_L_DISPLAY_BARO_*` var. [FCU-src OutputBusManager], [api]. |
| `A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE` | (same as above — `XMLVAR_Baro1_Mode` / EIS DISCRETE WORD 2 bits) | NO dedicated A380 var. |
| `KOHLSMAN SETTING MB:1` / `HG:1` | **`L:A32NX_FCU_LEFT_EIS_BARO_HPA`** (hPa) and **`L:A32NX_FCU_LEFT_EIS_BARO`** (inHg) — and `_RIGHT_` for F/O | A380 publishes its own EIS baro readout lvars [FCU-src OutputBusManager]. `KOHLSMAN SETTING MB:1`/`HG:1` MSFS simvars also still readable per [api]. |
| Baro SET event | **Baro is rotated via `H:A380X_EFIS_CP_BARO_PUSH_/PULL_{index}` + knob inc/dec**; no clean `A32NX.FCU_EFIS_L_BARO_SET` analog | UNCERTAIN exact event spelling — verify in-sim (see EFIS-CP events row above). |
| Preselected QNH readout | **`L:A380X_EFIS_L_BARO_PRESELECTED`** / **`L:A380X_EFIS_R_BARO_PRESELECTED`** (hPa or inHg; 0 when not shown) | A380X-prefixed. Confirmed [FCU-src BaroManager `preselectLocalVarName`], [simvars `A380X_EFIS_{side}_BARO_PRESELECTED`]. Marked "Not for FBW systems use" in [simvars] but is the readout source. |

---

## Other

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| Altitude increment (100/1000) selector | `L:XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT` (100..1000); set via `A32NX.FCU_ALT_INCREMENT_SET` / `_TOGGLE` | SAME as A320 [api]. |
| Metric alt toggle | **`L:A32NX_METRIC_ALT_TOGGLE`** | Present in A380 (PFD publisher) [FCU-src PFDSimvarPublisher]. Reuses A32NX name. |
| EXPED present? | **YES** — A380 has EXPED [FCU-src AutopilotManager]. |
| Separate AP1/AP2? | **YES** — separate AP1 and AP2 buttons/events [FCU-src AutopilotManager]. |

---

## Items with NO A380 analog (flag for the port)

- `A32NX_FCU_AFS_DISPLAY_*` family (SPD/HDG/ALT/VS value + managed) — **does not exist**; use the `A32NX_AUTOPILOT_*_SELECTED` / `AUTOPILOT ALTITUDE LOCK VAR:3` value vars and the `A32NX_FCU_*_MANAGED[_DOT|_DASHES]` indicator vars instead.
- `A32NX_FCU_*_LIGHT_ON` family — **does not exist**; derive from FG/FMA status vars (`AUTOPILOT_1/2_ACTIVE`, `AUTOTHRUST_STATUS`, `FCU_LOC/APPR_MODE_ACTIVE`, `FMA_EXPEDITE_MODE`, `AUTOPILOT FLIGHT DIRECTOR ACTIVE`).
- `A32NX.FCU_EFIS_L_FD_PUSH` / `A32NX_FCU_EFIS_L_BARO_*` family — **does not exist**; FD via `TOGGLE_FLIGHT_DIRECTOR`, baro via `A380X_EFIS_CP_BARO_*` H-events + `A380X_EFIS_{L|R}_BARO_PRESELECTED` readout.

## Uncertain / to verify in-sim

1. **EFIS-CP baro H-event spelling** (`A380X_EFIS_CP_BARO_PUSH_/PULL_/SET_{index}`): inferred from the FBW catalog + local `BaroManager` behaviour, but NOT present in `a380x-input-events.md`. Confirm the exact token (and whether a `_SET_` form exists).
2. **HDG/VS push-pull event names**: in-sim FCU fires the `A32NX.FCU_TO_AP_HDG_PUSH/PULL` and `A32NX.FCU_TO_AP_VS_PULL` variants, while the public API page documents the plain `A32NX.FCU_HDG_PUSH/PULL` / `A32NX.FCU_VS_PULL`. Both likely work; prefer the `_TO_AP_` variant to match in-sim behaviour, or test which the autoflight system reacts to.
3. **EFIS unit var sense**: `XMLVAR_Baro_Selector_HPA_1` is `1=hPa, 0=inHg` (opposite polarity to the A320 `..._IS_INHG`). Adjust logic accordingly.
