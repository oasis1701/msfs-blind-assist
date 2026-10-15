using System.Linq;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.G1000;

/// <summary>
/// An aircraft flown through the stock Working Title G1000's display windows
/// (<see cref="Forms.DA40.CowsDA40DisplayForm"/>): the COWS DA40 and the Cessna 172. The window
/// hands it what it reads, and MainForm starts and stops its CAS watcher in both
/// <c>MainForm_Load</c> and <c>SwitchAircraft</c> (DA40S-7).
/// </summary>
public interface IG1000DisplayOwner
{
    /// <summary>Starts the always-on CAS and FMA watcher (<see cref="G1000CasWatcher"/>).</summary>
    void StartCasMonitor(ScreenReaderAnnouncer announcer);

    /// <summary>Stops it, and anything else the aircraft ties to the same lifetime.</summary>
    void StopCasMonitor();

    /// <summary>The PFD window's rows, fed in while the window holds the AS1000_PFD socket.</summary>
    void ProcessCasRows(List<string> rows);

    /// <summary>The PFD window takes the socket (true) or gives it back (false).</summary>
    void SuspendCasMonitor(bool suspended, Func<string, Task<string>>? invokeWhileSuspended = null);

    /// <summary>The MFD window's rows, for the G1000's unit settings (the DA40 reads its readouts in them).</summary>
    void NoteDisplayUnits(IEnumerable<string> rows);

    /// <summary>A radio the window just tuned, so the aircraft's own settle announcer does not say it again.</summary>
    void MarkRadioTunedByWindow(string varKey);
}

/// <summary>
/// The always-on CAS and FMA watcher, for every aircraft on the stock G1000.
///
/// The PFD window is where a pilot READS the display; this is what tells them to look. It
/// runs whether or not that window is open, because a caution appearing is not something
/// you should have to be already watching for. On these aeroplanes the CAS window is the
/// ONLY place most failures are announced at all: the DA40 has four physical annunciator
/// lamps and the C172 none, and everything else the AFM calls an annunciation is drawn on
/// the G1000. Born in the DA40 (#242) and moved here unchanged when the C172 took it up.
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
///
/// A DARK SCREEN IS NOT A CLEARED MESSAGE. With the master off the G1000 draws nothing and
/// its CAS list is empty, which read as every standing message clearing at once ("LOW VOLTS
/// cleared", "OIL PRESSURE cleared"...) at each shutdown. The agent says when the screen is
/// unpowered (<see cref="IsDisplayDark"/>), and the watcher then forgets what it knew and
/// says nothing, WITHOUT dropping back to an unspoken baseline: when the power comes back,
/// every message the G1000 raises is new, and is spoken, which is how a pilot hears the
/// aeroplane come alive.
/// </summary>
internal sealed class G1000CasWatcher
{
    private readonly string _logCategory;
    private readonly Func<bool> _quiet;
    private readonly Action<List<string>>? _onRows;

    private CoherentDisplayClient? _client;
    private ScreenReaderAnnouncer? _announcer;

    private System.Windows.Forms.Timer? _watchdog;
    private bool _sawRows;

    private readonly CasClearDebounce _debounce = new();
    /// <summary>The clock the clear debounce reads; the suite steps it past the hold.</summary>
    internal Func<DateTime> Now = () => DateTime.UtcNow;
    private string _lastFma = "";
    private bool _baselined;

    // What the last scrape showed, for Alt+E (DescribeCurrentMessages): in the display's order,
    // which the known SET does not keep, and replaced whole so the UI thread reads a finished list.
    private volatile IReadOnlyList<string> _current = Array.Empty<string>();
    private volatile bool _currentRead;
    private volatile bool _currentDark;

    private volatile bool _suspended;
    private Func<string, Task<string>>? _lender;

    /// <param name="logCategory">The aircraft's debug.log category.</param>
    /// <param name="quiet">True while the aircraft's own post-load quiet period runs: every scrape
    /// then only records (the G1000 restarts on a flight load and raises its start-up messages).</param>
    /// <param name="onRows">Every scrape, before anything is announced (the DA40's display units).</param>
    public G1000CasWatcher(string logCategory, Func<bool> quiet, Action<List<string>>? onRows = null)
    {
        _logCategory = logCategory;
        _quiet = quiet;
        _onRows = onRows;
    }

    public bool IsRunning => _client != null;

    /// <summary>
    /// The PFD window's own rows, fed in while it holds the socket. Same detector, so a
    /// message seen through the window is not announced again when the monitor resumes.
    /// </summary>
    public void ProcessRows(List<string> rows) => OnRows(rows);

    public void Start(ScreenReaderAnnouncer announcer)
    {
        if (_client != null) return;

        Arm(announcer);

        _client = new CoherentDisplayClient("AS1000_PFD", pollIntervalMs: 1500,
            agentFileName: "coherent-da40-g1000-agent.js");
        _client.RowsUpdated += OnRows;
        _client.Error += msg => Log.Debug(_logCategory, $"CAS monitor error: {msg}");
        _client.Start();
        // A PFD window that took the socket before this start keeps it (Suspend recorded it).
        if (_suspended) _client.SetActive(false);
        Log.Debug(_logCategory, "CAS monitor: started, waiting for AS1000_PFD");

        // REPORT THE VERDICT. This monitor failed silently once already - it is the
        // background half, so there is no window to show an error in, and "nothing was
        // announced" is indistinguishable from "nothing happened". If no rows arrive it
        // says so, once, with the two things that would explain it.
        _watchdog = new System.Windows.Forms.Timer { Interval = 20000 };
        _watchdog.Tick += (_, _) =>
        {
            _watchdog!.Stop();
            if (_sawRows) return;
            Log.Warn(_logCategory, "CAS monitor: no rows after 20 s. Either the G1000 view was " +
                                   "not up yet, or something else holds the AS1000_PFD inspector " +
                                   "socket (only one per view is allowed).");
        };
        _watchdog.Start();
    }

    /// <summary>
    /// The announcer and a fresh baseline, without a socket. <see cref="Start"/> runs it first;
    /// the suite runs it alone and feeds <see cref="ProcessRows"/>.
    /// </summary>
    internal void Arm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;
        // Each start's watchdog judges that start: a second start (switched away and back)
        // must be able to report no rows again.
        _sawRows = false;
        _baselined = false;
        _debounce.Baseline(Array.Empty<string>());
        _lastFma = "";
        _current = Array.Empty<string>();
        _currentRead = false;
        _currentDark = false;
    }

    /// <summary>
    /// Output Alt+E: the CAS messages on the screen now, as the last scrape (the watcher's own, or
    /// the PFD window's) showed them. The G1000's CAS window is these aircraft's E/WD; the DA40's
    /// hotkey guide had promised the key since the window was built and nothing answered it.
    /// </summary>
    public string DescribeCurrentMessages() => DescribeCasList(_current, _currentDark, _currentRead);

    /// <summary>
    /// What Alt+E says: how many, then each message as the G1000 words it and in its order
    /// (warnings on top), the way a new one is spoken; nothing appended (DA40-8). A dark screen and
    /// a screen not read yet say so, never "No CAS messages". Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeCasList(IReadOnlyList<string> cas, bool dark, bool read)
    {
        if (dark) return "The PFD is off.";
        if (!read) return "CAS messages not read yet.";
        if (cas.Count == 0) return "No CAS messages.";
        string count = cas.Count == 1 ? "1 CAS message" : $"{cas.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} CAS messages";
        return count + ". " + string.Join(". ", cas.Select(c => c.TrimEnd('.'))) + ".";
    }

    public void Stop()
    {
        if (_client == null) return;

        _client.RowsUpdated -= OnRows;
        try { _watchdog?.Stop(); _watchdog?.Dispose(); } catch { }
        _watchdog = null;
        try { _client.Stop(); _client.Dispose(); } catch { /* teardown must not throw */ }
        _client = null;
        _announcer = null;
        _current = Array.Empty<string>();
        _currentRead = false;
        _currentDark = false;
    }

    /// <summary>
    /// Run one expression on the AS1000_PFD socket, or return empty.
    ///
    /// Shared rather than opening a second connection, because Coherent allows ONE inspector
    /// socket per view - a second would be refused, which is the whole reason the PFD window
    /// and this monitor hand the socket back and forth in the first place.
    ///
    /// ⚠️ WHILE THE PFD WINDOW HOLDS THE SOCKET, THE EXPRESSION RIDES THE WINDOW'S CLIENT.
    /// <c>CoherentDisplayClient.InvokeAsync</c> reconnects whether or not the client is active,
    /// so sending through the suspended monitor client pulled the socket back from the window
    /// the pilot was reading on every waypoint poll. With no window client to borrow, nothing
    /// is sent at all.
    /// </summary>
    public async Task<string> InvokeAsync(string expression)
    {
        var c = _client;
        var lender = _lender;
        try
        {
            switch (RouteCasInvoke(_suspended, c != null, lender != null))
            {
                case CasInvokeRoute.Own: return await c!.InvokeAsync(expression);
                case CasInvokeRoute.Lender: return await lender!(expression);
                default: return "";
            }
        }
        catch { return ""; }
    }

    internal enum CasInvokeRoute { None, Own, Lender }

    /// <summary>Which socket a one-shot expression may use. Pure so the handover is pinned.</summary>
    internal static CasInvokeRoute RouteCasInvoke(bool suspended, bool hasOwnClient, bool hasLender)
    {
        if (suspended) return hasLender ? CasInvokeRoute.Lender : CasInvokeRoute.None;
        return hasOwnClient ? CasInvokeRoute.Own : CasInvokeRoute.None;
    }

    /// <summary>
    /// The PFD window calls this while it is open, to hand over the SOCKET only. The
    /// announcements do not stop - the window feeds <see cref="ProcessRows"/> instead -
    /// and one-shot expressions go through <paramref name="invokeWhileSuspended"/>, the
    /// window's own client.
    /// </summary>
    public void Suspend(bool suspended, Func<string, Task<string>>? invokeWhileSuspended = null)
    {
        // Only the SOCKET is handed over. The detector keeps running on the window's rows,
        // so there is no gap in coverage and deliberately NO re-baseline: the known set is
        // continuous across the handover, which is what stops a message that appeared
        // while the window was open being re-announced when the monitor takes back over.
        _lender = suspended ? invokeWhileSuspended : null;
        _suspended = suspended;
        _client?.SetActive(!suspended);
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

    /// <summary>
    /// The row the agent puts first when the instrument has no power (its <c>#Electricity</c>
    /// element's state is "off"). A CONTRACT with <c>A.rows</c> in coherent-da40-g1000-agent.js.
    /// </summary>
    internal const string DisplayDarkRow = "Display power: off";

    /// <summary>Whether a scrape is of an unpowered screen. Pure so the suite can pin it.</summary>
    internal static bool IsDisplayDark(IEnumerable<string> rows) =>
        rows.Any(r => r.Equals(DisplayDarkRow, StringComparison.Ordinal));

    /// <summary>
    /// The PFD Alerts window's messages that are not already a CAS line, as "Alert: ..." so
    /// they ride the same appeared/cleared speech. A scheduler reminder ("SCHEDULER 1 - FUEL
    /// XFER") exists only there, and a sighted pilot learns of it from the Alerts softkey
    /// flashing; a CAS caution is listed there too, with its explanation ("PITOT HT OFF - Pitot
    /// heat is off."), and is already spoken from the CAS block, so it is left out. Pure so the
    /// suite can pin it.
    /// </summary>
    internal static List<string> AlertsNotOnCas(IEnumerable<string> rows, IEnumerable<string> cas)
    {
        var casTexts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string c in cas)
        {
            int colon = c.IndexOf(": ", StringComparison.Ordinal);
            casTexts.Add(colon >= 0 ? c.Substring(colon + 2).Trim() : c.Trim());
        }

        var alerts = new List<string>();
        bool inAlerts = false;
        foreach (string row in rows)
        {
            if (row.StartsWith("Alert messages:", StringComparison.Ordinal)) { inAlerts = true; continue; }
            if (!row.StartsWith("  ", StringComparison.Ordinal)) { inAlerts = false; continue; }
            if (!inAlerts) continue;

            string message = row.Trim();
            int dash = message.IndexOf(" - ", StringComparison.Ordinal);
            string head = dash >= 0 ? message.Substring(0, dash).Trim() : message;
            if (casTexts.Contains(head)) continue;
            alerts.Add("Alert: " + message);
        }
        return alerts;
    }

    private void OnRows(List<string> rows)
    {
        // BEFORE the announcer guard: the units are not an announcement, and they must
        // still be picked up on a scrape that arrives before the monitor has one.
        _onRows?.Invoke(rows);

        if (_announcer == null) return;

        if (!_sawRows)
        {
            _sawRows = true;
            Log.Debug(_logCategory, $"CAS monitor: first rows in ({rows.Count} lines)");
        }

        // A dark screen: forget, say nothing, and stay baselined, so that what the G1000
        // raises when it powers up is spoken (see the class note).
        if (IsDisplayDark(rows))
        {
            _baselined = true;
            _debounce.Baseline(Array.Empty<string>());
            _lastFma = "";
            _current = Array.Empty<string>();
            _currentDark = true;
            _currentRead = true;
            return;
        }

        List<string> cas;
        string fma = "";

        cas = ExtractCasMessages(rows);
        cas.AddRange(AlertsNotOnCas(rows, cas));

        // For Alt+E, before the baseline and quiet-period returns: what is on the screen is worth
        // saying when asked even while nothing is announced.
        _current = cas.ToArray();
        _currentDark = false;
        _currentRead = true;

        foreach (string row in rows)
        {
            if (row.StartsWith("Autopilot: ", StringComparison.Ordinal)) fma = FmaModes(row.Substring(11));
        }

        // First pass after a connect is the baseline and is never spoken — and so is every
        // pass in the quiet period after a flight load, when the G1000 restarts and raises its
        // start-up messages (DA40LoadSettle).
        if (!_baselined || _quiet())
        {
            _baselined = true;
            _debounce.Baseline(cas);
            _lastFma = fma;
            return;
        }

        // A message flickering on its threshold is one message (DA40G-32).
        var (appeared, cleared) = _debounce.Step(cas, Now());

        string? fmaChange = null;
        if (fma != _lastFma)
        {
            fmaChange = DescribeFmaChange(_lastFma, fma);
            _lastFma = fma;
        }

        var (immediate, queued) = ComposeCasSpeech(appeared, cleared, fmaChange);
        if (immediate.Length > 0) _announcer.AnnounceImmediate(immediate);
        foreach (string line in queued) _announcer.Announce(line);
    }

    /// <summary>
    /// Which CAS messages appeared and which cleared, with a message that flickers on its
    /// threshold held as ONE. ⚠️ Over the sea off VCBI (2026-10-15) the left tank sloshing on
    /// its low line said "Caution: L FUEL LOW" / "L FUEL LOW cleared" twelve times in 25 s,
    /// and a starving engine flapped FUEL PRES and PTRM the same way. A message that goes is
    /// cleared only once it has stayed gone for <see cref="ClearSettleMs"/>; back inside that,
    /// it was never gone and nothing is said. Pure, so the suite can pin it (DA40G-32).
    /// </summary>
    internal sealed class CasClearDebounce
    {
        /// <summary>Outlasts the widest measured gap (3.1 s) plus half a ~1.5 s poll.</summary>
        internal const int ClearSettleMs = 4000;

        private readonly List<string> _known = new();
        private readonly Dictionary<string, DateTime> _goneSince = new(StringComparer.Ordinal);

        /// <summary>What is on the display as far as the pilot has been told, in order.</summary>
        internal IReadOnlyList<string> Known => _known;

        internal void Baseline(IEnumerable<string> current)
        {
            _known.Clear();
            _known.AddRange(current.Distinct(StringComparer.Ordinal));
            _goneSince.Clear();
        }

        internal (List<string> Appeared, List<string> Cleared) Step(IReadOnlyCollection<string> current, DateTime now)
        {
            var appeared = new List<string>();
            foreach (string message in current)
            {
                if (_known.Contains(message)) _goneSince.Remove(message);
                else { _known.Add(message); appeared.Add(message); }
            }

            var cleared = new List<string>();
            foreach (string known in _known.ToList())
            {
                if (current.Contains(known)) continue;
                if (!_goneSince.TryGetValue(known, out var since)) { _goneSince[known] = now; continue; }
                if ((now - since).TotalMilliseconds < ClearSettleMs) continue;
                _known.Remove(known);
                _goneSince.Remove(known);
                cleared.Add(known);
            }
            return (appeared, cleared);
        }
    }

    /// <summary>
    /// What one CAS/FMA poll says.
    ///
    /// ⚠️ ONE INTERRUPTING UTTERANCE, NEVER ONE PER MESSAGE. Each new message used to be its
    /// own <c>AnnounceImmediate</c>, and each one cuts off the one before it — so two
    /// cautions appearing together (a failure usually raises more than one) were heard as
    /// the LAST alone, and an autopilot mode change in the same poll cut all of them off.
    /// New messages and the mode change are one sentence, in display order (warnings sit on
    /// top); a message that CLEARED is queued behind it, because a caution going away is how a
    /// pilot learns that what they just did worked, and it must not cut off a new one.
    /// "off" is the autopilot's resting state and announcing it on every disconnect would be
    /// noise; the modes themselves are the news. <paramref name="fmaChange"/> is already worded
    /// (<see cref="DescribeFmaChange"/>), or null when the FMA did not change.
    /// </summary>
    internal static (string Immediate, List<string> Queued) ComposeCasSpeech(
        IReadOnlyList<string> appeared, IReadOnlyList<string> cleared, string? fmaChange)
    {
        var parts = new List<string>(appeared);
        if (!string.IsNullOrEmpty(fmaChange)) parts.Add(fmaChange);

        var queued = new List<string>();
        foreach (string known in cleared)
            queued.Add(known.Replace("Caution: ", "").Replace("WARNING: ", "")
                            .Replace("Advisory: ", "").Replace("Status: ", "")
                            .Replace("Alert: ", "") + " cleared");

        return (string.Join(". ", parts.Select(p => p.TrimEnd('.'))) is { Length: > 0 } t ? t + "." : "", queued);
    }

    /// <summary>
    /// The FMA's MODES, without the vertical target the agent puts last ("AP, YD, GPS, FLC,
    /// ALTS armed, target 88 KT" gives "AP, YD, GPS, FLC, ALTS armed"). A target change is
    /// spoken once by the selected-value announcer; comparing it here re-read the whole strip
    /// on every knob step. Pure so the suite can pin it.
    /// </summary>
    internal static string FmaModes(string fma)
    {
        int t = fma.LastIndexOf(", target ", StringComparison.Ordinal);
        return t >= 0 ? fma.Substring(0, t) : fma;
    }

    /// <summary>
    /// What an FMA change says, or "" for nothing. Pure so the suite can pin it.
    ///
    /// ⚠️ MODES WITHOUT "AP" ARE THE FLIGHT DIRECTOR'S. Pulling the AFCS breaker in flight
    /// took the autopilot off and left the director in PIT with ALTS armed, and the change
    /// was spoken "Autopilot PIT, ALTS armed" - telling the pilot the autopilot was flying
    /// (2026-10-12). With "AP" the modes are the autopilot's; without it they are the flight
    /// director's. Losing "AP" with the director still up names the director's modes alone:
    /// the autopilot master row already says "Autopilot: Off" (DA40A-22), and "Autopilot off,
    /// flight director ..." here said it twice. A plain disconnect to "off" stays silent: that
    /// is the resting state, and the disconnect tone is the aeroplane's own.
    /// </summary>
    internal static string DescribeFmaChange(string previous, string current)
    {
        static bool HasAp(string f) =>
            f == "AP" || f.StartsWith("AP, ", StringComparison.Ordinal);

        if (current.Length == 0 || current == "off") return "";
        if (HasAp(current))
        {
            string modes = current == "AP" ? "" : current.Substring(4).Trim();
            return modes.Length > 0 ? "Autopilot " + modes : "Autopilot on";
        }
        return "Flight director " + current;
    }
}
