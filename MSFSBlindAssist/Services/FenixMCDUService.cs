using System.Net.WebSockets;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

public class MCDUDisplayData
{
    public string Title { get; set; } = "";
    public MCDULinePair[] Lines { get; set; } = new MCDULinePair[6];
    public string Scratchpad { get; set; } = "";
    public string[] RawLines { get; set; } = new string[14];

    // FBW-specific extras (Fenix leaves these at their defaults — no behavior change).
    public string Page { get; set; } = "";
    public bool[] Arrows { get; set; } = new bool[4];
    public List<string> Annunciators { get; set; } = new List<string>();

    public MCDUDisplayData()
    {
        for (int i = 0; i < 6; i++)
            Lines[i] = new MCDULinePair();
        for (int i = 0; i < 14; i++)
            RawLines[i] = "";
    }
}

public class MCDULinePair
{
    public string LeftLabel { get; set; } = "";
    public string LeftValue { get; set; } = "";
    public string RightLabel { get; set; } = "";
    public string RightValue { get; set; } = "";
}

public class FenixMCDUService : IDisposable
{
    // 127.0.0.1, never by name ([A320-40]): the Fenix data service listens on IPv4 only, and
    // Windows spends about 2 s refusing ::1 on every new connection before it falls back. The key
    // writes' pooled connection goes after about a minute idle, so by name the first key press
    // after a pause stalled 2 s.
    private const string WS_URL = "ws://127.0.0.1:8083/graphql";
    private const string HTTP_URL = "http://127.0.0.1:8083/graphql";

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly HttpClient _httpClient;
    private readonly SynchronizationContext? _syncContext;
    private bool _isConnected;
    private bool _disposed;
    private int _reconnectAttempt;
    // Both screens and the selected side; UI thread only (screens arrive through PostToUI).
    private readonly FenixMcduScreens _screens = new();
    private static readonly int[] ReconnectDelays = { 3000, 6000, 12000, 30000 };

    public event Action<MCDUDisplayData>? DisplayUpdated;
    public event Action<bool>? ConnectionStatusChanged;

    public bool IsConnected => _isConnected;

    public FenixMCDUService()
    {
        _syncContext = SynchronizationContext.Current;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public void Connect()
    {
        if (_disposed) return;
        _cts = new CancellationTokenSource();
        _ = ConnectLoop(_cts.Token);
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        CloseWebSocket();
        SetConnected(false);
    }

    /// <summary>
    /// Shows the other MCDU. Both screens stay subscribed, so this never reconnects: it raises
    /// <see cref="DisplayUpdated"/> with the stored screen at once (nothing before that side's first
    /// screen has arrived). UI thread only.
    /// </summary>
    public void SwitchMCDU(int mcduIndex)
    {
        if (_screens.TrySelect(mcduIndex, out var stored) && stored != null)
            DisplayUpdated?.Invoke(stored);
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndSubscribe(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Debug("Services", $"Connection error: {ex.Message}");
                SetConnected(false);
            }

            if (ct.IsCancellationRequested) break;

            // Reconnect with backoff
            int delay = ReconnectDelays[Math.Min(_reconnectAttempt, ReconnectDelays.Length - 1)];
            _reconnectAttempt++;
            Log.Debug("Services", $"Reconnecting in {delay}ms (attempt {_reconnectAttempt})");

            try { await Task.Delay(delay, ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task ConnectAndSubscribe(CancellationToken ct)
    {
        _ws = new ClientWebSocket();
        _ws.Options.AddSubProtocol("graphql-transport-ws");

        await _ws.ConnectAsync(new Uri(WS_URL), ct);

        // Send connection_init
        await SendJson(new { type = "connection_init" }, ct);

        // Wait for connection_ack
        var response = await ReceiveJson(ct);
        if (response?["type"]?.ToString() != "connection_ack")
        {
            throw new Exception($"Expected connection_ack, got: {response?["type"]}");
        }

        // Subscribe to MCDU display
        await SendJson(new
        {
            id = "1",
            type = "subscribe",
            payload = new
            {
                query = "subscription ($names: [String!]!) { dataRefs(names: $names) { name value } }",
                variables = new { names = FenixMcduSides.SubscribedRefs }
            }
        }, ct);

        _reconnectAttempt = 0;
        SetConnected(true);
        Log.Debug("Services", "Connected and subscribed");

        // Receive loop
        while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            var msg = await ReceiveJson(ct);
            if (msg == null) break;

            var msgType = msg["type"]?.ToString();
            if (msgType == "next")
            {
                try
                {
                    var dataRef = msg["payload"]?["data"]?["dataRefs"];
                    string? name = dataRef?["name"]?.ToString();
                    var displayXml = dataRef?["value"]?.ToString();
                    if (!FenixMcduSides.TryGetSide(name, out int side))
                    {
                        Log.Debug("Services", $"Ignoring unexpected dataRef '{name}'");
                    }
                    else if (!string.IsNullOrEmpty(displayXml))
                    {
                        var displayData = ParseDisplayXml(displayXml);
                        PostToUI(() => StoreScreen(side, displayData));
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Services", $"Parse error: {ex.Message}");
                }
            }
            else if (msgType == "ping")
            {
                // graphql-transport-ws keepalive: the service pings about every 15 s and the
                // protocol expects a pong. Only this loop sends after the subscribe, so this
                // never overlaps another SendAsync.
                await SendJson(new { type = "pong" }, ct);
            }
            else if (msgType == "error" || msgType == "complete")
            {
                Log.Debug("Services", $"Received {msgType}");
                break;
            }
        }
    }

    // UI thread (posted from the receive loop). Only the selected side reaches the window; the
    // other side's screen waits for a switch.
    private void StoreScreen(int side, MCDUDisplayData data)
    {
        if (_screens.Store(side, data) is { } show)
            DisplayUpdated?.Invoke(show);
    }

    public async Task SendButtonPress(string buttonName)
    {
        // The side is read once, so the press and its release reach the same MCDU even if the
        // pilot switches sides in the 50 ms between them.
        var keyName = FenixMcduSides.KeyRef(_screens.Selected, buttonName);

        // Send key press (value: 1)
        await SendKeyWrite(keyName, 1);
        await Task.Delay(50);
        // Send key release (value: 0) so the MCDU recognizes subsequent presses of the same key
        await SendKeyWrite(keyName, 0);
    }

    private async Task SendKeyWrite(string keyName, int value)
    {
        var mutation = new
        {
            query = @"mutation ($keyName: String!, $value: Int!) {
                dataRef {
                    writeInt(name: $keyName, value: $value)
                    __typename
                }
            }",
            variables = new { keyName, value }
        };

        try
        {
            var json = JsonConvert.SerializeObject(mutation);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(HTTP_URL, content);
            response.EnsureSuccessStatusCode();
            Log.Debug("Services", $"Key write sent: {keyName} = {value}");
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Key write error ({keyName} = {value}): {ex.Message}");
        }
    }

    private MCDUDisplayData ParseDisplayXml(string xml)
    {
        var data = new MCDUDisplayData();
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        if (root == null) return data;

        int lineIndex = 0;

        // Parse title
        var titleElement = root.Element("title");
        if (titleElement != null)
        {
            data.RawLines[0] = StripFormatCodes(titleElement.Value);
            data.Title = data.RawLines[0];
            lineIndex = 1;
        }

        // Parse lines
        foreach (var lineElement in root.Elements("line"))
        {
            if (lineIndex >= 13)
                break;
            data.RawLines[lineIndex] = StripFormatCodes(lineElement.Value);
            lineIndex++;
        }

        // Fill remaining lines up to 13
        while (lineIndex < 13)
        {
            data.RawLines[lineIndex] = "";
            lineIndex++;
        }

        // Parse scratchpad
        var scratchpadElement = root.Element("scratchpad");
        if (scratchpadElement != null)
        {
            data.RawLines[13] = StripFormatCodes(scratchpadElement.Value);
            data.Scratchpad = data.RawLines[13];
        }

        // Build line pairs from raw lines
        // Lines 1-2 -> pair 0, Lines 3-4 -> pair 1, etc.
        for (int i = 0; i < 6; i++)
        {
            int labelLineIdx = 1 + (i * 2);  // 1, 3, 5, 7, 9, 11
            int valueLineIdx = 2 + (i * 2);  // 2, 4, 6, 8, 10, 12

            var labelLine = data.RawLines[labelLineIdx];
            var valueLine = data.RawLines[valueLineIdx];

            // Split label line into left/right (24-char wide display)
            var (leftLabel, rightLabel) = SplitLine(labelLine);
            var (leftValue, rightValue) = SplitLine(valueLine);

            data.Lines[i] = new MCDULinePair
            {
                LeftLabel = leftLabel,
                LeftValue = leftValue,
                RightLabel = rightLabel,
                RightValue = rightValue
            };
        }

        return data;
    }

    // Format-code decoding lives in FenixMcduFormat (pure + unit-tested).
    private static string StripFormatCodes(string text) => FenixMcduFormat.StripFormatCodes(text);

    private static (string left, string right) SplitLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return ("", "");

        // MCDU display is 24 chars wide
        // Pad to 24 chars if shorter
        string padded = line.PadRight(24);
        if (padded.Length > 24) padded = padded.Substring(0, 24);

        // Left half: chars 0-11, Right half: chars 12-23
        string left = padded.Substring(0, 12).TrimEnd();
        string right = padded.Substring(12).TrimEnd();

        return (left, right);
    }

    private async Task SendJson(object obj, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var json = JsonConvert.SerializeObject(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<JObject?> ReceiveJson(CancellationToken ct)
    {
        if (_ws == null) return null;

        var buffer = new byte[65536];
        var sb = new StringBuilder();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        return JObject.Parse(sb.ToString());
    }

    private void SetConnected(bool connected)
    {
        if (_isConnected == connected) return;
        _isConnected = connected;
        PostToUI(() => ConnectionStatusChanged?.Invoke(connected));
    }

    private void PostToUI(Action action)
    {
        if (_syncContext != null)
            _syncContext.Post(_ => action(), null);
        else
            action();
    }

    private void CloseWebSocket()
    {
        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                        .Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            _ws.Dispose();
            _ws = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        CloseWebSocket();
        _httpClient.Dispose();
        _cts?.Dispose();
    }
}
