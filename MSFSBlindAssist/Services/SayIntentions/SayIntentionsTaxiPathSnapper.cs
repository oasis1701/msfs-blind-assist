namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>One position from SayIntentions' <c>current_flight.taxi_path</c>, which is
/// published as <c>{"heading":…, "point":{"lon":…,"lat":…}}</c> per entry.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>
/// One straight SEGMENT of a named taxiway — not a whole taxiway and not a whole OSM
/// way. Taxiways curve, so a way has to arrive here already split into consecutive
/// point pairs: measuring to the chord across a bend puts the aircraft tens of metres
/// from a taxiway it is standing on.
/// </summary>
/// <param name="TaxiwayName">Must never be blank — nothing here filters an unnamed segment out, so a blank flows straight through into <see cref="SnapResult.Taxiways"/> as an empty-string leg; the producer is responsible for excluding unnamed segments before they reach here.</param>
public sealed record NamedEdge(string TaxiwayName, double FromLat, double FromLon, double ToLat, double ToLon);

/// <summary>
/// The taxiway sequence a taxi path lies along, plus enough counting to tell "the
/// route is short" from "we could not read part of it". <paramref name="UnsnappedCount"/>
/// is points that were beyond every taxiway: normally the lead-in to the stand, which
/// is apron rather than taxiway pavement. <paramref name="DroppedRunCount"/> is
/// different: it is taxiways that WERE on the path — every point on them snapped fine
/// — but too briefly to pass <see cref="SayIntentionsTaxiPathSnapper.MinRunPoints"/>,
/// so they are missing from <paramref name="Taxiways"/> without ever showing up in
/// <paramref name="UnsnappedCount"/>. Without this field a genuinely short leg and a
/// perfectly clean read are indistinguishable to the caller.
/// </summary>
public sealed record SnapResult(IReadOnlyList<string> Taxiways, int PointCount, int UnsnappedCount, int DroppedRunCount);

/// <summary>
/// Turns SayIntentions' taxi_path GEOMETRY into a taxiway sequence, by snapping each
/// published point to the nearest named taxiway segment.
///
/// This exists because deriving the route from the PHRASING of the spoken clearance
/// keeps failing on naming variance — compass words for single-letter taxiways, digits
/// spoken as words, prefixes of other taxiway names. The geometry has none of that:
/// measured against a live LSZH arrival on 2026-07-29, where Zurich Ground cleared
/// "Taxi to Gate E52 via E4, E, C", the path published 9 s later snaps to exactly
/// E4, E, C.
///
/// Pure — no I/O, no UI, no SimConnect. Covered by SayIntentionsTaxiPathSnapperTests.
/// </summary>
public static class SayIntentionsTaxiPathSnapper
{
    /// <summary>
    /// How far a published point may sit from a taxiway segment and still count as
    /// being on it. 25 m is a taxiway half-width plus the slack in OSM centrelines;
    /// on the LSZH capture it accepts every point actually on pavement and rejects
    /// exactly the four that are the turn into the stand. Raising it does not improve
    /// the answer, it only stops those four being REPORTED as unread — the point of
    /// counting them is that a point off pavement must never be hung on whichever
    /// taxiway happens to be nearest.
    ///
    /// That measurement was against OSM centrelines only — the lszh-taxiways.json
    /// fixture this snapper is tested against — while the real caller feeds edges from
    /// TaxiGraph.GetNamedEdges(), which per CLAUDE.md's taxi-data-augmentation invariant
    /// is navdata geometry with OSM names, never OSM geometry. A systematic
    /// navdata-vs-OSM centreline offset would have been invisible to the OSM measurement,
    /// so this constant was re-measured against navdata-sourced edges. THAT IS DONE — it
    /// does not need doing again:
    ///
    /// EGLL, live capture, edges from the built graph. Median nearest-edge distance
    /// 1.53 m, p90 2.31 m, 4 of 68 points unsnapped — those four being the lead-in to the
    /// stand, which is apron, exactly the shape the LSZH/OSM read showed — and the import
    /// reproduced the cleared route exactly. So navdata centrelines sit an order of
    /// magnitude inside this tolerance and 25 m stands unchanged. Raising it would only
    /// stop the stand lead-in being REPORTED as unread, which is the one thing counting
    /// it is for.
    /// </summary>
    internal const double SnapToleranceMetres = 25.0;

    /// <summary>
    /// How many consecutive points a taxiway must hold before it counts as a leg of
    /// the route. SayIntentions' path clips the corners of unnamed connector stubs
    /// ("Link 5", "Link 6", "Inner" at LSZH) that no controller ever says, and each
    /// shows up as a single point. Not a tuned number: 2 and 3 give the same answer
    /// on the real capture.
    ///
    /// The published points are spaced ~28 m apart (measured on the LSZH capture: min
    /// 17.3 m, median 28.0 m, max 28.0 m — SI resamples the path at a fixed step), so
    /// this constant is also, unavoidably, "a taxiway must hold ~28 m of path to be
    /// reported": a genuinely cleared taxiway crossed in under one sample interval
    /// produces a single point and is dropped along with the connector stubs. That
    /// drop is not silent — see <see cref="SnapResult.DroppedRunCount"/> — but it is
    /// still a real leg missing from <see cref="SnapResult.Taxiways"/>. Lowering this
    /// to 1 would recover it, but 1 lets the connector stubs this constant exists to
    /// remove back through, which is worse.
    /// </summary>
    internal const int MinRunPoints = 2;

    /// <summary>
    /// The part of a published track that is still AHEAD of the aircraft: everything
    /// from the point nearest the aircraft onward.
    ///
    /// The track is not always what is left of the route. It was documented as the
    /// REMAINING route, shrinking as the aircraft taxis, on the strength of one live
    /// capture that went 77 → 40 points — and a KDTW capture on 2026-07-31 shows the
    /// other behaviour just as plainly. Holding short of runway 4R, cleared to cross and
    /// continue, the aircraft at 42.20763 N 83.36765 W was published a 124-point path
    /// whose FIRST point sat 1,510 m behind it: 76 of 124 points, 61 %, were pavement
    /// already flown. Snapped whole, that track named A and R — taxiways the aircraft had
    /// left — and with no clearance to check it against (the clearance had been missed
    /// separately, see SayIntentionsClearanceSelector) it became the route.
    ///
    /// So a late press is NOT made safe by the track having shrunk. This is what makes it
    /// safe, and it is why the "late press degrades to the clearance" reasoning in
    /// docs/sayintentions.md no longer stands on its own.
    ///
    /// GUARDED by <see cref="SnapToleranceMetres"/>, the same line this file already
    /// draws between "on pavement" and "not on it". If the nearest published point is
    /// farther than that, the aircraft is not on the published track at all — it has been
    /// towed, repositioned, or the track is for somewhere else — and NOTHING here can say
    /// which part of it is behind. In that case the path is handed back untouched: a
    /// wrong trim silently deletes legs the pilot was cleared for, where no trim at worst
    /// leaves the old behaviour.
    ///
    /// An exact tie breaks toward the EARLIER index, so a route that doubles back past
    /// the aircraft keeps the whole of the second pass rather than starting at it.
    ///
    /// Nothing about a trim is announced. A route that starts where the aircraft is
    /// standing is the expected answer, not a warning.
    /// </summary>
    public static IReadOnlyList<GeoPoint> TrimToPointsAhead(
        IReadOnlyList<GeoPoint> path, double aircraftLatitude, double aircraftLongitude)
    {
        if (path is null || path.Count == 0) return path ?? (IReadOnlyList<GeoPoint>)Array.Empty<GeoPoint>();

        int nearest = 0;
        double nearestMetres = double.MaxValue;

        for (int i = 0; i < path.Count; i++)
        {
            // Strict "<" is the tie-break: the first of two equidistant points wins.
            double metres = PointToPointMetres(
                aircraftLatitude, aircraftLongitude, path[i].Latitude, path[i].Longitude);
            if (metres < nearestMetres)
            {
                nearestMetres = metres;
                nearest = i;
            }
        }

        if (nearest == 0 || nearestMetres > SnapToleranceMetres) return path;

        var ahead = new List<GeoPoint>(path.Count - nearest);
        for (int i = nearest; i < path.Count; i++) ahead.Add(path[i]);
        return ahead;
    }

    /// <summary>
    /// The taxiways <paramref name="path"/> runs along, in order. Empty in, empty out —
    /// a missing or unreadable path degrades to "nothing to say", never an exception,
    /// because the caller is a hotkey a blind pilot presses mid-taxi.
    /// </summary>
    public static SnapResult Snap(IReadOnlyList<GeoPoint> path, IReadOnlyList<NamedEdge> edges)
    {
        if (path is null || path.Count == 0)
        {
            return new SnapResult(Array.Empty<string>(), 0, 0, 0);
        }

        var candidates = edges ?? Array.Empty<NamedEdge>();

        // 1. Snap every point to its nearest named edge. Beyond the tolerance it snaps
        //    to nothing and is counted instead of guessed.
        var perPoint = new string?[path.Count];
        int unsnappedCount = 0;

        for (int i = 0; i < path.Count; i++)
        {
            string? nearestName = null;
            double nearestMetres = double.MaxValue;

            // Linear over every segment, and NOT free: measured 20-90 ms per call —
            // a 111-point capture against EGLL's 5,189 named edges is ~576k point-segment
            // evaluations, about 40 ms. That is several frames, and it runs SYNCHRONOUSLY
            // ON THE UI THREAD from the Ctrl+Shift+Y handler. It is acceptable only
            // because it happens ONCE per import, while the aircraft is standing still,
            // inside an operation that has already spent seconds on HTTP. Do not move
            // this onto a per-frame path, and do not assume it is cheap: if it ever needs
            // to run repeatedly, a spatial index (or a bounding-box reject before the
            // segment math) comes first.
            foreach (var edge in candidates)
            {
                double metres = PointToSegmentMetres(
                    path[i].Latitude, path[i].Longitude,
                    edge.FromLat, edge.FromLon, edge.ToLat, edge.ToLon);

                if (metres < nearestMetres)
                {
                    nearestMetres = metres;
                    nearestName = edge.TaxiwayName;
                }
            }

            if (nearestName is null || nearestMetres > SnapToleranceMetres)
            {
                perPoint[i] = null;
                unsnappedCount++;
            }
            else
            {
                perPoint[i] = nearestName;
            }
        }

        // 2. Run-lengths over the RAW per-point sequence, nulls included. A null has to
        //    break a run rather than be skipped over, or two lone points either side of
        //    a gap in the data merge into a run long enough to be reported as a leg.
        var runs = new List<(string? Name, int Length)>();
        foreach (string? name in perPoint)
        {
            if (runs.Count > 0
                && string.Equals(runs[^1].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                // Preserve the first graph spelling as the stable canonical label. A
                // later case variant (`d` after `D` at CYVR) extends the same run; it
                // must not make the displayed name depend on the final sample.
                runs[^1] = (runs[^1].Name, runs[^1].Length + 1);
            }
            else
            {
                runs.Add((name, 1));
            }
        }

        var taxiways = new List<string>();
        int droppedRunCount = 0;
        foreach ((string? name, int length) in runs)
        {
            // 3. Drop the connector stubs. This MUST happen before the collapse below:
            //    collapsing first turns every run into length 1 and there is nothing
            //    left to filter on, so every stub survives.
            if (name is null)
            {
                // Already reflected in unsnappedCount above — this is a miss, not a
                // taxiway that was seen and then dropped, so it must not also inflate
                // droppedRunCount.
                continue;
            }

            if (length < MinRunPoints)
            {
                // Unlike a null run, every point here genuinely snapped to `name` — it
                // just did not hold for long enough to be reported. That is a
                // different failure than "could not read part of it", so it gets its
                // own count instead of silently vanishing (see SnapResult.DroppedRunCount).
                droppedRunCount++;
                continue;
            }

            // 4. Collapse consecutive duplicates — and only consecutive ones. A route
            //    that leaves a taxiway and comes back to it later names it twice.
            if (taxiways.Count > 0
                && string.Equals(taxiways[^1], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            taxiways.Add(name);
        }

        return new SnapResult(taxiways, path.Count, unsnappedCount, droppedRunCount);
    }

    /// <summary>Distance between two points, in metres. A degenerate segment through
    /// <see cref="PointToSegmentMetres"/>, so the trim and the snap measure the airport
    /// in exactly the same projection and can never disagree about the 25 m line.</summary>
    private static double PointToPointMetres(double lat, double lon, double toLat, double toLon) =>
        PointToSegmentMetres(lat, lon, toLat, toLon, toLat, toLon);

    /// <summary>
    /// Distance from a point to a segment, in metres, via equirectangular projection
    /// about the segment's midpoint. Correct to well under a metre at airport scale
    /// (≤5 km) and much cheaper than haversine per point-edge pair, of which there are
    /// tens of thousands per path.
    /// </summary>
    internal static double PointToSegmentMetres(
        double lat, double lon, double aLat, double aLon, double bLat, double bLon)
    {
        const double MetresPerDegreeLatitude = 111320.0;

        double midLatitude = (aLat + bLat) / 2.0;
        double metresPerDegreeLongitude = MetresPerDegreeLatitude * Math.Cos(midLatitude * Math.PI / 180.0);

        // Local metric frame with the segment's first node at the origin.
        double pointX = (lon - aLon) * metresPerDegreeLongitude;
        double pointY = (lat - aLat) * MetresPerDegreeLatitude;
        double segmentX = (bLon - aLon) * metresPerDegreeLongitude;
        double segmentY = (bLat - aLat) * MetresPerDegreeLatitude;

        double lengthSquared = (segmentX * segmentX) + (segmentY * segmentY);
        if (lengthSquared <= 0.0)
        {
            // Duplicate consecutive nodes are real in OSM ways: measure to the point.
            return Math.Sqrt((pointX * pointX) + (pointY * pointY));
        }

        double t = ((pointX * segmentX) + (pointY * segmentY)) / lengthSquared;

        // Clamping to the segment is load-bearing, not tidiness: unclamped this
        // measures to the segment's INFINITE line, so a point far past the end of a
        // short stub reads as sitting on it and that stub wins over the taxiway the
        // aircraft is really on.
        t = Math.Clamp(t, 0.0, 1.0);

        double deltaX = pointX - (segmentX * t);
        double deltaY = pointY - (segmentY * t);
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
