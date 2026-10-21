---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
---
# Steering and rollout exit-turn rules for TaxiGuidanceManager.cs

MIRRORS: copied word for word from taxi-steering.md and rollout-exit-turn.md, which leave this main partial out for the rule budget: these are their rules whose code it declares or runs (the steering fields and the turn cue, `exitCrossTrackPull`, the miss hold-off calls). Change a rule there and here together; ClaudeContextBudgetTests fails if the copies differ.

- [STR-1] `WAYPOINT_CAPTURE_RADIUS_M` (25m) must skip the last route segment, or it preempts the gate arrival radius and parking countdown. Full: docs/invariants/taxi-steering.md#str-1
- [STR-13] Any new public `TaxiGuidanceManager` method touching `_route`, `_state` or `_currentSegmentIndex` must acquire `_stateLock`. Full: docs/invariants/taxi-steering.md#str-13
- [STR-18] The route-start turn cue has ONE owner (`RouteStartTurnCue`), composed via `ComposeInitialTurnCue` from `LoadRoute` and the handoff RE-ANCHOR, never on the first taxiing frame; delivered once via `ConsumeInitialTurnCue()`, the per-frame one-shot included, angle from `ComputeSteeringHeadingError`, both sides true north (more: see full). Full: docs/invariants/taxi-steering.md#str-18
- [STR-22] Everything taxi guidance says in one position frame is ONE utterance (`SpeakNow` → `ComposeFrameUtterance`, flushed by `UpdatePosition`), safety lines first; never call `_announcer.AnnounceImmediate` directly inside the manager. Full: docs/invariants/taxi-steering.md#str-22
- [STR-23] While the opening turn is pending (`_initialTurnPending`) the tone keeps to the side the opening cue SAID (`_initialTurnSign`, capped at 179°), never the short way round, which flips between frames near a reversal. Full: docs/invariants/taxi-steering.md#str-23
- [TKO-2] Auto-activate-Takeoff-Assist-on-lineup is a one-shot latch (`_autoActivateFired`) that must NOT reset on lineup drift-out; re-engaging after a deliberate manual deactivation would surprise the pilot. Full: docs/invariants/takeoff-and-callouts.md#tko-2
- [ROL-35] Neither missed-exit detector calls a miss while the aircraft is turning onto the exit's own path (`LandingExitPathFollow.HoldsOffMiss`, fed by `GetRolloutExitPath`); keep every gate, and never make it position-only. Full: docs/invariants/rollout-exit-turn.md#rol-35
- [ROL-36] Both missed-exit detectors wait out the exit's on-centreline stub (`OnAxisRunMetres`; never widen the 6 m band), hold off within 15 m of the exit's path (`StillOnExitPath`) and share ONE margin (`MissMarginFeet()`), never stub + divergence summed. Full: docs/invariants/rollout-exit-turn.md#rol-36
- [ROL-40] Both post-handoff tone sources carry the bounded cross-track pull (`exitCrossTrackPull`: 1°/m, ±20°, runway-aligned segment only, never into a sharp turn or on a turn-back exit); without it a wide, parallel aircraft reads zero error. Full: docs/invariants/rollout-exit-turn.md#rol-40
