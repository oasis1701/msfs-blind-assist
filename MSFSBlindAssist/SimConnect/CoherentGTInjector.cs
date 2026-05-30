using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Injects a bridge JavaScript file into the FBW EFB Coherent GT instrument page
/// using the Chrome DevTools Protocol (CDP) via raw TCP WebSocket.
///
/// .NET's ClientWebSocket rejects Coherent GT's non-standard
/// "Connection: Upgrade,Keep-Alive" response header, so we bypass it
/// by doing the HTTP upgrade handshake and WebSocket framing manually.
/// Only a single Runtime.evaluate outbound message is sent — no persistent
/// subscription is required after injection.
/// </summary>
public class CoherentGTInjector : IDisposable
{
    private const string PageListUrl = "http://127.0.0.1:19999/pagelist.json";
    private const int PollIntervalMs = 3000;
    private const int ReinjectDelayMs = 2000;
    private const int HttpTimeoutMs = 2000;
    private const int WsConnectTimeoutMs = 3000;
    private const int WsEvalSettleMs = 500;

    private readonly string _bridgeJsPath;
    private readonly SynchronizationContext? _syncContext;
    private CancellationTokenSource? _cts;
    private string? _lastInjectedPageId;
    private bool _disposed;
    private readonly object _lock = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs) };

    private volatile bool _isConnected;
    public bool IsConnected => _isConnected;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public CoherentGTInjector(string bridgeJsPath)
    {
        _bridgeJsPath = bridgeJsPath;
        _syncContext = SynchronizationContext.Current;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => PollLoop(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _lastInjectedPageId = null;
        }
        SetConnected(false);
    }

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await TryInjectAsync(ct); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CGTInjector] Poll: {ex.Message}"); }

            try { await Task.Delay(PollIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TryInjectAsync(CancellationToken ct)
    {
        // 1. Discover EFB page ID from Coherent GT devtools page list
        string? pageId = await FindEfbPageIdAsync(ct);
        if (pageId == null)
        {
            SetConnected(false);
            _lastInjectedPageId = null;
            return;
        }

        // 2. Already injected into this page load — skip
        if (pageId == _lastInjectedPageId) return;

        // 3. Brief delay on re-injection so the page finishes reloading
        if (_lastInjectedPageId != null)
            await Task.Delay(ReinjectDelayMs, ct);

        // 4. Read bridge JS from output directory
        if (!File.Exists(_bridgeJsPath))
        {
            System.Diagnostics.Debug.WriteLine($"[CGTInjector] Bridge JS not found: {_bridgeJsPath}");
            return;
        }
        string bridgeJs = await File.ReadAllTextAsync(_bridgeJsPath, ct);

        // 5. Inject
        bool ok = await InjectAsync(pageId, bridgeJs, ct);
        if (ok)
        {
            _lastInjectedPageId = pageId;
            SetConnected(true);
            System.Diagnostics.Debug.WriteLine($"[CGTInjector] Injected into page {pageId}");
        }
    }

    private async Task<string?> FindEfbPageIdAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, PageListUrl);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var page in doc.RootElement.EnumerateArray())
            {
                string? title = page.TryGetProperty("title", out var t) ? t.GetString() : null;
                string? id    = page.TryGetProperty("id",    out var i) ? i.GetString() : null;
                if (title != null && id != null &&
                    title.EndsWith("- EFB", StringComparison.OrdinalIgnoreCase))
                    return id;
            }
        }
        catch { /* devtools not available or not in EFB aircraft */ }
        return null;
    }

    /// <summary>
    /// Opens a raw TCP connection to Coherent GT devtools, upgrades to WebSocket,
    /// sends a single CDP Runtime.evaluate command, and closes.
    /// </summary>
    private static async Task<bool> InjectAsync(string pageId, string bridgeJs, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync("127.0.0.1", 19999);
            if (await Task.WhenAny(connectTask, Task.Delay(WsConnectTimeoutMs, ct)) != connectTask)
                return false;
            await connectTask; // rethrow any connect exception

            using var stream = tcp.GetStream();

            // ── HTTP WebSocket upgrade handshake ───────────────────────────
            string wsKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string handshake =
                $"GET /devtools/page/{pageId} HTTP/1.1\r\n" +
                "Host: 127.0.0.1:19999\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {wsKey}\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(handshake), ct);

            // Read until end-of-headers marker
            var buf = new byte[4096];
            int total = 0;
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (stream.DataAvailable)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(total), ct);
                    if (n == 0) break;
                    total += n;
                    if (Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n")) break;
                }
                else { await Task.Delay(20, ct); }
            }

            if (!Encoding.ASCII.GetString(buf, 0, total).Contains("101"))
                return false;  // upgrade refused

            // ── CDP Runtime.evaluate ───────────────────────────────────────
            string cdp = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "Runtime.evaluate",
                @params = new { expression = bridgeJs, returnByValue = false }
            });

            await stream.WriteAsync(BuildWebSocketTextFrame(Encoding.UTF8.GetBytes(cdp)), ct);

            // Give the eval time to run before we close the socket
            await Task.Delay(WsEvalSettleMs, ct);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CGTInjector] Inject failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Encodes a byte payload as an RFC 6455 text frame, client→server (masked).
    /// </summary>
    private static byte[] BuildWebSocketTextFrame(byte[] payload)
    {
        int len = payload.Length;
        byte[] mask = new byte[4];
        RandomNumberGenerator.Fill(mask);

        // Header: FIN(1) + text opcode(0x01) = 0x81; MASK(1) + length
        var header = new List<byte> { 0x81 };
        if (len <= 125)
        {
            header.Add((byte)(0x80 | len));
        }
        else if (len <= 65535)
        {
            header.Add(0xFE); // MASK=1, extended-16
            header.Add((byte)(len >> 8));
            header.Add((byte)(len & 0xFF));
        }
        else
        {
            header.Add(0xFF); // MASK=1, extended-64
            for (int i = 7; i >= 0; i--) header.Add((byte)((len >> (8 * i)) & 0xFF));
        }
        header.AddRange(mask);

        var masked = new byte[len];
        for (int i = 0; i < len; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);

        var frame = new byte[header.Count + len];
        header.ToArray().CopyTo(frame, 0);
        masked.CopyTo(frame, header.Count);
        return frame;
    }

    private void SetConnected(bool value)
    {
        if (_isConnected == value) return;
        _isConnected = value;
        var evt = value ? Connected : Disconnected;
        if (_syncContext != null) _syncContext.Post(_ => evt?.Invoke(this, EventArgs.Empty), null);
        else evt?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
