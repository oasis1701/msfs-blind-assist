---
paths:
  - "MSFSBlindAssist/Aircraft/**"
  - "MSFSBlindAssist/Services/DefAnnounceMuteSets.cs"
  - "tests/MSFSBlindAssist.Tests/**/*VarNameCollision*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimVarDefinition*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DefAnnounceMuteSets*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ComboLabelCollapse*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*MuteWrap*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*WiperPosition*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*ApproachMinimums*.cs"
---
# Aircraft variable definitions rules

Loaded when Claude reads matching code. Background: docs/aircraft-definitions.md. Full text of each rule: docs/invariants/variable-definitions.md.

- [VAR-1] Two controls in one panel must never share a `DisplayName`; where rows share a leading phrase the discriminator comes FIRST ("Left Primary Engine Pump"), and a bound switch/annunciator pair comes from one constant plus a suffix, never typed twice. Full: docs/invariants/variable-definitions.md#var-1
- [VAR-2] Never register a STOCK SimVar as an L:var (`INTERACTIVE POINT OPEN:n` as one broke A380 detection). Tell them apart by the add-on's prefix or source: a space or colon settles it only if its L:vars never use one (they can: `A32NX_FUEL_USED:1`, [A380-26]). Such a name gets `SetLVar`'s data-def write ([SIM-12]): read it back. Full: docs/invariants/variable-definitions.md#var-2
- [VAR-3] Do NOT delete the per-prefix `ExecuteCalculatorCode` routing in the FBW defs' `HandleUIVariableSet` catch-alls as redundant: they write through the calculator UNCONDITIONALLY, while the global `SetLVar` routing is gated on a probe that can fail silently. Full: docs/invariants/variable-definitions.md#var-3
- [VAR-4] Any VALUELESS calc write (a bare K-event toggle) must go through `ExecuteCalculatorCodeUnique` — repeats are byte-identical and the second is dropped (wiper Off→Slow→Off→Slow loses the last step). Full: docs/invariants/variable-definitions.md#var-4
- [VAR-5] Every `ExecuteCalculatorCode` call embedding a computed double must use invariant fixed-point formatting — never default `{0}`/`$"{double}"` interpolation, which can emit scientific notation or comma decimals the RPN parser rejects. Full: docs/invariants/variable-definitions.md#var-5
- [VAR-6] Never add a Continuous+IsAnnounced monitoring var to `BuildPanelControls()` merely to register it; the one exception is a var that IS a panel control's own read-back (the speed-brake levers), and a `RefreshControlWhenDefHandled` predicate must never accept values a lever sweeps through. Full: docs/invariants/variable-definitions.md#var-6
- [VAR-7] Two var keys may share a `Name`, but NEVER when both are `Continuous` and batched: the batch sorts by name, so duplicates shift every later struct slot. Use ONE var and derive extra announcements, or exclude a copy from the batch (`VarNameCollisionTests`). Full: docs/invariants/variable-definitions.md#var-7
- [VAR-8] A def that announces from inside `ProcessSimVarUpdate` needs the `announcer.Suppressed` wrap for Ctrl+M mutes (which airframes: `Services/DefAnnounceMuteSets` alone); a branch speaking a call-out ANOTHER row owns must be in `IsMuteWrapExempt` and check each row itself. Full: docs/invariants/variable-definitions.md#var-8
- [VAR-9] A var `ProcessSimVarUpdate` consumes silently (a hotkey-readout or dialog cache, never spoken) must also set `ExcludeFromMonitorManager = true` (HS787: list it in `CacheOnlyVariables`), or it earns a Ctrl+M checkbox that mutes nothing. Full: docs/invariants/variable-definitions.md#var-9

Mirrored from visual-guidance.md (it governs `VisualGuidanceProfile` in IAircraftDefinition.cs; change it there and here together):
- [VG-15] Never collapse `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` into one shared constant: they apply in different code paths (glideslope error vs phase detection) and were measured separately. Full: docs/invariants/visual-guidance.md#vg-15

Mirrored from first-officer.md (they govern `IAircraftDefinition` members each definition implements; change them there and here together):
- [FO-17] Auto-AP-engage is floored by `IAircraftDefinition.MinimumAutopilotEngageAltitudeAgl` (PMDG 737 = 400 ft, its AFDS inhibits CMD below 400 ft RA; 777 = 200): never let the user setting press below an aircraft's own limit, and never lower the 737's floor, or the press is silently rejected while success is announced. Full: docs/invariants/first-officer.md#fo-17
- [FO-18] Auto-AP-engage is CLOSED-LOOP wherever `IAircraftDefinition.IsAutopilotEngaged` is non-null: announce only a CONFIRMED engagement, retry a rejected press (`MaxApEngageAttempts`), report a Captain action if it never takes. It returns null, never false, before the first CDA snapshot (PMDG engage switches toggle). Full: docs/invariants/first-officer.md#fo-18
