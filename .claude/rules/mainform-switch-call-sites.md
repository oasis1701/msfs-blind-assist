---
paths:
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
---
# Rules whose code MainForm's aircraft switch and dialogs hold

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards lives in `SwitchAircraft` or `RefreshDatabaseProvider` (MainForm.AircraftSwitch.cs), or in the windows they tear down, which MainForm.Dialogs.cs builds; that area's globs leave both partials out. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [A380C-23] Dispose every A380 form holding a Coherent client or the def in `SwitchAircraft`'s cleanup; a hide-on-close form (RMP) tears down in `Dispose(bool)`, since `Close()` is cancelled and `Form.Dispose()` skips `OnFormClosed`. Full: docs/invariants/a380-coherent.md#a380c-23
- [A380C-24] Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown), or seat/slider motor timers keep writing L:vars into the new aircraft. Full: docs/invariants/a380-coherent.md#a380c-24
- [DCK-15] A DATABASE switch is invalidated by CLOSING the window (`RefreshDatabaseProvider` closes `tcasForm`), not a cache clear: `GateResolver` captures its provider at construction. Full: docs/invariants/gsx-stands.md#dck-15
- [EXIT-11] A database switch must clear the landing-exit plan AND disarm the manual landing assist (`RefreshDatabaseProvider`: `landingExitPlanner.Clear()`, `flareAssistManager.Disarm`): both hold a runway list the new database may name or place differently. Full: docs/invariants/landing-exits.md#exit-11
- [A320-39] `SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement) of `AircraftLoaded`; without it a loading flight's first published FCU values are spoken as knob turns. Full: docs/invariants/a32nx-fenix.md#a320-39
