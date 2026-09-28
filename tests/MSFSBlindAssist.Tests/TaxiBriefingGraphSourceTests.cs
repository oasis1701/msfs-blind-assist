// tests/MSFSBlindAssist.Tests/TaxiBriefingGraphSourceTests.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.TaxiAugment;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class TaxiBriefingGraphSourceTests
{
    /// <summary>A navdata provider serving the TEST fixture airport, with or without taxi paths — and, like
    /// LSZH's navdata, optionally with not one taxiway segment named.</summary>
    private class FakeProvider : IAirportDataProvider
    {
        public bool HasTaxiPaths = true;
        public bool HasAirport = true;
        public bool HasDatabase = true;
        public bool UnnamedTaxiways;
        /// <summary>GetTaxiPaths returns only the stand lead-ins ("P" rows), as EDDA/LSZU/EGBH's navdata does.</summary>
        public bool OnlyLeadIns;
        /// <summary>Counts every call to <see cref="GetTaxiPaths"/> — a shared build reads it once for both legs.</summary>
        public int TaxiPathReads;
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
        public List<TaxiPath> GetTaxiPaths(string icao)
        {
            TaxiPathReads++;
            if (!HasTaxiPaths) return new List<TaxiPath>();
            var paths = Paths();
            if (UnnamedTaxiways) foreach (var p in paths) p.Name = "";
            if (OnlyLeadIns) paths = paths.Where(p => p.Type == "P").ToList();
            return paths;
        }
        public List<StartPosition> GetRunwayStarts(string icao) => Starts();
    }

    /// <summary>FakeProvider plus <see cref="IAirportFacilitiesProvider"/>, its one candidate a box covering the
    /// fixture — as a real navdata provider would answer <c>CurrentAirport.Resolve</c>.</summary>
    private sealed class FacilitiesFakeProvider : FakeProvider, IAirportFacilitiesProvider
    {
        public AirportFacilities? GetAirportFacilities(string icao) => null;
        public IReadOnlyList<AirportCandidate> GetNearbyAirportCandidates(double latitude, double longitude, double radiusNm) =>
            new[] { new AirportCandidate("TEST", Lat(500), Lon(1500), Lon(-1000), Lon(4000), Lat(1500), Lat(-500), NumTaxiPaths: 1) };
    }

    /// <summary>FakeProvider whose navdata box is the TEST fixture's own hull (north -100 to 1000, east -100 to 3100),
    /// as a real navdata provider answers <c>GetAirportFacilities</c>.</summary>
    private sealed class BoxedFakeProvider : FakeProvider, IAirportFacilitiesProvider
    {
        public AirportFacilities? GetAirportFacilities(string icao) => new()
        {
            Icao = "TEST", TopLat = Lat(1000), BottomLat = Lat(-100), LeftLon = Lon(-100), RightLon = Lon(3100),
            RefLat = Lat(500), RefLon = Lon(1500),
        };
    }

    /// <summary>An online source that answers at once with a name for every one of the TEST airport's taxiway
    /// segments, on the navdata's own geometry — as OpenStreetMap does for LSZH.</summary>
    private sealed class NamingSource : ITaxiDataSource
    {
        public string Id => "osm";
        public Task<AirportTaxiData?> FetchAsync(string icao, double airportLat, double airportLon, CancellationToken ct)
        {
            var data = new AirportTaxiData { Source = "osm" };
            foreach (var p in Paths().Where(p => p.Type == "T"))
                data.Taxiways.Add(new NamedTaxiSegment { Name = p.Name, Lat1 = p.StartLat, Lon1 = p.StartLon, Lat2 = p.EndLat, Lon2 = p.EndLon });
            return Task.FromResult<AirportTaxiData?>(data);
        }
    }

    /// <summary>An online source whose fetch never completes — a spent budget must not leave a leg waiting on it
    /// past its own bound.</summary>
    private sealed class NeverSource : ITaxiDataSource
    {
        public string Id => "never";
        public Task<AirportTaxiData?> FetchAsync(string icao, double airportLat, double airportLon, CancellationToken ct) =>
            new TaskCompletionSource<AirportTaxiData?>().Task;
    }

    [Fact]
    public void Stand_lead_ins_alone_are_no_taxiways()
    {
        Assert.False(TaxiBriefingGraphSource.HasTaxiways(new[] { LeadIn(300, 100, 300, 250) }));
        Assert.True(TaxiBriefingGraphSource.HasTaxiways(Paths()));
    }

    [Fact]
    public async Task An_airport_with_only_stand_lead_ins_is_planned_on_OpenStreetMap()
    {
        // 66 fs2024 airports (EDDA, LSZU, EGBH, EDHW, SAWS…) have navdata with only stand lead-ins — no real
        // taxiway — and taking that as "has taxiways" briefed an unnamed route ending on grass while
        // OpenStreetMap was never asked.
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { OnlyLeadIns = true }, new TaxiDataCache(1),
            new ITaxiDataSource[] { new NamingSource() }, new MergeOptions());

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(BriefingTier.OpenStreetMap, bundle!.Tier);
    }

    [Fact]
    public async Task The_budget_running_out_during_the_names_wait_ends_the_leg()
    {
        // NeverSource.FetchAsync never completes: the old Task.WhenAny(prefetch, Task.Delay(8000, ct)) swallowed
        // the Delay's cancellation inside a catch-all, and the abandoned leg went on to read the database and
        // build a graph nobody would use. A spent budget must end the leg instead.
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider(), new TaxiDataCache(1),
            new ITaxiDataSource[] { new NeverSource() }, new MergeOptions());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", cts.Token));
    }

    [Fact]
    public async Task One_airport_for_both_legs_is_built_once()
    {
        // Origin and destination are both TEST (TaxiBriefingFixture.Request's default): the two legs would
        // otherwise build the identical graph twice at the same moment, and nothing in the planner shares a
        // graph between threads.
        var provider = new FakeProvider();
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL");

        var briefing = await TaxiBriefingPlanner.PlanAsync(request, provider, null, TimeSpan.FromSeconds(20));

        Assert.Null(briefing.TaxiOut.Unavailable);
        Assert.Null(briefing.TaxiIn.Unavailable);
        Assert.Equal(1, provider.TaxiPathReads);
    }

    [Fact]
    public async Task The_first_briefing_of_a_session_waits_for_the_online_taxiway_names()
    {
        // LSZH's navdata names none of its taxiway segments: the names come from OpenStreetMap, whose cache is
        // memory-only, so it is the FIRST briefing of a session that meets an airport nothing has fetched yet.
        // Built from navdata at once, that briefing said "Taxiways: (unnamed)" and "no exit taxiway is mapped clear
        // of runway 14" — and a second press, after the background fetch had landed, answered differently.
        var augmenting = new AugmentingAirportDataProvider(new FakeProvider { UnnamedTaxiways = true }, new TaxiDataCache(ttlDays: 1),
            new ITaxiDataSource[] { new NamingSource() }, new MergeOptions());
        var md11F = AircraftSizeClass.Resolve("MD1F", "MD-11F", 0);

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(BriefingTier.Navdata, bundle!.Tier);
        Assert.Equal(new[] { "A", "E1" }, TaxiBriefingPlanner.PlanTaxiOut(Request(md11F, airline: "UPS"), bundle).Taxiways);
        Assert.Equal("C", TaxiBriefingPlanner.PlanTaxiIn(Request(md11F, airline: "UPS"), bundle).Exit!.Exit.TaxiwayName);
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
    public async Task IsAtAirport_is_the_facilities_providers_own_answer_and_null_without_one()
    {
        var (withFacilities, _) = await TaxiBriefingGraphSource.BuildAsync(new FacilitiesFakeProvider(), null, "TEST", CancellationToken.None);
        Assert.NotNull(withFacilities!.IsAtAirport);
        Assert.True(withFacilities.IsAtAirport!(Lat(250), Lon(2500)));
        Assert.False(withFacilities.IsAtAirport!(Lat(50000), Lon(0)));

        var (plain, _) = await TaxiBriefingGraphSource.BuildAsync(new FakeProvider(), null, "TEST", CancellationToken.None);
        Assert.Null(plain!.IsAtAirport);
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
    public async Task Online_data_beyond_the_airport_box_is_not_planned_on()
    {
        // The fetch reaches 5 km from the reference point: a neighbouring hub's taxiways (4NY2 beside KLGA) must not
        // become this airport's route. Kept only inside the navdata box grown CurrentAirportResolver's 300 m.
        var cache = new TaxiDataCache(ttlDays: 1);
        var osm = new AirportTaxiData { Source = "osm" };
        osm.Taxiways.Add(new NamedTaxiSegment { Name = "A", Lat1 = Lat(100), Lon1 = Lon(0), Lat2 = Lat(100), Lon2 = Lon(3000) });
        osm.Taxiways.Add(new NamedTaxiSegment { Name = "FAR", Lat1 = Lat(5000), Lon1 = Lon(0), Lat2 = Lat(5000), Lon2 = Lon(400) });
        cache.Save("TEST", new[] { osm });
        var augmenting = new AugmentingAirportDataProvider(new BoxedFakeProvider { HasTaxiPaths = false }, cache,
            Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(augmenting, null, "TEST", CancellationToken.None);

        Assert.Null(reason);
        Assert.Equal(new[] { "A" }, TaxiBriefingPlanner.AirportTaxiwayNames(bundle!.Graph));
        Assert.NotNull(bundle.IsAtAirport);
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
    public async Task A_cancellation_neither_token_asked_for_is_a_failure_not_a_timeout()
    {
        // The work itself throws OperationCanceledException while neither the caller nor the budget cancelled.
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189));
        var cancelling = new ThrowingProvider(() => new OperationCanceledException());

        var b = await TaxiBriefingPlanner.PlanAsync(request, cancelling, null, TaxiBriefingPlanner.DefaultBudget);

        Assert.StartsWith("taxi route could not be computed (", b.TaxiOut.Unavailable);
        Assert.StartsWith("taxi route could not be computed (", b.TaxiIn.Unavailable);
    }

    [Fact]
    public async Task The_two_legs_are_planned_at_the_same_time()
    {
        // No clock is raced: the departure airport's read can only be released by the arrival leg's read while
        // both are in progress (see OverlapProvider). Planned one after the other, in either order, neither read
        // ever completes, the budget runs out and the arrival leg is reported as timed out.
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189), airline: "DAL") with { OriginIcao = "SLOW" };
        var provider = new OverlapProvider();

        var b = await TaxiBriefingPlanner.PlanAsync(request, provider, null, TaxiBriefingPlanner.DefaultBudget);

        Assert.True(provider.SlowReleasedByArrivalRead, "the departure airport's read was not released by the arrival leg's read");
        Assert.Equal("SLOW is not in the navigation database", b.TaxiOut.Unavailable);
        Assert.Null(b.TaxiIn.Unavailable);
        Assert.Equal("C", b.TaxiIn.Exit!.Exit.TaxiwayName);
    }

    [Fact]
    public async Task A_leg_that_overruns_the_budget_times_out()
    {
        // SLOW's read does not return before this test's finally, so the budget is the only way that leg can end:
        // a late timer slows the test down, it never changes the answer. The blank destination ends its own leg
        // at once, on this thread.
        var request = Request(AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189)) with { OriginIcao = "SLOW", DestinationIcao = "" };
        var provider = new BlockingProvider(blockIcao: "SLOW");
        try
        {
            var b = await TaxiBriefingPlanner.PlanAsync(request, provider, null, TimeSpan.FromMilliseconds(300));

            Assert.Equal("taxi route computation timed out", b.TaxiOut.Unavailable);
            Assert.Equal("no airport in the flight plan", b.TaxiIn.Unavailable);
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
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
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

    /// <summary>The TEST airport served through <see cref="FakeProvider"/>; a subclass overrides the read it controls.</summary>
    private abstract class DelegatingProvider : IAirportDataProvider
    {
        private readonly FakeProvider _inner = new();
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public virtual Airport? GetAirport(string icao) => _inner.GetAirport(icao);
        public List<Runway> GetRunways(string icao) => _inner.GetRunways(icao);
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => _inner.GetParkingSpots(icao);
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 1;
        public int GetRunwayCount() => 4;
        public int GetParkingSpotCount() => 3;
        public HashSet<string> GetAllAirportICAOs() => new() { "TEST" };
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public virtual List<TaxiPath> GetTaxiPaths(string icao) => _inner.GetTaxiPaths(icao);
        public List<StartPosition> GetRunwayStarts(string icao) => _inner.GetRunwayStarts(icao);
    }

    /// <summary>
    /// GetAirport for <c>blockIcao</c> (every ident when null) blocks until <see cref="Release"/> is set — a read
    /// that overruns the budget. <see cref="Entered"/> completes when the first blocking read starts. Each test
    /// sets Release in a finally, and the wait is bounded too, so a failing test can never hold a pool thread for
    /// the rest of the run.
    /// </summary>
    private sealed class BlockingProvider : DelegatingProvider
    {
        private readonly string? _blockIcao;
        public readonly ManualResetEventSlim Release = new(false);
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingProvider(string? blockIcao) => _blockIcao = blockIcao;

        public override Airport? GetAirport(string icao)
        {
            if (_blockIcao != null && !string.Equals(icao, _blockIcao, StringComparison.OrdinalIgnoreCase))
                return base.GetAirport(icao);
            Entered.TrySetResult();
            Release.Wait(TimeSpan.FromSeconds(30));
            return null;
        }
    }

    /// <summary>
    /// The departure airport SLOW and the arrival airport TEST each wait inside a read for the other: GetAirport("SLOW")
    /// says it has started, then waits (30 s cap) for the arrival leg's GetTaxiPaths("TEST"), which waits (30 s cap)
    /// for SLOW's read to have started before it releases it. So <see cref="SlowReleasedByArrivalRead"/> is true only
    /// when the two reads were in progress at the same time — never when the legs run one after the other, in either
    /// order. GetAirport("SLOW") then returns null.
    /// </summary>
    private sealed class OverlapProvider : DelegatingProvider
    {
        private readonly ManualResetEventSlim _slowReadStarted = new(false);
        private readonly ManualResetEventSlim _arrivalRead = new(false);
        public volatile bool SlowReleasedByArrivalRead;

        public override Airport? GetAirport(string icao)
        {
            if (!string.Equals(icao, "SLOW", StringComparison.OrdinalIgnoreCase))
                return base.GetAirport(icao);
            _slowReadStarted.Set();
            SlowReleasedByArrivalRead = _arrivalRead.Wait(TimeSpan.FromSeconds(30));
            return null;
        }

        public override List<TaxiPath> GetTaxiPaths(string icao)
        {
            if (string.Equals(icao, "TEST", StringComparison.OrdinalIgnoreCase) && _slowReadStarted.Wait(TimeSpan.FromSeconds(30)))
                _arrivalRead.Set();
            return base.GetTaxiPaths(icao);
        }
    }

    /// <summary>Every airport read throws — by default an InvalidOperationException("boom").</summary>
    private sealed class ThrowingProvider : IAirportDataProvider
    {
        private readonly Func<Exception> _exception;
        public ThrowingProvider(Func<Exception>? exception = null) => _exception = exception ?? (() => new InvalidOperationException("boom"));

        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => "";
        public Airport? GetAirport(string icao) => throw _exception();
        public List<Runway> GetRunways(string icao) => throw _exception();
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
