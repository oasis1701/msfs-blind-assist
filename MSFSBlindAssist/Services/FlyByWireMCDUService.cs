using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The FlyByWire A32NX (and Headwind A330) MCDU as ONE service for the MCDU window,
/// over TWO transports the window never sees:
///
///  • PRIMARY — <see cref="CoherentA32nxMcduClient"/>, a persistent Coherent debugger
///    socket on the MCDU view. Nothing to launch: it works whenever the sim is up.
///  • FALLBACK — <see cref="FlyByWireSimBridgeMcduClient"/>, SimBridge's relay websocket,
///    the transport this window used exclusively before 2026-09. It carries the screen
///    only while the Coherent socket is down, and is still the ONLY source of MCDU
///    printouts (ATIS/OFP), which exist nowhere but on the relay.
///
/// <see cref="FbwMcduTransportArbiter"/> decides which transport is live, and both
/// transports post their callbacks to the UI context, so the arbiter and the window's
/// events are driven from one thread.
///
/// IMPORTANT — neither transport can separate the Captain and First Officer MCDUs.
/// FBW's panel.cfg declares ONE mcdu.html gauge on the shared MCDU texture, so one
/// instrument (one Coherent view) draws both screens, and its sendUpdate() writes the
/// same screenState into both the "left" and "right" relay keys (only annunciators and
/// brightness differ). We therefore mirror FBW's own web remote MCDU exactly: only ever
/// control the Captain MCDU and read the single shared screen. Do NOT reintroduce a side
/// selector on either transport.
/// </summary>
public class FlyByWireMCDUService : IDisposable
{
    private readonly CoherentA32nxMcduClient _coherent;
    private readonly FlyByWireSimBridgeMcduClient _simBridge;
    private readonly FbwMcduTransportArbiter _arbiter = new();
    private bool _disposed;

    public event Action<MCDUDisplayData>? DisplayUpdated;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<List<string>>? PrintReceived;

    /// <summary>Either transport is up — what the window shows as "MCDU: Connected".</summary>
    public bool IsConnected => _arbiter.AnyConnected;

    /// <summary>The transport currently feeding the window (diagnostics).</summary>
    public FbwMcduSource LiveSource => _arbiter.Live;

    /// <summary>
    /// True while the Coherent client holds the MCDU view's inspector socket. Coherent GT
    /// allows ONE socket per view, so any other eval against that view (the D / Shift+D
    /// flight-info readout) must go through <see cref="EvalOnMcduViewAsync"/> meanwhile.
    /// </summary>
    public bool HoldsMcduView => _coherent.HoldsView;

    /// <param name="mcduViewTitle">The MCDU's Coherent view title needle: "A32NX_MCDU", or
    /// "A339X_MCDU" on the Headwind A330 (<c>FlyByWireA320Definition.FlightInfoMcduView</c>).</param>
    /// <param name="simBridgeHost">SimBridge host:port for the relay fallback.</param>
    public FlyByWireMCDUService(string mcduViewTitle = "A32NX_MCDU", string simBridgeHost = "localhost:8380")
    {
        _coherent = new CoherentA32nxMcduClient(mcduViewTitle);
        _coherent.DisplayUpdated += d => Apply(_arbiter.Offer(FbwMcduSource.Coherent, d));
        _coherent.ConnectionStatusChanged += c =>
        {
            Log.Info("Services", $"A32NX MCDU over Coherent: {(c ? "readable" : "not readable")}");
            Apply(_arbiter.SetConnected(FbwMcduSource.Coherent, c));
        };

        _simBridge = new FlyByWireSimBridgeMcduClient(simBridgeHost);
        _simBridge.DisplayUpdated += d => Apply(_arbiter.Offer(FbwMcduSource.SimBridge, d));
        _simBridge.ConnectionStatusChanged += c =>
        {
            Log.Info("Services", $"A32NX MCDU over SimBridge: {(c ? "connected" : "disconnected")}");
            Apply(_arbiter.SetConnected(FbwMcduSource.SimBridge, c));
        };
        _simBridge.PrintReceived += lines => PrintReceived?.Invoke(lines);
    }

    /// <summary>
    /// Start both transports. There is deliberately NO Disconnect(): the Coherent client
    /// cannot be restarted (Start() after Stop() is a no-op), so a Disconnect/Connect pair
    /// would silently leave the primary transport dead. The lifecycle is Connect once,
    /// Dispose on aircraft switch — which is all MainForm ever did.
    /// </summary>
    public void Connect()
    {
        if (_disposed) { return; }
        _coherent.Start();
        _simBridge.Connect();
    }

    /// <summary>
    /// Poll the Coherent screen only while the MCDU window is visible; the socket stays
    /// warm while it is closed. The SimBridge relay pushes on its own and needs no gate.
    /// </summary>
    public void SetActive(bool active) => _coherent.SetActive(active);

    /// <summary>Send a single MCDU key (e.g. "L1", "INIT", "DOT", "CLR") to the Captain MCDU over the live transport.</summary>
    public Task SendButtonPress(string key)
    {
        return _arbiter.Live switch
        {
            FbwMcduSource.Coherent => _coherent.SendKeyAsync(key),
            // No live transport: the relay send is a no-op on a closed socket, so trying
            // costs nothing and covers the moment right after SimBridge comes up.
            _ => _simBridge.SendButtonPress(key),
        };
    }

    /// <summary>Evaluate a self-contained expression on the MCDU view over the held socket.</summary>
    public Task<string> EvalOnMcduViewAsync(string expression) => _coherent.EvalForResultAsync(expression);

    private void Apply(FbwMcduTransportArbiter.Decision decision)
    {
        if (decision.ConnectionChangedTo is bool connected) { ConnectionStatusChanged?.Invoke(connected); }
        if (decision.Publish != null) { DisplayUpdated?.Invoke(decision.Publish); }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _coherent.Dispose();
        _simBridge.Dispose();
    }
}
