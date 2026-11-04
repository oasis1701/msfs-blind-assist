namespace MSFSBlindAssist.Services;

/// <summary>
/// The captain's EFIS controls the ND window carries under its list: the Mode and Range combos and the six map option
/// buttons, with the panel's own variable for each and the words a button reads. Pure: the window builds the controls
/// and applies this state to them. The numbers are the panel's (<c>FenixA320Definition</c>'s <c>S_FCU_EFIS1_*</c>
/// variables); the window writes them through the panel's own path and never pulses a button itself.
/// </summary>
internal static class FenixChannelNdControls
{
    /// <summary>The panel's Mode variable: the combo's index is its value (0 LS to 4 PLAN).</summary>
    public const string ModeKey = "S_FCU_EFIS1_ND_MODE";

    /// <summary>The panel's Range variable: the combo's index is its value (0 for 10 to 5 for 320).</summary>
    public const string RangeKey = "S_FCU_EFIS1_ND_ZOOM";

    /// <summary>The Mode combo's items, in the panel's value order.</summary>
    public static readonly IReadOnlyList<string> Modes = ["LS", "VOR", "NAV", "ARC", "PLAN"];

    /// <summary>The Range combo's items, in the panel's value order.</summary>
    public static readonly IReadOnlyList<string> Ranges = ["10", "20", "40", "80", "160", "320"];

    /// <summary>The six buttons, left to right: the name the ND uses for the option, and the panel's press variable.</summary>
    public static readonly IReadOnlyList<(string Name, string Key)> Buttons =
    [
        ("CSTR", "S_FCU_EFIS1_CSTR"),
        ("WPT", "S_FCU_EFIS1_WPT"),
        ("VOR.D", "S_FCU_EFIS1_VORD"),
        ("NDB", "S_FCU_EFIS1_NDB"),
        ("ARPT", "S_FCU_EFIS1_ARPT"),
        ("LS", "S_FCU_EFIS1_LS_PRESS")
    ];

    /// <summary>
    /// What a button reads: "CSTR: on" when the ND shows that option ("LS: on" when the LS is on), "CSTR: off"
    /// otherwise, and the bare name while the ND is not trusted (<paramref name="header"/> null), so a state is never
    /// guessed.
    /// </summary>
    public static string ButtonText(string name, FenixNdData? header)
    {
        if (header is null) return name;

        bool on = name == "LS" ? header.LsOn : header.MapOption == name;
        return name + (on ? ": on" : ": off");
    }

    /// <summary>The Mode combo's index for the ND's mode word, or -1 (no selection) for one that is not in the list.</summary>
    public static int ModeIndex(string? mode) => IndexOf(Modes, mode);

    /// <summary>The Range combo's index for the ND's range figure, or -1 (no selection) for one that is not in the list.</summary>
    public static int RangeIndex(string? range) => IndexOf(Ranges, range);

    private static int IndexOf(IReadOnlyList<string> items, string? value)
    {
        if (value is null) return -1;

        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], value, StringComparison.Ordinal)) return i;
        }

        return -1;
    }
}
