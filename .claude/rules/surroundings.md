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
- [SUR-8] A navdata parking type belongs to at most ONE family in `Database/Models/ParkingTypes`, and every consumer reads the families from there, never hand-typed; `IsCargo` is civil cargo only (6), while 7 and 8 are `IsMilitary`. Full: docs/invariants/surroundings.md#sur-8
- [SUR-9] Passing callouts are queued and fire at the closest point of approach, abeam at the minimum, with NO start-up baseline; identity is kind + name + position (`SameFeatureMetres` 40 m, never widen); silent on runway pavement, and the probe never uses `Monitor.TryEnter` (more: see full). Full: docs/invariants/surroundings.md#sur-9
- [SUR-10] The surface-change callout (`SurfaceChangeGate`) has its own switch, is not behind `SuppressCheck`, speaks only a surface FAMILY change confirmed by `ConfirmMetres` from its first reading; other `lastKnownPosition` writers must carry the surface fields forward (more: see full). Full: docs/invariants/surroundings.md#sur-10
- [SUR-12] A Place with no stand ends only at `PlaceListBuilder.NearestRoutableNode`, which refuses a node on runway pavement or a hold line (`TaxiGraph.IsNavdataHoldShort` as well as `TaxiNode.Type`, which the parking pass restamps Parking), never the building's own coordinate, and carries NO heading. Full: docs/invariants/surroundings.md#sur-12
- [SUR-13] `TaxiAssistForm` never builds a Place catalog: it reads `SurroundingsCatalogCached` and starts at most ONE `LoadPlaces` (never while `_placesLoading` or `_placesSettling`), listing meanwhile from the catalog last loaded for that airport; a pick a rebuild lost is cleared and spoken (`PlaceListUpdatedMessage`). Full: docs/invariants/surroundings.md#sur-13
- [SUR-14] `OnlineFeatureStore.GetAsync` gives up with `task.WaitAsync(maxWait)`, never `WhenAny(task, Task.Delay(...))`, and only a caller that gave up arms `FeaturesUpdated`; `Prefetch` never arms it (an answer landing mid-build would discard the build about to include it) and never throws. OSM data stays in memory. Full: docs/invariants/surroundings.md#sur-14
- [SUR-15] A database switch (`RefreshDatabaseProvider`) clears `surroundingsCache`, `onlineFeatures`, `ClearWhereAmICache()` and `groundTrafficMonitor.ClearRunwayCache()` and calls `surroundingsMonitor.Reset()`, which also runs on reconnect, aircraft switch and turnaround liftoff: no staleness token moves on a switch. Full: docs/invariants/surroundings.md#sur-15
- [SUR-16] The Settings taxiway-name refresh resolves its airport from a position asked of the simulator at the press (`GetFreshAircraftPositionAsync`; `LastKnownPosition`, stale in quiet cruise, is only its 1.5 s fallback), via `CurrentAirport.Resolve` off the UI thread. Full: docs/invariants/surroundings.md#sur-16
- [SUR-17] Every spoken line of a surroundings lookup goes through `SpeakLookupLine` (`SurroundingsLookupNotice.Delivery`, timed from the PRESS); the catalog build starts before the Where-Am-I line and runs beside it, and "Looking around." waits `NoticeWait` from the press: never re-time from the position callback or a build's start. Full: docs/invariants/surroundings.md#sur-17
- [SUR-18] Every regex in `SceneryModelNameClassifier`, `FeatureLexicon` and the BGL readers is `static readonly` with `RegexOptions.CultureInvariant`, never built per call: a culture-sensitive or per-call pattern added with the next vocabulary change is the regression. Full: docs/invariants/surroundings.md#sur-18
- [SUR-21] `SurroundingsCatalogBuilder` requires only navdata: every OPTIONAL tier (GSX, OSM, scenery/census, any new one) goes through `SurroundingsTier.Read`, and the build is `Degraded` when a tier threw, OSM came back Pending/Failed, the scan was short or `UserCfg.opt` was unreadable, never because a list was empty. Full: docs/invariants/surroundings.md#sur-21
- [SUR-22] MainForm's passing-callout `SuppressCheck` covers takeoff assist, docking, `LandingRollout`, `LiningUp`, `HoldShort`, `ProgressiveHold` and both backtrack states, and deliberately NOT `Taxiing`: a pilot under active taxi guidance must still hear the surroundings. Full: docs/invariants/surroundings.md#sur-22
- [SUR-23] `AirportSurroundingsMonitor.OnPositionReceived` judges each `AIRCRAFT_POSITION` answer itself, never `LastKnownPosition`, inside one try (first subscriber); it POSTS every callout, queued, to the UI thread, runs the surface callout before any airport guard, and defers its own "Off the pavement" to the rollout's alert, never the reverse. Full: docs/invariants/surroundings.md#sur-23
- [SUR-24] The runway probe (`TaxiGuidanceManager.IsOnRunwayPavement`) answers only from geometry in hand and NEVER builds a graph; its warm-up reads runway rows only (`PrepareRunwayShapeWarmUp`, never `GetTaxiPaths`); `_graphGeneration` is stamped only when a NEW `_graph` instance is installed, never when a re-route hands the same graph back. Full: docs/invariants/surroundings.md#sur-24

Mirrored from a380-coherent.md (it governs the stock COM 1 tuning in Com1Tuning.cs; change it there and here together):
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18

Mirrored from gsx-stands.md (it governs `GsxTerminalFeatureSource`; change it there and here together):
- [DCK-44] `ParkingSpot.GsxUnconfigured` is set ONLY by `GsxRemoteParkingReader` (GSX sent no `heading` and no `hasJetway` value, a JSON null counting as none: no profile covers the stand); only then does `GsxNavdataGeometryFiller` borrow `HasJetway`/`AirlineCodes`, and `GsxTerminalFeatureSource` skips such stands (their header is GSX's own). Full: docs/invariants/gsx-stands.md#dck-44
