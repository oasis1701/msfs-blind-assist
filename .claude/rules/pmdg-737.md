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
- [P737-3] The 737 warning-test toggles (stick shaker, overspeed clacker) actuate via transmit LEFTSINGLE (engage, held) / LEFTRELEASE (release), never a CDA write; their state is app-tracked in the def, the keys stay OUT of `_simpleEventMap`, and `SwitchAircraft` and app exit release each engaged test and each hold `BeginHeldPress` recorded (FO, panel). Full: docs/invariants/pmdg-737.md#p737-3
- [P737-4] A third PMDG switch write transport is the stock `K:ROTOR_BRAKE` (66587), `param = (pmdgEventId - 69632) * 100 + mouseCode` (01 = left-single); `PMDG777Definition` already drives three soundpack switches through it, so check here before rediscovering it. It does NOT rescue the 737 gear lever. Full: docs/invariants/pmdg-737.md#p737-4
- [P737-5] The Ctrl+B baro dialog and the First Officer's transition-altitude rotation are ONE knob rotation under ONE guard (`BaroKnobClicks`, `BaroKnobBusy`); never press STD on a 29.92 guess (it SELECTS STD): over a 29.92 the app rotated to, the dialog turns straight to the QNH (`BaroSetPressesStdFirst`); any other is asked of the knob. Full: docs/invariants/pmdg-737.md#p737-5
