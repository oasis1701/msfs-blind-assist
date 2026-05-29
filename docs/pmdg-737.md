# PMDG 737-800 NG3 Patterns

Reference for working with `PMDG737Definition`, `PMDGNG3DataManager`, `PMDGNG3DataStruct`, and `PMDG737CDUForm`. Companion to the general PMDG patterns in CLAUDE.md (the 777 section).

## Scope

Only the 737-800 BW HD is currently supported. The data struct's `AircraftModel` field (`ushort`) discriminates variants if -600 / -700 / -900 support is added later. `EVT_*_600` and `EVT_*_800` entries are both in `EventIds`, so wiring a variant is largely a one-line `_simpleEventMap` change.

## Panel structure

Overhead: Electrical, ADIRU, Hydraulics, Fuel, Engines, Anti-Ice, Air Systems,
Lights, Signs, Oxygen, Wipers, Flight Controls, Flight Recorder

Glareshield: Warnings, EFIS Captain, EFIS First Officer, MCP, Display Select
(absorbs both DU source selectors and NAVDIS source selectors)

Forward Panel: Landing Gear, Autobrake, GPWS, Instruments

Pedestal: Control Stand, Transponder, Fire Protection, Cargo Fire,
Communication, Flight Deck Door, Trim

Layout philosophy: mirror the PMDG 777 panel structure where logical; prioritize
screen-reader navigability over physical-cockpit faithfulness. No empty panels,
no location-based buckets, no tiny single-control panels except where there's no
sensible merge target (Landing Gear, Autobrake, Oxygen, Flight Recorder).

## SDK CDA names

`PMDG_NG3_Data` / `PMDG_NG3_Control` / `PMDG_NG3_CDU_0` / `PMDG_NG3_CDU_1`. Event base offset: `THIRD_PARTY_EVENT_ID_MIN = 69632` (same as the 777X).

`PMDGNG3DataStruct` ends with `byte[255]` reserved tail. Do **not** reuse the 777's 84 — SimConnect silently truncates on size mismatch.

## Two CDUs, not three

All CDU-side arrays are `[2]` (Captain = 0, F/O = 1). No observer CDU. `PMDG737CDUForm` uses the raw 0/1 ordering — no L/C/R dropdown swap like the 777 form has.

## CDU keys must use TransmitClientEvent, not the CDA write

`PMDG737CDUForm.SendCDUKey` dispatches every CDU key (letters, LSKs, function
keys, CLR/DEL/EXEC) via `SendEventViaTransmitWithTarget(eventId,
MOUSE_FLAG_LEFTSINGLE = 0x20000000)` — a self-contained press+release click.

Do **not** use the CDA path (`SendEvent(name, id, 1)`) the 777 form uses for most
keys. The NG3 FMC ignores the CDA `{eventId, 1}` write for CDU keypad events —
the click sound plays but nothing registers (same momentary-button behavior
proven for the MCP buttons). This is the documented PMDG convention (the SDK's
flight-director sample uses `MOUSE_FLAG_LEFTSINGLE`/`LEFTRELEASE`, and TFM uses
TransmitClientEvent for every CDU key) and matches the 777's own FMCCOMM/HOLD
path. A single `LEFTSINGLE` is a complete click — no separate `LEFTRELEASE` is
needed for CDU keys. Live-verified against the NG3 (CLR + letter entry) with
`tools/CDUTest` (`CDUTest 737 transmit <eventId> 536870912`).

`tools/CDUTest` is a standalone single-shot probe: it maps the chosen Control
CDA (`PMDG_NG3_Control` / `PMDG_777X_Control`) and fires one event via either
`cda` or `transmit`, for confirming which dispatch shape a given switch accepts.

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

## Altimeter access

ALTIMETER_SETTING is an MSFS simvar (not a PMDG var). It is NOT in the panel
tree. Set/read via existing global hotkeys:

- Input mode `Ctrl+B` → set the altimeter (input dialog accepts hPa 900–1060 or
  inHg 26.50–31.30; magnitude-based branching, locale-safe)
- Output mode `B` → read current altimeter setting in both units; announces
  "Altimeter standard" if at 29.92 inHg

Implementation in PMDG737Definition.HandleHotkeyAction. MainForm has no generic
fallback for these actions — every PMDG aircraft must implement them or the
hotkey silently does nothing.

## Guarded selector dispatch

Two primitives on IPMDGDataManager:

- `SendGuardedToggle(guard, switch)` — guard open → switch event WITHOUT parameter
  → guard close. Use for guarded 2-position toggles only.
- `SendGuardedSelector(guard, switch, targetPosition)` — guard open → switch
  event WITH targetPosition → guard close. Use for guarded ≥3-position selectors
  (battery, standby power, emergency exit lights).

PMDG737Definition.HandleUIVariableSet picks the right primitive by inspecting
`varDef.ValueDescriptions.Count`. Never call SendGuardedToggle for a 3-position
selector — the SDK ignores the user's target position and the switch silently
fails to move.

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

The PMDG 737-600 / -700 / -800 / -900 EFB has full parity with the PMDG 777. The 737 ships the
**byte-identical** EFB application bundle as the 777 (MD5-confirmed for `PMDGTablet.js` across all
five aircraft variants), so the bridge JS, the shared `zzz-pmdg-efb-accessibility` Community
package, the `EFBBridgeServer`, and every EFB app panel are reused unchanged. The only per-variant
data is the path string inside `PMDGTabletCA.html` (`pmdg-737-800`, `pmdg-737-600`, etc.) —
`EFBModPackageManager.Variants` enumerates all four 737 entries and creates a tablet override
folder for each.

- Enabled via `IPMDGAircraft.HasEFBSupport => true` in `PMDG737Definition` — this single flag turns
  on the startup/aircraft-change EFB plumbing (bridge-server start + mod-package install prompt) and
  the Shift+T dispatch (`MainForm` gates those sites on `HasEFBSupport`).
- `Forms/PMDGEFB/PMDGEFBForm.cs` is the **shared** accessible form for both 737 and 777. The
  constructor takes `currentAircraft.AircraftCode`; the form title reads `"PMDG 737 EFB"` for
  `PMDG_737` and `"PMDG 777 EFB"` otherwise. All seven EFB app panels are hosted:
  Dashboard / Preferences / Navdata / Performance / Ground Ops / Weights & Balance / Manuals
  (plus a Display debug tab and Ctrl+Shift+{R,C,D,E} diagnostic hotkeys).
- `MainForm.ShowPMDGEFBDialog` constructs `PMDGEFBForm(efbBridgeServer, announcer, AircraftCode)`
  unconditionally — no per-aircraft switch.
- `EFBModPackageManager.Variants` includes the four 737 entries (`pmdg-aircraft-736 / 737 / 738 / 739`
  with tablet subfolders `pmdg-737-600 / -700 / -800 / -900`). Existing installs pick up newly
  installed variants via `UpdateModPackage`'s `HasMissingVariantOverride` check; the same path
  also re-runs after a `BridgeVersion` bump.

First-time install needs **one sim restart** for MSFS to load the override HTML (the bridge then
injects when the tablet opens). The 738's Flight Attendant Panel (`PMDGFlightAttendantPanel.*`)
is a separate VCockpit instrument with its own DOM and is NOT part of the EFB bridge — making it
accessible would need its own bridge package and is out of scope.
