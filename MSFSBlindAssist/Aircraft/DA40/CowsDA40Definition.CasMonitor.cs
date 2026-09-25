using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The always-on CAS and FMA watcher.
///
/// The PFD window is where a pilot READS the display; this is what tells them to look. It
/// runs whether or not that window is open, because a caution appearing is not something
/// you should have to be already watching for — and on this aeroplane the CAS window is
/// the ONLY place most failures are announced at all. There are four physical annunciator
/// lamps; everything else the AFM calls an annunciation is drawn on the G1000.
///
/// BASELINE-FIRST, like every other MSFSBA monitor. The first scrape after connecting is
/// absorbed silently: reconnecting mid-flight must not re-read every message that was
/// already standing, which is exactly what the A380 EWD monitor had to learn.
///
/// ONE INSPECTOR SOCKET PER VIEW, so this client and the PFD window cannot both hold
/// AS1000_PFD. The window therefore takes the socket — but it FEEDS THIS DETECTOR with
/// the rows it reads rather than silencing it.
///
/// The first version suspended the announcements outright, on the reasoning that a pilot
/// who opened the display is already reading it. That was wrong, and it showed up
/// immediately: four CAS messages raised by cycling the engine master appeared in the
/// window and none of them spoke. Having the window open does not mean the pilot is
/// looking at the CAS block at that moment, and a caution that arrives while they are
/// reading something else is exactly the one they need told about. One detector, two
/// sources, and the known-message set carries across the handover in both directions so
/// nothing is announced twice.
/// </summary>
public partial class CowsDA40Definition
{
    private CoherentDisplayClient? _casClient;
    private ScreenReaderAnnouncer? _casAnnouncer;

    private System.Windows.Forms.Timer? _casWatchdog;
    private bool _casSawRows;

    private readonly HashSet<string> _knownCas = new(StringComparer.Ordinal);
    private string _lastFma = "";
    private bool _casBaselined;

    /// <summary>
    /// The PFD window's own rows, fed in while it holds the socket. Same detector, so a
    /// message seen through the window is not announced again when the monitor resumes.
    /// </summary>
    public void ProcessCasRows(List<string> rows) => OnCasRows(rows);

    public void StartCasMonitor(ScreenReaderAnnouncer announcer)
    {
        if (_casClient != null) return;

        _casAnnouncer = announcer;
        _casBaselined = false;
        _knownCas.Clear();
        _lastFma = "";

        _casClient = new CoherentDisplayClient("AS1000_PFD", pollIntervalMs: 1500,
            agentFileName: "coherent-da40-g1000-agent.js");
        _casClient.RowsUpdated += OnCasRows;
        _casClient.Error += msg => Log.Debug("DA40", $"CAS monitor error: {msg}");
        _casClient.Start();
        Log.Debug("DA40", "CAS monitor: started, waiting for AS1000_PFD");

        // REPORT THE VERDICT. This monitor failed silently once already - it is the
        // background half, so there is no window to show an error in, and "nothing was
        // announced" is indistinguishable from "nothing happened". If no rows arrive it
        // says so, once, with the two things that would explain it.
        _casWatchdog = new System.Windows.Forms.Timer { Interval = 20000 };
        _casWatchdog.Tick += (_, _) =>
        {
            _casWatchdog!.Stop();
            if (_casSawRows) return;
            Log.Warn("DA40", "CAS monitor: no rows after 20 s. Either the G1000 view was " +
                             "not up yet, or something else holds the AS1000_PFD inspector " +
                             "socket (only one per view is allowed).");
        };
        _casWatchdog.Start();
    }

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
        StopPowerAnnounce();
        StopLampWatch();
        StopDoorAnnounce();
        StopWaypointSequencer();

        if (_casClient == null) return;

        _casClient.RowsUpdated -= OnCasRows;
        try { _casWatchdog?.Stop(); _casWatchdog?.Dispose(); } catch { }
        _casWatchdog = null;
        try { _casClient.Stop(); _casClient.Dispose(); } catch { /* teardown must not throw */ }
        _casClient = null;
        _casAnnouncer = null;
    }

    /// <summary>
    /// The PFD window calls this while it is open, to hand over the SOCKET only. The
    /// announcements do not stop - the window feeds <see cref="ProcessCasRows"/> instead.
    /// </summary>
    /// <summary>
    /// Run one expression on the CAS monitor's own Coherent socket, or return empty.
    ///
    /// Shared rather than opening a second connection, because Coherent allows ONE inspector
    /// socket per view - a second would be refused, which is the whole reason the PFD window
    /// and this monitor hand the socket back and forth in the first place.
    /// </summary>
    internal async System.Threading.Tasks.Task<string> InvokeOnCasClientAsync(string expression)
    {
        var c = _casClient;
        if (c == null) return "";
        try { return await c.InvokeAsync(expression); }
        catch { return ""; }
    }

    public void SuspendCasMonitor(bool suspended)
    {
        // Only the SOCKET is handed over. The detector keeps running on the window's rows,
        // so there is no gap in coverage and deliberately NO re-baseline: the known set is
        // continuous across the handover, which is what stops a message that appeared
        // while the window was open being re-announced when the monitor takes back over.
        _casClient?.SetActive(!suspended);
    }

    /// <summary>
    /// The CAS messages out of a scrape, and NOTHING ELSE.
    ///
    /// ⚠️ INDENTATION ALONE IS NOT THE TEST, and treating it as one was a real fault. The
    /// scrape indents every nested list it emits — the PFD's popout panes, and now the
    /// field list that every page carries — so an indentation-only rule announces
    /// "Weight: Pounds" as a caution the moment a pilot opens a setup page. The block
    /// starts at the "CAS messages:" header and ends at the first row that is not part
    /// of it.
    ///
    /// Static and separate so the boundary can be tested without a live display.
    /// </summary>
    internal static List<string> ExtractCasMessages(IEnumerable<string> rows)
    {
        var cas = new List<string>();
        bool inCas = false;

        foreach (string row in rows)
        {
            if (row.StartsWith("CAS messages:", StringComparison.Ordinal)) { inCas = true; continue; }
            if (!row.StartsWith("  ", StringComparison.Ordinal)) { inCas = false; continue; }
            if (inCas) cas.Add(row.Trim());
        }

        return cas;
    }

    private void OnCasRows(List<string> rows)
    {
        // BEFORE the announcer guard: the units are not an announcement, and they must
        // still be picked up on a scrape that arrives before the monitor has one.
        NoteDisplayUnits(rows);

        if (_casAnnouncer == null) return;

        if (!_casSawRows)
        {
            _casSawRows = true;
            Log.Debug("DA40", $"CAS monitor: first rows in ({rows.Count} lines)");
        }

        List<string> cas;
        string fma = "";

        cas = ExtractCasMessages(rows);

        foreach (string row in rows)
        {
            if (row.StartsWith("Autopilot: ", StringComparison.Ordinal)) fma = row.Substring(11);
        }

        // First pass after a connect is the baseline and is never spoken.
        if (!_casBaselined)
        {
            _casBaselined = true;
            _knownCas.Clear();
            foreach (string c in cas) _knownCas.Add(c);
            _lastFma = fma;
            return;
        }

        foreach (string message in cas)
        {
            if (_knownCas.Add(message)) _casAnnouncer.AnnounceImmediate(message);
        }

        // A message that CLEARED is worth one word, not silence: a caution going away is
        // how a pilot learns the thing they just did worked.
        _knownCas.RemoveWhere(known =>
        {
            if (cas.Contains(known)) return false;
            _casAnnouncer.Announce(known.Replace("Caution: ", "").Replace("WARNING: ", "")
                                        .Replace("Advisory: ", "").Replace("Status: ", "") + " cleared");
            return true;
        });

        if (fma != _lastFma)
        {
            _lastFma = fma;
            // "off" is the resting state and announcing it on every disconnect would be
            // noise; the modes themselves are the news.
            if (fma != "off") _casAnnouncer.AnnounceImmediate("Autopilot " + fma);
        }
    }
}
