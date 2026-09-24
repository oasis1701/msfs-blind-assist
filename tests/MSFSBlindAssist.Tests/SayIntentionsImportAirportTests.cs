using MSFSBlindAssist;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The airport a SayIntentions taxi import is built at (MainForm.ResolveImportAirport): flight.json's
/// own candidates first, each validated against the navigation database (SelectImportAirport — the
/// rule that keeps an ARTCC ident such as KZOA from dead-ending the import), and ONLY when every one
/// is absent or unknown, the airport the aircraft is AT: CurrentAirport.Resolve, the resolver Where
/// Am I and Look Around use. That fallback used to be the first four-character code nearest the
/// reference point — never a field with a three-character ident, and heliport 10CL at 111 of KSNA's
/// 201 stands, where the import would abort "No taxi path data available for 10CL."
/// </summary>
public class SayIntentionsImportAirportTests
{
    // Auburn Municipal (S50), a three-character ident: the exact fs2024 row.
    private static readonly AirportCandidate S50 =
        new("S50", 47.327625, -122.226654, -122.226730, -122.223457, 47.333683, 47.322327, 431);

    /// <summary>Counts both queries, so a test can prove which one answered.</summary>
    private sealed class FakeProvider : IAirportDataProvider, IAirportFacilitiesProvider
    {
        private readonly IReadOnlyList<AirportCandidate> _candidates;
        private readonly List<string> _legacy;
        public int CandidateCalls { get; private set; }
        public int NearbyIcaoCalls { get; private set; }

        public FakeProvider(IReadOnlyList<AirportCandidate> candidates, List<string> legacy)
        { _candidates = candidates; _legacy = legacy; }

        public IReadOnlyList<AirportCandidate> GetNearbyAirportCandidates(double lat, double lon, double nm)
        { CandidateCalls++; return _candidates; }
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm)
        { NearbyIcaoCalls++; return _legacy; }

        public AirportFacilities? GetAirportFacilities(string icao) => null;
        public bool DatabaseExists => true;
        public string DatabaseType => "Fake";
        public string DatabasePath => string.Empty;
        public Airport? GetAirport(string icao) => null;
        public List<Runway> GetRunways(string icao) => new();
        public ILSData? GetILSForRunway(string icao, string runwayName) => null;
        public List<ParkingSpot> GetParkingSpots(string icao) => new();
        public bool AirportExists(string icao) => true;
        public int GetAirportCount() => 0;
        public int GetRunwayCount() => 0;
        public int GetParkingSpotCount() => 0;
        public HashSet<string> GetAllAirportICAOs() => new();
        public List<TaxiPath> GetTaxiPaths(string icao) => new();
        public List<StartPosition> GetRunwayStarts(string icao) => new();
    }

    [Fact]
    public void With_no_known_airport_in_flight_json_the_import_uses_the_airport_the_aircraft_is_at()
    {
        // Cruise-phase flight.json shape: current_airport is Oakland Center, origin/destination
        // absent. The legacy list leads with the heliport, exactly as the old rule saw it at P 86.
        var provider = new FakeProvider(
            new[] { Fs2024AirportRows.Heliport10Cl, Fs2024AirportRows.Ksna }, new List<string> { "10CL", "KSNA" });

        Assert.Equal("KSNA", MainForm.ResolveImportAirport(
            new string?[] { "KZOA", null, "" }, _ => false, provider,
            Fs2024AirportRows.KsnaStandP86Lat, Fs2024AirportRows.KsnaStandP86Lon));
        Assert.Equal(1, provider.CandidateCalls);
        Assert.Equal(0, provider.NearbyIcaoCalls);
    }

    [Fact]
    public void A_field_with_a_three_character_ident_is_an_answer()
    {
        // The old fallback's four-character filter could only ever answer the neighbour here.
        var provider = new FakeProvider(new[] { S50 }, new List<string> { "S50", "WA85" });

        Assert.Equal("S50", MainForm.ResolveImportAirport(
            new string?[] { null, null, null }, _ => false, provider, 47.3280, -122.2262));
    }

    [Fact]
    public void A_candidate_the_database_knows_wins_and_the_position_is_never_consulted()
    {
        var provider = new FakeProvider(
            new[] { Fs2024AirportRows.Heliport10Cl, Fs2024AirportRows.Ksna }, new List<string> { "10CL" });
        var known = new HashSet<string> { "KDEN", "KSFO" };

        Assert.Equal("KDEN", MainForm.ResolveImportAirport(
            new string?[] { "KZOA", "kden", "KSFO" }, known.Contains, provider,
            Fs2024AirportRows.KsnaStandP86Lat, Fs2024AirportRows.KsnaStandP86Lon));
        Assert.Equal(0, provider.CandidateCalls);
        Assert.Equal(0, provider.NearbyIcaoCalls);
    }

    [Fact]
    public void Nothing_known_and_no_airport_nearby_is_null()
    {
        var provider = new FakeProvider(Array.Empty<AirportCandidate>(), new List<string>());

        Assert.Null(MainForm.ResolveImportAirport(
            new string?[] { "KZOA", null, null }, _ => false, provider, 0.0, 0.0));
    }
}
