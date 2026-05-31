using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Owns the single Coherent GT devtools (CDP) connection to the FBW A32NX EFB
/// page. Installs the generic agent (window.__MSFSBA_FLYPAD) once per connection,
/// then drives it via Runtime.evaluate request/response correlated by message id.
///
/// Coherent GT accepts only ONE devtools connection at a time, so this client is
/// the sole CDP consumer for the EFB page; its lifecycle is tied to the EFB form.
/// .NET's ClientWebSocket rejects Coherent GT's non-standard upgrade response, so
/// the WebSocket handshake and framing are done manually.
/// </summary>
public sealed class CoherentEFBClient : IDisposable
{
    private const string PageListUrl = "http://127.0.0.1:19999/pagelist.json";
    private const int Port = 19999;
    private const int HttpTimeoutMs = 2000;
    private const int WsConnectTimeoutMs = 3000;
    private const int EvalTimeoutMs = 4000;
    private const int HeartbeatMs = 4000;
    private const int ReconnectDelayMs = 1500;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs) };

    private readonly string _agentJsPath;
    private readonly SynchronizationContext? _sync;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _nextId;
    private volatile bool _ready;
    private bool _disposed;
    private readonly object _lifecycleLock = new();

    public bool IsReady => _ready;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public CoherentEFBClient(string agentJsPath)
    {
        _agentJsPath = agentJsPath;
        _sync = SynchronizationContext.Current;
    }

    // ── lifecycle ───────────────────────────────────────────────────────────

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        TearDown();
        SetReady(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_ready)
                {
                    if (await ConnectAndInstallAsync(ct)) SetReady(true);
                    else { await Delay(ReconnectDelayMs, ct); continue; }
                }

                // Heartbeat: ping; if the agent global is gone or the socket died, reconnect.
                await Delay(HeartbeatMs, ct);
                string pong = await PingAsync(ct);
                if (pong != "MSFSBA_FLYPAD_OK")
                {
                    SetReady(false);
                    TearDown();
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                SetReady(false);
                TearDown();
                await Delay(ReconnectDelayMs, ct);
            }
        }
    }

    private async Task<bool> ConnectAndInstallAsync(CancellationToken ct)
    {
        string? pageId = await FindEfbPageIdAsync(ct);
        if (pageId == null) return false;
        if (!File.Exists(_agentJsPath)) return false;

        if (!await OpenSocketAsync(pageId, ct)) return false;

        // Start the receive pump before sending anything.
        _ = Task.Run(() => ReceivePumpAsync(_stream!, ct));

        string agentJs = await File.ReadAllTextAsync(_agentJsPath, ct);
        await EvalRawAsync(agentJs, returnByValue: false, ct);          // install
        string pong = await PingAsync(ct);
        if (pong != "MSFSBA_FLYPAD_OK") return false;
        await PowerOnAsync(ct);
        return true;
    }

    private async Task<bool> OpenSocketAsync(string pageId, CancellationToken ct)
    {
        try
        {
            _tcp = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(WsConnectTimeoutMs);
            await _tcp.ConnectAsync("127.0.0.1", Port, connectCts.Token);
            _stream = _tcp.GetStream();

            string wsKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string handshake =
                $"GET /devtools/page/{pageId} HTTP/1.1\r\n" +
                "Host: 127.0.0.1:19999\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {wsKey}\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";
            await _stream.WriteAsync(Encoding.ASCII.GetBytes(handshake), ct);

            // Read until end-of-headers; confirm 101.
            var buf = new byte[4096];
            int total = 0;
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (_stream.DataAvailable)
                {
                    if (total >= buf.Length) break;
                    int n = await _stream.ReadAsync(buf.AsMemory(total), ct);
                    if (n == 0) break;
                    total += n;
                    if (Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n")) break;
                }
                else await Task.Delay(20, ct);
            }
            return Encoding.ASCII.GetString(buf, 0, total).Contains("101");
        }
        catch { return false; }
    }

    private void TearDown()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
        foreach (var kv in _pending)
            kv.Value.TrySetException(new IOException("connection torn down"));
        _pending.Clear();
    }

    // ── receive pump: reassemble server→client (unmasked) frames ─────────────

    private async Task ReceivePumpAsync(NetworkStream stream, CancellationToken ct)
    {
        var acc = new List<byte>(8192);
        var tmp = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(tmp, ct);
                if (n == 0) break;
                acc.AddRange(tmp.AsSpan(0, n).ToArray());
                DrainFrames(acc);
            }
        }
        catch { /* socket closed; RunAsync heartbeat triggers reconnect */ }
    }

    /// <summary>
    /// Extracts every complete WebSocket frame currently buffered. Server→client
    /// frames are unmasked. Handles 7-bit, 16-bit (126) and 64-bit (127) lengths
    /// and leaves partial frames in the buffer for the next read.
    /// </summary>
    private void DrainFrames(List<byte> acc)
    {
        while (true)
        {
            if (acc.Count < 2) return;
            int b1 = acc[1];
            bool masked = (b1 & 0x80) != 0;        // server frames are not masked
            long len = b1 & 0x7F;
            int headerLen = 2;
            if (len == 126)
            {
                if (acc.Count < 4) return;
                len = (acc[2] << 8) | acc[3];
                headerLen = 4;
            }
            else if (len == 127)
            {
                if (acc.Count < 10) return;
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | acc[2 + i];
                headerLen = 10;
            }
            int maskLen = masked ? 4 : 0;
            long frameLen = headerLen + maskLen + len;
            if (acc.Count < frameLen) return;       // incomplete; wait for more

            int opcode = acc[0] & 0x0F;
            int payloadStart = headerLen + maskLen;
            var payload = new byte[len];
            for (long i = 0; i < len; i++)
            {
                byte v = acc[(int)(payloadStart + i)];
                if (masked) v = (byte)(v ^ acc[headerLen + (int)(i % 4)]);
                payload[i] = v;
            }
            acc.RemoveRange(0, (int)frameLen);

            if (opcode == 0x1 || opcode == 0x2) // text / binary
                DispatchResponse(Encoding.UTF8.GetString(payload));
            // opcode 0x8 close / 0x9 ping / 0xA pong: ignore (heartbeat handles liveness)
        }
    }

    private void DispatchResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl)) return; // CDP event, not a response
            int id = idEl.GetInt32();
            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(root.Clone());
        }
        catch { /* ignore malformed frame */ }
    }

    // ── request/response ──────────────────────────────────────────────────────

    /// <summary>Runs an expression and returns the CDP response root element.</summary>
    private async Task<JsonElement> EvalRawAsync(string expression, bool returnByValue, CancellationToken ct)
    {
        var stream = _stream ?? throw new IOException("not connected");
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        string cdp = JsonSerializer.Serialize(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new { expression, returnByValue, awaitPromise = false }
        });
        var frame = BuildWebSocketTextFrame(Encoding.UTF8.GetBytes(cdp));
        await _writeLock.WaitAsync(ct);
        try { await stream.WriteAsync(frame, ct); }
        finally { _writeLock.Release(); }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(EvalTimeoutMs);
        await using var reg = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var t)) t.TrySetException(new TimeoutException("CDP eval timed out"));
        });
        return await tcs.Task;
    }

    /// <summary>Returns result.result.value as a string (for agent calls that return strings).</summary>
    private async Task<string> EvalStringAsync(string expression, CancellationToken ct)
    {
        var root = await EvalRawAsync(expression, returnByValue: true, ct);
        if (root.TryGetProperty("result", out var r1) &&
            r1.TryGetProperty("result", out var r2) &&
            r2.TryGetProperty("value", out var val) &&
            val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? "";
        return "";
    }

    // ── public agent API ──────────────────────────────────────────────────────

    private const string AgentGuard = "window.__MSFSBA_FLYPAD ? ";

    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        try { return await EvalStringAsync(AgentGuard + "__MSFSBA_FLYPAD.ping() : 'NO_AGENT'", ct); }
        catch { return ""; }
    }

    public async Task PowerOnAsync(CancellationToken ct = default)
    {
        try { await EvalStringAsync(AgentGuard + "__MSFSBA_FLYPAD.powerOn() : 'NO_AGENT'", ct); }
        catch { /* best effort */ }
    }

    public async Task<FlypadScrape> ScrapeAsync(CancellationToken ct = default)
    {
        try
        {
            string json = await EvalStringAsync(
                AgentGuard + "__MSFSBA_FLYPAD.scrape() : '{\"ok\":false,\"error\":\"agent not installed\"}'", ct);
            if (string.IsNullOrEmpty(json))
                return new FlypadScrape { Ok = false, Error = "no response" };
            var scrape = JsonSerializer.Deserialize<FlypadScrape>(json);
            return scrape ?? new FlypadScrape { Ok = false, Error = "parse failed" };
        }
        catch (Exception ex) { return new FlypadScrape { Ok = false, Error = ex.Message }; }
    }

    public async Task<bool> ClickAsync(int idx, CancellationToken ct = default)
    {
        try
        {
            string r = await EvalStringAsync(
                AgentGuard + "__MSFSBA_FLYPAD.clickElement(" + idx + ") : 'NO_AGENT'", ct);
            return r == "ok";
        }
        catch { return false; }
    }

    public async Task<bool> SetValueAsync(int idx, string text, CancellationToken ct = default)
    {
        string js = JsonSerializer.Serialize(text); // safely-quoted JS string literal
        try
        {
            string r = await EvalStringAsync(
                AgentGuard + "__MSFSBA_FLYPAD.setValue(" + idx + ", " + js + ") : 'NO_AGENT'", ct);
            return r == "ok";
        }
        catch { return false; }
    }

    // ── helpers (lifted from CoherentGTInjector) ───────────────────────────────

    private static async Task<string?> FindEfbPageIdAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(PageListUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;
            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var page in doc.RootElement.EnumerateArray())
            {
                string? title = page.TryGetProperty("title", out var t) ? t.GetString() : null;
                string? id = null;
                if (page.TryGetProperty("id", out var idProp))
                    id = idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : idProp.GetString();
                if (title == null || id == null) continue;
                if (title.EndsWith("- EFB", StringComparison.OrdinalIgnoreCase)) return id;
            }
        }
        catch { }
        return null;
    }

    private static byte[] BuildWebSocketTextFrame(byte[] payload)
    {
        int len = payload.Length;
        byte[] mask = new byte[4];
        RandomNumberGenerator.Fill(mask);
        var header = new List<byte> { 0x81 };
        if (len <= 125) header.Add((byte)(0x80 | len));
        else if (len <= 65535) { header.Add(0xFE); header.Add((byte)(len >> 8)); header.Add((byte)(len & 0xFF)); }
        else { header.Add(0xFF); for (int i = 7; i >= 0; i--) header.Add((byte)((len >> (8 * i)) & 0xFF)); }
        header.AddRange(mask);
        var masked = new byte[len];
        for (int i = 0; i < len; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);
        var frame = new byte[header.Count + len];
        header.ToArray().CopyTo(frame, 0);
        masked.CopyTo(frame, header.Count);
        return frame;
    }

    private static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    private void SetReady(bool value)
    {
        if (_ready == value) return;
        _ready = value;
        var evt = value ? Connected : Disconnected;
        if (_sync != null) _sync.Post(_ => evt?.Invoke(this, EventArgs.Empty), null);
        else evt?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _writeLock.Dispose();
    }
}
