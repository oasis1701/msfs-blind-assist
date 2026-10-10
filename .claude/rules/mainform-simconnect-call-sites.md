---
paths:
  - "MSFSBlindAssist/MainForm.cs"
---
# SimConnect manager and bridge probe rules for MainForm.cs

MIRRORS: copied word for word from simconnect-data.md and simconnect-events.md, whose globs leave MainForm.cs out; it builds the manager on the UI thread and declares and starts the bridge probe. Change both together (ClaudeContextBudgetTests checks).

- [SIM-5] `SimConnectManager.RequestVariable` must stay safe from ANY thread: off the UI thread it POSTS itself there (`UiThreadGate`; a post refused at shutdown is dropped, never run inline). Never read `continuousVariableIndexMap` or issue `RequestDataOnSimObject` from a pool thread directly. Full: docs/invariants/simconnect-data.md#sim-5
- [SIM-8] The calc-path probe MUST report its verdict: `CalcPathVerdict.LogLine` on both outcomes, `PilotWarning` spoken when an aircraft registering `MSFSBA_BRIDGE_PROBE` concludes UNVERIFIED (others conclude at once, logged, unspoken by design). MainForm's timer gates on that registration, never a type list. Never make the verdict silent again. Full: docs/invariants/simconnect-events.md#sim-8
- [SIM-9] The `MSFSBA_BRIDGE_PROBE` read-back lags its write by one round, so the match must accept the PREVIOUS nonce too (`SimConnect.BridgeProbe.IsEcho`); comparing only the current nonce means the probe never converges and `CalcPathVerified` stays false. Full: docs/invariants/simconnect-events.md#sim-9
