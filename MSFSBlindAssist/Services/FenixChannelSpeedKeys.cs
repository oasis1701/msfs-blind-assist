using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The six output keys that read one speed of the Fenix PFD's tape (Shift+1 to Shift+6): green dot, S, F, VLS, the
/// stall-warning speed and VFE next. A key reads the tape row of its speed, the row the PFD window shows, worded by
/// <see cref="FenixChannelPfdRows.SpeedWords"/>, so a key and its window row cannot disagree.
/// </summary>
/// <remarks>
/// Values only, never colors. A key has nothing to read while the display is not trusted, while the PFD cannot be read
/// (the window shows a status row then), and while the tape draws no row for its speed.
/// </remarks>
internal static class FenixChannelSpeedKeys
{
    // The name of the tape row the stall-warning key reads first: VSW is on the tape in alternate law, and then replaces
    // alpha prot (FenixChannelPfdRows.TapeSpeeds).
    private const string StallWarningRow = "VSW";

    /// <summary>
    /// The name of the tape row <paramref name="action"/> reads, as <see cref="FenixChannelPfdRows.TapeSpeeds"/> names it:
    /// "Green dot", "S speed", "F speed", "VLS", "Alpha prot" (Shift+5, which reads "VSW" in its place while the tape
    /// draws one) or "VFE next". Null for any action that is not a speed key.
    /// </summary>
    public static string? NameOf(HotkeyAction action) => action switch
    {
        HotkeyAction.ReadSpeedGD => "Green dot",
        HotkeyAction.ReadSpeedS => "S speed",
        HotkeyAction.ReadSpeedF => "F speed",
        HotkeyAction.ReadSpeedVLS => "VLS",
        HotkeyAction.ReadSpeedVS => "Alpha prot",
        HotkeyAction.ReadSpeedVFE => "VFE next",
        _ => null
    };

    /// <summary>
    /// What <paramref name="action"/> reads aloud for <paramref name="snapshot"/>: its tape row in whole knots ("Green dot
    /// 237 knots"). Null when the action is no speed key, when the snapshot's verdict is not trusted, when the PFD cannot
    /// be read, or when the tape has no row for the speed. The problem of a PFD that cannot be read, the one the window
    /// shows as its status row, also goes to <paramref name="reportProblem"/>; the monitor already logs why a display is
    /// not trusted, so that is not reported again.
    /// </summary>
    public static string? Spoken(HotkeyAction action, FenixChannelSnapshot snapshot, Action<string> reportProblem)
    {
        string? name = NameOf(action);
        if (name is null || !snapshot.Verdict.Trusted) return null;

        FenixChannelSection<FenixChannelPfd> section = FenixChannelPfd.From(snapshot);
        if (!section.IsOk)
        {
            reportProblem(section.Problem ?? "no data");
            return null;
        }

        IReadOnlyList<(string Name, double Knots)> tape = FenixChannelPfdRows.TapeSpeeds(section.Value!);
        string[] rows = action == HotkeyAction.ReadSpeedVS ? [StallWarningRow, name] : [name];

        foreach (string row in rows)
        {
            foreach ((string tapeName, double knots) in tape)
            {
                if (tapeName == row) return FenixChannelPfdRows.SpeedWords(tapeName, knots);
            }
        }

        return null;
    }
}
