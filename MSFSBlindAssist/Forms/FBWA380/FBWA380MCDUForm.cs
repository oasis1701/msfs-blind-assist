using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible MCDU window for the FlyByWire A380X (development build).
///
/// Two complementary views, one per tab:
///
///   Screen tab  — live text scrape of the active MFD's FMS page, plus
///                 a scratchpad text input that fires KCCU keys + ENT.
///                 Read-only view of what the cockpit MFD is showing,
///                 with a free-text entry that types into whichever field
///                 is currently selected on the page (same as a sighted
///                 pilot would do via the KCCU keyboard).
///
///   Page fields — enumerated list of every interactive element on the
///                 current FMS page (input fields, buttons, dropdowns,
///                 menu items, header tabs). Click any item to activate
///                 it; set a value on any input field and the bridge
///                 atomically clicks it, clears it, types the new value,
///                 and confirms with ENT — same end-state as doing it
///                 manually with the cockpit cursor + keyboard.
///
/// Bottom: hardcoded KCCU page buttons (FPLN/PERF/INIT/…). These mirror
/// the physical KCCU keyboard, which has the same fixed keys regardless
/// of the page being shown.
///
/// Wire protocol — see Resources/fbw-a380-bridge.js. State pushes:
///   fbwa380_mcdu_screen    { mcdu, rowCount, row0..row23 }
///   fbwa380_mcdu_elements  { mcdu, count, items.N.text/kind/value/disabled }
///   fbwa380_mcdu_connected (heartbeat marker)
/// Commands sent:
///   page_*  / key_*  / lsk_*  / type_key  / select_mcdu
///   get_mcdu_elements  / click_mcdu_element  / set_mcdu_element_value
/// </summary>
public class FBWA380MCDUForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeScreen = "fbwa380_mcdu_screen";
    private const string StateTypeElements = "fbwa380_mcdu_elements";
    private const string StateTypeConnected = "fbwa380_mcdu_connected";

    private readonly EFBBridgeServer _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;

    private TabControl _tabs = null!;
    private TabPage _tabScreen = null!;
    private TabPage _tabFields = null!;

    private ListBox _mcduDisplay = null!;
    private TextBox _scratchpad = null!;
    private Label _statusLabel = null!;
    private ComboBox _mcduSelector = null!;

    private ListBox _fieldsList = null!;
    private Button _activateBtn = null!;
    private TextBox _valueInput = null!;
    private Button _setValueBtn = null!;
    private Button _refreshFieldsBtn = null!;
    private Label _fieldDetailLabel = null!;

    // Page navigation buttons mirror the physical KCCU keyboard layout.
    private Button _btnInit = null!;
    private Button _btnData = null!;
    private Button _btnFPln = null!;
    private Button _btnPerf = null!;
    private Button _btnRadNav = null!;
    private Button _btnFuel = null!;
    private Button _btnSecFPln = null!;
    private Button _btnAtc = null!;
    private Button _btnMenu = null!;
    private Button _btnAirport = null!;
    private Button _btnDir = null!;
    private Button _btnOverfly = null!;
    private Button _btnNextPage = null!;
    private Button _btnPrevPage = null!;
    private Button _btnExec = null!;
    private Button _btnClr = null!;

    private string[] _rows = Array.Empty<string>();
    private List<McduElement> _elements = new();

    private string _previousTitle = "";
    private string? _previousScratchpad;
    private bool _typingInProgress;
    private bool _clearingInProgress;
    private int _clearingWatchdog;
    private bool _bridgeConnected;
    private int _mcduIndex = 1; // 1=CAPT, 2=FO

    private IntPtr _previousWindow = IntPtr.Zero;
    private System.Windows.Forms.Timer _statusTimer = null!;

    public FBWA380MCDUForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
    {
        _bridgeServer = bridgeServer;
        _announcer = announcer;

        InitializeComponent();
        SetupEventHandlers();

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
        _bridgeServer.EnqueueCommand("get_mcdu_elements");
        _mcduDisplay.Focus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "A380X MCDU (Captain)";
        ClientSize = new Size(660, 760);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;

        _statusLabel = new Label
        {
            Text = "MCDU bridge: connecting…",
            Location = new Point(10, y),
            Size = new Size(420, 22),
            AccessibleName = "MCDU bridge status"
        };
        Controls.Add(_statusLabel);

        _mcduSelector = new ComboBox
        {
            Location = new Point(440, y),
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU selector"
        };
        _mcduSelector.Items.AddRange(new object[] { "Captain (1)", "First Officer (2)" });
        _mcduSelector.SelectedIndex = 0;
        Controls.Add(_mcduSelector);
        y += 30;

        _tabs = new TabControl
        {
            Location = new Point(10, y),
            Size = new Size(630, 480),
            AccessibleName = "MCDU view selector"
        };
        Controls.Add(_tabs);

        // --- Screen tab ---------------------------------------------------
        _tabScreen = new TabPage("&Screen") { AccessibleName = "MCDU screen content" };
        _tabs.TabPages.Add(_tabScreen);

        _mcduDisplay = new ListBox
        {
            Location = new Point(5, 5),
            Size = new Size(610, 380),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU display",
            AccessibleDescription = "Live FMS page content. Use arrow keys to read each line.",
            IntegralHeight = false
        };
        _tabScreen.Controls.Add(_mcduDisplay);

        _scratchpad = new TextBox
        {
            Location = new Point(5, 395),
            Size = new Size(610, 25),
            AccessibleName = "MCDU scratchpad input",
            AccessibleDescription = "Type and press Enter to send to the active MFD field. Backspace removes one character."
        };
        _tabScreen.Controls.Add(_scratchpad);

        // --- Page fields tab ---------------------------------------------
        _tabFields = new TabPage("Page &fields") { AccessibleName = "Interactive MCDU page elements" };
        _tabs.TabPages.Add(_tabFields);

        _fieldsList = new ListBox
        {
            Location = new Point(5, 5),
            Size = new Size(610, 300),
            Font = new Font("Segoe UI", 10f),
            AccessibleName = "Interactive page elements",
            AccessibleDescription = "Every input, button, dropdown, and tab on the current MFD page. Enter to activate. F5 to refresh.",
            IntegralHeight = false
        };
        _tabFields.Controls.Add(_fieldsList);

        _fieldDetailLabel = new Label
        {
            Location = new Point(5, 310),
            Size = new Size(610, 22),
            AccessibleName = "Selected element details",
            Text = "Select an element to see its current value."
        };
        _tabFields.Controls.Add(_fieldDetailLabel);

        _activateBtn = new Button
        {
            Text = "&Activate (Enter)",
            Location = new Point(5, 340),
            Size = new Size(140, 30),
            AccessibleName = "Activate the selected element"
        };
        _tabFields.Controls.Add(_activateBtn);

        _valueInput = new TextBox
        {
            Location = new Point(155, 343),
            Size = new Size(260, 25),
            AccessibleName = "New value for the selected input field"
        };
        _tabFields.Controls.Add(_valueInput);

        _setValueBtn = new Button
        {
            Text = "Set &value",
            Location = new Point(425, 340),
            Size = new Size(100, 30),
            AccessibleName = "Send the value above to the selected input field"
        };
        _tabFields.Controls.Add(_setValueBtn);

        _refreshFieldsBtn = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(535, 340),
            Size = new Size(80, 30),
            AccessibleName = "Refresh the list of page elements"
        };
        _tabFields.Controls.Add(_refreshFieldsBtn);

        // --- KCCU page buttons (below tab control) ----------------------
        int btnY = y + _tabs.Height + 10;
        int btnW = 100, btnH = 28, gap = 4, col = 10;
        Button MakeBtn(string text, string accDesc)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(col, btnY),
                Size = new Size(btnW, btnH),
                AccessibleName = text,
                AccessibleDescription = accDesc
            };
            Controls.Add(b);
            col += btnW + gap;
            return b;
        }

        _btnInit    = MakeBtn("INIT",      "KCCU INIT key");
        _btnFPln    = MakeBtn("F-PLN",     "KCCU FPLN key");
        _btnPerf    = MakeBtn("PERF",      "KCCU PERF key");
        _btnRadNav  = MakeBtn("RAD NAV",   "KCCU NAVAID key");
        _btnFuel    = MakeBtn("FUEL",      "Open ACTIVE → FUEL&LOAD via FPLN key + header click");
        _btnSecFPln = MakeBtn("SEC F-PLN", "KCCU SECINDEX key");
        btnY += btnH + gap; col = 10;
        _btnAtc     = MakeBtn("ATC COM",   "KCCU ATCCOM key");
        _btnMenu    = MakeBtn("MCDU MENU", "Open MCDU MENU page (no dedicated KCCU key)");
        _btnAirport = MakeBtn("AIRPORT",   "Insert nearest airport — uses DIR + header navigation");
        _btnData    = MakeBtn("DATA",      "Open DATA page from header dropdown");
        _btnDir     = MakeBtn("DIR",       "KCCU DIR key");
        _btnOverfly = MakeBtn("OVFY",      "Toggle overfly on active waypoint");
        btnY += btnH + gap; col = 10;
        _btnPrevPage = MakeBtn("UP",        "KCCU UP — previous page / scroll up");
        _btnNextPage = MakeBtn("DOWN",      "KCCU DOWN — next page / scroll down");
        _btnExec     = MakeBtn("ENT",       "KCCU ENT — confirm pending entry");
        _btnClr      = MakeBtn("CLR",       "KCCU BACKSPACE — clear scratchpad / delete a field");

        ResumeLayout(true);
    }

    private void SetupEventHandlers()
    {
        Load += (_, _) => _mcduDisplay.Focus();

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };

        _btnInit.Click    += (_, _) => SendCommand("page_init");
        _btnData.Click    += (_, _) => SendCommand("page_data");
        _btnFPln.Click    += (_, _) => SendCommand("page_fpln");
        _btnPerf.Click    += (_, _) => SendCommand("page_perf");
        _btnRadNav.Click  += (_, _) => SendCommand("page_radnav");
        _btnFuel.Click    += (_, _) => SendCommand("page_fuel");
        _btnSecFPln.Click += (_, _) => SendCommand("page_sec_fpln");
        _btnAtc.Click     += (_, _) => SendCommand("page_atc");
        _btnMenu.Click    += (_, _) => SendCommand("page_menu");
        _btnAirport.Click += (_, _) => SendCommand("page_airport");
        _btnDir.Click     += (_, _) => SendCommand("page_dir");
        _btnOverfly.Click += (_, _) => SendCommand("page_overfly");
        _btnPrevPage.Click += (_, _) => SendCommand("key_prev_page");
        _btnNextPage.Click += (_, _) => SendCommand("key_next_page");
        _btnExec.Click    += (_, _) => SendCommand("key_exec");
        _btnClr.Click     += (_, _) => ClearOrDelete();

        _mcduSelector.SelectedIndexChanged += (_, _) =>
        {
            _mcduIndex = _mcduSelector.SelectedIndex + 1;
            Text = $"A380X MCDU ({_mcduSelector.SelectedItem})";
            _bridgeServer.EnqueueCommand("select_mcdu",
                new Dictionary<string, string> { ["mcdu"] = _mcduIndex.ToString() });
            _bridgeServer.EnqueueCommand("get_mcdu_elements");
        };

        _scratchpad.KeyDown += ScratchpadKeyDown;
        KeyDown += FormKeyDown;

        // Page fields tab interactions.
        _activateBtn.Click += (_, _) => ClickSelectedField();
        _setValueBtn.Click += (_, _) => SetSelectedFieldValue();
        _refreshFieldsBtn.Click += (_, _) => _bridgeServer.EnqueueCommand("get_mcdu_elements");
        _fieldsList.SelectedIndexChanged += (_, _) => UpdateFieldDetailLabel();
        _fieldsList.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                _bridgeServer.EnqueueCommand("get_mcdu_elements");
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Return)
            {
                ClickSelectedField();
                e.Handled = true; e.SuppressKeyPress = true;
            }
        };
        // Refresh element list whenever the user lands on the fields tab.
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == _tabFields)
                _bridgeServer.EnqueueCommand("get_mcdu_elements");
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
        if (e.Type == StateTypeScreen) { HandleScreenPush(e); return; }
        if (e.Type == StateTypeElements) { HandleElementsPush(e); return; }
    }

    private void HandleScreenPush(EFBStateUpdateEventArgs e)
    {
        _bridgeConnected = true;
        // The bridge now emits a variable number of rows per push, sized
        // to whatever the active page's content needs. Row 0 is the
        // title line, last row is the scratchpad / footer message;
        // everything between is numbered body content laid out on a
        // GRID_WIDTH-char monospace grid (see fbw-a380-bridge.js).
        if (!e.Data.TryGetValue("rowCount", out string? rcs) || !int.TryParse(rcs, out int rowCount))
            rowCount = 0;
        if (rowCount <= 0) { _rows = Array.Empty<string>(); }
        else
        {
            var newRows = new string[rowCount];
            for (int i = 0; i < rowCount; i++)
                newRows[i] = e.Data.TryGetValue($"row{i}", out string? v) ? v ?? "" : "";
            _rows = newRows;
        }
        if (IsHandleCreated) BeginInvoke(RefreshScreen);
    }

    private void HandleElementsPush(EFBStateUpdateEventArgs e)
    {
        _bridgeConnected = true;

        var byIndex = new SortedDictionary<int, McduElement>();
        foreach (var kv in e.Data)
        {
            if (!kv.Key.StartsWith("items.")) continue;
            var parts = kv.Key.Split('.');
            if (parts.Length != 3) continue;
            if (!int.TryParse(parts[1], out int idx)) continue;
            if (!byIndex.TryGetValue(idx, out var el))
            {
                el = new McduElement { Index = idx };
                byIndex[idx] = el;
            }
            switch (parts[2])
            {
                case "text":     el.Text = kv.Value; break;
                case "kind":     el.Kind = kv.Value; break;
                case "value":    el.Value = kv.Value; break;
                case "disabled": el.Disabled = kv.Value == "true"; break;
            }
        }
        _elements = byIndex.Values.ToList();
        if (IsHandleCreated) BeginInvoke(RefreshFieldsList);
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired = reallyConnected
            ? $"MCDU bridge: connected (MCDU {_mcduIndex})"
            : "MCDU bridge: not connected — install the FBW A380 bridge overlay from the File menu";
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void RefreshScreen()
    {
        // The bridge already produces a fully laid-out grid in Fenix-style
        // semantics: row 0 is "Title: …", the last row is "Scratchpad: …",
        // body rows are numbered "N: …" with monospace-padded content.
        // We render rows as-is so the Consolas font preserves the spatial
        // layout (label on the left, value on the right, etc.).
        if (_rows.Length == 0) return;

        var lines = new List<string>(_rows.Length);
        string scratchpad = "";
        for (int i = 0; i < _rows.Length; i++)
        {
            string row = _rows[i] ?? "";
            // Keep title and scratchpad rows verbatim; drop empty body rows.
            bool isLast = i == _rows.Length - 1;
            bool isTitle = i == 0;
            if (!isTitle && !isLast && string.IsNullOrWhiteSpace(row)) continue;
            if (isLast)
            {
                // Pull just the value half of "Scratchpad: ..." so the
                // change-detection logic below works on the underlying
                // text rather than the literal prefix.
                const string prefix = "Scratchpad:";
                int p = row.IndexOf(prefix, StringComparison.Ordinal);
                scratchpad = p >= 0 ? row.Substring(p + prefix.Length).Trim() : row.Trim();
            }
            lines.Add(row);
        }

        int saved = _mcduDisplay.SelectedIndex;
        _mcduDisplay.BeginUpdate();
        while (_mcduDisplay.Items.Count > lines.Count) _mcduDisplay.Items.RemoveAt(_mcduDisplay.Items.Count - 1);
        while (_mcduDisplay.Items.Count < lines.Count) _mcduDisplay.Items.Add("");
        for (int i = 0; i < lines.Count; i++)
            if (_mcduDisplay.Items[i]?.ToString() != lines[i])
                _mcduDisplay.Items[i] = lines[i];
        _mcduDisplay.EndUpdate();

        // Extract just the page name out of "Title: ..." for change
        // detection and screen-reader announcement.
        string title = _rows.Length > 0 ? _rows[0] : "";
        const string titlePrefix = "Title:";
        int titleIdx = title.IndexOf(titlePrefix, StringComparison.Ordinal);
        if (titleIdx >= 0) title = title.Substring(titleIdx + titlePrefix.Length).Trim();
        else title = title.Trim();
        if (!string.IsNullOrWhiteSpace(title) && title != _previousTitle)
        {
            _announcer.Announce(title);
            _previousTitle = title;
            if (_mcduDisplay.Items.Count > 0) _mcduDisplay.SelectedIndex = 0;
        }
        else if (saved >= 0 && saved < _mcduDisplay.Items.Count)
        {
            _mcduDisplay.SelectedIndex = saved;
        }

        // Scratchpad change announcement.
        bool firstPush = _previousScratchpad == null;
        if (firstPush) { _previousScratchpad = scratchpad; }
        else if (scratchpad != _previousScratchpad && !_typingInProgress)
        {
            if (_clearingInProgress)
            {
                _clearingWatchdog++;
                if (string.IsNullOrWhiteSpace(scratchpad))
                {
                    _clearingInProgress = false;
                    _announcer.Announce("Cleared");
                    _previousScratchpad = scratchpad;
                }
                else if (_clearingWatchdog > 3)
                {
                    _clearingInProgress = false;
                    _announcer.Announce(scratchpad);
                    _previousScratchpad = scratchpad;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(scratchpad))
                    _announcer.Announce(scratchpad);
                _previousScratchpad = scratchpad;
            }
        }

        UpdateStatusLabel();
    }

    private void RefreshFieldsList()
    {
        int saved = _fieldsList.SelectedIndex;
        _fieldsList.BeginUpdate();
        _fieldsList.Items.Clear();
        foreach (var el in _elements)
        {
            string prefix = el.Kind switch
            {
                "input"    => "[Input] ",
                "button"   => "[Button] ",
                "icon"     => "[Icon] ",
                "dropdown" => "[Dropdown] ",
                "menu"     => "[Menu item] ",
                "tab"      => "[Tab] ",
                _          => ""
            };
            string suffix = el.Disabled ? " (disabled)" : "";
            _fieldsList.Items.Add(prefix + el.Text + suffix);
        }
        if (saved >= 0 && saved < _fieldsList.Items.Count)
            _fieldsList.SelectedIndex = saved;
        _fieldsList.EndUpdate();
        UpdateFieldDetailLabel();
    }

    private void UpdateFieldDetailLabel()
    {
        var el = SelectedField();
        if (el == null)
        {
            _fieldDetailLabel.Text = $"{_elements.Count} interactive element(s) on this page.";
            return;
        }
        if (el.Kind == "input")
            _fieldDetailLabel.Text = $"Current value: \"{el.Value}\". Type a new value above and press Set value.";
        else if (el.Kind == "dropdown")
            _fieldDetailLabel.Text = $"Current selection: \"{el.Value}\". Press Activate to open the menu.";
        else
            _fieldDetailLabel.Text = el.Disabled ? "Disabled." : "Press Activate (Enter) to click.";
    }

    private McduElement? SelectedField()
    {
        int idx = _fieldsList.SelectedIndex;
        if (idx < 0 || idx >= _elements.Count) return null;
        return _elements[idx];
    }

    private void ClickSelectedField()
    {
        var el = SelectedField();
        if (el == null) { _announcer.AnnounceImmediate("No element selected"); return; }
        if (el.Disabled)  { _announcer.AnnounceImmediate("Element is disabled"); return; }
        _bridgeServer.EnqueueCommand("click_mcdu_element",
            new Dictionary<string, string> { ["index"] = el.Index.ToString() });
    }

    private void SetSelectedFieldValue()
    {
        var el = SelectedField();
        if (el == null) { _announcer.AnnounceImmediate("No element selected"); return; }
        if (el.Disabled)  { _announcer.AnnounceImmediate("Element is disabled"); return; }
        if (el.Kind != "input")
        {
            _announcer.AnnounceImmediate("This element does not accept a value. Use Activate instead.");
            return;
        }
        _bridgeServer.EnqueueCommand("set_mcdu_element_value", new Dictionary<string, string>
        {
            ["index"] = el.Index.ToString(),
            ["value"] = _valueInput.Text
        });
        _valueInput.Text = "";
    }

    private void SendCommand(string command) => _bridgeServer.EnqueueCommand(command);
    private void SendLskLeft(int n)  => _bridgeServer.EnqueueCommand($"lsk_L{n}");
    private void SendLskRight(int n) => _bridgeServer.EnqueueCommand($"lsk_R{n}");

    private void SendTypeKey(string key) =>
        _bridgeServer.EnqueueCommand("type_key", new Dictionary<string, string> { ["key"] = key });

    private void ClearOrDelete()
    {
        if (!string.IsNullOrWhiteSpace(_previousScratchpad))
        {
            _clearingInProgress = true; _clearingWatchdog = 0;
            SendTypeKey("CLR");
        }
        else
        {
            SendTypeKey("DEL");
        }
    }

    private void FormKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+1-6 / Alt+1-6 chord — accepted for parity with other CDU
        // forms even though the A380 KCCU has no LSKs; the bridge maps
        // them to the cursor + ENT pattern (placeholder no-op for now).
        if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            SendLskLeft(e.KeyCode - Keys.D1 + 1);
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            SendLskRight(e.KeyCode - Keys.D1 + 1);
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.KeyCode == Keys.PageUp || (e.Alt && e.KeyCode == Keys.Up))
        {
            SendCommand("key_prev_page");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.KeyCode == Keys.PageDown || (e.Alt && e.KeyCode == Keys.Down))
        {
            SendCommand("key_next_page");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.Control && e.KeyCode == Keys.Return)
        {
            SendCommand("key_exec");
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private void ScratchpadKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = _scratchpad.Text;
            if (!string.IsNullOrEmpty(text))
            {
                _typingInProgress = true;
                foreach (char c in text)
                {
                    string mapped = MapScratchpadChar(c);
                    if (!string.IsNullOrEmpty(mapped)) SendTypeKey(mapped);
                }
                _scratchpad.Text = "";
                _typingInProgress = false;
            }
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Back && string.IsNullOrEmpty(_scratchpad.Text))
        {
            ClearOrDelete();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private static string MapScratchpadChar(char c)
    {
        if (c >= 'A' && c <= 'Z') return c.ToString();
        if (c >= 'a' && c <= 'z') return char.ToUpper(c).ToString();
        if (c >= '0' && c <= '9') return c.ToString();
        return c switch
        {
            '.' => "DOT",
            '/' => "SLASH",
            '+' => "PLUSMINUS",
            '-' => "PLUSMINUS",
            ' ' => "SP",
            _   => "",
        };
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

    private class McduElement
    {
        public int Index;
        public string Text = "";
        public string Kind = "";
        public string Value = "";
        public bool Disabled;
    }
}
