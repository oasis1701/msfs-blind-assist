---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Displays.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.SimVarUpdate.cs"
---
# Rules whose code the A380 definition holds

MIRRORS: copied word for word from a380-coherent.md and takeoff-and-callouts.md, whose globs leave these partials out (the EWD socket, `StockComTuningRefusal`, the take-off calls, the weight-unit state); change both together (ClaudeContextBudgetTests checks).

- [A380C-4] Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists; the SD Upper-E/WD fallback goes through the one always-on monitor socket. Full: docs/invariants/a380-coherent.md#a380c-4
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
- [TKO-5] Take-off calls are ONE `AnnounceImmediate` per sample via `TakeoffVSpeedCallouts.Compose`, never one per call; every definition drops the ARM, never the speeds, on reconnect AND `OnSimContextReset`, and names keys through the one `TakeoffCalloutKeys` (more: see full). Full: docs/invariants/takeoff-and-callouts.md#tko-5
- [A380C-17] Never write `SetStoredData` for metric weight and expect it to propagate; only the real EFB "US Units" toggle changes the aircraft. MSFSBA's Units button is a local read-out preference, kept separate from the aircraft's value. Full: docs/invariants/a380-coherent.md#a380c-17
