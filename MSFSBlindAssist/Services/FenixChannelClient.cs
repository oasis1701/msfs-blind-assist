using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Where one call of the Fenix display channel stands, as <see cref="FenixChannelClient"/> reports it.
/// </summary>
internal enum FenixChannelState
{
    /// <summary>The call is open and answering: its messages follow.</summary>
    Connected,

    /// <summary>
    /// The call is not answering: the Fenix is not running or still loading, it does not offer the call, or the
    /// stream broke, ended or went silent. The client asks again by itself; the text says why.
    /// </summary>
    NotAnswering,

    /// <summary>
    /// The Fenix refused the client (grpc-status 7 or 16, or HTTP 401 or 403, which gRPC maps to them). The client
    /// ends that call for the session and never tries to get past the refusal: if the Fenix adds a key, that is
    /// the Fenix's decision. The monitor then stops the other call too: a refusal ends the channel ([FNX-21]).
    /// </summary>
    Refused
}

/// <summary>
/// How one unary call ended (<see cref="FenixChannelClient.CallOnceAsync"/>): the state it leaves the call in, the
/// one reply message when it answered, and why when it did not.
/// </summary>
/// <param name="State">Connected when the call answered with one message; NotAnswering when it did not (no answer,
/// not offered, timed out, a wrong shape); Refused when the Fenix refused the client.</param>
/// <param name="Message">The reply message without its gRPC header; null unless Connected.</param>
/// <param name="Text">Why, for the log; empty when Connected.</param>
internal readonly record struct FenixChannelUnaryResult(FenixChannelState State, byte[]? Message, string Text);

/// <summary>
/// The connection to the Fenix display channel, the local gRPC service on 127.0.0.1:18083 (HTTP/2 without TLS).
/// It opens one of the two stream calls (<see cref="FenixChannelCall"/>), splits the answer into messages, hands
/// each to a callback and reports the call's state, and reconnects quietly when the call fails or ends. The
/// display channel monitor owns the instance and disposes it when the pilot switches away.
/// </summary>
/// <remarks>
/// <para>Modeled on <see cref="FenixDataService"/>: nothing here throws at a caller because the Fenix is not
/// running or a stream broke, reconnects are quiet, and the client never speaks and never touches the UI. The
/// callbacks run on a thread-pool thread (the call's own loop), never the UI thread: an owner marshals to its
/// own thread ([FNX-2]).</para>
/// <para>The client is read only. It opens a call by <see cref="FenixChannelCall"/> and nothing else (no API
/// takes a path), and the service's other calls, which write values, run actions or hand out credentials,
/// have no member there. A refusal ends that call for the session, and the client never tries to get past it.</para>
/// </remarks>
internal sealed class FenixChannelClient : IDisposable
{
    /// <summary>
    /// How long to wait before asking again, by attempt: 3, 6 and 12 s, then every 30 s. The attempt starts over at 0
    /// when a call delivers its first message, not when it opens.
    /// </summary>
    public static readonly int[] ReconnectDelaysMs = { 3000, 6000, 12000, 30000 };

    /// <summary>How long to wait before asking again for a call the Fenix says it does not offer (grpc-status 12).</summary>
    public const int CallNotFoundDelayMs = 30000;

    /// <summary>
    /// How long the display data call may send no byte before the client drops it and reconnects. It streams
    /// about 17 messages a second, so ten seconds of silence is a stream that died without ending.
    /// </summary>
    public static readonly TimeSpan DisplayDataSilence = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the ECAM entries call may send no byte before the client drops it and reconnects. It sends its list
    /// only when the list changes, but a heartbeat every 5 s says it is alive (measured on the A321 IAE, build
    /// 3.0.0.7613, 2026-11-04), so fifteen seconds of silence is three beats missed: a stream that died without
    /// ending, whose last list would otherwise stand for good.
    /// </summary>
    public static readonly TimeSpan EcamEntriesSilence = TimeSpan.FromSeconds(15);

    private const string LogCategory = "FenixChannel";
    private const int ReadChunkBytes = 16 * 1024;

    private readonly HttpClient _http;
    private readonly Func<int, bool, TimeSpan> _retryDelay;
    private readonly TimeSpan _displayDataSilence;
    private readonly TimeSpan _ecamEntriesSilence;

    // Canceled by Dispose: ends every loop, and any read, send or wait one is in. Never disposed, as in
    // FenixDataService: it holds no timer and no handle, and a loop may still read its token.
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    /// <param name="handler">For tests: answers the HTTP calls instead of the network. The client disposes only a
    /// handler it created itself.</param>
    /// <param name="retryDelay">For tests: how long to wait before asking again, given the attempt (0 for the
    /// first) and whether the last answer was grpc-status 12. By default <see cref="DefaultRetryDelay"/>.</param>
    /// <param name="displayDataSilence">For tests: how long the display data call may send no byte. By default
    /// <see cref="DisplayDataSilence"/>.</param>
    /// <param name="ecamEntriesSilence">For tests: how long the ECAM entries call may send no byte. By default
    /// <see cref="EcamEntriesSilence"/>.</param>
    public FenixChannelClient(
        HttpMessageHandler? handler = null,
        Func<int, bool, TimeSpan>? retryDelay = null,
        TimeSpan? displayDataSilence = null,
        TimeSpan? ecamEntriesSilence = null)
    {
        _http = new HttpClient(
            handler ?? CreateDefaultHandler(),
            disposeHandler: handler == null)
        {
            // A stream has no end to time out: each call's silence limit and Dispose are what end one.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _displayDataSilence = displayDataSilence ?? DisplayDataSilence;
        _ecamEntriesSilence = ecamEntriesSilence ?? EcamEntriesSilence;
    }

    // The silence limit of a stream call: how long it may go without its answer's headers, and then without a byte.
    private TimeSpan SilenceOf(FenixChannelCall call)
        => call == FenixChannelCall.EcamEntries ? _ecamEntriesSilence : _displayDataSilence;

    /// <summary>
    /// The handler the client builds when a test gives it none: HTTP/2 over a socket, a 5 s connect timeout, and
    /// no redirects. A followed redirect could open a path <see cref="FenixChannelFields.PathOf"/> never gave, and
    /// the client opens only the <see cref="FenixChannelCall"/> paths, so a 3xx answer is a failure like any other status that is not 200.
    /// </summary>
    internal static SocketsHttpHandler CreateDefaultHandler()
        => new()
        {
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AllowAutoRedirect = false
        };

    /// <summary>
    /// The wait before asking again when no test has chosen one: the schedule of <see cref="ReconnectDelaysMs"/>
    /// by attempt, or <see cref="CallNotFoundDelayMs"/> for a call the Fenix does not offer.
    /// </summary>
    internal static TimeSpan DefaultRetryDelay(int attempt, bool callNotFound)
        => TimeSpan.FromMilliseconds(callNotFound
            ? CallNotFoundDelayMs
            : ReconnectDelaysMs[Math.Clamp(attempt, 0, ReconnectDelaysMs.Length - 1)]);

    /// <summary>
    /// Opens one call and keeps it open until <see cref="Dispose"/> or a refusal: a loop on its own pool thread
    /// connects, reads, and connects again when the call fails or ends. Start each call once; the two stream calls
    /// run independently over this one client.
    /// </summary>
    /// <param name="call">The call to open.</param>
    /// <param name="onMessage">Each whole message of the stream, without its gRPC header, in order.</param>
    /// <param name="onState">Each change of the call's state, with the reason (empty for Connected). The same state
    /// with the same reason is not reported twice in a row, so a Fenix that stays down is one report.</param>
    /// <remarks>
    /// Both callbacks run on a thread-pool thread, never the thread that called Start. An exception from one is
    /// swallowed (the first one per call is logged), and none starts after <see cref="Dispose"/>. A value of
    /// <paramref name="call"/> that is no <see cref="FenixChannelCall"/> throws <see cref="ArgumentOutOfRangeException"/>
    /// here, as a null callback throws <see cref="ArgumentNullException"/>: mistakes in the code, not conditions
    /// of the sim. After Dispose, Start does nothing.
    /// </remarks>
    public void Start(FenixChannelCall call, Action<byte[]> onMessage, Action<FenixChannelState, string> onState)
    {
        ArgumentNullException.ThrowIfNull(onMessage);
        ArgumentNullException.ThrowIfNull(onState);
        string path = FenixChannelFields.PathOf(call);

        // The map calls are unary (CallOnceAsync); a stream loop on one would send it an empty request.
        if (call is FenixChannelCall.MapDataVersion or FenixChannelCall.MapData)
        {
            throw new ArgumentOutOfRangeException(nameof(call), call, "Not a stream call.");
        }

        if (Volatile.Read(ref _disposed) != 0) return;

        var loop = new CallLoop(this, call, path, onMessage, onState, _lifetime.Token);
        _ = Task.Run(loop.RunAsync);
    }

    /// <summary>
    /// Makes one of the two map calls: one request, one reply message and the trailers. Whatever goes wrong at the
    /// Fenix is the result that says why, never a throw. A refusal (grpc-status 7 or 16, HTTP 401 or 403) is
    /// <see cref="FenixChannelState.Refused"/>; the owner ends the channel, and this method does not ask again by
    /// itself. After <see cref="Dispose"/> it returns at once without a call. A stream call throws
    /// <see cref="ArgumentException"/> here: a mistake in the code, not a condition of the sim.
    /// </summary>
    /// <param name="call">MapDataVersion or MapData.</param>
    /// <param name="request">The request message, without its gRPC header.</param>
    /// <param name="timeout">How long the whole call may take.</param>
    public Task<FenixChannelUnaryResult> CallOnceAsync(
        FenixChannelCall call, byte[] request, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (call is not (FenixChannelCall.MapDataVersion or FenixChannelCall.MapData))
        {
            throw new ArgumentException("Only the map calls are unary calls.", nameof(call));
        }

        string path = FenixChannelFields.PathOf(call);
        if (Volatile.Read(ref _disposed) != 0) return Task.FromResult(UnaryDown("the channel client is stopped"));

        return UnaryAsync(path, request, timeout, ct);
    }

    private static FenixChannelUnaryResult UnaryDown(string text) => new(FenixChannelState.NotAnswering, null, text);

    private async Task<FenixChannelUnaryResult> UnaryAsync(
        string path, byte[] request, TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, ct);
        limit.CancelAfter(timeout);
        try
        {
            using HttpRequestMessage message = BuildRequest(UriOf(path), FenixChannelFraming.Frame(request));
            using HttpResponseMessage response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK) return UnaryResultOf(HttpOutcome(path, (int)response.StatusCode));

            await using Stream body = await response.Content.ReadAsStreamAsync(limit.Token).ConfigureAwait(false);
            var reader = new FenixChannelMessageReader();
            var messages = new List<byte[]>();
            byte[] chunk = new byte[ReadChunkBytes];
            while (true)
            {
                int read = await body.ReadAsync(chunk.AsMemory(), limit.Token).ConfigureAwait(false);
                if (read == 0) break;

                reader.Append(chunk.AsSpan(0, read));
                while (reader.TryTake(out byte[] taken)) messages.Add(taken);
                if (reader.Error is not null) break;
            }

            // The trailers come after the body, which has been read to its end; a trailers-only answer has its
            // status in the headers.
            var (status, statusMessage) = FenixChannelFraming.ReadStatus(response);
            if (status is int s && s != 0) return UnaryResultOf(StatusOutcome(path, s, statusMessage));

            if (reader.Error is { } error) return UnaryDown(error);
            if (reader.HasPartialMessage) return UnaryDown("cut-off message");
            if (status is null) return UnaryDown("the reply ended with no grpc-status");
            if (messages.Count != 1)
            {
                return UnaryDown(string.Create(
                    CultureInfo.InvariantCulture, $"{messages.Count} reply messages, expected one"));
            }

            return new(FenixChannelState.Connected, messages[0], "");
        }
        catch (Exception) when (_lifetime.IsCancellationRequested)
        {
            return UnaryDown("the channel client is stopped");
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            return UnaryDown("the call was canceled");
        }
        catch (OperationCanceledException)
        {
            return UnaryDown(string.Create(CultureInfo.InvariantCulture, $"no reply within {timeout.TotalSeconds} s"));
        }
        catch (Exception ex)
        {
            return UnaryDown($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // A unary call's result for an outcome a stream call would act on: a refusal is still one, and anything else is a
    // call that did not answer, since a unary call does not ask again by itself.
    private static FenixChannelUnaryResult UnaryResultOf(Outcome outcome)
        => outcome.Kind == EndKind.Refused
            ? new(FenixChannelState.Refused, null, outcome.Text)
            : UnaryDown(outcome.Text);

    // ---- What every call shares ------------------------------------------------------------------

    // The numeric address, never "localhost" ([A320-40]); the path comes from FenixChannelFields.
    private static Uri UriOf(string path)
        => new(string.Create(
            CultureInfo.InvariantCulture, $"http://{FenixChannelFields.Host}:{FenixChannelFields.Port}{path}"));

    private static HttpRequestMessage BuildRequest(Uri uri, byte[] framedBody)
    {
        var content = new ByteArrayContent(framedBody);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            // HTTP/2 without TLS: the connection starts as HTTP/2 (prior knowledge), with no upgrade.
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content
        };

        // "te: trailers" says the client reads the trailers, where the status of a call comes; identity says it
        // takes no compressed message.
        request.Headers.TryAddWithoutValidation("te", "trailers");
        request.Headers.TryAddWithoutValidation("grpc-accept-encoding", "identity");
        return request;
    }

    // What an answer whose HTTP status is not 200 means. gRPC maps HTTP 401 and 403 to grpc-status 16 and 7: the Fenix
    // saying no is a refusal however it says it, and the client never tries to get past one.
    private static Outcome HttpOutcome(string path, int code)
    {
        if (code is not (401 or 403)) return Outcome.Reconnect(string.Create(CultureInfo.InvariantCulture, $"HTTP {code}"));

        string name = code == 401 ? "Unauthorized" : "Forbidden";
        return Outcome.Refuse(string.Create(
            CultureInfo.InvariantCulture,
            $"the Fenix refused {path} (HTTP {code}, {name}); the call stays off for the session"));
    }

    // What a call that ended with a grpc-status other than 0 means. A status of 0 is never asked here: in a stream's
    // headers it is no news, and after its messages it is an ordinary end.
    private static Outcome StatusOutcome(string path, int status, string? message)
    {
        string why = string.IsNullOrWhiteSpace(message) ? "" : ": " + message;
        switch (status)
        {
            case 7 or 16:
                string name = status == 7 ? "PERMISSION_DENIED" : "UNAUTHENTICATED";
                return Outcome.Refuse(string.Create(
                    CultureInfo.InvariantCulture,
                    $"the Fenix refused {path} (grpc-status {status}, {name}{why}); the call stays off for the session"));
            case 12:
                return Outcome.NotOffered($"the Fenix does not offer {path} (did an update change the channel?)");
            default:
                // The number alone: a message that changed from one try to the next would make each try news in the log.
                return Outcome.Reconnect(string.Create(CultureInfo.InvariantCulture, $"grpc-status {status}"));
        }
    }

    private enum EndKind
    {
        Reconnect,
        CallNotOffered,
        Refused,
        Stopped
    }

    // How a call ended: what to do next, and the reason to report.
    private readonly record struct Outcome(EndKind Kind, string Text)
    {
        public static readonly Outcome Stopped = new(EndKind.Stopped, "");

        public static Outcome Reconnect(string text) => new(EndKind.Reconnect, text);

        public static Outcome NotOffered(string text) => new(EndKind.CallNotOffered, text);

        public static Outcome Refuse(string text) => new(EndKind.Refused, text);
    }

    /// <summary>
    /// Ends every loop at once, dropping any stream open, and releases the HTTP client. Never throws and never
    /// waits for a loop, so it is safe on the UI thread; a second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Canceling ends the reads, sends and waits the loops are in; each loop sees its token canceled and
        // leaves without a word. The client goes with it, and with it the connection the streams run on.
        try { _lifetime.Cancel(); } catch (Exception) { }
        try { _http.Dispose(); } catch (Exception) { }
    }

    // One call's loop: connect, read until the call ends or breaks, report why, wait, connect again.
    private sealed class CallLoop
    {
        private readonly FenixChannelClient _owner;
        private readonly FenixChannelCall _call;
        private readonly string _path;
        private readonly Uri _uri;
        private readonly Action<byte[]> _onMessage;
        private readonly Action<FenixChannelState, string> _onState;
        private readonly CancellationToken _lifetime;
        private readonly TimeSpan _silence;

        // Only the loop touches these, one step at a time, so they need no lock.
        private int _attempt;
        private bool _reported;
        private FenixChannelState _lastState;
        private string _lastText = "";
        private bool _callbackFailureLogged;

        public CallLoop(
            FenixChannelClient owner,
            FenixChannelCall call,
            string path,
            Action<byte[]> onMessage,
            Action<FenixChannelState, string> onState,
            CancellationToken lifetime)
        {
            _owner = owner;
            _call = call;
            _path = path;
            _uri = UriOf(path);
            _onMessage = onMessage;
            _onState = onState;
            _lifetime = lifetime;
            _silence = owner.SilenceOf(call);
        }

        public async Task RunAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    Outcome outcome = await OpenOnceAsync().ConfigureAwait(false);
                    if (outcome.Kind == EndKind.Stopped) return;

                    if (outcome.Kind == EndKind.Refused)
                    {
                        Report(FenixChannelState.Refused, outcome.Text);
                        return;
                    }

                    Report(FenixChannelState.NotAnswering, outcome.Text);
                    TimeSpan wait = _owner._retryDelay(_attempt, outcome.Kind == EndKind.CallNotOffered);
                    _attempt++;
                    await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, _lifetime).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                // Disposed during a wait: the loop ends without a word.
            }
            catch (Exception ex)
            {
                // A bug in the loop must not leave the owner believing the call is still up.
                Log.Debug(LogCategory, $"{_call} loop stopped: {ex.GetType().Name}: {ex.Message}");
                Report(FenixChannelState.NotAnswering, $"the client stopped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // One connection, from the request to the end of its stream. Never throws: whatever goes wrong is the
        // outcome that says why, and a Dispose in the middle of it is Stopped.
        private async Task<Outcome> OpenOnceAsync()
        {
            try
            {
                // The body is the one empty message gRPC sends for a call with no request.
                using var request = BuildRequest(_uri, FenixChannelFraming.EmptyRequest());
                if (await SendWithinSilenceAsync(request).ConfigureAwait(false) is not { } response)
                {
                    return Outcome.Reconnect(string.Create(
                        CultureInfo.InvariantCulture, $"no answer for {_silence.TotalSeconds} s"));
                }

                using (response)
                {
                    if (response.StatusCode != HttpStatusCode.OK) return HttpOutcome(_path, (int)response.StatusCode);

                    // An answer that carries no message (an error) is one block of headers with its status in it. A
                    // normal answer has none yet: its status is in the trailers, after the last message.
                    var (headerStatus, headerMessage) = FenixChannelFraming.ReadStatus(response);
                    if (headerStatus is int status && status != 0) return StatusOutcome(_path, status, headerMessage);

                    Report(FenixChannelState.Connected, "");
                    return await ReadStreamAsync(response).ConfigureAwait(false);
                }
            }
            catch (Exception) when (_lifetime.IsCancellationRequested)
            {
                return Outcome.Stopped;
            }
            catch (Exception ex)
            {
                return Outcome.Reconnect($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // Sends the request and waits for the answer's headers, at most the call's silence limit: a Fenix that takes
        // the connection and never answers is as dead as a stream that stops sending. Null when the limit ran out.
        // The limit's own source is disposed as soon as the headers are in, so it can never cancel the stream that
        // follows, which the reads' own limits watch from there (HttpClient's Timeout works the same way).
        private async Task<HttpResponseMessage?> SendWithinSilenceAsync(HttpRequestMessage request)
        {
            using var answer = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
            answer.CancelAfter(_silence);
            try
            {
                return await _owner._http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, answer.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (answer.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                return null;
            }
        }

        // The body of an open call: every whole message to the callback, until the stream ends or breaks.
        private async Task<Outcome> ReadStreamAsync(HttpResponseMessage response)
        {
            await using Stream body = await response.Content.ReadAsStreamAsync(_lifetime).ConfigureAwait(false);
            var reader = new FenixChannelMessageReader();
            byte[] chunk = new byte[ReadChunkBytes];
            bool firstMessageSeen = false;

            while (true)
            {
                // Each read has its own limit, so a message that arrives just as the time runs out is not taken for
                // silence.
                int read;
                using (var silence = CancellationTokenSource.CreateLinkedTokenSource(_lifetime))
                {
                    silence.CancelAfter(_silence);
                    try
                    {
                        read = await body.ReadAsync(chunk.AsMemory(), silence.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (silence.IsCancellationRequested && !_lifetime.IsCancellationRequested)
                    {
                        return Outcome.Reconnect(string.Create(
                            CultureInfo.InvariantCulture, $"no data for {_silence.TotalSeconds} s"));
                    }
                }

                if (read == 0) break;

                reader.Append(chunk.AsSpan(0, read));
                while (reader.TryTake(out byte[] message))
                {
                    if (_lifetime.IsCancellationRequested) return Outcome.Stopped;

                    // A message shows the call works, which response headers do not: only now does the wait before
                    // the next reconnect start over. A call that opens and stays silent, or ends with nothing to
                    // say, backs off like a refused connection instead of asking again every few seconds.
                    if (!firstMessageSeen)
                    {
                        firstMessageSeen = true;
                        _attempt = 0;
                    }

                    Deliver(message);
                }

                // TryTake is what finds a header the reader refuses, so ask now, not when the body ends: a
                // stream that is out of step may never end. The whole messages ahead of it came out above.
                if (reader.Error is { } error) return Outcome.Reconnect(error);
            }

            if (reader.Error is { } endError) return Outcome.Reconnect(endError);
            if (reader.HasPartialMessage) return Outcome.Reconnect("cut-off message");

            // The trailers come after the body: ask only now that it has been read to its end, and before the
            // response is disposed.
            var (status, statusMessage) = FenixChannelFraming.ReadStatus(response);
            return status switch
            {
                null => Outcome.Reconnect("the stream ended with no grpc-status"),
                0 => Outcome.Reconnect("the stream ended"),
                int other => StatusOutcome(_path, other, statusMessage)
            };
        }

        // A state is reported when it changes, or its reason does; not after Dispose; and a callback that throws
        // is the owner's trouble, not the loop's.
        private void Report(FenixChannelState state, string text)
        {
            if (_lifetime.IsCancellationRequested) return;
            if (_reported && _lastState == state && _lastText == text) return;

            _reported = true;
            _lastState = state;
            _lastText = text;
            try
            {
                _onState(state, text);
            }
            catch (Exception ex)
            {
                LogCallbackFailure("state", ex);
            }
        }

        private void Deliver(byte[] message)
        {
            try
            {
                _onMessage(message);
            }
            catch (Exception ex)
            {
                LogCallbackFailure("message", ex);
            }
        }

        // The first failure of either callback, once per call: a callback that throws on every message must
        // not fill debug.log at 17 lines a second.
        private void LogCallbackFailure(string callback, Exception ex)
        {
            if (_callbackFailureLogged) return;

            _callbackFailureLogged = true;
            Log.Debug(LogCategory, $"{_call} {callback} callback threw: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
