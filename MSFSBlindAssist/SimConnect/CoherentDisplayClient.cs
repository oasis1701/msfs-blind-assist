using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// GENERIC reader for any FlyByWire A380X cockpit DISPLAY rendered in a Coherent
    /// GT view (SD / EWD / PFD / ND / ISIS), through the MSFS Coherent GT debugger
    /// (127.0.0.1:19999). NO injection. Parameterised by a view-title needle.
    ///
    /// Installs coherent-display-agent.js (window.__MSFSBA_DISP) and polls
    /// __MSFSBA_DISP.scrape(), which reconstructs readable ROWS from the SVG/HTML by
    /// clustering leaf text on Y and sorting each cluster by X. This surfaces the
    /// crew-visible DECODED values (oxygen PSI, GW/FOB, N1/EGT, temps, baro, …) that
    /// the SimVar path can only get as raw ARINC429 words — so the display windows
    /// can show exactly what the screen shows.
    ///
    /// RowsUpdated fires (on the creating thread) only when the row set actually
    /// changes, so the screen reader is not spammed. Polling can be paused when no
    /// window is open (SetActive).
    /// </summary>
    public sealed class CoherentDisplayClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;

        private readonly string _titleNeedle;
        private readonly int _pollIntervalMs;
        private readonly string _agentFileName;

        /// <summary>Raised (on the creating thread) with the new rows when they change.</summary>
        public event Action<List<string>>? RowsUpdated;
        public event Action<string>? Error;

        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        // Serializes EnsureConnected: the background RunLoop poll AND the public ScrapeNowAsync (called
        // from the UI thread by F5 / the RMP form's debounce timer) both call EnsureConnected. Without
        // this, two threads could tear down _ws and ConnectAsync concurrently, opening a SECOND inspector
        // socket to the same Coherent page — which Coherent GT rejects (one socket per page), the exact
        // "display frozen / not refreshing" failure. _sendLock only covers SendAsync, not connection setup.
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private int _msgId;
        private volatile bool _connected;
        private volatile bool _agentInstalled;
        private volatile bool _active = true;
        private string _lastHash = "";
        private List<string> _lastRows = new();
        private bool _disposed;

        /// <summary>One-shot latch so a bad agent is reported once, not every 2 s.</summary>
        private bool _reportedAgentFailure;

        public bool IsConnected => _connected;
        public List<string> CurrentRows => _lastRows;

        /// <param name="titleNeedle">Substring of the Coherent view title, e.g. "A380X_SDv2".</param>
        /// <param name="pollIntervalMs">How often to re-scrape while active.</param>
        public CoherentDisplayClient(string titleNeedle, int pollIntervalMs = 1200,
            string agentFileName = "coherent-display-agent.js")
        {
            _titleNeedle = titleNeedle;
            _pollIntervalMs = pollIntervalMs;
            _agentFileName = agentFileName;
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
                    Log.Debug("SimConnect", 
                        "CoherentDisplayClient.Start() called after Stop() — not supported; create a new instance.");
                    System.Diagnostics.Debug.Assert(false,
                        "CoherentDisplayClient: Start() after Stop() is a no-op. Dispose and create a new client instead.");
                }
                return;
            }
            _cts = new CancellationTokenSource();
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Resources", _agentFileName);
                _agentJs = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                RaiseError($"Could not load display agent script: {ex.Message}");
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
        /// Hand the display over to another reader, or take it back.
        ///
        /// ⚠️ THIS MUST RELEASE THE SOCKET, NOT MERELY STOP POLLING, AND FOR YEARS IT DID NOT.
        /// Coherent GT allows ONE inspector socket per view. Setting a flag left this client's
        /// WebSocket open on AS1000_PFD, so when the PFD window then tried to read the same
        /// screen its connection was refused and it showed:
        ///
        ///     "The G1000 allows only one debugger connection per screen, so the usual cause is
        ///      that something else is already reading this one - another copy of this window,
        ///      or a developer tool."
        ///
        /// The "something else" was MSFSBA'S OWN CAS MONITOR. The message sent the pilot hunting
        /// for a developer tool that was never running - live report: restarted the PC to be
        /// sure, and the display still would not open. The handover was designed (the CAS
        /// monitor's own comment says "the window therefore takes the socket") and only half
        /// built: the flag stopped the polling, nothing let go of the connection.
        ///
        /// Deactivating therefore tears the socket down the way <see cref="Stop"/> does, but
        /// leaves the cancellation token alive so RunLoop keeps spinning - it calls
        /// EnsureConnected on every active pass, so reactivation reconnects by itself with no
        /// extra plumbing. Aborting without taking _connectLock follows Stop()'s existing
        /// precedent: this is called from the UI thread, where waiting on that semaphore could
        /// deadlock against the very loop being released, and EnsureConnected already copes
        /// with the socket going null underneath it.
        /// </summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active)
            {
                // Forces a fresh push once reconnected - the rows have not changed from this
                // client's point of view, but the window that had the socket may have moved
                // the display somewhere else entirely.
                _lastHash = "";
                return;
            }

            try { _ws?.Abort(); } catch { /* handover must never throw */ }
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            _connected = false;
            _agentInstalled = false;
        }

        /// <summary>Force a one-shot scrape now and return the rows (used by F5 refresh).</summary>
        public async Task<List<string>> ScrapeNowAsync()
        {
            if (_disposed) return _lastRows;   // can be called from a form's F5/poll after Dispose()
            try
            {
                if (!await EnsureConnected(_cts?.Token ?? CancellationToken.None)) return _lastRows;
                var rows = await ScrapeOnce(_cts?.Token ?? CancellationToken.None);
                if (rows != null) { _lastRows = rows; return rows; }
            }
            catch { }
            return _lastRows;
        }

        /// <summary>
        /// Runs one expression in the page and returns what it evaluated to.
        ///
        /// This is the WRITE path for controls SimConnect cannot reach. It exists because
        /// the DA40's G1000 answers its softkeys over SimConnect but ignores the rest of
        /// its bezel there — the FMS knob, MENU, ENT, CLR, Direct-To, FPL, PROC, the range
        /// knob and the map joystick all arrive only through the instrument's own
        /// onInteractionEvent, measured both plainly and with a uniquifying calculator
        /// prefix. Everything that CAN go over SimConnect still does; this is for the
        /// keys that have no other road.
        ///
        /// Serialised behind the same connect lock as the poll, so a key press and a
        /// scrape can never interleave on one socket.
        /// </summary>
        public async Task<string> InvokeAsync(string expression)
        {
            if (_disposed) return "";
            try
            {
                var ct = _cts?.Token ?? CancellationToken.None;
                if (!await EnsureConnected(ct)) return "";
                return await EvalAsync(expression, ct);
            }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"CoherentDisplayClient[{_titleNeedle}] invoke: {ex.Message}");
                return "";
            }
        }

        // ---- connection + poll loop -------------------------------------

        private async Task RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!_active) { await Task.Delay(500, ct); continue; }
                    if (!await EnsureConnected(ct))
                    {
                        await Task.Delay(ReconnectDelayMs, ct);
                        continue;
                    }
                    var rows = await ScrapeOnce(ct);
                    if (rows != null)
                    {
                        string hash = string.Join("\n", rows);
                        if (hash != _lastHash)
                        {
                            _lastHash = hash;
                            _lastRows = rows;
                            RaiseRows(rows);
                        }
                    }
                    await Task.Delay(_pollIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Debug("SimConnect", $"CoherentDisplayClient[{_titleNeedle}] loop: {ex.Message}");
                    _connected = false; _agentInstalled = false;
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

        private async Task<List<string>?> ScrapeOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_DISP ? __MSFSBA_DISP.scrape() : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _agentInstalled = false; return null; }
            ScrapeResult? result;
            try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
            catch { return null; }
            if (result == null || !result.ok) return null;
            _connected = true;
            return result.rows ?? new List<string>();
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

            // Socket still OPEN but the agent went missing (an eval timed out / the page re-evaluated
            // and cleared window.__MSFSBA_DISP) — just re-install the agent on the SAME socket. This
            // is the common transient case and avoids a reconnect entirely.
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf("MSFSBA_DISP_INSTALLED", StringComparison.Ordinal) >= 0;
                if (_agentInstalled) { _connected = true; return true; }
            }

            // CRITICAL: tear down any existing socket BEFORE opening a new one. Coherent GT allows
            // only ONE inspector connection per page, so opening a SECOND socket to the same page
            // while the first is still alive is REJECTED — which left the client stuck forever on
            // stale _lastRows (the "RMP display frozen / not refreshing" bug). Always release first.
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
                _agentInstalled = false;
            }

            int? pageId = await ResolvePageId(ct);
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
            _agentInstalled = install.IndexOf("MSFSBA_DISP_INSTALLED", StringComparison.Ordinal) >= 0;

            // SAY SO when the agent does not answer with the token. Without this the loop
            // retries for ever, silently: no error, no rows, nothing in the log, and a
            // window stuck on "Connecting...". That is precisely how a DA40 agent
            // returning its own version string instead of the token went undiagnosed.
            // Reported ONCE per socket, not per retry, so a genuinely absent view does not
            // fill the log.
            if (!_agentInstalled && !_reportedAgentFailure)
            {
                _reportedAgentFailure = true;
                RaiseError($"The display agent did not install ({_agentFileName}). " +
                           "It must return a string containing MSFSBA_DISP_INSTALLED; " +
                           $"it returned {(string.IsNullOrEmpty(install) ? "nothing" : "\"" + install + "\"")}.");
            }
            else if (_agentInstalled)
            {
                _reportedAgentFailure = false;
            }

            _connected = _agentInstalled;
            return _agentInstalled;
            }
            finally { _connectLock.Release(); }
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
                    if (title.IndexOf(_titleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"CoherentDisplayClient ResolvePageId: {ex.Message}");
            }
            return null;
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
                Log.Debug("SimConnect", $"CoherentDisplayClient receive: {ex.Message}");
            }
            finally { _connected = false; _agentInstalled = false; }
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

        private void RaiseRows(List<string> rows)
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
            // Intentionally NOT disposing _sendLock or _connectLock: the background RunLoop is not
            // joined here and may be pending on either's WaitAsync — disposing a SemaphoreSlim with
            // waiters throws ObjectDisposedException. Neither wait handle is materialized, so nothing
            // leaks; Stop() cancels _cts, which unblocks the pending WaitAsync(ct).
        }

        private sealed class ScrapeResult
        {
            public bool ok { get; set; }
            public string? error { get; set; }
            public List<string>? rows { get; set; }
        }
    }
}
