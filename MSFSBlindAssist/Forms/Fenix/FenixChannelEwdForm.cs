using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The E/WD's window ("Fenix E/WD"): the upper ECAM as <see cref="FenixChannelEwdRows"/> words it, the engine block, the
/// thrust limit, the FOB line, the flap text and the warning and memo lines. It is a class of its own, holding only its
/// constructor, because Fenix windows are tracked one for each type and each display therefore needs a type of its own
/// (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
internal sealed class FenixChannelEwdForm : FenixChannelDisplayForm
{
    /// <param name="monitor">The channel monitor the E/WD's snapshots come from, and where its problems are logged.</param>
    /// <param name="engines">
    /// The loaded Fenix's engine family, read at each refresh on the UI thread, where the definition's variant changes:
    /// the engine block's gauges and their order follow the engines, which the channel does not send.
    /// </param>
    public FenixChannelEwdForm(FenixChannelMonitor monitor, Func<Aircraft.FenixEngineFamily> engines)
        : base(FenixChannelEwdRows.DisplayName, FenixChannelDisplay.Ewd, monitor,
            snapshot => FenixChannelEwdRows.Compose(
                snapshot, engines(), problem => monitor.ReportFeatureProblem(FenixChannelEwdRows.DisplayName, problem)))
    {
        // The base asks the composer only once the window starts, so the check comes in time.
        ArgumentNullException.ThrowIfNull(engines);
    }
}
