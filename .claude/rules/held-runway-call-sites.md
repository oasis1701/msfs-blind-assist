---
paths:
  - "MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs"
  - "MSFSBlindAssist/Navigation/RunwayRouteClassifier.cs"
  - "MSFSBlindAssist/Navigation/RunwayShape.cs"
  - "MSFSBlindAssist/Navigation/RunwayPavement.cs"
  - "MSFSBlindAssist/Navigation/*Hold*.cs"
  - "MSFSBlindAssist/Navigation/Progressive*.cs"
  - "MSFSBlindAssist/Services/RunwayIncursionWatch.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs"
  - "MSFSBlindAssist/Database/Models/TaxiRoute.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Hold*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Incursion*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayShape*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayPavement*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayMembership*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayRowShapes*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayCenterlinePairing*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayReach*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DestinationStripCrossing*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayEventDescription*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiMathUtils*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayRouteClassifier*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteRunwayCrossings*.cs"
---
# Held-runway and runway-probe rules for the runway-hold files

MIRRORS: each line below is copied word for word from its area's rule file, because the held-runway label (`GetStatusAnnouncement`, TaxiGuidanceManager.Announcements.cs) and the runway probe (`IsOnRunwayPavement`, TaxiGuidanceManager.cs) sit in the TaxiGuidanceManager partials whose runway code runway-holds.md guards.

This file globs runway-holds.md's files except TaxiGuidanceManager.TrafficContext.cs, where ground-traffic.md loads TRF-6, and RunwayShapeSource.cs, where surroundings.md loads SUR-24, so each loads once. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
- [SUR-24] The runway probe (`TaxiGuidanceManager.IsOnRunwayPavement`) answers only from geometry in hand and NEVER builds a graph; its warm-up reads runway rows only (`PrepareRunwayShapeWarmUp`, never `GetTaxiPaths`); `_graphGeneration` is stamped only when a NEW `_graph` instance is installed, never when a re-route hands the same graph back. Full: docs/invariants/surroundings.md#sur-24
