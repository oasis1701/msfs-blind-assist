// cohereval — evaluate a JS expression in an MSFS Coherent GT debugger view.
// Usage:  cohereval <titleNeedle> <exprFile>
//         cohereval <titleNeedle>            (reads expression from stdin)
// Resolves the view by title substring (e.g. "A380X_MFD", "- EFB"), connects
// to its inspector WebSocket, runs Runtime.evaluate(returnByValue), prints the
// returned value to stdout. .NET 9's ClientWebSocket handles the Coherent
// non-standard Connection header that PowerShell 5.1 chokes on.
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

string needle = args.Length > 0 ? args[0] : "A380X_MFD";
string expr = args.Length > 1 ? await File.ReadAllTextAsync(args[1]) : await Console.In.ReadToEndAsync();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
string list;
try { list = await http.GetStringAsync("http://127.0.0.1:19999/pagelist.json"); }
catch (Exception ex) { Console.Error.WriteLine("pagelist fetch failed: " + ex.Message); return 3; }

int? id = null;
using (var doc = JsonDocument.Parse(list))
    foreach (var v in doc.RootElement.EnumerateArray())
        if (v.TryGetProperty("title", out var t) &&
            (t.GetString() ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) &&
            v.TryGetProperty("id", out var idEl))
        {
            id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : int.Parse(idEl.GetString()!);
            break;
        }
if (id is null) { Console.Error.WriteLine("view not found for needle: " + needle); return 2; }

using var ws = new ClientWebSocket();
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{id.Value}"), cts.Token);

var msg = JsonSerializer.Serialize(new
{
    id = 1,
    method = "Runtime.evaluate",
    @params = new { expression = expr, returnByValue = true }
});
await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, cts.Token);

var buf = new byte[1 << 20];
var sb = new StringBuilder();
while (!cts.IsCancellationRequested)
{
    sb.Clear();
    WebSocketReceiveResult r;
    do { r = await ws.ReceiveAsync(buf, cts.Token); sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); }
    while (!r.EndOfMessage);

    using var resp = JsonDocument.Parse(sb.ToString());
    var root = resp.RootElement;
    if (!root.TryGetProperty("id", out var rid) || rid.GetInt32() != 1) continue; // skip unsolicited events
    if (root.TryGetProperty("result", out var o) && o.TryGetProperty("result", out var inner) && inner.TryGetProperty("value", out var val))
        Console.WriteLine(val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString());
    else
        Console.WriteLine(sb.ToString()); // surface errors/exceptionDetails raw
    return 0;
}
Console.Error.WriteLine("timed out waiting for response");
return 4;
