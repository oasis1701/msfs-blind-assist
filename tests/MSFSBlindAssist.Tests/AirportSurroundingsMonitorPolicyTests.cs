using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The monitor's two pure rules: when a first-time catalog build may be started, and what counts
/// as a teleport rather than a taxi. Everything else in the monitor is the WinForms timer, the
/// announcer and the cache, none of which belongs in a unit test.
/// </summary>
public class AirportSurroundingsMonitorPolicyTests
{
    [Theory]
    [InlineData(false, 12.0, true)]      // taxiing: go ahead
    [InlineData(false, 0.0, true)]       // parked: go ahead
    [InlineData(false, 120.0, false)]    // two seconds after touchdown: not now
    [InlineData(true, 12.0, false)]      // takeoff assist / rollout / docking / lineup: not now
    public void A_first_time_catalog_build_waits_for_a_quiet_moment(bool suppressed, double groundSpeedKts, bool expected)
        => Assert.Equal(expected, AirportSurroundingsMonitor.MayStartBuild(suppressed, groundSpeedKts));

    // KATL, and a point that many metres due north of it. TaxiGeo's spherical earth puts one
    // degree of latitude at 111,195 m.
    private const double Lat = 33.6407, Lon = -84.4277, MetresPerDegreeLat = 111_194.93;

    private static bool MovedBy(double metres)
        => AirportSurroundingsMonitor.IsPositionJump(Lat, Lon, Lat + metres / MetresPerDegreeLat, Lon);

    [Theory]
    [InlineData(0.0, false)]        // parked
    [InlineData(41.0, false)]       // one poll at the gate's own 40 kt ceiling
    [InlineData(145.0, false)]      // one poll of a 140 kt landing rollout — fast, but not a jump
    [InlineData(3000.0, true)]      // the gate-teleport dialog, same airport
    [InlineData(400_000.0, true)]   // a flight reload at another airport
    public void A_position_jump_is_further_than_a_taxiing_aircraft_can_travel_in_one_poll(double metres, bool expected)
        => Assert.Equal(expected, MovedBy(metres));
}
