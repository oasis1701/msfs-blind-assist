namespace MSFSBlindAssist.Aircraft;

// Plan 08's cabin background seams (declared in FenixA320Definition.Hooks.cs): the app-wide cabin-ready
// call starts with the definition's first sim update and stops when the pilot switches away, before the
// data service is disposed.
public partial class FenixA320Definition
{
    private Services.FenixCabinReadyMonitor? _cabinReadyMonitor;

    partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer)
    {
        _cabinReadyMonitor ??= Services.FenixCabinReadyMonitor.Start(DataService, announcer);
    }

    partial void StopCabinBackground()
    {
        _cabinReadyMonitor?.Dispose();
        _cabinReadyMonitor = null;
    }
}
