using System.Net.WebSockets;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

public class MCDUDisplayData
{
    public string Title { get; set; } = "";
    public MCDULinePair[] Lines { get; set; } = new MCDULinePair[6];
    public string Scratchpad { get; set; } = "";
    public string[] RawLines { get; set; } = new string[14];

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
    private const string WS_URL = "ws://localhost:8083/graphql";
    private const string HTTP_URL = "http://localhost:8083/graphql";

    private static readonly Dictionary<char, char> SpecialChars = new Dictionary<char, char>
    {
        { '#', '-' },  // box -> hyphen (better for Braille displays)
        { '&', '\u0394' },  // delta
        { '\u00A4', '\u2191' }, // ¤ -> up arrow
        { '\u00A5', '\u2193' }, // ¥ -> down arrow
        { '\u00A2', '\u2192' }, // ¢ -> right arrow
        { '\u00A3', '\u2190' }, // £ -> left arrow
    };

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly HttpClient _httpClient;
    private readonly SynchronizationContext? _syncContext;
    private bool _isConnected;
    private bool _disposed;
    private int _reconnectAttempt;
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
                System.Diagnostics.Debug.WriteLine($"[FenixMCDU] Connection error: {ex.Message}");
                SetConnected(false);
            }

            if (ct.IsCancellationRequested) break;

            // Reconnect with backoff
            int delay = ReconnectDelays[Math.Min(_reconnectAttempt, ReconnectDelays.Length - 1)];
            _reconnectAttempt++;
            System.Diagnostics.Debug.WriteLine($"[FenixMCDU] Reconnecting in {delay}ms (attempt {_reconnectAttempt})");

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
                variables = new { names = new[] { "aircraft.mcdu1.display" } }
            }
        }, ct);

        _reconnectAttempt = 0;
        SetConnected(true);
        System.Diagnostics.Debug.WriteLine("[FenixMCDU] Connected and subscribed");

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
                    var displayXml = msg["payload"]?["data"]?["dataRefs"]?["value"]?.ToString();
                    if (!string.IsNullOrEmpty(displayXml))
                    {
                        var displayData = ParseDisplayXml(displayXml);
                        PostToUI(() => DisplayUpdated?.Invoke(displayData));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FenixMCDU] Parse error: {ex.Message}");
                }
            }
            else if (msgType == "error" || msgType == "complete")
            {
                System.Diagnostics.Debug.WriteLine($"[FenixMCDU] Received {msgType}");
                break;
            }
        }
    }

    public async Task SendButtonPress(string buttonName)
    {
        var keyName = $"system.switches.S_CDU1_KEY_{buttonName}";

        var mutation = new
        {
            query = @"mutation ($keyName: String!) {
                dataRef {
                    writeInt(name: $keyName, value: 1)
                    __typename
                }
            }",
            variables = new { keyName }
        };

        try
        {
            var json = JsonConvert.SerializeObject(mutation);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(HTTP_URL, content);
            response.EnsureSuccessStatusCode();
            System.Diagnostics.Debug.WriteLine($"[FenixMCDU] Button press sent: {buttonName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixMCDU] Button press error ({buttonName}): {ex.Message}");
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

    private static readonly HashSet<char> ColorCodeSet = new HashSet<char> { 'a', 'c', 'g', 'm', 'w', 'y' };
    private static readonly HashSet<char> SizeCodeSet = new HashSet<char> { 's', 'l' };
    private static readonly HashSet<char> AllFormatCodes = new HashSet<char> { 'a', 'c', 'g', 'm', 'w', 'y', 's', 'l' };

    private static string StripFormatCodes(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // First pass: parse into colored segments
        var segments = ParseColorSegments(text);

        // Check if this line has mixed colors (indicates toggle/selection options)
        var distinctColors = new HashSet<char>();
        foreach (var seg in segments)
        {
            if (!string.IsNullOrWhiteSpace(seg.Text))
                distinctColors.Add(seg.Color);
        }
        bool hasMixedColors = distinctColors.Count > 1 && distinctColors.Contains('g');

        // Second pass: build output, marking green segments on mixed-color lines
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (hasMixedColors && seg.Color == 'g' && !string.IsNullOrWhiteSpace(seg.Text))
            {
                sb.Append('*');
                sb.Append(seg.Text);
            }
            else
            {
                sb.Append(seg.Text);
            }
        }

        return sb.ToString();
    }

    private static List<(char Color, string Text)> ParseColorSegments(string text)
    {
        var segments = new List<(char Color, string Text)>();
        char currentColor = 'w'; // default white
        var currentText = new StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (ColorCodeSet.Contains(c))
            {
                // Flush current segment
                if (currentText.Length > 0)
                {
                    segments.Add((currentColor, currentText.ToString()));
                    currentText.Clear();
                }
                currentColor = c;
                continue;
            }

            if (SizeCodeSet.Contains(c))
            {
                // Skip size codes
                continue;
            }

            // Handle special characters
            if (SpecialChars.TryGetValue(c, out char replacement))
            {
                currentText.Append(replacement);
            }
            else
            {
                currentText.Append(c);
            }
        }

        // Flush last segment
        if (currentText.Length > 0)
        {
            segments.Add((currentColor, currentText.ToString()));
        }

        return segments;
    }

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
