---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs"
---
# Taxi bridge rule for the route summary

MIRRORS: copied word for word from taxi-bridge.md, whose globs leave this file out. Its code: `BuildRouteSummary` says the `LinkClause` sentence. Change both together; ClaudeContextBudgetTests fails if they differ.

- [RTE-42] A bridge between cleared taxiways sharing no node joins the next where its leg first meets it (`JoinWhereTheBridgeFirstReaches`, only if `AStarSearchStrict` leads on), never goes out and back or loops at a seam (`AppendBridgeLeg`), and yields to the route as scored where it needs neither (`KeepAsScored`); a named link is said (`LinkClause`). Full: docs/invariants/taxi-bridge.md#rte-42
