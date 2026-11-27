---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
---
# Taxi steering rules for the routing partial

MIRRORS: copied word for word from taxi-steering.md, which leaves this partial out for the rule budget: these are its rules whose code `TaxiGuidanceManager.Routing.cs` declares (`AdvanceToNearestSegment`, `LoadRoute`, `RunwaysOnEntryStub`). Change a rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [STR-5] `AdvanceToNearestSegment` needs the pin-breaker `GuidanceGeometry.HasPassedOntoNextSegment` (30m cross-track) in BOTH un-advanceable cases, via `AdvanceSegment()`, and it must NEVER fire while the current segment is a hold-short (a silent pass is the incursion direction). Full: docs/invariants/taxi-steering.md#str-5
- [STR-18] The route-start turn cue has ONE owner (`RouteStartTurnCue`), composed via `ComposeInitialTurnCue` from `LoadRoute` and the handoff RE-ANCHOR, never on the first taxiing frame; delivered once via `ConsumeInitialTurnCue()`, the per-frame one-shot included, angle from `ComputeSteeringHeadingError`, both sides true north (more: see full). Full: docs/invariants/taxi-steering.md#str-18
- [STR-20] An entry stub ending on the destination centreline up to `ENTRY_STUB_BEYOND_END_M` (100 m, `RunwayReachGate.StubBeyondEndMeters`) past the recorded end still reaches the runway (`RunwaysOnEntryStub` fallback; `TaxiRouter.DestinationRunways` reads a destination node the same way); never widen either lateral bound past the runway's half-width. Full: docs/invariants/taxi-steering.md#str-20
- [STR-21] `AdvanceToNearestSegment` must not skip past a SHARP junction early (the `ADVANCE_*` hairpin guard, covering every segment past the junction); each gate is measured, so re-run a VirtualPilot A/B before touching one. Full: docs/invariants/taxi-steering.md#str-21

Mirrored from runway-entry.md (it governs `ApplyHoldingPointPin` and `TryForwardFacingRunwayEntry`, and leaves this partial out for the rule budget; change them there and here together):
- [ENT-3] The departure-entry holding-point picker skips DESCRIPTIVE OSM hold labels (`TaxiGraph.IsDescriptiveHoldLabel`); Progressive Taxi's hold-short list is not filtered; a holding-point pin that cannot be made is SPOKEN, never only logged. Full: docs/invariants/runway-entry.md#ent-3
- [ENT-4] A full-length route arriving facing the reciprocal is re-routed to a forward-facing entry (`TryForwardFacingRunwayEntry`, `RunwayEntryDirection`), never for an intersection, holding point, backtrack or cross-component route; pilot picks run AFTER it and the pin. Full: docs/invariants/runway-entry.md#ent-4

Mirrored from taxi-turn-picks.md (it governs `TryRecalculateRoute`; change it there and here together):
- [TURN-3] A recalculation (`TryRecalculateRoute`) never re-judges a Turn pick from the aircraft: one ahead keeps the loaded turn and tail (`TurnPickCarryOver.TailFrom`) or is said; one in the turn is missed unless on its taxiway (`MadeByCut`); neither is a no-op; a made junction stays blocked (`BlocksToKeep`); blocks change only on adoption. Full: docs/invariants/taxi-turn-picks.md#turn-3

Mirrored from taxi-position.md (it governs the off-route recalculation, the taxiway-change callout and the segment advances here, and leaves this partial out for the rule budget; change them there and here together):
- [RTE-6] Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons, which break on long segments. Full: docs/invariants/taxi-position.md#rte-6
- [RTE-7] Off-route auto-recalc must stay gated on the route-joined latch `_hasJoinedRoute`, or the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance. Full: docs/invariants/taxi-position.md#rte-7
- [RTE-27] The start grace window (`START_WARNING_CHATTER_GRACE_SEC`, 12.5 s) follows either start warning: turn, destination-ahead and curve callouts wait it out when safe (`StartWarningChatterGate`), the taxiway-change callout defers (`TaxiwayChangeGate`); never make any of them skip, never gate hold-short or runway-crossing callouts on it. Full: docs/invariants/taxi-position.md#rte-27
- [RTE-41] The hold-short countdown, the turn notices and "Slow for turn" measure ALONG THE ROUTE to the next stop or junction (`RouteAhead`), never to the current segment's end alone, latched per stop or junction, never cleared on a segment advance; the incursion guard silently latches a later-segment next stop within 600 ft, never skips it. Full: docs/invariants/taxi-position.md#rte-41
