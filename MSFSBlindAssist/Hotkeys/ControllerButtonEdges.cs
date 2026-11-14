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
    /// A few controllers report the angle itself (a logical range of 0–359 or 0–315 degrees):
    /// more than 8 positions is read as degrees.</summary>
    public static int HatDirectionFromHid(int value, int logicalMin, int logicalMax)
    {
        int positions = logicalMax - logicalMin + 1;
        if (positions < 4 || value < logicalMin || value > logicalMax) return 0;
        long hundredths = positions > 8
            ? (value - logicalMin) * 100L
            : (value - logicalMin) * 36000L / positions;
        return HatDirection((uint)hundredths);
    }

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
