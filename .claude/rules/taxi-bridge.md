---
paths:
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/RouteTaxiwaySequence.cs"
  - "tests/MSFSBlindAssist.Tests/TaxiRouterBridgeJoinTests.cs"
---
# Taxi router: bridges between cleared taxiways that never meet

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-bridge.md.

- [RTE-42] A bridge between cleared taxiways sharing no node joins the next where its leg first meets it (`JoinWhereTheBridgeFirstReaches`, only if `AStarSearchStrict` leads on), never goes out and back or loops at a seam (`AppendBridgeLeg`), and yields to the route as scored where it needs neither (`KeepAsScored`); a named link is said (`LinkClause`). Full: docs/invariants/taxi-bridge.md#rte-42
