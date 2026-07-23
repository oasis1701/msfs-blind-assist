using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect.IFly;
using System.Collections.Concurrent;

namespace MSFSBlindAssist.Forms.IFly737;

/// <summary>
/// Screen-reader-accessible CDU/FMC for the iFly 737 MAX8 (Shift+M).
///
/// The display renders the SDK shared-memory CDU screen (LSKChar 14 rows x 24
/// columns, with color/font metadata) polled from <see cref="IFlySdkClient"/>.
/// Keys are sent over the WM_COPYDATA command channel (FMS_CDU_1_* / FMS_CDU_2_*).
///
/// Layout mirrors the PMDG 737 CDU form: status label + unit selector, display
/// ListBox (row 0 = page title, rows 1-12 = six label/data pairs with data rows
/// prefixed "n:", row 13 = scratchpad), a dedicated SCRATCHPAD TEXTBOX (type,
/// Enter to send — Alt+S focuses it), and PAGE BUTTONS for every MAX CDU key
/// including Exec and CLR/DEL. Text entry goes through the scratchpad box ONLY
/// (type, then Enter commits — the PMDG-form behavior; direct display type-through
/// was removed 2026-07-23 by user ruling: keys must never reach the CDU while
/// the user navigates the display list or other controls).
/// Ctrl+1-6 / Alt+1-6 are the left/right LSKs (F1-F12 in alternate mode);
/// Alt-letter page chords match the PMDG form where the key exists (Alt+E EXEC,
/// Alt+G LEGS, Alt+A DEP ARR, Alt+C CLR/DEL) with iFly-specific extras kept
/// (Alt+L LEGS, Alt+D DEP ARR, Alt+T ATC, Alt+V VNAV, Alt+O FMC COMM, Alt+X DEL).
/// </summary>
public class IFly737CDUForm : Form
{
    private readonly IFlySdkClient _sdk;
    private readonly ScreenReaderAnnouncer _announcer;

    private readonly TextBox _statusBox;
    private readonly ListBox _display;
    private readonly TextBox _scratchpadInput;
    private readonly ComboBox _unitSelector;
    private readonly Label _helpLabel;
    private readonly System.Windows.Forms.Timer _pollTimer;

    private string _lastScreenHash = "";
    private string _lastTitle = "";
    private string _lastScratchpad = "";
    private bool _execLit, _msgLit;
    private bool _firstRender = true;
    private DateTime _suppressScratchpadUntil = DateTime.MinValue;
    private IntPtr _previousWindow;

    // Paced key queue: consecutive CDU keys are spaced so the FMC registers
    // repeated characters as separate presses.
    private readonly BlockingCollection<IFlyKeyCommand> _keyQueue = new();
    private readonly CancellationTokenSource _keyPumpCts = new();
    private const int KeyPumpSpacingMs = 80;

    private int CduUnit => _unitSelector.SelectedIndex == 1 ? 2 : 1;   // command channel: 1 = Captain, 2 = FO
    private int CduIndex => _unitSelector.SelectedIndex == 1 ? 1 : 0;  // shared memory index

    public IFly737CDUForm(IFlySdkClient sdk, ScreenReaderAnnouncer announcer)
    {
        _sdk = sdk;
        _announcer = announcer;

        Text = "iFly 737 FMC";
        Size = new Size(600, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AccessibleName = "iFly 737 FMC";

        int y = 10;

        _statusBox = new TextBox
        {
            Text = "FMC Not Connected",
            Location = new Point(10, y),
            Size = new Size(390, 20),
            AccessibleName = "C D U status",
            ReadOnly = true,
            TabStop = true,
            BorderStyle = BorderStyle.FixedSingle,
        };

        _unitSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(410, y),
            Width = 165,
            AccessibleName = "CDU Unit",
        };
        _unitSelector.Items.AddRange(new object[] { "Left (Captain)", "Right (First Officer)" });
        _unitSelector.SelectedIndex = 0;
        _unitSelector.SelectedIndexChanged += (_, _) => { _lastScreenHash = ""; RefreshDisplay(); };
        y += 30;

        _display = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(565, 280),
            Font = new Font("Consolas", 11f),
            IntegralHeight = false,
            AccessibleName = "FMC display",
        };
        y += 290;

        _scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(565, 25),
            AccessibleName = "Scratchpad (Alt+S, Enter to send)",
            CharacterCasing = CharacterCasing.Upper,
        };
        _scratchpadInput.KeyDown += ScratchpadInput_KeyDown;
        y += 35;

        // Page buttons — the MAX CDU key set, two rows of 7 (PMDG form layout).
        const int btnW = 78, btnH = 28, gap = 4;
        Button Mk(string text, string accessible, int col, int rowY, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(10 + col * (btnW + gap), rowY),
                Size = new Size(btnW, btnH),
                AccessibleName = accessible,
            };
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        Mk("Init Ref", "Init Ref (Alt+I)", 0, y, (_, _) => SendKey("INIT_REF"));
        Mk("RTE", "RTE (Alt+R)", 1, y, (_, _) => SendKey("RTE"));
        Mk("Legs", "Legs (Alt+G)", 2, y, (_, _) => SendKey("LEGS"));
        Mk("Dep/Arr", "Dep/Arr (Alt+A)", 3, y, (_, _) => SendKey("DEP_ARR"));
        Mk("ATC", "ATC (Alt+T)", 4, y, (_, _) => SendKey("ATC"));
        Mk("VNAV", "VNAV (Alt+V)", 5, y, (_, _) => SendKey("VNAV"));
        Mk("Fix", "Fix (Alt+F)", 6, y, (_, _) => SendKey("FIX"));
        y += btnH + gap;

        Mk("Hold", "Hold (Alt+H)", 0, y, (_, _) => SendKey("HOLD"));
        Mk("Prog", "Prog (Alt+P)", 1, y, (_, _) => SendKey("PROG"));
        Mk("FMC Comm", "FMC Comm (Alt+O)", 2, y, (_, _) => SendKey("FMC_COMM"));
        Mk("N1 Limit", "N1 Limit (Alt+N)", 3, y, (_, _) => SendKey("N1_LIMIT"));
        Mk("Menu", "Menu (Alt+M)", 4, y, (_, _) => SendKey("MENU"));
        Mk("Prev Page", "Prev Page (PageUp)", 5, y, (_, _) => SendKey("PREV_PAGE"));
        Mk("Next Page", "Next Page (PageDown)", 6, y, (_, _) => SendKey("NEXT_PAGE"));
        y += btnH + gap + 6;

        var execBtn = new Button
        {
            Text = "Exec",
            Location = new Point(10, y),
            Size = new Size(100, btnH),
            AccessibleName = "Execute (Alt+E)",
        };
        execBtn.Click += (_, _) => SendKey("EXEC");
        Controls.Add(execBtn);

        var clrBtn = new Button
        {
            Text = "CLR/DEL",
            Location = new Point(120, y),
            Size = new Size(100, btnH),
            AccessibleName = "CLR/DEL (Alt+C)",
        };
        clrBtn.Click += (_, _) => ClearOrDelete();
        Controls.Add(clrBtn);
        y += btnH + 8;

        _helpLabel = new Label
        {
            Location = new Point(10, y),
            Size = new Size(565, 34),
            Text = "Type in the scratchpad and press Enter to send. " +
                   "Ctrl+1-6 left keys, Alt+1-6 right keys, Alt+E EXEC, Alt+C CLR/DEL.",
            TabStop = false,
        };

        Controls.Add(_statusBox);
        Controls.Add(_unitSelector);
        Controls.Add(_display);
        Controls.Add(_scratchpadInput);
        Controls.Add(_helpLabel);

        // Logical tab order: status, display, scratchpad, unit selector, then the
        // buttons (already added in visual order).
        int tab = 0;
        _statusBox.TabIndex = tab++;
        _display.TabIndex = tab++;
        _scratchpadInput.TabIndex = tab++;
        _unitSelector.TabIndex = tab++;
        foreach (Control c in Controls)
            if (c is Button) c.TabIndex = tab++;

        KeyDown += Form_KeyDown;
        FormClosing += (s, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                RestorePreviousWindow();
            }
        };

        _pollTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _pollTimer.Tick += (_, _) => RefreshDisplay();

        // Key pump: 80 ms spacing between CDU key presses.
        Task.Run(async () =>
        {
            try
            {
                foreach (var cmd in _keyQueue.GetConsumingEnumerable(_keyPumpCts.Token))
                {
                    _sdk.SendCommand(cmd);
                    await Task.Delay(KeyPumpSpacingMs, _keyPumpCts.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    public void ShowForm()
    {
        _previousWindow = NativeMethods.GetForegroundWindow();
        _lastScreenHash = "";
        _firstRender = true;
        _pollTimer.Start();
        Show();
        Activate();
        _display.Focus();
        RefreshDisplay();
    }

    private void RestorePreviousWindow()
    {
        _pollTimer.Stop();
        if (_previousWindow != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(_previousWindow);
    }

    // ------------------------------------------------------------------
    // Rendering
    // ------------------------------------------------------------------

    private void RefreshDisplay()
    {
        var snap = _sdk.Snapshot;
        if (snap == null || !snap.IsRunning)
        {
            _statusBox.Text = "iFly 737 not detected";
            if (_display.Items.Count != 1 || (string)_display.Items[0] != "iFly 737 not detected.")
            {
                _display.Items.Clear();
                _display.Items.Add("iFly 737 not detected.");
            }
            return;
        }

        if (_statusBox.Text != "FMC Connected")
            _statusBox.Text = "FMC Connected";

        int unit = CduIndex;
        string screen = snap.CduScreenText(unit);
        string annun = $"{snap.CduExecLit(unit)}|{snap.CduMsgLit(unit)}";
        // The color plane (grey/inverse = code 4) is the only cue for the active option
        // on some pages — the character text alone can be unchanged while a selection
        // moves, so it must be part of the change hash or a highlight-only change would
        // never refresh the display.
        string colors = CduColorPlaneText(snap, unit);
        string hash = screen + annun + colors;
        if (hash == _lastScreenHash) return;
        _lastScreenHash = hash;

        string title = snap.CduLine(unit, 0).Trim();
        string scratchpad = snap.CduLine(unit, 13).Trim();

        var lines = new List<string> { title.Length > 0 ? title : "(no title)" };
        for (int pair = 0; pair < 6; pair++)
        {
            int labelRow = 1 + pair * 2;
            int dataRow = 2 + pair * 2;
            string label = MarkSelectedOption(snap, unit, labelRow, snap.CduLine(unit, labelRow).TrimEnd());
            string data = MarkSelectedOption(snap, unit, dataRow, snap.CduLine(unit, dataRow).TrimEnd());
            if (label.Trim().Length > 0)
                lines.Add("   " + label);
            lines.Add($"{pair + 1}: {data}");
        }
        lines.Add($"Scratchpad: {scratchpad}");
        // No "EXEC light on" screen row: the light's edge-announce below is the
        // whole interface, same as MSG — and the PMDG CDU forms render no light
        // rows either (user ruling 2026-07-23). The light state stays in the
        // change hash above so a light-only change still refreshes/announces.

        // Diff into the ListBox preserving the reading position.
        int selected = _display.SelectedIndex;
        _display.BeginUpdate();
        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (i < _display.Items.Count)
                {
                    if ((string)_display.Items[i] != lines[i])
                        _display.Items[i] = lines[i];
                }
                else
                {
                    _display.Items.Add(lines[i]);
                }
            }
            while (_display.Items.Count > lines.Count)
                _display.Items.RemoveAt(_display.Items.Count - 1);
            if (selected >= 0 && selected < _display.Items.Count)
                _display.SelectedIndex = selected;
        }
        finally
        {
            _display.EndUpdate();
        }

        // Announcements: page change, scratchpad change, annunciator edges.
        if (!_firstRender)
        {
            if (title != _lastTitle && title.Length > 0)
            {
                _announcer.Announce(title);
                _display.SelectedIndex = 0;
            }

            // Scratchpad: while typing (or while a queued text entry is still
            // being keyed) the change is NOT recorded, so the final content is
            // read back on the first poll after the suppression window expires.
            if (scratchpad != _lastScratchpad && DateTime.UtcNow >= _suppressScratchpadUntil)
            {
                _announcer.Announce(scratchpad.Length > 0 ? scratchpad : "Cleared");
                _lastScratchpad = scratchpad;
            }

            bool exec = snap.CduExecLit(unit);
            if (exec != _execLit)
                _announcer.Announce(exec ? "EXEC light on" : "EXEC light off");
            bool msg = snap.CduMsgLit(unit);
            if (msg != _msgLit)
                _announcer.Announce(msg ? "FMC message" : "FMC message cleared");
        }
        else
        {
            _lastScratchpad = scratchpad;
        }

        _lastTitle = title;
        _execLit = snap.CduExecLit(unit);
        _msgLit = snap.CduMsgLit(unit);
        _firstRender = false;
    }

    // ------------------------------------------------------------------
    // Color-based selection markers
    // ------------------------------------------------------------------

    /// <summary>Concatenates the raw color byte of every cell on the given unit's
    /// screen into one string, purely so <see cref="RefreshDisplay"/> can fold it
    /// into the change hash — a highlight-only change (e.g. a toggled selection)
    /// can leave every character on the screen identical.</summary>
    internal static string CduColorPlaneText(IFlySdkSnapshot snap, int unit)
    {
        var sb = new System.Text.StringBuilder(IFlySdkSnapshot.CduRows * IFlySdkSnapshot.CduCols);
        for (int row = 0; row < IFlySdkSnapshot.CduRows; row++)
            for (int col = 0; col < IFlySdkSnapshot.CduCols; col++)
                sb.Append((char)snap.CduColor(unit, row, col));
        return sb.ToString();
    }

    /// <summary>Marks the active option on a CDU row using the grey/inverse
    /// background cue (CduColor code 4 — see IFlySdkSnapshot's class comment).
    /// Unlike the PMDG 737 form (PMDG737CDUForm.MarkSelectedOption), where color is
    /// only known per adjacent-word pair around a "&lt;&gt;" toggle, the iFly SDK
    /// exposes color PER CELL, so this scans the row for every contiguous run of
    /// grey-background cells and marks each one — but uses the exact same marker
    /// convention (prefix the selected text with "X ", leave everything else
    /// untouched) so both 737 CDUs sound identical. Whitespace-only runs (a
    /// highlighted but empty field) are never marked.</summary>
    internal static string MarkSelectedOption(IFlySdkSnapshot snap, int unit, int row, string text)
    {
        var sb = new System.Text.StringBuilder(text.Length + 4);
        int i = 0;
        while (i < text.Length)
        {
            if (snap.CduColor(unit, row, i) == 4)
            {
                int start = i;
                while (i < text.Length && snap.CduColor(unit, row, i) == 4)
                    i++;
                string run = text.Substring(start, i - start);
                if (!string.IsNullOrWhiteSpace(run))
                    sb.Append("X ");
                sb.Append(run);
            }
            else
            {
                sb.Append(text[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Key handling
    // ------------------------------------------------------------------

    private void SendKey(string key)
    {
        if (Enum.TryParse<IFlyKeyCommand>($"FMS_CDU_{CduUnit}_{key}", out var cmd))
            _keyQueue.Add(cmd);
    }

    /// <summary>CDU key suffix for a typed character, or null when not a CDU character.</summary>
    private static string? MapChar(char c) => char.ToUpperInvariant(c) switch
    {
        >= 'A' and <= 'Z' and var u => u.ToString(),
        >= '0' and <= '9' and var u => u.ToString(),
        '.' => "PERIOD",
        '/' => "SLASH",
        '+' or '-' => "ADDMINUS",
        ' ' => "SP",
        _ => null,
    };

    /// <summary>Wipes the WHOLE scratchpad when it has content, or arms field
    /// deletion (DEL puts "DELETE" in the scratchpad) when it's empty. The iFly
    /// CLR key deletes ONE character (like the real FMC), so a full wipe queues
    /// one CLR per visible character — a message is cleared by the first press
    /// and any surplus presses land on an empty scratchpad as harmless no-ops.</summary>
    private void ClearOrDelete()
    {
        string current = _sdk.Snapshot?.CduLine(CduIndex, 13).Trim() ?? "";
        if (current.Length == 0)
        {
            _suppressScratchpadUntil = DateTime.UtcNow.AddMilliseconds(700);
            SendKey("DEL");
            return;
        }
        for (int i = 0; i < current.Length; i++)
            SendKey("CLR");
        // Hold the per-poll announce until the whole CLR burst has been keyed.
        _suppressScratchpadUntil = DateTime.UtcNow.AddMilliseconds(current.Length * KeyPumpSpacingMs + 500);
    }

    // NOTE: no Display_KeyPress type-through — text entry is scratchpad-box-only
    // (PMDG-form parity, user ruling 2026-07-23): typed characters while the
    // display list or any other control has focus must NOT reach the CDU.

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return)
        {
            string text = _scratchpadInput.Text;
            _scratchpadInput.Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;

            int queued = 0;
            foreach (char c in text)
            {
                string? key = MapChar(c);
                if (key != null && Enum.TryParse<IFlyKeyCommand>($"FMS_CDU_{CduUnit}_{key}", out var cmd))
                {
                    _keyQueue.Add(cmd);
                    queued++;
                }
            }
            // Suppress per-poll chatter until the queue has drained; the first
            // poll after that reads the final scratchpad content back.
            _suppressScratchpadUntil = DateTime.UtcNow.AddMilliseconds(queued * KeyPumpSpacingMs + 400);
        }
        // Backspace on an empty box (CLR) is handled globally in Form_KeyDown.
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        bool alternate = SettingsManager.Current.MCDUUseAlternateLSKKeys;

        // LSK keys.
        if (!alternate && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6 && (e.Control || e.Alt))
        {
            int n = e.KeyCode - Keys.D0;
            SendKey(e.Control ? $"LSK_L{n}" : $"LSK_R{n}");
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (alternate && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F12 && !e.Control && !e.Alt)
        {
            int f = e.KeyCode - Keys.F1 + 1;
            SendKey(f <= 6 ? $"LSK_L{f}" : $"LSK_R{f - 6}");
            e.Handled = true;
            return;
        }

        // Function/page keys. PMDG-form parity chords (Alt+E EXEC, Alt+G LEGS,
        // Alt+A DEP ARR, Alt+C CLR/DEL, Alt+S scratchpad) plus the iFly-specific
        // set (ATC, VNAV, FMC COMM, DEL).
        if (e.Alt && !e.Control && !e.Shift)
        {
            switch (e.KeyCode)
            {
                case Keys.E:
                    SendKey("EXEC");
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.C:
                    ClearOrDelete();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.S:
                    _scratchpadInput.Focus();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                case Keys.Home:
                    _display.Focus();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
            }

            string? page = e.KeyCode switch
            {
                Keys.I => "INIT_REF",
                Keys.R => "RTE",
                Keys.D or Keys.A => "DEP_ARR",
                Keys.T => "ATC",
                Keys.V => "VNAV",
                Keys.F => "FIX",
                Keys.L or Keys.G => "LEGS",
                Keys.H => "HOLD",
                Keys.O => "FMC_COMM",
                Keys.P => "PROG",
                Keys.M => "MENU",
                Keys.N => "N1_LIMIT",
                Keys.X => "DEL",
                _ => null,
            };
            if (page != null)
            {
                SendKey(page);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        switch (e.KeyCode)
        {
            case Keys.PageUp:
                SendKey("PREV_PAGE");
                e.Handled = true;
                break;
            case Keys.PageDown:
                SendKey("NEXT_PAGE");
                e.Handled = true;
                break;
            case Keys.Enter when e.Control:
                SendKey("EXEC");
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            // Backspace = CLR/DEL only while the DISPLAY is focused, or in the
            // scratchpad box when it is empty (PMDG-form parity — a Backspace on
            // the unit selector or a page button must not clear the scratchpad;
            // while editing scratchpad text it edits that text normally).
            case Keys.Back when ActiveControl == _display
                             || (ActiveControl == _scratchpadInput && _scratchpadInput.Text.Length == 0):
                ClearOrDelete();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Delete when ActiveControl == _display:
                SendKey("DEL");
                e.Handled = true;
                break;
            case Keys.Escape:
                Hide();
                RestorePreviousWindow();
                e.Handled = true;
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pollTimer.Dispose();
            _keyPumpCts.Cancel();
            _keyQueue.CompleteAdding();
            _keyPumpCts.Dispose();
        }
        base.Dispose(disposing);
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
