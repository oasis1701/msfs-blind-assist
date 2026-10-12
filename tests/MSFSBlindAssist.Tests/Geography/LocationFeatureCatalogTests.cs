using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class LocationFeatureCatalogTests
{
    [Theory]
    [InlineData("S", "AIRP", LocationCategory.Airport)]
    [InlineData("S", "AIRH", LocationCategory.Airport)]
    [InlineData("T", "MT", LocationCategory.Terrain)]
    [InlineData("T", "ISL", LocationCategory.Terrain)]
    [InlineData("T", "CAPE", LocationCategory.Terrain)]
    [InlineData("H", "LK", LocationCategory.Water)]
    [InlineData("H", "STM", LocationCategory.Water)]
    [InlineData("T", "BCH", LocationCategory.Water)]
    [InlineData("H", "STRT", LocationCategory.Water)]
    [InlineData("H", "GULF", LocationCategory.Water)]
    [InlineData("S", "MUS", LocationCategory.Landmark)]
    [InlineData("L", "PRK", LocationCategory.Landmark)]
    [InlineData("U", "SMU", LocationCategory.Undersea)]
    public void Keeps_every_code_the_online_lookups_used_and_the_coast_names(string cls, string code, LocationCategory expected)
    {
        Assert.True(LocationFeatureCatalog.TryClassify(cls, code, out var category, out bool populated));
        Assert.False(populated);
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData("S", "SCH")]
    [InlineData("S", "CH")]
    [InlineData("A", "ADM1")]
    [InlineData("R", "RD")]
    [InlineData("V", "FRST")]
    [InlineData("", "LK")]
    public void Drops_what_location_info_never_shows(string cls, string code)
    {
        Assert.False(LocationFeatureCatalog.TryClassify(cls, code, out _, out _));
    }

    [Theory]
    [InlineData("PPLH")]
    [InlineData("PPLQ")]
    [InlineData("PPLW")]
    [InlineData("PPLCH")]
    public void Drops_historical_abandoned_and_destroyed_places(string code)
    {
        Assert.False(LocationFeatureCatalog.TryClassify("P", code, out _, out _));
    }

    [Fact]
    public void Populated_places_are_flagged_for_the_town_split()
    {
        Assert.True(LocationFeatureCatalog.TryClassify("P", "PPL", out _, out bool populated));
        Assert.True(populated);
    }

    [Theory]
    [InlineData("PPL", 500, LocationCategory.Town)]
    [InlineData("PPL", 499, LocationCategory.Village)]
    [InlineData("PPL", 0, LocationCategory.Village)]
    [InlineData("PPLA2", 0, LocationCategory.Town)]
    [InlineData("PPLC", 0, LocationCategory.Town)]
    public void Town_means_500_people_or_a_seat(string code, long population, LocationCategory expected)
    {
        Assert.Equal(expected, LocationFeatureCatalog.PopulatedCategory(code, population));
    }
}
