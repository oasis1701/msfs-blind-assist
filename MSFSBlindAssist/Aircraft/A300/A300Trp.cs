using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The thrust rating panel (TRP), read from the aircraft's own code (package 1.0.11):
/// <list type="bullet">
/// <item>A button press writes <c>INI_TRP_&lt;X&gt;_BUTTON</c> 1, which <c>TRP::Update</c> consumes:
/// TOGA, MCT, CL, CR, AUTO and FLEX TO set <c>INI_TRP_MODE</c> to 1 to 6 (a live probe on 2026-10-04
/// measured the same numbers). Each button's lamp is <c>INI_TRP_MODE</c> == its number (the cockpit's emissive
/// code), so a button reads "On" only while its own mode is set.</item>
/// <item>In AUTO the mode STAYS 5 (the AUTO handler writes 5, and <c>TRP::Update</c> itself writes 5 in
/// a profile climb once past the thrust reduction altitude; read from the code, not measured);
/// <c>INI_TRP_AUTO_MODE</c> holds the limit the aircraft chose, 1 TOGA, 3 climb, 4 cruise, by flight
/// phase (<c>updateN1limit</c>). The autopilot logic can also write 1 and the MCDU's MODE page 2.</item>
/// <item><c>INI_THRUST_MAXIMUM_N1</c> is the active limit's N1 (<c>updateN1limit</c> copies the mode's
/// own N1 into it). With PW engines (<c>INI_IS_PW</c> 1) the TRP shows EPR limits instead, so the N1
/// limit is read only for GE engines.</item>
/// <item><c>INI_FLEX_TEMPERATURE</c> is the FMS's flex temperature, copied there every update
/// (<c>FMGS::Update</c>), so a direct write is undone; the knob steps it one degree, 0 to 72
/// (<c>INI_flex_temp_up/dn_handler</c>), and the typed flex temperature turns the knob
/// (<see cref="A300SteppedKnobs"/>, [A300-30]).</item>
/// </list>
/// Pure; every number is formatted with the invariant culture.
/// </summary>
public static class A300Trp
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The TRP mode; also the Thrust Rating Panel's "TRP" status line.</summary>
    public const string ModeKey = "A300_TRP_MODE";
    public const string ModeVar = "INI_TRP_MODE";
    public const string AutoModeKey = "A300_TRP_AUTO_MODE";
    public const string AutoModeVar = "INI_TRP_AUTO_MODE";
    public const string N1LimitKey = "A300_TRP_N1_LIMIT";
    public const string N1LimitVar = "INI_THRUST_MAXIMUM_N1";
    public const string PwEnginesKey = "A300_ENGINES_PW";
    public const string PwEnginesVar = "INI_IS_PW";

    /// <summary>The panel whose status box carries the TRP line (the TRP's own panel).</summary>
    public const string Panel = "Thrust Rating Panel";

    /// <summary>The flex temperature knob: an encoder with no panel row, which the typed box turns
    /// (<see cref="A300SteppedKnobs.Flex"/>).</summary>
    public const string FlexKnobKey = "A300_FLEX_TEMP";

    /// <summary>TRP button row key → the mode number its lamp shows.</summary>
    public static readonly IReadOnlyDictionary<string, int> ModeByButton = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["A300_TRP_TOGA"] = 1,
        ["A300_TRP_MCT"] = 2,
        ["A300_TRP_CL"] = 3,
        ["A300_TRP_CR"] = 4,
        ["A300_TRP_AUTO"] = 5,
        ["A300_TRP_FLEXTO"] = 6,
    };

    /// <summary>Every state variable the TRP's labels and line read.</summary>
    public static readonly IReadOnlySet<string> StateKeys =
        new HashSet<string>(StringComparer.Ordinal) { ModeKey, AutoModeKey, N1LimitKey, PwEnginesKey };

    /// <summary>A button's lamp: "On" while the TRP mode is its own, else "Off"; null while unread.</summary>
    public static string? ButtonState(int mode, double? current) =>
        current is double c ? ((int)Math.Round(c) == mode ? "On" : "Off") : null;

    /// <summary>A mode or AUTO limit number in words.</summary>
    private static string Words(int mode) => mode switch
    {
        1 => "TOGA",
        2 => "max continuous",
        3 => "climb",
        4 => "cruise",
        5 => "AUTO",
        6 => "FLEX",
        _ => $"mode {mode.ToString(Inv)}",
    };

    /// <summary>
    /// The TRP line: the mode, then AUTO's chosen limit or FLEX's temperature, then the N1 limit on GE
    /// engines ("AUTO, climb, N1 limit 95.3 percent", "FLEX, 45 degrees"). A part not read is left out.
    /// </summary>
    public static string Line(double mode, double? autoMode, double? flex, double? n1Limit, double? pwEngines)
    {
        int m = (int)Math.Round(mode);
        var parts = new List<string> { Words(m) };
        if (m == 5 && autoMode is double a && (int)Math.Round(a) is >= 1 and <= 4 and var limit)
            parts.Add(Words(limit));
        if (m == 6 && flex is double f)
            parts.Add($"{Whole(f)} degrees");
        if (n1Limit is double n1 && n1 > 0 && pwEngines is double pw && pw < 0.5)
            parts.Add($"N1 limit {n1.ToString("0.0", Inv)} percent");
        return string.Join(", ", parts);
    }

    /// <summary>A typed flex temperature's confirmation, once the knob has reached it ("Flex temperature 46 degrees").</summary>
    public static string FlexPhrase(double degrees) => $"Flex temperature {Whole(degrees)} degrees";

    private static string Whole(double value)
    {
        string text = Math.Round(value).ToString("0", Inv);
        return text == "-0" ? "0" : text;
    }
}
