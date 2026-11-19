using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The PFD's window ("Fenix PFD"): the captain's primary flight display as <see cref="FenixChannelPfdRows"/> words it.
/// It is a class of its own, holding only its constructor, because Fenix windows are tracked one for each type and each
/// display therefore needs a type of its own (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
internal sealed class FenixChannelPfdForm : FenixChannelDisplayForm
{
    public FenixChannelPfdForm(FenixChannelMonitor monitor)
        : base("PFD", FenixChannelDisplay.Pfd, monitor,
            snapshot => FenixChannelPfdRows.Compose(
                snapshot, problem => monitor.ReportFeatureProblem(FenixChannelPfdRows.Feature, problem)))
    {
    }
}
