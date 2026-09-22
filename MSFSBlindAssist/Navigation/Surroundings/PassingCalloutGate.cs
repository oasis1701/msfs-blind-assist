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
/// a false pass. Once per BUILDING per five minutes, once globally per ten seconds, and only
/// while ground speed is within the taxi speed band; a pass held back by the global gap OR by
/// ground speed outside that band stays PENDING while the building is still in range, and fires
/// — still describing the side and range it had at its OWN closest point, never the tick that
/// finally releases it — once every condition allows, or is dropped unspoken once
/// <see cref="PendingExpiry"/> has gone by. No clock inside: the caller passes `now`.
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
    /// position. Safety comes from AirportFeatureCatalog.MergeRadiusMetres, NOT SameNameRadiusMetres:
    /// that wider floor (at least 80 m) applies only when BOTH features carry a proper name, but the
    /// motivating case here — a navdata per-cluster generic label — is the one where NEITHER does,
    /// AirportFeatureCatalog.SameFeature's third branch, which merges two such features only when
    /// they are within MergeRadiusMetres for their kind. The smallest of those is Hangar's 40 m —
    /// exactly this constant, so the margin is ZERO for a generic hangar pair, not "half" of
    /// anything. NEVER widen this constant on the strength of a margin that, for that pair, does
    /// not exist.</summary>
    public const double SameFeatureMetres = 40.0;

    public static readonly TimeSpan PerFeatureRepeat = TimeSpan.FromMinutes(5), GlobalGap = TimeSpan.FromSeconds(10),
                                    TrackExpiry = TimeSpan.FromSeconds(30), RepeatMemory = TimeSpan.FromMinutes(10);

    /// <summary>How long a pass waits for its conditions before it is given up on. A pass held by
    /// the global gap or by ground speed keeps its own closest-point side and range, which is only
    /// worth saying while the building is still THERE: pass one, stop inside its radius (below
    /// <see cref="MinSpeedKts"/> nothing may fire) and taxi on three minutes later, and the pilot
    /// heard "Passing X, on the left" about somewhere they no longer were. Long enough for the 10 s
    /// <see cref="GlobalGap"/> and a late tick on top, short enough that what is described is still
    /// beside the aircraft. An expired pass is consumed silently, exactly like a non-abeam one.</summary>
    public static readonly TimeSpan PendingExpiry = TimeSpan.FromSeconds(20);

    // Min/MinRel freeze the instant Passed is set (Evaluate gates their update on !Passed): the
    // pair describes the pass exactly as it was JUDGED by IsAbeam at arm time, so a later, deeper
    // sample arriving while the pass sits out the global gap or excess speed — a second approach
    // after a turn back toward the building, a non-convex footprint's second local minimum, bearing
    // noise near a stop — can never drift what the pass reports once it finally fires.
    private sealed class Track { public string Key = ""; public double First, Min, MinRel, AnchorLat, AnchorLon; public DateTime LastSeen, PendingAt; public bool Passed, Pending; }
    private sealed class FiredRecord { public string Key = ""; public double Lat, Lon; public DateTime FiredAt; }

    private readonly List<Track> _tracks = new();
    private readonly List<FiredRecord> _lastFired = new();
    private DateTime? _lastAny;

    internal int TrackCount => _tracks.Count;

    /// <summary>
    /// How near a feature of this kind must come before a pass can be reported.
    ///
    /// <para>MEASURED, not chosen. Replaying two RECORDED pilot tracks — 5.59 km at EHAM, 4.35 km
    /// at LOWI, 31 Hz, through the production catalog, Rank and this gate — the shipped
    /// 150/200/100 m produced ONE callout at EHAM and TWO at LOWI. The blocker was neither the
    /// abeam rule nor the closure rule (on both real tracks EVERY announceable feature passed was
    /// abeam at its closest point); it was these numbers. What a taxiing aircraft goes past
    /// clusters just OUTSIDE them: EHAM's eight nearest piers at 141-223 m against a 150 m
    /// concourse radius — taxiway Bravo is Schiphol's OUTER parallel and never comes nearer —
    /// and LOWI's ten nearest hangars at 106-137 m against a 100 m one.</para>
    ///
    /// <para>x1.5 clears the whole cluster at both fields (225 &gt; 223, 150 &gt; 137) and takes
    /// EHAM from 1 callout to 7 and LOWI from 2 to 7, about 1.3-1.6 per kilometre. Wider was
    /// measured and rejected: x2 adds two at each field, x3 adds two more and both airports have
    /// SATURATED by then, so beyond this the radius only starts naming buildings the pilot is
    /// nowhere near. The gate's own limits — 10 s globally, 5 minutes per building — cap the
    /// worst case regardless of what is set here.</para>
    /// </summary>
    public static double PassRadiusMetres(FeatureKind k) => k switch
    {
        FeatureKind.Concourse or FeatureKind.Terminal => 225.0,
        FeatureKind.Tower => 300.0,
        _ => 150.0,
    };

    /// <summary>
    /// How wide the caller must rank before handing the list to <see cref="Evaluate"/>. A pass
    /// radius ABOVE this is a number the gate can never see: the monitor ranks once and the gate
    /// only ever sees what that list contains, so the two move together or the widest kind is
    /// silently capped at the window. This is the ceiling <see cref="PassRadiusMetres"/> is
    /// tested against, with headroom above the widest kind so a future widening there is a
    /// deliberate act rather than a silent no-op.
    /// </summary>
    public const double RankRadiusMetres = 350.0;

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
    /// null to start a new one. Several same-key tracks within range "should not happen" (see
    /// SameFeatureMetres's own doc comment for exactly how close a catalog can leave two of them),
    /// but take the nearest if it does.</summary>
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
    /// Returns the pass AS IT WAS AT ITS OWN CLOSEST POINT — the record's distance and bearing are
    /// the minimum sample's, never the tick that happens to release it. A pass held back by the
    /// global gap or by ground speed outside the taxi band can fire 10-25+ s after the closest
    /// point, by which time the bearing may have swung through a turn; announcing the CURRENT
    /// tick's bearing could then name the wrong side, or a direction that isn't a side at all
    /// ("behind"/"ahead"). Because Pending only ever arms when the minimum sample was abeam (45-135
    /// degrees either side), the returned bearing is always a genuine side — never behind or ahead.
    /// That pair is FROZEN the instant the pass arms (Min/MinRel stop updating once Passed is set
    /// — see Track's own comment), so a later, deeper sample arriving while the pass waits out the
    /// gap cannot drift what it eventually reports.
    /// </summary>
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
            if (!t.Passed && d < t.Min) { t.Min = d; t.MinRel = n.RelativeBearingDeg; }   // frozen once armed: see Track's own comment

            if (!t.Passed && t.First - t.Min >= MinApproachMetres && d >= t.Min + OpeningMetres)
            {
                t.Passed = true;
                t.Pending = IsAbeam(t.MinRel);   // tail-first, or a turn away before reaching it: consumed silently
                t.PendingAt = now;
            }
            if (!t.Pending) continue;
            // Checked ABOVE mayFire, not below it: the pass this exists for is one held by ground
            // speed, where mayFire is false on every tick and a check below would never be reached.
            if (now - t.PendingAt >= PendingExpiry) { t.Pending = false; continue; }
            if (!mayFire) continue;
            var fired = FindFired(key, n.Feature.Lat, n.Feature.Lon);
            if (fired != null && now - fired.FiredAt < PerFeatureRepeat) { t.Pending = false; continue; }
            if (fire == null || d < fire.DistanceMetres) { fire = n; fireTrack = t; }     // nearest first
        }
        if (fire == null) return null;
        fireTrack!.Pending = false;
        RecordFired(fireTrack.Key, fireTrack.AnchorLat, fireTrack.AnchorLon, now);
        _lastAny = now;
        return new NearbyFeature(fire.Feature, fireTrack.Min, fireTrack.MinRel);
    }

    private void Prune(DateTime now)
    {
        _tracks.RemoveAll(t => now - t.LastSeen > TrackExpiry);
        _lastFired.RemoveAll(f => now - f.FiredAt > RepeatMemory);
    }

    public void Reset() { _tracks.Clear(); _lastFired.Clear(); _lastAny = null; }

    /// <summary>
    /// Forget the approaches in progress, and NOTHING else — for a caller that has just been handed
    /// a different catalog INSTANCE (a late OSM answer, a GSX publish). A feature's geometry BASIS
    /// can change across such a rebuild — a stand cluster becomes a building outline — so the next
    /// range a same-identity track sees is a STEP rather than the next sample of an approach: a
    /// premature pass one way, a lost one the other. The fired memory and the global gap are facts
    /// about what the PILOT has just been told and survive: a full <see cref="Reset"/> here would
    /// let a building announced a moment ago be announced again on the very next approach.
    /// </summary>
    public void RebaselineTracks() => _tracks.Clear();
}
