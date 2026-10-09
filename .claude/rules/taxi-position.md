---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/Route*.cs"
  - "MSFSBlindAssist/Navigation/TaxiLeadIn.cs"
  - "MSFSBlindAssist/Navigation/TaxiwayChangeGate.cs"
  - "MSFSBlindAssist/Services/StartWarningChatterGate.cs"
  - "MSFSBlindAssist/Navigation/LoadRefusalRollback.cs"
  - "MSFSBlindAssist/Navigation/ReachabilityRefusalGate.cs"
  - "MSFSBlindAssist/Navigation/RunwayReachGate.cs"
  - "MSFSBlindAssist/Services/LiveRouteStates.cs"
  - "MSFSBlindAssist/Database/Models/TaxiNode.cs"
  - "MSFSBlindAssist/Database/Models/TaxiPath.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiGraph*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiRouter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteReachability*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteChangedCallout*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteRunwayCrossings*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteStartTurnCue*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteTaxiwaySequence*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LiveRouteStates*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LoadRefusalRollback*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ReachabilityRefusalGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*StartWarningChatterGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayChangeGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiLeadIn*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayEntryNode*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OrphanParkingIsland*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayReachGate*.cs"
---
# Taxi position: off-route, start grace, Where Am I and the runway probe rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-position.md.

- [RTE-4] Take-off assist's under-aircraft runway probe (`TryDetectRunwayUnderAircraft` -> `TaxiGraph.TryGetRunwayAtPosition`) must use a strict tolerance, `RunwayShape`'s own half-width with no +5m fudge, and stay gated on `_lastOnGround`; Where-Am-I's `DescribeLocation` keeps its +5 m on purpose. Full: docs/invariants/taxi-position.md#rte-4
- [RTE-6] Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons, which break on long segments. Full: docs/invariants/taxi-position.md#rte-6
- [RTE-7] Off-route auto-recalc must stay gated on the route-joined latch `_hasJoinedRoute`, or the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance. Full: docs/invariants/taxi-position.md#rte-7
- [RTE-11] "Where Am I" is ground-only by design (gated on `_lastOnGround`); runway detection must use `TaxiGraph.RunwayCenterlines`, never rely on `taxi_path.type='R'` edges (the DB has none). Full: docs/invariants/taxi-position.md#rte-11
- [RTE-12] `TaxiGraph.DescribeLocation` runs on pool threads: pool-reachable queries take `_structureLock` inside TaxiGraph, its lazy index is read only under it, no post-Build mutation skips it; never widen the runway-start reach without the owner, nor drop the stand's near-runway gate (pavement, runway start, `IsNavdataHoldShort`) (more: see full). Full: docs/invariants/taxi-position.md#rte-12
- [RTE-27] The start grace window (`START_WARNING_CHATTER_GRACE_SEC`, 12.5 s) follows either start warning: turn, destination-ahead and curve callouts wait it out when safe (`StartWarningChatterGate`), the taxiway-change callout defers (`TaxiwayChangeGate`); never make any of them skip, never gate hold-short or runway-crossing callouts on it. Full: docs/invariants/taxi-position.md#rte-27
