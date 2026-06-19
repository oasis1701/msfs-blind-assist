using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads + drives the HorizonSim 787 EFB (the Boeing EFB) through the MSFS Coherent GT
    /// remote debugger (127.0.0.1:19999, <c>HSB789_EFB</c> view) — NO HTTP injection, NO
    /// patched HTML, NO EFBBridgeServer. Runs <c>coherent-hs787-efb-agent.js</c>
    /// (window.__MSFSBA_HS787_EFB) via Runtime.evaluate; the agent scrapes the Boeing EFB
    /// DOM (.boeing-efb-button / -dropdown-button / -textfield-button, .button-name labels,
    /// efb-title) into {title, elements[]} and clicks/sets by a stamped idx.
    ///
    /// Exposes the SAME surface HS787EFBForm consumed from EFBBridgeServer — the
    /// <see cref="StateUpdated"/> event ("efb_screen" with pageTitle + buttonCount + btn{i})
    /// and <see cref="EnqueueCommand"/> (read_screen / click_btn_{n} / set_field_{n} ...) —
    /// so the form is a near-drop-in swap. Each btn{i}'s index == the agent element idx, so a
    /// click_btn_{i} maps straight to agent.click(i). Folds kind/value/disabled into each
    /// label so the screen reader hears e.g. "THRUST RTG: TO" / "ARPTINFO (unavailable)".
    /// (Modeled on CoherentHS787CduClient; one inspector socket per page, resolved by title.)
    /// </summary>
    public sealed class CoherentHS787EfbClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string EfbTitleNeedle = "HSB789_EFB";
        private const int PollIntervalMs = 400;
        private const int IdleIntervalMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        private const string InstallMarker = "MSFSBA_HS787_EFB_INSTALLED";

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
        private volatile bool _active = true;
        private DateTime _lastGoodScrapeUtc = DateTime.MinValue;
        private string _lastHash = "";
        private bool _disposed;

        public bool IsBridgeConnected =>
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 5;

        public CoherentHS787EfbClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-hs787-efb-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex) { RaiseError($"Could not load HS787 EFB agent script: {ex.Message}"); }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null; _connected = false; _agentInstalled = false;
        }

        public void SetActive(bool active)
        {
            _active = active;
            if (active) _lastHash = "";
        }

        // ---- command surface (matches EFBBridgeServer.EnqueueCommand) ----

        public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
        {
            string? expr = BuildCommandExpression(command, payload);
            if (expr == null) return;
            _ = Task.Run(async () =>
            {
                try { await EvalAsync(expr); }
                catch { /* dropped command self-heals on the next poll */ }
            });
        }

        private string? BuildCommandExpression(string command, Dictionary<string, string>? payload)
        {
            string Idx() => payload != null && payload.TryGetValue("index", out var i) ? i : "0";
            string Val() => payload != null && payload.TryGetValue("value", out var v) ? v : "";

            switch (command)
            {
                // The WebView2 EFB form (FbwEfbForm) speaks this vocabulary.
                case "get_display_elements":
                    _lastHash = "";   // force a re-push of the current page
                    return null;
                case "click_display_element":
                    _lastHash = "";
                    return $"window.__MSFSBA_HS787_EFB && __MSFSBA_HS787_EFB.click({JsInt(Idx())})";
                case "set_element_value":
                    _lastHash = "";
                    return $"window.__MSFSBA_HS787_EFB && __MSFSBA_HS787_EFB.setValue({JsInt(Idx())},{JsStr(Val())})";
                default:
                    return null;
            }
        }

        private static string JsInt(string s) => int.TryParse(s, out var n) ? n.ToString() : "0";

        private static string JsStr(string s) => JsonSerializer.Serialize(s);

        // ---- connection + poll loop -------------------------------------

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await EnsureConnected(ct)) { await Task.Delay(ReconnectDelayMs, ct); continue; }
                    if (_active) await PollOnce(ct);
                    await Task.Delay(_active ? PollIntervalMs : IdleIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CoherentHS787EfbClient loop: {ex.Message}");
                    _connected = false; _agentInstalled = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    foreach (var kv in _pending) kv.Value.TrySetCanceled();
                    _pending.Clear();
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0;
                if (_agentInstalled) { _connected = true; return true; }
            }

            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null; _agentInstalled = false;
            }

            int? pageId = await ResolvePageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(url, connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { ws.Dispose(); } catch { }
                _connected = false; return false;
            }
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0;
            _connected = _agentInstalled;
            return _agentInstalled;
        }

        private async Task<int?> ResolvePageId(CancellationToken ct)
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"HS787 EFB ResolvePageId: {ex.Message}"); }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_HS787_EFB ? JSON.stringify(__MSFSBA_HS787_EFB.scrape()) : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _agentInstalled = false; return; }

            EfbScrape? result;
            try { result = JsonSerializer.Deserialize<EfbScrape>(raw); }
            catch { return; }
            if (result == null || !result.ok) return;

            _lastGoodScrapeUtc = DateTime.UtcNow;
            var elements = result.elements ?? new List<EfbElement>();

            // Push the page as the generic fbw_efb_elements model the WebView2 form (FbwEfbForm)
            // renders as a semantic HTML document — so the HS787 EFB is a real accessible webpage
            // (headings, buttons, inputs) with native screen-reader browse mode, not a flat list.
            var sb = new StringBuilder(result.title ?? "");
            foreach (var e in elements)
                sb.Append('|').Append(e.idx).Append(':').Append(DisplayText(e))
                  .Append('/').Append(e.value).Append('/').Append(e.kind).Append('/').Append(e.disabled ? '1' : '0');
            string hash = sb.ToString();
            if (hash == _lastHash) return;
            _lastHash = hash;

            var data = new Dictionary<string, string>
            {
                ["count"] = elements.Count.ToString(),
                ["page"] = result.title ?? ""
            };
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                bool isInput = e.kind == "input";
                bool clickable = e.kind == "button" || e.kind == "nav" || e.kind == "dropdown" || e.kind == "option";
                data[$"items.{i}.aidx"] = e.idx.ToString();          // agent stamped idx for click/set
                data[$"items.{i}.text"] = DisplayText(e);
                data[$"items.{i}.value"] = e.value ?? "";
                data[$"items.{i}.type"] = isInput ? "text" : "";     // controlType: text -> HTML <input>
                data[$"items.{i}.clickable"] = clickable ? "true" : "false";
                data[$"items.{i}.kind"] = isInput ? "input" : (clickable ? "button" : "text");
                data[$"items.{i}.level"] = "0";
                data[$"items.{i}.disabled"] = e.disabled ? "true" : "false";
            }
            Raise("fbw_efb_elements", data);
        }

        // The visible label/button text for an element (the value is carried separately so an
        // input renders as a real field). Page-switch buttons read "Go to <page>"; an expanded
        // dropdown's choices read "<value> (option)".
        private static string DisplayText(EfbElement e)
        {
            if (e.kind == "nav") return "Go to " + (e.label ?? "");
            if (e.kind == "option") return (e.label ?? "") + " (option)";
            return e.label ?? "";
        }

        // ---- Runtime.evaluate over the inspector socket -----------------

        public Task<string> EvalForResultAsync(string expression) => EvalAsync(expression);
        private Task<string> EvalAsync(string expression) => EvalAsync(expression, _cts?.Token ?? CancellationToken.None);

        private async Task<string> EvalAsync(string expression, CancellationToken ct)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return "";

            int id = Interlocked.Increment(ref _msgId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var msg = JsonSerializer.Serialize(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } });
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EvalTimeoutMs);
            using (timeout.Token.Register(() => tcs.TrySetCanceled()))
            {
                try { return ExtractValue(await tcs.Task); }
                catch (OperationCanceledException) { return ""; }
                finally { _pending.TryRemove(id, out _); }
            }
        }

        private static string ExtractValue(JsonElement root)
        {
            if (root.TryGetProperty("result", out var outer)
                && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val))
                return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
            return "";
        }

        private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[131072];
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
                        if (res.MessageType == WebSocketMessageType.Close) { _connected = false; _agentInstalled = false; return; }
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CoherentHS787EfbClient receive: {ex.Message}"); }
            finally { _connected = false; _agentInstalled = false; }
        }

        private void DispatchMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id)
                    && _pending.TryGetValue(id, out var tcs))
                    tcs.TrySetResult(root.Clone());
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
            try { _cts?.Dispose(); } catch { }
            try { _http.Dispose(); } catch { }
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
        }

        private sealed class EfbScrape
        {
            public bool ok { get; set; }
            public string? title { get; set; }
            public List<EfbElement>? elements { get; set; }
        }

        private sealed class EfbElement
        {
            public int idx { get; set; }
            public string? kind { get; set; }
            public string? label { get; set; }
            public string? value { get; set; }
            public bool disabled { get; set; }
        }
    }
}
