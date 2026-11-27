---
paths:
  - "MSFSBlindAssist/Navigation/HoldShortAbeam.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs"
  - "tests/MSFSBlindAssist.Tests/HoldShortAbeamTests.cs"
---
# Hold-short passed beside its node rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/hold-abeam.md.

- [HLD-24] A hold-short passed BESIDE its node latches through `HoldShortAbeam.IsReached`, measured by `TryMeasureHoldAbeam`, never by `AlongTrackToSegmentEnd` on the hold segment alone (a sub-metre segment reads level from anywhere); never on the final segment, whose hold is `HandleArrival`'s; fly a VirtualPilot A/B before changing a bound. Full: docs/invariants/hold-abeam.md#hld-24
- [HLD-25] The abeam capture never takes an aircraft `HoldShortAbeam.IsRollingOnBehindTheLine`: over 10 m farther from the hold's runway than the node (`MetresBehindHoldLine`) AND the tone at full pan, the turn onto the connector missed; never one test alone (KCLE latches at full pan, VHHH 21 m behind); fly VirtualPilot missturn before changing either. Full: docs/invariants/hold-abeam.md#hld-25
