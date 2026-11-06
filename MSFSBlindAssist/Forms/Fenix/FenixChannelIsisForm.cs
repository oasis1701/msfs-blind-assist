using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The ISIS's window ("Fenix ISIS"): the integrated standby instrument as <see cref="FenixChannelIsisRows"/> words it, and
/// under the list the ISIS panel's buttons. It is a class of its own because Fenix windows are tracked one for each type
/// and each display therefore needs a type of its own (<see cref="FenixChannelDisplayForm"/>). It reads the variant at
/// each refresh, so a Fenix found to have the round standby gauges after the window opened shows that in its status row.
/// </summary>
/// <remarks>
/// <para>The buttons. One for each of the ISIS panel's controls (<see cref="IsisKeys"/>), after the list in tab order, so
/// that a pilot can press one and read what it did in the one window: the bugs page, a bug's value, the LS scales, the
/// baro setting. Added at the owner's word after the flight of 2026-11-05 (spec row 32 changed), as the EFIS, ECAM and
/// DCDU keys are in their windows. The baro knob is here too, by the owner's choice the same day: Ctrl+B sets the ISIS baro
/// as well, but on the bugs page the knob steps the selected bug's value, the only way to set one. A press calls the
/// press delegate with the key's variable name and nothing else: the definition's own press of that key, the panel's code
/// (a full press-release for a button, [FNX-6], [A320-3], RST held 3 s; a counter step for the knob, [FNX-5]).</para>
/// <para>Silence ([CORE-7]). The window holds no announcer and no SimConnect: what the panel's code announces (RST and the
/// baro push say "pressed") is the one confirmation of a press, as when the pilot presses the panel's button, and the
/// list shows the rest at its next refresh. A refresh, F5 and opening the window never press.</para>
/// </remarks>
internal sealed class FenixChannelIsisForm : FenixChannelDisplayForm
{
    /// <summary>
    /// The ISIS panel's controls the window carries, all of them, in the panel's order: the label the button shows and says
    /// (the bezel's word, with plus and minus spelled out for the reader; the knob's three in the panel's words), and the
    /// panel's own variable (<c>FenixA320Definition</c>'s "ISIS" panel). Data, so that a test reads it and holds it to the
    /// panel.
    /// </summary>
    internal static readonly IReadOnlyList<(string Label, string Key)> IsisKeys =
    [
        ("BUGS", "S_MIP_ISFD_BUGS"),
        ("LS", "S_MIP_ISFD_LS"),
        ("Plus", "S_MIP_ISFD_PLUS"),
        ("Minus", "S_MIP_ISFD_MINUS"),
        ("RST", "S_MIP_ISFD_RST"),
        ("Baro Inc", "E_MIP_ISFD_BARO_INC"),
        ("Baro Dec", "E_MIP_ISFD_BARO_DEC"),
        ("Baro Push", "S_MIP_ISFD_BARO_BUTTON")
    ];

    /// <param name="monitor">The channel monitor the ISIS's snapshots come from, and where its problems are logged.</param>
    /// <param name="variant">The loaded Fenix, read at each refresh on the UI thread, where the definition's variant changes.</param>
    /// <param name="pressKey">
    /// Presses a key as the panel does: called on the UI thread with the key's variable name, once for each click and
    /// never otherwise. The window has no SimConnect of its own; the definition's factory wires this.
    /// </param>
    public FenixChannelIsisForm(FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant, Action<string> pressKey)
        : base(FenixChannelIsisRows.Feature, FenixChannelDisplay.Isis, monitor,
            snapshot => FenixChannelIsisRows.Compose(
                snapshot, variant(), problem => monitor.ReportFeatureProblem(FenixChannelIsisRows.Feature, problem)))
    {
        // The base asks the composer only once the window starts, so the check comes in time.
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(pressKey);

        // The tab order is the list, then BUGS to Baro Push.
        AddControlAfterList(BuildKeyRow(IsisKeys, pressKey));
    }
}
