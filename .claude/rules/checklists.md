---
paths:
  - "MSFSBlindAssist/Services/NativeChecklistReader.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NativeChecklist*.cs"
---
# Native checklist rules

Loaded when Claude reads matching code. Background: docs/checklists.md.

Mirrored from da40-shared-code.md (they govern the shared checklist reader; change them there and here together):
- [DA40S-9] `NativeChecklistReader` renders the aircraft's own `Checklist/*.xml`, walking each page's children IN ORDER including one level of `<Block>`; it is pinned against the installed package, and packages resolve from UserCfg.opt's active key. Full: docs/invariants/da40-shared-code.md#da40s-9
- [DA40S-14] `NativeChecklistReader` opens every file through `LoadDocument`, behind a static constructor that registers the code-page provider: Asobo checklists declare Windows-1252, which .NET reads only with one, and without it every load threw and read as "no checklist". A real-package test skips only when the file is not found. Full: docs/invariants/da40-shared-code.md#da40s-14
