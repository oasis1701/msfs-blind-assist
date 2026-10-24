using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The one client of the Fenix data service, the local gateway the Fenix runs on 127.0.0.1:8083:
/// GraphQL reads, writes and queries and the REST paths over HTTP, and every data-ref subscription
/// over one shared <c>graphql-transport-ws</c> WebSocket. The Fenix aircraft definition owns the
/// instance (<c>FenixA320Definition.DataService</c>) and disposes it when the pilot switches away.
/// </summary>
/// <remarks>
/// <para>Nothing here throws at a caller because the Fenix is not loaded or the gateway is down:
/// a read gives <see cref="FenixDataValue.Unknown"/> (never 0, false or "off"), a write gives false,
/// a query or REST call gives null, and a subscription retries quietly in the background. A
/// cancelled <see cref="CancellationToken"/> gives the same failure values, never an
/// <see cref="OperationCanceledException"/>.</para>
/// <para>The service never speaks and never touches the UI. It never queries the schema
/// (introspection, <c>?sdl</c>, the IDE page) and never calls <c>navigraph { … }</c>.</para>
/// </remarks>
public sealed class FenixDataService : IDisposable
{
    // 127.0.0.1, never by name ([A320-40]): the data service listens on IPv4 only, and Windows spends
    // about 2 s refusing ::1 on every new connection before it falls back to IPv4.
    private const string RestBase = "http://127.0.0.1:8083";
    private const string GraphQlUrl = RestBase + "/graphql";
    private const string SocketUrl = "ws://127.0.0.1:8083/graphql";
    private const string LogCategory = "FenixData";

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SocketOpenTimeout = TimeSpan.FromSeconds(10);
    private static readonly int[] ReconnectDelaysMs = { 3000, 6000, 12000, 30000 };
    private const int MaxRememberedProblems = 100;

    private readonly HttpClient _http;
    private readonly CancellationTokenSource _lifetime = new();
    // ClientWebSocket allows one outstanding send: the loop's pongs and re-subscribes and a caller's
    // subscribe or complete all go through this.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // _gate guards the subscriptions, the id counter, the loop start and the two socket fields.
    private readonly object _gate = new();
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private int _lastId;
    private Task? _socketLoop;
    private ClientWebSocket? _socket;       // the loop's current socket, connected or not
    private ClientWebSocket? _ackedSocket;  // the same socket once the service acknowledged it
    private volatile bool _disposed;

    // Socket loop only (one loop, so no lock).
    private int _reconnectAttempt;
    private bool _socketDownReported;

    // Log rate limits: the HTTP side logs a change between answering and not, and each distinct
    // problem text once, so a session without the sim does not fill debug.log.
    private int _httpDown;
    private readonly HashSet<string> _loggedProblems = new(StringComparer.Ordinal);

    /// <param name="handler">For tests: answers the HTTP calls instead of the network. The service
    /// disposes only a handler it created itself.</param>
    public FenixDataService(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler == null)
        {
            Timeout = HttpTimeout
        };
    }

    // ---- Reads, writes and queries (HTTP GraphQL) -----------------------------------------------

    /// <summary>One data ref's value, or <see cref="FenixDataValue.Unknown"/> when the service has no
    /// such name or did not answer.</summary>
    public async Task<FenixDataValue> ReadAsync(string name, CancellationToken ct = default)
    {
        if (name == null) return FenixDataValue.Unknown;
        var values = await ReadManyAsync(new[] { name }, ct).ConfigureAwait(false);
        return values.TryGetValue(name, out var value) ? value : FenixDataValue.Unknown;
    }

    /// <summary>
    /// Many data refs in one request. Every name asked for is in the answer;
    /// one the service has no value for, or every name when it did not answer, maps to
    /// <see cref="FenixDataValue.Unknown"/>. No names: an empty dictionary, and no request.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, FenixDataValue>> ReadManyAsync(
        IReadOnlyList<string> names, CancellationToken ct = default)
    {
        var wanted = names == null ? new List<string>() : names.Where(n => n != null).ToList();
        if (wanted.Count == 0) return new Dictionary<string, FenixDataValue>();

        var data = await QueryAsync(FenixDataQueries.BuildReadMany(wanted), null, ct).ConfigureAwait(false);
        try
        {
            return FenixDataQueries.ParseReadMany(data, wanted);
        }
        catch (Exception ex)
        {
            LogProblem($"Unreadable data-ref answer: {ex.GetType().Name}: {ex.Message}");
            return FenixDataQueries.ParseReadMany(null, wanted);
        }
    }

    /// <summary>Writes an Int data ref; true only when the service answered 200 without errors and did not
    /// answer the write false.</summary>
    public Task<bool> WriteIntAsync(string name, int value, CancellationToken ct = default)
        => WriteAsync("Int", name, value, ct);

    /// <summary>Writes a Boolean data ref; true only when the service answered 200 without errors and did
    /// not answer the write false.</summary>
    public Task<bool> WriteBoolAsync(string name, bool value, CancellationToken ct = default)
        => WriteAsync("Bool", name, value, ct);

    /// <summary>
    /// Writes a Float data ref (an invariant decimal point under any PC culture); true only when the
    /// service answered 200 without errors and did not answer the write false. NaN or infinity is never
    /// sent and gives false.
    /// </summary>
    public Task<bool> WriteFloatAsync(string name, double value, CancellationToken ct = default)
    {
        if (double.IsFinite(value)) return WriteAsync("Float", name, value, ct);
        LogProblem($"Refused to write a number that is not finite to {name}");
        return Task.FromResult(false);
    }

    /// <summary>Writes a String data ref; true only when the service answered 200 without errors and did
    /// not answer the write false.</summary>
    public Task<bool> WriteStringAsync(string name, string value, CancellationToken ct = default)
        => value == null ? Task.FromResult(false) : WriteAsync("String", name, value, ct);

    /// <summary>
    /// Runs a GraphQL query or mutation and returns its <c>data</c> object, or null when the service
    /// did not answer 200, the answer carries <c>errors</c>, or it has no <c>data</c> object.
    /// <paramref name="variables"/> (an anonymous object, a dictionary or a JObject) is sent as the
    /// variables map; null sends none.
    /// </summary>
    public async Task<JObject?> QueryAsync(string query, object? variables = null, CancellationToken ct = default)
    {
        if (_disposed) return null;
        string body;
        try
        {
            var request = new JObject { ["query"] = query };
            if (variables != null)
                request["variables"] = variables as JToken ?? JToken.FromObject(variables);
            body = request.ToString(Formatting.None);
        }
        catch (Exception ex)
        {
            LogProblem($"Query variables could not be serialized: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        var answer = await PostGraphQlAsync(body, ct).ConfigureAwait(false);
        return answer?["data"] as JObject;
    }

    /// <summary>Every setting row of the Fenix settings API; empty when the service did not answer.</summary>
    public async Task<IReadOnlyList<FenixSetting>> GetSettingsAsync(CancellationToken ct = default)
    {
        var data = await QueryAsync(FenixDataQueries.SettingsQuery, null, ct).ConfigureAwait(false);
        try
        {
            return FenixDataQueries.ParseSettings(data);
        }
        catch (Exception ex)
        {
            LogProblem($"Unreadable settings answer: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<FenixSetting>();
        }
    }

    /// <summary>The Fenix software version (for example "3.0.0.7426"), or null when the service did not answer.</summary>
    public async Task<string?> GetSoftwareVersionAsync(CancellationToken ct = default)
    {
        var data = await QueryAsync(FenixDataQueries.SettingsQuery, null, ct).ConfigureAwait(false);
        try
        {
            return FenixDataQueries.ParseSoftwareVersion(data);
        }
        catch (Exception ex)
        {
            LogProblem($"Unreadable settings answer: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // ---- REST -----------------------------------------------------------------------------------

    /// <summary>
    /// GETs a REST path of the gateway (it starts with "/", for example "/fenix/failures/manual") and
    /// returns the response body, or null when the service did not answer with a 2xx status.
    /// </summary>
    public Task<string?> GetRestAsync(string path, CancellationToken ct = default)
        => RestAsync(HttpMethod.Get, path, null, ct);

    /// <summary>
    /// POSTs <paramref name="jsonBody"/> with <c>Content-Type: application/json</c> (as the EFB's own
    /// calls send it) to a REST path of the gateway (it starts with "/") and returns the response body,
    /// or null when the service did not answer with a 2xx status.
    /// </summary>
    public Task<string?> PostRestAsync(string path, string jsonBody, CancellationToken ct = default)
        => RestAsync(HttpMethod.Post, path, jsonBody ?? "", ct);

    // ---- Subscriptions (one shared WebSocket) ---------------------------------------------------

    /// <summary>
    /// Subscribes to changes of the named data refs. All subscriptions share one socket, which opens
    /// on the first subscription, reconnects after 3, 6, 12 and then every 30 s while the service is
    /// down, and subscribes every live subscription again on each reconnect, so values flow again
    /// once the Fenix is back. Disposing the returned handle ends the subscription; a callback
    /// already running, or one value already on its way, may still finish after it.
    /// </summary>
    /// <remarks>
    /// <paramref name="onChange"/> runs on a thread-pool thread (the socket loop), never the UI
    /// thread: a form marshals to its own thread. An exception from it is logged and swallowed.
    /// After <see cref="Dispose"/>, or with no names, the handle does nothing.
    /// <para>When the service itself ends a subscription (it sends <c>complete</c> or <c>error</c> for
    /// it), the subscription stays registered but receives nothing more until the socket next
    /// reconnects, which subscribes it again. Nothing tells the caller.</para>
    /// </remarks>
    public IDisposable Subscribe(IReadOnlyList<string> names, Action<string, FenixDataValue> onChange)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(onChange);

        string[] wanted = names.Where(n => !string.IsNullOrEmpty(n)).ToArray();
        if (wanted.Length == 0) return NoSubscription.Instance;

        Subscription subscription;
        ClientWebSocket? live;
        lock (_gate)
        {
            if (_disposed) return NoSubscription.Instance;
            _lastId++;
            subscription = new Subscription(this, _lastId.ToString(CultureInfo.InvariantCulture), wanted, onChange);
            _subscriptions.Add(subscription.Id, subscription);
            // Not yet acknowledged: the loop subscribes everything in the dictionary once it is.
            live = _ackedSocket;
            var token = _lifetime.Token;
            _socketLoop ??= Task.Run(() => RunSocketLoopAsync(token));
        }

        if (live != null)
            _ = SendQuietlyAsync(live, BuildSubscribeMessage(subscription.Id, subscription.Names));
        return subscription;
    }

    /// <summary>
    /// Stops the socket loop, ends every subscription and releases the HTTP client. Never throws;
    /// a second call does nothing. Afterwards reads give Unknown, writes false, queries null, and
    /// <see cref="Subscribe"/> a handle that does nothing.
    /// </summary>
    public void Dispose()
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var subscription in _subscriptions.Values)
                subscription.Closed = true;
            _subscriptions.Clear();
            socket = _socket;
            _ackedSocket = null;
        }

        // Cancelling ends the loop; aborting the socket ends a receive at once instead of a 2 s
        // close handshake on the thread that switched aircraft. The gateway drops the subscriptions
        // with the connection.
        try { _lifetime.Cancel(); } catch (Exception) { }
        try { socket?.Abort(); } catch (Exception) { }
        try { _http.Dispose(); } catch (Exception) { }
    }

    /// <summary>True once Dispose has run, so every call answers its failure value without a request. Read by
    /// the definition's background start (<c>EnsureFenixBackground</c>) and by the tests.</summary>
    internal bool IsDisposed => _disposed;

    // ---- Socket message helpers (pure; internal for the tests) ----------------------------------

    /// <summary>The <c>subscribe</c> message for one subscription.</summary>
    internal static string BuildSubscribeMessage(string id, IReadOnlyList<string> names)
        => new JObject
        {
            ["id"] = id,
            ["type"] = "subscribe",
            ["payload"] = new JObject
            {
                ["query"] = FenixDataQueries.SubscriptionQuery,
                ["variables"] = new JObject { ["names"] = JArray.FromObject(names) }
            }
        }.ToString(Formatting.None);

    /// <summary>The <c>complete</c> message that ends one subscription.</summary>
    internal static string BuildCompleteMessage(string id)
        => new JObject { ["id"] = id, ["type"] = "complete" }.ToString(Formatting.None);

    /// <summary>
    /// A JSON object from the service (a socket message or an HTTP answer), or null when the text is
    /// not one. Strings stay strings: a date-like value keeps its exact text.
    /// </summary>
    internal static JObject? ParseMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(reader) as JObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The name/value pairs of a <c>next</c> message's <c>payload.data.dataRefs</c>, which the service
    /// sends as one <c>{name, value}</c> object (an array of them is read too). An entry without a
    /// name is skipped; a value the service has none for reads as <see cref="FenixDataValue.Unknown"/>.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, FenixDataValue>> ReadNextEntries(JObject message)
    {
        var entries = new List<KeyValuePair<string, FenixDataValue>>();
        var dataRefs = ((message["payload"] as JObject)?["data"] as JObject)?["dataRefs"];
        if (dataRefs is JObject single)
        {
            AddEntry(single);
        }
        else if (dataRefs is JArray many)
        {
            foreach (var item in many)
            {
                if (item is JObject entry)
                    AddEntry(entry);
            }
        }
        return entries;

        void AddEntry(JObject entry)
        {
            if (TextOf(entry["name"]) is not { Length: > 0 } name) return;
            FenixDataValue value;
            try { value = FenixDataValue.FromJson(entry["value"]); }
            catch (Exception) { value = FenixDataValue.Unknown; }
            entries.Add(new KeyValuePair<string, FenixDataValue>(name, value));
        }
    }

    // ---- HTTP internals -------------------------------------------------------------------------

    private async Task<bool> WriteAsync(string kind, string name, object value, CancellationToken ct)
    {
        if (_disposed || name == null) return false;
        string body;
        try
        {
            body = FenixDataQueries.BuildWriteBody(kind, name, value);
        }
        catch (Exception ex)
        {
            LogProblem($"Write body for {name} could not be built: {ex.Message}");
            return false;
        }
        var answer = await PostGraphQlAsync(body, ct).ConfigureAwait(false);
        if (answer == null) return false;

        // The mutation answers data.dataRef.write<Kind>. Only an explicit JSON false there is a refusal;
        // anything else, a missing field included, is taken as accepted.
        var result = ((answer["data"] as JObject)?["dataRef"] as JObject)?["write" + kind];
        if (result is { Type: JTokenType.Boolean } && !(bool)result)
        {
            LogProblem($"The data service refused the write to {name}");
            return false;
        }
        return true;
    }

    // The whole answer object of a GraphQL POST, or null unless it is 200, JSON and free of errors.
    private async Task<JObject?> PostGraphQlAsync(string body, CancellationToken ct)
    {
        var answer = await SendHttpAsync(HttpMethod.Post, GraphQlUrl, body, ct).ConfigureAwait(false);
        if (answer is not { } reply) return null;

        // Whatever shape the answer has, nothing here reaches the caller as an exception.
        try
        {
            var json = ParseMessage(reply.Body);
            string? error = json == null ? null : FirstError(json);
            if (reply.Status != HttpStatusCode.OK)
            {
                LogProblem($"GraphQL answered HTTP {(int)reply.Status}{(error != null ? ": " + error : "")}");
                return null;
            }
            if (json == null)
            {
                LogProblem("GraphQL answer was not a JSON object");
                return null;
            }
            if (error != null)
            {
                LogProblem($"GraphQL errors: {error}");
                return null;
            }
            return json;
        }
        catch (Exception ex)
        {
            LogProblem($"Unreadable GraphQL answer: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> RestAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        if (_disposed) return null;
        // Only a leading "/" keeps the call on 127.0.0.1:8083 ("@host/x" would make it a user name).
        if (path == null || !path.StartsWith('/'))
        {
            LogProblem($"Refused a REST path that does not start with \"/\": {path}");
            return null;
        }

        var answer = await SendHttpAsync(method, RestBase + path, body, ct).ConfigureAwait(false);
        if (answer is not { } reply) return null;
        int status = (int)reply.Status;
        if (status is < 200 or > 299)
        {
            LogProblem($"{method} {path.Split('?')[0]} answered HTTP {status}");
            return null;
        }
        return reply.Body;
    }

    // One HTTP exchange: the status and body when the service answered at all, null when it did not
    // (refused, timed out, cancelled, disposed). Never throws.
    private async Task<(HttpStatusCode Status, string Body)?> SendHttpAsync(
        HttpMethod method, string url, string? jsonBody, CancellationToken ct)
    {
        if (_disposed) return null;
        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (jsonBody != null)
            {
                request.Content = new StringContent(jsonBody, Encoding.UTF8);
                // Bare "application/json", no charset: what the EFB's own calls send.
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (Interlocked.Exchange(ref _httpDown, 0) == 1)
                Log.Debug(LogCategory, "Data service answering again");
            return (response.StatusCode, text);
        }
        catch (Exception ex)
        {
            // A caller's cancellation and our own Dispose are not the service being down.
            if (!ct.IsCancellationRequested && !_disposed && ex is not ObjectDisposedException
                && Interlocked.Exchange(ref _httpDown, 1) == 0)
            {
                Log.Debug(LogCategory, $"Data service not answering ({ex.GetType().Name}: {ex.Message})");
            }
            return null;
        }
    }

    // The first entry of a non-empty "errors" as text, or null when there is none. Type-safe: an entry
    // that is not an object ("x", null) is still an error, and indexing a JValue would throw.
    private static string? FirstError(JObject answer)
    {
        var errors = answer["errors"];
        if (errors == null || errors.Type == JTokenType.Null) return null;
        if (errors is JArray list)
        {
            if (list.Count == 0) return null;
            var first = list[0];
            return first is JObject error
                ? TextOf(error["message"]) ?? error.ToString(Formatting.None)
                : first.ToString(Formatting.None);
        }
        return errors.ToString(Formatting.None);
    }

    private static string? TextOf(JToken? token)
        => token is { Type: JTokenType.String } ? (string?)token : null;

    // Each distinct problem text once (a poll that fails the same way every 2 s logs it once).
    private void LogProblem(string problem)
    {
        lock (_loggedProblems)
        {
            if (_loggedProblems.Contains(problem)) return;
            if (_loggedProblems.Count >= MaxRememberedProblems) _loggedProblems.Clear();
            _loggedProblems.Add(problem);
        }
        Log.Debug(LogCategory, problem);
    }

    // ---- Socket loop ----------------------------------------------------------------------------

    private async Task RunSocketLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string failure;
                using (var socket = new ClientWebSocket())
                {
                    socket.Options.AddSubProtocol("graphql-transport-ws");
                    lock (_gate)
                    {
                        if (_disposed) return;
                        _socket = socket;
                    }

                    try
                    {
                        await RunConnectionAsync(socket, ct).ConfigureAwait(false);
                        failure = "the data service closed the connection";
                    }
                    catch (Exception ex)
                    {
                        // Cancelled: Dispose aborted the socket, so whatever this is, it is not news.
                        if (ct.IsCancellationRequested) return;
                        failure = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (_socket == socket) _socket = null;
                            if (_ackedSocket == socket) _ackedSocket = null;
                        }
                    }
                }

                if (ct.IsCancellationRequested) return;
                int delay = ReconnectDelaysMs[Math.Min(_reconnectAttempt, ReconnectDelaysMs.Length - 1)];
                _reconnectAttempt++;
                if (!_socketDownReported)
                {
                    _socketDownReported = true;
                    Log.Debug(LogCategory, $"Subscription socket down ({failure}); retrying quietly every 3 to 30 s");
                }
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposed.
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"Subscription socket loop stopped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // One connection: open, connection_init, wait for connection_ack, subscribe everything live, then
    // receive until the socket closes (returns) or fails (throws).
    private async Task RunConnectionAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using (var opening = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            opening.CancelAfter(SocketOpenTimeout);
            await socket.ConnectAsync(new Uri(SocketUrl), opening.Token).ConfigureAwait(false);
            await SendAsync(socket, "{\"type\":\"connection_init\"}", opening.Token).ConfigureAwait(false);
            while (true)
            {
                string? text = await ReceiveTextAsync(socket, buffer, opening.Token).ConfigureAwait(false);
                if (text == null) return;
                string? type = ParseMessage(text) is { } message ? TextOf(message["type"]) : null;
                if (type == "connection_ack") break;
                if (type == "ping")
                    await SendAsync(socket, "{\"type\":\"pong\"}", opening.Token).ConfigureAwait(false);
                else
                    throw new InvalidOperationException($"expected connection_ack, got {type ?? "no type"}");
            }
        }

        string[] subscribes;
        lock (_gate)
        {
            if (_disposed) return;
            _ackedSocket = socket;
            // A Subscribe after this lock sees _ackedSocket and sends its own; one before is in here.
            subscribes = _subscriptions.Values.Select(s => BuildSubscribeMessage(s.Id, s.Names)).ToArray();
        }
        _reconnectAttempt = 0;
        _socketDownReported = false;
        Log.Debug(LogCategory, $"Subscription socket connected ({subscribes.Length} subscription(s))");

        foreach (string subscribe in subscribes)
            await SendAsync(socket, subscribe, ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            string? text = await ReceiveTextAsync(socket, buffer, ct).ConfigureAwait(false);
            if (text == null) return;
            if (ParseMessage(text) is { } message)
                await HandleMessageAsync(socket, message, ct).ConfigureAwait(false);
        }
    }

    private async Task HandleMessageAsync(ClientWebSocket socket, JObject message, CancellationToken ct)
    {
        string? type = TextOf(message["type"]);
        string? id = TextOf(message["id"]);
        switch (type)
        {
            case "next":
                await DeliverAsync(socket, id, message).ConfigureAwait(false);
                break;
            case "ping":
                // graphql-transport-ws keepalive: the service pings about every 15 s.
                await SendAsync(socket, "{\"type\":\"pong\"}", ct).ConfigureAwait(false);
                break;
            case "error":
            case "complete":
                // The subscription stays registered and is sent again on the next reconnect.
                string detail = type == "error" ? ": " + (message["payload"]?.ToString(Formatting.None) ?? "") : "";
                Log.Debug(LogCategory, $"Subscription {id} {type} from the service{detail}");
                break;
        }
    }

    private async Task DeliverAsync(ClientWebSocket socket, string? id, JObject message)
    {
        if (id == null) return;
        Subscription? subscription;
        lock (_gate) _subscriptions.TryGetValue(id, out subscription);
        if (subscription == null)
        {
            // A subscription disposed while its subscribe was on the way: end it at the service too.
            await SendQuietlyAsync(socket, BuildCompleteMessage(id)).ConfigureAwait(false);
            return;
        }

        var entries = ReadNextEntries(message);
        if (!subscription.ErrorLogged && message["payload"] is JObject payload && FirstError(payload) is { } error)
        {
            subscription.ErrorLogged = true;
            Log.Debug(LogCategory, $"Subscription {id} errors from the service: {error}");
        }

        foreach (var (name, value) in entries)
        {
            if (subscription.Closed) return;
            try
            {
                subscription.OnChange(name, value);
            }
            catch (Exception ex)
            {
                if (!subscription.CallbackFailureLogged)
                {
                    subscription.CallbackFailureLogged = true;
                    Log.Debug(LogCategory, $"Subscription {id} callback threw on {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    private async Task SendAsync(ClientWebSocket socket, string text, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // A send outside the loop's own sequence (a caller's subscribe or complete): best effort, since
    // a reconnect subscribes everything live again anyway.
    private async Task SendQuietlyAsync(ClientWebSocket socket, string text)
    {
        try
        {
            if (_disposed || socket.State != WebSocketState.Open) return;
            await SendAsync(socket, text, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The loop sees the same broken socket and reconnects.
        }
    }

    // One whole text message, or null when the service closed the socket.
    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    private void Unsubscribe(Subscription subscription)
    {
        try
        {
            ClientWebSocket? live;
            lock (_gate)
            {
                if (subscription.Closed) return;
                subscription.Closed = true;
                _subscriptions.Remove(subscription.Id);
                live = _ackedSocket;
            }
            if (live != null)
                _ = SendQuietlyAsync(live, BuildCompleteMessage(subscription.Id));
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"Unsubscribe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly FenixDataService _owner;

        public Subscription(FenixDataService owner, string id, string[] names, Action<string, FenixDataValue> onChange)
        {
            _owner = owner;
            Id = id;
            Names = names;
            OnChange = onChange;
        }

        public string Id { get; }
        public string[] Names { get; }
        public Action<string, FenixDataValue> OnChange { get; }
        public volatile bool Closed;
        // Socket loop only.
        public bool ErrorLogged;
        public bool CallbackFailureLogged;

        public void Dispose() => _owner.Unsubscribe(this);
    }

    private sealed class NoSubscription : IDisposable
    {
        public static readonly NoSubscription Instance = new();
        public void Dispose() { }
    }
}
