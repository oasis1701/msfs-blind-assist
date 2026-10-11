using System.IO.Compression;
using System.Text;
using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

/// <summary>
/// Builds a tiny world from fixture files with the real builder and queries it with the real
/// service: a bay east of a Maryland shoreline, a lake, a mountain range, a few places.
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

    private static string Row(int id, string name, double lat, double lon, string cls, string code, string a1, int pop) =>
        FormattableString.Invariant($"{id}\t{name}\t{name}\t\t{lat}\t{lon}\t{cls}\t{code}\tUS\t\t{a1}\t\t\t\t{pop}\t\t\tAmerica/New_York\t2026-01-01");

    private static readonly string[] Rows =
    {
        Row(1, "Bayside", 38.55, -76.48, "P", "PPL", "MD", 1238),
        Row(2, "Hamlet", 38.60, -76.70, "P", "PPL", "MD", 0),
        Row(3, "Bigtown", 39.29, -76.61, "P", "PPLA2", "MD", 600000),
        Row(4, "Bayside Airport", 38.40, -76.60, "S", "AIRP", "MD", 0),
        Row(5, "Pond", 38.70, -76.90, "H", "LK", "MD", 0),
        Row(6, "Sugarloaf Mountain", 38.90, -77.00, "T", "MT", "MD", 0),
        Row(7, "Bay Museum", 38.50, -76.50, "S", "MUS", "MD", 0),
        Row(8, "Some School", 38.51, -76.51, "S", "SCH", "MD", 0),
    };

    private static string Collection(params string[] features) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", features) + "]}";

    private static string Box(string props, double s, double w, double n, double e) => FormattableString.Invariant(
        $"{{\"type\":\"Feature\",\"properties\":{props},\"geometry\":{{\"type\":\"Polygon\",\"coordinates\":[[[{w},{s}],[{e},{s}],[{e},{n}],[{w},{n}],[{w},{s}]]]}}}}");

    private LocationBuildInputs WriteInputs(int minimumRows = 1)
    {
        string zip = Path.Combine(_dir, "allCountries.zip");
        File.Delete(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("allCountries.txt").Open(), new UTF8Encoding(false)))
            foreach (var row in Rows) writer.Write(row + "\n");
        File.WriteAllText(Path.Combine(_dir, "countryInfo.txt"), "#ISO\tISO3\tNum\tfips\tCountry\nUS\tUSA\t840\tUS\tUnited States\n");
        File.WriteAllText(Path.Combine(_dir, "admin1.txt"), "US.MD\tMaryland\tMaryland\t4361885\n");

        var files = new Dictionary<NaturalEarthLayer, string>
        {
            [NaturalEarthLayer.Marine] = Write("marine", Collection(Box("{\"name\":\"Test Bay\"}", 38.3, -76.6, 38.8, -76.2))),
            [NaturalEarthLayer.Coastline] = Write("coast", Collection(
                "{\"type\":\"Feature\",\"properties\":{},\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[-76.6,38.3],[-76.6,38.8]]}}")),
            [NaturalEarthLayer.Lake] = Write("lakes", Collection(Box("{\"name\":\"Test Lake\"}", 43, -87.5, 44, -86.5))),
            [NaturalEarthLayer.Region] = Write("regions", Collection(
                Box("{\"NAME\":\"TEST RANGE\",\"FEATURECLA\":\"Range/mtn\"}", 46, 9, 47, 11),
                Box("{\"NAME\":\"EUROPE\",\"FEATURECLA\":\"Continent\"}", 35, -10, 70, 40))),
            [NaturalEarthLayer.State] = Write("states", Collection(
                // Natural Earth spells it its own way; GeoNames' spelling must win (gn_a1_code US.MD).
                Box("{\"name\":\"State of Maryland\",\"admin\":\"United States of America\",\"iso_a2\":\"US\",\"gn_a1_code\":\"US.MD\"}", 38.3, -77.5, 39.5, -76.6))),
        };
        return new LocationBuildInputs(zip, Path.Combine(_dir, "countryInfo.txt"), Path.Combine(_dir, "admin1.txt"), files, "2026-10-10")
        {
            MinimumPlaces = 1,
            MinimumRows = minimumRows,
        };
    }

    private string Write(string name, string json)
    {
        string path = Path.Combine(_dir, name + ".geojson");
        File.WriteAllText(path, json);
        return path;
    }

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
        Assert.Equal(7, _store.Info!.PlaceCount); // the school is not kept
        Assert.Equal("2026-10-10", _store.Info.GeoNamesDate);
        Assert.False(File.Exists(_store.StagingPath));
        Assert.False(File.Exists(_store.BuildingPath));
        Assert.False(File.Exists(_store.ReadyPath));
    }

    [Fact]
    public void Over_the_bay_the_sea_its_coast_and_the_nearest_state_are_found()
    {
        var report = Built().GetNearest(38.55, -76.42, Miles)!;
        Assert.Equal(WaterKind.Sea, report.Where.Water);
        Assert.Equal("Test Bay", report.Where.WaterName);
        Assert.InRange(report.Where.EdgeNm!.Value, 8.0, 9.0); // 0.18° of longitude at 38.55°N ≈ 8.4 nm
        Assert.Equal("west", LocationPhrases.Direction(report.Where.EdgeBearingTrue));
        Assert.Equal("Maryland", report.Where.State);
        Assert.Equal("United States", report.Where.Country);
        Assert.True(report.Where.StateIsNearest);
        Assert.Equal("Bayside", report.Nearest!.Name);
        Assert.StartsWith("Over Test Bay, coast 9.7 miles west. Near Bayside, Maryland, United States, ",
            LocationPhrases.NearestCitySentence(report.Where, report.Nearest, false, 25));
    }

    [Fact]
    public void Over_land_the_state_polygon_wins_with_geonames_spelling()
    {
        var report = Built().GetNearest(38.9, -76.9, Miles)!;
        Assert.Equal(WaterKind.None, report.Where.Water);
        Assert.Equal("Maryland", report.Where.State); // not "State of Maryland"
        Assert.Equal("United States", report.Where.Country); // not "United States of America"
        Assert.False(report.Where.StateIsNearest);
        Assert.NotNull(report.Where.EdgeNm); // the coast is 16 miles east: within the 50-mile land limit
    }

    [Fact]
    public void Over_a_lake_the_edge_is_its_shore()
    {
        var report = Built().GetNearest(43.5, -87.0, Miles)!;
        Assert.Equal(WaterKind.Lake, report.Where.Water);
        Assert.Equal("Test Lake", report.Where.WaterName);
        Assert.InRange(report.Where.EdgeNm!.Value, 21.0, 22.5); // 0.5° of longitude at 43.5°N
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
