using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Connects to the FlyByWire SimBridge MCDU relay websocket and exposes the Captain
/// MCDU screen as <see cref="MCDUDisplayData"/>. Mirrors the FenixMCDUService shape
/// (connect-loop + backoff + UI marshalling) but uses a single socket for both directions.
///
/// IMPORTANT — the protocol cannot separate Captain and First Officer MCDUs.
/// FBW's A320_Neo_CDU_MainDisplay.sendUpdate() builds ONE screenState from its own
/// display and assigns that same object to BOTH the "left" and "right" keys of every
/// update message (only annunciators/brightness differ). This FBW version runs a single
/// MCDU instrument writing both keys (no sender tag); the no-side-separation conclusion
/// is unchanged. So content.left == content.right in every message and a "side selector"
/// cannot pick one MCDU. We therefore mirror FBW's own web remote MCDU exactly: only
/// ever control the Captain MCDU (event:left) and read the single shared screen
/// (content.left). Verified live: left == right in 100% of observed messages.
/// </summary>
public class FlyByWireMCDUService : IDisposable
{
    private readonly string _host;

    // The protocol's "left"/"right" keys both carry the same screen (see class remarks);
    // we always control and read the Captain MCDU, matching FBW's own remote.
    private const string CaptainSide = "left";

    private ClientWebSocket? _ws;

    // Serializes ALL SendAsync calls on the single socket. ClientWebSocket allows only one
    // outstanding send; SendButtonPress is fire-and-forget while SendTextToMCDU awaits a
    // sequential per-character loop, so two concurrent sends threw InvalidOperationException
    // (swallowed by SendRaw's catch → silently dropped keypress).
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private readonly SynchronizationContext? _syncContext;
    private bool _isConnected;
    private bool _disposed;
    private int _reconnectAttempt;
    private static readonly int[] ReconnectDelays = { 1000, 3000, 6000, 12000, 30000 };

    public event Action<MCDUDisplayData>? DisplayUpdated;
    public event Action<bool>? ConnectionStatusChanged;
    public event Action<List<string>>? PrintReceived;

    public bool IsConnected => _isConnected;

    public FlyByWireMCDUService(string host = "localhost:8380")
    {
        _host = host;
        _syncContext = SynchronizationContext.Current;
    }

    private string WsUrl => $"ws://{_host}/interfaces/v1/mcdu";

    public void Connect()
    {
        if (_disposed) { return; }
        _cts = new CancellationTokenSource();
        _ = ConnectLoop(_cts.Token);
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        CloseWebSocket();
        SetConnected(false);
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceive(ct);
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Connection error: {ex.Message}");
                    SetConnected(false);
                }
                // Cancellation-path exceptions (socket disposed under a pending
                // ReceiveAsync during Disconnect) are expected — swallow so they
                // don't surface as unobserved task exceptions.
            }

            if (ct.IsCancellationRequested) { break; }

            int delay = ReconnectDelays[Math.Min(_reconnectAttempt, ReconnectDelays.Length - 1)];
            _reconnectAttempt++;
            try { await Task.Delay(delay, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task ConnectAndReceive(CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        _ws = ws;
        try
        {
            await ws.ConnectAsync(new Uri(WsUrl), ct);
            _reconnectAttempt = 0;
            SetConnected(true);
            await SendRaw("requestUpdate", ct);

            var buffer = new byte[131072];
            // Accumulate raw bytes and decode once at EndOfMessage — per-fragment decoding
            // corrupts a multibyte UTF-8 char (°, arrows) split across a receive boundary.
            var ms = new System.IO.MemoryStream();
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close) { return; }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            }
        }
        finally
        {
            // Release THIS attempt's socket on every exit (close frame, exception,
            // cancellation). CompareExchange clears the field only when it still points at
            // OUR socket — a concurrent Dispose/Disconnect may have claimed it first via
            // Interlocked.Exchange, in which case it already aborted+disposed this same
            // instance and the calls below are idempotent no-ops.
            Interlocked.CompareExchange(ref _ws, null, ws);
            try { ws.Abort(); } catch { }
            try { ws.Dispose(); } catch { }
        }
    }

    private void HandleMessage(string msg)
    {
        // The gateway relays every message (including our own sends).
        int idx = msg.IndexOf(':');
        string type = idx == -1 ? msg : msg.Substring(0, idx);

        // Handle print: messages (ATIS/OFP print output) before the update filter.
        // FBW sends print:{lines:[...]} where lines are plain strings (markup already
        // stripped) that may contain embedded '\n' for multi-line entries.
        if (type == "print")
        {
            try
            {
                var payload = JObject.Parse(msg.Substring(idx + 1));
                var rawLines = payload["lines"] as JArray;
                if (rawLines != null)
                {
                    var lines = new List<string>();
                    foreach (var tok in rawLines)
                    {
                        // Each entry may embed '\n' (FBW strips {tag} markup before sending).
                        foreach (var part in tok.ToString().Split('\n'))
                        {
                            string trimmed = part.Trim();
                            if (!string.IsNullOrEmpty(trimmed)) { lines.Add(trimmed); }
                        }
                    }
                    if (lines.Count > 0)
                    {
                        PostToUI(() => PrintReceived?.Invoke(lines));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Print parse error: {ex.Message}");
            }
            return;
        }

        if (type != "update") { return; }

        try
        {
            var content = JObject.Parse(msg.Substring(idx + 1));
            // MCDU1 unpowered (AC ESS SHED) renders empty lines while MCDU2 may have
            // content — fall back to "right" when "left" carries no text at all.
            if (content[CaptainSide] is not JObject side) { return; }
            var data = FbwMcduFormat.BuildDisplayData(side);
            if (IsBlankScreen(data) && content["right"] is JObject rightSide)
            {
                var rightData = FbwMcduFormat.BuildDisplayData(rightSide);
                if (!IsBlankScreen(rightData)) { data = rightData; }
            }
            PostToUI(() => DisplayUpdated?.Invoke(data));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true when the display carries no readable text — title, scratchpad, and all
    /// 14 raw line slots are empty or whitespace. Used to detect an unpowered MCDU side.
    /// </summary>
    private static bool IsBlankScreen(MCDUDisplayData d)
    {
        if (!string.IsNullOrWhiteSpace(d.Title)) { return false; }
        if (!string.IsNullOrWhiteSpace(d.Scratchpad)) { return false; }
        foreach (var line in d.RawLines)
        {
            if (!string.IsNullOrWhiteSpace(line)) { return false; }
        }
        return true;
    }

    /// <summary>Send a single MCDU key (e.g. "L1", "INIT", "DOT", "CLR") to the Captain MCDU.</summary>
    public Task SendButtonPress(string key) => SendRaw($"event:{CaptainSide}:{key}", CancellationToken.None);

    public Task RequestUpdate() => SendRaw("requestUpdate", CancellationToken.None);

    private async Task SendRaw(string message, CancellationToken ct)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) { return; }
        var bytes = Encoding.UTF8.GetBytes(message);
        try
        {
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Send error ({message}): {ex.Message}");
        }
    }

    private void SetConnected(bool connected)
    {
        if (_isConnected == connected) { return; }
        _isConnected = connected;
        PostToUI(() => ConnectionStatusChanged?.Invoke(connected));
    }

    private void PostToUI(Action action)
    {
        if (_syncContext != null) { _syncContext.Post(_ => action(), null); }
        else { action(); }
    }

    private void CloseWebSocket()
    {
        // Interlocked.Exchange guarantees exactly ONE caller tears a given socket down.
        // Dispose() on the UI thread and the pool thread's ConnectAndReceive finally used to
        // race through the null check together: the UI thread parked up to 2 s in
        // CloseAsync(...).Wait() (UI freeze) while the pool thread disposed and nulled the
        // field under it, then NRE'd at _ws.Dispose() outside any catch. Abort() (no close
        // handshake, never blocks) matches the Coherent clients; the SimBridge gateway
        // treats it like any dropped client.
        var ws = Interlocked.Exchange(ref _ws, null);
        if (ws == null) { return; }
        try { ws.Abort(); } catch { }
        try { ws.Dispose(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _cts?.Cancel();
        CloseWebSocket();
        _cts?.Dispose();
    }
}
