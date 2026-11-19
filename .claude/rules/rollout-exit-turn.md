---
paths:
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs"
  - "MSFSBlindAssist/Navigation/LandingExitPathFollow.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExitPathFollow*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*StillOnExitPath*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ExitToneBearingAgreement*.cs"
---
# Rollout exit turn, hand-off and missed-exit rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md, docs/virtual-pilot.md. Full text of each rule: docs/invariants/rollout-exit-turn.md.

- [ROL-35] Neither missed-exit detector calls a miss while the aircraft is turning onto the exit's own path (`LandingExitPathFollow.HoldsOffMiss`, fed by `GetRolloutExitPath`); keep every gate, and never make it position-only. Full: docs/invariants/rollout-exit-turn.md#rol-35
- [ROL-36] Both missed-exit detectors wait out the exit's on-centreline stub (`OnAxisRunMetres`; never widen the 6 m band), hold off within 15 m of the exit's path (`StillOnExitPath`) and share ONE margin (`MissMarginFeet()`), never stub + divergence summed. Full: docs/invariants/rollout-exit-turn.md#rol-36
- [ROL-37] `exitedLaterally`'s heading branch (≥ 8°) is bounded to `ROLLOUT_NEAR_EXIT_FT` unless on mapped taxi pavement (`IsOnTaxiPavement`); the handoff re-route anchors on the exit's taxiway only within `ROLLOUT_STOPPED_HANDOFF_MAX_DIST_FT`. Full: docs/invariants/rollout-exit-turn.md#rol-37
- [ROL-38] The turn-now trigger (`ROLLOUT_TURN_NOW_FT`, 150 ft) stays GEOMETRIC, never speed-scaled (it also swings the Normal-exit tone); the speed-dependent lead is the separate preparatory call (`_rolloutTurnPrepAnnounced`, 11 ft/kt, capped 400 ft). Full: docs/invariants/rollout-exit-turn.md#rol-38
- [ROL-39] The Normal-exit speed handoff (`speedNearExitHandoff`) waits for the turn-now LATCH (`_rolloutTurnNowAnnounced`), never `nearExit` (500 ft) and never a 150 ft distance test, or the tone hard-pans at the exit segment with no verbal. Full: docs/invariants/rollout-exit-turn.md#rol-39
- [ROL-40] Both post-handoff tone sources carry the bounded cross-track pull (`exitCrossTrackPull`: 1°/m, ±20°, runway-aligned segment only, never into a sharp turn or on a turn-back exit); without it a wide, parallel aircraft reads zero error. Full: docs/invariants/rollout-exit-turn.md#rol-40
- [ROL-41] Never derive the spoken exit turn direction from the bearing aircraft→junction: use `ResolveExitTurnDirection` (junction→`ApronNodeId`, then `ExitBearingTrue`, each ≥ 10° to be spoken); below that DROP the direction word, never guess. Full: docs/invariants/rollout-exit-turn.md#rol-41
- [ROL-42] The rollout tone never pulls while the right action is "go straight": before a Normal exit's turn-now its desired heading is RUNWAY HEADING, then `ExitBearingTrue`; High-speed and End exits keep bearing-to-junction. Full: docs/invariants/rollout-exit-turn.md#rol-42
