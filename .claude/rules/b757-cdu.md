---
paths:
  - "MSFSBlindAssist/SimConnect/BBS757/Bbs757Cdu*.cs"
  - "MSFSBlindAssist/SimConnect/BBS757/CoherentBbs757TabletClient.cs"
  - "MSFSBlindAssist/Forms/BBS757/BBS757CduForm*.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.CduRows.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757Cdu*.cs"
  - "tests/MSFSBlindAssist.Tests/CoherentBbs757TabletClientTests.cs"
---
# BlueBird 757 CDU window rules

Loaded when Claude reads matching code. Background: docs/b757.md. Full text: docs/invariants/b757-cdu.md. The shared 757 rules are in b757.md.

- [B757-55] While hidden, `BBS757CduForm` keeps the tablet client scraping the captain's CDU (`ActiveCdu` 1) and judges each new captain scratchpad with `Bbs757CduMessageWatch`: never stop that scrape, and never speak a change inside the watch's `SettleMs` after the window's own keys, or the pilot's typing is read out as a CDU message. Full: docs/invariants/b757-cdu.md#b757-55
- [B757-56] A CDU row's selection marker (`Bbs757CduMarkers.Mark`, "X " in front of an inverse run) never moves a later column: it takes the blanks in front of the run or is paid back by the next gap, and a marked row stays within 37 cells, so with its three-cell prefix inside a 40-cell braille line. Full: docs/invariants/b757-cdu.md#b757-56
- [B757-57] `BBS757CduForm` restores the CDU cursor by row identity (`Bbs757CduRows.Restore`: the row's kind and line select key), never by list index: a label row comes or goes above the cursor, and a title turns ACT into MOD on the same page. Only a new page moves the cursor to line 1. Full: docs/invariants/b757-cdu.md#b757-57
- [B757-58] Never shorten `Bbs757CduTyping.GapMs` (300 ms after a 100 ms hold) on one quiet measurement: 100/50 typed 5 of 5 on a quiet sim, and 100/100 then lost a repeated digit in 4 of 10 on a busier one. Measure a new pair on a busy sim with a repeated character, and keep one step slower. Full: docs/invariants/b757-cdu.md#b757-58
