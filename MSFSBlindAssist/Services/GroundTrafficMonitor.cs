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
/// <item>while holding short of (or lining up on) a runway: aircraft on that runway and on final to it;</item>
/// <item>the pilot's position in the departure queue;</item>
/// <item>"traffic ahead is moving" while stopped in a queue, and a repeating "move up" nudge.</item>
/// </list>
/// Polls every second while traffic is close or a runway is being watched, every three seconds otherwise.
/// Pure geometry and phrasing live in <see cref="GroundTrafficLogic"/>.
/// </summary>
public sealed class GroundTrafficMonitor : IDisposable
{
    // Alert zone thresholds in feet — sized for widebody datum-to-datum measurement.
    // SimConnect gives center-to-center distance; two 787s have ~203 ft of combined
    // half-fuselage, so each threshold is the desired nose-to-tail gap plus ~200 ft.
    // These are the MINIMUM (stopped-aircraft) thresholds; EvaluateAlerts adds a
    // speed-based lead allowance so the pilot is actually stopped at these distances
    // when the brake application completes. See ZONE_LEAD_SEC below.
    private const double AWARENESS_FT = 600.0;  // ~120m gap for two widebodies
    private const double CAUTION_FT   = 400.0;  // ~60m gap  — meaningful slow-down
    private const double WARNING_FT   = 250.0;  // ~80m c-t-c — need to be stopped here

    // Lead time (seconds) added to each zone boundary to account for reaction time +
    // braking + poll lag. At speed v (kt), the effective zone boundary =
    // fixed_ft + v × ktsToFps × ZONE_LEAD_SEC. Kept at 7 s after the poll went to 1 s
    // when traffic is close: the extra margin costs nothing and the slow 3 s cadence
    // still applies until something comes within FAST_POLL_RANGE_FT.
    private const double ZONE_LEAD_SEC = 7.0;
    private const double ZONE_LEAD_KTSFPS = 1.6878; // ft/s per knot

    // Forward arc for action-required auto-alerts ("Slow down" / "Stop") — ±degrees from own nose.
    // Awareness pings fire in all directions; only Caution and Warning are arc-gated, because
    // a behind-arc threat cannot be mitigated by braking. The hotkey summary lists all directions.
    private const double FORWARD_ARC_DEG = 120.0;

    // Minimum own GS before a caution-zone alert becomes "Slow down" rather than plain "Traffic".
    private const double SLOW_DOWN_GS_KTS = 2.0;

    // Ground traffic with GS above this is taking off or landing — not a taxi proximity concern
    // (it IS a runway-watch concern, which reads it separately).
    private const double MAX_TRAFFIC_GS_KTS = 60.0;

    // Proximity tracking range; far aircraft never alert.
    private const double TRACK_RANGE_FT = 2000.0;

    // While a runway is watched, keep aircraft on its pavement and on its final too.
    private const double RUNWAY_WATCH_GROUND_RANGE_M = 5000.0;
    private const double RUNWAY_WATCH_AIR_RANGE_M = GroundTrafficLogic.FinalMaxNm * GroundTrafficLogic.MetresPerNm + 5000.0;

    // Age out aircraft not seen for this many milliseconds
    private const int PRUNE_AGE_MS = 12000;
    // Runway classification only trusts positions this fresh
    private const int RUNWAY_FRESH_MS = 4000;

    // Suppress a repeat AWARENESS ping for the same aircraft inside this window. Caution and
    // Warning escalations are never suppressed by it: they are already one-shot per escalation,
    // and a "Traffic" ping 10 s earlier must not swallow the "Stop" that follows it.
    private const int REANNOUNCE_SUPPRESS_MS = 15000;

    // Moving-away hysteresis — distance must grow by this to call it "moving away"
    private const double MOVING_AWAY_HYSTERESIS_FT = 20.0;
    // Relative-velocity test for "moving away" (see the zone evaluation): ~2 kt of opening.
    private const double MOVING_AWAY_MIN_OPENING_MPS = 1.0;
    // How much further along the route an aircraft must be than the first one to count as
    // queued behind it (two aircraft side by side on one taxiway are both "first").
    private const double SHADOW_MIN_GAP_M = 10.0;

    // Queue-moving detection: tight cone ahead. The GS thresholds and the two departure
    // triggers are pure logic and live in GroundTrafficLogic (QueueStoppedGs / QueueDeparted),
    // where GroundTrafficLogicTests pins them.
    private const double QUEUE_AHEAD_DEG  = 30.0;
    private const double OWN_QUEUE_GS     = 5.0;
    // "Move up" nudge: repeat interval, the gap below which there is nothing to close, and a
    // cap so it prompts rather than nags.
    private const int    QUEUE_NUDGE_INTERVAL_MS = 20000;
    private const double QUEUE_NUDGE_MIN_GAP_FT  = 250.0;
    private const int    QUEUE_NUDGE_MAX         = 3;

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

    // Departure queue
    private const double QUEUE_SCAN_M = 1500.0;
    private const double QUEUE_TRAFFIC_MAX_GS = 6.0;
    private const double QUEUE_MIN_AHEAD_M = 10.0;
    // How close the HEAD of the queue must be to the route's end — the runway holding point
    // — for this to be the "departure queue" rather than just a queue. The head sits ON the
    // hold line, so the slack only has to cover projection error and the node's own offset.
    private const double QUEUE_AT_RUNWAY_M = 150.0;
    private const int QUEUE_CONFIRM_EVALS = 2;

    // Runway watch
    private const double SHORT_FINAL_NM = 2.0;
    private const double ROLLING_KTS = 30.0;

    // Hotkey summary
    private const int SUMMARY_MAX_AIRCRAFT = 3;
    private const int SUMMARY_SWEEP_TIMEOUT_MS = 1500;

    // Polling: 1 s tick; the traffic sweep runs every tick when fast, every third otherwise.
    private const int POLL_INTERVAL_MS = 1000;
    private const int SLOW_POLL_EVERY_TICKS = 3;
    private const double FAST_POLL_RANGE_FT = 1500.0;

    private const double NM_TO_FEET = 6076.12;
    private const double FEET_PER_METRE = 3.28084;

    private static readonly LogChannel _log = Log.Channel("ground_traffic");

    /// <summary>When set, alerts are suppressed while this predicate returns true.</summary>
    public Func<bool>? SuppressCheck { get; set; }

    /// <summary>The taxi route in progress (route ahead, runways, held runway), or null.</summary>
    public Func<GroundTrafficRouteContext?>? RouteContextProvider { get; set; }

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IGroundTrafficSimSource _sim;
    // Null in the headless constructor (unit tests), which drives ticks itself.
    private readonly System.Windows.Forms.Timer? _timer;
    private readonly object _lock = new();
    private readonly Dictionary<uint, TrackedGroundAircraft> _tracked = new();

    // Own-position snapshot, updated each tick from LastKnownPosition
    private double _ownLat, _ownLon, _ownHeadingTrue, _ownGS, _ownAltFt;
    private bool _positionValid;

    // Poll bookkeeping (UI thread only)
    private int _tickCount;
    private long _sweepSeq;          // incremented per sweep WE request
    private long _completedSeq;      // highest of our requests known complete

    /// <summary>
    /// The sequence numbers of sweeps WE requested and have not yet seen complete, oldest
    /// first. SimConnect answers requests in order, so each completion belongs to the head.
    ///
    /// This must be a QUEUE, not one slot. A single in-flight value plus a bool credited
    /// the LATEST request to whichever sweep finished first: at the 1 s fast poll a sweep
    /// that takes longer than a tick has its sequence overwritten, and the older sweep's
    /// completion marks the newer one done. That is exactly reachable at the moment the
    /// runway watch arms — the sweep still in flight was requested while
    /// <see cref="_runwayWatchActive"/> was false, so the intake dropped every airborne
    /// aircraft and everything beyond <see cref="TRACK_RANGE_FT"/>. Crediting it satisfies
    /// the <see cref="_watchReadyFromSeq"/> gate, and the watch baselines
    /// <see cref="_knownOccupants"/>/<see cref="_knownFinals"/> from a snapshot that
    /// structurally could not contain either — so the pilot at the hold line is told
    /// "no traffic seen on the runway or on final" on the strength of a sweep that was
    /// never allowed to see any. The whole point of _watchReadyFromSeq is that the first
    /// status waits for a sweep requested AFTER the watch started.
    /// </summary>
    private readonly Queue<long> _sweepsInFlight = new();
    private const int MAX_SWEEPS_IN_FLIGHT = 8;

    // Runway watch (UI thread only; _runwayWatchActive is read by the intake under _lock)
    private bool _runwayWatchActive;
    private string _watchKey = "";
    private long _watchReadyFromSeq;
    private bool _watchSummaryDone;
    private readonly HashSet<uint> _knownOccupants = new();
    private readonly HashSet<uint> _knownFinals = new();
    private readonly HashSet<uint> _shortFinalAnnounced = new();

    // Departure queue
    private int _queueCandidate;
    private int _queueConfirm;
    private int _queueAnnounced;

    // "Move up" nudge (UI thread only, like the runway-watch fields): the aircraft whose
    // departure armed it, when it last spoke, and how many times.
    private uint _nudgeTargetId;
    private DateTime _nudgeLastSpoken = DateTime.MinValue;
    private int _nudgeCount;

    // True while a hotkey summary is waiting for its requested traffic sweep to complete.
    private bool _summaryPending;
    private readonly System.Windows.Forms.Timer? _summaryTimeout;

    public GroundTrafficMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim)
        : this(announcer, new SimConnectGroundTrafficSource(sim), startTimers: true) { }

    /// <summary>
    /// Headless constructor (GroundTrafficMonitorHeadlessTests): a simulated traffic source and NO WinForms
    /// timers — the caller calls <see cref="TickForHarness"/> once per simulated second.
    /// </summary>
    internal GroundTrafficMonitor(ScreenReaderAnnouncer announcer, IGroundTrafficSimSource source, bool startTimers)
    {
        _announcer = announcer;
        _sim = source;
        _sim.AiTrafficReceived += OnAiTrafficReceived;
        _sim.AiTrafficSweepCompleted += OnAiTrafficSweepCompleted;
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
    // Timer

    private bool IsActive()
    {
        bool onGround = _sim.LastKnownOnGround ?? false;
        if (!onGround || !_sim.IsConnected) return false;
        return SuppressCheck?.Invoke() != true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsActive())
        {
            ResetRunwayWatch();
            ResetQueueNudge();
            _runwayWatchActive = false;
            return;
        }

        // Proactively refresh own position so the distance measurements stay
        // accurate even when no other guidance system (visual / taxi) is active.
        _sim.RequestAircraftPosition();

        var pos = _sim.LastKnownPosition;
        if (pos == null) return;

        double hdgTrue = NormalizeDeg(pos.Value.HeadingMagnetic + pos.Value.MagneticVariation);
        bool anyClose;
        lock (_lock)
        {
            _ownLat = pos.Value.Latitude;
            _ownLon = pos.Value.Longitude;
            _ownHeadingTrue = hdgTrue;
            _ownGS = pos.Value.GroundSpeedKnots;
            _ownAltFt = pos.Value.Altitude;
            _positionValid = true;
            anyClose = _tracked.Values.Any(a => a.OnGround && a.CurrentDistance <= FAST_POLL_RANGE_FT);
        }

        // Decide the runway watch BEFORE requesting the sweep, so the intake keeps the
        // airborne/runway aircraft this very sweep returns.
        var ctx = SafeContext();
        string key = ctx == null ? "" : string.Join(",", ctx.WatchedRunways);
        if (key != _watchKey)
        {
            ResetRunwayWatch();
            _watchKey = key;
            _watchReadyFromSeq = _sweepSeq + 1; // the next sweep we request
        }
        lock (_lock) _runwayWatchActive = key.Length > 0;

        _tickCount++;
        bool fast = anyClose || key.Length > 0;
        if (fast || _tickCount % SLOW_POLL_EVERY_TICKS == 0)
        {
            _sweepSeq++;
            // Bounded: if the sim stops answering, the queue must not grow for the whole
            // session. A dropped entry is never credited — _completedSeq simply stops
            // advancing, which is the honest state ("no sweep of ours has completed").
            while (_sweepsInFlight.Count >= MAX_SWEEPS_IN_FLIGHT) _sweepsInFlight.Dequeue();
            _sweepsInFlight.Enqueue(_sweepSeq);
            _sim.RequestAiTrafficData();
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

    private void ResetRunwayWatch()
    {
        _watchKey = "";
        _watchSummaryDone = false;
        _knownOccupants.Clear();
        _knownFinals.Clear();
        _shortFinalAnnounced.Clear();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Traffic data intake

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
        bool watch;
        lock (_lock) watch = _runwayWatchActive;

        bool keep;
        if (onGround)
            keep = (distM * FEET_PER_METRE <= TRACK_RANGE_FT && e.GroundSpeedKnots <= MAX_TRAFFIC_GS_KTS)
                   || (watch && distM <= RUNWAY_WATCH_GROUND_RANGE_M);
        else
            keep = watch && distM <= RUNWAY_WATCH_AIR_RANGE_M;
        if (!keep) return;

        double magVar = pos?.MagneticVariation ?? 0.0;
        lock (_lock)
        {
            if (!_tracked.TryGetValue(e.ObjectId, out var ac))
            {
                ac = new TrackedGroundAircraft { ObjectId = e.ObjectId };
                _tracked[e.ObjectId] = ac;
            }
            ac.Lat = e.Latitude;
            ac.Lon = e.Longitude;
            ac.AltitudeFt = e.AltitudeFt;
            ac.HeadingTrue = NormalizeDeg(e.HeadingMagnetic + magVar);
            ac.OnGround = onGround;
            // On the very first receipt GS is the sentinel -1; copy the actual
            // GS as PreviousGS so we never see a fake 0→X "start moving" transition.
            ac.PreviousGS = ac.GS < 0 ? e.GroundSpeedKnots : ac.GS;
            ac.GS = e.GroundSpeedKnots;
            if (ac.Callsign != e.Callsign || ac.Name.Length == 0)
            {
                string type = string.IsNullOrEmpty(e.AircraftType)
                    ? VatsimPilotDataService.GetAircraftType(e.Callsign)
                    : e.AircraftType;
                ac.Callsign = e.Callsign;
                ac.Name = GroundTrafficLogic.SpokenName(e.Airline, e.Callsign, type);
            }
            ac.LastSeenTime = DateTime.UtcNow;
        }
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

    private void OnAiTrafficSweepCompleted(object? sender, EventArgs e)
    {
        bool spokeSummary = CompleteSummaryAnnounce();
        // Not one of ours (the Alt+G summary requests a sweep without taking a sequence,
        // and so may anything else that asks the sim for traffic) — nothing to credit.
        if (_sweepsInFlight.Count == 0) return;
        _completedSeq = Math.Max(_completedSeq, _sweepsInFlight.Dequeue());
        if (!IsActive()) return;
        // ONE AnnounceImmediate per completion. The summary is multi-sentence and
        // AnnounceImmediate INTERRUPTS, so letting EvaluateAlerts speak on the same call
        // truncates the summary after a word or two — the rule this file already enforces
        // inside EvaluateAlerts ("a second AnnounceImmediate would only erase the first"),
        // which was broken across these two calls. The alerts are re-evaluated on the very
        // next sweep anyway, at most a second later.
        if (spokeSummary) return;
        try { EvaluateAlerts(); }
        catch (Exception ex) { _log.Warn($"Evaluate error: {ex}"); }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Alert evaluation (UI thread)

    private sealed record Candidate(int Priority, double DistFt, string Message, Action OnSpoken);

    // Priorities for the ONE interrupting callout per evaluation (highest wins).
    private const int P_WARNING = 60, P_CAUTION = 50, P_CONVERGING = 40, P_ON_ROUTE = 30,
                      P_AWARENESS = 20, P_QUEUE_MOVING = 10;

    private void EvaluateAlerts()
    {
        var ctx = SafeContext();
        var immediate = new List<Candidate>();
        var queued = new List<string>();

        lock (_lock)
        {
            if (!_positionValid) return;
            double ownLat = _ownLat, ownLon = _ownLon, ownHdg = _ownHeadingTrue, ownGS = _ownGS;
            bool useMetres = SettingsManager.Current.GroundTrafficUseMetres;
            var (ownVx, ownVy) = GroundTrafficLogic.Velocity(ownHdg, ownGS);

            // Own aircraft's place on its route (null when off it or no route).
            var route = ctx?.RouteAhead ?? Array.Empty<GroundTrafficRoutePoint>();
            var ownProj = GroundTrafficLogic.ProjectOntoRoute(route, ownLat, ownLon);
            bool haveRoute = ownProj is { } op && op.LateralMetres <= OWN_ROUTE_MAX_LATERAL_M;
            double ownRouteM = haveRoute ? ownProj!.Value.RouteMetres : 0.0;

            // Ground traffic inside proximity range, with all the per-aircraft geometry.
            var ground = new List<TrafficView>();
            // Queue candidates BEYOND alert range. The departure queue scans QUEUE_SCAN_M
            // (1,500 m) of route, but `ground` is cut at TRACK_RANGE_FT (2,000 ft ≈ 610 m),
            // so without this the count could never look past ~610 m however long the line
            // was — "Number 4 in the departure queue" to a pilot who is eighth, which is a
            // confidently wrong number rather than a missing one. The data is there when it
            // matters: a departure route puts its runway in WatchedRunways, so the intake
            // keeps ground traffic out to RUNWAY_WATCH_GROUND_RANGE_M (5,000 m).
            //
            // These carry the ROUTE PROJECTION and nothing else — neutral Rel/Tcpa/Dcpa,
            // Stopped motion, OnRouteAhead/NearRoute false — and they never enter `ground`,
            // so no alert, zone, closest-approach or summary path can see them. Only
            // EvaluateQueue is given them, and it reads Proj, AheadM and GS alone.
            var queueOnly = new List<TrafficView>();
            foreach (var ac in _tracked.Values)
            {
                if (!ac.OnGround || ac.GS > MAX_TRAFFIC_GS_KTS) continue;
                double distFt = NavigationCalculator.CalculateDistance(ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
                if (distFt > TRACK_RANGE_FT)
                {
                    ac.CurrentZone = GroundZone.None;
                    ac.CurrentDistance = distFt;
                    if (haveRoute && ac.GS <= QUEUE_TRAFFIC_MAX_GS
                        && distFt / FEET_PER_METRE <= QUEUE_SCAN_M
                        && GroundTrafficLogic.ProjectOntoRoute(route, ac.Lat, ac.Lon) is { } qp)
                    {
                        queueOnly.Add(new TrafficView(ac, distFt, 0.0, double.NaN, double.NaN,
                            TrafficMotion.Stopped, qp, qp.RouteMetres - ownRouteM, false, false, 0.0));
                    }
                    continue;
                }

                double brg = NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon);
                double rel = NormalizeDeg(brg - ownHdg);
                var (rx, ry) = GroundTrafficLogic.ToLocal(ownLat, ownLon, ac.Lat, ac.Lon);
                var (tvx, tvy) = GroundTrafficLogic.Velocity(ac.HeadingTrue, ac.GS);
                var (tcpa, dcpa) = GroundTrafficLogic.ClosestApproach(rx, ry, tvx - ownVx, tvy - ownVy);
                double rLen = Math.Sqrt(rx * rx + ry * ry);
                double openingMps = rLen < 1.0 ? 0.0 : (rx * (tvx - ownVx) + ry * (tvy - ownVy)) / rLen;
                var motion = GroundTrafficLogic.ClassifyMotion(ownHdg, ac.HeadingTrue, ac.GS, rel);

                RouteProjection? proj = haveRoute ? GroundTrafficLogic.ProjectOntoRoute(route, ac.Lat, ac.Lon) : null;
                double aheadM = proj is { } p ? p.RouteMetres - ownRouteM : double.NaN;
                bool onRouteAhead = proj is { } p2 && p2.LateralMetres <= ON_ROUTE_LATERAL_M && aheadM >= ROUTE_ALERT_MIN_AHEAD_M;
                bool nearRoute = proj is { } p3 && p3.LateralMetres <= NEAR_ROUTE_M && aheadM >= -20.0;

                ground.Add(new TrafficView(ac, distFt, rel, tcpa, dcpa, motion, proj, aheadM, onRouteAhead, nearRoute, openingMps));
            }

            // Speed-based zone boundaries.
            double lead = ownGS * ZONE_LEAD_KTSFPS * ZONE_LEAD_SEC;
            double warnDistFt  = WARNING_FT  + lead;
            double cautDistFt  = CAUTION_FT  + lead;
            double awareDistFt = Math.Max(AWARENESS_FT, cautDistFt + 150.0);

            // --- Queue moving ahead, and the "move up" nudge that follows it ---
            EvaluateQueueMoving(ctx, ground, ownGS, useMetres, immediate);

            // The traffic you would reach FIRST on the route ahead. Aircraft queued beyond it are
            // behind it — you cannot reach them without passing it — so they are not called
            // separately: a three-aircraft queue was announced as three interrupting "on your
            // route" calls, then three "Slow down"s, each cutting off the last (simulated
            // traffic, EGLL, 2026-09-23). The queue position covers them.
            TrafficView? firstOnRoute = null;
            foreach (var v in ground)
                if (v.OnRouteAhead && (firstOnRoute == null || v.AheadM < firstOnRoute.AheadM)) firstOnRoute = v;

            foreach (var v in ground)
            {
                var ac = v.Ac;
                string name = Capitalise(ac.Name);
                bool shadowed = v.OnRouteAhead && firstOnRoute != null && !ReferenceEquals(v, firstOnRoute)
                                && v.AheadM > firstOnRoute.AheadM + SHADOW_MIN_GAP_M;
                string distStr = FormatDistance(v.DistFt, useMetres);
                string dir = GroundTrafficLogic.DescribeDirection(v.Rel);

                // ── Converging (closest point of approach) ──
                bool movingTraffic = ac.GS >= MOVING_KTS;
                bool conflict = movingTraffic && v.Dcpa < CONFLICT_DCPA_M
                                && v.Tcpa >= CONFLICT_MIN_TCPA_S && v.Tcpa <= CONFLICT_MAX_TCPA_S;
                if (!conflict && (v.Dcpa > CONFLICT_REARM_DCPA_M || v.Tcpa <= 0.0))
                    ac.ConflictAlertArmed = true;
                if (conflict && ac.ConflictAlertArmed && !v.OnRouteAhead
                    && ac.CurrentZone < GroundZone.Caution)
                {
                    int secs = Math.Max(5, (int)(Math.Round(v.Tcpa / 5.0) * 5));
                    immediate.Add(new Candidate(P_CONVERGING, v.DistFt,
                        $"{name} converging {GroundTrafficLogic.DescribeSide(v.Rel)}, about {secs} seconds.",
                        () => { ac.ConflictAlertArmed = false; ac.LastAlertTime = DateTime.UtcNow; }));
                }

                // ── On the route ahead ──
                if (v.Proj is { } proj && (proj.LateralMetres > OFF_ROUTE_REARM_M || v.AheadM < 0))
                    ac.RouteAlertArmed = true;
                bool pullingAway = v.Motion == TrafficMotion.SameDirection && ac.GS >= ownGS + 3.0;
                bool comingAtUs = v.Motion is TrafficMotion.HeadOn or TrafficMotion.OppositeDirection;
                if (v.OnRouteAhead && !shadowed && ac.RouteAlertArmed && !pullingAway
                    && v.AheadM <= ROUTE_ALERT_MAX_AHEAD_M
                    && (ownGS >= SLOW_DOWN_GS_KTS || comingAtUs)
                    && ac.CurrentZone < GroundZone.Caution)
                {
                    string taxiway = v.Proj!.Value.Taxiway;
                    string on = string.IsNullOrWhiteSpace(taxiway) ? "" : $", taxiway {taxiway}";
                    string motion = v.Motion switch
                    {
                        TrafficMotion.Stopped => "stopped",
                        TrafficMotion.SameDirection => "same direction",
                        TrafficMotion.HeadOn or TrafficMotion.OppositeDirection => "coming toward you",
                        _ => "crossing",
                    };
                    immediate.Add(new Candidate(P_ON_ROUTE, v.DistFt,
                        $"{name} on your route{on}, {FormatDistance(v.AheadM * FEET_PER_METRE, useMetres)} ahead, {motion}.",
                        () => { ac.RouteAlertArmed = false; ac.LastAlertTime = DateTime.UtcNow; }));
                }

                // ── Proximity zones ──
                ac.PreviousDistance = ac.CurrentDistance;
                ac.CurrentDistance = v.DistFt;
                // Opening, measured from the relative VELOCITY as well as from the distance
                // between two evaluations: the hysteresis was sized for the old 3 s poll, and at
                // 1 s an aircraft opening at 3 m/s gains 10 ft per poll — never the 20 ft needed —
                // so a pilot creeping up behind a departing aircraft heard "Stop, … very close"
                // about traffic pulling away from them (simulated traffic, 2026-09-23).
                // For traffic on the route ahead, the straight-line gap is the wrong measure through
                // a bend (an aircraft rounding a corner ahead moves sideways to the line of sight
                // while pulling away along the route): its growing lead ALONG the route counts too.
                var nowT = DateTime.UtcNow;
                bool leadGrowing = false;
                if (v.OnRouteAhead && !double.IsNaN(ac.PreviousAheadM))
                {
                    double dt = (nowT - ac.PreviousAheadTime).TotalSeconds;
                    if (dt > 0.2 && dt < 10.0)
                        leadGrowing = (v.AheadM - ac.PreviousAheadM) / dt >= MOVING_AWAY_MIN_OPENING_MPS;
                }
                ac.PreviousAheadM = v.OnRouteAhead ? v.AheadM : double.NaN;
                ac.PreviousAheadTime = nowT;
                bool movingAway = (ac.PreviousDistance < double.MaxValue
                                   && ac.CurrentDistance > ac.PreviousDistance + MOVING_AWAY_HYSTERESIS_FT)
                                  || (ac.GS >= MOVING_KTS && (v.OpeningMps >= MOVING_AWAY_MIN_OPENING_MPS || leadGrowing));

                GroundZone newZone;
                if (v.DistFt > awareDistFt)        newZone = GroundZone.None;
                else if (v.DistFt <= warnDistFt)   newZone = GroundZone.Warning;
                else if (v.DistFt <= cautDistFt)   newZone = GroundZone.Caution;
                else                               newZone = GroundZone.Awareness;

                if (newZone == GroundZone.None) { ac.CurrentZone = GroundZone.None; continue; }

                // Queued beyond the first aircraft on the route: silent unless it is somehow
                // very close (then "Stop" still speaks — never suppress that).
                if (shadowed && newZone < GroundZone.Warning) { ac.CurrentZone = newZone; continue; }

                // Caution/Warning only for traffic in the forward arc.
                bool inForwardArc = v.Rel <= FORWARD_ARC_DEG || v.Rel >= 360.0 - FORWARD_ARC_DEG;
                if (!inForwardArc && newZone >= GroundZone.Caution)
                { ac.CurrentZone = newZone; continue; }

                // With a route to judge by, "Slow down"/"Stop" need a real threat: traffic near the
                // route ahead, a predicted conflict, or genuinely close. Traffic merely beside the
                // route (a parallel taxiway, a parked aircraft off to the side) drops to a plain
                // awareness call — it can still escalate later if it becomes a threat.
                if (haveRoute && newZone >= GroundZone.Caution)
                {
                    // The straight-line closest approach is a threat test for MOVING traffic only.
                    // For a parked aircraft it assumes the pilot keeps going straight, and where the
                    // route bends toward one before turning away it predicted a near pass that the
                    // route never makes: "Slow down, … ahead, 160 metres" (and "Stop" at speed) for
                    // aircraft parked 100 m beside the route — 421 such calls in simulated
                    // traffic runs, 100 airports, 2026-09-23. A parked aircraft is a threat through
                    // the route (NearRoute) or by being genuinely very close, as before.
                    bool threat = v.NearRoute
                                  || (v.Dcpa < THREAT_DCPA_M && v.Tcpa <= 30.0 && movingTraffic)
                                  || v.DistFt <= WARNING_FT;
                    if (!threat) newZone = GroundZone.Awareness;
                }

                if (movingAway) { ac.CurrentZone = newZone; continue; }
                if (newZone <= ac.CurrentZone) { ac.CurrentZone = newZone; continue; }

                if (newZone == GroundZone.Awareness
                    && (DateTime.UtcNow - ac.LastAlertTime).TotalMilliseconds < REANNOUNCE_SUPPRESS_MS)
                { ac.CurrentZone = newZone; continue; }

                string motionPart = v.Motion == TrafficMotion.Stopped
                    ? ", stopped" : $", {GroundTrafficLogic.DescribeMotion(v.Motion)}";
                string announcement = newZone switch
                {
                    GroundZone.Warning  => $"Stop, {ac.Name} very close, {dir}, {distStr}.",
                    GroundZone.Caution when ownGS >= SLOW_DOWN_GS_KTS
                                        => $"Slow down, {ac.Name} {dir}, {distStr}.",
                    _                   => $"{name}, {dir}, {distStr}{motionPart}.",
                };
                int prio = newZone switch
                {
                    GroundZone.Warning => P_WARNING,
                    GroundZone.Caution => P_CAUTION,
                    _ => P_AWARENESS,
                };
                var zone = newZone;
                immediate.Add(new Candidate(prio, v.DistFt, announcement,
                    () => { ac.CurrentZone = zone; ac.LastAlertTime = DateTime.UtcNow; }));
            }

            // ── Departure queue position ──
            EvaluateQueue(ctx, ground, queueOnly, haveRoute, ownGS, ownRouteM, queued);

            // ── Runway watch ──
            EvaluateRunwayWatch(ctx, ownLat, ownLon, ownHdg, useMetres, queued, immediate);
        }

        // ONE interrupting callout per evaluation — AnnounceImmediate cuts off whatever is
        // speaking, so a second one would only erase the first. The most urgent wins; the
        // others were not marked as spoken and are re-evaluated on the next sweep.
        var best = immediate.OrderByDescending(c => c.Priority).ThenBy(c => c.DistFt).FirstOrDefault();
        if (best != null)
        {
            lock (_lock) best.OnSpoken();
            _announcer.AnnounceImmediate(best.Message);
            _log.Info($"Spoke: {best.Message}");
        }
        // Informational messages queue behind it, never interrupting.
        foreach (var msg in queued)
        {
            _announcer.Announce(msg);
            _log.Info($"Queued: {msg}");
        }
    }

    /// <summary>
    /// "<c>{Name}</c> ahead is moving." when the aircraft the pilot pulled up behind departs, then a
    /// repeating "Move up." while the pilot stays put.
    ///
    /// The departure test itself is <see cref="GroundTrafficLogic.QueueDeparted"/> - a speed edge
    /// OR an opening gap, because a real holding-point queue creeps and a speed edge alone misses
    /// it. This method owns only what is not pure: which aircraft is the closest one directly
    /// ahead, the stopped-gap baseline it is measured against, and the nudge.
    ///
    /// The nudge answers "it moved off, I missed it, now what": it repeats every
    /// <see cref="QUEUE_NUDGE_INTERVAL_MS"/> while the pilot is still stopped, capped at
    /// <see cref="QUEUE_NUDGE_MAX"/>. It is SILENT whenever guidance is deliberately holding the
    /// aircraft (<see cref="GroundTrafficRouteContext.IsHolding"/>: short of a runway, at a
    /// Progressive Taxi terminator, or lined up), because there "Move up" is an instruction ATC
    /// has not given and the aircraft ahead crossing or departing is exactly when a blind pilot
    /// must NOT roll. Gating on WatchedRunways instead would have missed the progressive-taxi
    /// holds, which populate no watched runway. The one-shot "is moving" call is NOT gated that
    /// way: that one is information, not an instruction.
    /// </summary>
    private void EvaluateQueueMoving(GroundTrafficRouteContext? ctx, List<TrafficView> ground,
                                     double ownGS, bool useMetres, List<Candidate> immediate)
    {
        // The pilot is taxiing - nothing to prompt.
        if (ownGS > OWN_QUEUE_GS) { ResetQueueNudge(); return; }

        TrafficView? mover = null;
        TrafficView? nudgeTarget = null;
        foreach (var v in ground)
        {
            bool directlyAhead = v.Rel <= QUEUE_AHEAD_DEG || v.Rel >= 360.0 - QUEUE_AHEAD_DEG;
            if (!directlyAhead) continue;
            if (_nudgeTargetId != 0 && v.Ac.ObjectId == _nudgeTargetId) nudgeTarget = v;
            if (v.DistFt > AWARENESS_FT) continue;

            if (v.Ac.GS <= GroundTrafficLogic.QueueStoppedGs)
            {
                // Stopped ahead of us: this is the gap to measure growth from, and its next
                // departure is a fresh one worth announcing.
                v.Ac.QueueMovingAlertSent = false;
                if (double.IsNaN(v.Ac.StoppedGapFt) || v.DistFt < v.Ac.StoppedGapFt)
                    v.Ac.StoppedGapFt = v.DistFt;
                continue;
            }

            if (!v.Ac.QueueMovingAlertSent
                && GroundTrafficLogic.QueueDeparted(v.Ac.PreviousGS, v.Ac.GS, v.DistFt, v.Ac.StoppedGapFt)
                && (mover == null || v.DistFt < mover.DistFt))
                mover = v;
        }

        if (mover != null)
        {
            var ac = mover.Ac;
            uint id = ac.ObjectId;
            immediate.Add(new Candidate(P_QUEUE_MOVING, mover.DistFt,
                $"{Capitalise(ac.Name)} ahead is moving.",
                () =>
                {
                    ac.QueueMovingAlertSent = true;
                    ac.StoppedGapFt = double.NaN;   // re-baselined when it next stops
                    _nudgeTargetId = id;
                    _nudgeLastSpoken = DateTime.UtcNow;
                    _nudgeCount = 0;
                }));
            return;   // the nudge never rides the same evaluation as the call that arms it
        }

        if (_nudgeTargetId == 0 || _nudgeCount >= QUEUE_NUDGE_MAX) return;
        if (ctx is { IsHolding: true }) return;   // held under an instruction: not our call
        if ((DateTime.UtcNow - _nudgeLastSpoken).TotalMilliseconds < QUEUE_NUDGE_INTERVAL_MS) return;

        // The aircraft that pulled away may be gone from the sweep entirely (taxied out of range,
        // aged out, turned out of the cone). That is a BIGGER gap, not a reason to go quiet - we
        // just cannot quote a distance for it.
        double gapFt = nudgeTarget?.DistFt ?? double.NaN;
        bool known = !double.IsNaN(gapFt);
        if (known && gapFt < QUEUE_NUDGE_MIN_GAP_FT) { ResetQueueNudge(); return; }

        immediate.Add(new Candidate(P_QUEUE_MOVING, known ? gapFt : AWARENESS_FT,
            known ? $"Move up. {FormatDistance(gapFt, useMetres)} to the traffic ahead."
                  : "Move up. The traffic ahead has taxied on.",
            () => { _nudgeLastSpoken = DateTime.UtcNow; _nudgeCount++; }));
    }

    private void ResetQueueNudge()
    {
        _nudgeTargetId = 0;
        _nudgeCount = 0;
        _nudgeLastSpoken = DateTime.MinValue;
    }

    private void EvaluateQueue(GroundTrafficRouteContext? ctx, List<TrafficView> ground,
                               List<TrafficView> queueOnly, bool haveRoute,
                               double ownGS, double ownRouteM, List<string> queued)
    {
        if (ctx == null || !ctx.IsDepartureRoute || ctx.IsLiningUp)
        {
            _queueAnnounced = 0; _queueCandidate = 0; _queueConfirm = 0;
            _queueIsAtRunway = false; _queueMoreBeyond = false;
            return;
        }
        // Off the route or moving: the last computed position no longer describes
        // anything. Clear what the Alt+G summary reads as well as the confirm counter —
        // the summary's own gate is only "departure route and stopped", so a pilot who
        // pulled onto a stub to let traffic past and stopped there would keep being told
        // "Number 3 in the departure queue" from a reading that had stopped applying.
        if (!haveRoute || ownGS > OWN_QUEUE_GS)
        {
            _queueConfirm = 0;
            _queuePositionForSummary = 0;
            _queueIsAtRunway = false;
            _queueMoreBeyond = false;
            return;
        }

        // Only the CONTIGUOUS line the pilot is in — see GroundTrafficLogic.QueueAheadCount.
        // The scan window says how far to look; it does not say where one queue ends and the
        // next begins, and at a busy field 1,500 m of route can hold two.
        var inQueueRange = ground.Concat(queueOnly);
        var cluster = GroundTrafficLogic.QueueAheadOf(
            inQueueRange.Where(v => v.Proj is { } p
                              && p.LateralMetres <= ON_ROUTE_LATERAL_M
                              && v.AheadM >= QUEUE_MIN_AHEAD_M && v.AheadM <= QUEUE_SCAN_M
                              && v.Ac.GS <= QUEUE_TRAFFIC_MAX_GS)
                  .Select(v => v.AheadM));
        int ahead = cluster.Count;
        int position = ahead + 1;
        _queuePositionForSummary = position;
        _queueIsAtRunway = QueueHeadIsAtRunwayHold(ctx, inQueueRange, ahead, ownRouteM);
        // Only worth saying where the pilot is NOT already at the runway hold: from there
        // anything "further ahead" is on the runway itself, which is the runway watch's to
        // report, not the queue's.
        _queueMoreBeyond = cluster.MoreBeyond && !_queueIsAtRunway;

        if (position == _queueCandidate) _queueConfirm++;
        else { _queueCandidate = position; _queueConfirm = 1; }
        if (_queueConfirm < QUEUE_CONFIRM_EVALS || position == _queueAnnounced) return;

        // "Departure queue" only where the queue really is the one feeding the runway.
        // A line at an intermediate holding point is a queue the pilot is in, but calling
        // it the departure queue says they are next for the runway when they are not.
        string what = _queueIsAtRunway ? "departure queue" : "queue";
        string beyond = _queueMoreBeyond ? " More traffic holding further ahead." : "";
        if (position >= 2)
            queued.Add($"Number {position} in the {what}.{beyond}");
        else if (_queueAnnounced >= 2)
            queued.Add($"First in the {what}.{beyond}");
        _queueAnnounced = position;
    }

    /// <summary>
    /// Is the HEAD of the queue standing at the route's end — the runway holding point?
    /// With no aircraft ahead the pilot is the head, so their own distance to the end is
    /// what counts. A route whose end lies beyond the context's look-ahead answers no by
    /// construction: a holding point 2.5 km away is not the one this queue is at.
    /// </summary>
    private static bool QueueHeadIsAtRunwayHold(
        GroundTrafficRouteContext ctx, IEnumerable<TrafficView> ground, int ahead, double ownRouteM)
    {
        if (ctx.RouteEndMetres is not { } routeEndM) return false;
        // RouteEndMetres is measured along RouteAhead, which starts at the START of the
        // aircraft's current segment; AheadM is measured from the aircraft. Put both on the
        // aircraft, or the head reads further from the hold by however far the pilot is into
        // that segment — EGLL 09L: a queue 35 m from the hold was called "the queue", not the
        // departure queue, with the pilot stopped partway down a long segment.
        double endM = routeEndM - ownRouteM;
        double headM = 0.0;
        if (ahead > 0)
        {
            foreach (var v in ground)
            {
                if (v.Proj is { } p && p.LateralMetres <= ON_ROUTE_LATERAL_M
                    && v.AheadM >= QUEUE_MIN_AHEAD_M && v.AheadM <= QUEUE_SCAN_M
                    && v.Ac.GS <= QUEUE_TRAFFIC_MAX_GS
                    && v.AheadM > headM
                    && v.AheadM <= endM + QUEUE_AT_RUNWAY_M)
                {
                    headM = v.AheadM;
                }
            }
        }
        return endM - headM <= QUEUE_AT_RUNWAY_M;
    }

    private bool _queueIsAtRunway;
    private bool _queueMoreBeyond;

    private int _queuePositionForSummary;

    private void EvaluateRunwayWatch(GroundTrafficRouteContext? ctx, double ownLat, double ownLon,
                                     double ownHdg, bool useMetres, List<string> queued, List<Candidate> immediate)
    {
        if (ctx == null || ctx.WatchedRunways.Count == 0 || _watchKey.Length == 0) return;
        // The watch's airborne/runway aircraft only arrive from a sweep requested after it started.
        if (_completedSeq < _watchReadyFromSeq) return;

        var status = ScanRunways(ctx, ownLat, ownLon, ownHdg);
        if (status.Count == 0) return;

        if (!_watchSummaryDone)
        {
            _watchSummaryDone = true;
            foreach (var s in status)
            {
                foreach (var o in s.Occupants) _knownOccupants.Add(o.Ac.ObjectId);
                foreach (var f in s.Finals)
                {
                    _knownFinals.Add(f.Ac.ObjectId);
                    if (f.Fix.DistanceNm <= SHORT_FINAL_NM) _shortFinalAnnounced.Add(f.Ac.ObjectId);
                }
            }
            // Lining up after a hold: the pilot heard the status at the hold; changes only from here.
            if (!ctx.IsLiningUp)
                queued.Add(ComposeRunwayStatus(status, useMetres));
            return;
        }

        var seenOccupants = new HashSet<uint>();
        var seenFinals = new HashSet<uint>();
        foreach (var s in status)
        {
            string rwy = s.Designator;
            foreach (var o in s.Occupants)
            {
                seenOccupants.Add(o.Ac.ObjectId);
                if (_knownOccupants.Add(o.Ac.ObjectId))
                {
                    string msg = $"{Capitalise(o.Ac.Name)} on runway {rwy}, {DescribeRunwayMovement(o)}, " +
                                 $"{GroundTrafficLogic.DescribeDirection(o.Rel)}, {FormatDistance(o.DistFt, useMetres)}.";
                    // Something entering the runway the pilot is lining up on must interrupt.
                    if (ctx.IsLiningUp) immediate.Add(new Candidate(P_WARNING + 5, o.DistFt, msg, () => { }));
                    else queued.Add(msg);
                }
            }
            foreach (var f in s.Finals)
            {
                seenFinals.Add(f.Ac.ObjectId);
                string nm = f.Fix.DistanceNm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                if (_knownFinals.Add(f.Ac.ObjectId))
                {
                    queued.Add($"{Capitalise(f.Ac.Name)} on final runway {f.Fix.Designator}, {nm} miles.");
                    if (f.Fix.DistanceNm <= SHORT_FINAL_NM) _shortFinalAnnounced.Add(f.Ac.ObjectId);
                }
                else if (f.Fix.DistanceNm <= SHORT_FINAL_NM && _shortFinalAnnounced.Add(f.Ac.ObjectId))
                {
                    string msg = $"{Capitalise(f.Ac.Name)} short final runway {f.Fix.Designator}, {nm} miles.";
                    if (ctx.IsLiningUp) immediate.Add(new Candidate(P_WARNING + 5, 0, msg, () => { }));
                    else queued.Add(msg);
                }
            }
        }

        bool hadOccupants = _knownOccupants.Count > 0;
        _knownOccupants.IntersectWith(seenOccupants);
        _knownFinals.IntersectWith(seenFinals);
        _shortFinalAnnounced.IntersectWith(seenFinals);
        if (hadOccupants && _knownOccupants.Count == 0)
        {
            string label = GroundTrafficLogic.RunwayLabel(status.Select(s => s.Designator));
            queued.Add(seenFinals.Count > 0
                ? $"{label}: no traffic seen on the runway now. Traffic still on final."
                : $"{label}: no traffic seen on the runway now.");
        }
    }

    private sealed record RunwayStatus(string Designator, List<RunwayOccupant> Occupants, List<RunwayFinal> Finals);
    private sealed record RunwayOccupant(TrackedGroundAircraft Ac, double DistFt, double Rel, TrafficMotion Motion);
    private sealed record RunwayFinal(TrackedGroundAircraft Ac, RunwayTrafficFix Fix);

    // Caller holds _lock.
    private List<RunwayStatus> ScanRunways(GroundTrafficRouteContext ctx, double ownLat, double ownLon, double ownHdg)
    {
        var result = new List<RunwayStatus>();
        var fresh = DateTime.UtcNow.AddMilliseconds(-RUNWAY_FRESH_MS);
        foreach (string des in ctx.WatchedRunways)
        {
            var cl = RouteRunwayCrossings.FindCenterlineForDesignator(ctx.Runways, des);
            if (cl == null) continue;
            var shape = RunwayShape.For(cl);
            var st = new RunwayStatus(des, new(), new());
            foreach (var ac in _tracked.Values)
            {
                if (ac.LastSeenTime < fresh) continue;
                var fix = GroundTrafficLogic.ClassifyAgainstRunway(shape, ac.Lat, ac.Lon, ac.OnGround,
                                                                   ac.HeadingTrue, ac.AltitudeFt - _ownAltFt);
                if (fix.Kind == RunwayTrafficKind.OnRunway)
                {
                    double distFt = NavigationCalculator.CalculateDistance(ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
                    double rel = NormalizeDeg(NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon) - ownHdg);
                    st.Occupants.Add(new RunwayOccupant(ac, distFt, rel,
                        GroundTrafficLogic.ClassifyMotion(ownHdg, ac.HeadingTrue, ac.GS, rel)));
                }
                else if (fix.Kind == RunwayTrafficKind.OnFinal)
                {
                    st.Finals.Add(new RunwayFinal(ac, fix));
                }
            }
            st.Occupants.Sort((a, b) => a.DistFt.CompareTo(b.DistFt));
            st.Finals.Sort((a, b) => a.Fix.DistanceNm.CompareTo(b.Fix.DistanceNm));
            result.Add(st);
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
                sb.Append($"{Capitalise(f.Ac.Name)} on final runway {f.Fix.Designator}, " +
                          $"{f.Fix.DistanceNm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} miles. ");
        }
        return sb.ToString().TrimEnd();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Hotkey summary (Alt+G in output mode)

    /// <summary>
    /// Spoken summary of the nearest on-ground aircraft (named, with direction, distance and
    /// movement), plus the departure-queue position and the watched runway's status when known.
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

        var sb = new System.Text.StringBuilder();
        lock (_lock)
        {
            var list = _tracked.Values
                .Where(ac => ac.OnGround)
                .Select(ac =>
                {
                    double d = NavigationCalculator.CalculateDistance(ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
                    double rel = NormalizeDeg(NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon) - hdgTrue);
                    return (d, ac, rel);
                })
                .Where(t => t.d <= TRACK_RANGE_FT)
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
                    var motion = GroundTrafficLogic.ClassifyMotion(hdgTrue, ac.HeadingTrue, ac.GS, rel);
                    sb.Append($"{Capitalise(ac.Name)}, {GroundTrafficLogic.DescribeDirection(rel)}, " +
                              $"{FormatDistance(distFt, useMetres)}, {GroundTrafficLogic.DescribeMotion(motion)}. ");
                }
            }

            if (ctx is { IsDepartureRoute: true } && _queuePositionForSummary >= 1 && _ownGS <= OWN_QUEUE_GS)
            {
                string summaryWhat = _queueIsAtRunway ? "departure queue" : "queue";
                sb.Append(_queuePositionForSummary == 1
                    ? $" First in the {summaryWhat}."
                    : $" Number {_queuePositionForSummary} in the {summaryWhat}.");
                if (_queueMoreBeyond) sb.Append(" More traffic holding further ahead.");
            }

            if (ctx != null && ctx.WatchedRunways.Count > 0 && _completedSeq >= _watchReadyFromSeq && _watchKey.Length > 0)
            {
                var status = ScanRunways(ctx, ownLat, ownLon, hdgTrue);
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

            // Defer the announcement until the sweep we just requested completes. The poll is
            // suppressed while taxi guidance is idle, so this request is often the ONLY thing
            // populating the dictionary — announcing synchronously read a stale (usually empty)
            // snapshot. The timeout is a safety net that announces from whatever arrived.
            _summaryPending = true;
            _summaryTimeout?.Stop();
            _summaryTimeout?.Start();
            _sim.RequestAiTrafficData();
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

    private sealed record TrafficView(
        TrackedGroundAircraft Ac, double DistFt, double Rel, double Tcpa, double Dcpa,
        TrafficMotion Motion, RouteProjection? Proj, double AheadM, bool OnRouteAhead, bool NearRoute,
        double OpeningMps);

    public void Dispose()
    {
        _sim.AiTrafficReceived -= OnAiTrafficReceived;
        _sim.AiTrafficSweepCompleted -= OnAiTrafficSweepCompleted;
        _timer?.Stop();
        _timer?.Dispose();
        _summaryTimeout?.Stop();
        _summaryTimeout?.Dispose();
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
    public double GS = -1;        // sentinel: -1 means no data received yet
    public double PreviousGS = 0;
    public string Callsign = "";
    public string Name = "";      // spoken name: "Delta A320", "DAL 123, A320", "A320", "traffic"
    public GroundZone CurrentZone      = GroundZone.None;
    public DateTime LastAlertTime      = DateTime.MinValue;
    public DateTime LastSeenTime       = DateTime.UtcNow;
    public double PreviousDistance     = double.MaxValue;
    public double CurrentDistance      = double.MaxValue;
    // Queue-moving alert: armed when aircraft was stopped, fired when it starts moving,
    // reset when it stops again so the next departure triggers a fresh alert.
    public bool QueueMovingAlertSent   = false;
    // Closest we have been to this aircraft while it sat stopped directly ahead - the queue gap
    // the "is moving" call measures growth from. NaN until it is seen stopped ahead of us.
    public double StoppedGapFt         = double.NaN;
    // One-shot per episode: re-armed when the aircraft leaves the route / stops converging.
    public bool RouteAlertArmed        = true;
    public bool ConflictAlertArmed     = true;
    // Its lead along OUR route at the previous evaluation (NaN when not on it) — whether that
    // lead is growing is what "moving away" means for traffic ahead on the route.
    public double PreviousAheadM       = double.NaN;
    public DateTime PreviousAheadTime  = DateTime.MinValue;
}

/// <summary>
/// What <see cref="GroundTrafficMonitor"/> needs from the simulator — the app passes
/// <see cref="SimConnectGroundTrafficSource"/>; the headless tests pass simulated traffic.
/// </summary>
internal interface IGroundTrafficSimSource
{
    bool IsConnected { get; }
    bool? LastKnownOnGround { get; set; }
    SimConnectManager.AircraftPosition? LastKnownPosition { get; }
    void RequestAircraftPosition();
    void RequestAircraftPositionAsync(Action<SimConnectManager.AircraftPosition> callback);
    void RequestAiTrafficData();
    event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived;
    event EventHandler? AiTrafficSweepCompleted;
}

/// <summary>Pass-through to <see cref="SimConnectManager"/> — no behaviour of its own.</summary>
internal sealed class SimConnectGroundTrafficSource : IGroundTrafficSimSource
{
    private readonly SimConnectManager _sim;
    public SimConnectGroundTrafficSource(SimConnectManager sim) => _sim = sim;
    public bool IsConnected => _sim.IsConnected;
    public bool? LastKnownOnGround { get => _sim.LastKnownOnGround; set => _sim.LastKnownOnGround = value; }
    public SimConnectManager.AircraftPosition? LastKnownPosition => _sim.LastKnownPosition;
    public void RequestAircraftPosition() => _sim.RequestAircraftPosition();
    public void RequestAircraftPositionAsync(Action<SimConnectManager.AircraftPosition> callback) => _sim.RequestAircraftPositionAsync(callback);
    public void RequestAiTrafficData() => _sim.RequestAiTrafficData();
    public event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived
    { add => _sim.AiTrafficReceived += value; remove => _sim.AiTrafficReceived -= value; }
    public event EventHandler? AiTrafficSweepCompleted
    { add => _sim.AiTrafficSweepCompleted += value; remove => _sim.AiTrafficSweepCompleted -= value; }
}
