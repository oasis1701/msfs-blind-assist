---
paths:
  - "MSFSBlindAssist/MainForm.cs"
---
# Rules whose code MainForm.cs holds

MIRRORS: copied word for word from simconnect-data.md, simconnect-events.md, ground-traffic.md and surroundings.md, whose globs leave MainForm.cs out; it builds the manager on the UI thread, declares and starts the bridge probe, and wires the ground-traffic route context and the surroundings runway probe. Change both together (ClaudeContextBudgetTests checks).

- [SIM-5] `SimConnectManager.RequestVariable` must stay safe from ANY thread: off the UI thread it POSTS itself there (`UiThreadGate`; a post refused at shutdown is dropped, never run inline). Never read `continuousVariableIndexMap` or issue `RequestDataOnSimObject` from a pool thread directly. Full: docs/invariants/simconnect-data.md#sim-5
- [SIM-8] The calc-path probe MUST report its verdict: `CalcPathVerdict.LogLine` on both outcomes, `PilotWarning` spoken when an aircraft registering `MSFSBA_BRIDGE_PROBE` concludes UNVERIFIED (others conclude at once, logged, unspoken by design). MainForm's timer gates on that registration, never a type list. Never make the verdict silent again. Full: docs/invariants/simconnect-events.md#sim-8
- [SIM-9] The `MSFSBA_BRIDGE_PROBE` read-back lags its write by one round, so the match must accept the PREVIOUS nonce too (`SimConnect.BridgeProbe.IsEcho`); comparing only the current nonce means the probe never converges and `CalcPathVerified` stays false. Full: docs/invariants/simconnect-events.md#sim-9
- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6
- [SUR-24] The runway probe (`TaxiGuidanceManager.IsOnRunwayPavement`) answers only from geometry in hand and NEVER builds a graph; its warm-up reads runway rows only (`PrepareRunwayShapeWarmUp`, never `GetTaxiPaths`); `_graphGeneration` is stamped only when a NEW `_graph` instance is installed, never when a re-route hands the same graph back. Full: docs/invariants/surroundings.md#sur-24

Mirrored from waypoint-flight-director.md (they govern MainForm.cs; change them there and here together):
- [WFD-4] FD and Visual Guidance are MUTUALLY EXCLUSIVE and share the ref-counted 505 stream: each Releases only what it Acquired (`_vgHoldsStream`/`_fdHoldsStream`) and Resumes only the HandFly suppression it took. Full: docs/invariants/waypoint-flight-director.md#wfd-4
- [WFD-11] The slip cue's ball SIDE is UNVERIFIED in-sim: the convention lives in the single `MainForm.SlipCueBallSign` const, and code and docs say it is unconfirmed until someone has flown it. Full: docs/invariants/waypoint-flight-director.md#wfd-11
