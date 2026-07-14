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

    /// <summary>Distance (nm) and true bearing to the nearest point on the polygon's
    /// BOUNDARY — point-to-segment over every edge, in a local equirectangular frame
    /// centred on the aircraft (adequate at SIGMET scales; poles/antimeridian remain
    /// non-goals). This is edge-true: convective outlook polygons have edges long
    /// enough for vertex distance to be off by tens of nm, and the 100 nm approach
    /// trigger IS the distance (spec 2026-07-14 §3). (The nearest-vertex-only sibling
    /// method this replaced was deleted as dead code — the separate Nearby Advisories
    /// box has always used its own independent vertex scan in
    /// WeatherService.ClosestPointGeometry, not this class.)</summary>
    internal static (double DistanceNm, double BearingTrueDeg) NearestEdge(
        IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)
    {
        if (vertices.Count == 0) return (double.MaxValue, 0);
        double cosLat = Math.Max(0.01, Math.Cos(lat * Math.PI / 180.0));
        // Local nm coordinates relative to the aircraft: 1° lat = 60 nm, 1° lon = 60·cos(lat) nm.
        (double X, double Y) Project((double Lat, double Lon) p)
            => ((p.Lon - lon) * 60.0 * cosLat, (p.Lat - lat) * 60.0);

        double bestSq = double.MaxValue;
        (double X, double Y) bestPt = default;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            var a = Project(vertices[j]);
            var b = Project(vertices[i]);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            double t = lenSq <= 0 ? 0 : Math.Clamp((-(a.X) * dx + -(a.Y) * dy) / lenSq, 0, 1);
            (double X, double Y) p = (a.X + t * dx, a.Y + t * dy);
            double dSq = p.X * p.X + p.Y * p.Y;
            if (dSq < bestSq) { bestSq = dSq; bestPt = p; }
        }
        double nearLat = lat + bestPt.Y / 60.0;
        double nearLon = lon + bestPt.X / (60.0 * cosLat);
        return (Math.Sqrt(bestSq),
            Navigation.NavigationCalculator.CalculateBearing(lat, lon, nearLat, nearLon));
    }

    /// <summary>|relative bearing| &gt; 90° = behind. Binary by design (spec §5).</summary>
    internal static bool IsBehind(double bearingToDeg, double trueHeadingDeg)
    {
        double rel = ((bearingToDeg - trueHeadingDeg) % 360 + 540) % 360 - 180;
        return Math.Abs(rel) > 90;
    }
}
