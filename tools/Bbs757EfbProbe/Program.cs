// Live probe for the BlueBird 757 tablet, over the Coherent debugger (127.0.0.1:19999). The tablet is now read from
// the module's WASM memory, not OCR, so this keeps only the commands that drive BlueBird's EFB through its agent:
//
//   Bbs757EfbProbe tap x,y [x,y ...]     tap the points (tablet pixels, 1.2 s apart)
//   Bbs757EfbProbe drag <x> <y0> <y1>    drag the page from y0 to y1 at x (this build scrolls on a drag)
//   Bbs757EfbProbe vars NAME [NAME ...]  BlueBird L:vars read in the tablet's page
//   Bbs757EfbProbe mirror on|off         mirror the tablet onto the ND pop-out (diagnostic only)
//
// Everything is the app's own agent (coherent-bbs757-efb-agent.js), evaluated over the debugger. Read-only but for the
// taps/drags the pilot could make themselves.
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("commands: tap x,y [x,y ...] | drag x y0 y1 | vars NAME [NAME ...] | mirror on|off");
            return 1;
        }
        switch (args[0])
        {
            case "mirror":
                Console.WriteLine(await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.mirror({(args[1] == "on" ? "true" : "false")}))"));
                return 0;
            case "drag":    // drag <x> <y0> <y1>   (the agent's drag takes about 1.4 s)
                Console.WriteLine(await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.drag({int.Parse(args[1])},{int.Parse(args[2])},{int.Parse(args[3])}))"));
                return 0;
            case "vars":    // vars NAME [NAME ...]
                Console.WriteLine(await Agent("JSON.stringify(__MSFSBA_BBS757EFB.readVars(" + JsonSerializer.Serialize(args.Skip(1).ToArray()) + "))"));
                return 0;
            case "tap":     // tap x,y [x,y ...]   (1.2 s apart)
                foreach (var t in args.Skip(1))
                {
                    var xy = t.Split(',');
                    Console.WriteLine($"  tap {t}: {await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({int.Parse(xy[0])},{int.Parse(xy[1])}))")}");
                    await Task.Delay(1200);
                }
                return 0;
        }
        Console.WriteLine("unknown command");
        return 1;
    }

    // ---------------- Coherent debugger: install the app's agent, evaluate ----------------

    private static string? _agent;

    private static async Task<string> Agent(string expression)
    {
        _agent ??= File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "coherent-bbs757-efb-agent.js"));
        return await Eval(_agent + ";\n" + expression);
    }

    private static async Task<string> Eval(string expression)
    {
        using var http = new HttpClient();
        var pages = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:19999/pagelist.json")).RootElement;
        int id = -1;
        foreach (var p in pages.EnumerateArray())
            if ((p.GetProperty("title").GetString() ?? "").Contains("BBSNavigraphMap")) id = p.GetProperty("id").GetInt32();
        if (id < 0) return "no BBSNavigraphMap view";
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:19999/devtools/inspector/{id}"), CancellationToken.None);
        var msg = JsonSerializer.Serialize(new { id = 1, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } });
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);
        var buf = new byte[1 << 20];
        while (true)
        {
            var ms = new MemoryStream();
            WebSocketReceiveResult r;
            do { r = await ws.ReceiveAsync(buf, CancellationToken.None); ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
            var root = JsonDocument.Parse(ms.ToArray()).RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.GetInt32() == 1)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                var res = root.GetProperty("result").GetProperty("result");
                return res.TryGetProperty("value", out var v) ? v.ToString() : res.ToString();
            }
        }
    }
}
