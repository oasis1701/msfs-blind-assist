using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Pure decision state for "Passing X, on the left." A building is PASSED when its range was
/// closing and is now opening — the closest point of approach — while it is inside its kind's
/// radius and the aircraft is at taxi speed. Parked beside a terminal, or pushed back from one, the
/// range never closes, so nothing is recited and no baseline is needed (the old one-shot baseline
/// was a 5-minute timestamp that lapsed during any normal preflight). Once per feature per five
/// minutes, once globally per ten seconds; a pass held back only by the global gap stays pending
/// while the building is still in range. No clock inside: the caller passes `now`.
/// </summary>
public sealed class PassingCalloutGate
{
    public const double MinSpeedKts = 2.0, MaxSpeedKts = 40.0;
    public const double MinApproachMetres = 15.0, OpeningMetres = 5.0, TrackRestartMetres = 300.0;
    public static readonly TimeSpan PerFeatureRepeat = TimeSpan.FromMinutes(5), GlobalGap = TimeSpan.FromSeconds(10),
                                    TrackExpiry = TimeSpan.FromSeconds(30), RepeatMemory = TimeSpan.FromMinutes(10);

    private sealed class Track { public double First, Min, AnchorLat, AnchorLon; public DateTime LastSeen; public bool Passed, Pending; }
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastFired = new(StringComparer.Ordinal);
    private DateTime? _lastAny;

    internal int TrackCount => _tracks.Count;

    public static double PassRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Concourse or FeatureKind.Terminal => 150.0,
        FeatureKind.Tower => 200.0,
        _ => 100.0,
    };

    public static bool IsAnnounceable(AirportFeature f) => f.Kind switch
    {
        FeatureKind.Terminal or FeatureKind.Concourse or FeatureKind.Fbo or FeatureKind.Tower
            or FeatureKind.Fuel or FeatureKind.Cargo or FeatureKind.FireStation => true,
        FeatureKind.Hangar => f.HasName,
        _ => false,
    };

    // Kind + name only: a rebuilt catalog may nudge a merged centroid, and that is the same building.
    private static string Key(AirportFeature f) => $"{(int)f.Kind}|{f.SpokenName.Trim().ToUpperInvariant()}";

    public NearbyFeature? Evaluate(IReadOnlyList<NearbyFeature> nearby, double groundSpeedKts, DateTime now)
    {
        Prune(now);
        bool mayFire = groundSpeedKts >= MinSpeedKts && groundSpeedKts <= MaxSpeedKts
                       && !(_lastAny is DateTime last && now - last < GlobalGap);
        NearbyFeature? fire = null; string? fireKey = null;
        foreach (var n in nearby)
        {
            if (!IsAnnounceable(n.Feature) || n.DistanceMetres > PassRadiusMetres(n.Feature.Kind)) continue;
            string key = Key(n.Feature); double d = n.DistanceMetres;
            if (!_tracks.TryGetValue(key, out var t)
                || TaxiGeo.HaversineMeters(t.AnchorLat, t.AnchorLon, n.Feature.Lat, n.Feature.Lon) > TrackRestartMetres)
                _tracks[key] = t = new Track { First = d, Min = d, AnchorLat = n.Feature.Lat, AnchorLon = n.Feature.Lon };
            t.LastSeen = now; t.AnchorLat = n.Feature.Lat; t.AnchorLon = n.Feature.Lon;
            if (d < t.Min) t.Min = d;

            if (!t.Passed && t.First - t.Min >= MinApproachMetres && d >= t.Min + OpeningMetres)
            { t.Passed = true; t.Pending = true; }
            if (!t.Pending || !mayFire) continue;
            if (_lastFired.TryGetValue(key, out var said) && now - said < PerFeatureRepeat) { t.Pending = false; continue; }
            if (fire == null || d < fire.DistanceMetres) { fire = n; fireKey = key; }     // nearest first
        }
        if (fire == null) return null;
        _tracks[fireKey!].Pending = false;
        _lastFired[fireKey!] = now; _lastAny = now;
        return fire;
    }

    private void Prune(DateTime now)
    {
        foreach (var k in _tracks.Where(kv => now - kv.Value.LastSeen > TrackExpiry).Select(kv => kv.Key).ToList()) _tracks.Remove(k);
        foreach (var k in _lastFired.Where(kv => now - kv.Value > RepeatMemory).Select(kv => kv.Key).ToList()) _lastFired.Remove(k);
    }

    public void Reset() { _tracks.Clear(); _lastFired.Clear(); _lastAny = null; }
}
