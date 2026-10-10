---
paths:
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
---
# Rules whose code MainForm's aircraft switch and dialogs hold

MIRRORS: copied word for word from the rule file each Full: link names, whose globs leave both partials out; the code is in `SwitchAircraft`, `StartA380EWDMonitor`, `RefreshDatabaseProvider` or the windows and GSX check MainForm.Dialogs.cs builds. Change both together (ClaudeContextBudgetTests checks).

- [A380C-23] Dispose every A380 form holding a Coherent client or the def in `SwitchAircraft`'s cleanup; a hide-on-close form (RMP) tears down in `Dispose(bool)`, since `Close()` is cancelled and `Form.Dispose()` skips `OnFormClosed`. Full: docs/invariants/a380-coherent.md#a380c-23
- [A380C-24] Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown), or seat/slider motor timers keep writing L:vars into the new aircraft. Full: docs/invariants/a380-coherent.md#a380c-24
- [A380C-4] Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists; the SD Upper-E/WD fallback goes through the one always-on monitor socket. Full: docs/invariants/a380-coherent.md#a380c-4
- [A380C-5] The ECL must share the EWD monitor's Coherent socket, never open its own: a second inspector connection to the same page is rejected. Full: docs/invariants/a380-coherent.md#a380c-5
- [MON-10] `FBWA380MonitorManagerForm.EcamMemosKey` must stay public with that exact name: `MainForm.AircraftSwitch.cs` gates the Coherent E/WD scrape and the FWS failure client on it, so one row mutes three speech sources. Full: docs/invariants/monitor-manager.md#mon-10
- [DCK-15] A DATABASE switch is invalidated by CLOSING the window (`RefreshDatabaseProvider` closes `tcasForm`), not a cache clear: `GateResolver` captures its provider at construction. Full: docs/invariants/gsx-stands.md#dck-15
- [EXIT-11] A database switch must clear the landing-exit plan AND disarm the manual landing assist (`RefreshDatabaseProvider`: `landingExitPlanner.Clear()`, `flareAssistManager.Disarm`): both hold a runway list the new database may name or place differently. Full: docs/invariants/landing-exits.md#exit-11
- [A320-39] `SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement) of `AircraftLoaded`; without it a loading flight's first published FCU values are spoken as knob turns. Full: docs/invariants/fcu-callouts.md#a320-39
- [GSX-20] "GSX available" for the `.ini` gate overlay, deice pads and profile stop positions is `GsxService.CouatlStarted` OR `SimConnectManager.GsxCouatlStartedLVar`, never the Remote flag alone; `GsxService` itself still touches SimConnect nowhere. Full: docs/invariants/gsx-remote.md#gsx-20
- [MD11-17] The `MD11MCDU` subscription never delivers the current page, so `RequestAll` keeps a start-up ONCE snapshot on a DIFFERENT request id. The window restores the cursor by ROW IDENTITY and polls only while visible; ONE `Md11McduDataManager` per connection. (more: see full) Full: docs/invariants/md11.md#md11-17
- [A320-5] The A32NX MCDU runs over Coherent (`CoherentA32nxMcduClient`) with SimBridge as fallback, picked by `FbwMcduTransportArbiter`, which never replays a remembered frame; the client claims its view (`CoherentViewOwnership`), and never add a Captain/FO side selector. (more: see full) Full: docs/invariants/a32nx-mcdu.md#a320-5
