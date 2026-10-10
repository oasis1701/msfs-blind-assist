---
paths:
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsClearanceParser.cs"
  - "MSFSBlindAssist/Navigation/Briefing/BriefingStandPicker.cs"
---
# SayIntentions gate rules for the clearance parser and the briefing's stand picker

MIRRORS: copied word for word from sayintentions-gates.md, whose globs leave these files out. Their code: `NormalizeParkingName` (the parser), which cuts a gate label at its last keyword and at the combo's spaced dash, and the picker's alias and position match (`AcceptanceMetres`). Change a rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [SI-2] `assigned_gate` is the FULL label ("Terminal 3 Gate J1"): the stand id is what follows the LAST gate/stand keyword, and a label with no keyword is used whole; never strip noise words instead. Full: docs/invariants/sayintentions-gates.md#si-2
- [SI-6] The ALIAS step compares the EXACT normalized alias with the exact normalized identifier, never `Contains` ("A2" must never seat A24); it exists because `NormalizeParkingName` cuts the combo label before the alias. Full: docs/invariants/sayintentions-gates.md#si-6
- [SI-7] The coordinate step attaches to the assigned gate ALONE, behind the `flight_destination` check: admit within radius × `NoseStopRadiusFactor` (2.0), take the NEAREST, convert `ParkingSpot.Radius` by `Source`. (more: see full) Full: docs/invariants/sayintentions-gates.md#si-7
