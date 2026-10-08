---
paths:
  - "MSFSBlindAssist/Navigation/Surroundings/**"
  - "MSFSBlindAssist/Services/Surroundings/**"
  - "MSFSBlindAssist/Services/SceneryIndex/**"
  - "MSFSBlindAssist/Services/AirportSurroundingsMonitor.cs"
  - "MSFSBlindAssist/Services/Surroundings*.cs"
  - "MSFSBlindAssist/Services/CurrentAirport.cs"
  - "MSFSBlindAssist/Services/AirportWarmUp.cs"
  - "MSFSBlindAssist/Database/Models/ParkingTypes.cs"
  - "MSFSBlindAssist/Database/Models/AirportFacilities.cs"
  - "MSFSBlindAssist/Database/MsfsPackage*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*MsfsPackage*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Surroundings*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Scenery*.cs"
  - "MSFSBlindAssist/Navigation/RunwayShapeSource.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OsmFeature*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OsmRing*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FeatureLexicon*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FeatureKind*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AirportFeatureCatalog*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NavdataFeatureSource*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OnlineFeatureStore*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BglPlacement*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ModelLibName*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GrownBox*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PassingCallout*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PassRadius*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AirportWarmUp*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SurfaceChange*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PlaceListBuilder*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CurrentAirportResolver*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AirportFacilities*.cs"
  - "MSFSBlindAssist/Services/Com1Tuning.cs"
  - "MSFSBlindAssist/Services/AircraftCfgCatalog.cs"
  - "MSFSBlindAssist/Database/IAirportFacilitiesProvider.cs"
---
# Airport surroundings, places and passing callouts rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/surroundings.md.

- [SUR-1] Surroundings features are READOUT ONLY, never a `TaxiGraph.Build` input; a place becomes a destination only via `PlaceListBuilder`, BY POSITION (never a `(Name, Number, Suffix)` join); after a rebuild restore the pick or select NOTHING, never item 0 (more: see full). Full: docs/invariants/surroundings.md#sur-1
- [SUR-2] OSM buildings keep their OWN query, store and event (`OsmFeatureSource` + `OnlineFeatureStore`), never fused with the taxiway-name query; they use `out body geom;` over one bounding box (never the `icao=` area query), and `FetchAsync` never throws on a bad body (more: see full). Full: docs/invariants/surroundings.md#sur-2
- [SUR-3] Feature identity needs NAME and DISTANCE together (`AirportFeatureCatalog.SameFeature`); distance and bearing go to the SAME point (`SurroundingsGeometry.Nearest`); geometry is donated only where it describes the winner, and a `GeometryMayBeOneBody` refusal refuses the merge (more: see full). Full: docs/invariants/surroundings.md#sur-3
- [SUR-4] "Which airport am I at" is always `CurrentAirport.Resolve` -> `CurrentAirportResolver.Pick` (four true-distance passes, order load-bearing), never `GetNearbyAirportICAOs`; the catalog is never built on the UI thread (`SurroundingsCatalogCache.GetAsync` only) (more: see full). Full: docs/invariants/surroundings.md#sur-4
- [SUR-5] `AirportWarmUp` readies online taxiway names and the surroundings catalog before the pilot asks (current airport on the ground only, Shift+D destination anywhere); name prefetches share `_augmentPrefetched`, and `PrefetchAsync` returns at once while online taxi data is off. Full: docs/invariants/surroundings.md#sur-5
- [SUR-6] A scenery model name reaches speech ONLY through `SceneryModelNameClassifier`; ground equipment needs BOTH the lexical and the structural (`SceneryPackageIndexer`) nets; every tier classifies via `FeatureLexicon.NamedKind`; widening kind keywords needs a `SceneryPackageIndexer.CurrentSchemaVersion` bump (more: see full). Full: docs/invariants/surroundings.md#sur-6
- [SUR-7] Neither `SceneryPackageCensus` nor the indexer ever PERSISTS a scan that could not read every file its `layout.json` lists; both walks use the ONE `SceneryPackageDisk.BglFiles`; the census reads `Community` and, on MSFS 2024, `Community2024` (`MsfsPackagesLocator.TryGetCommunityPaths`), never `InstalledPackagesPathNextBoot` (more: see full). Full: docs/invariants/surroundings.md#sur-7
- [SUR-8] A navdata parking type belongs to at most ONE family in `Database/Models/ParkingTypes`, and every consumer reads the families from there, never hand-typed; `IsCargo` is civil cargo only (6), while 7 and 8 are `IsMilitary`. Full: docs/invariants/surroundings.md#sur-8
- [SUR-9] Passing callouts are queued and fire at the closest point of approach, abeam at the minimum, with NO start-up baseline; identity is kind + name + position (`SameFeatureMetres` 40 m, never widen); silent on runway pavement, and the probe never uses `Monitor.TryEnter` (more: see full). Full: docs/invariants/surroundings.md#sur-9
- [SUR-10] The surface-change callout (`SurfaceChangeGate`) has its own switch, is not behind `SuppressCheck`, speaks only a surface FAMILY change confirmed by `ConfirmMetres` from its first reading; other `lastKnownPosition` writers must carry the surface fields forward (more: see full). Full: docs/invariants/surroundings.md#sur-10

Mirrored from a380-coherent.md (it governs the stock COM 1 tuning in Com1Tuning.cs; change it there and here together):
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
