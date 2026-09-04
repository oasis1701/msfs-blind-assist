using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Forms.MD11;

/// <summary>
/// Accessible MCDU for the TFDi Design MD-11.
///
/// WHERE THE TEXT COMES FROM. Not a DOM scrape — the MD-11's screens are WASM-rendered canvases
/// with no HTML behind them, which is why the Coherent transport used for the PMDG / FBW / HS787
/// CDUs cannot work here. TFDi instead export all three MCDUs as plain text over the SimConnect
/// client data area <c>MD11MCDU</c>; <see cref="Md11McduDataManager"/> decodes it. "No DOM" and
/// "unreadable" are different claims, and conflating them once cost this aircraft its CDU.
///
/// THREE UNITS, unlike every other CDU in this app. The MD-11 has Left (Captain), Center and Right
/// (First Officer) MCDUs, and the export carries all three independently — so this form has a unit
/// selector rather than being hardwired to the Captain's. The three key sets are identical
/// (74 nodes each), so one layout serves all three; only the node-id prefix changes.
///
/// WHY IT POLLS. The manager is recreated on every aircraft switch and only exists once SimConnect
/// is connected, so subscribing to its event would bind this form to one instance and silently go
/// deaf if the user opened the window before connecting. Reading the manager's cached screen on a
/// timer is immune to both, costs a reference compare per tick when nothing has changed, and is
/// what the PMDG CDU form already does. The underlying request is ON_SET/CHANGED, so the sim only
/// delivers on a real change regardless.
/// </summary>
public class Md11McduForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly TFDiMD11Definition _definition;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow = IntPtr.Zero;

    private ComboBox unitSelector = null!;
    private ListBox mcduDisplay = null!;
    private TextBox scratchpadInput = null!;
    private Label statusLabel = null!;

    private System.Windows.Forms.Timer? _pollTimer;
    private System.Windows.Forms.Timer? _scratchpadDebounceTimer;

    private Md11McduUnit _unit = Md11McduUnit.Left;
    private Md11McduScreen? _screen;
    private object? _lastRendered;
    private string _lastAnnouncedTitle = "";
    private string _lastAnnouncedScratchpad = "";
    private string _lastAnnouncedFlags = "";

    /// <summary>The MD-11's MCDU is a 14-row grid: title, six label/value pairs, scratchpad.</summary>
    private const int TitleRow = 0;
    private const int ScratchpadRow = Md11McduLayout.Rows - 1;   // 13
    private const int LskRows = 6;

    public Md11McduForm(TFDiMD11Definition definition, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _definition = definition;
        _sim = sim;
        _announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
        SetupEventHandlers();
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "MD-11 MCDU";
        this.ClientSize = new Size(620, 780);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;

        int y = 10;

        unitSelector = new ComboBox
        {
            Location = new Point(10, y),
            Size = new Size(220, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU unit",
            AccessibleDescription = "Which of the three MD-11 MCDUs to display and control."
        };
        unitSelector.Items.AddRange(new object[] { "Left (Captain)", "Center", "Right (First Officer)" });
        unitSelector.SelectedIndex = 0;

        statusLabel = new Label
        {
            Text = "MCDU: waiting for data",
            Location = new Point(240, y + 3),
            Size = new Size(370, 20),
            AccessibleName = "MCDU status",
            AccessibleDescription = "Connection state and lit MCDU annunciators."
        };
        y += 32;

        mcduDisplay = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 290),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Current MCDU screen. Use arrow keys to read lines.",
            IntegralHeight = false
        };
        y += 300;

        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 25),
            AccessibleName = "MCDU Input",
            AccessibleDescription = "Type text and press Enter to send it to the MCDU scratchpad."
        };
        y += 34;

        int btnWidth = 116, btnHeight = 30, btnSpacing = 5, perRow = 5;
        var buttons = new List<Control>();
        for (int i = 0; i < Md11McduKeys.PageButtons.Length; i++)
        {
            var (label, key) = Md11McduKeys.PageButtons[i];
            var btn = new Button
            {
                Text = label,
                Location = new Point(10 + (i % perRow) * (btnWidth + btnSpacing),
                                     y + (i / perRow) * (btnHeight + btnSpacing)),
                Size = new Size(btnWidth, btnHeight),
            };
            btn.Click += (s, e) => PressKey(key);
            buttons.Add(btn);
        }

        this.Controls.Add(unitSelector);
        this.Controls.Add(statusLabel);
        this.Controls.Add(mcduDisplay);
        this.Controls.Add(scratchpadInput);
        foreach (var b in buttons) this.Controls.Add(b);

        int tabIdx = 0;
        mcduDisplay.TabIndex = tabIdx++;
        scratchpadInput.TabIndex = tabIdx++;
        unitSelector.TabIndex = tabIdx++;
        foreach (var b in buttons) b.TabIndex = tabIdx++;

        this.ResumeLayout(false);
    }

    private void SetupAccessibility()
    {
        this.AccessibleName = "MD-11 MCDU";
        this.AccessibleDescription = "TFDi Design MD-11 MCDU display and controls";

        FormClosing += (sender, e) =>
        {
            e.Cancel = true;
            Hide();
            if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
        };

        _scratchpadDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _scratchpadDebounceTimer.Tick += (s, e) =>
        {
            _scratchpadDebounceTimer.Stop();
            var pad = _screen?.Scratchpad.Trim() ?? "";
            if (pad == _lastAnnouncedScratchpad) return;
            _lastAnnouncedScratchpad = pad;
            _announcer.Announce(string.IsNullOrEmpty(pad) ? "Scratchpad cleared" : pad);
        };

        // 250 ms: fast enough that a keypress feels immediate, slow enough to be free. A tick
        // where nothing arrived costs one reference compare.
        _pollTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _pollTimer.Tick += (s, e) => Poll();
        _pollTimer.Start();
    }

    private void SetupEventHandlers()
    {
        unitSelector.SelectedIndexChanged += (s, e) =>
        {
            _unit = (Md11McduUnit)unitSelector.SelectedIndex;
            // Re-render the newly selected unit at once rather than waiting for the next tick.
            // Suppress the title announce: the screen reader already spoke the combo change, and
            // re-announcing the page title on top of it is exactly the double-announce the panel
            // rules forbid. Adopt the new title silently so a LATER genuine page change still fires.
            _lastRendered = null;
            Render(silentTitle: true);
        };

        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;
        mcduDisplay.KeyDown += McduDisplay_KeyDown;
        this.KeyDown += Form_KeyDown;
    }

    // ---------------------------------------------------------------------------------
    // Key actuation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Presses one MCDU key on the SELECTED unit. <paramref name="key"/> is the node-id suffix
    /// ("INIT", "LSK_3L", "A", "7", …); the unit prefix is applied here so callers never build a
    /// node id by hand.
    ///
    /// A press that cannot be delivered is SPOKEN, never swallowed — see PressControl. The pilot
    /// cannot see the screen, so a dropped key would otherwise look exactly like a working one.
    /// </summary>
    private void PressKey(string key)
    {
        if (!_definition.PressControl(Md11McduKeys.NodeId(_unit, key)))
            _announcer.Announce($"{key} key unavailable");
    }

    private void McduDisplay_KeyDown(object? sender, KeyEventArgs e)
    {
        // Backspace = a single CLR — delete one character, matching the hardware key (verified
        // live: one CLR press removes one character, and a held CLR removes only one too). Only
        // from the display, because the scratchpad box needs Backspace to edit its own text.
        if (e.KeyCode == Keys.Back)
        {
            PressKey("CLR");
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// Clears the whole MCDU scratchpad in one keystroke.
    ///
    /// The MD-11's CLR key deletes ONE character per press — there is no clear-all key, and a held
    /// CLR still removes only one (both verified live). So this fires CLR repeatedly, reading the
    /// exported scratchpad back between presses and STOPPING the instant it is empty. Reading back
    /// is what makes it safe for both cases: typed text (N characters → N presses) and a scratchpad
    /// MESSAGE (one press clears it), without over-deleting into whatever the FMS shows next.
    /// </summary>
    private async void ClearScratchpad()
    {
        var manager = _sim.Md11McduDataManager;
        if (manager == null) { _announcer.Announce("Not connected"); return; }

        // Cap at the scratchpad width plus a margin — a backstop against an unreadable feed, so a
        // stuck read can never turn this into an unbounded CLR storm at the aircraft.
        for (var i = 0; i < Md11McduLayout.Cols + 4; i++)
        {
            var screen = manager.GetScreen(_unit);
            if (screen == null || string.IsNullOrWhiteSpace(screen.Scratchpad))
            {
                _announcer.Announce("Scratchpad cleared");
                return;
            }
            PressKey("CLR");
            // Long enough for the press to register AND the CHANGED client-data delivery to land,
            // so the next iteration reads the post-delete scratchpad rather than the stale one.
            await Task.Delay(150);
        }

        _announcer.Announce("Scratchpad cleared");
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        // Line-select keys — two layouts, switchable in FMC Settings. Read the setting on every
        // press so a change takes effect live, matching every other CDU form in this app:
        //   Default:   Ctrl+1..6 = L1..L6, Alt+1..6 = R1..R6
        //   Alternate: F1..F6    = L1..L6, F7..F12  = R1..R6
        bool useAltKeys = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

        if (useAltKeys)
        {
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
            {
                PressKey(Md11McduKeys.Lsk(e.KeyCode - Keys.F1 + 1, right: false));
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            if (!e.Control && !e.Alt && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12)
            {
                PressKey(Md11McduKeys.Lsk(e.KeyCode - Keys.F7 + 1, right: true));
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
        }
        else
        {
            if (e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                PressKey(Md11McduKeys.Lsk(e.KeyCode - Keys.D1 + 1, right: false));
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
            {
                PressKey(Md11McduKeys.Lsk(e.KeyCode - Keys.D1 + 1, right: true));
                e.Handled = true; e.SuppressKeyPress = true; return;
            }
        }

        // Slew. The MD-11 has UP/DOWN slew keys and a single NEXT PAGE key — there is no PREV
        // PAGE on this aircraft, so Alt+Left is deliberately unbound rather than faked.
        if (e.KeyCode == Keys.PageUp || (e.Alt && e.KeyCode == Keys.Up))
        {
            PressKey("UP");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.KeyCode == Keys.PageDown || (e.Alt && e.KeyCode == Keys.Down))
        {
            PressKey("DOWN");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.Alt && e.KeyCode == Keys.Right)
        {
            PressKey("NEXTPAGE");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Alt+Shift+F = SEC FPLN (Alt+F is Fpln) — same chord as the Fenix/FBW forms.
        if (e.Alt && e.Shift && e.KeyCode == Keys.F)
        {
            PressKey("SEC_FPLN");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Delete = clear the WHOLE scratchpad. Backspace is a single CLR (one character); Delete is
        // the accessible shortcut for "empty it", which the hardware has no single key for.
        if (e.KeyCode == Keys.Delete)
        {
            ClearScratchpad();
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Ctrl+Shift+L/C/R — switch unit without leaving the keyboard.
        if (e.Control && e.Shift && (e.KeyCode == Keys.L || e.KeyCode == Keys.C || e.KeyCode == Keys.R))
        {
            unitSelector.SelectedIndex = e.KeyCode switch
            {
                Keys.L => 0,
                Keys.C => 1,
                _ => 2,
            };
            _announcer.Announce(unitSelector.SelectedItem?.ToString() ?? "");
            e.Handled = true; e.SuppressKeyPress = true; return;
        }

        // Alt+S = focus scratchpad, Alt+Home = focus display.
        if (e.Alt && !e.Shift && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            mcduDisplay.Focus();
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return) return;

        SendTextToMcdu(scratchpadInput.Text.ToUpperInvariant());
        scratchpadInput.Clear();
        e.Handled = true; e.SuppressKeyPress = true;
    }

    /// <summary>
    /// Types a string into the scratchpad, one key at a time — the MCDU has no "set text" input.
    ///
    /// No delay here on purpose: Md11EventBus owns pacing (it is a single shared CEVENT slot the
    /// aircraft itself also uses) and serializes every press through one queue. A second pacing
    /// layer in the form would just make typing slower without making it safer. The scratchpad is
    /// 24 columns, so a full line is well inside the bus's queue bound.
    /// </summary>
    private void SendTextToMcdu(string text)
    {
        foreach (char c in text)
        {
            var key = Md11McduKeys.ForChar(c);
            if (key != null) PressKey(key);
        }
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    private void Poll()
    {
        var manager = _sim.Md11McduDataManager;
        if (manager == null)
        {
            statusLabel.Text = "MCDU: not connected";
            return;
        }

        var screen = manager.GetScreen(_unit);
        if (screen == null)
        {
            statusLabel.Text = "MCDU: waiting for data";
            return;
        }

        // The manager only builds a new screen object when the sim delivers, and the underlying
        // request is CHANGED-only — so an unchanged reference means nothing happened.
        if (ReferenceEquals(screen, _lastRendered)) return;

        _screen = screen;
        Render(silentTitle: false);
    }

    private void Render(bool silentTitle)
    {
        var manager = _sim.Md11McduDataManager;
        var screen = _screen ?? manager?.GetScreen(_unit);
        if (screen == null) return;

        _screen = screen;
        _lastRendered = screen;

        var lines = new List<string>(Md11McduLayout.Rows + 2)
        {
            $"Title: {screen.Lines[TitleRow].Trim()}"
        };

        // Six label/value pairs. The label row is unnumbered and sits above its value; the value
        // row carries the LSK number, so "3:" is what Ctrl+3 / Alt+3 acts on. Blank label rows are
        // dropped — a run of empty lines is noise to arrow through — but a blank VALUE row is kept,
        // because an empty LSK row is a real, selectable state on a CDU.
        for (int i = 0; i < LskRows; i++)
        {
            var label = screen.Lines[1 + 2 * i].TrimEnd();
            var value = screen.Lines[2 + 2 * i].TrimEnd();

            if (!string.IsNullOrWhiteSpace(label)) lines.Add("   " + label);
            lines.Add($"{i + 1}: {value}");
        }

        lines.Add($"Scratchpad: {screen.Lines[ScratchpadRow].Trim()}");

        int savedIndex = mcduDisplay.SelectedIndex;
        // Shared in-place reconcile. This form's selection semantics run below and override the
        // helper's content-based restore: a CDU screen is positional (LSK rows), so index restore
        // and the page force-select win.
        Forms.DisplayList.UpdateInPlace(mcduDisplay, lines);

        UpdateStatus(screen);

        var title = screen.Lines[TitleRow].Trim();
        bool titleChanged = !string.IsNullOrEmpty(title) && title != _lastAnnouncedTitle;
        if (titleChanged)
        {
            _lastAnnouncedTitle = title;
            if (!silentTitle) _announcer.Announce(title);
            if (mcduDisplay.Items.Count > 1) mcduDisplay.SelectedIndex = 1;
        }
        else if (savedIndex >= 0 && savedIndex < mcduDisplay.Items.Count && mcduDisplay.SelectedIndex != savedIndex)
        {
            mcduDisplay.SelectedIndex = savedIndex;
        }

        if (screen.Scratchpad.Trim() != _lastAnnouncedScratchpad)
        {
            _scratchpadDebounceTimer?.Stop();
            _scratchpadDebounceTimer?.Start();
        }
    }

    /// <summary>
    /// The four MCDU annunciators come from the export's own flags, not from the four *_LT lamp
    /// L:vars — same fact, one fewer data definition each, and always in step with the text they
    /// arrived with.
    ///
    /// MSG lighting up is a real event with NO text change behind it, so it is announced rather
    /// than only shown: it is how the FMS says "read the scratchpad", and a blind pilot has no
    /// other way to notice it.
    /// </summary>
    private void UpdateStatus(Md11McduScreen screen)
    {
        var lit = new List<string>(4);
        if (screen.Msg) lit.Add("MSG");
        if (screen.Fail) lit.Add("FAIL");
        if (screen.Dspy) lit.Add("DSPY");
        if (screen.Ofst) lit.Add("OFST");

        var flags = string.Join(", ", lit);
        statusLabel.Text = lit.Count == 0 ? "MCDU: connected" : "MCDU: " + flags;

        if (flags != _lastAnnouncedFlags)
        {
            // Announce only what came ON. A lamp going out is not something a pilot needs told.
            var previous = _lastAnnouncedFlags;
            _lastAnnouncedFlags = flags;
            var added = lit.Where(f => !previous.Contains(f, StringComparison.Ordinal)).ToList();
            if (added.Count > 0) _announcer.Announce(string.Join(", ", added));
        }
    }

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

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
            // Close() is cancelled by the hide-on-close guard above, so OnFormClosed never runs —
            // teardown has to live here or the timers outlive the aircraft switch.
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
            _scratchpadDebounceTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
