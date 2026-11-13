namespace MSFSBlindAssist.Aircraft;

// The cabin background seams (declared in FenixA320Definition.Hooks.cs): the app-wide cabin
// announcements (cabin ready, doors, slides, cautions) start with the definition's first sim update and stop
// when the pilot switches away, before the data service is disposed. The monitor takes this definition for
// its variant, which lays the cabin out, and its VariantChanged.
public partial class FenixA320Definition
{
    private Services.FenixCabinReadyMonitor? _cabinReadyMonitor;

    partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer)
    {
        _cabinReadyMonitor ??= Services.FenixCabinReadyMonitor.Start(DataService, announcer, this);
    }

    partial void StopCabinBackground()
    {
        _cabinReadyMonitor?.Dispose();
        _cabinReadyMonitor = null;
    }
}
