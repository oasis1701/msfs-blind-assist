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
    [InlineData("PPLZ")] // a populated code we have not listed
    public void Drops_historical_abandoned_destroyed_and_unlisted_places(string code)
    {
        Assert.False(LocationFeatureCatalog.TryClassify("P", code, out _, out _));
    }

    // Our own kinds (agreed list, 2026-11-06): GeoNames' codes, merged where one kind covers several.
    [Theory]
    [InlineData("S", "AIRP", "Airport", "Airports")]
    [InlineData("S", "AIRF", "Airfield", "Airfields")]
    [InlineData("S", "AIRH", "Heliport", "Heliports")]
    [InlineData("T", "ATOL", "Atoll", "Atolls")]
    [InlineData("T", "CAPE", "Cape", "Capes")]
    [InlineData("T", "HLL", "Hill", "Hills")]
    [InlineData("T", "HLLS", "Hill", "Hills")]
    [InlineData("T", "ISL", "Island", "Islands")]
    [InlineData("T", "ISLS", "Island", "Islands")]
    [InlineData("T", "ISLET", "Islet", "Islets")]
    [InlineData("T", "MT", "Mountain", "Mountains")]
    [InlineData("T", "MTS", "Mountain", "Mountains")]
    [InlineData("T", "PK", "Peak", "Peaks")]
    [InlineData("T", "PKS", "Peak", "Peaks")]
    [InlineData("T", "PEN", "Peninsula", "Peninsulas")]
    [InlineData("T", "PT", "Point", "Points")]
    [InlineData("T", "PTS", "Point", "Points")]
    [InlineData("T", "VAL", "Valley", "Valleys")]
    [InlineData("T", "VALS", "Valley", "Valleys")]
    [InlineData("H", "BAY", "Bay", "Bays")]
    [InlineData("H", "BAYS", "Bay", "Bays")]
    [InlineData("T", "BCH", "Beach", "Beaches")]
    [InlineData("T", "BCHS", "Beach", "Beaches")]
    [InlineData("H", "BGHT", "Bight", "Bights")]
    [InlineData("H", "CHN", "Channel", "Channels")]
    [InlineData("H", "COVE", "Cove", "Coves")]
    [InlineData("H", "ESTY", "Estuary", "Estuaries")]
    [InlineData("H", "FJD", "Fjord", "Fjords")]
    [InlineData("H", "GULF", "Gulf", "Gulfs")]
    [InlineData("H", "HBR", "Harbor", "Harbors")]
    [InlineData("H", "INLT", "Inlet", "Inlets")]
    [InlineData("H", "LGN", "Lagoon", "Lagoons")]
    [InlineData("H", "LGNS", "Lagoon", "Lagoons")]
    [InlineData("H", "LK", "Lake", "Lakes")]
    [InlineData("H", "LKS", "Lake", "Lakes")]
    [InlineData("H", "OCN", "Ocean", "Oceans")]
    [InlineData("H", "RF", "Reef", "Reefs")]
    [InlineData("H", "RSV", "Reservoir", "Reservoirs")]
    [InlineData("H", "STM", "River or stream", "Rivers and streams")]
    [InlineData("H", "STMS", "River or stream", "Rivers and streams")]
    [InlineData("H", "SEA", "Sea", "Seas")]
    [InlineData("H", "SHOL", "Shoal or bank", "Shoals and banks")]
    [InlineData("H", "BNK", "Shoal or bank", "Shoals and banks")]
    [InlineData("H", "SD", "Sound", "Sounds")]
    [InlineData("H", "STRT", "Strait", "Straits")]
    [InlineData("S", "AMTH", "Amphitheater", "Amphitheaters")]
    [InlineData("S", "LTHSE", "Lighthouse", "Lighthouses")]
    [InlineData("S", "MNMT", "Monument", "Monuments")]
    [InlineData("S", "MUS", "Museum", "Museums")]
    [InlineData("L", "PRK", "Park", "Parks")]
    [InlineData("S", "PIER", "Pier", "Piers")]
    [InlineData("S", "STDM", "Stadium", "Stadiums")]
    [InlineData("S", "TOWR", "Tower", "Towers")]
    [InlineData("S", "ZOO", "Zoo", "Zoos")]
    public void Every_kept_code_has_our_own_name(string cls, string code, string singular, string plural)
    {
        Assert.True(LocationFeatureCatalog.TryClassify(cls, code, out var category, out _));
        var kind = LocationFeatureCatalog.KindOf(category, code);
        Assert.Equal(singular, kind.Singular);
        Assert.Equal(plural, kind.Plural);
    }

    [Theory]
    [InlineData("VLY")]  // GeoNames' valley is VAL
    [InlineData("STAD")] // its stadium is STDM
    [InlineData("RSVR")]
    [InlineData("PRKS")]
    public void Codes_geonames_does_not_publish_are_not_listed(string code)
    {
        Assert.False(LocationFeatureCatalog.TryClassify("T", code, out _, out _));
        Assert.False(LocationFeatureCatalog.TryClassify("S", code, out _, out _));
    }

    [Theory]
    [InlineData("PPLC", 0, "Capital")]
    [InlineData("PPLG", 900, "Capital")]
    [InlineData("PPLCD", 900, "Capital")]
    [InlineData("PPLA", 0, "Regional capital")]
    [InlineData("PPLA2", 0, "Town")]
    [InlineData("PPLA5", 0, "Town")]
    [InlineData("PPL", 1238, "Town")]
    [InlineData("PPLF", 600, "Town")]
    [InlineData("PPLL", 600, "Town")]
    [InlineData("PPLR", 600, "Town")]
    [InlineData("PPLS", 600, "Town")]
    [InlineData("STLMT", 4596, "Town")]
    [InlineData("PPLX", 90000, "Neighborhood")]
    [InlineData("PPL", 0, "Village")]
    [InlineData("PPLG", 0, "Village")] // a capital only as a town
    [InlineData("STLMT", 300, "Village")]
    [InlineData("PPLX", 0, "Neighborhood")]
    public void Populated_places_are_capitals_towns_villages_or_neighborhoods(string code, long population, string singular)
    {
        Assert.True(LocationFeatureCatalog.TryClassify("P", code, out _, out bool populated));
        Assert.True(populated);
        var category = LocationFeatureCatalog.PopulatedCategory(code, population);
        Assert.Equal(singular, LocationFeatureCatalog.KindOf(category, code).Singular);
    }

    [Fact]
    public void Our_names_are_written_as_the_window_shows_them()
    {
        var kinds = LocationFeatureCatalog.Kinds;
        Assert.Equal(kinds.Count, kinds.Select(k => k.Key).Distinct().Count());
        Assert.All(kinds, k =>
        {
            Assert.True(char.IsUpper(k.Singular[0]) && char.IsUpper(k.Plural[0]), k.Key);
            Assert.DoesNotContain("(", k.Singular + k.Plural);
        });
        Assert.Equal(0, LocationFeatureCatalog.OrderOf(LocationFeatureCatalog.Capital.Key));
        Assert.Equal(int.MaxValue, LocationFeatureCatalog.OrderOf("no-such-kind"));
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
