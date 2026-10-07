---
paths:
  - "MSFSBlindAssist/SimConnect/*.cs"
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.PanelBuilder.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/Utils/ReadoutFormat.cs"
  - "MSFSBlindAssist/Utils/PanelRowRules.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CalcPath*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FreshRead*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RequestId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BridgeProbe*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimConnectId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimConnectPureLogic*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OwnAircraftFilter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DeliveryLogPolicy*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ValueChangeTolerance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*WakeableDelay*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ReadoutFormat*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PanelRowRules*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*EwdMessageLookup*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ControlStateHook*.cs"
---
# Core SimConnect and MainForm rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/core-simconnect.md.

- [SIM-1] The SimConnect data-definition budget is 1000 per connection; never register a var as BOTH an individual def AND a batch-covered def — a Continuous+IsAnnounced var skips its individual registration and reads the shared batch cache. Full: docs/invariants/core-simconnect.md#sim-1
- [SIM-2] `SetupDataDefinitions` must register bulk/batch vars LAST, after the fixed/critical defs (AIRCRAFT_INFO/ATC/position), so a def-count overflow degrades gracefully instead of stranding aircraft detection. Full: docs/invariants/core-simconnect.md#sim-2
- [SIM-3] Watch `registration.log`'s `approxTotalDefs`; never let a new var push a connection's total definitions near/over 1000 without splitting to a second SimConnect connection. Full: docs/invariants/core-simconnect.md#sim-3
- [SIM-4] `RequestVariable(key, forceUpdate:true)` must also work for batch-covered vars: `ProcessContinuousBatch` must consult `forceUpdateVariables`, or a forced re-read of an unchanged batch value silently no-ops. Full: docs/invariants/core-simconnect.md#sim-4
- [SIM-5] `SimConnectManager.RequestVariable` must stay safe from ANY thread: off the UI thread it POSTS itself there (`UiThreadGate`; a post refused at shutdown is dropped, never run inline). Never read `continuousVariableIndexMap` or issue `RequestDataOnSimObject` from a pool thread directly. Full: docs/invariants/core-simconnect.md#sim-5
- [SIM-6] Request ids 505–508 (guidance frames) and 600–607 (ground-traffic sweeps) are reserved: never assign one elsewhere, and grep for raw `(DATA_REQUESTS)` casts before choosing a new id, because a reused id REPLACES the existing request. Pinned by `GroundTrafficRequestIdTests`. Full: docs/invariants/core-simconnect.md#sim-6
- [SIM-7] A delivered value is a CHANGE only past its `SimVarDefinition.ChangeTolerance` (else the shared 0.001), through the one `SimConnectManager.IsValueChange`; never widen it for a var whose reader acts on a small cumulative change, or one `Md11SeedGate` counts. Full: docs/invariants/core-simconnect.md#sim-7
- [SIM-8] The calc-path probe MUST report its verdict (`CalcPathVerdict.LogLine` on both outcomes, `PilotWarning` spoken when UNVERIFIED); MainForm's timer gates on the aircraft registering `MSFSBA_BRIDGE_PROBE`, never on a type list. Never make the verdict silent again. Full: docs/invariants/core-simconnect.md#sim-8
- [SIM-9] The `MSFSBA_BRIDGE_PROBE` read-back lags its write by one round, so the match must accept the PREVIOUS nonce too (`SimConnect.BridgeProbe.IsEcho`); comparing only the current nonce means the probe never converges and `CalcPathVerified` stays false. Full: docs/invariants/core-simconnect.md#sim-9
- [SIM-10] A380 FCU events (`A32NX.FCU_*`) must NEVER wait on the calc-path probe (`SimConnectManager.IsFbwFcuEvent`), and that bypass must stay gated on `AircraftCode == "FBW_A380"`: the A32NX shares the names but keeps its working `TransmitClientEvent` fallback. Full: docs/invariants/core-simconnect.md#sim-10
- [SIM-11] `SetLVar`'s MobiFlight calc-path routing must gate on `CalcPathVerified`, never on bare `IsMobiFlightConnected`, which is true even with no WASM module installed. Full: docs/invariants/core-simconnect.md#sim-11
- [SIM-12] A name containing a space or colon (e.g. `TRANSPONDER STATE:1`) is a stock SimVar shape and must stay on the data-def write path — never route it through the L:var calc path. Full: docs/invariants/core-simconnect.md#sim-12
- [SIM-13] H: events always go to the MobiFlight channel whenever `IsMobiFlightConnected`; dotted events must wait for `CalcPathVerified` (queued, bounded, flushed on verify or probe-conclude) — never fire one before the probe concludes. Full: docs/invariants/core-simconnect.md#sim-13
- [SIM-14] The status-display auto-refresh repaint must be a LEADING-edge one-shot coalesce, never a restart-per-push trailing debounce, which starves under high-frequency PFD/ISIS streams. Full: docs/invariants/core-simconnect.md#sim-14
- [SIM-15] `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first; cached `displayValues` alone go stale for any def whose `ProcessSimVarUpdate` returns `true`. Full: docs/invariants/core-simconnect.md#sim-15
- [SIM-16] The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary every call. Full: docs/invariants/core-simconnect.md#sim-16
- [SIM-17] Every calc-path event string must be unique per call (`SimConnectManager.BuildCalcEventCode` prefixes `{seq} 0 *`): MobiFlight drops byte-identical consecutive commands, so a toggle could switch on and never off. The dedup keys on text, not elapsed time. Full: docs/invariants/core-simconnect.md#sim-17
- [SIM-18] The "Frame" system event is subscribed only while a consumer holds `StartFrameRateMonitoring` (the Sim Performance window), never unconditionally in `SetupEvents`: it costs one `ReceiveMessage` dispatch per rendered frame on the UI thread, app-wide, for a window nobody has open. Full: docs/invariants/core-simconnect.md#sim-18

Mirrored from md11.md (they govern SimConnectManager code; change them there and here together):
- [MD11-8] A walked control's state var is consumed by `ProcessSimVarUpdate` during the walk, so no forced read reaches MainForm's combo refresh mid-walk; the walker reads via `ReadFreshAsync` on the read's OWN request id, never the subscription's, and SIM_FRAME subscriptions are seeded. (more: see full) Full: docs/invariants/md11.md#md11-8
- [MD11-16] Every MD-11 write path refuses ALOUD, before writing, when it cannot reach the aircraft: ask `CalcWriteCanLand` (calc bus) or `CanSendEvent` (stock event), never bare `CanExecuteCalculatorCode`. Re-arm a NEGATIVE probe verdict on aircraft switch, keep a VERIFIED one. Success stays silent. (more: see full) Full: docs/invariants/md11.md#md11-16
- [MD11-17] The `MD11MCDU` subscription never delivers the current page, so `RequestAll` keeps a start-up ONCE snapshot on a DIFFERENT request id. The window restores the cursor by ROW IDENTITY and polls only while visible; ONE `Md11McduDataManager` per connection. (more: see full) Full: docs/invariants/md11.md#md11-17

Mirrored from gsx-stands-docking.md, surroundings.md and gsx-remote.md (they govern SimConnectManager code; change them there and here together):
- [DCK-34] SimConnect `PLANE_HEADING_DEGREES_TRUE`/`_MAGNETIC` are RADIANS despite the name: multiply by 57.2958 before using them as degrees. Full: docs/invariants/gsx-stands-docking.md#dck-34
- [SUR-10] The surface-change callout (`SurfaceChangeGate`) has its own switch, is not behind `SuppressCheck`, speaks only a surface FAMILY change confirmed by `ConfirmMetres` from its first reading; other `lastKnownPosition` writers must carry the surface fields forward (more: see full). Full: docs/invariants/surroundings.md#sur-10
- [GSX-20] "GSX available" for the `.ini` gate overlay, deice pads and profile stop positions is `GsxService.CouatlStarted` OR `SimConnectManager.GsxCouatlStartedLVar`, never the Remote flag alone; `GsxService` itself still touches SimConnect nowhere. Full: docs/invariants/gsx-remote.md#gsx-20
