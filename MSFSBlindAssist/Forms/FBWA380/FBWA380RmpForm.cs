using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible Radio Management Panel (RMP) window for the FlyByWire A380X (Ctrl+Shift+R, input
/// mode), modelled on the accessible MCDU/CDU forms. The live RMP touchscreen is scraped
/// (coherent-rmp-agent.js via <see cref="CoherentDisplayClient"/>) into a read-only TEXT box the
/// user reads with the arrows; it auto-refreshes and announces the selected radio's standby and
/// any RMP message (e.g. "VHF FREQ NOT VALID") as they change, debounced like the MCDU scratchpad.
///
/// Everything is a KEYBOARD SHORTCUT (no button clutter), mirroring the MCDU soft-key scheme:
///   Ctrl+1/2/3  = the LEFT line keys (LSK) — select radio 1/2/3, or load the typed standby.
///   Alt+1/2/3   = the RIGHT keys (ADK) — swap that radio's active ↔ standby (manual).
///   (or F1/2/3 = LSK, F7/8/9 = ADK when MCDUUseAlternateLSKKeys is on.)
///   Alt+V / Alt+T = the VHF / Transponder (SQWK) pages (the only two the FBW build models).
///   Just TYPE the digits on the screen itself (no separate field): each is keyed into the RMP
///   LIVE (the box is read-only, so the digits go to the radio, not the text); Enter loads the standby;
///   Backspace deletes a digit; Alt+C does a full clear; Alt+Home jumps back to the screen.
/// Captain ↔ First Officer is a combo (re-points the scrape + the H-event index).
/// </summary>
public sealed class FBWA380RmpForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FlyByWireA380Definition _def;
    private readonly SimConnectManager _sim;

    private CoherentDisplayClient _disp = null!;
    private int _rmp = 1;                  // 1 = Captain, 2 = First Officer
    private bool _haveRows;
    private int _selectedRowIndex;        // transceiver row the RMP has selected (0..2)
    private bool _busy;                   // held-clear running

    // Announce-on-change (debounced, like the MCDU scratchpad) — selected standby + message line.
    private bool _firstScrape = true;
    private string _standby = "", _lastAnnouncedStandby = "";
    private string _message = "", _lastAnnouncedMessage = "";
    private System.Windows.Forms.Timer? _announceTimer;
    private System.Windows.Forms.Timer? _refreshTimer;

    private ComboBox _side = null!;
    private TextBox _display = null!;
    private Label _status = null!;

    public FBWA380RmpForm(ScreenReaderAnnouncer announcer, FlyByWireA380Definition def, SimConnectManager sim)
    {
        _announcer = announcer; _def = def; _sim = sim;
        BuildUi();
        StartScrape();
    }

    private void BuildUi()
    {
        Text = "A380 Radio Management Panel";
        Size = new Size(620, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        var sideLabel = new Label { Text = "&Side:", Location = new Point(12, 14), Size = new Size(40, 22) };
        _side = new ComboBox { Location = new Point(56, 11), Size = new Size(170, 24), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Side" };
        _side.Items.AddRange(new object[] { "Captain", "First Officer" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += (_, _) => { _rmp = _side.SelectedIndex == 1 ? 2 : 1; SwitchSide(); };
        _status = new Label { Location = new Point(240, 14), Size = new Size(360, 22), Text = "Connecting…", AccessibleName = "Status" };

        // The RMP screen IS the input surface (like the real touchscreen): read it with the
        // arrows, and just TYPE the frequency / squawk digits right here — each digit is keyed
        // into the RMP live (the box is read-only, so the digits never change its text; they go
        // to the radio). Enter loads the standby, Backspace deletes a digit. No separate field.
        _display = new TextBox
        {
            Location = new Point(12, 44), Size = new Size(584, 340),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11), AccessibleName = "RMP screen — type digits to enter a frequency", Text = "Loading…"
        };
        _display.KeyPress += OnDigitKeyPress;
        _display.KeyDown += OnDisplayKeyDown;

        // Page selectors, in the tab order. The FBW A380 RMP only models the VHF and Transponder
        // (SQWK) pages — HF / TEL / NAV / MENU are not implemented on this build, so they are not
        // offered (pressing those keys does nothing).
        var pVhf = new Button { Text = "&VHF page", Location = new Point(12, 390), Size = new Size(130, 28), AccessibleName = "VHF page" };
        pVhf.Click += (_, _) => Page("VHF", "VHF page");
        var pSqwk = new Button { Text = "&Transponder page", Location = new Point(150, 390), Size = new Size(160, 28), AccessibleName = "Transponder page" };
        pSqwk.Click += (_, _) => Page("SQWK", "Transponder page");

        var help = new Label
        {
            Location = new Point(12, 424), Size = new Size(584, 44),
            Text = "Type the frequency / squawk digits right on the screen. Enter loads · Backspace deletes.\n" +
                   "Ctrl+1/2/3 select radio · Alt+1/2/3 swap · Alt+V / Alt+T pages · Alt+C clear (SQWK: Alt+3 ident)."
        };
        var close = new Button { Text = "&Close", Location = new Point(12, 472), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();   // hide, don't dispose — keeps the scrape warm for instant reopen

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _display, pVhf, pSqwk, help, close });
    }

    // Focus the screen (not the Side combo) every time the window is shown, and pause/resume
    // the scrape with visibility so a hidden window costs nothing. Showing forces an instant
    // refresh so the screen is current the moment it appears.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _disp?.SetActive(true);
            ActiveControl = _display;
            _display.Focus();
            _ = InitialScrape();
        }
        else _disp?.SetActive(false);
    }

    // The window is reused across opens (Ctrl+Shift+R), so the X / Alt+F4 just HIDES it and
    // keeps the Coherent connection alive — only a real app/aircraft teardown disposes it.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    // ---- keyboard: the soft keys + pages, MCDU-style -----------------------------------------

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        bool alt = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

        // Line keys (LSK, left) and adjacent keys (ADK, right = swap), rows 1..3.
        if (alt)
        {
            if (NoMods(e) && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F3) { PressLine(e.KeyCode - Keys.F1); Handled(e); return; }
            if (NoMods(e) && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F9) { Swap(e.KeyCode - Keys.F7); Handled(e); return; }
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D3) { PressLine(e.KeyCode - Keys.D1); Handled(e); return; }
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D3) { Swap(e.KeyCode - Keys.D1); Handled(e); return; }
        }

        if (e.Alt && !e.Control)
        {
            switch (e.KeyCode)
            {
                // Only VHF + Transponder (SQWK) pages are modelled by the FBW A380 dev build.
                // Alt+T matches the "&Transponder page" button mnemonic (one key, no duplicate).
                case Keys.V: Page("VHF", "VHF page"); Handled(e); return;
                case Keys.T: Page("SQWK", "Transponder page"); Handled(e); return;
                case Keys.C: _ = FullClear(); Handled(e); return;
                case Keys.Home: _display.Focus(); Handled(e); return;   // jump back to the screen from a button
            }
        }
    }

    private static bool NoMods(KeyEventArgs e) => !e.Control && !e.Alt && !e.Shift;
    private static void Handled(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

    private void PressLine(int row)
    {
        if (_busy) return;
        bool wasSel = _selectedRowIndex == row;
        _def.SendRmpKey(_rmp, $"LSK_{row + 1}", _sim);
        _announcer?.Announce(wasSel ? "Standby loaded" : $"Radio {row + 1}");
        ScheduleRefresh();
    }

    private void Swap(int row)
    {
        if (_busy) return;
        _def.SendRmpKey(_rmp, $"ADK_{row + 1}", _sim);
        _announcer?.Announce("Swapped");
        ScheduleRefresh();
    }

    private void Page(string key, string spoken)
    {
        if (_busy) return;
        _def.SendRmpKey(_rmp, key, _sim);
        _announcer?.Announce(spoken);
        ScheduleRefresh();
    }

    // ---- digit entry on the screen itself: type digits, Enter = load -------------------------

    // The display is read-only, so typing never edits its text — we intercept the keystrokes and
    // key them straight into the RMP (digits keyed live, Enter loads the standby, Backspace
    // deletes a digit). Arrow keys still read the screen normally.
    private void OnDigitKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar >= '0' && e.KeyChar <= '9')
        {
            _def.SendRmpKey(_rmp, $"DIGIT_{e.KeyChar}", _sim);   // keyed live; the standby auto-completes
            e.Handled = true;                                    // swallow so there's no error ding
            // No per-keystroke scrape here — that stacked a Coherent round-trip on every digit and
            // made fast typing lag. The auto-poll (RowsUpdated) refreshes the screen within one tick.
        }
    }

    private void OnDisplayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            _def.SendRmpKey(_rmp, $"LSK_{_selectedRowIndex + 1}", _sim);   // load standby (manual; no auto-swap)
            _announcer?.Announce("Standby loaded");
            ScheduleRefresh();
        }
        else if (e.KeyCode == Keys.Back)
        {
            e.SuppressKeyPress = true;
            _def.SendRmpKey(_rmp, "DIGIT_CLR", _sim);   // one-digit backspace in the RMP entry
            ScheduleRefresh();
        }
    }

    // ---- held clear (full reset of a stuck entry) -------------------------------------------

    private async Task FullClear()
    {
        if (_busy) return;
        _busy = true;
        try { _def.SendRmpKeyPress(_rmp, "DIGIT_CLR", _sim); await Task.Delay(1150); }
        finally { _def.SendRmpKeyRelease(_rmp, "DIGIT_CLR", _sim); _busy = false; }
        _announcer?.Announce("Cleared");
        Apply(await _disp.ScrapeNowAsync());
    }

    // ---- scrape + display -------------------------------------------------------------------

    private void StartScrape()
    {
        _haveRows = false; _firstScrape = true;
        _lastAnnouncedStandby = ""; _lastAnnouncedMessage = "";
        _disp = new CoherentDisplayClient($"A380X_RMP_{_rmp}", 300, "coherent-rmp-agent.js");
        _disp.RowsUpdated += OnRowsUpdated;
        _disp.Start();
        _ = InitialScrape();
    }

    private void SwitchSide()
    {
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        _status.Text = _rmp == 1 ? "Captain — connecting…" : "First Officer — connecting…";
        StartScrape();
    }

    private async Task InitialScrape() => Apply(await _disp.ScrapeNowAsync());

    private void ScheduleRefresh()
    {
        if (_refreshTimer == null)
        {
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _refreshTimer.Tick += async (_, _) => { _refreshTimer!.Stop(); Apply(await _disp.ScrapeNowAsync()); };
        }
        _refreshTimer.Stop(); _refreshTimer.Start();
    }

    private void OnRowsUpdated(List<string> rows) => Apply(rows);

    private void Apply(List<string>? rows)
    {
        if (rows == null || rows.Count == 0)
        {
            if (!_haveRows) _status.Text = "RMP not reachable — is the A380X loaded and powered?";
            return;
        }
        _haveRows = true;
        _status.Text = _rmp == 1 ? "Live — Captain" : "Live — First Officer";

        // Render the RMP as plain text (the touchscreen IS text), preserving the read caret.
        string text = string.Join("\r\n", rows);
        if (_display.Text != text)
        {
            int caret = _display.SelectionStart;
            _display.Text = text;
            _display.SelectionStart = Math.Min(caret, _display.TextLength);
        }

        // Track the selected row + its standby + the message line for the announce.
        _selectedRowIndex = 0;
        int tIdx = 0; string selRow = "";
        foreach (var r in rows)
        {
            if (r.IndexOf(": active ", StringComparison.Ordinal) < 0) continue;
            if (r.IndexOf(", selected", StringComparison.Ordinal) >= 0) { _selectedRowIndex = tIdx; selRow = r; }
            tIdx++;
        }
        _standby = Token(selRow, "standby ");
        var msgRow = rows.Find(r => r.StartsWith("Message: ", StringComparison.Ordinal)) ?? "";
        _message = msgRow.Length > 0 ? msgRow.Substring("Message: ".Length) : "";

        if (_firstScrape) { _lastAnnouncedStandby = _standby; _lastAnnouncedMessage = _message; _firstScrape = false; }
        else ScheduleAnnounce();
    }

    // Debounced announce (mirrors the MCDU scratchpad): once the dust settles, speak the
    // selected radio's standby if it changed, and any new RMP message (e.g. FREQ NOT VALID).
    private void ScheduleAnnounce()
    {
        if (_announceTimer == null)
        {
            _announceTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _announceTimer.Tick += (_, _) =>
            {
                _announceTimer!.Stop();
                if (_message.Length > 0 && _message != _lastAnnouncedMessage)
                { _lastAnnouncedMessage = _message; _announcer?.Announce(_message); }
                if (_standby.Length > 0 && _standby != _lastAnnouncedStandby)
                { _lastAnnouncedStandby = _standby; _announcer?.Announce($"Standby {_standby}"); }
            };
        }
        _announceTimer.Stop(); _announceTimer.Start();
    }

    private static string Token(string row, string after)
    {
        int i = row.IndexOf(after, StringComparison.Ordinal);
        if (i < 0) return "";
        int start = i + after.Length;
        int end = row.IndexOf(", ", start, StringComparison.Ordinal);
        return (end < 0 ? row.Substring(start) : row.Substring(start, end - start)).Trim();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Hide(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _refreshTimer?.Stop(); _refreshTimer?.Dispose(); } catch { }
        try { _announceTimer?.Stop(); _announceTimer?.Dispose(); } catch { }
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
