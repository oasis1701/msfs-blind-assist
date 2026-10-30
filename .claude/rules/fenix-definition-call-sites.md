---
paths:
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.cs"
---
# Rules whose code the Fenix A320 definition holds

MIRRORS: copied word for word from takeoff-and-callouts.md, whose globs leave this definition out (the take-off calls); change both together (ClaudeContextBudgetTests checks).

- [TKO-5] Take-off calls are ONE `AnnounceImmediate` per sample via `TakeoffVSpeedCallouts.Compose`, never one per call; every definition drops the ARM, never the speeds, on reconnect AND `OnSimContextReset`, and names keys through the one `TakeoffCalloutKeys` (more: see full). Full: docs/invariants/takeoff-and-callouts.md#tko-5
