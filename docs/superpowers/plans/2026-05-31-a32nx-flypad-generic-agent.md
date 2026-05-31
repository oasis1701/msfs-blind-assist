# A32NX flyPad Generic CDP Agent — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the A32NX flyPad's feature-specific HTTP bridge + curated-tab form with the A380-style generic in-page DOM scraper/clicker installed over CDP, a persistent request/response client, and a generic auto-polling accessible form.

**Architecture:** A generic in-page agent (`window.__MSFSBA_FLYPAD`, adapted from the A380 `coherent-flypad-agent.js`) is installed once over a single persistent Coherent-GT WebSocket. `CoherentEFBClient` owns that one connection and exposes `ScrapeAsync` / `ClickAsync` / `SetValueAsync`, correlating CDP responses by message id and reassembling multi-frame payloads. `A32NXEFBForm` renders the scraped element list as native accessible controls, ~1 s auto-poll + F5, with focus-preserving diff rendering. Connection lifecycle is tied to the form (connect on open, disconnect on close) because Coherent GT allows only one devtools connection at a time.

**Tech Stack:** .NET 9 / C# 13, Windows Forms, raw TCP + manual WebSocket framing (Coherent GT CDP on `127.0.0.1:19999`), `System.Text.Json`, ES5 Coherent-GT-safe JavaScript, Node.js (`efb-dom-tool.js`) for live verification.

**Spec:** `docs/superpowers/specs/2026-05-31-a32nx-flypad-generic-agent-design.md`

---

## Testing model (read first)

This repo has **no automated test project** and CLAUDE.md forbids speculative unit tests ("most code paths only execute against the real simulator"). So the TDD "write a failing test" step is replaced throughout by:

- **Build gate:** `dotnet build MSFSBlindAssist.sln -c Debug` must succeed with 0 errors.
- **Live CDP verification:** `node tools/efb-dom-tool.js <cmd>` against the running sim (FBW A32NX loaded, flyPad on, devtools `:19999` reachable).

**Single-connection caveat:** the verification tool and the app's `CoherentEFBClient` cannot both be connected at once. When using `efb-dom-tool.js`, the A32NX EFB form must be **closed** (so the client has released the socket) and any old `CoherentGTInjector` polling must be stopped (it is deleted in Task 8, but until then, run verification with MSFSBlindAssist not running). When testing in-app, stop the tool first.

---

## File Structure

**Create:**
- `MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js` — generic scraper/clicker agent (adapted from A380).
- `MSFSBlindAssist/SimConnect/FlypadModels.cs` — `FlypadElement`, `FlypadScrape` records + JSON parsing.
- `MSFSBlindAssist/SimConnect/CoherentEFBClient.cs` — persistent-WebSocket CDP client.

**Rewrite:**
- `MSFSBlindAssist/Forms/A32NX/A32NXEFBForm.cs` — generic live-flyPad renderer.

**Modify:**
- `MSFSBlindAssist/MainForm.cs` — drop `CoherentGTInjector` wiring; tie client lifecycle to the form.
- `MSFSBlindAssist/MSFSBlindAssist.csproj` — copy new agent JS; drop old bridge JS entry.
- `tools/efb-dom-tool.js` — retarget `state`/`inject` to `__MSFSBA_FLYPAD`.

**Delete:**
- `MSFSBlindAssist/Resources/a32nx-efb-accessibility-bridge.js`
- `MSFSBlindAssist/SimConnect/CoherentGTInjector.cs`

**Untouched (shared infra):** `SimConnect/EFBBridgeServer.cs` (still used by PMDG/HS787).

---

## Task 1: Create the generic agent JS (adapt from A380)

**Files:**
- Create: `MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` (around line 121)

- [ ] **Step 1: Copy the A380 agent verbatim as the starting point**

```bash
cp "D:/Documents/msfs BA 380/Resources/coherent-flypad-agent.js" \
   "D:/Claude/msfs-blind-assist/MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js"
```

- [ ] **Step 2: Rename the stamped data-attribute to an A32NX-specific name**

In `coherent-a32nx-flypad-agent.js`, replace **every** occurrence of `data-fbwa380-efb-idx` with `data-fbwa32nx-efb-idx` (appears in `isInsideStamped`, `enumerate` (set + clear), and `findByIdx`). Confirm none remain:

```bash
grep -c "fbwa380" "D:/Claude/msfs-blind-assist/MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js"
```
Expected: `0`

- [ ] **Step 3: Confirm the global, power-on L-var, and header are correct**

The agent must keep `window.__MSFSBA_FLYPAD = A;` (unchanged) and `powerOn()` must set `L:A32NX_EFB_TURNED_ON` (it already does — the A380 agent uses the same L-var name). Update the top comment block first line to read `// coherent-a32nx-flypad-agent.js` and the description to say "FlyByWire A32NX flyPad" instead of "A380X". No logic change.

- [ ] **Step 4: Register the agent JS for copy-to-output in the csproj**

In `MSFSBlindAssist/MSFSBlindAssist.csproj`, add immediately after the existing `Resources\a32nx-efb-accessibility-bridge.js` block (line ~121-123):

```xml
    <None Update="Resources\coherent-a32nx-flypad-agent.js">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
```

- [ ] **Step 5: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 errors (JS is a content file; this verifies the csproj edit is well-formed).

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js MSFSBlindAssist/MSFSBlindAssist.csproj
git commit -m "feat(fbw-efb): add generic flyPad CDP agent (adapted from A380)"
```

---

## Task 2: Live-verify and patch the agent against the A32NX DOM

This task is verification-driven; patches go into `coherent-a32nx-flypad-agent.js`. Sim must be running, FBW A32NX loaded, flyPad powered on. **MSFSBlindAssist must NOT be running** (single-connection rule).

- [ ] **Step 1: Install the agent live and confirm it loads**

```bash
node tools/efb-dom-tool.js eval "(function(){ /* paste nothing */ })()"   # sanity: tool reaches page
node tools/efb-dom-tool.js inject                                          # NOTE: Task 8 retargets this; until then, inject the OLD bridge — instead use the eval below
```

Preferred install for this task (independent of the tool's `inject` default path):

```bash
node tools/efb-dom-tool.js eval "$(cat MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js)"
node tools/efb-dom-tool.js eval "window.__MSFSBA_FLYPAD ? __MSFSBA_FLYPAD.ping() : 'NOT INSTALLED'"
```
Expected: `MSFSBA_FLYPAD_OK`

- [ ] **Step 2: Scrape the current page and inspect the element list**

```bash
node tools/efb-dom-tool.js eval "__MSFSBA_FLYPAD.scrape()"
```
Expected: a JSON string `{"ok":true,"page":"...","elements":[...]}`. Confirm `page` names the current flyPad page and `elements` carry sensible `text`/`kind`/`value`.

- [ ] **Step 3: Walk the key pages and record divergences**

For each page (navigate the flyPad in-sim, or click nav links via `__MSFSBA_FLYPAD.clickElement(idx)`), scrape and check classification: **Dashboard** (two-column read order), **Ground → Services / Pushback** (service buttons + state), **Ground → Fuel / Payload** (PAX/cargo/fuel numeric inputs labelled), **Settings → 3rd Party** (SimBrief ID input, Navigraph link/unlink), **Checklists** (checkitem rows toggle + show check icon). Note any control that is mislabeled, missing, or wrong `kind`/`value`.

- [ ] **Step 4: Patch the three known A32NX divergence risks if verification shows them**

Edit `coherent-a32nx-flypad-agent.js` only where Step 3 found a real problem:
- **Toggle pill shape:** the A380 `classify` matches `rounded-full` + `cursor-pointer` + `w-14`. If the A32NX Toggle uses a different width token, broaden the `w-14` check (e.g. also accept `w-12`) in both `classify` and the `isToggle` test in `labelFor`.
- **Nav-rail width threshold:** the `navRail` flag and the Pass-2 nav-rail skip use `leftRel < 100` / `tLeft < 100`. If the A32NX rail sits at a different x, adjust the literal to match the observed rail column width.
- **Segmented setting controls:** if a segmented option (e.g. ADIRS Align Time Instant/Fast/Real) is missed, confirm `isSelectedSegment`'s `bg-theme-highlight` / `bg-theme-accent` token pair still applies; adjust tokens to the observed class names.

Re-run Step 2/3 after each patch until labels/kinds/values are correct on all key pages.

- [ ] **Step 5: Verify click and setValue round-trips live**

```bash
# Click a nav link (use an idx of a nav-rail link from the scrape), then re-scrape and confirm page changed:
node tools/efb-dom-tool.js eval "__MSFSBA_FLYPAD.clickElement(IDX)"
node tools/efb-dom-tool.js eval "JSON.parse(__MSFSBA_FLYPAD.scrape()).page"
# Set a numeric input (PAX), then re-scrape and confirm the value committed:
node tools/efb-dom-tool.js eval "__MSFSBA_FLYPAD.setValue(IDX, '150')"
node tools/efb-dom-tool.js eval "JSON.parse(__MSFSBA_FLYPAD.scrape()).elements.filter(function(e){return e.idx===IDX})[0].value"
```
Expected: page label changes after the click; the input's `value` reflects the set text.

- [ ] **Step 6: Commit any patches**

```bash
git add MSFSBlindAssist/Resources/coherent-a32nx-flypad-agent.js
git commit -m "fix(fbw-efb): patch generic agent classifiers for live A32NX flyPad DOM"
```
(If Step 4 produced no patches, skip the commit and note "agent verified unchanged" in the task.)

---

## Task 3: Create the scrape data model

**Files:**
- Create: `MSFSBlindAssist/SimConnect/FlypadModels.cs`

- [ ] **Step 1: Write the model records**

```csharp
using System.Text.Json.Serialization;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// One control / text line scraped from the flyPad by the generic agent.
/// Mirrors the JSON object the agent's scrape() emits per element.
/// </summary>
public sealed class FlypadElement
{
    [JsonPropertyName("idx")]         public int Idx { get; set; }
    [JsonPropertyName("kind")]        public string Kind { get; set; } = "";
    [JsonPropertyName("tag")]         public string Tag { get; set; } = "";
    [JsonPropertyName("role")]        public string Role { get; set; } = "";
    [JsonPropertyName("text")]        public string Text { get; set; } = "";
    [JsonPropertyName("value")]       public string Value { get; set; } = "";
    [JsonPropertyName("controlType")] public string ControlType { get; set; } = "";
    [JsonPropertyName("clickable")]   public bool Clickable { get; set; }
    [JsonPropertyName("level")]       public int Level { get; set; }
    [JsonPropertyName("live")]        public string Live { get; set; } = "";
    [JsonPropertyName("disabled")]    public bool Disabled { get; set; }
    [JsonPropertyName("options")]     public List<string> Options { get; set; } = new();
}

/// <summary>Result of one scrape() call.</summary>
public sealed class FlypadScrape
{
    [JsonPropertyName("ok")]       public bool Ok { get; set; }
    [JsonPropertyName("error")]    public string? Error { get; set; }
    [JsonPropertyName("page")]     public string Page { get; set; } = "";
    [JsonPropertyName("elements")] public List<FlypadElement> Elements { get; set; } = new();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/FlypadModels.cs
git commit -m "feat(fbw-efb): add flyPad scrape data model"
```

---

## Task 4: Create the persistent CDP client

**Files:**
- Create: `MSFSBlindAssist/SimConnect/CoherentEFBClient.cs`

The handshake + frame-build helpers are lifted from `CoherentGTInjector` (proven). The new pieces are: a persistent socket, a receive pump, id→TCS correlation, and **multi-frame/partial reassembly** of server→client frames.

- [ ] **Step 1: Write the client**

```csharp
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Owns the single Coherent GT devtools (CDP) connection to the FBW A32NX EFB
/// page. Installs the generic agent (window.__MSFSBA_FLYPAD) once per connection,
/// then drives it via Runtime.evaluate request/response correlated by message id.
///
/// Coherent GT accepts only ONE devtools connection at a time, so this client is
/// the sole CDP consumer for the EFB page; its lifecycle is tied to the EFB form.
/// .NET's ClientWebSocket rejects Coherent GT's non-standard upgrade response, so
/// the WebSocket handshake and framing are done manually.
/// </summary>
public sealed class CoherentEFBClient : IDisposable
{
    private const string PageListUrl = "http://127.0.0.1:19999/pagelist.json";
    private const int Port = 19999;
    private const int HttpTimeoutMs = 2000;
    private const int WsConnectTimeoutMs = 3000;
    private const int EvalTimeoutMs = 4000;
    private const int HeartbeatMs = 4000;
    private const int ReconnectDelayMs = 1500;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMilliseconds(HttpTimeoutMs) };

    private readonly string _agentJsPath;
    private readonly SynchronizationContext? _sync;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

    private CancellationTokenSource? _cts;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _nextId;
    private volatile bool _ready;
    private bool _disposed;
    private readonly object _lifecycleLock = new();

    public bool IsReady => _ready;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public CoherentEFBClient(string agentJsPath)
    {
        _agentJsPath = agentJsPath;
        _sync = SynchronizationContext.Current;
    }

    // ── lifecycle ───────────────────────────────────────────────────────────

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        TearDown();
        SetReady(false);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_ready)
                {
                    if (await ConnectAndInstallAsync(ct)) SetReady(true);
                    else { await Delay(ReconnectDelayMs, ct); continue; }
                }

                // Heartbeat: ping; if the agent global is gone or the socket died, reconnect.
                await Delay(HeartbeatMs, ct);
                string pong = await PingAsync(ct);
                if (pong != "MSFSBA_FLYPAD_OK")
                {
                    SetReady(false);
                    TearDown();
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                SetReady(false);
                TearDown();
                await Delay(ReconnectDelayMs, ct);
            }
        }
    }

    private async Task<bool> ConnectAndInstallAsync(CancellationToken ct)
    {
        string? pageId = await FindEfbPageIdAsync(ct);
        if (pageId == null) return false;
        if (!File.Exists(_agentJsPath)) return false;

        if (!await OpenSocketAsync(pageId, ct)) return false;

        // Start the receive pump before sending anything.
        _ = Task.Run(() => ReceivePumpAsync(_stream!, ct));

        string agentJs = await File.ReadAllTextAsync(_agentJsPath, ct);
        await EvalRawAsync(agentJs, returnByValue: false, ct);          // install
        string pong = await PingAsync(ct);
        if (pong != "MSFSBA_FLYPAD_OK") return false;
        await PowerOnAsync(ct);
        return true;
    }

    private async Task<bool> OpenSocketAsync(string pageId, CancellationToken ct)
    {
        try
        {
            _tcp = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(WsConnectTimeoutMs);
            await _tcp.ConnectAsync("127.0.0.1", Port, connectCts.Token);
            _stream = _tcp.GetStream();

            string wsKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string handshake =
                $"GET /devtools/page/{pageId} HTTP/1.1\r\n" +
                "Host: 127.0.0.1:19999\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {wsKey}\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";
            await _stream.WriteAsync(Encoding.ASCII.GetBytes(handshake), ct);

            // Read until end-of-headers; confirm 101.
            var buf = new byte[4096];
            int total = 0;
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (_stream.DataAvailable)
                {
                    int n = await _stream.ReadAsync(buf.AsMemory(total), ct);
                    if (n == 0) break;
                    total += n;
                    if (Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n")) break;
                }
                else await Task.Delay(20, ct);
            }
            return Encoding.ASCII.GetString(buf, 0, total).Contains("101");
        }
        catch { return false; }
    }

    private void TearDown()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null;
        _tcp = null;
        foreach (var kv in _pending)
            kv.Value.TrySetException(new IOException("connection torn down"));
        _pending.Clear();
    }

    // ── receive pump: reassemble server→client (unmasked) frames ─────────────

    private async Task ReceivePumpAsync(NetworkStream stream, CancellationToken ct)
    {
        var acc = new List<byte>(8192);
        var tmp = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(tmp, ct);
                if (n == 0) break;
                acc.AddRange(tmp.AsSpan(0, n).ToArray());
                DrainFrames(acc);
            }
        }
        catch { /* socket closed; RunAsync heartbeat triggers reconnect */ }
    }

    /// <summary>
    /// Extracts every complete WebSocket frame currently buffered. Server→client
    /// frames are unmasked. Handles 7-bit, 16-bit (126) and 64-bit (127) lengths
    /// and leaves partial frames in the buffer for the next read.
    /// </summary>
    private void DrainFrames(List<byte> acc)
    {
        while (true)
        {
            if (acc.Count < 2) return;
            int b1 = acc[1];
            bool masked = (b1 & 0x80) != 0;        // server frames are not masked
            long len = b1 & 0x7F;
            int headerLen = 2;
            if (len == 126)
            {
                if (acc.Count < 4) return;
                len = (acc[2] << 8) | acc[3];
                headerLen = 4;
            }
            else if (len == 127)
            {
                if (acc.Count < 10) return;
                len = 0;
                for (int i = 0; i < 8; i++) len = (len << 8) | acc[2 + i];
                headerLen = 10;
            }
            int maskLen = masked ? 4 : 0;
            long frameLen = headerLen + maskLen + len;
            if (acc.Count < frameLen) return;       // incomplete; wait for more

            int opcode = acc[0] & 0x0F;
            int payloadStart = headerLen + maskLen;
            var payload = new byte[len];
            for (long i = 0; i < len; i++)
            {
                byte v = acc[(int)(payloadStart + i)];
                if (masked) v = (byte)(v ^ acc[headerLen + (int)(i % 4)]);
                payload[i] = v;
            }
            acc.RemoveRange(0, (int)frameLen);

            if (opcode == 0x1 || opcode == 0x2) // text / binary
                DispatchResponse(Encoding.UTF8.GetString(payload));
            // opcode 0x8 close / 0x9 ping / 0xA pong: ignore (heartbeat handles liveness)
        }
    }

    private void DispatchResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var idEl)) return; // CDP event, not a response
            int id = idEl.GetInt32();
            if (_pending.TryRemove(id, out var tcs))
                tcs.TrySetResult(root.Clone());
        }
        catch { /* ignore malformed frame */ }
    }

    // ── request/response ──────────────────────────────────────────────────────

    /// <summary>Runs an expression and returns the CDP response root element.</summary>
    private async Task<JsonElement> EvalRawAsync(string expression, bool returnByValue, CancellationToken ct)
    {
        var stream = _stream ?? throw new IOException("not connected");
        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        string cdp = JsonSerializer.Serialize(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new { expression, returnByValue, awaitPromise = false }
        });
        await stream.WriteAsync(BuildWebSocketTextFrame(Encoding.UTF8.GetBytes(cdp)), ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(EvalTimeoutMs);
        await using var reg = timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var t)) t.TrySetException(new TimeoutException("CDP eval timed out"));
        });
        return await tcs.Task;
    }

    /// <summary>Returns result.result.value as a string (for agent calls that return strings).</summary>
    private async Task<string> EvalStringAsync(string expression, CancellationToken ct)
    {
        var root = await EvalRawAsync(expression, returnByValue: true, ct);
        if (root.TryGetProperty("result", out var r1) &&
            r1.TryGetProperty("result", out var r2) &&
            r2.TryGetProperty("value", out var val) &&
            val.ValueKind == JsonValueKind.String)
            return val.GetString() ?? "";
        return "";
    }

    // ── public agent API ──────────────────────────────────────────────────────

    private const string AgentGuard = "window.__MSFSBA_FLYPAD ? ";

    public async Task<string> PingAsync(CancellationToken ct = default)
    {
        try { return await EvalStringAsync(AgentGuard + "__MSFSBA_FLYPAD.ping() : 'NO_AGENT'", ct); }
        catch { return ""; }
    }

    public async Task PowerOnAsync(CancellationToken ct = default)
    {
        try { await EvalStringAsync(AgentGuard + "__MSFSBA_FLYPAD.powerOn() : 'NO_AGENT'", ct); }
        catch { /* best effort */ }
    }

    public async Task<FlypadScrape> ScrapeAsync(CancellationToken ct = default)
    {
        try
        {
            string json = await EvalStringAsync(
                AgentGuard + "__MSFSBA_FLYPAD.scrape() : '{\"ok\":false,\"error\":\"agent not installed\"}'", ct);
            if (string.IsNullOrEmpty(json))
                return new FlypadScrape { Ok = false, Error = "no response" };
            var scrape = JsonSerializer.Deserialize<FlypadScrape>(json);
            return scrape ?? new FlypadScrape { Ok = false, Error = "parse failed" };
        }
        catch (Exception ex) { return new FlypadScrape { Ok = false, Error = ex.Message }; }
    }

    public async Task<bool> ClickAsync(int idx, CancellationToken ct = default)
    {
        try
        {
            string r = await EvalStringAsync(
                AgentGuard + "__MSFSBA_FLYPAD.clickElement(" + idx + ") : 'NO_AGENT'", ct);
            return r == "ok";
        }
        catch { return false; }
    }

    public async Task<bool> SetValueAsync(int idx, string text, CancellationToken ct = default)
    {
        string js = JsonSerializer.Serialize(text); // safely-quoted JS string literal
        try
        {
            string r = await EvalStringAsync(
                AgentGuard + "__MSFSBA_FLYPAD.setValue(" + idx + ", " + js + ") : 'NO_AGENT'", ct);
            return r == "ok";
        }
        catch { return false; }
    }

    // ── helpers (lifted from CoherentGTInjector) ───────────────────────────────

    private static async Task<string?> FindEfbPageIdAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(PageListUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;
            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var page in doc.RootElement.EnumerateArray())
            {
                string? title = page.TryGetProperty("title", out var t) ? t.GetString() : null;
                string? id = null;
                if (page.TryGetProperty("id", out var idProp))
                    id = idProp.ValueKind == JsonValueKind.Number ? idProp.GetRawText() : idProp.GetString();
                if (title == null || id == null) continue;
                if (title.EndsWith("- EFB", StringComparison.OrdinalIgnoreCase)) return id;
            }
        }
        catch { }
        return null;
    }

    private static byte[] BuildWebSocketTextFrame(byte[] payload)
    {
        int len = payload.Length;
        byte[] mask = new byte[4];
        RandomNumberGenerator.Fill(mask);
        var header = new List<byte> { 0x81 };
        if (len <= 125) header.Add((byte)(0x80 | len));
        else if (len <= 65535) { header.Add(0xFE); header.Add((byte)(len >> 8)); header.Add((byte)(len & 0xFF)); }
        else { header.Add(0xFF); for (int i = 7; i >= 0; i--) header.Add((byte)((len >> (8 * i)) & 0xFF)); }
        header.AddRange(mask);
        var masked = new byte[len];
        for (int i = 0; i < len; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);
        var frame = new byte[header.Count + len];
        header.ToArray().CopyTo(frame, 0);
        masked.CopyTo(frame, header.Count);
        return frame;
    }

    private static async Task Delay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
    }

    private void SetReady(bool value)
    {
        if (_ready == value) return;
        _ready = value;
        var evt = value ? Connected : Disconnected;
        if (_sync != null) _sync.Post(_ => evt?.Invoke(this, EventArgs.Empty), null);
        else evt?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/SimConnect/CoherentEFBClient.cs
git commit -m "feat(fbw-efb): add persistent CDP client (scrape/click/setValue over one socket)"
```

---

## Task 5: Rewrite the EFB form as a generic renderer

**Files:**
- Rewrite (replace entire file): `MSFSBlindAssist/Forms/A32NX/A32NXEFBForm.cs`

New constructor signature: `A32NXEFBForm(ScreenReaderAnnouncer announcer)` (no `EFBBridgeServer`). The form owns the `CoherentEFBClient` and ties its lifecycle to show/hide.

- [ ] **Step 1: Replace the file with the generic renderer**

```csharp
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Forms.A32NX;

/// <summary>
/// Accessible, generic live view of the FlyByWire A320 flyPad (EFB). Renders
/// whatever page is open as a flat list of native accessible controls, scraped
/// from the live DOM via the generic CDP agent. No per-feature knowledge.
/// </summary>
public sealed class A32NXEFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly CoherentEFBClient _client;

    private Label _statusLabel = null!;
    private Button _selfTestBtn = null!;
    private FlowLayoutPanel _list = null!;
    private System.Windows.Forms.Timer _pollTimer = null!;

    private IntPtr _previousWindow = IntPtr.Zero;
    private string _renderSignature = "";
    private string _lastPage = "";
    private bool _polling;
    private DateTime _lastScrapeUtc = DateTime.MinValue;
    private bool _lastScrapeOk;
    private string _lastScrapeError = "";

    public A32NXEFBForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        string agentPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "coherent-a32nx-flypad-agent.js");
        _client = new CoherentEFBClient(agentPath);
        _client.Connected    += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateStatus); };
        _client.Disconnected += (_, _) => { if (IsHandleCreated) BeginInvoke(UpdateStatus); };

        Text = "FlyByWire A320 flyPad";
        AccessibleName = "FlyByWire A320 flyPad";
        Size = new Size(700, 640);
        MinimumSize = new Size(560, 480);
        KeyPreview = true;

        BuildLayout();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pollTimer.Tick += async (_, _) => await PollOnce();

        KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { e.Handled = true; await PollOnce(force: true); }
        };

        FormClosing += (_, e) =>
        {
            // Hide instead of dispose so reopening is instant; release the single
            // CDP connection so the verification tool / other consumers can use it.
            e.Cancel = true;
            _pollTimer.Stop();
            _client.Stop();
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;

        _client.Start();          // connect + install agent + powerOn
        _pollTimer.Start();
        UpdateStatus();
        _selfTestBtn.Focus();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        _statusLabel = new Label
        {
            Dock = DockStyle.Top, Height = 24, Text = "Connecting…",
            AccessibleName = "Connection status", TabIndex = 0
        };

        _selfTestBtn = new Button
        {
            Dock = DockStyle.Top, Height = 28, Text = "Status / Self-test",
            AccessibleName = "Status and self-test", TabIndex = 1
        };
        _selfTestBtn.Click += async (_, _) => await SelfTest();

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(8), TabIndex = 2,
            AccessibleName = "flyPad controls"
        };

        Controls.Add(_list);
        Controls.Add(_selfTestBtn);
        Controls.Add(_statusLabel);
        ResumeLayout();
    }

    // ── polling ────────────────────────────────────────────────────────────────

    private async Task PollOnce(bool force = false)
    {
        if (_polling) return;
        _polling = true;
        try
        {
            if (!_client.IsReady) { UpdateStatus(); return; }
            var scrape = await _client.ScrapeAsync();
            _lastScrapeUtc = DateTime.UtcNow;
            _lastScrapeOk = scrape.Ok;
            _lastScrapeError = scrape.Error ?? "";
            if (!scrape.Ok)
            {
                // flyPad probably powered off — nudge it and keep last good render.
                await _client.PowerOnAsync();
                UpdateStatus();
                return;
            }
            string sig = BuildSignature(scrape);
            if (force || sig != _renderSignature)
            {
                Render(scrape);
                _renderSignature = sig;
            }
            if (scrape.Page != _lastPage)
            {
                _lastPage = scrape.Page;
                _announcer?.Announce(scrape.Page);
            }
            UpdateStatus();
        }
        finally { _polling = false; }
    }

    private static string BuildSignature(FlypadScrape s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(s.Page).Append('|');
        foreach (var e in s.Elements)
            sb.Append(e.Idx).Append(':').Append(e.Kind).Append(':')
              .Append(e.Text).Append('=').Append(e.Value)
              .Append(e.Disabled ? "#d" : "").Append(';');
        return sb.ToString();
    }

    // ── rendering ────────────────────────────────────────────────────────────

    private void Render(FlypadScrape scrape)
    {
        // Remember which scraped element had focus so we can restore it post-rebuild.
        int focusedIdx = (_list.GetContainerControl() as ContainerControl)?.ActiveControl is Control c
                         && c.Tag is int ti ? ti : -1;

        _list.SuspendLayout();
        _list.Controls.Clear();
        Control? toFocus = null;

        foreach (var e in scrape.Elements)
        {
            Control? ctrl = BuildControl(e);
            if (ctrl == null) continue;
            ctrl.Tag = e.Idx;
            ctrl.Width = _list.ClientSize.Width - 28;
            _list.Controls.Add(ctrl);
            if (e.Idx == focusedIdx) toFocus = ctrl;
        }

        _list.ResumeLayout();
        toFocus?.Focus();
    }

    private Control? BuildControl(FlypadElement e)
    {
        // Headings → label with Heading role (NVDA H-key nav).
        if (e.Kind == "heading")
            return new Label { Text = e.Text, AutoSize = true, AccessibleRole = AccessibleRole.StaticText,
                               Font = new Font(Font, FontStyle.Bold), AccessibleName = e.Text };

        // Toggles / checkboxes / checklist items → CheckBox.
        if (e.ControlType == "checkbox")
        {
            var cb = new CheckBox
            {
                Text = string.IsNullOrEmpty(e.Text) ? "(toggle)" : e.Text,
                Checked = e.Value == "true", AutoSize = true,
                Enabled = !e.Disabled, AccessibleName = e.Text
            };
            cb.Click += async (_, _) => { if (cb.Tag is int idx) await _client.SetValueAsync(idx, cb.Checked ? "true" : "false"); ScheduleQuickRescrape(); };
            return cb;
        }

        // Selects → ComboBox.
        if (e.ControlType == "select")
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Enabled = !e.Disabled,
                                       AccessibleName = e.Text };
            combo.Items.AddRange(e.Options.Cast<object>().ToArray());
            combo.SelectedItem = e.Value;
            combo.SelectionChangeCommitted += async (_, _) =>
            { if (combo.Tag is int idx && combo.SelectedItem != null) await _client.SetValueAsync(idx, combo.SelectedItem.ToString()!); ScheduleQuickRescrape(); };
            return combo;
        }

        // Text/numeric inputs → labeled TextBox (commit on Enter / leave).
        if (e.ControlType == "text")
        {
            var panel = new Panel { Height = 26, AccessibleName = e.Text };
            var box = new TextBox { Text = e.Value, Width = 160, Dock = DockStyle.Right,
                                    Enabled = !e.Disabled, AccessibleName = e.Text };
            async void Commit() { if (box.Tag is int idx) await _client.SetValueAsync(idx, box.Text); ScheduleQuickRescrape(); }
            box.Leave += (_, _) => Commit();
            box.KeyDown += (_, ke) => { if (ke.KeyCode == Keys.Enter) { ke.Handled = true; Commit(); } };
            panel.Controls.Add(box);
            panel.Controls.Add(new Label { Text = e.Text, Dock = DockStyle.Fill, AccessibleName = e.Text });
            // Tag carries idx for both panel (focus restore) and box (commit).
            box.Tag = e.Idx;
            return panel;
        }

        // Clickable links/buttons/tabs → Button.
        if (e.Clickable)
        {
            var btn = new Button { Text = string.IsNullOrEmpty(e.Text) ? "(button)" : e.Text,
                                   AutoSize = true, Enabled = !e.Disabled, AccessibleName = e.Text };
            btn.Click += async (_, _) => { if (btn.Tag is int idx) await _client.ClickAsync(idx); ScheduleQuickRescrape(); };
            return btn;
        }

        // Plain descriptive text.
        if (!string.IsNullOrEmpty(e.Text))
            return new Label { Text = e.Text, AutoSize = true, AccessibleName = e.Text };

        return null;
    }

    private void ScheduleQuickRescrape()
    {
        var t = new System.Windows.Forms.Timer { Interval = 300 };
        t.Tick += async (_, _) => { t.Stop(); t.Dispose(); await PollOnce(force: true); };
        t.Start();
    }

    // ── status / diagnostics ───────────────────────────────────────────────────

    private void UpdateStatus()
    {
        if (!_client.IsReady) { _statusLabel.Text = "flyPad not detected — connecting…"; return; }
        if (!_lastScrapeOk && _lastScrapeUtc != DateTime.MinValue)
        { _statusLabel.Text = $"flyPad off — {_lastScrapeError}"; return; }
        int n = _list.Controls.Count;
        string age = _lastScrapeUtc == DateTime.MinValue ? "—" : $"{(int)(DateTime.UtcNow - _lastScrapeUtc).TotalSeconds}s ago";
        _statusLabel.Text = $"Live: \"{_lastPage}\", {n} controls (updated {age})";
    }

    private async Task SelfTest()
    {
        string pong = await _client.PingAsync();
        var scrape = await _client.ScrapeAsync();
        string msg = _client.IsReady
            ? (pong == "MSFSBA_FLYPAD_OK"
                ? (scrape.Ok
                    ? $"Connected. Agent installed. Page \"{scrape.Page}\", {scrape.Elements.Count} controls."
                    : $"Connected, agent installed, but scrape failed: {scrape.Error}")
                : "Connected, but agent not responding — reinstalling.")
            : "Not connected to the flyPad. Is the FBW A320 loaded and the tablet powered on?";
        _announcer?.Announce(msg);
        UpdateStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pollTimer?.Dispose(); _client?.Dispose(); }
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 2: Build (expect errors from MainForm — the old ctor call is now wrong)**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: errors in `MainForm.cs` at the `new A32NXEFBForm(efbBridgeServer, announcer)` call (wrong args). These are fixed in Task 6. The form file itself should have no errors. (If the form file has its own errors, fix them before proceeding.)

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/A32NX/A32NXEFBForm.cs
git commit -m "feat(fbw-efb): rewrite EFB form as generic live flyPad renderer"
```

---

## Task 6: Rewire MainForm to the new client/form lifecycle

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs` (lines ~44, ~2245-2259, ~2625-2641, ~3919-3934, ~5653-5656)

- [ ] **Step 1: Remove the `coherentGTInjector` field**

Delete line 44:
```csharp
    private CoherentGTInjector? coherentGTInjector;
```
(Keep `private A32NXEFBForm? a32nxEFBForm;` on line 43.)

- [ ] **Step 2: Update `ShowA32NXEFBDialog` — drop the bridge-server dependency and use the new ctor**

Replace the body (lines 2245-2259):
```csharp
    private void ShowA32NXEFBDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        if (a32nxEFBForm == null || a32nxEFBForm.IsDisposed)
            a32nxEFBForm = new A32NXEFBForm(announcer);

        a32nxEFBForm.ShowForm();
    }
```

- [ ] **Step 3: Delete `StartCoherentGTInjector` / `StopCoherentGTInjector`**

Remove both methods (lines 2625-2641). `CleanupA32NXEFBForm` (2261-2268) stays — it is still the right teardown (the form's own `FormClosing` already stops the client; `Dispose` releases it fully).

- [ ] **Step 4: Update the aircraft-switch branch**

Replace lines 3919-3934:
```csharp
        // EFB bridge: PMDG (mod package) or FBW A320 (CDP, owned by the EFB form)
        if (newAircraft is IPMDGAircraft pmdgChange && pmdgChange.HasEFBSupport)
        {
            CheckAndOfferEFBModPackage();
            StartEFBBridgeServer();
            CleanupA32NXEFBForm();   // release the FBW CDP connection if it was open
        }
        else if (newAircraft.AircraftCode == "A320")
        {
            // FBW flyPad: the EFB form owns its CDP client; nothing to pre-start here.
        }
        else
        {
            StopEFBBridgeServer();
            CleanupA32NXEFBForm();
        }
```

- [ ] **Step 5: Update the dispose block**

Replace lines 5653-5656:
```csharp
        // Clean up A32NX EFB form (owns its CDP client)
        CleanupA32NXEFBForm();
```

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 errors. (If `CoherentGTInjector` is referenced anywhere else, the compiler names the line — it should only have been referenced in the spots above; the type itself is deleted in Task 7.)

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs
git commit -m "refactor(fbw-efb): tie CDP connection lifecycle to the EFB form; drop injector wiring"
```

---

## Task 7: Delete the obsolete bridge JS and injector

**Files:**
- Delete: `MSFSBlindAssist/Resources/a32nx-efb-accessibility-bridge.js`
- Delete: `MSFSBlindAssist/SimConnect/CoherentGTInjector.cs`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` (remove old bridge JS `<None Update>` entry, lines ~121-123)

- [ ] **Step 1: Delete the two files and remove the csproj entry**

```bash
git rm MSFSBlindAssist/Resources/a32nx-efb-accessibility-bridge.js
git rm MSFSBlindAssist/SimConnect/CoherentGTInjector.cs
```
Then in `MSFSBlindAssist.csproj` delete the block:
```xml
    <None Update="Resources\a32nx-efb-accessibility-bridge.js">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
```

- [ ] **Step 2: Confirm no remaining references**

```bash
grep -rn "CoherentGTInjector\|a32nx-efb-accessibility-bridge" MSFSBlindAssist
```
Expected: no matches.

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(fbw-efb): remove obsolete A32NX bridge JS and CoherentGTInjector"
```

---

## Task 8: Retarget the verification tool to the new agent

**Files:**
- Modify: `tools/efb-dom-tool.js` (`cmdState` ~171-180, `BRIDGE_JS_PATH` ~24, `cmdInject` ~226-239)

- [ ] **Step 1: Point the tool at the new agent file and globals**

In `tools/efb-dom-tool.js`:
- Change `BRIDGE_JS_PATH` (line 24) to:
```js
const BRIDGE_JS_PATH = path.join(__dirname, '..', 'MSFSBlindAssist', 'Resources', 'coherent-a32nx-flypad-agent.js');
```
- Replace `cmdState`'s expression (lines 172-175) with an agent-aware check:
```js
    const expr = 'JSON.stringify({ installed: !!window.__MSFSBA_FLYPAD, ' +
        'ping: window.__MSFSBA_FLYPAD ? window.__MSFSBA_FLYPAD.ping() : null, ' +
        'page: window.__MSFSBA_FLYPAD ? JSON.parse(window.__MSFSBA_FLYPAD.scrape()).page : null })';
```
- `cmdInject` already evals the file at `BRIDGE_JS_PATH`; no change needed beyond the path. Add a `scrape` convenience command in the help text and switch (optional but useful):
```js
        case 'scrape':  await cmdEval(pageId, 'window.__MSFSBA_FLYPAD ? __MSFSBA_FLYPAD.scrape() : "NO_AGENT"'); break;
```

- [ ] **Step 2: Verify the tool runs (sim up, app closed)**

```bash
node tools/efb-dom-tool.js inject
node tools/efb-dom-tool.js state
```
Expected: `state` reports `installed: true`, `ping: "MSFSBA_FLYPAD_OK"`, and a non-null `page`.

- [ ] **Step 3: Commit**

```bash
git add tools/efb-dom-tool.js
git commit -m "chore(tools): retarget efb-dom-tool to the generic __MSFSBA_FLYPAD agent"
```

---

## Task 9: Full in-app live verification

Sim up, FBW A32NX loaded, flyPad on. **Close `efb-dom-tool.js` usage** (single connection). NVDA running.

- [ ] **Step 1: Build and run**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```
Launch the built `MSFSBlindAssist.exe` from `MSFSBlindAssist/bin/x64/Debug/net9.0-windows/win-x64/`, connect to the sim, select the FBW A320.

- [ ] **Step 2: Open the form and verify the live render**

Press **Shift+T** (input mode) to open the flyPad form. Confirm:
- Status line transitions to `Live: "<page>", N controls`.
- NVDA reads the controls in reading order; headings are reachable with the H key.
- Navigating the flyPad in-sim (or activating a nav-rail Button in the form) updates the list within ~1 s **without** focus thrashing; F5 forces an immediate refresh.

- [ ] **Step 3: Verify interaction round-trips**

- Activate a nav Button → the page label announces and the list changes.
- Toggle a CheckBox (e.g. a Settings switch) → it flips and stays flipped after the next poll.
- Set a numeric TextBox (e.g. PAX) and press Enter → value commits and is reflected.

- [ ] **Step 4: Verify diagnostics and resilience**

- Press the **Status / Self-test** button → it announces connection state, agent presence, page, and control count.
- Reload the EFB page in the sim (switch instrument view away and back, or reload) → the client reinstalls within a few seconds and the form recovers.
- Close the form, then run `node tools/efb-dom-tool.js state` → confirm the tool can now connect (proving the form released the single connection on close).

- [ ] **Step 5: Update the project memory file**

Update `C:\Users\augus\.claude\projects\D--Claude-msfs-blind-assist\memory\project_fbw_efb_bridge.md` to record that the A32NX flyPad now uses the generic CDP agent (`__MSFSBA_FLYPAD`) + `CoherentEFBClient` + generic form, replacing the HTTP bridge; note any agent classifier patches made in Task 2.

- [ ] **Step 6: Final commit (if memory/doc tweaks were made in-repo) and PR**

```bash
git add -A
git commit -m "docs(fbw-efb): record generic flyPad agent verification results"
```
Open a PR to `main` with the in-sim test plan from Steps 2-4 as the verification section.

---

## Self-Review

**Spec coverage:**
- Generic agent installed via CDP → Task 1-2. ✓
- Persistent request/response client, multi-frame reassembly, reconnect/reinstall, single connection → Task 4. ✓
- Generic auto-poll (1 s) + F5 + delayed post-action rescrape + focus-preserving diff render → Task 5. ✓
- Diagnostics replacing wake button (status line + self-test) → Task 5. ✓
- powerOn on connect + on scrape-fail → Task 4 (`ConnectAndInstallAsync`) + Task 5 (`PollOnce`). ✓
- Connection lifecycle tied to form (connect on show, release on close) → Task 5 (`ShowForm`/`FormClosing`) + Task 6. ✓
- Remove old bridge JS / injector; keep `EFBBridgeServer` for PMDG/HS787 → Task 6-7 (only A32NX wiring removed; `StartEFBBridgeServer`/`StopEFBBridgeServer` and PMDG/HS787 paths untouched). ✓
- Retarget verification tool → Task 8. ✓
- Error handling (no sim, flyPad off, page reload, eval timeout, agent never throws) → Task 4 (timeouts/reconnect, agent guard) + Task 5 (scrape-fail handling) + agent's top-level try/catch (Task 1). ✓
- Live verification plan → Task 2 + Task 9. ✓

**Placeholder scan:** No TBD/TODO. Agent body is "copy A380 file + specific edits" (a concrete, named source artifact), not a placeholder. `IDX` in Task 2 Step 5 is an explicit "use an idx from the scrape" instruction, not a code placeholder.

**Type consistency:** `CoherentEFBClient` methods (`Start`, `Stop`, `IsReady`, `Connected`, `Disconnected`, `ScrapeAsync`, `ClickAsync`, `SetValueAsync`, `PingAsync`, `PowerOnAsync`, `Dispose`) are used consistently in Task 5. `FlypadScrape`/`FlypadElement` property names (`Ok`, `Error`, `Page`, `Elements`, `Idx`, `Kind`, `Text`, `Value`, `ControlType`, `Clickable`, `Disabled`, `Options`) match between Task 3 and Task 5. New form ctor `A32NXEFBForm(ScreenReaderAnnouncer announcer)` matches the Task 6 call site.

**Known follow-up (not blocking):** `FlowLayoutPanel` focus-restore reads `ActiveControl.Tag`; the text-input case stores idx on the inner `TextBox` (not the wrapping `Panel`), so focus restore for an in-edit text field lands on the panel, not the box — acceptable (the box is one Tab away) and called out for the implementer to refine if in-sim testing shows it's disruptive.
