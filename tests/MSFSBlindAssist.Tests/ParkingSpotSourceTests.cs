// Pins Services/ParkingSpotSource — the ONE seam through which every readout that can name a
// stand resolves its parking list, so a stand cannot be called two different things in one
// session.
//
// The motivating defect is the first test below, and it is encoded from the REAL committed KJFK
// capture joined to the REAL navdata letter measured against it, not from an invented conflict:
// an aircraft parked exactly at B25 was told "Aircraft appears near A 25, not assigned gate
// Terminal 4 Gate B25", because the SayIntentions readout called GetParkingSpots directly while
// the dialogs went through GateDataSource.
//
// GsxConcourseLetterFillerTests already pins WHY the corrected name is B25 (the terminal beats
// navdata, 46 of 222 stands, measured); this file pins that the readout now GETS it.

using System.Text.Json;
using MSFSBlindAssist;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class ParkingSpotSourceTests : IDisposable
{
    private const string Kjfk = "KJFK";

    // Same one-scratch-directory-per-test-instance pattern as GateDataSourceRoutingTests: a
    // GsxProfileLocator pointed at a directory that does not exist deterministically means "no
    // .ini profile for any ICAO", regardless of what is installed on the machine running these.
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pss-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ── A minimal IAirportDataProvider (navdata) ────────────────────────────────────────────
    private sealed class FakeAirportDataProvider : IAirportDataProvider
    {
        private readonly Dictionary<string, List<ParkingSpot>> _spotsByIcao;
        public FakeAirportDataProvider(Dictionary<string, List<ParkingSpot>>? spotsByIcao = null)
            => _spotsByIcao = spotsByIcao ?? new(StringComparer.OrdinalIgnoreCase);

        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => string.Empty;
        public Airport? GetAirport(string icao) => null;
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao)
            => _spotsByIcao.TryGetValue(icao, out var s) ? s : new List<ParkingSpot>();
        public bool AirportExists(string icao) => _spotsByIcao.ContainsKey(icao);
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    /// <summary>The committed live KJFK capture. Its ROOT is already the handlerData.airport
    /// shape (icao / name / parkings), so it feeds GateDataSource's Remote API path directly.</summary>
    private static JsonElement KjfkAirport()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-handlerdata-parkings-kjfk.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private GsxProfileLocator EmptyLocator() => new(_dir);

    private GateDataSource RemoteApiSource(IAirportDataProvider navdata, JsonElement airport)
        => new(navdata, () => false, EmptyLocator(),
               capabilities: () => new[] { "handlerData" },
               getHandlerDataAirport: () => airport);

    /// <summary>A navdata-shaped spot: Name is the bare concourse letter, exactly as
    /// LittleNavMapProvider.MapParkingName already produces it from the BGL enum ("GA" -> "A").</summary>
    private static ParkingSpot Nav(string letter, int number, double lat, double lon) =>
        new() { AirportICAO = Kjfk, Name = letter, Number = number, Latitude = lat, Longitude = lon,
                Type = 10, Source = GateSource.Navdata };

    // ── THE defect ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_stand_SayIntentions_assigned_now_matches_where_navdatas_own_name_could_not()
    {
        // KJFK Terminal 4, from the real capture: GSX publishes uiGateName "Gate 25" under
        // uiTerminalName "Terminal 4 - Concourse B", and the real T4 is Concourse A (A2-A7) plus
        // Concourse B (B20-B41) — so it is B25, which is what a controller and SayIntentions say.
        // Navdata calls it A, because its letter rides in the BGL parking NAME ENUM and KJFK's
        // scenery fills GATE_A across the whole concourse (navdata and GSX disagree on 46 of 222
        // letterless stands there; GSX right in every sampled case).
        var apiAirport = KjfkAirport();
        var gsxOnly = MSFSBlindAssist.Services.Gsx.Remote.GsxRemoteParkingReader.Read(apiAirport, Kjfk);
        var gate25 = gsxOnly.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");

        // The navdata row for that same physical stand — placed at the API stand's own
        // coordinates, which is the real relationship between the two datasets.
        var navdataRow = Nav("A", 25, gate25.Latitude, gate25.Longitude);
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { navdataRow },
        });

        const string assigned = "Terminal 4 Gate B25";

        // BEFORE: the readout called GetParkingSpots directly and got navdata's letter. It found
        // the right physical stand and announced the wrong identity for it.
        var beforeSpots = navdata.GetParkingSpots(Kjfk);
        Assert.False(MainForm.NearestSpotMatchesAssignedGate(beforeSpots.Single(), assigned));

        // AFTER: the same readout resolves through the seam and gets the authoritative name.
        var afterSpots = ParkingSpotSource.GetSpots(navdata, RemoteApiSource(navdata, apiAirport), Kjfk);
        var nearest = afterSpots.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");

        Assert.Equal("B", nearest.Name);
        Assert.True(MainForm.NearestSpotMatchesAssignedGate(nearest, assigned));

        // And it has not become a stand-matches-anything: the neighbouring concourse still fails.
        Assert.False(MainForm.NearestSpotMatchesAssignedGate(nearest, "Terminal 4 Gate A25"));
    }

    // ── The unwired default is byte-identical to the pre-seam call ──────────────────────────

    [Fact]
    public void No_gate_source_returns_the_navdata_list_unchanged()
    {
        // Every caller that does not wire a GateDataSource — the xUnit suite, a form constructed
        // outside MainForm, a session with no database — must behave exactly as before the seam.
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { Nav("A", 25, 40.6, -73.8) },
        });

        var spots = ParkingSpotSource.GetSpots(navdata, gateSource: null, Kjfk);

        var single = Assert.Single(spots);
        Assert.Equal("A", single.Name);
        Assert.Equal(25, single.Number);
        Assert.Equal(GateSource.Navdata, single.Source);
    }

    [Fact]
    public void An_airport_the_navdata_does_not_know_yields_an_empty_list_not_a_throw()
    {
        Assert.Empty(ParkingSpotSource.GetSpots(new FakeAirportDataProvider(), gateSource: null, "ZZZZ"));
    }

    // ── Aliases follow the name, on a GSX-sourced list too ──────────────────────────────────

    [Fact]
    public void A_GSX_sourced_stand_still_gets_this_scenerys_online_alias()
    {
        // The live KDTW arrival: the scenery names the stand A24A while SayIntentions, OSM and the
        // controller all say A24. AugmentingAirportDataProvider.GetParkingSpots aliases the NAVDATA
        // list on its way out, so a consumer that took the GSX list INSTEAD would get a list with
        // no aliases at all — and the alias is a real matching leg, not decoration. The seam runs
        // AugmentParking for every consumer so that cannot happen to one of them.
        const string Kdtw = "KDTW";
        double lat = 42.2125, lon = -83.3534;

        var navdata = new FakeAirportDataProvider();
        var cache = new TaxiDataCache(ttlDays: 1);
        var online = new AirportTaxiData { Source = "test" };
        online.Parking.Add(("A24", lat, lon));
        cache.Save(Kdtw, new[] { online });

        var augmenting = new AugmentingAirportDataProvider(
            navdata, cache, Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var airport = OneStandAirport(Kdtw, "Gate A24A", lat, lon);
        var gateSource = RemoteApiSource(navdata, airport);

        var spots = ParkingSpotSource.GetSpots(augmenting, gateSource, Kdtw);

        var stand = Assert.Single(spots);
        Assert.Equal("A", stand.Name);
        Assert.Equal(24, stand.Number);
        Assert.Equal("A", stand.Suffix);
        Assert.Contains("A24", stand.Aliases);

        // Idempotent: the seam is called once per graph build and once per dialog open on the same
        // cached list, so a second call must not accumulate duplicates.
        var again = ParkingSpotSource.GetSpots(augmenting, gateSource, Kdtw);
        Assert.Equal(1, Assert.Single(again).Aliases.Count(a => a == "A24"));
    }

    /// <summary>A handlerData.airport carrying exactly one selectable Gate Small stand — enough
    /// fields for GsxRemoteParkingReader to accept it. Mirrors GateDataSourceRoutingTests' own
    /// builder.</summary>
    private static JsonElement OneStandAirport(string icao, string uiGateName, double lat, double lon)
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["icao"] = icao,
            ["name"] = "Test",
            ["parkings"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["uiGateName"] = uiGateName,
                    ["uiTerminalName"] = "T1",
                    ["uiType"] = "Gate Small",
                    ["type"] = 8,
                    ["GATE_SMALL"] = 8,
                    ["lat"] = lat,
                    ["lon"] = lon,
                    ["heading"] = 90.0,
                    ["maxWingspan"] = 30.0,
                    ["parkingSystem"] = "Marshaller",
                    ["gateDistanceThreshold"] = 25.0,
                },
            },
        });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
