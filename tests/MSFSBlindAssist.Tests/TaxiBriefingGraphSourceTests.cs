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
        public bool HasDatabase = true;
        public bool DatabaseExists => HasDatabase;
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
    public async Task No_taxiways_and_no_online_data_is_not_available_right_now()
    {
        // Enabled, nothing cached, and no source answers: GetOnlineTaxiDataAsync returns null, exactly as it
        // does for an offline machine, an airport OpenStreetMap has nothing for, or a fetch that did not finish
        // in time. One sentence covers all three, so it must not promise that waiting will help.
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { HasTaxiPaths = false }, new TaxiDataCache(1),
            Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(bundle);
        Assert.Equal("the navigation database has no taxiways for TEST and OpenStreetMap data is not available right now", reason);
    }

    [Fact]
    public async Task No_taxiways_and_online_data_without_taxiways_says_so()
    {
        // OpenStreetMap answered, but with a stand and no taxiway at all: nothing to plan on.
        var cache = new TaxiDataCache(ttlDays: 1);
        var osm = new AirportTaxiData { Source = "osm" };
        osm.Parking.Add(("G1", Lat(250), Lon(300)));
        cache.Save("TEST", new[] { osm });
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { HasTaxiPaths = false }, cache,
            Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(bundle);
        Assert.Equal("the navigation database has no taxiways for TEST and OpenStreetMap has no named taxiways for it", reason);
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
    public async Task Plan_async_without_the_database_file_says_so_on_both_legs()
    {
        // A provider whose database file is missing, not only a null provider.
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL");

        var b = await TaxiBriefingPlanner.PlanAsync(request, new FakeProvider { HasDatabase = false }, null, TaxiBriefingPlanner.DefaultBudget);

        Assert.Equal("no navigation database loaded", b.TaxiOut.Unavailable);
        Assert.Equal("no navigation database loaded", b.TaxiIn.Unavailable);
    }

    [Fact]
    public async Task Plan_async_names_a_leg_with_no_airport_in_the_flight_plan()
    {
        // FakeProvider serves TEST for ANY ident, so only the blank check keeps this leg from being planned.
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL") with { DestinationIcao = "  " };

        var b = await TaxiBriefingPlanner.PlanAsync(request, new FakeProvider(), null, TaxiBriefingPlanner.DefaultBudget);

        Assert.Null(b.TaxiOut.Unavailable);
        Assert.Equal("no airport in the flight plan", b.TaxiIn.Unavailable);
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

    [Fact]
    public async Task A_departure_airport_that_overruns_the_budget_times_out_alone()
    {
        // The departure airport's read never returns inside the budget (as an OpenStreetMap fetch for an
        // airport with no navdata taxiways can take); the arrival leg shares the budget, not a queue, so it
        // is still planned instead of being reported as timed out without a single database read.
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL") with { OriginIcao = "SLOW" };
        await WarmUpAsync();
        var provider = new BlockingProvider(blockIcao: "SLOW");
        try
        {
            var b = await TaxiBriefingPlanner.PlanAsync(request, provider, null, TimeSpan.FromMilliseconds(300));

            Assert.Equal("SLOW", b.TaxiOut.Icao);
            Assert.Equal("taxi route computation timed out", b.TaxiOut.Unavailable);
            Assert.Null(b.TaxiIn.Unavailable);
            Assert.Equal("C", b.TaxiIn.Exit!.Exit.TaxiwayName);
        }
        finally
        {
            provider.Release.Set();
        }
    }

    [Fact]
    public async Task Cancelling_the_callers_token_throws_instead_of_reporting_a_timeout()
    {
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189));
        var provider = new BlockingProvider(blockIcao: null);
        using var caller = new CancellationTokenSource();
        try
        {
            var planning = TaxiBriefingPlanner.PlanAsync(request, provider, null, TaxiBriefingPlanner.DefaultBudget, caller.Token);
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            caller.Cancel();

            var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planning);
            Assert.Equal(caller.Token, ex.CancellationToken);
        }
        finally
        {
            provider.Release.Set();
        }
    }

    [Fact]
    public async Task An_already_cancelled_callers_token_throws_instead_of_reporting_a_timeout()
    {
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189));
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TaxiBriefingPlanner.PlanAsync(request, new FakeProvider(), null, TaxiBriefingPlanner.DefaultBudget, caller.Token));
        Assert.Equal(caller.Token, ex.CancellationToken);
    }

    /// <summary>One full plan of the TEST airport first, so a timing test's budget measures the leg's work
    /// rather than the JIT compiling the graph build and the planner.</summary>
    private static Task WarmUpAsync() => TaxiBriefingPlanner.PlanAsync(
        Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL"), new FakeProvider(), null,
        TaxiBriefingPlanner.DefaultBudget);

    /// <summary>
    /// The TEST airport, except that GetAirport for <c>blockIcao</c> (every ident when null) blocks until
    /// <see cref="Release"/> is set — a read that overruns the budget. <see cref="Entered"/> completes when the
    /// first blocking read starts. Each test sets Release in a finally, and the wait is bounded too, so a
    /// failing test can never hold a pool thread for the rest of the run.
    /// </summary>
    private sealed class BlockingProvider : IAirportDataProvider
    {
        private readonly FakeProvider _inner = new();
        private readonly string? _blockIcao;
        public readonly ManualResetEventSlim Release = new(false);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingProvider(string? blockIcao) => _blockIcao = blockIcao;

        public Airport? GetAirport(string icao)
        {
            if (_blockIcao != null && !string.Equals(icao, _blockIcao, StringComparison.OrdinalIgnoreCase))
                return _inner.GetAirport(icao);
            Entered.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(30));
            return null;
        }

        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public List<Runway> GetRunways(string icao) => _inner.GetRunways(icao);
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => _inner.GetParkingSpots(icao);
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 1;
        public int GetRunwayCount() => 4;
        public int GetParkingSpotCount() => 3;
        public HashSet<string> GetAllAirportICAOs() => new() { "TEST" };
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => _inner.GetTaxiPaths(icao);
        public List<StartPosition> GetRunwayStarts(string icao) => _inner.GetRunwayStarts(icao);
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
