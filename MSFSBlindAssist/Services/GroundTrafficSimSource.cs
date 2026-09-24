using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What <see cref="GroundTrafficMonitor"/> needs from the simulator, and nothing more: the own aircraft's
/// position and air/ground state, its OWN ground-traffic sweeps (<see cref="RequestGroundTrafficData"/>,
/// answered entry by entry through <see cref="AiTrafficReceived"/> and completed through
/// <see cref="GroundTrafficSweepCompleted"/> under the request id it returned). The app passes
/// <see cref="SimConnectGroundTrafficSource"/>; GroundTrafficMonitorHeadlessTests pass simulated traffic.
/// </summary>
internal interface IGroundTrafficSimSource
{
    bool IsConnected { get; }
    bool? LastKnownOnGround { get; set; }
    SimConnectManager.AircraftPosition? LastKnownPosition { get; }
    void RequestAircraftPosition();
    void RequestAircraftPositionAsync(Action<SimConnectManager.AircraftPosition> callback);
    /// <summary>Requests one sweep; returns its request id, or 0 when nothing was sent.</summary>
    uint RequestGroundTrafficData(uint radiusMeters);
    event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived;
    event EventHandler<GroundTrafficSweepEventArgs>? GroundTrafficSweepCompleted;
}

/// <summary>Pass-through to <see cref="SimConnectManager"/> — no behaviour of its own.</summary>
internal sealed class SimConnectGroundTrafficSource : IGroundTrafficSimSource
{
    private readonly SimConnectManager _sim;
    public SimConnectGroundTrafficSource(SimConnectManager sim) => _sim = sim;
    public bool IsConnected => _sim.IsConnected;
    public bool? LastKnownOnGround { get => _sim.LastKnownOnGround; set => _sim.LastKnownOnGround = value; }
    public SimConnectManager.AircraftPosition? LastKnownPosition => _sim.LastKnownPosition;
    public void RequestAircraftPosition() => _sim.RequestAircraftPosition();
    public void RequestAircraftPositionAsync(Action<SimConnectManager.AircraftPosition> callback)
        => _sim.RequestAircraftPositionAsync(callback);
    public uint RequestGroundTrafficData(uint radiusMeters) => _sim.RequestGroundTrafficData(radiusMeters);
    public event EventHandler<AiTrafficDataEventArgs>? AiTrafficReceived
    {
        add => _sim.AiTrafficReceived += value;
        remove => _sim.AiTrafficReceived -= value;
    }
    public event EventHandler<GroundTrafficSweepEventArgs>? GroundTrafficSweepCompleted
    {
        add => _sim.GroundTrafficSweepCompleted += value;
        remove => _sim.GroundTrafficSweepCompleted -= value;
    }
}
