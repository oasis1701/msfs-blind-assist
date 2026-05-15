using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Monitors on-ground AI and multiplayer aircraft for proximity alerts while taxiing.
/// Fires automatic spoken alerts on zone transitions and provides a hotkey summary
/// of the nearest aircraft.
/// </summary>
public sealed class GroundTrafficMonitor : IDisposable
{
    // Alert zone thresholds in feet (aviation basis: 500 ft ≈ 30s at 10 kt)
    private const double AWARENESS_FT = 500.0;
    private const double CAUTION_FT   = 250.0;
    private const double WARNING_FT   = 100.0;

    // Forward arc for zone-based auto-alerts (±degrees from own nose)
    // Aircraft outside this range only appear in the hotkey summary, not auto-alerts
    private const double FORWARD_ARC_DEG = 120.0;

    // Minimum own GS before a caution-zone alert becomes "Slow down" rather than plain "Traffic"
    private const double SLOW_DOWN_GS_KTS = 5.0;

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
    private const double OWN_QUEUE_GS     = 3.0;   // we must be near-stationary to be "in a queue"

    // Hotkey summary: announce this many nearest aircraft
    private const int SUMMARY_MAX_AIRCRAFT = 3;

    private const int POLL_INTERVAL_MS = 3000;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly object _lock = new();
    private readonly Dictionary<uint, TrackedGroundAircraft> _tracked = new();

    // Own-position snapshot, updated each tick from LastKnownPosition
    private double _ownLat, _ownLon, _ownHeadingTrue, _ownGS;
    private bool _positionValid;

    public GroundTrafficMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        _sim.AiTrafficReceived += OnAiTrafficReceived;

        _timer = new System.Windows.Forms.Timer { Interval = POLL_INTERVAL_MS };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Timer

    private void OnTick(object? sender, EventArgs e)
    {
        bool onGround = _sim.LastKnownOnGround ?? false;
        if (!onGround || !_sim.IsConnected) return;

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
            double d = DistanceFeet(pos.Value.Latitude, pos.Value.Longitude, e.Latitude, e.Longitude);
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
            ac.PreviousGS = ac.GS;
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

            foreach (var ac in _tracked.Values)
            {
                double distFt = DistanceFeet(ownLat, ownLon, ac.Lat, ac.Lon);
                double bearing = BearingDeg(ownLat, ownLon, ac.Lat, ac.Lon);
                double relBearing = NormalizeDeg(bearing - ownHdg);

                // Update closing-rate history
                ac.PreviousDistance = ac.CurrentDistance;
                ac.CurrentDistance = distFt;
                bool movingAway = ac.PreviousDistance < double.MaxValue
                                  && ac.CurrentDistance > ac.PreviousDistance + MOVING_AWAY_HYSTERESIS_FT;

                // Queue-moving alert: aircraft directly ahead was stopped and just started moving.
                // Only fires when we are also near-stationary (in the queue behind them).
                bool directlyAhead = relBearing <= QUEUE_AHEAD_DEG || relBearing >= (360.0 - QUEUE_AHEAD_DEG);
                if (directlyAhead && distFt <= AWARENESS_FT && ownGS <= OWN_QUEUE_GS)
                {
                    if (ac.PreviousGS <= QUEUE_STOPPED_GS && ac.GS >= QUEUE_MOVING_GS && !ac.QueueMovingAlertSent)
                    {
                        ac.QueueMovingAlertSent = true;
                        _announcer.AnnounceImmediate("Traffic ahead is moving.");
                    }
                    // Reset the flag once the aircraft stops again so the next departure fires a fresh alert
                    if (ac.GS <= QUEUE_STOPPED_GS)
                        ac.QueueMovingAlertSent = false;
                }

                // Determine new zone
                GroundZone newZone;
                if (distFt > AWARENESS_FT)       newZone = GroundZone.None;
                else if (distFt <= WARNING_FT)   newZone = GroundZone.Warning;
                else if (distFt <= CAUTION_FT)   newZone = GroundZone.Caution;
                else                             newZone = GroundZone.Awareness;

                if (newZone == GroundZone.None)  { ac.CurrentZone = GroundZone.None; continue; }

                // Awareness alerts only fire for traffic in the forward arc
                bool inForwardArc = relBearing <= FORWARD_ARC_DEG || relBearing >= (360.0 - FORWARD_ARC_DEG);
                if (!inForwardArc && newZone == GroundZone.Awareness) { ac.CurrentZone = newZone; continue; }

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
                int d = RoundFeet(distFt);

                string announcement = newZone switch
                {
                    GroundZone.Warning  => $"Stop, traffic very close, {dir}, {d} feet.",
                    GroundZone.Caution when ownGS >= SLOW_DOWN_GS_KTS
                                        => $"Slow down, traffic {dir}, {d} feet.",
                    _                   => $"Traffic, {dir}, {d} feet."
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
                    double d = DistanceFeet(ownLat, ownLon, ac.Lat, ac.Lon);
                    double b = BearingDeg(ownLat, ownLon, ac.Lat, ac.Lon);
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
            int d = RoundFeet(distFt);
            string label = !string.IsNullOrEmpty(ac.Callsign) ? ac.Callsign : "traffic";
            sb.Append($"{label}, {dir}, {d} feet. ");
        }
        return sb.ToString().TrimEnd();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Geometry helpers

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

    private static double DistanceFeet(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * (Math.PI / 180.0);
        double dLon = (lon2 - lon1) * (Math.PI / 180.0);
        double cosLat1 = Math.Cos(lat1 * (Math.PI / 180.0));
        double cosLat2 = Math.Cos(lat2 * (Math.PI / 180.0));
        double a = dLat * dLat + cosLat1 * cosLat2 * dLon * dLon;
        return Math.Sqrt(a) * R * 3.28084;
    }

    private static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon = (lon2 - lon1) * (Math.PI / 180.0);
        double y = Math.Sin(dLon) * Math.Cos(lat2 * (Math.PI / 180.0));
        double x = Math.Cos(lat1 * (Math.PI / 180.0)) * Math.Sin(lat2 * (Math.PI / 180.0))
                 - Math.Sin(lat1 * (Math.PI / 180.0)) * Math.Cos(lat2 * (Math.PI / 180.0)) * Math.Cos(dLon);
        return NormalizeDeg(Math.Atan2(y, x) * (180.0 / Math.PI));
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
        _timer.Stop();
        _timer.Dispose();
    }
}

internal enum GroundZone { None = 0, Awareness = 1, Caution = 2, Warning = 3 }

internal sealed class TrackedGroundAircraft
{
    public uint ObjectId;
    public double Lat, Lon, GS, PreviousGS;
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
