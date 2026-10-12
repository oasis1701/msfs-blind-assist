---
paths:
  - "MSFSBlindAssist/Services/SceneryIndex/**"
  - "MSFSBlindAssist/Database/MsfsPackage*.cs"
  - "MSFSBlindAssist/Services/AircraftCfgCatalog.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/FeatureLexicon.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/GsxTerminalFeatureSource.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/AirportFeatureCatalog.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/NavdataFeatureSource.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/GrownBox.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/CurrentAirportResolver.cs"
  - "MSFSBlindAssist/Database/Models/AirportFacilities.cs"
  - "MSFSBlindAssist/Services/Surroundings/OsmFeatureClassifier.cs"
  - "MSFSBlindAssist/Services/Surroundings/SurroundingsCatalogBuilder.cs"
  - "MSFSBlindAssist/Services/Surroundings/SurroundingsTier.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Scenery*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*MsfsPackage*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BglPlacement*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ModelLibName*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FeatureLexicon*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FeatureKind*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GsxTerminalFeatureSource*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AirportFeatureCatalog*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NavdataFeatureSource*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GrownBox*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CurrentAirportResolver*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*AirportFacilities*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OsmFeatureClassifier*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SurroundingsTier*.cs"
---
# Scenery index: BGL readers, package census and model-name classifier rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/scenery-index.md.

- [SUR-6] A scenery model name reaches speech ONLY through `SceneryModelNameClassifier`; ground equipment needs BOTH the lexical and the structural (`SceneryPackageIndexer`) nets; every tier classifies via `FeatureLexicon.NamedKind`; widening kind keywords needs a `SceneryPackageIndexer.CurrentSchemaVersion` bump (more: see full). Full: docs/invariants/scenery-index.md#sur-6
- [SUR-7] Neither `SceneryPackageCensus` nor the indexer ever PERSISTS a scan that could not read every file its `layout.json` lists; both walks use the ONE `SceneryPackageDisk.BglFiles`; the census reads `Community` and, on MSFS 2024, `Community2024` (`MsfsPackagesLocator.TryGetCommunityPaths`), never `InstalledPackagesPathNextBoot` (more: see full). Full: docs/invariants/scenery-index.md#sur-7
- [SUR-11] `MsfsPackagesLocator` reads the simulator's own `UserCfg.opt` while the simulator runs: open it with `FileShare.ReadWrite | FileShare.Delete` and close it before the first `Directory.Exists`, or the simulator's own write can fail. Full: docs/invariants/scenery-index.md#sur-11
- [SUR-19] `BglPlacementReader` keeps ONE parser (its span overloads delegate to the stream one; never a second implementation) bounded by the CUMULATIVE `DefaultMaxTotalBytes` (128 MB); neither BGL reader loads a whole file, and `ModelLibNameReader` streams with NO size cap (a 600 MB cap dropped ten packages' names). Full: docs/invariants/scenery-index.md#sur-19
- [SUR-20] `SceneryPackageDisk.BglFiles` keeps `AttributesToSkip = 0` deliberately (hidden/system files and the junctions add-on linkers use are followed), so `MaxBglRecursionDepth` (12) is what ends a link cycle; never restore the default skip, and keep every shared disk rule (stamp, `OpenShared`, `PersistJson`) in `SceneryPackageDisk`. Full: docs/invariants/scenery-index.md#sur-20
