---
paths:
  - "MSFSBlindAssist/Forms/ElectronicFlightBagForm.cs"
  - "MSFSBlindAssist/Forms/ColdTemperatureCorrectionForm.cs"
  - "MSFSBlindAssist/Database/NavigationDatabaseProvider.cs"
  - "MSFSBlindAssist/Database/OrphanIlsMatcher.cs"
  - "MSFSBlindAssist/Navigation/FlightPlan*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayInfo*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OrphanIls*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ColdTemperature*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FlightPlanCopy*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FlightPlanManager*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NavigationDatabaseProvider*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NavDataFixture*.cs"
  - "MSFSBlindAssist/Database/LittleNavMapProvider.cs"
  - "MSFSBlindAssist/Database/IAirportDataProvider.cs"
---
# Flight-planning EFB and procedure data (Shift+E) rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/flight-planning-efb.md.

- [EFB-1] Never drop a fix-less leg in `ParseLegToWaypoint`: empty-`fix_ident` ARINC 424 legs carry SID climb-outs and missed-approach heading legs; synthesize a maneuver label instead of returning null. Full: docs/invariants/flight-planning-efb.md#efb-1
- [EFB-2] Resolve fix coordinates across ALL fix tables (vor/ndb/runway_end/airport), not just `waypoint`, or fixes stay at (0,0) and corrupt distance/bearing; never rely on `approach_leg.fix_lonx`/`fix_laty` (NULL in this build). Full: docs/invariants/flight-planning-efb.md#efb-2
- [EFB-3] Circling approaches share `suffix='A'` with STARs — distinguish them by the presence of a missed-approach leg; `GetSTARs` must exclude them or they pollute the STAR list. Full: docs/invariants/flight-planning-efb.md#efb-3
- [EFB-4] "ALL"-runway SID/STAR must load the runway-INDEPENDENT procedure body (empty runway_name AND non-RW arinc_name); keying on `runway_name` alone degenerates to a random runway transition's legs. Full: docs/invariants/flight-planning-efb.md#efb-4
- [EFB-5] `FlightPlanManager.AppendWaypoints` must drop the duplicate boundary fix shared by a transition and its procedure, or a spurious 0 NM leg appears. Full: docs/invariants/flight-planning-efb.md#efb-5
- [EFB-6] `GetRunwayDetailedInfo` must explicitly alias the `runway_end` columns (`re.heading AS end_heading`, etc.) and read them by those `end_*` names; the bare columns are ambiguous, and the code must not rely on Sqlite picking the last occurrence. Full: docs/invariants/flight-planning-efb.md#efb-6
- [EFB-7] Minimums (DA/MDA/visibility) are not in navdata at all (not CIFP, MSFS or Navigraph DFD) — a data-source limitation, not a fixable bug; don't try to source them from the DB. Full: docs/invariants/flight-planning-efb.md#efb-7
- [EFB-8] The Cold Temperature Correction math must be transcribed VERBATIM from FlyByWire's EUROCONTROL formula (redundant term and round-up-to-10ft included) and must never correct a published altitude downward on a warm temperature. Full: docs/invariants/flight-planning-efb.md#efb-8
- [EFB-9] Never query the `ils` table by `ident` alone — always scope by airport (+runway); anything unscoped must go through `GetILSForRunway` so it is spatially validated. Full: docs/invariants/flight-planning-efb.md#efb-9
- [EFB-10] `OrphanIlsMatcher` links an orphaned `ils` row by nearest runway CENTERLINE, only when no other end at that airport is nearer and it lies ahead within `LengthMetres + MaxLocalizerSetbackMetres` (an end of unknown length, 0, skips that ceiling); never revert to nearest-threshold, tune `MaxCrossTrackMetres` or widen the competitor set. Full: docs/invariants/flight-planning-efb.md#efb-10
