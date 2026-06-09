using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MSFSBlindAssist.SimConnect
{
    /// <summary>
    /// One-shot Coherent GT remote-debugger eval: resolve a cockpit view by title needle,
    /// evaluate a JS expression in it, and return the string result. For lightweight,
    /// on-demand reads — e.g. the A32NX D / Shift+D flight-info readout, where the full
    /// <see cref="CoherentDebuggerClient"/> MCDU bridge (A380-specific, persistent socket +
    /// scrape loop) is overkill. Connect → eval → close each call; no persistent socket,
    /// no injected agent, no lifecycle to manage. Returns "" on any failure (caller decides
    /// the spoken fallback). Same transport coherent-eval.ps1 uses.
    /// </summary>
    public static class CoherentEvalClient
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

        public static async Task<string> EvalAsync(string titleNeedle, string js, CancellationToken ct = default)
        {
            int pageId;
            try
            {
                string list = await Http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                pageId = ResolvePageId(list, titleNeedle);
            }
            catch { return ""; }
            if (pageId < 0) return "";

            using var ws = new ClientWebSocket();
            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(4000);
                    await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId}"), connectCts.Token);
                }

                string msg = JsonSerializer.Serialize(new
                {
                    id = 1,
                    method = "Runtime.evaluate",
                    @params = new { expression = js, returnByValue = true, awaitPromise = true }
                });
                await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(5000);
                var buf = new byte[131072];
                // Accumulate raw bytes and decode once at EndOfMessage — decoding each read
                // separately corrupts a multibyte UTF-8 char split across the read boundary
                // (same fix as the sibling Coherent clients).
                var ms = new System.IO.MemoryStream();
                // Skip CDP event notifications; return the frame whose id matches our request.
                for (int i = 0; i < 60; i++)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), readCts.Token);
                        if (res.MessageType == WebSocketMessageType.Close) return "";
                        ms.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);

                    try
                    {
                        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                        if (doc.RootElement.TryGetProperty("id", out var idEl)
                            && idEl.ValueKind == JsonValueKind.Number && idEl.GetInt32() == 1)
                            return ExtractValue(doc.RootElement);
                    }
                    catch { /* not a JSON message we care about */ }
                }
                return "";
            }
            catch { return ""; }
            finally
            {
                try { if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { }
            }
        }

        private static int ResolvePageId(string pagelistJson, string titleNeedle)
        {
            using var doc = JsonDocument.Parse(pagelistJson);
            foreach (var view in doc.RootElement.EnumerateArray())
            {
                if (!view.TryGetProperty("title", out var t)) continue;
                if ((t.GetString() ?? "").IndexOf(titleNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (view.TryGetProperty("id", out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                    if (int.TryParse(idEl.GetString(), out var n)) return n;
                }
            }
            return -1;
        }

        private static string ExtractValue(JsonElement root)
        {
            // {"id":1,"result":{"result":{"type":"string","value":"..."}}}
            if (root.TryGetProperty("result", out var outer)
                && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val))
                return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
            return "";
        }
    }
}
