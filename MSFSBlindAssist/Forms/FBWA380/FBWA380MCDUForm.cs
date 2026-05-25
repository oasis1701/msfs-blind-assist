using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible MCDU display for the FlyByWire A380X (development build).
///
/// Mirrors the structure of <c>HS787FMCForm</c> (Horizon 787 reference) and
/// the visual organisation of <c>FenixMCDUForm</c> (Fenix A320 reference)
/// but talks to the standard MSFSBA <see cref="EFBBridgeServer"/>.
///
/// Wire protocol (state push from sim → MSFSBA):
///   { type: "fbwa380_mcdu_screen",
///     data: { rowCount: "14",
///             row0..row12: "&lt;line text&gt;",
///             row13: "&lt;scratchpad&gt;",
///             mcdu: "1" | "2" | "3" } }
///
/// Commands enqueued back (MSFSBA → sim, GET /commands):
///   page_init / page_data / page_dir / page_fpln / page_perf
///   page_radnav / page_fuel / page_sec_fpln / page_atc / page_menu
///   page_airport / page_overfly
///   key_next_page / key_prev_page / key_exec
///   lsk_L1..lsk_L6 / lsk_R1..lsk_R6
///   type_key:&lt;CHAR&gt;   (digit / letter / dot / slash / plus-minus / space / clr / ovfy)
///
/// The actual DOM-reading + key-press JS lives in
/// <c>Resources/fbw-a380-bridge.js</c>; selectors there will need adjustment
/// in-sim against the running FBW A380X MCDU markup.
/// </summary>
public class FBWA380MCDUForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const string StateTypeScreen = "fbwa380_mcdu_screen";
    private const string StateTypeConnected = "fbwa380_mcdu_connected";

    private readonly EFBBridgeServer _bridgeServer;
    private readonly ScreenReaderAnnouncer _announcer;

    private ListBox _mcduDisplay = null!;
    private TextBox _scratchpad = null!;
    private Label _statusLabel = null!;
    private ComboBox _mcduSelector = null!;

    // Page navigation buttons. A380 MCDU page set per FBW dev build —
    // names from https://docs.flybywiresim.com/aircraft/a380x/ and the
    // open-source aircraft repo (a380x/src/systems/instruments/src/MCDU).
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

    private string[] _rows = new string[14];
    private string _previousTitle = "";
    private string? _previousScratchpad;
    private bool _typingInProgress;
    private bool _clearingInProgress;
    private int _clearingWatchdog;
    private bool _bridgeConnected;
    private int _mcduIndex = 1; // 1=Captain, 2=F/O, 3=Standby (A380 has three)

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

        // One subscription for the form's whole lifetime; we hide on close
        // instead of dispose, so the live screen feed survives reopen.
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
        _mcduDisplay.Focus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "A380X MCDU (Captain)";
        ClientSize = new Size(620, 720);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;

        _statusLabel = new Label
        {
            Text = "MCDU bridge: connecting…",
            Location = new Point(10, y),
            Size = new Size(400, 22),
            AccessibleName = "MCDU bridge status"
        };
        Controls.Add(_statusLabel);

        _mcduSelector = new ComboBox
        {
            Location = new Point(420, y),
            Size = new Size(190, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU selector"
        };
        _mcduSelector.Items.AddRange(new object[] { "Captain (1)", "First Officer (2)", "Standby (3)" });
        _mcduSelector.SelectedIndex = 0;
        Controls.Add(_mcduSelector);
        y += 30;

        _mcduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 280),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU display",
            AccessibleDescription = "Use arrow keys to read lines. Ctrl+1-6 = left line-select keys, Alt+1-6 = right line-select keys.",
            IntegralHeight = false
        };
        Controls.Add(_mcduDisplay);
        y += 290;

        _scratchpad = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 25),
            AccessibleName = "MCDU scratchpad input",
            AccessibleDescription = "Type and press Enter to send to the MCDU scratchpad. Backspace removes the last character."
        };
        Controls.Add(_scratchpad);
        y += 35;

        int btnW = 95, btnH = 28, gap = 4, col = 10;
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

        _btnInit    = MakeBtn("INIT",      "Open the INIT page");
        _btnFPln    = MakeBtn("F-PLN",     "Open the active flight plan page");
        _btnPerf    = MakeBtn("PERF",      "Open the performance page");
        _btnRadNav  = MakeBtn("RAD NAV",   "Open the radio navigation page");
        _btnFuel    = MakeBtn("FUEL",      "Open the fuel and load page");
        _btnSecFPln = MakeBtn("SEC F-PLN", "Open the secondary flight plan page");
        y += btnH + gap;
        col = 10;
        _btnAtc     = MakeBtn("ATC COM",   "Open the ATC communications page");
        _btnMenu    = MakeBtn("MCDU MENU", "Open the MCDU menu page");
        _btnAirport = MakeBtn("AIRPORT",   "Insert nearest airport into the flight plan");
        _btnData    = MakeBtn("DATA",      "Open the data index page");
        _btnDir     = MakeBtn("DIR",       "Open the direct-to page");
        _btnOverfly = MakeBtn("OVFY",      "Insert an overfly marker on the active waypoint");
        y += btnH + gap;
        col = 10;
        _btnPrevPage = MakeBtn("PREV PAGE", "Previous page (PageUp)");
        _btnNextPage = MakeBtn("NEXT PAGE", "Next page (PageDown)");
        _btnExec     = MakeBtn("EXEC",      "Activate pending entry (Ctrl+Enter)");
        _btnClr      = MakeBtn("CLR / DEL", "Clear scratchpad or delete a field");

        ResumeLayout(true);
    }

    private void SetupEventHandlers()
    {
        Load += (_, _) => _mcduDisplay.Focus();

        FormClosing += (s, e) =>
        {
            // Hide instead of dispose — bridge subscription survives so the
            // display is up-to-date the next time the form is reopened.
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
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
        };

        _scratchpad.KeyDown += ScratchpadKeyDown;
        KeyDown += FormKeyDown;
    }

    private void OnBridgeStateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (e.Type == StateTypeConnected)
        {
            _bridgeConnected = true;
            if (IsHandleCreated) BeginInvoke(UpdateStatusLabel);
            return;
        }
        if (e.Type != StateTypeScreen) return;

        _bridgeConnected = true;

        int rowCount = 14;
        if (e.Data.TryGetValue("rowCount", out string? rcs) && int.TryParse(rcs, out int rc))
            rowCount = Math.Max(rc, 14);

        var newRows = new string[rowCount];
        for (int i = 0; i < rowCount; i++)
            newRows[i] = e.Data.TryGetValue($"row{i}", out string? v) ? v ?? "" : "";

        _rows = newRows;
        if (IsHandleCreated) BeginInvoke(RefreshDisplay);
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired = reallyConnected
            ? $"MCDU bridge: connected (MCDU {_mcduIndex})"
            : "MCDU bridge: not connected — install fbw-a380-bridge.js into the FBW A380X package";
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void RefreshDisplay()
    {
        // Row layout follows the A380 MCDU's 14-row convention used by FBW
        // and mirrored by the JS bridge: row0 = title; rows 1+2 / 3+4 / …
        // = label/data pairs for the six line-select positions; row 13 =
        // scratchpad. The same layout is used by the 787 reference form.
        if (_rows.Length < 14) return;

        var lines = new List<string>();
        lines.Add(_rows[0]); // title

        for (int pair = 0; pair < 6; pair++)
        {
            int labelIdx = 1 + pair * 2;
            int dataIdx  = 2 + pair * 2;
            string label = _rows[labelIdx];
            string data  = _rows[dataIdx];
            int n = pair + 1;

            if (!string.IsNullOrWhiteSpace(label))
                lines.Add($"   {label}");
            lines.Add(string.IsNullOrWhiteSpace(data) ? $"{n}:" : $"{n}: {data}");
        }

        string scratchpad = _rows[13];
        lines.Add(scratchpad);

        int saved = _mcduDisplay.SelectedIndex;
        _mcduDisplay.BeginUpdate();
        while (_mcduDisplay.Items.Count > lines.Count) _mcduDisplay.Items.RemoveAt(_mcduDisplay.Items.Count - 1);
        while (_mcduDisplay.Items.Count < lines.Count) _mcduDisplay.Items.Add("");
        for (int i = 0; i < lines.Count; i++)
        {
            if (_mcduDisplay.Items[i]?.ToString() != lines[i])
                _mcduDisplay.Items[i] = lines[i];
        }
        _mcduDisplay.EndUpdate();

        string title = _rows[0].Trim();
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

        // Scratchpad change announce. Suppress on first push so a benign
        // empty initial scratchpad doesn't speak "Cleared" on form open.
        bool firstPush = _previousScratchpad == null;
        if (firstPush)
        {
            _previousScratchpad = scratchpad;
        }
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

    private void SendCommand(string command) => _bridgeServer.EnqueueCommand(command);

    private void SendLskLeft(int n)  => _bridgeServer.EnqueueCommand($"lsk_L{n}");
    private void SendLskRight(int n) => _bridgeServer.EnqueueCommand($"lsk_R{n}");

    private void SendTypeKey(string key) =>
        _bridgeServer.EnqueueCommand("type_key", new Dictionary<string, string> { ["key"] = key });

    private void ClearOrDelete()
    {
        if (!string.IsNullOrWhiteSpace(_previousScratchpad))
        {
            _clearingInProgress = true;
            _clearingWatchdog = 0;
            SendTypeKey("CLR");
        }
        else
        {
            SendTypeKey("DEL");
        }
    }

    private void FormKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+1..6 → L1..L6, Alt+1..6 → R1..R6 (same chord scheme as HS787 form
        // and PMDG CDU). Use D1..D6 so the laptop number row works regardless
        // of NumLock; Keypad keys are not handled to avoid colliding with NVDA
        // numpad navigation.
        if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            SendLskLeft(e.KeyCode - Keys.D1 + 1);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            SendLskRight(e.KeyCode - Keys.D1 + 1);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.PageUp || (e.Alt && e.KeyCode == Keys.Up))
        {
            SendCommand("key_prev_page");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.PageDown || (e.Alt && e.KeyCode == Keys.Down))
        {
            SendCommand("key_next_page");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
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
                    if (mapped != null) SendTypeKey(mapped);
                }
                _scratchpad.Text = "";
                _typingInProgress = false;
            }
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Back)
        {
            // Let the textbox handle visible backspace; if the field's empty,
            // also tell the MCDU to clear its scratchpad.
            if (string.IsNullOrEmpty(_scratchpad.Text))
            {
                ClearOrDelete();
                e.Handled = true; e.SuppressKeyPress = true;
            }
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
            ' ' => "SPACE",
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
}
