using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads (and drives) the FlyByWire A380X flyPad EFB through the MSFS
    /// Coherent GT debugger — the same no-injection WebKit-Inspector endpoint
    /// (127.0.0.1:19999) the MFD client uses, but resolved to the flyPad's own
    /// Coherent view (title contains "- EFB", e.g. "VCockpitNN - EFB"). We run
    /// coherent-flypad-agent.js (window.__MSFSBA_FLYPAD) via Runtime.evaluate
    /// inside the React app's JS context, where its DOM is directly reachable.
    ///
    /// Page ids shift between sim restarts, so the flyPad view is resolved BY
    /// TITLE every (re)connect — never hardcoded (the VCockpit index is load
    /// order dependent; only the title/coui:// path is stable).
    ///
    /// Implements IMcduBridge so FBWA380EFBForm can consume it exactly like the
    /// old injection-based EFBBridgeServer: it raises the same
    /// fbwa380_efb_connected / fbwa380_efb_elements state pushes and accepts the
    /// same command vocabulary (get_display_elements / set_element_value /
    /// click_display_element), translating each into an agent call.
    /// </summary>
    public sealed class CoherentEFBClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string EfbTitleNeedle = "- EFB";
        private const int PollIntervalMs = 400;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        // Unit-separator used to join a <select>'s option labels into one state value.
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
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 5;

        public CoherentEFBClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-flypad-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load flyPad agent script: {ex.Message}");
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
                    // Force the next poll to re-push the current elements even if
                    // nothing changed, so a form opened mid-session fills in.
                    _lastElementsHash = "";
                    return null;
                case "click_display_element":
                    return $"window.__MSFSBA_FLYPAD && __MSFSBA_FLYPAD.clickElement({JsInt(Idx())})";
                case "set_element_value":
                    return $"window.__MSFSBA_FLYPAD && __MSFSBA_FLYPAD.setValue({JsInt(Idx())},{JsStr(Val())})";
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
                    System.Diagnostics.Debug.WriteLine($"CoherentEFBClient loop: {ex.Message}");
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

            int? pageId = await ResolveEfbPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            await ws.ConnectAsync(url, ct);
            _ws = ws;
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf("MSFSBA_FLYPAD_INSTALLED", StringComparison.Ordinal) >= 0;
            _connected = _agentInstalled;

            if (_agentInstalled)
            {
                // The flyPad screen is usually powered off; turn it on so the form
                // shows content instead of a blank tablet. Idempotent — sets
                // L:A32NX_EFB_TURNED_ON = 1 (the same state a cockpit tap sets).
                try { await EvalAsync("window.__MSFSBA_FLYPAD && __MSFSBA_FLYPAD.powerOn && __MSFSBA_FLYPAD.powerOn()", ct); }
                catch { /* best-effort; the form still works once the user wakes it */ }
            }
            return _agentInstalled;
        }

        private async Task<int?> ResolveEfbPageId(CancellationToken ct)
        {
            try
            {
                string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var view in doc.RootElement.EnumerateArray())
                {
                    if (!view.TryGetProperty("title", out var titleEl)) continue;
                    string title = titleEl.GetString() ?? "";
                    if (title.IndexOf(EfbTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveEfbPageId: {ex.Message}");
            }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_FLYPAD ? __MSFSBA_FLYPAD.scrape() : ''", ct);
            if (string.IsNullOrEmpty(raw))
            {
                _agentInstalled = false; // agent gone (page reloaded) — reinstall next round
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
                Raise("fbwa380_efb_connected", new Dictionary<string, string>());
            }

            var elements = result.elements ?? new List<ScrapeElement>();
            var sb = new StringBuilder((result.page ?? "") + "|" + elements.Count + "|");
            foreach (var e in elements)
                // e.idx (stamped) is part of the signature: if a reorder changes
                // which element carries which idx, the form must re-render so its
                // click/set targets stay correct even when text/value are unchanged.
                sb.Append(e.idx).Append(':').Append(e.text).Append('/').Append(e.value)
                  .Append('/').Append(e.controlType).Append('/').Append(e.clickable ? '1' : '0')
                  .Append('/').Append(e.kind).Append('/').Append(e.level)
                  .Append('/').Append(e.disabled ? '1' : '0').Append('|');
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
                    // The agent's STAMPED idx (data-fbwa380-efb-idx) — this, not the
                    // list position i, is what clickElement/setValue look up. They
                    // diverge because the list is sorted+deduped after stamping.
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
                    // Options for a real <select>; unit-separator joined (rare on the flyPad).
                    if (elements[i].options is { Count: > 0 })
                        data[$"items.{i}.options"] = string.Join(OptionSeparator, elements[i].options!);
                }
                Raise("fbwa380_efb_elements", data);
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
                System.Diagnostics.Debug.WriteLine($"CoherentEFBClient receive: {ex.Message}");
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
            public string? page { get; set; }
            public string? error { get; set; }
            public List<ScrapeElement>? elements { get; set; }
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
