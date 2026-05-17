using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.HS787;

/// <summary>
/// Accessible FMC display for the HorizonSim 787-9.
/// Screen data is pushed by the JS bridge running inside MSFS (via EFBBridgeServer on port 19778).
/// LSK and page commands are sent back to the sim via the same bridge server.
/// Text input is sent as individual H-event key presses (H:AS01B_FMC_1_BTN_*).
/// </summary>
public partial class HS787FMCForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly EFBBridgeServer _bridgeServer;
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;

    // Latest screen rows from the bridge (row0..row12)
    private string[] _rows = new string[13];
    private string _previousTitle = "";
    private string? _previousScratchpad = null; // null = not yet received first push
    private bool _mfdConnected = false;
    private bool _cduVisible = false;

    private bool _typingInProgress = false;
    private bool _clearingInProgress = false;
    private int _clearingWatchdog = 0; // incremented each bridge push, timeout after a few cycles

    private IntPtr _previousWindow = IntPtr.Zero;
    private System.Windows.Forms.Timer _statusTimer = null!;

    public HS787FMCForm(EFBBridgeServer bridgeServer, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _bridgeServer = bridgeServer;
        _simConnect = simConnect;
        _announcer = announcer;

        InitializeComponent();
        SetupEventHandlers();

        // Timer updates the connection status label independently of screen data arriving.
        // This ensures the user sees "FMC Connected" as soon as the bridge connects,
        // even before the CDU renders its first screen update.
        _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _statusTimer.Tick += (_, _) => UpdateConnectionStatus();
        _statusTimer.Start();

        // Subscribe once for the form's lifetime — never unsubscribe on hide.
        // Only unsubscribe in Dispose() so the display stays live across hide/show cycles.
        _bridgeServer.StateUpdated += BridgeServer_StateUpdated;
    }

    // ------------------------------------------------------------------
    // Event wiring
    // ------------------------------------------------------------------

    private void SetupEventHandlers()
    {
        Load += (s, e) => fmcDisplay.Focus();

        FormClosing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero)
                SetForegroundWindow(_previousWindow);
        };

        this.KeyDown           += Form_KeyDown;
        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;

        // Page button clicks
        btnInitRef.Click  += (s, e) => SendBridgeCommand("page_init_ref");
        btnRte.Click      += (s, e) => SendBridgeCommand("page_rte");
        btnDepArr.Click   += (s, e) => SendBridgeCommand("page_dep_arr");
        btnAltn.Click     += (s, e) => SendBridgeCommand("page_altn");
        btnVnav.Click     += (s, e) => SendBridgeCommand("page_vnav");
        btnFix.Click      += (s, e) => SendBridgeCommand("page_fix");
        btnLegs.Click     += (s, e) => SendBridgeCommand("page_legs");
        btnHold.Click     += (s, e) => SendBridgeCommand("page_hold");
        btnFmcComm.Click  += (s, e) => SendBridgeCommand("page_fmc_comm");
        btnProg.Click     += (s, e) => SendBridgeCommand("page_prog");
        btnNavRad.Click   += (s, e) => SendBridgeCommand("page_nav_rad");
        btnPrevPage.Click += (s, e) => SendBridgeCommand("key_prev_page");
        btnNextPage.Click += (s, e) => SendBridgeCommand("key_next_page");
        btnExec.Click     += (s, e) => SendBridgeCommand("key_exec");
        btnClr.Click      += (s, e) => ClearOrDelete();
    }

    // ------------------------------------------------------------------
    // Bridge state handler — runs on UI thread (SynchronizationContext)
    // ------------------------------------------------------------------

    private void BridgeServer_StateUpdated(object? sender, EFBStateUpdateEventArgs e)
    {
        if (e.Type == "mfd_connected" || e.Type == "heartbeat")
        {
            _mfdConnected = true;
            if (IsHandleCreated)
                BeginInvoke(UpdateConnectionStatus);
            return;
        }

        if (e.Type == "cdu_visible")
        {
            _cduVisible = true;
            if (IsHandleCreated)
                BeginInvoke(UpdateConnectionStatus);
            return;
        }

        if (e.Type == "cdu_not_visible")
        {
            _cduVisible = false;
            if (IsHandleCreated)
                BeginInvoke(UpdateConnectionStatus);
            return;
        }

        if (e.Type != "fmc_screen") return;

        if (!e.Data.TryGetValue("rowCount", out string? rowCountStr) ||
            !int.TryParse(rowCountStr, out int rowCount))
            rowCount = 13;

        var newRows = new string[Math.Max(rowCount, 13)];
        for (int i = 0; i < newRows.Length; i++)
        {
            newRows[i] = e.Data.TryGetValue($"row{i}", out string? val) ? val ?? "" : "";
        }

        _rows = newRows;

        if (IsHandleCreated)
            BeginInvoke(UpdateDisplay);
    }

    // ------------------------------------------------------------------
    // Display update
    // ------------------------------------------------------------------

    private void UpdateConnectionStatus()
    {
        string desired;
        if (!_mfdConnected)
        {
            int stage = (_simConnect.CurrentAircraft as HorizonSim787Definition)?.BridgeStage ?? 0;
            string stageInfo = stage switch
            {
                0 => "Script: not executing",
                1 => "Script: loaded, connecting...",
                2 => "Script: loaded, fetch blocked",
                3 => "Script: connected (race?)",
                _ => $"Script: stage {stage}"
            };
            desired = $"FMC Bridge Not Connected [{stageInfo}]";
        }
        else if (!_cduVisible)
            desired = "FMC Bridge Connected — open CDU view on an MFD screen";
        else
            desired = "FMC Connected";

        if (statusLabel.Text != desired)
            statusLabel.Text = desired;
    }

    private void UpdateDisplay()
    {
        UpdateConnectionStatus();

        if (!_mfdConnected)
            return;

        var rows = _rows;
        int rowCount = rows.Length;
        if (rowCount < 2) return;

        // Display format:
        //   Row 0           = page title (shown as-is)
        //   Odd rows 1,3,…  = label rows  → indented with spaces
        //   Even rows 2,4,… = data rows   → prefixed "N: " (Ctrl+N = left LSK, Alt+N = right LSK)
        //   Last row        = scratchpad  → shown at bottom (sent by bridge as a separate row)
        //
        // The bridge sends 14 rows: rows 0-12 are the CDU screen (title + 6 label/data pairs)
        // and row 13 is the scratchpad (read from .wt787-cdu-scratchpad, not .fmc-row).

        int scratchpadRow = rowCount - 1;
        int numPairs      = (rowCount - 2) / 2;   // (14-2)/2 = 6 for standard 787 layout

        var lines = new List<string>();

        lines.Add(rows[0]); // Title

        for (int pair = 0; pair < numPairs; pair++)
        {
            int labelRow = 1 + pair * 2;
            int dataRow  = 2 + pair * 2;
            int lineNum  = pair + 1;

            if (labelRow >= rowCount || dataRow >= rowCount) break;

            string label = rows[labelRow];
            string data  = rows[dataRow];

            if (!string.IsNullOrWhiteSpace(label))
                lines.Add($"   {label}");

            lines.Add(string.IsNullOrWhiteSpace(data) ? $"{lineNum}:" : $"{lineNum}: {data}");
        }

        string scratchpad = rows[scratchpadRow];
        lines.Add(scratchpad); // Scratchpad

        // Update ListBox
        int savedIndex = fmcDisplay.SelectedIndex;

        fmcDisplay.BeginUpdate();
        while (fmcDisplay.Items.Count > lines.Count) fmcDisplay.Items.RemoveAt(fmcDisplay.Items.Count - 1);
        while (fmcDisplay.Items.Count < lines.Count) fmcDisplay.Items.Add("");
        for (int i = 0; i < lines.Count; i++)
        {
            if (fmcDisplay.Items[i]?.ToString() != lines[i])
                fmcDisplay.Items[i] = lines[i];
        }
        fmcDisplay.EndUpdate();

        // Announce title change
        string title = rows[0].Trim();
        if (!string.IsNullOrWhiteSpace(title) && title != _previousTitle)
        {
            _announcer.Announce(title);
            _previousTitle = title;
            if (fmcDisplay.Items.Count > 0)
                fmcDisplay.SelectedIndex = 0;
        }
        else if (savedIndex >= 0 && savedIndex < fmcDisplay.Items.Count)
        {
            fmcDisplay.SelectedIndex = savedIndex;
        }

        // Announce scratchpad change (suppressed while typing/clearing).
        // Initialise _previousScratchpad on first push so an empty scratchpad at startup
        // doesn't immediately announce "Cleared".
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
                    // FMC posted an error or message during CLR
                    _clearingInProgress = false;
                    _announcer.Announce(scratchpad);
                    _previousScratchpad = scratchpad;
                }
            }
            else
            {
                // Don't announce "Cleared" when the scratchpad simply empties after page navigation
                if (!string.IsNullOrWhiteSpace(scratchpad))
                    _announcer.Announce(scratchpad);
                _previousScratchpad = scratchpad;
            }
        }
    }

    // ------------------------------------------------------------------
    // Command sending
    // ------------------------------------------------------------------

    private void SendBridgeCommand(string command) =>
        _bridgeServer.EnqueueMfdCommand(command);

    private void SendLskLeft(int n) =>
        _bridgeServer.EnqueueMfdCommand($"lsk_L{n}");

    private void SendLskRight(int n) =>
        _bridgeServer.EnqueueMfdCommand($"lsk_R{n}");

    private void ClearOrDelete()
    {
        if (!string.IsNullOrWhiteSpace(_previousScratchpad))
        {
            _clearingInProgress = true;
            _clearingWatchdog = 0;
            SendHKey("CLR");
        }
        else
        {
            SendHKey("DEL"); // Puts DELETE in scratchpad to clear a selected field
        }
    }

    // Send a single FMC key by firing H:AS01B_FMC_1_BTN_{key} from inside Coherent GT
    // via the MFD bridge. Routes to /commands/mfd so the EFB bridge doesn't consume it.
    private void SendHKey(string key)
    {
        _bridgeServer.EnqueueMfdCommand($"type_key:{key}");
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Line-select keys — two layouts, switchable in FMC Settings:
        //   Default: Ctrl+1..6 = L1..L6, Alt+1..6 = R1..R6
        //   Alternate: F1..F6 = L1..L6, F7..F12 = R1..R6
        // Setting is read every keypress so a runtime change takes effect immediately.
        bool useAltKeys = SettingsManager.Current.MCDUUseAlternateLSKKeys;

        if (useAltKeys)
        {
            // F1..F6 = L1..L6
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
            {
                SendLskLeft(e.KeyCode - Keys.F1 + 1);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            // F7..F12 = R1..R6
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12)
            {
                SendLskRight(e.KeyCode - Keys.F7 + 1);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
        }
        else
        {
            // Ctrl+1-6: left LSK
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                SendLskLeft(e.KeyCode - Keys.D1 + 1);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }

            // Alt+1-6: right LSK
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                SendLskRight(e.KeyCode - Keys.D1 + 1);
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
        }

        // PageUp / Alt+Up → PREV PAGE
        if ((e.KeyCode == Keys.PageUp && !e.Control && !e.Alt) ||
            (e.Alt && e.KeyCode == Keys.Up))
        {
            SendBridgeCommand("key_prev_page");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // PageDown / Alt+Down → NEXT PAGE
        if ((e.KeyCode == Keys.PageDown && !e.Control && !e.Alt) ||
            (e.Alt && e.KeyCode == Keys.Down))
        {
            SendBridgeCommand("key_next_page");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Ctrl+Enter → EXEC
        if (e.Control && e.KeyCode == Keys.Return)
        {
            SendBridgeCommand("key_exec");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Backspace (display focused) → CLR/DEL
        if (e.KeyCode == Keys.Back && !e.Control && !e.Alt && ActiveControl == fmcDisplay)
        {
            ClearOrDelete();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Ctrl+Backspace → CLR_Long (clear entire scratchpad at once)
        if (e.Control && e.KeyCode == Keys.Back && ActiveControl == fmcDisplay)
        {
            _clearingInProgress = true;
            _clearingWatchdog = 0;
            SendHKey("CLR_Long");
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+S: focus scratchpad
        if (e.Alt && !e.Shift && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+Home: focus display
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            fmcDisplay.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+C: CLR_Long (clear entire scratchpad at once)
        if (e.Alt && !e.Control && !e.Shift && e.KeyCode == Keys.C)
        {
            if (!string.IsNullOrWhiteSpace(_previousScratchpad))
            {
                _clearingInProgress = true;
                _clearingWatchdog = 0;
                SendHKey("CLR_Long");
            }
            else
            {
                SendHKey("DEL");
            }
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+letter page shortcuts
        if (e.Alt && !e.Control && !e.Shift)
        {
            string? cmd = e.KeyCode switch
            {
                Keys.I => "page_init_ref",
                Keys.R => "page_rte",
                Keys.D => "page_dep_arr",
                Keys.A => "page_altn",
                Keys.V => "page_vnav",
                Keys.F => "page_fix",
                Keys.G => "page_legs",
                Keys.H => "page_hold",
                Keys.O => "page_fmc_comm",
                Keys.P => "page_prog",
                Keys.N => "page_nav_rad",
                Keys.E => "key_exec",
                _ => null
            };

            if (cmd != null)
            {
                SendBridgeCommand(cmd);
                e.Handled = true; e.SuppressKeyPress = true;
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
            _ = SendTextToFMC(text);
        }
        else if (e.KeyCode == Keys.Back && scratchpadInput.Text.Length == 0)
        {
            ClearOrDelete();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private async Task SendTextToFMC(string text)
    {
        _typingInProgress = true;
        string? previousKey = null;
        foreach (char c in text)
        {
            // Key names must match what ScratchpadProducer.handleCduHEvent expects.
            string? key = c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),  // A-Z sent as the letter
                >= '0' and <= '9' => c.ToString(),  // 0-9 sent as the digit
                '.' => "DOT",
                '/' => "DIV",        // JS uses DIV (not SLASH)
                ' ' => "SP",         // JS uses SP (not SPACE)
                '-' or '+' => "PLUSMINUS",
                _ => null
            };

            if (key != null)
            {
                // Repeated key needs extra delay so the FMC can distinguish separate presses
                if (key == previousKey)
                    await Task.Delay(400);
                SendHKey(key);
                await Task.Delay(350);
                previousKey = key;
            }
        }
        await Task.Delay(500);

        // Clear the typing flag and announce on the UI thread so UpdateDisplay can't
        // race between _typingInProgress going false and _previousScratchpad being updated.
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _typingInProgress = false;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _announcer.AnnounceImmediate(text);
                // Update so the next bridge push with the same content doesn't double-announce.
                _previousScratchpad = text;
            }
        });
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        fmcDisplay.Focus();
        // Re-request screen so the display is populated immediately on each open.
        _bridgeServer.EnqueueMfdCommand("read_screen");
    }

    // ------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------

    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _statusTimer.Stop();
            _statusTimer.Dispose();
            _bridgeServer.StateUpdated -= BridgeServer_StateUpdated;
        }
        base.Dispose(disposing);
    }
}
