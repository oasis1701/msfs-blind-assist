---
paths:
  - "MSFSBlindAssist/Navigation/Briefing/**"
  - "MSFSBlindAssist/Services/RouteBriefingText.cs"
  - "MSFSBlindAssist/Services/RouteDescriptionSession.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Briefing*.cs"
  - "MSFSBlindAssist/Services/GeminiService.cs"
  - "MSFSBlindAssist/Services/ClaudeService.cs"
---
# Route briefing rules

Loaded when Claude reads matching code. Background: docs/gemini.md. Full text of each rule: docs/invariants/route-briefing.md.

- [BRF-1] The briefing's `OsmPlanningGraph` is PLANNING-ONLY: rendered under `TaxiBriefingRenderer.OsmLabel` (or `XPlaneLabel` for apt.dat), never stored where a form or `TaxiGuidanceManager` can reach it, and given NO parking in its `TaxiGraph.Build`. (more: see full) Full: docs/invariants/route-briefing.md#brf-1
- [BRF-2] Each taxi leg is a ROUTE PARAGRAPH from the computed route only, one CHECK LINE that never claims an agreement or difference it cannot support, then optional SUGGESTIONS, never a full alternative route. (more: see full) Full: docs/invariants/route-briefing.md#brf-2
- [BRF-3] `BriefingStandPicker` finds SayIntentions' gate in the import's order: NAME, then ALIASES, the pin only when neither matched; never re-gate a name/alias match on the pin's reach, and an ordinary stand always beats an excluded one. (more: see full) Full: docs/invariants/route-briefing.md#brf-3
- [BRF-4] Place leg ends with `TaxiBriefingPlanner.LegEndNode`, never the largest component alone, and never on a node whose piece lies across a runway (`PieceLiesAcrossARunway`); route starts still pass `excludeBridgeOnlyStandStubs: true`. (more: see full) Full: docs/invariants/route-briefing.md#brf-4
- [BRF-5] `GeminiService.RealWorldTaxiQuestion` is an instruction, never output: never tell the AI to substitute data into it, keep `RouteBriefingText.RemoveEchoedTaxiQuestion` in BOTH providers, and remove only an echo that STARTS its line. Full: docs/invariants/route-briefing.md#brf-5
- [BRF-6] Use SayIntentions' data only for THIS flight (`SayIntentionsArrivalGate.IsThisFlight`): its runways win over the plan's, its gate comes via `GetAssignedStatusAsync`, every distance uses ONE unit, and turns come from `BriefingTurns`. (more: see full) Full: docs/invariants/route-briefing.md#brf-6
