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
        State = "Maryland", Country = "United States", StateIsNearest = true,
        TimeZone = "Eastern Daylight Time",
    };

    [Fact]
    public void Over_a_bay_the_water_comes_first()
    {
        Assert.Equal(
            "Over Chesapeake Bay, coast 5.2 miles west. Near Dares Beach, Maryland, United States, 5.4 miles west",
            LocationPhrases.NearestCitySentence(OverChesapeake, DaresBeach, kilometres: false, nearbyRange: 25));
    }

    [Fact]
    public void Open_ocean_without_a_town_says_the_ocean_and_the_coast()
    {
        var atlantic = new WhereYouAre { Water = WaterKind.Sea, WaterName = "North Atlantic Ocean", EdgeNm = Nm(368), EdgeBearingTrue = 180 };
        Assert.Equal("Over North Atlantic Ocean, coast 368 miles south",
            LocationPhrases.NearestCitySentence(atlantic, null, false, 25));
    }

    [Fact]
    public void Inland_is_todays_sentence_without_the_coast()
    {
        var inland = new WhereYouAre { State = "Virginia", Country = "United States", EdgeNm = Nm(117), EdgeBearingTrue = 45 };
        Assert.Equal("Near Laurel Grove, Virginia, United States, 1.6 miles north",
            LocationPhrases.NearestCitySentence(inland, LaurelGrove, false, 25));
    }

    [Fact]
    public void Over_land_the_coast_is_added_within_50_miles()
    {
        var nearCoast = new WhereYouAre { EdgeNm = Nm(2.6), EdgeBearingTrue = 0 };
        Assert.Equal("Near Laurel Grove, Virginia, United States, 1.6 miles north, coast 2.6 miles north",
            LocationPhrases.NearestCitySentence(nearCoast, LaurelGrove, false, 25));

        var at51 = nearCoast with { EdgeNm = Nm(51) };
        Assert.DoesNotContain("coast", LocationPhrases.NearestCitySentence(at51, LaurelGrove, false, 25));
    }

    [Fact]
    public void Over_land_the_coast_threshold_is_80_km_in_kilometres()
    {
        var at70km = new WhereYouAre { EdgeNm = 70 / LocationPhrases.KmPerNm, EdgeBearingTrue = 90 };
        Assert.EndsWith(", coast 70 kilometers east", LocationPhrases.NearestCitySentence(at70km, LaurelGrove, true, 40));
        var at90km = at70km with { EdgeNm = 90 / LocationPhrases.KmPerNm };
        Assert.DoesNotContain("coast", LocationPhrases.NearestCitySentence(at90km, LaurelGrove, true, 40));
    }

    [Fact]
    public void Over_a_lake_the_edge_is_the_shore()
    {
        var lake = new WhereYouAre { Water = WaterKind.Lake, WaterName = "Lake Michigan", EdgeNm = Nm(20), EdgeBearingTrue = 90 };
        Assert.Equal("Over Lake Michigan, shore 20 miles east", LocationPhrases.NearestCitySentence(lake, null, false, 25));
    }

    [Fact]
    public void Land_without_a_town_in_range_still_answers()
    {
        var empty = new WhereYouAre { State = "Tamanrasset", Country = "Algeria", EdgeNm = Nm(700), EdgeBearingTrue = 0 };
        Assert.Equal("No town within 25 miles, Tamanrasset, Algeria", LocationPhrases.NearestCitySentence(empty, null, false, 25));
    }

    [Fact]
    public void Where_you_are_box_over_the_bay()
    {
        Assert.Equal(
            "Over Chesapeake Bay\r\nCoast: 5.2 miles west\r\nNearest state: Maryland, United States\r\nTime zone: Eastern Daylight Time",
            LocationPhrases.WhereYouAreText(OverChesapeake, false));
    }

    [Fact]
    public void Where_you_are_box_over_land_puts_the_state_first_and_the_close_coast_after_the_region()
    {
        var alps = new WhereYouAre
        {
            State = "Graubünden", Country = "Switzerland", Region = "Alps", RegionKind = "mountain range",
            EdgeNm = Nm(130), EdgeBearingTrue = 135, TimeZone = "W. Europe Daylight Time",
        };
        Assert.Equal("State: Graubünden, Switzerland\r\nRegion: Alps, mountain range\r\nTime zone: W. Europe Daylight Time",
            LocationPhrases.WhereYouAreText(alps, false));

        var coastal = alps with { EdgeNm = Nm(12) };
        Assert.Contains("Region: Alps, mountain range\r\nCoast: 12 miles southeast\r\n", LocationPhrases.WhereYouAreText(coastal, false));
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
            Assert.Contains("coast 5.2 miles west", s);
            Assert.Contains("5.4 miles west", s);
            Assert.Contains("Coast: 5.2 miles west", LocationPhrases.WhereYouAreText(OverChesapeake, false));
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
