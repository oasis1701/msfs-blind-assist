namespace MSFSBlindAssist.Hotkeys;

/// <summary>Pure press detection for <see cref="JoystickButtonMonitor"/>: which buttons went
/// from released to pressed between two polls. Only the press edge fires, so holding a button
/// runs its command once, and a release never runs anything.</summary>
public static class ControllerButtonEdges
{
    /// <summary>WinMM's "hat centred" value (JOY_POVCENTERED).</summary>
    public const uint PovCentred = 0xFFFF;

    /// <summary>The hat direction a WinMM POV reading (hundredths of a degree, clockwise from
    /// up) points to, or 0 when centred. Only the four cardinal directions count, each within
    /// 22.5° either side: a diagonal is between two directions and runs neither, so a thumb
    /// rolling across the hat cannot fire the neighbour by accident.</summary>
    public static int HatDirection(uint pov)
    {
        if (pov >= 36000) return 0; // centred (0xFFFF) or out of range
        double degrees = pov / 100.0;
        if (degrees >= 337.5 || degrees < 22.5) return ControllerBinding.HatUp;
        if (degrees >= 67.5 && degrees < 112.5) return ControllerBinding.HatRight;
        if (degrees >= 157.5 && degrees < 202.5) return ControllerBinding.HatDown;
        if (degrees >= 247.5 && degrees < 292.5) return ControllerBinding.HatLeft;
        return 0;
    }

    /// <summary>The buttons (1–32) and hat direction pressed now that were not pressed at the
    /// previous poll, in button order, the hat last.</summary>
    public static List<int> NewlyPressed(uint previousButtons, int previousHat, uint currentButtons, int currentHat)
    {
        var pressed = new List<int>();
        uint rising = currentButtons & ~previousButtons;
        for (int bit = 0; bit < 32; bit++)
        {
            if ((rising & (1u << bit)) != 0)
                pressed.Add(bit + 1);
        }
        if (currentHat != 0 && currentHat != previousHat)
            pressed.Add(currentHat);
        return pressed;
    }
}
