using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The SD's window ("Fenix SD"): the lower ECAM as <see cref="FenixChannelSd"/> words it, the page shown, its content
/// and the bottom line. It is a class of its own, holding only its constructor, because Fenix windows are tracked one
/// for each type and each display therefore needs a type of its own (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
internal sealed class FenixChannelSdForm : FenixChannelDisplayForm
{
    /// <param name="monitor">The channel monitor the SD's snapshots come from, and where its problems are logged.</param>
    /// <param name="variant">
    /// The loaded Fenix, read at each refresh on the UI thread, where the definition's variant changes: the FUEL page's
    /// layout follows the airframe, which the channel does not send.
    /// </param>
    public FenixChannelSdForm(FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant)
        : base(FenixChannelSd.DisplayName, FenixChannelDisplay.Sd, monitor,
            snapshot => FenixChannelSd.Compose(
                snapshot, variant(), problem => monitor.ReportFeatureProblem(FenixChannelSd.DisplayName, problem)))
    {
        // The base asks the composer only once the window starts, so the check comes in time.
        ArgumentNullException.ThrowIfNull(variant);
    }
}
