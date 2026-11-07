using System.Globalization;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

/// <summary>The Location Info window's pure parts: the in-memory filter, the airport matcher and the
/// wording of the first box and the table's columns.</summary>
public class PlaceSearchTests
{
    private static PlaceResult Place(int id, string name, double lat, double lon, LocationCategory category = LocationCategory.Terrain,
        string kind = "island", int population = 0) =>
        new(id, name, lat, lon, category, kind, "Island", population, "Västra Götaland", "Sweden") { FoldedName = SearchText.Fold(name) };

    // Around Trollhättan (58.28, 12.29).
    private static readonly PlaceResult[] Places =
    {
        Place(1, "Krokholmen", 57.93333, 11.66667),                          // southwest, about 28 nm
        Place(2, "Trollhättan", 58.28, 12.29, LocationCategory.Town, "town", 48573), // at the point
        Place(3, "Göta älv", 58.10, 12.10, LocationCategory.Water, "river"),   // southwest, about 12 nm
        Place(4, "Hunneberg", 58.35, 12.48, LocationCategory.Terrain, "mountain"), // northeast, about 7 nm
    };

    private static readonly HashSet<string> None = new();

    [Fact]
    public void Places_come_nearest_first()
    {
        var shown = PlaceFilter.Apply(Places, 58.28, 12.29, None, "", null, null);
        Assert.Equal(new[] { 2, 4, 3, 1 }, shown.Select(i => Places[i].Id));
    }

    [Fact]
    public void Unticked_kinds_are_left_out()
    {
        var hidden = new HashSet<string> { "Terrain:island", "Town:town" };
        var shown = PlaceFilter.Apply(Places, 58.28, 12.29, hidden, "", null, null);
        Assert.Equal(new[] { 4, 3 }, shown.Select(i => Places[i].Id));
    }

    [Fact]
    public void The_search_matches_folded_names_anywhere()
    {
        var shown = PlaceFilter.Apply(Places, 58.28, 12.29, None, SearchText.Fold("ÄLV"), null, null);
        Assert.Equal(new[] { 3 }, shown.Select(i => Places[i].Id));
    }

    [Fact]
    public void The_direction_is_the_compass_word_from_the_map_position()
    {
        var southwest = PlaceFilter.Apply(Places, 58.28, 12.29, None, "", "southwest", null);
        Assert.Equal(new[] { 3, 1 }, southwest.Select(i => Places[i].Id));
        Assert.Equal(new[] { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" }, PlaceFilter.Directions);
    }

    [Fact]
    public void The_range_limits_the_distance_from_the_map_position()
    {
        var near = PlaceFilter.Apply(Places, 58.28, 12.29, None, "", null, 15.0);
        Assert.Equal(new[] { 2, 4, 3 }, near.Select(i => Places[i].Id));
    }

    [Fact]
    public void The_tree_never_names_a_heading_after_its_own_item_or_lists_a_kind_twice()
    {
        var types = new[]
        {
            new PlaceType(LocationCategory.Village, "neighborhood", "Neighborhoods"),
            new PlaceType(LocationCategory.Town, "town", "Towns"),
            new PlaceType(LocationCategory.Village, "village", "Villages"),
            new PlaceType(LocationCategory.Town, "neighborhood", "Neighborhoods"),
            new PlaceType(LocationCategory.Town, "capital", "Capitals"),
            new PlaceType(LocationCategory.Airport, "airport", "Airports"),
            new PlaceType(LocationCategory.Airport, "heliport", "Heliports"),
            new PlaceType(LocationCategory.Terrain, "island", "Islands"),
        };
        var tree = PlaceTree.Build(types, "Major cities, within 200 miles");

        Assert.Equal(new[] { "Populated places", "Aviation", "Terrain" }, tree.Select(g => g.Name));
        Assert.Equal(new[] { "Major cities, within 200 miles", "Capitals", "Towns", "Villages", "Neighborhoods" },
            tree[0].Items.Select(i => i.Name));
        Assert.True(tree[0].Items[0].IsMajorCities);
        Assert.Equal(new[] { "Town:neighborhood", "Village:neighborhood" }, tree[0].Items[4].TypeKeys.OrderBy(k => k));
        Assert.Equal(new[] { "Airports", "Heliports" }, tree[1].Items.Select(i => i.Name));
        Assert.All(tree, g => Assert.DoesNotContain(g.Items, i => i.Name == g.Name));
    }

    [Fact]
    public void Major_cities_reach_their_own_range_even_with_their_kind_unticked()
    {
        var places = new[]
        {
            Place(1, "Trollhättan", 58.28, 12.29, LocationCategory.Town, "town", 48573),   // at the point
            Place(2, "Gothenburg", 57.70, 11.97, LocationCategory.Town, "town", 572799),   // about 37 nm south
            Place(3, "Majorna", 57.69, 11.92, LocationCategory.Town, "neighborhood", 90000), // a part of Gothenburg
            Place(4, "Borås", 57.72, 12.94, LocationCategory.Town, "town", 66273),         // about 40 nm
        };
        var hidden = new HashSet<string> { "Town:town", "Town:neighborhood" };
        var cities = new MajorCityFilter(50000, 39.0);

        var shown = PlaceFilter.Apply(places, 58.28, 12.29, hidden, "", null, 15.0, cities);
        Assert.Equal(new[] { 2 }, shown.Select(i => places[i].Id)); // Borås is past the cities' 39 nm

        var allTowns = PlaceFilter.Apply(places, 58.28, 12.29, None, "", null, 15.0, cities);
        Assert.Equal(new[] { 1, 2 }, allTowns.Select(i => places[i].Id)); // the towns' 15 nm, the city's 39

        Assert.Equal(new[] { 1 }, PlaceFilter.Apply(places, 58.28, 12.29, None, "", null, 15.0).Select(i => places[i].Id));
    }

    [Fact]
    public void Moving_the_map_position_reorders_the_list()
    {
        var shown = PlaceFilter.Apply(Places, 57.93333, 11.66667, None, "", null, null);
        Assert.Equal(1, Places[shown[0]].Id);
    }

    private static AirportSearchEntry Nav(string ident, string name, string city, double lat, double lon) =>
        new(ident, name, city, "", "", 5000, false, lat, lon);

    [Fact]
    public void An_airport_matches_the_ident_geonames_lists_for_it()
    {
        // Kuwait: the navigation database lists the old and the new ident at one field.
        var matcher = new AirportMatcher(new[]
        {
            Nav("OKBK", "Kuwait Intl", "Kuwait", 29.2266, 47.9689),
            Nav("OKKK", "Kuwait Intl", "Kuwait", 29.2267, 47.9690),
        });
        Assert.Equal("OKKK", matcher.Match("Kuwait International Airport", "OKKK,KWI", 29.22, 47.97)!.Ident);
    }

    [Fact]
    public void Without_a_listed_ident_the_nearest_airport_sharing_a_name_word_matches()
    {
        var matcher = new AirportMatcher(new[]
        {
            Nav("ESGT", "Trollhattan-Vanersborg", "Trollhattan", 58.31804, 12.345),
            Nav("ES-0001", "Some Strip", "Elsewhere", 58.315, 12.34),
        });
        var match = matcher.Match("Trollhättan–Vänersborg Airport", "", 58.31806, 12.345);
        Assert.Equal("ESGT", match!.Ident);
    }

    [Fact]
    public void Distance_alone_never_makes_a_match()
    {
        var matcher = new AirportMatcher(new[] { Nav("ES-0001", "Some Strip", "Elsewhere", 58.315, 12.34) });
        Assert.Null(matcher.Match("Trollhättan–Vänersborg Airport", "", 58.31806, 12.345));
    }

    [Fact]
    public void An_airport_beyond_five_kilometres_never_matches()
    {
        var matcher = new AirportMatcher(new[] { Nav("ESGT", "Trollhattan-Vanersborg", "Trollhattan", 58.40, 12.345) });
        Assert.Null(matcher.Match("Trollhättan–Vänersborg Airport", "ESGT", 58.31806, 12.345));
    }

    [Fact]
    public void Generic_words_say_nothing_about_which_airport()
    {
        Assert.Equal(new[] { "trollhattan", "vanersborg" },
            AirportMatcher.Words("Trollhättan–Vänersborg International Airport").OrderBy(w => w));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void The_columns_read_the_same_under_any_culture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("57.93333, 11.66667", LocationPhrases.Coordinates(57.93333, 11.66667));
            Assert.Equal("Population 48,573", LocationPhrases.PlaceDetails(LocationCategory.Town, 48573, null));
            Assert.Equal("23 miles northwest", LocationPhrases.DistanceAndDirection(20.0, 315, false));
            Assert.Equal("3.7 kilometers east", LocationPhrases.DistanceAndDirection(2.0, 90, true));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Details_say_an_airports_ident_or_that_it_was_not_found()
    {
        Assert.Equal("ESGT, in simulator", LocationPhrases.PlaceDetails(LocationCategory.Airport, 0, "ESGT"));
        Assert.Equal("Not found in simulator", LocationPhrases.PlaceDetails(LocationCategory.Airport, 0, null));
        Assert.Equal("", LocationPhrases.PlaceDetails(LocationCategory.Terrain, 0, null));
    }

    [Fact]
    public void The_first_box_adds_the_map_position_and_the_major_cities()
    {
        var where = new WhereYouAre { State = "Västra Götaland", Country = "Sweden" };
        var cities = new List<NearbyPlace>
        {
            new() { Name = "Gothenburg", State = "Västra Götaland", Country = "Sweden", Population = 572799, Distance = 38, Direction = "south", Type = "major_city" },
        };
        string following = LocationPhrases.LocationInfoText(where, null, 0, 0, cities, false);
        Assert.StartsWith("In: Västra Götaland, Sweden", following);
        Assert.DoesNotContain("Map position", following);
        Assert.Contains("Major cities:\r\nGothenburg", following);

        string away = LocationPhrases.LocationInfoText(where, "Krokholmen", 20.0, 225, cities, false);
        Assert.Contains("\r\nMap position: Krokholmen, 23 miles southwest of the aircraft", away);
        Assert.Contains("Major cities around the map position:", away);

        Assert.Contains("\r\nMap position: Krokholmen, where the aircraft is",
            LocationPhrases.LocationInfoText(where, "Krokholmen", 0.0, 0, cities, false));
        // The spot where the aircraft was when following was turned off has no name of its own.
        Assert.Contains("\r\nMap position: the aircraft's position, not following it",
            LocationPhrases.LocationInfoText(where, "", 0.0, 0, cities, false));
        Assert.Contains("\r\nMap position: where the aircraft was, now 2.3 miles south of it",
            LocationPhrases.LocationInfoText(where, "", 2.0, 180, cities, false));
    }
}
