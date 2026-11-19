---
paths:
  - "MSFSBlindAssist/Navigation/RouteRunwayStart.cs"
  - "MSFSBlindAssist/Navigation/UndrivenClearanceStart.cs"
  - "MSFSBlindAssist/Navigation/TaxiLeadIn.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "tests/MSFSBlindAssist.Tests/RouteRunwayStartTests.cs"
  - "tests/MSFSBlindAssist.Tests/UndrivenClearanceStartTests.cs"
  - "tests/MSFSBlindAssist.Tests/TaxiLeadInTests.cs"
---
# Route start rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/route-runway-start.md.

- [RTE-37] A plain taxi route loaded on a runway starts on that runway's own nodes (`RouteRunwayStart`: cleared taxiway ahead, nearest ahead, nearest behind), skipping an exit whose route comes back onto the runway; never a beeline at a parallel taxiway, and never for a runway destination or a landing-exit route. Full: docs/invariants/route-runway-start.md#rte-37
- [RTE-38] A lead-in is measured to where the route MEETS the first cleared taxiway (`TaxiLeadIn.Extract`: its first segment of that name, else its first node on it), never across the whole route when the router joined that taxiway at a junction. Full: docs/invariants/route-runway-start.md#rte-38
- [RTE-39] A first cleared taxiway the route never drives and would turn straight back at (`UndrivenClearanceStart`, `TaxiLeadIn.DoublesBackAtUndrivenStart`) is left out and SAID in the start warning, from a lead-in or a runway start; never the last cleared taxiway, never one a hold-short pick is keyed to, never one the route only passes through. Full: docs/invariants/route-runway-start.md#rte-39
