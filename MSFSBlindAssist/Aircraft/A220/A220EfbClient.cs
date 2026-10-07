using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// On-demand Coherent GT debugger client for ONE Synaptic A220 EFB tablet view
/// (default "efbA220_1"; the F/O tablet is "efbA220_2"). Installs
/// <c>Resources/coherent-a220-efb-agent.js</c> (window.__a220Efb) and exposes the
/// agent's collect / click-by-index / set-field-by-index surface.
///
/// Same connection invariants as every repo Coherent client (CoherentHS787CduClient
/// et al.): the view is resolved BY TITLE on every (re)connect (page ids shuffle per
/// sim session), <see cref="EnsureConnectedAsync"/> re-installs the agent on a
/// still-open socket instead of reconnecting, any dead socket is Abort()ed AND
/// Dispose()d BEFORE ConnectAsync (Coherent GT allows only ONE inspector socket per
/// page), and connection setup is serialized by its own connect-lock — the send-lock
/// only covers SendAsync and does not close the connect race.
///
/// Unlike the polling monitors this client is purely ON-DEMAND: nothing touches the
/// socket unless the EFB form asks for a collect or an action.
/// </summary>
public sealed class A220EfbClient : IDisposable
{
    private const string DebuggerBase = "http://127.0.0.1:19999";
    private const string AgentFileName = "coherent-a220-efb-agent.js";
    private const string InstallMarker = "MSFSBA_A220_EFB_INSTALLED";
    private const int EvalTimeoutMs = 5000;
    private const int ConnectTimeoutMs = 4000;

    private readonly string _titleNeedle;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();

    private ClientWebSocket? _ws;
    private string _agentJs = "";
    private int _msgId;
    private volatile bool _agentInstalled;
    private volatile bool _disposed;

    public A220EfbClient(string titleNeedle = "efbA220_1")
    {
        _titleNeedle = titleNeedle;
    }

    // ---- public surface --------------------------------------------------

    public sealed class Item
    {
        public int i { get; set; }
        public string kind { get; set; } = "";
        public string label { get; set; } = "";
        public string value { get; set; } = "";
    }

    public sealed class Snapshot
    {
        public bool ok { get; set; }
        public string? page { get; set; }
        public string? error { get; set; }
        public List<Item>? items { get; set; }
    }

    /// <summary>Collect the current EFB page. Never throws — a failure comes back as ok=false.</summary>
    public async Task<Snapshot> CollectAsync()
    {
        string raw = await EvalOnAgentAsync("window.__a220Efb ? __a220Efb.collect() : ''");
        if (string.IsNullOrEmpty(raw))
        {
            _agentInstalled = false;
            return new Snapshot { ok = false, error = "The A220 EFB is not reachable. Make sure the simulator is running with the A220 loaded." };
        }
        try
        {
            var snap = JsonSerializer.Deserialize<Snapshot>(raw);
            if (snap == null) return new Snapshot { ok = false, error = "The EFB returned an unreadable response." };
            snap.items ??= new List<Item>();
            return snap;
        }
        catch (Exception ex)
        {
            Log.Debug("A220Efb", $"collect parse: {ex.Message}");
            return new Snapshot { ok = false, error = "The EFB returned an unreadable response." };
        }
    }

    /// <summary>Click the collected item at <paramref name="index"/>. Returns "ok" or an error string.</summary>
    public Task<string> ClickAsync(int index)
        => EvalOnAgentAsync($"window.__a220Efb ? __a220Efb.click({index}) : 'ERR agent not installed'");

    /// <summary>Set the field at <paramref name="index"/> and commit (input+change+blur). Returns the committed value.</summary>
    public Task<string> SetFieldAsync(int index, string value)
        => EvalOnAgentAsync($"window.__a220Efb ? __a220Efb.setField({index}, {JsonSerializer.Serialize(value)}) : 'ERR agent not installed'");

    public void Shutdown() => Dispose();

    // ---- connection ------------------------------------------------------

    private async Task<string> EvalOnAgentAsync(string expression)
    {
        try
        {
            if (!await EnsureConnectedAsync()) return "";
            string result = await EvalAsync(expression, _cts.Token);
            if (string.IsNullOrEmpty(result))
            {
                // Socket may have silently died (aircraft swap / view reload) — one retry
                // through a full reconnect before giving up.
                _agentInstalled = false;
                if (!await EnsureConnectedAsync()) return "";
                result = await EvalAsync(expression, _cts.Token);
            }
            return result;
        }
        catch (OperationCanceledException) { return ""; }
        catch (Exception ex)
        {
            Log.Debug("A220Efb", $"eval: {ex.Message}");
            return "";
        }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_disposed) return false;
        await _connectLock.WaitAsync(_cts.Token);
        try
        {
            if (_disposed) return false;
            if (_ws != null && _ws.State == WebSocketState.Open && _agentInstalled) return true;

            if (_agentJs.Length == 0)
            {
                try
                {
                    _agentJs = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", AgentFileName));
                }
                catch (Exception ex)
                {
                    Log.Error("A220Efb", $"Could not load {AgentFileName}: {ex.Message}");
                    return false;
                }
            }

            // Re-install the agent on a still-open socket (view may have reloaded).
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                string reinstall = await EvalAsync(_agentJs, _cts.Token);
                _agentInstalled = reinstall.Contains(InstallMarker, StringComparison.Ordinal);
                if (_agentInstalled) return true;
            }

            // ONE inspector socket per page — Abort + Dispose BEFORE reconnecting.
            if (_ws != null)
            {
                try { _ws.Abort(); } catch { }
                try { _ws.Dispose(); } catch { }
                _ws = null;
                _agentInstalled = false;
            }

            int? pageId = await ResolvePageIdAsync();
            if (pageId == null) return false;

            var ws = new ClientWebSocket();
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                connectCts.CancelAfter(ConnectTimeoutMs);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}"), connectCts.Token);
            }
            catch
            {
                try { ws.Dispose(); } catch { }
                return false;
            }

            _ws = ws;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
            _ = Task.Run(() => ReceiveLoop(ws, _cts.Token));

            string install = await EvalAsync(_agentJs, _cts.Token);
            _agentInstalled = install.Contains(InstallMarker, StringComparison.Ordinal);
            return _agentInstalled;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task<int?> ResolvePageIdAsync()
    {
        try
        {
            string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", _cts.Token);
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
        catch (Exception ex) { Log.Debug("A220Efb", $"ResolvePageId: {ex.Message}"); }
        return null;
    }

    // ---- Runtime.evaluate over the inspector socket ----------------------

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
        catch (Exception ex) { Log.Debug("A220Efb", $"receive: {ex.Message}"); }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _ws?.Abort(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        try { _cts.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
        try { _connectLock.Dispose(); } catch { }
        try { _sendLock.Dispose(); } catch { }
    }
}
