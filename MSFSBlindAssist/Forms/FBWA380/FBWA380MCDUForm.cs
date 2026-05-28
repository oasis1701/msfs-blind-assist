using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible MCDU window for the FlyByWire A380X (development build).
///
/// Single flat layout — no tabs. Tab order (top to bottom):
///   1. MCDU selector       — Captain (1) / First Officer (2). The A380X
///                            has no third MCDU; the combo lists only
///                            those that exist.
///   2. Display list        — live scrape of the active MFD's FMS page.
///                            Rows follow the Fenix MCDU convention:
///                              "Title: <page>"  (row 0)
///                              "   <label>"     (3-space indent)
///                              "N: <value>"     (numbered interactive fields)
///                              "Scratchpad: …"  (last row)
///                            Enter on a numbered row activates that field.
///   3. Scratchpad input    — Type text, press Enter to fire each KCCU key
///                            then ENT. The screen reader speaks the text
///                            back ("3 2 0") after the bridge has sent it.
///                            Ctrl+1..9 sends the typed scratchpad to that
///                            field number (click + clear + type + ENT).
///   4. Page nav buttons    — KCCU keys for INIT, F-PLN, PERF, RAD NAV,
///                            DIR, SEC F-PLN, ATC, DOWN, UP, ENT, CLR.
///
/// Everything refreshes automatically: bridge JS polls the MFD DOM at
/// 350 ms and pushes new state via EFBBridgeServer when anything changes.
/// Title changes and scratchpad/footer-message changes are announced
/// without the user having to refocus.
///
/// Wire protocol — see Resources/fbw-a380-bridge.js. State pushes
/// consumed here: fbwa380_mcdu_screen, fbwa380_mcdu_elements,
/// fbwa380_mcdu_connected. Commands sent: page_*, key_*, type_key,
/// select_mcdu, get_mcdu_elements, click_mcdu_element,
/// send_scratchpad, send_to_field.
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
    private readonly FlyByWireA380Definition? _aircraftDefinition;

    private Label _statusLabel = null!;
    private ComboBox _mcduSelector = null!;
    private ListBox _display = null!;
    private TextBox _scratchpad = null!;

    // Page nav buttons mirror the physical KCCU keyboard.
    private Button _btnInit = null!;
    private Button _btnFPln = null!;
    private Button _btnPerf = null!;
    private Button _btnRadNav = null!;
    private Button _btnSecFPln = null!;
    private Button _btnAtc = null!;
    private Button _btnDir = null!;
    private Button _btnUp = null!;
    private Button _btnDown = null!;
    private Button _btnEnt = null!;
    private Button _btnClr = null!;
    private Button _btnRefresh = null!;

    private string[] _rows = Array.Empty<string>();
    private List<McduElement> _elements = new();

    private string _previousTitle = "";
    private string _previousScratchpad = "";
    private bool _bridgeConnected;
    private int _mcduIndex = 1;
    private bool _initialPushReceived;

    private IntPtr _previousWindow = IntPtr.Zero;
    private System.Windows.Forms.Timer _statusTimer = null!;

    private static readonly Regex FieldMarkerRegex = new Regex(@"\b(\d+):", RegexOptions.Compiled);

    public FBWA380MCDUForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer,
        FlyByWireA380Definition? aircraftDefinition = null)
    {
        _bridgeServer = bridgeServer;
        _announcer = announcer;
        _aircraftDefinition = aircraftDefinition;

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
        // Ask the bridge for a fresh push so the display populates the
        // moment the user opens the form, rather than waiting for the
        // next 350-ms poll cycle.
        _bridgeServer.EnqueueCommand("get_mcdu_elements");
        _display.Focus();
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        Text = "A380X MCDU (Captain)";
        ClientSize = new Size(640, 700);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;

        _mcduSelector = new ComboBox
        {
            Location = new Point(420, y),
            Size = new Size(210, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Active MCDU side",
            AccessibleDescription = "Choose which A380 MFD's FMS pages to display: Captain or First Officer."
        };
        _mcduSelector.Items.AddRange(new object[] { "Captain (1)", "First Officer (2)" });
        _mcduSelector.SelectedIndex = 0;
        Controls.Add(_mcduSelector);

        _statusLabel = new Label
        {
            Text = "MCDU bridge: connecting…",
            Location = new Point(10, y),
            Size = new Size(400, 76),  // multi-line — full stage diagnostic
            AutoSize = false,
            AccessibleName = "MCDU bridge status"
        };
        Controls.Add(_statusLabel);
        y += 82;

        _display = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(620, 380),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU display",
            AccessibleDescription = "FMS page contents. Use arrow keys to read each line. Press Enter on a numbered row to activate that field. Ctrl plus 1 to 9 in this window sends the scratchpad to the matching field.",
            IntegralHeight = false
        };
        Controls.Add(_display);
        y += 390;

        _scratchpad = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(620, 25),
            AccessibleName = "Scratchpad input",
            AccessibleDescription = "Type text and press Enter to send it to the currently focused MFD field. Press Ctrl plus a digit to send to that field number instead."
        };
        Controls.Add(_scratchpad);
        y += 35;

        int btnW = 90, btnH = 30, gap = 4, col = 10;
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

        _btnInit    = MakeBtn("&INIT",     "KCCU INIT key");
        _btnFPln    = MakeBtn("&F-PLN",    "KCCU FPLN key");
        _btnPerf    = MakeBtn("&PERF",     "KCCU PERF key");
        _btnRadNav  = MakeBtn("&RAD NAV",  "KCCU NAVAID key");
        _btnSecFPln = MakeBtn("&SEC FPL",  "KCCU SECINDEX key");
        _btnAtc     = MakeBtn("&ATC COM",  "KCCU ATCCOM key");
        _btnDir     = MakeBtn("&DIR",      "KCCU DIR key");
        y += btnH + gap; col = 10;

        _btnUp      = MakeBtn("&UP",       "KCCU UP / previous page");
        _btnDown    = MakeBtn("DO&WN",     "KCCU DOWN / next page");
        _btnEnt     = MakeBtn("&ENT",      "KCCU ENT / confirm entry");
        _btnClr     = MakeBtn("&CLR",      "KCCU BACKSPACE / clear");
        _btnRefresh = MakeBtn("Re&fresh",  "Re-request the current MFD content from the bridge.");

        ResumeLayout(true);
    }

    private void SetupEventHandlers()
    {
        Load += (_, _) => _display.Focus();

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };

        _btnInit.Click    += (_, _) => SendCommand("page_init");
        _btnFPln.Click    += (_, _) => SendCommand("page_fpln");
        _btnPerf.Click    += (_, _) => SendCommand("page_perf");
        _btnRadNav.Click  += (_, _) => SendCommand("page_radnav");
        _btnSecFPln.Click += (_, _) => SendCommand("page_sec_fpln");
        _btnAtc.Click     += (_, _) => SendCommand("page_atc");
        _btnDir.Click     += (_, _) => SendCommand("page_dir");
        _btnUp.Click      += (_, _) => SendCommand("key_prev_page");
        _btnDown.Click    += (_, _) => SendCommand("key_next_page");
        _btnEnt.Click     += (_, _) => SendCommand("key_exec");
        _btnClr.Click     += (_, _) => SendTypeKey("BACKSPACE");
        _btnRefresh.Click += (_, _) => _bridgeServer.EnqueueCommand("get_mcdu_elements");

        _mcduSelector.SelectedIndexChanged += (_, _) =>
        {
            _mcduIndex = _mcduSelector.SelectedIndex + 1;
            Text = $"A380X MCDU ({_mcduSelector.SelectedItem})";
            _bridgeServer.EnqueueCommand("select_mcdu",
                new Dictionary<string, string> { ["mcdu"] = _mcduIndex.ToString() });
            _bridgeServer.EnqueueCommand("get_mcdu_elements");
        };

        _scratchpad.KeyDown += ScratchpadKeyDown;
        _display.KeyDown    += DisplayKeyDown;
        KeyDown             += FormKeyDown;
    }

    private void OnBridgeStateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (e.Type == StateTypeConnected)
        {
            _bridgeConnected = true;
            if (IsHandleCreated) BeginInvoke(UpdateStatusLabel);
            return;
        }
        if (e.Type == StateTypeScreen)   { HandleScreenPush(e); return; }
        if (e.Type == StateTypeElements) { HandleElementsPush(e); return; }
    }

    private void HandleScreenPush(EFBStateUpdateEventArgs e)
    {
        _bridgeConnected = true;
        _initialPushReceived = true;
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
        if (IsHandleCreated) BeginInvoke(RefreshDisplay);
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
    }

    private void UpdateStatusLabel()
    {
        bool reallyConnected = _bridgeConnected && _bridgeServer.IsBridgeConnected;
        string desired;
        if (reallyConnected && _initialPushReceived)
        {
            desired = $"MCDU bridge: connected (MCDU {_mcduIndex})";
        }
        else if (reallyConnected)
        {
            desired = "MCDU bridge: connected — waiting for MFD content…";
        }
        else
        {
            // Surface the bridge-stage diagnostic so the user can tell
            // which step of bring-up failed. Stage value comes from the
            // continuously-monitored L:MSFSBA_FBWA380_STAGE that the JS
            // updates on every state transition (see fbw-a380-bridge.js).
            int stage = _aircraftDefinition?.BridgeStage ?? 0;
            string stageHint = stage switch
            {
                0 => "Stage 0: bridge JS hasn't run. The overlay package isn't being picked up by MSFS, or the script failed to load. Open the FBW A380 with MSFSBA running, accept any install prompt, then RESTART MSFS so the patched HTML is re-scanned.",
                1 => "Stage 1: bridge JS is running but hasn't reached the server yet. If this stays at 1 for more than 30 seconds, MSFSBA may not have started the bridge server — switch aircraft to FBW A380X in the Aircraft menu to start it.",
                2 => "Stage 2: bridge JS ran but its fetch to localhost was blocked. Coherent GT may have a CSP / network policy issue. Restart MSFS and MSFSBA in that order.",
                3 => "Stage 3: bridge JS is connected. If you see this without 'MCDU bridge: connected' then the server lost the connection — check MSFSBA is still running.",
                _ => $"Stage {stage}: unrecognised bridge state."
            };
            desired = "MCDU bridge: not connected. " + stageHint;
        }
        if (_statusLabel.Text != desired) _statusLabel.Text = desired;
    }

    private void RefreshDisplay()
    {
        // The bridge already produces Fenix-style labelled rows:
        //   row 0 ........ "Title: <page>"
        //   middle ....... "   <label>" (3-space indent) or "N: <value>"
        //   last ......... "Scratchpad: <text>"
        // We render verbatim into the monospace ListBox so column
        // alignment survives.
        if (_rows.Length == 0) return;

        var lines = new List<string>(_rows.Length);
        string scratchpad = "";
        for (int i = 0; i < _rows.Length; i++)
        {
            string row = _rows[i] ?? "";
            bool isTitle = i == 0;
            bool isLast  = i == _rows.Length - 1;
            if (!isTitle && !isLast && string.IsNullOrWhiteSpace(row)) continue;
            if (isLast)
            {
                const string prefix = "Scratchpad:";
                int p = row.IndexOf(prefix, StringComparison.Ordinal);
                scratchpad = p >= 0 ? row.Substring(p + prefix.Length).Trim() : row.Trim();
            }
            lines.Add(row);
        }

        int saved = _display.SelectedIndex;
        _display.BeginUpdate();
        while (_display.Items.Count > lines.Count) _display.Items.RemoveAt(_display.Items.Count - 1);
        while (_display.Items.Count < lines.Count) _display.Items.Add("");
        for (int i = 0; i < lines.Count; i++)
            if (_display.Items[i]?.ToString() != lines[i])
                _display.Items[i] = lines[i];
        _display.EndUpdate();

        // Extract just the page name out of "Title: …" for change
        // detection and announce.
        string title = _rows.Length > 0 ? _rows[0] : "";
        const string titlePrefix = "Title:";
        int titleIdx = title.IndexOf(titlePrefix, StringComparison.Ordinal);
        title = titleIdx >= 0 ? title.Substring(titleIdx + titlePrefix.Length).Trim() : title.Trim();
        if (!string.IsNullOrWhiteSpace(title) && title != _previousTitle)
        {
            _announcer.Announce(title);
            _previousTitle = title;
            if (_display.Items.Count > 0) _display.SelectedIndex = 0;
        }
        else if (saved >= 0 && saved < _display.Items.Count)
        {
            _display.SelectedIndex = saved;
        }

        // Background scratchpad / message announcement.
        if (scratchpad != _previousScratchpad)
        {
            if (!string.IsNullOrWhiteSpace(scratchpad))
                _announcer.Announce(scratchpad);
            _previousScratchpad = scratchpad;
        }

        UpdateStatusLabel();
    }

    // ---- input handlers ---------------------------------------------------

    private void SendCommand(string command) => _bridgeServer.EnqueueCommand(command);
    private void SendTypeKey(string key) =>
        _bridgeServer.EnqueueCommand("type_key", new Dictionary<string, string> { ["key"] = key });

    private void ScratchpadKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = _scratchpad.Text;
            if (!string.IsNullOrEmpty(text))
            {
                // Atomic composite — bridge fires the chars + ENT into
                // whichever field the cockpit cursor is currently on.
                _bridgeServer.EnqueueCommand("send_scratchpad",
                    new Dictionary<string, string> { ["text"] = text });
                _scratchpad.Text = "";
                // Speak back what was sent so the user gets immediate
                // confirmation independent of the live MFD scrape.
                _announcer.Announce(text);
            }
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Back && string.IsNullOrEmpty(_scratchpad.Text))
        {
            SendTypeKey("BACKSPACE");
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private void DisplayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            int fieldIdx = ExtractFirstFieldIndex(_display.SelectedItem?.ToString() ?? "");
            if (fieldIdx > 0)
            {
                _bridgeServer.EnqueueCommand("click_mcdu_element",
                    new Dictionary<string, string> { ["index"] = fieldIdx.ToString() });
                _announcer.Announce("Field " + fieldIdx + " activated");
            }
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            _bridgeServer.EnqueueCommand("get_mcdu_elements");
            e.Handled = true;
        }
    }

    private void FormKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+1..9 — send whatever's in the scratchpad to field N (composite:
        // click the field, clear any existing value, type the new value,
        // press ENT). Mirrors the LSK shortcuts on Fenix/PMDG even though
        // the A380 has no physical LSKs.
        if (e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            int fieldIdx = e.KeyCode - Keys.D0;
            string text = _scratchpad.Text;
            _bridgeServer.EnqueueCommand("send_to_field", new Dictionary<string, string>
            {
                ["index"] = fieldIdx.ToString(),
                ["text"]  = text
            });
            _scratchpad.Text = "";
            _announcer.Announce(
                string.IsNullOrEmpty(text)
                    ? $"Activating field {fieldIdx}"
                    : $"Sending {text} to field {fieldIdx}");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if ((e.KeyCode == Keys.PageUp) || (e.Alt && e.KeyCode == Keys.Up))
        {
            SendCommand("key_prev_page");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if ((e.KeyCode == Keys.PageDown) || (e.Alt && e.KeyCode == Keys.Down))
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

    /// <summary>
    /// Parses the leftmost "N:" field-index marker from a display row.
    /// Returns 0 if the row has no marker (e.g. label rows or the title).
    /// </summary>
    private static int ExtractFirstFieldIndex(string row)
    {
        if (string.IsNullOrEmpty(row)) return 0;
        // Skip the "Title: " and "Scratchpad: " prefixes — those aren't
        // field markers.
        if (row.StartsWith("Title:", StringComparison.Ordinal)) return 0;
        if (row.StartsWith("Scratchpad:", StringComparison.Ordinal)) return 0;
        var m = FieldMarkerRegex.Match(row);
        if (!m.Success) return 0;
        return int.TryParse(m.Groups[1].Value, out int n) ? n : 0;
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
