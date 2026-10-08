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

Mirrored from landing-rollout.md (they govern exit and tolerance code in TaxiGraph.cs and ExitBranch.cs; change them there and here together):
- [ROL-11] The implicit-exit shallow-angle override needs BOTH guards together (apron forward-direction AND `apronAngle > currentAngleFwd`); dropping either regresses the exit bearing. Full: docs/invariants/landing-rollout.md#rol-11
- [ROL-19] Derived-constant tripwire: re-derive all five (`VacatedShortAlongTrackFeet`, `EarlyVacateMaxPassedFeet`, `HandoffReachDefaultHalfWidthM`, `RunwayClearMarginM`, `DefaultRunwayWidthFeet`; three hang on `HandoffReachMarginM`) before any runway/rollout tolerance change, say so in the commit, re-run `tools/LandingExitSweep` (more: see full). Full: docs/invariants/landing-rollout.md#rol-19
