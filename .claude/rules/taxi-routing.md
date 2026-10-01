---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Navigation/TaxiRouter.cs"
  - "MSFSBlindAssist/Navigation/Route*.cs"
  - "MSFSBlindAssist/Navigation/TaxiLeadIn.cs"
  - "MSFSBlindAssist/Navigation/TaxiwayChangeGate.cs"
  - "MSFSBlindAssist/Services/StartWarningChatterGate.cs"
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TaxiGraph*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Route*.cs"
---
# Taxi routing rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-routing.md.

- [RTE-1] No airport-specific hardcoding: every taxiway, parking and runway name must flow through from the user's DB unchanged. Full: docs/invariants/taxi-routing.md#rte-1
- [RTE-2] Bridge a stranded stand (`BridgeOrphanParkingIslands`) ONLY when its island is all navdata `P` lead-ins within 50 m: never on/across runway pavement, onto a hold-short, a stand or another lead-in chain, never an island carrying a taxiway; runway-exit logic skips `IsStandBridge` edges (more: see full). Full: docs/invariants/taxi-routing.md#rte-2
- [RTE-3] A bridge-only stand stub (`TaxiGraph.IsBridgeOnlyStandStub`) must never be a route START, only a destination: every route-start caller of `FindNearestNode`/`…InDirection`/`…OnTaxiway` must pass `excludeBridgeOnlyStandStubs: true`, as must any future A*/Dijkstra start picker. Full: docs/invariants/taxi-routing.md#rte-3
- [RTE-4] Where-Am-I's runway-detection fallback must use a strict tolerance, `RunwayShape`'s own half-width with no +5m fudge, and stay gated on `_lastOnGround`. Full: docs/invariants/taxi-routing.md#rte-4
- [RTE-5] Never announce runway info (length, surface, ILS) from taxi guidance; it is out of scope. Full: docs/invariants/taxi-routing.md#rte-5
- [RTE-6] Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons, which break on long segments. Full: docs/invariants/taxi-routing.md#rte-6
- [RTE-7] Off-route auto-recalc must stay gated on the route-joined latch `_hasJoinedRoute`, or the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance. Full: docs/invariants/taxi-routing.md#rte-7
- [RTE-8] An accepted recalc must announce the new taxiway sequence by name ("Route changed. Now via X, Y…"), never the old generic "Recalculating… Taxiway X." wording. Full: docs/invariants/taxi-routing.md#rte-8
- [RTE-9] The Progressive Taxi terminator block keeps its OWN runway/taxiway combos: never reuse the per-row "Hold short of runway" combo for it, and hide and reset that per-row combo in Progressive Taxi mode so a stale pick can't leak into the route. Full: docs/invariants/taxi-routing.md#rte-9
- [RTE-10] Never restore an "already used taxiway" filter on the Add-Taxiway dropdown: clearances legitimately reuse a taxiway across a runway crossing; only the immediately-previous taxiway is conditionally hidden. Full: docs/invariants/taxi-routing.md#rte-10
- [RTE-11] "Where Am I" is ground-only by design (gated on `_lastOnGround`); runway detection must use `TaxiGraph.RunwayCenterlines`, never `taxi_path.type='R'` edges (the DB has none). Full: docs/invariants/taxi-routing.md#rte-11
- [RTE-12] `TaxiGraph.DescribeLocation` runs on pool threads: pool-reachable queries take `_structureLock` inside TaxiGraph, its lazy index is never read outside it, no post-Build mutation skips it; never widen the runway-start reach without asking the owner, nor let the stand's wider reach win near a runway without its gate (more: see full). Full: docs/invariants/taxi-routing.md#rte-12
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
- [RTE-24] The `FindRunwayBridge` 200m cap must not be raised: it prevents a silent half-airport jump when an ATC clearance is genuinely wrong (those fall back to shortest path with a log line). Full: docs/invariants/taxi-routing.md#rte-24
- [RTE-25] One taxiway has ONE spelling: `TaxiGraph.BuildCanonicalTaxiwayNames` folds case variants by PROVENANCE first (navdata beats online, `TaxiPath.NameFromOnlineSource`), then the majority spelling, then ordinal-smallest; never ordinal-smallest alone, as `TaxiwayName` is spoken verbatim (more: see full). Full: docs/invariants/taxi-routing.md#rte-25
