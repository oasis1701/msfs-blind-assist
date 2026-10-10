---
paths:
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/RouteChangedCallout.cs"
  - "MSFSBlindAssist/Navigation/RouteReachability.cs"
  - "MSFSBlindAssist/Navigation/RouteReachabilityMessages.cs"
  - "MSFSBlindAssist/Navigation/RouteStartTurnCue.cs"
  - "MSFSBlindAssist/Navigation/RouteTaxiwaySequence.cs"
  - "MSFSBlindAssist/Navigation/TaxiLeadIn.cs"
  - "MSFSBlindAssist/Navigation/TaxiwayChangeGate.cs"
  - "MSFSBlindAssist/Services/StartWarningChatterGate.cs"
  - "MSFSBlindAssist/Navigation/LoadRefusalRollback.cs"
  - "MSFSBlindAssist/Navigation/ReachabilityRefusalGate.cs"
  - "MSFSBlindAssist/Navigation/RunwayReachGate.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/Services/LiveRouteStates.cs"
  - "MSFSBlindAssist/Database/Models/TaxiNode.cs"
  - "MSFSBlindAssist/Database/Models/TaxiPath.cs"
  - "MSFSBlindAssist/Navigation/PavementMap.cs"
  - "MSFSBlindAssist/Navigation/RolloutExitGate.cs"
  - "MSFSBlindAssist/Navigation/RolloutRunwayReCrossing.cs"
  - "MSFSBlindAssist/Navigation/RunwayVacateResolver.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiGraph*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiRouter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteReachability*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteChangedCallout*.cs"
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
  - "tests/MSFSBlindAssist.Tests/**/*RolloutRunwayReCrossing*.cs"
---
# Runway-hold rules for the routing and rollout files

MIRRORS: copied word for word from runway-holds.md, whose globs leave these files out: TaxiGraph.cs reads the runway geometry they name, the rollout files read `RunwayShape`, and HLD-1, naming no code, keeps loading on every taxi-routing.md file. Change a rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [HLD-1] Hold-short-to-runway association must be by nearest runway CENTERLINE, never threshold distance, which mislabels crossings far from either threshold with the taxiway name instead of the runway. Full: docs/invariants/runway-holds.md#hld-1
- [HLD-7] Read runway geometry through the ONE `RunwayShape.For` (classifier, holds, Where-Am-I, takeoff, vacate, reach walk), bar `MatchHoldShortRunwayName` (start rows) and four callers with their own geometry; never read `Pavement*` directly or test membership on `Lat1..Lon2` alone; repair an outboard start ROW, never cap the extent (more: see full). Full: docs/invariants/runway-holds.md#hld-7
- [HLD-8] Never tune `NamedHoldingPointResolver`'s snap radii: don't widen `DESIGNATED_SNAP_M` (15 m) toward `MAX_SNAP_M` (30 m), don't require a designated node for runway/ILS kinds, and don't add a "never snap runway-ward" guard; all three were probed and are worse. Full: docs/invariants/runway-holds.md#hld-8
