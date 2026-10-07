---
paths:
  - "MSFSBlindAssist/Aircraft/*.cs"
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
- [VAR-2] Never register a name containing a space or colon as an L:var — those are stock SimVars (force-registering `INTERACTIVE POINT OPEN:n` as an L:var broke A380 detection). Full: docs/invariants/variable-definitions.md#var-2
- [VAR-3] Do NOT delete the per-prefix `ExecuteCalculatorCode` routing in the FBW defs' `HandleUIVariableSet` catch-alls as redundant: they write through the calculator UNCONDITIONALLY, while the global `SetLVar` routing is gated on a probe that can fail silently. Full: docs/invariants/variable-definitions.md#var-3
- [VAR-4] Any VALUELESS calc write (a bare K-event toggle) must go through `ExecuteCalculatorCodeUnique` — repeats are byte-identical and the second is dropped (wiper Off→Slow→Off→Slow loses the last step). Full: docs/invariants/variable-definitions.md#var-4
- [VAR-5] Every `ExecuteCalculatorCode` call embedding a computed double must use invariant fixed-point formatting — never default `{0}`/`$"{double}"` interpolation, which can emit scientific notation or comma decimals the RPN parser rejects. Full: docs/invariants/variable-definitions.md#var-5
- [VAR-6] Never add a Continuous+IsAnnounced monitoring var to `BuildPanelControls()` merely to register it; the one exception is a var that IS a panel control's own read-back (the speed-brake levers), and a `RefreshControlWhenDefHandled` predicate must never accept values a lever sweeps through. Full: docs/invariants/variable-definitions.md#var-6
- [VAR-7] Two var keys may share a `Name`, but NEVER when both are `Continuous` and batched: the batch sorts by name, so duplicates shift every later struct slot. Use ONE var and derive extra announcements, or exclude a copy from the batch (`VarNameCollisionTests`). Full: docs/invariants/variable-definitions.md#var-7
- [VAR-8] A def that announces from inside `ProcessSimVarUpdate` needs the `announcer.Suppressed` wrap for Ctrl+M mutes (which airframes: `Services/DefAnnounceMuteSets` alone); a branch speaking a call-out ANOTHER row owns must be in `IsMuteWrapExempt` and check each row itself. Full: docs/invariants/variable-definitions.md#var-8

Mirrored from visual-guidance.md (it governs `VisualGuidanceProfile` in IAircraftDefinition.cs; change it there and here together):
- [VG-15] Never collapse `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` into one shared constant: they apply in different code paths (glideslope error vs phase detection) and were measured separately. Full: docs/invariants/visual-guidance.md#vg-15

Mirrored from da40-shared-code.md (they govern every aircraft definition; change them there and here together):
- [DA40S-3] An L:var is registered `Units = "number"`, never a converting unit; use `SimVarDefinition.Scale` for a scale. Full: docs/invariants/da40-shared-code.md#da40s-3
- [DA40S-4] `ABS_AMBIENT_TEMPERATURE` is kelvin and an L:var's Units field is a label: use the `AMBIENT TEMPERATURE` SimVar. Full: docs/invariants/da40-shared-code.md#da40s-4
- [DA40S-5] `SimVarDefinition.Format` defaults to "F0" and `ValueDescriptions` is never null (test `Count`, not null). Full: docs/invariants/da40-shared-code.md#da40s-5
- [DA40S-6] A non-percentage numeric control never sets `RenderAsSlider` (the TrackBar is 0-100); `HelpText` reaches the pilot on BUTTONS only and the other ten PanelBuilder sites must not be wired to it. Full: docs/invariants/da40-shared-code.md#da40s-6
