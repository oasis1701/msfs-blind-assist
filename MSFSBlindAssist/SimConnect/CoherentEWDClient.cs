using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>One scraped Electronic-Checklist line (read from the A380X_EWD view).</summary>
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
    /// Background monitor that reads the FlyByWire A380X E/WD (Engine &amp; Warning
    /// Display) abnormal/warning PROCEDURES and announces new failures for a screen
    /// reader, through the MSFS Coherent GT debugger (127.0.0.1:19999), resolved to
    /// the E/WD Coherent view (title "A380X_EWD"). NO injection.
    ///
    /// Why DOM scrape and not SimVars: the A380 FwsCore publishes MASTER WARNING /
    /// CAUTION and the MEMO columns on L-vars (handled by the SimVar path), but the
    /// sensed/abnormal failure PROCEDURES (titles + action items) are published on an
    /// in-process EventBus, NOT SimVars — they exist only as rendered text in this
    /// view's DOM. So a blind pilot can only get them by scraping the E/WD.
    /// Runs coherent-ewd-agent.js (window.__MSFSBA_EWD) inside the E/WD JS context.
    ///
    /// Lifecycle: created when the A380X loads (background, no window needed) so a
    /// failure announces the moment it is sensed. Memos are deliberately NOT
    /// announced here — the SimVar EWD_LOWER path already covers them; this client
    /// only adds the failure procedures, which have no SimVar.
    /// </summary>
    public sealed class CoherentEWDClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string EwdTitleNeedle = "A380X_EWD";
        private const int PollIntervalMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;

        /// <summary>Raised (on the creating thread) for each new E/WD failure line.</summary>
        public event Action<string>? LineAnnounced;
        public event Action<string>? Error;

        /// <summary>
        /// Raised (on the creating thread) with the current Electronic-Checklist rows
        /// while <see cref="EclActive"/> is true. The ECL renders on the SAME
        /// A380X_EWD Coherent view, and Coherent GT (Chromium 49) allows only ONE
        /// inspector connection per page — so the live ECL is read through THIS
        /// already-connected monitor's socket (the ecl agent installed alongside the
        /// E/WD agent) instead of a second, conflicting connection.
        /// </summary>
        public event Action<List<EclRow>>? EclRowsUpdated;

        /// <summary>Set by the checklist window: poll + raise ECL rows only while open.</summary>
        public bool EclActive { get; set; }

        /// <summary>True once the shared A380X_EWD connection + agents are installed.</summary>
        public bool IsConnected => _connected && _agentInstalled;

        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        // Lines already spoken (or present at baseline) — kills the scroll/re-render
        // re-announce. Bounded reset guards a runaway over a very long flight.
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private bool _baselineDone;

        // The fixed ABN-PROC manual-procedure menu categories (shown only when the
        // pilot opens the ABN PROC page) — exact-match so they are not mistaken for
        // an active failure ("F/CTL PRIM 1 FAULT" != the bare "F/CTL" menu entry).
        private static readonly HashSet<string> MenuLines = new(StringComparer.OrdinalIgnoreCase)
        {
            "ABNORMAL PROC", "ABN PROC", "SMOKE / FUMES", "SMOKE/FUMES", "EMER EVAC",
            "EMER DESCENT", "DITCHING", "FORCED LANDING", "UNRELIABLE AIRSPEED INDICATION",
            "ENG", "F/CTL", "L/G", "NAV", "FUEL", "MISCELLANEOUS", "CLEAR", "STS"
        };

        private static readonly Regex DotRun = new(@"\s*\.{2,}\s*", RegexOptions.Compiled);
        private static readonly Regex WipTag = new(@"\s*\(WIP\)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private string _eclAgentJs = "";
        private bool _eclAgentInstalled;
        private string _lastEclHash = "";
        private int _msgId;
        private volatile bool _connected;
        private volatile bool _agentInstalled;
        private bool _disposed;

        public CoherentEWDClient()
        {
            _syncContext = SynchronizationContext.Current;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-ewd-agent.js");
                _agentJs = File.ReadAllText(path);
                // The live ECL agent is installed on the SAME connection (the ECL
                // renders on this view too; Chromium 49 allows only one inspector
                // socket per page, so we cannot open a second one for it).
                string eclPath = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-ecl-agent.js");
                _eclAgentJs = File.ReadAllText(eclPath);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load E/WD agent script: {ex.Message}");
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
                    System.Diagnostics.Debug.WriteLine($"CoherentEWDClient loop: {ex.Message}");
                    _connected = false;
                    _agentInstalled = false;
                    // A view that goes away (page reload) means the baseline is gone;
                    // re-baseline on reconnect so we do not dump the whole display.
                    _baselineDone = false;
                    try { _ws?.Abort(); } catch { }
                    _ws = null;
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            int? pageId = await ResolveEwdPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            await ws.ConnectAsync(url, ct);
            _ws = ws;
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf("MSFSBA_EWD_INSTALLED", StringComparison.Ordinal) >= 0;
            // Install the ECL agent on the same socket (best-effort; the failure
            // monitor still works even if this fails).
            _eclAgentInstalled = false;
            if (!string.IsNullOrEmpty(_eclAgentJs))
            {
                string eclInstall = await EvalAsync(_eclAgentJs, ct);
                _eclAgentInstalled = eclInstall.IndexOf("MSFSBA_ECL_INSTALLED", StringComparison.Ordinal) >= 0;
            }
            _connected = _agentInstalled;
            // A fresh agent install = a fresh page; take a new silent baseline so a
            // failure already on screen at connect time does not spam on every poll.
            _baselineDone = false;
            _lastEclHash = "";
            return _agentInstalled;
        }

        private async Task<int?> ResolveEwdPageId(CancellationToken ct)
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveEwdPageId: {ex.Message}");
            }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_EWD ? __MSFSBA_EWD.scrape() : ''", ct);
            if (string.IsNullOrEmpty(raw))
            {
                _agentInstalled = false;
                return;
            }

            ScrapeResult? result;
            try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
            catch { return; }
            if (result == null || !result.ok) return;
            _connected = true;

            var fresh = new List<string>();
            foreach (var w in result.warnings ?? new List<Warning>())
            {
                string clean = Clean(w.text);
                if (clean.Length == 0) continue;
                string key = WipTag.Replace(clean, "").Trim();
                if (MenuLines.Contains(key)) continue;          // the ABN-PROC menu, not a failure
                if (!_seen.Add(clean)) continue;                // already spoken / at baseline
                fresh.Add(clean);
            }
            // Memos (PARK BRK ON, ELEC EXT PWR, …) — announced from the SAME scrape so
            // the whole E/WD auto-call-out comes from one source (the SimVar
            // EWD_LOWER decode auto-announce is disabled while this monitor runs).
            foreach (var m in result.memos ?? new List<string>())
            {
                string clean = Clean(m);
                if (clean.Length == 0) continue;
                if (!_seen.Add(clean)) continue;
                fresh.Add(clean);
            }
            // PFD memo + limitations lines (SET HOLD SPD, SPEED LIM, …). These are FBW
            // 'string' SimVars the agent reads from the JS context — MSFSBA's numeric
            // SimConnect can't (it reads 0), so this scrape is the only way to surface
            // them. Announced on change, same baseline + dedup.
            foreach (var p in result.pfd ?? new List<string>())
            {
                string clean = Clean(p);
                if (clean.Length == 0) continue;
                if (!_seen.Add(clean)) continue;
                fresh.Add(clean);
            }

            // First successful scrape establishes the baseline silently — only
            // failures that appear AFTER connect are announced (matches every other
            // MSFSBA monitor and avoids re-reading the whole screen on reconnect).
            if (!_baselineDone)
            {
                _baselineDone = true;
                return;
            }

            if (_seen.Count > 600) { _seen.Clear(); _baselineDone = false; }

            foreach (var line in fresh)
                RaiseLine(line);

            // Live Electronic Checklist — only while the checklist window is open.
            if (EclActive && _eclAgentInstalled)
            {
                var rows = await ScrapeEclInternal(ct);
                if (rows != null)
                {
                    string hash = string.Join("\n", rows.Select(r => (r.Checked ? "1" : "0") + (r.selected ? "S" : "") + r.text));
                    if (hash != _lastEclHash) { _lastEclHash = hash; RaiseEclRows(rows); }
                }
            }
        }

        /// <summary>
        /// On-demand ECL scrape over the shared A380X_EWD socket (used by the
        /// checklist window after it pulses an ECP button, so the user hears the
        /// result on the now-selected line). Returns null if the agent isn't ready.
        /// </summary>
        public async Task<List<EclRow>?> ScrapeEclAsync()
        {
            try
            {
                var ct = _cts?.Token ?? CancellationToken.None;
                if (!await EnsureConnected(ct)) return null;
                return await ScrapeEclInternal(ct);
            }
            catch { return null; }
        }

        private async Task<List<EclRow>?> ScrapeEclInternal(CancellationToken ct)
        {
            if (!_eclAgentInstalled) return null;
            string raw = await EvalAsync("window.__MSFSBA_ECL ? __MSFSBA_ECL.scrape() : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _eclAgentInstalled = false; return null; }
            try
            {
                var res = JsonSerializer.Deserialize<EclScrapeResult>(raw);
                if (res == null || !res.ok) return null;
                return res.rows ?? new List<EclRow>();
            }
            catch { return null; }
        }

        private static string Clean(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = s.Trim();
            t = DotRun.Replace(t, ", ");                  // leader dots → spoken pause
            if (t.StartsWith("-") || t.StartsWith(".")) t = t.Substring(1);
            t = Ws.Replace(t, " ").Trim();
            return t;
        }

        // ---- Runtime.evaluate over the inspector socket -----------------

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
                System.Diagnostics.Debug.WriteLine($"CoherentEWDClient receive: {ex.Message}");
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

        private void RaiseLine(string line)
        {
            if (_syncContext != null) _syncContext.Post(_ => LineAnnounced?.Invoke(line), null);
            else LineAnnounced?.Invoke(line);
        }

        private void RaiseError(string message)
        {
            if (_syncContext != null) _syncContext.Post(_ => Error?.Invoke(message), null);
            else Error?.Invoke(message);
        }

        private void RaiseEclRows(List<EclRow> rows)
        {
            if (_syncContext != null) _syncContext.Post(_ => EclRowsUpdated?.Invoke(rows), null);
            else EclRowsUpdated?.Invoke(rows);
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
            public string? error { get; set; }
            public List<Warning>? warnings { get; set; }
            public List<string>? memos { get; set; }
            public List<string>? pfd { get; set; }
        }

        private sealed class Warning
        {
            public string? text { get; set; }
            public string? sev { get; set; }
            public bool headline { get; set; }
            public bool selected { get; set; }
        }

        private sealed class EclScrapeResult
        {
            public bool ok { get; set; }
            public bool shown { get; set; }
            public List<EclRow>? rows { get; set; }
        }
    }
}
