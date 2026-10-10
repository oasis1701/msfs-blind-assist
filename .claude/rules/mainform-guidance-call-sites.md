---
paths:
  - "MSFSBlindAssist/MainForm.cs"
  - "MSFSBlindAssist/MainForm.Announcers.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
---
# Docking, landing-exit and go-around rules for MainForm's guidance wiring

MIRRORS: copied word for word from gsx-docking.md, landing-exits.md and landing-touchdown.md, whose globs leave these partials out; change both together (ClaudeContextBudgetTests checks).

- [DCK-20] `DockingCompleted` must fire `taxiGuidanceManager.StopGuidance()` exactly once, with the event raised outside the docking lock, or taxi guidance stays stuck in LiningUp. Full: docs/invariants/gsx-docking.md#dck-20
- [DCK-28] Takeoff-assist activation and `LandingRollout` entry must both call `SetDestinationGate(null)`, or a stale gate keeps docking `IsActive` and mutes the rollout tone. Full: docs/invariants/gsx-docking.md#dck-28
- [DCK-32] MainForm must call `taxiGuidanceManager.SetSteeringToneSuppressed(dockingGuidanceManager.IsActive)` every frame so taxi and docking tones never pan at once. Full: docs/invariants/gsx-docking.md#dck-32
- [EXIT-1] The Landing Exit Planner's `SIM_ON_GROUND` handler must use `RequestAircraftPositionAsync`, never `LastKnownPosition`: that cache can be stale from a prior mode and silently fails the GS≥40 kt real-landing gate. Full: docs/invariants/landing-exits.md#exit-1
- [ROL-7] A go-around or touch-and-go ENDS landing-exit guidance and keeps the plan (`LandingExitGoAround`): held only while KNOWN airborne, decided after `ConfirmMs` by a FRESH position read (never the 1 Hz cache), then ONE sentence. Full: docs/invariants/landing-touchdown.md#rol-7
