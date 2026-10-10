---
paths:
  - "MSFSBlindAssist/Forms/TaxiAssistForm.cs"
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
  - "MSFSBlindAssist/Forms/LandingExitForm.cs"
  - "MSFSBlindAssist/Services/LandingExitPlanner*.cs"
---
# Taxi guidance rules for the taxi forms and MainForm

MIRRORS: copied word for word from their areas' rule files, whose globs leave these files out (TaxiGuidanceManager.cs and its Routing partial load them there); change both together (ClaudeContextBudgetTests checks).

- [HLD-4] Never disable auto-inserted runway-crossing hold-shorts: `ApplyAutoHoldShortPasses` runs only from `AdoptRoute`, the one place a route is adopted; never place a stop on any runway's pavement, and a `StartGuidance` caller that stays in `Taxiing` or `HoldShort` must speak `ConsumeStartHoldCue()` (more: see full). Full: docs/invariants/runway-holds.md#hld-4
- [STR-18] The route-start turn cue has ONE owner (`RouteStartTurnCue`), composed via `ComposeInitialTurnCue` from `LoadRoute` and the handoff RE-ANCHOR, never on the first taxiing frame; delivered once via `ConsumeInitialTurnCue()`, the per-frame one-shot included, angle from `ComputeSteeringHeadingError`, both sides true north (more: see full). Full: docs/invariants/taxi-steering.md#str-18
- [RTE-4] Take-off assist's under-aircraft runway probe (`TryDetectRunwayUnderAircraft` -> `TaxiGraph.TryGetRunwayAtPosition`) must use a strict tolerance, `RunwayShape`'s own half-width with no +5m fudge, and stay gated on `_lastOnGround`; Where-Am-I's `DescribeLocation` keeps its +5 m on purpose. Full: docs/invariants/taxi-position.md#rte-4
- [RTE-6] Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons, which break on long segments. Full: docs/invariants/taxi-position.md#rte-6
- [RTE-7] Off-route auto-recalc must stay gated on the route-joined latch `_hasJoinedRoute`, or the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance. Full: docs/invariants/taxi-position.md#rte-7
- [RTE-11] "Where Am I" is ground-only by design (gated on `_lastOnGround`); runway detection must use `TaxiGraph.RunwayCenterlines`, never rely on `taxi_path.type='R'` edges (the DB has none). Full: docs/invariants/taxi-position.md#rte-11
- [RTE-12] `TaxiGraph.DescribeLocation` runs on pool threads: pool-reachable queries take `_structureLock` inside TaxiGraph, its lazy index is read only under it, no post-Build mutation skips it; never widen the runway-start reach without the owner, nor drop the stand's near-runway gate (pavement, runway start, `IsNavdataHoldShort`) (more: see full). Full: docs/invariants/taxi-position.md#rte-12
- [RTE-27] The start grace window (`START_WARNING_CHATTER_GRACE_SEC`, 12.5 s) follows either start warning: turn, destination-ahead and curve callouts wait it out when safe (`StartWarningChatterGate`), the taxiway-change callout defers (`TaxiwayChangeGate`); never make any of them skip, never gate hold-short or runway-crossing callouts on it. Full: docs/invariants/taxi-position.md#rte-27
