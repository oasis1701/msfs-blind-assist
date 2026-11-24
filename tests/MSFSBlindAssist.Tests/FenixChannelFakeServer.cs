// Test double for the network under the Fenix display channel client and the monitor that owns it: a fake
// HttpMessageHandler that answers the way the gRPC service on 127.0.0.1:18083 would, and records what the
// client sent. Nothing here opens a socket.
//
// Answers come from a queue (Enqueue, Throw, HoldAnswer). When the queue is empty the server answers Endless(): a stream
// that stays open and silent until the client lets go of it. The factory methods build the answers a test
// needs. A response's trailers show up only once its body has been read to the end, as they do on a real
// HTTP/2 response, so a client that asks for the status too early finds none. Every response says when the
// client disposed it (Disposed), which is how a test knows the client dropped a stream without a sleep.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace MSFSBlindAssist.Tests;

internal sealed class FenixChannelFakeServer : HttpMessageHandler
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly List<SeenRequest> _requests = new();
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _answers = new();
    private readonly List<(int Count, TaskCompletionSource<int> Signal)> _waiters = new();

    /// <summary>One request as the client sent it.</summary>
    public sealed record SeenRequest(
        HttpMethod Method,
        string Uri,
        Version Version,
        HttpVersionPolicy VersionPolicy,
        string? ContentType,
        string? Te,
        string? GrpcAcceptEncoding,
        byte[] Body);

    /// <summary>The requests so far, in the order they arrived: a snapshot, safe to read while the client runs.</summary>
    public IReadOnlyList<SeenRequest> Requests
    {
        get { lock (_lock) return _requests.ToArray(); }
    }

    /// <summary>True once the server itself was disposed: a client must not dispose a handler it was given.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Queues the answer to the next request that has none queued before it.</summary>
    public void Enqueue(Func<HttpResponseMessage> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        lock (_lock) _answers.Enqueue(_ => Task.FromResult(answer()));
    }

    /// <summary>
    /// Queues an answer that never comes: the next request without an answer queued before it gets no response
    /// headers at all, until the client cancels it. A Fenix that took the connection and hangs.
    /// </summary>
    public void HoldAnswer()
    {
        lock (_lock)
        {
            _answers.Enqueue(async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                throw new InvalidOperationException("An infinite delay ended without a cancellation.");
            });
        }
    }

    /// <summary>Queues a failure: the next request without an answer queued before it throws this.</summary>
    public void Throw(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Enqueue(() => throw ex);
    }

    /// <summary>Completes with the number of requests once at least <paramref name="count"/> have arrived;
    /// times out after 5 s.</summary>
    public Task<int> WaitForRequestsAsync(int count)
    {
        TaskCompletionSource<int> signal;
        lock (_lock)
        {
            if (_requests.Count >= count) return Task.FromResult(_requests.Count);
            signal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, signal));
        }

        return signal.Task.WaitAsync(WaitLimit);
    }

    // ---- The answers ----------------------------------------------------------------------------

    /// <summary>
    /// A 200 answer whose body is <paramref name="frames"/> (gRPC frames, back to back) and then ends. The
    /// trailers carry grpc-status <paramref name="trailerStatus"/> (none when null), once the body has been
    /// read to the end. <paramref name="maxChunk"/> caps how many bytes one read gives, to cut messages
    /// across reads.
    /// </summary>
    public static FakeResponse Stream(byte[] frames, int? trailerStatus = 0, int maxChunk = int.MaxValue)
    {
        var response = Grpc200();
        Action? onEnd = null;
        if (trailerStatus is int status)
        {
            onEnd = () => response.TrailingHeaders.TryAddWithoutValidation(
                "grpc-status", status.ToString(CultureInfo.InvariantCulture));
        }

        response.Content = Body(new FakeBody(frames, hold: false, onEnd, maxChunk));
        return response;
    }

    /// <summary>
    /// An answer with no message: grpc-status <paramref name="status"/> (and the message, percent-encoded as
    /// gRPC sends it) in the headers, an empty body. How the service says "no such call" (12).
    /// </summary>
    public static FakeResponse TrailersOnly(int status, string? message = null)
    {
        var response = Grpc200();
        response.Content = Body(new FakeBody([], hold: false, onEnd: null, int.MaxValue));
        response.Headers.TryAddWithoutValidation("grpc-status", status.ToString(CultureInfo.InvariantCulture));
        if (message != null)
        {
            response.Headers.TryAddWithoutValidation("grpc-message", Uri.EscapeDataString(message));
        }

        return response;
    }

    /// <summary>A 200 answer whose body never ends and never carries a byte, until the client's read is canceled.</summary>
    public static FakeResponse Endless() => StreamThenHold([]);

    /// <summary>
    /// A 200 answer whose body gives <paramref name="frames"/> and then holds: no end, no trailers, until the
    /// client's read is canceled. A stream that delivers messages and stays open, as the live one does.
    /// </summary>
    public static FakeResponse StreamThenHold(byte[] frames)
    {
        var response = Grpc200();
        response.Content = Body(new FakeBody(frames, hold: true, onEnd: null, int.MaxValue));
        return response;
    }

    /// <summary>A response that says when it was disposed.</summary>
    public sealed class FakeResponse : HttpResponseMessage
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeResponse(HttpStatusCode statusCode) : base(statusCode)
        {
        }

        /// <summary>Completes when the client disposes the response: it has let go of the call, whether the call
        /// finished, was dropped or was canceled.</summary>
        public Task Disposed => _disposed.Task;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _disposed.TrySetResult();
        }
    }

    // ---- The handler ----------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content == null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var seen = new SeenRequest(
            request.Method,
            request.RequestUri?.ToString() ?? "",
            request.Version,
            request.VersionPolicy,
            request.Content?.Headers.ContentType?.ToString(),
            HeaderValue(request, "te"),
            HeaderValue(request, "grpc-accept-encoding"),
            body);

        Func<CancellationToken, Task<HttpResponseMessage>>? answer;
        int count;
        var ready = new List<TaskCompletionSource<int>>();
        lock (_lock)
        {
            _requests.Add(seen);
            count = _requests.Count;
            answer = _answers.Count > 0 ? _answers.Dequeue() : null;
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                if (count < _waiters[i].Count) continue;
                ready.Add(_waiters[i].Signal);
                _waiters.RemoveAt(i);
            }
        }

        foreach (var signal in ready) signal.TrySetResult(count);
        return answer != null ? await answer(cancellationToken).ConfigureAwait(false) : Endless();
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    private static string? HeaderValue(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static FakeResponse Grpc200() => new(HttpStatusCode.OK) { Version = HttpVersion.Version20 };

    private static StreamContent Body(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        return content;
    }

    // A response body: gives its bytes (at most maxChunk per read) and then either ends, or holds until the
    // read's token is canceled. onEnd runs once, when a read first finds the end: the trailers arrive then.
    private sealed class FakeBody : Stream
    {
        private readonly MemoryStream _data;
        private readonly bool _hold;
        private readonly Action? _onEnd;
        private readonly int _maxChunk;
        private bool _ended;

        public FakeBody(byte[] data, bool hold, Action? onEnd, int maxChunk)
        {
            _data = new MemoryStream(data, writable: false);
            _hold = hold;
            _onEnd = onEnd;
            _maxChunk = maxChunk;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = _data.Read(buffer[..Math.Min(buffer.Length, _maxChunk)]);
            if (read > 0) return read;
            if (_hold) throw new NotSupportedException("A held body can only be read asynchronously, so a cancellation can end the read.");

            End();
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = _data.Read(buffer.Span[..Math.Min(buffer.Length, _maxChunk)]);
            if (read > 0) return read;

            // Nothing is left: hold until the token is canceled (the delay never completes, it throws), or end.
            if (_hold) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);

            End();
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _data.Dispose();
            base.Dispose(disposing);
        }

        private void End()
        {
            if (_ended) return;
            _ended = true;
            _onEnd?.Invoke();
        }
    }
}
