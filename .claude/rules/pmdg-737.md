---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737*.cs"
  - "MSFSBlindAssist/Aircraft/Pmdg737*.cs"
  - "MSFSBlindAssist/SimConnect/PMDGNG3*.cs"
  - "MSFSBlindAssist/Forms/PMDG737/**"
  - "tests/MSFSBlindAssist.Tests/**/*Pmdg737*.cs"
---
# PMDG 737-800 NG3 rules

Loaded when Claude reads matching code. Background: docs/pmdg-737.md. Full text of each rule: docs/invariants/pmdg-737.md.

- [P737-1] PMDG 737 NG3 gotchas: two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (`LVL_CHG`/`HDG_SEL`/`VOR_LOC`), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test. Full: docs/invariants/pmdg-737.md#p737-1
- [P737-2] The PMDG 737 CDU sends every key (letters, LSKs, function keys, CLR/DEL/EXEC) as `TransmitClientEvent` with `MOUSE_FLAG_LEFTSINGLE`, NEVER the CDA write the 777 form uses: the NG3 FMC ignores CDA `{eventId, 1}` for CDU keys (the click sounds, nothing registers). Full: docs/invariants/pmdg-737.md#p737-2

Mirrored from pmdg-777.md (it governs the speed-brake lever in PMDG737Definition.cs; change it there and here together):
- [P777-15] Speed-brake ARM is EXACT (`PmdgLeverDetent.Tolerance`, 0.25) on the PMDG 737/777 and iFly 737 MAX, never widened into the settle band; read the 777 lever from `L:switch_498_a` (0-400), NEVER the truncated SDK `FCTL_Speedbrake_Lever` byte. (more: see full) Full: docs/invariants/pmdg-777.md#p777-15
