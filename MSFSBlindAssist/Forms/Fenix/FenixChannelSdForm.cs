using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The SD's window ("Fenix SD"): the lower ECAM as <see cref="FenixChannelSd"/> words it, the page shown, its content
/// and the bottom line, and under the list the ECAM control panel's page keys, ENG to STS. A class of its own because
/// Fenix windows are tracked one for each type and each display therefore needs a type of its own
/// (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
/// <remarks>
/// <para>The page keys. One button for each (<see cref="PageKeys"/>), after the list in tab order, so that a pilot can
/// show a page and read it in the one window. A press calls the press delegate with the key's variable name and nothing
/// else; the delegate is the definition's own press of that key, the panel's code, a full press-release ([FNX-6],
/// [A320-3]). They are buttons and never a combo box, which changes its selection and so would press a key at every
/// page the reader arrows past. The panel's T.O CONFIG, CLR, RCL and EMER CANC keys act on the E/WD and are in its window
/// (<see cref="FenixChannelEwdForm"/>), built by the same <see cref="BuildKeyRow"/>.</para>
/// <para>Silence ([CORE-7]). The window holds no announcer and no SimConnect: the screen reader reads a button's label
/// as it takes focus, and what the panel's code and the key's light announce is the one confirmation of a press, as when
/// the pilot presses the panel's button. The list shows the new page at its next refresh. A refresh, F5 and opening the
/// window never press.</para>
/// </remarks>
internal sealed class FenixChannelSdForm : FenixChannelDisplayForm
{
    /// <summary>
    /// The ECAM control panel's page keys the window carries, in the panel's order: the label the button shows and says,
    /// and the panel's own variable (<c>FenixA320Definition</c>'s "ECAM" panel). Data, so that a test reads it and holds
    /// it to the panel.
    /// </summary>
    internal static readonly IReadOnlyList<(string Label, string Key)> PageKeys =
    [
        ("ENG", "S_ECAM_ENGINE"),
        ("BLEED", "S_ECAM_BLEED"),
        ("PRESS", "S_ECAM_CAB_PRESS"),
        ("ELEC", "S_ECAM_ELEC"),
        ("HYD", "S_ECAM_HYD"),
        ("FUEL", "S_ECAM_FUEL"),
        ("APU", "S_ECAM_APU"),
        ("COND", "S_ECAM_COND"),
        ("DOOR", "S_ECAM_DOOR"),
        ("WHEEL", "S_ECAM_WHEEL"),
        ("F/CTL", "S_ECAM_FCTL"),
        ("ALL", "S_ECAM_ALL"),
        ("STS", "S_ECAM_STATUS")
    ];

    /// <param name="monitor">The channel monitor the SD's snapshots come from, and where its problems are logged.</param>
    /// <param name="variant">
    /// The loaded Fenix, read at each refresh on the UI thread, where the definition's variant changes: the FUEL page's
    /// layout follows the airframe, which the channel does not send.
    /// </param>
    /// <param name="pressKey">
    /// Presses a page key as the panel does: called on the UI thread with the key's variable name, once for each click
    /// and never otherwise. The window has no SimConnect of its own; the definition's factory wires this.
    /// </param>
    public FenixChannelSdForm(FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant, Action<string> pressKey)
        : base(FenixChannelSd.DisplayName, FenixChannelDisplay.Sd, monitor, Composer(monitor, variant))
    {
        // The base asks the composer only once the window starts, so the check comes in time.
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(pressKey);

        // The tab order is the list, then ENG to STS.
        AddControlAfterList(BuildKeyRow(PageKeys, pressKey));
    }

    // The window's composer, with the one hold on the APU bleed valve's word it keeps from refresh to refresh.
    private static Func<FenixChannelSnapshot, FenixChannelRows> Composer(FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant)
    {
        var apuBleedHold = new FenixChannelApuBleedHold();
        return snapshot => FenixChannelSd.Compose(
            snapshot, variant(), problem => monitor.ReportFeatureProblem(FenixChannelSd.DisplayName, problem), apuBleedHold);
    }
}
