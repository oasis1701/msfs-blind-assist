using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible reader for the FlyByWire A380X flyPad EFB (flyPadOS 3).
///
/// DESIGN — why native controls, not PMDG-style bespoke tabs
/// ---------------------------------------------------------
/// The PMDG 777 EFB form hand-builds a dedicated panel for each known
/// EFB app (Performance, Ground Ops, …) wired to specific stable DOM
/// ids. That only works because PMDG ships a documented, slow-changing
/// tablet. The A380X flyPad is a React app on a *development* build:
/// its DOM shifts between FBW updates and exposes no stable ids to
/// hard-wire. So this form stays dynamic — the bridge enumerates every
/// visible element on the current flyPad page — but each element is
/// rendered as a REAL native Windows control:
///
///   • input[type=text] / &lt;select&gt;  → labelled TextBox (type, Enter to set)
///   • input[type=checkbox|radio]     → CheckBox (Space toggles)
///   • button / link / clickable      → Button (Enter/Space activates)
///   • h1–h4                          → bold heading Label
///   • plain text                     → read-only Label (context)
///
/// Screen readers then announce the correct role and state for every
/// item automatically, with no redundant app-side speech. The flyPad
/// is a react-router MemoryRouter: activating a nav link changes route
/// and the bridge re-emits the new page's elements; page changes are
/// announced.
///
/// Layout (top to bottom): status label, page label, scrollable content
/// panel of native controls, Refresh button. F5 refreshes from anywhere.
/// </summary>
public class FBWA380EFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeElements = "fbwa380_efb_elements";
    private const string StateTypeConnected = "fbwa380_efb_connected";

    private readonly IMcduBridge _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _statusLabel = null!;
    private Label _pageLabel = null!;
    private Panel _contentPanel = null!;
    private Button _refreshBtn = null!;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _bridgeConnected;
    private bool _initialPushReceived;
    private List<EFBElement> _elements = new();
    private string _currentPage = "";
    private string _previousAnnouncedPage = "";

    // Maps a flyPad element index to the native control rendering it, so
    // value-only refreshes can update state in place without rebuilding
    // (which would steal focus from a screen-reader user mid-interaction).
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
        FocusFirstControl();
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

        _contentPanel = new Panel
        {
            Location = new Point(10, 62),
            Size = new Size(700, 506),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "flyPad page content",
            AccessibleDescription = "Interactive controls for the current flyPad page. Tab between them; F5 refreshes."
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
                case "text":      el.Text = kv.Value; break;
                case "tag":       el.Tag = kv.Value; break;
                case "role":      el.Role = kv.Value; break;
                case "value":     el.Value = kv.Value; break;
                case "type":      el.ControlType = kv.Value; break;
                case "clickable": el.Clickable = kv.Value == "true"; break;
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

    /// <summary>
    /// Either rebuilds the content panel (structure changed) or updates
    /// control state in place (only values changed). In-place updates keep
    /// keyboard focus where it is so a screen-reader user is not yanked
    /// back to the top of the panel every poll.
    /// </summary>
    private void ApplyElements()
    {
        _pageLabel.Text = $"Page: {(string.IsNullOrEmpty(_currentPage) ? "(unknown)" : _currentPage)}";
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

        UpdateStatusLabel();
    }

    private string StructureSignature()
    {
        // Everything that affects which/how controls are created — but NOT
        // their current value (a value change should update in place).
        return _currentPage + "" + string.Join("",
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
                    // Don't clobber what the user is currently typing.
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
        int innerWidth = _contentPanel.ClientSize.Width - 24; // leave room for scrollbar
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
        // Checkbox / radio → real CheckBox.
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

        // Text input or <select> → labelled TextBox (type, Enter to set).
        // The bridge only reports a select's current value (not its option
        // list), so a free-text field is the most reliable accessible editor.
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

        // Headings → bold label.
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

        // Buttons / links / anything clickable → real Button.
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
                // flyPad nav (links/buttons) changes the page; force a fresh scrape
                // shortly after so the new content + page label appear promptly
                // (and the page-change announcement fires) instead of waiting for
                // the next routine poll — that lag read as "clicking did nothing".
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

        // Plain text → read-only context label.
        return new Label
        {
            Text = el.Text,
            AutoSize = false,
            Size = new Size(width, 22),
            AccessibleName = el.Text,
            AccessibleRole = AccessibleRole.StaticText
        };
    }

    private void FocusFirstControl()
    {
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
        }
        base.Dispose(disposing);
    }

    private class RenderedControl
    {
        public Control Control = null!;
    }

    private class EFBElement
    {
        public int Index;
        public string Text = "";
        public string Tag = "";
        public string Role = "";
        public string Value = "";
        public string? ControlType;
        public bool Clickable;
    }
}
