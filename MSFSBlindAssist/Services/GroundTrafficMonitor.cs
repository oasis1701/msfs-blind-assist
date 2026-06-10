using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Monitors on-ground AI and multiplayer aircraft for proximity alerts while taxiing.
/// Fires automatic spoken alerts on zone transitions and provides a hotkey summary
/// of the nearest aircraft.
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
    // braking + the 3-second poll interval. At speed v (kt), the effective zone
    // boundary = fixed_ft + v × ktsToFps × ZONE_LEAD_SEC, so that after hearing the
    // callout and stopping, the pilot arrives at the fixed boundary (not past it).
    // 7 s = ~1 s reaction + ~3 s braking + 3 s worst-case poll lag at normal taxi speeds.
    private const double ZONE_LEAD_SEC = 7.0;
    private const double ZONE_LEAD_KTSFPS = 1.6878; // ft/s per knot

    // Forward arc for action-required auto-alerts ("Slow down" / "Stop") — ±degrees from own nose.
    // Awareness pings fire in all directions; only Caution and Warning are arc-gated, because
    // a behind-arc threat cannot be mitigated by braking. The hotkey summary lists all directions.
    private const double FORWARD_ARC_DEG = 120.0;

    // Minimum own GS before a caution-zone alert becomes "Slow down" rather than plain "Traffic".
    // Low threshold: at 2+ kts you're moving toward something at 250 ft — slow down is warranted.
    private const double SLOW_DOWN_GS_KTS = 2.0;

    // Traffic with GS above this is taking off or landing — not a taxi concern
    private const double MAX_TRAFFIC_GS_KTS = 60.0;

    // Track aircraft within this distance; far aircraft never alert but stay in the dict
    // until pruned so the hotkey summary can show them approaching before they hit 500 ft
    private const double TRACK_RANGE_FT = 2000.0;

    // Age out aircraft not seen for this many milliseconds (4 poll cycles)
    private const int PRUNE_AGE_MS = 12000;

    // Suppress re-announcement for the same aircraft in the same zone
    private const int REANNOUNCE_SUPPRESS_MS = 15000;

    // Moving-away hysteresis — distance must grow by this to call it "moving away"
    private const double MOVING_AWAY_HYSTERESIS_FT = 20.0;

    // Queue-moving detection: tight cone ahead and GS thresholds
    private const double QUEUE_AHEAD_DEG  = 30.0;  // ±30° — must be directly ahead to count as queue
    private const double QUEUE_STOPPED_GS = 1.5;   // aircraft considered stopped below this kt
    private const double QUEUE_MOVING_GS  = 3.0;   // aircraft considered moving above this kt
    private const double OWN_QUEUE_GS     = 5.0;   // we must be near-stationary to be "in a queue"

    // Hotkey summary: announce this many nearest aircraft
    private const int SUMMARY_MAX_AIRCRAFT = 3;

    // Safety net for the deferred hotkey summary: announce from whatever has
    // arrived if the sweep-completed marker never does (unobserved in practice;
    // the user's own aircraft is always in the sweep so the marker should
    // always fire).
    private const int SUMMARY_SWEEP_TIMEOUT_MS = 1500;

    private const int POLL_INTERVAL_MS = 3000;
    private const double NM_TO_FEET = 6076.12;

    // When set, alerts are suppressed while this predicate returns true.
    // Used to silence traffic callouts during takeoff roll.
    public Func<bool>? SuppressCheck { get; set; }

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly object _lock = new();
    private readonly Dictionary<uint, TrackedGroundAircraft> _tracked = new();

    // Own-position snapshot, updated each tick from LastKnownPosition
    private double _ownLat, _ownLon, _ownHeadingTrue, _ownGS;
    private bool _positionValid;

    // True while a hotkey summary is waiting for its requested traffic sweep
    // to complete. UI-thread only (SimConnect callbacks and WinForms timers
    // both run on the message pump), so no lock is needed.
    private bool _summaryPending;
    private readonly System.Windows.Forms.Timer _summaryTimeout;

    public GroundTrafficMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        _sim.AiTrafficReceived += OnAiTrafficReceived;
        _sim.AiTrafficSweepCompleted += OnAiTrafficSweepCompleted;

        _timer = new System.Windows.Forms.Timer { Interval = POLL_INTERVAL_MS };
        _timer.Tick += OnTick;
        _timer.Start();

        _summaryTimeout = new System.Windows.Forms.Timer { Interval = SUMMARY_SWEEP_TIMEOUT_MS };
        _summaryTimeout.Tick += (_, _) => CompleteSummaryAnnounce();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Timer

    private void OnTick(object? sender, EventArgs e)
    {
        bool onGround = _sim.LastKnownOnGround ?? false;
        if (!onGround || !_sim.IsConnected) return;
        if (SuppressCheck?.Invoke() == true) return;

        // Proactively refresh own position so the distance measurements stay
        // accurate even when no other guidance system (visual / taxi) is active.
        _sim.RequestAircraftPosition();

        var pos = _sim.LastKnownPosition;
        if (pos == null) return;

        double hdgTrue = NormalizeDeg(pos.Value.HeadingMagnetic + pos.Value.MagneticVariation);
        lock (_lock)
        {
            _ownLat = pos.Value.Latitude;
            _ownLon = pos.Value.Longitude;
            _ownHeadingTrue = hdgTrue;
            _ownGS = pos.Value.GroundSpeedKnots;
            _positionValid = true;
        }

        _sim.RequestAiTrafficData();
        PruneStaleAircraft();
        EvaluateAlerts();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Traffic data intake

    private void OnAiTrafficReceived(object? sender, AiTrafficDataEventArgs e)
    {
        if (!e.OnGround) return;
        if (e.GroundSpeedKnots > MAX_TRAFFIC_GS_KTS) return;

        // Distance pre-filter to keep the dictionary small on busy servers
        var pos = _sim.LastKnownPosition;
        if (pos != null)
        {
            double d = NavigationCalculator.CalculateDistance(
                pos.Value.Latitude, pos.Value.Longitude, e.Latitude, e.Longitude) * NM_TO_FEET;
            if (d > TRACK_RANGE_FT) return;
        }

        lock (_lock)
        {
            if (!_tracked.TryGetValue(e.ObjectId, out var ac))
            {
                ac = new TrackedGroundAircraft { ObjectId = e.ObjectId };
                _tracked[e.ObjectId] = ac;
            }
            ac.Lat = e.Latitude;
            ac.Lon = e.Longitude;
            // On the very first receipt GS is the sentinel -1; copy the actual
            // GS as PreviousGS so we never see a fake 0→X "start moving" transition.
            ac.PreviousGS = ac.GS < 0 ? e.GroundSpeedKnots : ac.GS;
            ac.GS = e.GroundSpeedKnots;
            ac.Callsign = e.Callsign;
            ac.AircraftType = e.AircraftType;
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

    // ──────────────────────────────────────────────────────────────────────────
    // Alert evaluation (runs on UI thread via WinForms timer)

    private void EvaluateAlerts()
    {
        // Hold _lock for the whole loop. EvaluateAlerts mutates per-aircraft
        // state (PreviousDistance, CurrentDistance, CurrentZone, LastAlertTime,
        // QueueMovingAlertSent) while OnAiTrafficReceived writes Lat/Lon/GS on
        // the same objects under the lock. SimConnect callbacks currently land
        // on the WinForms UI thread so this is single-threaded today, but the
        // dictionary stores references to mutable objects — taking the lock
        // for the loop body closes the torn-read window without needing to
        // pin the UI-thread assumption. N is bounded by TRACK_RANGE_FT so
        // contention is trivial.
        lock (_lock)
        {
            if (!_positionValid || _tracked.Count == 0) return;
            double ownLat = _ownLat, ownLon = _ownLon, ownHdg = _ownHeadingTrue, ownGS = _ownGS;

            // Pre-compute distance and relative bearing for every tracked aircraft.
            // Sort FARTHEST-first: when multiple zone alerts fire via AnnounceImmediate
            // in the same cycle, the last call (closest aircraft) is the one heard.
            var sorted = _tracked.Values
                .Select(ac =>
                {
                    double d = NavigationCalculator.CalculateDistance(ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
                    double b = NavigationCalculator.CalculateBearing(ownLat, ownLon, ac.Lat, ac.Lon);
                    double rel = NormalizeDeg(b - ownHdg);
                    return (ac, d, rel);
                })
                .OrderByDescending(t => t.d)  // farthest first
                .ToList();

            // Speed-based zone boundaries. Add a lead-time allowance to each fixed
            // threshold so that after the callout fires, the pilot stops AT the fixed
            // boundary (not past it) even accounting for reaction + braking + poll lag.
            double lead = ownGS * ZONE_LEAD_KTSFPS * ZONE_LEAD_SEC;
            double warnDistFt  = WARNING_FT  + lead;
            double cautDistFt  = CAUTION_FT  + lead;
            double awareDistFt = Math.Max(AWARENESS_FT, cautDistFt + 150.0);

            // --- Queue-moving pass ---
            // "Traffic ahead is moving" fires only for the single closest qualifying
            // aircraft. Multiple callouts in one cycle would be confusing and the
            // pilot only needs to know about the one immediately ahead.
            TrackedGroundAircraft? closestQueueMover = null;
            double closestQueueDist = double.MaxValue;
            foreach (var (ac, distFt, relBearing) in sorted)
            {
                bool directlyAhead = relBearing <= QUEUE_AHEAD_DEG || relBearing >= 360.0 - QUEUE_AHEAD_DEG;
                if (directlyAhead && distFt <= AWARENESS_FT && ownGS <= OWN_QUEUE_GS)
                {
                    if (ac.PreviousGS <= QUEUE_STOPPED_GS && ac.GS >= QUEUE_MOVING_GS
                        && !ac.QueueMovingAlertSent && distFt < closestQueueDist)
                    {
                        closestQueueDist = distFt;
                        closestQueueMover = ac;
                    }
                    // Reset the flag once the aircraft stops again so the next departure
                    // fires a fresh alert. Intentionally inside the outer queue-guard:
                    // if our own GS rises above OWN_QUEUE_GS while the lead stops, the
                    // flag stays armed; when we stop again the next departure re-triggers.
                    if (ac.GS <= QUEUE_STOPPED_GS)
                        ac.QueueMovingAlertSent = false;
                }
            }
            if (closestQueueMover != null)
            {
                closestQueueMover.QueueMovingAlertSent = true;
                _announcer.AnnounceImmediate("Traffic ahead is moving.");
            }

            // --- Zone alert pass (farthest first → closest announcement heard last) ---
            foreach (var (ac, distFt, relBearing) in sorted)
            {
                // Update closing-rate history
                ac.PreviousDistance = ac.CurrentDistance;
                ac.CurrentDistance = distFt;
                bool movingAway = ac.PreviousDistance < double.MaxValue
                                  && ac.CurrentDistance > ac.PreviousDistance + MOVING_AWAY_HYSTERESIS_FT;

                // Determine new zone using speed-adjusted boundaries
                GroundZone newZone;
                if (distFt > awareDistFt)        newZone = GroundZone.None;
                else if (distFt <= warnDistFt)   newZone = GroundZone.Warning;
                else if (distFt <= cautDistFt)   newZone = GroundZone.Caution;
                else                             newZone = GroundZone.Awareness;

                if (newZone == GroundZone.None)  { ac.CurrentZone = GroundZone.None; continue; }

                // Caution ("Slow down") and Warning ("Stop") only fire for traffic
                // in the forward arc — a behind-arc threat is not something the pilot
                // can act on by braking. Awareness pings still fire for behind-arc
                // traffic so the pilot retains passive awareness; the hotkey summary
                // (Alt+G) lists every direction regardless of arc.
                bool inForwardArc = relBearing <= FORWARD_ARC_DEG || relBearing >= 360.0 - FORWARD_ARC_DEG;
                if (!inForwardArc && (newZone == GroundZone.Caution || newZone == GroundZone.Warning))
                { ac.CurrentZone = newZone; continue; }

                // Don't escalate for moving-away aircraft
                if (movingAway) { ac.CurrentZone = newZone; continue; }

                // Only announce on zone escalation (entering a closer zone)
                if (newZone <= ac.CurrentZone) { ac.CurrentZone = newZone; continue; }

                // Suppress if this aircraft was announced recently
                if ((DateTime.UtcNow - ac.LastAlertTime).TotalMilliseconds < REANNOUNCE_SUPPRESS_MS)
                { ac.CurrentZone = newZone; continue; }

                // Fire the alert
                ac.CurrentZone = newZone;
                ac.LastAlertTime = DateTime.UtcNow;

                string dir = DescribeDirection(relBearing);
                string distStr = FormatDistance(distFt);

                string announcement = newZone switch
                {
                    GroundZone.Warning  => $"Stop, traffic very close, {dir}, {distStr}.",
                    GroundZone.Caution when ownGS >= SLOW_DOWN_GS_KTS
                                        => $"Slow down, traffic {dir}, {distStr}.",
                    _                   => $"Traffic, {dir}, {distStr}."
                };

                _announcer.AnnounceImmediate(announcement);
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Hotkey summary (Alt+G in output mode)

    /// <summary>
    /// Returns a spoken summary of the nearest on-ground aircraft, for manual readout
    /// via hotkey. Works at any time while connected, regardless of whether auto-alerts
    /// are firing. Returns up to <see cref="SUMMARY_MAX_AIRCRAFT"/> nearest aircraft.
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

        List<(double distFt, TrackedGroundAircraft ac, double relBearing)> list;
        lock (_lock)
        {
            if (_tracked.Count == 0)
                return "No ground traffic nearby.";

            list = _tracked.Values
                .Select(ac =>
                {
                    double d = NavigationCalculator.CalculateDistance(
                        ownLat, ownLon, ac.Lat, ac.Lon) * NM_TO_FEET;
                    double b = NavigationCalculator.CalculateBearing(
                        ownLat, ownLon, ac.Lat, ac.Lon);
                    double rel = NormalizeDeg(b - hdgTrue);
                    return (d, ac, rel);
                })
                .OrderBy(t => t.Item1)
                .Take(SUMMARY_MAX_AIRCRAFT)
                .ToList();
        }

        if (list.Count == 0)
            return "No ground traffic nearby.";

        string countWord = list.Count == 1 ? "1 aircraft" : $"{list.Count} aircraft";
        var sb = new System.Text.StringBuilder($"{countWord} nearby. ");
        foreach (var (distFt, ac, relBearing) in list)
        {
            string dir = DescribeDirection(relBearing);
            string distStr = FormatDistance(distFt);
            string label = !string.IsNullOrEmpty(ac.Callsign) ? ac.Callsign : "traffic";
            sb.Append($"{label}, {dir}, {distStr}. ");
        }
        return sb.ToString().TrimEnd();
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
                _positionValid = true;
            }

            // Defer the announcement until the sweep we just requested
            // completes (AiTrafficSweepCompleted = the sweep's final entry).
            // The 3-second poll is fully suppressed while taxi guidance is
            // idle, so this request is often the ONLY thing populating the
            // dictionary — announcing synchronously here read a stale (usually
            // empty) snapshot and said "no traffic" at a busy airport. The
            // timeout is a safety net that announces from whatever arrived.
            _summaryPending = true;
            _summaryTimeout.Stop();
            _summaryTimeout.Start();
            _sim.RequestAiTrafficData();
        });
    }

    private void OnAiTrafficSweepCompleted(object? sender, EventArgs e) => CompleteSummaryAnnounce();

    private void CompleteSummaryAnnounce()
    {
        // Background OnTick sweeps complete too; only speak when a hotkey
        // summary is actually waiting.
        if (!_summaryPending) return;
        _summaryPending = false;
        _summaryTimeout.Stop();
        PruneStaleAircraft();
        _announcer.AnnounceImmediate(GetNearestTrafficSummary());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Geometry helpers

    private static string FormatDistance(double feet)
    {
        if (SettingsManager.Current.GroundTrafficUseMetres)
        {
            double metres = feet * 0.3048;
            double step = metres < 100.0 ? 5.0 : 10.0;
            int rounded = (int)(Math.Round(metres / step) * step);
            return $"{rounded} metres";
        }
        return $"{RoundFeet(feet)} feet";
    }

    private static string DescribeDirection(double relBearing)
    {
        // relBearing: 0 = dead ahead, 90 = hard right, 180 = dead behind
        bool right = relBearing < 180.0;
        double abs = right ? relBearing : (360.0 - relBearing);

        if (abs <= 20.0) return "ahead";
        if (abs <= 70.0) return right ? "ahead and to the right" : "ahead and to the left";
        if (abs <= 110.0) return right ? "to the right" : "to the left";
        if (abs <= 160.0) return right ? "behind and to the right" : "behind and to the left";
        return "behind";
    }

    private static double NormalizeDeg(double d) => ((d % 360.0) + 360.0) % 360.0;

    private static int RoundFeet(double feet)
    {
        double step = feet > 200.0 ? 50.0 : 25.0;
        return (int)(Math.Round(feet / step) * step);
    }

    // ──────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _sim.AiTrafficReceived -= OnAiTrafficReceived;
        _sim.AiTrafficSweepCompleted -= OnAiTrafficSweepCompleted;
        _timer.Stop();
        _timer.Dispose();
        _summaryTimeout.Stop();
        _summaryTimeout.Dispose();
    }
}

internal enum GroundZone { None = 0, Awareness = 1, Caution = 2, Warning = 3 }

internal sealed class TrackedGroundAircraft
{
    public uint ObjectId;
    public double Lat, Lon;
    public double GS = -1;        // sentinel: -1 means no data received yet
    public double PreviousGS = 0;
    public string Callsign    = "";
    public string AircraftType = "";
    public GroundZone CurrentZone      = GroundZone.None;
    public DateTime LastAlertTime      = DateTime.MinValue;
    public DateTime LastSeenTime       = DateTime.UtcNow;
    public double PreviousDistance     = double.MaxValue;
    public double CurrentDistance      = double.MaxValue;
    // Queue-moving alert: armed when aircraft was stopped, fired when it starts moving,
    // reset when it stops again so the next departure triggers a fresh alert.
    public bool QueueMovingAlertSent   = false;
}
