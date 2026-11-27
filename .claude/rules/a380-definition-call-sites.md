---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Displays.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.SimVarUpdate.cs"
---
# Rules whose code the A380 definition holds

MIRRORS: copied word for word from a380-coherent.md, takeoff-and-callouts.md and first-officer-airbus.md, whose globs leave these partials out (the EWD socket, `StockComTuningRefusal`, the take-off calls, the weight-unit state, the CONF 3 registration); change both together (ClaudeContextBudgetTests checks).

- [A380C-4] Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists; the SD Upper-E/WD fallback goes through the one always-on monitor socket. Full: docs/invariants/a380-coherent.md#a380c-4
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
- [TKO-5] Take-off calls are ONE `AnnounceImmediate` per sample via `TakeoffVSpeedCallouts.Compose`, never one per call; every definition drops the ARM, never the speeds, on reconnect AND `OnSimContextReset`, and names keys through the one `TakeoffCalloutKeys` (more: see full). Full: docs/invariants/takeoff-and-callouts.md#tko-5
- [A380C-17] Never write `SetStoredData` for metric weight and expect it to propagate; only the real EFB "US Units" toggle changes the aircraft. MSFSBA's Units button is a local read-out preference, kept separate from the aircraft's value. Full: docs/invariants/a380-coherent.md#a380c-17
- [FOA-6] The CONF 3 landing input is per airframe and FBW build (FOA-12), each `OnRequest`, polled by its FO evaluator: A32NX `A32NX_FM_LANDING_CONF3` (FS2024, FBW #10890), A380 `A380X_FM_LANDING_CONF3` (FS2024), FS2020 FBW and Headwind A330 `A32NX_SPEEDS_LANDING_CONF3`; never `Continuous` without `IsAnnounced` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-6

Mirrored from visual-guidance.md (it governs this definition's `GetVisualGuidanceProfile`; change it there and here together):
- [VG-20] The A380X and HS787 must NOT inherit the A320 baseline VG profile: both carry 777-class overrides, and their biases are type ESTIMATES pending in-sim coupled-ILS calibration. Full: docs/invariants/visual-guidance.md#vg-20
