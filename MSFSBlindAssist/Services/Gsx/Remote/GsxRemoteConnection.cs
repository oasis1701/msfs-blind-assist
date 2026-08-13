using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// The WebSocket link to GSX's Couatl Remote API. Nothing above this class knows
/// about sockets.
///
/// GSX serves the PWA over HTTP and the API over an Upgrade on the SAME port, so
/// the URL is simply ws://127.0.0.1:8744/. No pairing, no auth.
/// </summary>
public sealed class GsxRemoteConnection : IDisposable
{
    public const int DefaultPort = 8744;

    // Fast-start backoff: after a RESTART_COUATL the listener returns within a few
    // hundred ms. A flat 2 s retry makes the app look dead through every restart.
    private const int ReconnectMinMs = 250;
    private const int ReconnectMaxMs = 600;
    private const int CommandTimeoutMs = 5000;
    private const int ReceiveBufferBytes = 64 * 1024;

    private static readonly string SubscribeJson =
        """{"type":"subscribe","channels":["state","prompts","toasts"]}""";

    public event Action<GsxFrame>? FrameReceived;
    public event Action<bool>? ConnectedChanged;

    private readonly int _port;
    private readonly GsxPendingRequests _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private ClientWebSocket? _ws;
    private Task? _loop;
    private volatile bool _connected;
    private bool _disposed;

    public GsxRemoteConnection(int port = DefaultPort) => _port = port;

    public bool IsConnected => _connected;

    public void Start()
    {
        // Guard on IsCompleted, not just non-null: Stop() only cancels and does not
        // (and must not) block waiting for RunAsync to unwind, so a Stop() immediately
        // followed by a Start() can otherwise land while the old loop is still mid-flight
        // — two RunAsync instances would then both write the shared _ws/_connected fields
        // and both call FailAll, corrupting whichever connection wins the race. Refusing
        // to start while the previous loop is still completing is the safe direction: the
        // caller can retry, whereas a second live loop cannot be un-started.
        if (_disposed) return;
        if (_loop != null && !_loop.IsCompleted)
        {
            // Reachable on a sim disconnect->reconnect cycle, where GsxService
            // has ALREADY logged "Remote API client starting." — without this
            // line the log claims a start that never happened, and the socket
            // stays down until the next reconnect with nothing to say why.
            Log.Debug("Gsx", "Remote API start refused: the previous receive loop is still unwinding.");
            return;
        }

        try { _cts?.Dispose(); } catch { /* already disposed */ }
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
        // RunAsync catches everything it knows how to handle and only exits by falling
        // off the end (cancellation) — a Faulted task here means something truly
        // unexpected escaped it. Observe it so it is logged instead of silently vanishing
        // as an unobserved task exception, and so IsCompleted still flips (letting a
        // future Start() recover).
        _loop.ContinueWith(
            t => Log.Error("Gsx", $"Remote API loop exited unexpectedly: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
        Log.Debug("Gsx", $"Remote API client started (port {_port}).");
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* already disposed */ }
        SetConnected(false);
        _pending.FailAll("connection stopped");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int backoff = ReconnectMinMs;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                _ws = ws;
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/"), ct).ConfigureAwait(false);

                await SendRawAsync(SubscribeJson, ct).ConfigureAwait(false);
                SetConnected(true);
                backoff = ReconnectMinMs;

                await ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // GSX not running, or the engine restarted mid-flight. Neither is an
                // error worth shouting about - we simply retry.
                Log.Debug("Gsx", $"Remote API connection lost: {ex.Message}");
            }
            finally
            {
                _ws = null;
                SetConnected(false);
                _pending.FailAll("connection lost");
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(backoff, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            backoff = Math.Min(ReconnectMaxMs, (int)(backoff * 1.6));
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[ReceiveBufferBytes];
        // Accumulate RAW BYTES and decode once at EndOfMessage — never per chunk. GSX's
        // handlerData payload (~1.7 MB) always arrives fragmented across many ReceiveAsync
        // calls, and Encoding.UTF8.GetString() on each chunk independently silently
        // corrupts any multi-byte character split across a chunk boundary: UTF8's default
        // replacement fallback never throws, so each half of a split character becomes
        // U+FFFD and the result is still syntactically valid JSON — no exception, no log
        // line, nothing to reveal it happened. Same bug, same fix, as
        // CoherentDebuggerClient.ReceiveLoop (SimConnect/CoherentDebuggerClient.cs).
        using var ms = new MemoryStream();

        // ONE outstanding ReceiveAsync at a time. Issuing a second while one is
        // pending faults the socket - a real failure hit while probing this API.
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            if (result.MessageType == WebSocketMessageType.Close) return;

            ms.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;   // handlerData alone is ~1.7 MB

            string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            ms.SetLength(0);   // reset for the next message (also clamps Position to 0)

            GsxFrame frame = GsxFrame.Parse(json);   // never throws
            if (frame.Type == GsxFrameType.Unknown) continue;
            if (_pending.Complete(frame)) continue;  // a command ack we were awaiting

            try { FrameReceived?.Invoke(frame); }
            catch (Exception ex) { Log.Error("Gsx", $"Remote frame handler threw: {ex.Message}"); }
        }
    }

    public async Task<GsxResult> SendAsync(string verb, object? args = null)
    {
        if (!_connected) return GsxResult.Fail("not connected");

        var (id, task) = _pending.Register();
        string json = BuildCommand(verb, args, id);

        try
        {
            await SendRawAsync(json, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Built via JsonSerializer rather than a hand-rolled interpolated string: a
            // $$"""..."""-style raw string here hits CS9007 (the trailing `}}` that
            // closes both the nested "error" object and the outer object reads as an
            // ambiguous interpolation-end with only two '$'), and three '$' would work
            // but is more fragile than just serializing the shape directly.
            string syntheticFailure = JsonSerializer.Serialize(new
            {
                type = "result",
                ok = false,
                id,
                error = new { code = "send_failed", message = "send failed" }
            });
            _pending.Complete(GsxFrame.Parse(syntheticFailure));
            Log.Debug("Gsx", $"Remote command '{verb}' send failed: {ex.Message}");
            return GsxResult.Fail("send failed");
        }

        var completed = await Task.WhenAny(task, Task.Delay(CommandTimeoutMs)).ConfigureAwait(false);
        if (completed != task) return GsxResult.Fail("timed out");

        var r = await task.ConfigureAwait(false);
        if (!r.Ok) Log.Debug("Gsx", $"Remote command '{verb}' failed: {r.ErrorCode} {r.ErrorMessage}");
        return r;
    }

    /// <summary>Fire-and-forget: for commands whose ack we do not need.</summary>
    public void Send(string verb, object? args = null)
        => _ = SendAsync(verb, args);

    private static string BuildCommand(string verb, object? args, string id)
    {
        // Hand-built so `args` can be any anonymous object without a serializer context.
        string argsJson = args is null ? "" : ",\"args\":" + JsonSerializer.Serialize(args);
        return $"{{\"type\":\"command\",\"verb\":{JsonSerializer.Serialize(verb)},\"id\":{JsonSerializer.Serialize(id)}{argsJson}}}";
    }

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        var ws = _ws;
        if (ws is null || ws.State != WebSocketState.Open) throw new InvalidOperationException("socket not open");

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                    .ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    private void SetConnected(bool value)
    {
        if (_connected == value) return;
        _connected = value;
        try { ConnectedChanged?.Invoke(value); }
        catch (Exception ex) { Log.Error("Gsx", $"ConnectedChanged handler threw: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { }
        // Intentionally NOT disposing _sendLock: Stop() cancels but does not join the
        // background loop, which may be sitting inside SendRawAsync's try/finally having
        // already acquired the lock (past WaitAsync, mid-send). Disposing here would race
        // its eventual Release(), which throws ObjectDisposedException once disposed.
        // Same reasoning, same fix as CoherentDebuggerClient.Dispose() in
        // SimConnect/CoherentDebuggerClient.cs. No unmanaged handle is ever allocated
        // (this type never touches AvailableWaitHandle), so nothing leaks by skipping it.
    }
}
