using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Pure decision state for "Passing X, on the left." A building is PASSED when its range was
/// closing and is now opening — the closest point of approach — while that closest point was
/// itself roughly ABEAM (not a building only ever approached head-on, and not one behind the
/// aircraft during a pushback reversal), the building is inside its kind's radius, and the
/// aircraft is at taxi speed. Parked beside a terminal, or pushed back from one without ever
/// closing past the very first reading, the range never closes, so nothing is recited and no
/// baseline is needed (the old one-shot baseline was a 5-minute timestamp that lapsed during any
/// normal preflight). Each track is identified by KIND, NAME *and* POSITION: two different
/// buildings can legitimately share a name (a navdata per-cluster generic "Fuel"/"Cargo", two
/// same-named piers, several same-named scenery clutter clusters), and a name-only key let the
/// nearer one's minimum make the farther one's still-closing range instantly read as "opening" —
/// a false pass. Once per BUILDING per five minutes, once globally per ten seconds; a pass held
/// back only by the global gap stays pending while the building is still in range. No clock
/// inside: the caller passes `now`.
/// </summary>
public sealed class PassingCalloutGate
{
    public const double MinSpeedKts = 2.0, MaxSpeedKts = 40.0;
    public const double MinApproachMetres = 15.0, OpeningMetres = 5.0;

    /// <summary>Under forward motion the range to a fixed point stops closing exactly when the
    /// point is abeam (perpendicular to the direction of travel) — closest point of approach
    /// implies abeam. The exception is a minimum reached because the aircraft stopped and reversed
    /// (a pushback straight toward a building behind the stand closes the range tail-first; taxiing
    /// away afterward then "opens" it), which must not be reported as a pass. Judged at the bearing
    /// of the MINIMUM-range sample, never the detection tick: at 40 kt and a 2 s poll the detection
    /// tick can land 40+ m past the true closest point, where a genuinely abeam building already
    /// reads well past 135 degrees.</summary>
    public const double AbeamMinDeg = 45.0, AbeamMaxDeg = 135.0;

    /// <summary>Two announceable features may legitimately share a name (navdata's per-cluster
    /// generic "Fuel"/"Cargo", two same-named piers at one airport, several same-named scenery
    /// clutter clusters) — a track's identity is its key AND position: an incoming feature only
    /// continues an existing track when it lies within this many metres of that track's last-seen
    /// position. The catalog merges same-named features within at least 80 m
    /// (AirportFeatureCatalog.SameNameRadiusMetres — the smallest of any kind, Hangar's), so two
    /// DIFFERENT same-named features that survive the merge are never this close together, while a
    /// catalog rebuild nudging a merged centroid moves it by metres, not tens of metres.</summary>
    public const double SameFeatureMetres = 40.0;

    public static readonly TimeSpan PerFeatureRepeat = TimeSpan.FromMinutes(5), GlobalGap = TimeSpan.FromSeconds(10),
                                    TrackExpiry = TimeSpan.FromSeconds(30), RepeatMemory = TimeSpan.FromMinutes(10);

    private sealed class Track { public string Key = ""; public double First, Min, MinRel, AnchorLat, AnchorLon; public DateTime LastSeen; public bool Passed, Pending; }
    private sealed class FiredRecord { public string Key = ""; public double Lat, Lon; public DateTime FiredAt; }

    private readonly List<Track> _tracks = new();
    private readonly List<FiredRecord> _lastFired = new();
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

    // Kind + name only: position (FindTrack/FindFired) is what tells two same-named features apart.
    private static string Key(AirportFeature f) => $"{(int)f.Kind}|{f.SpokenName.Trim().ToUpperInvariant()}";

    /// <summary>-180..180 is the documented shape of RelativeBearingDeg, but this normalises
    /// defensively so a 0..360 value still lands on the correct side.</summary>
    private static double NormalizeSigned(double deg)
    {
        double d = deg % 360.0;
        if (d > 180.0) d -= 360.0;
        else if (d <= -180.0) d += 360.0;
        return d;
    }

    private static bool IsAbeam(double relDeg)
    {
        double abs = Math.Abs(NormalizeSigned(relDeg));
        return abs >= AbeamMinDeg && abs <= AbeamMaxDeg;
    }

    /// <summary>Nearest track sharing this key within SameFeatureMetres of the given position, or
    /// null to start a new one. Several same-key tracks within range "should not happen" (a catalog
    /// keeps same-named features apart by at least 80 m), but take the nearest if it does.</summary>
    private Track? FindTrack(string key, double lat, double lon)
    {
        Track? best = null; double bestD = SameFeatureMetres;
        foreach (var t in _tracks)
            if (t.Key == key)
            {
                double d = TaxiGeo.HaversineMeters(t.AnchorLat, t.AnchorLon, lat, lon);
                if (d <= bestD) { bestD = d; best = t; }
            }
        return best;
    }

    private FiredRecord? FindFired(string key, double lat, double lon)
    {
        FiredRecord? best = null; double bestD = SameFeatureMetres;
        foreach (var f in _lastFired)
            if (f.Key == key)
            {
                double d = TaxiGeo.HaversineMeters(f.Lat, f.Lon, lat, lon);
                if (d <= bestD) { bestD = d; best = f; }
            }
        return best;
    }

    private void RecordFired(string key, double lat, double lon, DateTime now)
    {
        var existing = FindFired(key, lat, lon);
        if (existing != null) { existing.Lat = lat; existing.Lon = lon; existing.FiredAt = now; }
        else _lastFired.Add(new FiredRecord { Key = key, Lat = lat, Lon = lon, FiredAt = now });
    }

    public NearbyFeature? Evaluate(IReadOnlyList<NearbyFeature> nearby, double groundSpeedKts, DateTime now)
    {
        Prune(now);
        bool mayFire = groundSpeedKts >= MinSpeedKts && groundSpeedKts <= MaxSpeedKts
                       && !(_lastAny is DateTime last && now - last < GlobalGap);
        NearbyFeature? fire = null; Track? fireTrack = null;
        foreach (var n in nearby)
        {
            if (!IsAnnounceable(n.Feature) || n.DistanceMetres > PassRadiusMetres(n.Feature.Kind)) continue;
            string key = Key(n.Feature); double d = n.DistanceMetres;
            var t = FindTrack(key, n.Feature.Lat, n.Feature.Lon);
            if (t == null)
            {
                t = new Track { Key = key, First = d, Min = d, MinRel = n.RelativeBearingDeg, AnchorLat = n.Feature.Lat, AnchorLon = n.Feature.Lon };
                _tracks.Add(t);
            }
            t.LastSeen = now; t.AnchorLat = n.Feature.Lat; t.AnchorLon = n.Feature.Lon;
            if (d < t.Min) { t.Min = d; t.MinRel = n.RelativeBearingDeg; }

            if (!t.Passed && t.First - t.Min >= MinApproachMetres && d >= t.Min + OpeningMetres)
            {
                t.Passed = true;
                t.Pending = IsAbeam(t.MinRel);   // tail-first, or a turn away before reaching it: consumed silently
            }
            if (!t.Pending || !mayFire) continue;
            var fired = FindFired(key, n.Feature.Lat, n.Feature.Lon);
            if (fired != null && now - fired.FiredAt < PerFeatureRepeat) { t.Pending = false; continue; }
            if (fire == null || d < fire.DistanceMetres) { fire = n; fireTrack = t; }     // nearest first
        }
        if (fire == null) return null;
        fireTrack!.Pending = false;
        RecordFired(fireTrack.Key, fireTrack.AnchorLat, fireTrack.AnchorLon, now);
        _lastAny = now;
        return fire;
    }

    private void Prune(DateTime now)
    {
        _tracks.RemoveAll(t => now - t.LastSeen > TrackExpiry);
        _lastFired.RemoveAll(f => now - f.FiredAt > RepeatMemory);
    }

    public void Reset() { _tracks.Clear(); _lastFired.Clear(); _lastAny = null; }
}
