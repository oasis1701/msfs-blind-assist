using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible Radio Management Panel (RMP) window for the FlyByWire A380X (Ctrl+Shift+R, input mode).
///
/// ONE input surface — the RMP SCREEN — exactly like the real touchscreen, so a blind pilot's workflow
/// matches a sighted pilot's: pick a page, then type on the screen.
///   • Alt+V = VHF page, Alt+T = Transponder (SQWK) page (the two the FBW build models).
///   • Read the screen with the arrows; just TYPE the digits on it (the box is read-only, so digits go
///     to the radio, not the text). The form knows which page you're on and routes the digits:
///       - VHF page → keyed live into the cockpit RMP keypad; the standby auto-completes; Enter loads it,
///         Backspace deletes a digit, Alt+C full-clears, Ctrl+1/2/3 select a radio, Alt+1/2/3 swap.
///       - SQWK page → a 4-digit (0–7) squawk; set on the 4th digit (or Enter) via the stock XPNDR_SET
///         event — RELIABLE and RMP-page-independent (the keypad-validate route proved unreliable to
///         drive externally). Backspace deletes a digit, Alt+I sends IDENT.
///
/// The page switch also drives the COCKPIT RMP to the matching page (so a sighted observer + the scrape
/// follow along), but the squawk SET never depends on it. LIVE REGION: the screen is scraped every 300 ms;
/// on ANY change the form announces the selected radio's auto-completed standby and any new RMP message
/// ("VHF FREQ NOT VALID", "SQUAWK CODE NOT VALID", …). Captain/First Officer/Overhead is a combo.
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
    private string _page = "VHF";         // active page on the SCREEN: "VHF" or "SQWK"
    private string _squawkEntry = "";     // local squawk being typed on the SQWK page (set via XPNDR_SET)

    // ---- live region (announce-on-change, driven by the scrape poll) -------------------------
    private bool _firstScrape = true;
    private string _standby = "", _lastAnnouncedStandby = "";
    private string _message = "", _lastAnnouncedMessage = "";
    private List<string> _scrapeVhfRows = new();   // "VHF1: active 129.000, standby 121.500, transmit, selected"
    private System.Windows.Forms.Timer? _refreshTimer;
    private System.Windows.Forms.Timer? _simPoll;

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
        _side.SelectedIndexChanged += (_, _) => { _rmp = _side.SelectedIndex + 1; SwitchSide(); };
        _status = new Label { Location = new Point(240, 14), Size = new Size(360, 22), Text = "Connecting…", AccessibleName = "Status" };

        // The RMP SCREEN — read with the arrows AND type the digits for the CURRENT page (VHF freq or SQWK).
        _display = new TextBox
        {
            Location = new Point(12, 44), Size = new Size(584, 312),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 11),
            AccessibleName = "RMP screen — type the digits for the current page",
            Text = "Loading…"
        };
        _display.KeyPress += OnDisplayKeyPress;
        _display.KeyDown += OnDisplayKeyDown;

        // Page selectors (no mnemonics — Alt+V / Alt+T are handled in OnFormKeyDown so they can't steal
        // focus onto a button). The FBW A380 RMP models only VHF + Transponder.
        var pVhf = new Button { Text = "VHF page", Location = new Point(12, 362), Size = new Size(120, 28), AccessibleName = "VHF page" };
        pVhf.Click += (_, _) => Page("VHF", "VHF page");
        var pSqwk = new Button { Text = "Transponder page", Location = new Point(140, 362), Size = new Size(150, 28), AccessibleName = "Transponder page" };
        pSqwk.Click += (_, _) => Page("SQWK", "Transponder page");
        var ident = new Button { Text = "Ident", Location = new Point(300, 362), Size = new Size(80, 28), AccessibleName = "Send transponder ident" };
        ident.Click += (_, _) => SendIdent();

        var help = new Label
        {
            Location = new Point(12, 398), Size = new Size(584, 56),
            Text = "Alt+V VHF page · Alt+T Transponder page. Type the digits on the screen.\n" +
                   "VHF: Enter loads · Backspace deletes · Alt+C clear · Ctrl+1/2/3 radio · Alt+1/2/3 swap.  SQWK: 4 digits 0–7 · Alt+I ident."
        };
        var close = new Button { Text = "Close", Location = new Point(12, 460), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();   // hide, don't dispose — keeps the scrape warm for instant reopen

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _display, pVhf, pSqwk, ident, help, close });
        _display.TabIndex = 1; pVhf.TabIndex = 2; pSqwk.TabIndex = 3; ident.TabIndex = 4; close.TabIndex = 5;
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _disp?.SetActive(true);
            if (_simPoll == null)
            {
                _simPoll = new System.Windows.Forms.Timer { Interval = 300 };
                _simPoll.Tick += (_, _) => RenderFromSim();
            }
            _simPoll.Start();
            RenderFromSim();
            ActiveControl = _display;
            _display.Focus();
            _ = InitialScrape();
        }
        else { _disp?.SetActive(false); _simPoll?.Stop(); _refreshTimer?.Stop(); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    // ---- keyboard: page switch + VHF soft keys + ident ---------------------------------------

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        bool alt = MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

        // VHF line keys / swap — only meaningful on the VHF page.
        if (_page == "VHF")
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
                case Keys.V: Page("VHF", "VHF page"); Handled(e); return;
                case Keys.T: Page("SQWK", "Transponder page"); Handled(e); return;
                case Keys.I: SendIdent(); Handled(e); return;
                case Keys.C: ClearEntry(); Handled(e); return;
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

    private void Page(string key, string spoken)
    {
        if (_busy) return;
        _page = (key == "SQWK") ? "SQWK" : "VHF";
        _squawkEntry = "";                       // start each transponder visit with a fresh entry
        _def.SendRmpKey(_rmp, key, _sim);         // drive the cockpit RMP to match (best-effort; scrape follows)
        _announcer?.Announce(spoken);
        ScheduleRefresh();
        // A page switch via a button moves focus onto it; pull it back so typed digits reach the screen.
        ActiveControl = _display;
        _display.Focus();
        RenderFromSim();
    }

    // ---- digit entry on the screen (page-aware) ----------------------------------------------

    private void OnDisplayKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (_busy) { e.Handled = true; return; }
        if (e.KeyChar < '0' || e.KeyChar > '9') return;
        e.Handled = true;

        if (_page == "SQWK")
        {
            SquawkDigit(e.KeyChar);
            return;
        }
        _def.SendRmpKey(_rmp, $"DIGIT_{e.KeyChar}", _sim);   // VHF: keyed live; the standby auto-completes
        ScheduleRefresh();
    }

    private void OnDisplayKeyDown(object? sender, KeyEventArgs e)
    {
        if (_busy) { Handled(e); return; }

        if (e.KeyCode == Keys.Enter)
        {
            Handled(e);
            if (_page == "SQWK")
            {
                if (_squawkEntry.Length == 4) CommitSquawk();
                else _announcer?.AnnounceImmediate("Enter a 4 digit squawk, 0 to 7");
                return;
            }
            _def.SendRmpKey(_rmp, $"LSK_{_selectedRowIndex + 1}", _sim);   // VHF: load standby
            _announcer?.Announce("Standby loaded");
            ScheduleRefresh();
        }
        else if (e.KeyCode == Keys.Back)
        {
            Handled(e);
            if (_page == "SQWK")
            {
                if (_squawkEntry.Length > 0) { _squawkEntry = _squawkEntry.Substring(0, _squawkEntry.Length - 1); RenderFromSim(); }
                return;
            }
            _def.SendRmpKey(_rmp, "DIGIT_CLR", _sim);   // VHF: one-digit backspace
            ScheduleRefresh();
        }
    }

    // ---- squawk entry: type on the SQWK page, set via the reliable stock XPNDR_SET -----------

    private void SquawkDigit(char c)
    {
        if (c < '0' || c > '7') { _announcer?.AnnounceImmediate("Squawk digits are 0 to 7"); return; }
        if (_squawkEntry.Length >= 4) _squawkEntry = "";   // a 5th digit starts a fresh code
        _squawkEntry += c;
        _announcer?.Announce(c.ToString());                // echo each digit (the screen is read-only)
        if (_squawkEntry.Length == 4) CommitSquawk();
        else RenderFromSim();
    }

    private void CommitSquawk()
    {
        _def.SetSquawkFromForm(_squawkEntry, _sim, _announcer);   // fires XPNDR_SET + announces "Squawk 2222"
        _squawkEntry = "";
        RenderFromSim();
    }

    private void ClearEntry()
    {
        if (_page == "SQWK")
        {
            _squawkEntry = "";
            _announcer?.Announce("Cleared");
            RenderFromSim();
            return;
        }
        _ = FullClear();   // VHF: held full-clear of the keypad scratchpad
    }

    private void SendIdent()
    {
        _def.SendTransponderIdent(_sim);
        _announcer?.AnnounceImmediate("Ident");
    }

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

    // Live region: speak any NEW RMP message + the selected radio's COMPLETE standby on change.
    // Announced DIRECTLY (no debounce timer — the old timer was reset by every 300 ms poll, so it
    // never ticked; that was why the autocomplete never spoke).
    private void AnnounceLive()
    {
        if (_firstScrape)
        {
            _firstScrape = false;
            _lastAnnouncedStandby = _standby;
            _lastAnnouncedMessage = _message;
            return;
        }

        if (_message != _lastAnnouncedMessage)
        {
            _lastAnnouncedMessage = _message;
            if (_message.Length > 0) _announcer?.Announce(_message);
        }

        if (_standby.Length > 0 && _standby.IndexOf('_') < 0 && _standby != _lastAnnouncedStandby)
        {
            _lastAnnouncedStandby = _standby;
            _announcer?.Announce($"Standby {_standby}");
        }
    }

    // Build the read-out: VHF rows (scrape) + squawk/transponder line (reliable simvars) + the live
    // squawk-entry feedback when typing on the SQWK page + message. Caret-preserving.
    private void RenderFromSim()
    {
        if (_sim == null || !_sim.IsConnected) return;
        _status.Text = $"Live — {SideName()} — {(_page == "SQWK" ? "Transponder" : "VHF")} page";
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
        if (_page == "SQWK" && _squawkEntry.Length > 0)
            sb.AppendLine($"Squawk entry: {_squawkEntry.PadRight(4, '_')}");
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
