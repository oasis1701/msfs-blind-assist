using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class CurrentAirportResolverTests
{
    // Simplified from the fs2024 rows around John Wayne (KSNA) — Fs2024AirportRows carries the
    // exact ones. By the old rule's summed-degrees metric the heliport's reference point is nearer
    // than KSNA's own to 111 of KSNA's 201 stands, which is how Alt+L came to say "No taxi data
    // available for 10CL".
    private static readonly AirportCandidate Ksna = new("KSNA", 33.675667, -117.868222, -117.8790, -117.8570, 33.6890, 33.6630, 1598);
    private static readonly AirportCandidate Heliport = new("10CL", 33.6800, -117.8640, -117.8642, -117.8638, 33.6802, 33.6798, 0);

    // Exact fs2024 rows. 8TX2 Freeman Ranch has one runway and NO taxi paths; KECU Edwards County has
    // taxi paths and lies 4.4 km away — inside the 3 NM taxi-path pass, its own grown box nowhere
    // near 8TX2.
    private static readonly AirportCandidate Strip8Tx2 = new("8TX2", 29.978544, -100.201744, -100.205223, -100.198006, 29.981501, 29.976648, 0);
    private static readonly AirportCandidate Kecu = new("KECU", 29.946918, -100.173859, -100.177498, -100.170189, 29.951502, 29.942369, 31);

    [Fact]
    public void The_airport_whose_box_contains_the_aircraft_beats_a_nearer_heliport()
        => Assert.Equal("KSNA", CurrentAirportResolver.Pick(new[] { Heliport, Ksna }, 33.6795, -117.8645));

    [Theory]
    [InlineData(29.978544, -100.201744)]   // 8TX2's reference point
    [InlineData(29.976648, -100.205223)]   // the runway 05 end
    [InlineData(29.981501, -100.198006)]   // the runway 23 end
    public void A_strip_whose_box_contains_the_aircraft_beats_a_taxi_path_airport_within_3_NM(double lat, double lon)
        // Before the any-kind box pass this was "KECU": Where Am I on 8TX2's runway said "Not on a
        // known taxiway or ramp at KECU." about an airport 4.4 km away.
        => Assert.Equal("8TX2", CurrentAirportResolver.Pick(new[] { Kecu, Strip8Tx2 }, lat, lon));

    [Fact]
    public void The_same_holds_at_the_other_strips_the_review_measured()
    {
        // Exact fs2024 rows. SBHB went to SSYK (1.9 km off), SAKH to SAJF (1.1 km off): both
        // neighbours have taxi paths, and neither neighbour's grown box reaches the strip.
        var sbhb = new AirportCandidate("SBHB", -23.620821, -48.510406, -48.513737, -48.507076, -23.616276, -23.625366, 0);
        var ssyk = new AirportCandidate("SSYK", -23.619722, -48.491390, -48.494900, -48.490921, -23.618263, -23.628616, 29);
        Assert.Equal("SBHB", CurrentAirportResolver.Pick(new[] { ssyk, sbhb }, -23.620821, -48.510406));

        var sakh = new AirportCandidate("SAKH", -35.104816, -62.027363, -62.030224, -62.024502, -35.102646, -35.106987, 0);
        var sajf = new AirportCandidate("SAJF", -35.100426, -62.016521, -62.019863, -62.013180, -35.097729, -35.103123, 15);
        Assert.Equal("SAKH", CurrentAirportResolver.Pick(new[] { sajf, sakh }, -35.104816, -62.027363));
    }

    [Fact]
    public void An_airport_with_taxi_paths_keeps_its_stands_inside_an_overlapping_heliport_box()
    {
        // KSNA stand P 86 lies inside BOTH boxes and nearer the heliport's reference point. The
        // any-kind box pass would answer "10CL" here; the taxi-path box pass must keep answering
        // first, or 41 of KSNA's stands go to the heliport.
        Assert.Equal("KSNA", CurrentAirportResolver.Pick(
            new[] { Fs2024AirportRows.Heliport10Cl, Fs2024AirportRows.Ksna },
            Fs2024AirportRows.KsnaStandP86Lat, Fs2024AirportRows.KsnaStandP86Lon));
    }

    [Fact]
    public void A_strip_whose_grown_box_misses_the_aircraft_still_loses_to_a_taxi_path_airport_within_3_NM()
    {
        // The any-kind pass is CONTAINMENT, not proximity. The strip's reference point is 504 m east
        // and its box, grown 300 m, stops ~196 m short of the aircraft; KBBB (taxi paths) is 3.3 km
        // north with its grown box ~1.9 km short. Only the 3 NM taxi-path pass can answer.
        var strip = new AirportCandidate("1XX2", 45.0000, -99.9936, -99.9937, -99.9935, 45.0001, 44.9999, 0);
        var kbbb = new AirportCandidate("KBBB", 45.0300, -100.0000, -100.0100, -99.9900, 45.0400, 45.0200, 200);
        Assert.Equal("KBBB", CurrentAirportResolver.Pick(new[] { strip, kbbb }, 45.0000, -100.0000));
    }

    [Fact]
    public void Outside_every_box_with_no_taxi_path_airport_within_3_NM_the_nearest_of_any_kind_answers()
    {
        // 1.1 km north of the heliport, outside its grown box; FARR has taxi paths but is 3.6 NM off.
        var far = new AirportCandidate("FARR", 33.7500, -117.8645, -117.87, -117.86, 33.76, 33.74, 900);
        Assert.Equal("10CL", CurrentAirportResolver.Pick(new[] { far, Heliport }, 33.6900, -117.8640));
    }

    [Fact]
    public void A_three_character_ident_resolves_like_any_other()
    {
        var s50 = new AirportCandidate("S50", 47.3277, -122.2265, -122.2290, -122.2240, 47.3330, 47.3220, 431);
        var strip = new AirportCandidate("WA85", 47.3400, -122.2300, -122.2301, -122.2299, 47.3401, 47.3399, 0);
        Assert.Equal("S50", CurrentAirportResolver.Pick(new[] { strip, s50 }, 47.3280, -122.2262));
    }

    [Fact]
    public void Outside_every_box_the_nearest_airport_WITH_taxi_paths_wins_by_true_distance()
    {
        // At 60° N a degree of longitude is half a degree of latitude. A is 0.020° east (1.1 km),
        // B is 0.012° north (1.3 km): summed raw degrees pick B; metres pick A.
        var a = new AirportCandidate("AAAA", 60.0000, 10.0200, 10.0190, 10.0210, 60.0005, 59.9995, 50);
        var b = new AirportCandidate("BBBB", 60.0120, 10.0000, 9.9990, 10.0010, 60.0125, 60.0115, 50);
        Assert.Equal("AAAA", CurrentAirportResolver.Pick(new[] { b, a }, 60.0000, 10.0000));
    }

    [Fact]
    public void With_only_heliports_around_the_nearest_one_is_still_an_answer_and_nothing_is_null()
    {
        Assert.Equal("10CL", CurrentAirportResolver.Pick(new[] { Heliport }, 33.6801, -117.8641));
        Assert.Null(CurrentAirportResolver.Pick(Array.Empty<AirportCandidate>(), 0, 0));
    }

    [Fact]
    public void An_airport_with_taxi_paths_more_than_3_NM_away_does_not_beat_nothing_nearer()
    {
        var far = new AirportCandidate("FARR", 33.7500, -117.8645, -117.87, -117.86, 33.76, 33.74, 900);   // ~4.2 NM north
        Assert.Equal("10CL", CurrentAirportResolver.Pick(new[] { far, Heliport }, 33.6801, -117.8641));
    }

    [Fact]
    public void The_box_margin_reaches_an_airport_neither_distance_pass_could_have_chosen()
    {
        // Aircraft 200 m north of A's box top — outside the box, inside BoxMarginMetres (300 m).
        // B's reference point is NEARER (346 m vs 656 m) and B has taxi paths, so both distance
        // passes would answer "KBBB": only the margin on a box pass can produce "KAAA". B's own box
        // is ~30 m short of reaching the aircraft even with its margin, so it loses both box passes.
        var a = new AirportCandidate("KAAA", 44.99410, -100.00000, -100.01000, -99.99000, 44.99820, 44.99000, 500);
        var b = new AirportCandidate("KBBB", 45.00000, -100.00440, -100.00460, -100.00420, 45.00050, 44.99950, 300);
        Assert.Equal("KAAA", CurrentAirportResolver.Pick(new[] { a, b }, 45.0000, -100.0000));
    }

    [Fact]
    public void A_candidate_with_a_blank_ident_is_never_the_answer()
    {
        // A navdata row whose ident is whitespace: nearest, with taxi paths, and its box contains
        // the aircraft — it would win every pass if it were not dropped up front.
        var blank = new AirportCandidate("   ", 33.6801, -117.8641, -117.8643, -117.8639, 33.6803, 33.6799, 700);
        Assert.Equal("KSNA", CurrentAirportResolver.Pick(new[] { blank, Ksna }, 33.6795, -117.8645));
        Assert.Null(CurrentAirportResolver.Pick(new[] { blank }, 33.6801, -117.8641));
    }
}

public class CurrentAirportTests
{
    /// <summary>Counts the legacy call so a test can assert it never happened, and lets each test
    /// choose what each of the two queries answers.</summary>
    private sealed class FakeAirportDataProvider : IAirportDataProvider, IAirportFacilitiesProvider
    {
        private readonly IReadOnlyList<AirportCandidate> _candidates;
        private readonly List<string> _legacy;
        public int NearbyIcaoCalls { get; private set; }

        public FakeAirportDataProvider(IReadOnlyList<AirportCandidate> candidates, List<string> legacy)
        { _candidates = candidates; _legacy = legacy; }

        public IReadOnlyList<AirportCandidate> GetNearbyAirportCandidates(double lat, double lon, double nm) => _candidates;
        public List<string> GetNearbyAirportICAOs(double lat, double lon, double nm) { NearbyIcaoCalls++; return _legacy; }

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
    public void A_candidate_the_resolver_refused_is_not_handed_back_by_the_legacy_rule()
    {
        // Both queries scan the same ±5 NM BOX, so an airport 5 NM north AND 5 NM east is in the
        // list at 7.07 NM true — beyond every pass. The legacy query has no distance filter at
        // all, so falling through to it would hand back exactly the airport Pick just refused,
        // picked by the summed-raw-degrees order this whole change exists to retire.
        var corner = new AirportCandidate("KFAR", 0.0833333, 0.0833333, 0.0832, 0.0834, 0.0834, 0.0832, 900);
        var provider = new FakeAirportDataProvider(new[] { corner }, new List<string> { "KZZZ" });

        Assert.Null(CurrentAirport.Resolve(provider, 0.0, 0.0));
        Assert.Equal(0, provider.NearbyIcaoCalls);
    }

    [Fact]
    public void With_no_candidates_at_all_the_legacy_rule_still_answers()
    {
        // A provider that cannot supply candidates (the interface's default implementation, a test
        // double, a future provider). The 4-character filter is the legacy rule's own.
        var provider = new FakeAirportDataProvider(Array.Empty<AirportCandidate>(), new List<string> { "S50", "KZZZ" });

        Assert.Equal("KZZZ", CurrentAirport.Resolve(provider, 0.0, 0.0));
        Assert.Equal(1, provider.NearbyIcaoCalls);
    }
}
