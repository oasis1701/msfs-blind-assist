---
paths:
  - "MSFSBlindAssist/FirstOfficer/**/*AutoManager.cs"
  - "MSFSBlindAssist/FirstOfficer/**/*PhaseMonitor.cs"
  - "MSFSBlindAssist/FirstOfficer/TransitionAltimeters.cs"
  - "MSFSBlindAssist/FirstOfficer/TransitionCrossingDetector.cs"
  - "MSFSBlindAssist/FirstOfficer/SeatbeltAutomation.cs"
  - "MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs"
  - "MSFSBlindAssist/Forms/Settings/FirstOfficerPanel.cs"
  - "MSFSBlindAssist/Automation/UniversalAutomationService.cs"
  - "MSFSBlindAssist/MainForm.UniversalAutomation.cs"
  - "MSFSBlindAssist/Aircraft/FbwAutopilotEngage.cs"
  - "MSFSBlindAssist/Aircraft/TFDiMD11Definition.FirstOfficer.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/**/*AutoManager*Tests.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/**/*PhaseMonitor*Tests.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/Transition*Tests.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/FbwAutopilotEngageTests.cs"
  - "tests/MSFSBlindAssist.Tests/UniversalAutomationServiceTests.cs"
  - "tests/MSFSBlindAssist.Tests/SeatbeltAutomationTests.cs"
---
# First Officer automation rules

Loaded when Claude reads matching code, beside first-officer.md where both match. Background: docs/first-officer.md. Full text of each rule: docs/invariants/first-officer-automation.md. The flows, checklists and window: first-officer.md.

- [FO-15] PMDG auto-flaps was REMOVED (2026-07-08, owner decision): never reintroduce a 737/777 flap schedule; the "Auto-manage flaps" setting acts on the FBW A380 and A32NX and the Headwind A330 only (the Fenix and the PMDG managers store it and ignore it). Full: docs/invariants/first-officer-automation.md#fo-15
- [FO-16] The FO AP engagement height is the user setting `FOAutoApEngageAltitudeAgl` (default 350 ft AGL, one global value): never hardcode an engagement altitude in an `FOAutoManager`; the 737's LNAV/VNAV push height (400 ft AGL) is deliberately FIXED, annunciator-guarded (NaN = skip) and fires only on a climb it watched. Full: docs/invariants/first-officer-automation.md#fo-16
- [FO-17] Auto-AP-engage is floored by `IAircraftDefinition.MinimumAutopilotEngageAltitudeAgl` (PMDG 737 = 400 ft, its AFDS inhibits CMD below 400 ft RA; 777 = 200; DA40 = 800, AFM): never let the user setting press below an aircraft's own limit, and never lower the 737's floor, or the press is silently rejected while success is announced. Full: docs/invariants/first-officer-automation.md#fo-17
- [FO-18] Auto-AP-engage is CLOSED-LOOP wherever `IAircraftDefinition.IsAutopilotEngaged` is non-null: announce only a CONFIRMED engagement, retry a rejected press (`MaxApEngageAttempts`), report a Captain action if it never takes. Unread is null, never false: a toggle waits on it and presses 3 s apart. Full: docs/invariants/first-officer-automation.md#fo-18
- [FO-19] The 10k landing-light switching is gated by `FOAutoLights10kEnabled` (default ON), and ONLY the light actions/announcements are; the crossing latch always runs. Full: docs/invariants/first-officer-automation.md#fo-19
- [FO-21] FBW auto-AP (A32NX, A380, Headwind A330) presses `A32NX.FCU_AP_1_PUSH` via `FbwAutopilotEngage` only on a definite OFF (`AutopilotEngageWaitsForState`: it toggles, so never blind) and never into the probe queue (`PressWouldWait`), 3 s apart, confirmed on `A32NX_AUTOPILOT_1/2_ACTIVE`; never stock `AUTOPILOT_ON`, which FBW ignores. Full: docs/invariants/first-officer-automation.md#fo-21
- [FO-23] `FOAutoAltimetersEnabled` (default ON) gates every transition altimeter write on every monitor; off, `TransitionAltimeters.ReminderInstead` speaks the crossing, the detector keeps tracking, the MD-11 still sets Dial-A-Flap. Full: docs/invariants/first-officer-automation.md#fo-23
- [FO-27] The universal auto-gear acts and speaks only where `IAircraftDefinition.HasRetractableGear` (pushed as `UniversalAutomationService.HasRetractableGear`): the COWS DA40 has fixed gear the simulator reports retractable with the handle up, and its gear key is its steering-assist switch. Full: docs/invariants/first-officer-automation.md#fo-27
