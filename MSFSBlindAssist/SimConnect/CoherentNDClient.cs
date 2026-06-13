using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads (and drives) the FlyByWire A380X ND OANS / BTV (Brake-To-Vacate) — DATA-ONLY,
    /// ZERO-RENDER — through the MSFS Coherent GT debugger (127.0.0.1:19999), resolved to the
    /// Captain ND Coherent view (title "A380X_ND_1"). NO injection. Runs coherent-oans-agent.js
    /// (window.__MSFSBA_OANS) inside the ND's JS context.
    ///
    /// CRITICAL: this client NEVER renders or zooms the airport map. The agent reads OANS/BTV
    /// state directly from the ND JS instance objects (oansRef.instance.btvUtils / .labelManager /
    /// .fmsDataStore / .dataAirportIcao) plus OANS L:vars, and drives BTV via btvUtils methods +
    /// the msfs-sdk EventBus. It does NOT write A32NX_EFIS_L_ND_RANGE, force panel visibility, or
    /// scrape the control-panel DOM. (The previous "ensureAirportZoom + DOM scrape" design forced
    /// a continuous full-airport map render every poll and exhausted host memory — do not revive it.)
    ///
    /// PollOnce evaluates __MSFSBA_OANS.snapshot() (read-only) every PollIntervalMs and raises a
    /// dedup'd "oans_state" push (plus "oans_connected"); FBWA380OansForm consumes those.
    /// Command surface: get_snapshot, oans_arm_runway, oans_arm_exit, oans_clear,
    /// oans_display_airport (one-shot user load), oans_set_manual_stop (no-Navigraph manual BTV).
    /// </summary>
    public sealed class CoherentNDClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string NdTitleNeedle = "A380X_ND_1";
        private const int PollIntervalMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        private const char OptionSeparator = (char)0x1f;

        public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;
        public event Action<string>? Error;

        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private int _msgId;
        private volatile bool _connected;
        private volatile bool _agentInstalled;
        private DateTime _lastGoodScrapeUtc = DateTime.MinValue;
        private bool _connectedPushSent;
        private string _lastElementsHash = "";
        private bool _disposed;

        public bool IsBridgeConnected =>
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 6;

        public CoherentNDClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null)
            {
                // Stop() cancels _cts but intentionally does not null it (RunLoop may still be
                // unwinding on it), so Start() after Stop() used to be a SILENT no-op. Restart
                // is not a supported lifecycle — every call site dispose-and-recreates — so
                // fail loudly in Debug and log in Release rather than half-support it.
                if (_cts.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "CoherentNDClient.Start() called after Stop() — not supported; create a new instance.");
                    System.Diagnostics.Debug.Assert(false,
                        "CoherentNDClient: Start() after Stop() is a no-op. Dispose and create a new client instead.");
                }
                return;
            }
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-oans-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load OANS agent script: {ex.Message}");
            }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null;
            _connected = false;
            _agentInstalled = false;
        }

        // ---- IMcduBridge command surface --------------------------------

        public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
        {
            string? expr = BuildCommandExpression(command, payload);
            if (expr == null) return;
            // The BTV arm/clear/manual commands return a status string (e.g. "Armed BTV exit K1" or
            // "Exit K1 not valid for this runway …"). Surface it so a rejected exit is ANNOUNCED
            // rather than failing silently (the form would otherwise just keep saying "none selected").
            bool report = command is "oans_arm_runway" or "oans_arm_exit" or "oans_clear" or "oans_set_manual_stop";
            _ = Task.Run(async () =>
            {
                try
                {
                    string res = await EvalAsync(expr);
                    if (report && !string.IsNullOrWhiteSpace(res))
                        Raise("oans_action", new Dictionary<string, string> { ["result"] = res });
                }
                catch { /* a dropped command self-heals on the next poll */ }
            });
        }

        private string? BuildCommandExpression(string command, Dictionary<string, string>? payload)
        {
            string Val() => payload != null && payload.TryGetValue("value", out var v) ? v : "";

            switch (command)
            {
                case "get_snapshot":
                    // Form open / refresh: force a re-push on the next poll. NOTHING is rendered or forced.
                    _lastElementsHash = "";
                    return "window.__MSFSBA_OANS ? __MSFSBA_OANS.ping() : ''";
                // Accessible BTV — drive the OANS btvUtils directly (the runway-end / exit MAP LABELS a
                // sighted pilot clicks are unreachable to a screen reader). Pure data ops, no render.
                case "oans_arm_runway":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.armRunway({JsStr(Val())})";
                case "oans_arm_exit":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.armExit({JsStr(Val())})";
                case "oans_clear":
                    return "window.__MSFSBA_OANS && __MSFSBA_OANS.clearBtv()";
                case "oans_display_airport":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.displayAirport({JsStr(Val())})";
                case "oans_set_manual_stop":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.setManualStopDistance({JsInt(Val())})";
                default:
                    return null;
            }
        }

        private static string JsStr(string s) => JsonSerializer.Serialize(s);
        private static string JsInt(string s) => int.TryParse(s, out var n) ? n.ToString() : "0";

        // ---- connection + poll loop -------------------------------------

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await EnsureConnected(ct))
                    {
                        await Task.Delay(ReconnectDelayMs, ct);
                        continue;
                    }
                    await PollOnce(ct);
                    await Task.Delay(PollIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CoherentNDClient loop: {ex.Message}");
                    _connected = false;
                    _agentInstalled = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    // Fail any in-flight Runtime.evaluate calls now instead of letting
                    // them hang until their per-call timeout (the socket is dead).
                    foreach (var kv in _pending) kv.Value.TrySetCanceled();
                    _pending.Clear();
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            // Socket still OPEN but the agent went missing (an eval timed out / the page
            // re-evaluated) — re-install the agent on the SAME socket instead of reconnecting.
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf("MSFSBA_OANS_INSTALLED", StringComparison.Ordinal) >= 0;
                if (_agentInstalled) { _connected = true; return true; }
            }

            // CRITICAL: tear down any existing socket BEFORE opening a new one. Coherent GT
            // allows only ONE inspector connection per page — opening a SECOND socket while
            // the first is alive orphans the healthy one and blocks the page permanently.
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
                _agentInstalled = false;
            }

            int? pageId = await ResolveNdPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            try
            {
                // Per-attempt connect timeout (CoherentEvalClient pattern). Without it a
                // half-open debugger port can park ConnectAsync indefinitely — and in the
                // Display/EWD clients that wedges while HOLDING _connectLock.
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(url, connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout, NOT shutdown — must not surface as OperationCanceledException,
                // which the RunLoop catch treats as "stop": that would kill the loop forever.
                try { ws.Dispose(); } catch { }
                _connected = false;
                return false;
            }
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();   // cancel evals orphaned by the reconnect (else they hang to timeout)
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf("MSFSBA_OANS_INSTALLED", StringComparison.Ordinal) >= 0;
            _connected = _agentInstalled;
            return _agentInstalled;
        }

        private async Task<int?> ResolveNdPageId(CancellationToken ct)
        {
            try
            {
                string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var view in doc.RootElement.EnumerateArray())
                {
                    if (!view.TryGetProperty("title", out var titleEl)) continue;
                    string title = titleEl.GetString() ?? "";
                    if (title.IndexOf(NdTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveNdPageId: {ex.Message}");
            }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_OANS ? __MSFSBA_OANS.snapshot() : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _agentInstalled = false; return; }

            Snapshot? s;
            try { s = JsonSerializer.Deserialize<Snapshot>(raw); }
            catch { return; }
            if (s == null || !s.ok) return;

            _lastGoodScrapeUtc = DateTime.UtcNow;
            if (!_connectedPushSent) { _connectedPushSent = true; Raise("oans_connected", new Dictionary<string, string>()); }

            var b = s.btv ?? new BtvSnapshot();
            var sb = new StringBuilder();
            sb.Append(s.available ? '1' : '0').Append('|').Append(s.failed ? '1' : '0').Append('|')
              .Append(s.airport?.icao).Append('/').Append(s.airport?.name).Append('|')
              .Append(b.ready ? '1' : '0').Append('|').Append(b.runway).Append('/').Append(b.exit).Append('/')
              .Append(b.lda).Append('/').Append(b.exitDist).Append('/').Append(b.dry).Append('/').Append(b.wet).Append('/')
              .Append(b.stop).Append('/').Append(b.rot).Append('/').Append(b.turnMax).Append('/').Append(b.turnIdle).Append('/')
              .Append(b.rwyAheadQfu).Append('/').Append(b.metric ? '1' : '0').Append('/')
              .Append(b.computing ? '1' : '0').Append('|')
              .Append(string.Join(",", b.runways ?? new())).Append('|').Append(string.Join(",", b.exits ?? new())).Append('|')
              .Append(string.Join(",", b.exitDists ?? new())).Append('|')
              .Append(s.manual?.runwayLengthM).Append('/').Append(s.manual?.manualStopDist).Append('/')
              .Append(s.manual?.fmsLandingRunwaySelected == true ? '1' : '0').Append('|')
              .Append(s.fms?.origin).Append('/').Append(s.fms?.dest).Append('/').Append(s.fms?.altn).Append('|')
              .Append(s.airac);
            string hash = sb.ToString();
            if (hash == _lastElementsHash) return;
            _lastElementsHash = hash;

            var data = new Dictionary<string, string>
            {
                ["available"] = s.available ? "true" : "false",
                ["failed"] = s.failed ? "true" : "false",
                ["airport.icao"] = s.airport?.icao ?? "",
                ["airport.name"] = s.airport?.name ?? "",
                ["airac"] = s.airac ?? "",
                ["fms.origin"] = s.fms?.origin ?? "",
                ["fms.dest"] = s.fms?.dest ?? "",
                ["fms.altn"] = s.fms?.altn ?? "",
                ["btv.ready"] = b.ready ? "true" : "false",
                ["btv.runway"] = b.runway ?? "",
                ["btv.exit"] = b.exit ?? "",
                ["btv.lda"] = b.lda?.ToString() ?? "",
                ["btv.exitDist"] = b.exitDist?.ToString() ?? "",
                ["btv.dry"] = b.dry?.ToString() ?? "",
                ["btv.wet"] = b.wet?.ToString() ?? "",
                ["btv.stop"] = b.stop?.ToString() ?? "",
                ["btv.rot"] = b.rot?.ToString() ?? "",
                ["btv.turnMax"] = b.turnMax?.ToString() ?? "",
                ["btv.turnIdle"] = b.turnIdle?.ToString() ?? "",
                ["btv.rwyAheadQfu"] = b.rwyAheadQfu ?? "",
                ["btv.computing"] = b.computing ? "true" : "false",
                ["btv.metric"] = b.metric ? "true" : "false",
                ["manual.runwayLengthM"] = s.manual?.runwayLengthM?.ToString() ?? "",
                ["manual.manualStopDist"] = s.manual?.manualStopDist?.ToString() ?? "",
                ["manual.fmsLandingRunwaySelected"] = s.manual?.fmsLandingRunwaySelected == true ? "true" : "false"
            };
            if (b.runways is { Count: > 0 }) data["btv.runways"] = string.Join(OptionSeparator, b.runways);
            if (b.exits is { Count: > 0 }) data["btv.exits"] = string.Join(OptionSeparator, b.exits);
            if (b.exitDists is { Count: > 0 }) data["btv.exitDists"] = string.Join(OptionSeparator, b.exitDists);
            Raise("oans_state", data);
        }

        // ---- Runtime.evaluate over the inspector socket -----------------

        private Task<string> EvalAsync(string expression) => EvalAsync(expression, _cts?.Token ?? CancellationToken.None);

        private async Task<string> EvalAsync(string expression, CancellationToken ct)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return "";

            int id = Interlocked.Increment(ref _msgId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var msg = JsonSerializer.Serialize(new
            {
                id,
                method = "Runtime.evaluate",
                @params = new { expression, returnByValue = true }
            });

            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EvalTimeoutMs);
            using (timeout.Token.Register(() => tcs.TrySetCanceled()))
            {
                try { JsonElement root = await tcs.Task; return ExtractValue(root); }
                catch (OperationCanceledException) { return ""; }
                finally { _pending.TryRemove(id, out _); }
            }
        }

        private static string ExtractValue(JsonElement root)
        {
            if (root.TryGetProperty("result", out var outer)
                && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val))
            {
                return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
            }
            return "";
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[131072];
            // Accumulate raw bytes and decode once at EndOfMessage — decoding each read
            // separately corrupts a multibyte UTF-8 char split across the read boundary.
            var ms = new System.IO.MemoryStream();
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            _connected = false; _agentInstalled = false;
                            return;
                        }
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CoherentNDClient receive: {ex.Message}");
            }
            finally
            {
                _connected = false; _agentInstalled = false;
            }
        }

        private void DispatchMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id))
                {
                    if (_pending.TryGetValue(id, out var tcs))
                        tcs.TrySetResult(root.Clone());
                }
            }
            catch { }
        }

        private void Raise(string type, Dictionary<string, string> data)
        {
            var args = new EFBStateUpdateEventArgs { Type = type, Data = data };
            if (_syncContext != null) _syncContext.Post(_ => StateUpdated?.Invoke(this, args), null);
            else StateUpdated?.Invoke(this, args);
        }

        private void RaiseError(string message)
        {
            if (_syncContext != null) _syncContext.Post(_ => Error?.Invoke(message), null);
            else Error?.Invoke(message);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            _http.Dispose();
            // Intentionally NOT disposing _sendLock: the background RunLoop is not joined here and
            // may be pending on WaitAsync — disposing a SemaphoreSlim with waiters throws
            // ObjectDisposedException on the pool thread. Stop() cancels _cts, which unblocks the
            // waiter; the wait handle is never materialized, so nothing leaks.
        }

        private sealed class Snapshot
        {
            public bool ok { get; set; }
            public bool available { get; set; }
            public bool failed { get; set; }
            public string? airac { get; set; }
            public Airport? airport { get; set; }
            public Fms? fms { get; set; }
            public BtvSnapshot? btv { get; set; }
            public Manual? manual { get; set; }
        }

        private sealed class Airport { public string? icao { get; set; } public string? name { get; set; } }

        private sealed class Fms
        {
            public string? origin { get; set; }
            public string? dest { get; set; }
            public string? altn { get; set; }
            public string? landingRunway { get; set; }
        }

        private sealed class Manual
        {
            public int? runwayLengthM { get; set; }
            public int? manualStopDist { get; set; }
            public bool fmsLandingRunwaySelected { get; set; }
        }

        private sealed class BtvSnapshot
        {
            public bool ready { get; set; }
            public List<string>? runways { get; set; }
            public List<string>? exits { get; set; }
            public List<int>? exitDists { get; set; }
            public string? runway { get; set; }
            public string? exit { get; set; }
            public int? lda { get; set; }
            public int? exitDist { get; set; }
            public double? bearing { get; set; }
            public int? dry { get; set; }
            public int? wet { get; set; }
            public int? stop { get; set; }
            public int? rot { get; set; }
            public int? turnMax { get; set; }
            public int? turnIdle { get; set; }
            public string? rwyAheadQfu { get; set; }
            public bool computing { get; set; }
            public bool metric { get; set; }
        }
    }
}
