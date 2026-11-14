---
paths:
  - "MSFSBlindAssist/Hotkeys/Controller*.cs"
  - "MSFSBlindAssist/Hotkeys/JoystickButtonMonitor.cs"
  - "MSFSBlindAssist/Hotkeys/HotkeyManager.cs"
  - "MSFSBlindAssist/Forms/Settings/ControllerButtonsPanel.cs"
  - "MSFSBlindAssist/Forms/Settings/ControllerBindingDialog.cs"
  - "tests/MSFSBlindAssist.Tests/ControllerBindingTests.cs"
---
# Controller button rules

Loaded when Claude reads matching code. Background: docs/controller-buttons.md. Full text of each rule: docs/invariants/controller-buttons.md.

- [CTL-1] A binding names its device by USB vendor and product id (`ControllerBinding.VendorId`, `ProductId`), never a device handle or enumeration slot, and its command by the enum NAME (`ControllerBinding.Action`), never the number; `TryGetAction` accepts exact names only. Full: docs/invariants/controller-buttons.md#ctl-1
- [CTL-2] A bound press runs only through `HotkeyManager.TriggerFromController`: it closes an open ] or [ mode silently first (VG-2) and runs nothing while the hotkeys are suspended; never raise `HotkeyTriggered` for a controller any other way. Full: docs/invariants/controller-buttons.md#ctl-2
- [CTL-3] `JoystickButtonMonitor` only LISTENS, through Raw Input (`RIDEV_INPUTSINK`): never poll, open for reading or acquire a controller (WinMM joy calls, DirectInput, HidD_GetInputReport); a MobiFlight-driven throttle stopped working under the WinMM version. It fires press edges only (`ControllerButtonEdges.NewlyPressed`). Full: docs/invariants/controller-buttons.md#ctl-3
- [CTL-4] The live controller reading is stopped while the Settings dialog is open and restarted from the saved bindings on every close path, and it runs only while at least one button is bound. Full: docs/invariants/controller-buttons.md#ctl-4
- [CTL-5] Only one `JoystickButtonMonitor` may be started at a time: Windows keeps ONE Raw Input target per device type per process, so a second `Start` would steal the first one's input and its `Stop` (`RIDEV_REMOVE`) would unregister both; `Start` refuses while another is running. Full: docs/invariants/controller-buttons.md#ctl-5
