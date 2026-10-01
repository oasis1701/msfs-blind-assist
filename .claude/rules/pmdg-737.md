---
paths:
  - "MSFSBlindAssist/Aircraft/PMDG737*.cs"
  - "MSFSBlindAssist/Aircraft/Pmdg737*.cs"
  - "MSFSBlindAssist/SimConnect/PMDGNG3*.cs"
---
# PMDG 737-800 NG3 rules

Loaded when Claude reads matching code. Background: docs/pmdg-737.md. Full text of each rule: docs/invariants/pmdg-737.md.

- [P737-1] PMDG 737 NG3 gotchas: two CDUs (no observer), no FPA mode, annunciator names differ from the 777 (`LVL_CHG`/`HDG_SEL`/`VOR_LOC`), DU selectors reverse sequence for the F/O, and fire handles need an active fire to test. Full: docs/invariants/pmdg-737.md#p737-1
