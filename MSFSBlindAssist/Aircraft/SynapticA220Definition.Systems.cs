using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan A systems over the AFDX CommBus (docs/a220.md "Systems over the CommBus"): the display
/// pump's once-a-second <c>sys</c> block feeds the CAS, gear, engine-start, IRS and door monitors
/// and the panel rows that show them. Every decoder is pure (A220Systems, pinned by
/// SynapticA220SystemsTests); this partial only stores, renders and speaks. The pump runs on a
/// timer, OUTSIDE MainForm's Ctrl+M mute wrap, so every call-out here checks its own synthetic
/// row (A220MonitorRows) itself ([A220-39]).
/// </summary>
public partial class SynapticA220Definition
{
    /// <summary>How long a systems block stays the truth after its poll. The pump polls once a
    /// second; a client that stopped delivering (socket down, page reloading) must not leave the
    /// last block standing as current — rows then read "not available", never a frozen state.</summary>
    private const long SystemsFreshMs = 5000;
    private A220Systems.SystemsBlocks? _sys;     // Volatile.Read/Write; reference-swapped whole
    private long _sysTicks;
    private readonly A220Systems.CasMonitor _casMonitor = new();
    private readonly A220Systems.GearMonitor _gearMonitor = new();
    private readonly A220Systems.EngineStartTracker _engineTracker = new();
    private readonly A220Systems.IrsMonitor _irsMonitor = new();
    private readonly A220Systems.DoorMonitor _doorMonitor = new();

    // A rebaseline request is a counter, not a flag, because two pump-thread consumers must each
    // honour it once: the five monitors (ProcessSystems) and the EICAS-scrape CAS baseline (the
    // pump's CAS block, which runs BEFORE ProcessSystems in the same poll). A single flag consumed
    // by the first would starve the second. Each consumer keeps the count it last honoured.
    private int _rebaselineRequests;     // bumped by RequestSystemsRebaseline, from any thread
    private int _monitorsRebaselined;    // pump thread: the count the monitors have honoured
    private int _scrapeRebaselined;      // pump thread: the count the scrape's CAS baseline has honoured

    /// <summary>The CommBus panel rows (text from <see cref="TryGetSystemsDisplay"/>), beside
    /// one row per door (<see cref="DoorKey"/>). Display names are short: rows read
    /// "Name: value" on a 40-cell braille line.</summary>
    internal static readonly (string Key, string Display)[] SystemsRows =
    {
        ("A22X_GEAR_STATE", "Gear"),
        ("A22X_BRAKE_TEMPS", "Brake temps, L out to R out"),
        ("A22X_IRS_STATE", "IRS"),
        ("A22X_DOORS_STATE", "Doors"),
        ("A22X_ENG1_STATE", "Engine 1"),
        ("A22X_ENG2_STATE", "Engine 2"),
        ("A22X_CAS_STATE", "CAS"),
        ("A22X_XPDR_STATE", "Transponder"),
        ("A22X_L_ALTIMETER_STATE", "Altimeter"),
        ("A22X_R_ALTIMETER_STATE", "Altimeter"),
        ("A22X_L_MINIMUMS_STATE", "Minimums"),
        ("A22X_R_MINIMUMS_STATE", "Minimums"),
    };

    internal static string DoorKey(string id) => "A22X_DOOR_" + id.ToUpperInvariant();

    /// <summary>Latest systems blocks; null when nothing fresh has arrived for
    /// <see cref="SystemsFreshMs"/>.</summary>
    internal A220Systems.SystemsBlocks? LatestSystems
        => Environment.TickCount64 - Interlocked.Read(ref _sysTicks) <= SystemsFreshMs ? Volatile.Read(ref _sys) : null;

    /// <summary>Every writer of the systems blocks goes through here (pump, read-back polls).</summary>
    private void StoreSystems(A220Systems.SystemsBlocks? sys)
    {
        Volatile.Write(ref _sys, sys);
        Interlocked.Exchange(ref _sysTicks, Environment.TickCount64);
    }

    /// <summary>The snapshot's <c>sys</c> element as blocks, or null when it is absent, null, not
    /// an object, or has a field of an unexpected type. The snapshot declares it as a raw element
    /// so one odd store cannot make the snapshot's own deserialisation throw and cost the rest of
    /// the poll (FMA, CAS scrape, FD, trim); only the systems rows read "not available".</summary>
    private static A220Systems.SystemsBlocks? SystemsFrom(JsonElement? sys)
        => sys is { ValueKind: JsonValueKind.Object } element ? A220Systems.ParseSystems(element.GetRawText()) : null;

    /// <summary>The entry points stash the announcer as they run; tests hand one in.</summary>
    internal void UseAnnouncer(ScreenReaderAnnouncer announcer) => _announcer = announcer;

    /// <summary>A flight load or reconnect: the next poll is a new baseline, so nothing of the
    /// loaded state is announced as a change — not by the five monitors, and not by the EICAS
    /// scrape's CAS diff either (the CAS summary is spoken once again).</summary>
    internal void RequestSystemsRebaseline() => Interlocked.Increment(ref _rebaselineRequests);

    /// <summary>True once per <see cref="RequestSystemsRebaseline"/> for the consumer that owns
    /// <paramref name="honoured"/> (pump thread only).</summary>
    private bool TakeRebaseline(ref int honoured)
    {
        int requests = Volatile.Read(ref _rebaselineRequests);
        if (requests == honoured) return false;
        honoured = requests;
        return true;
    }

    /// <summary>One poll's systems blocks (pump thread). Null = the tap is down: keep every
    /// baseline, speak nothing.</summary>
    internal void ProcessSystems(A220Systems.SystemsBlocks? sys)
    {
        if (TakeRebaseline(ref _monitorsRebaselined))
        {
            _casMonitor.Reset();
            _gearMonitor.Reset();
            _engineTracker.Reset();
            _irsMonitor.Reset();
            _doorMonitor.Reset();
        }
        StoreSystems(sys);
        FeedTakeoffSpeeds(sys?.lpfd);   // the scrape fallback feeds it too while the store is down
        if (sys == null) return;

        Speak(_casMonitor.Observe(sys.cas?.msg,
            Muted(A220MonitorRows.CasWarningsKey), Muted(A220MonitorRows.CasAdvisoriesKey)));
        SpeakRow(A220MonitorRows.GearKey, _gearMonitor.Observe(sys.gear));
        foreach (string line in _engineTracker.Observe(sys.eng)) SpeakRow(A220MonitorRows.EngineStartKey, line);
        SpeakRow(A220MonitorRows.IrsKey,
            _irsMonitor.Observe(A220Systems.Irs(sys.lpfd, sys.fmspos), sys.lpfd != null || sys.fmspos != null));
        foreach (string line in _doorMonitor.Observe(sys.doors)) SpeakRow(A220MonitorRows.DoorsKey, line);
    }

    /// <summary>A queued call-out unless its Ctrl+M row is unticked.</summary>
    private void SpeakRow(string rowKey, string? text)
    {
        if (string.IsNullOrEmpty(text) || Muted(rowKey)) return;
        var announcer = _announcer;
        if (announcer == null) return;
        Marshal(() => announcer.Announce(text));
    }

    /// <summary>The CommBus panel rows. Each reads "not available" until its block arrives.</summary>
    private bool TryGetSystemsDisplay(string varKey, out string text)
    {
        var s = LatestSystems;
        switch (varKey)
        {
            case "A22X_GEAR_STATE": text = A220Systems.FormatGear(s?.gear); return true;
            case "A22X_BRAKE_TEMPS": text = A220Systems.FormatBrakeTemps(s?.gear); return true;
            case "A22X_IRS_STATE": text = A220Systems.FormatIrs(A220Systems.Irs(s?.lpfd, s?.fmspos)); return true;
            case "A22X_DOORS_STATE": text = A220Systems.FormatDoorsSummary(s?.doors); return true;
            case "A22X_ENG1_STATE": text = A220Systems.EngineState(s?.eng, 1); return true;
            case "A22X_ENG2_STATE": text = A220Systems.EngineState(s?.eng, 2); return true;
            case "A22X_CAS_STATE":
                text = A220Systems.FormatCasCounts(_casMonitor.HasData && s?.cas != null ? _casMonitor.Current : null);
                return true;
            case "A22X_XPDR_STATE": text = A220Systems.FormatAtc(s?.atc); return true;
            case "A22X_L_ALTIMETER_STATE": text = A220Systems.SideAltimeter(s?.lads, s?.lctp) ?? A220Systems.NotAvailable; return true;
            case "A22X_R_ALTIMETER_STATE": text = A220Systems.SideAltimeter(s?.rads, s?.rctp) ?? A220Systems.NotAvailable; return true;
            case "A22X_L_MINIMUMS_STATE": text = A220Systems.FormatMinimums(s?.lctp); return true;
            case "A22X_R_MINIMUMS_STATE": text = A220Systems.FormatMinimums(s?.rctp); return true;
        }
        if (varKey.StartsWith("A22X_DOOR_", StringComparison.Ordinal))
            foreach (var door in A220Systems.Doors)
                if (varKey == DoorKey(door.Id)) { text = A220Systems.FormatDoor(s?.doors, door.Id); return true; }
        text = "";
        return false;
    }

    // ---- Alt+E: the CAS window; Ctrl+E: the CAS call-outs ----------------------------

    /// <summary>Every active CAS message, warnings first, one row per message ("Caution: EMER
    /// LTS OFF"). From the CAS store; while it is missing, from the EICAS scrape, saying so.</summary>
    internal string CasWindowText()
    {
        if (_casMonitor.HasData && LatestSystems?.cas != null)
            return A220Systems.FormatCasWindow(_casMonitor.Current);
        var scraped = _prevCas;
        if (scraped == null) return "CAS not available — the A220 display link is not connected.";
        string note = "Read from the EICAS display; the data link is not connected.";
        return scraped.Count == 0
            ? $"No CAS messages. {note}"
            : note + "\n" + string.Join("\n", scraped.Select(t => $"{A220DisplayParsing.SeverityFromColor(t.Color)}: {t.Text}"));
    }

    /// <summary>Alt+E, the FbwEwdWindow pattern the A380 and A32NX use (auto-refresh, F5,
    /// Escape), as a tracked window so an aircraft switch disposes it.</summary>
    private void ShowCasWindow(ScreenReaderAnnouncer announcer)
        => ShowTrackedWindow(
            () => new Forms.FbwEwdWindow("A220 CAS — crew alerting messages",
                () => Task.FromResult(CasWindowText()), announcer,
                description: "Every active crew alerting message, warnings first. Read with the arrow keys. F5 refreshes; Escape closes. Auto-refreshes.",
                refreshedMessage: "CAS refreshed",
                loadingText: "Loading CAS messages..."),
            w => { w.Show(); w.BringToFront(); w.Activate(); });

    /// <summary>Ctrl+E: both CAS rows (warnings/cautions, advisories) off or on together; the
    /// same rows Ctrl+M shows, so the two never disagree.</summary>
    private static string ToggleCasCallouts()
    {
        bool on = A220MonitorRows.ToggleCas(Settings.SettingsManager.Current.A220DisabledMonitorVariables);
        Settings.SettingsManager.Save();
        return on ? "CAS call-outs on" : "CAS call-outs off";
    }

    // ---- Take-off roll: "Rotate" on the per-frame feed ([A220-37]) -------------------

    internal const string TakeoffIasKey = "A22X_TAKEOFF_IAS";

    /// <summary>The roll machine's Ctrl+M keys: only Rotate has a row. The aircraft calls V1
    /// itself ([A220-2]) and has no V2 call, so V1/V2 are filtered out of every sentence and
    /// their keys are empty (an empty key is never muted — TakeoffCalloutKeys).</summary>
    private static readonly TakeoffCalloutKeys TakeoffKeys = new(TakeoffIasKey, "", A220MonitorRows.RotateKey, "");
    private readonly TakeoffVSpeedCallouts _takeoffCallouts = new();
    private readonly A220Systems.RollWithoutVrGate _rollWithoutVr = new();
    private bool _calloutOnGround = true;   // last SIM_ON_GROUND; a ramp start is the norm
    private bool _takeoffVrKnown;

    /// <inheritdoc />
    public override string? TakeoffCalloutFeedKey => TakeoffIasKey;
    /// <inheritdoc />
    public override bool TakeoffCalloutFeedNeeded => _takeoffCallouts.NeedsSamples(_calloutOnGround);

    /// <summary>The feed speaks Rotate, whose row is the synthetic Rotate row, so MainForm's
    /// wrap leaves it alone and the branch checks the row itself (as the A320 states it).</summary>
    public override bool IsMuteWrapExempt(string varName) => varName == TakeoffIasKey;

    /// <summary>A reconnect drops the roll's ARM, never the speeds (TKO-5).</summary>
    public override void ResetAnnouncementBaselines()
    {
        base.ResetAnnouncementBaselines();
        _takeoffCallouts.Reset();
        _rollWithoutVr.Reset();
    }

    /// <summary>Every context reset (a flight load included) drops the roll's ARM (TKO-5) and
    /// re-baselines the systems monitors and the scrape's CAS diff, so a loaded flight's state
    /// is not announced.</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _takeoffCallouts.Reset();
        _rollWithoutVr.Reset();
        RequestSystemsRebaseline();
    }

    /// <summary>V1/VR/V2 from the PFD's own bugs (A22X.L PFD Data); the speed-tape scrape is the
    /// fallback while the store is missing. Applied on the UI thread, where the samples run.</summary>
    private void FeedTakeoffSpeeds(A220Systems.PfdBlock? pfd)
    {
        double v1 = pfd?.v1 ?? ScrapedVSpeed("V1");
        double vr = pfd?.vr ?? ScrapedVSpeed("VR");
        double v2 = pfd?.v2 ?? ScrapedVSpeed("V2");
        Marshal(() =>
        {
            _takeoffCallouts.SetV1(v1);
            _takeoffCallouts.SetVR(vr);
            _takeoffCallouts.SetV2(v2);
            _takeoffVrKnown = vr >= TakeoffVSpeedCallouts.ArmBelowKnots;
        });
    }

    private double ScrapedVSpeed(string key) => _vSpeeds.TryGetValue(key, out int v) ? v : 0;

    /// <summary>One per-frame airspeed sample: "Rotate" at VR on the ground (AnnounceImmediate —
    /// its value IS the timing) and one "V-speeds not set" when a roll starts with no VR.</summary>
    private void OnTakeoffIasSample(double ias, ScreenReaderAnnouncer announcer)
    {
        // A frame without a usable speed carries no information: skipping it keeps the last real
        // sample, so a roll's 40-knot crossing is still seen across it (the gate has no NaN guard).
        if (double.IsNaN(ias) || ias < 0) return;
        var callouts = _takeoffCallouts.ProcessSample(ias, _calloutOnGround);
        var muted = Settings.SettingsManager.Current.A220DisabledMonitorVariablesSet;
        if (_rollWithoutVr.Observe(ias, _calloutOnGround, _takeoffVrKnown)
            && !muted.Contains(A220MonitorRows.RotateKey) && !announcer.Suppressed)
            announcer.AnnounceImmediate("V-speeds not set — no Rotate call");
        if (callouts.Count == 0 || announcer.Suppressed) return;
        string? sentence = TakeoffVSpeedCallouts.Compose(callouts,
            callout => callout != "Rotate" || TakeoffKeys.IsMuted(callout, muted));
        if (sentence != null) announcer.AnnounceImmediate(sentence);
    }

    // ---- D / Shift+D: the FMS's active flight plan ------------------------------------

    private async Task AnnounceFlightPlanAsync(ScreenReaderAnnouncer announcer, bool tod)
    {
        A220Systems.FplnProgress? p = null;
        try
        {
            string? raw = await DisplaysAgentCallAsync("fplnProgress()");
            if (!string.IsNullOrEmpty(raw)) p = System.Text.Json.JsonSerializer.Deserialize<A220Systems.FplnProgress>(raw);
        }
        catch (System.Text.Json.JsonException) { }
        announcer.AnnounceImmediate(tod ? A220Systems.FormatDistanceToTod(p) : A220Systems.FormatDistanceToDestination(p));
    }
}
