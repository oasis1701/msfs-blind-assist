---
paths:
  - "MSFSBlindAssist/Services/TaxiSteeringTone.cs"
  - "MSFSBlindAssist/Navigation/GuidanceGeometry.cs"
  - "MSFSBlindAssist/Navigation/RunwayLineupTarget.cs"
  - "MSFSBlindAssist/Navigation/RouteStartTurnCue.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GuidanceGeometry*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Lineup*.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager*.cs"
---
# Taxi steering tone, lineup and turn cues rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/taxi-steering.md.

- [STR-1] `WAYPOINT_CAPTURE_RADIUS_M` (25m) must skip the last route segment, or it preempts the gate arrival radius and parking countdown. Full: docs/invariants/taxi-steering.md#str-1
- [STR-2] The taxi/lineup steering tone stays stereo-pan only: never add frequency or volume modulation (pulse mode's on/off volume toggle is the one deliberate exception). Full: docs/invariants/taxi-steering.md#str-2
- [STR-3] The taxiing tone target must be the continuous arc-length walk `GuidanceGeometry.WalkTarget`, never a turn/no-turn branch (one-frame pan-flips); clamp the walk start `t` at the upper bound only, while the target fraction `f` IS floored at 0. Full: docs/invariants/taxi-steering.md#str-3
- [STR-4] A sub-metre segment (`DEGENERATE_SEG_M` 1.0m) is a POINT: `WalkTarget`/`CumulativeTurnDeg` must SKIP it before projecting, never project onto it and never force `t = 1` for it, or the target slides or jumps ~25m. Full: docs/invariants/taxi-steering.md#str-4
- [STR-5] `AdvanceToNearestSegment` needs the pin-breaker `GuidanceGeometry.HasPassedOntoNextSegment` (30m cross-track) in BOTH un-advanceable cases, via `AdvanceSegment()`, and it must NEVER fire while the current segment is a hold-short (a silent pass is the incursion direction). Full: docs/invariants/taxi-steering.md#str-5
- [STR-6] "Straighten." must fire per sustained-yaw episode, never gated on per-junction `TurnAngleDegrees`: navdata splits real 90° turns into many small micro-bends. Full: docs/invariants/taxi-steering.md#str-6
- [STR-7] Runway lineup must use explicit thresholds (`UpdateHeadingErrorWithThresholds`, 0.5°/1°/15°), never the width-scaled tone overload, whose `MIN_SCALE` clamp leaves pilots 3° off heading with no audio cue. Full: docs/invariants/taxi-steering.md#str-7
- [STR-8] Lineup-aligned hysteresis is fixed literals in `UpdateLineup` (enter <1°/<10ft, exit >2°/>20ft); do not loosen it back toward the old 2°/5°–15ft/30ft deadband. Full: docs/invariants/taxi-steering.md#str-8
- [STR-9] Lineup pulse mode must key on BOTH heading error AND cross-track: intercept-angle saturation can read heading error as ~zero with huge cross-track, leaving the pilot no cue to move forward. Full: docs/invariants/taxi-steering.md#str-9
- [STR-10] Runway lineup steering must stay intercept-angle-based; never reintroduce a bearing-to-threshold blend, which sits on the ±180° wrap past the threshold and produces chaotic sign flips. Full: docs/invariants/taxi-steering.md#str-10
- [STR-11] Every `LiningUp` state entry must reset the heading-error smoother, or the taxi-phase low-pass residual leaks into the lineup tone and can steer the pilot off the runway. Full: docs/invariants/taxi-steering.md#str-11
- [STR-12] No feet-quantity verbal cues for spatial or cross-track guidance (a blind pilot has no reference for "42 feet left"); the tone is the cross-track instrument, heading numbers are fine. Full: docs/invariants/taxi-steering.md#str-12
- [STR-13] Any new public `TaxiGuidanceManager` method touching `_route`, `_state` or `_currentSegmentIndex` must acquire `_stateLock`. Full: docs/invariants/taxi-steering.md#str-13
- [STR-14] `TaxiSteeringTone` must reset `_pulseActive` in both `Start()` and `Stop()`, never trusting caller-side cleanup, or a leaked pulse state pulses the next route's taxiing tone at 3Hz. Full: docs/invariants/taxi-steering.md#str-14
- [STR-15] `TaxiSteeringTone` must refresh volume on every sounding frame, not only in pulse mode, or a pulse→continuous transition can leave the tone stuck at zero volume. Full: docs/invariants/taxi-steering.md#str-15
- [STR-16] Verbal turn direction must come from the aircraft's current heading (`ComputeTurnVerbalFromHeading`), never the route's static `TurnDirection`, which off-axis can contradict the correct tone. Full: docs/invariants/taxi-steering.md#str-16
- [STR-17] Runway-destination lineup must anchor on the `start` table (`GetRunwayStarts`), never `Runway.StartLat/StartLon`: that is the pavement edge, hundreds of metres off the lineup point at displaced thresholds. Full: docs/invariants/taxi-steering.md#str-17
- [STR-18] The route-start turn cue has ONE owner (`RouteStartTurnCue` via `ComposeInitialTurnCue`, from `LoadRoute` and the handoff RE-ANCHOR), never the first taxiing frame; delivered once via `ConsumeInitialTurnCue()`, angle from `ComputeSteeringHeadingError`, both sides true north (more: see full). Full: docs/invariants/taxi-steering.md#str-18
