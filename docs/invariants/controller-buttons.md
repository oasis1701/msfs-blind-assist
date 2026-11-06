# Controller buttons — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/controller-buttons.md`, which Claude Code loads when it reads matching code. Background: [controller-buttons.md](../controller-buttons.md).

## CTL-1

- A binding names its device by USB vendor and product id (`ControllerBinding.VendorId`, `ProductId`), never the WinMM joystick slot, and its command by the enum NAME (`ControllerBinding.Action`), never the number; `TryGetAction` accepts exact names only. → [controller-buttons.md](../controller-buttons.md)

Why. The WinMM slot (0–15) is the order Windows enumerated the controllers in. It changes when a pilot plugs the yoke into another port, or the throttle before the yoke, and a binding stored by slot would then answer on the wrong device. The USB ids belong to the device. `HotkeyAction` has no explicit values, so inserting a member in the middle renumbers everything after it; a saved number would silently move onto another command, while a saved name either still means the same command or stops parsing (shown as "no longer available" and skipped). `Enum.TryParse` alone also accepts "5" and "A, B", which is why `TryGetAction` checks the name against `Enum.GetNames` first.

## CTL-2

- A bound press runs only through `HotkeyManager.TriggerFromController`: it closes an open ] or [ mode silently first (VG-2) and runs nothing while the hotkeys are suspended; never raise `HotkeyTriggered` for a controller any other way. → [controller-buttons.md](../controller-buttons.md)

Why. A pilot may have pressed ] on the keyboard and then reached for the yoke. Left open, output mode keeps holding the single-letter keys, and hand fly or visual guidance started from the yoke would find its quick-access keys taken (VG-2). Closing the mode the way a chord inside it closes it (`Deactivated`, which MainForm does not speak) leaves the app as the keyboard would. The menu's Suspend Hotkeys is how a pilot stops the app answering while they type elsewhere; a yoke button that still ran commands would break that promise.

## CTL-3

- `JoystickButtonMonitor` probes empty joystick slots only at its rescan, never at the poll rate; it fires press edges only (`ControllerButtonEdges.NewlyPressed`) and seeds a newly found device silently, so a held button never fires. → [controller-buttons.md](../controller-buttons.md)

Why. `joyGetPosEx` on a slot with nothing plugged in is slow (Windows re-reads the joystick registry), and calling it for all 16 slots at the 30 ms poll rate is a known cause of stutter. The monitor polls only the devices its last rescan (every 3 s) found. Firing on the press edge means a held button runs its command once and a release runs nothing. A device found at a rescan (and every device when the monitor starts, as it does again after the Settings dialog) starts from the state it reads then, so a button already held does not fire.

## CTL-4

- The live controller reading is stopped while the Settings dialog is open and restarted from the saved bindings on every close path, and it runs only while at least one button is bound. → [controller-buttons.md](../controller-buttons.md)

Why. The Controller Buttons tab captures a press with its own monitor. If MainForm's kept running, pressing a button that is already bound, to reassign it, would also run its old command. Restarting in a `finally` after `ShowDialog`, from `SettingsManager.Current`, covers OK (the new bindings), Cancel and the close box (the unchanged ones). A pilot who never binds a button gets no polling thread at all.
