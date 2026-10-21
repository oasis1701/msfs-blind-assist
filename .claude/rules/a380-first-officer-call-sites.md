---
paths:
  - "MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380ActionExecutor.cs"
---
# A380 panel rule whose code the A380 First Officer executor holds

MIRRORS: copied word for word from a380-panels.md, whose globs leave the First Officer out; the code is the T.O CONFIG test's press on the shared ECP clock. Change both together (ClaudeContextBudgetTests checks).

- [A380-13] ECP keys (`L:A32NX_BTN_*`) are held `A380EcpKeyPulse.HoldMs` (250 ms; the FO's T.O CONFIG test 2 s, `PressClock.Reserve(now, holdMs)`) and released `ReleaseMs` (250 ms) between presses, on the one shared `A380EcpKeyPulse.Shared` clock reserving a slot at press start; never shorten them or guard per caller. Full: docs/invariants/a380-panels.md#a380-13
