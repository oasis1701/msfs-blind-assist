# Controller buttons

A pilot can bind a button on a yoke, joystick, throttle or gamepad (a hat direction too) to any hotkey command. The button runs the command directly, with no ] or [ key first, whichever window is in front. Taxiing with one hand on the tiller and the other on the yoke, the pilot asks for the ground speed or the next taxiway without reaching for the keyboard.

Rules: [invariants/controller-buttons.md](invariants/controller-buttons.md).

## For the pilot

Settings, Controller Buttons tab: Add button opens a dialog with the command list (every hotkey command, each name ending with its keyboard chord, such as "Ground speed (] G)"), a Capture button and a status box. After Capture the pilot presses the controller button within 15 seconds; what was captured is spoken and written to the status box. A button that is already bound says so, and OK replaces its command. The list shows each binding as command, controller, button; Delete or the Remove button removes one. Nothing is read from any controller until a button is bound.

Bind a button the simulator does not use too, or unbind it in MSFS's own controls: the app does not take the button away from the sim.

## How it works

The app LISTENS to controllers, it never reads them itself: Windows Raw Input hands it a copy of each HID input report, the same report the simulator, MobiFlight and any other program receive (CTL-3).

- `Hotkeys/JoystickButtonMonitor.cs`: registers for Generic Desktop joysticks, gamepads and multi-axis controllers with `RIDEV_INPUTSINK` (reports arrive while MSFS is in front) and `RIDEV_DEVNOTIFY` (removals), on a hidden tool window of its own. Each device is described once from its handle: vendor and product id, preparsed HID data, the hat switch's logical range, and a name (the OEM name Windows registered for the ids, else the device's product string through a handle opened with no access rights). Each report is decoded with `HidP_GetUsages` (button page) and `HidP_GetUsageValue` (hat). `DeviceFilter` lets MainForm skip devices with no binding after one dictionary lookup, so a throttle's axis reports cost almost nothing. Events are raised on the UI thread, after the report is fully read.
- `Hotkeys/ControllerButtonEdges.cs`: pure. Press edges of the buttons and the hat; the hat's four directions from a HID value (4 or 8 positions, or the angle in degrees); diagonals count as neither.
- `Hotkeys/ControllerBinding.cs`: the saved binding (`UserSettings.ControllerBindings`): vendor id, product id, device name, button, action name.
- `Hotkeys/ControllerActionCatalog.cs`: the pilot-facing name of every `HotkeyAction`; a test fails when an action has none.
- `HotkeyManager.TriggerFromController`: closes an open ] or [ mode silently and raises `HotkeyTriggered`, so the command runs through the same three-tier routing as its keyboard chord ([hotkey-system.md](hotkey-system.md)).
- `MainForm`: `OnControllerButtonPressed` finds the binding; `ApplyControllerBindings` starts the monitor only when a binding exists and filters it to the bound devices; the Settings menu handler stops it while the dialog is open (CTL-4, CTL-5).
- `Forms/Settings/ControllerButtonsPanel.cs` and `ControllerBindingDialog.cs`: the Settings tab and the capture dialog.

## History

The first version polled every controller through WinMM (`joyGetPosEx`) on a background thread. Reviewing PR #334, Robin's Honeycomb Bravo, driven by MobiFlight, stopped working in the simulator while it ran, so the monitor was rewritten on Raw Input, which cannot interfere with another reader. Button numbers are the HID button usages, the same numbers WinMM reported, so bindings made with the first version keep working.

## Limits

Buttons 1 to 100 can be bound (101 to 104 are the hat directions' numbers). Two identical controllers (same vendor and product id) share their bindings. A button held while the monitor starts counts as pressed at its next report, since Raw Input sends no initial state.

## Testing

`ControllerBindingTests` pins the pure parts. Reading real hardware is checked in the simulator: the PR's in-sim test plan, which includes a MobiFlight-driven controller.
