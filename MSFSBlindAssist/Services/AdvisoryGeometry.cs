using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure geometry for route-advisory location context (spec 2026-07-13 §5-6).
/// No I/O; fully characterization-tested (AdvisoryGeometryTests).
/// </summary>
internal static class AdvisoryGeometry
{
    /// <summary>[NS]ddmm [EW]dddmm degrees+minutes pair, tolerant of the live
    /// captures' spacing quirks ("W09304- N1127", double spaces).</summary>
    private static readonly Regex CoordPair = new(
        @"\b([NS])(\d{2})(\d{2})\s+([EW])(\d{3})(\d{2})\b", RegexOptions.Compiled);

    /// <summary>Extracts the "WI lat lon - lat lon - …" polygon from an ICAO-style
    /// advisory body. Coordinates BEFORE the WI token (e.g. a VA SIGMET's PSN) are
    /// ignored. Null when there is no WI token or fewer than 3 distinct vertices;
    /// a duplicated closing vertex (polygon closure) is dropped.</summary>
    internal static List<(double Lat, double Lon)>? ParseWiPolygon(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var wi = Regex.Match(body, @"\bWI\b");
        if (!wi.Success) return null;

        var verts = new List<(double Lat, double Lon)>();
        foreach (Match m in CoordPair.Matches(body, wi.Index))
        {
            double lat = int.Parse(m.Groups[2].Value) + int.Parse(m.Groups[3].Value) / 60.0;
            if (m.Groups[1].Value == "S") lat = -lat;
            double lon = int.Parse(m.Groups[5].Value) + int.Parse(m.Groups[6].Value) / 60.0;
            if (m.Groups[4].Value == "W") lon = -lon;
            verts.Add((lat, lon));
        }
        if (verts.Count > 1 && verts[0] == verts[^1]) verts.RemoveAt(verts.Count - 1);
        return verts.Count >= 3 ? verts : null;
    }

    /// <summary>Ray-cast point-in-polygon on plain lat/lon (adequate at SIGMET
    /// scales; antimeridian-spanning polygons are a documented non-goal).</summary>
    internal static bool IsInside(IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)
    {
        if (vertices.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            (double yi, double xi) = (vertices[i].Lat, vertices[i].Lon);
            (double yj, double xj) = (vertices[j].Lat, vertices[j].Lon);
            if ((yi > lat) != (yj > lat)
                && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Distance/bearing to the nearest VERTEX — deliberately the same
    /// approximation the Nearby Advisories box uses (WeatherService.ClosestPoint),
    /// so the two boxes never disagree about one advisory's distance.</summary>
    internal static (double DistanceNm, double BearingTrueDeg) NearestVertex(
        IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)
    {
        double bestDist = double.MaxValue, bestBrg = 0;
        foreach (var (vLat, vLon) in vertices)
        {
            double d = Navigation.NavigationCalculator.CalculateDistance(lat, lon, vLat, vLon);
            if (d < bestDist)
            {
                bestDist = d;
                bestBrg = Navigation.NavigationCalculator.CalculateBearing(lat, lon, vLat, vLon);
            }
        }
        return (bestDist, bestBrg);
    }

    /// <summary>|relative bearing| &gt; 90° = behind. Binary by design (spec §5).</summary>
    internal static bool IsBehind(double bearingToDeg, double trueHeadingDeg)
    {
        double rel = ((bearingToDeg - trueHeadingDeg) % 360 + 540) % 360 - 180;
        return Math.Abs(rel) > 90;
    }
}
