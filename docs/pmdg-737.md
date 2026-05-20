# PMDG 737-800 NG3 Patterns

Reference for working with `PMDG737Definition`, `PMDGNG3DataManager`, `PMDGNG3DataStruct`, and `PMDG737CDUForm`. Companion to the general PMDG patterns in CLAUDE.md (the 777 section).

## Scope

Only the 737-800 BW HD is currently supported. The data struct's `AircraftModel` field (`ushort`) discriminates variants if -600 / -700 / -900 support is added later. `EVT_*_600` and `EVT_*_800` entries are both in `EventIds`, so wiring a variant is largely a one-line `_simpleEventMap` change.

## SDK CDA names

`PMDG_NG3_Data` / `PMDG_NG3_Control` / `PMDG_NG3_CDU_0` / `PMDG_NG3_CDU_1`. Event base offset: `THIRD_PARTY_EVENT_ID_MIN = 69632` (same as the 777X).

`PMDGNG3DataStruct` ends with `byte[255]` reserved tail. Do **not** reuse the 777's 84 — SimConnect silently truncates on size mismatch.

## Two CDUs, not three

All CDU-side arrays are `[2]` (Captain = 0, F/O = 1). No observer CDU. `PMDG737CDUForm` uses the raw 0/1 ordering — no L/C/R dropdown swap like the 777 form has.

## No FPA mode

NG3 has no `MCP_FPA` field, no `MCP_annunVS_FPA`, and the VS dialog drops the FPA toggle the 777 dialog has. The VS dialog gates input on `MCP_annunVS` (not `MCP_annunVS_FPA`).

## Annunciator naming differs from 777

Use the SDK header verbatim:
- `MCP_annunLVL_CHG` (not FLCH)
- `MCP_annunHDG_SEL` (not HDG_HOLD)
- `MCP_annunVOR_LOC` (not LOC)

Do **not** translate names from 777 conventions.

## MCP value entry is dialog-based

`MCP_Heading`, `MCP_Altitude`, `MCP_IASMach`, `MCP_VertSpeed` are declared with `PreventTextInput = true` in `GetPMDGVariables()` (same pattern as the 777). The panel UI shows them as read-only readouts; values are set via the four MCP dialogs accessed by Shift+H / Shift+S / Shift+A / Shift+V. This matches the 777 UX. Do not add inline text inputs.

## Selector ValueDescriptions must match SDK enum positions verbatim

The panel UI sends the dropdown index as the position parameter to the sim. If the `ValueDescriptions` list order doesn't match the SDK's enum-position order, the user's pick silently triggers a different physical position — flight-relevant for things like speed reference, autobrake, N1 set.

When adding or editing any `Selector(...)` entry in `GetPMDGVariables`, cross-reference the corresponding SDK header comment (`PMDG_NG3_SDK.h`) and order the positions to match. If the SDK comment elides middle positions ("0: X ... N: Y"), look up the standard NG3 -800 cockpit layout and document the inferred middle positions in a code comment.

DU source selectors (`MAIN_MainPanelDUSel`, `MAIN_LowerDUSel`) have a "reverse sequence for FO" caveat: index 1 of the array reads positions in reverse enum order. Give the FO variant its own mirror-ordered `ValueDescriptions` list — don't share with the captain's.

## String display fields

`IRS_DisplayLeft[7]`, `IRS_DisplayRight[8]`, `ELEC_MeterDisplayTop[13]`, `ELEC_MeterDisplayBottom[13]`, `AIR_DisplayFltAlt[6]`, `AIR_DisplayLandAlt[6]`, `FMC_flightNumber[9]`.

`PMDGNG3DataManager` exposes a non-interface `GetStringFieldValue(string)` method for ASCII decoding — callers needing string content must cast `IPMDGDataManager` to `PMDGNG3DataManager`.

## Doors as annunciators

12 individual `DOOR_annun*` bools (FWD_ENTRY, FWD_SERVICE, AIRSTAIR, …) plus a 4-state `PED_FltDkDoorSel` enum. No 16-byte `DOOR_state[16]` array like the 777.

## Press-counter fields

`COMM_Attend_PressCount` and `COMM_GrdCall_PressCount` are byte counters that increment on each press. `ProcessSimVarUpdate` detects edges via signed-wrapping delta against the last known counter value (instance fields `_lastAttendPressCount` / `_lastGrdCallPressCount`).

## ACP observer slot is unused

`COMM_SelectedMic` is a 3-wide array in the SDK for binary compatibility, but the 737 cockpit has no observer ACP — index 2 always reads as 0 and is not exposed in the panel.

## Options.ini requirement

`737NG3_Options.ini` lives at:

```
%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages\Community\pmdg-aircraft-738\work\737NG3_Options.ini
```

Must contain:

```ini
[SDK]
EnableDataBroadcast=1
EnableCDUBroadcast.0=1
EnableCDUBroadcast.1=1
```

User-managed (same as the 777's options.ini workflow today).

## Fire handles need an active fire to test

PMDG NG3 mechanically locks fire handles in the "In" position unless a fire warning is active for the corresponding engine/APU. The `_guardedMap` entries for `FIRE_EngineHandle_1_Press` / `FIRE_APUHandle_Press` / `FIRE_EngineHandle_2_Press` chain `EVT_FIRE_UNLOCK_SWITCH_*` → `EVT_FIRE_HANDLE_*_TOP`, mirroring the 777 pattern; the SDK enforces the lock so presses are no-ops outside an active fire scenario.

`FIRE_HandlePos[3]` array indexing: `[0]=Engine 1, [1]=APU, [2]=Engine 2`. Inferred from sequential SDK event-ID ordering (`EVT_FIRE_HANDLE_ENGINE_1_TOP=697`, `_APU_TOP=698`, `_ENGINE_2_TOP=699`; `EVT_FIRE_UNLOCK_SWITCH_ENGINE_1=976`, `_APU=977`, `_ENGINE_2=978`). Same convention applies to `FIRE_HandleIlluminated[3]`. Verify in sim under an active fire scenario; if a tester reports the wrong handle moves on a fire press, swap the `DisplayName` strings on `FIRE_HandlePos_1` / `_2` (and on `FIRE_HandleIlluminated_1` / `_2`).

## EFB support

The 737 does not yet have an EFB tablet bridge wired up — it's planned as a follow-up once the panels are stable. The gating mechanism: `IPMDGAircraft.HasEFBSupport` (default `false`). `PMDG777Definition` overrides it to `true`; `PMDG737Definition` leaves it at the default.

`MainForm` gates three sites on `HasEFBSupport`: the startup EFB plumbing (constructor), the `ShowPMDGEFB` hotkey dispatch (Shift+T), and the aircraft-change EFB plumbing. With the gate at default, the 737 user never sees the 777's EFB mod-package prompt and Shift+T is a no-op.

When the 737 EFB lands:
1. Build `PMDG737EFBForm` (companion to `PMDG777EFBForm`).
2. Override `HasEFBSupport => true` in `PMDG737Definition`.
3. Make `MainForm.ShowPMDGEFBDialog` polymorphic — pick the right form based on `currentAircraft` type.
4. Extend `EFBModPackageManager.Variants` to include `pmdg-aircraft-738`.
