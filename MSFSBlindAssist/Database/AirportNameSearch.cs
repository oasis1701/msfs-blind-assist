using System.Globalization;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Database;

/// <summary>One airport row the name search ranks: what it matches on (ident, name, city), what
/// orders equally good matches (longest runway, closed) and where it is. State and country only
/// describe it.</summary>
public sealed record AirportSearchEntry(string Ident, string Name, string City, string State,
    string Country, int LongestRunwayFeet, bool IsClosed, double Lat, double Lon);

/// <summary>How well an airport matched; the list is ordered by it, best first. A town match counts
/// as <see cref="Exact"/>, like a name or city equal to what was typed.</summary>
public enum AirportMatchTier { Ident, Exact, WordStart, Contains }

/// <summary>One line of the result list: the airport, and the town it was found near when it
/// matched through a town rather than by its own ident, name or city.</summary>
public sealed record AirportSearchResult(AirportSearchEntry Airport, TownMatch? NearTown = null, double TownDistanceNm = 0)
{
    public AirportMatchTier Tier { get; init; } = AirportMatchTier.Contains;
}

/// <summary>The lines to list (at most the limit) and how many airports matched in all.</summary>
public sealed record AirportSearchOutcome(List<AirportSearchResult> Results, int TotalMatches);

/// <summary>
/// Finds airports by part of their name or city for the EFB's Airport Lookup (Shift+E), so a pilot
/// who does not know the ICAO can type "Göteborg" or "landvetter". Matching ignores case, accents
/// (the navdata spells the city "Goteborg" but the airport "Säve") and punctuation
/// ("charles de gaulle" finds "Charles-De-Gaulle"). Every word typed must appear in the ident,
/// name or city. Towns from the offline location data whose name is the query add the airports
/// around them, so "Gothenburg" finds Landvetter and a town without an airport finds its nearest.
/// The whole airport table is read and ranked in memory on each Enter: about 40,000 rows on
/// FS2020, 84,000 on MSFS 2024, whose navdata leaves every airport's city, state and country
/// empty, so there a city is found only through an airport's name or a town.
/// </summary>
public static class AirportNameSearch
{
    public const int DefaultLimit = 50;

    /// <summary>Airports this close to a matching town are listed for it, at most
    /// <see cref="TownAirportLimit"/> of them.</summary>
    public const double TownRadiusNm = 30;

    /// <summary>The most airports one town adds: its open ones first, then the longest runways, so
    /// "London" lists Heathrow to London City and still has room for London, Ontario and London,
    /// Kentucky. A site with no runway (a helipad: half of MSFS 2024's airports) is never added
    /// for a town, only found by its own ident or name.</summary>
    public const int TownAirportLimit = 10;

    /// <summary>A town with no airfield within <see cref="TownRadiusNm"/> lists this many of its
    /// nearest, out to <see cref="TownFallbackRadiusNm"/>.</summary>
    public const int TownFallbackCount = 3;
    public const double TownFallbackRadiusNm = 100;

    /// <summary>The most towns of one name whose airports are added, most populous first ("Springfield").</summary>
    public const int MaxTowns = 5;

    /// <summary>The most towns of one name read: beyond <see cref="MaxTowns"/> they only place an
    /// airport that matched by its own city in its town's group (Mora, Sweden is not among the five
    /// largest Moras, but Siljan airport still ranks with it).</summary>
    public const int TownLookupLimit = 50;

    /// <summary>Reads every airport from the navdata database.</summary>
    public static List<AirportSearchEntry> ReadAirports(string databasePath)
    {
        var entries = new List<AirportSearchEntry>();
        if (!File.Exists(databasePath))
            return entries;

        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=false;");
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT ident, icao, name, city, state, country, longest_runway_length, is_closed, laty, lonx FROM airport",
            connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string icao = Text(reader, 1);
            entries.Add(new AirportSearchEntry(
                Ident: icao.Length > 0 ? icao : Text(reader, 0),
                Name: Text(reader, 2),
                City: Text(reader, 3),
                State: Text(reader, 4),
                Country: Text(reader, 5),
                LongestRunwayFeet: reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                IsClosed: !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
                Lat: reader.IsDBNull(8) ? 0 : reader.GetDouble(8),
                Lon: reader.IsDBNull(9) ? 0 : reader.GetDouble(9)));
        }
        return entries;
    }

    /// <summary>The airports matching <paramref name="query"/>, best first, at most
    /// <paramref name="limit"/> (see <see cref="Search"/>).</summary>
    public static List<AirportSearchResult> Rank(string query, IReadOnlyList<AirportSearchEntry> entries,
        IEnumerable<TownMatch>? towns = null, int limit = DefaultLimit)
        => Search(query, entries, towns, limit).Results;

    /// <summary>
    /// The airports matching <paramref name="query"/>, best first: an exact ident; then a name or
    /// city equal to the query, or an airport near a town of that name; then every word starting a
    /// word; then words found anywhere. Within a tier the airports around the most populous town of
    /// that name come first (an airport belongs to the largest matching town within
    /// <see cref="TownRadiusNm"/>), then open before closed, then the longest runway, so "London"
    /// puts Heathrow ahead of London, Ohio and London, California's naval air station, and
    /// "Gothenburg" puts Landvetter ahead of Gothenburg, Nebraska. At most <paramref name="limit"/>
    /// are returned, with the count of all.
    /// </summary>
    public static AirportSearchOutcome Search(string query, IReadOnlyList<AirportSearchEntry> entries,
        IEnumerable<TownMatch>? towns = null, int limit = DefaultLimit)
    {
        string folded = SearchText.Fold(query);
        if (folded.Length == 0)
            return new AirportSearchOutcome(new List<AirportSearchResult>(), 0);
        string[] words = folded.Split(' ');
        var townsByPopulation = (towns ?? Enumerable.Empty<TownMatch>()).OrderByDescending(t => t.Population).ToList();

        var best = new Dictionary<AirportSearchEntry, AirportSearchResult>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (Match(folded, words, entry) is { } tier)
                best[entry] = new AirportSearchResult(entry) { Tier = tier };
        }

        // Towns come largest first, so an airport near two towns of the name stays with the larger.
        // Only an airport the larger town took as a fallback (none of its own within TownRadiusNm)
        // moves to a smaller town it lies within TownRadiusNm of.
        foreach (var town in townsByPopulation.Take(MaxTowns))
        {
            foreach (var (entry, nm) in AirportsNear(town, entries))
            {
                if (best.TryGetValue(entry, out var held) && held.Tier <= AirportMatchTier.Exact &&
                    !(held.NearTown != null && held.TownDistanceNm > TownRadiusNm && nm <= TownRadiusNm))
                    continue;
                best[entry] = new AirportSearchResult(entry, town, nm) { Tier = AirportMatchTier.Exact };
            }
        }

        // A town's fallback airports (none within TownRadiusNm) rank after every town's own.
        int GroupPopulation(AirportSearchResult r) => r.NearTown is { } near
            ? (r.TownDistanceNm <= TownRadiusNm ? near.Population : 0)
            : townsByPopulation.FirstOrDefault(t =>
                LocationGeometry.GreatCircleNm(t.Lat, t.Lon, r.Airport.Lat, r.Airport.Lon) <= TownRadiusNm)?.Population
            ?? 0;

        var ranked = best.Values
            .OrderBy(r => r.Tier)
            .ThenByDescending(GroupPopulation)
            .ThenBy(r => r.Airport.IsClosed)
            .ThenByDescending(r => r.Airport.LongestRunwayFeet)
            .ThenBy(r => r.Airport.Ident, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
        return new AirportSearchOutcome(ranked, best.Count);
    }

    /// <summary>
    /// The airport Enter loads at once, without the list: the only match, or an exact ident when no
    /// other airport matches the whole text exactly. KATL loads though Metlakatla and Kitkatla
    /// contain its letters; LIMA, Turin-Aeritalia's ident, opens the list because Lima, Peru is a
    /// town. Null when the pilot should pick from the list (or nothing matched).
    /// </summary>
    public static AirportSearchResult? DirectMatch(IReadOnlyList<AirportSearchResult> results)
    {
        if (results.Count == 1)
            return results[0];
        if (results.Count > 1 && results[0].Tier == AirportMatchTier.Ident && results[1].Tier != AirportMatchTier.Exact)
            return results[0];
        return null;
    }

    /// <summary>The list window's title: "2 airports match GÖTEBORG", or "50 of 312 airports match
    /// LONDON" when the list was cut at the limit.</summary>
    public static string ListTitle(string query, int shown, int total) => total > shown
        ? string.Create(CultureInfo.InvariantCulture, $"{shown} of {total} airports match {query}")
        : string.Create(CultureInfo.InvariantCulture, $"{shown} airports match {query}");

    /// <summary>What is spoken when nothing matches. On MSFS 2024 without the location data a city
    /// is found only in airport names (the navdata has no cities), so the pilot is told why.</summary>
    public static string NoMatchMessage(string query, string simulatorVersion, bool locationDataAvailable)
        => string.Equals(simulatorVersion, "FS2024", StringComparison.OrdinalIgnoreCase) && !locationDataAvailable
            ? $"No airports match {query}. Searching by city on MSFS 2024 needs the location data."
            : $"No airports match {query}";

    private static IEnumerable<(AirportSearchEntry Entry, double Nm)> AirportsNear(TownMatch town, IReadOnlyList<AirportSearchEntry> entries)
    {
        var withinFallback = entries
            .Where(e => e.LongestRunwayFeet > 0)
            .Select(e => (Entry: e, Nm: LocationGeometry.GreatCircleNm(town.Lat, town.Lon, e.Lat, e.Lon)))
            .Where(x => x.Nm <= TownFallbackRadiusNm)
            .ToList();
        var within = withinFallback
            .Where(x => x.Nm <= TownRadiusNm)
            .OrderBy(x => x.Entry.IsClosed)
            .ThenByDescending(x => x.Entry.LongestRunwayFeet)
            .Take(TownAirportLimit)
            .ToList();
        return within.Count > 0 ? within : withinFallback.OrderBy(x => x.Nm).Take(TownFallbackCount);
    }

    private static AirportMatchTier? Match(string folded, string[] words, AirportSearchEntry entry)
    {
        string ident = SearchText.Fold(entry.Ident);
        string name = SearchText.Fold(entry.Name);
        string city = SearchText.Fold(entry.City);

        if (ident == folded)
            return AirportMatchTier.Ident;

        string haystack = $"{ident} {name} {city}";
        if (!words.All(w => haystack.Contains(w, StringComparison.Ordinal)))
            return null;

        if (name == folded || city == folded)
            return AirportMatchTier.Exact;

        string[] haystackWords = haystack.Split(' ');
        if (words.All(w => haystackWords.Any(h => h.StartsWith(w, StringComparison.Ordinal))))
            return AirportMatchTier.WordStart;

        return AirportMatchTier.Contains;
    }

    /// <summary>
    /// One list line: "ESGG, Landvetter, Goteborg", then state and country where the data has them
    /// and "closed" for a closed field; for a town match "11 nm from Gothenburg, Vastra Gotaland,
    /// Sweden"; and with the aircraft's position, where the airport lies from it: "212 nm southwest".
    /// Numbers are invariant.
    /// </summary>
    public static string Describe(AirportSearchResult result, (double Lat, double Lon)? aircraft = null)
    {
        var airport = result.Airport;
        var parts = new List<string> { airport.Ident };
        AddDistinct(parts, airport.Name, airport.City, airport.State, airport.Country);
        if (airport.IsClosed)
            parts.Add("closed");

        if (result.NearTown is { } town)
        {
            var townParts = new List<string> { town.Name };
            AddDistinct(townParts, town.State, town.Country);
            parts.Add($"{Nm(result.TownDistanceNm)} from {string.Join(", ", townParts)}");
        }

        if (aircraft is { } at)
            parts.Add(FromAircraft(at, airport.Lat, airport.Lon));
        return string.Join(", ", parts);
    }

    /// <summary>Where a point lies from the aircraft, great-circle: "212 nm southwest", or "under
    /// 1 nm away". The search list and the Airport Information box both say it this way.</summary>
    public static string FromAircraft((double Lat, double Lon) aircraft, double lat, double lon)
    {
        double nm = LocationGeometry.GreatCircleNm(aircraft.Lat, aircraft.Lon, lat, lon);
        return nm < 1
            ? "under 1 nm away"
            : $"{Nm(nm)} {LocationPhrases.Direction(LocationGeometry.BearingTrue(aircraft.Lat, aircraft.Lon, lat, lon))}";
    }

    private static void AddDistinct(List<string> parts, params string[] values)
    {
        foreach (string value in values)
        {
            if (value.Length > 0 && !parts.Contains(value, StringComparer.OrdinalIgnoreCase))
                parts.Add(value);
        }
    }

    private static string Nm(double nm) =>
        nm < 1 ? "under 1 nm" : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(nm):0} nm");

    private static string Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : reader.GetValue(ordinal).ToString()?.Trim() ?? "";
}
