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
  - "tests/MSFSBlindAssist.Tests/**/*RouteThreat*.cs"
---
# Ground traffic and the runway watch rules

Loaded when Claude reads matching code. Background: docs/taxi-guidance.md. Full text of each rule: docs/invariants/ground-traffic.md.

- [TRF-1] `GroundTrafficSuppression` (pure, not a MainForm lambda) silences Caution/Warning on the takeoff roll, with no route and in a STILL-ROLLING rollout; the 120° arc never gates Awareness, Alt+G stays ungated; a fast landing exit filters speech, never mutes the monitor (more: see full). Full: docs/invariants/ground-traffic.md#trf-1
- [TRF-2] The departure-queue position counts only the CONTIGUOUS line (`GroundTrafficLogic.QueueAheadOf`, 250 m gap), and the wording is `ReadQueue`'s alone: "departure queue" only on a takeoff-runway route with its head within 150 m of the route end (more: see full). Full: docs/invariants/ground-traffic.md#trf-2
- [TRF-3] The runway watch never says "clear", only "no traffic seen"; it is keyed by `RunwayWatch.Key` (its reason's `IdentityKey`, else the sorted runway keys) so hold to takeoff wait is ONE watch, its first status is ALWAYS spoken, runway events queue while `Holding`/`Vacating`, and occupant and final records stay separate (more: see full). Full: docs/invariants/ground-traffic.md#trf-3
- [TRF-4] Ground-traffic callouts go through `TrafficSpeechPolicy.Plan` (Alt+G's summary speaks directly, [TRF-1]): only Warning, RunwayCritical and Caution interrupt, only a STRICTLY more urgent one cuts another within 3 s, a withheld interrupt is never latched or queued, latches commit in `TrafficCallout.OnEmitted` (more: see full). Full: docs/invariants/ground-traffic.md#trf-4
- [TRF-5] "... ahead is moving." comes from a LATCHED departure (`QueueMovementPolicy.Step`), nearest aircraft only; "Move up" (`EvaluateNudge`) speaks only on a joined route off runway pavement, below 1 kt, nothing else within 250 ft, at most 3 times (more: see full). Full: docs/invariants/ground-traffic.md#trf-5
- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
- [TRF-7] A runway CROSSING is ONE watch: a single-runway watch whose sources all end lingers (`ApplyLinger`, `RunwayWatchLinger`: same key, Holding mode; multi-runway watches never linger) until clear on the far side, 30 m back out on its own side, or 60 s; never restore the stop-and-restart that spoke a second full status mid-crossing. Full: docs/invariants/ground-traffic.md#trf-7
- [TRF-8] The runway-watch gate closing SUSPENDS the watch (`SuspendWatch`), never ends it: the same key back within `RunwayWatchScopes.WatchResumeGraceMs` (15 s) resumes it with no new first status, re-arming only `_watchStartedUtc` to the resume; another watch adopted, an airport or database change, or the grace lapsing ends it. Full: docs/invariants/ground-traffic.md#trf-8
- [TRF-9] Known runway traffic is recorded by `WatchedRunway.Key`, never the spoken designator; `EvaluateRunwayWatch` purges an id whose runway left the scan (`IdsOutOfScope`) SILENTLY, only AFTER the scan loop has refreshed every seen id's key and before `ForgetAbsent`, and a purged id never feeds `EmptiedRunwayKeys`. Full: docs/invariants/ground-traffic.md#trf-9
- [TRF-10] The first-status re-arm (`ShouldRearmOnModeChange`) fires ONCE per watch, critical-only; from Holding its 10 s window runs to the hold's RELEASE (`_holdReleasedUtc`, set as a crossing linger begins, dropped once a watch is adopted with no linger), never to the change to OnRunway; a re-armed status completed silently marks nothing known. Full: docs/invariants/ground-traffic.md#trf-10
- [TRF-11] Every `GroundTrafficMonitor` rule reads the injected `_utcNow`, never `DateTime.UtcNow`, or the headless harness's simulated seconds hide defects; its fake sweep answers only after `RequestGroundTrafficData` returns. A completion counts only under the outstanding `_sweepRequestId`, and at most one sweep is outstanding. Full: docs/invariants/ground-traffic.md#trf-11
