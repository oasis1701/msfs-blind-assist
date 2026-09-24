// The headless harness for GroundTrafficMonitor: a simulated simulator (IGroundTrafficSimSource), a
// text-capturing announcer, and a simulated clock, so a test drives the REAL monitor — its tick, its
// intake, its sweep bookkeeping, its evaluation and its speech policy — one simulated second at a time.
//
// Geometry: the equator, so 1 m east = 1 / 111,320 degrees of longitude (GroundTrafficHarness.M), and a
// magnetic heading equals the true one (the positions carry no magnetic variation).

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// One monitor on a simulated clock. <see cref="Tick"/> is one second in the sim: the clock advances,
/// the monitor ticks (<see cref="GroundTrafficMonitor.TickForHarness"/>), and the sweep that tick
/// requested — if it requested one — is answered AFTER the request returned, the way SimConnect answers
/// it: every aircraft through <c>AiTrafficReceived</c>, then the completion under the sweep's own request id.
/// </summary>
internal sealed class GroundTrafficHarness
{
    /// <summary>One metre east at the equator, in degrees of longitude.</summary>
    public const double M = 1.0 / 111320.0;

    public DateTime Now { get; private set; } = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    public FakeGroundTrafficSim Sim { get; } = new();
    public SpeechCapture Said { get; } = new();
    public GroundTrafficMonitor Monitor { get; }

    /// <summary>What the monitor's <see cref="GroundTrafficMonitor.RouteContextProvider"/> returns.</summary>
    public GroundTrafficRouteContext? Context { get; set; }

    public GroundTrafficHarness()
    {
        Monitor = new GroundTrafficMonitor(Said, Sim, startTimers: false, () => Now)
        {
            RouteContextProvider = () => Context,
        };
    }

    /// <summary><paramref name="seconds"/> simulated seconds, each a tick followed by the answer to the sweep it requested.</summary>
    public void Tick(int seconds = 1)
    {
        for (int i = 0; i < seconds; i++)
        {
            TickOnly();
            Sim.CompleteSweep();
        }
    }

    /// <summary>One simulated second in which the monitor ticks but no sweep is answered.</summary>
    public void TickOnly()
    {
        Now = Now.AddSeconds(1);
        Monitor.TickForHarness();
    }

    /// <summary>A taxi-route context with no runways, on <paramref name="route"/>.</summary>
    public static GroundTrafficRouteContext RouteContext(IReadOnlyList<GroundTrafficRoutePoint> route,
        double? routeEndM, bool departure) => new()
    {
        Runways = Array.Empty<TaxiGraph.RunwayCenterline>(),
        AirportIcao = "TEST",
        State = TaxiGuidanceState.Taxiing,
        IsRunwayDestination = departure,
        IsQueueRoute = departure,
        AllowsQueuePrompt = true,
        RouteAhead = route,
        RouteEndMetres = routeEndM,
    };

    /// <summary>A straight route due east from the origin.</summary>
    public static List<GroundTrafficRoutePoint> East(double lengthM) => new()
    {
        new GroundTrafficRoutePoint(0, 0, "A", 0), new GroundTrafficRoutePoint(0, lengthM * M, "A", lengthM),
    };

    /// <summary>The own aircraft, on the ground.</summary>
    public static SimConnectManager.AircraftPosition Own(double eastM, double gsKts, double northM = 0.0,
        double headingDeg = 90.0, double altitudeFt = 0.0) => new()
    {
        Latitude = northM * M, Longitude = eastM * M, Altitude = altitudeFt,
        HeadingMagnetic = headingDeg, GroundSpeedKnots = gsKts, SimOnGround = 1,
    };

    /// <summary>One traffic entry of a sweep.</summary>
    public static AiTrafficDataEventArgs Ac(uint id, double eastM, double northM, double gsKts, string airline,
        string callsign, double headingDeg = 90.0, bool onGround = true, double altitudeFt = 0.0) => new()
    {
        ObjectId = id, Callsign = callsign, Airline = airline, AircraftType = "A320",
        Latitude = northM * M, Longitude = eastM * M, AltitudeFt = altitudeFt,
        HeadingMagnetic = headingDeg, GroundSpeedKnots = gsKts, OnGround = onGround,
    };
}

/// <summary>
/// The simulator as the monitor sees it. A sweep is answered only by <see cref="CompleteSweep"/> — never
/// inside <see cref="RequestGroundTrafficData"/>, because the monitor learns the id it is waiting for
/// from that call's return value, exactly as with SimConnect.
/// </summary>
internal sealed class FakeGroundTrafficSim : IGroundTrafficSimSource
{
    private static uint FirstId => (uint)SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
    private uint _nextId = FirstId;

    /// <summary>Every aircraft the next sweep reports (the own aircraft is never among them, as with SimConnect).</summary>
    public List<AiTrafficDataEventArgs> Traffic { get; } = new();
    public SimConnectManager.AircraftPosition? Position { get; set; }
    public bool IsConnected { get; set; } = true;
    public bool? LastKnownOnGround { get; set; } = true;
    public SimConnectManager.AircraftPosition? LastKnownPosition => Position;

    /// <summary>The outstanding sweep's request id; 0 = none.</summary>
    public uint PendingSweepId { get; private set; }
    public int SweepsRequested { get; private set; }

    public void RequestAircraftPosition() { }

    public void RequestAircraftPositionAsync(Action<SimConnectManager.AircraftPosition> callback)
    {
        if (Position is { } p) callback(p);
    }

    public uint RequestGroundTrafficData(uint radiusMeters)
    {
        // The same rotation SimConnectManager uses: the next of its eight ids each time.
        uint id = _nextId;
        _nextId = FirstId + (_nextId - FirstId + 1) % SimConnectManager.GroundTrafficRequestIdCount;
        PendingSweepId = id;
        SweepsRequested++;
        return id;
    }

    /// <summary>Answers the outstanding sweep, if any: every aircraft, then the completion under its request id.</summary>
    public void CompleteSweep()
    {
        uint id = PendingSweepId;
        if (id == 0) return;
        PendingSweepId = 0;
        foreach (var t in Traffic.ToList()) AiTrafficReceived?.Invoke(this, t);
        GroundTrafficSweepCompleted?.Invoke(this, new GroundTrafficSweepEventArgs(id));
    }

    public event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived;
    public event EventHandler<GroundTrafficSweepEventArgs>? GroundTrafficSweepCompleted;
}

/// <summary>An announcer that records what the app WOULD have spoken, and whether it would have interrupted.</summary>
internal sealed class SpeechCapture : ScreenReaderAnnouncer
{
    public SpeechCapture() : base(IntPtr.Zero) { }

    /// <summary>Everything spoken, in order.</summary>
    public List<string> All { get; } = new();

    /// <summary>Only what was spoken with <see cref="ScreenReaderAnnouncer.AnnounceImmediate"/> (interrupting).</summary>
    public List<string> Interrupts { get; } = new();

    public override void Announce(string message) => All.Add(message);
    public override void AnnounceImmediate(string message) { All.Add(message); Interrupts.Add(message); }
    public override void AnnounceQueued(string message) => All.Add(message);
    public override void AnnounceWithQueue(string message) => All.Add(message);
}
