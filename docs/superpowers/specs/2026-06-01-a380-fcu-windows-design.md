# A380 Fenix-style FCU Windows — Design

**Date:** 2026-06-01
**Branch:** fly-by-wire-A380-integration
**Status:** Approved (design phase)

## Goal

Give the FlyByWire A380X the same rich, screen-reader-friendly FCU/autoflight
windows the Fenix A320 has — dedicated Speed, Heading, Altitude, V/S–FPA,
Autopilot, and Baro windows, each opened from input mode, each combining value
entry with inline knob actions (Push/Pull/mode toggles) and a live readout.
Replace the A380's current bare `ValueInputForm` dialogs (Ctrl+H/S/A/V/B) and add
the currently-missing Autopilot panel on Ctrl+P. Provide live-sim verification
tooling so every control is proven to actually drive the aircraft.

The A380's underlying FCU set/readback plumbing already exists in
`FlyByWireA380Definition.cs` and is live-verified (FCU set/readback, SI-unit
conversions, V/S sign, managed-mode dot, AP/APPR/LOC/EXPED push events). This
work is the **UI layer + the Ctrl+P panel + verification tooling** on top of that
plumbing — it does not change the verified set/readback logic.

## Non-goals

- No changes to the working Fenix window classes (no shared abstraction — the set
  mechanisms differ: Fenix counter-jump L-vars vs. A380 `*_SET` events).
- No Flight Director *control* (uncontrollable on this FBW build; read-only only).
- No new key chords — the `FCUSet*` hotkey actions already exist in the map.

## Architecture

Six new forms under `Forms/FBWA380/`, all delegating their set/readback to methods
that already live on `FlyByWireA380Definition` (no FCU logic duplicated in forms):

```
Input-mode hotkey ──► FlyByWireA380Definition.HandleHotkeyAction
   Ctrl+S FCUSetSpeed      ──► FBWA380SpeedWindow
   Ctrl+H FCUSetHeading    ──► FBWA380HeadingWindow
   Ctrl+A FCUSetAltitude   ──► FBWA380AltitudeWindow
   Ctrl+V FCUSetVS         ──► FBWA380VSWindow
   Ctrl+P FCUSetAutopilot  ──► FBWA380AutopilotWindow   (NEW dispatch)
   Ctrl+B FCUSetBaro       ──► FBWA380BaroWindow
            │
            ├─ set:  A32NX.FCU_*_SET events / calc code / KOHLSMAN_SET
            └─ read: A32NX_AUTOPILOT_*_SELECTED + managed flags + AP states
```

### Shared window shape (`FBWA380FCUWindowBase` or a shared helper)

Every window provides:
- A **read-only live readout label** at the top: current FCU value + managed/selected
  mode, refreshed on open and after every action by re-reading the SimConnect cache.
- A value **TextBox** (where the function takes a value) with Enter = Set.
- A **Set** button.
- A row of inline action buttons (Push / Pull / mode toggles), per function.
- Escape closes and returns focus to the simulator.

**Screen-reader rule (CRITICAL):** button presses and combo-box changes are NOT
re-announced (NVDA already speaks UI interaction). Only value-set confirmations and
background readouts speak (via `AnnounceImmediate`). Initial-open readout speaks the
current value.

## Components

### 1. FBWA380SpeedWindow (Ctrl+S)
- Value: 100–399 kt **or** 0.10–0.99 Mach.
- Set: `A32NX.FCU_SPD_SET` (knots as uint; Mach as Mach×100 uint — matches existing
  `ShowFCUSpeedDialog` converter).
- Buttons: **Push** `A32NX.FCU_SPD_PUSH`, **Pull** `A32NX.FCU_SPD_PULL`,
  **SPD/MACH toggle** `A32NX.FCU_SPD_MACH_TOGGLE_PUSH`.
- Readout: `A32NX_AUTOPILOT_SPEED_SELECTED` + `A32NX_FCU_SPD_MANAGED_DOT` +
  `FCU_MACH_MODE` (managed → "managed"; selected → knots or Mach).

### 2. FBWA380HeadingWindow (Ctrl+H)
- Value: 0–360°.
- Set: `A32NX.FCU_HDG_SET`.
- Buttons: **Push** `A32NX.FCU_TO_AP_HDG_PUSH`, **Pull** `A32NX.FCU_TO_AP_HDG_PULL`,
  **HDG·V/S ↔ TRK·FPA toggle** `A32NX.FCU_TRK_FPA_TOGGLE_PUSH`.
- Readout: `A32NX_AUTOPILOT_HEADING_SELECTED` + `A32NX_FCU_HDG_MANAGED_DASHES`.

### 3. FBWA380AltitudeWindow (Ctrl+A)
- Value: 100–49000 ft, **or** 30–14935 m when Metric (MTRS) is on.
- Set: `A32NX.FCU_ALT_INCREMENT_SET` (selected increment) then `A32NX.FCU_ALT_SET`
  (feet rounded to 100); metric input converted ft = m / 0.3048.
- Buttons: **Push** `A32NX.FCU_ALT_PUSH`, **Pull** `A32NX.FCU_ALT_PULL`,
  **100/1000 increment** selector (`XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT` /
  `A32NX.FCU_ALT_INCREMENT_SET`), **Metric toggle** `A32NX_METRIC_ALT_TOGGLE`.
- Readout: `FCU_ALT_VALUE` + `A32NX_FCU_ALT_MANAGED` (feet or metres per `_metricAlt`).

### 4. FBWA380VSWindow (Ctrl+V)
- Value: ±6000 fpm **or** ±9.9° FPA (signed).
- Set: signed → calculator code `{toSend} (>K:A32NX.FCU_VS_SET)` where FPA (|v|<100)
  is sent ×100, V/S sent as-is (matches existing `ShowFCUVSDialog`).
- Buttons: **Push** `A32NX.FCU_VS_PUSH`, **Pull** `A32NX.FCU_TO_AP_VS_PULL`.
- Readout: `A32NX_AUTOPILOT_VS_SELECTED` + `A32NX_AUTOPILOT_FPA_SELECTED` +
  `A32NX_TRK_FPA_MODE_ACTIVE` (m/s → fpm ×196.85; FPA in degrees).

### 5. FBWA380AutopilotWindow (Ctrl+P) — NEW
Each button shows its live engaged state in its label/accessible name:
- **AP1** `A32NX.FCU_AP_1_PUSH` (state `A32NX_AUTOPILOT_1_ACTIVE`)
- **AP2** `A32NX.FCU_AP_2_PUSH` (state `A32NX_AUTOPILOT_2_ACTIVE`)
- **A/THR engage** `AUTO_THROTTLE_ARM` (stock) — A380 A/THR arm path
- **A/THR disconnect** `A32NX.FCU_ATHR_DISCONNECT_PUSH`
- **AP disconnect** `A32NX.FCU_AP_DISCONNECT_PUSH`
- **APPR** `A32NX.FCU_APPR_PUSH` (state `A32NX_FCU_APPR_MODE_ACTIVE`)
- **LOC** `A32NX.FCU_LOC_PUSH` (state `A32NX_FCU_LOC_MODE_ACTIVE`)
- **EXPED** `A32NX.FCU_EXPED_PUSH` (state `A32NX_FMA_EXPEDITE_MODE`)
- **Flight Director** — read-only state (`A32NX_FCU_EFIS_L_FD_ACTIVE` /
  `A32NX_FCU_EFIS_R_FD_ACTIVE`); no control (uncontrollable on this build).

These engage events won't actuate on the ground (FBW gating); harness confirms the
state vars flip when airborne / when preconditions are met. The wiring is
source-correct per existing live-verified notes.

### 6. FBWA380BaroWindow (Ctrl+B) — enter altimeter ONCE, applies to both sides
- **One** QNH value field + a single **hPa/inHg** entry-unit toggle (not per side).
- **Set** applies to **both Captain and First Officer** in one action: fire
  `KOHLSMAN_SET` (hPa×16); if live testing shows it drives only one EIS, the handler
  additionally fires the per-side write so both end up synced — the user still types
  the value only once.
- A single **STD ↔ QNH** toggle that syncs both sides
  (`A32NX_FCU_LEFT_EIS_BARO_IS_STD` + `A32NX_FCU_RIGHT_EIS_BARO_IS_STD`).
- **Displays both readouts** (Captain + F/O), decoded from
  `A32NX_FCU_{LEFT,RIGHT}_EIS_BARO_HPA` (ARINC429 word) with unit from
  `XMLVAR_Baro_Selector_HPA_{1,2}` (1=hPa, 0=inHg) — so the user hears that *each*
  side took the value, while typing it once.

The exact event(s) that achieve the dual-set are pinned down empirically by the
`tools/fcu` harness as the first implementation step.

## Hotkey wiring

In `FlyByWireA380Definition.HandleHotkeyAction`, the `FCUSetSpeed`, `FCUSetHeading`,
`FCUSetAltitude`, `FCUSetVS`, and `FCUSetBaro` cases switch from opening
`ValueInputForm` to opening the new windows. A new `FCUSetAutopilot` case (Ctrl+P,
currently unhandled on the A380) opens `FBWA380AutopilotWindow`. Each case calls
`hotkeyManager.ExitInputHotkeyMode()` first, matching the Fenix dispatch pattern.

## Verification tooling — `tools/fcu/`

Reusable harness using the existing Coherent-debugger path (`tools/coherent-eval.ps1`;
no Dev Mode needed):
- **`fcu-probe.js`** — Coherent agent that reads all FCU outputs
  (`A32NX_AUTOPILOT_*_SELECTED`, managed flags, AP/APPR/LOC/EXPED states, baro words
  decoded) and can fire a `*_SET` value then re-read to report the round-trip.
- **`fcu-roundtrip.ps1`** — drives the matrix: for heading/speed/alt/VS, set a known
  value → wait → read back → PASS/FAIL on tolerance; for each AP button, toggle →
  confirm state var flips (where ground-gating allows).
- **`tools/fcu/README.md`** — usage + the single-inspector-socket caveat (close the
  app's Coherent clients before driving the page with the tool, and vice-versa).

## Testing / acceptance

No automated test project exists (SimConnect-driven UI app). Acceptance:
1. `tools/fcu` round-trip harness passes for every value control and every AP toggle
   against the live sim (run during implementation; results reported).
2. PR includes an in-sim manual test plan: open each window via its hotkey, set a
   value, confirm spoken readback and that the FCU actually moves; Baro window typed
   once updates both Capt + F/O.

## Risks / open items (resolved during implementation via the harness)

- **Baro dual-set mechanism:** whether `KOHLSMAN_SET` drives both EIS or needs a
  per-side fallback — determined live, first thing.
- **AP engage ground-gating:** several FCU engage events no-op on the ground; confirm
  state-var flips under valid preconditions.
- **Live readout latency:** `A32NX_AUTOPILOT_*_SELECTED` reads are OnRequest; the
  window refreshes the label on a short post-action delay (same pattern the existing
  Request*WithStatus readbacks use).
