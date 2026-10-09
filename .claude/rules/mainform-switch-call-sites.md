---
paths:
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
---
# Rules whose code MainForm's aircraft switch holds

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards lives in `SwitchAircraft` or `RefreshDatabaseProvider`, which only this MainForm partial holds and that area's globs leave out. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [A380C-23] Dispose every A380 form holding a Coherent client or the def in `SwitchAircraft`'s cleanup; a hide-on-close form (RMP) tears down in `Dispose(bool)`, since `Close()` is cancelled and `Form.Dispose()` skips `OnFormClosed`. Full: docs/invariants/a380-coherent.md#a380c-23
- [A380C-24] Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown), or seat/slider motor timers keep writing L:vars into the new aircraft. Full: docs/invariants/a380-coherent.md#a380c-24
- [DCK-15] A DATABASE switch is invalidated by CLOSING the window (`RefreshDatabaseProvider` closes `tcasForm`), not a cache clear: `GateResolver` captures its provider at construction. Full: docs/invariants/gsx-stands-docking.md#dck-15
- [EXIT-11] A database switch must clear the landing-exit plan AND disarm the manual landing assist (`RefreshDatabaseProvider`: `landingExitPlanner.Clear()`, `flareAssistManager.Disarm`): both hold a runway list the new database may name or place differently. Full: docs/invariants/landing-exits.md#exit-11
- [SUR-15] A database switch (`RefreshDatabaseProvider`) clears `surroundingsCache`, `onlineFeatures`, `ClearWhereAmICache()` and `groundTrafficMonitor.ClearRunwayCache()` and calls `surroundingsMonitor.Reset()`, which also runs on reconnect, aircraft switch and turnaround liftoff: no staleness token moves on a switch. Full: docs/invariants/surroundings.md#sur-15
