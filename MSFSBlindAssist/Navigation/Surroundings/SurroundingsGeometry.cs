using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Navigation.Surroundings;

public static class SurroundingsGeometry
{
    public readonly record struct NearestResult(double Metres, double BearingTrueDeg);

    /// <summary>
    /// Distance AND bearing to the same point: the nearest edge of a footprint, the nearest member
    /// stand, else the representative point. Rank used to pair distance-to-the-wall with
    /// bearing-to-the-roof-centroid, so a pier 60 m to the left read "ahead, 60 metres".
    /// Inside a footprint the distance is 0 and the bearing is to the centroid.
    /// </summary>
    public static NearestResult Nearest(double lat, double lon, AirportFeature f)
    {
        var fp = f.Footprint;
        if (fp != null && fp.Count >= 3)
            return Contains(fp, lat, lon)
                ? new NearestResult(0.0, TaxiGeo.BearingDeg(lat, lon, f.Lat, f.Lon))
                : NearestOnRing(lat, lon, fp);

        var members = f.Members;
        if (members != null && members.Count > 0)
        {
            double best = double.MaxValue; LatLon at = members[0];
            foreach (var m in members)
            {
                double d = TaxiGeo.HaversineMeters(lat, lon, m.Lat, m.Lon);
                if (d < best) { best = d; at = m; }
            }
            return new NearestResult(best, TaxiGeo.BearingDeg(lat, lon, at.Lat, at.Lon));
        }
        return new NearestResult(TaxiGeo.HaversineMeters(lat, lon, f.Lat, f.Lon), TaxiGeo.BearingDeg(lat, lon, f.Lat, f.Lon));
    }

    private static NearestResult NearestOnRing(double lat, double lon, IReadOnlyList<LatLon> ring)
    {
        // Local equirectangular metres centred on the aircraft — exact enough at airport scale.
        const double MetresPerDegLat = 111_320.0;
        double metresPerDegLon = MetresPerDegLat * Math.Cos(lat * Math.PI / 180.0);
        double bestSq = double.MaxValue, bestX = 0, bestY = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Count];
            double ax = (a.Lon - lon) * metresPerDegLon, ay = (a.Lat - lat) * MetresPerDegLat;
            double bx = (b.Lon - lon) * metresPerDegLon, by = (b.Lat - lat) * MetresPerDegLat;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = len2 <= 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / len2, 0.0, 1.0);
            double px = ax + t * dx, py = ay + t * dy, sq = px * px + py * py;
            if (sq < bestSq) { bestSq = sq; bestX = px; bestY = py; }
        }
        double bearing = (Math.Atan2(bestX, bestY) * 180.0 / Math.PI + 360.0) % 360.0;
        return new NearestResult(Math.Sqrt(bestSq), bearing);
    }

    /// <summary>The distance half of <see cref="Nearest"/> — 0 inside a footprint, else the nearest
    /// polygon edge or nearest member stand, else plain haversine to the representative point.</summary>
    public static double DistanceMetres(double lat, double lon, AirportFeature f) => Nearest(lat, lon, f).Metres;

    /// <summary>Ray-casting point-in-polygon on raw degrees (fine at airport scale; no antimeridian airports).</summary>
    public static bool Contains(IReadOnlyList<LatLon> polygon, double lat, double lon)
    {
        if (polygon == null || polygon.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            double yi = polygon[i].Lat, xi = polygon[i].Lon;
            double yj = polygon[j].Lat, xj = polygon[j].Lon;
            bool crosses = (yi > lat) != (yj > lat);
            if (!crosses) continue;
            double xAt = xj + (lat - yj) * (xi - xj) / (yi - yj);
            if (lon < xAt) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// Is the point INSIDE the polygon by more than <paramref name="marginMetres"/> — past its edge,
    /// not on it? A point ON the boundary (a node two outlines share) is neither in nor out to the
    /// ray cast in <see cref="Contains"/>, which answers it arbitrarily; this never counts it.
    /// </summary>
    public static bool ContainsBeyondEdge(IReadOnlyList<LatLon> polygon, double lat, double lon, double marginMetres)
        => Contains(polygon, lat, lon) && NearestOnRing(lat, lon, polygon).Metres > marginMetres;

    /// <summary>Signed relative bearing, -180..180: negative = left of the nose.</summary>
    public static double RelativeBearingDeg(double ownLat, double ownLon, double ownHeadingTrue, double lat, double lon)
        => RelativeBearingDeg(TaxiGeo.BearingDeg(ownLat, ownLon, lat, lon), ownHeadingTrue);

    /// <summary>Signed relative bearing, -180..180: negative = left of the nose.</summary>
    public static double RelativeBearingDeg(double bearingTrueDeg, double ownHeadingTrue)
        => ((bearingTrueDeg - ownHeadingTrue) % 360.0 + 540.0) % 360.0 - 180.0;

    public static LatLon Centroid(IReadOnlyList<LatLon> pts)
    {
        if (pts == null || pts.Count == 0) return new LatLon(0, 0);
        double lat = 0, lon = 0;
        foreach (var p in pts) { lat += p.Lat; lon += p.Lon; }
        return new LatLon(lat / pts.Count, lon / pts.Count);
    }

    /// <summary>Single-linkage clusters: two items are together when a chain of items no further
    /// apart than linkMetres joins them. One contiguous ramp or pier is ONE cluster however long it
    /// is — greedy "within 60 m of the running centroid" cut a single ramp into dozens.</summary>
    public static List<List<T>> SingleLinkage<T>(IReadOnlyList<T> items, Func<T, LatLon> position, double linkMetres)
    {
        int n = items.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        var pos = items.Select(position).ToArray();
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Find(i) != Find(j) && TaxiGeo.HaversineMeters(pos[i].Lat, pos[i].Lon, pos[j].Lat, pos[j].Lon) <= linkMetres)
                    parent[Find(i)] = Find(j);
        return Enumerable.Range(0, n).GroupBy(Find).Select(g => g.Select(i => items[i]).ToList()).ToList();
    }
}
