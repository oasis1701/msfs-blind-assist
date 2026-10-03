---
paths:
  - "MSFSBlindAssist/MainForm.Announcers.cs"
  - "MSFSBlindAssist/MainForm.AircraftSwitch.cs"
  - "MSFSBlindAssist/MainForm.Dialogs.cs"
---
# Rules whose code MainForm calls

MIRRORS: each line below is copied word for word from its area's rule file, because the code it guards is called from these MainForm partials, which that area's globs do not cover. Change a rule in its own file and here together; ClaudeContextBudgetTests fails if the two differ.

- [SIM-15] `UpdateDisplayText` must always refresh `displayValues` from `GetCachedVariableValue` first; cached `displayValues` alone go stale for any def whose `ProcessSimVarUpdate` returns `true`. Full: docs/invariants/core-simconnect.md#sim-15
- [SIM-16] The per-event "is this var in any panel display" gate must use the cached `GetDisplayVarNamesCached()` HashSet — never call a def's `GetPanelDisplayVariables()` per SimVar event; it rebuilds its whole dictionary every call. Full: docs/invariants/core-simconnect.md#sim-16
- [VG-2] Every path that activates hand fly or visual guidance must leave output hotkey mode first, or the quick-access keys stay dead for the rest of the session; the liftoff auto-handoff calls `hotkeyManager.ExitOutputHotkeyMode()` itself, and that exit must stay silent. Full: docs/invariants/visual-guidance.md#vg-2
- [AI-1] ONE AI capture at a time app-wide (`Services/DisplayReadGate.Shared`): `ReadDisplay` AND `MainForm.DescribeSceneAsync` take the same gate, and the holder must RELEASE it before any modal dialog, or every later read answers "already in progress". Full: docs/invariants/ai-display.md#ai-1
- [DCK-32] MainForm must call `taxiGuidanceManager.SetSteeringToneSuppressed(dockingGuidanceManager.IsActive)` every frame so taxi and docking tones never pan at once. Full: docs/invariants/gsx-stands-docking.md#dck-32
- [EXIT-1] The Landing Exit Planner's `SIM_ON_GROUND` handler must use `RequestAircraftPositionAsync`, never `LastKnownPosition`: that cache can be stale from a prior mode and silently fails the GS≥40 kt real-landing gate. Full: docs/invariants/landing-exits.md#exit-1
- [ROL-7] A go-around or touch-and-go ENDS landing-exit guidance and keeps the plan (`LandingExitGoAround`): held only while KNOWN airborne, decided after `ConfirmMs` by a FRESH position read (never the 1 Hz cache), then ONE sentence. Full: docs/invariants/landing-rollout.md#rol-7
- [A380C-23] Dispose every A380 form holding a Coherent client or the def in `SwitchAircraft`'s cleanup; a hide-on-close form (RMP) tears down in `Dispose(bool)`, since `Close()` is cancelled and `Form.Dispose()` skips `OnFormClosed`. Full: docs/invariants/a380-coherent.md#a380c-23
- [A380C-24] Capture the OUTGOING aircraft def at the top of `SwitchAircraft` for cleanup (`StopAllMotion()`, EWD-monitor teardown), or seat/slider motor timers keep writing L:vars into the new aircraft. Full: docs/invariants/a380-coherent.md#a380c-24
- [MD11-16] Every MD-11 write path refuses ALOUD, before writing, when it cannot reach the aircraft: ask `CalcWriteCanLand` (calc bus) or `CanSendEvent` (stock event), never bare `CanExecuteCalculatorCode`. Re-arm a NEGATIVE probe verdict on aircraft switch, keep a VERIFIED one. Success stays silent. (more: see full) Full: docs/invariants/md11.md#md11-16
- [DCK-15] A DATABASE switch is invalidated by CLOSING the window (`RefreshDatabaseProvider` closes `tcasForm`), not a cache clear: `GateResolver` captures its provider at construction. Full: docs/invariants/gsx-stands-docking.md#dck-15
