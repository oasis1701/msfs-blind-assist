// The ground-traffic monitor's sweep request ids. PR #247 B1 review / implementer concern 5: a sweep
// older than 3 s is treated as lost and re-issued — under the SAME request id, so when the old one then
// completed it was credited to the NEW request, including the watch-readiness gate, although its
// radius and intake were the old ones. The camera read paid for the same pattern once (one shared
// request id let an abandoned read's late reply complete the NEXT read) and rotates over a range of
// ids; the ground sweep now does too.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficRequestIdTests
{
    private static uint First => (uint)SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
    private static uint Last => First + SimConnectManager.GroundTrafficRequestIdCount - 1;

    [Fact]
    public void Sweeps_rotate_over_eight_ids()
        => Assert.Equal(8u, SimConnectManager.GroundTrafficRequestIdCount);

    [Fact]
    public void Every_id_in_the_range_is_a_ground_traffic_sweep()
    {
        for (uint id = First; id <= Last; id++)
            Assert.True(SimConnectManager.IsGroundTrafficRequestId(id), $"request id {id}");
    }

    [Fact]
    public void The_ids_either_side_of_the_range_are_not()
    {
        Assert.False(SimConnectManager.IsGroundTrafficRequestId(First - 1));
        Assert.False(SimConnectManager.IsGroundTrafficRequestId(Last + 1));
        // TCAS's own sweep is never taken for the monitor's (PR #247 review L5).
        Assert.False(SimConnectManager.IsGroundTrafficRequestId(
            (uint)SimConnectManager.DATA_REQUESTS.REQUEST_AI_TRAFFIC));
    }

    /// <summary>
    /// A request issued under an id already in use REPLACES that request (the MD-11 MCDU manager's
    /// note), so the range must hold no other request id: not a named one, not a definition id (a
    /// request-id namespace too — RequestSingleValue issues a DEF_* as its request id), not a
    /// per-variable id (INDIVIDUAL_VARIABLE_BASE upward), and not one of the hand-numbered ids the
    /// dispatcher matches by cast (324-328 takeoff assist / hand fly, 330-337 V-speeds, 370-372
    /// waypoint / hand fly, 505-508 guidance frames). The last is why the range is not 501-508: a
    /// sweep under 507 would cancel taxi guidance's own position stream.
    /// </summary>
    [Fact]
    public void The_range_is_clear_of_every_other_request_id()
    {
        Assert.True(Last < (uint)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);

        var named = Enum.GetValues<SimConnectManager.DATA_REQUESTS>().Select(v => (uint)(int)v).Where(id => id != First);
        Assert.DoesNotContain(named, id => id >= First && id <= Last);

        var definitions = Enum.GetValues<SimConnectManager.DATA_DEFINITIONS>().Select(v => (uint)(int)v);
        Assert.DoesNotContain(definitions, id => id >= First && id <= Last);

        uint[] handNumbered = { 324, 325, 326, 327, 328, 330, 331, 332, 333, 334, 335, 336, 337, 370, 371, 372, 505, 506, 507, 508 };
        Assert.DoesNotContain(handNumbered, id => id >= First && id <= Last);
    }
}
