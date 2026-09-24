using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Monitors AI and multiplayer aircraft while taxiing and speaks what the pilot would otherwise see:
/// <list type="bullet">
/// <item>proximity ("Traffic" / "Slow down" / "Stop"), naming the aircraft the way ATC does;</item>
/// <item>traffic ON the taxi route ahead, and traffic on a converging track (closest point of approach);</item>
/// <item>the runway watch — while holding short of, crossing, backtracking on, lining up on or waiting
///       on a runway: aircraft on it, on final to it, and landing on it;</item>
/// <item>the pilot's position in the queue (the "departure queue" only at the runway hold);</item>
/// <item>"… ahead is moving." while stopped in a queue, and a tightly gated "Move up."</item>
/// </list>
/// Every rule is a pure, tested unit (<see cref="GroundTrafficLogic"/>, <see cref="RunwayWatchScopes"/>,
/// <see cref="QueueMovementPolicy"/>, <see cref="TrafficSpeechPolicy"/>, <see cref="GroundTrafficSuppression"/>);
/// this class gathers inputs, calls them, and speaks and logs the result (ground_traffic.log).
/// It ticks every second and sweeps its OWN small-radius traffic request (never TCAS's) every second
/// while something can change an answer, every third second otherwise, and never with one of its
/// sweeps still outstanding.
/// </summary>
public sealed class GroundTrafficMonitor : IDisposable
{
    // Alert zone thresholds in feet — sized for widebody datum-to-datum measurement.
    // SimConnect gives center-to-center distance; two 787s have ~203 ft of combined
    // half-fuselage, so each threshold is the desired nose-to-tail gap plus ~200 ft.
    // These are the MINIMUM (stopped-aircraft) thresholds; a speed-based lead allowance is
    // added so the pilot is actually stopped at these distances. See ZONE_LEAD_SEC.
    private const double AWARENESS_FT = 600.0;
    private const double CAUTION_FT   = 400.0;
    private const double WARNING_FT   = 250.0;

    // Lead time (seconds) added to each zone boundary for reaction + braking + poll lag:
    // effective boundary = fixed_ft + v × ktsToFps × ZONE_LEAD_SEC. Sized for the 3 s worst-case
    // poll lag, which the slow cadence still has.
    private const double ZONE_LEAD_SEC = 7.0;
    private const double ZONE_LEAD_KTSFPS = 1.6878; // ft/s per knot

    /// <summary>The speed lead added to every zone boundary at <paramref name="ownGsKts"/>: the evaluation's and the fast poll's.</summary>
    private static double ZoneLeadFt(double ownGsKts) => ownGsKts * ZONE_LEAD_KTSFPS * ZONE_LEAD_SEC;

    // Minimum own GS before a caution-zone alert becomes "Slow down" rather than an awareness ping.
    private const double SLOW_DOWN_GS_KTS = 2.0;

    // Age out aircraft not seen for this long; evaluation ignores samples older than FRESH_MS (L10),
    // the runway watch older than RUNWAY_FRESH_MS.
    private const int PRUNE_AGE_MS = 12000;
    private const int FRESH_MS = 5000;
    private const int RUNWAY_FRESH_MS = 4000;

    // Route awareness
    private const double ON_ROUTE_LATERAL_M = 30.0;      // on the route
    private const double OFF_ROUTE_REARM_M = 50.0;       // clearly off it again → re-arm
    private const double NEAR_ROUTE_M = 60.0;            // close enough to the route to be a proximity threat
    private const double ROUTE_ALERT_MIN_AHEAD_M = 15.0;
    private const double ROUTE_ALERT_MAX_AHEAD_M = 600.0;
    private const double OWN_ROUTE_MAX_LATERAL_M = 40.0; // own aircraft must be on its route for route maths

    // Converging (closest point of approach). "Moving" traffic is GroundTrafficLogic.MovingTrafficKts; the
    // route threat test's own closest-approach limits live with it (GroundTrafficLogic.IsRouteThreat).
    private const double CONFLICT_DCPA_M = 45.0;
    private const double CONFLICT_REARM_DCPA_M = 90.0;
    private const double CONFLICT_MIN_TCPA_S = 5.0;
    private const double CONFLICT_MAX_TCPA_S = 40.0;

    // Queue position must read the same on this many evaluations before it is spoken.
    private const int QUEUE_CONFIRM_EVALS = 2;

    // Runway watch
    private const double SHORT_FINAL_NM = 2.0;
    private const double ROLLING_KTS = 30.0;
    // A watch's first status waits at most this long for an aircraft whose climb rate is not known yet
    // (RunwayTrafficKind.LandingPending) to be decided.
    private const int FIRST_STATUS_MAX_DEFER_MS = 3000;

    // Hotkey summary
    private const int SUMMARY_MAX_AIRCRAFT = 3;
    private const int SUMMARY_SWEEP_TIMEOUT_MS = 1500;

    // Polling: 1 s tick; the sweep runs every tick when fast, every third otherwise, and a new one is
    // only requested when none of ours is outstanding (or the outstanding one is older than this — lost).
    private const int POLL_INTERVAL_MS = 1000;
    private const int SLOW_POLL_EVERY_TICKS = 3;
    private const int SWEEP_STALE_MS = 3000;

    private const double NM_TO_FEET = 6076.12;
    private const double FEET_PER_METRE = GroundTrafficLogic.FeetPerMetre;

    private static readonly LogChannel _log = Log.Channel("ground_traffic");

    /// <summary>Proximity gate: while it returns true, proximity, route, queue and nudge callouts are silent.</summary>
    public Func<bool>? SuppressCheck { get; set; }

    /// <summary>Runway-watch gate (<see cref="GroundTrafficSuppression.SuppressRunwayWatch"/>). Unset → follows <see cref="SuppressCheck"/>.</summary>
    public Func<bool>? RunwayWatchSuppressCheck { get; set; }

    /// <summary>The taxi route in progress (route ahead, runways, hold facts), or null.</summary>
    public Func<GroundTrafficRouteContext?>? RouteContextProvider { get; set; }

    /// <summary>Takeoff assist's runway and airport while it is active, else null (the line-up wait).</summary>
    public Func<(string RunwayId, string AirportIcao)?>? TakeoffRunwayProvider { get; set; }

    /// <summary>
    /// The airport's runway centerlines by ICAO, for a takeoff-assist runway with no taxi route (a
    /// departure that starts on the runway). Called at most once per airport; the result is cached.
    /// </summary>
    public Func<string, IReadOnlyList<TaxiGraph.RunwayCenterline>>? RunwaySupplier { get; set; }

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IGroundTrafficSimSource _sim;
    // Null in the headless constructor (GroundTrafficMonitorHeadlessTests), which drives ticks itself.
    private readonly System.Windows.Forms.Timer? _timer;
    // The monitor's ONE clock: DateTime.UtcNow in the app; the headless tests pass a simulated one, so
    // the time-based rules (the speech policy's spacing, the escalation window, the runway watch's
    // grace and deferral timers) run as they would a second apart in the sim.
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private readonly Dictionary<uint, TrackedGroundAircraft> _tracked = new();

    // Own-position snapshot (written on the UI thread, read under _lock)
    private double _ownLat, _ownLon, _ownHeadingTrue, _ownGS, _ownAltFt;
    private bool _positionValid;

    // Intake gates, read by OnAiTrafficReceived under _lock
    private bool _runwayWatchActive;
    private bool _queueScanActive;

    // Poll bookkeeping (UI thread)
    private int _tickCount;
    private DateTime _sweepRequestedUtc = DateTime.MinValue;             // our outstanding sweep; MinValue = none
    private uint _sweepRequestId;                                       // its request id; 0 = none
    private DateTime _lastCompletedSweepRequestedUtc = DateTime.MinValue;
    private uint _lastRadius;

    /// <summary>
    /// What the tick that requested the outstanding sweep saw. The completion evaluates exactly this,
    /// so the watch key, the scope and the evaluation can never disagree (PR #247 review L9).
    /// </summary>
    private sealed record Cycle(GroundTrafficRouteContext? Ctx, RunwayWatch Watch, bool Proximity, bool WatchGate);
    private Cycle? _cycle;

    // The ONE runway cache: the last local context's runways and airport, or — for a takeoff-assist
    // runway with no local context — RunwaySupplier's for that airport. StopGuidance nulls the taxi
    // graph when takeoff assist takes over; the line-up wait still needs the runway's geometry.
    private IReadOnlyList<TaxiGraph.RunwayCenterline> _cachedRunways = Array.Empty<TaxiGraph.RunwayCenterline>();
    private string _cachedRunwaysIcao = "";

    // Runway watch (UI thread)
    private RunwayWatch _currentWatch = RunwayWatch.None;
    private string _watchKey = "";
    private RunwayWatchMode _loggedWatchMode = RunwayWatchMode.None;
    private DateTime _watchStartedUtc = DateTime.MinValue;
    private bool _watchSummaryDone;
    // The same watch moving from a queuing mode (Holding, Vacating) into an interrupting one (OnRunway,
    // LiningUp, TakeoffWait) re-arms the first status ONCE per watch
    // (RunwayWatchScopes.ShouldRearmOnModeChange): a status already handed to the announcer may have
    // been cut off by the very instruction that moved the pilot — from Holding only when the hold was
    // released within RunwayWatchScopes.RearmAfterHoldWindowMs of that hand-over
    // (_firstStatusHandedOverUtc, _holdReleasedUtc; PR #247 re-review M4, focused re-review N1), from
    // Vacating at any time. The re-armed status is CRITICAL-ONLY (_rearmCriticalOnly) — spoken,
    // interrupting, only when something is on the runway or on short final, otherwise completed
    // silently (PR #247 final review H2, replacing B4's full-status Vacating -> OnRunway re-arm). All
    // of them reset alongside _watchSummaryDone in ResetRunwayWatch.
    private bool _firstStatusRearmed;
    private bool _rearmCriticalOnly;
    // When the watch's first status was handed to the announcer (its OnEmitted); MinValue = not yet.
    private DateTime _firstStatusHandedOverUtc = DateTime.MinValue;
    // When the HOLD was released: the moment a linger began from a Holding watch (ApplyLinger) — at a
    // crossing, about when Continue was pressed. Continue ends the hold source there, but the watch then
    // LINGERS in Holding until the aircraft reaches the pavement and the watch becomes OnRunway, 11-15 s
    // later from a median hold line, so a re-arm window measured to that mode change almost never
    // admitted the crossing whose "Continuing." had cut the hold status off (PR #247 focused re-review
    // N1). It outlives the EndLinger("resumed") ApplyLinger runs at the runway entry, so SetWatch's
    // re-arm check at that change still reads it; SetWatch drops it once a watch is adopted with no
    // linger in progress, and ResetRunwayWatch clears it. Null = no such release pending.
    private DateTime? _holdReleasedUtc;
    // When the first status was first held back for a pending aircraft; MinValue = not deferred.
    private DateTime _firstStatusDeferredSinceUtc = DateTime.MinValue;
    // The runways (by KEY) whose "no traffic seen on the runway now" line is due: queued when the LAST
    // known occupant recorded under that key is forgotten by the grace timer (never by the scope purge —
    // GroundTrafficLogic.EmptiedRunwayKeys), removed when its line is handed over or when an aircraft is
    // seen on that runway again (PR #247 re-review M2).
    private readonly HashSet<string> _runwayEmptiedPendingKeys = new(StringComparer.Ordinal);
    // A single-runway watch whose sources all ended, kept while the aircraft is still crossing (ApplyLinger).
    private RunwayWatchLinger.Anchor? _linger;
    // A watch the watch GATE closed on is SUSPENDED, not ended (SuspendWatch; PR #247 final review H5):
    // its key and when; "" = none suspended. While suspended _watchKey is "" and nothing is watched,
    // but every per-watch field (known sets, first-status state, _watchStartedUtc, _loggedWatchMode) is
    // kept, and SetWatch RESUMES it — no new first status — when the same key comes back within
    // RunwayWatchScopes.WatchResumeGraceMs. Any other watch adopted meanwhile, an airport or database
    // change, or the grace lapsing (checked at the top of each tick) ends it as a stopped watch
    // (EndSuspendedWatch). At most one watch is suspended: adopting an active watch always consumes it.
    private string _suspendedKey = "";
    private DateTime _suspendedUtc = DateTime.MinValue;
    private readonly HashSet<uint> _knownOccupants = new();
    private readonly HashSet<uint> _knownFinals = new();
    private readonly HashSet<uint> _shortFinalAnnounced = new();
    // When each known occupant / known final was first missed (GroundTrafficLogic.ForgetAbsent): a
    // one-evaluation classification drop neither re-announces it nor empties the runway. The finals map
    // also covers _shortFinalAnnounced, which is only ever a subset of _knownFinals.
    private readonly Dictionary<uint, DateTime> _occupantAbsentSince = new();
    private readonly Dictionary<uint, DateTime> _finalAbsentSince = new();
    // The runway KEY (WatchedRunway.Key, both ends — never the spoken designator, which for a
    // position-only watch names the nearer end and flips at mid-runway) each known occupant was last
    // seen under, and — in a SEPARATE map — each known final. An id CAN be both: a landing aircraft is
    // a known occupant from touchdown while still a known final until KnownAbsenceGraceMs later, so the
    // final's cleanup must never touch the occupant's record (PR #247 re-review Critical 1: one shared
    // map lost the occupant's entry with the final's, and every landing aircraft was announced twice).
    // Each map is written only for its own kind — when an id becomes known, and refreshed for every id
    // seen under that kind each evaluation — and cleaned only with its own set: an occupant forgotten or
    // purged loses its occupant entry, a final its final entry. Only a known id's entry is ever read. A
    // runway that merely left the watch's widened scan (H3) is told apart from one that genuinely
    // emptied by these keys (GroundTrafficLogic.IdsOutOfScope): the former is purged silently (K3).
    private readonly Dictionary<uint, string> _knownOccupantRunway = new();
    private readonly Dictionary<uint, string> _knownFinalRunway = new();

    // Queue position (UI thread)
    private int _queueCandidate, _queueConfirm, _queueAnnounced;
    private GroundTrafficLogic.QueueReading? _queueReadingForSummary;

    // "Move up" nudge (UI thread). _nudgeLeaderId is the aircraft whose announced departure armed it:
    // while it is still moving it is not "something else ahead" that disarms the nudge (PR #247 final
    // review H1) — though while it is still within 250 ft the nudge stays silent (focused re-review
    // N3); stopped again, it counts like any other aircraft (re-review M3). Null whenever the nudge is
    // disarmed.
    private NudgeState _nudge = NudgeState.Disarmed;
    private uint? _nudgeLeaderId;

    // Speech bookkeeping (UI thread)
    private DateTime _lastAlertLineUtc = DateTime.MinValue;
    private TrafficCalloutKind? _lastInterruptKind;
    private DateTime _lastInterruptUtc = DateTime.MinValue;

    // Change-only logging (UI thread)
    private string _lastGateLog = "", _lastQueueLog = "";
    private bool _contextDroppedLogged;

    // True while a hotkey summary is waiting for its requested traffic sweep to complete.
    private bool _summaryPending;
    private readonly System.Windows.Forms.Timer? _summaryTimeout;

    public GroundTrafficMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim)
        : this(announcer, new SimConnectGroundTrafficSource(sim), startTimers: true) { }

    /// <summary>
    /// Headless constructor (GroundTrafficMonitorHeadlessTests): a simulated traffic source and NO WinForms
    /// timers — the caller runs each poll tick with <see cref="TickForHarness"/> and completes the sweep a
    /// tick requested through the source's own <see cref="IGroundTrafficSimSource.GroundTrafficSweepCompleted"/>,
    /// exactly as SimConnect does — and <paramref name="utcNow"/>, the clock every rule reads (null =
    /// <see cref="DateTime.UtcNow"/>, which is what the app uses).
    /// </summary>
    internal GroundTrafficMonitor(ScreenReaderAnnouncer announcer, IGroundTrafficSimSource source, bool startTimers,
        Func<DateTime>? utcNow = null)
    {
        _announcer = announcer;
        _sim = source;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _sim.AiTrafficReceived += OnAiTrafficReceived;
        _sim.GroundTrafficSweepCompleted += OnGroundTrafficSweepCompleted;
        if (!startTimers) return;

        _timer = new System.Windows.Forms.Timer { Interval = POLL_INTERVAL_MS };
        _timer.Tick += OnTick;
        _timer.Start();

        _summaryTimeout = new System.Windows.Forms.Timer { Interval = SUMMARY_SWEEP_TIMEOUT_MS };
        _summaryTimeout.Tick += (_, _) => CompleteSummaryAnnounce();
    }

    /// <summary>One poll tick, for the headless harness (the app's timer calls the same method).</summary>
    internal void TickForHarness() => OnTick(null, EventArgs.Empty);

    // ──────────────────────────────────────────────────────────────────────────
    // Gates

    private bool Live() => (_sim.LastKnownOnGround ?? false) && _sim.IsConnected;

    private bool ProximityGateOpen() => SuppressCheck?.Invoke() != true;

    private bool WatchGateOpen() => RunwayWatchSuppressCheck != null
        ? RunwayWatchSuppressCheck() != true
        : ProximityGateOpen();

    // ──────────────────────────────────────────────────────────────────────────
    // Timer

    private void OnTick(object? sender, EventArgs e)
    {
        bool live = Live();
        bool proximity = live && ProximityGateOpen();
        bool watchGate = live && WatchGateOpen();
        LogGates(proximity, watchGate);
        // A suspended watch whose key has not come back within the grace ends here, as a stopped watch.
        EndLapsedSuspension(_utcNow());

        if (!proximity) ResetProximityState();
        // The watch gate closing SUSPENDS the watch in progress rather than ending it (H5); the linger is
        // still cleared.
        if (!watchGate) { ClearLinger("gate"); SuspendWatch(); }
        if (!proximity && !watchGate)
        {
            lock (_lock) { _runwayWatchActive = false; _queueScanActive = false; }
            return;
        }

        // Proactively refresh own position so distances stay accurate even when no other guidance
        // system is streaming it.
        var pos = _sim.LastKnownPosition;
        _sim.RequestAircraftPosition();
        if (pos == null) return;
        var p = pos.Value;
        double hdgTrue = NormalizeDeg(p.HeadingMagnetic + p.MagneticVariation);
        lock (_lock)
        {
            _ownLat = p.Latitude;
            _ownLon = p.Longitude;
            _ownHeadingTrue = hdgTrue;
            _ownGS = p.GroundSpeedKnots;
            _ownAltFt = p.Altitude;
            _positionValid = true;
        }

        // Decide the watch BEFORE requesting the sweep, so the intake keeps the airborne/far runway
        // aircraft this very sweep returns.
        var ctx = LocalContext(p.Latitude, p.Longitude);
        // A clock read of its own, not the `now` below: that must stay AFTER SetWatch — the first-status gate
        // compares the completed sweep's REQUEST time against _watchStartedUtc (set inside SetWatch),
        // so the sweep requested later in this tick must carry a time no earlier than the watch start.
        // ApplyLinger's time feeds the linger's 60 s ceiling and, for a linger begun from a hold, the
        // hold's release time (_holdReleasedUtc) that SetWatch measures a re-arm to.
        var watch = watchGate
            ? ApplyLinger(ResolveWatch(ctx, p.Latitude, p.Longitude, p.GroundSpeedKnots), ctx, p.Latitude, p.Longitude, _utcNow())
            : ClearLinger("gate");
        SetWatch(watch);
        bool queueScan = proximity && ctx is { IsQueueRoute: true };
        lock (_lock) { _runwayWatchActive = watch.IsActive; _queueScanActive = queueScan; }

        _tickCount++;
        var now = _utcNow();
        bool outstanding = _sweepRequestedUtc != DateTime.MinValue
                           && (now - _sweepRequestedUtc).TotalMilliseconds < SWEEP_STALE_MS;
        bool fast;
        lock (_lock)
            fast = GroundTrafficLogic.NeedsFastPoll(watch.IsActive,
                _ownGS <= QueueMovementPolicy.OwnQueueGsKts, _ownGS, CAUTION_FT + ZoneLeadFt(_ownGS),
                GroundGeometryForPoll(now));
        if (!outstanding && (fast || _tickCount % SLOW_POLL_EVERY_TICKS == 0))
        {
            _cycle = new Cycle(ctx, watch, proximity, watchGate);
            RequestSweep(now, GroundTrafficLogic.SweepRadiusMeters(watch.IsActive, queueScan));
        }
        PruneStaleAircraft();
    }

    private GroundTrafficRouteContext? SafeContext()
    {
        try { return RouteContextProvider?.Invoke(); }
        catch (Exception ex)
        {
            _log.Debug($"Route context error: {ex.Message}");
            return null;
        }
    }

    /// <summary>The route context, unless it belongs to another airport (L6: a Progressive hold left over from a hand-flown departure).</summary>
    private GroundTrafficRouteContext? LocalContext(double lat, double lon)
    {
        var ctx = SafeContext();
        if (ctx == null) { _contextDroppedLogged = false; return null; }
        if (!RunwayWatchScopes.IsLocal(ctx.RouteAhead, ctx.Runways, lat, lon))
        {
            if (!_contextDroppedLogged)
            {
                _log.Info($"ev=context dropped reason=not-local icao={ctx.AirportIcao}");
                _contextDroppedLogged = true;
            }
            return null;
        }
        _contextDroppedLogged = false;
        if (ctx.Runways.Count > 0) CacheRunways(ctx.Runways, ctx.AirportIcao);
        return ctx;
    }

    /// <summary>
    /// Adopts <paramref name="runways"/> as the runway cache. Another airport ends the watch — a
    /// suspended one too — and any linger: all belong to the previous airport's runways, a watch
    /// surviving into the new airport could begin a linger against a same-named runway there (PR #247
    /// B2 review), and a suspended one could resume there. The same holds after
    /// <see cref="ClearRunwayCache"/>, which forgets which airport the cache held.
    /// </summary>
    private void CacheRunways(IReadOnlyList<TaxiGraph.RunwayCenterline> runways, string icao)
    {
        if (!string.Equals(icao, _cachedRunwaysIcao, StringComparison.OrdinalIgnoreCase))
        {
            ClearLinger("airport-change");
            EndSuspendedWatch();
            SetWatch(RunwayWatch.None);
        }
        _cachedRunways = runways;
        _cachedRunwaysIcao = icao;
    }

    /// <summary>
    /// Loads <paramref name="icao"/>'s runways from <see cref="RunwaySupplier"/> into the cache — kept
    /// even when empty, so an airport the database lacks is not queried again every tick.
    /// </summary>
    private void LoadSuppliedRunways(string icao)
    {
        IReadOnlyList<TaxiGraph.RunwayCenterline> runways = Array.Empty<TaxiGraph.RunwayCenterline>();
        try
        {
            runways = RunwaySupplier?.Invoke(icao) ?? runways;
            _log.Info($"ev=runways source=supplier icao={icao} count={runways.Count}");
        }
        catch (Exception ex)
        {
            runways = Array.Empty<TaxiGraph.RunwayCenterline>();
            _log.Warn($"ev=runways source=supplier icao={icao} count=0 error=\"{Q(ex.Message)}\"");
        }
        CacheRunways(runways, icao);
    }

    /// <summary>
    /// Clears the runway cache — a database switch, after which the same airport can carry different
    /// runway names and geometry — and any linger measured against it. The next local route context or
    /// takeoff-assist runway loads the runways again, and — like an airport change — ends the watch, so
    /// one in progress restarts on the new database's runways.
    /// </summary>
    public void ClearRunwayCache()
    {
        _cachedRunways = Array.Empty<TaxiGraph.RunwayCenterline>();
        _cachedRunwaysIcao = "";
        ClearLinger("runways-cleared");
    }

    /// <summary>
    /// The runways a watch is resolved and scanned against: a (local) route context's OWN list whenever
    /// there is one — even an empty one, never the cache, which may hold another airport's runways — and
    /// the cache only when there is no local context (the takeoff-wait / <see cref="RunwaySupplier"/>
    /// path). PR #247 final review H6.
    /// </summary>
    private IReadOnlyList<TaxiGraph.RunwayCenterline> RunwaysFor(GroundTrafficRouteContext? ctx)
        => ctx != null ? ctx.Runways : _cachedRunways;

    /// <summary>
    /// This tick's watch (<see cref="RunwayWatchScopes.Resolve"/>). <paramref name="ownGsKts"/> is the
    /// tick's own ground speed: on a landing-exit route, the runway just landed on
    /// (<see cref="GroundTrafficRouteContext.LandingRunway"/> — no other runway under the aircraft) is
    /// Vacating (queued) while the pilot is still moving and OnRunway (interrupting) once stopped on it.
    /// <see cref="_currentWatch"/>'s mode and key — the PREVIOUS evaluation's, since this runs before
    /// <see cref="SetWatch"/> adopts this tick's watch — feed <see cref="RunwayWatchInputs.VacatingRunwayKey"/>
    /// (the key of the runway that was Vacating, or null when the previous watch was not Vacating, or was
    /// a multi-runway watch whose joined key matches no single runway) so Vacating holds down to
    /// <see cref="RunwayWatchScopes.VacatingHoldGsKts"/> for THAT runway only, instead of flipping to
    /// OnRunway tick by tick as the pilot decelerates through the turn — and never leaking onto a
    /// different runway entered right after the exit (PR #247 B4 review Important 2).
    /// </summary>
    private RunwayWatch ResolveWatch(GroundTrafficRouteContext? ctx, double lat, double lon, double ownGsKts)
    {
        // With no local route context, a takeoff-assist runway at an airport the cache does not hold (a
        // departure that starts on the runway: a teleport, takeoff assist seeded from the dialog) loads
        // that airport's runways once. A blank ICAO names no airport and leaves the cache alone.
        var takeoff = TakeoffRunwayProvider?.Invoke();
        if (ctx == null && RunwaySupplier != null && takeoff is { } t && !string.IsNullOrWhiteSpace(t.AirportIcao)
            && !string.Equals(t.AirportIcao, _cachedRunwaysIcao, StringComparison.OrdinalIgnoreCase))
            LoadSuppliedRunways(t.AirportIcao);

        var runways = RunwaysFor(ctx);
        if (runways.Count == 0) return RunwayWatch.None;

        string? takeoffRunway = null;
        if (takeoff is { } ta
            && (ctx != null || string.Equals(ta.AirportIcao, _cachedRunwaysIcao, StringComparison.OrdinalIgnoreCase)))
            takeoffRunway = ta.RunwayId;

        return RunwayWatchScopes.Resolve(new RunwayWatchInputs(
            ctx?.State ?? TaxiGuidanceState.Inactive,
            ctx?.HeldRunwayLabel,
            ctx?.ProgressiveRunway,
            ctx?.DestinationName,
            ctx?.IsRunwayDestination ?? false,
            RunwayWatchScopes.RunwaysUnder(runways, lat, lon),
            takeoffRunway,
            runways,
            ctx?.IsLandingExit ?? false,
            ownGsKts,
            VacatingRunwayKey: _currentWatch.Mode == RunwayWatchMode.Vacating ? _currentWatch.Key : null,
            LandingRunway: ctx?.LandingRunway));
    }

    /// <summary>
    /// Adopts this tick's watch. A different key (the runway itself — unchanged from hold through
    /// backtrack, lineup and takeoff wait, and through another runway's pavement, which only widens
    /// <see cref="RunwayWatch.Runways"/>: <see cref="RunwayWatch.Key"/>) restarts the watch; the same
    /// key never does. A SUSPENDED watch (<see cref="SuspendWatch"/>) resumes when its own key comes
    /// back within <see cref="RunwayWatchScopes.WatchResumeGraceMs"/> — same known sets, same
    /// first-status state, same readiness, no new first status; any other active watch adopted
    /// meanwhile ends it first, as a stopped watch (PR #247 final review H5). Adopting no watch leaves
    /// a suspension as it is.
    /// </summary>
    private void SetWatch(RunwayWatch watch)
    {
        _currentWatch = watch;
        if (watch.IsActive && _suspendedKey.Length > 0)
        {
            if (RunwayWatchScopes.ShouldResumeSuspended(_suspendedKey, _suspendedUtc, watch.Key, _utcNow()))
            {
                _watchKey = _suspendedKey;
                _suspendedKey = "";
                _suspendedUtc = DateTime.MinValue;
                _log.Info($"ev=watch resume key={_watchKey}");
            }
            else
                EndSuspendedWatch();
        }
        if (watch.Key == _watchKey)
        {
            // The same watch in another mode (hold → backtrack → lineup → takeoff wait, a crossing's
            // linger): logged once per change, so the sim session can see it stayed one watch.
            if (watch.IsActive && watch.Mode != _loggedWatchMode)
            {
                _log.Info($"ev=watch mode key={watch.Key} mode={watch.Mode}");
                // From a queuing mode into an interrupting one under this same key (_loggedWatchMode is
                // the mode last adopted for it): Continue at a hold ("Continuing.", "Entering Runway
                // 27L…", the backtrack instruction) or stopping on the runway after a landing exit. A
                // first status already handed to the announcer may have been cut off by exactly that
                // AnnounceImmediate, and its latches marked every occupant and final known — so re-arm
                // it ONCE per watch, critical-only (PR #247 final review H2). From Holding only while a
                // PROMPT Continue could have cut the queued hold status off (the hold released within
                // RearmAfterHoldWindowMs of its hand-over); later, the re-armed status would interrupt
                // the Continue instruction with a status the pilot has most likely already heard (M4).
                // The window runs from the hand-over to the moment the HOLD WAS RELEASED, not to this
                // change: at a crossing the watch lingers in Holding from Continue until the pavement
                // makes it OnRunway, 11-15 s later from a median hold line, so the start of that linger
                // (_holdReleasedUtc) is the release; with no linger (a destination hold goes straight
                // to LiningUp or the backtrack) it is this change itself (PR #247 focused re-review N1).
                // Vacating has no window.
                var changeUtc = _utcNow();
                DateTime measuredTo = _loggedWatchMode == RunwayWatchMode.Holding ? _holdReleasedUtc ?? changeUtc : changeUtc;
                double sinceHandedOverMs = (measuredTo - _firstStatusHandedOverUtc).TotalMilliseconds;
                if (RunwayWatchScopes.ShouldRearmOnModeChange(_loggedWatchMode, watch.Mode, sinceHandedOverMs)
                    && _watchSummaryDone && !_firstStatusRearmed)
                {
                    _watchSummaryDone = false;
                    _firstStatusRearmed = true;
                    _rearmCriticalOnly = true;
                    _firstStatusDeferredSinceUtc = DateTime.MinValue;
                    string reason = _loggedWatchMode == RunwayWatchMode.Holding ? "entered-runway" : "stopped-on-runway";
                    _log.Info($"ev=watch status-rearmed key={watch.Key} reason={reason}");
                }
                _loggedWatchMode = watch.Mode;
            }
            // A release time belongs to the change that ends ITS linger, read just above: once a watch is
            // adopted with no linger in progress it is dropped, so a later change straight from a hold —
            // a hold taken up again under this key, then Continue to the lineup — is measured to itself,
            // never to an old linger's start (N1).
            if (watch.IsActive && _linger == null) _holdReleasedUtc = null;
            return;
        }
        if (_watchKey.Length > 0) _log.Info($"ev=watch stop key={_watchKey}");
        ResetRunwayWatch();
        _watchKey = watch.Key;
        _watchStartedUtc = _utcNow();
        _loggedWatchMode = watch.Mode;
        if (watch.IsActive)
            _log.Info($"ev=watch start key={watch.Key} des={string.Join("+", watch.Runways.Select(r => r.Designator))} mode={watch.Mode}");
    }

    /// <summary>
    /// A single-runway watch whose sources all ended lingers while the aircraft is still crossing that
    /// runway (<see cref="RunwayWatchLinger"/>) — same key, Holding mode — instead of stopping and
    /// restarting with a second full first status. Any active resolution replaces it at once.
    /// </summary>
    private RunwayWatch ApplyLinger(RunwayWatch resolved, GroundTrafficRouteContext? ctx, double lat, double lon, DateTime now)
    {
        if (resolved.IsActive) { if (_linger != null) EndLinger(resolved.Key == _watchKey ? "resumed" : "new-watch"); return resolved; }
        if (!_currentWatch.IsActive || _currentWatch.Runways.Count != 1) { if (_linger != null) EndLinger("no-watch"); return RunwayWatch.None; }

        var runways = RunwaysFor(ctx);
        var cl = runways.FirstOrDefault(r => RouteRunwayCrossings.CenterlineHasDesignator(r, _currentWatch.Runways[0].Designator));
        if (cl == null) { if (_linger != null) EndLinger("no-runway"); return RunwayWatch.None; }
        var shape = RunwayShape.For(cl);
        var (along, lateral) = shape.Project(lat, lon);

        if (_linger == null)
        {
            if (!RunwayWatchLinger.CanBegin(lateral, along, shape.ExtentMinMeters, shape.ExtentMaxMeters))
                return RunwayWatch.None;
            _linger = new RunwayWatchLinger.Anchor(lateral, now);
            // A linger begun from a HOLD: the hold has just been released (Continue ended its source),
            // the moment SetWatch measures a from-Holding re-arm to (N1). Kept, not overwritten, if a
            // linger begins again before any watch is adopted without one (only after ClearRunwayCache
            // ended the first): that hold was still released when the first began.
            if (_currentWatch.Mode == RunwayWatchMode.Holding) _holdReleasedUtc ??= now;
            _log.Info(FormattableString.Invariant($"ev=watch linger key={_watchKey} lateral={lateral:0}"));
        }
        var verdict = RunwayWatchLinger.Evaluate(_linger.Value, lateral, shape.HalfWidthMeters, now);
        if (verdict == RunwayLingerVerdict.Keep)
            return _currentWatch with { Mode = RunwayWatchMode.Holding };
        EndLinger(verdict switch
        {
            RunwayLingerVerdict.ClearFarSide => "clear",
            RunwayLingerVerdict.TurnedAway => "turned-away",
            _ => "timeout",
        });
        return RunwayWatch.None;
    }

    private void EndLinger(string reason)
    {
        _log.Info($"ev=watch linger-end key={_watchKey} reason={reason}");
        _linger = null;
    }

    private RunwayWatch ClearLinger(string reason)
    {
        if (_linger != null) EndLinger(reason);
        return RunwayWatch.None;
    }

    /// <summary>
    /// The watch gate is closed: nothing is watched, but the watch in progress is SUSPENDED, not ended
    /// (PR #247 final review H5) — in a landing rollout the gate follows <c>Suppress</c>'s rolling line
    /// (about 3 kt), so creeping at about that speed flipped it, and every reopening restarted the
    /// watch with a full first status. Every per-watch field is kept for <see cref="SetWatch"/> to
    /// resume; the caller has already cleared any linger. Logs <c>ev=watch suspend</c> once.
    /// </summary>
    private void SuspendWatch()
    {
        _currentWatch = RunwayWatch.None;
        if (_watchKey.Length == 0) return;   // no watch in progress, or it is already suspended
        _suspendedKey = _watchKey;
        _suspendedUtc = _utcNow();
        _watchKey = "";
        _log.Info($"ev=watch suspend key={_suspendedKey}");
    }

    /// <summary>A suspended watch whose key has not come back within the grace ends as a stopped watch.</summary>
    private void EndLapsedSuspension(DateTime now)
    {
        if (_suspendedKey.Length > 0
            && !RunwayWatchScopes.ShouldResumeSuspended(_suspendedKey, _suspendedUtc, _suspendedKey, now))
            EndSuspendedWatch();
    }

    /// <summary>Ends the suspended watch, if any, exactly as a stopped watch: <c>ev=watch stop</c> and a reset.</summary>
    private void EndSuspendedWatch()
    {
        if (_suspendedKey.Length == 0) return;
        _log.Info($"ev=watch stop key={_suspendedKey}");
        _suspendedKey = "";
        _suspendedUtc = DateTime.MinValue;
        ResetRunwayWatch();
        _loggedWatchMode = RunwayWatchMode.None;
    }

    private void ResetRunwayWatch()
    {
        _watchKey = "";
        _watchSummaryDone = false;
        _firstStatusRearmed = false;
        _rearmCriticalOnly = false;
        _firstStatusHandedOverUtc = DateTime.MinValue;
        _holdReleasedUtc = null;
        _firstStatusDeferredSinceUtc = DateTime.MinValue;
        _runwayEmptiedPendingKeys.Clear();
        _knownOccupants.Clear();
        _knownFinals.Clear();
        _shortFinalAnnounced.Clear();
        _occupantAbsentSince.Clear();
        _finalAbsentSince.Clear();
        _knownOccupantRunway.Clear();
        _knownFinalRunway.Clear();
    }

    private void ResetQueue()
    {
        _queueCandidate = 0;
        _queueConfirm = 0;
        _queueAnnounced = 0;
        _queueReadingForSummary = null;
        _lastQueueLog = "";
    }

    /// <summary>Proximity gate closed: the queue, the nudge, every mover state and every held "Stop" stop describing anything.</summary>
    private void ResetProximityState()
    {
        ResetQueue();
        if (_nudge.Armed) _log.Info("ev=nudge reset reason=gate");
        _nudge = NudgeState.Disarmed;
        _nudgeLeaderId = null;
        lock (_lock)
            foreach (var ac in _tracked.Values)
            {
                ac.Mover = QueueMoverState.Initial;
                SetStopHeld(ac, false, "gate");
            }
    }

    // Caller holds _lock.
    private List<(double DistFt, double GsKts, double RelBearingDeg)> GroundGeometryForPoll(DateTime now)
    {
        var fresh = now.AddMilliseconds(-FRESH_MS);
        var list = new List<(double, double, double)>();
        foreach (var ac in _tracked.Values)
        {
            if (!ac.OnGround || ac.LastSeenTime < fresh) continue;
            double distFt = NavigationCalculator.CalculateDistance(_ownLat, _ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
            double rel = NormalizeDeg(NavigationCalculator.CalculateBearing(_ownLat, _ownLon, ac.Lat, ac.Lon) - _ownHeadingTrue);
            list.Add((distFt, ac.GS, rel));
        }
        return list;
    }

    private void RequestSweep(DateTime now, uint radius)
    {
        if (radius != _lastRadius)
        {
            _log.Info($"ev=sweep radius={radius}");
            _lastRadius = radius;
        }
        // Each sweep has its own request id (SimConnectManager rotates over eight), and only the answer
        // to this one is credited. Nothing sent (0): nothing is outstanding, and no cycle waits for it.
        _sweepRequestId = _sim.RequestGroundTrafficData(radius);
        _sweepRequestedUtc = _sweepRequestId != 0 ? now : DateTime.MinValue;
        if (_sweepRequestId == 0) _cycle = null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Intake

    private void OnAiTrafficReceived(object? sender, AiTrafficDataEventArgs e)
    {
        var pos = _sim.LastKnownPosition;
        double distM = double.MaxValue;
        double ownAlt = 0;
        if (pos != null)
        {
            distM = NavigationCalculator.CalculateDistance(
                pos.Value.Latitude, pos.Value.Longitude, e.Latitude, e.Longitude) * GroundTrafficLogic.MetresPerNm;
            ownAlt = pos.Value.Altitude;
        }

        // SimConnect sometimes flags an airborne aircraft on-ground; > 500 ft above us is airborne.
        bool onGround = e.OnGround && (pos == null || e.AltitudeFt - ownAlt <= 500.0);
        bool watch, queue;
        lock (_lock) { watch = _runwayWatchActive; queue = _queueScanActive; }
        if (!GroundTrafficLogic.KeepInIntake(onGround, distM, e.GroundSpeedKnots, watch, queue)) return;

        double magVar = pos?.MagneticVariation ?? 0.0;
        var now = _utcNow();
        string? dataQuality = null;
        lock (_lock)
        {
            if (!_tracked.TryGetValue(e.ObjectId, out var ac))
            {
                ac = new TrackedGroundAircraft { ObjectId = e.ObjectId };
                _tracked[e.ObjectId] = ac;
            }
            if (ac.HasFix && now > ac.LastSeenTime)
                GroundTrafficLogic.AddToHistory(ac.History, new PositionFix(ac.Lat, ac.Lon, ac.LastSeenTime, ac.AltitudeFt));
            ac.Lat = e.Latitude;
            ac.Lon = e.Longitude;
            ac.AltitudeFt = e.AltitudeFt;
            ac.HeadingTrue = NormalizeDeg(e.HeadingMagnetic + magVar);
            ac.OnGround = onGround;
            if (onGround) ac.LastOnGroundUtc = now;
            ac.GS = e.GroundSpeedKnots;
            ac.HasFix = true;

            // Name: rebuilt when the callsign changes, and once a missing type becomes known (L3).
            bool callsignChanged = ac.Callsign != e.Callsign || ac.Name.Length == 0;
            if (callsignChanged || !ac.NameHasType)
            {
                string type = string.IsNullOrEmpty(e.AircraftType)
                    ? VatsimPilotDataService.GetAircraftType(e.Callsign)
                    : e.AircraftType;
                if (callsignChanged || GroundTrafficLogic.NameNeedsRefresh(ac.NameHasType, type))
                {
                    ac.Callsign = e.Callsign;
                    ac.Airline = e.Airline;
                    ac.RawType = type;
                    ac.Name = GroundTrafficLogic.SpokenName(e.Airline, e.Callsign, type);
                    ac.NameHasType = GroundTrafficLogic.SpokenType(type).Length > 0;
                }
            }
            dataQuality = CheckDataQuality(ac, now);
            ac.LastSeenTime = now;
        }
        if (dataQuality != null) _log.Info(dataQuality);
    }

    /// <summary>
    /// Once per aircraft: its REPORTED ground speed disagrees with the speed its positions imply by
    /// more than 5 kt on three consecutive samples. This is the sim-session check for traffic injected
    /// by other programs (e.g. SayIntentions) whose speed data may not be the sim's own — every speed
    /// rule (queue, "is moving", converging, "rolling") trusts it. Caller holds _lock.
    /// </summary>
    private static string? CheckDataQuality(TrackedGroundAircraft ac, DateTime now)
    {
        if (ac.DataQualityLogged
            || GroundTrafficLogic.NewestSampleAged(ac.History, now, 0.5, 5.0) is not { } p) return null;
        double dt = (now - p.Utc).TotalSeconds;
        var (dx, dy) = GroundTrafficLogic.ToLocal(p.Lat, p.Lon, ac.Lat, ac.Lon);
        double derivedKts = Math.Sqrt(dx * dx + dy * dy) / dt / 0.514444;
        ac.SpeedMismatchCount = Math.Abs(derivedKts - ac.GS) > 5.0 ? ac.SpeedMismatchCount + 1 : 0;
        if (ac.SpeedMismatchCount < 3) return null;
        ac.DataQualityLogged = true;
        return FormattableString.Invariant(
            $"ev=data-quality id={ac.ObjectId} name=\"{Q(ac.Name)}\" reportedGs={ac.GS:0.0} derivedGs={derivedKts:0.0} onGround={(ac.OnGround ? 1 : 0)}");
    }

    // Caller holds _lock.
    private static PositionFix CurrentFix(TrackedGroundAircraft ac) => new(ac.Lat, ac.Lon, ac.LastSeenTime, ac.AltitudeFt);

    // Caller holds _lock.
    private static double Direction(TrackedGroundAircraft ac)
    {
        var cur = CurrentFix(ac);
        return GroundTrafficLogic.EffectiveDirection(ac.HeadingTrue, ac.GS, GroundTrafficLogic.TrackAnchor(ac.History, cur), cur);
    }

    private void PruneStaleAircraft()
    {
        lock (_lock)
        {
            var cutoff = _utcNow().AddMilliseconds(-PRUNE_AGE_MS);
            var stale = _tracked.Where(kv => kv.Value.LastSeenTime < cutoff)
                                 .Select(kv => kv.Key).ToList();
            foreach (var id in stale) _tracked.Remove(id);
        }
    }

    private void OnGroundTrafficSweepCompleted(object? sender, GroundTrafficSweepEventArgs e)
    {
        // This event is raised ONLY for our own sweeps (PR #247 review L5), each under its own request
        // id. A sweep given up as lost (SWEEP_STALE_MS) can still complete later: its radius, intake and
        // cycle were an older tick's, so it credits no readiness, evaluates nothing and completes no
        // summary (the Alt+G summary has its own timeout). Only the outstanding one, requested at
        // _sweepRequestedUtc, counts.
        if (e.RequestId != _sweepRequestId)
        {
            _log.Info($"ev=sweep stale id={e.RequestId}");
            return;
        }
        _sweepRequestId = 0;
        if (_sweepRequestedUtc != DateTime.MinValue) _lastCompletedSweepRequestedUtc = _sweepRequestedUtc;
        _sweepRequestedUtc = DateTime.MinValue;
        var cycle = _cycle;
        _cycle = null;

        // The pilot-requested summary wins this completion; the alerts are re-evaluated on the next
        // sweep, and only a safety callout may interrupt the summary (TrafficSpeechPolicy).
        if (CompleteSummaryAnnounce()) return;
        if (cycle == null) return;

        bool live = Live();
        bool proximity = cycle.Proximity && live && ProximityGateOpen();
        bool watchGate = cycle.WatchGate && live && WatchGateOpen();
        if (!proximity && !watchGate) return;
        try { EvaluateAlerts(cycle.Ctx, cycle.Watch, proximity, watchGate && cycle.Watch.Key == _watchKey); }
        catch (Exception ex) { _log.Warn($"Evaluate error: {ex}"); }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Evaluation (UI thread)

    private void EvaluateAlerts(GroundTrafficRouteContext? ctx, RunwayWatch watch, bool proximity, bool runwayWatch)
    {
        var candidates = new List<TrafficCallout>();
        var now = _utcNow();
        lock (_lock)
        {
            if (!_positionValid) return;
            bool useMetres = SettingsManager.Current.GroundTrafficUseMetres;
            if (proximity) EvaluateProximity(ctx, useMetres, now, candidates);
            if (runwayWatch && watch.IsActive) EvaluateRunwayWatch(ctx, watch, useMetres, now, candidates);
        }
        Speak(candidates, now);
    }

    /// <summary>
    /// Speaks what <see cref="TrafficSpeechPolicy"/> plans: at most one interrupt (safety callouts
    /// only), at most one alert line, every informational line — and commits each line's one-shot
    /// state ONLY when it is actually spoken.
    /// </summary>
    private void Speak(List<TrafficCallout> candidates, DateTime now)
    {
        var plan = TrafficSpeechPolicy.Plan(candidates, now, _lastAlertLineUtc, _announcer.Suppressed,
            _lastInterruptKind, _lastInterruptUtc);
        if (plan.Interrupt is { } interrupt)
        {
            lock (_lock) interrupt.OnEmitted();
            _announcer.AnnounceImmediate(interrupt.Message);
            _lastInterruptKind = interrupt.Kind;
            _lastInterruptUtc = now;
            _log.Info($"ev=speak kind={interrupt.Kind} interrupt=1 text=\"{Q(interrupt.Message)}\"");
        }
        if (plan.Alert is { } alert)
        {
            lock (_lock) alert.OnEmitted();
            _announcer.Announce(alert.Message);
            _lastAlertLineUtc = now;
            _log.Info($"ev=speak kind={alert.Kind} interrupt=0 text=\"{Q(alert.Message)}\"");
        }
        foreach (var info in plan.Info)
        {
            lock (_lock) info.OnEmitted();
            _announcer.Announce(info.Message);
            _log.Info($"ev=speak kind={info.Kind} interrupt=0 text=\"{Q(info.Message)}\"");
        }
    }

    // Caller holds _lock.
    private void EvaluateProximity(GroundTrafficRouteContext? ctx, bool useMetres, DateTime now, List<TrafficCallout> candidates)
    {
        double ownLat = _ownLat, ownLon = _ownLon, ownHdg = _ownHeadingTrue, ownGS = _ownGS;
        var (ownVx, ownVy) = GroundTrafficLogic.Velocity(ownHdg, ownGS);

        // Own aircraft's place on its route (null when off it or no route).
        var route = ctx?.RouteAhead ?? Array.Empty<GroundTrafficRoutePoint>();
        var ownProj = GroundTrafficLogic.ProjectOntoRoute(route, ownLat, ownLon);
        bool haveRoute = ownProj is { } op && op.LateralMetres <= OWN_ROUTE_MAX_LATERAL_M;
        double ownRouteM = haveRoute ? ownProj!.Value.RouteMetres : 0.0;
        bool queueRoute = ctx is { IsQueueRoute: true };

        var fresh = now.AddMilliseconds(-FRESH_MS);
        var views = new List<TrafficView>();
        // The queue's own inputs, built directly (no placeholder views with a fake "dead ahead").
        var queueCandidates = new List<GroundTrafficLogic.QueueCandidate>();
        foreach (var ac in _tracked.Values)
        {
            if (!ac.OnGround || ac.LastSeenTime < fresh || ac.GS > GroundTrafficLogic.MaxTaxiGsKts) continue;
            double distFt = NavigationCalculator.CalculateDistance(ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
            double direction = Direction(ac);
            RouteProjection? proj = haveRoute ? GroundTrafficLogic.ProjectOntoRoute(route, ac.Lat, ac.Lon) : null;
            double aheadM = proj is { } pj ? pj.RouteMetres - ownRouteM : double.NaN;

            if (queueRoute && proj is { } qp && distFt / FEET_PER_METRE <= GroundTrafficLogic.QueueScanM)
                queueCandidates.Add(new GroundTrafficLogic.QueueCandidate(
                    aheadM, qp.LateralMetres, ac.GS, direction, qp.SegmentBearingDeg));

            if (distFt > GroundTrafficLogic.TrackRangeFt)
            {
                ac.CurrentZone = GroundZone.None;
                ac.PreviousDistance = double.MaxValue;
                ac.PreviousAheadM = double.NaN;
                SetStopHeld(ac, false, "far");
                continue;
            }

            double rel = NormalizeDeg(NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon) - ownHdg);
            var (rx, ry) = GroundTrafficLogic.ToLocal(ownLat, ownLon, ac.Lat, ac.Lon);
            var (tvx, tvy) = GroundTrafficLogic.Velocity(direction, ac.GS);
            var (tcpa, dcpa) = GroundTrafficLogic.ClosestApproach(rx, ry, tvx - ownVx, tvy - ownVy);
            double openingMps = GroundTrafficLogic.OpeningSpeedMps(rx, ry, tvx - ownVx, tvy - ownVy);
            var motion = GroundTrafficLogic.ClassifyMotion(ownHdg, direction, ac.GS, rel);
            bool onRouteAhead = proj is { } p2 && p2.LateralMetres <= ON_ROUTE_LATERAL_M && aheadM >= ROUTE_ALERT_MIN_AHEAD_M;
            bool nearRoute = proj is { } p3 && p3.LateralMetres <= NEAR_ROUTE_M && aheadM >= -20.0;
            RouteRelativeMotion? routeMotion = proj is { } p4
                ? GroundTrafficLogic.ClassifyAlongRoute(direction, ac.GS, p4.SegmentBearingDeg)
                : null;
            views.Add(new TrafficView(ac, distFt, rel, tcpa, dcpa, motion, proj, aheadM, onRouteAhead, nearRoute, routeMotion,
                openingMps));
        }

        // Speed-based zone boundaries.
        double lead = ZoneLeadFt(ownGS);
        double warnDistFt  = WARNING_FT  + lead;
        double cautDistFt  = CAUTION_FT  + lead;
        double awareDistFt = Math.Max(AWARENESS_FT, cautDistFt + 150.0);

        EvaluateQueueMovement(ctx, views, ownLat, ownLon, ownGS, useMetres, now, candidates);

        // The traffic the pilot would reach FIRST on the route ahead. Aircraft queued beyond it cannot be
        // reached without passing it, so they are not called on their own — only "Stop" still speaks for
        // one — and the queue position covers them (PR #247 author's fix: a three-aircraft queue was
        // announced as three "on your route" calls, then three "Slow down"s, each cutting off the last).
        // Only traffic stopped on the route or moving along it can be the first — one merely crossing it
        // hid the stopped aircraft beyond it — and head-on traffic is never queued behind it (PR #247
        // integration review Q3).
        double? firstOnRouteM = GroundTrafficLogic.FirstOnRouteAheadM(views.Select(v => (v.OnRouteAhead, v.AheadM, v.RouteMotion)));

        foreach (var v in views)
        {
            var ac = v.Ac;
            string name = Capitalise(ac.Name);
            string distStr = FormatDistance(v.DistFt, useMetres);
            string dir = GroundTrafficLogic.DescribeDirection(v.Rel);
            bool behindFirst = GroundTrafficLogic.IsQueuedBehindFirst(v.OnRouteAhead, v.AheadM, firstOnRouteM, v.RouteMotion);

            // ── Converging (closest point of approach) ──
            bool movingTraffic = ac.GS >= GroundTrafficLogic.MovingTrafficKts;
            bool conflict = movingTraffic && v.Dcpa < CONFLICT_DCPA_M
                            && v.Tcpa >= CONFLICT_MIN_TCPA_S && v.Tcpa <= CONFLICT_MAX_TCPA_S;
            if (!conflict && (v.Dcpa > CONFLICT_REARM_DCPA_M || v.Tcpa <= 0.0))
                ac.ConflictAlertArmed = true;
            if (conflict && ac.ConflictAlertArmed && !v.OnRouteAhead
                && ac.CurrentZone < GroundZone.Caution
                && GroundTrafficLogic.ConvergingAllowed(v.Rel, ownGS))
            {
                int secs = Math.Max(5, (int)(Math.Round(v.Tcpa / 5.0) * 5));
                candidates.Add(new TrafficCallout(TrafficCalloutKind.Converging, v.DistFt,
                    $"{name} converging {GroundTrafficLogic.DescribeSide(v.Rel)}, about {secs} seconds.",
                    () => { ac.ConflictAlertArmed = false; ac.LastAlertTime = now; }));
            }

            // ── On the route ahead ──
            if (v.Proj is { } proj && (proj.LateralMetres > OFF_ROUTE_REARM_M || v.AheadM < 0))
                ac.RouteAlertArmed = true;
            bool pullingAway = v.RouteMotion == RouteRelativeMotion.Along && ac.GS >= ownGS + 3.0;
            bool comingAtUs = v.RouteMotion == RouteRelativeMotion.Toward;
            if (v.OnRouteAhead && !behindFirst && ac.RouteAlertArmed && !pullingAway
                && v.AheadM <= ROUTE_ALERT_MAX_AHEAD_M
                && (ownGS >= SLOW_DOWN_GS_KTS || comingAtUs)
                && ac.CurrentZone < GroundZone.Caution)
            {
                string taxiway = v.Proj!.Value.Taxiway;
                string on = string.IsNullOrWhiteSpace(taxiway) ? "" : $", taxiway {taxiway}";
                string motion = v.RouteMotion switch
                {
                    RouteRelativeMotion.Stopped => "stopped",
                    RouteRelativeMotion.Along => "same direction",
                    RouteRelativeMotion.Toward => "coming toward you",
                    _ => "crossing",
                };
                candidates.Add(new TrafficCallout(TrafficCalloutKind.OnRoute, v.DistFt,
                    $"{name} on your route{on}, {FormatDistance(v.AheadM * FEET_PER_METRE, useMetres)} ahead, {motion}.",
                    () => { ac.RouteAlertArmed = false; ac.LastAlertTime = now; }));
            }

            // ── Proximity zones ──
            double prevDist = ac.PreviousDistance;
            DateTime prevUtc = ac.PreviousDistanceUtc;
            ac.PreviousDistance = v.DistFt;
            ac.PreviousDistanceUtc = now;
            // Its lead along the route at the previous evaluation, kept only while it was on the route ahead.
            double prevAheadM = ac.PreviousAheadM;
            DateTime prevAheadUtc = ac.PreviousAheadUtc;
            ac.PreviousAheadM = v.OnRouteAhead ? v.AheadM : double.NaN;
            ac.PreviousAheadUtc = now;
            // Opening is judged from the distance between two evaluations (a RATE, R8) and — PR #247
            // author's fix — from motion: the relative velocity, and for traffic on the route ahead its
            // growing lead ALONG the route (through a bend the straight-line gap is the wrong measure). The
            // rate needs a previous evaluation, so a pilot creeping up behind a departing aircraft heard
            // "Stop, … very close" on the first evaluation that saw it pulling away.
            bool leadGrowing = v.OnRouteAhead && GroundTrafficLogic.IsLeadGrowing(prevAheadM, prevAheadUtc, v.AheadM, now);
            bool openingNow = GroundTrafficLogic.IsMovingAway(prevDist, prevUtc, v.DistFt, now)
                              || GroundTrafficLogic.IsOpeningByMotion(ac.GS, v.OpeningMps, leadGrowing);
            // A "Stop" withheld because the traffic was opening stays HELD while it keeps moving and is not
            // closing on the pilot (PR #247 integration review Q2): following a departing leader, catching up
            // to its speed brought the opening under 1 m/s and drew "Stop" with the gap still growing.
            bool movingAway = GroundTrafficLogic.IsMovingAwayOrHeld(openingNow, ac.StopHeldWhileOpening, ac.GS, v.OpeningMps);
            if (!movingAway)
                SetStopHeld(ac, false, ac.GS < GroundTrafficLogic.MovingTrafficKts ? "stopped" : "closing");

            GroundZone newZone;
            if (v.DistFt > awareDistFt)        newZone = GroundZone.None;
            else if (v.DistFt <= warnDistFt)   newZone = GroundZone.Warning;
            else if (v.DistFt <= cautDistFt)   newZone = GroundZone.Caution;
            else                               newZone = GroundZone.Awareness;

            if (newZone == GroundZone.None) { ac.CurrentZone = GroundZone.None; SetStopHeld(ac, false, "far"); continue; }

            // Queued beyond the first aircraft on the route: silent unless it is very close — "Stop" still
            // speaks, never withheld (GroundTrafficLogic.WithholdsZoneBehindFirst). The zone is recorded, as
            // every withheld non-Warning zone is.
            if (GroundTrafficLogic.WithholdsZoneBehindFirst(behindFirst, newZone)) { ac.CurrentZone = newZone; continue; }

            // Caution/Warning only for traffic in the forward arc. The withheld zone is recorded by the same rule
            // as every other withheld escalation — never a WARNING (PR #247 integration review Q5): recorded,
            // "Stop" for an aircraft very close behind the pilot was swallowed for good once the pilot turned
            // to face it, since Warning was no longer an escalation.
            bool inForwardArc = v.Rel <= GroundTrafficLogic.ForwardArcDeg || v.Rel >= 360.0 - GroundTrafficLogic.ForwardArcDeg;
            if (!inForwardArc && newZone >= GroundZone.Caution)
            {
                ac.CurrentZone = GroundTrafficLogic.ZoneToRecordWhenWithheld(newZone, ac.CurrentZone);
                continue;
            }

            // With a route to judge by, "Slow down"/"Stop" need a real threat (GroundTrafficLogic.IsRouteThreat):
            // traffic near the route ahead, genuinely close, or MOVING on a predicted collision course — a
            // parked aircraft's straight-line closest approach assumes the pilot keeps going straight, which
            // the route may not (PR #247 author's fix). Traffic merely beside the route drops to an awareness
            // ping — it can still escalate later if it becomes a threat.
            if (haveRoute && newZone >= GroundZone.Caution
                && !GroundTrafficLogic.IsRouteThreat(v.NearRoute, v.Dcpa, v.Tcpa, ac.GS, v.DistFt <= WARNING_FT))
                newZone = GroundZone.Awareness;

            // Moving away: nothing is said, and the zone is recorded by the same rule as any other withheld
            // escalation — a withheld WARNING is not recorded. Recorded, a "Stop" withheld while the traffic
            // pulled away inside the Warning distance was swallowed for good if it then stopped there, since
            // Warning would no longer be an escalation; with the motion signals above moving-away fires on
            // the first evaluation that sees traffic opening, which made that the common case. A withheld
            // Warning escalation is HELD until the traffic stops or closes (above); a zone below Warning
            // releases it, since no "Stop" is due there.
            if (movingAway)
            {
                SetStopHeld(ac, GroundTrafficLogic.StopHeldAfterMovingAway(newZone, ac.CurrentZone, ac.StopHeldWhileOpening),
                    "zone", v.DistFt);
                ac.CurrentZone = GroundTrafficLogic.ZoneToRecordWhenWithheld(newZone, ac.CurrentZone);
                continue;
            }
            if (!GroundTrafficLogic.ShouldAnnounceEscalation(newZone, ac.CurrentZone, ac.LastSpokenZone,
                    ac.LastAlertTime, now, v.DistFt, ac.LastSpokenZoneDistFt))
            {
                // A withheld WARNING escalation is not recorded: it is judged again next evaluation, so
                // "Stop" speaks once the aircraft has closed by EscalationReclosureFt or the window has
                // passed, instead of being swallowed for good (PR #247 B1 review I3). Everything else
                // withheld — a de-escalation, a Caution re-entry, an Awareness ping — is recorded
                // silently (R8; PR #247 B2 review).
                ac.CurrentZone = GroundTrafficLogic.ZoneToRecordWhenWithheld(newZone, ac.CurrentZone);
                continue;
            }

            string motionPart = v.Motion == TrafficMotion.Stopped
                ? ", stopped" : $", {GroundTrafficLogic.DescribeMotion(v.Motion)}";
            TrafficCalloutKind kind;
            string announcement;
            if (newZone == GroundZone.Warning)
            {
                kind = TrafficCalloutKind.Warning;
                announcement = $"Stop, {ac.Name} very close, {dir}, {distStr}.";
            }
            else if (newZone == GroundZone.Caution && ownGS >= SLOW_DOWN_GS_KTS)
            {
                kind = TrafficCalloutKind.Caution;
                announcement = $"Slow down, {ac.Name} {dir}, {distStr}.";
            }
            else
            {
                kind = TrafficCalloutKind.Awareness;
                announcement = $"{name}, {dir}, {distStr}{motionPart}.";
            }
            var zone = newZone;
            double zoneDistFt = v.DistFt;
            candidates.Add(new TrafficCallout(kind, v.DistFt, announcement,
                () =>
                {
                    ac.CurrentZone = zone;
                    ac.LastSpokenZone = zone;
                    ac.LastSpokenZoneDistFt = zoneDistFt;
                    ac.LastAlertTime = now;
                }));
        }

        EvaluateQueuePosition(ctx, queueCandidates, haveRoute, ownRouteM, ownGS, candidates);
    }

    /// <summary>
    /// "… ahead is moving." for the NEAREST aircraft directly ahead once its departure has latched,
    /// then the gated "Move up." (<see cref="QueueMovementPolicy"/>): disarmed by the nearest aircraft
    /// directly ahead OTHER than the one whose departure armed it while that one is still moving
    /// (<see cref="QueueMovementPolicy.NearestOtherAheadFt"/> — each aircraft's ground speed rides
    /// along; M3), but spoken naming the plain nearest aircraft directly ahead — the leader included,
    /// since it is still the traffic the pilot will close on (K1) — and not spoken at all while that
    /// one is still within <see cref="QueueMovementPolicy.NudgeMinGapFt"/> (N3). Caller holds _lock.
    /// </summary>
    private void EvaluateQueueMovement(GroundTrafficRouteContext? ctx, List<TrafficView> views,
        double ownLat, double ownLon, double ownGS, bool useMetres, DateTime now, List<TrafficCallout> candidates)
    {
        var byId = views.ToDictionary(v => v.Ac.ObjectId);
        TrafficView? nearestAhead = null;
        var directlyAhead = new List<(uint Id, double DistFt, double GsKts)>();
        foreach (var v in views)
        {
            if (!GroundTrafficLogic.IsDirectlyAhead(v.Rel)) continue;
            directlyAhead.Add((v.Ac.ObjectId, v.DistFt, v.Ac.GS));
            if (nearestAhead == null || v.DistFt < nearestAhead.DistFt) nearestAhead = v;
        }

        foreach (var ac in _tracked.Values)
        {
            var obs = byId.TryGetValue(ac.ObjectId, out var view)
                ? new QueueMoverObservation(GroundTrafficLogic.IsInQueueCone(view.DistFt, view.Rel), view.DistFt, ac.GS)
                : new QueueMoverObservation(false, double.MaxValue, ac.GS);
            ac.Mover = QueueMovementPolicy.Step(ac.Mover, obs, ownGS);
        }

        if (nearestAhead is { } mover
            && mover.Ac.Mover.Phase == QueueMoverPhase.Departed
            && GroundTrafficLogic.IsInQueueCone(mover.DistFt, mover.Rel))
        {
            var ac = mover.Ac;
            bool? along = mover.RouteMotion is { } rm ? rm == RouteRelativeMotion.Along : null;
            double gap = ac.Mover.StoppedGapFt, dist = mover.DistFt;
            candidates.Add(new TrafficCallout(TrafficCalloutKind.QueueMoving, mover.DistFt,
                $"{Capitalise(ac.Name)} ahead is moving.",
                () =>
                {
                    ac.Mover = QueueMovementPolicy.MarkAnnounced(ac.Mover);
                    if (QueueMovementPolicy.ShouldArmNudge(along, dist, gap))
                    {
                        _nudge = NudgeState.ArmedAt(now);
                        // The aircraft just announced as moving — the one ShouldArmNudge judged — is
                        // the reason to move up, not "something else ahead" while it keeps moving (H1, M3).
                        _nudgeLeaderId = ac.ObjectId;
                        _log.Info("ev=nudge armed");
                    }
                }));
            return;   // the nudge never rides the same evaluation as the call that arms it
        }

        bool onRunway = ctx != null && RunwayWatchScopes.RunwaysUnder(ctx.Runways, ownLat, ownLon).Count > 0;
        bool allowsPrompt = ctx is { AllowsQueuePrompt: true } && !onRunway;
        double? nearestOtherFt = QueueMovementPolicy.NearestOtherAheadFt(directlyAhead, _nudgeLeaderId);
        // The disarm rule leaves the leader out only while it is still moving (M3); the spoken text
        // names whichever aircraft is nearest ahead, the leader included (K1) — the traffic to close on —
        // and waits, armed, while that one is still within NudgeMinGapFt (N3).
        var decision = QueueMovementPolicy.EvaluateNudge(_nudge, allowsPrompt, ownGS, nearestOtherFt,
            nearestAhead?.DistFt, now, ft => FormatDistance(ft, useMetres));
        switch (decision.Action)
        {
            case NudgeAction.Disarm:
                _nudge = NudgeState.Disarmed;
                _nudgeLeaderId = null;
                // NudgeDecision carries no reason of its own (it is Action + Text), so this is the
                // policy's disarm, told apart from the gate path's reason=gate.
                _log.Info("ev=nudge reset reason=disarmed");
                break;
            case NudgeAction.Speak:
                candidates.Add(new TrafficCallout(TrafficCalloutKind.MoveUp, nearestOtherFt ?? AWARENESS_FT,
                    decision.Text, () => _nudge = QueueMovementPolicy.AfterSpoken(_nudge, now)));
                break;
        }
    }

    /// <summary>The (departure) queue position (<see cref="GroundTrafficLogic.ReadQueue"/>). Caller holds _lock.</summary>
    private void EvaluateQueuePosition(GroundTrafficRouteContext? ctx, List<GroundTrafficLogic.QueueCandidate> queueCandidates,
        bool haveRoute, double ownRouteM, double ownGS, List<TrafficCallout> candidates)
    {
        if (ctx is not { IsQueueRoute: true }) { ResetQueue(); return; }
        if (!haveRoute || ownGS > QueueMovementPolicy.OwnQueueGsKts)
        {
            // Off the route (including lined up: no route ahead) or moving: the last reading no
            // longer describes anything — clear what Alt+G reads too (L1).
            _queueConfirm = 0;
            _queueReadingForSummary = null;
            return;
        }

        // Both distances from the AIRCRAFT (R10): the context measures the route end from the
        // start of the current segment.
        double? endAheadM = ctx.RouteEndMetres is double end ? end - ownRouteM : null;
        var reading = GroundTrafficLogic.ReadQueue(queueCandidates, endAheadM, ctx.IsRunwayDestination);
        _queueReadingForSummary = reading;
        LogQueue(reading, endAheadM);

        if (reading.Position == _queueCandidate) _queueConfirm++;
        else { _queueCandidate = reading.Position; _queueConfirm = 1; }
        if (_queueConfirm < QUEUE_CONFIRM_EVALS || reading.Position == _queueAnnounced) return;

        string beyond = reading.MoreBeyond ? " More traffic holding further ahead." : "";
        int position = reading.Position;
        string? text = position >= 2 ? $"Number {position} in the {reading.Wording}.{beyond}"
                     : _queueAnnounced >= 2 ? $"First in the {reading.Wording}.{beyond}"
                     : null;
        if (text == null) { _queueAnnounced = position; return; }
        candidates.Add(new TrafficCallout(TrafficCalloutKind.QueuePosition, 0, text, () => _queueAnnounced = position));
    }

    // ── Runway watch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// One watched runway's traffic. <see cref="Designator"/> is the end the watch names (what is
    /// spoken); <see cref="Key"/> is the runway itself (<see cref="WatchedRunway.Key"/>, both ends) —
    /// what the known-traffic records are kept by. <see cref="Pending"/>: aircraft over its pavement
    /// whose climb rate is not known yet (<see cref="RunwayTrafficKind.LandingPending"/>) — neither
    /// occupants nor finals, never spoken; they only hold back the watch's first status.
    /// </summary>
    private sealed record RunwayStatus(string Designator, string Key, List<RunwayOccupant> Occupants, List<RunwayFinal> Finals,
        List<TrackedGroundAircraft> Pending);
    private sealed record RunwayOccupant(TrackedGroundAircraft Ac, double DistFt, double Rel, TrafficMotion Motion);
    private sealed record RunwayFinal(TrackedGroundAircraft Ac, RunwayTrafficFix Fix);

    private static bool IsShortFinal(RunwayFinal f)
        => f.Fix.Kind == RunwayTrafficKind.Landing || f.Fix.DistanceNm <= SHORT_FINAL_NM;

    /// <summary>Caller holds _lock.</summary>
    private void EvaluateRunwayWatch(GroundTrafficRouteContext? ctx, RunwayWatch watch, bool useMetres,
        DateTime now, List<TrafficCallout> candidates)
    {
        // The watch's airborne / far aircraft only arrive from a sweep requested after it started.
        if (_lastCompletedSweepRequestedUtc < _watchStartedUtc) return;

        var status = ScanRunways(RunwaysFor(ctx), watch, now);
        if (status.Count == 0) return;
        bool interrupts = watch.RunwayEventsInterrupt;

        if (!_watchSummaryDone)
        {
            // An aircraft over the pavement whose climb rate is not known yet (its first sample — the
            // intake keeps airborne traffic only while a runway is watched) would be missing from the
            // most important sentence of the watch; wait for its next sample (G1), but never longer
            // than FIRST_STATUS_MAX_DEFER_MS.
            int pending = status.Sum(s => s.Pending.Count);
            if (pending > 0)
            {
                if (_firstStatusDeferredSinceUtc == DateTime.MinValue)
                {
                    _firstStatusDeferredSinceUtc = now;
                    _log.Info($"ev=watch first-status-deferred key={_watchKey} pending={pending}");
                }
                if ((now - _firstStatusDeferredSinceUtc).TotalMilliseconds < FIRST_STATUS_MAX_DEFER_MS) return;
            }

            // The first status is ALWAYS spoken (R3). The key is the runway itself, so hold →
            // backtrack → lineup → takeoff wait never restarts the watch; one that starts fresh on the
            // runway has not been heard, and must be — interrupting when something is on the runway or
            // on short final and the pilot is on it (RunwayEventsInterrupt). Turning off after landing
            // on the landing-exit route (Vacating) it waits its turn while taxi guidance speaks the exit
            // (PR #247 B2 review).
            // Each id carries the KEY of the runway it was seen under, so the first status also seeds
            // _knownOccupantRunway / _knownFinalRunway exactly as the event path does — never just an
            // id list (K3; keyed by runway since PR #247 re-review Critical 1).
            var occ = status.SelectMany(s => s.Occupants.Select(o => (Id: o.Ac.ObjectId, s.Key))).ToList();
            var fin = status.SelectMany(s => s.Finals.Select(f => (Id: f.Ac.ObjectId, s.Key))).ToList();
            var shortFin = status.SelectMany(s => s.Finals.Where(IsShortFinal).Select(f => f.Ac.ObjectId)).ToList();
            bool onRunwayOrShortFinal = occ.Count > 0 || shortFin.Count > 0;
            // A RE-ARMED status (SetWatch, H2) is judged by the watch ADOPTED THIS TICK (_currentWatch),
            // never by the possibly-stale cycle a slow sweep completes with (`watch` / `interrupts`, the
            // mode of the tick that REQUESTED the sweep). That adopted watch decides BOTH things:
            // - whether a queuing evaluation WAITS (PR #247 re-review M5): a sweep requested before the
            //   mode change completes with its OLD, queuing cycle, in which nothing can be critical, and
            //   completing the re-armed status there would spend the once-per-watch re-arm before the new
            //   mode was ever evaluated — so it waits, but only while the adopted watch still interrupts.
            //   Waiting on the stale cycle's own mode alone never ended once the pilot went back to a
            //   queuing mode before any evaluation in the interrupting one completed: every later sweep
            //   failed the same test and the whole runway watch was muted for the rest of the session
            //   (PR #247 re-review follow-up, concern 1 — replay G);
            // - whether the re-armed status is CRITICAL (PR #247 focused re-review N2): the cycle's mode
            //   can be the interrupting one while the pilot has already moved again — re-armed on stopping
            //   after landing, the OnRunway cycle's sweep completing once Vacating had been adopted again —
            //   and that status interrupted the exit instructions (replay X3).
            // The ordinary first status still follows the evaluated cycle's mode.
            if (_rearmCriticalOnly && !interrupts && _currentWatch.RunwayEventsInterrupt) return;
            bool critical = (_rearmCriticalOnly ? _currentWatch.RunwayEventsInterrupt : interrupts) && onRunwayOrShortFinal;
            // The one exception: a status RE-ARMED on entering the runway (SetWatch, H2) is critical-only.
            // With nothing on the runway or on short final — or once the adopted watch no longer interrupts
            // — it completes silently, and marks NOTHING as known (PR #247 B5 follow-up K2): nothing was
            // spoken, so an occupant or a final that showed up since the last (already-spoken) status must
            // still reach the ordinary event path below as a fresh occupant/final once _watchSummaryDone
            // flips true, rather than being absorbed here with no callout at all.
            if (_rearmCriticalOnly && !critical)
            {
                _watchSummaryDone = true;
                _rearmCriticalOnly = false;
                return;
            }
            string key = _watchKey;
            candidates.Add(new TrafficCallout(
                critical ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, 0,
                ComposeRunwayStatus(status, useMetres),
                () =>
                {
                    _watchSummaryDone = true;
                    _rearmCriticalOnly = false;
                    _firstStatusHandedOverUtc = now;
                    foreach (var (id, runwayKey) in occ) { _knownOccupants.Add(id); _knownOccupantRunway[id] = runwayKey; }
                    foreach (var (id, runwayKey) in fin) { _knownFinals.Add(id); _knownFinalRunway[id] = runwayKey; }
                    _shortFinalAnnounced.UnionWith(shortFin);
                    _log.Info($"ev=watch status-spoken key={key}");
                }));
            return;
        }

        var seenOccupants = new HashSet<uint>();
        var seenFinals = new HashSet<uint>();
        foreach (var s in status)
        {
            string runwayKey = s.Key;
            // An aircraft on this runway again: its "no traffic seen" line, if still due, is no longer true.
            if (s.Occupants.Count > 0) _runwayEmptiedPendingKeys.Remove(runwayKey);
            foreach (var o in s.Occupants)
            {
                seenOccupants.Add(o.Ac.ObjectId);
                // Refreshed for every seen occupant, known or not (K3): an already-known id that
                // continues below without a new candidate must still keep its recorded runway current.
                _knownOccupantRunway[o.Ac.ObjectId] = runwayKey;
                if (_knownOccupants.Contains(o.Ac.ObjectId)) continue;
                uint id = o.Ac.ObjectId;
                candidates.Add(new TrafficCallout(
                    interrupts ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, o.DistFt,
                    $"{Capitalise(o.Ac.Name)} on runway {s.Designator}, {DescribeRunwayMovement(o)}, " +
                    $"{GroundTrafficLogic.DescribeDirection(o.Rel)}, {FormatDistance(o.DistFt, useMetres)}.",
                    () => { _knownOccupants.Add(id); _knownOccupantRunway[id] = runwayKey; }));
            }
            foreach (var f in s.Finals)
            {
                seenFinals.Add(f.Ac.ObjectId);
                _knownFinalRunway[f.Ac.ObjectId] = runwayKey;
                uint id = f.Ac.ObjectId;
                bool isShort = IsShortFinal(f);
                if (!_knownFinals.Contains(id))
                {
                    candidates.Add(new TrafficCallout(
                        interrupts && isShort ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, 0,
                        DescribeFinal(f, shortWord: false),
                        () => { _knownFinals.Add(id); _knownFinalRunway[id] = runwayKey; if (isShort) _shortFinalAnnounced.Add(id); }));
                }
                else if (isShort && !_shortFinalAnnounced.Contains(id))
                {
                    candidates.Add(new TrafficCallout(
                        interrupts ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, 0,
                        DescribeFinal(f, shortWord: true),
                        () => _shortFinalAnnounced.Add(id)));
                }
            }
        }

        // A known occupant/final whose recorded runway KEY is no longer among this evaluation's scanned
        // keys is forgotten SILENTLY: the watch widened its scan for a runway the aircraft was merely on
        // (H3) and has since narrowed back down, so that runway simply left the scan — it never emptied.
        // Occupants are judged by _knownOccupantRunway and finals by _knownFinalRunway, never one by
        // the other's record (PR #247 re-review Critical 1); keys, not spoken designators, so the nearer
        // end flipping at mid-runway is no purge (Important 2); an id with no recorded key is left to
        // the grace timer below. A purged occupant never reaches EmptiedRunwayKeys below, so the purge
        // itself can never be read as "the runway emptied" (PR #247 B5 follow-up K3).
        // Run AFTER the scan loop above, never before it: the loop just refreshed every id it SAW this
        // evaluation to whichever runway key it is standing on now, so an occupant parked inside an
        // intersection — recorded last under the intersecting runway's key — is reclassified onto the
        // still-watched runway's key before this test ever runs, and reads as in scope. Purging first (the
        // old order) tested that occupant's STALE, pre-refresh key against the narrowed scope, read it out
        // of scope, removed it — and then the very same scan loop, finding it no longer known, announced
        // it again as new, in the evaluation that had just purged it (PR #247 re-review follow-up, concern
        // 3). An id the loop never saw this evaluation is untouched by it, so its recorded key is exactly
        // as stale as before and the purge still drops it precisely as it did before this reordering — the
        // K3 "runway left the scan" case above is unaffected. Placed here, before ForgetAbsent, the purge
        // still runs — and both known sets still reflect it — exactly where it always has relative to the
        // grace-timed absence and the per-runway "emptied" bookkeeping below: silently, and never feeding
        // EmptiedRunwayKeys.
        var scopeKeys = new HashSet<string>(status.Select(s => s.Key), StringComparer.Ordinal);
        foreach (uint id in GroundTrafficLogic.IdsOutOfScope(_knownOccupants, _knownOccupantRunway, scopeKeys))
        {
            _knownOccupants.Remove(id);
            _occupantAbsentSince.Remove(id);
            _knownOccupantRunway.Remove(id);
        }
        foreach (uint id in GroundTrafficLogic.IdsOutOfScope(_knownFinals, _knownFinalRunway, scopeKeys))
        {
            _knownFinals.Remove(id);
            _shortFinalAnnounced.Remove(id);
            _finalAbsentSince.Remove(id);
            _knownFinalRunway.Remove(id);
        }

        // A known occupant or final is forgotten only once it has been unseen for KnownAbsenceGraceMs:
        // one evaluation's classification drop (a climb-rate sample over the line, an on-ground flag
        // flicker, a lateral boundary) must neither announce it again nor call the runway empty while
        // it is still there (PR #247 B2 review Minor 7). A forgotten final takes its short-final latch
        // with it.
        var forgottenOccupants = GroundTrafficLogic.ForgetAbsent(_knownOccupants, seenOccupants, _occupantAbsentSince, now);
        // A runway is empty once the LAST known occupant recorded under ITS key has been gone for the
        // grace period — judged per runway, before the forgotten ids' entries are removed, and never
        // while an aircraft is seen on it this evaluation (PR #247 re-review M2).
        var keysWithOccupantsSeen = new HashSet<string>(
            status.Where(s => s.Occupants.Count > 0).Select(s => s.Key), StringComparer.Ordinal);
        _runwayEmptiedPendingKeys.UnionWith(GroundTrafficLogic.EmptiedRunwayKeys(
            forgottenOccupants, _knownOccupantRunway, _knownOccupants, keysWithOccupantsSeen));
        foreach (uint id in forgottenOccupants) _knownOccupantRunway.Remove(id);
        foreach (uint id in GroundTrafficLogic.ForgetAbsent(_knownFinals, seenFinals, _finalAbsentSince, now))
        {
            _shortFinalAnnounced.Remove(id);
            _knownFinalRunway.Remove(id);
        }
        // One line per emptied runway, named by this evaluation's designator for its key — never every
        // runway the watch scans (K3) — adding "Traffic still on final." only when THAT runway has a
        // final now. A due runway this evaluation does not scan has no designator to be named by; its
        // line stays due until it is scanned again.
        foreach (var s in status)
        {
            if (!_runwayEmptiedPendingKeys.Contains(s.Key)) continue;
            string emptiedKey = s.Key;
            string label = GroundTrafficLogic.RunwayLabel(new[] { s.Designator });
            candidates.Add(new TrafficCallout(TrafficCalloutKind.RunwayInfo, 0,
                s.Finals.Count > 0
                    ? $"{label}: no traffic seen on the runway now. Traffic still on final."
                    : $"{label}: no traffic seen on the runway now.",
                () => _runwayEmptiedPendingKeys.Remove(emptiedKey)));
        }
    }

    /// <summary>
    /// Every fresh aircraft on a watched runway, on final to it, or landing on it. Airborne traffic is
    /// attributed to its best-fitting runway among ALL runways first (R4), so a parallel runway's
    /// arrival is never reported against this one. Caller holds _lock.
    /// </summary>
    private List<RunwayStatus> ScanRunways(IReadOnlyList<TaxiGraph.RunwayCenterline> runways, RunwayWatch watch, DateTime now)
    {
        var result = new List<RunwayStatus>();
        if (runways.Count == 0) return result;
        var shapes = runways.Select(RunwayShape.For).ToList();

        var buckets = new Dictionary<int, RunwayStatus>();
        foreach (var w in watch.Runways)
        {
            for (int i = 0; i < runways.Count; i++)
            {
                if (!RouteRunwayCrossings.CenterlineHasDesignator(runways[i], w.Designator)) continue;
                if (!buckets.ContainsKey(i))
                {
                    var st = new RunwayStatus(w.Designator, w.Key, new List<RunwayOccupant>(), new List<RunwayFinal>(),
                        new List<TrackedGroundAircraft>());
                    buckets[i] = st;
                    result.Add(st);
                }
                break;
            }
        }
        if (buckets.Count == 0) return result;

        var fresh = now.AddMilliseconds(-RUNWAY_FRESH_MS);
        foreach (var ac in _tracked.Values)
        {
            if (ac.LastSeenTime < fresh) continue;
            var assignments = GroundTrafficLogic.ClassifyAgainstRunways(shapes, ac.Lat, ac.Lon, ac.OnGround,
                ac.HeadingTrue, ac.AltitudeFt - _ownAltFt, GroundTrafficLogic.ClimbFpm(ac.History, CurrentFix(ac)),
                recentlyOnGround: (now - ac.LastOnGroundUtc).TotalSeconds <= GroundTrafficLogic.LandingGroundMemorySec);
            foreach (var a in assignments)
            {
                if (!buckets.TryGetValue(a.ShapeIndex, out var st)) continue;
                switch (a.Fix.Kind)
                {
                    case RunwayTrafficKind.OnRunway:
                    {
                        double distFt = NavigationCalculator.CalculateDistance(_ownLat, _ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
                        double rel = NormalizeDeg(NavigationCalculator.CalculateBearing(_ownLat, _ownLon, ac.Lat, ac.Lon) - _ownHeadingTrue);
                        double direction = Direction(ac);
                        st.Occupants.Add(new RunwayOccupant(ac, distFt, rel,
                            GroundTrafficLogic.ClassifyMotion(_ownHeadingTrue, direction, ac.GS, rel)));
                        break;
                    }
                    case RunwayTrafficKind.OnFinal:
                    case RunwayTrafficKind.Landing:
                        st.Finals.Add(new RunwayFinal(ac, a.Fix));
                        break;
                    case RunwayTrafficKind.LandingPending:
                        // Neither an occupant nor a final, and never spoken: it only holds back the
                        // watch's first status until its next sample decides it (G1).
                        st.Pending.Add(ac);
                        break;
                }
                LogRunwayFix(ac, a.Fix, st.Designator);
            }
        }
        foreach (var st in result)
        {
            st.Occupants.Sort((x, y) => x.DistFt.CompareTo(y.DistFt));
            st.Finals.Sort((x, y) => x.Fix.DistanceNm.CompareTo(y.Fix.DistanceNm));
        }
        return result;
    }

    private static string DescribeRunwayMovement(RunwayOccupant o)
    {
        if (o.Ac.GS < 3.0) return "stopped";
        string speed = o.Ac.GS >= ROLLING_KTS ? "rolling" : "taxiing";
        string way = o.Motion switch
        {
            TrafficMotion.HeadOn => "coming toward you",
            TrafficMotion.OppositeDirection => "opposite direction",
            TrafficMotion.SameDirection => "same direction as you",
            TrafficMotion.CrossingLeftToRight => "moving left to right",
            TrafficMotion.CrossingRightToLeft => "moving right to left",
            _ => "",
        };
        return way.Length > 0 ? $"{speed}, {way}" : speed;
    }

    private static string DescribeFinal(RunwayFinal f, bool shortWord)
    {
        string name = Capitalise(f.Ac.Name);
        if (f.Fix.Kind == RunwayTrafficKind.Landing) return $"{name} landing runway {f.Fix.Designator}.";
        string nm = f.Fix.DistanceNm.ToString("0.0", CultureInfo.InvariantCulture);
        return shortWord
            ? $"{name} short final runway {f.Fix.Designator}, {nm} miles."
            : $"{name} on final runway {f.Fix.Designator}, {nm} miles.";
    }

    private static string ComposeRunwayStatus(List<RunwayStatus> status, bool useMetres)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in status)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append($"Runway {s.Designator}: ");
            if (s.Occupants.Count == 0 && s.Finals.Count == 0)
            {
                sb.Append("no traffic seen on the runway or on final.");
                continue;
            }
            if (s.Occupants.Count == 0) sb.Append("no traffic seen on the runway. ");
            foreach (var o in s.Occupants)
                sb.Append($"{Capitalise(o.Ac.Name)} on the runway, {DescribeRunwayMovement(o)}, " +
                          $"{GroundTrafficLogic.DescribeDirection(o.Rel)}, {FormatDistance(o.DistFt, useMetres)}. ");
            if (s.Finals.Count == 0) sb.Append("No traffic seen on final.");
            foreach (var f in s.Finals)
                sb.Append(DescribeFinal(f, shortWord: false)).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Hotkey summary (Alt+G in output mode)

    /// <summary>
    /// Spoken summary of the nearest on-ground aircraft (named with their callsign, direction,
    /// distance and movement), plus the queue position and the watched runway's status when known.
    /// </summary>
    public string GetNearestTrafficSummary()
    {
        bool onGround = _sim.LastKnownOnGround ?? false;
        var pos = _sim.LastKnownPosition;

        if (!onGround || pos == null)
            return "Ground traffic monitor not active in flight.";

        double hdgTrue = NormalizeDeg(pos.Value.HeadingMagnetic + pos.Value.MagneticVariation);
        double ownLat = pos.Value.Latitude;
        double ownLon = pos.Value.Longitude;
        bool useMetres = SettingsManager.Current.GroundTrafficUseMetres;
        // The same local-filtered context the tick uses: a leftover route at another airport never
        // shapes the summary (PR #247 final review H6).
        var ctx = LocalContext(ownLat, ownLon);
        var now = _utcNow();
        var fresh = now.AddMilliseconds(-FRESH_MS);

        var sb = new System.Text.StringBuilder();
        lock (_lock)
        {
            var list = _tracked.Values
                .Where(ac => ac.OnGround && ac.LastSeenTime >= fresh)
                .Select(ac => (d: NavigationCalculator.CalculateDistance(ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET,
                               ac,
                               rel: NormalizeDeg(NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon) - hdgTrue)))
                .Where(t => t.d <= GroundTrafficLogic.TrackRangeFt)
                .OrderBy(t => t.d)
                .Take(SUMMARY_MAX_AIRCRAFT)
                .ToList();

            if (list.Count == 0)
                sb.Append("No ground traffic nearby.");
            else
            {
                sb.Append(list.Count == 1 ? "1 aircraft nearby. " : $"{list.Count} aircraft nearby. ");
                foreach (var (distFt, ac, rel) in list)
                {
                    double direction = Direction(ac);
                    var motion = GroundTrafficLogic.ClassifyMotion(hdgTrue, direction, ac.GS, rel);
                    string name = Capitalise(GroundTrafficLogic.SpokenNameWithCallsign(ac.Airline, ac.Callsign, ac.RawType));
                    sb.Append($"{name}, {GroundTrafficLogic.DescribeDirection(rel)}, " +
                              $"{FormatDistance(distFt, useMetres)}, {GroundTrafficLogic.DescribeMotion(motion)}. ");
                }
            }

            if (_queueReadingForSummary is { } q && _ownGS <= QueueMovementPolicy.OwnQueueGsKts)
            {
                sb.Append(q.Position == 1
                    ? $" First in the {q.Wording}."
                    : $" Number {q.Position} in the {q.Wording}.");
                if (q.MoreBeyond) sb.Append(" More traffic holding further ahead.");
            }

            if (_currentWatch.IsActive && _currentWatch.Key == _watchKey
                && _lastCompletedSweepRequestedUtc >= _watchStartedUtc)
            {
                var status = ScanRunways(RunwaysFor(ctx), _currentWatch, now);
                if (status.Count > 0) sb.Append(' ').Append(ComposeRunwayStatus(status, useMetres));
            }
        }
        return sb.ToString().Trim();
    }

    public void AnnounceNearestTrafficSummary()
    {
        if (!_sim.IsConnected)
        {
            _announcer.AnnounceImmediate("Ground traffic monitor not connected to the simulator.");
            return;
        }

        _sim.RequestAircraftPositionAsync(position =>
        {
            bool onGround = position.SimOnGround >= 0.5;
            _sim.LastKnownOnGround = onGround;

            if (!onGround)
            {
                _announcer.AnnounceImmediate("Ground traffic monitor not active in flight.");
                return;
            }

            double hdgTrue = NormalizeDeg(position.HeadingMagnetic + position.MagneticVariation);
            lock (_lock)
            {
                _ownLat = position.Latitude;
                _ownLon = position.Longitude;
                _ownHeadingTrue = hdgTrue;
                _ownGS = position.GroundSpeedKnots;
                _ownAltFt = position.Altitude;
                _positionValid = true;
            }

            // Defer the announcement until a sweep of ours completes. The poll is suppressed while
            // taxi guidance is idle, so this request is often the ONLY thing populating the
            // dictionary. Only one sweep of ours is ever outstanding: if one already is, its
            // completion speaks the summary. The timeout is a safety net.
            _summaryPending = true;
            _summaryTimeout?.Stop();
            _summaryTimeout?.Start();
            var now = _utcNow();
            bool outstanding = _sweepRequestedUtc != DateTime.MinValue
                               && (now - _sweepRequestedUtc).TotalMilliseconds < SWEEP_STALE_MS;
            if (!outstanding)
            {
                bool watch, queue;
                lock (_lock) { watch = _runwayWatchActive; queue = _queueScanActive; }
                RequestSweep(now, GroundTrafficLogic.SweepRadiusMeters(watch, queue));
            }
        });
    }

    /// <summary>Speaks the pending Alt+G summary; true when it actually spoke.</summary>
    private bool CompleteSummaryAnnounce()
    {
        if (!_summaryPending) return false;
        _summaryPending = false;
        _summaryTimeout?.Stop();
        PruneStaleAircraft();
        _announcer.AnnounceImmediate(GetNearestTrafficSummary());
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers

    /// <summary>
    /// Change-only. <c>reason=</c> says why a gate is closed: <c>disconnected</c> (SimConnect is not
    /// connected), <c>airborne</c> (not on the ground), <c>suppressed</c> (live, but a gate's suppress
    /// check closed it) — or <c>none</c> when both gates are open.
    /// </summary>
    private void LogGates(bool proximity, bool watchGate)
    {
        string reason = !_sim.IsConnected ? "disconnected"
            : !(_sim.LastKnownOnGround ?? false) ? "airborne"
            : proximity && watchGate ? "none"
            : "suppressed";
        string line = $"ev=gate proximity={(proximity ? "on" : "off")} watch={(watchGate ? "on" : "off")} reason={reason}";
        if (line == _lastGateLog) return;
        _lastGateLog = line;
        _log.Info(line);
    }

    /// <summary>
    /// Change-only on the SPOKEN reading (position, at-the-runway-hold, more beyond). <c>end=</c> rides
    /// along in the line but never makes an unchanged reading log again — it moves every second while
    /// the pilot creeps.
    /// </summary>
    private void LogQueue(GroundTrafficLogic.QueueReading r, double? endAheadM)
    {
        string reading = $"pos={r.Position} atHold={(r.AtRunwayHold ? 1 : 0)} more={(r.MoreBeyond ? 1 : 0)}";
        if (reading == _lastQueueLog) return;
        _lastQueueLog = reading;
        string end = endAheadM is double e ? e.ToString("0", CultureInfo.InvariantCulture) : "none";
        _log.Info($"ev=queue {reading} end={end}");
    }

    /// <summary>
    /// Holds or releases <paramref name="ac"/>'s withheld "Stop" (<see cref="TrackedGroundAircraft.StopHeldWhileOpening"/>),
    /// logging only a change: <c>ev=stop-hold … state=on distFt=…</c> when a "Stop" withheld as moving away
    /// starts being held, <c>state=off reason=…</c> when the hold ends — <c>stopped</c> (the traffic is below
    /// 3 kt) or <c>closing</c> (the pilot closes on it), after which the same evaluation judges the "Stop";
    /// <c>zone</c> (below the Warning distance), <c>far</c> (beyond the Awareness distance or the tracking
    /// range) or <c>gate</c> (the proximity gate closed). <paramref name="releaseReason"/> is used only for a
    /// release. Caller holds _lock.
    /// </summary>
    private static void SetStopHeld(TrackedGroundAircraft ac, bool held, string releaseReason, double distFt = double.NaN)
    {
        if (ac.StopHeldWhileOpening == held) return;
        ac.StopHeldWhileOpening = held;
        _log.Info(held
            ? FormattableString.Invariant($"ev=stop-hold id={ac.ObjectId} name=\"{Q(ac.Name)}\" state=on distFt={distFt:0}")
            : FormattableString.Invariant($"ev=stop-hold id={ac.ObjectId} name=\"{Q(ac.Name)}\" state=off reason={releaseReason}"));
    }

    // Caller holds _lock.
    private void LogRunwayFix(TrackedGroundAircraft ac, RunwayTrafficFix fix, string watchedDesignator)
    {
        string rwy = fix.Designator.Length > 0 ? fix.Designator : watchedDesignator;
        string tag = $"{fix.Kind}/{rwy}";
        if (tag == ac.LastRunwayTag) return;
        ac.LastRunwayTag = tag;
        _log.Info(FormattableString.Invariant(
            $"ev=runway id={ac.ObjectId} name=\"{Q(ac.Name)}\" kind={fix.Kind} rwy={rwy} nm={fix.DistanceNm:0.0}"));
    }

    private static string FormatDistance(double feet, bool useMetres)
    {
        if (useMetres)
        {
            double metres = feet * 0.3048;
            double step = metres < 100.0 ? 5.0 : 10.0;
            int rounded = (int)(Math.Round(metres / step) * step);
            return $"{rounded} metres";
        }
        return $"{RoundFeet(feet)} feet";
    }

    private static int RoundFeet(double feet)
    {
        double step = feet > 200.0 ? 50.0 : 25.0;
        return (int)(Math.Round(feet / step) * step);
    }

    private static string Capitalise(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static double NormalizeDeg(double d) => ((d % 360.0) + 360.0) % 360.0;

    /// <summary>A value safe inside a quoted log field.</summary>
    private static string Q(string s) => (s ?? "").Replace('"', '\'');

    private sealed record TrafficView(
        TrackedGroundAircraft Ac, double DistFt, double Rel, double Tcpa, double Dcpa,
        TrafficMotion Motion, RouteProjection? Proj, double AheadM, bool OnRouteAhead, bool NearRoute,
        RouteRelativeMotion? RouteMotion, double OpeningMps);

    public void Dispose()
    {
        _sim.AiTrafficReceived -= OnAiTrafficReceived;
        _sim.GroundTrafficSweepCompleted -= OnGroundTrafficSweepCompleted;
        _timer?.Stop();
        _timer?.Dispose();
        _summaryTimeout?.Stop();
        _summaryTimeout?.Dispose();
        _linger = null;
    }
}

internal enum GroundZone { None = 0, Awareness = 1, Caution = 2, Warning = 3 }

internal sealed class TrackedGroundAircraft
{
    public uint ObjectId;
    public double Lat, Lon;
    public double AltitudeFt;
    public double HeadingTrue;
    public bool OnGround = true;
    public bool HasFix;
    /// <summary>
    /// Recent samples, oldest first (<c>GroundTrafficLogic.AddToHistory</c>) — the motion model's track
    /// baseline, the climb rate and the data-quality check.
    /// </summary>
    public readonly List<PositionFix> History = new();
    /// <summary>When the intake last saw it on the ground (MinValue = never): a departure is not "landing" for a minute after.</summary>
    public DateTime LastOnGroundUtc = DateTime.MinValue;
    public double GS = -1;        // sentinel: -1 means no data received yet
    public string Callsign = "";
    public string Airline = "";
    public string RawType = "";
    public string Name = "";      // spoken name: "Delta A320", "DAL 123, A320", "A320", "traffic"
    public bool NameHasType;
    public GroundZone CurrentZone      = GroundZone.None;
    /// <summary>The zone of the last zone callout actually spoken for this aircraft.</summary>
    public GroundZone LastSpokenZone   = GroundZone.None;
    /// <summary>The distance (feet) of that callout; NaN until one is spoken.</summary>
    public double LastSpokenZoneDistFt = double.NaN;
    /// <summary>When any callout for this aircraft was last spoken.</summary>
    public DateTime LastAlertTime      = DateTime.MinValue;
    public DateTime LastSeenTime       = DateTime.UtcNow;
    public double PreviousDistance     = double.MaxValue;
    public DateTime PreviousDistanceUtc = DateTime.MinValue;
    /// <summary>Its lead along OUR route at the previous evaluation; NaN when it was not on the route ahead then.</summary>
    public double PreviousAheadM       = double.NaN;
    public DateTime PreviousAheadUtc   = DateTime.MinValue;
    /// <summary>
    /// A "Stop" withheld because it was opening is HELD — still unrecorded — while it keeps moving and is
    /// not closing (<c>GroundTrafficLogic.IsMovingAwayOrHeld</c>); set and cleared only through
    /// <c>GroundTrafficMonitor.SetStopHeld</c>, which logs the change.
    /// </summary>
    public bool StopHeldWhileOpening;
    public QueueMoverState Mover       = QueueMoverState.Initial;
    // One-shot per episode: re-armed when the aircraft leaves the route / stops converging.
    public bool RouteAlertArmed        = true;
    public bool ConflictAlertArmed     = true;
    public int SpeedMismatchCount;
    public bool DataQualityLogged;
    public string LastRunwayTag        = "";
}
