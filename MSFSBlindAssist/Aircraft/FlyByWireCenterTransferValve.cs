namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// One side of the SD FUEL page's center-to-inner transfer valve, as the A32NX SD draws it (FBW
/// <c>Fuel.tsx</c>, <c>CentreToInnerTransfer</c>; the Headwind A339X's <c>sd.js</c> carries the same
/// component over the same fuel system). The valve is a pair in series: the auto transfer valve
/// the fuel-system triggers move (<c>FUELSYSTEM VALVE OPEN:11</c> left, <c>:12</c> right) and the
/// valve the CTR TK XFR button commands (<c>:9</c> / <c>:10</c>, button
/// <c>FUELSYSTEM VALVE SWITCH:9</c> / <c>:10</c>). Junction 4 / 5 above 1.5 is MANUAL routing,
/// which bypasses the auto valve.
///
/// The SD draws the valve closed under 1 % open and open otherwise, amber when it is open with its
/// button off, or in AUTO (the MODE SEL L:var, not the junction) while it is not fully open (over
/// 99 %). Its XX fault symbol needs the valve both over 99 % and under 1 %, so it never shows, and
/// a closed valve is never amber; the transfer arrow is drawn exactly when the valve is open, in
/// the same color, so it adds nothing to say. "Amber" is capitalized as the E/WD readout's color
/// names are (<c>EWDMessageLookup.GetMessagePriority</c>). A NaN input (a variable the airframe
/// lacks) counts as unread.
///
/// FBW #10971 (acbfe58df) closes the auto valves when an inner tank falls below 1,651 gallons, so
/// with an empty center tank this reads closed in AUTO although the button valve is open.
/// </summary>
public static class FlyByWireCenterTransferValve
{
    public static string Describe(double? autoValveOpen, double? transferValveOpen, double? junctionSetting,
                                  double? buttonOn, double? modeSelectManual)
    {
        if (Read(transferValveOpen) is not double transfer || Read(junctionSetting) is not double junction) return "--";

        double position;
        if (junction > 1.5) position = transfer;
        else if (Read(autoValveOpen) is double auto) position = auto > 0 ? transfer : 0;
        else return "--";

        if (position < 0.01) return "closed";

        // An unread sibling gives no color rather than a guess; a button read as off settles it.
        if (Read(buttonOn) is not double button) return "open";
        if (button < 0.5) return "open, Amber";
        if (position > 0.99 || Read(modeSelectManual) is not double manual) return "open";
        return manual > 0.5 ? "open" : "open, Amber";
    }

    private static double? Read(double? value) => value is double v && !double.IsNaN(v) ? v : null;
}
