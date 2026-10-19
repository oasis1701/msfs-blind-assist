---
paths:
  - "MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs"
---
# PMDG 737 rules whose code the 737 First Officer executor calls

MIRRORS: copied word for word from pmdg-737.md, whose globs leave the First Officer out; the code is the executor's held tests (`HoldThenReleaseAsync`) and its transition-altitude baro rotation, both the definition's own. Change both together (ClaudeContextBudgetTests checks).

- [P737-6] The 737 warning-test toggles (stick shaker, overspeed clacker) actuate via transmit LEFTSINGLE (engage, held) / LEFTRELEASE (release), never a CDA write; their state is app-tracked in the def, the keys stay OUT of `_simpleEventMap`, and `SwitchAircraft` and app exit release each engaged test and each hold `BeginHeldPress` recorded (FO, panel). Full: docs/invariants/pmdg-737.md#p737-6
- [P737-5] The Ctrl+B baro dialog and the First Officer's transition-altitude rotation are ONE knob rotation under ONE guard (`BaroKnobClicks`, `BaroKnobBusy`); never press STD on a 29.92 guess (it SELECTS STD): over a 29.92 the app rotated to, the dialog turns straight to the QNH (`BaroSetPressesStdFirst`); any other is asked of the knob. Full: docs/invariants/pmdg-737.md#p737-5
