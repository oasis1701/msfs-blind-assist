---
paths:
  - "MSFSBlindAssist/Services/TaxiAugment/**"
  - "tests/MSFSBlindAssist.Tests/**/*ProviderWrap*.cs"
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AptDat*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Overpass*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiDataMerger*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiGeo*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Augmenting*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OsmTaxiSource*.cs"
---
# Online taxi-data augmentation rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-augmentation.md.

- [AUG-1] Navdata is AUTHORITATIVE: never overwrite an existing navdata taxiway/gate name, online names only fill UNNAMED segments, and online-only geometry is IGNORED (never steer on an offset online line). Full: docs/invariants/taxi-augmentation.md#aug-1
- [AUG-2] Every non-null assignment of `MainForm.airportDataProvider` must go through `MainForm.WithTaxiAugmentation`, or online names, aliases and the briefing's OSM tier are silently lost; a database switch carries the plan via `FlightPlan.CopyFrom`, never a field-by-field copy. Full: docs/invariants/taxi-augmentation.md#aug-2
- [AUG-3] Augmentation is anti-grass: online data NEVER sets a gate Name or position and NEVER adds a selectable gate; gate identity comes from GSX/navdata, online contributes searchable aliases only. Full: docs/invariants/taxi-augmentation.md#aug-3
- [AUG-4] Do NOT implement OSM `holding_position` hold-short sharpening without an explicit sim-verified ask; augmentation stays NAME-only and never touches the safety-critical hold-short placement. Full: docs/invariants/taxi-augmentation.md#aug-4

Mirrored from surroundings.md and taxi-routing.md (they govern AugmentingAirportDataProvider and OverpassClient code; change them there and here together):
- [SUR-2] OSM buildings keep their OWN query, store and event (`OsmFeatureSource` + `OnlineFeatureStore`), never fused with the taxiway-name query; they use `out body geom;` over one bounding box (never the `icao=` area query), and `FetchAsync` never throws on a bad body (more: see full). Full: docs/invariants/surroundings.md#sur-2
- [SUR-5] `AirportWarmUp` readies online taxiway names and the surroundings catalog before the pilot asks (current airport on the ground only, Shift+D destination anywhere); name prefetches share `_augmentPrefetched`, and `PrefetchAsync` returns at once while online taxi data is off. Full: docs/invariants/surroundings.md#sur-5
- [RTE-25] One taxiway has ONE spelling: `TaxiGraph.BuildCanonicalTaxiwayNames` folds case variants by PROVENANCE first (navdata beats online, `TaxiPath.NameFromOnlineSource`), then the majority spelling, then ordinal-smallest; never ordinal-smallest alone, as `TaxiwayName` is spoken verbatim (more: see full). Full: docs/invariants/taxi-routing.md#rte-25
