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

- [EXIT-9] `BeginRunwayEndCountdownRollout` must install the graph, provider and ICAO and START the tone (no LoadRoute ran); only it and `BeginLandingRolloutNoGraph` raise `PositionStreamRequired` (no other `LandingRollout` entry restarts the stream); distance to the end is `RunwayFrame.DistanceToEnd`, never raw `Runway.Length`. Full: docs/invariants/landing-exits.md#exit-9
