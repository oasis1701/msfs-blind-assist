---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayShapeSource*.cs"
---
# Held-runway and runway-probe rules for the TaxiGuidanceManager partials

MIRRORS: each line is copied word for word from its area's rule file, whose globs leave these files out. Their code: `GetStatusAnnouncement` (Announcements), `_graphGeneration` stamping (Routing, Rollout); TaxiGuidanceManager.cs (the probe) has its own copies in taxi-manager-call-sites.md. Change every copy together; ClaudeContextBudgetTests fails if they differ.

- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
- [SUR-24] The runway probe (`TaxiGuidanceManager.IsOnRunwayPavement`) answers only from geometry in hand and NEVER builds a graph; its warm-up reads runway rows only (`PrepareRunwayShapeWarmUp`, never `GetTaxiPaths`); `_graphGeneration` is stamped only when a NEW `_graph` instance is installed, never when a re-route hands the same graph back. Full: docs/invariants/surroundings.md#sur-24
