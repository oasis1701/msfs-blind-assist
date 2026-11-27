namespace MSFSBlindAssist.Hotkeys;

/// <summary>One controller button (a yoke, joystick or throttle button, or a hat direction)
/// bound to a <see cref="HotkeyAction"/>. Persisted in <c>UserSettings.ControllerBindings</c>.
///
/// The device is identified by its USB vendor and product ids, never by its Raw Input device
/// handle or a joystick slot: the handle lasts only until the device is unplugged and the slot
/// is the order Windows enumerated the devices in, so both change when the pilot plugs them
/// into other ports or in another order. <see cref="DeviceName"/> is display
/// only, so a renamed or relabelled device still matches.
///
/// <see cref="Action"/> stores the enum NAME, not its number: <see cref="HotkeyAction"/> has no
/// explicit values, so a member inserted in the middle would silently move every saved number
/// onto a different command. A name that no longer parses (a retired action) is skipped.</summary>
public class ControllerBinding
{
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public string DeviceName { get; set; } = "";

    /// <summary>1–100 for the buttons (the HID button usage, up to
    /// <c>JoystickButtonMonitor.MaxButton</c>), or one of the <c>Hat*</c> constants.</summary>
    public int Button { get; set; }

    public string Action { get; set; } = "";

    // Hat-switch (POV) directions, stored as button numbers the monitor never reports for a
    // button (it ignores buttons above 100).
    public const int HatUp = 101;
    public const int HatRight = 102;
    public const int HatDown = 103;
    public const int HatLeft = 104;

    private static readonly HashSet<string> ActionNames = new(Enum.GetNames<HotkeyAction>(), StringComparer.Ordinal);

    // Exact names only: Enum.TryParse would also accept "5" or "ReadHeading, ReadSpeed".
    public bool TryGetAction(out HotkeyAction action)
    {
        action = default;
        return ActionNames.Contains(Action) && Enum.TryParse(Action, ignoreCase: false, out action);
    }

    public bool Matches(int vendorId, int productId, int button)
        => VendorId == vendorId && ProductId == productId && Button == button;

    /// <summary>"button 5", "hat up". Spoken and listed; the pilot never sees the raw number for a hat.</summary>
    public static string ButtonLabel(int button) => button switch
    {
        HatUp => "hat up",
        HatRight => "hat right",
        HatDown => "hat down",
        HatLeft => "hat left",
        _ => $"button {button}",
    };
}
