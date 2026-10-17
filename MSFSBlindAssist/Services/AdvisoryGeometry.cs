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

    /// <summary>Any position after the ring, in any ICAO form: degrees only ("N15 W070") as
    /// well as degrees and minutes. Only decides <c>wholeShape</c>; the ring itself still needs
    /// <see cref="CoordPair"/>.</summary>
    private static readonly Regex AnyPosition = new(
        @"\b[NS]\d{2,4}\s*[EW]\d{3,5}\b", RegexOptions.Compiled);

    /// <summary>"WI 120NM WID LINE BTN …" (or KM) straight after the WI token: the corridor's
    /// full width.</summary>
    private static readonly Regex WidLine = new(
        @"\G\s*(\d+)\s*(NM|KM)\s+WID\s+LINE\b", RegexOptions.Compiled);

    /// <summary>Extracts the "WI lat lon - lat lon - …" polygon from an ICAO-style
    /// advisory body. Coordinates BEFORE the WI token (e.g. a VA SIGMET's PSN) are
    /// ignored. The first coordinate pair after WI is accepted unconditionally; subsequent
    /// pairs are chained by separators (whitespace, dashes) only — the first wordy gap ends
    /// collection (VA/TC bodies routinely carry a SECOND coordinate group after the polygon,
    /// "FCST AT 1750Z …" or "TC CENTRE PSN …", which does not belong to THIS ring).
    /// A "WI nnNM WID LINE BTN …" body returns the corridor's outline, the line buffered by
    /// half its width (<see cref="CorridorOutline"/>), never the line points themselves.
    /// Null when there is no WI token or fewer than 3 points after dropping a duplicated
    /// closing vertex (polygon closure) — interior duplicate vertices are not deduped;
    /// harmless downstream (ray-cast and nearest-vertex math both tolerate a repeated point).</summary>
    internal static List<(double Lat, double Lon)>? ParseWiPolygon(string body) => ParseWiPolygon(body, out _);

    /// <summary><see cref="ParseWiPolygon(string)"/>, also saying whether the ring is the body's
    /// WHOLE shape: true only when no position follows it (<see cref="AnyPosition"/>). A VA/TC
    /// forecast group, a TC center position or a second area ("AND WI …") after the ring means
    /// the advisory may cover more than the ring, so a position outside the ring is not proof of
    /// being outside the advisory (RouteAdvisoryLocator.BuildFact). False whenever the result is
    /// null.</summary>
    internal static List<(double Lat, double Lon)>? ParseWiPolygon(string body, out bool wholeShape)
    {
        wholeShape = false;
        if (string.IsNullOrEmpty(body)) return null;
        var wi = Regex.Match(body, @"\bWI\b");
        if (!wi.Success) return null;
        var wid = WidLine.Match(body, wi.Index + wi.Length);

        var verts = new List<(double Lat, double Lon)>();
        int prevEnd = -1;
        bool trailingGroup = false;
        foreach (Match m in CoordPair.Matches(body, wi.Index))
        {
            // VA/TC bodies routinely carry a SECOND coordinate group after the polygon
            // ("FCST AT 1750Z … WI …", "TC CENTRE PSN …"). Pairs belong to THIS ring only
            // while chained by separators (whitespace/dashes); the first wordy gap ends it.
            if (prevEnd >= 0 && !Regex.IsMatch(body[prevEnd..m.Index], @"^[\s\-]*$"))
            {
                trailingGroup = true;
                break;
            }
            double lat = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                + int.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) / 60.0;
            if (m.Groups[1].Value == "S") lat = -lat;
            double lon = int.Parse(m.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture)
                + int.Parse(m.Groups[6].Value, System.Globalization.CultureInfo.InvariantCulture) / 60.0;
            if (m.Groups[4].Value == "W") lon = -lon;
            verts.Add((lat, lon));
            prevEnd = m.Index + m.Length;
        }
        if (!trailingGroup && prevEnd >= 0) trailingGroup = AnyPosition.IsMatch(body, prevEnd);
        if (!wid.Success && verts.Count > 1 && verts[0] == verts[^1]) verts.RemoveAt(verts.Count - 1);
        if (verts.Count < 3) return null;
        if (wid.Success)
        {
            double widthNm = int.Parse(wid.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                / (wid.Groups[2].Value == "KM" ? 1.852 : 1.0);
            var outline = CorridorOutline(verts, widthNm / 2);
            if (outline == null) return null;
            verts = outline;
        }
        wholeShape = !trailingGroup;
        return verts;
    }

    /// <summary>The outline of a corridor: <paramref name="line"/> buffered by
    /// <paramref name="halfWidthNm"/> on each side, square-capped at both ends, mitered at each
    /// bend (the miter capped at twice the half-width). Built in a flat frame at the line's
    /// mean latitude, adequate at SIGMET scales. Null when fewer than 2 distinct points.</summary>
    internal static List<(double Lat, double Lon)>? CorridorOutline(
        IReadOnlyList<(double Lat, double Lon)> line, double halfWidthNm)
    {
        double k = 60.0 * Math.Max(0.01, Math.Cos(line.Average(p => p.Lat) * Math.PI / 180.0));
        var pts = new List<(double X, double Y)>();
        foreach (var p in line)
        {
            (double X, double Y) q = (p.Lon * k, p.Lat * 60.0);
            if (pts.Count == 0 || pts[^1] != q) pts.Add(q);
        }
        if (pts.Count < 2) return null;

        // Unit direction of each segment; its left normal is (-Y, X).
        var dir = new (double X, double Y)[pts.Count - 1];
        for (int i = 0; i < dir.Length; i++)
        {
            double dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            dir[i] = (dx / len, dy / len);
        }
        var left = new List<(double X, double Y)>();
        var right = new List<(double X, double Y)>();
        for (int i = 0; i < pts.Count; i++)
        {
            var (px, py) = pts[i];
            (double X, double Y) n, along = (0, 0);
            double scale = halfWidthNm;
            if (i == 0) { n = (-dir[0].Y, dir[0].X); along = (-dir[0].X * halfWidthNm, -dir[0].Y * halfWidthNm); }
            else if (i == pts.Count - 1)
            {
                var d = dir[^1];
                n = (-d.Y, d.X);
                along = (d.X * halfWidthNm, d.Y * halfWidthNm);
            }
            else
            {
                (double X, double Y) n1 = (-dir[i - 1].Y, dir[i - 1].X), n2 = (-dir[i].Y, dir[i].X);
                double mx = n1.X + n2.X, my = n1.Y + n2.Y, mlen = Math.Sqrt(mx * mx + my * my);
                n = mlen < 1e-9 ? n1 : (mx / mlen, my / mlen);
                scale = halfWidthNm / Math.Max(0.5, n.X * n1.X + n.Y * n1.Y);
            }
            left.Add((px + along.X + n.X * scale, py + along.Y + n.Y * scale));
            right.Add((px + along.X - n.X * scale, py + along.Y - n.Y * scale));
        }
        right.Reverse();
        return left.Concat(right).Select(q => (q.Y / 60.0, q.X / k)).ToList();
    }

    /// <summary>True when the ring crosses the 180° meridian (an edge spanning more than 180° of
    /// longitude, or a longitude beyond ±180°). Plain lat/lon <see cref="IsInside"/> and
    /// <see cref="NearestEdge"/> cannot measure such a ring, so RouteAdvisoryLocator.BuildFact never
    /// lets one overrule the positional probe.</summary>
    internal static bool CrossesAntimeridian(IReadOnlyList<(double Lat, double Lon)> vertices)
    {
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
            if (Math.Abs(vertices[i].Lon) > 180 || Math.Abs(vertices[i].Lon - vertices[j].Lon) > 180)
                return true;
        return false;
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
