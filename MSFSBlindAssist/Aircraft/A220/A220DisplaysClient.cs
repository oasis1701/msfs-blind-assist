using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// The SINGLE owner of the Synaptic A220 "DisplayUnits" Coherent inspector socket
/// (one-socket-per-page is a hard Coherent GT rule — the later FMS/ECL scrape must
/// share this client, never open its own). Installs
/// <c>coherent-a220-displays-agent.js</c> and serves on-demand
/// <see cref="SnapshotAsync"/> polls for the def's display pump; no background
/// RunLoop of its own.
///
/// Connection invariants (CLAUDE.md / the HS787-family clients this mirrors):
/// <c>EnsureConnected</c> re-installs the agent on a still-open socket instead of
/// reconnecting, Aborts+Disposes any existing socket BEFORE <c>ConnectAsync</c>,
/// is serialized by its own <c>_connectLock</c> (the <c>_sendLock</c> only covers
/// SendAsync), and resolves the page BY TITLE from pagelist.json — ids shuffle.
/// </summary>
public sealed class A220DisplaysClient : IDisposable
{
    private const string DebuggerBase = "http://127.0.0.1:19999";
    private const string ViewTitleNeedle = "DisplayUnits";
    private const int ReconnectBackoffMs = 3000;
    private const int EvalTimeoutMs = 5000;
    private const int ConnectTimeoutMs = 4000;
    private const string InstallMarker = "MSFSBA_A220_DISPLAYS_INSTALLED";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();

    private ClientWebSocket? _ws;
    private string _agentJs = "";
    private int _msgId;
    private volatile bool _agentInstalled;
    private long _nextConnectAttemptTicks; // Environment.TickCount64 gate — don't hammer pagelist.json at poll rate while the view is absent
    private volatile bool _disposed;

    /// <summary>
    /// One agent poll: connect/install if needed, then evaluate snapshot(). Returns
    /// the agent's JSON string, or null on any failure (disconnected sim, view not
    /// up yet, eval timeout) — callers just skip the poll.
    /// </summary>
    public async Task<string?> SnapshotAsync()
    {
        if (_disposed) return null;
        try
        {
            if (!await EnsureConnected(_cts.Token)) return null;
            string raw = await EvalAsync("window.__a220Displays ? __a220Displays.snapshot() : ''", _cts.Token);
            if (string.IsNullOrEmpty(raw))
            {
                _agentInstalled = false; // page reloaded out from under us — reinstall next poll
                return null;
            }
            return raw;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Log.Debug("A220", $"A220DisplaysClient snapshot: {ex.Message}");
            DropSocket();
            return null;
        }
    }

    /// <summary>
    /// Generic agent call for the FMS/ECL surfaces (P3c/P4): <paramref name="call"/>
    /// is the agent function invocation, e.g. <c>fms()</c> or
    /// <c>clickWinText("ecl","NORMAL",0)</c> (args already JS-escaped by the
    /// caller — use <see cref="JsString"/>). Shares this client's one socket and
    /// serialization with the display pump; returns the agent's string result or
    /// null on any failure.
    /// </summary>
    public async Task<string?> CallAgentAsync(string call)
    {
        if (_disposed) return null;
        try
        {
            if (!await EnsureConnected(_cts.Token)) return null;
            string raw = await EvalAsync($"window.__a220Displays ? __a220Displays.{call} : ''", _cts.Token);
            if (string.IsNullOrEmpty(raw))
            {
                _agentInstalled = false; // page reloaded out from under us — reinstall next call
                return null;
            }
            return raw;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Log.Debug("A220", $"A220DisplaysClient call '{call}': {ex.Message}");
            DropSocket();
            return null;
        }
    }

    /// <summary>JS string literal (double-quoted, escaped) for CallAgentAsync args.</summary>
    public static string JsString(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ") + "\"";

    public void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        DropSocket();
        try { _cts.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
    }

    public void Dispose() => Shutdown();

    // ---- connection plumbing (mirrors CoherentHS787CasClient) ----------------

    private async Task<bool> EnsureConnected(CancellationToken ct)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_disposed) return false; // never resurrect a socket on a disposed client (swap race)
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            LoadAgentJs();
            if (_agentJs.Length == 0) return false;

            // Still-open socket, agent gone (page reload): re-install, do NOT reconnect.
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, ct);
                _agentInstalled = reinstall.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0;
                if (_agentInstalled) return true;
            }

            if (Environment.TickCount64 < Interlocked.Read(ref _nextConnectAttemptTicks)) return false;

            // Abort+Dispose any existing socket BEFORE ConnectAsync — an orphaned open
            // socket permanently loses the page for the process (one socket per page).
            DropSocket();

            int? pageId = await ResolvePageId(ct);
            if (pageId == null) { ArmBackoff(); return false; }

            var ws = new ClientWebSocket();
            var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(url, connectCts.Token);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                try { ws.Dispose(); } catch { }
                ArmBackoff();
                return false;
            }
            _ws = ws;
            _ = Task.Run(() => ReceiveLoop(ws, _cts.Token));

            string install = await EvalAsync(_agentJs, ct);
            _agentInstalled = install.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0;
            if (!_agentInstalled) ArmBackoff();
            return _agentInstalled;
        }
        finally { _connectLock.Release(); }
    }

    private void ArmBackoff()
        => Interlocked.Exchange(ref _nextConnectAttemptTicks, Environment.TickCount64 + ReconnectBackoffMs);

    private void LoadAgentJs()
    {
        if (_agentJs.Length > 0) return;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-a220-displays-agent.js");
            _agentJs = File.ReadAllText(path);
        }
        catch (Exception ex) { Log.Warn("A220", $"Could not load A220 displays agent script: {ex.Message}"); }
    }

    private void DropSocket()
    {
        var ws = _ws;
        _ws = null;
        _agentInstalled = false;
        if (ws != null)
        {
            try { ws.Abort(); } catch { }
            try { ws.Dispose(); } catch { }
        }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
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
        catch (Exception ex) { Log.Debug("A220", $"A220 ResolvePageId: {ex.Message}"); }
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
                    if (res.MessageType == WebSocketMessageType.Close) { _agentInstalled = false; return; }
                    ms.Write(buf, 0, res.Count);
                } while (!res.EndOfMessage);
                DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("A220", $"A220DisplaysClient receive: {ex.Message}"); }
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
}
