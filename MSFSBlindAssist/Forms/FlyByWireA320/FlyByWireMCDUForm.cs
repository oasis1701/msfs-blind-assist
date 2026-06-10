using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.FlyByWireA320;

/// <summary>
/// Accessible MCDU for the FlyByWire A32NX (ListBox display, scratchpad input, page
/// buttons) over the SimBridge websocket. Single MCDU (Captain) by design — the remote
/// protocol cannot separate Captain and First Officer screens (both keys carry the same
/// screen); see <see cref="FlyByWireMCDUService"/> remarks. This matches FBW's own remote.
/// </summary>
public class FlyByWireMCDUForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly FlyByWireMCDUService _service;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow = IntPtr.Zero;

    private ListBox mcduDisplay = null!;
    private TextBox scratchpadInput = null!;
    private Label connectionStatus = null!;

    private System.Windows.Forms.Timer? _scratchpadDebounceTimer;
    private string _lastAnnouncedScratchpad = "";
    private string _lastAnnouncedTitle = "";
    private MCDUDisplayData? _currentDisplay;

    // Function/page keys: (button label with Alt-accelerator, FBW key name). The page
    // accelerators MATCH the Fenix MCDU form so the muscle memory transfers: Alt+I/D/P/
    // F/E/R/U/K/M/A/T/O for Init/Dir/Prog/Fpln/Perf/Rad/Fuel/Atc/Menu/Airport/Data/Ovfy.
    // Sec F-PLN has no plain accelerator (Alt+Shift+F, handled in Form_KeyDown, like
    // Fenix). Up/Down/Prev/Next carry no letter accelerator — they have dedicated keys
    // (Page Up/Down, Alt+Up/Down/Left/Right; plain Left/Right also page when the display
    // list is focused, matching the Fenix MCDU).
    private static readonly (string Label, string Key)[] PageButtons =
    {
        ("&Init", "INIT"), ("&Dir", "DIR"), ("&Prog", "PROG"), ("&Fpln", "FPLN"), ("P&erf", "PERF"),
        ("&Rad Nav", "RAD"), ("Sec Fpln", "SEC"), ("F&uel Pred", "FUEL"), ("Atc &Kom", "ATC"), ("&Menu", "MENU"),
        ("&Airport", "AIRPORT"), ("Da&ta", "DATA"), ("&Ovfy", "OVFY"), ("&Clr", "CLR"),
        ("Prev Page", "PREVPAGE"), ("Next Page", "NEXTPAGE"), ("Up", "UP"), ("Down", "DOWN"),
    };

    public FlyByWireMCDUForm(FlyByWireMCDUService service, ScreenReaderAnnouncer announcer)
    {
        _service = service;
        _announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
        SetupEventHandlers();
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "FlyByWire MCDU";
        this.ClientSize = new Size(620, 760);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;

        int y = 10;

        connectionStatus = new Label
        {
            Text = "MCDU: Disconnected",
            Location = new Point(10, y),
            Size = new Size(600, 20),
            AccessibleName = "Connection status",
            AccessibleDescription = "Shows whether the MCDU is connected"
        };
        y += 28;

        mcduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 280),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Current MCDU screen. Use arrow keys to read lines.",
            IntegralHeight = false
        };
        y += 290;

        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 25),
            AccessibleName = "MCDU Input",
            AccessibleDescription = "Type text and press Enter to send to the MCDU scratchpad."
        };
        y += 34;

        int btnWidth = 116;
        int btnHeight = 30;
        int btnSpacing = 5;
        int perRow = 5;
        var buttons = new List<Control>();
        for (int i = 0; i < PageButtons.Length; i++)
        {
            int rowIdx = i / perRow;
            int colIdx = i % perRow;
            var (label, key) = PageButtons[i];
            var btn = new Button
            {
                Text = label,
                Location = new Point(10 + colIdx * (btnWidth + btnSpacing), y + rowIdx * (btnHeight + btnSpacing)),
                Size = new Size(btnWidth, btnHeight),
            };
            btn.Click += (s, e) => _ = _service.SendButtonPress(key);
            buttons.Add(btn);
        }

        this.Controls.Add(connectionStatus);
        this.Controls.Add(mcduDisplay);
        this.Controls.Add(scratchpadInput);
        foreach (var b in buttons) { this.Controls.Add(b); }

        int tabIdx = 0;
        mcduDisplay.TabIndex = tabIdx++;
        scratchpadInput.TabIndex = tabIdx++;
        foreach (var b in buttons) { b.TabIndex = tabIdx++; }

        this.ResumeLayout(false);
    }

    private void SetupAccessibility()
    {
        this.AccessibleName = "FlyByWire MCDU";
        this.AccessibleDescription = "FlyByWire A32NX MCDU display and controls";

        FormClosing += (sender, e) =>
        {
            e.Cancel = true;
            Hide();
            if (previousWindow != IntPtr.Zero) { SetForegroundWindow(previousWindow); }
        };

        _scratchpadDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _scratchpadDebounceTimer.Tick += (s, e) =>
        {
            _scratchpadDebounceTimer.Stop();
            if (_currentDisplay != null && _currentDisplay.Scratchpad != _lastAnnouncedScratchpad)
            {
                _lastAnnouncedScratchpad = _currentDisplay.Scratchpad;
                string announcement = string.IsNullOrEmpty(_currentDisplay.Scratchpad)
                    ? "Scratchpad cleared"
                    : _currentDisplay.Scratchpad;
                _announcer.Announce(announcement);
            }
        };
    }

    private void SetupEventHandlers()
    {
        _service.DisplayUpdated += OnDisplayUpdated;
        _service.ConnectionStatusChanged += OnConnectionStatusChanged;

        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;
        mcduDisplay.KeyDown += McduDisplay_KeyDown;
        this.KeyDown += Form_KeyDown;
    }

    private void McduDisplay_KeyDown(object? sender, KeyEventArgs e)
    {
        // Backspace = CLR, but ONLY in the display (the scratchpad needs Backspace to
        // edit text). Plain Left/Right page multi-page forms (PERF 1/2, INIT, ...)
        // like the Fenix MCDU; Up/Down stay free to read display lines. The same
        // paging also works window-wide via Alt+Left/Right in Form_KeyDown.
        if (e.KeyCode == Keys.Back)
        {
            _ = _service.SendButtonPress("CLR");
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Left && !e.Alt && !e.Control)
        {
            _ = _service.SendButtonPress("PREVPAGE");
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Right && !e.Alt && !e.Control)
        {
            _ = _service.SendButtonPress("NEXTPAGE");
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Line-select keys — two layouts, switchable in FMC Settings (read every keypress):
        //   Default:   Ctrl+1..6 = L1..L6, Alt+1..6 = R1..R6
        //   Alternate: F1..F6     = L1..L6, F7..F12  = R1..R6
        bool useAltKeys = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

        if (useAltKeys)
        {
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
            {
                int lsk = e.KeyCode - Keys.F1 + 1;
                _ = _service.SendButtonPress($"L{lsk}");
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12)
            {
                int lsk = e.KeyCode - Keys.F7 + 1;
                _ = _service.SendButtonPress($"R{lsk}");
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                int lsk = e.KeyCode - Keys.D1 + 1;
                _ = _service.SendButtonPress($"L{lsk}");
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                int lsk = e.KeyCode - Keys.D1 + 1;
                _ = _service.SendButtonPress($"R{lsk}");
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
        }

        // MCDU vertical slew (↑↓) — scroll multi-line lists (F-PLN, DEP/ARR runways).
        // Page Up/Down AND Alt+Up/Down both slew; plain arrows are left free to read
        // display lines. Handled at the form (KeyPreview) so they work from any focus.
        if (e.KeyCode == Keys.PageUp || (e.Alt && e.KeyCode == Keys.Up))
        {
            _ = _service.SendButtonPress("UP");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.KeyCode == Keys.PageDown || (e.Alt && e.KeyCode == Keys.Down))
        {
            _ = _service.SendButtonPress("DOWN");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        // MCDU horizontal page change (‹ ›) — multi-page forms (PERF 1/2, INIT, etc.).
        if (e.Alt && e.KeyCode == Keys.Left)
        {
            _ = _service.SendButtonPress("PREVPAGE");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.Alt && e.KeyCode == Keys.Right)
        {
            _ = _service.SendButtonPress("NEXTPAGE");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Alt+Shift+F: SEC F-PLN — the Sec Fpln button has no plain accelerator (it
        // would collide with Fpln = Alt+F). Matches the Fenix MCDU form.
        if (e.Alt && e.Shift && e.KeyCode == Keys.F)
        {
            _ = _service.SendButtonPress("SEC");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Alt+S: focus scratchpad input
        if (e.Alt && !e.Shift && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Alt+Home: focus the display
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            mcduDisplay.Focus();
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = scratchpadInput.Text.ToUpperInvariant();
            _ = SendTextToMCDU(text);
            scratchpadInput.Clear();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private async Task SendTextToMCDU(string text)
    {
        foreach (char c in text)
        {
            string? buttonName = c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),
                >= '0' and <= '9' => c.ToString(),
                '.' => "DOT",
                '/' => "DIV",
                '-' or '+' => "PLUSMINUS",
                ' ' => "SP",
                '*' => "OVFY",
                _ => null
            };
            if (buttonName != null)
            {
                await _service.SendButtonPress(buttonName);
                await Task.Delay(50);
            }
        }
    }

    private void OnDisplayUpdated(MCDUDisplayData data)
    {
        if (IsDisposed || !IsHandleCreated) return;
        _currentDisplay = data;

        var lines = new List<string>();
        if (data.Annunciators.Count > 0)
        {
            lines.Add("Annunciators: " + string.Join(", ", data.Annunciators));
        }

        string titleLine = $"Title: {data.Title}";
        if (!string.IsNullOrWhiteSpace(data.Page)) { titleLine += "   " + data.Page; }
        if (data.Arrows.Length > 0 && data.Arrows[0]) { titleLine += " ▲"; }
        if (data.Arrows.Length > 1 && data.Arrows[1]) { titleLine += " ▼"; }
        if (data.Arrows.Length > 2 && data.Arrows[2]) { titleLine += " ◄"; }
        if (data.Arrows.Length > 3 && data.Arrows[3]) { titleLine += " ►"; }
        lines.Add(titleLine);

        for (int i = 0; i < 6; i++)
        {
            string labelLine = data.RawLines[1 + 2 * i].TrimEnd();
            string valueLine = data.RawLines[2 + 2 * i].TrimEnd();
            if (!string.IsNullOrWhiteSpace(labelLine)) { lines.Add("   " + labelLine); }
            lines.Add($"{i + 1}: {valueLine}");
        }

        lines.Add($"Scratchpad: {data.Scratchpad}");

        int savedIndex = mcduDisplay.SelectedIndex;
        if (mcduDisplay.Items.Count == 0)
        {
            foreach (var line in lines) { mcduDisplay.Items.Add(line); }
        }
        else
        {
            mcduDisplay.BeginUpdate();
            while (mcduDisplay.Items.Count > lines.Count) { mcduDisplay.Items.RemoveAt(mcduDisplay.Items.Count - 1); }
            while (mcduDisplay.Items.Count < lines.Count) { mcduDisplay.Items.Add(""); }
            for (int idx = 0; idx < lines.Count; idx++)
            {
                if (mcduDisplay.Items[idx].ToString() != lines[idx]) { mcduDisplay.Items[idx] = lines[idx]; }
            }
            mcduDisplay.EndUpdate();
        }

        string trimmedTitle = data.Title.Trim();
        bool titleChanged = !string.IsNullOrEmpty(trimmedTitle) && trimmedTitle != _lastAnnouncedTitle;
        if (titleChanged)
        {
            _lastAnnouncedTitle = trimmedTitle;
            _announcer.Announce(trimmedTitle);
            if (mcduDisplay.Items.Count > 1) { mcduDisplay.SelectedIndex = 1; }
        }
        else if (savedIndex >= 0 && savedIndex < mcduDisplay.Items.Count)
        {
            mcduDisplay.SelectedIndex = savedIndex;
        }

        if (data.Scratchpad != _lastAnnouncedScratchpad)
        {
            _scratchpadDebounceTimer?.Stop();
            _scratchpadDebounceTimer?.Start();
        }
    }

    private void OnConnectionStatusChanged(bool isConnected)
    {
        if (IsDisposed || !IsHandleCreated) return;
        connectionStatus.Text = isConnected ? "MCDU: Connected" : "MCDU: Disconnected";
        _announcer.Announce(isConnected ? "MCDU connected" : "MCDU disconnected");
    }

    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        this.ActiveControl = mcduDisplay;
        mcduDisplay.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _service.DisplayUpdated -= OnDisplayUpdated;
            _service.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _scratchpadDebounceTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
