using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The sim's own tablet panel, opened and closed from its toolbar as a sighted pilot does (STAB-16).
///
/// Coherent gives the tablet's view animation frames only while something shows it (STAB-15). An
/// aircraft with a tablet of its own shows the sim's nowhere, and the parts of an app that only move
/// with frames then never fill in: measured 2026-11-15 with the iniBuilds A350, the Pilot book's
/// logbook list stayed "loading" (its size comes from a ResizeObserver polyfill that waits for a
/// frame) until the sim's tablet panel was opened from the toolbar, when it listed all 135 flights.
/// The reader then offers <see cref="ActionIdx"/>, which the tablet window hands to MainForm.
///
/// The toolbar is its own Coherent view ("Toolbar"), which no other MSFSBA client opens, so a one-off
/// inspector socket to it never competes with another (DBG-9). The tablet's button there is the
/// element around the toolbar's "EFB" icon that listens to clicks; a click toggles the panel.
/// </summary>
public static class SimTabletPanel
{
    /// <summary>The idx of the reader's synthetic "Show the tablet in the sim" button (A.SHOW_TABLET_IDX).</summary>
    public const int ActionIdx = 990002;

    private const string DebuggerBase = "http://127.0.0.1:19999";
    private const string ToolbarTitle = "Toolbar";

    /// <summary>
    /// Clicks the toolbar's tablet button, the nearest ancestor of its "EFB" icon that has a click
    /// listener (getEventListeners needs the command line API). "clicked" when it was pressed.
    /// </summary>
    internal const string ToggleScript =
        "(function(){var g=document.getElementById('EFB');if(!g)return 'no-icon';" +
        "for(var n=g,i=0;n&&i<12;n=n.parentElement,i++){var l=getEventListeners(n);" +
        "if(l&&l.click&&l.click.length){n.click();return 'clicked';}}return 'no-button';})()";

    /// <summary>The "Toolbar" view's id from the debugger's page list, or null.</summary>
    internal static int? FindToolbarPage(string pageListJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(pageListJson);
            foreach (var view in doc.RootElement.EnumerateArray())
            {
                if (!view.TryGetProperty("title", out var t) || t.GetString() != ToolbarTitle) continue;
                if (!view.TryGetProperty("id", out var idEl)) continue;
                if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                if (int.TryParse(idEl.GetString(), out int n)) return n;
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// Toggles the sim's tablet panel. True when the toolbar's button was pressed; false when the
    /// debugger, the toolbar view or its tablet button could not be reached (logged).
    /// </summary>
    public static async Task<bool> ToggleAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        int? page;
        try { page = FindToolbarPage(await http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct)); }
        catch (Exception ex) { Log.Warn("SimTablet", $"toolbar: no page list: {ex.Message}"); return false; }
        if (page == null) { Log.Warn("SimTablet", "toolbar: no Toolbar view"); return false; }

        using var ws = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(4000);
        try
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{page.Value}"), cts.Token);
            var msg = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "Runtime.evaluate",
                @params = new { expression = ToggleScript, returnByValue = true, includeCommandLineAPI = true }
            });
            await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, cts.Token);
            var buf = new byte[16384];
            using var ms = new MemoryStream();
            for (int frame = 0; frame < 20; frame++)
            {
                ms.SetLength(0);
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close) return false;
                    ms.Write(buf, 0, res.Count);
                } while (!res.EndOfMessage);
                string value = ReplyValue(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
                if (value.Length == 0) continue;          // not our reply yet
                if (value != "clicked") Log.Warn("SimTablet", $"toolbar: tablet button not found ({value})");
                return value == "clicked";
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn("SimTablet", $"toolbar: {ex.Message}");
            return false;
        }
        finally
        {
            // A proper close, so Coherent releases the view at once (CoherentPmdgEfbClient.Stop).
            try { if (ws.State == WebSocketState.Open) await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(300)); } catch { }
        }
    }

    // The value of the reply with id 1, or "" for any other message.
    internal static string ReplyValue(string message)
    {
        try
        {
            using var d = JsonDocument.Parse(message);
            var root = d.RootElement;
            if (!root.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out int id) || id != 1) return "";
            if (root.TryGetProperty("result", out var outer) && outer.TryGetProperty("result", out var inner)
                && inner.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString() ?? "";
            return "error";
        }
        catch (JsonException) { return ""; }
    }
}
