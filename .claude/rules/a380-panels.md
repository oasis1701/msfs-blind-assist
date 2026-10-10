---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Displays.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.HotkeysAndMotion.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.PanelControls.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.SimVarUpdate.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.UiVariableSet.cs"
  - "MSFSBlindAssist/Aircraft/A380*.cs"
  - "MSFSBlindAssist/Aircraft/WiperPosition.cs"
  - "MSFSBlindAssist/Forms/FBWA380/**"
  - "tests/MSFSBlindAssist.Tests/**/*A380*.cs"
---
# FlyByWire A380X panel controls and read-outs rules

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/a380-panels.md.

- [A380-2] Every A380 panel control renders as a COMBO, never a hardware button, except true one-shot momentary actions (ECAM-CP keys, chrono, calls) and the seat-motor `RenderAsButton` toggles [A380-10] prescribes; a control showing ongoing state is never a plain button. Full: docs/invariants/a380-panels.md#a380-2
- [A380-6] FBW buses and batteries publish as `A32NX_ELEC_{rawBusName}_BUS_IS_POWERED` with the raw bus id; never invent a descriptive name, confirm the id in the Rust source. Full: docs/invariants/a380-panels.md#a380-6
- [A380-10] Never periodically re-read and snap a combo whose synthetic motor var idles at 0 (it loops restart/stop/announce); use a `RenderAsButton` toggle so state changes only on the click edge. Full: docs/invariants/a380-panels.md#a380-10
- [A380-12] Never regress the ROW/ROP and BTV rollout call-outs (safety call-outs, verifiable only live); decode `A32NX_ROW_ROP_WORD_1` with the current `RowRopWord1Bits` map (11 max braking, 12/13 reverse, 14/15 too short), as the PFD does. Full: docs/invariants/a380-panels.md#a380-12
- [A380-13] ECP keys (`L:A32NX_BTN_*`) are held `A380EcpKeyPulse.HoldMs` (250 ms) and released `ReleaseMs` (250 ms) between presses, on the one shared `A380EcpKeyPulse.Shared` clock reserving a slot at press start; never shorten them or guard per caller. Full: docs/invariants/a380-panels.md#a380-13
- [A380-16] A readout-only var (a `TryGetDisplayOverride` decoder with no hotkey, window or auto-announce) must be in a panel display list, or it is never requested and its decoder never runs. Full: docs/invariants/a380-panels.md#a380-16
- [A380-21] When a status has both a PB-light L:var and an ECAM memo, make the L:var `ReadEnumQuiet` and the memo the single call-out; never announce one condition from two sources. Full: docs/invariants/a380-panels.md#a380-21
- [A380-22] Master Warning/Caution acknowledge must pulse the exact glareshield L:var name, misspelled `MASTERAWARN` on the A380; the plausible spelling is a silent no-op that never clears the aural. Full: docs/invariants/a380-panels.md#a380-22
- [A380-23] A380 overhead PBs (packs, bleeds, fans, hyd pumps, bus-tie, galley, PTU, exit sign) are settable via the calculator path; `XMLVAR_` sign combos use the OVHD calc catch-all. Test FBW L:var writes via the calculator path, never `set_lvar`. Full: docs/invariants/a380-panels.md#a380-23
- [A380-24] Multi-position switches stay ONE combo, never On/Off pairs: Nose light T.O./Taxi/Off (actuated by indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET`; `LIGHTING_LANDING_1` only mirrors it), Seat Belts ON/AUTO/OFF (always write `XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position`, AUTO's input; ON/OFF add `CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE`). Full: docs/invariants/a380-panels.md#a380-24
- [A380-25] A380 wing anti-ice is the stock `K:TOGGLE_STRUCTURAL_DEICE` / `A:STRUCTURAL DEICE SWITCH`, never the A32NX's L:var (a dead mirror), sent by `SendEvent` via `A380ToggleCommand.ShouldFire`. Never infer one FBW airframe's wiring from the other's. Full: docs/invariants/a380-panels.md#a380-25
- [A380-26] SD-page row registration classifies by FBW prefix, not space/colon: a colon-indexed FBW L:var (`A32NX_FUEL_USED:n`) is an L:var; only names without an FBW prefix register as stock SimVars. Full: docs/invariants/a380-panels.md#a380-26
- [A380-27] Frequency readouts need explicit formatting: stock `COM ACTIVE/STANDBY FREQUENCY:n` needs a "0.000 MHz" display override, and ND ADF/VOR need kHz/MHz unit labels. Full: docs/invariants/a380-panels.md#a380-27
- [A380-28] "Passengers on Board" sums the per-station `A32NX_PAX_<st>_DESIRED` bitmasks, never the boarded `A32NX_PAX_<st>` set, which lags and under GSX boarding settles below target. Full: docs/invariants/a380-panels.md#a380-28
- [A380-29] Wipers are a synthetic 3-position OFF/SLOW/FAST combo per side (circuits 141/143): read BOTH `CIRCUIT SWITCH ON` and `CIRCUIT POWER SETTING` (75/100%), switch first, since power stays 100% while off. Full: docs/invariants/a380-panels.md#a380-29
