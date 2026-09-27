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
        public List<bool> ActiveCalls { get; } = new();
        public void Start() { }
        public void SetActive(bool active) => ActiveCalls.Add(active);
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

    // ------------------------------------------------------------------ showing the window

    [Fact]
    public void Showing_the_window_asks_SimBridge_for_a_fresh_frame_while_it_carries_the_screen()
    {
        // SimBridge pushes only when the screen changes, so without this a page reached while
        // the window was closed was never spoken when it opened.
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        var service = new FlyByWireMCDUService(coherent, relay);
        relay.Connected(true);
        int before = relay.FreshFrameRequests;

        service.SetActive(true);

        Assert.Equal(before + 1, relay.FreshFrameRequests);
    }

    [Fact]
    public void Showing_the_window_while_Coherent_carries_the_screen_leaves_SimBridge_alone()
    {
        // The Coherent client re-reads at once on activation; SimBridge's frame would be dropped.
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        var service = new FlyByWireMCDUService(coherent, relay);
        relay.Connected(true);
        coherent.Connected(true);
        int before = relay.FreshFrameRequests;

        service.SetActive(true);

        Assert.Equal(before, relay.FreshFrameRequests);
        Assert.Equal(new[] { true }, coherent.ActiveCalls);
    }

    [Fact]
    public void Hiding_the_window_asks_no_transport_for_a_frame()
    {
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        var service = new FlyByWireMCDUService(coherent, relay);
        relay.Connected(true);
        int before = relay.FreshFrameRequests;

        service.SetActive(false);

        Assert.Equal(before, relay.FreshFrameRequests);
        Assert.Equal(new[] { false }, coherent.ActiveCalls);
    }

    // ------------------------------------------------------------------ key presses

    private static readonly DateTime T0 = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A clock the service's relay-settle wait advances instead of sleeping.</summary>
    private sealed class Clock
    {
        public DateTime Now = T0;
        public List<TimeSpan> Waits { get; } = new();
        public Task Delay(TimeSpan wait) { Waits.Add(wait); Now += wait; return Task.CompletedTask; }
    }

    private static (FlyByWireMCDUService Service, FakeCoherent Coherent, FakeRelay Relay, Clock Clock) Build()
    {
        var coherent = new FakeCoherent();
        var relay = new FakeRelay();
        var clock = new Clock();
        return (new FlyByWireMCDUService(coherent, relay, () => clock.Now, clock.Delay), coherent, relay, clock);
    }

    [Fact]
    public async Task A_key_goes_over_coherent_whenever_it_holds_the_view()
    {
        // Even before the posted "readable" has made Coherent the live transport: routed by that
        // lagging state, a key pressed straight after the window opened went to a closed
        // SimBridge socket and vanished without a trace.
        var (service, coherent, relay, _) = Build();
        coherent.HoldsView = true;

        Assert.Equal(FbwMcduKeyOutcome.Delivered, await service.SendButtonPress("INIT"));
        Assert.Equal(new[] { "INIT" }, coherent.KeysSent);
        Assert.Empty(relay.KeysSent);
    }

    [Fact]
    public async Task A_key_coherent_certainly_missed_is_resent_over_the_relay()
    {
        var (service, coherent, relay, _) = Build();
        coherent.HoldsView = true;
        coherent.KeyResults.Enqueue("no-agent");
        relay.IsConnected = true;

        Assert.Equal(FbwMcduKeyOutcome.SentOverRelay, await service.SendButtonPress("L1"));
        Assert.Equal(new[] { "L1" }, relay.KeysSent);
    }

    [Theory]
    [InlineData("")]              // the eval timed out — it may still have run
    [InlineData("error: boom")]   // the dispatch threw part-way
    public async Task An_ambiguous_coherent_result_is_never_resent(string result)
    {
        var (service, coherent, relay, _) = Build();
        coherent.HoldsView = true;
        coherent.KeyResults.Enqueue(result);
        relay.IsConnected = true;

        Assert.Equal(FbwMcduKeyOutcome.Ambiguous, await service.SendButtonPress("L1"));
        Assert.Empty(relay.KeysSent);
    }

    [Fact]
    public async Task Without_the_coherent_view_a_key_goes_over_the_relay()
    {
        var (service, coherent, relay, _) = Build();
        relay.IsConnected = true;

        Assert.Equal(FbwMcduKeyOutcome.SentOverRelay, await service.SendButtonPress("CLR"));
        Assert.Empty(coherent.KeysSent);
        Assert.Equal(new[] { "CLR" }, relay.KeysSent);
    }

    [Fact]
    public async Task With_no_transport_a_key_is_reported_not_delivered()
    {
        var (service, coherent, relay, _) = Build();

        Assert.Equal(FbwMcduKeyOutcome.NotDelivered, await service.SendButtonPress("CLR"));
        Assert.Empty(coherent.KeysSent);
        Assert.Empty(relay.KeysSent);
    }

    [Fact]
    public async Task A_certain_miss_with_no_relay_is_reported_not_delivered()
    {
        var (service, coherent, _, _) = Build();
        coherent.HoldsView = true;
        coherent.KeyResults.Enqueue("no-socket");

        Assert.Equal(FbwMcduKeyOutcome.NotDelivered, await service.SendButtonPress("CLR"));
    }

    [Fact]
    public async Task After_a_relayed_key_the_next_coherent_key_waits_until_the_relayed_one_has_landed()
    {
        // FBW's keypad applies each key 150-200 ms after it arrives, so keys stay in order only
        // when they arrive at least 50 ms apart. A relayed key still has to cross SimBridge, the
        // aircraft's relay client and an H-event frame; a Coherent key sent straight after it
        // could arrive first and the scratchpad would read "205" for a typed "250".
        var (service, coherent, relay, clock) = Build();
        coherent.HoldsView = true;
        coherent.KeyResults.Enqueue("no-agent");   // the '5' misses over Coherent and is relayed
        relay.IsConnected = true;

        await service.SendButtonPress("5");
        clock.Now += TimeSpan.FromMilliseconds(100);
        await service.SendButtonPress("0");        // delivered over Coherent

        Assert.Equal(new[] { TimeSpan.FromMilliseconds(FlyByWireMCDUService.RelaySettleMs - 100) }, clock.Waits);
        Assert.Equal(new[] { "5" }, relay.KeysSent);
        Assert.Equal(new[] { "5", "0" }, coherent.KeysSent);
    }

    [Fact]
    public async Task A_coherent_key_long_after_a_relayed_one_does_not_wait()
    {
        var (service, coherent, relay, clock) = Build();
        relay.IsConnected = true;
        await service.SendButtonPress("5");        // no Coherent view yet: relayed

        clock.Now += TimeSpan.FromSeconds(1);
        coherent.HoldsView = true;
        await service.SendButtonPress("0");

        Assert.Empty(clock.Waits);
    }

    [Fact]
    public async Task After_dispose_a_key_is_not_sent()
    {
        var (service, coherent, relay, _) = Build();
        coherent.HoldsView = true;
        relay.IsConnected = true;

        service.Dispose();

        Assert.Equal(FbwMcduKeyOutcome.NotDelivered, await service.SendButtonPress("CLR"));
        Assert.Empty(coherent.KeysSent);
        Assert.Empty(relay.KeysSent);
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
