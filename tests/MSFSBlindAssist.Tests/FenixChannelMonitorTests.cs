// Characterization tests for FenixChannelMonitor, the owner of one session of the Fenix display channel (the
// local gRPC service on 127.0.0.1:18083): it runs the client's two calls, decodes each message, feeds the trust
// gate under one lock, keeps the latest frame, asks for the Fenix build on each connection and writes
// fenix_channel.log. The network is a fake (FenixChannelFakeServer, one per call behind a router): no test opens
// a socket or talks to the real port.
//
// The tests of the main flows start the monitor and let the client deliver, as the app does. The rules about
// ORDER cannot be tested that way, because a real stream will not deliver a state or a message at an exact
// moment, twice in a row, or after Dispose: a build answer that comes back late, an ECAM entries message that
// arrived before a reconnect, a failure that repeats, a verdict that changes under one headline. Those tests call
// the monitor's callbacks and its tick directly (they are internal for this) on a monitor that was never
// started, which also makes them instant.
//
// The rules the monitor keeps: each connection of the data call starts the gate fresh, then gives it the last
// ECAM entries message again, then asks for the build, and a build answer belongs to its connection only; an
// empty push does not replace the latest frame; after Dispose every callback is ignored, and Dispose itself
// writes "channel stopped" once, as the last line of the log; "display data: connected" is written with the
// first message, not the headers, and a failure that repeats is written once until a message arrives; a
// verdict is written when its Trusted or its headline (the text before the first colon) changes.
//
// Timing: every wait for something to happen is a poll with a 5 s limit. The short pauses (Quiet) only prove that
// nothing MORE happens, so a slow machine can make them pass trivially but never fail.

using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.MonitorTestKit;

namespace MSFSBlindAssist.Tests;

/// <summary>What the monitor tests share: the fake network, a log sink, a scripted build query and a few waits.</summary>
internal static class MonitorTestKit
{
    public const string CheckedBuild = "3.0.0.7613";

    public static readonly IReadOnlySet<string> Checked = new HashSet<string> { CheckedBuild };

    public static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan TickEvery = TimeSpan.FromMilliseconds(50);
    public static readonly TimeSpan BuildRetry = TimeSpan.FromMilliseconds(50);

    /// <summary>A build query that answers at once with the one build the tests treat as checked.</summary>
    public static readonly Func<CancellationToken, Task<string?>> AnswersTheCheckedBuild =
        _ => Task.FromResult<string?>(CheckedBuild);

    /// <summary>Messages as gRPC frames, back to back.</summary>
    public static byte[] Frames(params byte[][] messages)
        => messages.SelectMany(message => FenixChannelProtoWriter.Frame(message)).ToArray();

    /// <summary>A whole display push that passes every check of the gate, at the given altitude.</summary>
    public static byte[] GoodPush(double altitude = 1000) => FenixChannelProtoWriter.GoodPush(altitude: altitude);

    /// <summary>An ECAM entries message whose entries arrive as a number instead of a nested message: a wrong shape.</summary>
    public static byte[] WrongShapedEntries() => new FenixChannelProtoWriter().Varint(1, 5).ToArray();

    /// <summary>An ECAM entries message that has the shape the contract gives it: just a heartbeat.</summary>
    public static byte[] GoodEntries() => new FenixChannelProtoWriter().Bool(2, true).ToArray();

    /// <summary>
    /// A monitor on the fake network, with the 50 ms tick and build retry of the brief, no wait before a
    /// reconnect, and its log in <paramref name="lines"/>.
    /// </summary>
    public static FenixChannelMonitor NewMonitor(
        HttpMessageHandler server,
        Lines lines,
        Func<CancellationToken, Task<string?>>? readBuild = null,
        IReadOnlySet<string>? verified = null)
        => new(readBuild ?? AnswersTheCheckedBuild, verified ?? Checked, server, lines.Write, TickEvery, BuildRetry,
            (_, _) => TimeSpan.Zero);

    /// <summary>A monitor that is never started: a test calls its callbacks and its tick itself.</summary>
    public static FenixChannelMonitor NewIdleMonitor(Lines lines, Func<CancellationToken, Task<string?>>? readBuild = null)
        => NewMonitor(new ChannelServer(), lines, readBuild);

    /// <summary>Waits, polling, until <paramref name="reached"/> holds; fails after 5 s and shows the log.</summary>
    public static async Task WaitUntil(Func<bool> reached, string what, Lines? lines = null)
    {
        long deadline = Environment.TickCount64 + (long)Generous.TotalMilliseconds;
        while (!reached())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Timed out after 5 s waiting for {what}." + (lines is null ? "" : $" The log: {lines}"));
            }

            await Task.Delay(10);
        }
    }

    public static bool AllTrusted(FenixChannelMonitor monitor)
        => monitor.VerdictFor(FenixChannelDisplay.Pfd).Trusted
           && monitor.VerdictFor(FenixChannelDisplay.Ewd).Trusted
           && monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted;

    /// <summary>Sends <paramref name="message"/> into an open stream every 20 ms until <paramref name="stop"/>.</summary>
    public static Task Feed(OpenStream stream, byte[] message, CancellationToken stop)
        => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await stream.SendAsync(message);
                try
                {
                    await Task.Delay(20, stop);
                }
                catch (OperationCanceledException)
                {
                }
            }
        });

    /// <summary>The monitor's log lines, as the injected log wrote them (from any thread).</summary>
    internal sealed class Lines
    {
        private readonly object _lock = new();
        private readonly List<string> _lines = new();

        public Action<string> Write => line =>
        {
            lock (_lock) _lines.Add(line);
        };

        public string[] All
        {
            get { lock (_lock) return _lines.ToArray(); }
        }

        /// <summary>The lines that begin with <paramref name="prefix"/>, in order.</summary>
        public string[] Starting(string prefix)
            => All.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        public string[] Comparisons() => Starting("comparisons: ");

        /// <summary>
        /// The verdict lines ("PFD trusted", "E/WD not trusted: ..."), in order: those of the display named
        /// ("PFD", "E/WD" or "SD"), or of all three.
        /// </summary>
        public string[] Verdicts(string? display = null)
        {
            string[] names = display is null ? new[] { "PFD", "E/WD", "SD" } : new[] { display };
            return All
                .Where(line => names.Any(name =>
                    line == name + " trusted" || line.StartsWith(name + " not trusted: ", StringComparison.Ordinal)))
                .ToArray();
        }

        public override string ToString() => string.Join(" | ", All);
    }

    /// <summary>
    /// The network under the monitor: one fake per call, so a test scripts the display data call and the ECAM
    /// entries call apart. A fake with one queue would give a queued answer to whichever call asked first. The
    /// call a request is for is read from the end of its path.
    /// </summary>
    internal sealed class ChannelServer : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _data;
        private readonly HttpMessageInvoker _ecam;

        public ChannelServer()
        {
            _data = new HttpMessageInvoker(Data, disposeHandler: false);
            _ecam = new HttpMessageInvoker(Ecam, disposeHandler: false);
        }

        public FenixChannelFakeServer Data { get; } = new();

        public FenixChannelFakeServer Ecam { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool ecam = request.RequestUri?.AbsolutePath.EndsWith("/SubscribeEcamEntries", StringComparison.Ordinal) == true;
            return (ecam ? _ecam : _data).SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// An answer of the data call that the test feeds by hand: <see cref="SendAsync"/> adds messages to the open
    /// stream whenever it likes and <see cref="End"/> ends it with no trailers, as a cut connection does. The
    /// fake's own streams give all their frames at once, so they cannot send one after something else happened.
    /// </summary>
    internal sealed class OpenStream
    {
        private readonly Pipe _pipe = new();

        public OpenStream()
        {
            Response = new FenixChannelFakeServer.FakeResponse(HttpStatusCode.OK) { Version = HttpVersion.Version20 };
            var content = new StreamContent(_pipe.Reader.AsStream());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            Response.Content = content;
        }

        public FenixChannelFakeServer.FakeResponse Response { get; }

        public async Task SendAsync(params byte[][] messages)
        {
            foreach (byte[] message in messages)
            {
                await _pipe.Writer.WriteAsync(FenixChannelProtoWriter.Frame(message));
            }
        }

        public void End() => _pipe.Writer.Complete();
    }

    /// <summary>
    /// A scripted build query: each call is one pending question, answered when the test says. It ignores its
    /// token on purpose, as a query that is already on the wire does when a newer connection cancels it.
    /// </summary>
    internal sealed class BuildQueries
    {
        private readonly object _lock = new();
        private readonly List<TaskCompletionSource<string?>> _asked = new();

        public Func<CancellationToken, Task<string?>> Read => _ =>
        {
            var question = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) _asked.Add(question);
            return question.Task;
        };

        public int Asked
        {
            get { lock (_lock) return _asked.Count; }
        }

        /// <summary>Answers the question asked as number <paramref name="index"/> (0 for the first).</summary>
        public void Answer(int index, string? build)
        {
            TaskCompletionSource<string?> question;
            lock (_lock) question = _asked[index];
            question.TrySetResult(build);
        }
    }
}

public class FenixChannelMonitorTests
{
    // ---- The comparison keys --------------------------------------------------------------------

    [Theory]
    [InlineData("I_FCU_EFIS1_FD", 1.0, "FdLightCapt", 1.0, false)]
    [InlineData("I_FCU_EFIS2_FD", 0.0, "FdLightFo", 0.0, false)]
    [InlineData("INDICATED_ALTITUDE", 1023.0, "AltitudeFt", 1023.0, false)]
    [InlineData(FenixChannelMonitor.HeadingKey, 271.5, "MagHeadingDeg", 271.5, true)]
    [InlineData(FenixChannelMonitor.FuelKey, 6480.0, "FuelKg", 2939.3, true)]
    public void Maps_the_five_comparison_keys(
        string key, double value, string expectedKind, double expectedValue, bool expectedConsume)
    {
        // The flight director lights and the altitude are peeked: they carry on to their own handling. The two
        // channel keys are consumed. Fuel comes in pounds and goes to the gate in kilograms.
        Assert.True(FenixChannelMonitor.TryMapComparison(
            key, value, out FenixComparison kind, out double converted, out bool consume));

        Assert.Equal(Enum.Parse<FenixComparison>(expectedKind), kind);
        Assert.Equal(expectedValue, converted, 0.1);
        Assert.Equal(expectedConsume, consume);
    }

    [Theory]
    [InlineData("SIM_ON_GROUND", 1.0)]
    [InlineData("i_fcu_efis1_fd", 1.0)]   // a var key is exact: the definition's keys are upper case
    [InlineData("", 0.0)]
    public void Maps_no_other_key(string key, double value)
    {
        Assert.False(FenixChannelMonitor.TryMapComparison(key, value, out _, out _, out bool consume));
        Assert.False(consume);   // a key that is not the channel's is never consumed
    }

    // ---- The data flow --------------------------------------------------------------------------

    [Fact]
    public async Task Feeds_decoded_frames_to_the_gate_and_keeps_the_latest()
    {
        // The live stream: frames, then nothing, and no end.
        var server = new ChannelServer();
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush(1000), GoodPush(2000))));
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();

        await WaitUntil(() => monitor.Latest?.Altitude == 2000, "the second frame to become the latest", lines);
        await WaitUntil(
            () => monitor.VerdictFor(FenixChannelDisplay.Sd) == new FenixDisplayVerdict(true, "trusted"),
            "SD to be trusted",
            lines);

        // No SimConnect value reached the gate, so the displays that cross-check one are not trusted.
        Assert.False(monitor.VerdictFor(FenixChannelDisplay.Pfd).Trusted);
        Assert.False(monitor.VerdictFor(FenixChannelDisplay.Ewd).Trusted);

        // Start opened both calls, each once.
        await server.Ecam.WaitForRequestsAsync(1);
        Assert.Single(server.Data.Requests);
        Assert.Single(server.Ecam.Requests);
    }

    [Fact]
    public async Task Trusts_all_three_displays_when_the_five_values_agree()
    {
        // SimConnect's side comes first and the frames after it, as in the sim: a check runs on a frame. A feeder
        // keeps the frames fresh, so a slow machine cannot let the stream stall before the poll sees the verdicts.
        var server = new ChannelServer();
        var stream = new OpenStream();
        server.Data.Enqueue(() => stream.Response);
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);
        using var stop = new CancellationTokenSource();
        Task feeder = Task.CompletedTask;
        try
        {
            monitor.Start();
            monitor.OnComparison(FenixComparison.FdLightCapt, 1);
            monitor.OnComparison(FenixComparison.FdLightFo, 1);
            monitor.OnComparison(FenixComparison.AltitudeFt, 2000);
            monitor.OnComparison(FenixComparison.MagHeadingDeg, 90);
            monitor.OnComparison(FenixComparison.FuelKg, 2940);   // the FOB line of GoodPush

            feeder = Feed(stream, GoodPush(altitude: 2000), stop.Token);

            await WaitUntil(() => AllTrusted(monitor), "all three displays to be trusted", lines);
        }
        finally
        {
            await stop.CancelAsync();
            await feeder;
        }
    }

    [Fact]
    public async Task The_ecam_entries_call_feeds_the_gate()
    {
        // A wrong-shaped ECAM entries message latches the whole channel, whichever call gets there first: one
        // that comes before the data call connects is given to the gate again at the connect, and one that comes
        // after it latches the gate at once. Either way the SD reads the latch.
        var server = new ChannelServer();
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush())));
        server.Ecam.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(WrongShapedEntries())));
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();

        await WaitUntil(
            () => lines.All.Contains("SD not trusted: ECAM entries field 1 has the wrong shape"),
            "the ECAM entries latch to reach SD",
            lines);
    }

    [Fact]
    public void An_empty_push_does_not_replace_the_latest_frame()
    {
        // The gate skips a push with no display data, and so does the latest frame: a read must never find a
        // frame of defaults where the last real frame was.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(1000));
        FenixChannelFrame? first = monitor.Latest;
        Assert.NotNull(first);
        Assert.Equal(1000, first.Altitude);

        monitor.OnDataMessage([]);   // no PrimaryData
        monitor.OnDataMessage(new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()).ToArray());   // an empty one
        Assert.Same(first, monitor.Latest);

        // The next real frame does replace it.
        monitor.OnDataMessage(GoodPush(2000));
        Assert.Equal(2000, monitor.Latest?.Altitude);
    }

    [Fact]
    public async Task The_ecam_entries_latch_survives_a_data_reconnect()
    {
        // The entries stream sends only when its list changes, so a wrong-shaped message that arrived before a
        // reconnect is the last word it has said. The gate starts each connection fresh; the monitor gives it that
        // message again, so the reconnect does not erase the latch.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted", lines);

        monitor.OnEcamMessage(WrongShapedEntries());
        Assert.Equal(
            new FenixDisplayVerdict(false, "ECAM entries field 1 has the wrong shape"),
            monitor.VerdictFor(FenixChannelDisplay.Sd));

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(
            () => lines.All.Count(line => line == "build 3.0.0.7613: checked") == 2,
            "the second connection's build",
            lines);

        Assert.Equal(
            new FenixDisplayVerdict(false, "ECAM entries field 1 has the wrong shape"),
            monitor.VerdictFor(FenixChannelDisplay.Sd));
    }

    // ---- The build ------------------------------------------------------------------------------

    [Fact]
    public async Task Asks_for_the_build_on_each_connect()
    {
        // The first stream is held open until the first connection has asked, then ends: the client connects
        // again, and the second connection asks too. (A stream that ended at once could end before the first
        // query started, and a connection that is over needs no build.)
        var server = new ChannelServer();
        var first = new OpenStream();
        server.Data.Enqueue(() => first.Response);
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush())));
        var lines = new Lines();
        int asked = 0;
        using var monitor = NewMonitor(server, lines, _ =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult<string?>(CheckedBuild);
        });

        monitor.Start();
        await WaitUntil(() => Volatile.Read(ref asked) == 1, "the first connection to ask for the build", lines);
        first.End();
        await WaitUntil(() => Volatile.Read(ref asked) == 2, "the second connection to ask for the build", lines);
        await Task.Delay(Quiet);

        Assert.Equal(2, Volatile.Read(ref asked));
        Assert.Equal(2, server.Data.Requests.Count);
    }

    [Fact]
    public async Task Retries_the_build_until_it_answers()
    {
        var server = new ChannelServer();
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush())));
        var lines = new Lines();
        int asked = 0;
        using var monitor = NewMonitor(server, lines,
            _ => Task.FromResult<string?>(Interlocked.Increment(ref asked) == 1 ? null : CheckedBuild));

        monitor.Start();

        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted", lines);
        Assert.Equal(2, Volatile.Read(ref asked));   // one unanswered, one answered: the loop ends with the answer
        Assert.Contains("build 3.0.0.7613: checked", lines.All);
    }

    [Fact]
    public async Task A_build_the_app_has_not_checked_is_logged_as_such()
    {
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the build query", lines);

        queries.Answer(0, "3.0.0.9999");

        await WaitUntil(() => lines.Starting("build ").Length == 1, "the build line", lines);
        Assert.Equal(
            new[] { "build 3.0.0.9999: not checked; every display uses the AI read" },
            lines.Starting("build "));
    }

    [Fact]
    public async Task A_failing_build_query_is_retried_and_logged_once()
    {
        // FenixDataService answers null rather than throw, but the monitor must not depend on that: a query that
        // throws is an answer that did not come. The same failure is one line, not one every 30 s.
        var lines = new Lines();
        int asked = 0;
        using var monitor = NewIdleMonitor(lines, _ =>
        {
            if (Interlocked.Increment(ref asked) <= 3) throw new InvalidOperationException("the query failed");
            return Task.FromResult<string?>(CheckedBuild);
        });

        monitor.OnDataState(FenixChannelState.Connected, "");

        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build to be answered at last", lines);
        Assert.Equal(4, Volatile.Read(ref asked));
        Assert.Single(lines.Starting("build query failed"));
        Assert.Contains("InvalidOperationException: the query failed", lines.Starting("build query failed")[0]);
    }

    [Fact]
    public async Task A_build_answer_from_an_earlier_connection_is_ignored()
    {
        // Connection 1 asks (question 0) and is gone; connection 2 asks again (question 1). Question 0's answer
        // comes back late, and the gate must not take a build that belongs to a connection that has ended.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the first connection to ask for the build", lines);
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => queries.Asked == 2, "the second connection to ask for the build", lines);

        queries.Answer(0, "3.0.0.7000");
        await Task.Delay(Quiet);

        Assert.Equal(new FenixDisplayVerdict(false, "build unknown"), monitor.VerdictFor(FenixChannelDisplay.Sd));
        Assert.DoesNotContain(lines.All, line => line.StartsWith("build ", StringComparison.Ordinal));

        // The current connection's answer is the one that counts.
        queries.Answer(1, CheckedBuild);
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "the current build to count", lines);
        Assert.Equal(new[] { "build 3.0.0.7613: checked" }, lines.Starting("build "));
    }

    [Fact]
    public async Task A_late_build_answer_does_not_replace_the_current_one()
    {
        // The dangerous order: the current connection's build is in, and the earlier one's comes back after it.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the first connection to ask for the build", lines);
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => queries.Asked == 2, "the second connection to ask for the build", lines);
        queries.Answer(1, CheckedBuild);
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "the current build to count", lines);

        queries.Answer(0, "3.0.0.7000");
        await Task.Delay(Quiet);

        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Sd));
        Assert.DoesNotContain(lines.All, line => line.Contains("3.0.0.7000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_build_answer_after_the_channel_dropped_is_ignored()
    {
        // The build belongs to the connection that asked: once that has gone, an answer from it is no news.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the build query", lines);
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");

        queries.Answer(0, CheckedBuild);
        await Task.Delay(Quiet);

        Assert.DoesNotContain(lines.All, line => line.StartsWith("build ", StringComparison.Ordinal));
    }

    // ---- The log --------------------------------------------------------------------------------

    [Fact]
    public async Task Writes_a_verdict_change_once()
    {
        var server = new ChannelServer();
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush())));
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();
        await WaitUntil(() => lines.All.Contains("SD trusted"), "SD trusted to be logged", lines);
        await Task.Delay(20 * TickEvery);   // twenty more ticks, with nothing changed

        Assert.Single(lines.All, line => line == "SD trusted");
    }

    [Fact]
    public async Task Writes_a_verdict_when_its_trust_or_its_headline_changes()
    {
        // The headline is the reason up to its first colon. A reason that changes after it (the numbers of a
        // disagreement, the text of a refusal) is the same news; a reason with no colon is stable while it holds,
        // so each distinct one is written once.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);

        monitor.Tick();
        Assert.Equal(
            new[]
            {
                "PFD not trusted: channel not answering",
                "E/WD not trusted: channel not answering",
                "SD not trusted: channel not answering"
            },
            lines.Verdicts());
        monitor.Tick();
        Assert.Equal(3, lines.Verdicts().Length);

        // "channel not answering: refused" and "...: reset" have the headline the first line had.
        monitor.OnDataState(FenixChannelState.NotAnswering, "refused");
        monitor.Tick();
        monitor.OnDataState(FenixChannelState.NotAnswering, "reset");
        monitor.Tick();
        Assert.Equal(3, lines.Verdicts().Length);

        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the first connection to ask for the build", lines);
        monitor.Tick();
        Assert.Equal("SD not trusted: no data yet", lines.Verdicts("SD").Last());

        monitor.OnDataMessage(GoodPush());
        monitor.Tick();
        Assert.Equal("SD not trusted: build unknown", lines.Verdicts("SD").Last());

        queries.Answer(0, "3.0.0.7000");
        await WaitUntil(
            () => monitor.VerdictFor(FenixChannelDisplay.Sd).Reason == "build 3.0.0.7000 not checked", "the build", lines);
        monitor.Tick();
        monitor.Tick();
        Assert.Equal("SD not trusted: build 3.0.0.7000 not checked", lines.Verdicts("SD").Last());

        // A reconnect, and a build that is checked: Trusted changes. (What the reasons were on the way, with no
        // tick to see them, is never written.)
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => queries.Asked == 2, "the second connection to ask for the build", lines);
        queries.Answer(1, CheckedBuild);
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted", lines);
        monitor.Tick();
        monitor.Tick();
        Assert.Equal("SD trusted", lines.Verdicts("SD").Last());

        // Trusted changing is news whatever the headline was: a disconnect, with its reason.
        monitor.OnDataState(FenixChannelState.NotAnswering, "gone");
        monitor.Tick();
        Assert.Equal("SD not trusted: channel not answering: gone", lines.Verdicts("SD").Last());

        Assert.Equal(
            new[]
            {
                "SD not trusted: channel not answering",
                "SD not trusted: no data yet",
                "SD not trusted: build unknown",
                "SD not trusted: build 3.0.0.7000 not checked",
                "SD trusted",
                "SD not trusted: channel not answering: gone"
            },
            lines.Verdicts("SD"));
    }

    [Fact]
    public async Task A_disagreement_whose_numbers_keep_changing_is_written_once()
    {
        // The pair in "altitude disagrees: channel 1,000 ft, sim 5,000 ft" moves with every frame; the line is
        // written for the first and not again until the headline changes.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);
        monitor.OnComparison(FenixComparison.FdLightCapt, 1);
        monitor.OnComparison(FenixComparison.FdLightFo, 1);
        monitor.OnComparison(FenixComparison.MagHeadingDeg, 90);

        for (int step = 0; step < 4; step++)
        {
            monitor.OnComparison(FenixComparison.AltitudeFt, 5000 + 10 * step);
            monitor.OnDataMessage(GoodPush(1000 + 10 * step));
            monitor.Tick();
        }

        Assert.Equal(
            new[] { "PFD not trusted: altitude disagrees: channel 1,000 ft, sim 5,000 ft" },
            lines.Verdicts("PFD"));
    }

    [Fact]
    public void Writes_the_comparisons_every_30th_tick_while_connected()
    {
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);

        for (int tick = 0; tick < 35; tick++) monitor.Tick();
        Assert.Empty(lines.Comparisons());   // not connected: no pairs to show

        monitor.OnDataState(FenixChannelState.Connected, "");
        for (int tick = 0; tick < 29; tick++) monitor.Tick();
        Assert.Empty(lines.Comparisons());

        monitor.Tick();   // the 30th tick of the connection
        Assert.Equal(
            "comparisons: FD captain: channel missing, sim missing; FD first officer: channel missing, sim missing; "
            + "altitude: channel missing, sim missing; heading: channel missing, sim missing; "
            + "fuel: channel missing, sim missing",
            Assert.Single(lines.Comparisons()));

        for (int tick = 0; tick < 30; tick++) monitor.Tick();
        Assert.Equal(2, lines.Comparisons().Length);

        monitor.OnDataState(FenixChannelState.NotAnswering, "gone");
        for (int tick = 0; tick < 60; tick++) monitor.Tick();
        Assert.Equal(2, lines.Comparisons().Length);
    }

    [Fact]
    public void Writes_connected_with_the_first_message_not_the_headers()
    {
        // The client reports Connected when the headers arrive, which says the call opened and not that it works.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);

        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Empty(lines.Starting("display data:"));

        monitor.OnDataMessage(GoodPush());
        Assert.Equal(new[] { "display data: connected" }, lines.Starting("display data:"));

        monitor.OnDataMessage(GoodPush());
        monitor.OnDataMessage([]);   // an empty push is a message too: the call works
        Assert.Single(lines.Starting("display data:"));

        // A new connection says so again with its first message.
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        Assert.Equal(
            new[]
            {
                "display data: connected",
                "display data: not answering (the stream ended)",
                "display data: connected"
            },
            lines.Starting("display data:"));
    }

    [Fact]
    public void Writes_a_data_failure_when_its_text_changes_and_not_when_it_repeats()
    {
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);

        monitor.OnDataState(FenixChannelState.NotAnswering, "refused");
        monitor.OnDataState(FenixChannelState.NotAnswering, "refused");   // the same news
        monitor.OnDataState(FenixChannelState.Connected, "");             // a call that opens and says nothing is none
        monitor.OnDataState(FenixChannelState.NotAnswering, "refused");
        monitor.OnDataState(FenixChannelState.NotAnswering, "HTTP 503");  // a new text
        monitor.OnDataState(FenixChannelState.NotAnswering, "refused");   // differs from the one written last
        monitor.OnDataState(FenixChannelState.Refused, "the Fenix refused it");
        monitor.OnDataState(FenixChannelState.Refused, "the Fenix refused it");

        Assert.Equal(
            new[]
            {
                "display data: not answering (refused)",
                "display data: not answering (HTTP 503)",
                "display data: not answering (refused)",
                "display data: refused (the Fenix refused it); stopped for this session"
            },
            lines.Starting("display data:"));
    }

    [Fact]
    public async Task A_stream_that_opens_and_ends_with_nothing_writes_its_failure_once()
    {
        // The call opens (200) and the body ends with no message, again and again: the client reports Connected
        // and NotAnswering("the stream ended") each cycle. Neither "connected" (no message came) nor the failure
        // (the same text) is written more than the first time, or the log would grow two lines per cycle for hours.
        var server = new ChannelServer();
        for (int cycle = 0; cycle < 4; cycle++)
        {
            server.Data.Enqueue(() => FenixChannelFakeServer.Stream([], trailerStatus: 0));
        }

        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();
        await server.Data.WaitForRequestsAsync(5);   // the fifth follows the fourth stream's end

        Assert.Equal(
            new[] { "display data: not answering (the stream ended)" },
            lines.Starting("display data:"));
    }

    [Fact]
    public async Task A_stream_that_delivers_before_it_ends_writes_both_lines_each_time()
    {
        // Each cycle delivers a message first, so the call worked, and its end is news again.
        var server = new ChannelServer();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            server.Data.Enqueue(() => FenixChannelFakeServer.Stream(Frames(GoodPush()), trailerStatus: 0));
        }

        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();
        await server.Data.WaitForRequestsAsync(4);

        Assert.Equal(
            new[]
            {
                "display data: connected", "display data: not answering (the stream ended)",
                "display data: connected", "display data: not answering (the stream ended)",
                "display data: connected", "display data: not answering (the stream ended)"
            },
            lines.Starting("display data:"));
    }

    [Fact]
    public void Writes_the_ecam_entries_changes_with_the_same_rule_for_a_repeated_failure()
    {
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);

        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamState(FenixChannelState.NotAnswering, "refused");
        monitor.OnEcamState(FenixChannelState.NotAnswering, "refused");   // the same news
        monitor.OnEcamMessage(GoodEntries());                             // the call worked: the same failure is news again
        monitor.OnEcamState(FenixChannelState.NotAnswering, "refused");
        monitor.OnEcamState(FenixChannelState.Refused, "the Fenix refused it");

        Assert.Equal(
            new[]
            {
                "ECAM entries: connected",
                "ECAM entries: not answering (refused)",
                "ECAM entries: not answering (refused)",
                "ECAM entries: refused (the Fenix refused it); stopped for this session"
            },
            lines.Starting("ECAM entries:"));
    }

    // ---- Start and Dispose ----------------------------------------------------------------------

    [Fact]
    public async Task A_second_Start_does_nothing()
    {
        var server = new ChannelServer();
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();
        monitor.Start();
        await server.Data.WaitForRequestsAsync(1);
        await server.Ecam.WaitForRequestsAsync(1);
        await Task.Delay(Quiet);

        Assert.Single(server.Data.Requests);
        Assert.Single(server.Ecam.Requests);
    }

    [Fact]
    public async Task Dispose_stops_everything()
    {
        var server = new ChannelServer();
        var stream = new OpenStream();
        server.Data.Enqueue(() => stream.Response);
        var lines = new Lines();
        var monitor = NewMonitor(server, lines);
        monitor.Start();
        await stream.SendAsync(GoodPush(2000));
        await WaitUntil(() => lines.All.Contains("SD trusted"), "SD trusted to be logged", lines);
        await WaitUntil(() => lines.All.Contains("ECAM entries: connected"), "the ECAM entries call to connect", lines);
        FenixChannelFrame? latest = monitor.Latest;
        int linesBefore = lines.All.Length;

        var clock = Stopwatch.StartNew();
        monitor.Dispose();
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds < 1000, $"Dispose took {clock.ElapsedMilliseconds} ms");

        // The client let go of the stream, and what comes after it reaches nobody: a frame, then the end.
        await stream.Response.Disposed.WaitAsync(Generous);
        await stream.SendAsync(GoodPush(7777));
        stream.End();
        await Task.Delay(Quiet);

        Assert.Same(latest, monitor.Latest);
        Assert.Equal(linesBefore + 1, lines.All.Length);   // the one new line is Dispose's own: "channel stopped"
        monitor.Dispose();   // and a second Dispose does nothing
        Assert.Equal(linesBefore + 1, lines.All.Length);
    }

    [Fact]
    public async Task Dispose_writes_channel_stopped_once_as_the_last_line()
    {
        // The in-sim check that an aircraft switch or a quit ended the channel reads this line in fenix_channel.log:
        // one line, written by Dispose after everything else the monitor said. A running channel has not said it.
        var server = new ChannelServer();
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush())));
        var lines = new Lines();
        var monitor = NewMonitor(server, lines);
        monitor.Start();
        await WaitUntil(() => lines.All.Contains("SD trusted"), "SD trusted to be logged", lines);
        Assert.DoesNotContain("channel stopped", lines.All);

        monitor.Dispose();

        string[] atDispose = lines.All;
        Assert.Equal("channel stopped", atDispose[^1]);
        Assert.Single(atDispose, line => line == "channel stopped");
    }

    [Fact]
    public void A_second_Dispose_writes_nothing_more()
    {
        // An idle monitor has said nothing, so its log is exactly the stop line; another Dispose, however many,
        // adds none.
        var lines = new Lines();
        var monitor = NewIdleMonitor(lines);

        monitor.Dispose();
        Assert.Equal(new[] { "channel stopped" }, lines.All);

        monitor.Dispose();
        monitor.Dispose();
        Assert.Equal(new[] { "channel stopped" }, lines.All);
    }

    [Fact]
    public async Task Nothing_is_written_after_channel_stopped()
    {
        // Whatever still arrives after Dispose finds the log closed: the callbacks of both calls, the tick,
        // SimConnect's values, a late build answer, a late frame and the end of the stream. The monitor runs (its
        // own tick and both calls), so those have to stay quiet too.
        var server = new ChannelServer();
        var stream = new OpenStream();
        server.Data.Enqueue(() => stream.Response);
        var lines = new Lines();
        var queries = new BuildQueries();
        var monitor = NewMonitor(server, lines, queries.Read);
        monitor.Start();
        await stream.SendAsync(GoodPush());
        await WaitUntil(() => queries.Asked == 1, "the build query", lines);
        await WaitUntil(() => lines.All.Contains("display data: connected"), "the data call to connect", lines);

        monitor.Dispose();
        string[] atDispose = lines.All;

        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(9000));
        monitor.OnDataState(FenixChannelState.NotAnswering, "late");
        monitor.OnDataState(FenixChannelState.Refused, "late");
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(WrongShapedEntries());
        monitor.OnEcamState(FenixChannelState.NotAnswering, "late");
        monitor.OnComparison(FenixComparison.AltitudeFt, 1);
        monitor.Tick();
        queries.Answer(0, CheckedBuild);
        await stream.Response.Disposed.WaitAsync(Generous);
        await stream.SendAsync(GoodPush(7777));
        stream.End();
        await Task.Delay(Quiet);

        Assert.Equal("channel stopped", atDispose[^1]);
        Assert.Equal(atDispose, lines.All);
    }

    [Fact]
    public async Task Dispose_stops_the_tick()
    {
        // The first tick, 100 ms after Start, writes the three verdicts (nothing has connected). Disposing at once
        // leaves it no time to; a timer that was not stopped would write them after the count taken here.
        var lines = new Lines();
        var monitor = new FenixChannelMonitor(
            AnswersTheCheckedBuild, Checked, new ChannelServer(), lines.Write,
            TimeSpan.FromMilliseconds(100), BuildRetry, (_, _) => TimeSpan.Zero);
        monitor.Start();
        monitor.Dispose();
        int linesAtDispose = lines.All.Length;

        await Task.Delay(Quiet + Quiet);

        Assert.Equal(linesAtDispose, lines.All.Length);
    }

    [Fact]
    public async Task Callbacks_after_Dispose_are_ignored()
    {
        // The client may deliver a callback that was already under way when Dispose ran. None may reach the gate,
        // the log, the latest frame or the build query.
        var lines = new Lines();
        int asked = 0;
        using var monitor = NewIdleMonitor(lines, _ =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult<string?>(CheckedBuild);
        });
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(1000));
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);
        FenixChannelFrame? latest = monitor.Latest;
        monitor.Dispose();
        int linesAtDispose = lines.All.Length;   // Dispose's own line, "channel stopped", is in it

        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(9000));
        monitor.OnDataState(FenixChannelState.NotAnswering, "late");
        monitor.OnDataState(FenixChannelState.Refused, "late");
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(WrongShapedEntries());
        monitor.OnEcamState(FenixChannelState.NotAnswering, "late");
        monitor.OnComparison(FenixComparison.AltitudeFt, 1);
        monitor.Tick();
        await Task.Delay(Quiet);

        Assert.Same(latest, monitor.Latest);
        Assert.Equal(linesAtDispose, lines.All.Length);
        Assert.Equal(1, Volatile.Read(ref asked));   // a connection after Dispose asked for no build
    }

    [Fact]
    public async Task A_build_answer_after_Dispose_is_dropped()
    {
        var lines = new Lines();
        var queries = new BuildQueries();
        var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => queries.Asked == 1, "the build query", lines);
        monitor.Dispose();

        queries.Answer(0, CheckedBuild);
        await Task.Delay(Quiet);

        Assert.DoesNotContain(lines.All, line => line.StartsWith("build ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_disposed_monitor_trusts_nothing()
    {
        // The frames stop with the client, and the gate would only notice after 2 s: a read in between must not
        // find a trusted verdict from a monitor that is no longer running.
        var lines = new Lines();
        var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted", lines);

        monitor.Dispose();

        Assert.Equal(new FenixDisplayVerdict(false, "channel stopped"), monitor.VerdictFor(FenixChannelDisplay.Sd));
    }

    [Fact]
    public void A_monitor_built_with_only_a_build_reader_trusts_nothing()
    {
        // The defaults are the shipped ones: the real handler (no connection is made until Start), the real log
        // channel, the empty set of checked builds, a one-second tick. Never started, so no socket is opened.
        var monitor = new FenixChannelMonitor(_ => Task.FromResult<string?>(null));

        Assert.Null(monitor.Latest);
        Assert.Equal(new FenixDisplayVerdict(false, "channel not answering"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
        monitor.Dispose();
    }

    // ---- The log's numbers ----------------------------------------------------------------------

    /// <summary>
    /// Its own class, in a collection that never runs beside another test: the test sets
    /// <c>CultureInfo.DefaultThreadCurrentCulture</c>, which is process-global, for a moment. xUnit runs such a
    /// collection after every parallel one, so no other test sees de-DE. Nested, so that a run of
    /// <c>FenixChannelMonitorTests</c> includes it.
    /// </summary>
    [Collection("FenixChannelMonitorCultureState")]
    public class InvariantCultureTests
    {
        [Fact]
        public async Task Logs_the_comparisons_in_the_invariant_culture()
        {
            // The comparisons line is written on the timer's thread, so the test sets the default culture of every
            // thread as well as its own: under de-DE a number formatted with the PC's culture reads "1.020", and
            // the log must read "1,020" on every machine.
            CultureInfo savedCurrent = CultureInfo.CurrentCulture;
            CultureInfo? savedDefault = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                var german = new CultureInfo("de-DE");
                CultureInfo.CurrentCulture = german;
                CultureInfo.DefaultThreadCurrentCulture = german;

                // The setup works: a thread that inherits nothing formats under de-DE.
                string? onFreshThread = null;
                var fresh = new Thread(() => onFreshThread = 1020.ToString("N0"));
                fresh.Start();
                fresh.Join();
                Assert.Equal("1.020", onFreshThread);

                var server = new ChannelServer();
                server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush(altitude: 1023))));
                var lines = new Lines();
                using var monitor = NewMonitor(server, lines);

                monitor.Start();
                monitor.OnComparison(FenixComparison.AltitudeFt, 1020);
                await WaitUntil(() => lines.Comparisons().Length > 0, "a comparisons line", lines);

                Assert.Contains("altitude: channel 1,023 ft, sim 1,020 ft", lines.Comparisons()[0]);
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentCulture = savedDefault;
                CultureInfo.CurrentCulture = savedCurrent;
            }
        }
    }
}

/// <summary>The collection of <see cref="FenixChannelMonitorTests.InvariantCultureTests"/>: never beside another test.</summary>
[CollectionDefinition("FenixChannelMonitorCultureState", DisableParallelization = true)]
public class FenixChannelMonitorCultureStateCollection
{
}
