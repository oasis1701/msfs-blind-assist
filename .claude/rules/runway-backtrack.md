---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Backtrack.cs"
  - "MSFSBlindAssist/Navigation/BacktrackConnectionHandoff.cs"
  - "MSFSBlindAssist/Navigation/LandingExitBacktrack.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BacktrackConnectionHandoff*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitBacktrack*.cs"
---
# Runway backtrack after landing rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md, docs/virtual-pilot.md. Full text of each rule: docs/invariants/runway-backtrack.md.

- [BTK-1] An exit's "(backtrack required)" and the guided backtrack share ONE rule (`LandingExitBacktrack`); keep its forward-way-off gate, never widen it to every runway, never let it engage on a RET; `TryStartPlannedBacktrack` runs BEFORE the normal handoff, keyed on 150 ft. Full: docs/invariants/runway-backtrack.md#btk-1
- [BTK-2] "Runway vacated." is a safety claim: the connection node must clear every runway corridor and so must the aircraft; the node is passed ABEAM, so `BacktrackConnectionHandoff` names taxiway and side and hands off when clear, never needing 25 m (more: see full). Full: docs/invariants/runway-backtrack.md#btk-2
