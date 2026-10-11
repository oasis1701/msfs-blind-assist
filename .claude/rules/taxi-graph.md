---
paths:
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Database/Models/TaxiNode.cs"
  - "MSFSBlindAssist/Database/Models/TaxiPath.cs"
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/RouteReachability.cs"
  - "MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs"
  - "MSFSBlindAssist/Navigation/RouteTaxiwaySequence.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs"
  - "MSFSBlindAssist/Navigation/ExitBranch.cs"
  - "MSFSBlindAssist/Navigation/LandingExitDestination.cs"
  - "MSFSBlindAssist/Navigation/PavementMap.cs"
  - "MSFSBlindAssist/Navigation/RunwayVacateResolver.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiGraph*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiRouter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteReachability*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OrphanParkingIsland*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayEntryNode*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitApronNode*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayVacateResolver*.cs"
---
# Taxi graph: stand bridges and named edges rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-graph.md.

- [RTE-2] Bridge a stranded stand (`BridgeOrphanParkingIslands`) ONLY when its island is all navdata `P` lead-ins within 50 m: never on/across runway pavement, onto a hold-short, a stand or another lead-in chain, never an island carrying a taxiway; runway-exit logic skips `IsStandBridge` edges (more: see full). Full: docs/invariants/taxi-graph.md#rte-2

Mirrored from sayintentions-import.md (it governs `TaxiGraph.GetNamedEdges`; change it there and here together):
- [SI-20] The snapper takes an already-built `TaxiGraph` (`GetNamedEdges()`) and never fetches names itself; `GetNamedEdges` must stay sorted on an INTRINSIC key (name + endpoint coordinates), never node id. Full: docs/invariants/sayintentions-import.md#si-20
