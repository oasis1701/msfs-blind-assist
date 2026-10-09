---
paths:
  - "MSFSBlindAssist/Forms/LandingExitForm.cs"
  - "MSFSBlindAssist/Services/LandingExitPlanner*.cs"
---
# Rules whose code the landing-exit form and planner call

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards lives in or is called from these files, which that area's globs do not cover. EXIT-9 loads here from landing-exits.md and the routing rules from taxi-position.md, so they are not copied here.

Change a rule in its own file and in every copy together (taxi-call-sites.md, taxi-manager-call-sites.md); ClaudeContextBudgetTests fails if they differ.

- [DCK-40] A stand has ONE name app-wide: `GetSelectableGates` to ACT on a stand, `GetNamedSpots` to name one and for every `TaxiGraph.Build` given parking; never build a pilot-heard list from `GetParkingSpots`, nor call the supplier per position update (more: see full). Full: docs/invariants/gsx-stands-docking.md#dck-40
- [DCK-41] Never feed `TaxiGraph.Build` a spot list other than navdata's own set: its parking pass sets `TaxiNodeType.Parking` and can MOVE A HOLD-SHORT; the exceptions are builds given no parking at all: the runway-rows-only ones and the briefing's `OsmPlanningGraph`. Full: docs/invariants/gsx-stands-docking.md#dck-41
- [SUR-9] Passing callouts are queued and fire at the closest point of approach, abeam at the minimum, with NO start-up baseline; identity is kind + name + position (`SameFeatureMetres` 40 m, never widen); silent on runway pavement, and the probe never uses `Monitor.TryEnter` (more: see full). Full: docs/invariants/surroundings.md#sur-9
- [DCK-33] Hot paths must not regress: docking far-field math gated to <150 m or engaged, fired callout latches early-out, `TaxiAssistForm`'s gate list cached per ICAO, `SettingsManager.Save` writing outside its static lock. Full: docs/invariants/gsx-stands-docking.md#dck-33
- [DCK-36] A per-ICAO gate-list cache keys on `GetGateListVersion(icao)` too, via `ShouldRebuildGateList` (never rebuilt on a downgrade); token-only consumers use static `ComputeGateListVersion`; a lost stand leaves NOTHING selected (more: see full). Full: docs/invariants/gsx-stands-docking.md#dck-36
- [SUR-1] Surroundings features are READOUT ONLY, never a `TaxiGraph.Build` input; a place becomes a destination only via `PlaceListBuilder`, BY POSITION (never a `(Name, Number, Suffix)` join); after a rebuild restore the pick or select NOTHING, never item 0 (more: see full). Full: docs/invariants/surroundings.md#sur-1
- [DCK-14] Any cache holding STAND NAMES must key on `GateDataSource.GetGateListVersion`'s token as well as the ICAO, compared through `ShouldRebuildGateList`, or a graph built before GSX published keeps navdata's letters. Full: docs/invariants/gsx-stands-docking.md#dck-14
- [GSX-7] "4.0.8" appears only in `GsxService.ReasonNoRemoteApi` and `GsxGateSelectAnnouncer.GateSelectUnsupportedMessage`, never 4.0.1; that message latches once per `TaxiAssistForm` (the announcer stays stateless), never via `UnavailableReason` or the Access GSX status. Full: docs/invariants/gsx-remote.md#gsx-7
- [HLD-4] Never disable auto-inserted runway-crossing hold-shorts: `ApplyAutoHoldShortPasses` runs only from `AdoptRoute`, the one place a route is adopted; never place a stop on any runway's pavement, and a `StartGuidance` caller that stays in `Taxiing` or `HoldShort` must speak `ConsumeStartHoldCue()` (more: see full). Full: docs/invariants/runway-holds.md#hld-4
- [STR-18] The route-start turn cue has ONE owner (`RouteStartTurnCue`), composed via `ComposeInitialTurnCue` from `LoadRoute` and the handoff RE-ANCHOR, never on the first taxiing frame; delivered once via `ConsumeInitialTurnCue()`, the per-frame one-shot included, angle from `ComputeSteeringHeadingError`, both sides true north (more: see full). Full: docs/invariants/taxi-steering.md#str-18
- [DCK-4] The `.py` per-aircraft stop offset must apply to ALL non-deice gates, `.ini` gates included. Full: docs/invariants/gsx-stands-docking.md#dck-4
- [DCK-5] `GsxOffset.Zero` must be a strict no-op (skip the shift); any resolver miss at any layer degrades to Zero, never throws or half-applies. Full: docs/invariants/gsx-stands-docking.md#dck-5
