using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Connects to the FlyByWire SimBridge MCDU relay websocket and exposes the selected
/// side's screen as <see cref="MCDUDisplayData"/>. Mirrors the FenixMCDUService shape
/// (connect-loop + backoff + UI marshalling) but uses a single socket for both directions.
/// </summary>
public class FlyByWireMCDUService : IDisposable
{
    private readonly string _host;
    private string _side = "left"; // "left" = Captain (MCDU1), "right" = First Officer (MCDU2)

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly SynchronizationContext? _syncContext;
    private bool _isConnected;
    private bool _disposed;
    private int _reconnectAttempt;
    private static readonly int[] ReconnectDelays = { 1000, 3000, 6000, 12000, 30000 };

    public event Action<MCDUDisplayData>? DisplayUpdated;
    public event Action<bool>? ConnectionStatusChanged;

    public bool IsConnected => _isConnected;
    public string Side => _side;

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
    }

    /// <summary>Switch the displayed side and pull a fresh screen for it.</summary>
    public void SwitchSide(string side)
    {
        if (side != "left" && side != "right") { return; }
        if (side == _side) { return; }
        _side = side;
        _ = RequestUpdate();
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceive(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Connection error: {ex.Message}");
                SetConnected(false);
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
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(WsUrl), ct);
        _reconnectAttempt = 0;
        SetConnected(true);
        await SendRaw("requestUpdate", ct);

        var buffer = new byte[131072];
        var sb = new StringBuilder();
        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            sb.Clear();
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) { return; }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            HandleMessage(sb.ToString());
        }
    }

    private void HandleMessage(string msg)
    {
        // The gateway relays every message (including our own sends); only "update:" matters.
        int idx = msg.IndexOf(':');
        string type = idx == -1 ? msg : msg.Substring(0, idx);
        if (type != "update") { return; }

        try
        {
            var content = JObject.Parse(msg.Substring(idx + 1));
            if (content[_side] is not JObject side) { return; }
            var data = FbwMcduFormat.BuildDisplayData(side);
            PostToUI(() => DisplayUpdated?.Invoke(data));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Parse error: {ex.Message}");
        }
    }

    /// <summary>Send a single MCDU key (e.g. "L1", "INIT", "DOT", "CLR") to the current side.</summary>
    public Task SendButtonPress(string key) => SendRaw($"event:{_side}:{key}", CancellationToken.None);

    public Task RequestUpdate() => SendRaw("requestUpdate", CancellationToken.None);

    private async Task SendRaw(string message, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) { return; }
        var bytes = Encoding.UTF8.GetBytes(message);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
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
        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                        .Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch { }
            _ws.Dispose();
            _ws = null;
        }
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
