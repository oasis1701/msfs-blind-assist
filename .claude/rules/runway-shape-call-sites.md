---
paths:
  - "MSFSBlindAssist/Navigation/RunwayFrame.cs"
  - "MSFSBlindAssist/Services/GroundTrafficLogic.Runway.cs"
---
# Runway-shape rule for the runway frame and the runway traffic classifier

MIRRORS: copied word for word from runway-holds.md, whose globs leave these files out: `GroundTrafficLogic.ClassifyAgainstRunway` tests membership and finds the landing threshold through `RunwayShape`, and `RunwayFrame` is the own geometry `FindFarSideRunwayNode` and `GetTaxiwaysCrossingRunway` keep. Change both together (ClaudeContextBudgetTests checks).

- [HLD-7] Read runway geometry through the ONE `RunwayShape.For` (classifier, holds, Where-Am-I, takeoff, vacate, reach walk), bar `MatchHoldShortRunwayName` (start rows) and four callers with their own geometry; never read `Pavement*` directly or test membership on `Lat1..Lon2` alone; repair an outboard start ROW, never cap the extent (more: see full). Full: docs/invariants/runway-holds.md#hld-7
