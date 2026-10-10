---
paths:
  - "MSFSBlindAssist/SimConnect/SimConnectManager*.cs"
  - "MSFSBlindAssist/SimConnect/BridgeProbe.cs"
  - "MSFSBlindAssist/SimConnect/CalcPathVerdict.cs"
  - "MSFSBlindAssist/SimConnect/MobiFlightWasmModule.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.PanelBuilder.cs"
  - "MSFSBlindAssist/Forms/SimPerformanceForm.cs"
  - "MSFSBlindAssist/Services/SimPerformance/FrameRateMeter.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CalcPath*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*BridgeProbe*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CalcEventCode*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FrameRateMeter*.cs"
---
# SimConnect events and calc path rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/simconnect-events.md.

- [SIM-8] The calc-path probe MUST report its verdict: `CalcPathVerdict.LogLine` on both outcomes, `PilotWarning` spoken when an aircraft registering `MSFSBA_BRIDGE_PROBE` concludes UNVERIFIED (others conclude at once, logged, unspoken by design). MainForm's timer gates on that registration, never a type list. Never make the verdict silent again. Full: docs/invariants/simconnect-events.md#sim-8
- [SIM-9] The `MSFSBA_BRIDGE_PROBE` read-back lags its write by one round, so the match must accept the PREVIOUS nonce too (`SimConnect.BridgeProbe.IsEcho`); comparing only the current nonce means the probe never converges and `CalcPathVerified` stays false. Full: docs/invariants/simconnect-events.md#sim-9
- [SIM-10] A380 FCU events (`A32NX.FCU_*`) must NEVER wait on the calc-path probe (`SimConnectManager.IsFbwFcuEvent`), and that bypass must stay gated on `AircraftCode == "FBW_A380"`: the A32NX shares the names but keeps its working `TransmitClientEvent` fallback. Full: docs/invariants/simconnect-events.md#sim-10
- [SIM-11] `SetLVar`'s MobiFlight calc-path routing must gate on `CalcPathVerified`, never on bare `IsMobiFlightConnected`, which is true even with no WASM module installed. Full: docs/invariants/simconnect-events.md#sim-11
- [SIM-12] `SetLVar` keeps stock SimVar names (`TRANSPONDER STATE:1`) and every name with a space off the L:var calc path. Colon names take the data-def write too; for a colon-indexed add-on L:var (`B787_IRS_Knob_State:1`, [VAR-2]) that is unmeasured, not a rule: move it to the calc path only if an in-sim read-back shows that write reverting. Full: docs/invariants/simconnect-events.md#sim-12
- [SIM-13] H: events always go to the MobiFlight channel whenever `IsMobiFlightConnected`; dotted events must wait for `CalcPathVerified` (queued, bounded, flushed on verify or probe-conclude) — never fire one before the probe concludes, except [SIM-10]'s A380 `A32NX.FCU_*` events, which never wait. Full: docs/invariants/simconnect-events.md#sim-13
- [SIM-17] Every calc-path event string must be unique per call (`SimConnectManager.BuildCalcEventCode` prefixes `{seq} 0 *`): MobiFlight drops byte-identical consecutive commands, so a toggle could switch on and never off. The dedup keys on text, not elapsed time. Full: docs/invariants/simconnect-events.md#sim-17
- [SIM-18] The "Frame" system event is subscribed only while a consumer holds `StartFrameRateMonitoring` (the Sim Performance window), never unconditionally in `SetupEvents`: it costs one `ReceiveMessage` dispatch per rendered frame on the UI thread, app-wide, for a window nobody has open. Full: docs/invariants/simconnect-events.md#sim-18

Mirrored from md11.md and a32nx-fenix.md (they govern SimConnectManager.EventSend.cs, the bridge probe in MainForm.AircraftSwitch.cs and the panel's echo arm in MainForm.PanelBuilder.cs; change them there and here together):
- [MD11-16] Every MD-11 write path refuses ALOUD, before writing, when it cannot reach the aircraft: ask `CalcWriteCanLand` (calc bus) or `CanSendEvent` (stock event), never bare `CanExecuteCalculatorCode`; the probe check never moves into `ExecuteCalculatorCode` ([VAR-3]). Re-arm a NEGATIVE verdict on aircraft switch, keep a VERIFIED one. Success is silent. Full: docs/invariants/md11.md#md11-16
- [A320-38] Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, keys from `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`, `OnPanelButtonFiring`; the calc-code V/S set arms the same two directly); a queued dotted event re-arms when `FlushPendingCalcEvents` sends it (`QueuedEventDispatched`). Full: docs/invariants/a32nx-fenix.md#a320-38
