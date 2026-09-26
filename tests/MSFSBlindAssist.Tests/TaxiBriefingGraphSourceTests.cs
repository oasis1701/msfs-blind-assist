// tests/MSFSBlindAssist.Tests/TaxiBriefingGraphSourceTests.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.TaxiAugment;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class TaxiBriefingGraphSourceTests
{
    /// <summary>A navdata provider serving the TEST fixture airport, with or without taxi paths.</summary>
    private sealed class FakeProvider : IAirportDataProvider
    {
        public bool HasTaxiPaths = true;
        public bool HasAirport = true;
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => HasAirport ? AirportRef() : null;
        public List<Runway> GetRunways(string icao) => Runways();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => Spots();
        public bool AirportExists(string icao) => HasAirport;
        public int GetAirportCount() => 1;
        public int GetRunwayCount() => 4;
        public int GetParkingSpotCount() => 3;
        public HashSet<string> GetAllAirportICAOs() => new() { "TEST" };
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => HasTaxiPaths ? Paths() : new List<TaxiPath>();
        public List<StartPosition> GetRunwayStarts(string icao) => Starts();
    }

    [Fact]
    public async Task Navdata_with_taxiways_is_tier_one()
    {
        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider(), null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(BriefingTier.Navdata, bundle!.Tier);
        Assert.Equal(3, bundle.Spots.Count);
        Assert.Equal("TEST", bundle.Airport!.ICAO);
    }

    [Fact]
    public async Task Unknown_airport_is_a_reason()
    {
        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider { HasAirport = false }, null, "ZZZZ", CancellationToken.None);
        Assert.Null(bundle);
        Assert.Equal("ZZZZ is not in the navigation database", reason);
    }

    [Fact]
    public async Task No_taxiways_and_a_plain_provider_means_no_online_fallback()
    {
        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider { HasTaxiPaths = false }, null, "TEST", CancellationToken.None);
        Assert.Null(bundle);
        Assert.Equal("the navigation database has no taxiways for TEST and OpenStreetMap data is not available", reason);
    }

    [Fact]
    public async Task No_taxiways_with_cached_osm_data_is_tier_two()
    {
        var cache = new TaxiDataCache(ttlDays: 1);
        var osm = new AirportTaxiData { Source = "osm" };
        osm.Taxiways.Add(new NamedTaxiSegment { Name = "A", Lat1 = Lat(100), Lon1 = Lon(0), Lat2 = Lat(100), Lon2 = Lon(3000) });
        cache.Save("TEST", new[] { osm });
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { HasTaxiPaths = false }, cache, Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(BriefingTier.OpenStreetMap, bundle!.Tier);
        Assert.Equal(OsmPlanningGraph.Note, bundle.Note);
    }

    [Fact]
    public async Task No_taxiways_with_augmentation_disabled_says_so()
    {
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { HasTaxiPaths = false }, new TaxiDataCache(1), Array.Empty<ITaxiDataSource>(), new MergeOptions()) { Enabled = false };
        var (_, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);
        Assert.Equal("the navigation database has no taxiways for TEST and online taxi data is disabled in settings", reason);
    }

    [Fact]
    public async Task Plan_async_answers_both_legs_and_survives_a_null_provider()
    {
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL");

        var full = await TaxiBriefingPlanner.PlanAsync(request, new FakeProvider(), null, TaxiBriefingPlanner.DefaultBudget);
        Assert.Null(full.TaxiOut.Unavailable);
        Assert.Null(full.TaxiIn.Unavailable);
        Assert.Equal("C", full.TaxiIn.Exit!.Exit.TaxiwayName);

        var none = await TaxiBriefingPlanner.PlanAsync(request, null, null, TaxiBriefingPlanner.DefaultBudget);
        Assert.Equal("no navigation database loaded", none.TaxiOut.Unavailable);
        Assert.Equal("no navigation database loaded", none.TaxiIn.Unavailable);
    }

    [Fact]
    public async Task Plan_async_reports_a_leg_that_throws_instead_of_failing_the_briefing()
    {
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189));
        var throwing = new ThrowingProvider();
        var b = await TaxiBriefingPlanner.PlanAsync(request, throwing, null, TaxiBriefingPlanner.DefaultBudget);
        Assert.StartsWith("taxi route could not be computed (", b.TaxiOut.Unavailable);
        Assert.StartsWith("taxi route could not be computed (", b.TaxiIn.Unavailable);
    }

    private sealed class ThrowingProvider : IAirportDataProvider
    {
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => throw new InvalidOperationException("boom");
        public List<Runway> GetRunways(string icao) => throw new InvalidOperationException("boom");
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }
}
