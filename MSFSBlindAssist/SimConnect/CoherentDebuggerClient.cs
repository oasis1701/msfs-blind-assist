using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// The surface the FBW A380X MCDU window depends on. Both the legacy
    /// injection server (EFBBridgeServer) and the new no-injection
    /// CoherentDebuggerClient implement it, so the form is agnostic to which
    /// transport is wired underneath.
    /// </summary>
    public interface IMcduBridge
    {
        event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;
        bool IsBridgeConnected { get; }
        void EnqueueCommand(string command, Dictionary<string, string>? payload = null);
    }

    /// <summary>
    /// Reads (and drives) the FlyByWire A380X MFD/FMS through the MSFS
    /// Coherent GT debugger — the WebKit-Inspector endpoint MSFS exposes on
    /// 127.0.0.1:19999. (Verified on MSFS 2024 cold boot: this is open with
    /// Developer Mode OFF — the sim opens the port itself, no dev toolbar.)
    /// NO Community-folder injection and
    /// NO patched HTML: we connect to the live MFD view's WebSocket and run
    /// our scrape/input agent via Runtime.evaluate inside its own JS context
    /// (where SimVar/Coherent and the MFD DOM are all reachable).
    ///
    /// Page ids are assigned as views spawn and shift between sim restarts,
    /// so the MFD view is resolved BY TITLE ("A380X_MFD") from /pagelist.json
    /// every time we (re)connect — never hardcoded.
    ///
    /// Implements IMcduBridge so FBWA380MCDUForm can consume it exactly like
    /// the old EFBBridgeServer: it raises the same fbwa380_mcdu_screen /
    /// fbwa380_mcdu_elements / fbwa380_mcdu_connected state pushes and accepts
    /// the same command vocabulary, translating each into an agent call.
    /// </summary>
    public sealed class CoherentDebuggerClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string MfdTitleNeedle = "A380X_MFD";
        private const int PollIntervalMs = 350;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;

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
        private int _mcduIndex = 1;
        private volatile bool _connected;
        private volatile bool _agentInstalled;
        private DateTime _lastGoodScrapeUtc = DateTime.MinValue;
        private bool _connectedPushSent;
        private string _lastScreenHash = "";
        private string _lastElementsHash = "";
        private bool _disposed;

        public bool IsBridgeConnected =>
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 5;

        public CoherentDebuggerClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-a380-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load A380 agent script: {ex.Message}");
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
            // Translate the form's command vocabulary into an agent call and
            // fire it (fire-and-forget; the next poll reflects the result).
            string? expr = BuildCommandExpression(command, payload);
            if (expr == null) return;
            _ = Task.Run(async () =>
            {
                try { await EvalAsync(expr); }
                catch { /* a dropped command self-heals on the next poll */ }
            });
        }

        private string? BuildCommandExpression(string command, Dictionary<string, string>? payload)
        {
            string Idx() => payload != null && payload.TryGetValue("index", out var i) ? i : "0";
            string Text() => payload != null && payload.TryGetValue("text", out var t) ? t : "";

            switch (command)
            {
                case "get_mcdu_elements":
                    // Force the next poll to re-push the current screen + elements
                    // even if nothing changed. Without this, a form that opens
                    // mid-session (after the client has already scraped and set
                    // the hashes) gets no push until the MFD content next changes
                    // — so it sits empty on first open.
                    _lastScreenHash = ""; _lastElementsHash = "";
                    return null;
                case "select_mcdu":
                    if (payload != null && payload.TryGetValue("mcdu", out var m) && int.TryParse(m, out var mi))
                        _mcduIndex = mi == 2 ? 2 : 1;
                    _lastScreenHash = ""; _lastElementsHash = "";
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.setMcdu({_mcduIndex})";
                case "click_mcdu_element":
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.clickElement({JsInt(Idx())})";
                case "send_to_field":
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.sendToField({JsInt(Idx())},{JsStr(Text())})";
                case "send_scratchpad":
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.sendScratchpad({JsStr(Text())})";
                case "type_key":
                    var key = payload != null && payload.TryGetValue("key", out var k) ? k : "";
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.fireKey({JsStr(key)})";
                case "navigate":
                    // Click the on-screen MFD control whose label matches, with an
                    // optional KCCU key fallback. After navigating, force a re-push
                    // so the new page's title/rows/elements arrive immediately.
                    var navLabel = payload != null && payload.TryGetValue("label", out var nl) ? nl : "";
                    var navKey = payload != null && payload.TryGetValue("key", out var nk) ? nk : "";
                    _lastScreenHash = ""; _lastElementsHash = "";
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.navigate({JsStr(navLabel)},{JsStr(navKey)})";
                case "navigate_by_id":
                    // Click a page-selector menu item by its stable element id
                    // ({CAPT|FO}_MFD_pageSelector{prefix}_{index}); KCCU key is the
                    // cross-system fallback. The reliable navigation path.
                    var navPrefix = payload != null && payload.TryGetValue("prefix", out var np) ? np : "";
                    var navIndex = payload != null && payload.TryGetValue("index", out var nix) && int.TryParse(nix, out var nixv) ? nixv : -1;
                    var navIdKey = payload != null && payload.TryGetValue("key", out var nik) ? nik : "";
                    _lastScreenHash = ""; _lastElementsHash = "";
                    return $"window.__MSFSBA_A380 && __MSFSBA_A380.navigateById({JsStr(navPrefix)},{navIndex},{JsStr(navIdKey)})";
                default:
                    // page_* / key_* legacy navigation commands map to a KCCU key
                    // fallback via the agent (kept for compatibility).
                    if (command.StartsWith("page_") || command.StartsWith("key_"))
                    {
                        _lastScreenHash = ""; _lastElementsHash = "";
                        return $"window.__MSFSBA_A380 && __MSFSBA_A380.pageCommand({JsStr(command)})";
                    }
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
                    System.Diagnostics.Debug.WriteLine($"CoherentDebuggerClient loop: {ex.Message}");
                    _connected = false;
                    _agentInstalled = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            // Resolve the MFD page id by title (restart-proof).
            int? pageId = await ResolveMfdPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            await ws.ConnectAsync(url, ct);
            _ws = ws;
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            // Install the persistent agent into the page context.
            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf("MSFSBA_A380_INSTALLED", StringComparison.Ordinal) >= 0;
            _connected = _agentInstalled;
            return _agentInstalled;
        }

        private async Task<int?> ResolveMfdPageId(CancellationToken ct)
        {
            try
            {
                string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var view in doc.RootElement.EnumerateArray())
                {
                    if (!view.TryGetProperty("title", out var titleEl)) continue;
                    string title = titleEl.GetString() ?? "";
                    if (title.IndexOf(MfdTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveMfdPageId: {ex.Message}");
            }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync($"window.__MSFSBA_A380 ? __MSFSBA_A380.scrape({_mcduIndex}) : ''", ct);
            if (string.IsNullOrEmpty(raw))
            {
                // Agent gone (page reloaded) — force reinstall next round.
                _agentInstalled = false;
                return;
            }

            ScrapeResult? result;
            try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
            catch { return; }
            if (result == null || !result.ok) return;

            _lastGoodScrapeUtc = DateTime.UtcNow;
            if (!_connectedPushSent)
            {
                _connectedPushSent = true;
                Raise("fbwa380_mcdu_connected", new Dictionary<string, string>());
            }

            // Page title + scratchpad/footer -> fbwa380_mcdu_screen. Title is
            // announced once on change; the scratchpad is announced on change
            // (the hash keys on both so a scratchpad edit always pushes).
            string title = result.title ?? "";
            string scratchpad = result.scratchpad ?? "";
            string screenHash = result.mcdu + "|" + title + "|" + scratchpad;
            if (screenHash != _lastScreenHash)
            {
                _lastScreenHash = screenHash;
                Raise("fbwa380_mcdu_screen", new Dictionary<string, string>
                {
                    ["mcdu"] = result.mcdu.ToString(),
                    ["title"] = title,
                    ["scratchpad"] = scratchpad
                });
            }

            // The full one-per-line list (static text + interactive) ->
            // fbwa380_mcdu_elements (items.N.*). N is the display position;
            // items.N.index carries the agent's stable handle (0 = static text).
            var elements = result.elements ?? new List<ScrapeElement>();
            var sb = new StringBuilder(result.mcdu + "|" + elements.Count + "|");
            foreach (var e in elements) sb.Append(e.idx).Append(':').Append(e.text).Append('/').Append(e.value).Append('/').Append(e.disabled ? '1' : '0').Append('/').Append(e.expanded == true ? 'E' : e.expanded == false ? 'c' : '-').Append('|');
            string elHash = sb.ToString();
            if (elHash != _lastElementsHash)
            {
                _lastElementsHash = elHash;
                var data = new Dictionary<string, string>
                {
                    ["count"] = elements.Count.ToString(),
                    ["mcdu"] = result.mcdu.ToString()
                };
                for (int i = 0; i < elements.Count; i++)
                {
                    data[$"items.{i}.index"] = elements[i].idx.ToString();
                    data[$"items.{i}.text"] = elements[i].text ?? "";
                    data[$"items.{i}.kind"] = elements[i].kind ?? "";
                    data[$"items.{i}.value"] = elements[i].value ?? "";
                    data[$"items.{i}.disabled"] = elements[i].disabled ? "true" : "false";
                    // Combo-box open state: "expanded" / "collapsed" / "" (not a combo).
                    data[$"items.{i}.expandstate"] = elements[i].expanded == true ? "expanded"
                                                   : elements[i].expanded == false ? "collapsed" : "";
                }
                Raise("fbwa380_mcdu_elements", data);
            }
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
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally { _sendLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EvalTimeoutMs);
            using (timeout.Token.Register(() => tcs.TrySetCanceled()))
            {
                try
                {
                    JsonElement root = await tcs.Task;
                    return ExtractValue(root);
                }
                catch (OperationCanceledException) { return ""; }
                finally { _pending.TryRemove(id, out _); }
            }
        }

        private static string ExtractValue(JsonElement root)
        {
            // {"id":N,"result":{"result":{"type":"string","value":"..."},"wasThrown":false}}
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
            var sb = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (res.MessageType == WebSocketMessageType.Close)
                        {
                            _connected = false; _agentInstalled = false;
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                    } while (!res.EndOfMessage);

                    DispatchMessage(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CoherentDebuggerClient receive: {ex.Message}");
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
                    {
                        // Clone so the value survives past the JsonDocument dispose.
                        tcs.TrySetResult(root.Clone());
                    }
                }
                // Unsolicited protocol events (no matching id) are ignored.
            }
            catch { /* malformed frame — ignore */ }
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
            _sendLock.Dispose();
        }

        // ---- scrape DTOs -------------------------------------------------

        private sealed class ScrapeResult
        {
            public bool ok { get; set; }
            public int mcdu { get; set; }
            public string? title { get; set; }
            public string? scratchpad { get; set; }
            public string? error { get; set; }
            public List<ScrapeElement>? elements { get; set; }
        }

        private sealed class ScrapeElement
        {
            public int idx { get; set; }
            public string? kind { get; set; }
            public string? text { get; set; }
            public string? value { get; set; }
            public bool disabled { get; set; }
            // Tri-state for combo boxes: true = option list open, false = collapsed,
            // null = not a combo box (no state announced).
            public bool? expanded { get; set; }
        }
    }
}
