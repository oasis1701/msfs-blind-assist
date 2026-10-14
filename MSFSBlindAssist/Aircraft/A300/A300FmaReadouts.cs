namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// What Shift+A, Shift+S and Shift+H add after their FCU value: the flight mode annunciator's mode on that
/// key's axis. The altitude window is the vertical axis (the pitch column, and what is armed beside it),
/// the heading window the lateral axis (the roll column, and what is armed beside it), the speed window the
/// thrust column. A combined mode (Land, Flare, Rollout, Go around) flies both axes at once, so it replaces
/// the pitch or roll column on each, and an altitude armed beside it still reads on the vertical axis. Each
/// method returns "" or text that begins with ", " so that it follows the value: "" while the FMA is not
/// shown, as the PFD draws no guidance columns then, and for a blank column with nothing armed. The words
/// are <see cref="A300Fma"/>'s, unchanged ([A300-9]). Pure.
/// </summary>
public static class A300FmaReadouts
{
    /// <summary>
    /// The armed words on the lateral axis: the ones <see cref="A300Fma"/> decodes from
    /// <c>INI_ROLL_MODE_ARM</c>. Every other armed word is vertical. A300FmaReadoutsTests reads every value of
    /// every armed slot through the decoder, so a word the decoder gains fails there until it is placed.
    /// </summary>
    private static readonly HashSet<string> LateralArmed = new(StringComparer.Ordinal)
    {
        "NAV", "Heading select", "Localizer", "VOR",
    };

    /// <summary>Shift+A: ", pitch mode Vertical speed, Altitude armed".</summary>
    public static string AltitudeModes(A300FmaReading fma) => Axis(fma, "pitch mode", fma.Pitch, lateral: false);

    /// <summary>Shift+H: ", roll mode Heading select, NAV armed".</summary>
    public static string HeadingModes(A300FmaReading fma) => Axis(fma, "roll mode", fma.Roll, lateral: true);

    /// <summary>Shift+S: ", thrust mode Speed".</summary>
    public static string SpeedModes(A300FmaReading fma) =>
        fma.IsShown && fma.Thrust != null ? $", thrust mode {fma.Thrust}" : "";

    /// <summary>One axis: the combined mode alone, else its column ("pitch mode Vertical speed"), then each
    /// word armed on this axis, in the decoder's order.</summary>
    private static string Axis(A300FmaReading fma, string column, string? mode, bool lateral)
    {
        if (!fma.IsShown)
            return "";
        var parts = new List<string>();
        if (fma.Common != null)
            parts.Add(fma.Common);
        else if (mode != null)
            parts.Add($"{column} {mode}");
        parts.AddRange(fma.Armed.Where(word => LateralArmed.Contains(word) == lateral).Select(word => $"{word} armed"));
        return parts.Count == 0 ? "" : $", {string.Join(", ", parts)}";
    }
}
