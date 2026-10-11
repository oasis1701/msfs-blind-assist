using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.FenixA320;

public class FenixMCDUForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly FenixMCDUService _service;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow = IntPtr.Zero;

    private ListBox mcduDisplay = null!;
    private TextBox scratchpadInput = null!;
    private Label connectionStatus = null!;
    private ComboBox mcduSelector = null!;

    // Page buttons
    private Button btnInit = null!;
    private Button btnDir = null!;
    private Button btnProg = null!;
    private Button btnFpln = null!;
    private Button btnPerf = null!;
    private Button btnRadNav = null!;
    private Button btnSecFpln = null!;
    private Button btnFuelPred = null!;
    private Button btnAtcCom = null!;
    private Button btnMenu = null!;
    private Button btnAirport = null!;
    private Button btnData = null!;
    private Button btnOverfly = null!;

    // Scratchpad debounce
    private System.Windows.Forms.Timer? _scratchpadDebounceTimer;
    private string _lastAnnouncedScratchpad = "";
    private string _lastAnnouncedTitle = "";

    // Current display data for screen reader access
    private MCDUDisplayData? _currentDisplay;

    public FenixMCDUForm(FenixMCDUService service, ScreenReaderAnnouncer announcer)
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

        // The service outlives a window, and holds the selected side: start on it.
        this.Text = $"Fenix MCDU ({SideName(_service.SelectedSide)})";
        this.ClientSize = new Size(600, 700);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;

        int y = 10;

        // Connection status
        connectionStatus = new Label
        {
            Text = "MCDU: Disconnected",
            Location = new Point(10, y),
            Size = new Size(400, 20),
            AccessibleName = "Connection status",
            AccessibleDescription = "Shows whether the MCDU is connected"
        };

        // MCDU selector
        mcduSelector = new ComboBox
        {
            Location = new Point(420, y),
            Size = new Size(170, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU selector"
        };
        mcduSelector.Items.AddRange(new object[] { "Left (Captain)", "Right (First Officer)" });
        mcduSelector.SelectedIndex = _service.SelectedSide - 1;
        y += 25;

        // MCDU Display
        mcduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(580, 230),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Shows the current MCDU screen content. Use arrow keys to read lines.",
            IntegralHeight = false
        };
        y += 240;

        // Scratchpad input (key capture zone)
        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(580, 25),
            AccessibleName = "MCDU Input",
            AccessibleDescription = "Type text and press Enter to send to the MCDU scratchpad."
        };
        y += 35;

        // Page buttons - row 1
        int btnWidth = 100;
        int btnHeight = 30;
        int btnSpacing = 5;
        int col = 10;

        btnInit = CreateButton("&Init", "INIT", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnDir = CreateButton("&Dir", "DIR", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnProg = CreateButton("&Prog", "PROG", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnFpln = CreateButton("&Fpln", "FPLN", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnPerf = CreateButton("P&erf", "PERF", col, y, btnWidth, btnHeight);
        y += btnHeight + btnSpacing;
        col = 10;

        // Page buttons - row 2
        btnRadNav = CreateButton("&Rad Nav", "RAD_NAV", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnSecFpln = CreateButton("Sec Fpln", "SEC_FPLN", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnFuelPred = CreateButton("F&uel Pred", "FUEL_PRED", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnAtcCom = CreateButton("Atc &Kom", "ATC_COM", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnMenu = CreateButton("&Menu", "MENU", col, y, btnWidth, btnHeight);
        y += btnHeight + btnSpacing;
        col = 10;

        // Page buttons - row 3
        btnAirport = CreateButton("&Airport", "AIRPORT", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnData = CreateButton("Da&ta", "DATA", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;
        btnOverfly = CreateButton("&Ovfly", "OVFLY", col, y, btnWidth, btnHeight);
        col += btnWidth + btnSpacing;

        // Add all controls
        this.Controls.AddRange(new Control[]
        {
            connectionStatus, mcduSelector, mcduDisplay, scratchpadInput,
            btnInit, btnDir, btnProg, btnFpln, btnPerf,
            btnRadNav, btnSecFpln, btnFuelPred, btnAtcCom, btnMenu,
            btnAirport, btnData, btnOverfly
        });

        // Set tab order
        int tabIdx = 0;
        mcduSelector.TabIndex = tabIdx++;
        mcduDisplay.TabIndex = tabIdx++;
        scratchpadInput.TabIndex = tabIdx++;
        btnInit.TabIndex = tabIdx++;
        btnDir.TabIndex = tabIdx++;
        btnProg.TabIndex = tabIdx++;
        btnFpln.TabIndex = tabIdx++;
        btnPerf.TabIndex = tabIdx++;
        btnRadNav.TabIndex = tabIdx++;
        btnSecFpln.TabIndex = tabIdx++;
        btnFuelPred.TabIndex = tabIdx++;
        btnAtcCom.TabIndex = tabIdx++;
        btnMenu.TabIndex = tabIdx++;
        btnAirport.TabIndex = tabIdx++;
        btnData.TabIndex = tabIdx++;
        btnOverfly.TabIndex = tabIdx++;

        this.ResumeLayout(false);
    }

    private Button CreateButton(string text, string mcduButton, int x, int y, int width, int height)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height)
        };
        btn.Click += (s, e) => _ = _service.SendButtonPress(mcduButton);
        return btn;
    }

    private void SetupAccessibility()
    {
        this.AccessibleName = "Fenix MCDU";
        this.AccessibleDescription = "Fenix A320 MCDU display and controls";

        // Handle form closing: hide instead of dispose
        FormClosing += (sender, e) =>
        {
            // Let real app/OS shutdown through: Application.Exit raises FormClosing on
            // every open form (hidden included) and ABORTS the whole exit if any form
            // cancels — an unconditional cancel here left the auto-updater stalled
            // against a still-running exe. Everything else still hides.
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }

            e.Cancel = true;
            Hide();

            if (previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(previousWindow);
            }
        };

        // Scratchpad debounce timer
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

        // Submit scratchpad input on Enter
        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;

        // Left/Right arrow keys in display list navigate MCDU sections
        mcduDisplay.KeyDown += McduDisplay_KeyDown;

        // Form-level key handling
        this.KeyDown += Form_KeyDown;

        mcduSelector.SelectedIndexChanged += (s, e) => OnSideSelected();
    }

    private void OnSideSelected()
    {
        int side = mcduSelector.SelectedIndex + 1; // 0→1 (left), 1→2 (right)

        // Forget the old side's title and scratchpad so the new side's page title is spoken (and
        // its scratchpad, if not empty), then show its stored screen. SwitchMCDU raises
        // DisplayUpdated synchronously, so the list is rebuilt before this returns.
        _currentDisplay = null;
        _lastAnnouncedScratchpad = "";
        _lastAnnouncedTitle = "";
        mcduDisplay.Items.Clear();
        this.Text = $"Fenix MCDU ({SideName(side)})";
        // Off the selector, say which MCDU this is before its page title. On the selector the screen
        // reader has just read the new item, so the app says only the title ([CORE-7]).
        if (!mcduSelector.Focused)
            _announcer.Announce($"{SideName(side)} MCDU");
        _service.SwitchMCDU(side);
    }

    private static string SideName(int side) => side == FenixMcduSides.Right ? "Right" : "Left";

    private void McduDisplay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left)
        {
            _ = _service.SendButtonPress("ARROW_LEFT");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Right)
        {
            _ = _service.SendButtonPress("ARROW_RIGHT");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Back)
        {
            _ = _service.SendButtonPress("CLEAR");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.PageUp)
        {
            _ = _service.SendButtonPress("ARROW_UP");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.PageDown)
        {
            _ = _service.SendButtonPress("ARROW_DOWN");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// Ctrl+Shift+L / Ctrl+Shift+R → the MCDU side they select (<see cref="FenixMcduSides.Left"/> /
    /// <see cref="FenixMcduSides.Right"/>), or null for any other chord. Pure, so
    /// FenixMcduSideShortcutTests pins it without building the form.
    /// </summary>
    internal static class SideShortcut
    {
        public static int? For(Keys keyCode, bool control, bool shift, bool alt)
        {
            if (!control || !shift || alt) return null;
            return keyCode switch
            {
                Keys.L => FenixMcduSides.Left,
                Keys.R => FenixMcduSides.Right,
                _ => null,
            };
        }
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Shift+L / Ctrl+Shift+R: switch MCDU from anywhere in the window, the side selector
        // included (MD-11 precedent). Setting the selector runs its change handler, which says the
        // side. Choosing the side already shown fires no change event, so confirm it here instead
        // (off the selector only, as the handler does, and as the MD-11 does for its unit chords):
        // silence would sound like a dropped key.
        if (SideShortcut.For(e.KeyCode, e.Control, e.Shift, e.Alt) is int side)
        {
            if (side - 1 == mcduSelector.SelectedIndex)
            {
                if (!mcduSelector.Focused)
                    _announcer.Announce($"{SideName(side)} MCDU");
            }
            else
            {
                mcduSelector.SelectedIndex = side - 1;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // The side selector keeps its own keyboard interface (Alt+Up/Down and F4 open its list, a
        // letter jumps to an item): none of the window's MCDU keys below may take one from it. The
        // MD-11 window's rule, shared.
        if (ActiveControl == mcduSelector
            && MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey(
                e.KeyCode, e.Alt, e.Control, e.Shift, mcduSelector.DroppedDown))
        {
            return;
        }

        // Line-select keys — two layouts, switchable in FMC Settings:
        //   Default: Ctrl+1..6 = LSK1L..LSK6L, Alt+1..6 = LSK1R..LSK6R
        //   Alternate: F1..F6 = LSK1L..LSK6L, F7..F12 = LSK1R..LSK6R
        // The alternate layout frees Ctrl/Alt for other hotkeys; many TFM
        // users prefer it. Setting is read every keypress so a runtime
        // change in the FMC Settings dialog takes effect immediately.
        bool useAltKeys = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

        if (useAltKeys)
        {
            // F1..F6 = LSK1L..LSK6L
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
            {
                int lsk = e.KeyCode - Keys.F1 + 1;
                _ = _service.SendButtonPress($"LSK{lsk}L");
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            // F7..F12 = LSK1R..LSK6R
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12)
            {
                int lsk = e.KeyCode - Keys.F7 + 1;
                _ = _service.SendButtonPress($"LSK{lsk}R");
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }
        else
        {
            // Ctrl+1..6: left line select L1..L6
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                int lsk = e.KeyCode - Keys.D1 + 1;
                _ = _service.SendButtonPress($"LSK{lsk}L");
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            // Alt+1..6: right line select R1..R6
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                int lsk = e.KeyCode - Keys.D1 + 1;
                _ = _service.SendButtonPress($"LSK{lsk}R");
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        // Alt+Arrow keys for navigation
        if (e.Alt && e.KeyCode == Keys.Left)
        {
            _ = _service.SendButtonPress("ARROW_LEFT");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.Alt && e.KeyCode == Keys.Right)
        {
            _ = _service.SendButtonPress("ARROW_RIGHT");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.Alt && e.KeyCode == Keys.Up)
        {
            _ = _service.SendButtonPress("ARROW_UP");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.Alt && e.KeyCode == Keys.Down)
        {
            _ = _service.SendButtonPress("ARROW_DOWN");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+S: focus scratchpad input
        if (e.Alt && !e.Shift && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+Shift+F: SEC F-PLN
        if (e.Alt && e.Shift && e.KeyCode == Keys.F)
        {
            _ = _service.SendButtonPress("SEC_FPLN");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Alt+Home: focus output area
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            mcduDisplay.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            // Submit text to MCDU as individual button presses
            string text = scratchpadInput.Text.ToUpperInvariant();
            _ = SendTextToMCDU(text);
            scratchpadInput.Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private async Task SendTextToMCDU(string text)
    {
        // The side is read once, so the whole entry lands on the MCDU that was shown when Enter was
        // pressed, even if the pilot switches sides while it is still being typed in.
        int side = _service.SelectedSide;
        foreach (char c in text)
        {
            string? buttonName = c switch
            {
                >= 'A' and <= 'Z' => c.ToString(),
                >= '0' and <= '9' => c.ToString(),
                '.' => "DOT",
                '/' => "SLASH",
                '-' => "MINUS",
                ' ' => "SPACE",
                _ => null
            };

            if (buttonName != null)
            {
                await _service.SendButtonPress(buttonName, side);
                // Delay between presses to ensure the MCDU processes each press-release cycle
                await Task.Delay(50);
            }
        }
    }

    private void OnDisplayUpdated(MCDUDisplayData data)
    {
        _currentDisplay = data;

        // Build display text
        var lines = new List<string>();
        lines.Add($"Title: {data.Title}");

        for (int i = 0; i < 6; i++)
        {
            int labelLineIdx = 1 + (i * 2);  // Raw lines: 1, 3, 5, 7, 9, 11
            int valueLineIdx = 2 + (i * 2);  // Raw lines: 2, 4, 6, 8, 10, 12

            string labelLine = data.RawLines[labelLineIdx].TrimEnd();
            string valueLine = data.RawLines[valueLineIdx].TrimEnd();

            bool hasLabel = !string.IsNullOrWhiteSpace(labelLine);
            bool hasValue = !string.IsNullOrWhiteSpace(valueLine);

            // Show label line without number (provides context like "Log", "Status", etc.)
            if (hasLabel)
            {
                lines.Add($"   {labelLine}");
            }

            // Show value line with the line number (these are the selectable LSK items)
            if (hasValue)
            {
                lines.Add($"{i + 1}: {valueLine}");
            }

            // If neither label nor value, show empty numbered line
            if (!hasLabel && !hasValue)
            {
                lines.Add($"{i + 1}:");
            }
        }

        lines.Add($"Scratchpad: {data.Scratchpad}");

        // Update display - update individual items to trigger Braille display refresh
        int savedIndex = mcduDisplay.SelectedIndex;

        // Shared in-place reconcile (grow/shrink tail + rewrite changed rows, incl. first
        // populate). This form's own selection semantics run BELOW and override the helper's
        // content-based restore — CDU screens are positional (LSK rows), so index restore /
        // page force-select wins.
        Forms.DisplayList.UpdateInPlace(mcduDisplay, lines);

        // Announce page title changes and reset selection to first line
        string trimmedTitle = data.Title.Trim();
        bool titleChanged = !string.IsNullOrEmpty(trimmedTitle) && trimmedTitle != _lastAnnouncedTitle;

        if (titleChanged)
        {
            _lastAnnouncedTitle = trimmedTitle;
            _announcer.Announce(trimmedTitle);

            // Move focus to first content line when page changes
            if (mcduDisplay.Items.Count > 1)
                mcduDisplay.SelectedIndex = 1;
        }
        else
        {
            // Restore selection on same-page updates
            if (savedIndex >= 0 && savedIndex < mcduDisplay.Items.Count && mcduDisplay.SelectedIndex != savedIndex)
                mcduDisplay.SelectedIndex = savedIndex;
        }

        // Debounce scratchpad announcement
        if (data.Scratchpad != _lastAnnouncedScratchpad)
        {
            _scratchpadDebounceTimer?.Stop();
            _scratchpadDebounceTimer?.Start();
        }
    }

    private void OnConnectionStatusChanged(bool isConnected)
    {
        // One connection serves both MCDUs, so the messages name no side.
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
