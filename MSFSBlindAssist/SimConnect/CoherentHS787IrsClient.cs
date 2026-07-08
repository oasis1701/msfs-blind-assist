using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads the WT 787 IRS "TIME TO ALIGN" state over the MSFS Coherent GT remote debugger
    /// (127.0.0.1:19999) and writes the synthetic L-vars <c>MSFSBA_IRS_ALIGN_STATE</c> /
    /// <c>MSFSBA_IRS_ALIGN_MINUTES</c> that <see cref="Aircraft.HorizonSim787Definition"/> reads
    /// through its normal SimVar monitoring (so the align state surfaces in the IRS panel and is
    /// announced on completion — no event plumbing needed).
    ///
    /// The realistic, Realistic-respecting alignment state is exposed by the WT 787 ONLY on the
    /// ND/PFD <c>.time-to-align</c> DOM element — the only IRS L-var WT itself sets is
    /// WT_IRS_POS_SET_N (position accepted, NOT alignment complete). This is the Coherent
    /// replacement for the old hs787-mfd-bridge.js irs_align scrape (the HTTP bridge was retired).
    ///
    /// Lightweight: connects to the PFD view, installs <c>coherent-hs787-irs-agent.js</c>
    /// (window.__MSFSBA_HS787_IRS), and calls <c>poll()</c> on a timer — the agent does the
    /// scrape + L-var write itself. (A Coherent Runtime.evaluate CAN write L-vars — verified the
    /// write is read back via SimConnect/MobiFlight.) One inspector socket, resolved by title,
    /// agent auto-reinstall on a dropped connection (the one-socket-per-page discipline).
    /// </summary>
    public sealed class CoherentHS787IrsClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        // The .time-to-align element exists on the ND (MFD_1/2) AND the PFD; the PFD is the
        // stable identity (never reconfigured by the pilot), and the panel shows there.
        private const string ViewTitleNeedle = "HSB789_PFD";
        private const int PollIntervalMs = 500;
        private const int ReconnectDelayMs = 3000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        private const string InstallMarker = "MSFSBA_HS787_IRS_INSTALLED";

        public event Action<string>? Error;

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private int _msgId;
        private volatile bool _agentInstalled;
        private bool _disposed;

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-hs787-irs-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex) { RaiseError($"Could not load HS787 IRS agent script: {ex.Message}"); }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null; _agentInstalled = false;
        }

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await EnsureConnected(ct)) { await Task.Delay(ReconnectDelayMs, ct); continue; }
                    // The agent scrapes .time-to-align + the IRS L-vars and writes the synthetic
                    // align-state L-vars itself; we just keep calling it.
                    string r = await EvalAsync("window.__MSFSBA_HS787_IRS ? JSON.stringify(__MSFSBA_HS787_IRS.poll()) : ''", ct);
                    if (string.IsNullOrEmpty(r)) _agentInstalled = false;   // agent gone — reinstall next loop
                    await Task.Delay(PollIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Debug("SimConnect", $"CoherentHS787IrsClient loop: {ex.Message}");
                    _agentInstalled = false;
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
                if (_agentInstalled) return true;
            }

            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null; _agentInstalled = false;
            }

            int? pageId = await ResolvePageId(ct);
            if (pageId == null) return false;

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
                return false;
            }
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0;
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
                    if (title.IndexOf(ViewTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex) { Log.Debug("SimConnect", $"HS787 IRS ResolvePageId: {ex.Message}"); }
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
            var buf = new byte[65536];
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
                        if (res.MessageType == WebSocketMessageType.Close) { _agentInstalled = false; return; }
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("SimConnect", $"CoherentHS787IrsClient receive: {ex.Message}"); }
            finally { _agentInstalled = false; }
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

        private void RaiseError(string message) => Error?.Invoke(message);

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
    }
}
