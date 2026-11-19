---
paths:
  - "MSFSBlindAssist/FirstOfficer/BBS757/**"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/BBS757/**"
---
# BlueBird 757 First Officer rules

Loaded when Claude reads matching code. Background: docs/b757.md and docs/first-officer.md. Full text: docs/invariants/b757-first-officer.md. The shared 757 rules are in b757.md.

- [B757-60] The 757 First Officer's "APU: OFF" lines and steps read the APU switch (`FO_APU_OFF`), never `B757_APU_STATE`: BlueBird runs the APU on through a cool-down after OFF. Final Securing waits `ApuCooldownSeconds` (120) only after a switch-off it sent, then for the state to read 0; the battery waits for 0 (`BatteryMustStay`). Full: docs/invariants/b757-first-officer.md#b757-60
- [B757-61] Never send the 757 parking brake SET without brake pressure (`Bbs757FoChecklistDefinitions.NoBrakePressure`: the left or right electric hydraulic pump ON, or an engine running; unread is no pressure), and never turn a pump on to set it: the step is left alone with `BrakeStaysText`. Full: docs/invariants/b757-first-officer.md#b757-61
- [B757-62] The 757's center-pump ON gate sits at `Bbs757FoActionExecutor.DispatchAsync`, its one chokepoint (the shared gate, keyed on `B757_OVHD_C_PUMPS_L_BUTTON` and `_R_BUTTON`), the tank in pounds (`FO_CENTER_FUEL_LBS`, 6.7 lb/gal until BlueBird's density reads), as [FOB-10] and [FOB-18]; an unread tank (NaN) is not gated, OFF never is. Full: docs/invariants/b757-first-officer.md#b757-62
- [B757-63] The 757's 400 ft LNAV/VNAV push says only what a press did (`McpPush`, `Bbs757FoAutoManager.PushWords`): a light the fresh read finds lit or unread is not pressed and not reported, never "did not come on" over a lit mode (the pilot would toggle it off). Full: docs/invariants/b757-first-officer.md#b757-63
