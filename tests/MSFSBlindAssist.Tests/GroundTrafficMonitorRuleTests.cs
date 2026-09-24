// The reviewed monitor's own highest-risk rules, driven through the headless harness
// (GroundTrafficHarness: the real tick, intake, sweep bookkeeping, evaluation and speech policy, one
// simulated second at a time). The pure units behind each rule have their own characterization tests;
// these pin that the monitor actually wires them together as documented.
//
// Geometry: the equator, heading east, so 1 m east = 1 / 111320 degrees of longitude.

using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficMonitorRuleTests
{
    private static GroundTrafficHarness OnRoute(double ownGs)
    {
        var h = new GroundTrafficHarness { Context = RouteContext(East(1000), null, departure: false) };
        h.Sim.Position = Own(0, ownGs);
        return h;
    }

    // ── Only the first aircraft on the route is called — but "Stop" is never withheld ─────────────────

    [Fact]
    public void An_aircraft_queued_behind_the_first_still_earns_Stop_when_very_close()
    {
        // Both inside the Warning distance at 6 kt (250 ft + the speed lead, about 98 m): the first gets its
        // "Stop", and the one queued 30 m behind it still gets its own — the author's rule withholds only
        // Awareness and Caution for traffic behind the first.
        var h = OnRoute(ownGs: 6);
        h.Sim.Traffic.Add(Ac(1, 30, 0, 0, "British Airways", "BAW1"));
        h.Sim.Traffic.Add(Ac(2, 60, 0, 0, "Lufthansa", "DLH2"));

        h.Tick(6);

        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, British Airways"));
        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, Lufthansa"));
    }

    // ── A "Stop" withheld while traffic moves away is not swallowed ─────────────────────────────────

    [Fact]
    public void Traffic_that_pulls_away_then_stops_inside_the_Warning_distance_still_earns_Stop()
    {
        // 60 m ahead, inside the Warning distance (about 98 m at 6 kt), pulling away at 20 kt: moving
        // away, so no "Stop". Then it stops where it is. A withheld Warning is never recorded as if it had
        // been spoken, so the next evaluation says "Stop" — recording it silently while the aircraft
        // pulled away swallowed that "Stop" for good.
        var h = OnRoute(ownGs: 6);
        var leader = Ac(1, 60, 0, 20, "British Airways", "BAW1");
        h.Sim.Traffic.Add(leader);

        h.Tick(4);
        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop,"));

        leader.GroundSpeedKnots = 0;
        h.Tick(2);
        Assert.Contains(h.Said.Interrupts, m => m.StartsWith("Stop, British Airways"));
    }
}
