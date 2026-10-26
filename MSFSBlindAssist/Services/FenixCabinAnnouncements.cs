namespace MSFSBlindAssist.Services;

/// <summary>
/// The kinds of app-wide cabin announcement <see cref="FenixCabinReadyMonitor"/> makes while the Fenix is loaded,
/// each with its own Fenix Monitor Manager (Ctrl+M) row: the one place their mute keys and row labels are
/// written. An unticked row puts its key in <c>UserSettings.FenixDisabledMonitorVariables</c>, which the
/// monitor reads each time it is about to speak that kind, so a mute applies at once. The keys are stable
/// (a saved mute names them) and are no variable's key.
/// </summary>
public static class FenixCabinAnnouncements
{
    /// <summary>The cabin-ready call (<see cref="FenixCabinReadyMonitor.SpokenText"/>).</summary>
    public const string CabinReadyKey = "FENIX_CABIN_READY";

    /// <summary>A passenger or cargo door opening or closing: "Door 1 left open", "Forward cargo door closed".</summary>
    public const string DoorsKey = "FENIX_CABIN_DOORS";

    /// <summary>The passenger doors' slides all armed or all disarmed: "Slides armed", "Slides disarmed".</summary>
    public const string SlidesKey = "FENIX_CABIN_SLIDES";

    /// <summary>A FAP caution appearing: "Cabin caution: Water service door open".</summary>
    public const string CautionsKey = "FENIX_CABIN_CAUTIONS";

    /// <summary>
    /// The four Ctrl+M rows, in this order; <c>FenixMonitorManagerForm</c> appends them after the variable
    /// rows. The cabin-ready row is named by the words it mutes.
    /// </summary>
    public static IReadOnlyList<MonitorRow> Rows { get; } = Array.AsReadOnly(new MonitorRow[]
    {
        new(CabinReadyKey, FenixCabinReadyMonitor.SpokenText),
        new(DoorsKey, "Cabin doors opening and closing"),
        new(SlidesKey, "Cabin slides armed and disarmed"),
        new(CautionsKey, "Cabin cautions")
    });
}
