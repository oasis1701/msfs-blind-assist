namespace MSFSBlindAssist.Hotkeys;

/// <summary>Pure press detection for <see cref="JoystickButtonMonitor"/>: which buttons went
/// from released to pressed between two input reports. Only the press edge fires, so holding a
/// button runs its command once, and a release never runs anything.</summary>
public static class ControllerButtonEdges
{
    /// <summary>The hat direction a POV angle (hundredths of a degree, clockwise from up)
    /// points to, or 0 when centred. Only the four cardinal directions count, each within 22.5°
    /// either side: a diagonal is between two directions and runs neither, so a thumb rolling
    /// across the hat cannot fire the neighbour by accident.</summary>
    public static int HatDirection(uint pov)
    {
        if (pov >= 36000) return 0; // centred or out of range
        double degrees = pov / 100.0;
        if (degrees >= 337.5 || degrees < 22.5) return ControllerBinding.HatUp;
        if (degrees >= 67.5 && degrees < 112.5) return ControllerBinding.HatRight;
        if (degrees >= 157.5 && degrees < 202.5) return ControllerBinding.HatDown;
        if (degrees >= 247.5 && degrees < 292.5) return ControllerBinding.HatLeft;
        return 0;
    }

    /// <summary>The hat direction of a HID hat-switch value. A HID hat reports one of
    /// <c>logicalMax - logicalMin + 1</c> positions spread evenly clockwise from up (8 on most
    /// controllers, 4 on some), and a value outside that range when centred (its "null state").
    /// A few controllers report the angle itself (a logical range of 0–359, 0–315 or 0–270
    /// degrees): a range that reaches 270 is read as degrees. A range of 9 to 270 values is
    /// 8 directions with the centred value INSIDE the declared range (0–8 with 8 centred, or
    /// the whole 4 bits, 0–15): anything past the eighth position is centred.</summary>
    public static int HatDirectionFromHid(int value, int logicalMin, int logicalMax)
    {
        int positions = logicalMax - logicalMin + 1;
        if (positions < 4 || value < logicalMin || value > logicalMax) return 0;
        int index = value - logicalMin;
        if (logicalMax - logicalMin >= 270) return HatDirection((uint)(index * 100L));
        if (positions > 8)
        {
            if (index >= 8) return 0;
            positions = 8;
        }
        return HatDirection((uint)(index * 36000L / positions));
    }

    /// <summary>What a device's FIRST report may fire. Raw Input sends no initial state, so that
    /// report carries everything the device already holds (a latched switch, a lever in a
    /// detent) as well as the button that caused it. One button down is taken as the press, so
    /// a yoke at rest answers its first press; with several down nothing says which one is the
    /// press, so they become the starting point and nothing fires: firing the lowest number
    /// would run, or capture, a switch the pilot never touched.</summary>
    public static IReadOnlyList<int> FromFirstReport(IReadOnlyList<int> pressed)
        => pressed.Count == 1 ? pressed : Array.Empty<int>();

    /// <summary>The buttons and hat direction pressed now that were not pressed in the previous
    /// report, buttons in ascending order, the hat last.</summary>
    public static List<int> NewlyPressed(IReadOnlySet<int> previousButtons, int previousHat,
                                         IReadOnlySet<int> currentButtons, int currentHat)
    {
        var pressed = currentButtons.Where(b => !previousButtons.Contains(b)).OrderBy(b => b).ToList();
        if (currentHat != 0 && currentHat != previousHat)
            pressed.Add(currentHat);
        return pressed;
    }
}
