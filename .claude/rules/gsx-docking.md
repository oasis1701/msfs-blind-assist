---
paths:
  - "MSFSBlindAssist/Services/Docking*.cs"
  - "MSFSBlindAssist/Services/DistanceFormatter.cs"
  - "MSFSBlindAssist/Settings/DistanceUnit.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Docking*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DistanceFormatter*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*DistanceUnit*.cs"
---
# Docking guidance: approach geometry, tones, completion and disengage rules

Loaded when Claude reads matching code. Background: docs/gsx.md. Full text of each rule: docs/invariants/gsx-docking.md.

- [DCK-17] The docking lateral cue must use the heading-error angle, never `CalculateCrossTrackError`, which yields +/-180 deg garbage when docking from behind. Full: docs/invariants/gsx-docking.md#dck-17
- [DCK-18] Never remove `DockingGeometry.ClampStopToOccupancy` or make it clamp on `gatedistancethreshold` unconditionally; it clamps only the `.py`-shifted stop, never the navdata base point, and stays a no-op for deice pads and navdata-only gates (more: see full). Full: docs/invariants/gsx-docking.md#dck-18
- [DCK-19] Docking's lateral tone must use the runway-lineup PRECISION profile (`UpdateHeadingErrorWithThresholds`), never the width-scaled overload, whose MIN_SCALE clamp is too loose for parking. Full: docs/invariants/gsx-docking.md#dck-19
- [DCK-20] `DockingCompleted` must fire `taxiGuidanceManager.StopGuidance()` exactly once, with the event raised outside the docking lock, or taxi guidance stays stuck in LiningUp. Full: docs/invariants/gsx-docking.md#dck-20
- [DCK-21] Arrival ownership stays ENGAGE-LATCHED (docking `IsActive` = Docking or Stopped), never widened back to gate-set semantics, which left pilots silent when docking never engaged. Full: docs/invariants/gsx-docking.md#dck-21
- [DCK-22] `StopToleranceMetres` stays 0.3 m and `BeepNearMetres` must equal it (no beep plateau), or 2 m to stop sounds like the stop and pilots park short. Full: docs/invariants/gsx-docking.md#dck-22
- [DCK-23] Docking completion requires squareness (`DockingGeometry.IsSquare`, `StopMaxHeadingErrorDeg` 7 deg) as well as the 2 m cross gate; an askew arrival terminates like `IsLateralMiss`, never advising an in-place turn. Full: docs/invariants/gsx-docking.md#dck-23
- [DCK-24] A verified-good park CONCLUDES guidance: hold the "docked" tone `CompletedHoldToneSeconds` (3 s), then fall silent with a verbal closure; never restore hold-until-Stop, never hold any tone after an overshoot/askew stop. Full: docs/invariants/gsx-docking.md#dck-24
- [DCK-25] The concluded-park hold tone fades on a one-shot `Timer` armed at completion, NEVER a per-frame countdown (the completing frame is the last); cancel it at both reset sites and dispose, non-blocking `Timer.Dispose()` only. Full: docs/invariants/gsx-docking.md#dck-25
- [DCK-27] Disabling docking or losing the gate mid-approach must fully `ResetLocked`, not just go silent, or `IsActive` stays latched and mutes taxi's steering tone. Full: docs/invariants/gsx-docking.md#dck-27
- [DCK-28] Takeoff-assist activation and `LandingRollout` entry must both call `SetDestinationGate(null)`, or a stale gate keeps docking `IsActive` and mutes the rollout tone. Full: docs/invariants/gsx-docking.md#dck-28
- [DCK-29] Docking's "Slow down." knob is `SlowDownSpeedKts` 3.0, not distance: never widen `SlowDownMetres` (6 m), or the one-shot is spent during normal deceleration. Full: docs/invariants/gsx-docking.md#dck-29
- [DCK-30] Docking's 1-knot speed callout stays QUEUED (`Announce`), placed LAST and skipped on a frame a one-shot fired; `EngageLocked` must `Arm(currentSpeed)`, never `Reset()`. Full: docs/invariants/gsx-docking.md#dck-30
- [DCK-32] MainForm must call `taxiGuidanceManager.SetSteeringToneSuppressed(dockingGuidanceManager.IsActive)` every frame so taxi and docking tones never pan at once. Full: docs/invariants/gsx-docking.md#dck-32
- [DCK-33] Hot paths must not regress: docking far-field math gated to <150 m or engaged, fired callout latches early-out, `TaxiAssistForm`'s gate list cached per ICAO, `SettingsManager.Save` writing outside its static lock. Full: docs/invariants/gsx-docking.md#dck-33
- [DCK-35] `DistanceFormatter` is display-only; guidance thresholds stay unit-native (metric). `GroundTrafficUseMetres` and `GroundDistanceUnit` are independent toggles: never fold them together. Full: docs/invariants/gsx-docking.md#dck-35

Mirrored from simconnect-data.md (it governs the heading `DockingGuidanceManager.UpdatePosition` takes from the taxi-guidance position and adds the variation to; change it there and here together):
- [DCK-34] A SimConnect heading (`PLANE HEADING DEGREES TRUE`/`MAGNETIC`) arrives in the unit its `AddToDataDefinition` asks for: degrees in the position, AI-traffic, visual-guidance and flare definitions, radians in the hotkey, take-off and hand-fly ones, converted once on receipt. Never convert `AircraftPosition.HeadingMagnetic` again. Full: docs/invariants/simconnect-data.md#dck-34
