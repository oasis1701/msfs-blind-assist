using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The ND's window ("Fenix ND"): the captain's navigation display as <see cref="FenixChannelNdRows"/> words it. A class
/// of its own, holding only its constructor, because Fenix windows are tracked one for each type
/// (<see cref="FenixChannelDisplayForm"/>). Nothing constructs it yet.
/// </summary>
internal sealed class FenixChannelNdForm : FenixChannelDisplayForm
{
    public FenixChannelNdForm(FenixChannelMonitor monitor)
        : base("ND", FenixChannelDisplay.Nd, monitor,
            snapshot => FenixChannelNdRows.Compose(
                snapshot, problem => monitor.ReportFeatureProblem(FenixChannelNdRows.Feature, problem)))
    {
    }
}
