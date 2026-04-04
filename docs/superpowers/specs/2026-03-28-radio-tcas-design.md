# Radio Panel & TCAS Squawk Code Entry Design

**Date:** 2026-03-28
**Branch:** feature/pmdg-777

## Overview

Add a Radio panel for COM1/COM2 frequency management and a squawk code text entry to the existing TCAS panel. Uses standard SimConnect SimVars and events (not PMDG SDK) for frequency and transponder code setting, bypassing the non-functional PMDG rotary knob events.

## Radio Panel

New panel under Pedestal section: **"Radio"**

### COM1 Controls

- **COM1_ActiveFreq** — read-only display of COM1 active frequency. SimVar `COM_ACTIVE_FREQUENCY:1` (MHz). `PreventTextInput = true`, `UpdateFrequency.Continuous`, `IsAnnounced = true`. Announced as "COM1 active {freq}".
- **COM1_StandbyFreq** — text entry for COM1 standby frequency. SimVar `COM_STANDBY_FREQUENCY:1` for reading. Set via SimConnect event `COM_STBY_RADIO_SET` with BCD16-encoded parameter. `UpdateFrequency.Continuous`, `IsAnnounced = true`. Announced as "COM1 standby {freq}".
- **COM1_Swap** — momentary button. SimConnect event `COM_STBY_RADIO_SWAP`. Swaps active and standby frequencies.

### COM2 Controls

- **COM2_ActiveFreq** — read-only display. SimVar `COM_ACTIVE_FREQUENCY:2`. `PreventTextInput = true`, `UpdateFrequency.Continuous`, `IsAnnounced = true`. Announced as "COM2 active {freq}".
- **COM2_StandbyFreq** — text entry. SimVar `COM_STANDBY_FREQUENCY:2` for reading. Set via SimConnect event `COM2_STBY_RADIO_SET` with BCD16-encoded parameter. `UpdateFrequency.Continuous`, `IsAnnounced = true`. Announced as "COM2 standby {freq}".
- **COM2_Swap** — momentary button. SimConnect event `COM2_STBY_RADIO_SWAP`. Swaps active and standby frequencies.

### Variable Type

All radio variables use `SimVarType.SimVar` (standard SimConnect), NOT `SimVarType.PMDGVar`. The existing architecture already supports SimVar types from the FlyByWire A320 implementation.

### Frequency Validation

- Range: 118.000 - 136.975 MHz
- Format: 3 decimal places (e.g., 121.500)
- Invalid entries: rejected with error announcement to screen reader

## TCAS Panel Enhancement

Add one control to the existing **Transponder/TCAS** panel:

- **XPDR_SquawkCode** — text entry for 4-digit squawk code. SimVar `TRANSPONDER_CODE:1` (BCO16) for reading. Set via SimConnect event `XPNDR_SET` with BCD16-encoded parameter. `UpdateFrequency.Continuous`, `IsAnnounced = true`. Announced as "Squawk {code}".

### Squawk Code Validation

- Format: exactly 4 digits
- Each digit: 0-7 only (octal)
- Invalid entries: rejected with error announcement

## BCD16 Encoding

Both frequencies and squawk codes use BCD16 encoding for SimConnect events:
- Each digit occupies one nibble (4 bits)
- Frequency 121.50 -> strip decimal -> 12150 -> BCD: 0x12150 (but BCD16 uses last 4 significant digits: 2150 -> 0x2150 = 8528 decimal)
- Squawk 1234 -> BCD: 0x1234 = 4660 decimal

Implementation needs:
- `FrequencyToBCD16(string freq)` — converts "121.500" to BCD16 int for SimConnect event
- `BCD16ToFrequency(double bcd)` — converts SimVar BCO16 value to display string
- `SquawkToBCD16(string code)` — converts "1234" to BCD16 int
- `BCD16ToSquawk(double bcd)` — converts SimVar BCO16 value to display string

## Announcements (ProcessSimVarUpdate)

Custom handlers for human-friendly formatting:
- COM1/COM2 active: "COM1 active 121 point 5"
- COM1/COM2 standby: "COM1 standby 118.000"
- Squawk: "Squawk 7000"

## Panel Structure

```
["Pedestal"] = {
    ...,
    "Radio",              // NEW
    "Transponder/TCAS",   // existing (enhanced with squawk entry)
    ...
}
```

## HandleUIVariableSet

Radio frequency set and squawk set need custom handling in HandleUIVariableSet:
- Intercept COM1/COM2 standby frequency text entry: validate, BCD16 encode, send via SimConnect `trigger_event` (NOT PMDG CDA)
- Intercept squawk code text entry: validate, BCD16 encode, send via `XPNDR_SET`
- COM1/COM2 swap buttons: send via SimConnect event

These use `simConnect.SendEvent()` (standard SimConnect TransmitClientEvent), not `SendPMDGEvent()` (PMDG CDA).

## Deferred

- COM3 radio (rarely used)
- NAV1/NAV2 frequencies (managed via CDU)
- ACP (Audio Control Panel) mic/receiver selection
