using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

// The route briefing's OpenStreetMap tier reads the decorator's already-cached online data through
// this one accessor (read-only; nothing here builds geometry into the navdata path).
public class AugmentingProviderOnlineDataTests
{
    private sealed class NoAirportProvider : IAirportDataProvider
    {
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => null;      // a fetch cannot even start
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => false;
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    private static AugmentingAirportDataProvider Provider(TaxiDataCache cache) =>
        new(new NoAirportProvider(), cache, Array.Empty<ITaxiDataSource>(), new MergeOptions());

    [Fact]
    public async Task Cached_sources_are_returned_without_a_fetch()
    {
        var cache = new TaxiDataCache(ttlDays: 1);
        var osm = new AirportTaxiData { Source = "osm" };
        osm.Taxiways.Add(new NamedTaxiSegment { Name = "A", Lat1 = 0.01, Lon1 = 0.01, Lat2 = 0.011, Lon2 = 0.01 });
        cache.Save("LOWI", new[] { osm });

        var data = await Provider(cache).GetOnlineTaxiDataAsync("LOWI", CancellationToken.None);

        Assert.NotNull(data);
        Assert.Equal("osm", Assert.Single(data!).Source);
    }

    [Fact]
    public async Task Disabled_augmentation_returns_null()
    {
        var cache = new TaxiDataCache(ttlDays: 1);
        cache.Save("LOWI", new[] { new AirportTaxiData { Source = "osm" } });
        var provider = Provider(cache);
        provider.Enabled = false;

        Assert.Null(await provider.GetOnlineTaxiDataAsync("LOWI", CancellationToken.None));
    }

    [Fact]
    public async Task Nothing_cached_and_nothing_fetchable_returns_null()
    {
        var provider = Provider(new TaxiDataCache(ttlDays: 1));
        Assert.Null(await provider.GetOnlineTaxiDataAsync("LOWI", CancellationToken.None));
    }

    [Fact]
    public async Task Blank_icao_returns_null()
        => Assert.Null(await Provider(new TaxiDataCache(ttlDays: 1)).GetOnlineTaxiDataAsync(" ", CancellationToken.None));
}
