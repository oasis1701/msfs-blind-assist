using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible reader for the FlyByWire A380X flyPad EFB (flyPadOS 3).
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
public class FBWA380EFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeElements = "fbwa380_efb_elements";
    private const string StateTypeConnected = "fbwa380_efb_connected";
    private const char OptionSeparator = (char)0x1f;

    private readonly IMcduBridge _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _statusLabel = null!;
    private Label _pageLabel = null!;
    private Panel _contentPanel = null!;
    private WebView2 _webView = null!;
    private Button _refreshBtn = null!;

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

    // Maps a flyPad element index to the native control rendering it (list mode).
    private readonly Dictionary<int, RenderedControl> _rendered = new();
    private string _lastStructureSignature = "";
    private bool _suppressControlEvents;

    private System.Windows.Forms.Timer _statusTimer = null!;

    public FBWA380EFBForm(IMcduBridge bridgeServer, ScreenReaderAnnouncer announcer)
    {
        _bridgeServer = bridgeServer;
        _announcer = announcer;

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

        Text = "A380X flyPad EFB";
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

        _refreshBtn = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(10, 576),
            Size = new Size(140, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            AccessibleName = "Refresh",
            AccessibleDescription = "Re-pull the element list from the flyPad."
        };
        Controls.Add(_refreshBtn);

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

        _refreshBtn.Click += (_, _) => _bridgeServer.EnqueueCommand("get_display_elements");

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                _bridgeServer.EnqueueCommand("get_display_elements");
                e.Handled = true;
            }
        };
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
            desired = $"flyPad connected ({_elements.Count} elements)";
        else if (reallyConnected)
            desired = "flyPad connected — waiting for content (power on the tablet)…";
        else
            desired = "flyPad not connected. Make sure MSFS is running with Developer Mode on, the A380X is loaded, and the flyPad tablet is powered up.";
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
            options = e.Options
        });
        return JsonSerializer.Serialize(new { page = _currentPage, items });
    }

    private void PushToBrowser(string json)
    {
        if (!_webViewReady || _webView.CoreWebView2 == null)
        {
            _pendingRenderJson = json;
            return;
        }
        try
        {
            _ = _webView.CoreWebView2.ExecuteScriptAsync("window.__render(" + json + ")");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[A380 flyPad] render push failed: {ex.Message}");
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
            int idx = el.Index;
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
            int idx = el.Index;
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
            int idx = el.Index;
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
        public int AgentIdx;     // agent's stamped data-fbwa380-efb-idx (for click/set)
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
    }

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
  button.btn[aria-disabled=""true""] { background: #333; color: #999; cursor: default; }
  label.fld { display: block; margin: 8px 0; color: #cfe3ff; font-size: 14px; }
  input.in, select.sel { display: block; width: 92%; padding: 6px 8px; margin-top: 2px; border: 1px solid #555; border-radius: 4px; background: #1a1a30; color: #eee; font-size: 14px; font-family: inherit; }
  label.chk { display: block; margin: 8px 0; color: #ddd; font-size: 14px; cursor: pointer; }
  label.chk input { width: 18px; height: 18px; margin-right: 8px; vertical-align: middle; }
  #live { position: absolute; left: -9999px; }
</style>
</head>
<body>
<div id=""live"" aria-live=""polite""></div>
<main id=""content""><p class=""txt"">Waiting for the flyPad…</p></main>
<script>
  var lastPage = '';
  function post(obj) {
    try { window.chrome.webview.postMessage(JSON.stringify(obj)); } catch (e) {}
  }
  function announce(msg) {
    var l = document.getElementById('live');
    l.textContent = '';
    setTimeout(function () { l.textContent = msg; }, 30);
  }
  window.__render = function (payload) {
    var content = document.getElementById('content');
    // Remember focus so a poll-driven re-render doesn't yank the cursor.
    var act = document.activeElement;
    var focusIdx = act ? act.getAttribute('data-idx') : null;
    var caret = (act && typeof act.selectionStart === 'number') ? act.selectionStart : null;

    content.innerHTML = '';
    var page = payload.page || '';
    var h = document.createElement('h1');
    h.setAttribute('tabindex', '-1');
    h.textContent = page ? ('flyPad: ' + page) : 'flyPad';
    content.appendChild(h);
    if (page && page !== lastPage) { announce('flyPad page: ' + page); lastPage = page; }

    var items = payload.items || [];
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var el;
      var ct = it.controlType || '';

      if (ct === 'checkbox') {
        var lab = document.createElement('label'); lab.className = 'chk';
        var cb = document.createElement('input'); cb.type = 'checkbox';
        cb.checked = it.value === 'true';
        cb.setAttribute('data-idx', String(it.idx));
        if (it.disabled) cb.disabled = true;
        cb.addEventListener('change', function () {
          post({ type: 'set', idx: this.getAttribute('data-idx'), value: String(this.checked), controlType: 'checkbox' });
        });
        lab.appendChild(cb);
        lab.appendChild(document.createTextNode(' ' + (it.text || '(checkbox)')));
        el = lab;
      } else if (ct === 'select' && it.options && it.options.length) {
        var sl = document.createElement('label'); sl.className = 'fld';
        sl.textContent = (it.text || 'Choice') + ': ';
        var sel = document.createElement('select'); sel.className = 'sel';
        sel.setAttribute('data-idx', String(it.idx));
        sel.setAttribute('aria-label', it.text || 'Choice');
        for (var o = 0; o < it.options.length; o++) {
          var op = document.createElement('option');
          op.value = it.options[o]; op.textContent = it.options[o];
          if (it.options[o] === it.value) op.selected = true;
          sel.appendChild(op);
        }
        sel.addEventListener('change', function () {
          post({ type: 'set', idx: this.getAttribute('data-idx'), value: this.value, controlType: 'select' });
        });
        sl.appendChild(sel); el = sl;
      } else if (ct === 'text' || ct === 'select') {
        var fl = document.createElement('label'); fl.className = 'fld';
        fl.textContent = (it.text || 'Field') + ': ';
        var inp = document.createElement('input'); inp.type = 'text'; inp.className = 'in';
        inp.value = it.value || '';
        inp.setAttribute('data-idx', String(it.idx));
        inp.setAttribute('aria-label', it.text || 'Field');
        inp.addEventListener('change', function () {
          post({ type: 'set', idx: this.getAttribute('data-idx'), value: this.value, controlType: 'text' });
        });
        fl.appendChild(inp); el = fl;
      } else if (it.level >= 1 && it.level <= 6) {
        el = document.createElement('h' + it.level);
        el.textContent = it.text;
        el.setAttribute('data-idx', String(it.idx));
      } else if (it.clickable || it.kind === 'button' || it.kind === 'tab' || it.tag === 'button') {
        el = document.createElement('button'); el.className = 'btn';
        el.textContent = it.text || '(button)';
        el.setAttribute('data-idx', String(it.idx));
        if (it.disabled) { el.setAttribute('aria-disabled', 'true'); }
        el.addEventListener('click', onActivate);
        el.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { onActivate.call(this, e); } });
      } else if (it.kind === 'link' || it.tag === 'a') {
        el = document.createElement('a'); el.className = 'lnk'; el.href = '#';
        el.textContent = it.text || '(link)';
        el.setAttribute('data-idx', String(it.idx));
        if (it.disabled) el.setAttribute('aria-disabled', 'true');
        el.addEventListener('click', function (e) { e.preventDefault(); onActivate.call(this, e); });
      } else {
        el = document.createElement('p'); el.className = 'txt';
        el.textContent = it.text;
        el.setAttribute('data-idx', String(it.idx));
        if (it.live === 'assertive' || it.live === 'polite') el.setAttribute('aria-live', it.live);
      }
      content.appendChild(el);
    }

    // Restore focus.
    if (focusIdx) {
      var tgt = content.querySelector('[data-idx=""' + focusIdx + '""]');
      if (tgt) {
        tgt.focus();
        if (caret !== null && typeof tgt.setSelectionRange === 'function') {
          try { tgt.setSelectionRange(caret, caret); } catch (e) {}
        }
      } else {
        h.focus();
      }
    } else {
      h.focus();
    }
  };

  function onActivate(e) {
    var idx = this.getAttribute('data-idx');
    if (this.getAttribute('aria-disabled') === 'true') { announce('Unavailable'); return; }
    announce('Activating ' + (this.textContent || ''));
    post({ type: 'click', idx: idx });
  }

  document.addEventListener('keydown', function (e) {
    if (e.key === 'F5') { e.preventDefault(); post({ type: 'refresh' }); }
  });
</script>
</body>
</html>";
}

