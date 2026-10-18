using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.G1000;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The DA40's side of the always-on CAS and FMA watcher, which lives in
/// <see cref="G1000CasWatcher"/> and is shared with the Cessna 172's G1000. What stays here is
/// the DA40's: its post-load quiet period (<see cref="DA40LoadSettle"/>), the display units the
/// watcher hands it from every scrape, and everything else <see cref="StopCasMonitor"/> ties to
/// the same lifetime.
/// </summary>
public partial class CowsDA40Definition : IG1000DisplayOwner
{
    private G1000CasWatcher? _casWatcher;

    private G1000CasWatcher Cas => _casWatcher ??=
        new G1000CasWatcher("DA40", () => _loadSettle.Settling, rows => NoteDisplayUnits(rows));

    /// <summary>
    /// The PFD window's own rows, fed in while it holds the socket. Same detector, so a
    /// message seen through the window is not announced again when the monitor resumes.
    /// </summary>
    public void ProcessCasRows(List<string> rows) => Cas.ProcessRows(rows);

    public void StartCasMonitor(ScreenReaderAnnouncer announcer) => Cas.Start(announcer);

    public void StopCasMonitor()
    {
        // The barometric settle timer belongs to the same lifetime: it is armed from
        // ProcessSimVarUpdate and would otherwise keep a Forms timer alive against a
        // definition the app has finished with.
        StopBaroAnnounce();
        StopMagnetoAnnounce();
        ResetPrimingState();
        ResetXlsStartState();
        ResetXlsMixtureState();
        ResetPerfTableState();
        StopRadioAnnounce();
        try { _afcsSettle?.Stop(); _afcsSettle?.Dispose(); } catch { }
        _afcsSettle = null;
        _afcsPending.Clear();
        StopPowerAnnounce();
        StopLampWatch();
        StopDoorAnnounce();
        StopWaypointSequencer();
        CloseEngineGlance();
        // A press still being held must be let go, or its timer keeps writing an L:var into
        // whatever aircraft comes next.
        ReleaseAllHeldLVars();

        _casWatcher?.Stop();
    }

    /// <summary>
    /// Run one expression on the AS1000_PFD socket, or return empty: the watcher's own client,
    /// or the PFD window's while the window holds the socket (<see cref="G1000CasWatcher.InvokeAsync"/>).
    /// </summary>
    internal System.Threading.Tasks.Task<string> InvokeOnCasClientAsync(string expression) =>
        Cas.InvokeAsync(expression);

    /// <summary>
    /// The PFD window calls this while it is open, to hand over the SOCKET only; the
    /// announcements carry on from the window's rows (<see cref="G1000CasWatcher.Suspend"/>).
    /// </summary>
    public void SuspendCasMonitor(bool suspended,
        Func<string, System.Threading.Tasks.Task<string>>? invokeWhileSuspended = null) =>
        Cas.Suspend(suspended, invokeWhileSuspended);

    void IG1000DisplayOwner.MarkRadioTunedByWindow(string varKey) => MarkRadioTunedByWindow(varKey);
}
