// Pins GateDataSource's ROUTING decision only (Spec 2 Task 5): which of three sources -- the
// GSX Remote API (current airport only), the pre-existing GSX .ini/navdata merge, or plain
// navdata -- GetGates/GetActiveSource choose, and that a failure anywhere in the new Remote API
// attempt degrades to the pre-existing path rather than propagating. GsxRemoteParkingReader,
// GsxStopPositionJoiner and GsxNavdataMerger each have their own dedicated test suites (Tasks
// 3/4, and pre-existing) for what they DO with the data once GateDataSource has decided to call
// them; this file does not re-test their internals, only that GateDataSource calls the right one.
//
// See docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
// §"1. The API only knows the CURRENT airport" and §"GateDataSource — routing only".

using System.Text.Json;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GateDataSourceRoutingTests : IDisposable
{
    private const string Kjfk = "KJFK";
    private const string Eddf = "EDDF";

    // One scratch directory per test-method instance (xUnit constructs a fresh test class
    // instance per [Fact]), matching the pattern already used by
    // SayIntentionsFlightContextTests/SayIntentionsLiveClearanceTests. A GsxProfileLocator
    // pointed here — WITHOUT anything written into it — behaves exactly like "no .ini profile
    // exists for any ICAO", deterministically, regardless of what happens to be installed under
    // the real %APPDATA%\Virtuali\GSX\MSFS on the machine running these tests.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gds-routing-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // IOException also covers DirectoryNotFoundException (a subtype) — most tests never
        // create _dir at all (EmptyLocator's whole point is a directory that doesn't exist).
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* not created, or a leaked handle — must not cascade into every teardown */ }
        catch (UnauthorizedAccessException) { }
    }

    // ── A minimal, controllable IAirportDataProvider ────────────────────────────────────────
    // No fake implementing this interface exists anywhere in the test project yet. Records
    // every GetParkingSpots call so a test can assert the Remote API path never touches navdata
    // (GsxNavdataMerger stays on the remote-airport path only — spec constraint 1).
    private sealed class FakeAirportDataProvider : IAirportDataProvider
    {
        private readonly Dictionary<string, List<ParkingSpot>> _spotsByIcao;
        public List<string> GetParkingSpotsCalls { get; } = new();

        public FakeAirportDataProvider(Dictionary<string, List<ParkingSpot>>? spotsByIcao = null)
            => _spotsByIcao = spotsByIcao ?? new(StringComparer.OrdinalIgnoreCase);

        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => string.Empty;
        public Airport? GetAirport(string icao) => null;
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;

        public List<ParkingSpot> GetParkingSpots(string icao)
        {
            GetParkingSpotsCalls.Add(icao);
            return _spotsByIcao.TryGetValue(icao, out var spots) ? spots : new List<ParkingSpot>();
        }

        public bool AirportExists(string icao) => _spotsByIcao.ContainsKey(icao);
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double latitude, double longitude, double radiusNm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    // ── handlerData.airport JSON builders ───────────────────────────────────────────────────

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>One handlerData.airport.parkings[] entry — enough fields for
    /// GsxRemoteParkingReader to accept it as a real, selectable Gate Small stand.
    /// <paramref name="heading"/> null omits the "heading" key entirely, matching GSX's own
    /// real-world shape for the rare stand it publishes with no heading (see
    /// GsxRemoteParkingReaderTests.Gate_missing_heading_is_kept_with_NaN_not_dropped).</summary>
    private static Dictionary<string, object?> Parking(string uiGateName, double lat, double lon, double? heading,
        string uiTerminalName = "T1")
    {
        var dict = new Dictionary<string, object?>
        {
            ["uiGateName"] = uiGateName,
            ["uiTerminalName"] = uiTerminalName,
            ["uiType"] = "Gate Small",
            ["type"] = 8,
            ["GATE_SMALL"] = 8,
            ["lat"] = lat,
            ["lon"] = lon,
            ["maxWingspan"] = 30.0,
            ["parkingSystem"] = "Marshaller",
            ["gateDistanceThreshold"] = 25.0,
        };
        if (heading.HasValue) dict["heading"] = heading.Value;
        return dict;
    }

    private static string AirportJsonText(string icao, params Dictionary<string, object?>[] parkings)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["icao"] = icao,
            ["name"] = "Test",
            ["parkings"] = parkings,
        });

    private static JsonElement AirportJson(string icao, params Dictionary<string, object?>[] parkings)
        => Parse(AirportJsonText(icao, parkings));

    // ── GateDataSource construction helper ──────────────────────────────────────────────────

    private GateDataSource Build(
        IAirportDataProvider navdata,
        bool isGsxAvailable = false,
        GsxProfileLocator? locator = null,
        Func<IReadOnlyCollection<string>>? capabilities = null,
        Func<JsonElement?>? getHandlerDataAirport = null)
        => new(navdata, () => isGsxAvailable, locator ?? EmptyLocator(), capabilities, getHandlerDataAirport);

    /// <summary>Points at <see cref="_dir"/> without creating it — GsxProfileLocator.TryFindProfile
    /// short-circuits false on a non-existent directory, so this deterministically means "no .ini
    /// profile exists for any ICAO" without depending on anything installed on the test machine.</summary>
    private GsxProfileLocator EmptyLocator() => new(_dir);

    /// <summary>Writes one .ini file for <paramref name="icao"/> into <see cref="_dir"/> and
    /// returns a locator pointed there. Multiple calls (different ICAOs) may target the same
    /// test's <see cref="_dir"/> without colliding.</summary>
    private GsxProfileLocator LocatorWithIni(string icao, string iniContents)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, $"{icao}.ini"), iniContents);
        return new GsxProfileLocator(_dir);
    }

    private static readonly Func<IReadOnlyCollection<string>> HasHandlerData = () => new[] { "handlerData" };
    private static readonly Func<IReadOnlyCollection<string>> NoHandlerData = () => new[] { "gate" };

    // ── 1. Current airport + capability -> the Remote API path ─────────────────────────────

    [Fact]
    public void Current_airport_with_capability_routes_to_the_remote_api_path()
    {
        var navdata = new FakeAirportDataProvider(); // no KJFK entry at all -- proves it's unused
        var airport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, heading: 90.0));
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => airport);

        var spots = source.GetGates(Kjfk);

        var spot = Assert.Single(spots);
        Assert.Equal("Gate 1", spot.GsxIdentifier);
        // API-sourced spots still report GateSource.Gsx on ParkingSpot.Source itself (they
        // share the .ini path's metres-based Radius/MaxWingspanMeters convention) — the NEW
        // third answer is GetActiveSource-only, see the GetActiveSource tests below.
        Assert.Equal(GateSource.Gsx, spot.Source);
        Assert.Empty(navdata.GetParkingSpotsCalls); // navdata/GsxNavdataMerger never touched
    }

    // ── 2. A DIFFERENT icao -> the existing path, identical to today ───────────────────────

    [Fact]
    public void A_different_icao_routes_to_the_existing_path_identical_to_todays_behaviour()
    {
        var navdataEddfSpots = new List<ParkingSpot> { new() { AirportICAO = Eddf, Name = "Navdata Stand", Number = 1 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Eddf] = navdataEddfSpots });
        // GSX's CURRENT airport is KJFK -- the pilot is asking about EDDF (a typed remote ICAO,
        // e.g. planning a taxi route at the destination). No .ini for EDDF either (EmptyLocator).
        var kjfkAirport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, heading: 90.0));
        var source = Build(navdata, isGsxAvailable: true,
            capabilities: HasHandlerData, getHandlerDataAirport: () => kjfkAirport);

        var spots = source.GetGates(Eddf);

        // "Byte-identical to today" at the object level: the existing else-branch's last line
        // returns _navdata.GetParkingSpots(icao) verbatim, no copy, no mutation.
        Assert.Same(navdataEddfSpots, spots);
        Assert.Equal(new[] { Eddf }, navdata.GetParkingSpotsCalls);
    }

    // ── 3. Capability absent -> the existing path ───────────────────────────────────────────

    [Fact]
    public void Capability_absent_routes_to_the_existing_path_even_when_airport_data_matches()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        var kjfkAirport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, heading: 90.0));
        // Data WOULD match (same ICAO) if only the capability were advertised -- it isn't.
        var source = Build(navdata, isGsxAvailable: true,
            capabilities: NoHandlerData, getHandlerDataAirport: () => kjfkAirport);

        var spots = source.GetGates(Kjfk);

        Assert.Same(navdataSpots, spots);
    }

    // ── 4. handlerData.airport absent/null/malformed -> the existing path ──────────────────

    [Fact]
    public void Missing_handlerData_airport_routes_to_the_existing_path()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        // Capability IS advertised, but no airport data has arrived yet.
        var source = Build(navdata, isGsxAvailable: true,
            capabilities: HasHandlerData, getHandlerDataAirport: () => null);

        var spots = source.GetGates(Kjfk);

        Assert.Same(navdataSpots, spots);
    }

    [Fact]
    public void A_handlerData_airport_with_no_icao_property_also_routes_to_the_existing_path()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        var source = Build(navdata, isGsxAvailable: true,
            capabilities: HasHandlerData, getHandlerDataAirport: () => Parse("""{"name":"no icao field"}"""));

        var spots = source.GetGates(Kjfk);

        Assert.Same(navdataSpots, spots);
    }

    // ── 5/6. An API-path exception -> falls back rather than propagating ───────────────────

    [Fact]
    public void An_exception_from_the_capabilities_provider_falls_back_rather_than_propagating()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        var source = Build(navdata, isGsxAvailable: true,
            capabilities: () => throw new InvalidOperationException("capabilities not ready"),
            getHandlerDataAirport: () => AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0)));

        var spots = source.GetGates(Kjfk); // must not throw

        Assert.Same(navdataSpots, spots);
    }

    [Fact]
    public void An_exception_from_the_handlerData_airport_provider_falls_back_rather_than_propagating()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        var source = Build(navdata, isGsxAvailable: true,
            capabilities: HasHandlerData,
            getHandlerDataAirport: () => throw new InvalidOperationException("handlerData not ready"));

        var spots = source.GetGates(Kjfk);

        Assert.Same(navdataSpots, spots);
    }

    [Fact]
    public void GetActiveSource_also_never_throws_when_a_provider_throws()
    {
        var navdata = new FakeAirportDataProvider();
        var source = Build(navdata, capabilities: () => throw new InvalidOperationException("boom"));

        Assert.Equal(GateSource.Navdata, source.GetActiveSource(Kjfk));
    }

    // ── 7/8. A spot still lacking a usable heading after the join is dropped ───────────────

    [Fact]
    public void A_spot_still_lacking_a_usable_heading_after_the_join_is_dropped()
    {
        var navdata = new FakeAirportDataProvider();
        var airport = AirportJson(Kjfk,
            Parking("Gate 1", 10.0, 20.0, heading: 90.0),   // usable
            Parking("Gate 2", 30.0, 40.0, heading: null));  // GSX omitted heading; no .ini exists to recover it
        var source = Build(navdata, // EmptyLocator: no .ini for KJFK in this test
            capabilities: HasHandlerData, getHandlerDataAirport: () => airport);

        var spots = source.GetGates(Kjfk);

        var spot = Assert.Single(spots);
        Assert.Equal("Gate 1", spot.GsxIdentifier);
        Assert.DoesNotContain(spots, s => s.GsxIdentifier == "Gate 2");
    }

    [Fact]
    public void A_NaN_heading_recovered_by_the_ini_join_is_kept_not_dropped()
    {
        // Pins the ORDER: the .ini join must run BEFORE the drop-unusable-headings filter, or a
        // stand the join could have rescued gets discarded first.
        var navdata = new FakeAirportDataProvider();
        var airport = AirportJson(Kjfk, Parking("Gate 2", 30.0, 40.0, heading: null));
        var locator = LocatorWithIni(Kjfk, """
            [gate a 2]
            this_parking_pos = 30.0 40.0 271.5
            """);
        var source = Build(navdata, locator: locator,
            capabilities: HasHandlerData, getHandlerDataAirport: () => airport);

        var spots = source.GetGates(Kjfk);

        var spot = Assert.Single(spots);
        Assert.Equal("Gate 2", spot.GsxIdentifier);
        Assert.Equal(271.5, spot.Heading, 3);
    }

    // ── 9/10/11. The API path's own cache ───────────────────────────────────────────────────

    [Fact]
    public void The_api_paths_cache_does_not_serve_one_airports_stands_for_another()
    {
        var navdata = new FakeAirportDataProvider();
        JsonElement current = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0));
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => current);

        var kjfkSpots = source.GetGates(Kjfk);
        Assert.Single(kjfkSpots, s => s.GsxIdentifier == "Gate 1");

        // The aircraft has flown on: handlerData now describes EDDF instead.
        current = AirportJson(Eddf, Parking("Gate 9", 50.0, 8.0, 180.0));
        var eddfSpots = source.GetGates(Eddf);

        Assert.Single(eddfSpots, s => s.GsxIdentifier == "Gate 9");
        Assert.DoesNotContain(eddfSpots, s => s.GsxIdentifier == "Gate 1");
    }

    [Fact]
    public void The_api_paths_cache_refreshes_when_the_same_airports_handlerData_content_changes()
    {
        // The central risk the brief calls out: "handlerData changes when the aircraft moves OR
        // THE AIRPORT RELOADS" -- same ICAO, different content, must NOT be served stale.
        var navdata = new FakeAirportDataProvider();
        JsonElement current = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0));
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => current);

        var first = source.GetGates(Kjfk);
        Assert.Single(first, s => s.GsxIdentifier == "Gate 1");

        current = AirportJson(Kjfk, Parking("Gate 2", 30.0, 40.0, 180.0));
        var second = source.GetGates(Kjfk);

        Assert.Single(second, s => s.GsxIdentifier == "Gate 2");
        Assert.DoesNotContain(second, s => s.GsxIdentifier == "Gate 1");
    }

    [Fact]
    public void Unchanged_handlerData_content_reuses_the_cached_list_by_reference()
    {
        // Two SEPARATE JsonDocument/JsonElement instances parsed from the identical text --
        // simulates GSX republishing byte-identical content. Proves the cache keys on CONTENT
        // (raw JSON text), not on JsonElement/JsonDocument object identity.
        var navdata = new FakeAirportDataProvider();
        string airportJson = AirportJsonText(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0));
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => Parse(airportJson));

        var first = source.GetGates(Kjfk);
        var second = source.GetGates(Kjfk);

        Assert.Same(first, second); // same List<ParkingSpot> instance -> genuinely cached, not re-read/re-joined
    }

    // ── 12. GetActiveSource reports the third answer ────────────────────────────────────────

    [Fact]
    public void GetActiveSource_reports_GsxRemote_for_the_current_airport()
    {
        var navdata = new FakeAirportDataProvider();
        var airport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0));
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => airport);

        Assert.Equal(GateSource.GsxRemote, source.GetActiveSource(Kjfk));
    }

    [Fact]
    public void GetActiveSource_still_reports_Gsx_for_an_ini_profile_on_a_different_icao()
    {
        var navdata = new FakeAirportDataProvider();
        var kjfkAirport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0)); // current airport is KJFK
        var locator = LocatorWithIni(Eddf, """
            [gate a 1]
            this_parking_pos = 1.0 2.0 3.0
            """);
        var source = Build(navdata, isGsxAvailable: true, locator: locator,
            capabilities: HasHandlerData, getHandlerDataAirport: () => kjfkAirport);

        Assert.Equal(GateSource.Gsx, source.GetActiveSource(Eddf));
    }

    [Fact]
    public void GetActiveSource_still_reports_Navdata_when_neither_applies()
    {
        var navdata = new FakeAirportDataProvider();
        var source = Build(navdata); // no capability, no handlerData, GSX not available, empty locator

        Assert.Equal(GateSource.Navdata, source.GetActiveSource(Kjfk));
    }

    // ── 13. Backward compatibility: the one existing production call site ──────────────────

    [Fact]
    public void Existing_caller_that_omits_the_new_parameters_behaves_exactly_as_before()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        // The exact constructor shape MainForm.Dialogs.BuildGateDataSource uses today -- two
        // positional args, nothing else. The Remote API path must be structurally unreachable.
        var source = new GateDataSource(navdata, () => false);

        var spots = source.GetGates(Kjfk);

        Assert.Same(navdataSpots, spots);
        Assert.Equal(GateSource.Navdata, source.GetActiveSource(Kjfk));
    }

    // ── 14. A broken .ini keeps the API list (minus stop positions), does not fall back ────

    [Fact]
    public void A_broken_ini_keeps_the_api_list_without_stop_positions_rather_than_falling_back()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Should not be used", Number = 1 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        var airport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0));

        Directory.CreateDirectory(_dir);
        string iniPath = Path.Combine(_dir, $"{Kjfk}.ini");
        File.WriteAllText(iniPath, "[gate a 1]\nthis_parking_pos = 10.0 20.0 90.0\n");
        var locator = new GsxProfileLocator(_dir);

        // Exclusive lock: GsxProfileLocator.TryFindProfile still finds the file (it only lists
        // names), but GsxProfileParser.Parse's File.ReadAllLines throws IOException on it.
        using (new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var source = Build(navdata, locator: locator,
                capabilities: HasHandlerData, getHandlerDataAirport: () => airport);

            var spots = source.GetGates(Kjfk);

            var spot = Assert.Single(spots);
            Assert.Equal("Gate 1", spot.GsxIdentifier); // still the API list, NOT navdata
            Assert.Null(spot.StopLatitude); // the join couldn't run -- stop stays null, same as "no .ini"
        }
    }

    // ── 15. The else branch's .ini+navdata merge sub-path is still reachable ───────────────

    [Fact]
    public void An_ini_profile_for_a_different_remote_icao_still_merges_with_navdata_as_today()
    {
        var navdataEddfSpots = new List<ParkingSpot>
        {
            new() { AirportICAO = Eddf, Name = "A", Number = 1, Latitude = 1.0, Longitude = 2.0, Heading = 3.0, Radius = 100 },
        };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Eddf] = navdataEddfSpots });
        var kjfkAirport = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0)); // current airport is KJFK
        var locator = LocatorWithIni(Eddf, """
            [gate a 1]
            this_parking_pos = 50.0 8.0 45.0
            maxwingspan = 40.0
            """);
        var source = Build(navdata, isGsxAvailable: true, locator: locator,
            capabilities: HasHandlerData, getHandlerDataAirport: () => kjfkAirport);

        var spots = source.GetGates(Eddf);

        // GsxNavdataMerger ran and produced a GSX-overlaid spot -- the else branch's .ini
        // sub-path (unrelated to the current-airport API path) still works exactly as today.
        var spot = Assert.Single(spots);
        Assert.Equal(GateSource.Gsx, spot.Source);
        Assert.Equal(50.0, spot.Latitude); // GSX's own this_parking_pos wins over navdata's 1.0
    }

    // ── 16. An empty Remote API result falls back, and is never cached ─────────────────────

    [Fact]
    public void An_empty_remote_api_result_falls_back_to_the_existing_path()
    {
        var navdataSpots = new List<ParkingSpot> { new() { AirportICAO = Kjfk, Name = "Navdata KJFK", Number = 9 } };
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase) { [Kjfk] = navdataSpots });
        var airport = AirportJson(Kjfk); // well-formed, but zero parkings published yet
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => airport);

        var spots = source.GetGates(Kjfk);

        Assert.Same(navdataSpots, spots);
    }

    [Fact]
    public void An_empty_remote_api_result_is_not_cached_so_a_later_successful_read_still_works()
    {
        var navdata = new FakeAirportDataProvider();
        JsonElement current = AirportJson(Kjfk); // starts empty (GSX hasn't finished loading yet)
        var source = Build(navdata, capabilities: HasHandlerData, getHandlerDataAirport: () => current);

        var first = source.GetGates(Kjfk);
        Assert.Empty(first); // falls back to (empty) navdata

        current = AirportJson(Kjfk, Parking("Gate 1", 10.0, 20.0, 90.0)); // GSX finished loading
        var second = source.GetGates(Kjfk);

        Assert.Single(second, s => s.GsxIdentifier == "Gate 1");
    }
}
