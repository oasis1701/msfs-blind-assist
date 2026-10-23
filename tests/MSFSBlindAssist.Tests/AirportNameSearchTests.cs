// AirportNameSearch: the EFB Airport Lookup's search by name or city. Rows below are the
// navdatareader values from a pilot's FS2020 database (ESGG/ESGP city "Goteborg", ESGP name "Säve",
// EGLL/EGLC/KUYF all city "London", KGTE in Gothenburg, Nebraska) at their real positions; the towns
// are GeoNames' (Gothenburg, Sweden, 608,462 people). The last test reads a real SQLite file.

using System.Globalization;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests;

public sealed class AirportNameSearchTests
{
    private static AirportSearchEntry A(string ident, string name, string city, int runwayFeet,
        double lat = 0, double lon = 0, string state = "", bool closed = false)
        => new(ident, name, city, state, "", runwayFeet, closed, lat, lon);

    private static readonly AirportSearchEntry Landvetter = A("ESGG", "Landvetter", "Goteborg", 10802, 57.6628, 12.2798);
    private static readonly AirportSearchEntry Save = A("ESGP", "Säve", "Goteborg", 6293, 57.7747, 11.8704);
    private static readonly AirportSearchEntry Quinn = A("KGTE", "Quinn", "Gothenburg", 4500, 40.9264, -100.1484, state: "Nebraska");
    private static readonly AirportSearchEntry MadisonCo = A("KUYF", "Madison Co", "London", 3994, 39.9327, -83.4620, state: "Ohio");

    private static readonly AirportSearchEntry[] World =
    {
        Landvetter,
        Save,
        A("EGLL", "Heathrow", "London", 12822, 51.4706, -0.4619),
        A("EGLC", "London City", "London", 4927, 51.5053, 0.0553),
        MadisonCo,
        A("KNEF", "New London Airport", "New London", 2500),
        A("LFPG", "Charles-De-Gaulle", "Paris", 13829, 49.0097, 2.5479),
        A("EKBJ", "Grønbæk Flyveplads", "Bjerringbro", 2000),
        A("KBOS", "Logan Intl", "Boston", 10227, 42.3656, -71.0096, state: "Massachusetts"),
        Quinn,
    };

    private static readonly TownMatch GothenburgSweden = new("Gothenburg", "Vastra Gotaland", "Sweden", 57.7072, 11.9668, 608462);
    private static readonly TownMatch GothenburgNebraska = new("Gothenburg", "Nebraska", "United States", 40.9294, -100.1607, 3500);

    private static string[] Idents(string query, IEnumerable<TownMatch>? towns = null) =>
        AirportNameSearch.Rank(query, World, towns).Select(r => r.Airport.Ident).ToArray();

    [Theory]
    [InlineData("Göteborg", "goteborg")]
    [InlineData("Charles-De-Gaulle", "charles de gaulle")]
    [InlineData("  Tokyo (Haneda) Intl ", "tokyo haneda intl")]
    [InlineData("Grønbæk", "gronbaek")]
    [InlineData("Straße", "strasse")]
    [InlineData("Chicago-O'Hare Intl", "chicago ohare intl")]
    [InlineData("St John’s Intl", "st johns intl")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Fold_drops_case_accents_and_punctuation(string? input, string expected)
        => Assert.Equal(expected, SearchText.Fold(input));

    [Fact]
    public void Fold_ignores_the_current_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // Turkish lower-cases "I" to dotless "ı"; the fold must not.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("istanbul", SearchText.Fold("ISTANBUL"));
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("goteborg", SearchText.Fold("GÖTEBORG"));
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
    public void A_name_with_an_apostrophe_is_found_without_it()
    {
        // MSFS 2024 navdata spells them "Chicago-O'Hare Intl" and "St John's Intl".
        var world = new[] { A("KORD", "Chicago-O'Hare Intl", "", 13000), A("CYYT", "St John's Intl", "", 8500) };
        string[] Found(string query) => AirportNameSearch.Rank(query, world).Select(r => r.Airport.Ident).ToArray();
        Assert.Equal(new[] { "KORD" }, Found("ohare"));
        Assert.Equal(new[] { "KORD" }, Found("O'Hare"));
        Assert.Equal(new[] { "CYYT" }, Found("st johns"));
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
        var world = World.Append(A("XBOS", "Kbos Field", "Elsewhere", 20000)).ToList();
        var idents = AirportNameSearch.Rank("kbos", world).Select(r => r.Airport.Ident).ToArray();
        Assert.Equal(new[] { "KBOS", "XBOS" }, idents);
    }

    [Fact]
    public void An_exact_ident_loads_directly_though_its_letters_appear_in_other_names()
    {
        // MSFS 2024 navdata: KATL's letters are inside Metlakatla and Kitkatla.
        var world = new[]
        {
            A("KATL", "Hartsfield - Jackson Atlanta I", "", 12390, 33.6367, -84.4281),
            A("MTM", "Metlakatla", "", 0, 55.1311, -131.5781),
            A("CAP7", "Kitkatla Seaplane Base", "", 0, 53.8000, -130.4300),
        };
        var results = AirportNameSearch.Rank("katl", world);
        Assert.Equal(3, results.Count);
        Assert.Equal("KATL", AirportNameSearch.DirectMatch(results)?.Airport.Ident);
    }

    [Fact]
    public void An_exact_ident_opens_the_list_when_another_airport_matches_the_whole_text()
    {
        // LIMA is Turin-Aeritalia's ident and Lima, Peru a town: the list, the ident first.
        var world = new[]
        {
            A("LIMA", "Torino Aeritalia", "", 3000, 45.0861, 7.6033),
            A("SPJC", "Jorge Chavez Intl", "", 11506, -12.0219, -77.1143),
        };
        var lima = new TownMatch("Lima", "Lima", "Peru", -12.0432, -77.0282, 7737002);
        var results = AirportNameSearch.Rank("LIMA", world, new[] { lima });
        Assert.Equal(new[] { "LIMA", "SPJC" }, results.Select(r => r.Airport.Ident).ToArray());
        Assert.Null(AirportNameSearch.DirectMatch(results));

        // So does an airport named exactly what was typed.
        var named = AirportNameSearch.Rank("lima", new[] { world[0], A("XLIM", "Lima", "", 2000) });
        Assert.Null(AirportNameSearch.DirectMatch(named));
    }

    [Fact]
    public void One_result_loads_directly_and_several_by_name_open_the_list()
    {
        Assert.Equal("ESGG", AirportNameSearch.DirectMatch(AirportNameSearch.Rank("landvetter", World))?.Airport.Ident);
        Assert.Null(AirportNameSearch.DirectMatch(AirportNameSearch.Rank("Göteborg", World)));
        Assert.Null(AirportNameSearch.DirectMatch(AirportNameSearch.Rank("nowhere", World)));
    }

    [Fact]
    public void Closed_airports_rank_after_open_ones_in_the_same_tier()
    {
        var world = new[] { A("AAAA", "Old Field", "Town", 9000, closed: true), A("BBBB", "New Field", "Town", 3000) };
        Assert.Equal(new[] { "BBBB", "AAAA" },
            AirportNameSearch.Rank("town", world).Select(r => r.Airport.Ident).ToArray());
    }

    [Fact]
    public void Limit_caps_the_list_and_blank_query_finds_nothing()
    {
        Assert.Equal(2, AirportNameSearch.Rank("london", World, limit: 2).Count);
        Assert.Empty(AirportNameSearch.Rank("  - ", World));
        Assert.Empty(Idents("nowhere at all"));
    }

    [Fact]
    public void Search_counts_the_matches_the_limit_leaves_out()
    {
        var outcome = AirportNameSearch.Search("london", World, limit: 2);
        Assert.Equal(new[] { "EGLL", "EGLC" }, outcome.Results.Select(r => r.Airport.Ident).ToArray());
        Assert.Equal(4, outcome.TotalMatches);
    }

    [Fact]
    public void The_list_title_says_when_matches_were_left_out()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("50 of 1312 airports match LONDON", AirportNameSearch.ListTitle("LONDON", 50, 1312));
            Assert.Equal("2 airports match GÖTEBORG", AirportNameSearch.ListTitle("GÖTEBORG", 2, 2));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("FS2024", false, "No airports match ROME. Searching by city on MSFS 2024 needs the location data.")]
    [InlineData("fs2024", false, "No airports match ROME. Searching by city on MSFS 2024 needs the location data.")]
    [InlineData("FS2024", true, "No airports match ROME")]
    [InlineData("FS2020", false, "No airports match ROME")]
    public void No_match_explains_city_search_on_MSFS_2024_without_location_data(string simulator, bool locationData, string expected)
        => Assert.Equal(expected, AirportNameSearch.NoMatchMessage("ROME", simulator, locationData));

    [Fact]
    public void A_town_adds_its_airports_ranked_with_exact_city_matches()
    {
        // Without the location data only Gothenburg, Nebraska's own airport matches.
        Assert.Equal(new[] { "KGTE" }, Idents("Gothenburg"));

        var results = AirportNameSearch.Rank("Gothenburg", World, new[] { GothenburgNebraska, GothenburgSweden });
        Assert.Equal(new[] { "ESGG", "ESGP", "KGTE" }, results.Select(r => r.Airport.Ident).ToArray());
        Assert.Same(GothenburgSweden, results[0].NearTown);
        Assert.InRange(results[0].TownDistanceNm, 10, 12);
        Assert.Null(results[2].NearTown); // its own city matched; the Nebraska town does not replace that
    }

    [Fact]
    public void A_town_never_downgrades_an_ident_match_and_upgrades_a_partial_one()
    {
        var town = new TownMatch("Landvetter Town", "", "Sweden", 57.68, 12.20, 6000);
        var ident = AirportNameSearch.Rank("ESGG", World, new[] { town });
        Assert.Null(ident.Single(r => r.Airport.Ident == "ESGG").NearTown);

        // "teborg" matches both only partially (city "Goteborg"); a town of that name nearby lifts
        // them to town matches, still ordered by runway.
        var teborg = new TownMatch("Teborg", "", "Sweden", 57.78, 11.88, 600);
        var results = AirportNameSearch.Rank("teborg", World, new[] { teborg });
        Assert.Equal(new[] { "ESGG", "ESGP" }, results.Select(r => r.Airport.Ident).ToArray());
        Assert.All(results, r => Assert.Same(teborg, r.NearTown));
    }

    [Fact]
    public void A_town_without_an_airport_within_30_nm_lists_its_three_nearest_within_100()
    {
        var world = new[]
        {
            A("AAA1", "One", "", 3000, 0.0, 0.6),   // 36 nm east of the town
            A("AAA2", "Two", "", 3000, 0.0, 0.8),
            A("AAA3", "Three", "", 3000, 0.0, 1.0),
            A("AAA4", "Four", "", 9000, 0.0, 1.2),  // fourth nearest: left out despite its runway
            A("AAA5", "Five", "", 9000, 0.0, 2.0),  // 120 nm: beyond the fallback radius
        };
        var town = new TownMatch("Remote", "", "", 0.0, 0.0, 700);
        var idents = AirportNameSearch.Rank("Remote", world, new[] { town }).Select(r => r.Airport.Ident).ToHashSet();
        Assert.Equal(new HashSet<string> { "AAA1", "AAA2", "AAA3" }, idents);
    }

    [Fact]
    public void A_town_adds_its_ten_longest_open_runways_and_never_a_helipad()
    {
        // Half of MSFS 2024's airports have no runway: helipads at hospitals, shops, tunnels.
        var world = Enumerable.Range(1, 12).Select(i => A($"FLD{i:00}", $"Field {i}", "", i * 1000, 51.5 + i * 0.01, -0.1))
            .Append(A("CLSD", "Old Field", "", 20000, 51.52, -0.1, closed: true))
            .Append(A("HELI1", "Harrods Heliport", "", 0, 51.50, -0.16))
            .Append(A("HELI2", "Hospital Helipad", "", 0, 51.51, -0.12))
            .ToList();
        var town = new TownMatch("Bigcity", "", "", 51.5, -0.12, 9000000);
        var idents = AirportNameSearch.Rank("Bigcity", world, new[] { town }).Select(r => r.Airport.Ident).ToArray();
        Assert.Equal(Enumerable.Range(3, 10).Reverse().Select(i => $"FLD{i:00}").ToArray(), idents);

        // A helipad is still found by its own name.
        Assert.Equal(new[] { "HELI1" }, AirportNameSearch.Rank("harrods", world).Select(r => r.Airport.Ident).ToArray());
    }

    [Fact]
    public void A_town_with_only_a_helipad_nearby_lists_its_nearest_runways()
    {
        var world = new[]
        {
            A("HELI", "Clinic Helipad", "", 0, 0.0, 0.1),   // 6 nm: not an airfield
            A("AAA1", "One", "", 3000, 0.0, 0.6),            // 36 nm
        };
        var town = new TownMatch("Remote", "", "", 0.0, 0.0, 700);
        Assert.Equal(new[] { "AAA1" },
            AirportNameSearch.Rank("Remote", world, new[] { town }).Select(r => r.Airport.Ident).ToArray());
    }

    [Fact]
    public void An_airport_near_two_towns_of_a_name_goes_with_the_larger()
    {
        // 25 nm from a city of 8 million, 10 nm from a town of 5,000 with the same name.
        var airport = A("XXXX", "Between", "", 9000, 0.0, 0.0);
        var big = new TownMatch("Samename", "", "Big", 0.0, 25 / 60.0, 8000000);
        var small = new TownMatch("Samename", "", "Small", 0.0, -10 / 60.0, 5000);
        var result = Assert.Single(AirportNameSearch.Rank("Samename", new[] { airport }, new[] { small, big }));
        Assert.Same(big, result.NearTown);
    }

    [Fact]
    public void An_airport_a_big_town_only_falls_back_to_goes_with_the_town_it_is_near()
    {
        // Nothing within 30 nm of the big town, so the airport (36 nm) is its fallback; it is 5 nm from the small town.
        var airport = A("XXXX", "Near Small", "", 9000, 0.0, 0.6);
        var big = new TownMatch("Samename", "", "Big", 0.0, 0.0, 8000000);
        var small = new TownMatch("Samename", "", "Small", 0.0, 0.6 + 5 / 60.0, 5000);
        var result = Assert.Single(AirportNameSearch.Rank("Samename", new[] { airport }, new[] { big, small }));
        Assert.Same(small, result.NearTown);
        Assert.InRange(result.TownDistanceNm, 4, 6);
    }

    [Fact]
    public void The_most_populous_towns_airports_come_first_whatever_their_runways()
    {
        // London, California (about 1,900 people) has Lemoore naval air station 26 nm away, with a
        // longer runway than Heathrow; London, England's airports still come first.
        var world = new[]
        {
            A("EGLL", "Heathrow", "London", 12822, 51.4706, -0.4619),
            A("EGLC", "London City", "London", 4927, 51.5053, 0.0553),
            A("KNLC", "Lemoore NAS", "Lemoore", 13500, 36.3330, -119.9520, state: "California"),
        };
        var towns = new[]
        {
            new TownMatch("London", "California", "United States", 36.4761, -119.4432, 1869),
            new TownMatch("London", "England", "United Kingdom", 51.5085, -0.1257, 8961989),
        };
        Assert.Equal(new[] { "EGLL", "EGLC", "KNLC" },
            AirportNameSearch.Rank("London", world, towns).Select(r => r.Airport.Ident).ToArray());
    }

    [Fact]
    public void A_city_match_ranks_with_its_town_even_beyond_the_five_largest()
    {
        // Five bigger Moras elsewhere add their airports; Siljan (city "Mora") still ranks with
        // Mora, Sweden, ahead of the smaller Moras' airports.
        var world = new List<AirportSearchEntry> { A("ESKM", "Siljan", "Mora", 7500, 60.9579, 14.5114) };
        var towns = new List<TownMatch> { new("Mora", "Dalarna", "Sweden", 61.0049, 14.5374, 10000) };
        for (int i = 0; i < 5; i++)
        {
            world.Add(A($"BIG{i}", $"Big {i}", "", 3000, 10 + i * 5, 0));
            world.Add(A($"SML{i}", $"Small {i}", "", 9000, -10 - i * 5, 0));
            towns.Add(new TownMatch("Mora", "", "", 10 + i * 5, 0, 20000 + i));
            towns.Add(new TownMatch("Mora", "", "", -10 - i * 5, 0, 500 + i));
        }
        var idents = AirportNameSearch.Rank("Mora", world, towns).Select(r => r.Airport.Ident).ToList();
        Assert.Equal(new[] { "BIG4", "BIG3", "BIG2", "BIG1", "BIG0", "ESKM" }, idents);
    }

    [Fact]
    public void A_big_towns_fallback_airports_rank_after_a_small_towns_own()
    {
        // Mora, Cameroon has no airport within 30 nm, so its nearest (36 nm) is listed, but after
        // Siljan, the airport of the smaller Mora, Sweden.
        var world = new[]
        {
            A("FKKL", "Salak", "Maroua", 11000, 10.4514, 14.2574),
            A("ESKM", "Siljan", "Mora", 7500, 60.9579, 14.5114),
        };
        var towns = new[]
        {
            new TownMatch("Mora", "Far North", "Cameroon", 11.0459, 14.1401, 50000),
            new TownMatch("Mora", "Dalarna", "Sweden", 61.0049, 14.5374, 10000),
        };
        var results = AirportNameSearch.Rank("Mora", world, towns);
        Assert.Equal(new[] { "ESKM", "FKKL" }, results.Select(r => r.Airport.Ident).ToArray());
        Assert.InRange(results[1].TownDistanceNm, 30, 40);
    }

    [Fact]
    public void Only_the_five_most_populous_towns_of_a_name_are_used()
    {
        var world = Enumerable.Range(0, 6).Select(i => A($"SPR{i}", $"Field {i}", "", 3000, i * 10.0, 0)).ToList();
        var towns = Enumerable.Range(0, 6).Select(i => new TownMatch("Springfield", "", "", i * 10.0, 0, 1000 + i)).ToList();
        var idents = AirportNameSearch.Rank("Springfield", world, towns).Select(r => r.Airport.Ident).ToHashSet();
        Assert.Equal(5, idents.Count);
        Assert.DoesNotContain("SPR0", idents); // the smallest Springfield
    }

    [Fact]
    public void Describe_lists_ident_name_city_and_skips_blanks_and_repeats()
    {
        Assert.Equal("ESGG, Landvetter, Goteborg", AirportNameSearch.Describe(new AirportSearchResult(Landvetter)));
        Assert.Equal("KUYF, Madison Co, London, Ohio", AirportNameSearch.Describe(new AirportSearchResult(MadisonCo)));
        Assert.Equal("EDDM, Munich", AirportNameSearch.Describe(new AirportSearchResult(A("EDDM", "Munich", "Munich", 13000))));
        Assert.Equal("AAAA, Old Field, Town, closed",
            AirportNameSearch.Describe(new AirportSearchResult(A("AAAA", "Old Field", "Town", 0, closed: true))));
    }

    [Fact]
    public void Describe_names_the_town_and_where_the_airport_lies_from_the_aircraft()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            var result = AirportNameSearch.Rank("Gothenburg", World, new[] { GothenburgSweden })
                .Single(r => r.Airport.Ident == "ESGG");
            // From Stockholm Arlanda (ESSA) Landvetter lies about 212 nm southwest.
            string line = AirportNameSearch.Describe(result, (59.6519, 17.9186));
            Assert.Matches(@"^ESGG, Landvetter, Goteborg, 1[01] nm from Gothenburg, Vastra Gotaland, Sweden, 21\d nm southwest$", line);

            Assert.Equal("ESGG, Landvetter, Goteborg, under 1 nm away",
                AirportNameSearch.Describe(new AirportSearchResult(Landvetter), (57.6628, 12.2798)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void FromAircraft_gives_great_circle_distance_and_compass_direction()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            // Arlanda to Landvetter, and to Kiruna (ESNQ 67.8222 N, 20.3368 E) to the north.
            Assert.Matches(@"^21\d nm southwest$", AirportNameSearch.FromAircraft((59.6519, 17.9186), 57.6628, 12.2798));
            Assert.Matches(@"^49\d nm north$", AirportNameSearch.FromAircraft((59.6519, 17.9186), 67.8222, 20.3368));
            Assert.Equal("under 1 nm away", AirportNameSearch.FromAircraft((59.6519, 17.9186), 59.655, 17.92));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void ReadAirports_reads_the_navdata_airport_table()
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
    city TEXT, state TEXT, country TEXT, longest_runway_length INTEGER, is_closed INTEGER, laty REAL, lonx REAL);
INSERT INTO airport VALUES (1, 'ESGG', NULL, 'Landvetter', 'Goteborg', NULL, NULL, 10802, 0, 57.6628, 12.2798);
INSERT INTO airport VALUES (2, 'ESGP', NULL, 'Säve', 'Goteborg', NULL, NULL, 6293, 0, 57.7747, 11.8704);
INSERT INTO airport VALUES (3, 'XX01', 'ZZZZ', 'Icao Over Ident', NULL, NULL, NULL, NULL, NULL, NULL, NULL);";
                cmd.ExecuteNonQuery();
            }

            var airports = AirportNameSearch.ReadAirports(path);
            Assert.Equal(new[] { "ESGG", "ESGP", "ZZZZ" }, airports.Select(a => a.Ident).ToArray());
            Assert.Equal("Säve", airports[1].Name);
            Assert.Equal(57.6628, airports[0].Lat, 4);
            Assert.Equal(12.2798, airports[0].Lon, 4);
            Assert.Equal("", airports[2].City);
            Assert.Equal(0, airports[2].LongestRunwayFeet);

            Assert.Equal(new[] { "ESGG", "ESGP" },
                AirportNameSearch.Rank("Göteborg", airports).Select(r => r.Airport.Ident).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadAirports_without_a_database_is_empty()
        => Assert.Empty(AirportNameSearch.ReadAirports(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.sqlite")));
}
