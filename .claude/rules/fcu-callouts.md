---
paths:
  - "MSFSBlindAssist/Aircraft/Fcu*.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.Displays.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.HotkeysAndMotion.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.PanelControls.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.SimVarUpdate.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.UiVariableSet.cs"
  - "MSFSBlindAssist/Forms/FBWA320/**"
  - "MSFSBlindAssist/Forms/FBWA380/FBWA380*Window*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Fcu*.cs"
---
# FCU dial callout rules (A32NX, A330, A380)

Loaded when Claude reads matching code. Background: docs/a32nx.md. Full text of each rule: docs/invariants/fcu-callouts.md.

- [A320-22] FCU dial callouts (A32NX, Headwind A330, A380) listen only to sources that say themselves whether the window shows a selection, never the `A32NX_FCU_AFS_DISPLAY_*_VALUE` values; changes are STAGED, released at batch end only while the FCU is available. (more: see full) Full: docs/invariants/fcu-callouts.md#a320-22
- [A320-36] FCU V/S and FPA callouts read the ARINC words (`FcuSources`: A32NX `A32NX_FCU_SELECTED_{VERTICAL_SPEED,FPA}`, A380 `A32NX_PRIM_1_SELECTED_*`), never the `A32NX_AUTOPILOT_{VS,FPA}_SELECTED` shims: unlike the heading/speed shims they never read -1 when dashed (live value on the A32NX, 0 on the A380). Full: docs/invariants/fcu-callouts.md#a320-36
- [A320-37] FCU dial callouts are released by `BaseAircraftDefinition.OnContinuousBatchDelivered`, OUTSIDE MainForm's `announcer.Suppressed` wrap: every `AnnounceFcuValue` caller passes `muted:` from its own Ctrl+M set (`A32NX`/`A380DisabledMonitorVariablesSet`) or a pending readout; never rely on the wrap. Full: docs/invariants/fcu-callouts.md#a320-37
- [A320-38] Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, keys from `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`, `OnPanelButtonFiring`; the calc-code V/S set arms the same two directly); a queued dotted event re-arms when `FlushPendingCalcEvents` sends it (`QueuedEventDispatched`). Full: docs/invariants/fcu-callouts.md#a320-38
- [A320-39] `SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement) of `AircraftLoaded`; without it a loading flight's first published FCU values are spoken as knob turns. Full: docs/invariants/fcu-callouts.md#a320-39
