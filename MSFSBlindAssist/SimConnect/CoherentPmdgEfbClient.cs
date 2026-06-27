using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads (and drives) the PMDG tablet EFB through the MSFS Coherent GT
    /// debugger — the same no-injection WebKit-Inspector endpoint
    /// (127.0.0.1:19999) the flyPad client uses, but resolved to the PMDG
    /// tablet's own Coherent view (title contains "PMDGTablet"). We run
    /// coherent-pmdg-efb-agent.js (window.__MSFSBA_PMDG_EFB) via Runtime.evaluate
    /// inside the tablet's JS context, where its DOM is directly reachable.
    ///
    /// Page ids shift between sim restarts, so the tablet view is resolved BY
    /// TITLE every (re)connect — never hardcoded. There are TWO PMDGTablet views
    /// (Captain + First Officer), so resolution is SIDE-AWARE: the candidate
    /// whose in-page getTabletSide() equals the requested side is chosen.
    ///
    /// Implements IMcduBridge so FbwEfbForm can consume it exactly like the
    /// flyPad CoherentEFBClient: it raises the same fbw_efb_connected /
    /// fbw_efb_elements state pushes (deliberately reused — FbwEfbForm hardcodes
    /// those strings) and accepts the same command vocabulary
    /// (get_display_elements / set_element_value / click_display_element),
    /// translating each into an agent call.
    /// </summary>
    public sealed class CoherentPmdgEfbClient : IMcduBridge, IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string TabletTitleNeedle = "PMDGTablet";
        // Background scrape cadence. Kept moderate: a user click forces an immediate
        // re-scrape (the form posts get_display_elements), so this only governs how
        // fast AMBIENT changes (clock, live values) are picked up. 600ms eases the
        // WebSocket/JSON load vs the old 400ms without feeling sluggish, and the form
        // coalesces renders so polls can never pile up overlapping WebView2 updates.
        private const int PollIntervalMs = 600;
        private const int IdleIntervalMs = 1500;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        // Unit-separator used to join a <select>'s option labels into one state value.
        private const char OptionSeparator = (char)0x1f;

        public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;
        public event Action<string>? Error;

        private readonly string _side;
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
        private bool _connectedPushSent;
        private string _lastElementsHash = "";
        private bool _disposed;

        public bool IsBridgeConnected =>
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 5;

        public CoherentPmdgEfbClient(string side)
        {
            _side = side;
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
                        "CoherentPmdgEfbClient.Start() called after Stop() — not supported; create a new instance.");
                    System.Diagnostics.Debug.Assert(false,
                        "CoherentPmdgEfbClient: Start() after Stop() is a no-op. Dispose and create a new client instead.");
                }
                return;
            }
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-pmdg-efb-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load PMDG EFB agent script: {ex.Message}");
            }
            // With no agent script every EnsureConnected installs nothing -> never "installed" ->
            // RunLoop would spin forever, opening + aborting an inspector socket on the live tablet
            // every ReconnectDelayMs with only the single error above. Don't start the loop.
            if (string.IsNullOrEmpty(_agentJs))
            {
                RaiseError("PMDG EFB agent script is missing or empty; EFB unavailable.");
                return;
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

        /// <summary>
        /// Pause/resume the 600 ms tablet scrape poll — only poll while the tablet window is
        /// visible. The inspector socket and installed agent are KEPT WARM while idle so
        /// reactivation needs no reconnect or agent re-install.
        /// Re-activation forces a full re-push so the form fills in immediately.
        /// </summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active) { _lastElementsHash = ""; }
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
                    return $"window.__MSFSBA_PMDG_EFB && __MSFSBA_PMDG_EFB.clickElement({JsInt(Idx())})";
                case "set_element_value":
                    return $"window.__MSFSBA_PMDG_EFB && __MSFSBA_PMDG_EFB.setValue({JsInt(Idx())},{JsStr(Val())})";
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
                    // Idle = connection + agent stay warm, but the heavy DOM scrape is paused.
                    if (_active) await PollOnce(ct);
                    await Task.Delay(_active ? PollIntervalMs : IdleIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CoherentPmdgEfbClient loop: {ex.Message}");
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
                _agentInstalled = reinstall.IndexOf("MSFSBA_PMDG_EFB_INSTALLED", StringComparison.Ordinal) >= 0;
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

            int? pageId = await ResolveEfbPageId(ct);
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
            _agentInstalled = install.IndexOf("MSFSBA_PMDG_EFB_INSTALLED", StringComparison.Ordinal) >= 0;
            _connected = _agentInstalled;
            return _agentInstalled;
        }

        private async Task<int?> ResolveEfbPageId(CancellationToken ct)
        {
            try
            {
                string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                using var doc = JsonDocument.Parse(json);
                var candidates = new List<int>();
                foreach (var view in doc.RootElement.EnumerateArray())
                {
                    if (!view.TryGetProperty("title", out var t)) continue;
                    if ((t.GetString() ?? "").IndexOf(TabletTitleNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!view.TryGetProperty("id", out var idEl)) continue;
                    if (idEl.ValueKind == JsonValueKind.Number) candidates.Add(idEl.GetInt32());
                    else if (int.TryParse(idEl.GetString(), out var n)) candidates.Add(n);
                }
                foreach (var id in candidates)
                {
                    string side = await EvalSideAsync(id, ct);
                    if (string.Equals(side, _side, StringComparison.OrdinalIgnoreCase))
                    {
                        // Let the probe socket's close fully release the page before the main
                        // socket connects to the SAME id (Coherent allows ONE inspector per page).
                        try { await Task.Delay(200, ct); } catch (OperationCanceledException) { }
                        return id;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ResolvePmdgTablet: {ex.Message}"); }
            return null;
        }

        // One-shot WS eval of getTabletSide() on a candidate page (separate from the main socket).
        // Accumulates each CDP frame to EndOfMessage and reads the id==1 reply (robust to
        // fragmentation / interleaved frames); gracefully closes so Coherent releases the page.
        private async Task<string> EvalSideAsync(int pageId, CancellationToken ct)
        {
            var ws = new System.Net.WebSockets.ClientWebSocket();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId}"), cts.Token);
                var msg = JsonSerializer.Serialize(new { id = 1, method = "Runtime.evaluate", @params = new { expression = "(typeof getTabletSide==='function')?getTabletSide():''", returnByValue = true } });
                await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(msg), System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token);
                var buf = new byte[8192];
                using var ms = new System.IO.MemoryStream();
                for (int frame = 0; frame < 20; frame++)
                {
                    ms.SetLength(0);
                    System.Net.WebSockets.WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                        if (res.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return "";
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    try
                    {
                        using var d = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                        var root = d.RootElement;
                        if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var rid) && rid == 1)
                        {
                            if (root.TryGetProperty("result", out var outer) && outer.TryGetProperty("result", out var inner) && inner.TryGetProperty("value", out var val))
                                return val.GetString() ?? "";
                            return "";
                        }
                    }
                    catch { /* not our frame yet — keep reading */ }
                }
            }
            catch { }
            finally
            {
                try { await ws.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
                try { ws.Abort(); } catch { }
                try { ws.Dispose(); } catch { }
            }
            return "";
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_PMDG_EFB ? __MSFSBA_PMDG_EFB.scrape() : ''", ct);
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
                Raise("fbw_efb_connected", new Dictionary<string, string>());
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
                    // The agent's STAMPED idx — this, not the list position i, is what
                    // clickElement/setValue look up. They diverge because the list is
                    // sorted+deduped after stamping.
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
                    // Options for a real <select>; unit-separator joined.
                    if (elements[i].options is { Count: > 0 })
                        data[$"items.{i}.options"] = string.Join(OptionSeparator, elements[i].options!);
                    // Range (slider) bounds for controlType "range".
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (elements[i].min is { } mn) data[$"items.{i}.min"] = mn.ToString(inv);
                    if (elements[i].max is { } mx) data[$"items.{i}.max"] = mx.ToString(inv);
                    if (elements[i].step is { } sp) data[$"items.{i}.step"] = sp.ToString(inv);
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
                System.Diagnostics.Debug.WriteLine($"CoherentPmdgEfbClient receive: {ex.Message}");
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
            // Intentionally NOT disposing _sendLock: the background RunLoop is not joined here and
            // may be pending on WaitAsync — disposing a SemaphoreSlim with waiters throws
            // ObjectDisposedException on the pool thread. Stop() cancels _cts, which unblocks the
            // waiter; the wait handle is never materialized, so nothing leaks.
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
            public double? min { get; set; }
            public double? max { get; set; }
            public double? step { get; set; }
        }
    }
}
