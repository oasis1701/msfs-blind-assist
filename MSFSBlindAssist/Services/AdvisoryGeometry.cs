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
    /// ignored. The first coordinate pair after WI is accepted unconditionally; subsequent
    /// pairs are chained by separators (whitespace, dashes) only — the first wordy gap ends
    /// collection (VA/TC bodies routinely carry a SECOND coordinate group after the polygon,
    /// "FCST AT 1750Z …" or "TC CENTRE PSN …", which does not belong to THIS ring).
    /// Null when there is no WI token or fewer than 3 vertices after dropping a duplicated
    /// closing vertex (polygon closure) — interior duplicate vertices are not deduped;
    /// harmless downstream (ray-cast and nearest-vertex math both tolerate a repeated point).</summary>
    internal static List<(double Lat, double Lon)>? ParseWiPolygon(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var wi = Regex.Match(body, @"\bWI\b");
        if (!wi.Success) return null;

        var verts = new List<(double Lat, double Lon)>();
        int prevEnd = -1;
        foreach (Match m in CoordPair.Matches(body, wi.Index))
        {
            // VA/TC bodies routinely carry a SECOND coordinate group after the polygon
            // ("FCST AT 1750Z … WI …", "TC CENTRE PSN …"). Pairs belong to THIS ring only
            // while chained by separators (whitespace/dashes); the first wordy gap ends it.
            if (prevEnd >= 0 && !Regex.IsMatch(body[prevEnd..m.Index], @"^[\s\-]*$"))
                break;
            double lat = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                + int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) / 60.0;
            if (m.Groups[1].Value == "S") lat = -lat;
            double lon = int.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture)
                + int.Parse(m.Groups[6].Value, System.Globalization.CultureInfo.InvariantCulture) / 60.0;
            if (m.Groups[4].Value == "W") lon = -lon;
            verts.Add((lat, lon));
            prevEnd = m.Index + m.Length;
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
    /// so the two boxes normally agree about one advisory's distance. Caveat: tier-2
    /// geometry only ever supplies the FIRST ring of a MultiPolygon feature, while
    /// the Nearby Advisories box scans every ring — a rare multi-area advisory can
    /// therefore show a different nearest-vertex distance in the two boxes (recorded
    /// follow-up, not fixed here).</summary>
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
