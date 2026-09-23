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

    // Converging (closest point of approach)
    private const double CONFLICT_DCPA_M = 45.0;
    private const double CONFLICT_REARM_DCPA_M = 90.0;
    private const double THREAT_DCPA_M = 60.0;
    private const double CONFLICT_MIN_TCPA_S = 5.0;
    private const double CONFLICT_MAX_TCPA_S = 40.0;
    private const double MOVING_KTS = 3.0;

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
    private readonly SimConnectManager _sim;
    private readonly System.Windows.Forms.Timer _timer;
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
    // When the first status was first held back for a pending aircraft; MinValue = not deferred.
    private DateTime _firstStatusDeferredSinceUtc = DateTime.MinValue;
    private bool _runwayEmptiedPending;
    // A single-runway watch whose sources all ended, kept while the aircraft is still crossing (ApplyLinger).
    private RunwayWatchLinger.Anchor? _linger;
    private readonly HashSet<uint> _knownOccupants = new();
    private readonly HashSet<uint> _knownFinals = new();
    private readonly HashSet<uint> _shortFinalAnnounced = new();

    // Queue position (UI thread)
    private int _queueCandidate, _queueConfirm, _queueAnnounced;
    private GroundTrafficLogic.QueueReading? _queueReadingForSummary;

    // "Move up" nudge (UI thread)
    private NudgeState _nudge = NudgeState.Disarmed;

    // Speech bookkeeping (UI thread)
    private DateTime _lastAlertLineUtc = DateTime.MinValue;
    private TrafficCalloutKind? _lastInterruptKind;
    private DateTime _lastInterruptUtc = DateTime.MinValue;

    // Change-only logging (UI thread)
    private string _lastGateLog = "", _lastQueueLog = "";
    private bool _contextDroppedLogged;

    // True while a hotkey summary is waiting for its requested traffic sweep to complete.
    private bool _summaryPending;
    private readonly System.Windows.Forms.Timer _summaryTimeout;

    public GroundTrafficMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        _sim.AiTrafficReceived += OnAiTrafficReceived;
        _sim.GroundTrafficSweepCompleted += OnGroundTrafficSweepCompleted;

        _timer = new System.Windows.Forms.Timer { Interval = POLL_INTERVAL_MS };
        _timer.Tick += OnTick;
        _timer.Start();

        _summaryTimeout = new System.Windows.Forms.Timer { Interval = SUMMARY_SWEEP_TIMEOUT_MS };
        _summaryTimeout.Tick += (_, _) => CompleteSummaryAnnounce();
    }

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

        if (!proximity) ResetProximityState();
        if (!watchGate) { ClearLinger("gate"); SetWatch(RunwayWatch.None); }
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
        // DateTime.UtcNow, not the `now` below: that must stay AFTER SetWatch — the first-status gate
        // compares the completed sweep's REQUEST time against _watchStartedUtc (set inside SetWatch),
        // so the sweep requested later in this tick must carry a time no earlier than the watch start.
        // ApplyLinger's time only feeds the linger's 60 s ceiling.
        var watch = watchGate
            ? ApplyLinger(ResolveWatch(ctx, p.Latitude, p.Longitude), ctx, p.Latitude, p.Longitude, DateTime.UtcNow)
            : ClearLinger("gate");
        SetWatch(watch);
        bool queueScan = proximity && ctx is { IsQueueRoute: true };
        lock (_lock) { _runwayWatchActive = watch.IsActive; _queueScanActive = queueScan; }

        _tickCount++;
        var now = DateTime.UtcNow;
        bool outstanding = _sweepRequestedUtc != DateTime.MinValue
                           && (now - _sweepRequestedUtc).TotalMilliseconds < SWEEP_STALE_MS;
        bool fast;
        lock (_lock)
            fast = GroundTrafficLogic.NeedsFastPoll(watch.IsActive,
                _ownGS <= QueueMovementPolicy.OwnQueueGsKts, GroundGeometryForPoll(now));
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
    /// Adopts <paramref name="runways"/> as the runway cache. Another airport ends any linger: its
    /// anchor was measured against the previous airport's runway.
    /// </summary>
    private void CacheRunways(IReadOnlyList<TaxiGraph.RunwayCenterline> runways, string icao)
    {
        if (!string.Equals(icao, _cachedRunwaysIcao, StringComparison.OrdinalIgnoreCase)) ClearLinger("airport-change");
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
    /// takeoff-assist runway loads the runways again.
    /// </summary>
    public void ClearRunwayCache()
    {
        _cachedRunways = Array.Empty<TaxiGraph.RunwayCenterline>();
        _cachedRunwaysIcao = "";
        ClearLinger("runways-cleared");
    }

    private IReadOnlyList<TaxiGraph.RunwayCenterline> RunwaysFor(GroundTrafficRouteContext? ctx)
        => ctx is { Runways.Count: > 0 } c ? c.Runways : _cachedRunways;

    private RunwayWatch ResolveWatch(GroundTrafficRouteContext? ctx, double lat, double lon)
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
            runways));
    }

    /// <summary>
    /// Adopts this tick's watch. A different key (the runway itself — unchanged from hold through
    /// backtrack, lineup and takeoff wait) restarts the watch; the same key never does.
    /// </summary>
    private void SetWatch(RunwayWatch watch)
    {
        _currentWatch = watch;
        if (watch.Key == _watchKey)
        {
            // The same watch in another mode (hold → backtrack → lineup → takeoff wait, a crossing's
            // linger): logged once per change, so the sim session can see it stayed one watch.
            if (watch.IsActive && watch.Mode != _loggedWatchMode)
            {
                _log.Info($"ev=watch mode key={watch.Key} mode={watch.Mode}");
                _loggedWatchMode = watch.Mode;
            }
            return;
        }
        if (_watchKey.Length > 0) _log.Info($"ev=watch stop key={_watchKey}");
        ResetRunwayWatch();
        _watchKey = watch.Key;
        _watchStartedUtc = DateTime.UtcNow;
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
        double lateral = shape.Project(lat, lon).Lateral;

        if (_linger == null)
        {
            if (!RunwayWatchLinger.CanBegin(lateral)) return RunwayWatch.None;
            _linger = new RunwayWatchLinger.Anchor(lateral, now);
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

    private void ResetRunwayWatch()
    {
        _watchKey = "";
        _watchSummaryDone = false;
        _firstStatusDeferredSinceUtc = DateTime.MinValue;
        _runwayEmptiedPending = false;
        _knownOccupants.Clear();
        _knownFinals.Clear();
        _shortFinalAnnounced.Clear();
    }

    private void ResetQueue()
    {
        _queueCandidate = 0;
        _queueConfirm = 0;
        _queueAnnounced = 0;
        _queueReadingForSummary = null;
        _lastQueueLog = "";
    }

    /// <summary>Proximity gate closed: the queue, the nudge and every mover state stop describing anything.</summary>
    private void ResetProximityState()
    {
        ResetQueue();
        if (_nudge.Armed) _log.Info("ev=nudge reset reason=gate");
        _nudge = NudgeState.Disarmed;
        lock (_lock)
            foreach (var ac in _tracked.Values) ac.Mover = QueueMoverState.Initial;
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
        var now = DateTime.UtcNow;
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
            var cutoff = DateTime.UtcNow.AddMilliseconds(-PRUNE_AGE_MS);
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
        var now = DateTime.UtcNow;
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
                continue;
            }

            double rel = NormalizeDeg(NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon) - ownHdg);
            var (rx, ry) = GroundTrafficLogic.ToLocal(ownLat, ownLon, ac.Lat, ac.Lon);
            var (tvx, tvy) = GroundTrafficLogic.Velocity(direction, ac.GS);
            var (tcpa, dcpa) = GroundTrafficLogic.ClosestApproach(rx, ry, tvx - ownVx, tvy - ownVy);
            var motion = GroundTrafficLogic.ClassifyMotion(ownHdg, direction, ac.GS, rel);
            bool onRouteAhead = proj is { } p2 && p2.LateralMetres <= ON_ROUTE_LATERAL_M && aheadM >= ROUTE_ALERT_MIN_AHEAD_M;
            bool nearRoute = proj is { } p3 && p3.LateralMetres <= NEAR_ROUTE_M && aheadM >= -20.0;
            RouteRelativeMotion? routeMotion = proj is { } p4
                ? GroundTrafficLogic.ClassifyAlongRoute(direction, ac.GS, p4.SegmentBearingDeg)
                : null;
            views.Add(new TrafficView(ac, distFt, rel, tcpa, dcpa, motion, proj, aheadM, onRouteAhead, nearRoute, routeMotion));
        }

        // Speed-based zone boundaries.
        double lead = ownGS * ZONE_LEAD_KTSFPS * ZONE_LEAD_SEC;
        double warnDistFt  = WARNING_FT  + lead;
        double cautDistFt  = CAUTION_FT  + lead;
        double awareDistFt = Math.Max(AWARENESS_FT, cautDistFt + 150.0);

        EvaluateQueueMovement(ctx, views, ownLat, ownLon, ownGS, useMetres, now, candidates);

        foreach (var v in views)
        {
            var ac = v.Ac;
            string name = Capitalise(ac.Name);
            string distStr = FormatDistance(v.DistFt, useMetres);
            string dir = GroundTrafficLogic.DescribeDirection(v.Rel);

            // ── Converging (closest point of approach) ──
            bool movingTraffic = ac.GS >= MOVING_KTS;
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
            if (v.OnRouteAhead && ac.RouteAlertArmed && !pullingAway
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
            bool movingAway = GroundTrafficLogic.IsMovingAway(prevDist, prevUtc, v.DistFt, now);

            GroundZone newZone;
            if (v.DistFt > awareDistFt)        newZone = GroundZone.None;
            else if (v.DistFt <= warnDistFt)   newZone = GroundZone.Warning;
            else if (v.DistFt <= cautDistFt)   newZone = GroundZone.Caution;
            else                               newZone = GroundZone.Awareness;

            if (newZone == GroundZone.None) { ac.CurrentZone = GroundZone.None; continue; }

            // Caution/Warning only for traffic in the forward arc.
            bool inForwardArc = v.Rel <= GroundTrafficLogic.ForwardArcDeg || v.Rel >= 360.0 - GroundTrafficLogic.ForwardArcDeg;
            if (!inForwardArc && newZone >= GroundZone.Caution) { ac.CurrentZone = newZone; continue; }

            // With a route to judge by, "Slow down"/"Stop" need a real threat: traffic near the route
            // ahead, a predicted conflict, or genuinely close. Traffic merely beside the route drops
            // to an awareness ping — it can still escalate later if it becomes a threat.
            if (haveRoute && newZone >= GroundZone.Caution)
            {
                bool threat = v.NearRoute
                              || (v.Dcpa < THREAT_DCPA_M && v.Tcpa <= 30.0 && (movingTraffic || ownGS >= MOVING_KTS))
                              || v.DistFt <= WARNING_FT;
                if (!threat) newZone = GroundZone.Awareness;
            }

            if (movingAway) { ac.CurrentZone = newZone; continue; }
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
    /// then the gated "Move up." (<see cref="QueueMovementPolicy"/>). Caller holds _lock.
    /// </summary>
    private void EvaluateQueueMovement(GroundTrafficRouteContext? ctx, List<TrafficView> views,
        double ownLat, double ownLon, double ownGS, bool useMetres, DateTime now, List<TrafficCallout> candidates)
    {
        var byId = views.ToDictionary(v => v.Ac.ObjectId);
        TrafficView? nearestAhead = null;
        foreach (var v in views)
            if (GroundTrafficLogic.IsDirectlyAhead(v.Rel) && (nearestAhead == null || v.DistFt < nearestAhead.DistFt))
                nearestAhead = v;

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
                        _log.Info("ev=nudge armed");
                    }
                }));
            return;   // the nudge never rides the same evaluation as the call that arms it
        }

        bool onRunway = ctx != null && RunwayWatchScopes.RunwaysUnder(ctx.Runways, ownLat, ownLon).Count > 0;
        bool allowsPrompt = ctx is { AllowsQueuePrompt: true } && !onRunway;
        var decision = QueueMovementPolicy.EvaluateNudge(_nudge, allowsPrompt, ownGS, nearestAhead?.DistFt, now,
            ft => FormatDistance(ft, useMetres));
        switch (decision.Action)
        {
            case NudgeAction.Disarm:
                _nudge = NudgeState.Disarmed;
                // NudgeDecision carries no reason of its own (it is Action + Text), so this is the
                // policy's disarm, told apart from the gate path's reason=gate.
                _log.Info("ev=nudge reset reason=disarmed");
                break;
            case NudgeAction.Speak:
                candidates.Add(new TrafficCallout(TrafficCalloutKind.MoveUp, nearestAhead?.DistFt ?? AWARENESS_FT,
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
    /// One watched runway's traffic. <see cref="Pending"/>: aircraft over its pavement whose climb rate
    /// is not known yet (<see cref="RunwayTrafficKind.LandingPending"/>) — neither occupants nor finals,
    /// never spoken; they only hold back the watch's first status.
    /// </summary>
    private sealed record RunwayStatus(string Designator, List<RunwayOccupant> Occupants, List<RunwayFinal> Finals,
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
            // on short final and the pilot is on it for a reason. A watch the aircraft's position alone
            // started (turning off after landing, while taxi guidance speaks the exit) gives it in turn
            // (RunwayWatch.FirstStatusInterrupts, PR #247 B1 review I1).
            var occ = status.SelectMany(s => s.Occupants.Select(o => o.Ac.ObjectId)).ToList();
            var fin = status.SelectMany(s => s.Finals.Select(f => f.Ac.ObjectId)).ToList();
            var shortFin = status.SelectMany(s => s.Finals.Where(IsShortFinal).Select(f => f.Ac.ObjectId)).ToList();
            bool critical = watch.FirstStatusInterrupts && (occ.Count > 0 || shortFin.Count > 0);
            string key = _watchKey;
            candidates.Add(new TrafficCallout(
                critical ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, 0,
                ComposeRunwayStatus(status, useMetres),
                () =>
                {
                    _watchSummaryDone = true;
                    _knownOccupants.UnionWith(occ);
                    _knownFinals.UnionWith(fin);
                    _shortFinalAnnounced.UnionWith(shortFin);
                    _log.Info($"ev=watch status-spoken key={key}");
                }));
            return;
        }

        var seenOccupants = new HashSet<uint>();
        var seenFinals = new HashSet<uint>();
        foreach (var s in status)
        {
            foreach (var o in s.Occupants)
            {
                seenOccupants.Add(o.Ac.ObjectId);
                if (_knownOccupants.Contains(o.Ac.ObjectId)) continue;
                uint id = o.Ac.ObjectId;
                candidates.Add(new TrafficCallout(
                    interrupts ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, o.DistFt,
                    $"{Capitalise(o.Ac.Name)} on runway {s.Designator}, {DescribeRunwayMovement(o)}, " +
                    $"{GroundTrafficLogic.DescribeDirection(o.Rel)}, {FormatDistance(o.DistFt, useMetres)}.",
                    () => { _knownOccupants.Add(id); _runwayEmptiedPending = false; }));
            }
            foreach (var f in s.Finals)
            {
                seenFinals.Add(f.Ac.ObjectId);
                uint id = f.Ac.ObjectId;
                bool isShort = IsShortFinal(f);
                if (!_knownFinals.Contains(id))
                {
                    candidates.Add(new TrafficCallout(
                        interrupts && isShort ? TrafficCalloutKind.RunwayCritical : TrafficCalloutKind.RunwayInfo, 0,
                        DescribeFinal(f, shortWord: false),
                        () => { _knownFinals.Add(id); if (isShort) _shortFinalAnnounced.Add(id); }));
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

        bool hadOccupants = _knownOccupants.Count > 0;
        _knownOccupants.IntersectWith(seenOccupants);
        _knownFinals.IntersectWith(seenFinals);
        _shortFinalAnnounced.IntersectWith(seenFinals);
        if (seenOccupants.Count > 0) _runwayEmptiedPending = false;
        else if (hadOccupants) _runwayEmptiedPending = true;
        if (_runwayEmptiedPending)
        {
            string label = GroundTrafficLogic.RunwayLabel(status.Select(s => s.Designator));
            candidates.Add(new TrafficCallout(TrafficCalloutKind.RunwayInfo, 0,
                seenFinals.Count > 0
                    ? $"{label}: no traffic seen on the runway now. Traffic still on final."
                    : $"{label}: no traffic seen on the runway now.",
                () => _runwayEmptiedPending = false));
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
                    var st = new RunwayStatus(w.Designator, new List<RunwayOccupant>(), new List<RunwayFinal>(),
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
        var ctx = SafeContext();
        var now = DateTime.UtcNow;
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
            _summaryTimeout.Stop();
            _summaryTimeout.Start();
            var now = DateTime.UtcNow;
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
        _summaryTimeout.Stop();
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
        RouteRelativeMotion? RouteMotion);

    public void Dispose()
    {
        _sim.AiTrafficReceived -= OnAiTrafficReceived;
        _sim.GroundTrafficSweepCompleted -= OnGroundTrafficSweepCompleted;
        _timer.Stop();
        _timer.Dispose();
        _summaryTimeout.Stop();
        _summaryTimeout.Dispose();
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
    public QueueMoverState Mover       = QueueMoverState.Initial;
    // One-shot per episode: re-armed when the aircraft leaves the route / stops converging.
    public bool RouteAlertArmed        = true;
    public bool ConflictAlertArmed     = true;
    public int SpeedMismatchCount;
    public bool DataQualityLogged;
    public string LastRunwayTag        = "";
}
