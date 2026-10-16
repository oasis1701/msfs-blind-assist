using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for Services/Volanta/VolantaTraffic.cs: placing flights around the pilot, moving old
// positions on to now, leaving the pilot's own flight out, and the Traffic tab's filters.
public class VolantaTrafficTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 21, 15, 0, TimeSpan.Zero);
    private static readonly VolantaOwnPosition Me = new(0, 0, 30000, 0, 450, false, Now);

    private static VolantaFlight Flight(string id, double lat, double lon, double alt = 30000, double gs = 0,
        double heading = 0, bool onGround = false, VolantaSource source = VolantaSource.Volanta,
        DateTimeOffset? time = null, string callsign = "", string user = "", string type = "")
        => new()
        {
            Id = id, Latitude = lat, Longitude = lon, AltitudeFt = alt, GroundSpeedKt = gs, HeadingTrue = heading,
            OnGround = onGround, Source = source, PositionTime = time ?? Now, Callsign = callsign, UserName = user,
            AircraftIcao = type,
        };

    [Theory]
    [InlineData(0, 12)]
    [InlineData(20, 1)]
    [InlineData(90, 3)]
    [InlineData(180, 6)]
    [InlineData(270, 9)]
    [InlineData(340, 11)]
    [InlineData(359, 12)]
    [InlineData(-90, 9)]
    public void Clock_positions_match_the_TCAS_window(double relativeBearing, int clock)
        => Assert.Equal(clock, VolantaTrafficCalculator.Clock(relativeBearing));

    [Fact]
    public void Projecting_sixty_nm_north_or_east_from_the_equator_moves_about_one_degree()
    {
        var (lat, lon) = VolantaTrafficCalculator.Project(0, 0, 0, 60);
        Assert.Equal(1.0, lat, 2);
        Assert.Equal(0.0, lon, 6);
        (lat, lon) = VolantaTrafficCalculator.Project(0, 0, 90, 60);
        Assert.Equal(0.0, lat, 6);
        Assert.Equal(1.0, lon, 2);
        Assert.Equal((10.0, 20.0), VolantaTrafficCalculator.Project(10, 20, 45, 0));
    }

    [Fact]
    public void An_airborne_flight_is_moved_on_along_its_heading_by_its_age()
    {
        // 360 kt for 60 s = 6 nm east.
        var f = Flight("a", 0, 0, gs: 360, heading: 90, time: Now.AddSeconds(-60));
        var (lat, lon) = VolantaTrafficCalculator.Extrapolate(f, Now);
        Assert.Equal(0.0, lat, 6);
        Assert.Equal(6.0 / 60.0, lon, 3);
    }

    [Fact]
    public void A_very_old_position_is_moved_at_most_three_minutes()
    {
        var f = Flight("a", 0, 0, gs: 360, heading: 0, time: Now.AddMinutes(-20));
        var (lat, _) = VolantaTrafficCalculator.Extrapolate(f, Now);
        Assert.Equal(18.0 / 60.0, lat, 3);   // 180 s at 360 kt = 18 nm
    }

    [Theory]
    [InlineData(true, 200)]   // on the ground
    [InlineData(false, 30)]   // taxiing speed
    public void Ground_and_slow_traffic_stays_where_it_reported(bool onGround, double gs)
    {
        var f = Flight("a", 1, 2, gs: gs, heading: 90, onGround: onGround, time: Now.AddSeconds(-60));
        Assert.Equal((1.0, 2.0), VolantaTrafficCalculator.Extrapolate(f, Now));
    }

    [Fact]
    public void The_pilot_s_own_Volanta_report_is_moved_on_too()
    {
        var report = new VolantaOwnPosition(0, 0, 36000, 0, 600, false, Now.AddSeconds(-60));
        var moved = VolantaTrafficCalculator.Extrapolate(report, Now);
        Assert.Equal(10.0 / 60.0, moved.Latitude, 3);
        Assert.Equal(Now, moved.Time);
    }

    [Fact]
    public void The_own_flight_is_left_out_by_its_flight_id()
    {
        var flights = new[] { Flight("mine", 0, -0.25), Flight("other", 0, 0.5) };
        var entries = VolantaTrafficCalculator.Compute(flights, Me, Now, ownFlightId: "MINE");
        Assert.Equal(new[] { "other" }, entries.Select(e => e.Flight.Id));
    }

    [Fact]
    public void Before_its_id_is_known_the_own_flight_is_the_entry_matching_Volanta_s_own_report()
    {
        var reported = Now.AddSeconds(-113);
        var report = new VolantaOwnPosition(0, -0.25, 30000, 0, 450, false, reported);
        var flights = new[]
        {
            Flight("mine", 0, -0.25, time: reported.AddMilliseconds(400)),
            Flight("same place, other time", 0, -0.25, time: reported.AddSeconds(30)),
            Flight("same time, elsewhere", 1, 1, time: reported),
        };
        var ids = VolantaTrafficCalculator.Compute(flights, Me, Now, ownVolantaReport: report).Select(e => e.Flight.Id);
        Assert.DoesNotContain("mine", ids);
        Assert.Contains("same place, other time", ids);
        Assert.Contains("same time, elsewhere", ids);
    }

    [Fact]
    public void The_own_flight_is_left_out_by_the_logged_in_user_s_id_even_with_no_report_yet()
    {
        // The sim paused: Volanta sends no position report or live status, but the user id is known.
        var flights = new[] { Flight("mine", 0, 0.01) with { UserId = "73E6C47E-9B8D-416B-A257-B8E9DD32B6F9" }, Flight("other", 0, 0.5) };
        var entries = VolantaTrafficCalculator.Compute(flights, Me, Now, ownUserId: "73e6c47e-9b8d-416b-a257-b8e9dd32b6f9");
        Assert.Equal(new[] { "other" }, entries.Select(e => e.Flight.Id));
    }

    [Fact]
    public void A_known_user_id_that_matches_nothing_does_not_fall_back_to_position_matching()
    {
        var report = new VolantaOwnPosition(0, 0, 30000, 0, 450, false, Now);
        var f = Flight("someone", 0, 0, time: Now) with { UserId = "11111111-1111-1111-1111-111111111111" };
        Assert.False(VolantaTrafficCalculator.IsOwnFlight(f, null, report, "73e6c47e-9b8d-416b-a257-b8e9dd32b6f9"));
        Assert.True(VolantaTrafficCalculator.IsOwnFlight(f, null, report));
    }

    [Fact]
    public void A_network_pilot_is_never_taken_for_the_own_flight()
        => Assert.False(VolantaTrafficCalculator.IsOwnFlight(Flight("x", 0, 0, source: VolantaSource.Vatsim), "x", null));

    [Fact]
    public void Distance_bearing_clock_and_altitude_are_from_the_pilot()
    {
        var me = Me with { HeadingTrue = 90 };
        var e = VolantaTrafficCalculator.Compute(new[] { Flight("north", 0.5, 0, alt: 23100) }, me, Now).Single();
        Assert.Equal(30.0, e.DistanceNm, 1);
        Assert.Equal(0.0, e.BearingTrue, 3);
        Assert.Equal(270.0, e.RelativeBearing, 3);
        Assert.Equal(9, e.ClockPosition);      // north is on the left when heading east
        Assert.Equal(-6900.0, e.AltitudeDiffFt);
        Assert.Equal(0.0, e.PositionAgeSeconds);
    }

    private static IReadOnlyList<VolantaTrafficEntry> Sample() => VolantaTrafficCalculator.Compute(new[]
    {
        Flight("near-high", 0, 0.1, alt: 31000, callsign: "DAL1022", user: "pilot_one", type: "B738"),
        Flight("far", 0, 3.0, alt: 30000, type: "A320"),
        Flight("ground", 0, 0.5, alt: 800, onGround: true, source: VolantaSource.Vatsim, type: "A339"),
        Flight("ivao", 0, 0.3, alt: 37000, source: VolantaSource.Ivao, callsign: "UAL16"),
    }, Me, Now);

    [Fact]
    public void The_default_filter_shows_everything_within_100_nm_nearest_first()
        => Assert.Equal(new[] { "near-high", "ivao", "ground" },
            new VolantaTrafficFilter().Apply(Sample()).Select(e => e.Flight.Id));

    [Fact]
    public void Any_distance_shows_the_far_one_too()
        => Assert.Equal(4, new VolantaTrafficFilter { RadiusNm = null }.Apply(Sample()).Count);

    [Fact]
    public void Sources_phase_and_search_each_narrow_the_list()
    {
        var all = Sample();
        Assert.Equal(new[] { "near-high" },
            new VolantaTrafficFilter { Sources = new HashSet<VolantaSource> { VolantaSource.Volanta } }.Apply(all).Select(e => e.Flight.Id));
        Assert.Equal(new[] { "ground" },
            new VolantaTrafficFilter { Phase = VolantaPhaseFilter.OnGround }.Apply(all).Select(e => e.Flight.Id));
        Assert.Equal(new[] { "near-high", "ivao" },
            new VolantaTrafficFilter { Phase = VolantaPhaseFilter.Airborne }.Apply(all).Select(e => e.Flight.Id));
        Assert.Equal(new[] { "ivao" },
            new VolantaTrafficFilter { Search = " ual " }.Apply(all).Select(e => e.Flight.Id));
        Assert.Equal(new[] { "near-high" },
            new VolantaTrafficFilter { Search = "PILOT_one" }.Apply(all).Select(e => e.Flight.Id));
        Assert.Equal(new[] { "ground" },
            new VolantaTrafficFilter { Search = "a339" }.Apply(all).Select(e => e.Flight.Id));
    }
}
