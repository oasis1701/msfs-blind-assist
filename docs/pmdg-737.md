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
**byte-identical** EFB application bundle as the 777. The EFB is read and driven over the MSFS
Coherent debugger via the shared `coherent-pmdg-efb-agent.js` (generic agent that resolves controls
by TYPE, so new PMDG pages read automatically) + `CoherentPmdgEfbClient` + `FbwEfbForm` (the same
WebView2 form that the FBW flyPad uses). NO Community mod package is installed, NO HTTP bridge is
needed, and NO sim restart is required.

- Enabled via `IPMDGAircraft.HasEFBSupport => true` in `PMDG737Definition` — this single flag turns
  on the EFB plumbing (Coherent client startup) and the Shift+T dispatch (`MainForm` gates those on
  `HasEFBSupport`).
- Hotkeys: **Shift+T (input mode) = Captain EFB**, **Ctrl+Shift+T (input mode) = First Officer EFB**.
  The shared `FbwEfbForm.cs` shows the currently selected tablet side. Form title reads `"PMDG 737
  EFB"` for `PMDG_737` and `"PMDG 777 EFB"` otherwise.
- On startup, `Patching/LegacyEfbBridgeCleanup.cs` removes the retired Community packages
  (`zzz-pmdg-efb-accessibility`, `zzz-hs787-accessibility`) automatically. The old HTTP bridge
  (`EFBBridgeServer`) was deleted; Coherent transport is the only mechanism (shared with FBW A380).

## Interior section

A dedicated "Interior" panel section exposes the cockpit/cabin items PMDG models as
plain L-vars (names + polarity verified directly against PMDG's behavior XMLs in
`pmdg-aircraft-738\SimObjects\...\attachments\pmdg\` — `73X_Cockpit_Behavior.xml`,
`73X_Cabin_Ceiling/Walls_Behavior.xml`, `73X_Galley_Fwd/Aft_Behavior.xml`). Every dispatch shape
below was live-verified CLOSED-LOOP on 2026-06-12 with `tools/PMDGDispatchTester` (new
`lvar` / `lvarget` / `kev` commands — write, read back, write the other way, read back).

- **Cockpit Furniture** — sliding windows (CA/FO), sun visors, window shades, headrests,
  rudder-pedal adjust, jumpseat, armrests, storage cubbies (side/doc/glareshield), cubby bar,
  cupholder drinks, and the binder cookie-stash easter egg (PMDG's model Update drives the
  reveal once `L:CubbyTrigger` is set with the cubby bar raised — both live-verified).
- **Cabin Bins** — all 38 overhead "SPACE BIN" click-spots individually + Open All / Close All.
- **Cabin Items** — window-blind raise/lower-all composites (87 blinds incl. EE-row),
  cabin/galley lights, galley + class-divider curtains, lavatory doors.
- **Galley** — water on/off + cold/warm (pushbutton radio-pairs: set one L-var, clear its
  opposite), sink taps, coffee valves, sanitizer pumps, power-outlet covers, the two secret
  compartments, the forward-airstair control panel, and the FAP ground-service switch.

**Undocumented switch events.** The airstair panel (retract 1646 / extend 1648 / lights 1654 /
standby 1658) and the FAP ground-service switch (2050) have NO defines in the public SDK header,
but PMDG's switch-number == event-offset convention holds: `event_base + N` moves the
corresponding `switch_N_73X` read-back L-var (all live-verified 2026-06-12; ground service
two-way 0↔100; lights is 3-position — param 0/1/2 → L-var 0/50/100). The airstair
extend/retract/standby buttons are PUSHBUTTONS — dispatch is press-and-release (param 1, 350 ms,
param 0) or the switch L-var latches pressed. Note: switch presses verified, but the actual
STAIR did not deploy on the test airframe (`DOOR_annunAIRSTAIR` stayed 0) — the airstair is an
airframe option and may also need standby armed; without it the switches are no-ops.

Key dispatch rules (all in the `0-cabin` region of `HandleUIVariableSet`):

- **Jumpseat + armrests: the CDA parameter IS the position (0 = stowed/down, 1 = extended/up),
  NOT a press.** Live-verified: `cda 71633 1` → `L:switch_2001_73X` = 100, `cda 71633 0` → 0;
  armrests 70638–70641 ↔ `L:switch_1006..1009_73X` identical. The first implementation exposed
  these as momentary buttons that always sent parameter 1 — they "worked once" (extend) and never
  went back. They are now COMBOS whose varKey is the PMDG-owned read-back L-var (0/100 display
  scale) and whose set dispatches the SDK event with the 0/1 position. A raw SetLVar to those
  switch L-vars reverts (SDK-owned read-backs, same family as the 777's `switch_NNN_a`) — the
  event is the actuator; do NOT let them fall through to the generic LVar branch.
- **Sliding windows are NOT a bare L-var set.** PMDG's VC click code toggles
  `L:Window_OpenClose_CA/FO` **and** fires `K:TOGGLE_AIRCRAFT_EXIT_FAST` with exit index
  16 (CA) / 17 (FO), gated on PMDG-owned `L:CanOpenWindows` (ground + slow). The dispatch
  replicates that atomically via MobiFlight calculator code (with an unguarded
  SetLVar+SendEvent fallback when MobiFlight is absent). Both directions live-verified.
- **Visor deploy is blocked while the same-side window is open** (mirrors PMDG's guard).
- **ATTEND / GRD CALL buttons** (`COMM_AttendCallBtn` / `COMM_GrndCallBtn`) dispatch the SDK
  events via CDA **parameter 1** — live-verified (press counters increment). **Parameter 0 ALSO
  registers as a press** on this event family (same as the CDU keys) — unlike the
  jumpseat/armrest family where the parameter is a position. Never assume one family's parameter
  semantics for another; probe each. The PMDG ground-call horn keeps sounding until the button
  is pressed a second time — PMDG behaviour, not a stuck dispatch.
- Plain L-var toggles (bins, shades, visors, blinds, lights, curtains, lav doors) and drag
  positions (headrests, rudder pedals, clamped 0–100) write-stick both directions — verified.
  Composite bin/blind buttons loop `SetLVar` over static lists (`s_binLvars` / `s_blindLvars`);
  L-vars not fitted on the loaded cabin layout are harmless no-ops.
- **Drag-position varKeys carry a `_SET` suffix** (`headrest_CA_drag_h_SET` etc., Name = the bare
  L-var). MainForm only renders the TextBox + Set numeric input for keys containing "_SET"; a
  no-ValueDescriptions var without it falls through to the plain-button branch, whose click
  always dispatches value 1 — the position slammed to 1/100 on every press ("only goes to one
  position"). Keep the suffix on any future numeric-input L-var.
- **Cabin item audio is positional.** Bins (BinIn/Out), blinds (BlindIn/Out), curtains
  (CurtainIn/Out) and lavatory doors (\*lavatoryIn/Out) have wwise sounds at their CABIN
  location — from the cockpit they're attenuated to near-silence, and the cabin/galley LIGHTS
  have no sound at all (visual-only). "No sound from the cockpit" does not mean the control
  failed; the app's state-change announcement is the confirmation channel.
- Seats themselves are **not movable** — `L:capt_seat` / `L:fo_seat` are model-variant
  visibility selectors, not positions. Headrests are the only adjustable seat part.
