using System.Text.Json;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Systems over the AFDX CommBus (docs/a220.md "Systems over the CommBus"): the display
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
    // honour it once: the five monitors (ProcessSystems) and the scrape-side baselines (FMA, trim
    // and the EICAS CAS diff, dropped at the top of the pump's poll, BEFORE ProcessSystems). A
    // single flag consumed by the first would starve the second. Each consumer keeps the count it
    // last honoured.
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

    /// <summary>The snapshot's <c>sys</c> element as blocks, or null when it is absent, null or not
    /// an object. The snapshot declares it as a raw element so one odd store cannot make the
    /// snapshot's own deserialisation throw and cost the rest of the poll (FMA, CAS scrape, FD,
    /// trim); and ParseSystems reads each store on its own, so a field of an unexpected type
    /// leaves only that store's rows "not available".</summary>
    private static A220Systems.SystemsBlocks? SystemsFrom(JsonElement? sys)
        => sys is { ValueKind: JsonValueKind.Object } element ? A220Systems.ParseSystems(element.GetRawText()) : null;

    /// <summary>The entry points stash the announcer as they run; tests hand one in.</summary>
    internal void UseAnnouncer(ScreenReaderAnnouncer announcer) => _announcer = announcer;

    /// <summary>A flight load or reconnect: the next poll is a new baseline, so nothing of the
    /// loaded state is announced as a change — not by the five monitors, and not by the scrape's
    /// FMA, trim or CAS diffs either (the CAS summary is spoken once again).</summary>
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
            lock (_bridged) _bridged.Clear();   // every bus state is re-sent to the panel
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
        EmitBridgeValues(sys);
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
    // The V1 and VR the machine holds (UI thread; 0 = unset). FeedTakeoffSpeeds only ever changes a
    // speed on evidence, so these are also what "can the machine arm?" is judged from.
    private double _heldV1, _heldVr;

    /// <summary>Whether a roll can be called at all: the machine arms only with BOTH V1 and VR
    /// set, so a missing V1 counts as "not known" exactly like a missing VR.</summary>
    private bool TakeoffVrKnown
        => _heldV1 >= TakeoffVSpeedCallouts.ArmBelowKnots && _heldVr >= TakeoffVSpeedCallouts.ArmBelowKnots;

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
    /// re-baselines the systems monitors and the scrape's FMA, trim and CAS diffs, so a loaded
    /// flight's state is not announced.</summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _takeoffCallouts.Reset();
        _rollWithoutVr.Reset();
        RequestSystemsRebaseline();
    }

    /// <summary>V1/VR/V2, decided per BLOCK. With the PFD block (A22X.L PFD Data) present it speaks
    /// for all three: each speed is the store's value, else the tape's bug, else 0 — the store
    /// reports an unset speed as null (or 0), so a wiped FMS, a flight load or a fly-again clears
    /// the previous flight's speeds. With NO PFD block (tap down, a poll whose block lacks the
    /// PFD) nothing is known: a speed takes the tape's bug when the tape shows it, otherwise the
    /// machine keeps what it holds. That is absence of evidence, not "unset" — treating it as
    /// unset disarmed a roll in progress, which re-arms only on the ground below 40 kt, and the
    /// next poll restored the speeds too late. Applied on the UI thread, where the samples run.</summary>
    private void FeedTakeoffSpeeds(A220Systems.PfdBlock? pfd)
    {
        double? Pick(double? stored, string key)
            => pfd != null ? stored ?? ScrapedVSpeed(key) ?? 0 : ScrapedVSpeed(key);
        double? v1 = Pick(pfd?.v1, "V1");
        double? vr = Pick(pfd?.vr, "VR");
        double? v2 = Pick(pfd?.v2, "V2");
        if (v1 == null && vr == null && v2 == null) return;
        Marshal(() =>
        {
            if (v1 is { } a) { _takeoffCallouts.SetV1(a); _heldV1 = a; }
            if (vr is { } b) { _takeoffCallouts.SetVR(b); _heldVr = b; }
            if (v2 is { } c) _takeoffCallouts.SetV2(c);
        });
    }

    private double? ScrapedVSpeed(string key) => _vSpeeds.TryGetValue(key, out int v) ? v : null;

    /// <summary>One per-frame airspeed sample: "Rotate" at VR on the ground (AnnounceImmediate —
    /// its value IS the timing) and one "V-speeds not set" when a roll starts with no VR.</summary>
    private void OnTakeoffIasSample(double ias, ScreenReaderAnnouncer announcer)
    {
        // A frame without a usable speed carries no information: skipping it keeps the last real
        // sample, so a roll's 40-knot crossing is still seen across it (the gate has no NaN guard).
        if (double.IsNaN(ias) || ias < 0) return;
        var callouts = _takeoffCallouts.ProcessSample(ias, _calloutOnGround);
        var muted = Settings.SettingsManager.Current.A220DisabledMonitorVariablesSet;
        if (_rollWithoutVr.Observe(ias, _calloutOnGround, TakeoffVrKnown)
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

    // ---- The CommBus bridge ([A220-39]) ------------------------------------------------
    // Some panel controls have NO readable sim variable — their state exists only on the bus
    // (transponder mode, TCAS altitude limit, baro STD, minimums mode). Their combos are
    // UpdateFrequency.Never (never read from the sim), and their current value reaches MainForm
    // here, as a SILENT initial-snapshot update (MainForm.A220 forwards it), exactly as the iFly's
    // SDK bridge feeds its panel. Silent: the change was either the pilot's own (the screen
    // reader read the pick) or a read-back the write path speaks itself.

    /// <summary>Raised from the pump thread with (variable key, value) for a bus-only state.</summary>
    internal event Action<string, double>? CommBusValueChanged;

    private readonly Dictionary<string, double> _bridged = new();
    private volatile bool _bridgeResendPending;

    /// <summary>Raise <see cref="CommBusValueChanged"/> when the value differs from the last one
    /// sent, or always with <paramref name="force"/>; never while the key is held
    /// (<see cref="HoldBridge"/>).</summary>
    internal void EmitBridgeValue(string key, double value, bool force = false)
    {
        lock (_bridged)
        {
            if (_bridgeHolds.ContainsKey(key)) return;   // a write for this control is in flight
            if (!force && _bridged.TryGetValue(key, out double last) && Math.Abs(last - value) < 1e-9) return;
            _bridged[key] = value;
        }
        try { CommBusValueChanged?.Invoke(key, value); }
        catch (Exception ex) { Utils.Logging.Log.Debug("A220", $"CommBus bridge {key}: {ex.Message}"); }
    }

    // A key's emissions are HELD while a write for its control is in flight: MainForm sets a
    // snapshot value straight onto the combo (UpdateControlFromSimVar has no just-set or focus
    // guard), so an emission between two arrow presses would snap the combo back to an
    // intermediate value under the pilot's key. A count, not a flag: overlapping writes each hold
    // the key, and it opens only when the last one is judged. Held values are not recorded as
    // sent, and the write's own ResendBridge() sends the truth once the hold opens.
    private readonly Dictionary<string, int> _bridgeHolds = new();

    internal void HoldBridge(string key)
    {
        lock (_bridged) _bridgeHolds[key] = (_bridgeHolds.TryGetValue(key, out int n) ? n : 0) + 1;
    }

    internal void ReleaseBridge(string key)
    {
        lock (_bridged)
        {
            if (!_bridgeHolds.TryGetValue(key, out int n) || n <= 1) _bridgeHolds.Remove(key);
            else _bridgeHolds[key] = n - 1;
        }
    }

    /// <summary>After a write the aircraft refused (or a combo pick that may not have landed),
    /// re-send every bus state on the next poll so each combo shows the truth again.</summary>
    private void ResendBridge() => _bridgeResendPending = true;

    private void EmitBridgeValues(A220Systems.SystemsBlocks sys)
    {
        bool force = _bridgeResendPending;
        _bridgeResendPending = false;
        void Emit(string key, double? value) { if (value is { } v) EmitBridgeValue(key, v, force); }
        Emit(XpdrModeKey, sys.atc?.mode);
        Emit(TcasLimitKey, sys.atc?.tss_limits_sel);
        Emit("A22X_XPDR_CODE_BUS", sys.atc?.code);
        Emit("A22X_L_BARO_STD", StdValue(sys.lads, sys.lctp));
        Emit("A22X_R_BARO_STD", StdValue(sys.rads, sys.rctp));
        Emit("A22X_L_MINIMUMS_MODE", sys.lctp?.minimums_mode);
        Emit("A22X_R_MINIMUMS_MODE", sys.rctp?.minimums_mode);
        Emit("A22X_L_MINIMUMS", sys.lctp?.minimums);
        Emit("A22X_R_MINIMUMS", sys.rctp?.minimums);
    }

    /// <summary>STD as the PFD draws it (1/0), or null — nothing is claimed for an unknown side.</summary>
    private static double? StdValue(A220Systems.AdsBlock? ads, A220Systems.CtpBlock? ctp)
        => A220Systems.IsStd(ads, ctp) is { } std ? (std ? 1.0 : 0.0) : (double?)null;
}
