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
    private volatile bool _systemsResetPending;

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

    /// <summary>A flight load or reconnect: the next block is a new baseline, so nothing of the
    /// loaded state is announced as a change (the CAS summary is spoken once again).</summary>
    internal void RequestSystemsRebaseline() => _systemsResetPending = true;

    /// <summary>One poll's systems blocks (pump thread). Null = the tap is down: keep every
    /// baseline, speak nothing.</summary>
    internal void ProcessSystems(A220Systems.SystemsBlocks? sys)
    {
        if (_systemsResetPending)
        {
            _systemsResetPending = false;
            _casMonitor.Reset();
            _gearMonitor.Reset();
            _engineTracker.Reset();
            _irsMonitor.Reset();
            _doorMonitor.Reset();
        }
        StoreSystems(sys);
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
}
