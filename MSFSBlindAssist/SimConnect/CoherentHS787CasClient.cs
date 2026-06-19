using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// EICAS Crew-Alerting-System reader for the HorizonSim 787. Connects to the MFD_1 / EICAS
    /// Coherent view, installs <c>coherent-hs787-cas-agent.js</c>, and keeps the active
    /// <c>.cas-warning</c> / <c>.cas-caution</c> / <c>.cas-advisory</c> message lists current so
    /// <see cref="AnnounceCurrentAlerts"/> can read them all back ON DEMAND (the EICAS key, Alt+E).
    /// It does NOT auto-announce per message: the def already auto-announces Master Caution /
    /// Master Warning, so the pilot is told the moment something posts and checks the specifics
    /// with Alt+E — no redundant chatter.
    ///
    /// Mirrors the A380 CoherentEWDClient role. Owns the MFD_1 socket; no other client uses
    /// MFD_1 (the ND read-out key was repointed to AI-vision precisely so this monitor can own it),
    /// so the one-socket-per-page rule holds (CDU=MFD_3, EFB=EFB, IRS=PFD, synoptic window=MFD_2).
    /// </summary>
    public sealed class CoherentHS787CasClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string ViewTitleNeedle = "HSB789_MFD_1";
        private const int PollIntervalMs = 1000;
        private const int ReconnectDelayMs = 3000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        private const string InstallMarker = "MSFSBA_HS787_CAS_INSTALLED";

        public event Action<string>? Error;

        private readonly ScreenReaderAnnouncer _announcer;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        // Current active alerts (lock-guarded snapshots) for the on-demand read-back.
        private readonly object _lock = new();
        private List<string> _warnings = new(), _cautions = new(), _advisories = new();

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private int _msgId;
        private volatile bool _agentInstalled;
        private bool _disposed;

        public CoherentHS787CasClient(ScreenReaderAnnouncer announcer) { _announcer = announcer; }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-hs787-cas-agent.js");
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex) { RaiseError($"Could not load HS787 CAS agent script: {ex.Message}"); }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null; _agentInstalled = false;
        }

        /// <summary>On-demand read-back of every active CAS alert (the EICAS key).</summary>
        public void AnnounceCurrentAlerts()
        {
            List<string> w, c, a;
            lock (_lock) { w = new(_warnings); c = new(_cautions); a = new(_advisories); }
            if (w.Count == 0 && c.Count == 0 && a.Count == 0)
            {
                _announcer.AnnounceImmediate("No EICAS alerts.");
                return;
            }
            var parts = new List<string>();
            if (w.Count > 0) parts.Add($"{w.Count} warning{(w.Count == 1 ? "" : "s")}: {string.Join(", ", w)}");
            if (c.Count > 0) parts.Add($"{c.Count} caution{(c.Count == 1 ? "" : "s")}: {string.Join(", ", c)}");
            if (a.Count > 0) parts.Add($"{a.Count} advisor{(a.Count == 1 ? "y" : "ies")}: {string.Join(", ", a)}");
            _announcer.AnnounceImmediate("EICAS. " + string.Join(". ", parts) + ".");
        }

        // ---- poll loop -------------------------------------------------

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!await EnsureConnected(ct)) { await Task.Delay(ReconnectDelayMs, ct); continue; }
                    string raw = await EvalAsync("window.__MSFSBA_HS787_CAS ? JSON.stringify(__MSFSBA_HS787_CAS.cas()) : ''", ct);
                    if (string.IsNullOrEmpty(raw)) { _agentInstalled = false; }
                    else ProcessScrape(raw);
                    await Task.Delay(PollIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CoherentHS787CasClient loop: {ex.Message}");
                    _agentInstalled = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    foreach (var kv in _pending) kv.Value.TrySetCanceled();
                    _pending.Clear();
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        // Keep the active-alert lists current for the on-demand Alt+E read-back. We do NOT
        // auto-announce per message: the def already auto-announces Master Caution / Master Warning
        // (Generic_Master_Caution_Active / _Warning_Active), so the pilot is told the moment
        // something posts and reads the specifics on demand with Alt+E — no redundant chatter.
        private void ProcessScrape(string raw)
        {
            CasScrape? s;
            try { s = JsonSerializer.Deserialize<CasScrape>(raw); } catch { return; }
            if (s == null || !s.ok) return;
            lock (_lock)
            {
                _warnings = s.warnings ?? new();
                _cautions = s.cautions ?? new();
                _advisories = s.advisories ?? new();
            }
        }

        // ---- connection plumbing (mirrors the other HS787 Coherent clients) ----

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"HS787 CAS ResolvePageId: {ex.Message}"); }
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CoherentHS787CasClient receive: {ex.Message}"); }
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
            catch { }
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

        private sealed class CasScrape
        {
            public bool ok { get; set; }
            public List<string>? warnings { get; set; }
            public List<string>? cautions { get; set; }
            public List<string>? advisories { get; set; }
        }
    }
}
