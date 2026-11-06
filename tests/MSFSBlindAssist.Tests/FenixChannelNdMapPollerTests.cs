// Characterization tests for FenixChannelNdMapPoller: the version is asked every PollMs and the points are fetched
// only when it changes. The request, the clock and the waits are fakes of this file's own: nothing opens a socket and
// nothing waits in real time. The loop stops at each wait until the test lets it go on.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdMapPollerTests
{
    private static FenixChannelUnaryResult Answer(byte[] message)
        => new(FenixChannelState.Connected, message, "");

    private static FenixChannelUnaryResult Down(string text)
        => new(FenixChannelState.NotAnswering, null, text);

    private static byte[] VersionReply(ulong version) => new FenixChannelProtoWriter().Varint(1, version).ToArray();

    private static byte[] DataReply(ulong version, string ident = "LGC") => new FenixChannelProtoWriter()
        .Message(2, new FenixChannelProtoWriter().Varint(1, 3).Double(2, 33.0).Double(3, -85.0).String(5, ident))
        .Double(5, 33.6).Double(6, -84.4).Varint(8, 80).Varint(9, 2).Varint(10, version).Varint(12, 2).ToArray();

    private sealed class Rig : IDisposable
    {
        private readonly object _lock = new();
        private readonly SemaphoreSlim _waited = new(0);
        private readonly SemaphoreSlim _gate = new(0);
        private readonly List<TimeSpan> _delays = [];
        private readonly List<(FenixChannelCall Call, byte[] Request)> _calls = [];

        public Func<FenixChannelUnaryResult> Version = () => Answer(VersionReply(1));
        public Func<FenixChannelUnaryResult> Data = () => Answer(DataReply(1));
        public long Now = 1000;
        public readonly FenixChannelNdMapPoller Poller;

        public Rig()
        {
            Poller = new FenixChannelNdMapPoller(Request, () => Interlocked.Read(ref Now), Delay);
        }

        public int Calls(FenixChannelCall call)
        {
            lock (_lock) return _calls.Count(c => c.Call == call);
        }

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_lock) return _delays.ToArray();
            }
        }

        public IReadOnlyList<byte[]> Requests
        {
            get
            {
                lock (_lock) return _calls.Select(c => c.Request).ToArray();
            }
        }

        private Task<FenixChannelUnaryResult> Request(FenixChannelCall call, byte[] request, CancellationToken ct)
        {
            lock (_lock) _calls.Add((call, request));
            return Task.FromResult(call == FenixChannelCall.MapDataVersion ? Version() : Data());
        }

        private Task Delay(TimeSpan time, CancellationToken ct)
        {
            lock (_lock) _delays.Add(time);
            _waited.Release();
            return _gate.WaitAsync(ct);
        }

        /// <summary>Waits until the loop is at a wait (its poll is done).</summary>
        public async Task PollDone() => Assert.True(await _waited.WaitAsync(TimeSpan.FromSeconds(10)));

        /// <summary>Lets the loop go on from its wait.</summary>
        public void Go() => _gate.Release();

        public void Dispose() => Poller.Dispose();
    }

    [Fact]
    public async Task The_poller_fetches_only_when_the_version_changes()
    {
        using var rig = new Rig();
        rig.Version = () => Answer(VersionReply(1));
        rig.Data = () => Answer(DataReply(1));
        rig.Poller.Start();

        await rig.PollDone();
        Assert.Equal(1, rig.Calls(FenixChannelCall.MapData));
        var first = rig.Poller.Latest;
        Assert.Equal("LGC", first.Points!.Points.Single().Ident);
        Assert.Equal(1000, first.ReceivedAtMs);
        Assert.Null(first.Problem);

        // The same version: no fetch, and the reply is confirmed current now.
        rig.Now = 2000;
        rig.Go();
        await rig.PollDone();
        Assert.Equal(2, rig.Calls(FenixChannelCall.MapDataVersion));
        Assert.Equal(1, rig.Calls(FenixChannelCall.MapData));
        Assert.Equal(2000, rig.Poller.Latest.ReceivedAtMs);
        Assert.Same(first.Points, rig.Poller.Latest.Points);

        // A new version: fetched.
        rig.Version = () => Answer(VersionReply(2));
        rig.Data = () => Answer(DataReply(2, "DOB"));
        rig.Now = 3000;
        rig.Go();
        await rig.PollDone();
        Assert.Equal(2, rig.Calls(FenixChannelCall.MapData));
        Assert.Equal("DOB", rig.Poller.Latest.Points!.Points.Single().Ident);
        Assert.Equal(3000, rig.Poller.Latest.ReceivedAtMs);

        Assert.All(rig.Delays, delay => Assert.Equal(TimeSpan.FromMilliseconds(FenixChannelNdMapPoller.PollMs), delay));
        Assert.All(rig.Requests, request => Assert.Equal(new byte[] { 0x08, 0x01 }, request));
    }

    [Fact]
    public async Task A_failure_waits_before_the_next_try()
    {
        using var rig = new Rig();
        rig.Version = () => Down("the Fenix is not answering");
        rig.Poller.Start();

        await rig.PollDone();
        Assert.Equal("the Fenix is not answering", rig.Poller.Latest.Problem);
        Assert.Null(rig.Poller.Latest.Points);
        Assert.Equal(TimeSpan.FromMilliseconds(FenixChannelNdMapPoller.RetryMs), rig.Delays[^1]);

        // The next success clears the problem and goes back to the ordinary pace.
        rig.Version = () => Answer(VersionReply(1));
        rig.Go();
        await rig.PollDone();
        Assert.Null(rig.Poller.Latest.Problem);
        Assert.NotNull(rig.Poller.Latest.Points);
        Assert.Equal(TimeSpan.FromMilliseconds(FenixChannelNdMapPoller.PollMs), rig.Delays[^1]);

        // A reply that does not decode is a failure too, and so is a map call that throws.
        rig.Version = () => Answer(VersionReply(2));
        rig.Data = () => Answer(new FenixChannelProtoWriter().String(8, "80").ToArray());
        rig.Go();
        await rig.PollDone();
        Assert.Equal("field 8 has the wrong shape", rig.Poller.Latest.Problem);
        Assert.Equal(TimeSpan.FromMilliseconds(FenixChannelNdMapPoller.RetryMs), rig.Delays[^1]);

        rig.Version = () => throw new InvalidOperationException("boom");
        rig.Go();
        await rig.PollDone();
        Assert.Contains("boom", rig.Poller.Latest.Problem);
        Assert.Equal(TimeSpan.FromMilliseconds(FenixChannelNdMapPoller.RetryMs), rig.Delays[^1]);
    }

    [Fact]
    public async Task The_poller_keeps_the_last_good_reply()
    {
        using var rig = new Rig();
        rig.Poller.Start();
        await rig.PollDone();
        var good = rig.Poller.Latest;

        // The version moves and the fetch fails: the old points stay, at their old time, with the problem beside them.
        rig.Version = () => Answer(VersionReply(2));
        rig.Data = () => Down("HTTP 500");
        rig.Now = 9000;
        rig.Go();
        await rig.PollDone();

        var after = rig.Poller.Latest;
        Assert.Same(good.Points, after.Points);
        Assert.Equal(good.ReceivedAtMs, after.ReceivedAtMs);
        Assert.Equal("HTTP 500", after.Problem);
    }

    [Fact]
    public async Task Nothing_is_asked_after_dispose()
    {
        using var rig = new Rig();
        rig.Poller.Start();
        await rig.PollDone();
        int versionCalls = rig.Calls(FenixChannelCall.MapDataVersion);
        int dataCalls = rig.Calls(FenixChannelCall.MapData);

        rig.Poller.Dispose();
        rig.Go();
        try
        {
            await rig.Poller.Loop!;
        }
        catch (OperationCanceledException)
        {
            // The loop was cancelled while it waited.
        }

        Assert.Equal(versionCalls, rig.Calls(FenixChannelCall.MapDataVersion));
        Assert.Equal(dataCalls, rig.Calls(FenixChannelCall.MapData));

        // A poller disposed before it starts never asks.
        using var idle = new Rig();
        idle.Poller.Dispose();
        idle.Poller.Start();
        Assert.Null(idle.Poller.Loop);
        Assert.Equal(0, idle.Calls(FenixChannelCall.MapDataVersion));
    }

    [Fact]
    public async Task Dispose_releases_the_cancellation_source_once_the_loop_has_ended()
    {
        using var rig = new Rig();
        rig.Poller.Start();
        await rig.PollDone();

        rig.Poller.Dispose();
        rig.Go();
        try
        {
            await rig.Poller.Loop!;
        }
        catch (OperationCanceledException)
        {
            // The loop was cancelled while it waited.
        }

        Assert.True(SpinWait.SpinUntil(() => CancelSourceDisposed(rig.Poller), TimeSpan.FromSeconds(5)));

        // With no loop there is nothing to wait for.
        using var idle = new Rig();
        idle.Poller.Dispose();
        Assert.True(CancelSourceDisposed(idle.Poller));
    }

    private static bool CancelSourceDisposed(FenixChannelNdMapPoller poller)
    {
        var source = (CancellationTokenSource)typeof(FenixChannelNdMapPoller)
            .GetField("_cancel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(poller)!;
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
