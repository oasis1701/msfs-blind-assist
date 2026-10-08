---
paths:
  - "MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs"
---
# Rules whose code BaseAircraftDefinition holds

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards lives in BaseAircraftDefinition, the shared base class, which that area's globs leave out. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [A320-22] FCU dial callouts (A32NX, Headwind A330, A380) listen only to sources that say themselves whether the window shows a selection, never the `A32NX_FCU_AFS_DISPLAY_*_VALUE` values; changes are STAGED, released at batch end only while the FCU is available. (more: see full) Full: docs/invariants/a32nx-fenix.md#a320-22
