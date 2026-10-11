using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class GeoNamesNameFilesTests
{
    [Fact]
    public void Country_names_come_from_column_4_and_comments_are_skipped()
    {
        var names = GeoNamesNameFiles.ParseCountryInfo(new[]
        {
            "# GeoNames.org Country Information",
            "#ISO\tISO3\tISO-Numeric\tfips\tCountry\tCapital",
            "US\tUSA\t840\tUS\tUnited States\tWashington",
            "CH\tCHE\t756\tSZ\tSwitzerland\tBern",
            "bad line",
        });
        Assert.Equal("United States", names["US"]);
        Assert.Equal("Switzerland", names["CH"]);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void State_names_are_keyed_country_dot_code()
    {
        var names = GeoNamesNameFiles.ParseAdmin1(new[]
        {
            "US.MD\tMaryland\tMaryland\t4361885",
            "CH.GR\tGrisons\tGrisons\t2660522",
            "XX\tNo dot\tNo dot\t1",
        });
        Assert.Equal("Maryland", names[GeoNamesNameFiles.Admin1Key("US", "MD")]);
        Assert.Equal("Grisons", names["CH.GR"]);
        Assert.Equal(2, names.Count);
    }
}
