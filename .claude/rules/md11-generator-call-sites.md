---
paths:
  - "tools/md11-gen/**"
---
# MD-11 control rule for the control-map generator

MIRRORS: copied word for word from md11-controls.md, whose globs leave tools/md11-gen out; the generator sets the Engine and APU Fire Test label the rule's full text governs. Change both together (ClaudeContextBudgetTests checks).

- [MD11-14] `Md11TestButtons` holds eleven test buttons 3 s via `PressAndHoldAsync`, but never the ANNUNCIATOR LIGHT TEST (it stays a tap). `Md11EventBus.Dispose` stops the pump first and writes every owed UP before draining, bounded; an UP never precedes its DOWN. (more: see full) Full: docs/invariants/md11-controls.md#md11-14
