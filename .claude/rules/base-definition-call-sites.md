---
paths:
  - "MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs"
  - "MSFSBlindAssist/Aircraft/IAircraftDefinition.cs"
---
# Rules whose code BaseAircraftDefinition and IAircraftDefinition hold

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards lives in BaseAircraftDefinition, the shared base class, or is declared in IAircraftDefinition, the contract it implements, which that area's globs leave out. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [A320-14] Momentary FBW L:var button pulses must be TWO SEPARATE calc calls spaced ~250 ms apart (`PulseMomentaryLVar`), never one same-frame `1 (>L:X) 0 (>L:X)` string, nor two back-to-back calls (they land in one frame and the second overwrites the first): the Rust sampler doesn't see a same-tick pulse. Full: docs/invariants/a32nx-fenix.md#a320-14
- [A320-22] FCU dial callouts (A32NX, Headwind A330, A380) listen only to sources that say themselves whether the window shows a selection, never the `A32NX_FCU_AFS_DISPLAY_*_VALUE` values; changes are STAGED, released at batch end only while the FCU is available. (more: see full) Full: docs/invariants/fcu-callouts.md#a320-22
- [A320-37] FCU dial callouts are released by `BaseAircraftDefinition.OnContinuousBatchDelivered`, OUTSIDE MainForm's `announcer.Suppressed` wrap: every `AnnounceFcuValue` caller passes `muted:` from its own Ctrl+M set (`A32NX`/`A380DisabledMonitorVariablesSet`) or a pending readout; never rely on the wrap. Full: docs/invariants/fcu-callouts.md#a320-37
- [A320-38] Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, keys from `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`, `OnPanelButtonFiring`; the calc-code V/S set arms the same two directly); a queued dotted event re-arms when `FlushPendingCalcEvents` sends it (`QueuedEventDispatched`). Full: docs/invariants/fcu-callouts.md#a320-38
- [A320-39] `SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement) of `AircraftLoaded`; without it a loading flight's first published FCU values are spoken as knob turns. Full: docs/invariants/fcu-callouts.md#a320-39
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
