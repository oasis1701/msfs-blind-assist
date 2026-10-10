---
paths:
  - "MSFSBlindAssist/SimConnect/SimConnectManager*.cs"
  - "MSFSBlindAssist/SimConnect/FreshReadPolicy.cs"
  - "MSFSBlindAssist/SimConnect/FreshReadWaiters.cs"
  - "MSFSBlindAssist/SimConnect/UiThreadGate.cs"
  - "MSFSBlindAssist/SimConnect/ContinuousBatchLayout.cs"
  - "MSFSBlindAssist/SimConnect/GenericBatch.cs"
  - "MSFSBlindAssist/SimConnect/SimVarDefinitions.cs"
  - "MSFSBlindAssist/SimConnect/DeliveryLogPolicy.cs"
  - "MSFSBlindAssist/SimConnect/CameraReadWaiters.cs"
  - "MSFSBlindAssist/SimConnect/MobiFlightWasmModule.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FreshRead*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*UiThreadGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RequestId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimConnectId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ValueChangeTolerance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DeliveryLogPolicy*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CameraReadWaiters*.cs"
---
# SimConnect data definitions and requests rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/simconnect-data.md.

- [SIM-2] `SetupDataDefinitions` must register bulk/batch vars LAST, after the fixed/critical defs (AIRCRAFT_INFO/ATC/position), so a def-count overflow degrades gracefully instead of stranding aircraft detection. Full: docs/invariants/simconnect-data.md#sim-2
- [SIM-3] Watch `registration.log`'s `approxTotalDefs`; never let a new var push a connection's total definitions near/over 1000 without splitting to a second SimConnect connection. Full: docs/invariants/simconnect-data.md#sim-3
- [SIM-4] `RequestVariable(key, forceUpdate:true)` must also work for batch-covered vars: `ProcessContinuousBatch` must consult `forceUpdateVariables`, or a forced re-read of an unchanged batch value silently no-ops. Full: docs/invariants/simconnect-data.md#sim-4
- [SIM-5] `SimConnectManager.RequestVariable` must stay safe from ANY thread: off the UI thread it POSTS itself there (`UiThreadGate`; a post refused at shutdown is dropped, never run inline). Never read `continuousVariableIndexMap` or issue `RequestDataOnSimObject` from a pool thread directly. Full: docs/invariants/simconnect-data.md#sim-5
- [SIM-6] Request ids 341–348 (camera reads), 505–508 (guidance frames) and 600–607 (ground-traffic sweeps) are reserved: never assign one elsewhere, and grep for raw `(DATA_REQUESTS)` casts before choosing a new id, because a reused id REPLACES the existing request. Pinned by `GroundTrafficRequestIdTests` and `CameraReadWaitersTests`. Full: docs/invariants/simconnect-data.md#sim-6
- [SIM-7] A delivered value is a CHANGE only past its `SimVarDefinition.ChangeTolerance` (else the shared 0.001), through the one `SimConnectManager.IsValueChange`; never widen it for a var whose reader acts on a small cumulative change, or one `Md11SeedGate` counts. Full: docs/invariants/simconnect-data.md#sim-7
- [SIM-19] A SIM_FRAME + CHANGED own subscription is seeded by a ONCE on `FreshReadPolicy.SeedRequestId` (def id + 900000, no map) only once `OnRecvSimobjectData` is attached (`SeedSimFrameSubscriptions` after `SetupEvents`, after re-registration): `SetupDataDefinitions`' `DoEvents` drains earlier answers. A seed answers no fresh-read waiter. Full: docs/invariants/simconnect-data.md#sim-19
- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/simconnect-data.md#dck-34

Mirrored from md11-controls.md, md11.md, surroundings.md and gsx-remote.md (they govern SimConnectManager code; change them there and here together):
- [MD11-8] A walked control's state var is consumed by `ProcessSimVarUpdate` during the walk, so no forced read reaches MainForm's combo refresh mid-walk; the walker reads via `ReadFreshAsync` on the read's OWN request id, never the subscription's, and SIM_FRAME subscriptions are seeded. (more: see full) Full: docs/invariants/md11-controls.md#md11-8
- [MD11-17] The `MD11MCDU` subscription never delivers the current page, so `RequestAll` keeps a start-up ONCE snapshot on a DIFFERENT request id. The window restores the cursor by ROW IDENTITY and polls only while visible; ONE `Md11McduDataManager` per connection. (more: see full) Full: docs/invariants/md11.md#md11-17
- [SUR-10] The surface-change callout (`SurfaceChangeGate`) has its own switch, is not behind `SuppressCheck`, speaks only a surface FAMILY change confirmed by `ConfirmMetres` from its first reading; other `lastKnownPosition` writers must carry the surface fields forward (more: see full). Full: docs/invariants/surroundings.md#sur-10
- [GSX-20] "GSX available" for the `.ini` gate overlay, deice pads and profile stop positions is `GsxService.CouatlStarted` OR `SimConnectManager.GsxCouatlStartedLVar`, never the Remote flag alone; `GsxService` itself still touches SimConnect nowhere. Full: docs/invariants/gsx-remote.md#gsx-20
