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
}
