using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>One scraped Electronic-Checklist line.</summary>
    public sealed class EclRow
    {
        public string text { get; set; } = "";
        public string type { get; set; } = "";   // headline | item | abnormal | completed | line
        [System.Text.Json.Serialization.JsonPropertyName("checked")]
        public bool Checked { get; set; }
        public string style { get; set; } = "";   // done | action | caution | manual
        public bool selected { get; set; }
    }

    /// <summary>
    /// Live reader for the FlyByWire A380X Electronic Checklist (ECL) — the normal
    /// checklists + abnormal-procedure menu rendered on the A380X_EWD Coherent view.
    /// Installs coherent-ecl-agent.js (window.__MSFSBA_ECL) and polls scrape() for
    /// the structured rows. READ-ONLY — the ECL lines have no DOM click handlers
    /// (the FWS drives them from the KCCU cursor + ECP buttons), so the checklist
    /// ACTIONS are done by pulsing the real ECP button L-vars in C# (see
    /// FBWA380ChecklistForm), which FwsCore reads and applies.
    /// </summary>
    public sealed class CoherentEclClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string EwdTitleNeedle = "A380X_EWD";
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;

        public event Action<List<EclRow>>? RowsUpdated;
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
        private readonly int _pollIntervalMs;
        private string _lastHash = "";
        private List<EclRow> _lastRows = new();
        private bool _disposed;

        public bool IsConnected => _connected;
        public List<EclRow> CurrentRows => _lastRows;

        public CoherentEclClient(int pollIntervalMs = 700)
        {
            _pollIntervalMs = pollIntervalMs;
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-ecl-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex) { RaiseError($"Could not load ECL agent script: {ex.Message}"); }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null; _connected = false; _agentInstalled = false;
        }

        public void SetActive(bool active) { _active = active; if (active) _lastHash = ""; }

        public async Task<List<EclRow>> ScrapeNowAsync()
        {
            try
            {
                if (!await EnsureConnected(_cts?.Token ?? CancellationToken.None)) return _lastRows;
                var rows = await ScrapeOnce(_cts?.Token ?? CancellationToken.None);
                if (rows != null) { _lastRows = rows; return rows; }
            }
            catch { }
            return _lastRows;
        }

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!_active) { await Task.Delay(500, ct); continue; }
                    if (!await EnsureConnected(ct)) { await Task.Delay(ReconnectDelayMs, ct); continue; }
                    var rows = await ScrapeOnce(ct);
                    if (rows != null)
                    {
                        string hash = string.Join("\n", rows.Select(r => (r.Checked ? "1" : "0") + r.text));
                        if (hash != _lastHash) { _lastHash = hash; _lastRows = rows; RaiseRows(rows); }
                    }
                    await Task.Delay(_pollIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CoherentEclClient loop: {ex.Message}");
                    _connected = false; _agentInstalled = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<List<EclRow>?> ScrapeOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_ECL ? __MSFSBA_ECL.scrape() : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _agentInstalled = false; return null; }
            ScrapeResult? result;
            try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
            catch { return null; }
            if (result == null || !result.ok) return null;
            _connected = true;
            return result.rows ?? new List<EclRow>();
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;
            int? pageId = await ResolvePageId(ct);
            if (pageId == null) { _connected = false; return false; }
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}"), ct);
            _ws = ws; _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));
            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf("MSFSBA_ECL_INSTALLED", StringComparison.Ordinal) >= 0;
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
                    if (title.IndexOf(EwdTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CoherentEclClient ResolvePageId: {ex.Message}"); }
            return null;
        }

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
                try { JsonElement root = await tcs.Task; return ExtractValue(root); }
                catch (OperationCanceledException) { return ""; }
                finally { _pending.TryRemove(id, out _); }
            }
        }

        private static string ExtractValue(JsonElement root)
        {
            if (root.TryGetProperty("result", out var outer) && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val))
                return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
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
                        if (res.MessageType == WebSocketMessageType.Close) { _connected = false; _agentInstalled = false; return; }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                    } while (!res.EndOfMessage);
                    DispatchMessage(sb.ToString());
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CoherentEclClient receive: {ex.Message}"); }
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
            catch { }
        }

        private void RaiseRows(List<EclRow> rows)
        {
            if (_syncContext != null) _syncContext.Post(_ => RowsUpdated?.Invoke(rows), null);
            else RowsUpdated?.Invoke(rows);
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
            public bool shown { get; set; }
            public List<EclRow>? rows { get; set; }
        }
    }
}
