// Pins Services/GateResolver — the TCAS window's "at Gate A 25" label — against the app-wide
// stand-name seam (Services/ParkingSpotSource). Before this, GateResolver read the raw navdata
// parking list, so at KJFK Terminal 4 the TCAS list said "at Gate A 25" for the same stand every
// other readout (taxi dialog, Where-Am-I, SayIntentions) called "B 25": one stand, two names, in
// one session — the exact defect ParkingSpotSource exists to prevent. GsxStandNameOverlay's own
// tests pin WHY the corrected letter is B; this file pins that the TCAS label now GETS it, and
// that the resolver's per-ICAO cache follows the gate-list source rather than freezing on the
// first answer.

using System.Text.Json;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx;

namespace MSFSBlindAssist.Tests;

public class GateResolverTests : IDisposable
{
    private const string Kjfk = "KJFK";

    // Same one-scratch-directory-per-test-instance pattern as GateDataSourceRoutingTests: a
    // GsxProfileLocator pointed at a directory that does not exist deterministically means "no
    // .ini profile for any ICAO", regardless of what is installed on the machine running these.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gr-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ── A minimal IAirportDataProvider (navdata) ────────────────────────────────────────────
    private sealed class FakeAirportDataProvider : IAirportDataProvider
    {
        private readonly Func<string, List<ParkingSpot>> _spotsFor;
        public int GetParkingSpotsCalls { get; private set; }

        public FakeAirportDataProvider(Func<string, List<ParkingSpot>> spotsFor) => _spotsFor = spotsFor;

        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => string.Empty;
        public Airport? GetAirport(string icao) => null;
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) { GetParkingSpotsCalls++; return _spotsFor(icao); }
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new() { Kjfk };
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    // The real "Gate 25" @ "Terminal 4 - Concourse B" coordinates from the committed KJFK capture.
    private const double Gate25Lat = 40.64213;
    private const double Gate25Lon = -73.77872;

    /// <summary>A navdata-shaped Gate Medium row: Name is the bare concourse letter, exactly as
    /// LittleNavMapProvider.MapParkingName produces it from the BGL enum. Built FRESH per call, as
    /// every real provider does — GsxStandNameOverlay corrects the list in place.</summary>
    private static List<ParkingSpot> NavdataA25() => new()
    {
        new() { AirportICAO = Kjfk, Name = "A", Number = 25, Type = 10,
                Latitude = Gate25Lat, Longitude = Gate25Lon, Radius = 100, Source = GateSource.Navdata },
    };

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>A handlerData.airport whose one stand is GSX's "Gate 25" at "Terminal 4 - Concourse
    /// B" — the same physical stand as the navdata A25 row, which GSX (correctly) calls B25.</summary>
    private static JsonElement GsxAirportWithB25() => Parse($$"""
        {"icao":"KJFK","name":"Kennedy Intl","parkings":[
          {"uiGateName":"Gate B25","uiTerminalName":"Terminal 4 - Concourse B","uiType":"Gate Medium",
           "type":9,"GATE_MEDIUM":9,"lat":{{Gate25Lat}},"lon":{{Gate25Lon}},"heading":270.0,
           "maxWingspan":50.0,"hasJetway":1}]}
        """);

    private GateDataSource RemoteApiSource(IAirportDataProvider navdata, Func<JsonElement?> airport, Func<long>? version = null)
        => new(navdata, () => true, new GsxProfileLocator(_dir),
               capabilities: () => new[] { "handlerData" },
               getHandlerDataAirport: airport,
               handlerDataVersion: version);

    private static TcasTraffic ParkedAtGate25() => new()
    {
        Callsign = "DAL123", OnGround = true, GroundSpeedKnots = 0,
        Latitude = Gate25Lat, Longitude = Gate25Lon, FromAirport = Kjfk,
    };

    // ── THE defect ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_TCAS_label_uses_the_corrected_stand_name_not_navdatas_letter()
    {
        var navdata = new FakeAirportDataProvider(_ => NavdataA25());
        var airport = GsxAirportWithB25();

        // BEFORE (no gate source wired): navdata's letter, verbatim.
        Assert.Equal("Gate A 25", new GateResolver(navdata).Resolve(ParkedAtGate25()));

        // AFTER: the same resolver, given the app's gate source, names the stand the way every
        // other readout does.
        var resolver = new GateResolver(navdata, () => RemoteApiSource(navdata, () => airport));
        Assert.Equal("Gate B 25", resolver.Resolve(ParkedAtGate25()));
    }

    [Fact]
    public void The_resolver_names_stands_from_the_NAVDATA_set_never_the_selectable_gate_list()
    {
        // GetNamedSpots, never GetSelectableGates: the resolver NAMES a stand a TCAS target is
        // parked at, it does not act on one — and a navdata stand GSX does not list (Vehicle/
        // Fuel, or one dropped for having no heading) must still be nameable. Here the GSX list
        // has NO stand near the traffic; only navdata does. The label must still resolve.
        var navdata = new FakeAirportDataProvider(_ => new List<ParkingSpot>
        {
            new() { AirportICAO = Kjfk, Name = "Parking", Number = 301, Type = 2,
                    Latitude = 40.6456, Longitude = -73.8011, Radius = 60, Source = GateSource.Navdata },
        });
        var airport = GsxAirportWithB25(); // B25 is ~2 km from Parking 301
        var resolver = new GateResolver(navdata, () => RemoteApiSource(navdata, () => airport));

        var traffic = new TcasTraffic
        {
            Callsign = "N123", OnGround = true, GroundSpeedKnots = 0,
            Latitude = 40.6456, Longitude = -73.8011, FromAirport = Kjfk,
        };

        Assert.Equal("Ramp Parking 301", resolver.Resolve(traffic));
    }

    // ── The cache follows the gate-list source ──────────────────────────────────────────────

    [Fact]
    public void The_per_icao_cache_rebuilds_when_the_gate_list_source_moves()
    {
        // The TCAS window can be open before GSX has published the airport. Frozen on its first
        // answer, the resolver would keep saying "A 25" for the whole session after every other
        // readout had switched to "B 25" — the same one-stand-two-names defect by another route.
        JsonElement? current = null;
        var navdata = new FakeAirportDataProvider(_ => NavdataA25());
        var resolver = new GateResolver(navdata, () => RemoteApiSource(navdata, () => current));

        Assert.Equal("Gate A 25", resolver.Resolve(ParkedAtGate25())); // GSX not published yet: navdata's letter
        int callsAfterFirst = navdata.GetParkingSpotsCalls;

        Assert.Equal("Gate A 25", resolver.Resolve(ParkedAtGate25())); // same source: served from cache
        Assert.Equal(callsAfterFirst, navdata.GetParkingSpotsCalls);

        current = GsxAirportWithB25();                                  // GSX publishes KJFK
        Assert.Equal("Gate B 25", resolver.Resolve(ParkedAtGate25()));
    }

    [Fact]
    public void An_unchanged_source_is_served_from_the_cache_without_re_reading_navdata()
    {
        var airport = GsxAirportWithB25();
        var navdata = new FakeAirportDataProvider(_ => NavdataA25());
        var resolver = new GateResolver(navdata, () => RemoteApiSource(navdata, () => airport));

        resolver.Resolve(ParkedAtGate25());
        int calls = navdata.GetParkingSpotsCalls;
        resolver.Resolve(ParkedAtGate25());
        resolver.Resolve(ParkedAtGate25());

        Assert.Equal(calls, navdata.GetParkingSpotsCalls);
    }

    [Fact]
    public void ClearCache_drops_the_spots_and_the_gate_source_so_a_database_switch_starts_clean()
    {
        int factoryCalls = 0;
        var navdata = new FakeAirportDataProvider(_ => NavdataA25());
        var airport = GsxAirportWithB25();
        var resolver = new GateResolver(navdata, () => { factoryCalls++; return RemoteApiSource(navdata, () => airport); });

        resolver.Resolve(ParkedAtGate25());
        resolver.Resolve(ParkedAtGate25());
        Assert.Equal(1, factoryCalls);       // ONE lazily-created gate source, reused

        resolver.ClearCache();
        int callsBefore = navdata.GetParkingSpotsCalls;
        Assert.Equal("Gate B 25", resolver.Resolve(ParkedAtGate25()));
        Assert.Equal(2, factoryCalls);       // recreated after the clear
        Assert.True(navdata.GetParkingSpotsCalls > callsBefore);   // and the spots re-read
    }

    // ── "Null entry = tried, no spots" semantics are preserved ──────────────────────────────

    [Fact]
    public void An_airport_with_no_spots_resolves_null_and_is_not_re_queried_while_the_source_holds()
    {
        var navdata = new FakeAirportDataProvider(_ => new List<ParkingSpot>());
        var resolver = new GateResolver(navdata, () => RemoteApiSource(navdata, () => null));

        Assert.Null(resolver.Resolve(ParkedAtGate25()));
        int calls = navdata.GetParkingSpotsCalls;
        Assert.Null(resolver.Resolve(ParkedAtGate25()));
        Assert.Equal(calls, navdata.GetParkingSpotsCalls);
    }

    [Fact]
    public void A_null_gate_source_factory_result_degrades_to_the_plain_navdata_name()
    {
        var navdata = new FakeAirportDataProvider(_ => NavdataA25());
        var resolver = new GateResolver(navdata, () => null);
        Assert.Equal("Gate A 25", resolver.Resolve(ParkedAtGate25()));
    }
}
