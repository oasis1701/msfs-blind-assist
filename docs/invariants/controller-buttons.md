# Controller buttons — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/controller-buttons.md`, which Claude Code loads when it reads matching code. Background: [controller-buttons.md](../controller-buttons.md).

## CTL-1

- A binding names its device by USB vendor and product id (`ControllerBinding.VendorId`, `ProductId`), never a device handle or enumeration slot, and its command by the enum NAME (`ControllerBinding.Action`), never the number; `TryGetAction` accepts exact names only. → [controller-buttons.md](../controller-buttons.md)

Why. A Raw Input device handle is valid only until the device is unplugged, and a joystick slot is the order Windows enumerated the controllers in: both change when a pilot plugs the yoke into another port, or the throttle before the yoke, and a binding stored that way would answer on the wrong device. The USB ids belong to the device. `HotkeyAction` has no explicit values, so inserting a member in the middle renumbers everything after it; a saved number would silently move onto another command, while a saved name either still means the same command or stops parsing (shown as "no longer available" and skipped). `Enum.TryParse` alone also accepts "5" and "A, B", which is why `TryGetAction` checks the name against the enum's names first.

## CTL-2

- A bound press runs only through `HotkeyManager.TriggerFromController`: it closes an open ] or [ mode silently first (VG-2) and runs nothing while the hotkeys are suspended; never raise `HotkeyTriggered` for a controller any other way. → [controller-buttons.md](../controller-buttons.md)

Why. A pilot may have pressed ] on the keyboard and then reached for the yoke. Left open, output mode keeps holding the single-letter keys, and hand fly or visual guidance started from the yoke would find its quick-access keys taken (VG-2). Closing the mode the way a chord inside it closes it (`Deactivated`, which MainForm does not speak) leaves the app as the keyboard would. The menu's Suspend Hotkeys is how a pilot stops the app answering while they type elsewhere; a yoke button that still ran commands would break that promise.

## CTL-3

- `JoystickButtonMonitor` only LISTENS, through Raw Input (`RIDEV_INPUTSINK`): never poll, open for reading or acquire a controller (WinMM joy calls, DirectInput, HidD_GetInputReport); a MobiFlight-driven throttle stopped working under the WinMM version. It fires press edges only (`ControllerButtonEdges.NewlyPressed`). → [controller-buttons.md](../controller-buttons.md)

Why. The first version (PR #334) polled every controller through WinMM's `joyGetPosEx` every 30 ms and probed the 16 joystick slots, empty ones included, every 3 s. Reviewing it, Robin found his Honeycomb Bravo throttle quadrant, which MobiFlight drives, no longer worked in the simulator while it ran. Raw Input is a copy of each HID input report that Windows hands to every registered reader alike: this app never touches the device, so nothing it does can block the simulator, MobiFlight or another reader. The one handle it opens is with no access rights, once per device and only when one of its buttons is pressed, to ask the HID class driver for the product name. Firing on the press edge means a held button runs its command once and a release runs nothing. Raw Input sends no state at registration, and throttle quadrants hold buttons down all the time (a lever resting in a detent, a latched switch) while their axes report continuously: starting every device from "nothing pressed", the capture dialog caught such a button the instant it started listening (the owner's Thrustmaster Boeing and WinCTRL Airbus quadrants, 2026-11-09). So for `SettleMs` (500 ms) after each start, reports only set each device's state and fire nothing; what is held then is the starting point. A quiet device whose first report comes later still starts from "nothing pressed". Asking a device for its current state instead (HidD_GetInputReport) would talk to it, which is what this rule forbids.

## CTL-4

- The live controller reading is stopped while the Settings dialog is open and restarted from the saved bindings on every close path, and it runs only while at least one button is bound. → [controller-buttons.md](../controller-buttons.md)

Why. The Controller Buttons tab captures a press with its own monitor. If MainForm's kept running, pressing a button that is already bound, to reassign it, would also run its old command, and the two monitors would fight over the process's one Raw Input registration (CTL-5). Restarting in a `finally` after `ShowDialog`, from `SettingsManager.Current`, covers OK (the new bindings), Cancel and the close box (the unchanged ones). A pilot who never binds a button registers no controller input at all.

## CTL-5

- Only one `JoystickButtonMonitor` may be started at a time: Windows keeps ONE Raw Input target per device type per process, so a second `Start` would steal the first one's input and its `Stop` (`RIDEV_REMOVE`) would unregister both; `Start` refuses while another is running. → [controller-buttons.md](../controller-buttons.md)

Why. `RegisterRawInputDevices` keys its registrations on the usage page and usage, not on the window: registering joysticks for a second window moves them there, and removing them removes them for the whole process. The capture dialog and MainForm therefore never run theirs together (CTL-4). Any other part of the app that wants controller input must go through the running monitor rather than register its own.
