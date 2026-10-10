---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
  - "MSFSBlindAssist/Forms/LandingExitForm.cs"
  - "MSFSBlindAssist/Services/LandingExitPlanner*.cs"
---
# Stand, GSX and surroundings rules for the taxi entry points

MIRRORS: copied word for word from their areas' rule files, whose globs leave these files out; change both together (ClaudeContextBudgetTests checks).

- [DCK-40] A stand has ONE name app-wide: `GetSelectableGates` to ACT on a stand, `GetNamedSpots` to name one and for every `TaxiGraph.Build` given parking; never build a pilot-heard list from `GetParkingSpots`, nor call the supplier per position update (more: see full). Full: docs/invariants/gsx-stands.md#dck-40
- [DCK-13] Never mutate a list from `GateDataSource`/`ParkingSpotSource.GetSelectableGates` (`.Clear()`, `.Remove`, `.Sort`): it is `GateDataSource`'s cached instance; drop the reference instead. Full: docs/invariants/gsx-stands.md#dck-13
- [DCK-41] Never feed `TaxiGraph.Build` a spot list other than navdata's own set: its parking pass sets `TaxiNodeType.Parking` and can MOVE A HOLD-SHORT; the exceptions are builds given no parking at all: the runway-rows-only ones and the briefing's `OsmPlanningGraph`. Full: docs/invariants/gsx-stands.md#dck-41
- [SUR-9] Passing callouts are queued and fire at the closest point of approach, abeam at the minimum, with NO start-up baseline; identity is kind + name + position (`SameFeatureMetres` 40 m, never widen); silent on runway pavement, and the probe never uses `Monitor.TryEnter` (more: see full). Full: docs/invariants/surroundings.md#sur-9
- [DCK-33] Hot paths must not regress: docking far-field math gated to <150 m or engaged, fired callout latches early-out, `TaxiAssistForm`'s gate list cached per ICAO, `SettingsManager.Save` writing outside its static lock. Full: docs/invariants/gsx-docking.md#dck-33
- [DCK-36] A per-ICAO gate-list cache keys on `GetGateListVersion(icao)` too, via `ShouldRebuildGateList` (never rebuilt on a downgrade); token-only consumers use static `ComputeGateListVersion`; a lost stand leaves NOTHING selected (more: see full). Full: docs/invariants/gsx-stands.md#dck-36
- [SUR-1] Surroundings features are READOUT ONLY, never a `TaxiGraph.Build` input; a place becomes a destination only via `PlaceListBuilder`, BY POSITION (never a `(Name, Number, Suffix)` join); after a rebuild restore the pick or select NOTHING, never item 0 (more: see full). Full: docs/invariants/surroundings.md#sur-1
- [DCK-14] Any cache holding STAND NAMES must key on `GateDataSource.GetGateListVersion`'s token as well as the ICAO, compared through `ShouldRebuildGateList`, or a graph built before GSX published keeps navdata's letters. Full: docs/invariants/gsx-stands.md#dck-14
- [GSX-7] "4.0.8" appears only in `GsxService.ReasonNoRemoteApi` and `GsxGateSelectAnnouncer.GateSelectUnsupportedMessage`, never 4.0.1; that message latches once per `TaxiAssistForm` (the announcer stays stateless), never via `UnavailableReason` or the Access GSX status. Full: docs/invariants/gsx-remote.md#gsx-7
- [GSX-4] `GsxGateSelectAnnouncer` speaks `gate.select` outcomes that end the request QUEUED (`Announce`), never immediate; never let one go silent again (a few are silent by design), and its switch keeps NO `default:` arm so a new outcome stays silent until deliberately voiced. Full: docs/invariants/gsx-remote.md#gsx-4
- [GSX-10] `gsx.log` records `ev=publish ... route=background|window|none` and must NEVER assert that a phrase was SPOKEN (by default nothing routes it); `route=window` must not claim delivery either. Full: docs/invariants/gsx-remote.md#gsx-10
- [DCK-4] The `.py` per-aircraft stop offset must apply to ALL non-deice gates, `.ini` gates included. Full: docs/invariants/gsx-stands-docking.md#dck-4
- [DCK-5] `GsxOffset.Zero` must be a strict no-op (skip the shift); any resolver miss at any layer degrades to Zero, never throws or half-applies. Full: docs/invariants/gsx-stands-docking.md#dck-5
