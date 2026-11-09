namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The A300's take-off roll call-outs ("V1", "Rotate", "V2") on the shared
/// <see cref="TakeoffVSpeedCallouts"/> machine, as the MD-11, the FBW A380 and the iFly 737 have them.
///
/// V1 and VR are the MCDU take-off page's (<c>INI_V1_FMGS</c>, <c>INI_VR_FMGS</c>, measured
/// 2026-10-04). <c>INI_V2_FMGS</c> follows the FCU speed window instead (250 with V2 typed as 150),
/// because the A300 crew sets V2 there for take-off; so it is V2 only when it is plausible: at or
/// above VR and no more than <see cref="MaxV2AboveVr"/> above it. A window left at 250 gives no V2
/// call rather than a wrong one. V1 and VR are spoken as they are entered ("V1: 148 knots") and the PFD
/// box shows all three, as on the FBW A380; V2 is never spoken (it moves with every FCU speed change).
/// Each speed's Ctrl+M row mutes its entry read-back and its call.
/// </summary>
public static class A300TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "A300_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "A300_TAKEOFF_V1";
    public const string VrKey = "A300_TAKEOFF_VR";
    public const string V2Key = "A300_TAKEOFF_V2";

    /// <summary>The feed, the speeds and their shared Ctrl+M rule.</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);

    /// <summary>Each speed's key, its variable and its name (spoken, the PFD box line and the Ctrl+M row).</summary>
    public static readonly IReadOnlyList<(string Key, string Var, string Name)> Speeds = new[]
    {
        (V1Key, "INI_V1_FMGS", "V1"),
        (VrKey, "INI_VR_FMGS", "VR"),
        (V2Key, "INI_V2_FMGS", "V2"),
    };

    /// <summary>Whether the speed's changes are spoken: V1 and VR, never V2 ([A300-11]).</summary>
    public static bool IsSpoken(string key) => key is V1Key or VrKey;

    /// <summary>A V1 or VR line: the FMS has 0 for a speed it does not have.</summary>
    public static string Speed(double knots) => knots >= 1 ? A300DisplayText.Speed(knots) : "not set";

    /// <summary>A V1 or VR entry read-back, as the FBW A380 words it: "V1: 148 knots", "V1: not set".</summary>
    public static string Entry(string key, double knots) => $"{Speeds.Single(s => s.Key == key).Name}: {Speed(knots)}";

    /// <summary>The V2 line: the FCU speed while it is plausibly V2.</summary>
    public static string V2(double vr, double fcuSpeed) =>
        PlausibleV2(vr, fcuSpeed) is > 0 and var v2 ? A300DisplayText.Speed(v2) : V2NotSet;

    /// <summary>The widest V2 the FCU speed is taken for, above VR. A judgment: a real V2 is a few
    /// knots above VR, never this far.</summary>
    public const double MaxV2AboveVr = 30;

    /// <summary>The PFD box's V2 line while the FCU speed is not plausibly V2.</summary>
    public const string V2NotSet = "not set (V2 is the FCU speed window)";

    /// <summary>The FCU speed when it is plausibly V2, else 0 (no V2 call).</summary>
    public static double PlausibleV2(double vr, double fcuSpeed) =>
        vr > 0 && fcuSpeed >= vr && fcuSpeed <= vr + MaxV2AboveVr ? fcuSpeed : 0;
}
