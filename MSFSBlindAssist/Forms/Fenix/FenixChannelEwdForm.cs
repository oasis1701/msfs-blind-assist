using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The E/WD's window ("Fenix E/WD"): the upper ECAM as <see cref="FenixChannelEwdRows"/> words it, the engine block, the
/// thrust limit, the FOB line, the flap text and the warning and memo lines, and under the list the ECAM control panel's
/// keys that act on the E/WD. It is a class of its own because Fenix windows are tracked one for each type and each
/// display therefore needs a type of its own (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
/// <remarks>
/// <para>The keys. One button for each (<see cref="EcamKeys"/>), after the list in tab order: T.O CONFIG, CLR left, CLR
/// right, RCL and EMER CANC, whose results show on the E/WD, so that a pilot can press one and read what it did in the
/// one window. The SD's page keys (<see cref="FenixChannelSdForm.PageKeys"/>) are the other half of the panel; both
/// windows build their row with <see cref="FenixChannelDisplayForm.BuildKeyRow"/>. A press calls the press delegate with
/// the key's variable name and nothing else: the definition's own press of that key, the panel's code (T.O CONFIG's
/// verdict watcher [FNX-4], a full press-release for the others [FNX-6], [A320-3]).</para>
/// <para>Silence ([CORE-7]). The window holds no announcer and no SimConnect: what the panel's code and the keys' lights
/// announce is the one confirmation of a press, as when the pilot presses the panel's button. A refresh, F5 and opening
/// the window never press.</para>
/// </remarks>
internal sealed class FenixChannelEwdForm : FenixChannelDisplayForm
{
    /// <summary>
    /// The ECAM control panel's keys the window carries, in the panel's order: the label the button shows and says, and
    /// the panel's own variable (<c>FenixA320Definition</c>'s "ECAM" panel). Data, so that a test reads it and holds it
    /// to the panel.
    /// </summary>
    internal static readonly IReadOnlyList<(string Label, string Key)> EcamKeys =
    [
        ("T.O CONFIG", "S_ECAM_TO"),
        ("CLR left", "S_ECAM_CLR_LEFT"),
        ("CLR right", "S_ECAM_CLR_RIGHT"),
        ("RCL", "S_ECAM_RCL"),
        ("EMER CANC", "S_ECAM_EMER_CANCEL")
    ];

    /// <param name="monitor">The channel monitor the E/WD's snapshots come from, and where its problems are logged.</param>
    /// <param name="engines">
    /// The loaded Fenix's engine family, read at each refresh on the UI thread, where the definition's variant changes:
    /// the engine block's gauges and their order follow the engines, which the channel does not send.
    /// </param>
    /// <param name="pressKey">
    /// Presses a key as the panel does: called on the UI thread with the key's variable name, once for each click and
    /// never otherwise. The window has no SimConnect of its own; the definition's factory wires this.
    /// </param>
    public FenixChannelEwdForm(FenixChannelMonitor monitor, Func<Aircraft.FenixEngineFamily> engines, Action<string> pressKey)
        : base(FenixChannelEwdRows.DisplayName, FenixChannelDisplay.Ewd, monitor,
            snapshot => FenixChannelEwdRows.Compose(
                snapshot, engines(), problem => monitor.ReportFeatureProblem(FenixChannelEwdRows.DisplayName, problem)))
    {
        // The base asks the composer only once the window starts, so the check comes in time.
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(pressKey);

        // The tab order is the list, then T.O CONFIG to EMER CANC.
        AddControlAfterList(BuildKeyRow(EcamKeys, pressKey));
    }
}
