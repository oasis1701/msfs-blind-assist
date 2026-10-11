using System.Globalization;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class GeoNamesDumpLineTests
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

    // Real rows from allCountries.txt (alternate names trimmed).
    private const string DaresBeach =
        "4354234\tDares Beach\tDares Beach\t\t38.55179\t-76.48357\tP\tPPL\tUS\t\tMD\t009\t\t\t1238\t3\t2\tAmerica/New_York\t2011-05-14";
    private const string ChesapeakeBay =
        "4351177\tChesapeake Bay\tChesapeake Bay\t\t37.9\t-76.2\tH\tBAY\tUS\t\tVA\t\t\t\t0\t\t-9999\tAmerica/New_York\t2017-12-31";
    private const string Hamlet =
        "4462070\tKillian Crossroads\tKillian Crossroads\t\t35.56\t-81.07\tP\tPPL\tUS\t\tNC\t109\t\t\t0\t\t262\tAmerica/New_York\t2006-01-15";
    private const string School =
        "4459999\tSome School\tSome School\t\t35.5\t-81.0\tS\tSCH\tUS\t\tNC\t\t\t\t0\t\t\tAmerica/New_York\t2006-01-15";

    [Fact]
    public void Keeps_a_town_with_its_fields()
    {
        Assert.True(GeoNamesDumpLine.TryParse(DaresBeach, out var row));
        Assert.Equal(4354234, row.Id);
        Assert.Equal("Dares Beach", row.Name);
        Assert.Equal(38.55179, row.Latitude, 6);
        Assert.Equal(-76.48357, row.Longitude, 6);
        Assert.Equal(LocationCategory.Town, row.Category);
        Assert.Equal("PPL", row.FeatureCode);
        Assert.Equal("US", row.CountryCode);
        Assert.Equal("MD", row.Admin1);
        Assert.Equal(1238, row.Population);
    }

    [Fact]
    public void A_place_under_500_people_is_a_village()
    {
        Assert.True(GeoNamesDumpLine.TryParse(Hamlet, out var row));
        Assert.Equal(LocationCategory.Village, row.Category);
    }

    [Fact]
    public void A_bay_is_water()
    {
        Assert.True(GeoNamesDumpLine.TryParse(ChesapeakeBay, out var row));
        Assert.Equal(LocationCategory.Water, row.Category);
    }

    [Fact]
    public void Rows_outside_the_catalog_are_rejected()
    {
        Assert.False(GeoNamesDumpLine.TryParse(School, out _));
    }

    [Fact]
    public void Short_and_malformed_rows_are_rejected()
    {
        Assert.False(GeoNamesDumpLine.TryParse("", out _));
        Assert.False(GeoNamesDumpLine.TryParse("1\tOnly\tthree", out _));
        Assert.False(GeoNamesDumpLine.TryParse(DaresBeach.Replace("38.55179", "north"), out _));
        Assert.False(GeoNamesDumpLine.TryParse(DaresBeach.Replace("38.55179", "95.0"), out _));
    }

    [Fact]
    public void A_row_ending_at_the_population_column_still_parses()
    {
        string truncated = string.Join('\t', DaresBeach.Split('\t').Take(15));
        Assert.True(GeoNamesDumpLine.TryParse(truncated, out var row));
        Assert.Equal(1238, row.Population);
    }

    [Fact]
    public void Falls_back_to_the_ascii_name_when_the_name_is_blank()
    {
        string blankName = DaresBeach.Replace("\tDares Beach\tDares Beach\t", "\t \tDares Beach\t");
        Assert.True(GeoNamesDumpLine.TryParse(blankName, out var row));
        Assert.Equal("Dares Beach", row.Name);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Coordinates_parse_the_same_under_a_comma_decimal_culture(string culture)
    {
        UnderCulture(culture, () =>
        {
            Assert.True(GeoNamesDumpLine.TryParse(DaresBeach, out var row));
            Assert.Equal(38.55179, row.Latitude, 6);
            Assert.Equal(-76.48357, row.Longitude, 6);
        });
    }
}
