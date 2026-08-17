// Pins Services/ParkingSpotSource — the ONE seam through which every readout that can name a
// stand gets that name, so a stand cannot be called two different things in one session.
//
// The seam has two shapes and the difference is the whole point of this file:
//   GetSelectableGates — "which stands can I be sent to?"  -> GSX's own list (identity + metadata)
//   GetNamedSpots      — "what is this stand called?"      -> NAVDATA's set, names corrected in place
//
// GetNamedSpots must never swap the SET. TaxiGraph.Build's parking pass marks node TYPES as well as
// names, and NamedHoldingPointResolver / HoldShortNodeResolver / the route truncation all read that
// type — so a different set of spots could move a hold-short. A stand GSX omits (Vehicle/Fuel) must
// also keep its Where-Am-I label. Both are pinned below.
//
// The motivating defect is the first test, encoded from the REAL committed KJFK capture joined to
// the REAL navdata letter measured against it, not from an invented conflict: an aircraft parked
// exactly at B25 was told "Aircraft appears near A 25, not assigned gate Terminal 4 Gate B25".
//
// GsxConcourseLetterFillerTests already pins WHY the corrected name is B25 (the GSX terminal beats
// navdata, 46 of 222 stands, measured); this file pins that the readouts now GET it.

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
    private static ParkingSpot Nav(string name, int number, double lat, double lon) =>
        new() { AirportICAO = Kjfk, Name = name, Number = number, Latitude = lat, Longitude = lon,
                Type = 10, Source = GateSource.Navdata };

    /// <summary>The real "Gate 25" @ "Terminal 4 - Concourse B" stand from the committed capture —
    /// the one stand the two datasets provably disagree about.</summary>
    private static ParkingSpot Kjfk25(JsonElement airport)
        => MSFSBlindAssist.Services.Gsx.Remote.GsxRemoteParkingReader.Read(airport, Kjfk)
               .Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");

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
        var gate25 = Kjfk25(apiAirport);

        // The navdata row for that same physical stand, at the API stand's own coordinates —
        // the real relationship between the two datasets.
        var navdataRow = Nav("A", 25, gate25.Latitude, gate25.Longitude);
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { navdataRow },
        });

        const string assigned = "Terminal 4 Gate B25";

        // BEFORE: the readout called GetParkingSpots directly and got navdata's letter. It found
        // the right physical stand and announced the wrong identity for it.
        Assert.False(MainForm.NearestSpotMatchesAssignedGate(navdata.GetParkingSpots(Kjfk).Single(), assigned));

        // AFTER: the same readout resolves through the seam. Same row, corrected name.
        var spots = ParkingSpotSource.GetNamedSpots(navdata, RemoteApiSource(navdata, apiAirport), Kjfk);
        var nearest = Assert.Single(spots);

        Assert.Equal("B", nearest.Name);
        Assert.True(MainForm.NearestSpotMatchesAssignedGate(nearest, assigned));

        // Not a stand-matches-anything: the neighbouring concourse still fails.
        Assert.False(MainForm.NearestSpotMatchesAssignedGate(nearest, "Terminal 4 Gate A25"));

        // The stand itself is untouched apart from its name — this is a NAME fix, and the
        // coordinates are what TaxiGraph.Build matches a node on.
        Assert.Equal(gate25.Latitude, nearest.Latitude);
        Assert.Equal(gate25.Longitude, nearest.Longitude);
        Assert.Equal(25, nearest.Number);
        Assert.Equal(GateSource.Navdata, nearest.Source);
    }

    // ── The guarantee that makes this safe to feed to TaxiGraph.Build ───────────────────────

    [Fact]
    public void A_navdata_stand_GSX_does_not_have_survives_with_its_navdata_name_intact()
    {
        // THE property the whole design rests on. GSX's list is not navdata's set: the reader
        // excludes Vehicle/Fuel parkings, and DropUnusableHeadings discards any stand neither GSX
        // nor the .ini gave a heading. If the seam returned GSX's list, every such stand would
        // vanish from the graph — losing its Where-Am-I label ("Parking 301" -> "Near taxiway X")
        // AND un-marking its graph node as Parking, which NamedHoldingPointResolver and the
        // hold-short scans read. So the set must come through unchanged.
        var apiAirport = KjfkAirport();
        var gate25 = Kjfk25(apiAirport);

        // "Parking 301" is a real capture entry with uiType "Vehicle" — GsxRemoteParkingReader
        // excludes it, so GSX's list genuinely does not contain this stand.
        var vehicleStand = Nav("Parking", 301, 40.64565762877464, -73.80112275481224);
        // A lettered stand nowhere near any GSX stand: nothing can speak for its letter.
        var loneStand = Nav("C", 99, 40.7000, -73.9000);
        var conflicted = Nav("A", 25, gate25.Latitude, gate25.Longitude);

        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { vehicleStand, loneStand, conflicted },
        });

        var spots = ParkingSpotSource.GetNamedSpots(navdata, RemoteApiSource(navdata, apiAirport), Kjfk);

        // SAME SET: same count, same order, same coordinates. Nothing added from GSX's 231 stands,
        // nothing removed.
        Assert.Equal(3, spots.Count);
        Assert.Same(vehicleStand, spots[0]);
        Assert.Same(loneStand, spots[1]);
        Assert.Same(conflicted, spots[2]);

        // The two stands GSX cannot speak for keep their navdata names verbatim...
        Assert.Equal("Parking", spots[0].Name);
        Assert.Equal(301, spots[0].Number);
        Assert.Equal("C", spots[1].Name);
        Assert.Equal(99, spots[1].Number);

        // ...while the one it can is corrected.
        Assert.Equal("B", spots[2].Name);
    }

    [Fact]
    public void A_stand_category_is_never_replaced_by_a_concourse_letter()
    {
        // LittleNavMapProvider.MapParkingName produces WORDS for non-concourse parking ("Parking",
        // "North", "Dock"). A word is a stand CATEGORY, not a competing identity claim — asserting
        // that a GA parking spot is really gate B is a wrong stand identity, and no measurement
        // supports it. Only an empty name, or a bare letter GSX disagrees with, may be written.
        var apiAirport = KjfkAirport();
        var gate25 = Kjfk25(apiAirport);

        var category = Nav("Parking", 25, gate25.Latitude, gate25.Longitude);
        var empty = Nav("", 25, gate25.Latitude, gate25.Longitude);
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { category, empty },
        });

        var spots = ParkingSpotSource.GetNamedSpots(navdata, RemoteApiSource(navdata, apiAirport), Kjfk);

        Assert.Equal("Parking", spots[0].Name);   // left alone
        Assert.Equal("B", spots[1].Name);         // filled, exactly as the .ini path has always done
    }

    // ── The selectable shape stays GSX's own list ───────────────────────────────────────────

    [Fact]
    public void The_selectable_gate_list_is_still_GSXs_own_with_its_identity_and_metadata()
    {
        // The two dialogs must NOT get the navdata set: a destination has to be acted on, and that
        // needs GsxIdentifier (gate.select), TerminalName (telling identically-named stands apart)
        // and GSX's own size/stop metadata. This is what they already did; the seam preserves it.
        var apiAirport = KjfkAirport();
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { Nav("A", 25, 40.6, -73.8) },
        });

        var gates = ParkingSpotSource.GetSelectableGates(navdata, RemoteApiSource(navdata, apiAirport), Kjfk);

        // GSX's list, not navdata's single row. 230, not the reader's 231: GateDataSource's
        // DropUnusableHeadings discards the one KJFK stand GSX publishes with no heading, and with
        // no .ini here nothing recovers it. That one-stand gap is exactly why GetNamedSpots must
        // NOT be this list — a stand missing from it would lose its Where-Am-I label and its
        // graph node's Parking mark. Harmless HERE: you cannot be sent to a stand with no heading.
        Assert.Equal(230, gates.Count);
        var g25 = gates.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        Assert.Equal("B", g25.Name);
        Assert.Equal(GateSource.Gsx, g25.Source);
    }

    // ── The unwired default is byte-identical to the pre-seam call ──────────────────────────

    [Fact]
    public void No_gate_source_returns_the_navdata_list_unchanged()
    {
        // Every caller that does not wire a GateDataSource — the xUnit suite, a form constructed
        // outside MainForm, a session with no database — must behave exactly as before the seam.
        var row = Nav("A", 25, 40.6, -73.8);
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { row },
        });

        var named = ParkingSpotSource.GetNamedSpots(navdata, gateSource: null, Kjfk);
        Assert.Same(row, Assert.Single(named));
        Assert.Equal("A", named[0].Name);

        var selectable = ParkingSpotSource.GetSelectableGates(navdata, gateSource: null, Kjfk);
        Assert.Equal("A", Assert.Single(selectable).Name);
    }

    [Fact]
    public void An_airport_the_navdata_does_not_know_yields_an_empty_list_not_a_throw()
    {
        var empty = new FakeAirportDataProvider();
        Assert.Empty(ParkingSpotSource.GetNamedSpots(empty, gateSource: null, "ZZZZ"));
        Assert.Empty(ParkingSpotSource.GetSelectableGates(empty, gateSource: null, "ZZZZ"));
    }

    // ── Aliases follow the CORRECTED name ───────────────────────────────────────────────────

    [Fact]
    public void Online_aliases_are_resolved_against_the_corrected_identity_not_the_old_one()
    {
        // GateAliasResolver matches an online stand to a gate on their concourse letters agreeing,
        // so an alias resolved BEFORE the correction is resolved against the wrong identity: with
        // navdata still calling this stand A25, the online "B25A" would be rejected outright for
        // disagreeing on the letter. Re-running AugmentParking after the correction is what makes
        // the alias leg — a real matching leg, not decoration (live KDTW: scenery A 24A, SI/OSM
        // A24) — agree with the name the pilot is now given.
        var apiAirport = KjfkAirport();
        var gate25 = Kjfk25(apiAirport);

        var row = Nav("A", 25, gate25.Latitude, gate25.Longitude);
        var navdata = new FakeAirportDataProvider(new(StringComparer.OrdinalIgnoreCase)
        {
            [Kjfk] = new List<ParkingSpot> { row },
        });

        var cache = new TaxiDataCache(ttlDays: 1);
        var online = new AirportTaxiData { Source = "test" };
        online.Parking.Add(("B25A", gate25.Latitude, gate25.Longitude));
        cache.Save(Kjfk, new[] { online });

        var augmenting = new AugmentingAirportDataProvider(
            navdata, cache, Array.Empty<ITaxiDataSource>(), new MergeOptions());

        var spots = ParkingSpotSource.GetNamedSpots(augmenting, RemoteApiSource(navdata, apiAirport), Kjfk);

        var stand = Assert.Single(spots);
        Assert.Equal("B", stand.Name);
        Assert.Contains("B25A", stand.Aliases);

        // Idempotent: the seam runs once per graph build and once per readout on a freshly-read
        // list, so a second call must neither re-correct nor accumulate duplicate aliases.
        var again = ParkingSpotSource.GetNamedSpots(augmenting, RemoteApiSource(navdata, apiAirport), Kjfk);
        Assert.Equal("B", Assert.Single(again).Name);
        Assert.Equal(1, again[0].Aliases.Count(a => a == "B25A"));
    }
}
