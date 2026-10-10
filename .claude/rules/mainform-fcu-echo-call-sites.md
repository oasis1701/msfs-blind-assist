---
paths:
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
---
# FCU echo rule for MainForm's queued-event wiring

MIRRORS: copied word for word from fcu-callouts.md, whose globs leave these partials out (simconnect-events.md carries it for SimConnectManager and the panel builder); change both together (ClaudeContextBudgetTests checks).

- [A320-38] Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, keys from `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`, `OnPanelButtonFiring`; the calc-code V/S set arms the same two directly); a queued dotted event re-arms when `FlushPendingCalcEvents` sends it (`QueuedEventDispatched`). Full: docs/invariants/fcu-callouts.md#a320-38
