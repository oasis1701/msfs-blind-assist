using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads (and drives) the FlyByWire A380X ND OANS (Onboard Airport Navigation
    /// System) CONTROL PANEL — the airport-map controls used for BTV (Brake-To-
    /// Vacate) exit selection, runway/exit/airport search and LDG SHIFT — through
    /// the MSFS Coherent GT debugger (127.0.0.1:19999), resolved to the Captain ND
    /// Coherent view (title "A380X_ND_1"). NO injection. Runs coherent-oans-agent.js
    /// (window.__MSFSBA_OANS) inside the ND's JS context.
    ///
    /// The OANS control panel is built from the same MFD UI widgets as the MCDU
    /// (.mfd-input-field / .mfd-dropdown / .mfd-button / tabs), and the agent emits
    /// elements in the SAME shape the flyPad agent uses — so this client raises the
    /// identical fbw_efb_connected / fbw_efb_elements pushes and command
    /// vocabulary, letting FbwEfbForm (the WebView2 browser view) render it with
    /// no changes. The agent auto-opens the panel (it is toggled by the ND map
    /// "MAP DATA" context-menu item) and reports when the OANS is not yet available
    /// (no aircraft position / ADIRS not aligned).
    /// </summary>
    public sealed class CoherentNDClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string NdTitleNeedle = "A380X_ND_1";
        private const int PollIntervalMs = 500;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
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
            if (_cts != null) return;
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
            _ = Task.Run(async () =>
            {
                try { await EvalAsync(expr); }
                catch { /* a dropped command self-heals on the next poll */ }
            });
        }

        private string? BuildCommandExpression(string command, Dictionary<string, string>? payload)
        {
            string Idx() => payload != null && payload.TryGetValue("index", out var i) ? i : "0";
            string Val() => payload != null && payload.TryGetValue("value", out var v) ? v : "";

            switch (command)
            {
                case "get_display_elements":
                    // Form open / refresh: open the OANS control panel (it is toggled
                    // by the ND "MAP DATA" context-menu item) and force a re-push.
                    _lastElementsHash = "";
                    return "window.__MSFSBA_OANS && __MSFSBA_OANS.openPanel()";
                case "click_display_element":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.clickElement({JsInt(Idx())})";
                case "set_element_value":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.setValue({JsInt(Idx())},{JsStr(Val())})";
                // Accessible BTV exit arming — drive the OANS btvUtils directly (the runway-end /
                // exit MAP LABELS a sighted pilot clicks are unreachable to a screen reader). The
                // native OANS form sends these; the next poll's btv snapshot reflects the result.
                case "oans_btv_arm_runway":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.armBtvRunway({JsStr(Val())})";
                case "oans_btv_arm_exit":
                    return $"window.__MSFSBA_OANS && __MSFSBA_OANS.armBtvExit({JsStr(Val())})";
                case "oans_btv_clear":
                    return "window.__MSFSBA_OANS && __MSFSBA_OANS.clearBtv()";
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
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            int? pageId = await ResolveNdPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            await ws.ConnectAsync(url, ct);
            _ws = ws;
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
            string raw = await EvalAsync("window.__MSFSBA_OANS ? __MSFSBA_OANS.scrape() : ''", ct);
            if (string.IsNullOrEmpty(raw))
            {
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
                Raise("fbw_efb_connected", new Dictionary<string, string>());
            }

            var elements = result.elements ?? new List<ScrapeElement>();
            var sb = new StringBuilder((result.page ?? "") + "|" + elements.Count + "|");
            foreach (var e in elements)
                sb.Append(e.idx).Append(':').Append(e.text).Append('/').Append(e.value)
                  .Append('/').Append(e.controlType).Append('/').Append(e.clickable ? '1' : '0')
                  .Append('/').Append(e.kind).Append('/').Append(e.level)
                  .Append('/').Append(e.disabled ? '1' : '0').Append('|');
            // The BTV snapshot (armed runway/exit + predicted distances) must also trigger a push
            // when it changes — fold it into the dedup hash.
            var btv = result.btv;
            if (btv != null)
                sb.Append("BTV|").Append(btv.ready ? '1' : '0').Append('|')
                  .Append(btv.runway).Append('/').Append(btv.exit).Append('/')
                  .Append(btv.lda).Append('/').Append(btv.exitDist).Append('/')
                  .Append(btv.dry).Append('/').Append(btv.wet).Append('/').Append(btv.stop).Append('/')
                  .Append(btv.rot).Append('/').Append(btv.turnMax).Append('/').Append(btv.turnIdle).Append('/')
                  .Append(btv.metric ? '1' : '0').Append('/')
                  .Append(string.Join(",", btv.runways ?? new())).Append('/')
                  .Append(string.Join(",", btv.exits ?? new())).Append('|');
            string elHash = sb.ToString();
            if (elHash != _lastElementsHash)
            {
                _lastElementsHash = elHash;
                var data = new Dictionary<string, string>
                {
                    ["count"] = elements.Count.ToString(),
                    ["page"] = result.page ?? ""
                };
                for (int i = 0; i < elements.Count; i++)
                {
                    data[$"items.{i}.aidx"] = elements[i].idx.ToString();
                    data[$"items.{i}.text"] = elements[i].text ?? "";
                    data[$"items.{i}.tag"] = elements[i].tag ?? "";
                    data[$"items.{i}.role"] = elements[i].role ?? "";
                    data[$"items.{i}.value"] = elements[i].value ?? "";
                    data[$"items.{i}.type"] = elements[i].controlType ?? "";
                    data[$"items.{i}.clickable"] = elements[i].clickable ? "true" : "false";
                    data[$"items.{i}.kind"] = elements[i].kind ?? "";
                    data[$"items.{i}.level"] = elements[i].level.ToString();
                    data[$"items.{i}.live"] = elements[i].live ?? "";
                    data[$"items.{i}.disabled"] = elements[i].disabled ? "true" : "false";
                    if (elements[i].options is { Count: > 0 })
                        data[$"items.{i}.options"] = string.Join(OptionSeparator, elements[i].options!);
                }
                if (btv != null)
                {
                    data["btv.ready"] = btv.ready ? "true" : "false";
                    data["btv.runway"] = btv.runway ?? "";
                    data["btv.exit"] = btv.exit ?? "";
                    data["btv.lda"] = btv.lda?.ToString() ?? "";
                    data["btv.exitDist"] = btv.exitDist?.ToString() ?? "";
                    data["btv.dry"] = btv.dry?.ToString() ?? "";
                    data["btv.wet"] = btv.wet?.ToString() ?? "";
                    data["btv.stop"] = btv.stop?.ToString() ?? "";
                    data["btv.rot"] = btv.rot?.ToString() ?? "";
                    data["btv.turnMax"] = btv.turnMax?.ToString() ?? "";
                    data["btv.turnIdle"] = btv.turnIdle?.ToString() ?? "";
                    data["btv.metric"] = btv.metric ? "true" : "false";
                    data["btv.computing"] = btv.computing ? "true" : "false";
                    if (btv.runways is { Count: > 0 }) data["btv.runways"] = string.Join(OptionSeparator, btv.runways);
                    if (btv.exits is { Count: > 0 }) data["btv.exits"] = string.Join(OptionSeparator, btv.exits);
                }
                Raise("fbw_efb_elements", data);
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
            _sendLock.Dispose();
        }

        private sealed class ScrapeResult
        {
            public bool ok { get; set; }
            public string? page { get; set; }
            public string? error { get; set; }
            public List<ScrapeElement>? elements { get; set; }
            public BtvSnapshot? btv { get; set; }
        }

        private sealed class BtvSnapshot
        {
            public bool ready { get; set; }
            public List<string>? runways { get; set; }
            public List<string>? exits { get; set; }
            public string? runway { get; set; }
            public string? exit { get; set; }
            public int? lda { get; set; }
            public int? exitDist { get; set; }
            public int? dry { get; set; }
            public int? wet { get; set; }
            public int? stop { get; set; }
            public int? rot { get; set; }
            public int? turnMax { get; set; }
            public int? turnIdle { get; set; }
            public bool metric { get; set; }
            public bool computing { get; set; }
        }

        private sealed class ScrapeElement
        {
            public int idx { get; set; }
            public string? kind { get; set; }
            public string? tag { get; set; }
            public string? role { get; set; }
            public string? text { get; set; }
            public string? value { get; set; }
            public string? controlType { get; set; }
            public bool clickable { get; set; }
            public int level { get; set; }
            public string? live { get; set; }
            public bool disabled { get; set; }
            public List<string>? options { get; set; }
        }
    }
}
