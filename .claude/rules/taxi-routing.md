---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/Route*.cs"
  - "MSFSBlindAssist/Navigation/TaxiLeadIn.cs"
  - "MSFSBlindAssist/Navigation/TaxiwayChangeGate.cs"
  - "MSFSBlindAssist/Services/StartWarningChatterGate.cs"
  - "MSFSBlindAssist/Navigation/LoadRefusalRollback.cs"
  - "MSFSBlindAssist/Navigation/ReachabilityRefusalGate.cs"
  - "MSFSBlindAssist/Navigation/RunwayReachGate.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/Services/LiveRouteStates.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiGraph*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiRouter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteReachability*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteChangedCallout*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteRunwayCrossings*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteStartTurnCue*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RouteTaxiwaySequence*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LiveRouteStates*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LoadRefusalRollback*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ReachabilityRefusalGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*StartWarningChatterGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayChangeGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiLeadIn*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiwayEntryNode*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OrphanParkingIsland*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayReachGate*.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Announcements.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs"
  - "MSFSBlindAssist/Database/Models/TaxiNode.cs"
  - "MSFSBlindAssist/Database/Models/TaxiPath.cs"
---
# Taxi routing rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-routing.md.

- [RTE-1] No airport-specific hardcoding: every taxiway, parking and runway name must flow through from the user's DB unchanged. Full: docs/invariants/taxi-routing.md#rte-1
- [RTE-3] A bridge-only stand stub (`TaxiGraph.IsBridgeOnlyStandStub`) must never be a route START, only a destination: every route-start caller of `FindNearestNode`/`…InDirection`/`…OnTaxiway` must pass `excludeBridgeOnlyStandStubs: true`, as must any future A*/Dijkstra start picker. Full: docs/invariants/taxi-routing.md#rte-3
- [RTE-5] Never announce runway info (length, surface, ILS) from taxi guidance; it is out of scope. Full: docs/invariants/taxi-routing.md#rte-5
- [RTE-8] An accepted recalc must announce the new taxiway sequence by name ("Route changed. Now via X, Y…"), never the old generic "Recalculating… Taxiway X." wording. Full: docs/invariants/taxi-routing.md#rte-8
- [RTE-9] The Progressive Taxi terminator block keeps its OWN runway/taxiway combos: never reuse the per-row "Hold short of runway" combo for it, and hide and reset that per-row combo in Progressive Taxi mode so a stale pick can't leak into the route. Full: docs/invariants/taxi-routing.md#rte-9
- [RTE-10] Never restore an "already used taxiway" filter on the Add-Taxiway dropdown: clearances legitimately reuse a taxiway across a runway crossing; only the immediately-previous taxiway is conditionally hidden. Full: docs/invariants/taxi-routing.md#rte-10
- [RTE-13] `LoadRoute`/`TryRecalculateRoute` must filter start-node candidates to the destination's `ComponentId`, or A* can't reach an isolated taxiway island and route calc silently fails. Full: docs/invariants/taxi-routing.md#rte-13
- [RTE-14] Node ID 0 is a permanent "not set" sentinel in `TaxiGraph`; never reuse it as a real node id. Full: docs/invariants/taxi-routing.md#rte-14
- [RTE-15] Taxiway exit/intersection picking must use GRAPH distance (Dijkstra from the destination), never Euclidean, which silently fails on a graph dead-end. Full: docs/invariants/taxi-routing.md#rte-15
- [RTE-16] `FindBestIntersection` has no Euclidean fallback by design: returning -1 (unreachable) on a malformed graph is intentional; never add one back, it produced silently wrong routes. Full: docs/invariants/taxi-routing.md#rte-16
- [RTE-17] The last cleared taxiway must be honored as the route terminus when it branches off the destination; never let the final unconstrained leg silently drop it. Full: docs/invariants/taxi-routing.md#rte-17
- [RTE-18] The post-recalc sanity gate needs BOTH the length-blowup and the backwards-bearing indicators, OR'd (not AND'd); either alone misses real dead-end-backtrack recalcs. Full: docs/invariants/taxi-routing.md#rte-18
- [RTE-19] The initial-load sanity advisory must compare against the PRE-truncation `fullRouteMeters`, never the truncated total, which can hide a genuine backtrack detour. Full: docs/invariants/taxi-routing.md#rte-19
- [RTE-20] `TryRecalculateRoute` must fall back to shortest path, never the full original clearance sequence, when the aircraft isn't near any sequence taxiway; reapplying it routes the pilot backwards through the whole clearance. Full: docs/invariants/taxi-routing.md#rte-20
- [RTE-21] `TaxiAssistForm.OnCalculateClicked` must refresh the aircraft position from `LastKnownPosition` immediately before building the route, or the route starts from a stale pre-pushback position and off-routes on frame one. Full: docs/invariants/taxi-routing.md#rte-21
- [RTE-22] A recalculated route identical to the current remaining sequence must skip the "Route changed" callout, latch resets and tone re-slew entirely, or a sharp corner-cut trips a spurious "Route changed". Full: docs/invariants/taxi-routing.md#rte-22
- [RTE-23] Do NOT remove the first-taxiway pre-snap (the anchoring fix); the pavement lead-in replaces it only when the first cleared taxiway is far (>75 m from the aircraft). Full: docs/invariants/taxi-routing.md#rte-23
- [RTE-24] Never drop `FindRunwayBridge`'s `MAX_BRIDGE_METERS` cap (400 m, raised from 200 m in #76) or go back to scoring a bridge by gap alone: candidates are scored by total route cost, and the cap still prevents a silent half-airport jump when an ATC clearance is genuinely wrong (those fall back to shortest path with a log line). Full: docs/invariants/taxi-routing.md#rte-24
- [RTE-25] One taxiway has ONE spelling: `TaxiGraph.BuildCanonicalTaxiwayNames` folds case variants by PROVENANCE first (navdata beats online, `TaxiPath.NameFromOnlineSource`), then the majority spelling, then ordinal-smallest; never ordinal-smallest alone, as `TaxiwayName` is spoken verbatim (more: see full). Full: docs/invariants/taxi-routing.md#rte-25
- [RTE-26] `RouteReachability` classifies position before `LoadRoute`/`TryRecalculateRoute` adopt a route: off the destination's network, refuse (naming the runway) if the unmapped first leg touches a runway, else warn; refuse an unreachable destination by name; runway pavement or no taxi edge is `Unchanged`. Never classify from the nearest node alone. Full: docs/invariants/taxi-routing.md#rte-26
- [RTE-28] Reachability sentences name a stand by its identifier only (the label up to its first spaced dash, `RouteReachabilityMessages.SpokenDestinationName`), never the whole label. Full: docs/invariants/taxi-routing.md#rte-28
- [RTE-29] A refused `LoadRoute` must put back the destination, lineup and graph state it had already overwritten (`LoadRefusalRollback`), so a refusal mid-taxi leaves the route being flown untouched; the older failure returns do not roll back. Full: docs/invariants/taxi-routing.md#rte-29
- [RTE-30] Post-Build `TaxiGraph` structure changes run on the UI thread only (UI-thread readers take no lock) and only through TaxiGraph's own mutators, which drop the cell index (`InvalidateCellIndex`); never write `Nodes`/`Adjacency` directly on a graph `DescribeLocation` serves, nor detect staleness by recounting adjacency per query. Full: docs/invariants/taxi-routing.md#rte-30

Mirrored from surroundings.md (it governs the Place list in TaxiAssistForm.cs; change it there and here together):
- [SUR-13] `TaxiAssistForm` never builds a Place catalog: it reads `SurroundingsCatalogCached` and starts at most ONE `LoadPlaces` (never while `_placesLoading` or `_placesSettling`), listing meanwhile from the catalog last loaded for that airport; a pick a rebuild lost is cleared and spoken (`PlaceListUpdatedMessage`). Full: docs/invariants/surroundings.md#sur-13
