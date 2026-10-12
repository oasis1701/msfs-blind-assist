---
paths:
  - "MSFSBlindAssist/Services/SurroundingsCatalogCache.cs"
  - "MSFSBlindAssist/Services/Surroundings/SurroundingsCatalogBuilder.cs"
  - "MSFSBlindAssist/Navigation/Surroundings/GsxTerminalFeatureSource.cs"
  - "tests/MSFSBlindAssist.Tests/SurroundingsCatalogCacheTests.cs"
  - "tests/MSFSBlindAssist.Tests/GsxTerminalFeatureSourceTests.cs"
---
# Stand rules for the surroundings catalog

MIRRORS: copied word for word from gsx-stands.md, whose globs leave these files out: the catalog cache keys on the gate-list token through `GateDataSource.ShouldRebuildGateList`, the builder names stands through `GetNamedSpots` and hands `GetSelectableGates`' list to `GsxTerminalFeatureSource`, and the two tests pin both. Change both together (ClaudeContextBudgetTests checks).

- [DCK-13] Never mutate a list from `GateDataSource`/`ParkingSpotSource.GetSelectableGates` (`.Clear()`, `.Remove`, `.Sort`): it is `GateDataSource`'s cached instance; drop the reference instead. Full: docs/invariants/gsx-stands.md#dck-13
- [DCK-14] Any cache holding STAND NAMES must key on `GateDataSource.GetGateListVersion`'s token as well as the ICAO, compared through `ShouldRebuildGateList`, or a graph built before GSX published keeps navdata's letters. Full: docs/invariants/gsx-stands.md#dck-14
- [DCK-36] A per-ICAO gate-list cache keys on `GetGateListVersion(icao)` too, via `ShouldRebuildGateList` (never rebuilt on a downgrade); token-only consumers use static `ComputeGateListVersion`; a lost stand leaves NOTHING selected (more: see full). Full: docs/invariants/gsx-stands.md#dck-36
- [DCK-40] A stand has ONE name app-wide: `GetSelectableGates` to ACT on a stand, `GetNamedSpots` to name one and for every `TaxiGraph.Build` given parking; never build a pilot-heard list from `GetParkingSpots`, nor call the supplier per position update (more: see full). Full: docs/invariants/gsx-stands.md#dck-40
