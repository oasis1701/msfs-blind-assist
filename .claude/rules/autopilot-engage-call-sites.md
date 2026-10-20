---
paths:
  - "MSFSBlindAssist/Aircraft/IAircraftDefinition.cs"
  - "MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.HotkeysAndMotion.cs"
  - "MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs"
  - "MSFSBlindAssist/Aircraft/PMDG737Definition.cs"
  - "MSFSBlindAssist/Aircraft/PMDG777Definition.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.AutopilotEngage.cs"
  - "MSFSBlindAssist/Aircraft/C172/CessnaC172BaseDefinition.cs"
  - "MSFSBlindAssist/Aircraft/C172/CessnaC172G1000Definition.AutopilotEngage.cs"
---
# Auto-autopilot engage rules for the aircraft definitions

MIRRORS: copied word for word from first-officer-automation.md, whose globs leave these files out; they declare or override `MinimumAutopilotEngageAltitudeAgl`, `IsAutopilotEngaged` and `HasRetractableGear` (the MD-11's FirstOfficer partial loads first-officer-automation.md itself). Change both together (ClaudeContextBudgetTests checks).

- [FO-17] Auto-AP-engage is floored by `IAircraftDefinition.MinimumAutopilotEngageAltitudeAgl` (PMDG 737 = 400 ft, its AFDS inhibits CMD below 400 ft RA; 777 = 200; DA40 = 800, AFM): never let the user setting press below an aircraft's own limit, and never lower the 737's floor, or the press is silently rejected while success is announced. Full: docs/invariants/first-officer-automation.md#fo-17
- [FO-18] Auto-AP-engage is CLOSED-LOOP wherever `IAircraftDefinition.IsAutopilotEngaged` is non-null: announce only a CONFIRMED engagement, retry a rejected press (`MaxApEngageAttempts`), report a Captain action if it never takes. Unread is null, never false: a toggle waits on it and presses 3 s apart. Full: docs/invariants/first-officer-automation.md#fo-18
- [FO-27] The universal auto-gear acts and speaks only where `IAircraftDefinition.HasRetractableGear` (pushed as `UniversalAutomationService.HasRetractableGear`): the COWS DA40 has fixed gear the simulator reports retractable with the handle up, and its gear key is its steering-assist switch. Full: docs/invariants/first-officer-automation.md#fo-27
