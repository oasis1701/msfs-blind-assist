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
    // The VHF rows (active + STANDBY + transmit/receive/selected) come from the Coherent RMP-screen
    // SCRAPE — it is the ONLY reliable source for the STANDBY frequency (the stock COM STANDBY simvar
    // stays frozen at the A380 default and never tracks the RMP standby, and FBW_RMP_FREQUENCY_STANDBY_n
    // reads uninitialised garbage). The single-socket EnsureConnected bug that used to freeze the scrape
    // is fixed, so the scrape now matches the cockpit live (verified: active 129.000 == COM ACTIVE). The
    // SQUAWK/transponder line is still built from the reliable stock simvars. Audio standby announces are
    // debounced off the scrape here; freq/squawk audio also comes from the def's simvar monitors.
    private System.Windows.Forms.Timer? _simPoll;
    private List<string> _scrapeVhfRows = new();   // "VHF1: active 129.000, standby 121.500, transmit, selected"

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
        _side.Items.AddRange(new object[] { "Captain", "First Officer", "Overhead" });
        _side.SelectedIndex = 0;
        // index 0/1/2 -> RMP 1/2/3 (Captain / First Officer / Overhead). The scrape view
        // (A380X_RMP_{_rmp}) and the keypad H-events (RMP_{_rmp}_*) both follow _rmp.
        _side.SelectedIndexChanged += (_, _) => { _rmp = _side.SelectedIndex + 1; SwitchSide(); };
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
            _disp?.SetActive(true);   // scrape kept alive for the best-effort message line
            if (_simPoll == null)
            {
                _simPoll = new System.Windows.Forms.Timer { Interval = 300 };
                _simPoll.Tick += (_, _) => RenderFromSim();   // reliable freqs/squawk every 300 ms
            }
            _simPoll.Start();
            RenderFromSim();          // instant accurate read-out the moment the window appears
            ActiveControl = _display;
            _display.Focus();
            _ = InitialScrape();
        }
        else { _disp?.SetActive(false); _simPoll?.Stop(); }
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
        _selectedRowIndex = row;   // track the selection ourselves (the simvar render reads it)
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
            // Force a DEBOUNCED scrape so the RMP's own autocomplete is heard. ScheduleRefresh is
            // Stop+Start, so rapid typing does NOT stack a round-trip per digit — it scrapes ONCE,
            // ~250 ms after the LAST digit. That re-read catches the auto-completed standby (e.g.
            // type 8 -> 118.000) and Apply()'s standby-change check then announces "Standby 118.000".
            // (The background poll alone was too flaky to reliably catch the autocomplete.)
            ScheduleRefresh();
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
        _standby = ""; _message = ""; _scrapeVhfRows = new();
        _disp = new CoherentDisplayClient($"A380X_RMP_{_rmp}", 300, "coherent-rmp-agent.js");
        _disp.RowsUpdated += OnRowsUpdated;
        _disp.Start();
        _ = InitialScrape();
    }

    // RMP 1/2/3 -> spoken side name (Captain / First Officer / Overhead).
    private string SideName() => _rmp == 1 ? "Captain" : _rmp == 2 ? "First Officer" : "Overhead";

    private void SwitchSide()
    {
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        _status.Text = $"{SideName()} — connecting…";
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
        // The SCRAPE supplies the VHF rows — the only reliable source for the STANDBY frequency (the
        // stock COM STANDBY simvar is frozen at the A380 default and the FBW standby L:var is garbage).
        // We cache the VHF rows + message, capture the SELECTED radio's standby for the auto-announce,
        // and re-render. An empty/failed scrape is harmless — the last good rows + simvar squawk persist.
        if (rows != null && rows.Count > 0)
        {
            var vhf = rows.FindAll(r => r.StartsWith("VHF", StringComparison.Ordinal));
            if (vhf.Count > 0) _scrapeVhfRows = vhf;

            var msgRow = rows.Find(r => r.StartsWith("Message: ", StringComparison.Ordinal)) ?? "";
            _message = msgRow.Length > 0 ? msgRow.Substring("Message: ".Length) : "";

            // The selected radio's standby (the one the keypad is loading into) — prefer the scrape's
            // own ", selected" marker, else fall back to the row the user's LSK presses point at.
            string? sel = vhf.Find(r => r.EndsWith(", selected", StringComparison.Ordinal));
            if (sel == null && _selectedRowIndex >= 0 && _selectedRowIndex < vhf.Count) sel = vhf[_selectedRowIndex];
            if (sel != null) { string sby = Token(sel, "standby "); if (sby.Length > 0) _standby = sby; }

            // First scrape after open / side-switch: seed the baselines SILENTLY (no announce on open).
            if (_firstScrape)
            {
                _firstScrape = false;
                _lastAnnouncedStandby = _standby;
                _lastAnnouncedMessage = _message;
            }
            else ScheduleAnnounce();   // debounced: speak the selected standby + any new message on change
        }
        RenderFromSim();
    }

    // Build the RMP read-out: VHF rows (active + STANDBY + transmit/receive/selected) from the SCRAPE
    // (the only reliable standby source), squawk + transponder mode from the reliable stock simvars.
    // Falls back to a simvar-only VHF render if no scrape rows are in yet. Caret-preserving.
    private void RenderFromSim()
    {
        if (_sim == null || !_sim.IsConnected) return;
        _haveRows = true;
        _status.Text = $"Live — {SideName()}";
        var sb = new System.Text.StringBuilder();
        if (_scrapeVhfRows.Count > 0)
        {
            foreach (var r in _scrapeVhfRows) sb.AppendLine(r);
        }
        else
        {
            // Pre-first-scrape fallback: active from the reliable COM simvar (standby unavailable yet).
            for (int i = 1; i <= 3; i++)
            {
                double act = _sim.GetCachedVariableValue($"COM_ACTIVE_{i}") ?? 0;
                bool tx = (_sim.GetCachedVariableValue($"A380X_RMP_{_rmp}_VHF_TX_{i}") ?? 0) > 0.5;
                string line = $"VHF {i}: active {act:0.000}";
                if (tx) line += ", transmit";
                if (i - 1 == _selectedRowIndex) line += ", selected";
                sb.AppendLine(line);
            }
        }
        int bcd = (int)Math.Round(_sim.GetCachedVariableValue("XPNDR_CODE") ?? 0);
        string sq = $"{(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}";
        double xst = _sim.GetCachedVariableValue("XPNDR_STATE") ?? 0;
        string mode = xst >= 4 ? "Mode C" : xst >= 3 ? "Mode A" : xst >= 1 ? "Standby" : "Off";
        sb.AppendLine($"Squawk: {sq}, transponder {mode}");
        if (_message.Length > 0) sb.AppendLine($"Message: {_message}");

        string text = sb.ToString().TrimEnd();
        if (_display.Text != text)
        {
            int caret = _display.SelectionStart;
            _display.Text = text;
            _display.SelectionStart = Math.Min(caret, _display.TextLength);
        }
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
