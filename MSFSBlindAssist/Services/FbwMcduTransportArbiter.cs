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
/// Coherent is down, and a frame from the transport that is not live is remembered but
/// never published — two sockets narrating the same screen a poll apart would read as
/// flicker. When the live transport changes, the newcomer's last frame is re-published
/// so the window catches up without waiting for the screen to change — but only a frame
/// taken while that transport was connected: a disconnect discards it. The window's
/// connection state is "either transport connected" and is reported only on change.
///
/// Pure and single-threaded by contract: both transports post their callbacks to the
/// UI synchronization context, which is where the facade drives this.
/// </summary>
public sealed class FbwMcduTransportArbiter
{
    private bool _coherentConnected;
    private bool _simBridgeConnected;
    private bool _reportedConnected;
    private MCDUDisplayData? _lastCoherentFrame;
    private MCDUDisplayData? _lastSimBridgeFrame;

    /// <summary>The transport that feeds the window and receives its key presses.</summary>
    public FbwMcduSource Live { get; private set; } = FbwMcduSource.None;

    public bool AnyConnected => _coherentConnected || _simBridgeConnected;

    /// <summary>What the facade should do after an arbiter input.</summary>
    public readonly record struct Decision(MCDUDisplayData? Publish, bool? ConnectionChangedTo);

    public Decision SetConnected(FbwMcduSource source, bool connected)
    {
        // A transport that disconnects FORGETS its last frame: the screen may move on while
        // it is down, and re-publishing the pre-drop frame when it comes back would read the
        // old page title first and the current one a poll later. Both transports push a
        // fresh frame after reconnecting (SimBridge sends requestUpdate; the Coherent client
        // clears its change filter on every agent install), so nothing is lost.
        switch (source)
        {
            case FbwMcduSource.Coherent:
                _coherentConnected = connected;
                if (!connected) { _lastCoherentFrame = null; }
                break;
            case FbwMcduSource.SimBridge:
                _simBridgeConnected = connected;
                if (!connected) { _lastSimBridgeFrame = null; }
                break;
            default: return default;
        }

        var previousLive = Live;
        Live = _coherentConnected ? FbwMcduSource.Coherent
             : _simBridgeConnected ? FbwMcduSource.SimBridge
             : FbwMcduSource.None;

        MCDUDisplayData? publish = null;
        if (Live != previousLive && Live != FbwMcduSource.None)
        {
            publish = Live == FbwMcduSource.Coherent ? _lastCoherentFrame : _lastSimBridgeFrame;
        }

        bool? changed = null;
        if (AnyConnected != _reportedConnected)
        {
            _reportedConnected = AnyConnected;
            changed = _reportedConnected;
        }
        return new Decision(publish, changed);
    }

    public Decision Offer(FbwMcduSource source, MCDUDisplayData frame)
    {
        switch (source)
        {
            case FbwMcduSource.Coherent: _lastCoherentFrame = frame; break;
            case FbwMcduSource.SimBridge: _lastSimBridgeFrame = frame; break;
            default: return default;
        }
        return source == Live ? new Decision(frame, null) : default;
    }
}
