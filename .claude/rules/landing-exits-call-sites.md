---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
---
# Landing-exit rules for the taxi entry points

MIRRORS: copied word for word from landing-exits.md, whose globs leave these files out; change both together (ClaudeContextBudgetTests checks).

- [EXIT-9] `BeginRunwayEndCountdownRollout` must install the graph, provider and ICAO and START the tone (no LoadRoute ran); of rollout entries only it and `BeginLandingRolloutNoGraph` raise `PositionStreamRequired` (clearing-phase entries too, after `SetState`); distance to the end is `RunwayFrame.DistanceToEnd`, never raw `Runway.Length`. Full: docs/invariants/landing-exits.md#exit-9
- [EXIT-18] A landing-exit route starts on the exit's OWN SIDE of the landed runway (`ExitOwnSide`, via `LoadRoute`'s anchor and its backwards-start retry, `ExitOwnSideFilter`); the re-crossing guard is a backstop, not the fix; with no qualifying node the unfiltered pick stands. Full: docs/invariants/landing-exits.md#exit-18
