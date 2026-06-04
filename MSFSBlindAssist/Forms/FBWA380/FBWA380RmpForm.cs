using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible Radio Management Panel (RMP) window for the FlyByWire A380X (Ctrl+Shift+R, input mode).
///
/// TWO separate, clearly-labelled input surfaces — no page modes, no fragile RMP-keypad page switching:
///   • The RMP SCREEN (read-only multiline text box): read VHF active/standby with the arrows, and TYPE
///     the VHF frequency digits right on it — each is keyed live into the cockpit RMP keypad (the box is
///     read-only, so the digits go to the radio, not the text). Enter loads the standby, Backspace deletes
///     a digit, Alt+C does a full clear. Ctrl+1/2/3 select a radio, Alt+1/2/3 swap active↔standby.
///   • The SQUAWK field (a normal edit box): type a 4-digit (0–7) squawk; it is set DIRECTLY via the stock
///     XPNDR_SET event (independent of the RMP page — the page/keypad route proved unreliable to drive).
///     Alt+I sends IDENT.
///
/// LIVE REGION: the screen is scraped (coherent-rmp-agent.js) every 300 ms; on ANY change the form
/// announces the selected radio's auto-completed standby and any new RMP message line ("VHF FREQ NOT
/// VALID", "SQUAWK CODE NOT VALID", …) — exactly like a screen-reader live region, with no user action
/// required. The squawk auto-announces via the def's XPNDR_CODE monitor. Captain/First Officer/Overhead
/// is a combo (re-points the scrape view + the keypad H-event index).
/// </summary>
public sealed class FBWA380RmpForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FlyByWireA380Definition _def;
    private readonly SimConnectManager _sim;

    private CoherentDisplayClient _disp = null!;
    private int _rmp = 1;                  // 1 = Captain, 2 = First Officer, 3 = Overhead
    private int _selectedRowIndex;        // VHF transceiver row the RMP keypad is on (0..2)
    private bool _busy;                   // held-clear running
    private bool _committingSquawk;       // re-entrancy guard for the squawk field's auto-commit

    // ---- live region (announce-on-change, driven by the scrape poll) -------------------------
    private bool _firstScrape = true;
    private string _standby = "", _lastAnnouncedStandby = "";
    private string _message = "", _lastAnnouncedMessage = "";
    private List<string> _scrapeVhfRows = new();   // "VHF1: active 129.000, standby 121.500, transmit, selected"
    // The VHF rows (active + STANDBY) come from the SCRAPE — the ONLY reliable standby source (the stock
    // COM STANDBY simvar is frozen at the default; the FBW standby L:var is garbage). The squawk line is
    // built from the reliable stock XPNDR_CODE/XPNDR_STATE simvars.
    private System.Windows.Forms.Timer? _refreshTimer;
    private System.Windows.Forms.Timer? _simPoll;

    private ComboBox _side = null!;
    private TextBox _display = null!;
    private TextBox _squawk = null!;
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
        _side.SelectedIndexChanged += (_, _) => { _rmp = _side.SelectedIndex + 1; SwitchSide(); };
        _status = new Label { Location = new Point(240, 14), Size = new Size(360, 22), Text = "Connecting…", AccessibleName = "Status" };

        // The RMP SCREEN — read it with the arrows AND type VHF frequency digits on it (keyed live to the
        // cockpit keypad; the box is read-only so the digits never edit its text). NOT for the squawk.
        _display = new TextBox
        {
            Location = new Point(12, 44), Size = new Size(584, 312),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11),
            AccessibleName = "RMP screen — type VHF frequency digits here",
            Text = "Loading…"
        };
        _display.KeyPress += OnDisplayKeyPress;
        _display.KeyDown += OnDisplayKeyDown;

        // The SQUAWK field — a normal edit box. Type 4 octal (0–7) digits; set directly via XPNDR_SET.
        var sqLabel = new Label { Text = "S&quawk:", Location = new Point(12, 366), Size = new Size(64, 22) };
        _squawk = new TextBox
        {
            Location = new Point(80, 363), Size = new Size(90, 26), MaxLength = 4,
            Font = new Font("Consolas", 11),
            AccessibleName = "Squawk code, 4 digits 0 to 7",
            AccessibleDescription = "Type a 4 digit transponder squawk code (digits 0 to 7). It sets automatically on the fourth digit, or press Enter."
        };
        _squawk.KeyPress += OnSquawkKeyPress;
        _squawk.KeyDown += OnSquawkKeyDown;
        _squawk.TextChanged += OnSquawkTextChanged;

        // IDENT (Alt+I) — a plain button; no mnemonic clash with the Alt shortcuts handled in OnFormKeyDown.
        var ident = new Button { Text = "Ident", Location = new Point(190, 362), Size = new Size(90, 28), AccessibleName = "Send transponder ident" };
        ident.Click += (_, _) => SendIdent();

        var help = new Label
        {
            Location = new Point(12, 398), Size = new Size(584, 56),
            Text = "Screen: type VHF frequency digits · Enter loads standby · Backspace deletes · Alt+C clear.\n" +
                   "Ctrl+1/2/3 select radio · Alt+1/2/3 swap · Squawk field (Alt+Q): 4 digits · Alt+I ident · Alt+Home screen."
        };
        var close = new Button { Text = "Close", Location = new Point(12, 460), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();   // hide, don't dispose — keeps the scrape warm for instant reopen

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _display, sqLabel, _squawk, ident, help, close });

        _display.TabIndex = 1; _squawk.TabIndex = 2; ident.TabIndex = 3; close.TabIndex = 4;
    }

    // Focus the screen every time the window is shown; pause/resume the scrape with visibility.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _disp?.SetActive(true);
            if (_simPoll == null)
            {
                _simPoll = new System.Windows.Forms.Timer { Interval = 300 };
                _simPoll.Tick += (_, _) => RenderFromSim();   // refresh the squawk line from the live simvar
            }
            _simPoll.Start();
            RenderFromSim();
            ActiveControl = _display;
            _display.Focus();
            _ = InitialScrape();
        }
        else { _disp?.SetActive(false); _simPoll?.Stop(); _refreshTimer?.Stop(); }
    }

    // X / Alt+F4 just HIDES (the window is reused across opens); only a real teardown disposes it.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    // ---- keyboard: VHF soft keys + ident + clear (NO page switching) --------------------------

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        bool alt = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

        // Don't hijack plain digit typing in the squawk field.
        bool inSquawk = ActiveControl == _squawk;

        // Line keys (LSK, left = select radio / load standby) and swap (ADK, right), rows 1..3.
        if (!inSquawk)
        {
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
        }

        if (e.Alt && !e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.I: SendIdent(); Handled(e); return;
                case Keys.C: _ = FullClear(); Handled(e); return;          // full clear of the VHF entry
                case Keys.Q: ActiveControl = _squawk; _squawk.Focus(); _squawk.SelectAll(); Handled(e); return;
                case Keys.Home: ActiveControl = _display; _display.Focus(); Handled(e); return;
            }
        }
    }

    private static bool NoMods(KeyEventArgs e) => !e.Control && !e.Alt && !e.Shift;
    private static void Handled(KeyEventArgs e) { e.Handled = true; e.SuppressKeyPress = true; }

    private void PressLine(int row)
    {
        if (_busy) return;
        bool wasSel = _selectedRowIndex == row;
        _selectedRowIndex = row;
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

    // ---- VHF digit entry on the screen (frequencies only) ------------------------------------

    private void OnDisplayKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_busy) { e.Handled = true; return; }
        if (e.KeyChar >= '0' && e.KeyChar <= '9')
        {
            _def.SendRmpKey(_rmp, $"DIGIT_{e.KeyChar}", _sim);   // keyed live; the standby auto-completes
            e.Handled = true;
            ScheduleRefresh();   // debounced re-scrape so the live region announces the auto-completed standby
        }
    }

    private void OnDisplayKeyDown(object? sender, KeyEventArgs e)
    {
        if (_busy) { Handled(e); return; }
        if (e.KeyCode == Keys.Enter)
        {
            Handled(e);
            _def.SendRmpKey(_rmp, $"LSK_{_selectedRowIndex + 1}", _sim);   // load standby (manual; no auto-swap)
            _announcer?.Announce("Standby loaded");
            ScheduleRefresh();
        }
        else if (e.KeyCode == Keys.Back)
        {
            Handled(e);
            _def.SendRmpKey(_rmp, "DIGIT_CLR", _sim);   // one-digit backspace in the RMP entry
            ScheduleRefresh();
        }
    }

    // ---- squawk field (XPNDR_SET, RMP-page-independent) --------------------------------------

    private static bool AllOctal(string s)
    {
        foreach (char c in s) if (c < '0' || c > '7') return false;
        return s.Length > 0;
    }

    private void OnSquawkKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Back) return;   // allow backspace edits
        if (e.KeyChar < '0' || e.KeyChar > '7')
        {
            e.Handled = true;   // block 8, 9, letters, etc.
            if (e.KeyChar == '8' || e.KeyChar == '9') _announcer?.AnnounceImmediate("Squawk digits are 0 to 7");
        }
    }

    private void OnSquawkTextChanged(object? sender, EventArgs e)
    {
        if (_committingSquawk) return;
        string t = _squawk.Text;
        if (t.Length == 4 && AllOctal(t)) CommitSquawk(t);   // auto-set on the 4th valid digit
    }

    private void OnSquawkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            Handled(e);
            string t = _squawk.Text;
            if (t.Length == 4 && AllOctal(t)) CommitSquawk(t);
            else _announcer?.AnnounceImmediate("Enter a 4 digit squawk, 0 to 7");
        }
    }

    private void CommitSquawk(string code)
    {
        _committingSquawk = true;
        try
        {
            _def.SetSquawkFromForm(code, _sim, _announcer);   // fires XPNDR_SET + announces "Squawk 2222"
            _squawk.Clear();                                   // ready for the next entry (committed value shows on the screen)
        }
        finally { _committingSquawk = false; }
        RenderFromSim();
    }

    private void SendIdent()
    {
        _def.SendTransponderIdent(_sim);
        _announcer?.AnnounceImmediate("Ident");
    }

    // ---- held clear (full reset of a stuck VHF entry) ----------------------------------------

    private async Task FullClear()
    {
        if (_busy) return;
        _busy = true;
        try { _def.SendRmpKeyPress(_rmp, "DIGIT_CLR", _sim); await Task.Delay(1150); }
        finally { _def.SendRmpKeyRelease(_rmp, "DIGIT_CLR", _sim); _busy = false; }
        _announcer?.Announce("Cleared");
        Apply(await _disp.ScrapeNowAsync());
    }

    // ---- scrape + live region ----------------------------------------------------------------

    private void StartScrape()
    {
        _firstScrape = true;
        _lastAnnouncedStandby = ""; _lastAnnouncedMessage = "";
        _standby = ""; _message = ""; _scrapeVhfRows = new();
        _disp = new CoherentDisplayClient($"A380X_RMP_{_rmp}", 300, "coherent-rmp-agent.js");
        _disp.RowsUpdated += OnRowsUpdated;
        _disp.Start();
        _ = InitialScrape();
    }

    private string SideName() => _rmp == 1 ? "Captain" : _rmp == 2 ? "First Officer" : "Overhead";

    private void SwitchSide()
    {
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        _status.Text = $"{SideName()} — connecting…";
        _selectedRowIndex = 0;
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
        if (rows != null && rows.Count > 0)
        {
            var vhf = rows.FindAll(r => r.StartsWith("VHF", StringComparison.Ordinal));
            if (vhf.Count > 0) _scrapeVhfRows = vhf;

            // Keep our keypad-row tracking in sync with the cockpit's actual selection.
            for (int k = 0; k < vhf.Count; k++)
                if (vhf[k].EndsWith(", selected", StringComparison.Ordinal)) { _selectedRowIndex = k; break; }

            var msgRow = rows.Find(r => r.StartsWith("Message: ", StringComparison.Ordinal)) ?? "";
            _message = msgRow.Length > 0 ? msgRow.Substring("Message: ".Length) : "";

            string? sel = vhf.Find(r => r.EndsWith(", selected", StringComparison.Ordinal));
            if (sel == null && _selectedRowIndex >= 0 && _selectedRowIndex < vhf.Count) sel = vhf[_selectedRowIndex];
            if (sel != null) _standby = Token(sel, "standby ");

            AnnounceLive();
        }
        RenderFromSim();
    }

    // The live region: speak any NEW RMP message and the selected radio's COMPLETE standby on change.
    // Driven by the 300 ms scrape poll + the post-keystroke ScheduleRefresh — no user action required.
    private void AnnounceLive()
    {
        if (_firstScrape)   // seed the baselines SILENTLY on open / side switch
        {
            _firstScrape = false;
            _lastAnnouncedStandby = _standby;
            _lastAnnouncedMessage = _message;
            return;
        }

        if (_message != _lastAnnouncedMessage)
        {
            _lastAnnouncedMessage = _message;
            if (_message.Length > 0) _announcer?.Announce(_message);   // "VHF FREQ NOT VALID", "SQUAWK CODE NOT VALID", …
        }

        // Skip partial entries (contain '_', e.g. "1__.___") so only the auto-completed standby is spoken.
        if (_standby.Length > 0 && _standby.IndexOf('_') < 0 && _standby != _lastAnnouncedStandby)
        {
            _lastAnnouncedStandby = _standby;
            _announcer?.Announce($"Standby {_standby}");
        }
    }

    // Build the read-out: VHF rows (scrape) + squawk/transponder line (reliable stock simvars). Caret-preserving.
    private void RenderFromSim()
    {
        if (_sim == null || !_sim.IsConnected) return;
        _status.Text = $"Live — {SideName()}";
        var sb = new System.Text.StringBuilder();
        if (_scrapeVhfRows.Count > 0)
        {
            foreach (var r in _scrapeVhfRows) sb.AppendLine(r);
        }
        else
        {
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
        try { _simPoll?.Stop(); _simPoll?.Dispose(); } catch { }
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
