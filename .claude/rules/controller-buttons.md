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

- [CTL-1] A binding names its device by USB vendor and product id (`ControllerBinding.VendorId`, `ProductId`), never the WinMM joystick slot, and its command by the enum NAME (`ControllerBinding.Action`), never the number; `TryGetAction` accepts exact names only. Full: docs/invariants/controller-buttons.md#ctl-1
- [CTL-2] A bound press runs only through `HotkeyManager.TriggerFromController`: it closes an open ] or [ mode silently first (VG-2) and runs nothing while the hotkeys are suspended; never raise `HotkeyTriggered` for a controller any other way. Full: docs/invariants/controller-buttons.md#ctl-2
- [CTL-3] `JoystickButtonMonitor` probes empty joystick slots only at its rescan, never at the poll rate; it fires press edges only (`ControllerButtonEdges.NewlyPressed`) and seeds a newly found device silently, so a held button never fires. Full: docs/invariants/controller-buttons.md#ctl-3
- [CTL-4] The live controller reading is stopped while the Settings dialog is open and restarted from the saved bindings on every close path, and it runs only while at least one button is bound. Full: docs/invariants/controller-buttons.md#ctl-4
