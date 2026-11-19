---
paths:
  - "MSFSBlindAssist/Services/BBS757/Bbs757Adi*.cs"
  - "MSFSBlindAssist.DisplayLayouts/Bbs757/Bbs757AdiLayout.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Adi.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Bbs757Adi*.cs"
---
# BlueBird 757 ADI reader rules

Loaded when Claude reads matching code. Background: docs/b757.md. Full text: docs/invariants/b757-adi.md. The shared 757 rules (B757-30 and on) are in b757.md.

- [B757-64] Alt+P's fallback follows the ADI reader alone (`Adi.KeyFallsBackToAi` in `BBS757Definition.Adi.cs`): the AI read while it cannot run, whatever the FMA does; else the one window, `Fma.View()` then the ADI's rows. The ADI reader never reads the FMA itself. Full: docs/invariants/b757-adi.md#b757-64
- [B757-65] `Bbs757AdiText.Changes` speaks only the five flags (ATT, SPD, LOC, G/S, RA) as they come and go; the speeds, V1, DH, ALT, radio altitude and pointers are rows only, so no ADI line repeats a callout BlueBird plays (V1, minimums, altitudes). Full: docs/invariants/b757-adi.md#b757-65
- [B757-66] `Bbs757AdiReader.Decode` copies BlueBird's draw conditions and measured signs, never tidier ones: `PLANE PITCH DEGREES` is positive nose down and the rows turn it into "up"/"down"; the flight director pitch command, localizer (right) and glideslope (up) are positive as measured; ALT only with the radio altimeter valid. Full: docs/invariants/b757-adi.md#b757-66
