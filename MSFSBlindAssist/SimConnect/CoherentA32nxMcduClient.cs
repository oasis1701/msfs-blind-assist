using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// Reads and drives the FlyByWire A32NX MCDU over the MSFS Coherent GT remote debugger
    /// (127.0.0.1:19999), the same transport the A380's MFD/MCDU uses, in place of
    /// SimBridge's relay websocket. A persistent inspector socket is held on the MCDU view
    /// ("A32NX_MCDU"; the Headwind A330's is "A339X_MCDU" — the needle is per airframe),
    /// <c>Resources/coherent-a32nx-mcdu-agent.js</c> is installed once into the page, and
    /// while the MCDU window is visible <c>read()</c> is polled every
    /// <see cref="PollIntervalMs"/> ms. Its answer is the SAME <c>{left, right}</c> body the
    /// relay streams, decoded by the shared <see cref="FbwMcduUpdate"/>.
    ///
    /// Coherent GT accepts only ONE inspector socket per view. The D / Shift+D flight-info
    /// readout evaluates against this very view, so while this client holds it that
    /// readout must go through <see cref="EvalForResultAsync"/> rather than a one-shot
    /// <see cref="CoherentEvalClient"/> eval (which would be refused). Like the A380 client,
    /// the socket and the agent are KEPT WARM while the window is closed — only the poll
    /// stops — so that readout works with the window closed and reopening needs no
    /// reconnect. Restart is not supported: dispose and create a new instance.
    /// </summary>
    public sealed class CoherentA32nxMcduClient : IDisposable
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private const string AgentFile = "coherent-a32nx-mcdu-agent.js";
        private const string InstalledMarker = "MSFSBA_A32NX_MCDU_INSTALLED";
        private const int PollIntervalMs = 250;
        private const int IdleIntervalMs = 1000;
        private const int ReconnectDelayMs = 2000;
        private const int EvalTimeoutMs = 5000;
        private const int ConnectTimeoutMs = 4000;
        // Consecutive evals answered by nothing (timeout) before the socket is presumed dead
        // and torn down for a reconnect — a half-open socket never sends a Close frame.
        private const int DeadSocketEvalFailures = 3;

        private static readonly Regex KeyName = new("^[A-Z0-9_]{1,16}$", RegexOptions.Compiled);

        /// <summary>The Captain screen changed (posted to the UI context).</summary>
        public event Action<MCDUDisplayData>? DisplayUpdated;
        /// <summary>The MCDU became readable, or stopped being (posted to the UI context).</summary>
        public event Action<bool>? ConnectionStatusChanged;

        private readonly string _viewTitleNeedle;
        private readonly SynchronizationContext? _syncContext;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

        private CancellationTokenSource? _cts;
        private ClientWebSocket? _ws;
        private string _agentJs = "";
        private int _msgId;
        private volatile bool _socketOpen;
        private volatile bool _agentInstalled;
        private volatile bool _active;
        private volatile bool _refreshRequested;
        private bool _readable;            // last read() answered ok:true (reported state)
        private int _emptyEvalStreak;
        private string _lastRawHash = "";
        private bool _disposed;

        public CoherentA32nxMcduClient(string viewTitleNeedle)
        {
            _viewTitleNeedle = viewTitleNeedle;
            _syncContext = SynchronizationContext.Current;
        }

        /// <summary>True while this client holds the MCDU view's inspector socket with the agent installed.</summary>
        public bool HoldsView => _socketOpen && _agentInstalled;

        /// <summary>True when the MCDU instrument answered the last read — what the window calls "connected".</summary>
        public bool IsConnected => HoldsView && _readable;

        public void Start()
        {
            if (_cts != null)
            {
                if (_cts.IsCancellationRequested)
                {
                    Log.Debug("SimConnect", "CoherentA32nxMcduClient.Start() after Stop() — not supported; create a new instance.");
                    System.Diagnostics.Debug.Assert(false, "CoherentA32nxMcduClient: Start() after Stop() is a no-op.");
                }
                return;
            }
            _cts = new CancellationTokenSource();
            try
            {
                _agentJs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", AgentFile));
            }
            catch (Exception ex)
            {
                Log.Warn("SimConnect", $"Could not load {AgentFile}: {ex.Message}");
            }
            _ = Task.Run(() => RunLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Abort(); } catch { }
            _ws = null;
            _socketOpen = false;
            _agentInstalled = false;
            SetReadable(false);
        }

        /// <summary>
        /// Poll only while the MCDU window is visible. The socket and agent stay warm while
        /// idle (see the class remarks); re-activation forces a full re-push.
        /// </summary>
        public void SetActive(bool active)
        {
            _active = active;
            if (active) { _lastRawHash = ""; }
        }

        /// <summary>Re-read the screen on the next loop pass even if unchanged.</summary>
        public void RequestRefresh()
        {
            _lastRawHash = "";
            _refreshRequested = true;
        }

        /// <summary>
        /// Press one Captain-MCDU key ("INIT", "L1", "DOT", "CLR" …). Fire-and-forget by the
        /// caller; the next poll reflects the new screen. Returns the dispatch path the
        /// agent used ("" when nothing was reachable).
        /// </summary>
        public async Task<string> SendKeyAsync(string key)
        {
            if (!KeyName.IsMatch(key)) { return ""; }
            string result = await EvalAsync($"window.__MSFSBA_A32NX_MCDU ? __MSFSBA_A32NX_MCDU.press(\"{key}\") : 'no-agent'");
            _refreshRequested = true;
            if (!string.IsNullOrEmpty(result) && result.StartsWith("no-", StringComparison.Ordinal))
            {
                Log.Debug("SimConnect", $"A32NX MCDU key {key}: {result}");
            }
            return result;
        }

        /// <summary>
        /// Evaluate an arbitrary self-contained expression on the MCDU view over this
        /// client's socket — the D / Shift+D flight-info script rides here while the view
        /// is held. Returns "" when the socket is down or the eval times out.
        /// </summary>
        public Task<string> EvalForResultAsync(string expression) => EvalAsync(expression);

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

                    if (_active || _refreshRequested) { await PollOnce(ct); }
                    else { await PingOnce(ct); }

                    if (_emptyEvalStreak >= DeadSocketEvalFailures)
                    {
                        Log.Debug("SimConnect", "CoherentA32nxMcduClient: no eval answered — reconnecting.");
                        DropSocket();
                        continue;
                    }
                    await Task.Delay(_active ? PollIntervalMs : IdleIntervalMs, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Debug("SimConnect", $"CoherentA32nxMcduClient loop: {ex.Message}");
                    DropSocket();
                    try { await Task.Delay(ReconnectDelayMs, ct); } catch { break; }
                }
            }
        }

        private void DropSocket()
        {
            _socketOpen = false;
            _agentInstalled = false;
            _emptyEvalStreak = 0;
            SetReadable(false);
            try { _ws?.Abort(); } catch { }
            _ws = null;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
        }

        private async Task<bool> EnsureConnected(CancellationToken ct)
        {
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            // Socket still open but the agent went missing (the page re-evaluated) —
            // re-install on the SAME socket rather than reconnecting.
            if (_ws != null && _ws.State == WebSocketState.Open && !string.IsNullOrEmpty(_agentJs))
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf(InstalledMarker, StringComparison.Ordinal) >= 0;
                if (_agentInstalled) { _socketOpen = true; return true; }
            }

            // Tear down any existing socket BEFORE opening a new one: Coherent GT allows only
            // ONE inspector connection per view, and a second one while the first is alive
            // orphans the healthy socket and blocks the view for the rest of the process.
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
                _agentInstalled = false;
            }
            if (string.IsNullOrEmpty(_agentJs)) { return false; }

            int? pageId = await ResolvePageId(ct);
            if (pageId == null) { _socketOpen = false; return false; }

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
                // A connect timeout, not shutdown — must not read as "stop the loop".
                try { ws.Dispose(); } catch { }
                _socketOpen = false;
                return false;
            }
            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
            _emptyEvalStreak = 0;
            _ = Task.Run(() => ReceiveLoop(ws, ct));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf(InstalledMarker, StringComparison.Ordinal) >= 0;
            _socketOpen = _agentInstalled;
            if (_agentInstalled)
            {
                Log.Info("SimConnect", $"A32NX MCDU agent installed on view '{_viewTitleNeedle}' (page {pageId.Value}).");
                _lastRawHash = "";
            }
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
                    if (title.IndexOf(_viewTitleNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                        && view.TryGetProperty("id", out var idEl))
                    {
                        if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                        if (int.TryParse(idEl.GetString(), out var n)) return n;
                    }
                }
            }
            catch (Exception ex)
            {
                // The debugger is not up, or the view is not loaded (another aircraft, the
                // menu). Routine while the SimBridge fallback carries the window.
                Log.Debug("SimConnect", $"CoherentA32nxMcduClient.ResolvePageId: {ex.Message}");
            }
            return null;
        }

        private async Task PollOnce(CancellationToken ct)
        {
            _refreshRequested = false;
            string raw = await EvalAsync("window.__MSFSBA_A32NX_MCDU ? __MSFSBA_A32NX_MCDU.read() : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _emptyEvalStreak++; return; }
            _emptyEvalStreak = 0;

            JObject body;
            try { body = JObject.Parse(raw); }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"A32NX MCDU read parse error: {ex.Message}");
                return;
            }

            if (body["ok"]?.Value<bool>() != true)
            {
                // The view is up but the instrument is not (still loading, or unloading).
                SetReadable(false);
                return;
            }
            if (body["content"] is not JObject content) { SetReadable(false); return; }

            SetReadable(true);

            // The screen is re-read every poll; only a CHANGED frame reaches the window.
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(raw)));
            if (hash == _lastRawHash) { return; }
            _lastRawHash = hash;

            var data = FbwMcduUpdate.Parse(content);
            if (data == null) { return; }
            PostToUI(() => DisplayUpdated?.Invoke(data));
        }

        private async Task PingOnce(CancellationToken ct)
        {
            string raw = await EvalAsync("window.__MSFSBA_A32NX_MCDU ? __MSFSBA_A32NX_MCDU.ping() : ''", ct);
            if (string.IsNullOrEmpty(raw)) { _emptyEvalStreak++; return; }
            _emptyEvalStreak = 0;
            SetReadable(raw == "ready");
        }

        private void SetReadable(bool readable)
        {
            if (_readable == readable) { return; }
            _readable = readable;
            PostToUI(() => ConnectionStatusChanged?.Invoke(readable));
        }

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
            try
            {
                await _sendLock.WaitAsync(ct);
                try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
                finally { _sendLock.Release(); }
            }
            catch (Exception)
            {
                _pending.TryRemove(id, out _);
                return "";
            }

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
            // {"id":N,"result":{"result":{"type":"string","value":"..."},"wasThrown":false}}
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
            // separately corrupts a multibyte UTF-8 char (°, arrows) split across reads.
            var ms = new MemoryStream();
            try
            {
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (res.MessageType == WebSocketMessageType.Close) { return; }
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Debug("SimConnect", $"CoherentA32nxMcduClient receive: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_ws, ws))
                {
                    _socketOpen = false;
                    _agentInstalled = false;
                    SetReadable(false);
                }
            }
        }

        private void DispatchMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id)
                    && _pending.TryGetValue(id, out var tcs))
                {
                    tcs.TrySetResult(root.Clone());   // clone: the value must outlive the JsonDocument
                }
                // Unsolicited protocol events (no matching id) are ignored.
            }
            catch { /* malformed frame — ignore */ }
        }

        private void PostToUI(Action action)
        {
            if (_syncContext != null) { _syncContext.Post(_ => action(), null); }
            else { action(); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            _http.Dispose();
            // _sendLock is deliberately not disposed: RunLoop is not joined and may be waiting
            // on it; Stop() cancelled _cts, which releases the waiter, and the wait handle is
            // never materialised, so nothing leaks (the sibling Coherent clients do the same).
        }
    }
}
