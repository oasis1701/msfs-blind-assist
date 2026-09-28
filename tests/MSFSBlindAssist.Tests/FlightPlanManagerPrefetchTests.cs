using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// FlightPlanManager.AdoptSimBriefPlan force-refreshes the online taxiway-name augmentation for
/// both airports a loaded OFP names, as LoadDeparture/LoadArrival already do — without this the
/// first Describe Route of every flight waited up to 8 s per cold airport (see
/// AugmentingAirportDataProvider.PrefetchAsync/FetchCoreAsync's 60 s network timeout — 8 s is the
/// typical settle, not a hard bound). internal, reached via InternalsVisibleTo (see
/// MSFSBlindAssist/Properties/InternalsVisibleTo.cs).
/// </summary>
public class FlightPlanManagerPrefetchTests
{
    /// <summary>A navdata provider that answers GetAirport for ANY ICAO (coordinates only matter to
    /// the real merge geometry, which this suite never reaches — RecordingSource returns no data).</summary>
    private sealed class AnyAirportProvider : IAirportDataProvider
    {
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => new Airport(icao, icao, "", "", 0, 0, 0, 0);
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 1;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    /// <summary>Records every ICAO it is asked to fetch. <see cref="Signal"/> is released once per call
    /// (bounded-wait tests await it instead of a fixed sleep); <see cref="WhenBoth"/> completes once
    /// both KMEM and KATL have been seen. Answers no data — nothing in these tests reads the merge.</summary>
    private sealed class RecordingSource : ITaxiDataSource
    {
        private readonly object _gate = new();
        private readonly List<string> _seen = new();
        private readonly TaskCompletionSource _whenBoth = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public readonly SemaphoreSlim Signal = new(0);
        public string Id => "recording";
        public List<string> Seen { get { lock (_gate) return new List<string>(_seen); } }
        public Task WhenBoth => _whenBoth.Task;

        public Task<AirportTaxiData?> FetchAsync(string icao, double airportLat, double airportLon, CancellationToken ct)
        {
            lock (_gate)
            {
                _seen.Add(icao);
                if (_seen.Any(s => string.Equals(s, "KMEM", StringComparison.OrdinalIgnoreCase)) &&
                    _seen.Any(s => string.Equals(s, "KATL", StringComparison.OrdinalIgnoreCase)))
                    _whenBoth.TrySetResult();
            }
            Signal.Release();
            return Task.FromResult<AirportTaxiData?>(null);
        }
    }

    [Fact]
    public async Task Loading_an_OFP_fetches_both_airports_online_taxiway_names()
    {
        var source = new RecordingSource();   // FetchAsync records the ICAO, completes WhenBoth once KMEM and KATL are in
        var provider = new AugmentingAirportDataProvider(new AnyAirportProvider(), new TaxiDataCache(1),
            new ITaxiDataSource[] { source }, new MergeOptions());
        var manager = new FlightPlanManager("no-such-navdata.sqlite", provider);

        manager.AdoptSimBriefPlan(new FlightPlan { DepartureICAO = "KMEM", ArrivalICAO = "KATL" });

        await source.WhenBoth.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "KATL", "KMEM" }, source.Seen.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_round_trip_OFP_fetches_its_one_airport_once()
    {
        // KMEM → KMEM: one prefetch, not two for the same field.
        var source = new RecordingSource();
        var provider = new AugmentingAirportDataProvider(new AnyAirportProvider(), new TaxiDataCache(1),
            new ITaxiDataSource[] { source }, new MergeOptions());
        var manager = new FlightPlanManager("no-such-navdata.sqlite", provider);

        manager.AdoptSimBriefPlan(new FlightPlan { DepartureICAO = "KMEM", ArrivalICAO = "KMEM" });

        // Wait for the one expected fetch, then a bounded settle: a stray SECOND fetch for the
        // same field (a regression of the departure/arrival equality guard) would have landed
        // well within this window rather than needing a fixed sleep before the first fetch even
        // starts (flaky under load).
        await source.Signal.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300);

        Assert.Equal(new[] { "KMEM" }, source.Seen);
    }
}
