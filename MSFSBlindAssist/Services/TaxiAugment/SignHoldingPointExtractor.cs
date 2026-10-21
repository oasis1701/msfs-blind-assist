using System.Text;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// One named holding point read off X-Plane's red mandatory SIGNS (apt.dat row 20), as a
/// FALLBACK for airports whose painted hold lines carry no name anywhere else. OMDB is the case
/// this exists for: ATC routinely says "hold KK", its 235 OSM hold lines carry no ref at all, and
/// the scenery's navdata has no KK — but X-Plane has a red "KK" sign beside taxiway K.
/// <para><see cref="Lat"/>/<see cref="Lon"/> are the centroid of every same-named sign in the
/// cluster (signs usually stand on both sides of the taxiway, so the centroid lands near its
/// centreline). <see cref="Taxiway"/> is the X-Plane taxiway the signs stand beside, found in
/// X-Plane's OWN frame — the sign and that taxiway share X-Plane's geometry, so the association
/// is sound even though X-Plane sits ~20 m off MSFS scenery. The resolver then places the point on
/// the navdata taxiway of that NAME, never on whatever pavement is nearest.</para>
/// </summary>
public sealed record SignHoldingPoint(string Name, double Lat, double Lon, string Taxiway, int SignCount);

/// <summary>
/// Pure extraction of <see cref="SignHoldingPoint"/>s from apt.dat sign rows. Only RED text that
/// looks like a holding-point designator is kept; any sign that also carries a runway designator
/// in red (a runway holding position — "A1 | 27R-09L") is dropped whole, because where a pilot
/// holds before a runway stays navdata-authoritative and is never taken from a sign.
/// </summary>
public static class SignHoldingPointExtractor
{
    /// <summary>A sign further than this from every X-Plane taxiway edge names no taxiway.</summary>
    public const double MaxSignToTaxiwayM = 60.0;

    /// <summary>Same-named signs within this of each other describe one holding point.</summary>
    public const double ClusterRadiusM = 150.0;

    // Red words that describe a kind of sign, never a holding point's name.
    private static readonly HashSet<string> NonNameWords = new(StringComparer.Ordinal)
    {
        "ILS", "LS", "CAT", "HOLD", "MIL", "INST", "STOP", "LAHSO", "NO", "ENTRY", "CLOSED",
        "CHECK", "MILITARY", "APCH", "APPR", "RWY", "RW", "RUNWAY", "GP", "GS", "LOC", "CONTACT",
        "ATC", "AHEAD", "TWY",
    };

    /// <summary>
    /// The holding-point names a sign's markup carries, or empty. Markup is X-Plane's apt.dat
    /// sign syntax: <c>{@R}</c> red, <c>{@L}</c> location, <c>{@Y}</c> direction, <c>{@B}</c>
    /// distance-remaining, <c>{@@}</c> switches to the back face, other braces are glyphs
    /// (arrows), <c>|</c> is a divider.
    /// </summary>
    public static IReadOnlyList<string> RedSignNames(string? markup)
    {
        var red = RedSegments(markup ?? "");
        var names = new List<string>();
        foreach (var raw in red)
        {
            // Underscore is a space in sign text ("CAT_II").
            string t = raw.Replace('_', ' ').Trim().ToUpperInvariant();
            if (t.Length == 0) continue;
            if (IsRunwaySignText(t)) return Array.Empty<string>();   // runway holding position
            if (!IsNameShaped(t)) continue;
            if (!names.Contains(t)) names.Add(t);
        }
        return names;
    }

    // A red segment that means "this is a runway holding position": starts with a digit
    // ("27R-09L", "12L"), a runway word followed by a designator ("RWY 31L", "RWY11", "RW13"),
    // or anything the shared hold-label filters call a designator/descriptive label.
    private static bool IsRunwaySignText(string t)
    {
        if (char.IsDigit(t[0])) return true;
        // "O5L" — a letter-O typo for a zero, seen in the dataset beside real runway signs.
        if (t.Length >= 2 && t[0] == 'O' && char.IsDigit(t[1])) return true;
        string compact = t.Replace(" ", "");
        if ((compact.StartsWith("RWY", StringComparison.Ordinal) && compact.Length > 3 && char.IsDigit(compact[3]))
            || (compact.StartsWith("RW", StringComparison.Ordinal) && compact.Length > 2 && char.IsDigit(compact[2]))
            || (compact.StartsWith("RUNWAY", StringComparison.Ordinal) && compact.Length > 6 && char.IsDigit(compact[6])))
            return true;
        foreach (var w in t.Split(new[] { ' ', '/', '-' }, StringSplitOptions.RemoveEmptyEntries))
            if (TaxiGraph.IsRunwayDesignatorLabel(w)) return true;
        return false;
    }

    // A holding-point designator: one word, a letter first, letters/digits only, ≤ 6 characters
    // (KK, N2E, VIKAS), and not a word describing the sign.
    private static bool IsNameShaped(string t)
    {
        if (t.Length > 6 || !char.IsLetter(t[0])) return false;
        foreach (char c in t)
            if (!char.IsLetterOrDigit(c) || c > 'z') return false;
        if (NonNameWords.Contains(t)) return false;
        if (TaxiGraph.IsDescriptiveHoldLabel(t)) return false;
        return true;
    }

    private static List<string> RedSegments(string markup)
    {
        var result = new List<string>();
        char? colour = null;
        var cur = new StringBuilder();

        void Flush()
        {
            if (colour == 'R' && cur.ToString().Trim().Length > 0) result.Add(cur.ToString());
            cur.Clear();
        }

        for (int i = 0; i < markup.Length; i++)
        {
            char ch = markup[i];
            if (ch == '{')
            {
                int end = markup.IndexOf('}', i);
                if (end < 0) end = markup.Length;
                foreach (var tok in markup.Substring(i + 1, end - i - 1).Split(','))
                {
                    string d = tok.Trim();
                    Flush();
                    if (d.Length == 2 && d[0] == '@' && "YRLB".Contains(d[1])) colour = d[1];
                    else if (d == "@@") colour = null;
                }
                i = end;
                continue;
            }
            if (ch == '|') { Flush(); continue; }
            cur.Append(ch);
        }
        Flush();
        return result;
    }

    /// <summary>
    /// Clusters the name signs and ties each cluster to the X-Plane taxiway it stands beside.
    /// A name whose signs form MORE than one cluster is dropped (two different places share the
    /// name, so neither can be trusted), as is a cluster no X-Plane taxiway lies within
    /// <see cref="MaxSignToTaxiwayM"/> of. The taxiway is the one nearest the most signs in the
    /// cluster, nearest-summed distance breaking a tie.
    /// </summary>
    public static List<SignHoldingPoint> Extract(
        IEnumerable<(double Lat, double Lon, string Markup)> signs,
        IReadOnlyList<NamedTaxiSegment> xplaneTaxiways)
    {
        var byName = new Dictionary<string, List<(double Lat, double Lon)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (lat, lon, markup) in signs)
            foreach (var name in RedSignNames(markup))
            {
                if (!byName.TryGetValue(name, out var list)) byName[name] = list = new();
                list.Add((lat, lon));
            }

        var result = new List<SignHoldingPoint>();
        foreach (var (name, pts) in byName)
        {
            var clusters = Cluster(pts);
            if (clusters.Count != 1) continue;
            var cluster = clusters[0];

            var votes = new Dictionary<string, (int Count, double Dist)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (lat, lon) in cluster)
            {
                string? best = null; double bestD = MaxSignToTaxiwayM;
                foreach (var seg in xplaneTaxiways)
                {
                    double d = PerpDistanceMeters(lat, lon, seg);
                    if (d < bestD) { bestD = d; best = seg.Name; }
                }
                if (best == null) continue;
                string key = best.Trim();
                votes[key] = votes.TryGetValue(key, out var v) ? (v.Count + 1, v.Dist + bestD) : (1, bestD);
            }
            if (votes.Count == 0) continue;
            string taxiway = votes.OrderByDescending(kv => kv.Value.Count)
                                  .ThenBy(kv => kv.Value.Dist)
                                  .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                                  .First().Key;

            result.Add(new SignHoldingPoint(
                name,
                cluster.Average(p => p.Lat),
                cluster.Average(p => p.Lon),
                taxiway,
                cluster.Count));
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Single-link clustering at ClusterRadiusM. A handful of signs per name, so O(n²) is fine.
    private static List<List<(double Lat, double Lon)>> Cluster(List<(double Lat, double Lon)> pts)
    {
        var clusters = new List<List<(double Lat, double Lon)>>();
        foreach (var p in pts)
        {
            var joined = clusters.Where(c => c.Any(q =>
                TaxiGraph.FastDistanceMeters(p.Lat, p.Lon, q.Lat, q.Lon) <= ClusterRadiusM)).ToList();
            if (joined.Count == 0) { clusters.Add(new() { p }); continue; }
            var merged = joined[0];
            merged.Add(p);
            foreach (var other in joined.Skip(1)) { merged.AddRange(other); clusters.Remove(other); }
        }
        return clusters;
    }

    private static double PerpDistanceMeters(double lat, double lon, NamedTaxiSegment s)
    {
        double k = Math.Cos(lat * Math.PI / 180.0) * 111320.0;
        const double m = 111320.0;
        double px = (lon - s.Lon1) * k, py = (lat - s.Lat1) * m;
        double vx = (s.Lon2 - s.Lon1) * k, vy = (s.Lat2 - s.Lat1) * m;
        double len2 = vx * vx + vy * vy;
        double t = len2 <= 0 ? 0 : Math.Clamp((px * vx + py * vy) / len2, 0, 1);
        double dx = px - t * vx, dy = py - t * vy;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
