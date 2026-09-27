// FlyByWireMCDUService is the facade the FlyByWire MCDU window talks to; it drives
// FbwMcduTransportArbiter over two transports (Coherent debugger, SimBridge relay). These run
// it over fakes of both transports and pin what the window sees and where key presses go.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FlyByWireMCDUServiceTests
{
    private sealed class FakeCoherent : IFbwMcduCoherentTransport
    {
        public event Action<MCDUDisplayData>? DisplayUpdated;
        public event Action<bool>? ConnectionStatusChanged;
        public bool HoldsView { get; set; }
        public int FreshFrameRequests { get; private set; }
        public Queue<string> KeyResults { get; } = new();
        public List<string> KeysSent { get; } = new();
        public void Start() { }
        public void SetActive(bool active) { }
        public void RequestFreshFrame() => FreshFrameRequests++;
        public Task<string> SendKeyAsync(string key)
        {
            KeysSent.Add(key);
            return Task.FromResult(KeyResults.Count > 0 ? KeyResults.Dequeue() : "dispatchHEvent");
        }
        public Task<string> EvalForResultAsync(string expression) => Task.FromResult("");
        public void Connected(bool connected) => ConnectionStatusChanged?.Invoke(connected);
        public void Shows(string title) => DisplayUpdated?.Invoke(new MCDUDisplayData { Title = title });
        public void Dispose() { }
    }

    private sealed class FakeRelay : IFbwMcduRelayTransport
    {
        public event Action<MCDUDisplayData>? DisplayUpdated;
        public event Action<bool>? ConnectionStatusChanged;
        public event Action<List<string>>? PrintReceived;
        public bool IsConnected { get; set; }
        public int FreshFrameRequests { get; private set; }
        public List<string> KeysSent { get; } = new();
        public void Connect() { }
        public void RequestFreshFrame() => FreshFrameRequests++;
        public Task<bool> SendButtonPress(string key)
        {
            if (IsConnected) KeysSent.Add(key);
            return Task.FromResult(IsConnected);
        }
        public void Connected(bool connected) { IsConnected = connected; ConnectionStatusChanged?.Invoke(connected); }
        public void Shows(string title) => DisplayUpdated?.Invoke(new MCDUDisplayData { Title = title });
        public void Prints(params string[] lines) => PrintReceived?.Invoke(lines.ToList());
        public void Dispose() { }
    }

    /// <summary>What the MCDU window would see from the service.</summary>
    private sealed class Window
    {
        public List<string> Titles { get; } = new();
        public List<bool> Connection { get; } = new();
        public List<string> Printed { get; } = new();

        public Window(FlyByWireMCDUService service)
        {
            service.DisplayUpdated += d => Titles.Add(d.Title);
            service.ConnectionStatusChanged += c => Connection.Add(c);
            service.PrintReceived += lines => Printed.AddRange(lines);
        }
    }

    [Fact]
    public void A_frame_from_the_live_transport_reaches_the_window()
    {
        var coherent = new FakeCoherent();
        var service = new FlyByWireMCDUService(coherent, new FakeRelay());
        var window = new Window(service);

        coherent.Connected(true);
        coherent.Shows("INIT");

        Assert.Equal(new[] { "INIT" }, window.Titles);
        Assert.Equal(new[] { true }, window.Connection);
    }

    [Fact]
    public void When_coherent_drops_simbridge_is_asked_for_a_fresh_frame_and_nothing_old_is_shown()
    {
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        var service = new FlyByWireMCDUService(coherent, relay);
        var window = new Window(service);
        relay.Connected(true);
        coherent.Connected(true);
        relay.Shows("INIT");                       // the relay's frame, dropped while Coherent is live
        int requestsBefore = relay.FreshFrameRequests;

        coherent.Connected(false);

        Assert.Equal(requestsBefore + 1, relay.FreshFrameRequests);
        Assert.Empty(window.Titles);
    }

    [Fact]
    public void When_coherent_comes_up_it_is_asked_for_a_fresh_frame()
    {
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        _ = new FlyByWireMCDUService(coherent, relay);
        relay.Connected(true);

        coherent.Connected(true);

        Assert.Equal(1, coherent.FreshFrameRequests);
    }

    [Fact]
    public void After_dispose_nothing_a_transport_reports_reaches_the_window()
    {
        // App exit disposed the service while its window was still open and subscribed, then
        // pumped messages: the Coherent client's posted "not readable" came through the arbiter
        // and the window said "MCDU disconnected" on the way out.
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        var service = new FlyByWireMCDUService(coherent, relay);
        var window = new Window(service);
        coherent.Connected(true);
        relay.Connected(true);
        window.Connection.Clear();

        service.Dispose();
        coherent.Connected(false);
        relay.Shows("PERF");
        relay.Prints("ATIS");

        Assert.Empty(window.Connection);
        Assert.Empty(window.Titles);
        Assert.Empty(window.Printed);
    }
}
