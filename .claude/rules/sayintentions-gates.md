---
paths:
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsGatePositionMatcher.cs"
  - "MSFSBlindAssist/Services/SayIntentions/SayIntentionsService.cs"
  - "MSFSBlindAssist/MainForm.SayIntentions.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsExternalRoute*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsGatePositionMatcher*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SayIntentionsDestinationCandidates*.cs"
---
# SayIntentions gate resolution rules

Loaded when Claude reads matching code. Background: docs/sayintentions.md. Full text of each rule: docs/invariants/sayintentions-gates.md.

- [SI-1] `assigned_gate` is always an ARRIVAL gate at `flight_destination`: never infer its role from where the aircraft stands, and use it in destination resolution (and the parked-at-stand check) only when the routed airport IS the destination. Full: docs/invariants/sayintentions-gates.md#si-1
- [SI-2] `assigned_gate` is the FULL label ("Terminal 3 Gate J1"): the stand id is what follows the LAST gate/stand keyword, and a label with no keyword is used whole; never strip noise words instead. Full: docs/invariants/sayintentions-gates.md#si-2
- [SI-3] A gate candidate resolves by name, then online ALIASES, then the published `assigned_gate_lat`/`assigned_gate_lon`, both fallbacks INSIDE `TryResolveExternalDestination`'s candidate loop, never after it (the arrival runway would win). Full: docs/invariants/sayintentions-gates.md#si-3
- [SI-4] `TryResolveExternalDestination` must neutralise EVERY gate-list filter before probing (search box, `_suppressOccupiedFilter`, `_suppressFitFilter`); latch them on a successful seat, restore them on a failed probe, never untick `chkFitFilter`. Full: docs/invariants/sayintentions-gates.md#si-4
- [SI-5] On a KNOWN ARRIVAL (routed airport is `flight_destination` and a gate is assigned), `BuildSayIntentionsDestinationCandidates` carries GATE candidates ONLY; an unresolvable gate fails loudly (`ComposeUnresolvedArrivalGateMessage`), never seats a runway. Full: docs/invariants/sayintentions-gates.md#si-5
- [SI-6] The ALIAS step compares the EXACT normalized alias with the exact normalized identifier, never `Contains` ("A2" must never seat A24); it exists because `NormalizeParkingName` cuts the combo label before the alias. Full: docs/invariants/sayintentions-gates.md#si-6
- [SI-7] The coordinate step attaches to the assigned gate ALONE, behind the `flight_destination` check: admit within radius × `NoseStopRadiusFactor` (2.0), take the NEAREST, convert `ParkingSpot.Radius` by `Source`. (more: see full) Full: docs/invariants/sayintentions-gates.md#si-7
