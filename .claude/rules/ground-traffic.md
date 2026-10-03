---
paths:
  - "MSFSBlindAssist/Services/GroundTraffic*.cs"
  - "MSFSBlindAssist/Services/TrafficSpeechPolicy.cs"
  - "MSFSBlindAssist/Services/QueueMovementPolicy.cs"
  - "MSFSBlindAssist/Services/RunwayWatch*.cs"
  - "MSFSBlindAssist/Services/TaxiGuidanceManager.TrafficContext.cs"
  - "MSFSBlindAssist/Navigation/HeldRunwayLabel.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GroundTraffic*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayWatch*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Queue*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TrafficSpeech*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TrafficMotion*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ProximityEscalation*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*MovingAwayByMotion*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*RunwayTrafficClassification*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*HeldRunwayLabel*.cs"
---
# Ground traffic and the runway watch rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/ground-traffic.md.

- [TRF-1] `GroundTrafficSuppression` (pure, not a MainForm lambda) silences Caution/Warning on the takeoff roll, with no route and in a STILL-ROLLING rollout; the 120° arc never gates Awareness, Alt+G stays ungated; a fast landing exit filters speech, never mutes the monitor (more: see full). Full: docs/invariants/ground-traffic.md#trf-1
- [TRF-2] The departure-queue position counts only the CONTIGUOUS line (`GroundTrafficLogic.QueueAheadOf`, 250 m gap), and the wording is `ReadQueue`'s alone: "departure queue" only on a takeoff-runway route with its head within 150 m of the route end (more: see full). Full: docs/invariants/ground-traffic.md#trf-2
- [TRF-3] The runway watch never says "clear", only "no traffic seen"; it is keyed by runway (`RunwayWatch.IdentityKey`) so hold to takeoff wait is ONE watch, its first status is ALWAYS spoken, runway events queue while `Holding`/`Vacating`, and occupant and final records stay separate (more: see full). Full: docs/invariants/ground-traffic.md#trf-3
- [TRF-4] All ground-traffic speech goes through `TrafficSpeechPolicy.Plan`: only Warning, RunwayCritical and Caution interrupt, only a STRICTLY more urgent one cuts another within 3 s, a withheld interrupt is never latched or queued, latches commit in `TrafficCallout.OnEmitted` (more: see full). Full: docs/invariants/ground-traffic.md#trf-4
- [TRF-5] "... ahead is moving." comes from a LATCHED departure (`QueueMovementPolicy.Step`), nearest aircraft only; "Move up" (`EvaluateNudge`) speaks only on a joined route off runway pavement, below 1 kt, nothing else within 250 ft, at most 3 times (more: see full). Full: docs/invariants/ground-traffic.md#trf-5
