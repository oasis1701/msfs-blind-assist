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

        /// <summary>
        /// When false, the FAILURE/abnormal warning lines scraped from the E/WD DOM are
        /// NOT announced here — the authoritative <see cref="CoherentFwsFailureClient"/>
        /// (reading the FwsCore directly) owns failure call-outs, so this avoids double
        /// speech. Memos / PFD lines / status boxes are still announced from this scrape.
        /// Baseline + recur bookkeeping still tracks the warning lines either way.
        /// </summary>
        public bool AnnounceWarnings { get; set; } = true;

        /// <summary>True once the shared A380X_EWD connection + agents are installed.</summary>
        public bool IsConnected => _connected && _agentInstalled;

        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        // Serializes EnsureConnected. This client exposes public on-demand scrapes (ScrapeEclAsync from
        // the checklist form, ScrapeDisplayAsync from the A380 def) that run on the UI thread WHILE the
        // background RunLoop also calls EnsureConnected. Without this, two threads could ConnectAsync to
        // the same A380X_EWD page at once, opening a SECOND inspector socket — rejected by Coherent GT
        // (one socket per page), the exact failure this shared-socket design exists to prevent.
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        // Lines already spoken (or present at baseline) — kills the scroll/re-render
        // re-announce. Bounded reset guards a runaway over a very long flight.
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private bool _baselineDone;

        // The EWD status-area boxes (STS / ADV / FAILURE PENDING) + display
        // self-test currently shown — tracked across polls so we can edge-detect
        // appear vs clear (these persist, unlike one-shot failure/memo lines, so
        // they are announced on BOTH transitions rather than deduped by text).
        private HashSet<string> _lastStatus = new(StringComparer.OrdinalIgnoreCase);

        // The full active abnormal/warning PROCEDURE block (each procedure's title + its
        // action-item steps) from the latest scrape, for the Alt+E window's "Procedure"
        // section — so a blind pilot gets the STEPS TO ACTION, not just the failure title.
        private readonly object _procLock = new();
        private List<string> _activeProcedureLines = new();
        /// <summary>Snapshot of the current E/WD procedure lines (titles + indented steps).</summary>
        public List<string> ActiveProcedureLines { get { lock (_procLock) return new List<string>(_activeProcedureLines); } }

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
        // Generic display-scrape agent (coherent-display-agent.js, window.__MSFSBA_DISP),
        // installed on this SAME shared A380X_EWD socket so the SD "Upper E/WD" page can
        // read the full E/WD display content. A second CoherentDisplayClient on A380X_EWD
        // would be rejected (Chromium 49 = one inspector socket per page), which is what
        // produced the "content not available" box. Coexists with the EWD + ECL agents
        // (all isolated-IIFE window.__MSFSBA_* globals).
        private string _dispAgentJs = "";
        private bool _dispAgentInstalled;
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
                // Generic display agent on the same socket → SD "Upper E/WD" page content.
                string dispPath = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-display-agent.js");
                _dispAgentJs = File.ReadAllText(dispPath);
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
            _eclAgentInstalled = false;
            _dispAgentInstalled = false;
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
            if (_disposed) return false;
            // Fast path: steady-state (already connected) needs no lock.
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;
            await _connectLock.WaitAsync(ct);
            try
            {
            // Re-check under the lock: a concurrent caller may have just (re)connected.
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            // Socket still OPEN but an agent flag went missing (an eval timed out / the page
            // re-evaluated) — re-install the agents on the SAME socket. This is the common
            // transient case and avoids a reconnect entirely. CRITICAL: never open a second
            // socket while this one is alive — Coherent GT allows only ONE inspector
            // connection per page, and replacing _ws would orphan the healthy socket and
            // block the A380X_EWD page for the rest of the process.
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf("MSFSBA_EWD_INSTALLED", StringComparison.Ordinal) >= 0;
                if (_agentInstalled)
                {
                    if (!_eclAgentInstalled && !string.IsNullOrEmpty(_eclAgentJs))
                    {
                        string eclReinstall = await EvalAsync(_eclAgentJs, ct);
                        _eclAgentInstalled = eclReinstall.IndexOf("MSFSBA_ECL_INSTALLED", StringComparison.Ordinal) >= 0;
                    }
                    if (!_dispAgentInstalled && !string.IsNullOrEmpty(_dispAgentJs))
                    {
                        string dispReinstall = await EvalAsync(_dispAgentJs, ct);
                        _dispAgentInstalled = dispReinstall.IndexOf("MSFSBA_DISP_INSTALLED", StringComparison.Ordinal) >= 0;
                    }
                    _connected = true;
                    return true;
                }
            }

            // Tear down any existing socket BEFORE opening a new one (one-socket-per-page rule).
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
                _agentInstalled = false;
                _eclAgentInstalled = false;
                _dispAgentInstalled = false;
            }

            int? pageId = await ResolveEwdPageId(ct);
            if (pageId == null) { _connected = false; return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            await ws.ConnectAsync(url, ct);
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();   // cancel evals orphaned by the reconnect (else they hang to timeout)
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
            // Install the generic display agent on the same socket (best-effort) — used by
            // the SD "Upper E/WD" page via ScrapeDisplayAsync.
            _dispAgentInstalled = false;
            if (!string.IsNullOrEmpty(_dispAgentJs))
            {
                string dispInstall = await EvalAsync(_dispAgentJs, ct);
                _dispAgentInstalled = dispInstall.IndexOf("MSFSBA_DISP_INSTALLED", StringComparison.Ordinal) >= 0;
            }
            _connected = _agentInstalled;
            // A fresh agent install = a fresh page; take a new silent baseline so a
            // failure already on screen at connect time does not spam on every poll.
            _baselineDone = false;
            _lastEclHash = "";
            return _agentInstalled;
            }
            finally { _connectLock.Release(); }
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
            // Every spoken-category line present in THIS scrape. Used at the end to
            // prune _seen down to what is still on screen, so a warning/memo that
            // CLEARS and later RECURS is announced again (the old behaviour kept it in
            // _seen forever, so the second occurrence was silently swallowed).
            var currentLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var procBlock = new List<string>();                 // title + indented steps, for Alt+E
            foreach (var w in result.warnings ?? new List<Warning>())
            {
                string clean = Clean(w.text);
                if (clean.Length == 0) continue;
                string key = WipTag.Replace(clean, "").Trim();
                if (MenuLines.Contains(key)) continue;          // the ABN-PROC menu, not a failure
                // Append the EWD colour name so the auto-announce reads the colour a sighted pilot
                // sees, the same way the on-demand viewer does (the agent classifies it from the
                // EclLine CSS class).
                string spoken = WithColour(clean, w.colour);
                // Full procedure block: a headline is a procedure TITLE, the rest are its
                // action-item STEPS (indented) — surfaced in the Alt+E "Procedure" section.
                procBlock.Add(w.headline ? spoken : "  " + spoken);
                currentLines.Add(spoken);
                if (!_seen.Add(spoken)) continue;               // already spoken / at baseline
                if (AnnounceWarnings) fresh.Add(spoken);         // else: FwsFailureClient owns failure call-outs
            }
            lock (_procLock) { _activeProcedureLines = procBlock; }
            // Memos (PARK BRK ON, ELEC EXT PWR, …) — announced from the SAME scrape so
            // the whole E/WD auto-call-out comes from one source (the SimVar
            // EWD_LOWER decode auto-announce is disabled while this monitor runs).
            foreach (var m in result.memos ?? new List<Labelled>())
            {
                string clean = Clean(m.text);
                if (clean.Length == 0) continue;
                string spoken = WithColour(clean, m.colour);
                currentLines.Add(spoken);
                if (!_seen.Add(spoken)) continue;
                fresh.Add(spoken);
            }
            // PFD memo + limitations lines (SET HOLD SPD, SPEED LIM, …). These are FBW
            // 'string' SimVars the agent reads from the JS context — MSFSBA's numeric
            // SimConnect can't (it reads 0), so this scrape is the only way to surface
            // them. Announced on change, same baseline + dedup.
            foreach (var p in result.pfd ?? new List<Labelled>())
            {
                string clean = Clean(p.text);
                if (clean.Length == 0) continue;
                string spoken = WithColour(clean, p.colour);
                currentLines.Add(spoken);
                if (!_seen.Add(spoken)) continue;
                fresh.Add(spoken);
            }

            // EWD status-area indications (STS / ADV / FAILURE PENDING) + display
            // self-test, normalised to spoken phrases. Unlike failures/memos these
            // PERSIST, so they are edge-detected (announce on appear AND on clear)
            // rather than deduped by text.
            var curStatus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in result.status ?? new List<string>())
            {
                string phrase = StatusPhrase(Clean(s));
                if (phrase.Length > 0) curStatus.Add(phrase);
            }

            // First successful scrape establishes the baseline silently — only
            // failures that appear AFTER connect are announced (matches every other
            // MSFSBA monitor and avoids re-reading the whole screen on reconnect).
            if (!_baselineDone)
            {
                _baselineDone = true;
                _lastStatus = curStatus;   // seed so a box already up at connect is silent
                return;
            }

            if (_seen.Count > 600) { _seen.Clear(); _baselineDone = false; }

            foreach (var line in fresh)
                RaiseLine(line);

            // Forget lines that are no longer on the E/WD, so the SAME warning/memo
            // re-announces if it recurs later (e.g. a caution that clears then trips
            // again). Keeps _seen bounded to what's actually displayed.
            _seen.IntersectWith(currentLines);

            // Status boxes that newly appeared / newly cleared since the last poll.
            if (!curStatus.SetEquals(_lastStatus))
            {
                foreach (var appeared in curStatus)
                    if (!_lastStatus.Contains(appeared)) RaiseLine(appeared);
                foreach (var cleared in _lastStatus)
                    if (!curStatus.Contains(cleared)) RaiseLine(cleared + " cleared");
                _lastStatus = curStatus;
            }

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
            if (_disposed) return null;   // can be called from the checklist form after an aircraft-swap Dispose()
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
            if (!_eclAgentInstalled)
            {
                // One transient eval timeout used to latch this false until the socket
                // actually dropped — self-heal by re-installing on the live socket
                // (idempotent IIFE; runs under the same connection the monitor owns).
                if (string.IsNullOrEmpty(_eclAgentJs) || _ws == null || _ws.State != WebSocketState.Open)
                    return null;
                string eclReinstall = await EvalAsync(_eclAgentJs, ct);
                _eclAgentInstalled = eclReinstall.IndexOf("MSFSBA_ECL_INSTALLED", StringComparison.Ordinal) >= 0;
                if (!_eclAgentInstalled) return null;
            }
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

        /// <summary>
        /// On-demand generic display scrape of the E/WD over the shared A380X_EWD socket
        /// (the SD "Upper E/WD" page). Returns the reconstructed rows, or null if the
        /// agent isn't ready. Funnels through this one socket because a second
        /// CoherentDisplayClient on A380X_EWD would be rejected (one inspector per page).
        /// </summary>
        public async Task<List<string>?> ScrapeDisplayAsync()
        {
            if (_disposed) return null;   // can be called from the A380 def after an aircraft-swap Dispose()
            try
            {
                var ct = _cts?.Token ?? CancellationToken.None;
                if (!await EnsureConnected(ct)) return null;
                if (!_dispAgentInstalled)
                {
                    // One transient eval timeout used to latch this false until the socket
                    // actually dropped — self-heal by re-installing on the live socket
                    // (idempotent IIFE; runs under the same connection the monitor owns).
                    if (string.IsNullOrEmpty(_dispAgentJs) || _ws == null || _ws.State != WebSocketState.Open)
                        return null;
                    string dispReinstall = await EvalAsync(_dispAgentJs, ct);
                    _dispAgentInstalled = dispReinstall.IndexOf("MSFSBA_DISP_INSTALLED", StringComparison.Ordinal) >= 0;
                    if (!_dispAgentInstalled) return null;
                }
                string raw = await EvalAsync("window.__MSFSBA_DISP ? __MSFSBA_DISP.scrape() : ''", ct);
                if (string.IsNullOrEmpty(raw)) { _dispAgentInstalled = false; return null; }
                var res = JsonSerializer.Deserialize<DispScrapeResult>(raw);
                if (res == null || !res.ok) return null;
                return res.rows ?? new List<string>();
            }
            catch { return null; }
        }

        private sealed class DispScrapeResult
        {
            public bool ok { get; set; }
            public List<string>? rows { get; set; }
        }

        /// <summary>
        /// Map a raw E/WD status-area token (box text or display-overlay class
        /// token) to the phrase spoken to the pilot. STS = a STATUS-page reminder,
        /// ADV = an advisory is on the SD, FAILURE PENDING = the FWS is still
        /// processing; the overlays are the display power-up self-test / maintenance
        /// screens. Unknown tokens pass through verbatim.
        /// </summary>
        private static string StatusPhrase(string token)
        {
            string t = (token ?? "").Trim();
            string u = t.ToUpperInvariant();
            if (u == "SELF TEST") return "Display safety test in progress";
            if (u == "MAINTENANCE MODE") return "Display maintenance mode";
            if (u == "ENGINEERING TEST") return "Display engineering test mode";
            if (u == "FAILURE PENDING") return "Failure pending";
            if (u == "ADV") return "Advisory, check the system display";
            if (u == "STS") return "Status message, check the status page";
            if (u.StartsWith("STS") && u.Contains("DEFRD"))
                return "Status and deferred procedure, check the status page";
            return t;
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

        // Append the EWD colour name to a spoken line (", Red" / ", Amber" / ...). The agent
        // supplies the colour from the EclLine CSS class (warnings/memos) or the FWC colour code
        // (PFD lines). No-op when there's no colour.
        private static string WithColour(string text, string? colour)
            => string.IsNullOrEmpty(colour) ? text : $"{text}, {colour}";

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
            // Intentionally NOT disposing _sendLock or _connectLock: the background RunLoop is not
            // joined here and may be pending on either's WaitAsync — disposing a SemaphoreSlim with
            // waiters throws ObjectDisposedException. Neither wait handle is materialized, so nothing
            // leaks; Stop() cancels _cts, which unblocks the pending WaitAsync(ct).
        }

        private sealed class ScrapeResult
        {
            public bool ok { get; set; }
            public string? error { get; set; }
            public List<Warning>? warnings { get; set; }
            public List<Labelled>? memos { get; set; }
            public List<Labelled>? pfd { get; set; }
            public List<string>? status { get; set; }
        }

        private sealed class Warning
        {
            public string? text { get; set; }
            public string? sev { get; set; }
            public string? colour { get; set; }   // EWD colour name (Red/Amber/Cyan/White/Green)
            public bool headline { get; set; }
            public bool selected { get; set; }
        }

        // A memo / PFD line carrying its EWD colour name so the auto-announce reads it.
        private sealed class Labelled
        {
            public string? text { get; set; }
            public string? colour { get; set; }
        }

        private sealed class EclScrapeResult
        {
            public bool ok { get; set; }
            public bool shown { get; set; }
            public List<EclRow>? rows { get; set; }
        }
    }
}
