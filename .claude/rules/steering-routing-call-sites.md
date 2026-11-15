---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs"
---
# Taxi steering rules for the routing partial

MIRRORS: copied word for word from taxi-steering.md, which leaves this partial out for the rule budget: these are its rules whose code `TaxiGuidanceManager.Routing.cs` declares (`AdvanceToNearestSegment`, `LoadRoute`, `RunwaysOnEntryStub`). Change a rule there and here together; ClaudeContextBudgetTests fails if the two differ.

- [STR-5] `AdvanceToNearestSegment` needs the pin-breaker `GuidanceGeometry.HasPassedOntoNextSegment` (30m cross-track) in BOTH un-advanceable cases, via `AdvanceSegment()`, and it must NEVER fire while the current segment is a hold-short (a silent pass is the incursion direction). Full: docs/invariants/taxi-steering.md#str-5
- [STR-18] The route-start turn cue has ONE owner (`RouteStartTurnCue`), composed via `ComposeInitialTurnCue` from `LoadRoute` and the handoff RE-ANCHOR, never on the first taxiing frame; delivered once via `ConsumeInitialTurnCue()`, the per-frame one-shot included, angle from `ComputeSteeringHeadingError`, both sides true north (more: see full). Full: docs/invariants/taxi-steering.md#str-18
- [STR-20] An entry stub ending on the destination centreline up to `ENTRY_STUB_BEYOND_END_M` (100 m) past the recorded end still reaches the runway (`RunwaysOnEntryStub` fallback); never widen its lateral bound past the runway's half-width. Full: docs/invariants/taxi-steering.md#str-20
- [STR-21] `AdvanceToNearestSegment` must not skip past a SHARP junction early (the `ADVANCE_*` hairpin guard, covering every segment past the junction); each gate is measured, so re-run a VirtualPilot A/B before touching one. Full: docs/invariants/taxi-steering.md#str-21

Mirrored from taxi-turn-picks.md (it governs `TryRecalculateRoute`; change it there and here together):
- [TURN-3] A recalculation (`TryRecalculateRoute`) never re-judges a Turn pick from the aircraft: one ahead keeps the loaded turn and tail (`TurnPickCarryOver.TailFrom`) or is said; one in the turn is missed; neither is a no-op; a made junction stays blocked (`BlocksToKeep` drops none); blocks change only on adoption. Full: docs/invariants/taxi-turn-picks.md#turn-3
