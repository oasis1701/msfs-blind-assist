---
paths:
  - "MSFSBlindAssist/Navigation/ExitBranch.cs"
  - "MSFSBlindAssist/Navigation/LandingExit*.cs"
  - "MSFSBlindAssist/Navigation/LandingRunwayMatch.cs"
  - "MSFSBlindAssist/Navigation/RolloutExitGate.cs"
  - "MSFSBlindAssist/Navigation/LandingAssistRunwaySwitch.cs"
  - "MSFSBlindAssist/Navigation/RunwayFrame.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.ExitRefinement.cs"
  - "MSFSBlindAssist/Services/LandingExitPlanner*.cs"
  - "MSFSBlindAssist/Forms/LandingExitForm.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingExit*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ExitBranch*.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingRunwayMatch*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ExitRelativeBearing*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingAssistRunwaySwitch*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayFrame*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*EarlyVacateExitMatcher*.cs"
  - "MSFSBlindAssist/Services/DistanceMilestones.cs"
  - "MSFSBlindAssist/Navigation/ExitOwnSide.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ExitOwnSide*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ExitTurnBack*.cs"
---
# Landing exits: measurement, planner and re-plan rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/landing-exits.md.

- [EXIT-1] The Landing Exit Planner's `SIM_ON_GROUND` handler must use `RequestAircraftPositionAsync`, never `LastKnownPosition`: that cache can be stale from a prior mode and silently fails the GS≥40 kt real-landing gate. Full: docs/invariants/landing-exits.md#exit-1
- [EXIT-2] `SetExit(..., currentlyAirborne)` must be armed from the actual air/ground state, never unconditionally true, or the exit plan false-triggers during a high-speed taxi or a rejected takeoff. Full: docs/invariants/landing-exits.md#exit-2
- [EXIT-3] Match the plan's runway to the one landed on (`LandingRunwayMatch`: by alignment among landable ends containing the aircraft, never pavement alone) before it frames the rollout; on another runway or the other end re-plan (`LandingExitReplan`); `Unknown` never hands over or consumes the plan (more: see full). Full: docs/invariants/landing-exits.md#exit-3
- [EXIT-4] On overshoot never answer "any exit left?" from the lossy `_rolloutAllExits` alone: ask `TaxiGraph.FindDownfieldExits` before "Missed last exit", merge via `RolloutExitGate.MergeRescueExits`, never offer an unreachable site (`IsRescueCandidateSite`) (more: see full). Full: docs/invariants/landing-exits.md#exit-4
- [EXIT-5] Log the exit LIST itself, not just its count, at `BeginLandingRollout` and at the no-exit verdict (`DescribeExits`), or a wrong verdict cannot be traced to the list or the scan. Full: docs/invariants/landing-exits.md#exit-5
- [EXIT-6] Measure every exit by its whole branch (`ExitBranch`, via `TaxiGraph.RefineExitByBranch`), never the first named edge; refinement never relocates an exit or changes WHICH exits exist, its one move is a turnaround's forward-sibling swap; re-run `tools/LandingExitSweep` (more: see full). Full: docs/invariants/landing-exits.md#exit-6
- [EXIT-7] The planner never pre-selects a turnaround while a forward exit exists (`LandingExitDefault`); the online name refresh restores the pick by node, else the nearest same-named exit of the same kind (`RestoreIndex`), never the name's first entry. Full: docs/invariants/landing-exits.md#exit-7
- [EXIT-8] `LandingExitPlanner.ReplanOnActualRunway` measures exits on the runway landed on (`GetLandingExits(actual)`), `LandingExitVacateScreen.Mark`s each list before `LandingExitReplan.ChooseExit`, tries Comfortable, then the rescue scan, then the `ExitLeadFeet` floor, then the countdown; the planned exit is passed only for `ReciprocalEnd`. Full: docs/invariants/landing-exits.md#exit-8
- [EXIT-9] `BeginRunwayEndCountdownRollout` must install the graph, provider and ICAO and START the tone (no LoadRoute ran); of rollout entries only it and `BeginLandingRolloutNoGraph` raise `PositionStreamRequired` (clearing-phase entries too, after `SetState`); distance to the end is `RunwayFrame.DistanceToEnd`, never raw `Runway.Length`. Full: docs/invariants/landing-exits.md#exit-9
- [EXIT-10] A touchdown runway correction is ONE `AnnounceInstruction` (`TouchdownCallout`): first retire every milestone due within the MEASURED `ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC` (re-measure when the wording changes), reached by `RolloutCalloutSupersession.ReachFeet` braking to taxi speed; turn-now only when already inside. Full: docs/invariants/landing-exits.md#exit-10
- [EXIT-11] A database switch must clear the landing-exit plan AND disarm the manual landing assist (`RefreshDatabaseProvider`: `landingExitPlanner.Clear()`, `flareAssistManager.Disarm`): both hold a runway list the new database may name or place differently. Full: docs/invariants/landing-exits.md#exit-11
- [EXIT-12] The manual landing assist points its tones at the runway actually landed on for ONE engagement (`LandingAssistRunwaySwitch`) and restores the ARMED runway in `StopEngagement`; never let the switch outlive the engagement. Full: docs/invariants/landing-exits.md#exit-12
- [EXIT-13] The overshoot margin and the alignment handoff read how the exit leaves its OWN node (`LandingExit.DivergenceAngleDegrees` into `RolloutExitGate.OvershootMarginFor`/`IsAlignedWithExit`), never `ExitAngleDegrees`, the branch's sharpest turn (EDDB 24L M3: 100 ft for 291). Full: docs/invariants/landing-exits.md#exit-13
- [EXIT-14] The own-node reading (`ExitBranch.FromExitNode`, `ForwardOnlyFromItsNode`) is `RefineExitByBranch`'s last resort, after the sibling swap; it never reaches the hold-short gate (`hasHoldShortOnRunway`) or the sibling decision, and a flagged exit never displaces an unflagged one of its name in a dedup (`ReplacesInDedupWindow`). Full: docs/invariants/landing-exits.md#exit-14
- [EXIT-15] `hsOnlyEnds` judges the PRODUCER's types (`producerExitTypes`) over the REFINED, de-duplicated list, deliberately: on the producer's own list 16 directions fall back to hold-short mode and lose 54 exits (KDTW 09L 11 -> 2); change it only with a `tools/LandingExitSweep` showing pilots gain. Full: docs/invariants/landing-exits.md#exit-15
- [EXIT-16] The landing-exit list collapses same-turnoff duplicates for DISPLAY ONLY (`LandingExitDisplayList.Collapse` in `LandingExitForm`); never move the collapse into `GetLandingExits`, whose siblings the rollout reads through `_rolloutAllExits`. Full: docs/invariants/landing-exits.md#exit-16
- [EXIT-17] "(sharp turn back)" comes from `LandingExitDestination.RequiresTurnBack` → `PathTurnsBack` (any >120° leg or turn in the path's first 150 m), never the first leg alone; `CorrectBackwardsStart` deliberately keeps the first-leg test. Full: docs/invariants/landing-exits.md#exit-17
- [EXIT-18] A landing-exit route starts on the exit's OWN SIDE of the landed runway (`ExitOwnSide`, via `LoadRoute`'s anchor and its backwards-start retry, `ExitOwnSideFilter`); the re-crossing guard is a backstop, not the fix; with no qualifying node the unfiltered pick stands. Full: docs/invariants/landing-exits.md#exit-18
- [EXIT-19] Hold-short mode ADDS the turnoffs its markers never drew (`GetLandingExits`: windowed, with no exit of their name within `EXIT_COVERAGE_GAP_FT`) to a marked list SETTLED FIRST, gap fill and all; never merge them in (moved 393, dropped 89; KJFK 13L offered V alone). Sweep fs2024 AND fs2020 with `tools/LandingExitSweep`: additions only. Full: docs/invariants/landing-exits.md#exit-19
