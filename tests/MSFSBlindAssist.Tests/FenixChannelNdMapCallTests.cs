// The two map reads (GetMapDataVersion, GetMapData) on the Fenix display channel: one request, one reply message,
// through FenixChannelClient.CallOnceAsync and FenixChannelMonitor.RequestOnceAsync. The network is a small fake
// of this file's own: it answers each request from a queue and records what was sent. Nothing opens a socket.

using System.Net;
using System.Net.Http.Headers;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdMapCallTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private sealed class MapFake : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _answers = new();
        private readonly List<(string Uri, byte[] Body)> _requests = new();

        public IReadOnlyList<(string Uri, byte[] Body)> Requests
        {
            get { lock (_lock) return _requests.ToArray(); }
        }

        public void Answer(Func<CancellationToken, Task<HttpResponseMessage>> answer)
        {
            lock (_lock) _answers.Enqueue(answer);
        }

        public void Reply(byte[] frames, int? status = 0) => Answer(_ => Task.FromResult(Ok(frames, status)));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            byte[] body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            Func<CancellationToken, Task<HttpResponseMessage>>? answer;
            lock (_lock)
            {
                _requests.Add((request.RequestUri!.AbsolutePath, body));
                answer = _answers.Count > 0 ? _answers.Dequeue() : null;
            }

            if (answer is null) throw new HttpRequestException("no answer queued");
            return await answer(ct);
        }

        public static HttpResponseMessage Ok(byte[] frames, int? status)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20 };
            response.Content = new ByteArrayContent(frames);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            if (status is int s) response.TrailingHeaders.TryAddWithoutValidation("grpc-status", s.ToString());
            return response;
        }

        public static HttpResponseMessage TrailersOnly(int status)
        {
            var response = Ok([], null);
            response.Headers.TryAddWithoutValidation("grpc-status", status.ToString());
            return response;
        }
    }

    private static byte[] Frame(params byte[] message)
    {
        byte[] frame = new byte[5 + message.Length];
        frame[4] = (byte)message.Length;
        message.CopyTo(frame, 5);
        return frame;
    }

    private static FenixChannelClient ClientOn(MapFake fake) => new(fake);

    private static FenixChannelMonitor MonitorOn(MapFake fake, List<string> log)
        => new(_ => Task.FromResult<string?>(null), handler: fake, log: log.Add);

    [Fact]
    public async Task A_unary_call_returns_its_one_message()
    {
        var fake = new MapFake();
        fake.Reply(Frame(7, 8, 9));
        using var client = ClientOn(fake);

        var result = await client.CallOnceAsync(FenixChannelCall.MapDataVersion, [1, 2], Limit, CancellationToken.None);

        Assert.Equal(FenixChannelState.Connected, result.State);
        Assert.Equal(new byte[] { 7, 8, 9 }, result.Message);
        Assert.Equal("", result.Text);
        var seen = Assert.Single(fake.Requests);
        Assert.Equal(FenixChannelFields.PathOf(FenixChannelCall.MapDataVersion), seen.Uri);
        Assert.Equal(Frame(1, 2), seen.Body);
    }

    [Fact]
    public async Task A_failed_unary_call_says_why()
    {
        var fake = new MapFake();
        fake.Answer(_ => throw new HttpRequestException("connection refused"));
        fake.Answer(_ => Task.FromResult(MapFake.TrailersOnly(12)));
        fake.Answer(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return MapFake.Ok([], 0);
        });
        using var client = ClientOn(fake);

        var noAnswer = await client.CallOnceAsync(FenixChannelCall.MapData, [], Limit, CancellationToken.None);
        var notOffered = await client.CallOnceAsync(FenixChannelCall.MapData, [], Limit, CancellationToken.None);
        var timedOut = await client.CallOnceAsync(
            FenixChannelCall.MapData, [], TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.Equal(FenixChannelState.NotAnswering, noAnswer.State);
        Assert.Null(noAnswer.Message);
        Assert.Contains("connection refused", noAnswer.Text);
        Assert.Equal(FenixChannelState.NotAnswering, notOffered.State);
        Assert.Contains("does not offer", notOffered.Text);
        Assert.Equal(FenixChannelState.NotAnswering, timedOut.State);
        Assert.Null(timedOut.Message);
        Assert.Contains("no reply within", timedOut.Text);
    }

    [Fact]
    public async Task A_refused_map_call_ends_the_channel()
    {
        var fake = new MapFake();
        fake.Answer(_ => Task.FromResult(MapFake.TrailersOnly(7)));
        var log = new List<string>();
        using var monitor = MonitorOn(fake, log);

        var result = await monitor.RequestOnceAsync(FenixChannelCall.MapData, [], CancellationToken.None);

        Assert.Equal(FenixChannelState.Refused, result.State);
        Assert.True(monitor.Session.Refused);
        string line = Assert.Single(log);
        Assert.Contains("refused", line);
        Assert.Contains("GetMapData", line);
    }

    [Fact]
    public void A_stream_call_is_not_a_unary_call()
    {
        using var client = ClientOn(new MapFake());

        // A mistake in the code, so it throws at the call itself, not into the returned task.
        Assert.Throws<ArgumentException>(
            () => { _ = client.CallOnceAsync(FenixChannelCall.DisplayData, [], Limit, CancellationToken.None); });
        Assert.Throws<ArgumentException>(
            () => { _ = client.CallOnceAsync(FenixChannelCall.EcamEntries, [], Limit, CancellationToken.None); });
    }

    [Fact]
    public async Task Nothing_is_called_after_a_refusal()
    {
        var fake = new MapFake();
        fake.Answer(_ => Task.FromResult(MapFake.TrailersOnly(16)));
        fake.Reply(Frame(1));
        var log = new List<string>();
        using var monitor = MonitorOn(fake, log);
        await monitor.RequestOnceAsync(FenixChannelCall.MapDataVersion, [], CancellationToken.None);

        var after = await monitor.RequestOnceAsync(FenixChannelCall.MapData, [], CancellationToken.None);

        Assert.Equal(FenixChannelState.NotAnswering, after.State);
        Assert.Null(after.Message);
        Assert.Single(fake.Requests);
        Assert.Single(log);

        // And after Dispose, on a monitor that was never refused.
        var other = new MapFake();
        var monitor2 = MonitorOn(other, new List<string>());
        monitor2.Dispose();
        var disposed = await monitor2.RequestOnceAsync(FenixChannelCall.MapData, [], CancellationToken.None);
        Assert.Equal(FenixChannelState.NotAnswering, disposed.State);
        Assert.Empty(other.Requests);
    }
}
