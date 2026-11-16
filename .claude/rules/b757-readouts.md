---
paths:
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757FlapSpeeds.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757LightTable.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757CockpitAnnouncements.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Mcp.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Mach.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757AutoflightAnnouncements.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Annunciators.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757ChangeTracker.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Squawk.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Readouts.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Mcp.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Readouts.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Announcements.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Altimeter.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757FlapSpeedsTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757LightTableTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757CockpitAnnouncementsTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757McpTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757MachTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757AutoflightAnnouncementsTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757AnnunciatorsTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757ReadoutsTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757FlapSpeedsFmcTests.cs"
---
# BlueBird 757 MCP, readout and announcement rules

Loaded when Claude reads matching code. Background: docs/b757.md. Full text: docs/invariants/b757-readouts.md. The shared 757 rules (B757-3 for the MCP speed among them) are in b757.md.

- [B757-9] 757 flap speeds come first from the FMC's own VREF 30 and 25 (`Bbs757FlapSpeeds.DescribeFromFmc`, the ADI reader's `Newest` reading); only while those are absent or not above 0 from `Bbs757FlapSpeeds.Describe`'s table measured on APPROACH REF (140,000 to 250,000 lb, "calculated"); never extrapolate the table, never a generic formula. Full: docs/invariants/b757-readouts.md#b757-9
- [B757-11] Cockpit-wide 757 announcements watch L:vars through the tablet agent's watch list (one Coherent call, no SimConnect slot), never as SimConnect rows, and skip any L:var a definition row already announces: `Bbs757LightTable.WatchSet` drops the names `BBS757Definition.CockpitWatched` hands it. Full: docs/invariants/b757-readouts.md#b757-11
- [B757-50] Read the 757's MCP Mach only from the speed window's wheels (`Bbs757Mcp.MachFromDrums`), never `L:BBS_TARGET_MACH` (it reads 0 in Mach mode, measured); a wheel reading below .40 is knots (the wheels show knots for 1-2 s after a changeover), never a Mach. Full: docs/invariants/b757-readouts.md#b757-50
- [B757-51] Never let a Mach set command past .86 (`Bbs757Mach.Max`): BlueBird lets the target run to .95 (measured). `Bbs757Mach.Bursts` plans no burst past 25 events, and `Bbs757Mach.SetMachAsync` never corrects more in the set's direction than the planned difference. Full: docs/invariants/b757-readouts.md#b757-51
- [B757-52] A row whose call-out depends on another row of the same batch is judged once the batch is in (`EvaluateSpeedRows`), never per delivery: the batch is sorted by name, so the MCP speed arrives before the Mach flag and the blank flag, and the Mach's hundredths wheel before its tenths wheel. Full: docs/invariants/b757-readouts.md#b757-52
- [B757-53] A `Bbs757Annunciators` row's silent state is a `Bbs757Annunciators.Quiet` phrase (tracked, never spoken), never null: `Bbs757ChangeTracker` takes the first phrase after an untracked state for a baseline, so the next spoken state would be lost. Full: docs/invariants/b757-readouts.md#b757-53
- [B757-54] Never register a second Continuous row on `TRANSPONDER CODE:1`: the squawk watch is the squawk field's read-back row itself (`Bbs757Annunciators.SquawkWatchKey` is `Bbs757Squawk.CodeKey`); two continuous rows of one name shift every later batch slot ([VAR-7]). Full: docs/invariants/b757-readouts.md#b757-54
