namespace MSFSBlindAssist.Services;

/// <summary>
/// What <see cref="FlyByWireMCDUService"/> needs from either FlyByWire MCDU transport: a
/// screen feed, a connection state, and a way to ask for a fresh frame when it becomes the
/// live transport (<see cref="FbwMcduTransportArbiter.Decision.RequestFreshFrom"/>). Both
/// transports raise their events on the UI synchronization context they were built on.
/// </summary>
internal interface IFbwMcduScreenSource : IDisposable
{
    event Action<MCDUDisplayData>? DisplayUpdated;
    event Action<bool>? ConnectionStatusChanged;

    /// <summary>Push the current screen at the next opportunity, even if it has not changed.</summary>
    void RequestFreshFrame();
}

/// <summary>The Coherent debugger transport (<see cref="SimConnect.CoherentA32nxMcduClient"/>).</summary>
internal interface IFbwMcduCoherentTransport : IFbwMcduScreenSource
{
    /// <summary>True while an inspector socket is open on the MCDU view, agent installed or not.</summary>
    bool HoldsView { get; }
    void Start();
    void SetActive(bool active);
    Task<string> SendKeyAsync(string key);
    Task<string> EvalForResultAsync(string expression);
}

/// <summary>The SimBridge relay transport (<see cref="FlyByWireSimBridgeMcduClient"/>).</summary>
internal interface IFbwMcduRelayTransport : IFbwMcduScreenSource
{
    event Action<List<string>>? PrintReceived;
    bool IsConnected { get; }
    void Connect();

    /// <summary>Send one key to the Captain MCDU; true when it was written to an open relay socket
    /// (which says nothing about whether the aircraft received it).</summary>
    Task<bool> SendButtonPress(string key);
}
