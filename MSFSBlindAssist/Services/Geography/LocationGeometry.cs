using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// Pure geometry over Natural Earth shapes. Coordinates are flat arrays in GeoJSON order,
/// <c>[lon0, lat0, lon1, lat1, …]</c>. Modelled on <see cref="AdvisoryGeometry"/>'s ray cast and
/// local equirectangular nearest-edge search, but extended: polygons have holes, lines are
/// OPEN (a coastline's ends are not joined), and the nearest-point search wraps longitude
/// across ±180°. Natural Earth already splits its shapes at ±180°, so point-in-polygon works on
/// raw longitudes; wrapping there would fold the polygons that span the whole globe.
/// </summary>
internal static class LocationGeometry
{
    /// <summary>A longitude difference folded into (-180, 180].</summary>
    internal static double WrapDelta(double deltaDeg)
    {
        double d = deltaDeg % 360.0;
        if (d > 180.0) d -= 360.0;
        else if (d <= -180.0) d += 360.0;
        return d;
    }

    /// <summary>Ray-cast point-in-ring (the ring may repeat its first vertex at the end).</summary>
    internal static bool InRing(ReadOnlySpan<double> ring, double lat, double lon)
    {
        int n = ring.Length / 2;
        if (n < 3) return false;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = ring[2 * i], yi = ring[2 * i + 1];
            double xj = ring[2 * j], yj = ring[2 * j + 1];
            if ((yi > lat) != (yj > lat) && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Inside the first ring and outside every following ring (holes).</summary>
    internal static bool InPolygon(IReadOnlyList<double[]> rings, double lat, double lon)
    {
        if (rings.Count == 0) return false;
        lon = LocationGrid.NormalizeLon(lon);
        if (!InRing(rings[0], lat, lon)) return false;
        for (int h = 1; h < rings.Count; h++)
            if (InRing(rings[h], lat, lon)) return false;
        return true;
    }

    /// <summary>
    /// The nearest point on a line to a position, found in a local equirectangular frame
    /// centred on the position (longitude wrapped across ±180°). <paramref name="closed"/> adds
    /// the segment from the last vertex back to the first (a lake shore); a coastline is open.
    /// The returned distance is the frame's approximation; callers that compare far candidates
    /// re-rank by <see cref="GreatCircleNm"/> on the returned point.
    /// </summary>
    internal static (double DistanceNm, double Lat, double Lon) NearestOnLine(
        ReadOnlySpan<double> coords, bool closed, double lat, double lon)
    {
        int n = coords.Length / 2;
        if (n == 0) return (double.MaxValue, lat, lon);
        double cosLat = Math.Max(0.01, Math.Cos(lat * Math.PI / 180.0));

        double bestSq = double.MaxValue, bestX = 0, bestY = 0;
        if (n == 1)
        {
            bestX = WrapDelta(coords[0] - lon) * 60.0 * cosLat;
            bestY = (coords[1] - lat) * 60.0;
            bestSq = bestX * bestX + bestY * bestY;
        }
        int segments = closed ? n : n - 1;
        for (int s = 0; s < segments; s++)
        {
            int a = s, b = (s + 1) % n;
            double ax = WrapDelta(coords[2 * a] - lon) * 60.0 * cosLat, ay = (coords[2 * a + 1] - lat) * 60.0;
            // b is framed from a, not from the aircraft: wrapped on its own, a segment across the
            // aircraft's antipodal meridian would span 360° and pass through its own meridian.
            double bx = ax + WrapDelta(coords[2 * b] - coords[2 * a]) * 60.0 * cosLat, by = (coords[2 * b + 1] - lat) * 60.0;
            double dx = bx - ax, dy = by - ay;
            double lenSq = dx * dx + dy * dy;
            double t = lenSq <= 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / lenSq, 0, 1);
            double px = ax + t * dx, py = ay + t * dy;
            double dSq = px * px + py * py;
            if (dSq < bestSq) { bestSq = dSq; bestX = px; bestY = py; }
        }
        double nearLat = Math.Clamp(lat + bestY / 60.0, -90.0, 90.0);
        double nearLon = LocationGrid.NormalizeLon(lon + bestX / (60.0 * cosLat));
        return (Math.Sqrt(bestSq), nearLat, nearLon);
    }

    /// <summary>Great-circle distance in nautical miles (the app's shared haversine).</summary>
    internal static double GreatCircleNm(double lat1, double lon1, double lat2, double lon2) =>
        NavigationCalculator.CalculateDistance(lat1, lon1, lat2, lon2);

    /// <summary>Initial true bearing from the first point to the second, 0-360.</summary>
    internal static double BearingTrue(double lat1, double lon1, double lat2, double lon2) =>
        NavigationCalculator.CalculateBearing(lat1, lon1, lat2, lon2);
}
