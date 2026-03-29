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
            cduDisplay.Focus();
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
        btnPrevPage.Click += (s, e) => SendCDUKey("PREV_PAGE");
        btnNextPage.Click += (s, e) => SendCDUKey("NEXT_PAGE");

        // Special buttons
        btnExec.Click += (s, e) => SendCDUKey("EXEC");
        btnClr.Click  += (s, e) => ClearOrDelete();
    }

    // ------------------------------------------------------------------
    // Polling
    // ------------------------------------------------------------------

    private void PollTimer_Tick(object? sender, EventArgs e)
    {
        _dataManager.RequestCDUScreen(_selectedCDU);
        var result = _dataManager.GetCDURowsWithColors(_selectedCDU);
        if (result != null)
            UpdateDisplay(result.Value.rows, result.Value.colors);
        else
            UpdateDisplay(null, null);
    }

    // ------------------------------------------------------------------
    // Display update
    // ------------------------------------------------------------------

    private void UpdateDisplay(string[]? rows, byte[,]? colors)
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

        // Build display lines (matches Fenix MCDU format)
        var lines = new List<string>();
        lines.Add(rows[0]); // Row 0: page title

        // Rows 1-12: 6 label/value pairs
        for (int pair = 0; pair < 6; pair++)
        {
            int labelRow = 1 + (pair * 2);  // odd rows: 1, 3, 5, 7, 9, 11
            int dataRow = 2 + (pair * 2);    // even rows: 2, 4, 6, 8, 10, 12
            int lineNum = pair + 1;

            string label = rows[labelRow];
            string data = rows[dataRow];

            // Mark selected options on data rows using color data
            if (colors != null)
                data = MarkSelectedOption(data, colors, dataRow);

            // Label: indented (no line number — not selectable)
            if (!string.IsNullOrWhiteSpace(label))
                lines.Add($"   {label}");

            // Data: numbered (selectable via LSK) — always show line number
            if (!string.IsNullOrWhiteSpace(data))
                lines.Add($"{lineNum}: {data}");
            else
                lines.Add($"{lineNum}:");
        }

        lines.Add($"Scratchpad: {rows[13]}"); // Row 13: scratchpad

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
    }

    private void ClearOrDelete()
    {
        // If CDU scratchpad has content → CLR (clear it)
        // If CDU scratchpad is empty → DEL (puts "DELETE" in scratchpad for field deletion)
        if (!string.IsNullOrWhiteSpace(_previousScratchpad))
            SendCDUKey("CLR");
        else
            SendCDUKey("DEL");
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

        // Backspace (when CDU display is focused) → CLR/DEL
        if (e.KeyCode == Keys.Back && !e.Control && !e.Alt && ActiveControl == cduDisplay)
        {
            ClearOrDelete();
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

        // Alt+C: combined CLR/DEL
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode == Keys.C)
        {
            ClearOrDelete();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+letter: page button hotkeys
        if (e.Alt && !e.Control && !e.Shift)
        {
            string? key = e.KeyCode switch
            {
                Keys.I => "INIT_REF",
                Keys.R => "RTE",
                Keys.D => "DEP_ARR",
                Keys.A => "ALTN",
                Keys.V => "VNAV",
                Keys.F => "FIX",
                Keys.G => "LEGS",
                Keys.H => "HOLD",
                Keys.P => "PROG",
                Keys.E => "EXEC",
                Keys.M => "MENU",
                Keys.O => "FMCCOMM",
                _ => null
            };

            if (key != null)
            {
                SendCDUKey(key);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
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
            ClearOrDelete();
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
                await Task.Delay(150);
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
        cduDisplay.Focus();
    }

    // ------------------------------------------------------------------
    // Color-based selection markers
    // ------------------------------------------------------------------

    private static string MarkSelectedOption(string row, byte[,] colors, int rowIndex)
    {
        // Check if this row has mixed colors (indicating a toggle like OFF←→ON)
        // Green (color=2) = selected option
        bool hasGreen = false;
        bool hasNonGreen = false;

        for (int col = 0; col < 24 && col < row.Length; col++)
        {
            char ch = row[col];
            if (ch != ' ' && ch != '<' && ch != '>' && ch != '\u2190' && ch != '\u2192')
            {
                byte c = colors[rowIndex, col];
                if (c == 2) hasGreen = true;
                else if (c != 0 || char.IsLetterOrDigit(ch)) hasNonGreen = true;
            }
        }

        // Only mark if there's a toggle (both green and non-green text)
        if (!hasGreen || !hasNonGreen) return row;

        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < row.Length)
        {
            char ch = row[i];

            // Pass through spaces and arrows
            if (ch == ' ' || ch == '<' || ch == '>' || ch == '\u2190' || ch == '\u2192')
            {
                sb.Append(ch);
                i++;
                continue;
            }

            // Start of a text word — check its color
            byte segColor = colors[rowIndex, i];
            string marker = segColor == 2 ? "[X]" : "[ ]";
            sb.Append(marker);

            // Copy the word
            while (i < row.Length && row[i] != ' ' && row[i] != '<' && row[i] != '>'
                   && row[i] != '\u2190' && row[i] != '\u2192')
            {
                sb.Append(row[i]);
                i++;
            }
        }

        return sb.ToString();
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
