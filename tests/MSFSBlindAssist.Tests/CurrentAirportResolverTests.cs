using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class CurrentAirportResolverTests
{
    // Real fs2024 rows around John Wayne (KSNA): the heliport's reference point is nearer to most
    // KSNA stands than KSNA's own, which is how Alt+L came to say "No taxi data available for 10CL".
    private static readonly AirportCandidate Ksna = new("KSNA", 33.675667, -117.868222, -117.8790, -117.8570, 33.6890, 33.6630, 1598);
    private static readonly AirportCandidate Heliport = new("10CL", 33.6800, -117.8640, -117.8642, -117.8638, 33.6802, 33.6798, 0);

    [Fact]
    public void The_airport_whose_box_contains_the_aircraft_beats_a_nearer_heliport()
        => Assert.Equal("KSNA", CurrentAirportResolver.Pick(new[] { Heliport, Ksna }, 33.6795, -117.8645));

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
}
