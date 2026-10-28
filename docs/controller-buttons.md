# Controller buttons

A pilot can bind a button on a yoke, joystick, throttle or gamepad (a hat direction too) to any hotkey command. The button runs the command directly, with no ] or [ key first, whichever window is in front. Taxiing with one hand on the tiller and the other on the yoke, the pilot asks for the ground speed or the next taxiway without reaching for the keyboard.

Rules: [invariants/controller-buttons.md](invariants/controller-buttons.md).

## For the pilot

Settings, Controller Buttons tab: Add button opens a dialog with the command list (every hotkey command, each name ending with its keyboard chord, such as "Ground speed (] G)"), a Capture button and a status box. After Capture the pilot presses the controller button within 15 seconds; what was captured is spoken and written to the status box. A button that is already bound says so, and OK replaces its command. The list shows each binding as command, controller, button; Delete or the Remove button removes one. Nothing is read from any controller until a button is bound.

Bind a button the simulator does not use too, or unbind it in MSFS's own controls: the app does not take the button away from the sim.

## How it works

| Piece | Does |
| --- | --- |
| `Hotkeys/JoystickButtonMonitor.cs` | Reads every controller through WinMM (`joyGetPosEx`) on a background thread every 30 ms, rescans for plugged and unplugged devices every 3 s, posts `ButtonPressed` to the UI thread. Names a device from the OEM name its HID driver registered (`MediaProperties\PrivateProperties\Joystick\OEM\VID_xxxx&PID_xxxx`). |
| `Hotkeys/ControllerButtonEdges.cs` | Pure: press edges of the 32 buttons and the hat, the hat's four directions (diagonals count as neither). |
| `Hotkeys/ControllerBinding.cs` | The saved binding (`UserSettings.ControllerBindings`): vendor id, product id, device name, button, action name. |
| `Hotkeys/ControllerActionCatalog.cs` | The pilot-facing name of every `HotkeyAction`; a test fails when an action has none. |
| `HotkeyManager.TriggerFromController` | Closes an open ] or [ mode silently and raises `HotkeyTriggered`, so the command runs through the same three-tier routing as its keyboard chord ([hotkey-system.md](hotkey-system.md)). |
| `MainForm` | `OnControllerButtonPressed` finds the binding; `ApplyControllerBindings` starts the monitor only when a binding exists; the Settings menu handler stops it while the dialog is open. |
| `Forms/Settings/ControllerButtonsPanel.cs`, `ControllerBindingDialog.cs` | The Settings tab and the capture dialog. |

## Limits

WinMM sees 16 controllers and 32 buttons per controller; a button numbered above 32 cannot be bound. Two identical controllers (same vendor and product id) share their bindings. The WinMM API needs no package and no window focus, which is why it was chosen over DirectInput or Windows.Gaming.Input.

## Testing

`ControllerBindingTests` pins the pure parts. Reading real hardware is checked in the simulator: the PR's in-sim test plan.
