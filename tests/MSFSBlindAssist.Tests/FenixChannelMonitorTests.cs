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
// ECAM entries message again, then asks for the build, and a build answer belongs to its connection only; each
// connection clears the latest frame and the RemoteData message that frame was decoded from, and raises the
// connection number, and neither an empty push nor a damaged one replaces either; a snapshot gives a display's
// verdict, the latest frame, its message, the latest ECAM entries (a bare heartbeat never empties them), the clock
// and the connection number from one instant; a refusal of either call ends the channel (both calls stop, every
// display reads the refusal, and every later callback is ignored); after Dispose every callback is ignored, and
// Dispose itself writes "channel stopped" once, as the last line of the log; "display data: connected" is written
// with the first message, not the headers, and a failure that repeats is written once until a message arrives; a
// verdict is written when its Trusted or its headline (the text before the first colon) changes; a problem a
// feature reports is written once per distinct line.
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

    /// <summary>
    /// A whole display push that passes every check of the gate, at the given heading, which is how a test tells one
    /// push from another, with every display unit powered. The ISIS, which has a part of its own, says it is powered
    /// only when asked: without, the push has none and the ISIS is off.
    /// </summary>
    public static byte[] GoodPush(double heading = 90, bool isisPowered = false)
        => FenixChannelProtoWriter.GoodPush(magHeading: heading, isisPowered: isisPowered);

    /// <summary>The six displays the gate judges, in the order the log writes them.</summary>
    public static readonly FenixChannelDisplay[] AllDisplays =
    [
        FenixChannelDisplay.Pfd, FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd,
        FenixChannelDisplay.Nd, FenixChannelDisplay.Isis, FenixChannelDisplay.Dcdu
    ];

    /// <summary>An ECAM entries message whose entries arrive as a number instead of a nested message: a wrong shape.</summary>
    public static byte[] WrongShapedEntries() => new FenixChannelProtoWriter().Varint(1, 5).ToArray();

    /// <summary>
    /// A display push with its 16 FMA slots whose flight director arrives as a number instead of a nested
    /// message: a wrong shape ("110"), with data in it.
    /// </summary>
    public static byte[] WrongShapedPush(double heading = 90)
    {
        var remote = new FenixChannelProtoWriter().Varint(110, 1);
        for (int slot = 0; slot < 16; slot++)
        {
            remote.Message(111, new FenixChannelProtoWriter());
        }

        remote.Message(194, new FenixChannelProtoWriter().Double(1, heading));
        return new FenixChannelProtoWriter().Message(1, remote).ToArray();
    }

    /// <summary>An ECAM entries message that has the shape the contract gives it: just a heartbeat.</summary>
    public static byte[] GoodEntries() => new FenixChannelProtoWriter().Bool(2, true).ToArray();

    /// <summary>
    /// An ECAM entries message that lists these entries (ata, name, title, is-alert), with the heartbeat set or
    /// not: what the stream sends when its list changes.
    /// </summary>
    public static byte[] ListedEntries(bool heartbeat, params (string Ata, string Name, string Title, bool IsAlert)[] entries)
    {
        var message = new FenixChannelProtoWriter();
        foreach ((string ata, string name, string title, bool isAlert) in entries)
        {
            message.Message(1, new FenixChannelProtoWriter().String(1, ata).String(2, name).String(3, title).Bool(4, isAlert));
        }

        if (heartbeat) message.Bool(2, true);
        return message.ToArray();
    }

    /// <summary>
    /// The heading (field 194.1) of the RemoteData message of a snapshot, null when it has none: which push that message
    /// is, since every push the tests tell apart has a heading of its own.
    /// </summary>
    public static double? HeadingOf(FenixChannelPush? message)
        => message is not null
           && message.TryGet(194, out FenixChannelField part)
           && part.AsMessage() is { } heading
           && heading.TryGet(1, out FenixChannelField value)
            ? value.AsDouble()
            : null;

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
        => AllDisplays.All(display => monitor.VerdictFor(display).Trusted);

    /// <summary>Asserts every display's verdict.</summary>
    public static void AssertEveryDisplay(FenixChannelMonitor monitor, FenixDisplayVerdict expected)
    {
        foreach (FenixChannelDisplay display in AllDisplays)
        {
            Assert.Equal(expected, monitor.VerdictFor(display));
        }
    }

    /// <summary>
    /// The reason in the one refused line of <paramref name="call"/> ("display data" or "ECAM entries"),
    /// "&lt;call&gt;: refused (&lt;reason&gt;); stopped for this session".
    /// </summary>
    public static string RefusalIn(Lines lines, string call)
    {
        string prefix = call + ": refused (";
        const string Suffix = "); stopped for this session";
        string line = Assert.Single(lines.Starting(prefix));
        Assert.EndsWith(Suffix, line, StringComparison.Ordinal);
        return line[prefix.Length..^Suffix.Length];
    }

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

        /// <summary>
        /// The verdict lines ("PFD trusted", "E/WD not trusted: ..."), in order: those of the display named
        /// ("PFD", "E/WD", "SD", "ND", "ISIS" or "DCDU"), or of all six.
        /// </summary>
        public string[] Verdicts(string? display = null)
        {
            string[] names = display is null ? new[] { "PFD", "E/WD", "SD", "ND", "ISIS", "DCDU" } : new[] { display };
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
    /// An answer of either call that the test feeds by hand: <see cref="SendAsync"/> adds messages to the open
    /// stream whenever it likes and <see cref="End"/> ends it, with no trailers, as a cut connection does, or with
    /// a grpc-status in them, as the service ends a call. The fake's own streams give all their frames at once, so
    /// they cannot send one after something else happened.
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

        /// <summary>
        /// Ends the stream. With <paramref name="grpcStatus"/>, the trailers carry it, as they do behind a call's
        /// last message (16 or 7 is a refusal); without it there are none, as on a cut connection.
        /// </summary>
        public void End(int? grpcStatus = null)
        {
            // Before the end: the client reads the trailers only once the body has ended.
            if (grpcStatus is int status)
            {
                Response.TrailingHeaders.TryAddWithoutValidation(
                    "grpc-status", status.ToString(CultureInfo.InvariantCulture));
            }

            _pipe.Writer.Complete();
        }
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
    // ---- The data flow --------------------------------------------------------------------------

    [Fact]
    public async Task Feeds_decoded_frames_to_the_gate_and_keeps_the_latest()
    {
        // The live stream: frames, then nothing, and no end.
        var server = new ChannelServer();
        server.Data.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(GoodPush(100), GoodPush(200))));
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);

        monitor.Start();

        await WaitUntil(() => monitor.Latest?.MagHeading == 200, "the second frame to become the latest", lines);
        await WaitUntil(
            () => monitor.VerdictFor(FenixChannelDisplay.Sd) == new FenixDisplayVerdict(true, "trusted"),
            "SD to be trusted",
            lines);

        // Start opened both calls, each once.
        await server.Ecam.WaitForRequestsAsync(1);
        Assert.Single(server.Data.Requests);
        Assert.Single(server.Ecam.Requests);
    }

    [Fact]
    public async Task Trusts_all_six_displays_on_a_good_push()
    {
        // A feeder keeps the frames fresh, so a slow machine cannot let the stream stall before the poll sees the
        // verdicts. The push powers the ISIS too, which has a part of its own.
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

            feeder = Feed(stream, GoodPush(isisPowered: true), stop.Token);

            await WaitUntil(() => AllTrusted(monitor), "all six displays to be trusted", lines);
        }
        finally
        {
            await stop.CancelAsync();
            await feeder;
        }
    }

    [Fact]
    public async Task A_push_with_the_displays_dark_trusts_no_display()
    {
        // The display units the decoder reads reach the gate: cold and dark no powered unit shows a display, and
        // every display reads "display off" (the ISIS, which has a part of its own, because that push has none);
        // powered up, with the ISIS powered too, all six are trusted.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);

        monitor.OnDataMessage(FenixChannelProtoWriter.GoodPush(displaysOn: false));

        AssertEveryDisplay(monitor, new FenixDisplayVerdict(false, "display off"));

        monitor.OnDataMessage(GoodPush(isisPowered: true));

        AssertEveryDisplay(monitor, new FenixDisplayVerdict(true, "trusted"));
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
        monitor.OnDataMessage(GoodPush(100));
        FenixChannelFrame? first = monitor.Latest;
        Assert.NotNull(first);
        Assert.Equal(100, first.MagHeading);

        monitor.OnDataMessage([]);   // no PrimaryData
        monitor.OnDataMessage(new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()).ToArray());   // an empty one
        Assert.Same(first, monitor.Latest);

        // The next real frame does replace it.
        monitor.OnDataMessage(GoodPush(200));
        Assert.Equal(200, monitor.Latest?.MagHeading);
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

    // ---- The snapshot: a verdict and what it judged, together -----------------------------------

    [Fact]
    public async Task A_snapshot_takes_the_verdict_frame_message_and_entries_from_one_instant()
    {
        // The log is written under the monitor's lock, in the order the gate saw its events, so a log that waits
        // holds a frame's delivery inside the lock: here the connection's first frame, stopped at its "connected"
        // line, before the gate has judged it and before it is the latest. A snapshot asked meanwhile waits for the
        // lock and gets the verdict, the frame and the message that frame was decoded from as they stand after that
        // frame, with the list of ECAM entries, the clock and the connection number. A reader that took the latest
        // frame first and the verdict after it, two steps, would get the verdict on the new frame beside no frame;
        // one that read the message before it took the lock would get the new frame beside no message. (One that
        // read it after the lock would get the new message as well: only the test below, in which pushes follow
        // each other, tells that one.)
        // The pause before the release lets the snapshot reach the lock; on a slow machine it may not, and the test
        // then passes without proving anything, but it never fails.
        using var held = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int holdTheNextConnectedLine = 0;
        var lines = new Lines();
        void Log(string line)
        {
            lines.Write(line);
            if (line == "display data: connected" && Interlocked.Exchange(ref holdTheNextConnectedLine, 0) == 1)
            {
                held.Set();
                release.Wait(Generous);
            }
        }

        using var monitor = new FenixChannelMonitor(
            AnswersTheCheckedBuild, Checked, new ChannelServer(), Log, TickEvery, BuildRetry, (_, _) => TimeSpan.Zero);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(ListedEntries(false, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);

        Volatile.Write(ref holdTheNextConnectedLine, 1);
        Task delivery = Task.Run(() => monitor.OnDataMessage(GoodPush(200)));
        Assert.True(held.Wait(Generous), "the frame never reached its log line");
        Task<FenixChannelSnapshot> snapshot = Task.Run(() => monitor.Snapshot(FenixChannelDisplay.Pfd));
        await Task.Delay(Quiet);
        long beforeRelease = Environment.TickCount64;
        release.Set();
        await delivery.WaitAsync(Generous);
        FenixChannelSnapshot taken = await snapshot.WaitAsync(Generous);
        long afterSnapshot = Environment.TickCount64;

        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), taken.Verdict);
        Assert.NotNull(taken.Frame);
        Assert.Equal(200, taken.Frame.MagHeading);
        Assert.Same(monitor.Latest, taken.Frame);

        // The message is there, and it is the one push this monitor was sent. With only one, which push a message
        // belongs to cannot be told here: the test below does.
        Assert.Equal(taken.Frame.MagHeading, HeadingOf(taken.Push));

        // The list the entries stream gave, the one connection so far, and the clock the verdict was judged at.
        Assert.Equal(new[] { "AUTOFLT-AP-OFF" }, taken.Entries?.Entries.Select(entry => entry.Name));
        Assert.Equal(1, taken.Connection);
        Assert.InRange(taken.AtMs, beforeRelease, afterSnapshot);
    }

    [Fact]
    public async Task A_snapshot_never_pairs_a_frame_with_the_message_of_another_push()
    {
        // The test above holds one push inside the lock, so it can only tell that the message is there. Here pushes
        // of two headings follow each other as fast as one thread can deliver them while another takes snapshots,
        // and the message of every snapshot must be the push its frame was decoded from: the two are swapped in
        // together under the lock, and a snapshot takes them under it. A message swapped in before the lock, or in a
        // lock section after the frame's, or read by a snapshot outside the lock, gives a frame beside the message
        // of the push before or after it at some snapshot now and then, so the reader runs for half a second. It
        // asks for one snapshot with a frame in it and no more, so a slow machine proves less and never fails.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        byte[] low = GoodPush(100);
        byte[] high = GoodPush(200);
        using var stop = new CancellationTokenSource();
        long withFrame = 0;
        string? mismatch = null;

        Task writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                monitor.OnDataMessage(low);
                monitor.OnDataMessage(high);
            }
        });
        Task reader = Task.Run(() =>
        {
            try
            {
                long start = Environment.TickCount64;
                while (mismatch is null)
                {
                    long elapsed = Environment.TickCount64 - start;
                    if (elapsed >= 3000 || (elapsed >= 500 && withFrame > 0)) break;

                    FenixChannelSnapshot taken = monitor.Snapshot(FenixChannelDisplay.Sd);
                    if (taken.Frame is null)
                    {
                        if (taken.Push is not null) mismatch = "a message with no frame";
                        continue;
                    }

                    withFrame++;
                    if (HeadingOf(taken.Push) is not double heading)
                    {
                        mismatch = "a frame with no message";
                    }
                    else if (heading != taken.Frame.MagHeading)
                    {
                        mismatch = string.Create(CultureInfo.InvariantCulture,
                            $"a frame of the push at {taken.Frame.MagHeading} degrees beside the message of the push at {heading} degrees");
                    }
                }
            }
            finally
            {
                stop.Cancel();
            }
        });

        await Task.WhenAll(reader, writer).WaitAsync(Generous);

        Assert.True(mismatch is null, mismatch);   // the message in full: Assert.Null cuts a long string short
        Assert.True(withFrame > 0, "no snapshot held a frame");
    }

    [Fact]
    public void A_frame_with_a_wrong_shape_keeps_the_last_good_message()
    {
        // The message is replaced exactly when the frame is: a push the gate takes no values from (a damaged one, a
        // wrong-shaped one, an empty one) leaves the last good message beside the last good frame, never a message
        // that no frame was decoded from.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");

        monitor.OnDataMessage(GoodPush()[..20]);   // malformed: cut short
        Assert.Null(monitor.Snapshot(FenixChannelDisplay.Sd).Push);

        monitor.OnDataMessage(GoodPush(100));
        FenixChannelSnapshot good = monitor.Snapshot(FenixChannelDisplay.Sd);
        Assert.NotNull(good.Frame);
        Assert.Equal(100, HeadingOf(good.Push));

        monitor.OnDataMessage(GoodPush(200)[..20]);             // malformed
        monitor.OnDataMessage(WrongShapedPush(heading: 300));   // a wrong shape, with data in it
        monitor.OnDataMessage([]);                              // no RemoteData
        monitor.OnDataMessage(new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()).ToArray());   // an empty one

        FenixChannelSnapshot kept = monitor.Snapshot(FenixChannelDisplay.Sd);
        Assert.Same(good.Frame, kept.Frame);
        Assert.Same(good.Push, kept.Push);
        Assert.Equal(100, HeadingOf(kept.Push));

        // The next good push replaces both.
        monitor.OnDataMessage(GoodPush(40));
        FenixChannelSnapshot next = monitor.Snapshot(FenixChannelDisplay.Sd);
        Assert.Equal(40, next.Frame?.MagHeading);
        Assert.Equal(40, HeadingOf(next.Push));
    }

    [Fact]
    public void A_connect_clears_the_message()
    {
        // A message of an earlier connection is never read: it goes with the frame, at the connect.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(100));
        Assert.Equal(100, HeadingOf(monitor.Snapshot(FenixChannelDisplay.Sd).Push));

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");

        FenixChannelSnapshot afterConnect = monitor.Snapshot(FenixChannelDisplay.Sd);
        Assert.Null(afterConnect.Push);
        Assert.Null(afterConnect.Frame);

        monitor.OnDataMessage(GoodPush(200));
        Assert.Equal(200, HeadingOf(monitor.Snapshot(FenixChannelDisplay.Sd).Push));
    }

    [Fact]
    public void Each_connect_raises_the_connection_number()
    {
        // 0 before the first connect of the data call, and one more with each: an announcer that sees another
        // number takes its next good reading silently, whatever the client's wait before it reconnected.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        Assert.Equal(0, monitor.Snapshot(FenixChannelDisplay.Pfd).Connection);

        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Equal(1, monitor.Snapshot(FenixChannelDisplay.Pfd).Connection);

        // Neither a push, nor the other call connecting, nor the data call dropping is a connect.
        monitor.OnDataMessage(GoodPush());
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        Assert.Equal(1, monitor.Snapshot(FenixChannelDisplay.Pfd).Connection);

        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Equal(2, monitor.Snapshot(FenixChannelDisplay.Pfd).Connection);
        Assert.Equal(2, monitor.Snapshot(FenixChannelDisplay.Sd).Connection);   // every display's snapshot has it

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Equal(3, monitor.Snapshot(FenixChannelDisplay.Pfd).Connection);

        // After a refusal the channel is over, and a connect that is heard late is ignored like every callback.
        monitor.OnEcamState(FenixChannelState.Refused, "no key");
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.Equal(3, monitor.Snapshot(FenixChannelDisplay.Pfd).Connection);
    }

    [Fact]
    public async Task A_snapshot_gives_each_verdict_with_the_frame_it_judged()
    {
        // A frame with a text code the app does not know untrusts the E/WD, and the next one, without it, trusts it
        // again: each snapshot pairs the verdict with the frame that gave it, and with the message of that frame.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);

        monitor.OnDataMessage(FenixChannelProtoWriter.GoodPush(upperEcamLeft: ["xENG 1 FIRE"]));
        FenixChannelSnapshot doubted = monitor.Snapshot(FenixChannelDisplay.Ewd);

        Assert.Equal(new FenixDisplayVerdict(false, "unknown code 'x' in the upper ECAM"), doubted.Verdict);
        Assert.Equal(new[] { "xENG 1 FIRE" }, doubted.Frame?.UpperEcamLeft);
        Assert.NotNull(doubted.Push);
        Assert.Single(doubted.Push.All(173));   // the E/WD's left column, in the message too

        monitor.OnDataMessage(GoodPush());
        FenixChannelSnapshot trusted = monitor.Snapshot(FenixChannelDisplay.Ewd);

        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), trusted.Verdict);
        Assert.NotNull(trusted.Frame);
        Assert.Empty(trusted.Frame.UpperEcamLeft);
        Assert.NotNull(trusted.Push);
        Assert.Empty(trusted.Push.All(173));
    }

    [Fact]
    public void Each_connection_of_the_data_call_clears_the_latest_frame()
    {
        // A frame from an earlier connection is never read: after a reconnect the latest frame is the new
        // connection's first good one, and until it comes there is none.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(100));
        Assert.Equal(100, monitor.Latest?.MagHeading);

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");

        Assert.Null(monitor.Latest);
        Assert.Null(monitor.Snapshot(FenixChannelDisplay.Sd).Frame);

        monitor.OnDataMessage(GoodPush(200));
        Assert.Equal(200, monitor.Latest?.MagHeading);
    }

    [Fact]
    public void A_damaged_frame_never_becomes_the_latest()
    {
        // The gate takes no values from a malformed or wrong-shaped frame (it latches instead), and the latest frame
        // is the one a read composes, so it skips them too: as the connection's first message, and after a good one.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");

        monitor.OnDataMessage(GoodPush()[..20]);   // malformed: cut short
        Assert.Null(monitor.Latest);

        monitor.OnDataMessage(GoodPush(100));
        FenixChannelFrame? good = monitor.Latest;
        Assert.Equal(100, good?.MagHeading);

        monitor.OnDataMessage(GoodPush(200)[..20]);
        monitor.OnDataMessage(WrongShapedPush(heading: 300));
        Assert.Same(good, monitor.Latest);
    }

    // ---- The ECAM entries in the snapshot -------------------------------------------------------

    [Fact]
    public void A_bare_heartbeat_keeps_the_last_entries()
    {
        // The stream sends a heartbeat with no entries to say it is alive, and sends a list only when it changes:
        // a heartbeat is not an emptied list, or every alert would seem to clear at each one. A list with no
        // entries and no heartbeat is one that emptied.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        Assert.Null(monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);   // nothing listed yet

        monitor.OnEcamMessage(GoodEntries());   // a heartbeat before any list is no list
        Assert.Null(monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);

        monitor.OnEcamMessage(ListedEntries(false,
            ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true), ("31", "MEMO-SEAT-BELTS", "SEAT BELTS", false)));
        FenixEcamEntries? listed = monitor.Snapshot(FenixChannelDisplay.Ewd).Entries;
        Assert.NotNull(listed);
        Assert.Equal(new[] { "AUTOFLT-AP-OFF", "MEMO-SEAT-BELTS" }, listed.Entries.Select(entry => entry.Name));

        monitor.OnEcamMessage(GoodEntries());   // a bare heartbeat: the list stands
        Assert.Same(listed, monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);

        // A heartbeat that carries entries lists them.
        monitor.OnEcamMessage(ListedEntries(true, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        FenixEcamEntries? relisted = monitor.Snapshot(FenixChannelDisplay.Ewd).Entries;
        Assert.NotNull(relisted);
        Assert.Equal(new[] { "AUTOFLT-AP-OFF" }, relisted.Entries.Select(entry => entry.Name));

        monitor.OnEcamMessage(GoodEntries());
        Assert.Same(relisted, monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);

        monitor.OnEcamMessage([]);   // no entries and no heartbeat
        FenixEcamEntries? emptied = monitor.Snapshot(FenixChannelDisplay.Ewd).Entries;
        Assert.NotNull(emptied);
        Assert.Empty(emptied.Entries);

        // A message that is in the wrong shape, or damaged, is no bare heartbeat either: it replaces the list, and
        // the entries then say so (ShapesOk is false), which is why a feature looks at ShapesOk before it reads them.
        // The gate has latched the channel on such a message, so no display is trusted meanwhile.
        monitor.OnEcamMessage(WrongShapedEntries());
        FenixEcamEntries? wrongShaped = monitor.Snapshot(FenixChannelDisplay.Ewd).Entries;
        Assert.NotNull(wrongShaped);
        Assert.False(wrongShaped.ShapesOk);
        Assert.NotSame(emptied, wrongShaped);

        monitor.OnEcamMessage(ListedEntries(false, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        monitor.OnEcamMessage(new FenixChannelProtoWriter().Tag(1, 2).Raw(5, 1).ToArray());   // an entry cut short
        FenixEcamEntries? damaged = monitor.Snapshot(FenixChannelDisplay.Ewd).Entries;
        Assert.NotNull(damaged);
        Assert.True(damaged.Malformed);
        Assert.False(damaged.ShapesOk);
    }

    [Fact]
    public void A_connect_of_the_ecam_entries_call_starts_its_list_over()
    {
        // A list belongs to the connection that sent it: after a reconnect of the ECAM entries call (its stream broke,
        // or the Fenix restarted) the snapshot has no list until the new connection sends one, and the ECAM connection
        // number tells a reader to forget the list it remembered. The data call's connection number is not touched.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        Assert.Equal(0, monitor.Snapshot(FenixChannelDisplay.Ewd).EcamConnection);

        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(ListedEntries(false, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        FenixChannelSnapshot first = monitor.Snapshot(FenixChannelDisplay.Ewd);
        Assert.Equal(1, first.EcamConnection);
        Assert.NotNull(first.Entries);

        monitor.OnEcamState(FenixChannelState.NotAnswering, "no data for 15 s");
        Assert.Same(first.Entries, monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);   // a gap alone keeps it

        monitor.OnEcamState(FenixChannelState.Connected, "");
        FenixChannelSnapshot second = monitor.Snapshot(FenixChannelDisplay.Ewd);
        Assert.Equal(2, second.EcamConnection);
        Assert.Null(second.Entries);
        Assert.Equal(0, second.Connection);

        monitor.OnEcamMessage(GoodEntries());   // a bare heartbeat is still no list
        Assert.Null(monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);

        monitor.OnEcamMessage(ListedEntries(false, ("31", "MEMO-SEAT-BELTS", "SEAT BELTS", false)));
        Assert.Equal(new[] { "MEMO-SEAT-BELTS" },
            monitor.Snapshot(FenixChannelDisplay.Ewd).Entries?.Entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task A_wrong_shaped_ecam_message_of_an_ended_connection_is_not_given_to_the_gate_again()
    {
        // The gate is given the last ECAM entries message again at each connect of the data call, but only one of
        // the ECAM call's current connection: a reconnect of that call drops what the connection before had said.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted", lines);

        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(WrongShapedEntries());
        Assert.False(monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted);   // latched until the data call reconnects
        monitor.OnEcamState(FenixChannelState.NotAnswering, "no data for 15 s");
        monitor.OnEcamState(FenixChannelState.Connected, "");

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted again", lines);
    }

    [Fact]
    public void A_connect_of_the_data_call_keeps_the_entries()
    {
        // The entries stream sends only when its list changes, so what it last listed is still the list after the
        // data stream reconnects: unlike the frame and its message, the list does not go at a connect.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(ListedEntries(false, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        FenixEcamEntries? listed = monitor.Snapshot(FenixChannelDisplay.Ewd).Entries;
        Assert.NotNull(listed);

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");

        Assert.Same(listed, monitor.Snapshot(FenixChannelDisplay.Ewd).Entries);
    }

    [Fact]
    public async Task A_bare_heartbeat_still_goes_to_the_gate()
    {
        // What the gate is given again at each connect is the last ECAM entries message of any kind, as it always
        // was: only the snapshot's list skips a bare heartbeat. A wrong-shaped message latches the channel; a good
        // one after it is the last word the entries stream has said, so the next connection starts without the latch.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(() => monitor.VerdictFor(FenixChannelDisplay.Sd).Trusted, "SD to be trusted", lines);

        monitor.OnEcamMessage(WrongShapedEntries());
        monitor.OnEcamMessage(GoodEntries());   // a bare heartbeat
        Assert.Equal(   // a latch lasts until the connection ends
            new FenixDisplayVerdict(false, "ECAM entries field 1 has the wrong shape"),
            monitor.VerdictFor(FenixChannelDisplay.Sd));

        monitor.OnDataState(FenixChannelState.NotAnswering, "the stream ended");
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush());
        await WaitUntil(
            () => lines.All.Count(line => line == "build 3.0.0.7613: checked") == 2,
            "the second connection's build",
            lines);

        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Sd));
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
        // The headline is the reason up to its first colon. A reason that changes after it (the text of a failure or a
        // refusal) is the same news; a reason with no colon is stable while it holds, so each distinct one is written
        // once.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);

        monitor.Tick();
        Assert.Equal(
            new[]
            {
                "PFD not trusted: channel not answering",
                "E/WD not trusted: channel not answering",
                "SD not trusted: channel not answering",
                "ND not trusted: channel not answering",
                "ISIS not trusted: channel not answering",
                "DCDU not trusted: channel not answering"
            },
            lines.Verdicts());
        monitor.Tick();
        Assert.Equal(6, lines.Verdicts().Length);

        // "channel not answering: refused" and "...: reset" have the headline the first line had.
        monitor.OnDataState(FenixChannelState.NotAnswering, "refused");
        monitor.Tick();
        monitor.OnDataState(FenixChannelState.NotAnswering, "reset");
        monitor.Tick();
        Assert.Equal(6, lines.Verdicts().Length);

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
    public async Task The_tick_writes_six_verdicts()
    {
        // The ND, the ISIS and the DCDU are judged and logged like the PFD, the E/WD and the SD: the first tick of a
        // working channel writes a line for each, in the order of FenixChannelDisplay, and a later tick only the
        // lines of the displays whose verdict changed. The ISIS has a part of its own, so it can be powered while
        // the display units are dark and the reverse.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);

        monitor.OnDataMessage(GoodPush());   // every unit powered; the push has no ISIS part
        monitor.Tick();
        Assert.Equal(
            new[]
            {
                "PFD trusted",
                "E/WD trusted",
                "SD trusted",
                "ND trusted",
                "ISIS not trusted: display off",
                "DCDU trusted"
            },
            lines.Verdicts());
        monitor.Tick();
        Assert.Equal(6, lines.Verdicts().Length);

        monitor.OnDataMessage(GoodPush(isisPowered: true));
        monitor.Tick();
        Assert.Equal("ISIS trusted", lines.Verdicts().Last());
        Assert.Equal(7, lines.Verdicts().Length);

        // The units go dark and the ISIS stays powered: five lines, in order, and none for the ISIS.
        monitor.OnDataMessage(FenixChannelProtoWriter.GoodPush(displaysOn: false, isisPowered: true));
        monitor.Tick();
        Assert.Equal(
            new[]
            {
                "PFD not trusted: display off",
                "E/WD not trusted: display off",
                "SD not trusted: display off",
                "ND not trusted: display off",
                "DCDU not trusted: display off"
            },
            lines.Verdicts().Skip(7).ToArray());
        monitor.Tick();
        Assert.Equal(12, lines.Verdicts().Length);
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
    public void Writes_the_ecam_entries_changes_with_the_same_rules_as_the_data_call()
    {
        // As for the data call: "connected" is written with the first message of a connection, not with its headers,
        // and a failure is written once until a message shows the call worked. The client drops a silent ECAM call
        // too, so a call that opens and says nothing must not write two lines each time it is asked again.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines, new BuildQueries().Read);

        monitor.OnEcamState(FenixChannelState.Connected, "");             // opened, silent: no line
        monitor.OnEcamState(FenixChannelState.NotAnswering, "no data for 15 s");
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamState(FenixChannelState.NotAnswering, "no data for 15 s");   // the same news
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(GoodEntries());                             // the call worked: connected, and the failure is news again
        monitor.OnEcamMessage(GoodEntries());
        monitor.OnEcamState(FenixChannelState.NotAnswering, "no data for 15 s");
        monitor.OnEcamState(FenixChannelState.Refused, "the Fenix refused it");

        Assert.Equal(
            new[]
            {
                "ECAM entries: not answering (no data for 15 s)",
                "ECAM entries: connected",
                "ECAM entries: not answering (no data for 15 s)",
                "ECAM entries: refused (the Fenix refused it); stopped for this session"
            },
            lines.Starting("ECAM entries:"));
    }

    [Fact]
    public void A_feature_problem_is_logged_once()
    {
        // A feature's decoder that finds a field in the wrong shape finds it in every push, 17 times a second: the
        // log gets the line "<feature>: <problem>" once, as it gets every distinct problem of the monitor's own.
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);

        monitor.ReportFeatureProblem("PFD", "field 125.2 has the wrong shape");
        monitor.ReportFeatureProblem("PFD", "field 125.2 has the wrong shape");
        monitor.ReportFeatureProblem("PFD", "no speed limits");
        monitor.ReportFeatureProblem("ND", "field 125.2 has the wrong shape");
        monitor.ReportFeatureProblem("PFD", "field 125.2 has the wrong shape");

        Assert.Equal(
            new[]
            {
                "PFD: field 125.2 has the wrong shape",
                "PFD: no speed limits",
                "ND: field 125.2 has the wrong shape"
            },
            lines.All);
    }

    [Fact]
    public async Task A_feature_problem_reported_from_several_threads_is_logged_once()
    {
        var lines = new Lines();
        using var monitor = NewIdleMonitor(lines);
        using var start = new ManualResetEventSlim(false);

        Task[] reporters = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                monitor.ReportFeatureProblem("SD", "field 110 has the wrong shape");
            }))
            .ToArray();
        start.Set();
        await Task.WhenAll(reporters).WaitAsync(Generous);

        Assert.Equal(new[] { "SD: field 110 has the wrong shape" }, lines.All);
    }

    [Fact]
    public void A_feature_problem_after_Dispose_writes_nothing()
    {
        // Dispose's own line, "channel stopped", is the last the log ever gets.
        var lines = new Lines();
        var monitor = NewIdleMonitor(lines);
        monitor.ReportFeatureProblem("SD", "field 110 has the wrong shape");
        monitor.Dispose();
        string[] atDispose = lines.All;

        monitor.ReportFeatureProblem("SD", "no pages");
        monitor.ReportFeatureProblem("SD", "field 110 has the wrong shape");

        Assert.Equal(new[] { "SD: field 110 has the wrong shape", "channel stopped" }, atDispose);
        Assert.Equal(atDispose, lines.All);
    }

    [Fact]
    public void A_feature_problem_never_throws_when_the_log_does()
    {
        // A decoder reports from wherever it runs, and a log that cannot be written must not take it down.
        using var monitor = new FenixChannelMonitor(
            AnswersTheCheckedBuild, Checked, new ChannelServer(), _ => throw new IOException("disk full"),
            TickEvery, BuildRetry, (_, _) => TimeSpan.Zero);

        Exception? thrown = Record.Exception(() => monitor.ReportFeatureProblem("PFD", "field 110 has the wrong shape"));

        Assert.Null(thrown);
    }

    // ---- A refusal ends the channel -------------------------------------------------------------

    [Fact]
    public async Task A_refusal_of_the_data_call_stops_the_ecam_entries_call_too()
    {
        // A refusal ends the channel for the session ([FNX-21]), not only the call refused: the monitor stops the
        // client, so the other call is dropped too, neither asks again, and every display reads the refusal.
        var server = new ChannelServer();
        var data = new OpenStream();
        var ecam = new OpenStream();
        server.Data.Enqueue(() => data.Response);
        server.Ecam.Enqueue(() => ecam.Response);
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);
        monitor.Start();
        await data.SendAsync(GoodPush());
        await WaitUntil(() => lines.All.Contains("display data: connected"), "the data call to connect", lines);
        await ecam.SendAsync(GoodEntries());   // "connected" is written with the first message
        await WaitUntil(() => lines.All.Contains("ECAM entries: connected"), "the ECAM entries call to connect", lines);

        data.End(grpcStatus: 16);   // the refusal, in the trailers behind the messages

        await ecam.Response.Disposed.WaitAsync(Generous);   // the call that was not refused is dropped
        await Task.Delay(Quiet);                             // and neither call asks again
        Assert.Single(server.Data.Requests);
        Assert.Single(server.Ecam.Requests);

        string refusal = RefusalIn(lines, "display data");
        Assert.Contains("grpc-status 16", refusal);
        Assert.Empty(lines.Starting("ECAM entries: refused"));
        AssertEveryDisplay(monitor, new FenixDisplayVerdict(false, $"channel not answering: {refusal}"));
    }

    [Fact]
    public async Task A_refusal_of_the_ecam_entries_call_stops_the_data_call_too()
    {
        // The other way round: no verdict depends on the ECAM entries call, but its refusal is the Fenix saying no
        // to the client, and the channel ends with it, the streaming data call included.
        var server = new ChannelServer();
        var data = new OpenStream();
        var ecam = new OpenStream();
        server.Data.Enqueue(() => data.Response);
        server.Ecam.Enqueue(() => ecam.Response);
        var lines = new Lines();
        using var monitor = NewMonitor(server, lines);
        monitor.Start();
        await data.SendAsync(GoodPush());
        await WaitUntil(() => lines.All.Contains("display data: connected"), "the data call to connect", lines);
        await ecam.SendAsync(GoodEntries());   // "connected" is written with the first message
        await WaitUntil(() => lines.All.Contains("ECAM entries: connected"), "the ECAM entries call to connect", lines);

        ecam.End(grpcStatus: 7);

        await data.Response.Disposed.WaitAsync(Generous);
        await Task.Delay(Quiet);
        Assert.Single(server.Data.Requests);
        Assert.Single(server.Ecam.Requests);

        string refusal = RefusalIn(lines, "ECAM entries");
        Assert.Contains("grpc-status 7", refusal);
        Assert.Empty(lines.Starting("display data: refused"));
        AssertEveryDisplay(monitor, new FenixDisplayVerdict(false, $"channel not answering: {refusal}"));
    }

    [Fact]
    public async Task After_a_refusal_every_callback_of_the_client_is_ignored()
    {
        // A callback already under way when the client stopped may still arrive: none may reach the gate, the
        // log, the latest frame and its message, the list of ECAM entries, the connection number or the build query.
        // A late "connected" would otherwise start the gate fresh.
        var lines = new Lines();
        var queries = new BuildQueries();
        using var monitor = NewIdleMonitor(lines, queries.Read);
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(100));
        monitor.OnEcamMessage(ListedEntries(false, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        await WaitUntil(() => queries.Asked == 1, "the build query", lines);
        FenixChannelSnapshot beforeRefusal = monitor.Snapshot(FenixChannelDisplay.Pfd);
        Assert.NotNull(beforeRefusal.Frame);
        Assert.NotNull(beforeRefusal.Push);
        Assert.NotNull(beforeRefusal.Entries);

        monitor.OnEcamState(FenixChannelState.Refused, "no key");

        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(200));
        monitor.OnDataState(FenixChannelState.NotAnswering, "late");
        monitor.OnDataState(FenixChannelState.Refused, "again");
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(WrongShapedEntries());
        monitor.OnEcamState(FenixChannelState.Refused, "again");
        queries.Answer(0, CheckedBuild);   // the build loop of the connection the refusal ended
        await Task.Delay(Quiet);

        Assert.Equal(
            new[]
            {
                "display data: connected", "ECAM entries: connected",
                "ECAM entries: refused (no key); stopped for this session"
            },
            lines.All);
        AssertEveryDisplay(monitor, new FenixDisplayVerdict(false, "channel not answering: no key"));
        Assert.Equal(100, monitor.Latest?.MagHeading);

        // What a snapshot hands out beside that verdict stays as the refusal found it.
        FenixChannelSnapshot afterRefusal = monitor.Snapshot(FenixChannelDisplay.Pfd);
        Assert.Same(beforeRefusal.Frame, afterRefusal.Frame);
        Assert.Same(beforeRefusal.Push, afterRefusal.Push);
        Assert.Same(beforeRefusal.Entries, afterRefusal.Entries);
        Assert.Equal(1, afterRefusal.Connection);
        Assert.Equal(1, queries.Asked);
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
        var ecam = new OpenStream();
        server.Data.Enqueue(() => stream.Response);
        server.Ecam.Enqueue(() => ecam.Response);
        var lines = new Lines();
        var monitor = NewMonitor(server, lines);
        monitor.Start();
        await stream.SendAsync(GoodPush(200));
        await ecam.SendAsync(GoodEntries());   // "connected" is written with the first message
        // The DCDU is the last display a tick writes: its line shows that the tick which wrote the others is over, so
        // the count taken below is of a settled log.
        await WaitUntil(() => lines.All.Contains("DCDU trusted"), "DCDU trusted to be logged", lines);
        await WaitUntil(() => lines.All.Contains("ECAM entries: connected"), "the ECAM entries call to connect", lines);
        FenixChannelFrame? latest = monitor.Latest;
        int linesBefore = lines.All.Length;

        var clock = Stopwatch.StartNew();
        monitor.Dispose();
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds < 1000, $"Dispose took {clock.ElapsedMilliseconds} ms");

        // The client let go of the stream, and what comes after it reaches nobody: a frame, then the end.
        await stream.Response.Disposed.WaitAsync(Generous);
        await stream.SendAsync(GoodPush(77));
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
        // The DCDU is the last display a tick writes: its line shows that the tick which wrote the others is over.
        await WaitUntil(() => lines.All.Contains("DCDU trusted"), "DCDU trusted to be logged", lines);
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
        // Whatever still arrives after Dispose finds the log closed: the callbacks of both calls, the tick, a late
        // build answer, a late frame and the end of the stream. The monitor runs (its own tick and both calls), so
        // those have to stay quiet too.
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
        monitor.OnDataMessage(GoodPush(350));
        monitor.OnDataState(FenixChannelState.NotAnswering, "late");
        monitor.OnDataState(FenixChannelState.Refused, "late");
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(WrongShapedEntries());
        monitor.OnEcamState(FenixChannelState.NotAnswering, "late");
        monitor.Tick();
        queries.Answer(0, CheckedBuild);
        await stream.Response.Disposed.WaitAsync(Generous);
        await stream.SendAsync(GoodPush(77));
        stream.End();
        await Task.Delay(Quiet);

        Assert.Equal("channel stopped", atDispose[^1]);
        Assert.Equal(atDispose, lines.All);
    }

    [Fact]
    public async Task Dispose_stops_the_tick()
    {
        // The first tick, 100 ms after Start, writes the six verdicts (nothing has connected). Disposing at once
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
        // the log, the latest frame and its message, the list of ECAM entries, the connection number or the build
        // query.
        var lines = new Lines();
        int asked = 0;
        using var monitor = NewIdleMonitor(lines, _ =>
        {
            Interlocked.Increment(ref asked);
            return Task.FromResult<string?>(CheckedBuild);
        });
        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(100));
        monitor.OnEcamMessage(ListedEntries(false, ("22", "AUTOFLT-AP-OFF", "AUTO FLT AP OFF", true)));
        await WaitUntil(() => lines.All.Contains("build 3.0.0.7613: checked"), "the build", lines);
        FenixChannelFrame? latest = monitor.Latest;
        FenixChannelSnapshot beforeDispose = monitor.Snapshot(FenixChannelDisplay.Pfd);
        Assert.NotNull(beforeDispose.Push);
        Assert.NotNull(beforeDispose.Entries);
        monitor.Dispose();
        int linesAtDispose = lines.All.Length;   // Dispose's own line, "channel stopped", is in it

        monitor.OnDataState(FenixChannelState.Connected, "");
        monitor.OnDataMessage(GoodPush(350));
        monitor.OnDataState(FenixChannelState.NotAnswering, "late");
        monitor.OnDataState(FenixChannelState.Refused, "late");
        monitor.OnEcamState(FenixChannelState.Connected, "");
        monitor.OnEcamMessage(WrongShapedEntries());
        monitor.OnEcamState(FenixChannelState.NotAnswering, "late");
        monitor.Tick();
        await Task.Delay(Quiet);

        Assert.Same(latest, monitor.Latest);

        // A stopped monitor still hands out what it had, beside a verdict that trusts nothing, and none of it moved.
        FenixChannelSnapshot afterDispose = monitor.Snapshot(FenixChannelDisplay.Pfd);
        Assert.Equal(new FenixDisplayVerdict(false, "channel stopped"), afterDispose.Verdict);
        Assert.Same(beforeDispose.Frame, afterDispose.Frame);
        Assert.Same(beforeDispose.Push, afterDispose.Push);
        Assert.Same(beforeDispose.Entries, afterDispose.Entries);
        Assert.Equal(1, afterDispose.Connection);   // a connect after Dispose is not counted

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
        // channel, the shipped set of checked builds, a one-second tick. Never started, so no socket is opened, and
        // a monitor that is not connected trusts nothing whatever the set holds.
        var monitor = new FenixChannelMonitor(_ => Task.FromResult<string?>(null));

        Assert.Null(monitor.Latest);
        Assert.Equal(new FenixDisplayVerdict(false, "channel not answering"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
        monitor.Dispose();
    }
}
