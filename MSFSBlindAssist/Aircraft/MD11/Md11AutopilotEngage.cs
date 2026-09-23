namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The universal auto-autopilot's MD-11 rule. TFDi disables the stock autopilot
/// (engines.cfg DisableAutopilotControls=1), so engagement is the AUTO FLIGHT button (86094/86095)
/// read back on the MD11_AP_STATE export (0 off, 1 AP1, 2 AP2, 3 both). The button never
/// disengages — pressed with the autopilot on it swaps AP1 and AP2 — so it is pressed only on a
/// DEFINITELY-off read. Unread is null: a guessed false would let a retry swap an engaged AP.
/// </summary>
public static class Md11AutopilotEngage
{
    public const string ApStateKey = "MD11_AP_STATE";
    public const string AutoflightKey = "MD11_CGS_AUTOFLIGHT_BT";

    /// <summary>TFDi quick start: autopilot allowed above 400 ft AGL (the aircraft refuses below 100).</summary>
    public const int MinimumEngageAglFt = 400;

    public static bool? Engaged(double? apState)
        => apState is double v && !double.IsNaN(v) ? v >= 0.5 : null;

    public static bool ShouldPress(double? apState) => Engaged(apState) == false;
}
