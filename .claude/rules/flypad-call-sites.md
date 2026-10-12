---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.SimVarUpdate.cs"
---
# flyPad door-name rule for the FBW definitions

MIRRORS: copied word for word from flypad.md, whose globs leave the definitions holding `_doorDefs` out; change both together (ClaudeContextBudgetTests checks).

- [A380C-16] Keep `A.DOOR_NAMES` (flyPad agent) in sync with each aircraft def's `_doorDefs` table, so the flyPad label and the spoken door name agree. Full: docs/invariants/flypad.md#a380c-16
