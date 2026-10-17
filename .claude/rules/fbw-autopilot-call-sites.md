---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.HotkeysAndMotion.cs"
---
# Rules whose code the FlyByWire engage overrides hold

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards lives in the A32NX and A380 `EngageAutopilot` overrides (FlyByWireA320Definition.cs, FlyByWireA380Definition.HotkeysAndMotion.cs), which that area's globs leave out. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [FO-21] FlyByWire auto-AP (A32NX, A380, Headwind A330) presses `A32NX.FCU_AP_1_PUSH` via `FbwAutopilotEngage` only on a definite OFF (`AutopilotEngageWaitsForState`: the push toggles, so never blind) and never into the probe queue (`PressWouldWait`), 3 s apart, confirmed on `A32NX_AUTOPILOT_1/2_ACTIVE`; never stock `AUTOPILOT_ON`, which FBW ignores. Full: docs/invariants/first-officer.md#fo-21
