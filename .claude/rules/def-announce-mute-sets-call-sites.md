---
paths:
  - "MSFSBlindAssist/Services/DefAnnounceMuteSets.cs"
---
# A300 rule for the Ctrl+M mute-list table

MIRRORS: copied word for word from a300.md, whose globs leave DefAnnounceMuteSets.cs out. The A300 code never calls it (MainForm.Announcers.cs does), yet its lamps and levers are muted only while it lists `INI_A300`, which `DefAnnounceMuteSetsTests` pins. Change both together (ClaudeContextBudgetTests checks).

- [A300-7] Master warning and caution speak only as they come ON (the press writes 0 to the same var). Lamps and the announced levers (`AnnouncedKeys`) speak from `ProcessSimVarUpdate` inside MainForm's mute wrap (`DefAnnounceMuteSets` lists `INI_A300`); a context reset wipes the baselines and `Md11SeedGate` decides the re-seed. Full: docs/invariants/a300.md#a300-7
