---
paths:
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/Navigation/RunwayEntryDirection.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DepartureLineupPoint*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FullLengthEntrance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayEntryDirection*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AngledRunwayEntry*.cs"
---
# Departure runway entry and lineup point rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md, docs/virtual-pilot.md. Full text of each rule: docs/invariants/runway-entry.md.

- [ENT-1] The departure lineup point comes from ONE place, `TaxiGraph.DepartureLineupPoint`, never a re-inlined pick/snap pair; a point > 10 m behind the pavement edge is kept only on a starter extension, else moves forward to the edge, never further. Full: docs/invariants/runway-entry.md#ent-1
- [ENT-2] An intersection's listed distance is quoted from where the taxiway meets the pavement on the AIRCRAFT's side (`PavementEntryAlongLeft/Right`), only ever further down, within 300 m, never without a position; `FindFullLengthEntrance` keeps the scan untouched. Full: docs/invariants/runway-entry.md#ent-2
- [ENT-3] The departure-entry holding-point picker skips DESCRIPTIVE OSM hold labels (`TaxiGraph.IsDescriptiveHoldLabel`); Progressive Taxi's hold-at list is not filtered; a holding-point pin that cannot be made is SPOKEN, never only logged. Full: docs/invariants/runway-entry.md#ent-3
- [ENT-4] A full-length route arriving facing the reciprocal is re-routed to a forward-facing entry (`TryForwardFacingRunwayEntry`, `RunwayEntryDirection`), never for an intersection, holding point, backtrack or cross-component route; pilot picks run AFTER it and the pin. Full: docs/invariants/runway-entry.md#ent-4
- [ENT-5] With no painted holding-point names, the full-length call-out names the navdata entrance nearest the LINEUP POINT (`TaxiGraph.FindFullLengthEntrance`), 150 m ahead / 400 m behind, named taxiways only; nothing in that window is silence. Full: docs/invariants/runway-entry.md#ent-5
- [ENT-6] "(backtrack required)" is part of the runway combo ITEM LABEL (`TaxiGraph.RunwayNeedsBacktrack` + `BacktrackRequiredSuffix`), never an announcement on selection; consumers strip it via `StripBacktrackSuffix`; a TRUE label must mean the backtrack works. Full: docs/invariants/runway-entry.md#ent-6
- [ENT-7] A start row on a STARTER EXTENSION is not pulled back onto the pavement (`TaxiGraph.StarterExtensionReachesRow` gating `PullOutboardStartRowOntoPavement`); a taxiway crossing the extended centreline never counts as one. Full: docs/invariants/runway-entry.md#ent-7
