using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The Fenix display channel's announcements, each with its own Fenix Monitor Manager (Ctrl+M) row: the one place their
/// mute keys and row labels are written, as <see cref="FenixCabinAnnouncements"/> is for the cabin's. An unticked row
/// puts its key in <c>UserSettings.FenixDisabledMonitorVariables</c>, and the announcer of that row asks
/// <see cref="IsMuted"/> each time it is about to speak, so a mute applies at once. An announcer keeps tracking while its
/// row is muted, so unmuting replays nothing. The keys are stable (a saved mute names them) and are no variable's key.
/// </summary>
/// <remarks>
/// Every row starts on: nothing seeds a mute for any of them.
/// </remarks>
public static class FenixChannelAnnouncementRows
{
    /// <summary>The FMA announcer's row.</summary>
    public const string FmaKey = "FENIX_CHANNEL_FMA";

    /// <summary>The E/WD alert announcer's warnings row.</summary>
    public const string WarningsKey = "FENIX_CHANNEL_WARNINGS";

    /// <summary>The E/WD alert announcer's cautions row.</summary>
    public const string CautionsKey = "FENIX_CHANNEL_CAUTIONS";

    /// <summary>The E/WD alert announcer's memos row.</summary>
    public const string MemosKey = "FENIX_CHANNEL_MEMOS";

    /// <summary>The DCDU announcer's row.</summary>
    public const string DcduKey = "FENIX_CHANNEL_DCDU";

    /// <summary>The ND messages announcer's row.</summary>
    public const string NdKey = "FENIX_CHANNEL_ND";

    /// <summary>
    /// The Ctrl+M rows, in this order; <c>FenixMonitorManagerForm</c> appends them after the cabin rows.
    /// </summary>
    public static IReadOnlyList<MonitorRow> Rows { get; } = Array.AsReadOnly(new MonitorRow[]
    {
        new(FmaKey, "FMA changes"),
        new(WarningsKey, "E/WD warnings"),
        new(CautionsKey, "E/WD cautions"),
        new(MemosKey, "E/WD memos"),
        new(DcduKey, "DCDU messages"),
        new(NdKey, "ND messages")
    });

    /// <summary>
    /// Whether a row is unticked in Ctrl+M: asked as an announcer is about to speak. It reads the live mute set of the
    /// current settings on each call, as <see cref="FenixCabinReadyMonitor"/> does, and keeps no copy, so a tick or an
    /// untick (the dialog saves the settings, which rebuilds the set read here) counts at once.
    /// </summary>
    public static bool IsMuted(string key)
        => SettingsManager.Current.FenixDisabledMonitorVariablesSet.Contains(key);
}
