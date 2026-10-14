using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// P3 Coherent display pump: polls the DisplayUnits scrape (A220DisplaysClient +
/// coherent-a220-displays-agent.js) once a second and announces FMA/ASA transitions,
/// and CAS changes (the CommBus systems ride the same poll — Systems partial). All transition logic is pure
/// (A220DisplayParsing, pinned by SynapticA220DisplayParseTests); everything here is
/// baseline-first (the first successful snapshot records silently) and
/// exception-swallowing — a scrape hiccup must never crash the timer.
///
/// The pump self-starts from the first ProcessSimVarUpdate (nothing in MainForm
/// calls StartDisplayMonitoring today), which also stashes the announcer and runs on
/// the UI thread — the captured SynchronizationContext marshals every announcement
/// back there (announcer calls can silently fail off the UI thread; A380 RMP lesson).
/// </summary>
public partial class SynapticA220Definition
{
    private A220DisplaysClient? _displaysClient;
    private System.Threading.Timer? _displayPollTimer;
    private int _displayPollBusy;
    private SynchronizationContext? _displaySyncCtx;

    // Baselines (null = no successful snapshot yet -> record silently).
    private List<A220DisplayParsing.DisplayToken>? _prevFma;
    private List<A220DisplayParsing.DisplayToken>? _prevCas;
    private string _fmaSignature = "";
    private string _casSignature = "";

    private volatile string? _latestAsa;                       // for ReadApproachCapability
    private Dictionary<string, int> _vSpeeds = new();          // reference-swapped per poll

    // Latest AFDX CommBus blocks (see A220Afdx for why these are the only source for
    // FD state, FD commands and trim). Reference-swapped whole per poll — readers take
    // one snapshot and never see a half-updated block. Null = the tap has no fresh data.
    private volatile A220Afdx.AutoflightBlock? _afdxAutoflight;
    private volatile A220Afdx.FcpBlock? _afdxFcp;
    private volatile A220Afdx.FlightControlBlock? _afdxFlightControl;
    private volatile A220Afdx.TapDiag? _afdxDiag;

    // Trim announcer state (settle-gated; see A220Afdx.TrimSettled).
    private double? _trimPrevPitch;
    private double? _trimSpokenPitch;

    /// <summary>Set by StopDisplayPump so a late SimVar event for a DISCARDED def
    /// instance (aircraft-swap race: StopAllMotion runs before the reassignment) can't
    /// restart the pump and orphan the one-socket-per-page DisplayUnits connection.
    /// Only an explicit StartDisplayMonitoring un-retires.</summary>
    private volatile bool _displayPumpRetired;

    public override void StartDisplayMonitoring(SimConnectManager simConnect)
    {
        _sim = simConnect;
        _displayPumpRetired = false;
        EnsureDisplayPump();
    }

    public override void StopDisplayMonitoring(SimConnectManager simConnect) => StopDisplayPump();

    /// <summary>
    /// Called at the top of every ProcessSimVarUpdate: stashes the announcer, lazily
    /// starts the pump, and peeks the on-ground sample the take-off roll machine needs
    /// (Systems partial).
    /// </summary>
    private void ObserveForDisplays(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        _announcer ??= announcer;
        EnsureDisplayPump();
        if (varName == "SIM_ON_GROUND") _calloutOnGround = value > 0.5;
    }

    private void EnsureDisplayPump()
    {
        if (_displayPumpRetired || _displayPollTimer != null) return;
        _displaySyncCtx ??= SynchronizationContext.Current;
        _displaysClient ??= new A220DisplaysClient();
        _displayPollTimer = new System.Threading.Timer(OnDisplayPollTimer, null, 1000, 1000);
    }

    private void StopDisplayPump()
    {
        _displayPumpRetired = true;
        try { _displayPollTimer?.Dispose(); } catch { }
        _displayPollTimer = null;
        try { _displaysClient?.Shutdown(); } catch { }
        _displaysClient = null;
    }

    private void OnDisplayPollTimer(object? state)
    {
        // Single-flight: a slow eval must not stack polls.
        if (Interlocked.Exchange(ref _displayPollBusy, 1) == 1) return;
        _ = PollDisplaysOnce();
    }

    private async Task PollDisplaysOnce()
    {
        try
        {
            var client = _displaysClient;
            if (client == null) return;
            string? raw = await client.SnapshotAsync();
            if (string.IsNullOrEmpty(raw)) return;
            ProcessDisplaysSnapshot(raw);
        }
        catch { /* scrape hiccups are expected; next poll retries */ }
        finally { Interlocked.Exchange(ref _displayPollBusy, 0); }
    }

    internal void ProcessDisplaysSnapshot(string raw)
    {
        DisplaysSnap? snap;
        try { snap = System.Text.Json.JsonSerializer.Deserialize<DisplaysSnap>(raw); }
        catch { return; }
        if (snap == null || !snap.ok) return;

        // The CommBus systems blocks, parsed on their own (see DisplaysSnap.sys).
        var sys = SystemsFrom(snap.sys);

        // ---- A flight load (RequestSystemsRebaseline) ------------------------
        // Every scrape-side baseline (FMA, trim, the EICAS CAS diff) is dropped BEFORE the diffs
        // below run, so this poll records the loaded flight silently instead of diffing it against
        // the old flight's. This runs before ProcessSystems too, so it honours the request itself
        // rather than leave it to the monitors' reset below.
        if (TakeRebaseline(ref _scrapeRebaselined))
        {
            _prevFma = null;
            _fmaSignature = "";
            _trimPrevPitch = null;
            _trimSpokenPitch = null;
            _prevCas = null;
            _casSignature = "";
        }

        // ---- AFDX CommBus blocks (FD state/commands, FCP selections, trim) --
        _afdxAutoflight = snap.af;
        _afdxFcp = snap.fcp;
        _afdxFlightControl = snap.fc;
        _afdxDiag = snap.diag;
        AnnounceTrimIfSettled(snap.fc);

        // ---- FMA / ASA -----------------------------------------------------
        var fma = new List<A220DisplayParsing.DisplayToken>();
        foreach (var t in snap.fma ?? new())
        {
            string text = (t.t ?? "").Trim();
            if (text.Length == 0 || A220DisplayParsing.IsFmaNoise(text)) continue;
            fma.Add(new A220DisplayParsing.DisplayToken(text, t.c ?? "white"));
        }
        _latestAsa = A220DisplayParsing.LatestAsa(fma);

        string fmaSig = A220DisplayParsing.Signature(fma);
        if (_prevFma == null)
        {
            _prevFma = fma; _fmaSignature = fmaSig;    // silent baseline
        }
        else if (fmaSig != _fmaSignature)
        {
            var ann = A220DisplayParsing.DiffFma(_prevFma, fma);
            _prevFma = fma; _fmaSignature = fmaSig;
            // One fact, one voice: the PFD's "FD OFF" legend and the CommBus l_fd/r_fd
            // say the same thing, and the CommBus one is per-side and authoritative. The
            // scraped legend stays as the FALLBACK for a session where the CommBus tap
            // never delivered (both come from the same page, so this is belt-and-braces,
            // not a second source of truth).
            if (snap.af != null) ann.Queued.RemoveAll(m => m == "Flight directors off");
            if (!Muted(A220MonitorRows.FmaKey)) Speak(ann);   // a pump call-out: its own row ([A220-39])
        }

        // ---- V-speeds ------------------------------------------------------
        var spdRaw = new List<(string, double)>();
        foreach (var s in snap.spd ?? new())
            if (!string.IsNullOrWhiteSpace(s.t)) spdRaw.Add((s.t!.Trim(), s.y));
        var vSpeeds = A220DisplayParsing.ParseVSpeeds(spdRaw);
        if (vSpeeds.Count > 0 || _vSpeeds.Count > 0) _vSpeeds = vSpeeds;

        // ---- CAS -----------------------------------------------------------
        // The CAS Data store is the truth ([A220-36]); the scrape diff below speaks only while
        // the store is missing, and drops its baseline whenever the store takes over, so a
        // later fallback re-baselines silently instead of diffing against stale text. A flight
        // load drops it too (the rebaseline block at the top).
        if (sys?.cas != null)
        {
            _prevCas = null;
            _casSignature = "";
        }
        else
        {
            var cas = new List<A220DisplayParsing.DisplayToken>();
            foreach (var t in snap.cas ?? new())
            {
                string text = (t.t ?? "").Trim();
                if (text.Length == 0) continue;
                cas.Add(new A220DisplayParsing.DisplayToken(text, t.c ?? "white"));
            }
            string casSig = A220DisplayParsing.Signature(cas);
            if (_prevCas == null)
            {
                _prevCas = cas; _casSignature = casSig;    // silent baseline
            }
            else if (casSig != _casSignature)
            {
                var ann = A220DisplayParsing.DiffCas(_prevCas, cas);
                _prevCas = cas; _casSignature = casSig;
                FilterScrapedCas(ann);
                Speak(ann);
            }
        }

        // ---- Systems over the CommBus (Systems partial) --------------------
        ProcessSystems(sys);
    }

    /// <summary>The scrape fallback keeps the store's rules: status (memo) lines are never
    /// spoken, and each severity answers to its own Ctrl+M row.</summary>
    private static void FilterScrapedCas(A220DisplayParsing.Announcements ann)
    {
        ann.Queued.RemoveAll(m => m.StartsWith("Memo: ", StringComparison.Ordinal));
        if (Muted(A220MonitorRows.CasWarningsKey))
        {
            ann.Immediate.Clear();
            ann.Queued.RemoveAll(m => m.EndsWith(" cleared", StringComparison.Ordinal));
        }
        if (Muted(A220MonitorRows.CasAdvisoriesKey))
            ann.Queued.RemoveAll(m => m.StartsWith("Advisory: ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Speak the stabilizer/rudder trim once the pilot STOPS trimming. Gated on the
    /// shared Shift+T trim-announcements toggle (BaseAircraftDefinition), and settle-
    /// gated so a continuous trim run doesn't read a number every second. Silent until
    /// the first value has been seen twice — an announcement on connect would read the
    /// whole trim state at startup, which no other MSFSBA monitor does.
    /// </summary>
    private void AnnounceTrimIfSettled(A220Afdx.FlightControlBlock? fc)
    {
        double? cur = fc?.pitch_trim;
        double? prev = _trimPrevPitch;
        _trimPrevPitch = cur;
        if (!_trimAnnouncementsEnabled) { _trimSpokenPitch = cur; return; }
        if (_trimSpokenPitch == null) { if (prev != null && cur != null) _trimSpokenPitch = cur; return; }
        if (!A220Afdx.TrimSettled(_trimSpokenPitch, prev, cur)) return;
        _trimSpokenPitch = cur;
        string? text = A220Afdx.TrimAnnouncement(fc);
        var announcer = _announcer;
        if (text == null || announcer == null) return;
        Marshal(() => announcer.Announce(text));
    }

    /// <summary>Latest FD/FG block for the readout hotkey and the FD panel cells.</summary>
    internal A220Afdx.AutoflightBlock? LatestAutoflight => _afdxAutoflight;

    /// <summary>Latest trim block for the Trim panel cells.</summary>
    internal A220Afdx.FlightControlBlock? LatestFlightControl => _afdxFlightControl;

    /// <summary>Latest FCP selections (for dialog prompts and the metres-mode note).</summary>
    internal A220Afdx.FcpBlock? LatestFcp => _afdxFcp;

    /// <summary>Why the CommBus tap is or isn't delivering — spoken by the Alt+F readout
    /// when there is no data, so a dead link names its own failing layer.</summary>
    internal A220Afdx.TapDiag? LatestTapDiag => _afdxDiag;

    /// <summary>
    /// One-shot AFDX read straight off the agent, bypassing the 1 s display poll. The
    /// knob walks need this: reading a target back from the 1 Hz SimConnect batch is what
    /// forced a ~1.3 s settle per round (slow), and the stock AUTOPILOT * VAR SimVars are
    /// not what the A220's FCP actually publishes (imprecise). Returns null if the link is
    /// down, and every caller must fall back rather than assume.
    /// </summary>
    internal async Task<A220Afdx.LiveBlocks?> ReadAfdxLiveAsync()
    {
        try
        {
            var client = _displaysClient;
            if (client == null) return null;
            string? raw = await client.CallAgentAsync("afdx()");
            if (string.IsNullOrEmpty(raw)) return null;
            var snap = System.Text.Json.JsonSerializer.Deserialize<DisplaysSnap>(raw);
            if (snap == null) return null;
            if (snap.fcp != null) _afdxFcp = snap.fcp;
            if (snap.fc != null) _afdxFlightControl = snap.fc;
            return new A220Afdx.LiveBlocks { fcp = snap.fcp, fc = snap.fc };
        }
        catch { return null; }
    }

    /// <summary>The immediate lines go out as ONE interrupting utterance, then the queued ones.
    /// Every AnnounceImmediate cancels the speech before it, so one call per line left only the
    /// last line of a burst audible (warnings come first, so the warning was the one cut off).</summary>
    private void Speak(A220DisplayParsing.Announcements ann)
    {
        if (ann.Queued.Count == 0 && ann.Immediate.Count == 0) return;
        var announcer = _announcer;
        if (announcer == null) return;
        string immediate = string.Join(". ", ann.Immediate);
        var queued = ann.Queued.ToList();
        Marshal(() =>
        {
            if (immediate.Length > 0) announcer.AnnounceImmediate(immediate);
            foreach (var msg in queued) announcer.Announce(msg);
        });
    }

    private void Marshal(Action action)
    {
        void Safe() { try { action(); } catch { } }
        var ctx = _displaySyncCtx;
        if (ctx != null) ctx.Post(_ => Safe(), null);
        else Safe();
    }

    // Agent JSON shapes (see coherent-a220-displays-agent.js).
    private sealed class DisplaysSnap
    {
        public bool ok { get; set; }
        public List<FmaTok>? fma { get; set; }
        public List<SpdTok>? spd { get; set; }
        public List<CasTok>? cas { get; set; }
        public A220Afdx.AutoflightBlock? af { get; set; }
        public A220Afdx.FcpBlock? fcp { get; set; }
        public A220Afdx.FlightControlBlock? fc { get; set; }
        // A raw element, parsed on its own (SystemsFrom): one store field of an unexpected type
        // must not make the whole snapshot's Deserialize throw and drop FMA, CAS, FD and trim.
        public System.Text.Json.JsonElement? sys { get; set; }
        public A220Afdx.TapDiag? diag { get; set; }
    }
    private sealed class FmaTok { public string? t { get; set; } public double x { get; set; } public string? c { get; set; } }
    private sealed class SpdTok { public string? t { get; set; } public double y { get; set; } }
    private sealed class CasTok { public string? t { get; set; } public string? c { get; set; } }
}
