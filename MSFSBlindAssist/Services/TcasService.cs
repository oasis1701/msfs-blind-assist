using MSFSBlindAssist.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Polls SimConnect for AI/multiplayer aircraft, computes relative position data,
/// and maintains a live snapshot that TcasForm reads on its refresh timer.
/// </summary>
public class TcasService : IDisposable
{
    private readonly SimConnectManager _simConnect;
    private readonly System.Windows.Forms.Timer _pollTimer;

    private readonly object _lock = new();
    private Dictionary<uint, TcasTraffic> _current = new();
    private Dictionary<uint, TcasTraffic> _pending = new();

    private readonly HashSet<string> _trackedCallsigns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Last-known traffic for each tracked callsign.
    /// Preserved even after the aircraft leaves SimConnect range so the hotkey
    /// can still report stale data with a "last known" marker.
    /// </summary>
    private readonly Dictionary<string, TcasTraffic> _lastSeenTracked = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler? TrafficUpdated;

    public TcasService(SimConnectManager simConnect)
    {
        _simConnect = simConnect;
        _simConnect.AiTrafficReceived += OnAiTrafficReceived;

        _pollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _pollTimer.Tick += (_, _) => Poll();
    }

    public void Start() => _pollTimer.Start();
    public void Stop()  => _pollTimer.Stop();

    /// <summary>
    /// Triggers an immediate poll outside the normal 3-second cycle.
    /// Used by the tracked-aircraft hotkey so announcements reflect the
    /// freshest possible position before reading out.
    /// </summary>
    public void PollNow() => Poll();

    private void Poll()
    {
        // Refresh own-aircraft position cache before requesting traffic
        _simConnect.RequestAircraftPosition();
        // Start a new pending dict — responses accumulate here without
        // clearing _current, so GetTraffic() always returns the previous
        // complete snapshot until the new one is promoted.
        lock (_lock) _pending = new Dictionary<uint, TcasTraffic>();
        _simConnect.RequestAiTrafficData();
    }

    private void OnAiTrafficReceived(object? sender, AiTrafficDataEventArgs e)
    {
        // Read the cached own-position that was just refreshed in Poll()
        var own = _simConnect.LastKnownPosition;

        double distNm     = 0;
        double altDiff    = 0;
        double relBearing = 0;

        if (own.HasValue)
        {
            distNm    = NavigationCalculator.CalculateDistance(
                own.Value.Latitude, own.Value.Longitude,
                e.Latitude, e.Longitude);

            altDiff   = e.AltitudeFt - own.Value.Altitude;

            double trueBearing = NavigationCalculator.CalculateBearing(
                own.Value.Latitude, own.Value.Longitude,
                e.Latitude, e.Longitude);

            // Convert true bearing to relative-to-own-heading.
            double ownTrueHeading = own.Value.HeadingMagnetic + own.Value.MagneticVariation;
            relBearing = NormalizeBearing(trueBearing - ownTrueHeading);
        }

        // If SimConnect says on-ground but the aircraft is >500ft above own altitude
        // it is clearly airborne — treat it as airborne regardless of the flag.
        bool effectivelyOnGround = e.OnGround && altDiff <= 500;

        // SimConnect often returns "ATCCONN" or a model path for vPilot/FSLTL-injected
        // VATSIM traffic.  If the resolved type is still empty, cross-reference the
        // VATSIM live data feed using the callsign.
        string aircraftType = string.IsNullOrEmpty(e.AircraftType)
            ? VatsimPilotDataService.GetAircraftType(e.Callsign)
            : e.AircraftType;

        // SimConnect AI TRAFFIC FROMAIRPORT/TOAIRPORT are only populated for
        // scheduled AI traffic.  For VATSIM traffic, fall back to the live feed
        // which has departure/arrival from each pilot's filed flight plan.
        string fromAirport = e.FromAirport;
        string toAirport   = e.ToAirport;
        if (string.IsNullOrEmpty(fromAirport) && string.IsNullOrEmpty(toAirport))
        {
            var vatsimRoute = VatsimPilotDataService.GetRoute(e.Callsign);
            fromAirport = vatsimRoute.Departure;
            toAirport   = vatsimRoute.Arrival;
        }

        var traffic = new TcasTraffic
        {
            ObjectId         = e.ObjectId,
            Callsign         = e.Callsign,
            AircraftType     = aircraftType,
            Latitude         = e.Latitude,
            Longitude        = e.Longitude,
            AltitudeFt       = e.AltitudeFt,
            HeadingMagnetic  = e.HeadingMagnetic,
            GroundSpeedKnots = e.GroundSpeedKnots,
            OnGround         = effectivelyOnGround,
            FromAirport      = fromAirport,
            ToAirport        = toAirport,
            Airline          = e.Airline,
            TrafficState     = e.TrafficState,
            DistanceNm       = distNm,
            AltitudeDiffFt   = altDiff,
            RelativeBearing  = relBearing,
        };

        lock (_lock)
        {
            _pending[e.ObjectId] = traffic;
            // Promote pending to current so GetTraffic() always sees
            // the most complete snapshot available.
            _current = _pending;

            // Keep the last-known snapshot for any tracked callsign so announcements
            // still work after the aircraft drifts out of SimConnect range.
            if (!string.IsNullOrEmpty(traffic.Callsign) &&
                _trackedCallsigns.Contains(traffic.Callsign))
            {
                _lastSeenTracked[traffic.Callsign] = traffic;
            }
        }

        TrafficUpdated?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<TcasTraffic> GetTraffic(bool onGround)
    {
        lock (_lock)
        {
            return _current.Values
                .Where(t => t.OnGround == onGround)
                .OrderBy(t => t.DistanceNm)
                .ToList();
        }
    }

    // ── Track list ────────────────────────────────────────────────────────────

    public void AddToTrackList(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return;
        lock (_lock) _trackedCallsigns.Add(callsign.Trim());
    }

    public void RemoveFromTrackList(string callsign)
    {
        string key = callsign.Trim();
        lock (_lock)
        {
            _trackedCallsigns.Remove(key);
            _lastSeenTracked.Remove(key);
        }
    }

    public bool IsTracked(string callsign)
    {
        lock (_lock) return _trackedCallsigns.Contains(callsign.Trim());
    }

    public IReadOnlyList<string> GetTrackedAnnouncements()
    {
        lock (_lock)
        {
            var results = new List<string>();
            foreach (var call in _trackedCallsigns)
            {
                // Live data first
                var live = _current.Values.FirstOrDefault(x =>
                    string.Equals(x.Callsign, call, StringComparison.OrdinalIgnoreCase));

                if (live != null)
                {
                    results.Add(live.BriefAnnouncement);
                }
                else if (_lastSeenTracked.TryGetValue(call, out var last))
                {
                    // Aircraft has left SimConnect range — report stale position
                    results.Add($"{last.BriefAnnouncement}, last known");
                }
                else
                {
                    results.Add($"{call}, not yet in range");
                }
            }
            return results;
        }
    }

    public bool HasTracked { get { lock (_lock) return _trackedCallsigns.Count > 0; } }

    private static double NormalizeBearing(double b)
    {
        while (b >  180) b -= 360;
        while (b < -180) b += 360;
        return b;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _simConnect.AiTrafficReceived -= OnAiTrafficReceived;
    }
}
