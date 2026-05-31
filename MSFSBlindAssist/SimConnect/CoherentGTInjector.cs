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
    private string? _lastInjectedHash;   // SHA-256 prefix of the last injected bridge JS content
    private bool _disposed;
    private readonly object _lock = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs) };

    private volatile bool _isConnected;
    public bool IsConnected => _isConnected;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    /// <summary>
    /// When set, called each poll cycle. If it returns false while the injector
    /// believes it already injected, _lastInjectedPageId is cleared so the next
    /// poll re-injects (handles silent eval failure and page reloads where the
    /// Coherent GT page ID stays stable).
    /// </summary>
    public Func<bool>? HeartbeatAlive { get; set; }

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
            _lastInjectedHash = null;
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

        // 2. Read bridge JS now so we can hash it for version comparison
        if (!File.Exists(_bridgeJsPath))
        {
            System.Diagnostics.Debug.WriteLine($"[CGTInjector] Bridge JS not found: {_bridgeJsPath}");
            return;
        }
        string bridgeJs = await File.ReadAllTextAsync(_bridgeJsPath, ct);
        string currentHash = ComputeBridgeHash(bridgeJs);

        // 3. Already injected — verify JS is still present and up-to-date.
        // Page reloads clear the window flag instantly; this detects the reload within one
        // poll cycle (~3 s) instead of waiting for the 6 s heartbeat timeout.
        // A changed bridge JS hash triggers automatic hot-reload without app restart.
        if (pageId == _lastInjectedPageId)
        {
            bool? jsPresent = await CheckBridgeLoadedAsync(pageId, ct);
            bool heartbeatDead = HeartbeatAlive?.Invoke() == false;
            bool versionChanged = currentHash != _lastInjectedHash;

            // JS present, heartbeat alive, same version — nothing to do.
            if (jsPresent == true && !versionChanged) return;
            // Can't check CDP, heartbeat fresh, same version — assume ok.
            if (jsPresent == null && !heartbeatDead && !versionChanged) return;

            // Bridge JS file was updated while running: clear the double-load guard
            // (window._a32nx_efb_bridge_loaded) before reinjecting so the new version
            // isn't silently skipped by the existing bridge's entry-point guard.
            if (jsPresent == true && versionChanged)
            {
                System.Diagnostics.Debug.WriteLine($"[CGTInjector] Bridge JS updated (hash changed), hot-reloading");
                await ClearLoadGuardAsync(pageId, ct);
                await Task.Delay(200, ct);
            }

            // JS is gone, stale heartbeat, or version changed — fall through to reinject.
            _lastInjectedPageId = null;
        }

        // 4. Brief delay on re-injection so the page finishes reloading
        if (_lastInjectedPageId != null)
            await Task.Delay(ReinjectDelayMs, ct);

        // 5. Inject
        bool ok = await InjectAsync(pageId, bridgeJs, ct);
        if (ok)
        {
            _lastInjectedPageId = pageId;
            _lastInjectedHash = currentHash;
            SetConnected(true);
            System.Diagnostics.Debug.WriteLine($"[CGTInjector] Injected into page {pageId} (hash={currentHash})");
        }
    }

    /// <summary>
    /// Clears the bridge's double-load guard so a fresh injection isn't skipped
    /// by the existing bridge's entry-point check.
    /// </summary>
    private async Task ClearLoadGuardAsync(string pageId, CancellationToken ct)
    {
        try { await InjectAsync(pageId, "window._a32nx_efb_bridge_loaded = false;", ct); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CGTInjector] ClearLoadGuard: {ex.Message}"); }
    }

    private static string ComputeBridgeHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes))[..16]; // first 16 hex chars is enough
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

            string? vcockpitEfbId = null;

            foreach (var page in doc.RootElement.EnumerateArray())
            {
                string? title = page.TryGetProperty("title", out var t) ? t.GetString() : null;

                // id is a JSON number in Coherent GT's pagelist — GetString() returns null for numbers
                string? id = null;
                if (page.TryGetProperty("id", out var idProp))
                    id = idProp.ValueKind == JsonValueKind.Number
                        ? idProp.GetRawText()
                        : idProp.GetString();

                if (title == null || id == null) continue;

                // FBW EFB runs as a VCockpit instrument in both FS2020 and FS2024.
                // This page always has #MSFS_REACT_MOUNT; the MSFS EFB OS shell does not.
                if (title.EndsWith("- EFB", StringComparison.OrdinalIgnoreCase))
                    vcockpitEfbId = id;
            }

            return vcockpitEfbId;
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
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(WsConnectTimeoutMs);
            try { await tcp.ConnectAsync("127.0.0.1", 19999, connectCts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }

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
                    if (total >= buf.Length) break;
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
    /// Opens a short-lived CDP connection to check if the bridge JS flag is still
    /// set in the page. Returns true if loaded, false if definitely gone,
    /// null if the check could not complete (treat as "unknown").
    /// </summary>
    private static async Task<bool?> CheckBridgeLoadedAsync(string pageId, CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(WsConnectTimeoutMs);
            try { await tcp.ConnectAsync("127.0.0.1", 19999, connectCts.Token); }
            catch { return null; }

            using var stream = tcp.GetStream();

            string wsKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string handshake =
                $"GET /devtools/page/{pageId} HTTP/1.1\r\n" +
                "Host: 127.0.0.1:19999\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {wsKey}\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(handshake), ct);

            var buf = new byte[2048];
            int total = 0;
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (stream.DataAvailable)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(total), ct);
                    if (n == 0) break;
                    total += n;
                    if (Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n")) break;
                }
                else await Task.Delay(20, ct);
            }
            if (!Encoding.ASCII.GetString(buf, 0, total).Contains("101")) return null;

            string cdp = JsonSerializer.Serialize(new
            {
                id = 2,
                method = "Runtime.evaluate",
                @params = new { expression = "!!window._a32nx_efb_bridge_loaded", returnByValue = true }
            });
            await stream.WriteAsync(BuildWebSocketTextFrame(Encoding.UTF8.GetBytes(cdp)), ct);

            // Read response frame (server→client, unmasked).
            // Response is ~70 bytes; keep reading until we have the full payload.
            total = 0;
            deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                if (stream.DataAvailable)
                {
                    int n = await stream.ReadAsync(buf.AsMemory(total), ct);
                    if (n == 0) break;
                    total += n;
                    if (total >= 2)
                    {
                        int lb = buf[1] & 0x7F;
                        int hdrLen = lb <= 125 ? 2 : (lb == 126 ? 4 : 10);
                        if (total >= hdrLen)
                        {
                            int pl = lb <= 125 ? lb : (lb == 126 ? ((buf[2] << 8) | buf[3]) : -1);
                            if (pl >= 0 && total >= hdrLen + pl) break;
                        }
                    }
                }
                else await Task.Delay(20, ct);
            }

            if (total < 4) return null;
            int lenByte = buf[1] & 0x7F;
            int payloadStart = lenByte <= 125 ? 2 : 4;
            int payloadLen  = lenByte <= 125 ? lenByte : ((buf[2] << 8) | buf[3]);
            if (total < payloadStart + payloadLen) return null;

            string json = Encoding.UTF8.GetString(buf, payloadStart, payloadLen);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var r1) &&
                r1.TryGetProperty("result", out var r2) &&
                r2.TryGetProperty("value", out var val))
                return val.GetBoolean();

            return null;
        }
        catch { return null; }
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
