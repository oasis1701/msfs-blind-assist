using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MSFSBlindAssist.Database;

/// <summary>One airport row the name search ranks: what it matches on (ident, name, city) and what
/// orders equally good matches (longest runway, closed). State and country only describe it.</summary>
public sealed record AirportSearchEntry(string Ident, string Name, string City, string State,
    string Country, int LongestRunwayFeet, bool IsClosed);

/// <summary>
/// Finds airports by part of their name or city for the EFB's Airport Lookup (Shift+E), so a pilot
/// who does not know the ICAO can type "Göteborg" or "landvetter". Matching ignores case, accents
/// (the navdata spells the city "Goteborg" but the airport "Säve") and punctuation
/// ("charles de gaulle" finds "Charles-De-Gaulle"). Every word typed must appear in the ident,
/// name or city. The whole table is read and ranked in memory: about 40,000 rows, an Enter press.
/// </summary>
public static class AirportNameSearch
{
    public const int DefaultLimit = 50;

    // How well an airport matched; lower ranks first.
    private enum Tier { IdentExact, FieldExact, WordPrefix, Contains }

    /// <summary>Reads every airport from the navdata database and returns the best matches.</summary>
    public static List<AirportSearchEntry> Search(string databasePath, string query, int limit = DefaultLimit)
    {
        if (string.IsNullOrWhiteSpace(query) || !File.Exists(databasePath))
            return new List<AirportSearchEntry>();

        var entries = new List<AirportSearchEntry>();
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=false;");
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT ident, icao, name, city, state, country, longest_runway_length, is_closed FROM airport",
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
                IsClosed: !reader.IsDBNull(7) && reader.GetInt32(7) != 0));
        }
        return Rank(query, entries, limit);
    }

    /// <summary>
    /// The entries matching <paramref name="query"/>, best first: an exact ident, then a name or city
    /// equal to the query, then every word starting a word, then words found anywhere. Within a tier
    /// open airports come before closed ones, then the longest runway first, so "London" puts
    /// Heathrow ahead of London, Ohio.
    /// </summary>
    public static List<AirportSearchEntry> Rank(string query, IEnumerable<AirportSearchEntry> entries, int limit = DefaultLimit)
    {
        string folded = Fold(query);
        if (folded.Length == 0)
            return new List<AirportSearchEntry>();
        string[] words = folded.Split(' ');

        var matches = new List<(AirportSearchEntry Entry, Tier Tier)>();
        foreach (var entry in entries)
        {
            Tier? tier = Match(folded, words, entry);
            if (tier != null)
                matches.Add((entry, tier.Value));
        }

        return matches
            .OrderBy(m => m.Tier)
            .ThenBy(m => m.Entry.IsClosed)
            .ThenByDescending(m => m.Entry.LongestRunwayFeet)
            .ThenBy(m => m.Entry.Ident, StringComparer.Ordinal)
            .Take(limit)
            .Select(m => m.Entry)
            .ToList();
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

    /// <summary>One list line: "ESGG, Landvetter, Goteborg", then state and country where the data
    /// has them, and "closed" for a closed field.</summary>
    public static string Describe(AirportSearchEntry entry)
    {
        var parts = new List<string> { entry.Ident };
        foreach (string part in new[] { entry.Name, entry.City, entry.State, entry.Country })
        {
            if (part.Length > 0 && !parts.Contains(part, StringComparer.OrdinalIgnoreCase))
                parts.Add(part);
        }
        if (entry.IsClosed)
            parts.Add("closed");
        return string.Join(", ", parts);
    }

    private static string Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : reader.GetValue(ordinal).ToString()?.Trim() ?? "";
}
