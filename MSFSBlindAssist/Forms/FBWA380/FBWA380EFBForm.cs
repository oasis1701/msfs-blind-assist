using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible reader for the FlyByWire A380X flyPad EFB (flyPadOS 3).
///
/// Reuses the generic <see cref="EFBBridgeServer"/> infrastructure that PMDG
/// uses: the JS bridge inside the flyPad tablet posts a flat array of
/// "display elements" (one item per visible DOM node — heading, paragraph,
/// button, input, checkbox, select) to <c>/state</c> with type
/// <c>fbwa380_efb_elements</c>; clicks and value sets are sent back as
/// queued commands.
///
/// Why "elements" not page-by-page state: flyPadOS is a React app with many
/// dynamic pages (Dashboard, Dispatch, Ground, Performance, Navigation,
/// ATC, Failures, Checklist, Settings). A generic element list is the only
/// approach that survives layout changes without hardcoded per-page logic.
/// </summary>
public class FBWA380EFBForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeElements = "fbwa380_efb_elements";
    private const string StateTypeConnected = "fbwa380_efb_connected";

    private readonly EFBBridgeServer _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;

    private Label _statusLabel = null!;
    private ListBox _elementsList = null!;
    private Button _refreshBtn = null!;
    private Button _clickBtn = null!;
    private TextBox _valueInput = null!;
    private Button _setValueBtn = null!;
    private Label _pageLabel = null!;

    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _bridgeConnected;
    private List<EFBElement> _elements = new();
    private string _currentPage = "";

    public FBWA380EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
    {
        _bridgeServer = bridgeServer;
        _announcer = announcer;

        InitializeComponent();
        WireEvents();
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
        // Request a fresh snapshot whenever the user opens the form so a
        // newly-launched session doesn't sit on stale cached items.
        _bridgeServer.EnqueueCommand("get_display_elements");
        _elementsList.Focus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "A380X flyPad EFB";
        ClientSize = new Size(720, 600);
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
            AccessibleName = "Current EFB page"
        };
        Controls.Add(_pageLabel);
        y += 26;

        _elementsList = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(700, 420),
            Font = new Font("Segoe UI", 10f),
            AccessibleName = "EFB content",
            AccessibleDescription = "Use arrow keys to read items. Enter to click a button or open an input. F5 to refresh.",
            IntegralHeight = false
        };
        Controls.Add(_elementsList);
        y += 430;

        _refreshBtn = new Button
        {
            Text = "Refresh (F5)",
            Location = new Point(10, y),
            Size = new Size(130, 30),
            AccessibleName = "Refresh EFB content"
        };
        Controls.Add(_refreshBtn);

        _clickBtn = new Button
        {
            Text = "Activate selected (Enter)",
            Location = new Point(150, y),
            Size = new Size(180, 30),
            AccessibleName = "Activate selected element"
        };
        Controls.Add(_clickBtn);

        _valueInput = new TextBox
        {
            Location = new Point(340, y),
            Size = new Size(220, 25),
            AccessibleName = "Value for the selected input or select"
        };
        Controls.Add(_valueInput);

        _setValueBtn = new Button
        {
            Text = "Set value",
            Location = new Point(570, y),
            Size = new Size(120, 30),
            AccessibleName = "Send the value above to the selected input or select"
        };
        Controls.Add(_setValueBtn);

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
        _clickBtn.Click += (_, _) => ClickSelected();
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

        e.Data.TryGetValue("page", out string? page);
        if (page != null) _currentPage = page;

        // The bridge serialises the element array as items[i].field
        // ("items.0.text", "items.0.tag", "items.0.idx", "items.0.value", "items.0.role")
        // because EFBStateUpdateEventArgs is a flat Dictionary<string,string>.
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
                case "text":  el.Text = kv.Value; break;
                case "tag":   el.Tag = kv.Value; break;
                case "role":  el.Role = kv.Value; break;
                case "value": el.Value = kv.Value; break;
                case "type":  el.ControlType = kv.Value; break;
                case "clickable": el.Clickable = kv.Value == "true"; break;
            }
        }
        _elements = byIndex.Values.ToList();

        if (IsHandleCreated) BeginInvoke(RefreshList);
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired = reallyConnected
            ? "EFB bridge: connected"
            : "EFB bridge: not connected — install fbw-a380-bridge.js into the FBW A380X package";
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void RefreshList()
    {
        _pageLabel.Text = $"Page: {(string.IsNullOrEmpty(_currentPage) ? "(unknown)" : _currentPage)}";

        int saved = _elementsList.SelectedIndex;
        _elementsList.BeginUpdate();
        _elementsList.Items.Clear();
        foreach (var el in _elements)
        {
            string prefix = el.ControlType switch
            {
                "text"     => "[Text input] ",
                "checkbox" => $"[Checkbox: {(el.Value == "true" ? "on" : "off")}] ",
                "select"   => $"[Choice: {el.Value}] ",
                _          => el.Clickable || el.Tag == "button" ? "[Button] " :
                              el.Tag is "h1" or "h2" or "h3"     ? "[Heading] " :
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
            ["index"] = el.Index.ToString(),
            ["value"] = _valueInput.Text,
            ["controlType"] = el.ControlType ?? ""
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bridgeServer.StateUpdated -= OnBridgeStateUpdated;
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
