---
paths:
  - "MSFSBlindAssist/Services/Gsx/*.cs"
  - "MSFSBlindAssist/Services/Docking*.cs"
  - "MSFSBlindAssist/Services/GateDataSource.cs"
  - "MSFSBlindAssist/Services/GateResolver.cs"
  - "MSFSBlindAssist/Services/ParkingSpotSource.cs"
  - "MSFSBlindAssist/Services/DistanceFormatter.cs"
  - "MSFSBlindAssist/Database/Models/ParkingSpot.cs"
  - "MSFSBlindAssist/Forms/GateTeleportForm.cs"
  - "MSFSBlindAssist/Services/StandId.cs"
  - "MSFSBlindAssist/Services/GateSearchFilter.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Docking*.cs"
  - "MSFSBlindAssist/Services/Gsx/Remote/GsxRemoteParkingReader.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Services/Gsx/Remote/GsxConcourseLetterFiller.cs"
  - "MSFSBlindAssist/Services/Gsx/Remote/GsxTerminalDisambiguator.cs"
  - "MSFSBlindAssist/Services/Gsx/Remote/GsxNavdataGeometryFiller.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DistanceFormatter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DistanceUnit*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GateResolver*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GateSearchFilter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GateDataSource*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GateAlias*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ParkingSpot*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ParkingTypes*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*StandId*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AircraftSizeClass*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BacktrackEntry*.cs"
  - "MSFSBlindAssist/Database/Models/GsxGate.cs"
  - "MSFSBlindAssist/Database/Models/GateSource.cs"
  - "MSFSBlindAssist/Settings/DistanceUnit.cs"
---
# Stands and docking: domain boundary and stop position rules

Loaded when Claude reads matching code. Background: docs/gsx.md. Full text of each rule: docs/invariants/gsx-stands-docking.md.

- [DCK-1] The docking and positioning code owns GSX POSITIONING only (gate/stand selection, docking geometry, deice positioning): never add live service-state logic to it, and docking must never read service vars. Service state and its announcements are Access GSX's ([GSX-16], [GSX-18], [GSX-19]). Full: docs/invariants/gsx-stands-docking.md#dck-1
- [DCK-3] GSX gate spot-position priority is `this_parking_pos` -> navdata -> stop position LAST; the stop position is a VDGS nose-stop reference, not an aircraft-datum location. Full: docs/invariants/gsx-stands-docking.md#dck-3
- [DCK-4] The `.py` per-aircraft stop offset must apply to ALL non-deice gates, `.ini` gates included. Full: docs/invariants/gsx-stands-docking.md#dck-4
- [DCK-5] `GsxOffset.Zero` must be a strict no-op (skip the shift); any resolver miss at any layer degrades to Zero, never throws or half-applies. Full: docs/invariants/gsx-stands-docking.md#dck-5
- [DCK-16] Docking's forward distance math must stay datum-aligned: never put the per-aircraft `gsx.cfg` longitudinal door offset into the stop math. Full: docs/invariants/gsx-stands-docking.md#dck-16
- [DCK-26] Docking's taxi-away disengage must use ABSOLUTE distance, never along-track, which goes negative once the stop is behind and never trips. Full: docs/invariants/gsx-stands-docking.md#dck-26
- [DCK-31] Never re-add the runway-style stopped-misaligned pulse to gate lineup; precision parking is docking's job. Full: docs/invariants/gsx-stands-docking.md#dck-31
- [DCK-39] The Remote API publishes no docking stop position, so stop geometry still comes from GSX's `.ini`/`.py` profiles; never source the stop from the API's `lat`/`lon`, far from the real VDGS stop point. Full: docs/invariants/gsx-stands-docking.md#dck-39
