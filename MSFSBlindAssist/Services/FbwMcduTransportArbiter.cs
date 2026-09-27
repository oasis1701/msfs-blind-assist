namespace MSFSBlindAssist.Services;

/// <summary>Which transport currently feeds the FlyByWire MCDU window.</summary>
public enum FbwMcduSource
{
    None,
    /// <summary>The Coherent debugger socket on the MCDU view — the primary transport.</summary>
    Coherent,
    /// <summary>SimBridge's MCDU relay websocket — the fallback, and the only printer source.</summary>
    SimBridge,
}

/// <summary>
/// Decides which of the two FlyByWire MCDU transports is LIVE and what the window is
/// told, so the window sees ONE MCDU whichever socket happens to be up.
///
/// Rules: Coherent wins whenever it is connected, SimBridge feeds the window only while
/// Coherent is down, and a frame from the transport that is not live is dropped — two
/// sockets narrating the same screen a poll apart would read as flicker. When the live
/// transport changes, the newcomer is asked for a FRESH frame
/// (<see cref="Decision.RequestFreshFrom"/>); a frame remembered from earlier is never put
/// back on screen. A hung SimBridge keeps its socket open (so it still counts as connected)
/// while its last frame grows hours old, and FBW's MCDU sends SimBridge a blank screen when
/// it detaches, so a remembered relay frame can be stale or empty — and replaying it spoke
/// its title even to a closed window. Until the fresh frame arrives, the window keeps what
/// it last showed. The window's connection state is "either transport connected" and is
/// reported only on change.
///
/// Pure and single-threaded by contract: both transports post their callbacks to the
/// UI synchronization context, which is where the facade drives this.
/// </summary>
public sealed class FbwMcduTransportArbiter
{
    private bool _coherentConnected;
    private bool _simBridgeConnected;
    private bool _reportedConnected;

    /// <summary>The transport that feeds the window.</summary>
    public FbwMcduSource Live { get; private set; } = FbwMcduSource.None;

    public bool AnyConnected => _coherentConnected || _simBridgeConnected;

    /// <summary>What the facade should do after an arbiter input.</summary>
    /// <param name="Publish">A frame to hand the window now.</param>
    /// <param name="ConnectionChangedTo">The window's new connection state, when it changed.</param>
    /// <param name="RequestFreshFrom">The transport that just became live and must be asked for
    /// a fresh frame; <see cref="FbwMcduSource.None"/> when the live transport did not change,
    /// or when nothing is live any more.</param>
    public readonly record struct Decision(MCDUDisplayData? Publish, bool? ConnectionChangedTo, FbwMcduSource RequestFreshFrom = FbwMcduSource.None);

    public Decision SetConnected(FbwMcduSource source, bool connected)
    {
        switch (source)
        {
            case FbwMcduSource.Coherent: _coherentConnected = connected; break;
            case FbwMcduSource.SimBridge: _simBridgeConnected = connected; break;
            default: return default;
        }

        var previousLive = Live;
        Live = _coherentConnected ? FbwMcduSource.Coherent
             : _simBridgeConnected ? FbwMcduSource.SimBridge
             : FbwMcduSource.None;

        bool? changed = null;
        if (AnyConnected != _reportedConnected)
        {
            _reportedConnected = AnyConnected;
            changed = _reportedConnected;
        }
        return new Decision(null, changed, Live != previousLive ? Live : FbwMcduSource.None);
    }

    public Decision Offer(FbwMcduSource source, MCDUDisplayData frame)
        => source != FbwMcduSource.None && source == Live ? new Decision(frame, null) : default;
}
