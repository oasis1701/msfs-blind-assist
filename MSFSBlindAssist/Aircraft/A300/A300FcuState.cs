namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>A variable an FCU button's label reads its state from.</summary>
public sealed record A300FcuLight(string Key, string Var, string On, string Off);

/// <summary>
/// What each FCU push button shows on its label ("Heading select: On"). The lamps are the aircraft's
/// own <c>INI_MCU_*_LIGHT</c> variables (heading select lit as its button was pressed on ground power,
/// measured 2026-10-03); the autothrottle, CWS and SPD/MACH buttons read the state they switch.
/// Each light rides the continuous batch and is consumed silently: the label is the only place it
/// shows, so pressing a button is never spoken back.
/// </summary>
public static class A300FcuState
{
    public const string SpeedMachLightKey = "A300_FCU_LT_SPDMACH";

    /// <summary>The SPD/MACH button's row. A press FLIPS the speed window between knots and Mach.</summary>
    public const string SpeedMachButtonKey = "A300_SPDMACH";

    /// <summary>FCU button row key → the light its label reads.</summary>
    public static readonly IReadOnlyDictionary<string, A300FcuLight> ByButton = new Dictionary<string, A300FcuLight>(StringComparer.Ordinal)
    {
        ["A300_ATHR_BUTTON"] = new("A300_FCU_LT_ATHR", "INI_AT_ON", "On", "Off"),
        [SpeedMachButtonKey] = new(SpeedMachLightKey, "INI_Airspeed_is_mach", "Mach", "Speed"),
        ["A300_ALTHLD_BUTTON"] = new("A300_FCU_LT_ALTHLD", "INI_MCU_ALTTUDE_HOLD_LIGHT", "On", "Off"),
        ["A300_LVLCH_BUTTON"] = new("A300_FCU_LT_LVLCH", "INI_MCU_LEVEL_CHANGE_LIGHT", "On", "Off"),
        ["A300_PROFILE_BUTTON"] = new("A300_FCU_LT_PROFILE", "INI_MCU_PROFILE_LIGHT", "On", "Off"),
        ["A300_HDGSEL_BUTTON"] = new("A300_FCU_LT_HDGSEL", "INI_MCU_HDG_SEL_LIGHT", "On", "Off"),
        ["A300_NAV_BUTTON"] = new("A300_FCU_LT_NAV", "INI_MCU_NAV_LIGHT", "On", "Off"),
        ["A300_VL_BUTTON"] = new("A300_FCU_LT_VL", "INI_MCU_LOC_LIGHT", "On", "Off"),
        ["A300_LAND_BUTTON"] = new("A300_FCU_LT_LAND", "INI_MCU_LAND_LIGHT", "On", "Off"),
        ["A300_CWS_PUSH"] = new("A300_FCU_LT_CWS", "INI_CWS_on", "On", "Off"),
    };

    public static readonly IReadOnlySet<string> LightKeys =
        ByButton.Values.Select(l => l.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>The words for a light's value.</summary>
    public static string Describe(A300FcuLight light, double value) => value >= 0.5 ? light.On : light.Off;

    /// <summary>A press read back: "VOR LOC on" for an on/off lamp; "Speed Mach: Mach" for SPD/MACH,
    /// whose two states are not on and off.</summary>
    public static string ReadBack(string name, A300FcuLight light, double value) =>
        light.On == "On" && light.Off == "Off"
            ? $"{name} {Describe(light, value).ToLowerInvariant()}"
            : $"{name}: {Describe(light, value)}";

    /// <summary>The speed window as the FCU shows it: Mach while SPD/MACH is in Mach.</summary>
    public static string SpeedWindow(double value, bool isMach) => isMach
        ? $"Mach {value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"
        : $"{Math.Round(value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)} knots";
}
