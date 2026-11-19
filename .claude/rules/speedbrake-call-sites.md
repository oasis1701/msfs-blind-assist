---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737Definition.cs"
  - "MSFSBlindAssist/FirstOfficer/SpeedbrakeLeverState.cs"
  - "MSFSBlindAssist/FirstOfficer/Pmdg777SpeedbrakeLever.cs"
  - "MSFSBlindAssist/FirstOfficer/PMDG737/SpeedbrakeArmLadder.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Pmdg737Speedbrake*.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Pmdg737FlowChecklistLinkTests.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SpeedbrakeLeverState*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SpeedbrakeArmLadder*.cs"
---
# Speed-brake ARM rule for the 737 definition and the First Officer's lever code

MIRRORS: copied word for word from pmdg-777.md, whose globs leave these files out; they read the 737 lever or judge the ARM detent for the First Officer. Change both together (ClaudeContextBudgetTests checks).

- [P777-15] Speed-brake ARM is EXACT (`PmdgLeverDetent.Tolerance`, 0.25) on the PMDG 737/777 and iFly 737 MAX, never widened into the settle band; read the 777 lever from `L:switch_498_a` (0-400), NEVER the truncated SDK `FCTL_Speedbrake_Lever` byte. (more: see full) Full: docs/invariants/pmdg-777.md#p777-15
