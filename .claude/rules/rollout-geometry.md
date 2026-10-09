---
paths:
  - "MSFSBlindAssist/Navigation/RolloutExitGate.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Navigation/TaxiGraph.cs"
  - "MSFSBlindAssist/Navigation/ExitBranch.cs"
  - "MSFSBlindAssist/Navigation/LandingExitReplan.cs"
  - "MSFSBlindAssist/Navigation/PavementMap.cs"
  - "MSFSBlindAssist/Navigation/RunwayShape.cs"
  - "MSFSBlindAssist/Navigation/HoldShortNodeResolver.cs"
  - "MSFSBlindAssist/Navigation/RouteRunwayCrossings.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RolloutExitGate*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*EarlyVacateAlongTrack*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RolloutLateralClearance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ExitBranch*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayShape*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayHoldPlacement*.cs"
---
# Rollout geometry: the implicit-exit override and the tolerance tripwire rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/rollout-geometry.md.

- [ROL-11] The implicit-exit shallow-angle override needs BOTH guards together (apron forward-direction AND `apronAngle > currentAngleFwd`); dropping either regresses the exit bearing. Full: docs/invariants/rollout-geometry.md#rol-11
- [ROL-19] Derived-constant tripwire: re-derive all five (`VacatedShortAlongTrackFeet`, `EarlyVacateMaxPassedFeet`, `HandoffReachDefaultHalfWidthM`, `RunwayClearMarginM`, `DefaultRunwayWidthFeet`; three hang on `HandoffReachMarginM`) before any runway/rollout tolerance change, say so in the commit, re-run `tools/LandingExitSweep` (more: see full). Full: docs/invariants/rollout-geometry.md#rol-19
