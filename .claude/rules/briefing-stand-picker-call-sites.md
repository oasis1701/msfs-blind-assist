---
paths:
  - "MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs"
  - "tests/MSFSBlindAssist.Tests/BriefingStandPickerTests.cs"
---
# SayIntentions gate-position rule for the briefing's stand picker and its tests

MIRRORS: copied word for word from sayintentions-gates.md, whose globs leave these files out: the picker's position match (`AcceptanceMetres`) reuses the import's `MaxMatchMetres` and `NoseStopRadiusFactor`, converts the radius by `Source` and rejects (0, 0), and its test pins it to the import's matcher. Change both together (ClaudeContextBudgetTests checks).

- [SI-7] The coordinate step attaches to the assigned gate ALONE, behind the `flight_destination` check: admit within radius × `NoseStopRadiusFactor` (2.0), take the NEAREST, convert `ParkingSpot.Radius` by `Source`. (more: see full) Full: docs/invariants/sayintentions-gates.md#si-7
