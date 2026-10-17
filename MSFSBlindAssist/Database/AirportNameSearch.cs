using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Database;

/// <summary>One airport row the name search ranks: what it matches on (ident, name, city), what
/// orders equally good matches (longest runway, closed) and where it is. State and country only
/// describe it.</summary>
public sealed record AirportSearchEntry(string Ident, string Name, string City, string State,
    string Country, int LongestRunwayFeet, bool IsClosed, double Lat, double Lon);

/// <summary>A town from the offline location data whose name is what the pilot typed:
/// "Gothenburg" is GeoNames' name for the city the navdata calls "Goteborg".</summary>
public sealed record TownMatch(string Name, string State, string Country, double Lat, double Lon, int Population);

/// <summary>One line of the result list: the airport, and the town it was found near when it
/// matched through a town rather than by its own ident, name or city.</summary>
public sealed record AirportSearchResult(AirportSearchEntry Airport, TownMatch? NearTown = null, double TownDistanceNm = 0);

/// <summary>
/// Finds airports by part of their name or city for the EFB's Airport Lookup (Shift+E), so a pilot
/// who does not know the ICAO can type "Göteborg" or "landvetter". Matching ignores case, accents
/// (the navdata spells the city "Goteborg" but the airport "Säve") and punctuation
/// ("charles de gaulle" finds "Charles-De-Gaulle"). Every word typed must appear in the ident,
/// name or city. Towns from the offline location data whose name is the query add the airports
/// around them, so "Gothenburg" finds Landvetter and a town without an airport finds its nearest.
/// The whole airport table is read and ranked in memory: about 40,000 rows, an Enter press.
/// </summary>
public static class AirportNameSearch
{
    public const int DefaultLimit = 50;

    /// <summary>Airports this close to a matching town are listed for it.</summary>
    public const double TownRadiusNm = 30;

    /// <summary>A town with no airport within <see cref="TownRadiusNm"/> lists this many of its
    /// nearest, out to <see cref="TownFallbackRadiusNm"/>.</summary>
    public const int TownFallbackCount = 3;
    public const double TownFallbackRadiusNm = 100;

    /// <summary>The most towns of one name whose airports are added, most populous first ("Springfield").</summary>
    public const int MaxTowns = 5;

    /// <summary>The most towns of one name read: beyond <see cref="MaxTowns"/> they only place an
    /// airport that matched by its own city in its town's group (Mora, Sweden is not among the five
    /// largest Moras, but Siljan airport still ranks with it).</summary>
    public const int TownLookupLimit = 50;

    // How well an airport matched; lower ranks first. A town match ranks with an exact city.
    private enum Tier { IdentExact, FieldExact, WordPrefix, Contains }

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

    /// <summary>
    /// The airports matching <paramref name="query"/>, best first: an exact ident; then a name or
    /// city equal to the query, or an airport near a town of that name; then every word starting a
    /// word; then words found anywhere. Within a tier the airports around the most populous town of
    /// that name come first (an airport belongs to the largest matching town within
    /// <see cref="TownRadiusNm"/>), then open before closed, then the longest runway, so "London"
    /// puts Heathrow ahead of London, Ohio and London, California's naval air station, and
    /// "Gothenburg" puts Landvetter ahead of Gothenburg, Nebraska.
    /// </summary>
    public static List<AirportSearchResult> Rank(string query, IReadOnlyList<AirportSearchEntry> entries,
        IEnumerable<TownMatch>? towns = null, int limit = DefaultLimit)
    {
        string folded = Fold(query);
        if (folded.Length == 0)
            return new List<AirportSearchResult>();
        string[] words = folded.Split(' ');
        var townsByPopulation = (towns ?? Enumerable.Empty<TownMatch>()).OrderByDescending(t => t.Population).ToList();

        var best = new Dictionary<AirportSearchEntry, (Tier Tier, AirportSearchResult Result)>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (Match(folded, words, entry) is { } tier)
                best[entry] = (tier, new AirportSearchResult(entry));
        }

        foreach (var town in townsByPopulation.Take(MaxTowns))
        {
            foreach (var (entry, nm) in AirportsNear(town, entries))
            {
                // A direct match at least as good keeps its own line; a nearer town wins a tie.
                if (best.TryGetValue(entry, out var held) &&
                    (held.Tier < Tier.FieldExact || (held.Tier == Tier.FieldExact && (held.Result.NearTown == null || held.Result.TownDistanceNm <= nm))))
                    continue;
                best[entry] = (Tier.FieldExact, new AirportSearchResult(entry, town, nm));
            }
        }

        // A town's fallback airports (none within TownRadiusNm) rank after every town's own.
        int GroupPopulation(AirportSearchResult r) => r.NearTown is { } near
            ? (r.TownDistanceNm <= TownRadiusNm ? near.Population : 0)
            : townsByPopulation.FirstOrDefault(t =>
                LocationGeometry.GreatCircleNm(t.Lat, t.Lon, r.Airport.Lat, r.Airport.Lon) <= TownRadiusNm)?.Population
            ?? 0;

        return best.Values
            .OrderBy(m => m.Tier)
            .ThenByDescending(m => GroupPopulation(m.Result))
            .ThenBy(m => m.Result.Airport.IsClosed)
            .ThenByDescending(m => m.Result.Airport.LongestRunwayFeet)
            .ThenBy(m => m.Result.Airport.Ident, StringComparer.Ordinal)
            .Take(limit)
            .Select(m => m.Result)
            .ToList();
    }

    private static IEnumerable<(AirportSearchEntry Entry, double Nm)> AirportsNear(TownMatch town, IReadOnlyList<AirportSearchEntry> entries)
    {
        var withinFallback = entries
            .Select(e => (Entry: e, Nm: LocationGeometry.GreatCircleNm(town.Lat, town.Lon, e.Lat, e.Lon)))
            .Where(x => x.Nm <= TownFallbackRadiusNm)
            .OrderBy(x => x.Nm)
            .ToList();
        var within = withinFallback.Where(x => x.Nm <= TownRadiusNm).ToList();
        return within.Count > 0 ? within : withinFallback.Take(TownFallbackCount);
    }

    private static Tier? Match(string folded, string[] words, AirportSearchEntry entry)
    {
        string ident = Fold(entry.Ident);
        string name = Fold(entry.Name);
        string city = Fold(entry.City);

        if (ident == folded)
            return Tier.IdentExact;

        string haystack = $"{ident} {name} {city}";
        if (!words.All(w => haystack.Contains(w, StringComparison.Ordinal)))
            return null;

        if (name == folded || city == folded)
            return Tier.FieldExact;

        string[] haystackWords = haystack.Split(' ');
        if (words.All(w => haystackWords.Any(h => h.StartsWith(w, StringComparison.Ordinal))))
            return Tier.WordPrefix;

        return Tier.Contains;
    }

    /// <summary>
    /// Lower case without accents, with every run of punctuation or spaces as one space:
    /// "Göteborg" → "goteborg", "Charles-De-Gaulle" → "charles de gaulle". Letters Unicode does not
    /// decompose are spelled out (ø → o, æ → ae, ß → ss) so the Nordic and German names match their
    /// plain spellings.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (!char.IsLetterOrDigit(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c switch
            {
                'ø' => "o",
                'æ' => "ae",
                'œ' => "oe",
                'ß' => "ss",
                'đ' => "d",
                'ð' => "d",
                'ł' => "l",
                'ı' => "i",
                'þ' => "th",
                _ => c.ToString()
            });
        }
        return sb.ToString();
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
