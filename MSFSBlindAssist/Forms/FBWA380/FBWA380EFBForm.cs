using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible reader for the FlyByWire A380X flyPad EFB (flyPadOS 3).
///
/// Single flat layout — no tabs. Tab order (top to bottom):
///   1. Status label   — shows whether the bridge is connected and
///                       whether the EFB tablet is currently powered.
///   2. Page label     — current flyPad route (Dashboard, Ground,
///                       Performance, Navigation, ATC, Failures,
///                       Checklists, Presets, Settings).
///   3. Element list   — every visible interactive element on the
///                       current flyPad page, prefixed with its kind
///                       ([Button], [Input], [Checkbox], [Heading], …).
///                       Enter to activate; F5 to refresh.
///   4. Value input    — type a new value for the selected input /
///                       checkbox / select.
///   5. Set / Refresh  — Set value sends the value above. Refresh
///                       re-pulls the element list from the bridge.
///
/// The flyPad is a React app (react-router-dom MemoryRouter, see
/// Efb.tsx). Activating any "Nav link" element causes a route change
/// and the bridge re-emits the new page's elements automatically; no
/// page-specific code on this side. Page changes are also announced.
/// </summary>
public class FBWA380EFBForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeElements = "fbwa380_efb_elements";
    private const string StateTypeConnected = "fbwa380_efb_connected";

    private readonly EFBBridgeServer _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _statusLabel = null!;
    private Label _pageLabel = null!;
    private ListBox _elementsList = null!;
    private TextBox _valueInput = null!;
    private Button _activateBtn = null!;
    private Button _setValueBtn = null!;
    private Button _refreshBtn = null!;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _bridgeConnected;
    private bool _initialPushReceived;
    private List<EFBElement> _elements = new();
    private string _currentPage = "";
    private string _previousAnnouncedPage = "";

    private System.Windows.Forms.Timer _statusTimer = null!;

    public FBWA380EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
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
        _elementsList.Focus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "A380X flyPad EFB";
        ClientSize = new Size(720, 620);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;

        _statusLabel = new Label
        {
            Text = "EFB bridge: connecting…",
            Location = new Point(10, y),
            Size = new Size(700, 22),
            AccessibleName = "EFB bridge status"
        };
        Controls.Add(_statusLabel);
        y += 26;

        _pageLabel = new Label
        {
            Text = "Page: (unknown)",
            Location = new Point(10, y),
            Size = new Size(700, 22),
            AccessibleName = "Current flyPad page"
        };
        Controls.Add(_pageLabel);
        y += 26;

        _elementsList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(700, 440),
            Font = new Font("Segoe UI", 10f),
            AccessibleName = "EFB content",
            AccessibleDescription = "Every visible item on the current flyPad page. Press Enter to activate the selected item, F5 to refresh.",
            IntegralHeight = false
        };
        Controls.Add(_elementsList);
        y += 450;

        int btnW = 130, btnH = 32, gap = 6, col = 10;
        Button MakeBtn(string text, string accDesc)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(col, y),
                Size = new Size(btnW, btnH),
                AccessibleName = text,
                AccessibleDescription = accDesc
            };
            Controls.Add(b);
            col += btnW + gap;
            return b;
        }

        _activateBtn = MakeBtn("&Activate (Enter)", "Activate the selected element (click button, toggle checkbox, open nav link).");
        _valueInput = new TextBox
        {
            Location = new Point(col, y + 3),
            Size = new Size(260, 25),
            AccessibleName = "Value for the selected input or select"
        };
        Controls.Add(_valueInput);
        col += 270;
        _setValueBtn = MakeBtn("&Set value", "Send the value above to the selected input, checkbox, or select.");
        _refreshBtn  = MakeBtn("&Refresh (F5)", "Re-pull the element list from the bridge.");

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
        _activateBtn.Click += (_, _) => ClickSelected();
        _setValueBtn.Click += (_, _) => SetValueSelected();

        _elementsList.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                _bridgeServer.EnqueueCommand("get_display_elements");
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Return)
            {
                ClickSelected();
                e.Handled = true; e.SuppressKeyPress = true;
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
        if (IsHandleCreated) BeginInvoke(RefreshList);
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired;
        if (reallyConnected && _initialPushReceived)
            desired = $"EFB bridge: connected ({_elements.Count} elements)";
        else if (reallyConnected)
            desired = "EFB bridge: connected — waiting for flyPad content (power on the tablet)…";
        else
            desired = "EFB bridge: not connected — switch aircraft to FBW A380X, accept the install dialog, and restart MSFS.";
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void RefreshList()
    {
        _pageLabel.Text = $"Page: {(string.IsNullOrEmpty(_currentPage) ? "(unknown)" : _currentPage)}";
        if (!string.IsNullOrEmpty(_currentPage) && _currentPage != _previousAnnouncedPage)
        {
            _announcer.Announce("flyPad page: " + _currentPage);
            _previousAnnouncedPage = _currentPage;
        }

        int saved = _elementsList.SelectedIndex;
        _elementsList.BeginUpdate();
        _elementsList.Items.Clear();
        foreach (var el in _elements)
        {
            string prefix = el.ControlType switch
            {
                "text"     => "[Input] ",
                "checkbox" => $"[Checkbox {(el.Value == "true" ? "on" : "off")}] ",
                "select"   => $"[Choice: {el.Value}] ",
                _          => el.Clickable || el.Tag == "button" ? "[Button] " :
                              el.Tag is "h1" or "h2" or "h3" or "h4" ? "[Heading] " :
                                                                       ""
            };
            _elementsList.Items.Add(prefix + el.Text);
        }
        if (saved >= 0 && saved < _elementsList.Items.Count)
            _elementsList.SelectedIndex = saved;
        _elementsList.EndUpdate();
        UpdateStatusLabel();
    }

    private EFBElement? SelectedElement()
    {
        int idx = _elementsList.SelectedIndex;
        if (idx < 0 || idx >= _elements.Count) return null;
        return _elements[idx];
    }

    private void ClickSelected()
    {
        var el = SelectedElement();
        if (el == null)
        {
            _announcer.AnnounceImmediate("No element selected");
            return;
        }
        _bridgeServer.EnqueueCommand("click_display_element",
            new Dictionary<string, string> { ["index"] = el.Index.ToString() });
    }

    private void SetValueSelected()
    {
        var el = SelectedElement();
        if (el == null)
        {
            _announcer.AnnounceImmediate("No element selected");
            return;
        }
        _bridgeServer.EnqueueCommand("set_element_value", new Dictionary<string, string>
        {
            ["index"]       = el.Index.ToString(),
            ["value"]       = _valueInput.Text,
            ["controlType"] = el.ControlType ?? ""
        });
        _announcer.Announce("Set " + el.Text + " to " + _valueInput.Text);
        _valueInput.Text = "";
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
