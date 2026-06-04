using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible reader for the FlyByWire flyPad EFB (flyPadOS 3) — SHARED by the
/// A32NX and A380X flyPads, and reused a third time for the A380 OANS airport map
/// (driven by CoherentNDClient instead of CoherentEFBClient). The window title and
/// device noun ("flyPad" / "OANS") are constructor parameters; everything else is
/// generic over IMcduBridge, so it is not A380-specific despite the folder.
///
/// DESIGN — WebView2 "browser mode" with a native-control fallback
/// ---------------------------------------------------------------
/// The flyPad is a live React web app reached through the Coherent GT
/// debugger (CoherentEFBClient, no injection). Its page is scraped into a
/// flat element list. We render that list two ways:
///
///   • BROWSER MODE (default): the elements are rendered as a real semantic
///     HTML document inside an embedded WebView2 (Edge/Chromium). Because
///     WebView2 exposes a genuine UI-Automation accessibility tree, the
///     screen reader gets true browse mode for free — heading quick-nav (H),
///     link quick-nav (K), arrow-by-line reading, real edit fields, and
///     aria-live regions — exactly like browsing a web page. User actions
///     (click / type) are posted back to C# via window.chrome.webview and
///     proxied to the live flyPad through the same Coherent agent.
///
///   • LIST MODE (silent fallback): the original flat list of native WinForms
///     controls (Tab / Shift+Tab). Used automatically ONLY if WebView2 fails
///     to initialise; there is no user-facing toggle.
///
/// Both modes share the exact same data flow: elements arrive via
/// IMcduBridge.StateUpdated; commands go out via IMcduBridge.EnqueueCommand
/// (get_display_elements / click_display_element / set_element_value).
/// </summary>
public class FbwEfbForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeElements = "fbw_efb_elements";
    private const string StateTypeConnected = "fbw_efb_connected";
    private const char OptionSeparator = (char)0x1f;

    private readonly IMcduBridge _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _windowTitle;
    private readonly string _deviceNoun; // "flyPad" or "OANS" — used in status text

    private Label _statusLabel = null!;
    private Label _pageLabel = null!;
    private Panel _contentPanel = null!;
    private WebView2 _webView = null!;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _bridgeConnected;
    private bool _initialPushReceived;
    private List<EFBElement> _elements = new();
    private string _currentPage = "";
    private string _previousAnnouncedPage = "";

    // Browser mode = WebView2 semantic document; otherwise the native list.
    private bool _useBrowser = true;
    private bool _webViewReady;
    private bool _webViewFailed;
    private string? _pendingRenderJson;
    // Render coalescing: the ~400ms scrape poll can push renders faster than the
    // WebView2 applies them. We run ONE ExecuteScriptAsync at a time and keep only
    // the LATEST queued render — so navigation never piles up dozens of overlapping
    // script executions (the "crashes when moving through the EFB" / refresh-rate issue).
    private bool _renderInFlight;
    private string? _queuedRender;

    // Maps a flyPad element index to the native control rendering it (list mode).
    private readonly Dictionary<int, RenderedControl> _rendered = new();
    private string _lastStructureSignature = "";
    private bool _suppressControlEvents;

    private System.Windows.Forms.Timer _statusTimer = null!;

    public FbwEfbForm(IMcduBridge bridgeServer, ScreenReaderAnnouncer announcer,
                          string? windowTitle = null, string? deviceNoun = null)
    {
        _bridgeServer = bridgeServer;
        _announcer = announcer;
        _windowTitle = windowTitle ?? "FBW flyPad EFB";
        _deviceNoun = deviceNoun ?? "flyPad";

        InitializeComponent();
        WireEvents();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _statusTimer.Tick += (_, _) => UpdateStatusLabel();
        _statusTimer.Start();

        _bridgeServer.StateUpdated += OnBridgeStateUpdated;
        _ = InitWebViewAsync();
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        if (!Visible) Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _bridgeServer.EnqueueCommand("get_display_elements");
        FocusContent();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = _windowTitle;
        ClientSize = new Size(720, 620);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _statusLabel = new Label
        {
            Text = "EFB bridge: connecting…",
            Location = new Point(10, 10),
            Size = new Size(700, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = "EFB bridge status"
        };
        Controls.Add(_statusLabel);

        _pageLabel = new Label
        {
            Text = "Page: (unknown)",
            Location = new Point(10, 36),
            Size = new Size(700, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = "Current flyPad page"
        };
        Controls.Add(_pageLabel);

        var contentBounds = new Rectangle(10, 62, 700, 506);

        // Browser view (primary).
        _webView = new WebView2
        {
            Location = contentBounds.Location,
            Size = contentBounds.Size,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AccessibleName = "flyPad page (browser view)",
            Visible = true
        };
        Controls.Add(_webView);

        // List view (fallback) — same bounds, hidden until needed.
        _contentPanel = new Panel
        {
            Location = contentBounds.Location,
            Size = contentBounds.Size,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "flyPad page content (list view)",
            AccessibleDescription = "Interactive controls for the current flyPad page. Tab between them; F5 refreshes.",
            Visible = false
        };
        Controls.Add(_contentPanel);

        // (No "Refresh" button — F5 refreshes; a dedicated button was redundant clutter.)

        ResumeLayout(true);
    }

    private void WireEvents()
    {
        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                _bridgeServer.EnqueueCommand("get_display_elements");
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F6)
            {
                AnnounceSelfTest();
                e.Handled = true;
            }
        };
    }

    // On-demand spoken connection/self-test (F6). The status label is always live,
    // but a blind user shouldn't have to hunt for it — F6 speaks the same state.
    // Shared by both flyPads and the OANS view (uses _deviceNoun).
    private void AnnounceSelfTest()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string msg;
        if (reallyConnected && _initialPushReceived)
            msg = $"{_deviceNoun} connected. Page {(string.IsNullOrEmpty(_currentPage) ? "unknown" : _currentPage)}. {_elements.Count} elements.";
        else if (reallyConnected)
            msg = $"{_deviceNoun} connected, waiting for content.";
        else
            msg = $"{_deviceNoun} not connected. Make sure the aircraft is loaded and the tablet is powered on.";
        _announcer.Announce(msg);
    }

    // ---- WebView2 setup ---------------------------------------------------

    private async Task InitWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            var s = _webView.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsZoomControlEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            // If the WebView2 render/browser process dies, don't let it take the host
            // with it: mark not-ready (so pushes queue instead of throwing) and try one
            // reload. The native list view remains as a backstop if it can't recover.
            _webView.CoreWebView2.ProcessFailed += (_, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[FBW flyPad] WebView2 process failed: {args.ProcessFailedKind}");
                _webViewReady = false;
                _renderInFlight = false;
                try { if (!IsDisposed && _webView?.CoreWebView2 != null) _webView.CoreWebView2.Reload(); } catch { }
            };
            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _webViewReady = true;
                if (_pendingRenderJson != null) { PushToBrowser(_pendingRenderJson); _pendingRenderJson = null; }
            };
            _webView.CoreWebView2.NavigateToString(PageHtml);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[A380 flyPad] WebView2 init failed: {ex.Message}");
            _webViewFailed = true;
            _useBrowser = false;
            if (IsHandleCreated) BeginInvoke(() => { SwitchToListMode(); ApplyElements(); });
        }
    }

    private void SwitchToListMode()
    {
        _useBrowser = false;
        _webView.Visible = false;
        _contentPanel.Visible = true;
    }

    // ---- incoming elements ------------------------------------------------

    private void OnBridgeStateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (e.Type == StateTypeConnected)
        {
            _bridgeConnected = true;
            if (IsHandleCreated) BeginInvoke(UpdateStatusLabel);
            return;
        }
        if (e.Type != StateTypeElements) return;

        _bridgeConnected = true;
        _initialPushReceived = true;

        e.Data.TryGetValue("page", out string? page);
        if (page != null) _currentPage = page;

        var byIndex = new SortedDictionary<int, EFBElement>();
        foreach (var kv in e.Data)
        {
            if (!kv.Key.StartsWith("items.")) continue;
            var parts = kv.Key.Split('.');
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[1], out int idx)) continue;
            if (!byIndex.TryGetValue(idx, out var el))
            {
                el = new EFBElement { Index = idx };
                byIndex[idx] = el;
            }
            switch (parts[2])
            {
                case "aidx":      el.AgentIdx = int.TryParse(kv.Value, out var ai) ? ai : 0; break;
                case "text":      el.Text = kv.Value; break;
                case "tag":       el.Tag = kv.Value; break;
                case "role":      el.Role = kv.Value; break;
                case "value":     el.Value = kv.Value; break;
                case "type":      el.ControlType = kv.Value; break;
                case "clickable": el.Clickable = kv.Value == "true"; break;
                case "kind":      el.Kind = kv.Value; break;
                case "level":     el.Level = int.TryParse(kv.Value, out var lv) ? lv : 0; break;
                case "live":      el.Live = kv.Value; break;
                case "disabled":  el.Disabled = kv.Value == "true"; break;
                case "options":   el.Options = string.IsNullOrEmpty(kv.Value) ? null : kv.Value.Split(OptionSeparator); break;
                case "min":       el.Min = ParseInv(kv.Value); break;
                case "max":       el.Max = ParseInv(kv.Value); break;
                case "step":      el.Step = ParseInv(kv.Value); break;
            }
        }
        _elements = byIndex.Values.ToList();
        if (IsHandleCreated) BeginInvoke(ApplyElements);
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired;
        if (reallyConnected && _initialPushReceived)
            desired = $"{_deviceNoun} connected ({_elements.Count} elements)";
        else if (reallyConnected)
            desired = $"{_deviceNoun} connected — waiting for content…";
        else
            desired = $"{_deviceNoun} not connected. Make sure MSFS is running, the A380X is loaded, and the displays are powered up.";
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void ApplyElements()
    {
        _pageLabel.Text = $"Page: {(string.IsNullOrEmpty(_currentPage) ? "(unknown)" : _currentPage)}";

        if (_useBrowser && !_webViewFailed)
        {
            // The browser document carries the page name in an aria-live region,
            // so the screen reader announces page changes itself — no app TTS.
            PushToBrowser(BuildRenderJson());
        }
        else
        {
            if (!string.IsNullOrEmpty(_currentPage) && _currentPage != _previousAnnouncedPage)
            {
                _announcer.Announce("flyPad page: " + _currentPage);
                _previousAnnouncedPage = _currentPage;
            }
            string signature = StructureSignature();
            if (signature == _lastStructureSignature && _rendered.Count > 0)
                UpdateControlsInPlace();
            else
                RebuildControls(signature);
        }

        UpdateStatusLabel();
    }

    // ---- browser-mode rendering ------------------------------------------

    private string BuildRenderJson()
    {
        var items = _elements.Select(e => new
        {
            idx = e.AgentIdx,
            kind = e.Kind,
            tag = e.Tag,
            role = e.Role,
            text = e.Text,
            value = e.Value,
            controlType = e.ControlType ?? "",
            clickable = e.Clickable,
            level = e.Level,
            live = e.Live,
            disabled = e.Disabled,
            options = e.Options,
            min = e.Min,
            max = e.Max,
            step = e.Step
        });
        return JsonSerializer.Serialize(new { page = _currentPage, items });
    }

    private async void PushToBrowser(string json)
    {
        // Disposed/closing guard — the poll runs on a background thread and marshals
        // here; the form/WebView can be torn down between the check and the call.
        if (IsDisposed || Disposing || _webView == null || _webView.IsDisposed
            || !_webViewReady || _webView.CoreWebView2 == null)
        {
            _pendingRenderJson = json;
            return;
        }
        // Coalesce: if a render is already running, keep only the newest and bail.
        if (_renderInFlight) { _queuedRender = json; return; }
        _renderInFlight = true;
        try
        {
            // AWAIT it (not fire-and-forget) so a fault — e.g. the WebView2 went away
            // mid-navigation — is caught HERE instead of becoming an unobserved task
            // exception that could crash the process.
            await _webView.CoreWebView2.ExecuteScriptAsync("window.__render(" + json + ")");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FBW flyPad] render push failed: {ex.Message}");
        }
        finally
        {
            _renderInFlight = false;
            // Apply the most recent render that arrived while this one was running.
            if (_queuedRender != null && !IsDisposed && !Disposing)
            {
                string next = _queuedRender;
                _queuedRender = null;
                PushToBrowser(next);
            }
            else
            {
                _queuedRender = null;
            }
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string body;
        try { body = e.TryGetWebMessageAsString(); }
        catch { return; }
        if (string.IsNullOrEmpty(body)) return;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            string idx = root.TryGetProperty("idx", out var i) ? i.ToString() : "0";

            if (type == "click")
            {
                _bridgeServer.EnqueueCommand("click_display_element",
                    new Dictionary<string, string> { ["index"] = idx });
                // flyPad nav changes the route; force a fresh scrape shortly after.
                var tm = new System.Windows.Forms.Timer { Interval = 450 };
                tm.Tick += (_, _) => { tm.Stop(); tm.Dispose(); _bridgeServer.EnqueueCommand("get_display_elements"); };
                tm.Start();
            }
            else if (type == "set")
            {
                string value = root.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                string ctype = root.TryGetProperty("controlType", out var c) ? c.GetString() ?? "" : "";
                _bridgeServer.EnqueueCommand("set_element_value", new Dictionary<string, string>
                {
                    ["index"] = idx,
                    ["value"] = value,
                    ["controlType"] = ctype
                });
            }
            else if (type == "refresh")
            {
                // F5 inside the WebView2 (it captures the key before the form does).
                _bridgeServer.EnqueueCommand("get_display_elements");
            }
        }
        catch { /* malformed message — ignore */ }
    }

    // ---- list-mode rendering (fallback) ----------------------------------

    private string StructureSignature()
    {
        return _currentPage + "" + string.Join("",
            _elements.Select(e => e.Index + "|" + (e.ControlType ?? "") + "|" + e.Clickable + "|" + e.Tag + "|" + e.Text));
    }

    private void UpdateControlsInPlace()
    {
        _suppressControlEvents = true;
        try
        {
            foreach (var el in _elements)
            {
                if (!_rendered.TryGetValue(el.Index, out var r)) continue;
                if (r.Control is CheckBox cb)
                {
                    bool want = el.Value == "true";
                    if (cb.Checked != want) cb.Checked = want;
                }
                else if (r.Control is TextBox tb)
                {
                    if (!tb.Focused && tb.Text != el.Value) tb.Text = el.Value;
                }
            }
        }
        finally { _suppressControlEvents = false; }
    }

    private void RebuildControls(string signature)
    {
        _contentPanel.SuspendLayout();
        _contentPanel.Controls.Clear();
        _rendered.Clear();

        int y = 8;
        int tabIndex = 0;
        int innerWidth = _contentPanel.ClientSize.Width - 24;
        if (innerWidth < 200) innerWidth = 200;

        foreach (var el in _elements)
        {
            Control c = CreateControlFor(el, innerWidth);
            c.Location = new Point(8, y);
            c.TabIndex = tabIndex++;
            _contentPanel.Controls.Add(c);
            _rendered[el.Index] = new RenderedControl { Control = c };
            y += c.Height + 6;
        }

        _contentPanel.ResumeLayout(true);
        _lastStructureSignature = signature;
    }

    private Control CreateControlFor(EFBElement el, int width)
    {
        if (el.ControlType is "checkbox" or "radio")
        {
            var cb = new CheckBox
            {
                Text = string.IsNullOrEmpty(el.Text) ? "(unnamed checkbox)" : el.Text,
                Checked = el.Value == "true",
                AutoSize = false,
                Size = new Size(width, 24),
                AccessibleName = el.Text
            };
            int idx = el.AgentIdx;   // stamped agent idx — what click/set look up
            cb.CheckedChanged += (_, _) =>
            {
                if (_suppressControlEvents) return;
                _bridgeServer.EnqueueCommand("set_element_value", new Dictionary<string, string>
                {
                    ["index"] = idx.ToString(),
                    ["value"] = cb.Checked ? "true" : "false",
                    ["controlType"] = "checkbox"
                });
            };
            return cb;
        }

        if (el.ControlType is "text" or "select")
        {
            var container = new Panel { Size = new Size(width, 46), AccessibleRole = AccessibleRole.Grouping };
            var label = new Label
            {
                Text = (string.IsNullOrEmpty(el.Text) ? "(unnamed field)" : el.Text)
                       + (el.ControlType == "select" ? " (choice — type a value)" : ""),
                Location = new Point(0, 0),
                AutoSize = true
            };
            var tb = new TextBox
            {
                Text = el.Value,
                Location = new Point(0, 20),
                Size = new Size(width - 4, 23),
                AccessibleName = el.Text,
                AccessibleDescription = "Type a value and press Enter to set it."
            };
            int idx = el.AgentIdx;   // stamped agent idx — what click/set look up
            string controlType = el.ControlType!;
            string fieldName = el.Text;
            tb.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return)
                {
                    _bridgeServer.EnqueueCommand("set_element_value", new Dictionary<string, string>
                    {
                        ["index"] = idx.ToString(),
                        ["value"] = tb.Text,
                        ["controlType"] = controlType
                    });
                    _announcer.Announce("Set " + (string.IsNullOrEmpty(fieldName) ? "field" : fieldName) + " to " + tb.Text);
                    e.Handled = true; e.SuppressKeyPress = true;
                }
            };
            container.Controls.Add(label);
            container.Controls.Add(tb);
            return container;
        }

        if (el.Tag is "h1" or "h2" or "h3" or "h4")
        {
            return new Label
            {
                Text = el.Text,
                Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(width, 24),
                AccessibleName = el.Text,
                AccessibleRole = AccessibleRole.StaticText
            };
        }

        if (el.Clickable || el.Tag == "button" || el.Role == "button" || el.Tag == "a")
        {
            string text = string.IsNullOrEmpty(el.Text) ? "(unnamed button)" : el.Text;
            var btn = new Button
            {
                Text = text,
                AutoSize = false,
                Size = new Size(Math.Min(width, Math.Max(120, TextRenderer.MeasureText(text, Font).Width + 32)), 30),
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = text
            };
            int idx = el.AgentIdx;   // stamped agent idx — what click/set look up
            btn.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("click_display_element",
                    new Dictionary<string, string> { ["index"] = idx.ToString() });
                var t = new System.Windows.Forms.Timer { Interval = 450 };
                t.Tick += (_, _) =>
                {
                    t.Stop(); t.Dispose();
                    _bridgeServer.EnqueueCommand("get_display_elements");
                };
                t.Start();
            };
            return btn;
        }

        return new Label
        {
            Text = el.Text,
            AutoSize = false,
            Size = new Size(width, 22),
            AccessibleName = el.Text,
            AccessibleRole = AccessibleRole.StaticText
        };
    }

    private void FocusContent()
    {
        if (_useBrowser && !_webViewFailed) { _webView.Focus(); return; }
        foreach (Control c in _contentPanel.Controls)
        {
            if (c.CanSelect && (c is CheckBox or Button or TextBox)) { c.Focus(); return; }
            foreach (Control child in c.Controls)
                if (child is TextBox tb) { tb.Focus(); return; }
        }
        _contentPanel.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridgeServer.StateUpdated -= OnBridgeStateUpdated;
            _statusTimer?.Stop();
            _statusTimer?.Dispose();
            // Unsubscribe the WebView2 message handler before disposing so a
            // late message can't fire against a half-disposed form.
            try { if (_webView?.CoreWebView2 != null) _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived; } catch { }
            _webView?.Dispose();
        }
        base.Dispose(disposing);
    }

    private class RenderedControl
    {
        public Control Control = null!;
    }

    private class EFBElement
    {
        public int Index;        // list position (stable dict / control key)
        public int AgentIdx;     // agent's stamped data-fbw-efb-idx (for click/set)
        public string Text = "";
        public string Tag = "";
        public string Role = "";
        public string Value = "";
        public string? ControlType;
        public bool Clickable;
        public string Kind = "";
        public int Level;
        public string Live = "";
        public bool Disabled;
        public string[]? Options;
        public double? Min;      // range (slider) bounds for controlType "range"
        public double? Max;
        public double? Step;
    }

    private static double? ParseInv(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    // ---- the semantic HTML document hosted in WebView2 --------------------
    //
    // window.__render({page, items}) rebuilds the body as real HTML so the
    // screen reader gets native browse mode. Interactions post back to C#
    // through window.chrome.webview.postMessage. Focus is preserved across
    // re-renders by remembering the focused element's data-idx (and caret).
    private const string PageHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>A380X flyPad</title>
<style>
  body { font-family: 'Segoe UI', Tahoma, sans-serif; margin: 0; padding: 10px; background: #14141f; color: #eee; }
  h1 { font-size: 20px; margin: 0 0 4px 0; color: #fff; }
  h2 { font-size: 17px; margin: 12px 0 4px 0; color: #cfe3ff; }
  h3 { font-size: 15px; margin: 10px 0 4px 0; color: #cfe3ff; }
  h4, h5, h6 { font-size: 14px; margin: 8px 0 4px 0; color: #cfe3ff; }
  p.txt { margin: 3px 0; color: #ddd; }
  a.lnk { display: inline-block; margin: 4px 0; color: #7fbfff; }
  a.lnk:focus, button.btn:focus, input:focus, select:focus { outline: 3px solid #FFD700; }
  button.btn { display: block; width: 100%; text-align: left; padding: 8px 12px; margin: 4px 0; border: 1px solid #445; border-radius: 6px; cursor: pointer; font-size: 14px; background: #0e4d92; color: #fff; font-family: inherit; }
  button.btn[data-disabled=""true""] { background: #333; color: #999; cursor: default; }
  label.fld { display: block; margin: 8px 0; color: #cfe3ff; font-size: 14px; }
  input.in, select.sel { display: block; width: 92%; padding: 6px 8px; margin-top: 2px; border: 1px solid #555; border-radius: 4px; background: #1a1a30; color: #eee; font-size: 14px; font-family: inherit; }
  label.chk { display: block; margin: 8px 0; color: #ddd; font-size: 14px; cursor: pointer; }
  label.chk input { width: 18px; height: 18px; margin-right: 8px; vertical-align: middle; }
  #live { position: absolute; left: -9999px; }
</style>
</head>
<body>
<div id=""live"" aria-live=""polite""></div>
<main id=""content"">
  <h1 id=""pgttl"" tabindex=""-1"">flyPad</h1>
  <div id=""list""><p class=""txt"">Waiting for the flyPad…</p></div>
</main>
<script>
  var lastPage = '';
  var renderedPage = null;   // last page whose list was built; drives teardown-on-page-change
  function post(obj) {
    try { window.chrome.webview.postMessage(JSON.stringify(obj)); } catch (e) {}
  }
  function announce(msg) {
    var l = document.getElementById('live');
    l.textContent = '';
    setTimeout(function () { l.textContent = msg; }, 30);
  }
  function setText(node, t) { t = t || ''; if (node.textContent !== t) node.textContent = t; }

  // Classify an element to its rendered node TYPE — used to build AND to key.
  function rk(it) {
    var ct = it.controlType || '';
    if (ct === 'range') return 'rng';
    if (ct === 'checkbox') return 'cb';
    if (ct === 'select' && it.options && it.options.length) return 'sel';
    if (ct === 'text' || ct === 'select') return 'in';
    if (it.level >= 1 && it.level <= 6) return 'h' + it.level;
    if (it.clickable || it.kind === 'button' || it.kind === 'tab' || it.tag === 'button') return 'btn';
    if (it.kind === 'link' || it.tag === 'a') return 'lnk';
    return 'p';
  }
  // CONTENT key, NOT the scrape idx. The agent re-stamps data-idx from 1 in DOM
  // order on EVERY scrape, so an idx key cross-patches controls across pages and
  // across same-named sub-tabs. Keying by rendered-type + label keeps a node stable
  // across same-page polls (values are patched in place) while a sub-tab/page switch
  // cleanly swaps controls. The live data-idx for click/set is patched in place.
  // Strip the DYNAMIC state suffixes the agent appends -- (active) / (called) /
  // (selected) / (current page) and the colon placed/not-placed markers -- from the
  // reconcile key, so a control whose state changes (a door tile activated, a rate
  // option selected) maps to the SAME node and is patched IN PLACE rather than
  // destroyed + rebuilt. Rebuilding moved the screen-reader focus off the control
  // the user just activated. The visible label still updates via patchEl; only the
  // key is stabilised.
  function baseLabel(t) {
    return (t || '')
      .replace(/\s*\((active|called|selected|current page|expanded|collapsed)\)\s*$/i, '')
      .replace(/:\s*(placed|not placed)\s*$/i, '');
  }
  function keyOf(it) { return rk(it) + '|' + baseLabel(it.text || ''); }

  function onActivate(e) {
    var idx = this.getAttribute('data-idx');
    if (this.getAttribute('data-disabled') === 'true') { announce('Unavailable'); return; }
    announce('Activating ' + (this.textContent || ''));
    post({ type: 'click', idx: idx });
  }

  // Build a fresh node for an element.
  function buildEl(it) {
    var type = rk(it), text = it.text || '';
    if (type === 'cb') {
      var lab = document.createElement('label'); lab.className = 'chk';
      var cb = document.createElement('input'); cb.type = 'checkbox';
      cb.checked = it.value === 'true'; if (it.disabled) cb.disabled = true;
      cb.setAttribute('data-idx', String(it.idx));
      cb.addEventListener('change', function () { post({ type: 'set', idx: this.getAttribute('data-idx'), value: String(this.checked), controlType: 'checkbox' }); });
      lab.appendChild(cb); lab.appendChild(document.createTextNode(' ' + (text || '(checkbox)')));
      return lab;
    }
    if (type === 'sel') {
      var sl = document.createElement('label'); sl.className = 'fld';
      var sp = document.createElement('span'); sp.textContent = (text || 'Choice') + ': '; sl.appendChild(sp);
      var sel = document.createElement('select'); sel.className = 'sel';
      sel.setAttribute('data-idx', String(it.idx)); sel.setAttribute('aria-label', text || 'Choice');
      if (it.disabled) sel.disabled = true;
      var opts = it.options || [];
      for (var o = 0; o < opts.length; o++) { var op = document.createElement('option'); op.value = opts[o]; op.textContent = opts[o]; if (opts[o] === it.value) op.selected = true; sel.appendChild(op); }
      sel.addEventListener('change', function () { post({ type: 'set', idx: this.getAttribute('data-idx'), value: this.value, controlType: 'select' }); });
      sl.appendChild(sel); return sl;
    }
    if (type === 'rng') {
      var rl = document.createElement('label'); rl.className = 'fld';
      var rsp = document.createElement('span'); rsp.textContent = (text || 'Slider') + ': '; rl.appendChild(rsp);
      var rg = document.createElement('input'); rg.type = 'range'; rg.className = 'in';
      if (it.min != null) rg.min = it.min;
      if (it.max != null) rg.max = it.max;
      if (it.step != null) rg.step = it.step;
      rg.value = it.value || (it.min != null ? it.min : 0);
      rg.setAttribute('data-idx', String(it.idx)); rg.setAttribute('aria-label', text || 'Slider');
      if (it.disabled) rg.disabled = true;
      rg.addEventListener('change', function () { post({ type: 'set', idx: this.getAttribute('data-idx'), value: this.value, controlType: 'range' }); });
      rl.appendChild(rg); return rl;
    }
    if (type === 'in') {
      var fl = document.createElement('label'); fl.className = 'fld';
      var sp2 = document.createElement('span'); sp2.textContent = (text || 'Field') + ': '; fl.appendChild(sp2);
      var inp = document.createElement('input'); inp.type = 'text'; inp.className = 'in';
      inp.value = it.value || ''; if (it.disabled) inp.disabled = true;
      inp.setAttribute('data-idx', String(it.idx)); inp.setAttribute('aria-label', text || 'Field');
      function commit() { post({ type: 'set', idx: inp.getAttribute('data-idx'), value: inp.value, controlType: 'text' }); }
      inp.addEventListener('change', commit);
      inp.addEventListener('keydown', function (ev) { if (ev.key === 'Enter') { ev.preventDefault(); commit(); } });
      fl.appendChild(inp); return fl;
    }
    if (type.charAt(0) === 'h' && type.length === 2) {
      var hd = document.createElement(type); hd.textContent = text; hd.setAttribute('data-idx', String(it.idx)); return hd;
    }
    if (type === 'btn') {
      var b = document.createElement('button'); b.className = 'btn'; b.textContent = text || '(button)';
      b.setAttribute('data-idx', String(it.idx));
      if (it.disabled) { b.setAttribute('data-disabled', 'true'); b.textContent += ', dimmed'; }
      b.addEventListener('click', onActivate);
      b.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { onActivate.call(this, e); } });
      return b;
    }
    if (type === 'lnk') {
      var a = document.createElement('a'); a.className = 'lnk'; a.href = '#'; a.textContent = text || '(link)';
      a.setAttribute('data-idx', String(it.idx));
      if (it.disabled) { a.setAttribute('data-disabled', 'true'); a.textContent += ', dimmed'; }
      a.addEventListener('click', function (e) { e.preventDefault(); onActivate.call(this, e); });
      return a;
    }
    var p = document.createElement('p'); p.className = 'txt'; p.textContent = text; p.setAttribute('data-idx', String(it.idx));
    if (it.live === 'assertive' || it.live === 'polite') p.setAttribute('aria-live', it.live);
    return p;
  }

  function ctrlOf(node) { if (node.matches && node.matches('[data-idx]')) return node; return node.querySelector ? node.querySelector('[data-idx]') : null; }

  // Patch a reused node's mutable bits IN PLACE — never recreate it, never write to
  // the control the user is currently editing (preserves the screen-reader cursor).
  function patchEl(node, it) {
    var type = rk(it), text = it.text || '';
    var c = ctrlOf(node);
    if (!c) { setText(node, text); return; }
    if (c.dataset) c.dataset.idx = it.idx;            // keep the click/set handle current
    if (type.charAt(0) === 'h' && type.length === 2) { setText(c, text); return; }
    if (type === 'cb') {
      var want = it.value === 'true';
      if (document.activeElement !== c && c.checked !== want) c.checked = want;
      c.disabled = !!it.disabled;
      var tn = c.nextSibling; if (tn && tn.nodeType === 3) tn.nodeValue = ' ' + (text || '(checkbox)');
      return;
    }
    if (type === 'sel') {
      c.disabled = !!it.disabled;
      var opts = it.options || [];
      var same = c.options.length === opts.length;
      if (same) { for (var k = 0; k < opts.length; k++) { if (c.options[k].value !== opts[k]) { same = false; break; } } }
      if (!same) { c.innerHTML = ''; for (var j = 0; j < opts.length; j++) { var op = document.createElement('option'); op.value = opts[j]; op.textContent = opts[j]; c.appendChild(op); } }
      if (document.activeElement !== c && c.value !== (it.value || '')) c.value = it.value || '';
      var sp = node.querySelector('span'); if (sp) setText(sp, (text || 'Choice') + ': ');
      return;
    }
    if (type === 'rng') {
      c.disabled = !!it.disabled;
      if (it.min != null) c.min = it.min;
      if (it.max != null) c.max = it.max;
      if (it.step != null) c.step = it.step;
      if (document.activeElement !== c) { var rv = it.value || ''; if (c.value !== rv) c.value = rv; }
      var rsp2 = node.querySelector('span'); if (rsp2) setText(rsp2, (text || 'Slider') + ': ');
      return;
    }
    if (type === 'in') {
      c.disabled = !!it.disabled;
      if (document.activeElement !== c) { var nv = it.value || ''; if (c.value !== nv) c.value = nv; }
      var sp2 = node.querySelector('span'); if (sp2) setText(sp2, (text || 'Field') + ': ');
      return;
    }
    if (type === 'btn' || type === 'lnk') {
      var label = text || (type === 'btn' ? '(button)' : '(link)');
      if (it.disabled) { c.setAttribute('data-disabled', 'true'); label += ', dimmed'; }
      else if (c.getAttribute('data-disabled') === 'true') { c.removeAttribute('data-disabled'); }
      setText(c, label);
      return;
    }
    setText(c, text);   // plain text <p>
  }

  // Keyed in-place reconciliation (adopted from Gus's A32NX flyPad). Reusing nodes
  // instead of wiping innerHTML is what stops NVDA's browse cursor resetting to the
  // top on every poll. Full teardown happens ONLY on a real page change.
  window.__render = function (payload) {
    var sig = JSON.stringify(payload);
    if (sig === window.__lastRenderSig) return;        // identical payload — nothing to do
    window.__lastRenderSig = sig;

    var page = payload.page || '';
    setText(document.getElementById('pgttl'), page ? ('flyPad: ' + page) : 'flyPad');
    if (page && page !== lastPage) { announce('flyPad page: ' + page); lastPage = page; }

    var list = document.getElementById('list');
    if (page !== renderedPage) { list.innerHTML = ''; renderedPage = page; }   // page change → clean rebuild

    var els = payload.items || [];
    var existing = {};
    for (var j = 0; j < list.children.length; j++) { var ck = list.children[j].getAttribute('data-key'); if (ck) existing[ck] = list.children[j]; }

    var used = {}, seen = {}, prev = null;
    for (var n = 0; n < els.length; n++) {
      var it = els[n], base = keyOf(it);
      var occ = (seen[base] = (seen[base] || 0) + 1);
      var key = base + '#' + occ;                       // disambiguate duplicate labels
      var node = existing[key];
      if (node) { patchEl(node, it); }
      else { node = buildEl(it); if (!node) continue; node.setAttribute('data-key', key); }
      used[key] = true;
      var ref = prev ? prev.nextSibling : list.firstChild;
      if (node !== ref) list.insertBefore(node, ref);    // move only if out of place
      prev = node;
    }
    for (var gone in existing) { if (!used[gone]) list.removeChild(existing[gone]); }
  };

  document.addEventListener('keydown', function (e) {
    if (e.key === 'F5') { e.preventDefault(); post({ type: 'refresh' }); }
  });
</script>
</body>
</html>";
}

