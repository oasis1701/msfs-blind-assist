using System.Text.RegularExpressions;
using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// Matches a GeoNames airport to the simulator's navigation database, so the Location Info window can
/// show its ident and say it is in the simulator. A match is likely, not certain. Measured 2026-11-05 on
/// the MSFS 2020 database against GeoNames' own codes (10,378 airports): 0.55% wrong, most of them the
/// same airport listed twice under an old and a new ident (docs/design/2026-11-05-location-info-redesign.md).
/// The rule: an ident GeoNames lists among the airport's alternate names, within
/// <see cref="MatchRadiusKm"/>; otherwise the nearest airport within it sharing a name word. Distance
/// alone is never enough: it pairs every GeoNames airfield with whatever field is nearest.
/// </summary>
internal sealed class AirportMatcher
{
    internal const double MatchRadiusKm = 5.0;

    /// <summary>Words every airport name has, which say nothing about which airport it is.</summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "airport", "aeroport", "aeropuerto", "aeroporto", "flughafen", "lufthavn", "flygplats", "lotnisko", "letiste",
        "international", "intl", "int", "regional", "municipal", "muni", "county", "field", "airfield", "aerodrome",
        "airstrip", "strip", "air", "base", "ab", "afb", "heliport", "helipad", "hospital", "the", "de", "del", "la",
        "le", "of", "and", "airpark", "park", "bandara", "udara", "aeroclub", "aerodromo", "flugplatz", "landing",
        "area", "ultralight", "private",
    };

    private static readonly Regex IcaoLike = new("^[A-Z]{4}$", RegexOptions.Compiled);
    private static readonly Regex CodeLike = new("^[A-Z0-9]{3,4}$", RegexOptions.Compiled);

    private readonly Dictionary<(int, int), List<AirportSearchEntry>> _grid = new();

    /// <summary>Indexes the navigation database's airports by whole degree, once per window.</summary>
    internal AirportMatcher(IEnumerable<AirportSearchEntry> airports)
    {
        foreach (var a in airports)
        {
            var key = ((int)Math.Floor(a.Lat), (int)Math.Floor(a.Lon));
            if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<AirportSearchEntry>();
            list.Add(a);
        }
    }

    /// <summary>The navigation-database airport this GeoNames airport most likely is, or null.</summary>
    internal AirportSearchEntry? Match(string name, string alternateNames, double lat, double lon)
    {
        var near = Near(lat, lon);
        if (near.Count == 0) return null;

        var alternates = alternateNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var codes = alternates.Where(a => IcaoLike.IsMatch(a)).ToHashSet(StringComparer.Ordinal);
        foreach (var (entry, _) in near)
            if (codes.Contains(entry.Ident)) return entry;

        var words = Words(name);
        foreach (var a in alternates)
            if (!CodeLike.IsMatch(a)) words.UnionWith(Words(a));
        foreach (var (entry, _) in near)
            if (words.Overlaps(Words(entry.Name)) || words.Overlaps(Words(entry.City))) return entry;
        return null;
    }

    /// <summary>The airports within <see cref="MatchRadiusKm"/>, nearest first.</summary>
    private List<(AirportSearchEntry Entry, double Km)> Near(double lat, double lon)
    {
        var found = new List<(AirportSearchEntry, double)>();
        int la = (int)Math.Floor(lat), lo = (int)Math.Floor(lon);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cellLon = lo + dx;
            if (cellLon < -180) cellLon += 360;
            if (cellLon >= 180) cellLon -= 360;
            if (!_grid.TryGetValue((la + dy, cellLon), out var list)) continue;
            foreach (var a in list)
            {
                double km = LocationGeometry.GreatCircleNm(lat, lon, a.Lat, a.Lon) * LocationPhrases.KmPerNm;
                if (km <= MatchRadiusKm) found.Add((a, km));
            }
        }
        found.Sort((x, y) => x.Item2.CompareTo(y.Item2));
        return found;
    }

    /// <summary>The words of a name that can tell one airport from another: folded, three letters or
    /// more, generic words dropped.</summary>
    internal static HashSet<string> Words(string? text) =>
        SearchText.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !GenericWords.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
}
