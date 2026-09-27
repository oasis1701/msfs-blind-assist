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
    /// <summary>How long a key sent straight over Coherent waits after a key sent over the relay.</summary>
    internal const int RelaySettleMs = 300;

    private readonly IFbwMcduCoherentTransport _coherent;
    private readonly IFbwMcduRelayTransport _simBridge;
    private readonly FbwMcduTransportArbiter _arbiter = new();
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan, Task> _delay;
    private DateTime _lastRelayedKeyUtc = DateTime.MinValue;
    private bool _disposed;

    public event Action<MCDUDisplayData>? DisplayUpdated;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<List<string>>? PrintReceived;

    /// <summary>Either transport is up — what the window shows as "MCDU: Connected".</summary>
    public bool IsConnected => _arbiter.AnyConnected;

    /// <param name="mcduViewTitle">The MCDU's Coherent view title needle: "A32NX_MCDU", or
    /// "A339X_MCDU" on the Headwind A330 (<c>FlyByWireA320Definition.FlightInfoMcduView</c>).</param>
    /// <param name="simBridgeHost">SimBridge host:port for the relay fallback.</param>
    public FlyByWireMCDUService(string mcduViewTitle = "A32NX_MCDU", string simBridgeHost = "localhost:8380")
        : this(new CoherentA32nxMcduClient(mcduViewTitle), new FlyByWireSimBridgeMcduClient(simBridgeHost))
    {
    }

    /// <summary>The two transports, injected (the public constructor builds the real ones), and the
    /// clock and wait the relay-settle rule uses (real ones by default).</summary>
    internal FlyByWireMCDUService(IFbwMcduCoherentTransport coherent, IFbwMcduRelayTransport simBridge,
        Func<DateTime>? utcNow = null, Func<TimeSpan, Task>? delay = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _delay = delay ?? (wait => Task.Delay(wait));
        _coherent = coherent;
        _coherent.DisplayUpdated += d => Apply(_arbiter.Offer(FbwMcduSource.Coherent, d));
        _coherent.ConnectionStatusChanged += c =>
        {
            Log.Info("Services", $"A32NX MCDU over Coherent: {(c ? "readable" : "not readable")}");
            Apply(_arbiter.SetConnected(FbwMcduSource.Coherent, c));
        };

        _simBridge = simBridge;
        _simBridge.DisplayUpdated += d => Apply(_arbiter.Offer(FbwMcduSource.SimBridge, d));
        _simBridge.ConnectionStatusChanged += c =>
        {
            Log.Info("Services", $"A32NX MCDU over SimBridge: {(c ? "connected" : "disconnected")}");
            Apply(_arbiter.SetConnected(FbwMcduSource.SimBridge, c));
        };
        _simBridge.PrintReceived += lines => { if (!_disposed) { PrintReceived?.Invoke(lines); } };
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
    /// Poll the Coherent screen fast while the MCDU window is visible, at the idle rate while
    /// it is closed (a closed window still speaks FMS messages). Showing it re-reads at once on
    /// whichever transport carries the screen, so a page reached while it was closed is spoken
    /// when it opens: the Coherent client re-reads on activation, and SimBridge — which pushes
    /// only when the screen changes — is asked for the current screen.
    /// </summary>
    public void SetActive(bool active)
    {
        _coherent.SetActive(active);
        if (active && _arbiter.Live == FbwMcduSource.SimBridge) { _simBridge.RequestFreshFrame(); }
    }

    /// <summary>
    /// Send a single MCDU key (e.g. "L1", "INIT", "DOT", "CLR") to the Captain MCDU. It goes
    /// over Coherent whenever that socket holds the view; a Coherent press that provably never
    /// reached the instrument (no socket, the agent re-installing, no instrument —
    /// <see cref="CoherentA32nxMcduClient.IsUndeliveredKey"/>) is resent over the relay when
    /// SimBridge is up, and so is any key while Coherent does not hold the view. An AMBIGUOUS
    /// result (a timeout, a dispatch that threw) is never resent: it may have landed, and a
    /// second press on an MCDU key is its own error. A key nothing could take is logged. The
    /// typing loop awaits each key, and a Coherent key after a relayed one waits out
    /// <see cref="RelaySettleMs"/>, so a resend cannot swap two characters of an entry.
    /// </summary>
    public async Task<FbwMcduKeyOutcome> SendButtonPress(string key)
    {
        if (_disposed) { return FbwMcduKeyOutcome.NotDelivered; }

        // Routed by what can deliver the key NOW, not by the arbiter's Live — that follows a
        // POSTED state change, so right after the window opens (or during an agent re-install)
        // it still said "not Coherent" while the socket could already take the key.
        string? coherentResult = null;
        if (_coherent.HoldsView)
        {
            await WaitForRelayedKeyToLand();
            coherentResult = await _coherent.SendKeyAsync(key);
            if (CoherentA32nxMcduClient.IsDeliveredKey(coherentResult)) { return FbwMcduKeyOutcome.Delivered; }
            // Ambiguous (a timeout, a dispatch that threw): it may have landed, and a second
            // press on an MCDU key is its own error. The client has logged it.
            if (!CoherentA32nxMcduClient.IsUndeliveredKey(coherentResult)) { return FbwMcduKeyOutcome.Ambiguous; }
        }

        if (_simBridge.IsConnected && await _simBridge.SendButtonPress(key))
        {
            _lastRelayedKeyUtc = _utcNow();
            if (coherentResult != null)
            {
                Log.Debug("Services", $"A32NX MCDU key {key} not delivered over Coherent ({coherentResult}) — resent over SimBridge.");
            }
            return FbwMcduKeyOutcome.SentOverRelay;
        }

        Log.Debug("Services", $"A32NX MCDU key {key} not delivered: {(coherentResult != null ? $"Coherent answered {coherentResult}" : "no Coherent view")}, and SimBridge is not connected.");
        return FbwMcduKeyOutcome.NotDelivered;
    }

    /// <summary>
    /// FBW's keypad applies each key 150-200 ms after it arrives (a random delay), so keys stay
    /// in order only when they ARRIVE at least 50 ms apart. A relayed key still has to cross
    /// SimBridge, the aircraft's relay client and an H-event frame after the relay send returns,
    /// so a key sent straight over Coherent just after it could arrive first and a typed "250"
    /// would read "205". The first Coherent key after a relayed one therefore waits until
    /// <see cref="RelaySettleMs"/> has passed since the relay send (8 fps covers the frame hops).
    /// </summary>
    private Task WaitForRelayedKeyToLand()
    {
        var wait = _lastRelayedKeyUtc + TimeSpan.FromMilliseconds(RelaySettleMs) - _utcNow();
        return wait > TimeSpan.Zero ? _delay(wait) : Task.CompletedTask;
    }

    /// <summary>
    /// Evaluate a self-contained expression on the MCDU view over the service's own socket.
    /// While the service exists it OWNS that view (<see cref="CoherentViewOwnership"/>), so this
    /// is the only way to evaluate on it: a one-shot eval is refused. Returns "" while the
    /// socket is down (e.g. reconnecting after a flight reload) or when the eval times out. If
    /// the Coherent client could not load its page agent it owns nothing, and the expression
    /// goes out as a one-shot eval instead.
    /// </summary>
    public Task<string> EvalOnMcduViewAsync(string expression) => _coherent.EvalForResultAsync(expression);

    private void Apply(FbwMcduTransportArbiter.Decision decision)
    {
        // Both transports post their callbacks, so some are still queued when the service is
        // disposed — at app exit the message pump then delivered the Coherent client's own
        // "not readable" to a window still subscribed, and it said "MCDU disconnected" on the way
        // out. A disposed service tells the window nothing.
        if (_disposed) { return; }
        switch (decision.RequestFreshFrom)
        {
            case FbwMcduSource.Coherent: _coherent.RequestFreshFrame(); break;
            case FbwMcduSource.SimBridge: _simBridge.RequestFreshFrame(); break;
        }
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
