using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

/// <summary>
/// Builds the <see cref="GeographyFixtureWorld"/> with the real builder and queries it with the
/// real service: a bay east of a Maryland shoreline, a lake, a mountain range, a few places.
/// </summary>
public sealed class OfflineLocationServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-geo-" + Guid.NewGuid().ToString("N"));
    private readonly LocationDataStore _store;
    private static readonly LocationQuerySettings Miles = new(false, 25, 50, 185, 30, 50000, 10, 10, 8, 8, 8, 8);

    public OfflineLocationServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new LocationDataStore(Path.Combine(_dir, "location-data.sqlite"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private LocationBuildInputs WriteInputs(int minimumRows = 1) => GeographyFixtureWorld.WriteInputs(_dir, minimumRows);

    private OfflineLocationService Built()
    {
        new LocationDataBuilder(_store).Build(WriteInputs(), null, CancellationToken.None);
        return new OfflineLocationService(_store);
    }

    [Fact]
    public void Without_a_database_nothing_is_available()
    {
        var service = new OfflineLocationService(_store);
        Assert.False(service.IsAvailable);
        Assert.Null(service.GetNearest(38.55, -76.42, Miles));
    }

    [Fact]
    public void A_build_installs_a_complete_database_and_leaves_no_build_files()
    {
        Built();
        Assert.True(_store.IsAvailable);
        Assert.Equal(GeographyFixtureWorld.KeptPlaces, _store.Info!.PlaceCount); // the school is not kept
        Assert.Equal("2026-10-10", _store.Info.GeoNamesDate);
        Assert.False(File.Exists(_store.StagingPath));
        Assert.False(File.Exists(_store.BuildingPath));
        Assert.False(File.Exists(_store.ReadyPath));
    }

    [Fact]
    public void Over_the_bay_the_sea_the_nearest_land_and_the_nearby_state_are_found()
    {
        var report = Built().GetNearest(38.55, -76.42, Miles)!;
        Assert.Equal(WaterKind.Sea, report.Where.Water);
        Assert.Equal("Test Bay", report.Where.WaterName);
        Assert.InRange(report.Where.EdgeNm!.Value, 8.0, 9.0); // 0.18° of longitude at 38.55°N ≈ 8.4 nm
        Assert.Equal("west", LocationPhrases.Direction(report.Where.EdgeBearingTrue));
        Assert.Equal("Maryland", report.Where.State);
        Assert.Equal("United States", report.Where.Country);
        Assert.Equal("Bayside", report.Nearest!.Name);
        Assert.StartsWith("Over Test Bay, nearest land 9.7 miles west. Near Bayside, Maryland, United States, ",
            LocationPhrases.NearestCitySentence(report.Where, report.Nearest, false, 25));
        Assert.Contains("Near: Maryland, United States", LocationPhrases.WhereYouAreText(report.Where, false));
    }

    [Fact]
    public void Over_land_the_area_is_spelled_as_geonames_spells_it()
    {
        var report = Built().GetNearest(38.9, -76.9, Miles)!;
        Assert.Equal(WaterKind.None, report.Where.Water);
        Assert.Equal("Maryland", report.Where.State); // not "State of Maryland", nor the misleading gn_a1_code
        Assert.Equal("United States", report.Where.Country); // not "United States of America"
        Assert.NotNull(report.Where.EdgeNm); // the coast is 16 miles away: within the 50-mile land limit
        Assert.StartsWith("In: Maryland, United States", LocationPhrases.WhereYouAreText(report.Where, false));
    }

    [Fact]
    public void Over_land_the_coast_is_named_by_the_sea_past_it()
    {
        var report = Built().GetNearest(38.9, -76.9, Miles)!;
        Assert.Equal("Test Bay", report.Where.CoastWater);
        Assert.Contains("Coast: Test Bay, ", LocationPhrases.WhereYouAreText(report.Where, false));
        Assert.Contains(", Test Bay coast ", LocationPhrases.NearestCitySentence(report.Where, report.Nearest, false, 25));
    }

    [Fact]
    public void Over_land_the_coast_is_said_only_within_the_coast_range()
    {
        var service = Built();
        // The fixture coast is about 16 miles (26 km) from this point.
        Assert.NotNull(service.GetNearest(38.9, -76.9, Miles)!.Where.EdgeNm);                       // default 50 miles
        var narrow = service.GetNearest(38.9, -76.9, Miles with { CoastRange = 10 })!;
        Assert.Null(narrow.Where.EdgeNm);
        Assert.DoesNotContain("coast", LocationPhrases.NearestCitySentence(narrow.Where, narrow.Nearest, false, 25));
        Assert.DoesNotContain("Coast:", LocationPhrases.WhereYouAreText(narrow.Where, false));

        var km = Miles with { Kilometres = true, NearbyRange = 40 };
        Assert.Null(service.GetNearest(38.9, -76.9, km with { CoastRange = 20 })!.Where.EdgeNm);
        Assert.NotNull(service.GetNearest(38.9, -76.9, km with { CoastRange = 30 })!.Where.EdgeNm);
    }

    [Fact]
    public void The_coast_range_never_limits_nearest_land_over_water()
    {
        var report = Built().GetNearest(38.55, -76.42, Miles with { CoastRange = 1 })!;
        Assert.Equal(WaterKind.Sea, report.Where.Water);
        Assert.NotNull(report.Where.EdgeNm); // 9.7 miles to land, still said
    }

    [Fact]
    public void The_coast_range_comes_from_the_geonames_tab()
    {
        var settings = new MSFSBlindAssist.Settings.UserSettings();
        Assert.Equal(50, LocationQuerySettings.From(settings).CoastRange);
        settings.CoastRange = 200;
        Assert.Equal(200, LocationQuerySettings.From(settings).CoastRange);
    }

    [Fact]
    public void A_town_across_the_border_never_names_the_area_you_are_in()
    {
        var report = Built().GetNearest(38.31, -76.9, Miles)!; // just inside Maryland's outline
        Assert.Equal("Bordertown", report.Nearest!.Name);     // the nearest town is in Virginia...
        Assert.Equal("Virginia", report.Nearest.State);       // ...and C names it with its own state
        Assert.Equal("Maryland", report.Where.State);         // ...but the box says where the aircraft IS
    }

    [Fact]
    public void A_remote_spot_with_no_town_in_its_outline_uses_the_outline_name()
    {
        var report = Built().GetNearest(39.4, -77.4, Miles)!;
        Assert.Null(report.Nearest);
        Assert.Equal("State of Maryland", report.Where.State);
        Assert.Equal("United States", report.Where.Country);
    }

    [Fact]
    public void Over_a_lake_the_shore_comes_from_its_own_outline()
    {
        var report = Built().GetNearest(43.5, -87.0, Miles)!;
        Assert.Equal(WaterKind.Lake, report.Where.Water);
        Assert.Equal("Test Lake", report.Where.WaterName);
        Assert.InRange(report.Where.EdgeNm!.Value, 21.0, 22.5); // 0.5° of longitude at 43.5°N, not the sea coast
        Assert.Null(report.Nearest);
        Assert.StartsWith("Over Test Lake, shore ", LocationPhrases.NearestCitySentence(report.Where, null, false, 25));
    }

    [Fact]
    public void A_mountain_range_is_named_and_continents_are_not()
    {
        var report = Built().GetNearest(46.5, 10.0, Miles)!;
        Assert.Equal("Test Range", report.Where.Region);
        Assert.Equal("mountain range", report.Where.RegionKind);
    }

    [Fact]
    public void Location_info_fills_every_list()
    {
        var details = Built().GetLocationInfo(38.6, -76.6, Miles)!.Details!;
        Assert.Contains(details.MajorCities, c => c.Name == "Bigtown");
        Assert.DoesNotContain(details.MajorCities, c => c.Name == "Bayside"); // under the 50,000 threshold
        Assert.Contains(details.NearbyPlaces, p => p.Name == "Hamlet");
        Assert.Contains(details.CategorizedLandmarks["Airports"], l => l.Name == "Bayside Airport");
        Assert.Contains(details.CategorizedLandmarks["Water Bodies"], l => l.Name == "Pond");
        Assert.Contains(details.CategorizedLandmarks["Terrain"], l => l.Name == "Sugarloaf Mountain");
        Assert.Contains(details.CategorizedLandmarks["Tourist Landmarks"], l => l.Name == "Bay Museum");
        Assert.DoesNotContain(details.CategorizedLandmarks["Tourist Landmarks"], l => l.Name == "Some School");
    }

    [Fact]
    public void An_update_replaces_the_live_database_in_place()
    {
        Built();
        var first = File.GetLastWriteTimeUtc(_store.DatabasePath);
        Thread.Sleep(50);
        new LocationDataBuilder(_store).Build(WriteInputs(), null, CancellationToken.None);
        Assert.True(File.GetLastWriteTimeUtc(_store.DatabasePath) > first);
        Assert.True(_store.IsAvailable);
    }

    [Fact]
    public void A_failed_build_leaves_the_live_database_untouched()
    {
        Built();
        var before = File.ReadAllBytes(_store.DatabasePath);
        var tooShort = WriteInputs(minimumRows: 1_000);
        Assert.Throws<InvalidDataException>(() => new LocationDataBuilder(_store).Build(tooShort, null, CancellationToken.None));
        Assert.Equal(before, File.ReadAllBytes(_store.DatabasePath));
        Assert.False(File.Exists(_store.StagingPath));
        Assert.False(File.Exists(_store.BuildingPath));
    }

    [Fact]
    public void A_cancelled_build_leaves_no_build_files()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new LocationDataBuilder(_store).Build(WriteInputs(), null, cts.Token));
        Assert.False(File.Exists(_store.StagingPath));
        Assert.False(File.Exists(_store.BuildingPath));
        Assert.False(_store.IsAvailable);
    }

    [Fact]
    public void Startup_sweep_promotes_a_ready_file_and_removes_half_builds()
    {
        Built();
        File.Move(_store.DatabasePath, _store.ReadyPath);
        File.WriteAllText(_store.StagingPath, "half");
        var fresh = new LocationDataStore(_store.DatabasePath);
        fresh.Sweep();
        Assert.True(fresh.IsAvailable);
        Assert.False(File.Exists(fresh.ReadyPath));
        Assert.False(File.Exists(fresh.StagingPath));
    }
}
