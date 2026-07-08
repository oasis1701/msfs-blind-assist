using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads + drives the HorizonSim 787 CDU (the WT Boeing FMC) through the MSFS
    /// Coherent GT remote debugger (127.0.0.1:19999) — NO HTTP injection, NO patched
    /// HTML, NO EFBBridgeServer. Connects to the live <c>HSB789_MFD_3</c> view's
    /// WebSocket and runs <c>coherent-hs787-cdu-agent.js</c> (window.__MSFSBA_HS787)
    /// via Runtime.evaluate, where the CDU DOM + SimVar are reachable.
    ///
    /// Exposes the SAME surface the form consumed from EFBBridgeServer — the
    /// <see cref="StateUpdated"/> event (cdu_visible / cdu_not_visible / fmc_screen with
    /// row0..rowN) and <see cref="EnqueueMfdCommand"/> (page_* / lsk_L|R{n} / type_key:KEY /
    /// read_screen ...) — so HS787FMCForm is transport-agnostic. Replaces the legacy
    /// hs787-mfd-bridge.js + HTTP server entirely.
    ///
    /// Page ids shift between sim runs, so the view is resolved BY TITLE every (re)connect.
    /// (Modeled on the proven CoherentDebuggerClient; Coherent GT allows only ONE inspector
    /// socket per page, so the teardown-before-reconnect discipline is preserved.)
    /// </summary>
    public sealed class CoherentHS787CduClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string CduTitleNeedle = "HSB789_MFD_3";
        private const int PollIntervalMs = 350;
        private const int IdleIntervalMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        private const string InstallMarker = "MSFSBA_HS787_INSTALLED";

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
        private string _lastScreenHash = "";
        private bool? _cduVisible;   // null = unknown
        private bool _disposed;

        public bool IsBridgeConnected =>
            _connected && (DateTime.UtcNow - _lastGoodScrapeUtc).TotalSeconds < 5;

        public CoherentHS787CduClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return; // restart is not a supported lifecycle — dispose + recreate
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-hs787-cdu-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load HS787 CDU agent script: {ex.Message}");
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

        /// <summary>Pause/resume the scrape poll (keep socket + agent warm while idle).</summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active) { _lastScreenHash = ""; _cduVisible = null; }
        }

        // ---- command surface (matches EFBBridgeServer.EnqueueMfdCommand) ----

        public void EnqueueMfdCommand(string command, Dictionary<string, string>? payload = null)
        {
            string? expr = BuildCommandExpression(command, payload);
            if (expr == null) return;
            _ = Task.Run(async () =>
            {
                try { await EvalAsync(expr); }
                catch { /* a dropped command self-heals on the next poll */ }
            });
        }

        private static readonly Dictionary<string, string> PageMap = new()
        {
            ["page_init_ref"] = "INIT_REF", ["page_rte"] = "RTE", ["page_dep_arr"] = "DEP_ARR",
            ["page_altn"] = "ALTN", ["page_vnav"] = "VNAV", ["key_exec"] = "EXEC",
            ["page_fix"] = "FIX", ["page_legs"] = "LEGS", ["page_hold"] = "HOLD",
            ["page_fmc_comm"] = "FMC_COMM", ["page_prog"] = "PROG", ["page_nav_rad"] = "NAV_RAD",
            ["key_prev_page"] = "PREV_PAGE", ["key_next_page"] = "NEXT_PAGE"
        };

        private string? BuildCommandExpression(string command, Dictionary<string, string>? payload)
        {
            if (PageMap.TryGetValue(command, out var page))
            {
                _lastScreenHash = "";   // force a re-push so the new page lands immediately
                return $"window.__MSFSBA_HS787 && __MSFSBA_HS787.clickPage({JsStr(page)})";
            }
            // lsk_L{n} / lsk_R{n}
            var m = System.Text.RegularExpressions.Regex.Match(command, "^lsk_([LR])([1-6])$");
            if (m.Success)
            {
                _lastScreenHash = "";
                return $"window.__MSFSBA_HS787 && __MSFSBA_HS787.clickLsk({JsStr(m.Groups[1].Value)},{m.Groups[2].Value})";
            }
            // type_key:{KEY}  /  fcu_key:{KEY}
            if (command.StartsWith("type_key:", StringComparison.Ordinal))
                return $"window.__MSFSBA_HS787 && __MSFSBA_HS787.typeKey({JsStr(command.Substring(9))})";
            if (command.StartsWith("fcu_key:", StringComparison.Ordinal))
                return $"window.__MSFSBA_HS787 && __MSFSBA_HS787.fcuKey({JsStr(command.Substring(8))})";

            switch (command)
            {
                case "read_screen":
                    _lastScreenHash = ""; // force re-push of the current screen
                    return null;
                case "eval_js":
                    // The legacy HTTP-bridge hot-reload is unneeded on the Coherent transport
                    // (the agent is re-installed on every connect). Run any supplied code anyway.
                    return payload != null && payload.TryGetValue("code", out var c) ? c : null;
                default:
                    return null;
            }
        }

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
                    Log.Debug("SimConnect", $"CoherentHS787CduClient loop: {ex.Message}");
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
            if (_disposed) return false; // never resurrect a socket on a disposed client (swap race)
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0;
                if (_agentInstalled) { _connected = true; return true; }
            }

            // ONE inspector socket per page — tear down before reconnecting.
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
                    if (title.IndexOf(CduTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex) { Log.Debug("SimConnect", $"HS787 ResolvePageId: {ex.Message}"); }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_HS787 ? JSON.stringify(__MSFSBA_HS787.scrape()) : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _agentInstalled = false; return; }

            CduScrape? result;
            try { result = JsonSerializer.Deserialize<CduScrape>(raw); }
            catch { return; }
            if (result == null || !result.ok) return;

            _lastGoodScrapeUtc = DateTime.UtcNow;

            if (!result.visible || result.rows == null || result.rows.Count == 0)
            {
                if (_cduVisible == true)
                {
                    _cduVisible = false;
                    Raise("cdu_not_visible", new Dictionary<string, string>());
                }
                return;
            }

            if (_cduVisible != true)
            {
                _cduVisible = true;
                _lastScreenHash = ""; // force a full push on becoming visible
                Raise("cdu_visible", new Dictionary<string, string>());
            }

            string hash = string.Join("\n", result.rows);
            if (hash == _lastScreenHash) return;
            _lastScreenHash = hash;

            var data = new Dictionary<string, string> { ["rowCount"] = result.rows.Count.ToString() };
            for (int i = 0; i < result.rows.Count; i++) data[$"row{i}"] = result.rows[i] ?? "";
            Raise("fmc_screen", data);
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
            catch (Exception ex) { Log.Debug("SimConnect", $"CoherentHS787CduClient receive: {ex.Message}"); }
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

        private sealed class CduScrape
        {
            public bool ok { get; set; }
            public bool visible { get; set; }
            public List<string>? rows { get; set; }
            public string? scratchpad { get; set; }
        }
    }
}
