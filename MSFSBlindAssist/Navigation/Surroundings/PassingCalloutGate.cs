using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// Pure decision state for "Passing X, on the left." A feature is PASSED at its closest point of
/// approach: its range closed by at least <see cref="MinApproachMetres"/> and has since opened by
/// <see cref="OpeningMetres"/>, and that closest point was abeam, driven through (not stopped at),
/// outside zero range and inside the kind's <see cref="PassRadiusMetres"/>. Parked beside a
/// building the range never closes, so no start-up baseline is needed.
///
/// <para>Tracks are keyed on kind, name AND position, because distinct buildings share names. At
/// most one callout per building per <see cref="PerFeatureRepeat"/>, one per
/// <see cref="GlobalGap"/> overall, and only at taxi speed. A pass held back by the gap or by speed
/// waits (describing its own closest point) until <see cref="PendingExpiry"/>. The caller passes
/// <c>now</c>; there is no clock inside.</para>
/// </summary>
public sealed class PassingCalloutGate
{
    public const double MinSpeedKts = 2.0, MaxSpeedKts = 40.0;
    public const double MinApproachMetres = 15.0, OpeningMetres = 5.0;

    /// <summary>The closest point must itself be roughly abeam, judged at the MINIMUM sample (never
    /// the detection tick, which at 40 kt can already read ~150°). This is what rejects a building
    /// approached tail-first during a pushback.</summary>
    public const double AbeamMinDeg = 45.0, AbeamMaxDeg = 135.0;

    /// <summary>A feature continues an existing same-key track only within this distance of it.
    /// Never widen: a generic hangar pair can be exactly this far apart (Hangar's merge radius), so
    /// the margin is zero.</summary>
    public const double SameFeatureMetres = 40.0;

    public static readonly TimeSpan PerFeatureRepeat = TimeSpan.FromMinutes(5), GlobalGap = TimeSpan.FromSeconds(10),
                                    TrackExpiry = TimeSpan.FromSeconds(30), RepeatMemory = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long one spoken sentence (name + side) is held, whichever feature carries it next. Many
    /// distinct features share a name — synthesized labels ("Cargo ramp" per stand cluster) and
    /// repeated scenery names (RJFF has thirty "Fuk City Hangar") — and the pilot cannot tell the
    /// sentences apart, so the second is not news. Measurements: docs/taxi-guidance.md.
    /// </summary>
    public static readonly TimeSpan SameNameRepeat = PerFeatureRepeat;

    /// <summary>How long a held pass waits before it is dropped unspoken, so a callout never names a
    /// building the aircraft has since left (stop inside its radius, taxi on minutes later).</summary>
    public static readonly TimeSpan PendingExpiry = TimeSpan.FromSeconds(20);

    // Min/MinRel (and StoppedNearest) freeze once Passed is set, so a pass reports exactly what was
    // judged when it armed, however long it then waits.
    private sealed class Track { public string Key = ""; public double First, Min, MinRel, AnchorLat, AnchorLon, StoppedNearest = double.PositiveInfinity; public DateTime LastSeen, PendingAt; public bool Passed, Pending; }
    private sealed class FiredRecord { public string Key = ""; public double Lat, Lon; public DateTime FiredAt; }

    private readonly List<Track> _tracks = new();
    private readonly List<FiredRecord> _lastFired = new();
    /// <summary>Spoken name + side → when that sentence was last said. "On the left" and "on the
    /// right" are different sentences about buildings the pilot can tell apart.</summary>
    private readonly Dictionary<string, DateTime> _lastSpokenSentence = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sentence's identity: the spoken name and the side (bearing normalised to
    /// (-180, 180]).</summary>
    private static string SentenceKey(AirportFeature f, double relBearingDeg)
        => f.SpokenName + "|" + (DockingGeometry.NormalizeDeg180(relBearingDeg) < 0 ? "L" : "R");
    private DateTime? _lastAny;

    internal int TrackCount => _tracks.Count;

    /// <summary>
    /// How near a feature of this kind must come, judged at its closest point when the pass arms.
    /// Measured on recorded EHAM and LOWI taxis: what a taxiing aircraft passes sits just outside
    /// the original 150/200/100 m (EHAM piers 141-223 m, LOWI hangars 106-137 m); these values clear
    /// both. Details in docs/taxi-guidance.md.
    /// </summary>
    public static double PassRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Concourse or FeatureKind.Terminal => 225.0,
        FeatureKind.Tower => 300.0,
        _ => 150.0,
    };

    /// <summary>
    /// How wide the caller ranks before <see cref="Evaluate"/>, and where tracking starts. Must be
    /// at least <see cref="MinApproachMetres"/> above every pass radius, or a feature passed just
    /// inside its radius could never close enough to arm. Pinned by tests.
    /// </summary>
    public const double RankRadiusMetres = 350.0;

    /// <summary>
    /// Which features a callout may name. A hangar only with a proper name — "Passing Hangar" names
    /// nothing a pilot can look for. Unnamed fuel, tower and fire station still speak; pilots orient
    /// by them.
    /// </summary>
    public static bool IsAnnounceable(AirportFeature f) => f.Kind switch
    {
        FeatureKind.Terminal or FeatureKind.Concourse or FeatureKind.Fbo or FeatureKind.Tower
            or FeatureKind.Fuel or FeatureKind.Cargo or FeatureKind.FireStation => true,
        FeatureKind.Hangar => f.HasProperName,
        _ => false,
    };

    // Kind + name; FindTrack/FindFired add the position.
    private static string Key(AirportFeature f) => $"{(int)f.Kind}|{f.SpokenName.Trim().ToUpperInvariant()}";

    private static bool IsAbeam(double relDeg)
    {
        double abs = Math.Abs(DockingGeometry.NormalizeDeg180(relDeg));
        return abs >= AbeamMinDeg && abs <= AbeamMaxDeg;
    }

    /// <summary>
    /// Is an armed closest point worth saying? Not when it was not abeam, was at or inside
    /// <see cref="SurroundingsReport.ZeroRangeMetres"/> (the side would be bearing noise), or was
    /// reached while stopped — a navdata Fuel or Cargo ramp IS its stands, so parking on one and
    /// leaving is not a pass. Such a track is consumed silently.
    /// </summary>
    private static bool IsSayablePass(double minMetres, double minRelDeg, double stoppedNearestMetres)
        => IsAbeam(minRelDeg)
           && minMetres > SurroundingsReport.ZeroRangeMetres
           && !(stoppedNearestMetres < minMetres + OpeningMetres);

    /// <summary>Nearest track with this key within <see cref="SameFeatureMetres"/>, or null.</summary>
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

    /// <summary>
    /// Feeds one ranked sample; returns the pass to announce, if any, described as it was at its own
    /// closest point (always a genuine side, never ahead/behind), not at the tick that releases it.
    /// </summary>
    public NearbyFeature? Evaluate(IReadOnlyList<NearbyFeature> nearby, double groundSpeedKts, DateTime now)
    {
        Prune(now);
        bool mayFire = groundSpeedKts >= MinSpeedKts && groundSpeedKts <= MaxSpeedKts
                       && !(_lastAny is DateTime last && now - last < GlobalGap);
        // Below the taxi band (or unreadable) counts as stopped; that can only withhold a callout.
        bool stopped = !(groundSpeedKts >= MinSpeedKts);
        NearbyFeature? fire = null; Track? fireTrack = null;
        foreach (var n in nearby)
        {
            // Tracked from the rank window's edge; the kind's radius is applied when the pass arms.
            // `!(d <= …)` also skips a NaN range.
            if (!IsAnnounceable(n.Feature) || !(n.DistanceMetres <= RankRadiusMetres)) continue;
            string key = Key(n.Feature); double d = n.DistanceMetres;
            var t = FindTrack(key, n.Feature.Lat, n.Feature.Lon);
            if (t == null)
            {
                t = new Track { Key = key, First = d, Min = d, MinRel = n.RelativeBearingDeg, AnchorLat = n.Feature.Lat, AnchorLon = n.Feature.Lon };
                _tracks.Add(t);
            }
            t.LastSeen = now; t.AnchorLat = n.Feature.Lat; t.AnchorLon = n.Feature.Lon;
            if (!t.Passed && d < t.Min) { t.Min = d; t.MinRel = n.RelativeBearingDeg; }
            if (!t.Passed && stopped && d < t.StoppedNearest) t.StoppedNearest = d;

            // Outside the kind's radius the track stays unarmed, so a later, nearer approach still counts.
            if (!t.Passed && t.First - t.Min >= MinApproachMetres && d >= t.Min + OpeningMetres
                && t.Min <= PassRadiusMetres(n.Feature.Kind))
            {
                t.Passed = true;
                t.Pending = IsSayablePass(t.Min, t.MinRel, t.StoppedNearest);
                t.PendingAt = now;
            }
            if (!t.Pending) continue;
            // Checked before mayFire: a pass held by speed never gets past mayFire.
            if (now - t.PendingAt >= PendingExpiry) { t.Pending = false; continue; }
            if (!mayFire) continue;
            var fired = FindFired(key, n.Feature.Lat, n.Feature.Lon);
            if (fired != null && now - fired.FiredAt < PerFeatureRepeat) { t.Pending = false; continue; }
            // The same sentence was just said about another feature: consumed like a repeat.
            if (_lastSpokenSentence.TryGetValue(SentenceKey(n.Feature, t.MinRel), out var saidAt)
                && now - saidAt < SameNameRepeat) { t.Pending = false; continue; }
            if (fire == null || d < fire.DistanceMetres) { fire = n; fireTrack = t; }
        }
        if (fire == null) return null;
        fireTrack!.Pending = false;
        RecordFired(fireTrack.Key, fireTrack.AnchorLat, fireTrack.AnchorLon, now);
        _lastSpokenSentence[SentenceKey(fire.Feature, fireTrack.MinRel)] = now;
        _lastAny = now;
        return new NearbyFeature(fire.Feature, fireTrack.Min, fireTrack.MinRel);
    }

    private void Prune(DateTime now)
    {
        _tracks.RemoveAll(t => now - t.LastSeen > TrackExpiry);
        _lastFired.RemoveAll(f => now - f.FiredAt > RepeatMemory);
    }

    public void Reset() { _tracks.Clear(); _lastFired.Clear(); _lastSpokenSentence.Clear(); _lastAny = null; }

    /// <summary>
    /// Forget approaches in progress but keep what the pilot has been told — for a caller handed a
    /// rebuilt catalog, whose geometry can differ (a range STEP is not an approach). A full
    /// <see cref="Reset"/> would let a building just announced be announced again.
    /// </summary>
    public void RebaselineTracks() => _tracks.Clear();
}
