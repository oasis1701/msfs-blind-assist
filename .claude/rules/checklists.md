---
paths:
  - "MSFSBlindAssist/Services/NativeChecklistReader.cs"
  - "MSFSBlindAssist/Services/ChecklistContent.cs"
  - "MSFSBlindAssist/Forms/ChecklistForm.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Checklist*.cs"
---
# Checklist window rules

Loaded when Claude reads matching code. Background: docs/checklists.md.

Mirrored from da40-shared-code.md (it governs the shared checklist reader; change it there and here together):
- [DA40S-9] `NativeChecklistReader` renders the aircraft's own `Checklist/*.xml`, walking each page's children IN ORDER including one level of `<Block>`; it is pinned against the installed package, and packages resolve from UserCfg.opt's active key. Full: docs/invariants/da40-shared-code.md#da40s-9
