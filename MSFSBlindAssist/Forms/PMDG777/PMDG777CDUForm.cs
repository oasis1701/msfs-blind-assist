using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777;

public partial class PMDG777CDUForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly PMDG777DataManager _dataManager;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private string[]? _previousRows;
    private string _previousScratchpad = "";
    private int _selectedCDU = 0;
    private IntPtr _previousWindow = IntPtr.Zero;

    public PMDG777CDUForm(PMDG777DataManager dataManager, ScreenReaderAnnouncer announcer)
    {
        _dataManager = dataManager;
        _announcer = announcer;

        InitializeComponent();
        SetupEventHandlers();

        _pollTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _pollTimer.Tick += PollTimer_Tick;
    }

    private void SetupEventHandlers()
    {
        this.Load += (s, e) =>
        {
            scratchpadInput.Focus();
            _pollTimer.Start();
        };

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            _pollTimer.Stop();
            Hide();

            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };

        this.KeyDown += Form_KeyDown;
        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;

        cduSelector.SelectedIndexChanged += (s, e) =>
        {
            _selectedCDU = cduSelector.SelectedIndex;
            _previousRows = null;
            _previousScratchpad = "";
        };

        // Line select button click handlers
        btnL1.Click += (s, e) => OnLineSelect("L1", 1);
        btnL2.Click += (s, e) => OnLineSelect("L2", 2);
        btnL3.Click += (s, e) => OnLineSelect("L3", 3);
        btnL4.Click += (s, e) => OnLineSelect("L4", 4);
        btnL5.Click += (s, e) => OnLineSelect("L5", 5);
        btnL6.Click += (s, e) => OnLineSelect("L6", 6);
        btnR1.Click += (s, e) => OnLineSelect("R1", 1);
        btnR2.Click += (s, e) => OnLineSelect("R2", 2);
        btnR3.Click += (s, e) => OnLineSelect("R3", 3);
        btnR4.Click += (s, e) => OnLineSelect("R4", 4);
        btnR5.Click += (s, e) => OnLineSelect("R5", 5);
        btnR6.Click += (s, e) => OnLineSelect("R6", 6);

        // Page button click handlers
        btnInitRef.Click  += (s, e) => SendCDUKey("INIT_REF");
        btnRte.Click      += (s, e) => SendCDUKey("RTE");
        btnDepArr.Click   += (s, e) => SendCDUKey("DEP_ARR");
        btnAltn.Click     += (s, e) => SendCDUKey("ALTN");
        btnVnav.Click     += (s, e) => SendCDUKey("VNAV");
        btnFix.Click      += (s, e) => SendCDUKey("FIX");
        btnLegs.Click     += (s, e) => SendCDUKey("LEGS");
        btnHold.Click     += (s, e) => SendCDUKey("HOLD");
        btnFmcComm.Click  += (s, e) => SendCDUKey("FMCCOMM");
        btnProg.Click     += (s, e) => SendCDUKey("PROG");
        btnMenu.Click     += (s, e) => SendCDUKey("MENU");
        btnNavRad.Click   += (s, e) => SendCDUKey("NAV_RAD");
        btnPrevPage.Click += (s, e) => SendCDUKey("PREV_PAGE");
        btnNextPage.Click += (s, e) => SendCDUKey("NEXT_PAGE");

        // Special buttons
        btnExec.Click += (s, e) => SendCDUKey("EXEC");
        btnClr.Click  += (s, e) => SendCDUKey("CLR");
        btnDel.Click  += (s, e) => SendCDUKey("DEL");
    }

    // ------------------------------------------------------------------
    // Polling
    // ------------------------------------------------------------------

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        _dataManager.RequestCDUScreen(_selectedCDU);
        var rows = _dataManager.GetCDURows(_selectedCDU);
        UpdateDisplay(rows);
    }

    // ------------------------------------------------------------------
    // Display update
    // ------------------------------------------------------------------

    private void UpdateDisplay(string[]? rows)
    {
        if (rows == null)
        {
            if (statusLabel.Text != "CDU Not Powered")
                statusLabel.Text = "CDU Not Powered";
            return;
        }

        if (statusLabel.Text != "CDU Connected")
            statusLabel.Text = "CDU Connected";

        // Row 0 = title, row 13 = scratchpad
        string title      = rows[0].Trim();
        string scratchpad = rows[13].Trim();

        // Build display lines
        var lines = new List<string>(rows.Length);
        for (int i = 0; i < rows.Length; i++)
            lines.Add(rows[i]);

        // Update ListBox items efficiently
        int savedIndex = cduDisplay.SelectedIndex;

        cduDisplay.BeginUpdate();
        while (cduDisplay.Items.Count > lines.Count)
            cduDisplay.Items.RemoveAt(cduDisplay.Items.Count - 1);
        while (cduDisplay.Items.Count < lines.Count)
            cduDisplay.Items.Add("");

        for (int i = 0; i < lines.Count; i++)
        {
            if (cduDisplay.Items[i]?.ToString() != lines[i])
                cduDisplay.Items[i] = lines[i];
        }
        cduDisplay.EndUpdate();

        // Announce title change
        bool titleChanged = !string.IsNullOrWhiteSpace(title) &&
                            (_previousRows == null || title != _previousRows[0].Trim());
        if (titleChanged)
        {
            _announcer.Announce($"Page: {title}");
            if (cduDisplay.Items.Count > 0)
                cduDisplay.SelectedIndex = 0;
        }
        else if (savedIndex >= 0 && savedIndex < cduDisplay.Items.Count)
        {
            cduDisplay.SelectedIndex = savedIndex;
        }

        // Announce scratchpad change
        if (scratchpad != _previousScratchpad)
        {
            string msg = string.IsNullOrWhiteSpace(scratchpad)
                ? "Scratchpad cleared"
                : $"Scratchpad: {scratchpad}";
            _announcer.Announce(msg);
            _previousScratchpad = scratchpad;
        }

        _previousRows = rows;
    }

    // ------------------------------------------------------------------
    // CDU key sending
    // ------------------------------------------------------------------

    private void SendCDUKey(string eventSuffix)
    {
        string prefix = _selectedCDU switch
        {
            1 => "EVT_CDU_C_",
            2 => "EVT_CDU_R_",
            _ => "EVT_CDU_L_"
        };

        string eventName = $"{prefix}{eventSuffix}";

        // Fall back to left CDU events if center/right not found (PMDG 777 only has left CDU events)
        if (!PMDG777Definition.EventIds.TryGetValue(eventName, out int eventId))
        {
            eventName = $"EVT_CDU_L_{eventSuffix}";
            if (!PMDG777Definition.EventIds.TryGetValue(eventName, out eventId))
                return;
        }

        _dataManager.SendEvent(eventName, (uint)eventId, 1);
    }

    private void OnLineSelect(string suffix, int lineNumber)
    {
        SendCDUKey(suffix);

        // Announce the content of the selected line
        if (_previousRows != null)
        {
            // LSK rows: L1=row2, L2=row4, L3=row6, L4=row8, L5=row10, L6=row12
            // R keys are in same rows (right side of same line)
            bool isLeft = suffix.StartsWith('L');
            int rowIndex = lineNumber * 2; // rows 2,4,6,8,10,12
            if (rowIndex < _previousRows.Length)
            {
                string content = _previousRows[rowIndex].Trim();
                if (string.IsNullOrWhiteSpace(content) && rowIndex - 1 >= 0)
                    content = _previousRows[rowIndex - 1].Trim();
                if (!string.IsNullOrWhiteSpace(content))
                    _announcer.Announce($"Line {suffix}: {content}");
                else
                    _announcer.Announce($"Line {suffix}: empty");
            }
        }
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+1-6: left line select L1-L6
        if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            int num = e.KeyCode - Keys.D1 + 1;
            OnLineSelect($"L{num}", num);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+1-6: right line select R1-R6
        if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
        {
            int num = e.KeyCode - Keys.D1 + 1;
            OnLineSelect($"R{num}", num);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // PageUp → PREV_PAGE
        if (e.KeyCode == Keys.PageUp && !e.Control && !e.Alt)
        {
            SendCDUKey("PREV_PAGE");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // PageDown → NEXT_PAGE
        if (e.KeyCode == Keys.PageDown && !e.Control && !e.Alt)
        {
            SendCDUKey("NEXT_PAGE");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Ctrl+Enter → EXEC
        if (e.Control && e.KeyCode == Keys.Return)
        {
            SendCDUKey("EXEC");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+S: focus scratchpad
        if (e.Alt && !e.Shift && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+Home: focus display
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            cduDisplay.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = scratchpadInput.Text.ToUpperInvariant();
            scratchpadInput.Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = SendTextToCDU(text);
        }
        else if (e.KeyCode == Keys.Back && scratchpadInput.Text.Length == 0)
        {
            SendCDUKey("CLR");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private async Task SendTextToCDU(string text)
    {
        foreach (char c in text)
        {
            string? keySuffix = c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),
                '1' => "1",
                '2' => "2",
                '3' => "3",
                '4' => "4",
                '5' => "5",
                '6' => "6",
                '7' => "7",
                '8' => "8",
                '9' => "9",
                '0' => "0",
                '.' => "DOT",
                '/' => "SLASH",
                ' ' => "SPACE",
                '-' => "PLUS_MINUS",
                '+' => "PLUS_MINUS",
                _ => null
            };

            if (keySuffix != null)
            {
                SendCDUKey(keySuffix);
                await Task.Delay(50);
            }
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        _pollTimer.Start();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        scratchpadInput.Focus();
    }

    // ------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
