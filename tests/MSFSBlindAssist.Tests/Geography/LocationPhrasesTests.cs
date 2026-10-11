using System.Globalization;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

/// <summary>The sentences the owner approved on 2026-10-10, at positions from a real flight.</summary>
public class LocationPhrasesTests
{
    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static double Nm(double miles) => miles / LocationPhrases.MilesPerNm;

    private static readonly PlaceHit DaresBeach = new("Dares Beach", "Maryland", "United States", Nm(5.4), 270);
    private static readonly PlaceHit LaurelGrove = new("Laurel Grove", "Virginia", "United States", Nm(1.6), 0);

    private static readonly WhereYouAre OverChesapeake = new()
    {
        Water = WaterKind.Sea, WaterName = "Chesapeake Bay", EdgeNm = Nm(5.2), EdgeBearingTrue = 270,
        State = "Maryland", Country = "United States",
        TimeZone = "Eastern Daylight Time",
    };

    [Fact]
    public void Over_a_bay_the_water_comes_first_then_the_nearest_land()
    {
        Assert.Equal(
            "Over Chesapeake Bay, nearest land 5.2 miles west. Near Dares Beach, Maryland, United States, 5.4 miles west",
            LocationPhrases.NearestCitySentence(OverChesapeake, DaresBeach, kilometres: false, nearbyRange: 25));
    }

    [Fact]
    public void Open_ocean_without_a_town_says_the_ocean_and_the_nearest_land()
    {
        var atlantic = new WhereYouAre { Water = WaterKind.Sea, WaterName = "North Atlantic Ocean", EdgeNm = Nm(368), EdgeBearingTrue = 180 };
        Assert.Equal("Over North Atlantic Ocean, nearest land 368 miles south",
            LocationPhrases.NearestCitySentence(atlantic, null, false, 25));
    }

    [Fact]
    public void Inland_is_todays_sentence_without_the_coast()
    {
        // Beyond the Coast Range the service finds no coast, so there is no edge to say.
        var inland = new WhereYouAre { State = "Virginia", Country = "United States" };
        Assert.Equal("Near Laurel Grove, Virginia, United States, 1.6 miles north",
            LocationPhrases.NearestCitySentence(inland, LaurelGrove, false, 25));
    }

    [Fact]
    public void Over_land_a_coast_the_service_found_is_added()
    {
        var nearCoast = new WhereYouAre { EdgeNm = Nm(2.6), EdgeBearingTrue = 0 };
        Assert.Equal("Near Laurel Grove, Virginia, United States, 1.6 miles north, coast 2.6 miles north",
            LocationPhrases.NearestCitySentence(nearCoast, LaurelGrove, false, 25));

        // A pilot who widened the Coast Range hears a far coast too: the phrases never filter by distance.
        var far = nearCoast with { EdgeNm = Nm(240) };
        Assert.EndsWith(", coast 240 miles north", LocationPhrases.NearestCitySentence(far, LaurelGrove, false, 25));
    }

    [Fact]
    public void Over_land_the_coast_is_named_by_its_sea_when_known()
    {
        var brighton = new WhereYouAre { EdgeNm = Nm(1.2), EdgeBearingTrue = 180, CoastWater = "English Channel" };
        var place = new PlaceHit("Brighton", "England", "United Kingdom", Nm(0.5), 0);
        Assert.Equal("Near Brighton, England, United Kingdom, 0.5 miles north, English Channel coast 1.2 miles south",
            LocationPhrases.NearestCitySentence(brighton, place, false, 25));
    }

    [Fact]
    public void Over_land_the_coast_is_said_in_kilometres_when_selected()
    {
        var at70km = new WhereYouAre { EdgeNm = 70 / LocationPhrases.KmPerNm, EdgeBearingTrue = 90 };
        Assert.EndsWith(", coast 70 kilometers east", LocationPhrases.NearestCitySentence(at70km, LaurelGrove, true, 40));
    }

    [Fact]
    public void Over_a_lake_the_edge_is_its_shore()
    {
        var lake = new WhereYouAre
        {
            Water = WaterKind.Lake, WaterName = "Lake Michigan", EdgeNm = Nm(20), EdgeBearingTrue = 90,
            State = "Michigan", Country = "United States",
        };
        Assert.Equal("Over Lake Michigan, shore 20 miles east", LocationPhrases.NearestCitySentence(lake, null, false, 25));
        Assert.StartsWith("Over Lake Michigan\r\nShore: 20 miles east\r\nNear: Michigan, United States",
            LocationPhrases.WhereYouAreText(lake, false));
    }

    [Fact]
    public void Land_without_a_town_in_range_still_answers()
    {
        var empty = new WhereYouAre { State = "Tamanrasset", Country = "Algeria" };
        Assert.Equal("No town within 25 miles, Tamanrasset, Algeria", LocationPhrases.NearestCitySentence(empty, null, false, 25));
    }

    [Fact]
    public void Where_you_are_box_over_the_bay()
    {
        Assert.Equal(
            "Over Chesapeake Bay\r\nNearest land: 5.2 miles west\r\nNear: Maryland, United States\r\nTime zone: Eastern Daylight Time",
            LocationPhrases.WhereYouAreText(OverChesapeake, false));
    }

    [Fact]
    public void Where_you_are_box_over_land_puts_the_area_first_and_the_close_coast_after_the_region()
    {
        var alps = new WhereYouAre
        {
            State = "Grisons", Country = "Switzerland", Region = "Alps", RegionKind = "mountain range",
            EdgeBearingTrue = 135, TimeZone = "W. Europe Daylight Time", // no coast within the Coast Range
        };
        Assert.Equal("In: Grisons, Switzerland\r\nRegion: Alps, mountain range\r\nTime zone: W. Europe Daylight Time",
            LocationPhrases.WhereYouAreText(alps, false));

        var coastal = alps with { EdgeNm = Nm(12) };
        Assert.Contains("Region: Alps, mountain range\r\nCoast: 12 miles southeast\r\n", LocationPhrases.WhereYouAreText(coastal, false));

        var named = coastal with { CoastWater = "Mediterranean Sea" };
        Assert.Contains("\r\nCoast: Mediterranean Sea, 12 miles southeast\r\n", LocationPhrases.WhereYouAreText(named, false));
    }

    [Fact]
    public void A_country_without_a_state_reads_just_the_country()
    {
        Assert.StartsWith("In: Monaco\r\n", LocationPhrases.WhereYouAreText(new WhereYouAre { Country = "Monaco", TimeZone = "x" }, false));
    }

    [Fact]
    public void Where_you_are_box_is_never_empty()
    {
        Assert.Equal("No details for this position.", LocationPhrases.WhereYouAreText(new WhereYouAre(), false));
    }

    [Theory]
    [InlineData(0, "north")]
    [InlineData(22, "north")]
    [InlineData(23, "northeast")]
    [InlineData(180, "south")]
    [InlineData(337, "northwest")]
    [InlineData(338, "north")]
    [InlineData(360, "north")]
    public void Direction_uses_the_eight_point_compass(double bearing, string expected)
    {
        Assert.Equal(expected, LocationPhrases.Direction(bearing));
    }

    [Theory]
    [InlineData(9.94, "9.9 miles")]
    [InlineData(9.96, "10 miles")]
    [InlineData(367.6, "368 miles")]
    public void Edge_distance_has_one_decimal_under_ten(double miles, string expected)
    {
        Assert.Equal(expected, LocationPhrases.EdgeDistance(Nm(miles), false));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Sentences_keep_a_decimal_point_under_a_comma_decimal_culture(string culture)
    {
        UnderCulture(culture, () =>
        {
            string s = LocationPhrases.NearestCitySentence(OverChesapeake, DaresBeach, false, 25);
            Assert.Contains("nearest land 5.2 miles west", s);
            Assert.Contains("5.4 miles west", s);
            Assert.Contains("Nearest land: 5.2 miles west", LocationPhrases.WhereYouAreText(OverChesapeake, false));
        });
    }

    [Theory]
    [InlineData("Continent", false)]
    [InlineData("Geoarea", false)]
    [InlineData("", false)]
    [InlineData("Range/mtn", true)]
    [InlineData("Desert", true)]
    public void Continents_and_geoareas_are_not_spoken_as_regions(string featureClass, bool shown)
    {
        Assert.Equal(shown, LocationPhrases.IsRegionShown(featureClass));
    }

    [Theory]
    [InlineData("Range/mtn", "mountain range")]
    [InlineData("Pen/cape", "peninsula")]
    [InlineData("Foothills", "foothills")]
    public void Region_kinds_are_spoken_words(string featureClass, string expected)
    {
        Assert.Equal(expected, LocationPhrases.RegionKind(featureClass));
    }

    [Theory]
    [InlineData("ALPS", "Alps")]
    [InlineData("GREAT LAKES", "Great Lakes")]
    [InlineData("Wasatch Range", "Wasatch Range")]
    public void All_caps_region_names_are_tidied(string raw, string expected)
    {
        Assert.Equal(expected, LocationPhrases.TidyName(raw));
    }
}
