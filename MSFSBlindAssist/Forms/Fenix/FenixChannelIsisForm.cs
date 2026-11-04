using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The ISIS's window ("Fenix ISIS"): the integrated standby instrument as <see cref="FenixChannelIsisRows"/> words it.
/// It is a class of its own, holding only its constructor, because Fenix windows are tracked one for each type and each
/// display therefore needs a type of its own (<see cref="FenixChannelDisplayForm"/>). It reads the variant at each
/// refresh, so a Fenix found to have the round standby gauges after the window opened shows that in its status row.
/// </summary>
internal sealed class FenixChannelIsisForm : FenixChannelDisplayForm
{
    public FenixChannelIsisForm(FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant)
        : base(FenixChannelIsisRows.Feature, FenixChannelDisplay.Isis, monitor,
            snapshot => FenixChannelIsisRows.Compose(
                snapshot, variant(), problem => monitor.ReportFeatureProblem(FenixChannelIsisRows.Feature, problem)))
    {
    }
}
