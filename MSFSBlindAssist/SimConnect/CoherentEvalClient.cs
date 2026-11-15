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
    /// on-demand reads of a view no persistent client holds — e.g. the A32NX DCDU window, or
    /// the A32NX D / Shift+D flight-info readout before the MCDU window has ever been opened
    /// (after that the MCDU service owns the view and the readout rides its socket, unless its
    /// Coherent client could not load its agent and so owns nothing). Connect →
    /// eval → close each call; no persistent socket, no injected agent, no lifecycle to manage.
    /// Returns "" on any failure (caller decides the spoken fallback), and at once for a view a
    /// persistent client has claimed (<see cref="CoherentViewOwnership"/>): Coherent GT accepts
    /// one inspector socket per view. Same transport coherent-eval.ps1 uses.
    /// </summary>
    public static class CoherentEvalClient
    {
        private const string DebuggerBase = "http://127.0.0.1:19999";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

        public static Task<string> EvalAsync(string titleNeedle, string js, CancellationToken ct = default)
            => EvalAsync(titleNeedle, js, exactTitle: false, commandLineApi: false, ct);

        /// <param name="exactTitle">The view whose title IS the needle, never one that only holds it
        /// (the sim's "Toolbar" view, STAB-16).</param>
        /// <param name="commandLineApi">Evaluate with the debugger's command line API, whose
        /// <c>getEventListeners</c> finds the element that takes a click.</param>
        public static async Task<string> EvalAsync(string titleNeedle, string js, bool exactTitle, bool commandLineApi, CancellationToken ct = default)
        {
            // Never a second socket on a view a persistent client owns; while this one runs, the
            // owner-to-be waits for it instead of connecting on top of it.
            using var oneShot = CoherentViewOwnership.TryEnterOneShot(titleNeedle);
            if (oneShot == null) return "";

            int pageId;
            try
            {
                string list = await Http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
                pageId = ResolvePageId(list, titleNeedle, exactTitle);
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
                    @params = commandLineApi
                        ? (object)new { expression = js, returnByValue = true, awaitPromise = true, includeCommandLineAPI = true }
                        : new { expression = js, returnByValue = true, awaitPromise = true }
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
                // Bounded: a view that never answers the close frame must not hold the caller (and its
                // one-shot entry) for good; a cancelled close aborts the socket.
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        using var closeCts = new CancellationTokenSource(500);
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", closeCts.Token);
                    }
                }
                catch { }
            }
        }

        /// <summary>The first view whose title holds the needle (or is it, <paramref name="exact"/>), or -1.</summary>
        internal static int ResolvePageId(string pagelistJson, string titleNeedle, bool exact = false)
        {
            using var doc = JsonDocument.Parse(pagelistJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return -1;
            foreach (var view in doc.RootElement.EnumerateArray())
            {
                if (view.ValueKind != JsonValueKind.Object) continue;
                if (!view.TryGetProperty("title", out var t) || t.ValueKind != JsonValueKind.String) continue;
                string title = t.GetString() ?? "";
                if (exact ? !string.Equals(title, titleNeedle, StringComparison.OrdinalIgnoreCase)
                          : title.IndexOf(titleNeedle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (view.TryGetProperty("id", out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number && idEl.TryGetInt32(out int id)) return id;
                    if (idEl.ValueKind == JsonValueKind.String && int.TryParse(idEl.GetString(), out var n)) return n;
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
