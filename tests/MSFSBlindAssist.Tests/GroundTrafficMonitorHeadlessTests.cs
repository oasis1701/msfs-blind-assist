// GroundTrafficMonitor driven headlessly (IGroundTrafficSimSource + TickForHarness, on a simulated clock —
// see GroundTrafficHarness) — the four callout fixes found by simulating traffic around real taxi routes
// (PR #247 author, 2026-09-23):
//   * the departure-queue label compared distances measured from two different origins;
//   * a parked aircraft off a route that bends away earned "Slow down"/"Stop" from a
//     straight-line closest-approach test that assumes the pilot keeps going straight;
//   * every aircraft of a queue was called separately, each call interrupting the last;
//   * traffic pulling away ahead earned "Stop, … very close" (the distance-growth test was
//     sized for a 3 s poll and never fires at 1 s).
//
// Geometry: the equator, heading east, so 1 m east = 1 / 111320 degrees of longitude.

using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.GroundTrafficHarness;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficMonitorHeadlessTests
{
    private static GroundTrafficHarness Monitor(
        IReadOnlyList<GroundTrafficRoutePoint> route, double? routeEndM, double ownEastM, double ownGs, bool departure = true)
    {
        var h = new GroundTrafficHarness { Context = RouteContext(route, routeEndM, departure) };
        h.Sim.Position = Own(ownEastM, ownGs);
        return h;
    }

    [Fact]
    public void Queue_at_the_hold_is_the_departure_queue_with_the_pilot_partway_down_a_segment()
    {
        // RouteAhead (and RouteEndMetres) start at the START of the pilot's segment; the pilot is
        // 530 m into it. Queue head 800 m, hold 900 m: 100 m from the hold. Before the fix the
        // head read 630 m from the hold (the 530 m counted twice) and it was "the queue".
        var h = Monitor(East(900), 900, ownEastM: 530, ownGs: 0);
        h.Sim.Traffic.Add(Ac(1, 600, 0, 0, "British Airways", "BAW1"));
        h.Sim.Traffic.Add(Ac(2, 700, 0, 0, "Lufthansa", "DLH2"));
        h.Sim.Traffic.Add(Ac(3, 800, 0, 0, "easyJet", "EZY3"));

        h.Tick(6);

        Assert.Contains(h.Said.All, m => m.StartsWith("Number 4 in the departure queue"));
    }

    [Fact]
    public void Parked_aircraft_off_a_route_that_bends_away_is_not_a_slow_down()
    {
        // Route east for 80 m, then north. A parked aircraft 170 m straight ahead is 90 m from the
        // route: straight-line closest approach says "you will hit it", the route never goes there.
        var route = new List<GroundTrafficRoutePoint>
        {
            new(0, 0, "A", 0), new(0, 80 * M, "A", 80), new(300 * M, 80 * M, "B", 380),
        };
        var h = Monitor(route, null, ownEastM: 0, ownGs: 15, departure: false);
        h.Sim.Traffic.Add(Ac(1, 170, 0, 0, "British Airways", "BAW1"));

        h.Tick(6);

        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Slow down") || m.StartsWith("Stop,"));
    }

    [Fact]
    public void Only_the_first_aircraft_on_the_route_is_called_not_the_queue_behind_it()
    {
        var h = Monitor(East(1000), 1000, ownEastM: 0, ownGs: 15, departure: false);
        h.Sim.Traffic.Add(Ac(1, 150, 0, 0, "British Airways", "BAW1"));
        h.Sim.Traffic.Add(Ac(2, 220, 0, 0, "Lufthansa", "DLH2"));
        h.Sim.Traffic.Add(Ac(3, 290, 0, 0, "easyJet", "EZY3"));

        h.Tick(6);

        Assert.Contains(h.Said.All, m => m.Contains("British Airways"));
        Assert.DoesNotContain(h.Said.All, m => m.Contains("Lufthansa") || m.Contains("easyJet"));
    }

    [Fact]
    public void Traffic_pulling_away_ahead_is_not_a_stop()
    {
        // 95 m ahead on the route, 20 kt against the pilot's 6 kt — the gap opens at ~7 m/s.
        var h = Monitor(East(1000), null, ownEastM: 0, ownGs: 6, departure: false);
        h.Sim.Traffic.Add(Ac(1, 95, 0, 20, "British Airways", "BAW1"));

        h.Tick(3);   // the first sweep goes out on the third tick

        Assert.DoesNotContain(h.Said.All, m => m.StartsWith("Stop,"));
    }

    [Fact]
    public void Stopped_traffic_on_the_route_ahead_is_still_a_stop_when_very_close()
    {
        // The guard for the three fixes above: a parked aircraft ON the route, very close, still stops you.
        var h = Monitor(East(1000), null, ownEastM: 0, ownGs: 6, departure: false);
        h.Sim.Traffic.Add(Ac(1, 60, 0, 0, "British Airways", "BAW1"));

        h.Tick(3);   // the first sweep goes out on the third tick

        Assert.Contains(h.Said.All, m => m.StartsWith("Stop,"));
    }

    [Fact]
    public void On_the_fast_landing_exit_a_caution_waits_but_a_stop_does_not()
    {
        // 40 kt: the Caution band ends 400 + 473 = 873 ft out, the Warning band 250 + 473 = 723 ft.
        var caution = Monitor(East(1000), null, ownEastM: 0, ownGs: 40, departure: false);
        caution.Monitor.LandingExitWarningsOnlyCheck = () => true;
        caution.Sim.Traffic.Add(Ac(1, 240, 0, 0, "British Airways", "BAW1"));   // 787 ft: Caution band
        caution.Tick(3);
        Assert.Empty(caution.Said.All);

        var stop = Monitor(East(1000), null, ownEastM: 0, ownGs: 40, departure: false);
        stop.Monitor.LandingExitWarningsOnlyCheck = () => true;
        stop.Sim.Traffic.Add(Ac(1, 100, 0, 0, "British Airways", "BAW1"));      // 328 ft: Warning band
        stop.Tick(3);
        Assert.Contains(stop.Said.All, m => m.StartsWith("Stop,"));
    }

    [Fact]
    public void The_caution_held_back_on_the_fast_exit_is_said_once_the_aircraft_is_at_taxi_speed()
    {
        // Not spoken means not latched: still true at taxi speed, it is said then.
        bool fast = true;
        var h = Monitor(East(1000), null, ownEastM: 0, ownGs: 40, departure: false);
        h.Monitor.LandingExitWarningsOnlyCheck = () => fast;
        h.Sim.Traffic.Add(Ac(1, 240, 0, 0, "British Airways", "BAW1"));
        h.Tick(3);
        Assert.Empty(h.Said.All);

        fast = false;
        h.Tick(3);
        Assert.Contains(h.Said.All, m => m.StartsWith("Slow down"));
    }
}
