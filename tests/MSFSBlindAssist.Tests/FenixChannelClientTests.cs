// Characterization tests for FenixChannelClient, the client of the Fenix display channel (the local gRPC
// service on 127.0.0.1:18083) that the display channel monitor owns. The network is a fake HttpMessageHandler
// (FenixChannelFakeServer): no test opens a socket or talks to the real port.
//
// The contract the monitor builds on: the client sends one kind of request, to the path of the call it was
// started for, and hands each message to its callback in order; it reports a state once, not again until it
// changes; a failure, a broken stream or an end reconnects quietly and nothing throws at the caller, with a
// wait that grows until a call delivers a message; a refusal (grpc-status 7 or 16, or HTTP 401 or 403) ends
// the call for good; no redirect is followed; and Dispose drops the stream at once.
//
// Timing: every wait for something to happen is a signal (a TaskCompletionSource) with a 5 s timeout. The
// two short pauses (Quiet) only prove that nothing MORE happens, so a slow machine can make them pass
// trivially but never fail. The wait before a reconnect is zero in every test; the real schedule is pinned
// by its own test.

using System.Diagnostics;
using System.Net;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelClientTests
{
    private const string DisplayDataPath = "/ProsimRemoting.GrpcAircraftDataSource/Subscribe";
    private const string EcamEntriesPath = "/ProsimRemoting.GrpcSdk/SubscribeEcamEntries";

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

    // ---- Helpers --------------------------------------------------------------------------------

    // A client on the fake server with no wait before a reconnect, and the silence limit of the data call
    // shortened when a test needs it to run out.
    private static FenixChannelClient NewClient(
        FenixChannelFakeServer server, Func<int, bool, TimeSpan>? retryDelay = null, TimeSpan? silence = null,
        TimeSpan? ecamSilence = null)
        => new(server, retryDelay ?? ((_, _) => TimeSpan.Zero), silence, ecamSilence);

    // Messages as gRPC frames, back to back.
    private static byte[] Frames(params byte[][] messages)
        => messages.SelectMany(message => FenixChannelProtoWriter.Frame(message)).ToArray();

    // What the client handed to its callbacks, from a pool thread, in order.
    private sealed class Events
    {
        private readonly object _lock = new();
        private readonly List<byte[]> _messages = new();
        private readonly List<(FenixChannelState State, string Text)> _states = new();
        private readonly List<string> _timeline = new();
        private readonly List<(Func<bool> Reached, TaskCompletionSource Signal, string What)> _waiters = new();

        public Action<byte[]> OnMessage => message => Record(() =>
        {
            _messages.Add(message);
            _timeline.Add("message");
        });

        public Action<FenixChannelState, string> OnState => (state, text) => Record(() =>
        {
            _states.Add((state, text));
            _timeline.Add(state.ToString());
        });

        public byte[][] Messages
        {
            get { lock (_lock) return _messages.ToArray(); }
        }

        public (FenixChannelState State, string Text)[] States
        {
            get { lock (_lock) return _states.ToArray(); }
        }

        // "Connected", "NotAnswering", "Refused" and "message", in the order they happened.
        public string[] Timeline
        {
            get { lock (_lock) return _timeline.ToArray(); }
        }

        public Task WhenMessages(int count) => When(() => _messages.Count >= count, $"{count} message(s)");

        public Task WhenStates(int count) => When(() => _states.Count >= count, $"{count} state(s)");

        private void Record(Action add)
        {
            var ready = new List<TaskCompletionSource>();
            lock (_lock)
            {
                add();
                for (int i = _waiters.Count - 1; i >= 0; i--)
                {
                    if (!_waiters[i].Reached()) continue;
                    ready.Add(_waiters[i].Signal);
                    _waiters.RemoveAt(i);
                }
            }

            foreach (var signal in ready) signal.TrySetResult();
        }

        private async Task When(Func<bool> reached, string what)
        {
            TaskCompletionSource signal;
            lock (_lock)
            {
                if (reached()) return;
                signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((reached, signal, what));
            }

            try
            {
                await signal.Task.WaitAsync(Generous);
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException($"Timed out after 5 s waiting for {what}. Seen: {Describe()}");
            }
        }

        private string Describe()
        {
            lock (_lock)
            {
                return $"states [{string.Join("; ", _states.Select(s => $"{s.State}: {s.Text}"))}], {_messages.Count} message(s)";
            }
        }
    }

    // The wait before each reconnect, as the client asked for it: the retry delay function a test hands the
    // client, which records its arguments and answers zero.
    private sealed class DelayLog
    {
        private readonly object _lock = new();
        private readonly List<(int Attempt, bool CallNotFound)> _asked = new();

        public Func<int, bool, TimeSpan> Delay => (attempt, callNotFound) =>
        {
            lock (_lock) _asked.Add((attempt, callNotFound));
            return TimeSpan.Zero;
        };

        public (int Attempt, bool CallNotFound)[] Asked
        {
            get { lock (_lock) return _asked.ToArray(); }
        }
    }

    // ---- The request ----------------------------------------------------------------------------

    [Fact]
    public async Task Sends_the_exact_grpc_request()
    {
        var server = new FenixChannelFakeServer();
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await server.WaitForRequestsAsync(1);

        var request = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://127.0.0.1:18083" + DisplayDataPath, request.Uri);
        Assert.Equal(new Version(2, 0), request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
        Assert.Equal("application/grpc", request.ContentType);
        Assert.Equal("trailers", request.Te);
        Assert.Equal("identity", request.GrpcAcceptEncoding);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0 }, request.Body);
    }

    [Fact]
    public async Task Ecam_entries_use_their_own_path()
    {
        var server = new FenixChannelFakeServer();
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.EcamEntries, events.OnMessage, events.OnState);
        await server.WaitForRequestsAsync(1);

        var request = Assert.Single(server.Requests);
        Assert.Equal("http://127.0.0.1:18083" + EcamEntriesPath, request.Uri);
    }

    [Fact]
    public void Start_refuses_a_value_that_is_not_one_of_the_two_calls()
    {
        // There is no path for any other value, and no API takes one: a cast number fails at the call site.
        var server = new FenixChannelFakeServer();
        using var client = NewClient(server);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => client.Start((FenixChannelCall)2, _ => { }, (_, _) => { }));
    }

    // ---- Messages -------------------------------------------------------------------------------

    [Fact]
    public async Task Delivers_messages_in_order()
    {
        byte[] a = [1, 2, 3];
        byte[] b = [4, 5];
        byte[] c = [6];
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames(a, b, c)));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);   // Connected, then the end of the stream: every message came before it

        Assert.Equal(new[] { a, b, c }, events.Messages);
        Assert.Equal(["Connected", "message", "message", "message", "NotAnswering"], events.Timeline.Take(5));
    }

    [Fact]
    public async Task Messages_cut_across_reads_arrive_whole()
    {
        // Each read gives three bytes at most, so every message straddles reads, as a 9 KB push does.
        byte[] a = [1, 2, 3, 4, 5, 6, 7];
        byte[] b = [];
        byte[] c = [8, 9, 10, 11];
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames(a, b, c), maxChunk: 3));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);

        Assert.Equal(new[] { a, b, c }, events.Messages);
    }

    [Fact]
    public async Task A_stream_that_delivers_and_stays_open_stays_connected()
    {
        // StreamThenHold is the live stream: messages, then nothing, and no end.
        byte[] a = [1, 2];
        byte[] b = [3];
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(a, b)));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenMessages(2);
        await Task.Delay(Quiet);

        Assert.Equal(new[] { a, b }, events.Messages);
        Assert.Equal([FenixChannelState.Connected], events.States.Select(s => s.State));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task A_callback_that_throws_does_not_stop_the_stream()
    {
        byte[] first = [1];
        byte[] second = [2];
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(first, second)));
        var secondArrived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var client = NewClient(server);

        client.Start(
            FenixChannelCall.DisplayData,
            message =>
            {
                if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("the message callback failed");
                secondArrived.TrySetResult(message);
            },
            (_, _) => throw new InvalidOperationException("the state callback failed"));

        Assert.Equal(second, await secondArrived.Task.WaitAsync(Generous));
    }

    // ---- The service says no --------------------------------------------------------------------

    [Fact]
    public async Task Status_12_is_reported_and_delivers_nothing()
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.TrailersOnly(12, "Method not found"));
        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(1);
        await server.WaitForRequestsAsync(2);   // it asks again

        var (state, text) = events.States[0];
        Assert.Equal(FenixChannelState.NotAnswering, state);
        Assert.Equal($"the Fenix does not offer {DisplayDataPath} (did an update change the channel?)", text);
        Assert.Empty(events.Messages);
        // The wait before asking again is the long one for a call the Fenix does not offer.
        Assert.Equal((0, true), delays.Asked[0]);
    }

    [Fact]
    public async Task Status_16_stops_the_channel()
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.TrailersOnly(16, "Unauthenticated"));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(1);
        await Task.Delay(Quiet);

        var (state, text) = events.States[0];
        Assert.Equal(FenixChannelState.Refused, state);
        Assert.Contains("grpc-status 16", text);
        Assert.Contains("Unauthenticated", text);
        Assert.Single(events.States);
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    public async Task An_http_refusal_stops_the_channel(int code, string name)
    {
        // gRPC maps HTTP 401 and 403 to grpc-status 16 and 7: the Fenix saying no is a refusal however it says it.
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => new HttpResponseMessage((HttpStatusCode)code));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(1);
        await Task.Delay(Quiet);

        var (state, text) = events.States[0];
        Assert.Equal(FenixChannelState.Refused, state);
        Assert.Contains($"HTTP {code}", text);
        Assert.Contains(name, text);
        Assert.Single(events.States);
        Assert.Single(server.Requests);
    }

    [Theory]
    [InlineData(16, "grpc-status 16")]
    [InlineData(7, "grpc-status 7")]
    public async Task A_refusal_after_the_messages_ends_the_call_too(int status, string expectedText)
    {
        // The status can also come in the trailers, behind the messages. Both refusals are classified by
        // the same code (Status_16_stops_the_channel pins that it stops for good), so this checks the reading.
        byte[] a = [1];
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames(a), trailerStatus: status));
        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);

        var states = events.States;
        Assert.Equal(FenixChannelState.Connected, states[0].State);
        Assert.Equal(FenixChannelState.Refused, states[1].State);
        Assert.Contains(expectedText, states[1].Text);
        Assert.Equal(new[] { a }, events.Messages);
        Assert.Empty(delays.Asked);
    }

    // ---- Failures reconnect quietly -------------------------------------------------------------

    [Fact]
    public async Task A_refused_connection_is_retried_quietly()
    {
        byte[] message = [1, 2, 3];
        var server = new FenixChannelFakeServer();
        server.Throw(new HttpRequestException("No connection could be made because the target machine actively refused it."));
        server.Throw(new HttpRequestException("No connection could be made because the target machine actively refused it."));
        server.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(message)));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenMessages(1);

        // Two refusals, one report: the same news is not repeated.
        var states = events.States;
        Assert.Equal([FenixChannelState.NotAnswering, FenixChannelState.Connected], states.Select(s => s.State));
        Assert.StartsWith("HttpRequestException: No connection could be made", states[0].Text);
        Assert.Equal("", states[1].Text);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task A_new_reason_is_reported_again()
    {
        var server = new FenixChannelFakeServer();
        server.Throw(new HttpRequestException("refused"));
        server.Throw(new HttpRequestException("refused"));
        server.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(3);   // the third request is answered by an endless stream: Connected

        Assert.Equal(
            [
                (FenixChannelState.NotAnswering, "HttpRequestException: refused"),
                (FenixChannelState.NotAnswering, "HTTP 503"),
                (FenixChannelState.Connected, "")
            ],
            events.States.Take(3));
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_reported_as_an_http_status()
    {
        // Following a redirect could open a path no FenixChannelCall names. The client asks again, to its own
        // path, and the 3xx answer reads like any other status that is not 200.
        var server = new FenixChannelFakeServer();
        server.Enqueue(() =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            redirect.Headers.Location = new Uri("/Other.Service/Method", UriKind.Relative);
            return redirect;
        });
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);   // the 307, then Connected on the second request

        Assert.Equal((FenixChannelState.NotAnswering, "HTTP 307"), events.States[0]);
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request => Assert.Equal("http://127.0.0.1:18083" + DisplayDataPath, request.Uri));
    }

    [Fact]
    public async Task Another_grpc_status_is_reported_and_retried()
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.TrailersOnly(14, "unavailable"));
        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);

        // The status alone, not its message: the same status reads the same each time it comes.
        Assert.Equal((FenixChannelState.NotAnswering, "grpc-status 14"), events.States[0]);
        Assert.Equal(FenixChannelState.Connected, events.States[1].State);
        Assert.Equal((0, false), delays.Asked[0]);
    }

    [Fact]
    public async Task The_attempt_count_goes_up_with_each_failure_and_starts_over_after_a_message()
    {
        var server = new FenixChannelFakeServer();
        server.Throw(new HttpRequestException("refused"));
        server.Throw(new HttpRequestException("refused"));
        server.Throw(new HttpRequestException("refused"));
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames([1])));   // connects, delivers a message, ends
        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await server.WaitForRequestsAsync(5);   // the fifth follows the stream that ended

        Assert.Equal(new[] { (0, false), (1, false), (2, false), (0, false) }, delays.Asked);
    }

    [Fact]
    public async Task A_stream_that_ends_without_a_message_does_not_reset_the_backoff()
    {
        // The answer is 200 and the body ends with no message, again and again: the call opens but does not
        // work, so each wait is longer than the last, as after a refused connection.
        var server = new FenixChannelFakeServer();
        for (int i = 0; i < 4; i++)
        {
            server.Enqueue(() => FenixChannelFakeServer.Stream([], trailerStatus: 0));
        }

        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await server.WaitForRequestsAsync(5);   // the fifth follows the fourth empty stream

        Assert.Equal(new[] { (0, false), (1, false), (2, false), (3, false) }, delays.Asked);
    }

    [Fact]
    public async Task A_stream_that_delivers_a_message_resets_the_backoff()
    {
        // Two empty streams (the count goes up to 2), then one that delivers a message (the count starts over
        // at 0), then another empty one (1 again).
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream([], trailerStatus: 0));
        server.Enqueue(() => FenixChannelFakeServer.Stream([], trailerStatus: 0));
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames([1]), trailerStatus: 0));
        server.Enqueue(() => FenixChannelFakeServer.Stream([], trailerStatus: 0));
        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await server.WaitForRequestsAsync(5);

        Assert.Equal(new[] { (0, false), (1, false), (0, false), (1, false) }, delays.Asked);
    }

    [Fact]
    public async Task A_stream_that_ends_normally_reconnects()
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames([1, 2, 3]), trailerStatus: 0));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(3);

        Assert.Equal(
            [
                (FenixChannelState.Connected, ""),
                (FenixChannelState.NotAnswering, "the stream ended"),
                (FenixChannelState.Connected, "")
            ],
            events.States.Take(3));
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_stream_that_ends_with_no_status_is_reported_as_such()
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames([1]), trailerStatus: null));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);

        Assert.Equal((FenixChannelState.NotAnswering, "the stream ended with no grpc-status"), events.States[1]);
    }

    [Theory]
    [InlineData(14, "grpc-status 14")]
    [InlineData(12, "does not offer")]
    public async Task A_finished_stream_is_told_by_the_status_in_its_trailers(int status, string expectedText)
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(Frames([1]), trailerStatus: status));
        var events = new Events();
        var delays = new DelayLog();
        using var client = NewClient(server, delays.Delay);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);
        await server.WaitForRequestsAsync(2);

        Assert.Equal(FenixChannelState.NotAnswering, events.States[1].State);
        Assert.Contains(expectedText, events.States[1].Text);
        Assert.Equal((0, status == 12), delays.Asked[0]);
    }

    [Fact]
    public async Task A_compressed_message_drops_the_stream_and_reconnects()
    {
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(FenixChannelProtoWriter.Frame([1, 2, 3], flag: 1)));
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(3);

        Assert.Empty(events.Messages);
        Assert.Equal(
            [FenixChannelState.Connected, FenixChannelState.NotAnswering, FenixChannelState.Connected],
            events.States.Take(3).Select(s => s.State));
        Assert.Contains("compressed", events.States[1].Text);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_bad_header_drops_the_stream_without_waiting_for_its_end()
    {
        // The stream stays open behind the bad header, and never ends: the client has to act on the error as
        // soon as it is found, not when the body ends. The message ahead of the bad header still arrives.
        byte[] good = [1, 2, 3];
        var server = new FenixChannelFakeServer();
        var open = FenixChannelFakeServer.StreamThenHold(
            [.. FenixChannelProtoWriter.Frame(good), .. FenixChannelProtoWriter.Frame([4, 5], flag: 1)]);
        server.Enqueue(() => open);
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(3);   // Connected, the error, and Connected again on the second request
        await open.Disposed.WaitAsync(Generous);

        Assert.Equal(new[] { good }, events.Messages);
        Assert.Equal(FenixChannelState.NotAnswering, events.States[1].State);
        Assert.Contains("compressed", events.States[1].Text);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_cut_off_message_drops_the_stream_and_reconnects()
    {
        byte[] frame = FenixChannelProtoWriter.Frame([1, 2, 3, 4, 5]);
        var server = new FenixChannelFakeServer();
        server.Enqueue(() => FenixChannelFakeServer.Stream(frame[..^2]));   // the stream ends two bytes short
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(3);

        Assert.Empty(events.Messages);
        Assert.Equal((FenixChannelState.NotAnswering, "cut-off message"), events.States[1]);
        Assert.Equal(2, server.Requests.Count);
    }

    // ---- Silence --------------------------------------------------------------------------------

    [Fact]
    public async Task A_silent_data_stream_is_dropped_and_reconnects()
    {
        // Every answer is an endless stream that never carries a byte. The silence limit is 100 ms here.
        var server = new FenixChannelFakeServer();
        var events = new Events();
        using var client = NewClient(server, silence: TimeSpan.FromMilliseconds(100));

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(3);

        Assert.Equal(
            [
                (FenixChannelState.Connected, ""),
                (FenixChannelState.NotAnswering, "no data for 0.1 s"),
                (FenixChannelState.Connected, "")
            ],
            events.States.Take(3));
        Assert.True(server.Requests.Count >= 2);
    }

    [Fact]
    public async Task Each_call_has_its_own_silence_limit()
    {
        // The ECAM entries come when the list changes and a heartbeat every 5 s, so its limit is longer (15 s by
        // default). Both calls get an endless stream; once the data call has dropped two (at least 200 ms of silence),
        // the ECAM call is still connected, and nothing else has been reported for it.
        var server = new FenixChannelFakeServer();
        var data = new Events();
        var ecam = new Events();
        using var client = NewClient(server, silence: TimeSpan.FromMilliseconds(100));

        client.Start(FenixChannelCall.DisplayData, data.OnMessage, data.OnState);
        client.Start(FenixChannelCall.EcamEntries, ecam.OnMessage, ecam.OnState);
        await ecam.WhenStates(1);
        await data.WhenStates(4);

        Assert.Equal([FenixChannelState.Connected], ecam.States.Select(s => s.State));
    }

    [Fact]
    public async Task A_silent_ecam_stream_is_dropped_and_reconnects()
    {
        // A stream that died without ending would leave the last list standing for good: past its own limit (100 ms
        // here) the ECAM call is dropped and asked again, as the data call is.
        var server = new FenixChannelFakeServer();
        var events = new Events();
        using var client = NewClient(server, ecamSilence: TimeSpan.FromMilliseconds(100));

        client.Start(FenixChannelCall.EcamEntries, events.OnMessage, events.OnState);
        await events.WhenStates(3);

        Assert.Equal(
            [
                (FenixChannelState.Connected, ""),
                (FenixChannelState.NotAnswering, "no data for 0.1 s"),
                (FenixChannelState.Connected, "")
            ],
            events.States.Take(3));
        Assert.True(server.Requests.Count >= 2);
    }

    [Fact]
    public async Task A_call_that_never_answers_is_dropped_and_reconnects()
    {
        // The Fenix takes the connection and never sends the answer's headers. Past the call's silence limit (100 ms
        // here) the client gives up on it and asks again; the next answer streams as usual.
        byte[] a = [1, 2];
        var server = new FenixChannelFakeServer();
        server.HoldAnswer();
        server.Enqueue(() => FenixChannelFakeServer.StreamThenHold(Frames(a)));
        var events = new Events();
        using var client = NewClient(server, silence: TimeSpan.FromMilliseconds(100));

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(2);
        await events.WhenMessages(1);

        Assert.Equal(
            [
                (FenixChannelState.NotAnswering, "no answer for 0.1 s"),
                (FenixChannelState.Connected, "")
            ],
            events.States.Take(2));
        Assert.Equal(new[] { a }, events.Messages.Take(1));
    }

    [Fact]
    public async Task A_stream_that_stays_silent_backs_off()
    {
        // Every answer is an endless stream that never carries a byte, so each is dropped for silence. A call
        // that has not delivered a message has not shown it works: the wait grows (3, 6, 12, then 30 s), not
        // the same 3 s each time.
        var server = new FenixChannelFakeServer();
        var delays = new DelayLog();
        var events = new Events();
        using var client = NewClient(server, delays.Delay, silence: TimeSpan.FromMilliseconds(100));

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await server.WaitForRequestsAsync(5);

        Assert.Equal(new[] { (0, false), (1, false), (2, false), (3, false) }, delays.Asked.Take(4));
    }

    // ---- The schedule ---------------------------------------------------------------------------

    [Fact]
    public void The_schedule_constants_are_the_ones_the_design_gives()
    {
        Assert.Equal(new[] { 3000, 6000, 12000, 30000 }, FenixChannelClient.ReconnectDelaysMs);
        Assert.Equal(30000, FenixChannelClient.CallNotFoundDelayMs);
        Assert.Equal(TimeSpan.FromSeconds(10), FenixChannelClient.DisplayDataSilence);
        Assert.Equal(TimeSpan.FromSeconds(15), FenixChannelClient.EcamEntriesSilence);
    }

    [Fact]
    public void The_default_handler_follows_no_redirect()
    {
        // The tests give the client a fake, so the handler it builds for itself is checked here. It follows no
        // redirect: one could open a path no FenixChannelCall names, and the client opens only those calls.
        using var handler = FenixChannelClient.CreateDefaultHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
    }

    [Theory]
    [InlineData(0, false, 3000)]
    [InlineData(1, false, 6000)]
    [InlineData(2, false, 12000)]
    [InlineData(3, false, 30000)]
    [InlineData(4, false, 30000)]
    [InlineData(1000, false, 30000)]
    [InlineData(0, true, 30000)]
    [InlineData(2, true, 30000)]
    public void The_wait_before_a_reconnect_follows_the_schedule(int attempt, bool callNotFound, int expectedMs)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMs),
            FenixChannelClient.DefaultRetryDelay(attempt, callNotFound));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_reasons_do_not_depend_on_the_pc_culture(string culture)
    {
        // The silence is written in seconds, "0.1" here: formatted with a comma-decimal culture it reads "0,1".
        // The loop starts under the culture and inherits it, as the app's does under the pilot's PC culture.
        var server = new FenixChannelFakeServer();
        var events = new Events();
        using var client = NewClient(server, silence: TimeSpan.FromMilliseconds(100));

        TestCulture.Run(culture, () =>
        {
            client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
            return 0;
        });
        await events.WhenStates(2);

        Assert.Equal("no data for 0.1 s", events.States[1].Text);
    }

    // ---- Dispose --------------------------------------------------------------------------------

    [Fact]
    public async Task Dispose_returns_promptly_while_a_stream_is_open()
    {
        var server = new FenixChannelFakeServer();
        var open = FenixChannelFakeServer.Endless();
        server.Enqueue(() => open);
        var events = new Events();
        using var client = NewClient(server);

        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(1);   // Connected: the client is reading the open stream

        var clock = Stopwatch.StartNew();
        client.Dispose();
        clock.Stop();

        Assert.True(clock.ElapsedMilliseconds < 1000, $"Dispose took {clock.ElapsedMilliseconds} ms");
        // The stream was dropped at once, not left open, and nothing was reported for it.
        await open.Disposed.WaitAsync(Generous);
        Assert.Single(server.Requests);
        Assert.Equal([FenixChannelState.Connected], events.States.Select(s => s.State));
    }

    [Fact]
    public async Task Dispose_twice_does_nothing()
    {
        var server = new FenixChannelFakeServer();
        var events = new Events();
        var client = NewClient(server);
        client.Start(FenixChannelCall.DisplayData, events.OnMessage, events.OnState);
        await events.WhenStates(1);

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public void Dispose_does_nothing_to_a_client_that_never_started()
    {
        var client = NewClient(new FenixChannelFakeServer());

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public void Dispose_leaves_a_handler_it_was_given_alone()
    {
        // The client disposes only a handler it created itself; the test owns this one.
        var server = new FenixChannelFakeServer();
        var client = NewClient(server);

        client.Dispose();

        Assert.False(server.IsDisposed);
    }
}
