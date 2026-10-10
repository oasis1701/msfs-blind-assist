---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.UiVariableSet.cs"
  - "MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs"
---
# First Officer rules whose code the A32NX and A330 definitions hold

MIRRORS: copied word for word from first-officer-airbus.md, whose globs leave these files out; the code is `HandleUIVariableSet` and its `ApplyUIVariable` wrapper, the A330's `BuildPanelControls` override and the CONF 3 registrations. Change both together (ClaudeContextBudgetTests checks).

- [FOA-4] An Event-typed key with no `varKey ==` branch in `FlyByWireA320Definition.HandleUIVariableSet` is a SILENT DEAD WRITE that reports success: both FBW FO executors REFUSE an unclaimed Event key, swept by `FoFbwUnclaimedEventKeyTests`. The FD pushes stay GUARDED on `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-4
- [FOA-5] The A330 panel inherits the A32NX's, so `HeadwindA330Definition.BuildPanelControls` overrides three controls: the `LIGHTING_LANDING_2/_3` rows become the read-back `LIGHT LANDING:2`, and pots 10/11 (the A339X ceiling and map lights) are dropped. Never over-apply either to the A32NX. Full: docs/invariants/first-officer-airbus.md#foa-5
- [FOA-6] The CONF 3 landing input is per airframe, each `OnRequest` and polled by its FO evaluator: `A32NX_FM_LANDING_CONF3` (A32NX, FBW #10890), `A32NX_SPEEDS_LANDING_CONF3` (Headwind A330, registered by `HeadwindA330Definition`), `A380X_FM_LANDING_CONF3` (A380); never `Continuous` without `IsAnnounced` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-6
