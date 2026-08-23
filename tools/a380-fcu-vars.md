# A380X FCU / AFS Control Panel variable mapping (vs A32NX FCU)

Reference for porting the A320 FCU accessibility integration to the A380X.

> [!CAUTION]
> **This page was written BEFORE FlyByWire #10855 ("add FG part to PRIM", a380x `1bbd304`,
> 18 Aug 2026) and several of its headline claims are now FALSE.** It is kept because the
> A320-vs-A380 mapping and the H-event paths are still useful, but do not take a row here as
> current without checking it against the FBW tree. Corrected below where it matters; the
> authoritative account of the move is the "FG-into-PRIM variable migration" section of
> [docs/a380x.md](../docs/a380x.md), and the event names are pinned by
> `FlyByWireA380EventContractTests`.
>
> Specifically: the A380 **does** now publish `A32NX_FCU_*_LIGHT_ON` per-button lights; the
> `A32NX_FCU_EFIS_{L,R}_*` family **does** now exist and is how the EFIS-CP is read and driven;
> the A380 FCU has **no EXPED button**; and the `A32NX.FCU_TO_AP_*` events below were deleted.

## TL;DR — how the A380 FCU differs architecturally

- The A380 "FCU" is a **self-contained TypeScript instrument** (`fbw-a380x/src/systems/instruments/src/FCU/`), not the A32NX glass FCU. Its knobs/buttons are still driven by the **legacy `H:A320_Neo_FCU_*` H-events**, and the managers translate those into **`K:A32NX.FCU_*` key events** plus they **write the SAME `L:A32NX_*` display L:vars the A320 uses** — NOT the A320's ARINC-style `A32NX_FCU_AFS_DISPLAY_*` words. Those `A32NX_FCU_AFS_DISPLAY_*` vars do **not exist** on the A380.
- ~~The A380 does NOT publish per-button FCU light L:vars.~~ **REVERSED by #10855**: `A32NX_FCU_{LOC,APPR}_LIGHT_ON` and the whole `A32NX_FCU_EFIS_{L,R}_*_LIGHT_ON` family are now the per-button state, written per frame by the WASM and read by the cockpit's own INDICATOR_CODE. The FG/FMA status L:vars this bullet pointed at are the ones that were deleted.
- ~~EFIS-CP baro is completely re-architected; there is no `A32NX_FCU_EFIS_L_*` family.~~ **REVERSED by #10855**: the EFIS-CP is driven by `A32NX.FCU_EFIS_{L,R}_BARO_{PUSH,PULL}` (PUSH=STD, PULL=QNH — opposite of the A32NX knob). Mode is still read back from the stock `KOHLSMAN SETTING STD:n`.

Sources cited per row: **[FCU-src]** = the FCU instrument managers/components; **[simvars]** = `fbw-a380x/docs/a380-simvars.md`; **[api]** = flybywiresim.com a380x flight-deck-api page; **[input-events]** = `fbw-a380x/docs/a380x-input-events.md`.

---

## Events (knobs / pushbuttons)

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX.FCU_HDG_SET` | `A32NX.FCU_HDG_SET` (via H-event `A320_Neo_FCU_HDG_SET` → reads `L:A320_Neo_FCU_HDG_SET_DATA`) | SAME key event. HDG SET path confirmed [FCU-src HeadingManager.onEvent]. `A32NX.FCU_HDG_SET` listed [api]. |
| `A32NX.FCU_HDG_PUSH` | `A32NX.FCU_HDG_PUSH` | **CORRECTED post-#10855.** SAME name. The `_TO_AP_` variant this row used to recommend was DELETED with the standalone FCU managers; the surviving event drives the AFS knob directly (`hdg_trk_knob.pushed`, SimConnectInterface.cpp:2349). |
| `A32NX.FCU_HDG_PULL` | `A32NX.FCU_HDG_PULL` | **CORRECTED post-#10855.** SAME name (`hdg_trk_knob.pulled`, SimConnectInterface.cpp:2356). |
| `A32NX.FCU_SPD_SET` | `A32NX.FCU_SPD_SET` | SAME [api]. In-sim path: H-event `A320_Neo_FCU_SPEED_SET` → `L:A320_Neo_FCU_SPEED_SET_DATA` [FCU-src SpeedManager]. |
| `A32NX.FCU_SPD_PUSH` / `_PULL` | `A32NX.FCU_SPD_PUSH` / `A32NX.FCU_SPD_PULL` | SAME [api]. (FCU SpeedManager handles push/pull internally via `K:SPEED_SLOT_INDEX_SET`, but the documented stable events keep the A32NX names.) |
| `A32NX.FCU_ALT_SET` | `A32NX.FCU_ALT_SET` | SAME [api]. |
| `A32NX.FCU_ALT_PUSH` | `A32NX.FCU_ALT_PUSH` | SAME. AltitudeManager fires `K:A32NX.FCU_ALT_PUSH` (+`K:ALTITUDE_SLOT_INDEX_SET 2`) [FCU-src AltitudeManager.onHEvent]. |
| `A32NX.FCU_ALT_PULL` | `A32NX.FCU_ALT_PULL` | SAME [FCU-src AltitudeManager.onHEvent]. |
| `A32NX.FCU_ALT_INCREMENT_SET` | `A32NX.FCU_ALT_INCREMENT_SET` (also `A32NX.FCU_ALT_INCREMENT_TOGGLE`) | SAME [api]. Selector value var `L:XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT` (100..1000) [api]. |
| `A32NX.FCU_VS_SET` | `A32NX.FCU_VS_SET` | SAME [api]. In-sim: H-event `A320_Neo_FCU_VS_SET` → `L:A320_Neo_FCU_VS_SET_DATA` [FCU-src VerticalSpeedManager]. |
| `A32NX.FCU_VS_PUSH` | `A32NX.FCU_VS_PUSH` | SAME (documented) [api]. Note: the in-sim VerticalSpeedManager handles VS PUSH internally; only PULL fires a key event. |
| `A32NX.FCU_VS_PULL` | `A32NX.FCU_VS_PULL` | **CORRECTED post-#10855.** SAME name (`vs_fpa_knob.pulled`, SimConnectInterface.cpp:2460). |
| `A32NX.FCU_EXPED_PUSH` | *(none)* | **CORRECTED post-#10855.** The A380 FCU has **NO EXPED button**; both the event and `L:A32NX_FMA_EXPEDITE_MODE` are gone from the A380 tree. The old "A380 HAS EXPED" claim on this row is what put an Expedite control in the app. A320-only. |
| `A32NX.FCU_APPR_PUSH` | `A32NX.FCU_APPR_PUSH` | SAME [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_LOC_PUSH` | `A32NX.FCU_LOC_PUSH` | SAME [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_AP_1_PUSH` | `A32NX.FCU_AP_1_PUSH` | SAME. A380 **has separate AP1/AP2** — AutopilotManager fires `K:A32NX.FCU_AP_1_PUSH` [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_AP_2_PUSH` | `A32NX.FCU_AP_2_PUSH` | SAME [FCU-src AutopilotManager.onEvent]. |
| `A32NX.FCU_ATHR_PUSH` | **`K:AUTO_THROTTLE_ARM`** (stock) | CORRECTION: the A380X FCU A/THR button uses the STOCK `K:AUTO_THROTTLE_ARM`, not the FBW dot-event [fcu.xml:131, source audit]. |
| `A32NX.FCU_AP_DISCONNECT_PUSH` | `A32NX.FCU_AP_DISCONNECT_PUSH` | SAME [api]. |
| `A32NX.FCU_ATHR_DISCONNECT_PUSH` | `A32NX.FCU_ATHR_DISCONNECT_PUSH` | SAME [api]. |
| `A32NX.FCU_SPD_MACH_TOGGLE_PUSH` | `A32NX.FCU_SPD_MACH_TOGGLE_PUSH` | SAME [api]. In-sim toggles via `K:AP_MANAGED_SPEED_IN_MACH_ON/OFF` [FCU-src SpeedManager.onSwitchSpeedMach]. |
| `A32NX.FCU_TRK_FPA_TOGGLE_PUSH` | **(no event — direct L:var write)** | CORRECTION: NOT wired on the A380X. The cockpit button only runs RPN `(L:A32NX_TRK_FPA_MODE_ACTIVE) ! (>L:A32NX_TRK_FPA_MODE_ACTIVE)`. Drive it by writing `L:A32NX_TRK_FPA_MODE_ACTIVE` (0/1) directly [A32NX_Interior_FCU.xml:137, source audit]. |

## EFIS-CP events (FD / baro)

| A320 item | A380X equivalent | Status / source |
|---|---|---|
| `A32NX.FCU_EFIS_L_FD_PUSH` (+ `_R_`) | **NO `A32NX.FCU_EFIS_*` event on A380, AND the FD is UNCONTROLLABLE on this build.** `TOGGLE_FLIGHT_DIRECTOR` (indexed or not), the `A320_Neo_FCU_FD_n_PUSH` H-event, and direct writes to `A380X_EFIS_L_FD_BUTTON_IS_ON` / `A32NX_FCU_LEFT_EIS_FD_ACTIVE` ALL fail — FBW recomputes the L-var every tick (verified live). The MSFSBA FD button + event were REMOVED. | FD-on STATE is still read-only via `AUTOPILOT FLIGHT DIRECTOR ACTIVE` (`FD_ACTIVE`, kept as a status readout). |
| `A32NX.FCU_EFIS_L_BARO_PUSH/PULL` (+ `_R_`) | `A32NX.FCU_EFIS_{L,R}_BARO_{PUSH,PULL}` | **CORRECTED post-#10855.** SAME names as the A32NX, OPPOSITE polarity: **PUSH=STD, PULL=QNH** (A380FcuComputer.cpp:2142-2150 clears `std_active` on pull, sets it on push). The `H:A380X_EFIS_CP_BARO_{PUSH,PULL}_{1,2}` events this row used to recommend were DELETED along with `MsfsBaroManager.ts` — firing them is a silent no-op. PULL is idempotent because `pin_prog_qfe_avail` is hardcoded false, so both may be fired unconditionally. |

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

- `A32NX_FCU_AFS_DISPLAY_*` family (SPD/HDG/ALT/VS value + managed) — **does not exist**; use the `A32NX_AUTOPILOT_*_SELECTED` / `AUTOPILOT ALTITUDE LOCK VAR:3` value vars and the `A32NX_FCU_*_MANAGED[_DOT|_DASHES]` indicator vars instead. **The `_SELECTED` vars read in SI base units via the data-def read** — convert in `ProcessSimVarUpdate`: heading/track rad→deg (`×180/π`), FPA rad→deg, V/S m/s→fpm (`×196.8503937`), speed m/s→kt (`×1.943844`); altitude is already feet. (Missing this = "V/S reads 15 not 2000".)
- `A32NX_FCU_*_LIGHT_ON` family — **does not exist**; derive from FG/FMA status vars (`AUTOPILOT_1/2_ACTIVE`, `AUTOTHRUST_STATUS`, `FCU_LOC/APPR_MODE_ACTIVE`, `FMA_EXPEDITE_MODE`, `AUTOPILOT FLIGHT DIRECTOR ACTIVE`).
- `A32NX.FCU_EFIS_L_FD_PUSH` / `A32NX_FCU_EFIS_L_BARO_*` family — **does not exist**; the **FD is uncontrollable on this build (button removed)** — see the EFIS table row above; baro via the EFIS-CP controls (driven through the MobiFlight calculator path, `ExecuteCalculatorCode("{val} (>L:{key})")`) + the decoded `A380X_EFIS_{L|R}_BARO_PRESELECTED` / ARINC `*_EIS_BARO_HPA` readout (decoded, never shown raw).

## Uncertain / to verify in-sim

1. **EFIS-CP baro H-event spelling** (`A380X_EFIS_CP_BARO_PUSH_/PULL_/SET_{index}`): inferred from the FBW catalog + local `BaroManager` behaviour, but NOT present in `a380x-input-events.md`. Confirm the exact token (and whether a `_SET_` form exists).
2. **HDG/VS push-pull event names**: in-sim FCU fires the `A32NX.FCU_TO_AP_HDG_PUSH/PULL` and `A32NX.FCU_TO_AP_VS_PULL` variants, while the public API page documents the plain `A32NX.FCU_HDG_PUSH/PULL` / `A32NX.FCU_VS_PULL`. Both likely work; prefer the `_TO_AP_` variant to match in-sim behaviour, or test which the autoflight system reacts to.
3. **EFIS unit var sense**: `XMLVAR_Baro_Selector_HPA_1` is `1=hPa, 0=inHg` (opposite polarity to the A320 `..._IS_INHG`). Adjust logic accordingly.
