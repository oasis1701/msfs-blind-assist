// AirportNameSearch: the EFB Airport Lookup's search by name or city. Rows below are the
// navdatareader values from a pilot's FS2020 database (ESGG/ESGP city "Goteborg", ESGP name "Säve",
// EGLL/EGLC/KUYF all city "London"); the last test runs Search against a real SQLite file.

using System.Globalization;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public sealed class AirportNameSearchTests
{
    private static AirportSearchEntry A(string ident, string name, string city, int runwayFeet,
        string state = "", bool closed = false)
        => new(ident, name, city, state, "", runwayFeet, closed);

    private static readonly AirportSearchEntry[] World =
    {
        A("ESGG", "Landvetter", "Goteborg", 10802),
        A("ESGP", "Säve", "Goteborg", 6293),
        A("EGLL", "Heathrow", "London", 12822),
        A("EGLC", "London City", "London", 4927),
        A("KUYF", "Madison Co", "London", 3994, state: "Ohio"),
        A("KNEF", "New London Airport", "New London", 2500),
        A("LFPG", "Charles-De-Gaulle", "Paris", 13829),
        A("EKBJ", "Grønbæk Flyveplads", "Bjerringbro", 2000),
        A("KBOS", "Logan Intl", "Boston", 10227, state: "Massachusetts"),
    };

    private static string[] Idents(string query) =>
        AirportNameSearch.Rank(query, World).Select(e => e.Ident).ToArray();

    [Theory]
    [InlineData("Göteborg", "goteborg")]
    [InlineData("Charles-De-Gaulle", "charles de gaulle")]
    [InlineData("  Tokyo (Haneda) Intl ", "tokyo haneda intl")]
    [InlineData("Grønbæk", "gronbaek")]
    [InlineData("Straße", "strasse")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Fold_drops_case_accents_and_punctuation(string? input, string expected)
        => Assert.Equal(expected, AirportNameSearch.Fold(input));

    [Fact]
    public void Fold_ignores_the_current_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // Turkish lower-cases "I" to dotless "ı"; the fold must not.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("istanbul", AirportNameSearch.Fold("ISTANBUL"));
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("goteborg", AirportNameSearch.Fold("GÖTEBORG"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Accented_city_finds_the_ascii_spelling_longest_runway_first()
        => Assert.Equal(new[] { "ESGG", "ESGP" }, Idents("Göteborg"));

    [Fact]
    public void Part_of_a_word_matches_in_any_case()
    {
        Assert.Equal(new[] { "ESGG", "ESGP" }, Idents("TEBORG"));
        Assert.Equal(new[] { "ESGG" }, Idents("landvetter"));
        Assert.Equal(new[] { "ESGP" }, Idents("save"));
    }

    [Fact]
    public void Exact_city_ranks_ahead_of_partial_names_then_by_runway()
        => Assert.Equal(new[] { "EGLL", "EGLC", "KUYF", "KNEF" }, Idents("london"));

    [Fact]
    public void Every_word_must_match_across_name_and_city()
    {
        Assert.Equal(new[] { "EGLL" }, Idents("london heathrow"));
        Assert.Equal(new[] { "LFPG" }, Idents("charles de gaulle"));
        Assert.Empty(Idents("london paris"));
    }

    [Fact]
    public void Exact_ident_ranks_first()
    {
        var world = World.Append(A("XBOS", "Kbos Field", "Elsewhere", 20000));
        var idents = AirportNameSearch.Rank("kbos", world).Select(e => e.Ident).ToArray();
        Assert.Equal(new[] { "KBOS", "XBOS" }, idents);
    }

    [Fact]
    public void Closed_airports_rank_after_open_ones_in_the_same_tier()
    {
        var world = new[] { A("AAAA", "Old Field", "Town", 9000, closed: true), A("BBBB", "New Field", "Town", 3000) };
        Assert.Equal(new[] { "BBBB", "AAAA" },
            AirportNameSearch.Rank("town", world).Select(e => e.Ident).ToArray());
    }

    [Fact]
    public void Limit_caps_the_list_and_blank_query_finds_nothing()
    {
        Assert.Equal(2, AirportNameSearch.Rank("london", World, limit: 2).Count);
        Assert.Empty(AirportNameSearch.Rank("  - ", World));
        Assert.Empty(Idents("nowhere at all"));
    }

    [Fact]
    public void Describe_lists_ident_name_city_and_skips_blanks_and_repeats()
    {
        Assert.Equal("ESGG, Landvetter, Goteborg", AirportNameSearch.Describe(World[0]));
        Assert.Equal("KUYF, Madison Co, London, Ohio", AirportNameSearch.Describe(World[4]));
        Assert.Equal("EDDM, Munich", AirportNameSearch.Describe(A("EDDM", "Munich", "Munich", 13000)));
        Assert.Equal("AAAA, Old Field, Town, closed",
            AirportNameSearch.Describe(A("AAAA", "Old Field", "Town", 0, closed: true)));
    }

    [Fact]
    public void Search_reads_the_navdata_airport_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"airport-search-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var write = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                write.Open();
                using var cmd = write.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE airport (airport_id INTEGER PRIMARY KEY, ident TEXT NOT NULL, icao TEXT, name TEXT,
    city TEXT, state TEXT, country TEXT, longest_runway_length INTEGER, is_closed INTEGER);
INSERT INTO airport VALUES (1, 'ESGG', NULL, 'Landvetter', 'Goteborg', NULL, NULL, 10802, 0);
INSERT INTO airport VALUES (2, 'ESGP', NULL, 'Säve', 'Goteborg', NULL, NULL, 6293, 0);
INSERT INTO airport VALUES (3, 'XX01', 'ZZZZ', 'Icao Over Ident', NULL, NULL, NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }

            var results = AirportNameSearch.Search(path, "Göteborg");
            Assert.Equal(new[] { "ESGG", "ESGP" }, results.Select(r => r.Ident).ToArray());
            Assert.Equal("Säve", results[1].Name);

            var icaoRow = Assert.Single(AirportNameSearch.Search(path, "icao over"));
            Assert.Equal("ZZZZ", icaoRow.Ident);
            Assert.Equal("", icaoRow.City);
            Assert.Equal(0, icaoRow.LongestRunwayFeet);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
