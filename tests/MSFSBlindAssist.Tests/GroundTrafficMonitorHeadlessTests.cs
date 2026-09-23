// GroundTrafficMonitor driven headlessly (IGroundTrafficSimSource + TickForHarness) — the four
// callout fixes found by simulating traffic around real taxi routes (2026-09-23):
//   * the departure-queue label compared distances measured from two different origins;
//   * a parked aircraft off a route that bends away earned "Slow down"/"Stop" from a
//     straight-line closest-approach test that assumes the pilot keeps going straight;
//   * every aircraft of a queue was called separately, each call interrupting the last;
//   * traffic pulling away ahead earned "Stop, … very close" (the distance-growth test was
//     sized for a 3 s poll and never fires at 1 s).
//
// Geometry: the equator, heading east, so 1 m east = 1 / 111320 degrees of longitude.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficMonitorHeadlessTests
{
    private const double M = 1.0 / 111320.0;

    private sealed class Fake : IGroundTrafficSimSource
    {
        public readonly List<AiTrafficDataEventArgs> Traffic = new();
        public SimConnectManager.AircraftPosition? Position;
        public bool IsConnected => true;
        public bool? LastKnownOnGround { get; set; } = true;
        public SimConnectManager.AircraftPosition? LastKnownPosition => Position;
        public void RequestAircraftPosition() { }
        public void RequestAircraftPositionAsync(Action<SimConnectManager.AircraftPosition> cb) { if (Position is { } p) cb(p); }
        public void RequestAiTrafficData()
        {
            foreach (var t in Traffic) AiTrafficReceived?.Invoke(this, t);
            AiTrafficSweepCompleted?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived;
        public event EventHandler? AiTrafficSweepCompleted;
    }

    private sealed class Said : ScreenReaderAnnouncer
    {
        public readonly List<string> All = new();
        public Said() : base(IntPtr.Zero) { }
        public override void Announce(string m) => All.Add(m);
        public override void AnnounceImmediate(string m) => All.Add(m);
        public override void AnnounceQueued(string m) => All.Add(m);
        public override void AnnounceWithQueue(string m) => All.Add(m);
    }

    private static AiTrafficDataEventArgs Ac(uint id, double eastM, double northM, double gs, string airline, string cs) => new()
    {
        ObjectId = id, Callsign = cs, Airline = airline, AircraftType = "A320",
        Latitude = northM * M, Longitude = eastM * M, HeadingMagnetic = 90, GroundSpeedKnots = gs, OnGround = true,
    };

    private static (GroundTrafficMonitor mon, Fake src, Said said) Monitor(
        IReadOnlyList<GroundTrafficRoutePoint> route, double? routeEndM, double ownEastM, double ownGs, bool departure = true)
    {
        var src = new Fake
        {
            Position = new SimConnectManager.AircraftPosition
                { Latitude = 0, Longitude = ownEastM * M, HeadingMagnetic = 90, GroundSpeedKnots = ownGs, SimOnGround = 1 },
        };
        var said = new Said();
        var mon = new GroundTrafficMonitor(said, src, startTimers: false)
        {
            RouteContextProvider = () => new GroundTrafficRouteContext
            {
                Runways = Array.Empty<MSFSBlindAssist.Navigation.TaxiGraph.RunwayCenterline>(),
                WatchedRunways = Array.Empty<string>(),
                IsDepartureRoute = departure,
                RouteAhead = route,
                RouteEndMetres = routeEndM,
            },
        };
        return (mon, src, said);
    }

    private static List<GroundTrafficRoutePoint> East(double lengthM) => new()
    {
        new GroundTrafficRoutePoint(0, 0, "A", 0), new GroundTrafficRoutePoint(0, lengthM * M, "A", lengthM),
    };

    [Fact]
    public void Queue_at_the_hold_is_the_departure_queue_with_the_pilot_partway_down_a_segment()
    {
        // RouteAhead (and RouteEndMetres) start at the START of the pilot's segment; the pilot is
        // 530 m into it. Queue head 800 m, hold 900 m: 100 m from the hold. Before the fix the
        // head read 630 m from the hold (the 530 m counted twice) and it was "the queue".
        var (mon, src, said) = Monitor(East(900), 900, ownEastM: 530, ownGs: 0);
        src.Traffic.Add(Ac(1, 600, 0, 0, "British Airways", "BAW1"));
        src.Traffic.Add(Ac(2, 700, 0, 0, "Lufthansa", "DLH2"));
        src.Traffic.Add(Ac(3, 800, 0, 0, "easyJet", "EZY3"));

        for (int i = 0; i < 6; i++) mon.TickForHarness();

        Assert.Contains(said.All, m => m.StartsWith("Number 4 in the departure queue"));
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
        var (mon, src, said) = Monitor(route, null, ownEastM: 0, ownGs: 15, departure: false);
        src.Traffic.Add(Ac(1, 170, 0, 0, "British Airways", "BAW1"));

        for (int i = 0; i < 6; i++) mon.TickForHarness();

        Assert.DoesNotContain(said.All, m => m.StartsWith("Slow down") || m.StartsWith("Stop,"));
    }

    [Fact]
    public void Only_the_first_aircraft_on_the_route_is_called_not_the_queue_behind_it()
    {
        var (mon, src, said) = Monitor(East(1000), 1000, ownEastM: 0, ownGs: 15, departure: false);
        src.Traffic.Add(Ac(1, 150, 0, 0, "British Airways", "BAW1"));
        src.Traffic.Add(Ac(2, 220, 0, 0, "Lufthansa", "DLH2"));
        src.Traffic.Add(Ac(3, 290, 0, 0, "easyJet", "EZY3"));

        for (int i = 0; i < 6; i++) mon.TickForHarness();

        Assert.Contains(said.All, m => m.Contains("British Airways"));
        Assert.DoesNotContain(said.All, m => m.Contains("Lufthansa") || m.Contains("easyJet"));
    }

    [Fact]
    public void Traffic_pulling_away_ahead_is_not_a_stop()
    {
        // 95 m ahead on the route, 20 kt against the pilot's 6 kt — the gap opens at ~7 m/s.
        var (mon, src, said) = Monitor(East(1000), null, ownEastM: 0, ownGs: 6, departure: false);
        src.Traffic.Add(Ac(1, 95, 0, 20, "British Airways", "BAW1"));

        for (int i = 0; i < 3; i++) mon.TickForHarness();   // the first sweep goes out on the third tick

        Assert.DoesNotContain(said.All, m => m.StartsWith("Stop,"));
    }

    [Fact]
    public void Stopped_traffic_on_the_route_ahead_is_still_a_stop_when_very_close()
    {
        // The guard for the three fixes above: a parked aircraft ON the route, very close, still stops you.
        var (mon, src, said) = Monitor(East(1000), null, ownEastM: 0, ownGs: 6, departure: false);
        src.Traffic.Add(Ac(1, 60, 0, 0, "British Airways", "BAW1"));

        for (int i = 0; i < 3; i++) mon.TickForHarness();   // the first sweep goes out on the third tick

        Assert.Contains(said.All, m => m.StartsWith("Stop,"));
    }
}
