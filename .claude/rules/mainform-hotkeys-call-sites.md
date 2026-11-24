---
paths:
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
---
# Ground-traffic rules for MainForm.Hotkeys.cs

MIRRORS: copied word for word from ground-traffic.md, whose globs leave MainForm.Hotkeys.cs out; its hotkey dispatch runs the Alt+G traffic summary TRF-1 keeps ungated and speaks the taxi status (`GetStatusAnnouncement`) whose held runway TRF-6 derives. Change both together (ClaudeContextBudgetTests checks).

- [TRF-1] `GroundTrafficSuppression` (pure, not a MainForm lambda) silences Caution/Warning on the takeoff roll, with no route and in a STILL-ROLLING rollout; the 120° arc never gates Awareness, Alt+G stays ungated; a fast landing exit filters speech, never mutes the monitor (more: see full). Full: docs/invariants/ground-traffic.md#trf-1
- [TRF-6] The held runway the watch scopes and the HoldShort status readout speaks come from the ONE `Navigation.HeldRunwayLabel.Resolve` (`GetGroundTrafficContext`, `GetStatusAnnouncement`), never a mirrored field: PR #247's `_heldRunwayLabel` was never assigned, so no hold-short was watched. Full: docs/invariants/ground-traffic.md#trf-6

Mirrored from controller-buttons.md (the controller press handler and the monitor's start live here; change them there and here together):
- [CTL-2] A bound press runs only through `HotkeyManager.TriggerFromController`: it closes an open ] or [ mode silently first (VG-2) and runs nothing while the hotkeys are suspended; never raise `HotkeyTriggered` for a controller any other way. Full: docs/invariants/controller-buttons.md#ctl-2
- [CTL-5] Only one `JoystickButtonMonitor` may be started at a time: Windows keeps ONE Raw Input target per device type per process, so a second `Start` would steal the first one's input and its `Stop` (`RIDEV_REMOVE`) would unregister both; `Start` refuses while another is running. Full: docs/invariants/controller-buttons.md#ctl-5
