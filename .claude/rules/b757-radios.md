---
paths:
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757NavRad.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Radios.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757IlsPanel.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Radios.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.cs"
  - "MSFSBlindAssist/SimConnect/BBS757/Bbs757IlsVariableDisplay.cs"
  - "MSFSBlindAssist/Forms/BBS757/BBS757NavRadiosForm.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757NavRadTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757RadiosTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757IlsPanelTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757IlsVariableDisplayTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757GlideslopeGateTests.cs"
---
# BlueBird 757 radio rules

Loaded when Claude reads matching code. Background: docs/b757.md. Full text: docs/invariants/b757-radios.md. The shared 757 rules are in b757.md.

- [B757-5] Never read the stock NAV variables (frequency, OBS, CDI, glideslope) on the 757 (`BBS757Definition.UsesStockNavReceivers` is false): BlueBird's VOR and ILS receivers are its own and leave them untouched (measured). `Bbs757NavRad` reads the CDU's NAV RADIO page, BlueBird's tuning L:vars when the page cannot be read, and its course L:vars. Full: docs/invariants/b757-radios.md#b757-5
- [B757-8] The 757's VHF L, C and R are the stock COM 1, 2 and 3 (BlueBird keeps its frequencies there, measured): `Bbs757Radios` reads `COM ACTIVE/STANDBY FREQUENCY:n` and sets a standby with the stock `COMn_STBY_RADIO_SET_HZ` (COM 1's has no digit); never assume VHF C is COM 3, or that BlueBird holds a frequency of its own. Full: docs/invariants/b757-radios.md#b757-8
- [B757-10] Read the 757's ILS frequency from `L:BBS_757_ILS_FREQ` (-1 parked) through `Bbs757IlsVariableDisplay`, never from a picture of the panel (the owner, 2026-11-05); the app's click count is only the fallback when the variable cannot be read, and is always said as a count (`Bbs757IlsPanel.ByCount`, `Bbs757IlsPanel.CountPhrase`). Full: docs/invariants/b757-radios.md#b757-10
- [B757-59] Send each ILS knob click as one unique calculator write (`Bbs757IlsPanel.StepCode`: a request of 2 moves one channel, and two writes in one sim frame count once), each after a fresh read of the ILS course and `IlsStepGapMs` (250 ms); SimConnect never shows the request non-zero, so count what was sent. Full: docs/invariants/b757-radios.md#b757-59
