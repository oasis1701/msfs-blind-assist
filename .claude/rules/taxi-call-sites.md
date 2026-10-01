---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/Forms/LandingExitForm.cs"
  - "MSFSBlindAssist/MainForm.cs"
---
# Rules whose code the taxi entry points call

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards is called from these files, which that area's globs do not cover. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [DCK-40] A stand has ONE name app-wide: `GetSelectableGates` to ACT on a stand, `GetNamedSpots` to name one and for `TaxiGraph.Build`; never build a pilot-heard list from `GetParkingSpots`, nor call the supplier per position update (more: see full). Full: docs/invariants/gsx-stands-docking.md#dck-40
- [DCK-41] Never feed `TaxiGraph.Build` a spot list other than navdata's own set: its parking pass sets `TaxiNodeType.Parking` and can MOVE A HOLD-SHORT; the one exception is a runway-rows-only build with no parking. Full: docs/invariants/gsx-stands-docking.md#dck-41
- [RTE-12] `TaxiGraph.DescribeLocation` runs on pool threads: pool-reachable queries take `_structureLock` inside TaxiGraph, its lazy index is never read outside it, no post-Build mutation skips it; never widen the runway-start reach without asking the owner, nor let the stand's wider reach win near a runway without its gate (more: see full). Full: docs/invariants/taxi-routing.md#rte-12
- [SUR-9] Passing callouts are queued and fire at the closest point of approach, abeam at the minimum, with NO start-up baseline; identity is kind + name + position (`SameFeatureMetres` 40 m, never widen); silent on runway pavement, and the probe never uses `Monitor.TryEnter` (more: see full). Full: docs/invariants/surroundings.md#sur-9
- [DCK-33] Hot paths must not regress: docking far-field math gated to <150 m or engaged, fired callout latches early-out, `TaxiAssistForm`'s gate list cached per ICAO, `SettingsManager.Save` writing outside its static lock. Full: docs/invariants/gsx-stands-docking.md#dck-33
- [DCK-36] A per-ICAO gate-list cache keys on `GetGateListVersion(icao)` too, via `ShouldRebuildGateList` (never rebuilt on a downgrade); token-only consumers use static `ComputeGateListVersion`; a lost stand leaves NOTHING selected (more: see full). Full: docs/invariants/gsx-stands-docking.md#dck-36
- [HLD-7] Runway geometry is read through the ONE `RunwayShape.For` (classifier, hold placement, Where-Am-I, takeoff, vacate, reach walk, hold naming); never read `Pavement*` directly or test membership on `Lat1..Lon2` alone; repair an outboard start ROW, never cap the extent (more: see full). Full: docs/invariants/runway-holds.md#hld-7
- [SUR-1] Surroundings features are READOUT ONLY, never a `TaxiGraph.Build` input; a place becomes a destination only via `PlaceListBuilder`, BY POSITION (never a `(Name, Number, Suffix)` join); after a rebuild restore the pick or select NOTHING, never item 0 (more: see full). Full: docs/invariants/surroundings.md#sur-1
