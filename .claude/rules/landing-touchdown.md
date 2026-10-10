---
paths:
  - "MSFSBlindAssist/Services/LandingFlareAssistManager.cs"
  - "MSFSBlindAssist/Services/LandingGuidanceLaws.cs"
  - "MSFSBlindAssist/Forms/DestinationRunwayForm.cs"
  - "MSFSBlindAssist/Services/LandingExitGoAround.cs"
  - "MSFSBlindAssist/Navigation/TouchdownCallout.cs"
  - "MSFSBlindAssist/Navigation/RolloutCalloutSupersession.cs"
  - "MSFSBlindAssist/Navigation/RolloutRunwayReCrossing.cs"
  - "MSFSBlindAssist/Navigation/RunwayEndCountdownGate.cs"
  - "MSFSBlindAssist/Navigation/RetargetCallout.cs"
  - "MSFSBlindAssist/Navigation/OffPavementAlert.cs"
  - "MSFSBlindAssist/Navigation/PavementMap.cs"
  - "MSFSBlindAssist/Navigation/PavementTolerance.cs"
  - "MSFSBlindAssist/Navigation/RunwayVacateResolver.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingFlare*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LandingGuidanceLaw*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TouchdownCallout*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Rollout*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*EarlyVacateAlongTrack*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OffPavement*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayEndCountdown*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RetargetCallout*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*OvershootRetarget*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PavementMap*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*PavementTolerance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayVacateResolver*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*KmemLanding*.cs"
---
# Landing assist, touchdown and go-around rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/landing-touchdown.md.

- [ROL-1] The landing assist hands its rollout tone to taxi guidance SILENTLY (`LandingFlareAssistManager.StepTaxiHandover`), never by widening `IsLandingExitTaxiSteering` to backtracking; its own end is QUEUED when taxi guidance ran, so it never clips a taxi sentence. Full: docs/invariants/landing-touchdown.md#rol-1
- [ROL-7] A go-around or touch-and-go ENDS landing-exit guidance and keeps the plan (`LandingExitGoAround`): held only while KNOWN airborne, decided after `ConfirmMs` by a FRESH position read (never the 1 Hz cache), then ONE sentence. Full: docs/invariants/landing-touchdown.md#rol-7

Mirrored from landing-exits.md (they govern the touchdown callout, the landing assist's runway switch and the exit-angle reads; change them there and here together). This file globs every landing-rollout.md file that landing-exits.md does not, except TaxiGuidanceManager.cs (taxi-manager-call-sites.md holds its copies), so each loads once:
- [EXIT-10] A touchdown runway correction is ONE `AnnounceInstruction` (`TouchdownCallout`): first retire every milestone due within the MEASURED `ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC` (re-measure when the wording changes), reached by `RolloutCalloutSupersession.ReachFeet` braking to taxi speed; turn-now only when already inside. Full: docs/invariants/landing-exits.md#exit-10
- [EXIT-12] The manual landing assist points its tones at the runway actually landed on for ONE engagement (`LandingAssistRunwaySwitch`) and restores the ARMED runway in `StopEngagement`; never let the switch outlive the engagement. Full: docs/invariants/landing-exits.md#exit-12
- [EXIT-13] The overshoot margin and the alignment handoff read how the exit leaves its OWN node (`LandingExit.DivergenceAngleDegrees` into `RolloutExitGate.OvershootMarginFor`/`IsAlignedWithExit`), never `ExitAngleDegrees`, the branch's sharpest turn (EDDB 24L M3: 100 ft for 291). Full: docs/invariants/landing-exits.md#exit-13
