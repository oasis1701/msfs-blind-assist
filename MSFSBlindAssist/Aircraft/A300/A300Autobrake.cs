namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One autobrake button: the level it arms, its spoken name, and its DECEL light's variable.</summary>
public sealed record A300AutobrakeButton(int Level, string Name, string DecelKey, string DecelVar)
{
    /// <summary>The board's id for the button's armed segment, which has no variable of its own.</summary>
    public string ArmedLampId => DecelKey.Replace("_DECEL", "_ARMED", StringComparison.Ordinal);
}

/// <summary>
/// The LO, MED and MAX autobrake buttons, read from the aircraft's own code (package 1.0.11):
/// <list type="bullet">
/// <item>Each button has two lamp segments, both on AC light power (<see cref="A300LampBoard"/>). The
/// lower one lights while <c>INI_AUTOBRAKE_LEVEL</c> is its level (1 LO, 2 MED, 3 MAX; 0 off) and its
/// DECEL segment is dark; the DECEL segment lights from <c>INI_AUTOBRAKE_LOW/MED/HI_DECEL</c> (the
/// cockpit's emissive code), which the aircraft sets only at that level, engaged, past 1.36 of
/// deceleration (<c>update_ab_datarefs</c>). So a label reads "Armed", "Decel" or "Off", as the lamp
/// does (<see cref="Describe"/> reads an unread DECEL as "Armed", which suits a label), and the lights are
/// spoken as they change; the board decides the armed one only once its DECEL input has been read.</item>
/// <item>A press writes <c>INI_ABRK_LOW/MED/HI_COMMAND</c>, which the aircraft consumes: the active
/// level pressed again disarms it (level 0) by design; any other level arms only with the gear handle
/// down, green hydraulics at 2,200 psi or more, no autobrake fault, and, with the parking brake off,
/// <c>INI_brake_system</c> at 0 (<c>INI_autobrake_low_handler</c> and its two siblings). A press says
/// nothing about its own button's armed light: the screen reader reads the relabeled button, so the
/// light is left to the label for a few seconds ([CORE-7], <c>IniA300Definition.PressedLampMs</c>), as
/// for every light-labeled A300 button. The light of the level that was armed going out, the DECEL lights
/// and any change the pilot did not make are spoken. A press the aircraft refuses changes no light, so
/// nothing is said.</item>
/// <item><c>INI_autobrake_armed</c> is NOT the state: only the three button handlers write it, while
/// the aircraft's own disarms (the gear handle leaving down, its disconnects in <c>INI_autobrake</c>)
/// set the level to 0 and leave it at 1 ([A300-20]).</item>
/// </list>
/// Pure.
/// </summary>
public static class A300Autobrake
{
    public const string LevelKey = "A300_AUTOBRAKE_LEVEL";
    public const string LevelVar = "INI_AUTOBRAKE_LEVEL";

    /// <summary>Button row key → its level, name and DECEL light.</summary>
    public static readonly IReadOnlyDictionary<string, A300AutobrakeButton> ByButton =
        new Dictionary<string, A300AutobrakeButton>(StringComparer.Ordinal)
        {
            ["A300_AUTO_BRK_LO"] = new(1, "Autobrake low", "A300_AUTOBRAKE_LOW_DECEL", "INI_AUTOBRAKE_LOW_DECEL"),
            ["A300_AUTO_BRK_MID"] = new(2, "Autobrake medium", "A300_AUTOBRAKE_MED_DECEL", "INI_AUTOBRAKE_MED_DECEL"),
            ["A300_AUTO_BRK_MAX"] = new(3, "Autobrake max", "A300_AUTOBRAKE_HI_DECEL", "INI_AUTOBRAKE_HI_DECEL"),
        };

    /// <summary>Every state variable the lamps read: the level, then the three DECEL lights.</summary>
    public static readonly IReadOnlySet<string> StateKeys =
        ByButton.Values.Select(b => b.DecelKey).Prepend(LevelKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>A button's lamp in words: "Decel", "Armed" or "Off" ("Off" on a dark AC bus, as the lamp
    /// is); null while the level or the AC light power is unread.</summary>
    public static string? Describe(A300AutobrakeButton button, double? level, double? decel, double? acPower)
    {
        if (level is not double l || acPower is not double ac)
            return null;
        if (ac < 0.5 || (int)Math.Round(l) != button.Level)
            return "Off";
        return decel is double d && d >= 0.5 ? "Decel" : "Armed";
    }
}
