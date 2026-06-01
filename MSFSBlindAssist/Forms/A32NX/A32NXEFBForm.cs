using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MSFSBlindAssist.Forms.A32NX;

/// <summary>
/// Accessible, generic live view of the FlyByWire A320 flyPad (EFB). Hosts a
/// WebView2 that renders whatever flyPad page is open as an accessible HTML
/// document (so NVDA gets full browse mode), built from the live DOM scrape via
/// the generic CDP agent. No per-feature knowledge.
/// </summary>
public sealed class A32NXEFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly CoherentEfbCdpClient _client;
    private readonly WebView2 _web;
    private readonly System.Windows.Forms.Timer _pollTimer;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _webReady;
    private bool _polling;
    private string _renderSignature = "";
    private string _lastPage = "";
    private DateTime _lastScrapeUtc = DateTime.MinValue;
    private bool _lastScrapeOk;
    private string _lastScrapeError = "";

    public A32NXEFBForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        string agentPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "coherent-flypad-agent.js");
        _client = new CoherentEfbCdpClient(agentPath);
        _client.Connected    += (_, _) => OnConnChanged(true);
        _client.Disconnected += (_, _) => OnConnChanged(false);

        Text = "FlyByWire A320 flyPad";
        AccessibleName = "FlyByWire A320 flyPad";
        Size = new Size(820, 720);
        MinimumSize = new Size(600, 480);
        KeyPreview = true;

        _web = new WebView2 { Dock = DockStyle.Fill, TabIndex = 0, AccessibleName = "flyPad" };
        Controls.Add(_web);

        _pollTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pollTimer.Tick += async (_, _) => await PollOnce();

        KeyDown += async (_, e) => { if (e.KeyCode == Keys.F5) { e.Handled = true; await PollOnce(force: true); } };

        FormClosing += (_, e) =>
        {
            // Hide rather than dispose so reopening is instant; release the single
            // CDP connection so other consumers can use it while the form is closed.
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
        TopMost = true; TopMost = false;
        _ = InitializeAndStartAsync();
    }

    private async Task InitializeAndStartAsync()
    {
        try
        {
            await InitializeWebAsync();
            _client.Start();
            _pollTimer.Start();
            PushStatus();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[A32NX EFB] init: {ex.Message}"); }
    }

    private async Task InitializeWebAsync()
    {
        if (_webReady) return;
        await _web.EnsureCoreWebView2Async();
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.WebMessageReceived += OnWebMessage;

        string shellPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "flypad-shell.html");
        string html = File.Exists(shellPath)
            ? await File.ReadAllTextAsync(shellPath)
            : "<html><body>flyPad shell missing</body></html>";

        var nav = new TaskCompletionSource<bool>();
        void onNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
        { core.NavigationCompleted -= onNav; nav.TrySetResult(true); }
        core.NavigationCompleted += onNav;
        core.NavigateToString(html);
        await nav.Task;
        _webReady = true;
    }

    private void OnConnChanged(bool connected)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _announcer?.Announce(connected ? "flyPad connected." : "flyPad disconnected.");
            PushStatus();
        });
    }

    // ── polling ──────────────────────────────────────────────────────────────

    private async Task PollOnce(bool force = false)
    {
        if (IsDisposed || Disposing || !_webReady) return;
        if (_polling) return;
        _polling = true;
        try
        {
            if (!_client.IsReady) { PushStatus(); return; }
            var scrape = await _client.ScrapeAsync();
            _lastScrapeUtc = DateTime.UtcNow;
            _lastScrapeOk = scrape.Ok;
            _lastScrapeError = scrape.Error ?? "";
            if (!scrape.Ok) { await _client.PowerOnAsync(); PushStatus(); return; }
            string sig = BuildSignature(scrape);
            if (force || sig != _renderSignature) { PushRender(scrape); _renderSignature = sig; }
            if (scrape.Page != _lastPage) { _lastPage = scrape.Page; _announcer?.Announce(scrape.Page); }
            PushStatus();
        }
        finally { _polling = false; }
    }

    private static string BuildSignature(FlypadScrape s)
    {
        // Order-INDEPENDENT content signature. The agent's spatial enumeration
        // returns the same elements in a jittering order between polls; an
        // order-sensitive signature flipped every poll and forced a full re-render
        // (which reset the NVDA browse position). Sorting a per-element content key
        // — and deliberately EXCLUDING idx, which is assigned by enumeration order —
        // means a pure reorder of identical content yields the same signature, so no
        // re-render fires. A genuine change (value/text/element-set/page) still does.
        var keys = new List<string>(s.Elements.Count);
        foreach (var e in s.Elements)
            keys.Add(e.Kind + "" + e.Text + "" + e.Value + "" +
                     e.ControlType + (e.Disabled ? "D" : ""));
        keys.Sort(StringComparer.Ordinal);
        return s.Page + "" + string.Join("", keys);
    }

    // ── push to web ────────────────────────────────────────────────────────────

    private void PushRender(FlypadScrape s)
    {
        if (!_webReady) return;
        string payload = JsonSerializer.Serialize(new { type = "render", page = s.Page, elements = s.Elements });
        _web.CoreWebView2?.PostWebMessageAsJson(payload);
    }

    private void PushStatus()
    {
        string text;
        if (!_client.IsReady) text = "flyPad not detected — connecting…";
        else if (!_lastScrapeOk && _lastScrapeUtc != DateTime.MinValue) text = $"flyPad off — {_lastScrapeError}";
        else
        {
            string age = _lastScrapeUtc == DateTime.MinValue
                ? "—" : $"{(int)(DateTime.UtcNow - _lastScrapeUtc).TotalSeconds}s ago";
            text = $"Live: \"{_lastPage}\" (updated {age})";
        }
        if (!_webReady) return;
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "status", text }));
    }

    // ── messages from web ──────────────────────────────────────────────────────

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            string action = root.TryGetProperty("action", out var a) ? (a.GetString() ?? "") : "";
            switch (action)
            {
                case "click":
                    if (TryIdx(root, out int cidx)) { await _client.ClickAsync(cidx); await DelayThenPoll(); }
                    break;
                case "set":
                    if (TryIdx(root, out int sidx))
                    {
                        string val = root.TryGetProperty("value", out var v) ? (v.GetString() ?? "") : "";
                        await _client.SetValueAsync(sidx, val);
                        await DelayThenPoll();
                    }
                    break;
                case "refresh": await PollOnce(force: true); break;
                case "selftest": await SelfTest(); break;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[A32NX EFB] web msg: {ex.Message}"); }
    }

    private static bool TryIdx(JsonElement root, out int idx)
    {
        idx = 0;
        if (!root.TryGetProperty("idx", out var i)) return false;
        if (i.ValueKind == JsonValueKind.Number) { idx = i.GetInt32(); return true; }
        if (i.ValueKind == JsonValueKind.String && int.TryParse(i.GetString(), out idx)) return true;
        return false;
    }

    private async Task DelayThenPoll()
    {
        try { await Task.Delay(300); } catch { }
        await PollOnce(force: true);
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
        PushStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pollTimer?.Dispose(); _client?.Dispose(); _web?.Dispose(); }
        base.Dispose(disposing);
    }
}
