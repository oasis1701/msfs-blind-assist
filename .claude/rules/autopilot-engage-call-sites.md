---
paths:
  - "MSFSBlindAssist/Aircraft/IAircraftDefinition.cs"
  - "MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs"
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition.HotkeysAndMotion.cs"
  - "MSFSBlindAssist/Aircraft/IFly737MAXDefinition.cs"
  - "MSFSBlindAssist/Aircraft/PMDG737Definition.cs"
  - "MSFSBlindAssist/Aircraft/PMDG777Definition.cs"
---
# Auto-autopilot engage rules for the aircraft definitions

MIRRORS: copied word for word from first-officer-automation.md, whose globs leave these files out; they declare or override `MinimumAutopilotEngageAltitudeAgl` and `IsAutopilotEngaged` (the MD-11's FirstOfficer partial loads first-officer-automation.md itself). Change both together (ClaudeContextBudgetTests checks).

- [FO-17] Auto-AP-engage is floored by `IAircraftDefinition.MinimumAutopilotEngageAltitudeAgl` (PMDG 737 = 400 ft, its AFDS inhibits CMD below 400 ft RA; 777 = 200): never let the user setting press below an aircraft's own limit, and never lower the 737's floor, or the press is silently rejected while success is announced. Full: docs/invariants/first-officer-automation.md#fo-17
- [FO-18] Auto-AP-engage is CLOSED-LOOP wherever `IAircraftDefinition.IsAutopilotEngaged` is non-null: announce only a CONFIRMED engagement, retry a rejected press (`MaxApEngageAttempts`), report a Captain action if it never takes. It returns null, never false, before the first CDA snapshot (PMDG engage switches toggle). Full: docs/invariants/first-officer-automation.md#fo-18
