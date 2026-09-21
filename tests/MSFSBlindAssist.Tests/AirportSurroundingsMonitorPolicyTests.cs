using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The monitor's two pure rules: whether a BACKGROUND JOB may start on this tick (one policy, both
/// jobs — the first-time catalog build and the runway probe's graph warm-up), and what counts as a
/// teleport rather than a taxi. Everything else in the monitor is the WinForms timer, the announcer
/// and the cache, none of which belongs in a unit test.
/// </summary>
public class AirportSurroundingsMonitorPolicyTests
{
    [Theory]
    [InlineData(false, 12.0, true)]      // taxiing: go ahead
    [InlineData(false, 0.0, true)]       // parked: go ahead
    [InlineData(false, 120.0, false)]    // two seconds after touchdown: not now
    [InlineData(true, 12.0, false)]      // takeoff assist / rollout / docking / lineup: not now
    [InlineData(true, 0.0, false)]       // suppressed outranks a standstill
    public void A_background_job_waits_for_a_quiet_moment(bool suppressed, double groundSpeedKts, bool expected)
        => Assert.Equal(expected, AirportSurroundingsMonitor.MayStartBuild(suppressed, groundSpeedKts));

    /// <summary>The speed ceiling is the gate's own taxi-band ceiling, not a second number: above
    /// it nothing can be announced anyway, so there is nothing to prepare for.</summary>
    [Fact]
    public void The_quiet_moment_ends_at_the_gate_s_own_taxi_band_ceiling()
    {
        Assert.True(AirportSurroundingsMonitor.MayStartBuild(false, PassingCalloutGate.MaxSpeedKts));
        Assert.False(AirportSurroundingsMonitor.MayStartBuild(false, PassingCalloutGate.MaxSpeedKts + 0.1));
    }

    // KATL for the north-south cases; ENAT (69.98 N) for the east-west ones, where one degree of
    // longitude is barely a third of one of latitude — an implementation that dropped the
    // cos(latitude) term would read a 125 m shuffle there as a 365 m teleport.
    private const double Lat = 33.6407, Lon = -84.4277;
    private const double EnatLat = 69.9787, EnatLon = 23.3717;
    // TaxiGeo's own sphere (R = 6,371,000 m): a pure-meridian arc is exactly R x delta-latitude.
    private const double MetresPerDegreeLat = 6_371_000.0 * Math.PI / 180.0;

    private static bool MovedNorth(double metres)
        => AirportSurroundingsMonitor.IsPositionJump(Lat, Lon, Lat + metres / MetresPerDegreeLat, Lon);

    private static bool MovedEastAtEnat(double metres)
        => AirportSurroundingsMonitor.IsPositionJump(
            EnatLat, EnatLon,
            EnatLat, EnatLon + metres / (MetresPerDegreeLat * Math.Cos(EnatLat * Math.PI / 180.0)));

    [Theory]
    [InlineData(0.0, false)]        // parked
    [InlineData(41.0, false)]       // one poll at the gate's own 40 kt ceiling
    [InlineData(145.0, false)]      // one poll of a 140 kt landing rollout — fast, but not a jump
    [InlineData(3000.0, true)]      // the gate-teleport dialog, same airport
    [InlineData(400_000.0, true)]   // a flight reload at another airport
    public void A_position_jump_is_further_than_a_taxiing_aircraft_can_travel_in_one_poll(double metres, bool expected)
        => Assert.Equal(expected, MovedNorth(metres));

    // The threshold is pinned from both sides a millimetre out, and deliberately not AT it:
    // converting metres to degrees and back through the haversine's own Asin(Sin(...)) round trip
    // lands an "exactly JumpMetres" offset a fraction of a nanometre either side of the constant,
    // and which side is a property of the platform's math library. A test that can flake on a
    // library's last bit is worse than one that pins the rule to within two millimetres.
    [Fact]
    public void A_move_a_millimetre_short_of_the_jump_threshold_is_not_a_jump()
        => Assert.False(MovedNorth(AirportSurroundingsMonitor.JumpMetres - 0.001));

    [Fact]
    public void A_move_a_millimetre_past_the_jump_threshold_is_a_jump()
        => Assert.True(MovedNorth(AirportSurroundingsMonitor.JumpMetres + 0.001));

    [Fact]
    public void A_taxi_east_at_seventy_degrees_north_is_not_a_jump()
        => Assert.False(MovedEastAtEnat(AirportSurroundingsMonitor.JumpMetres * 0.5));

    [Fact]
    public void A_teleport_east_at_seventy_degrees_north_is_a_jump()
        => Assert.True(MovedEastAtEnat(AirportSurroundingsMonitor.JumpMetres * 2.0));

    /// <summary>An unreadable sample is not evidence that the aircraft moved, so it never drops the
    /// tracks — the safe direction, and the same NaN reaches the gate as a NaN range, where no pass
    /// can arm either.</summary>
    [Theory]
    [InlineData(double.NaN, double.NaN, Lat, Lon)]
    [InlineData(Lat, Lon, double.NaN, double.NaN)]
    public void An_unreadable_position_is_never_a_jump(double fromLat, double fromLon, double toLat, double toLon)
        => Assert.False(AirportSurroundingsMonitor.IsPositionJump(fromLat, fromLon, toLat, toLon));

    /// <summary>Null island is a real coordinate to a distance test, so a step to or from it reads
    /// as the teleport it is. Also the safe direction: a reset can only lose a callout.</summary>
    [Fact]
    public void A_step_to_null_island_is_a_jump()
        => Assert.True(AirportSurroundingsMonitor.IsPositionJump(Lat, Lon, 0.0, 0.0));
}
