using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

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
    private string _vhfEntry = "";        // VHF digits typed on the VHF page — the readback formats THESE (no scrape race)

    // ---- live region (announce-on-change, driven by the scrape poll) -------------------------
    private bool _firstScrape = true;
    private string _message = "", _lastAnnouncedMessage = "";
    private List<string> _scrapeVhfRows = new();   // "VHF1: active 129.000, standby 121.500, transmit, selected"
    private System.Windows.Forms.Timer? _refreshTimer;
    private System.Windows.Forms.Timer? _simPoll;
    private System.Windows.Forms.Timer? _standbyTimer;   // debounce for the post-typing standby announce
    private string _lastStandbyAnnounced = "";           // dedup key "row:freq" so it speaks once

    private ComboBox _side = null!;
    private DisplayListBox _display = null!;
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
        Size = new Size(620, 625);
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
        _display = new DisplayListBox
        {
            Location = new Point(12, 44), Size = new Size(584, 312),
            SuppressTypeAhead = true,   // character keys are RADIO INPUT here, not list navigation
            Font = new Font("Consolas", 11),
            AccessibleName = "RMP screen — type the digits for the current page",
        };
        _display.SetText("Loading…");
        _display.KeyPress += OnDisplayKeyPress;
        _display.KeyDown += OnDisplayKeyDown;

        // Page selectors (no mnemonics — Alt+V / Alt+T are handled in OnFormKeyDown so they can't steal
        // focus onto a button). The FBW A380 RMP models only VHF + Transponder.
        var pVhf = new Button { Text = "VHF page", Location = new Point(12, 362), Size = new Size(120, 28), AccessibleName = "VHF page" };
        pVhf.Click += (_, _) => Page("VHF", "VHF page", fromButton: true);
        var pSqwk = new Button { Text = "Transponder page", Location = new Point(140, 362), Size = new Size(150, 28), AccessibleName = "Transponder page" };
        pSqwk.Click += (_, _) => Page("SQWK", "Transponder page", fromButton: true);

        // VHF radio SELECT (mirrors Ctrl+1/2/3 — line keys): pick which transceiver the keypad tunes; a
        // second select of the same radio loads the typed standby (the PressLine re-press behaviour).
        var sel1 = MakeVhfButton("Select VHF 1", 12,  394, () => SelectRadioFromButton(0));
        var sel2 = MakeVhfButton("Select VHF 2", 128, 394, () => SelectRadioFromButton(1));
        var sel3 = MakeVhfButton("Select VHF 3", 244, 394, () => SelectRadioFromButton(2));

        // VHF SWAP active<->standby (mirrors Alt+1/2/3 — the ADK keys).
        var swap1 = MakeVhfButton("Swap VHF 1", 12,  426, () => SwapFromButton(0));
        var swap2 = MakeVhfButton("Swap VHF 2", 128, 426, () => SwapFromButton(1));
        var swap3 = MakeVhfButton("Swap VHF 3", 244, 426, () => SwapFromButton(2));

        // Clear (mirrors Alt+C — page-aware: VHF held full-clear, SQWK clears the typed squawk) and Ident.
        var clear = new Button { Text = "Clear", Location = new Point(12, 458), Size = new Size(110, 28), AccessibleName = "Clear entry" };
        clear.Click += (_, _) => { ClearEntry(fromButton: true); FocusDisplay(); };
        var ident = new Button { Text = "Ident", Location = new Point(128, 458), Size = new Size(110, 28), AccessibleName = "Send transponder ident" };
        ident.Click += (_, _) => { SendIdent(); FocusDisplay(); };

        var help = new Label
        {
            Location = new Point(12, 494), Size = new Size(584, 40),
            Text = "Buttons mirror the keys: VHF page (Alt+V), Transponder page (Alt+T), Select/Swap VHF 1-3 " +
                   "(Ctrl/Alt+1-3), Clear (Alt+C), Ident (Alt+I). Type the digits on the screen; Enter loads, Backspace deletes."
        };
        var close = new Button { Text = "Close", Location = new Point(12, 542), Size = new Size(100, 30), AccessibleName = "Close" };
        close.Click += (_, _) => Hide();   // hide, don't dispose — keeps the scrape warm for instant reopen

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _display, pVhf, pSqwk,
            sel1, sel2, sel3, swap1, swap2, swap3, clear, ident, help, close });
        int tab = 1;
        _display.TabIndex = tab++;
        pVhf.TabIndex = tab++; pSqwk.TabIndex = tab++;
        sel1.TabIndex = tab++; sel2.TabIndex = tab++; sel3.TabIndex = tab++;
        swap1.TabIndex = tab++; swap2.TabIndex = tab++; swap3.TabIndex = tab++;
        clear.TabIndex = tab++; ident.TabIndex = tab++; close.TabIndex = tab++;
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

    private void PressLine(int row, bool fromButton = false)
    {
        if (_busy) return;
        bool wasSel = _selectedRowIndex == row;
        _selectedRowIndex = row;
        _def.SendRmpKey(_rmp, $"LSK_{row + 1}", _sim);
        if (wasSel) { _standbyTimer?.Stop(); AnnounceVhfEntry(force: true); _vhfEntry = ""; }   // re-press = load -> read back
        else { _vhfEntry = ""; if (!fromButton) _announcer?.Announce($"Radio {row + 1}"); ScheduleRefresh(); }    // new radio = fresh entry
    }

    // Announce the SELECTED radio's standby as "VHF standby 1, 123.500", formatting the DIGITS the user
    // typed (no scrape -> no autocomplete-timing race). FBW completes an entry as entered.padEnd(6,'0')
    // shown XXX.XXX; we mirror that. If the typed digits don't form a plausible VHF frequency (e.g. the
    // rare leading-omitted shortcut "8" = 118.000), fall back to the scrape-settle reader.
    private void AnnounceVhfEntry(bool force)
    {
        int row = _selectedRowIndex;
        string freq = FormatVhfEntry(_vhfEntry);
        if (freq.Length == 0) { AnnounceSelectedStandby(force); return; }   // empty / shortcut -> read the live value
        string key = $"{row}:{freq}";
        if (force || key != _lastStandbyAnnounced)
        {
            _lastStandbyAnnounced = key;
            _announcer?.AnnounceImmediate($"VHF standby {row + 1}, {freq}");
        }
    }

    // FBW completes a VHF entry as `entered.padEnd(6,'0')` displayed as XXX.XXX. Returns "" if the result
    // isn't a plausible 118.000–136.975 COM frequency (caller then falls back to the scrape).
    private static string FormatVhfEntry(string digits)
    {
        if (string.IsNullOrEmpty(digits)) return "";
        string d = digits.Length >= 6 ? digits.Substring(0, 6) : digits.PadRight(6, '0');
        string f = $"{d.Substring(0, 3)}.{d.Substring(3, 3)}";
        return double.TryParse(f, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out double mhz)
               && mhz >= 118.0 && mhz <= 137.0
            ? f : "";
    }

    // Debounce: ~350 ms after the user stops typing a VHF frequency, announce the selected radio's standby.
    private void ScheduleStandbyAnnounce()
    {
        if (_standbyTimer == null)
        {
            _standbyTimer = new System.Windows.Forms.Timer { Interval = 350 };
            _standbyTimer.Tick += (_, _) => { _standbyTimer!.Stop(); AnnounceVhfEntry(false); };
        }
        _standbyTimer.Stop(); _standbyTimer.Start();
    }

    // Scrape FRESH and announce the SELECTED radio's standby ONCE: "VHF standby 1, 121.900". Reading fresh
    // (not a cached value) avoids the stale-cache bug; deduped per radio+value so the typing-settle and the
    // Enter/LSK paths don't double; marshalled to the UI thread so the announce reliably speaks (the scrape
    // continuation can resume off the UI thread, where the screen-reader announce silently fails).
    // force = an explicit action (Enter / re-press the selected radio) — always speak, even if the
    // typing-settle debounce already announced this same value. The debounce path passes force = false
    // (deduped) so continuous typing speaks only once.
    // Non-handler async void (called from AnnounceVhfEntry, not subscribed to an event) —
    // wrapped end-to-end so a fault in Finish()/Apply(rows) can't escape as an unobserved
    // async-void exception; the scrape loop already had its own per-iteration guard, but
    // the rest of the method didn't.
    private async void AnnounceSelectedStandby(bool force = false)
    {
      try
      {
        int row = _selectedRowIndex;
        // The FBW RMP auto-completes over a FEW FRAMES after the last keystroke, so a single scrape can
        // catch a transient mid-entry value (e.g. 123.400 while the final is 123.450 — the bug the user
        // hit). Poll the FRESH scrape until the standby reads the SAME complete value twice in a row
        // (settled), capped at ~0.9 s so it can never hang.
        string sby = "", prev = "";
        List<string>? rows = null;
        for (int i = 0; i < 8; i++)
        {
            try { rows = await _disp.ScrapeNowAsync(); } catch { }
            sby = StandbyFromRows(rows, row);
            if (sby.Length > 0 && sby.IndexOf('_') < 0 && sby == prev) break;   // settled on a complete value
            prev = sby;
            await Task.Delay(110);
        }
        void Finish()
        {
            if (sby.Length > 0 && sby.IndexOf('_') < 0)   // a complete (auto-completed) frequency
            {
                string key = $"{row}:{sby}";
                if (force || key != _lastStandbyAnnounced)
                {
                    _lastStandbyAnnounced = key;
                    _announcer?.AnnounceImmediate($"VHF standby {row + 1}, {sby}");
                }
            }
            else if (force)
            {
                _announcer?.AnnounceImmediate("Standby not set");   // Enter on an empty/incomplete entry
            }
            Apply(rows);
        }
        if (InvokeRequired) { try { BeginInvoke((Action)Finish); } catch { } } else Finish();
      }
      catch (Exception ex)
      {
          Log.Debug("Forms", $"AnnounceSelectedStandby error: {ex.Message}");
      }
    }

    // The SELECTED VHF row's standby frequency from a scrape row set ("" if none / not found).
    private static string StandbyFromRows(List<string>? rows, int row)
    {
        if (rows == null) return "";
        var vhf = rows.FindAll(r => r.StartsWith("VHF", StringComparison.Ordinal));
        string? sel = vhf.Find(r => r.EndsWith(", selected", StringComparison.Ordinal));
        if (sel == null && row >= 0 && row < vhf.Count) sel = vhf[row];
        return sel != null ? Token(sel, "standby ") : "";
    }

    private void Swap(int row, bool fromButton = false)
    {
        if (_busy) return;
        _def.SendRmpKey(_rmp, $"ADK_{row + 1}", _sim);
        if (!fromButton) _announcer?.Announce("Swapped");
        ScheduleRefresh();
    }

    // ---- button equivalents of the VHF soft keys ---------------------------------------------
    // The select/swap KEYS only act on the VHF page (OnFormKeyDown gates them). The BUTTONS ensure the
    // VHF page first so a click is always meaningful from anywhere, then hand focus back to the screen
    // so the pilot can keep typing digits (same as Page() does on a page-button click).

    private Button MakeVhfButton(string text, int x, int y, Action onClick)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(110, 28), AccessibleName = text };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void FocusDisplay() { ActiveControl = _display; _display.Focus(); }

    private void EnsureVhfPage()
    {
        if (_page != "VHF") Page("VHF", "VHF page", fromButton: true);   // Page() already refocuses the screen + resets the entry
    }

    private void SelectRadioFromButton(int row)
    {
        EnsureVhfPage();
        PressLine(row, fromButton: true);     // first select = "Radio N"; second select of the same radio loads the standby
        FocusDisplay();
    }

    private void SwapFromButton(int row)
    {
        EnsureVhfPage();
        Swap(row, fromButton: true);
        FocusDisplay();
    }

    private void Page(string key, string spoken, bool fromButton = false)
    {
        if (_busy) return;
        _page = (key == "SQWK") ? "SQWK" : "VHF";
        _squawkEntry = ""; _vhfEntry = "";       // start each page visit with a fresh entry
        _def.SendRmpKey(_rmp, key, _sim);         // drive the cockpit RMP to match (best-effort; scrape follows)
        if (!fromButton) _announcer?.Announce(spoken);
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
        if (_vhfEntry.Length >= 6) _vhfEntry = "";           // a 7th digit starts a fresh frequency
        _vhfEntry += e.KeyChar;                              // remember what was typed (the readback uses this)
        _def.SendRmpKey(_rmp, $"DIGIT_{e.KeyChar}", _sim);   // VHF: keyed live; the standby auto-completes
        ScheduleStandbyAnnounce();   // ~350 ms after the last digit: announce "VHF standby N, freq"
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
            _def.SendRmpKey(_rmp, $"LSK_{_selectedRowIndex + 1}", _sim);   // VHF: confirm/load the typed standby
            _standbyTimer?.Stop();                                          // cancel the pending typing-settle announce
            AnnounceVhfEntry(force: true);                                  // read back what was typed: "VHF standby N, freq"
            _vhfEntry = "";                                                 // next digit starts a fresh entry
        }
        else if (e.KeyCode == Keys.Back)
        {
            Handled(e);
            if (_page == "SQWK")
            {
                if (_squawkEntry.Length > 0) { _squawkEntry = _squawkEntry.Substring(0, _squawkEntry.Length - 1); RenderFromSim(); }
                return;
            }
            if (_vhfEntry.Length > 0) _vhfEntry = _vhfEntry.Substring(0, _vhfEntry.Length - 1);
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
        // No per-digit echo (the screen shows "Squawk entry: 22__"); the commit announces "Squawk NNNN".
        if (_squawkEntry.Length == 4) CommitSquawk();
        else RenderFromSim();
    }

    private void CommitSquawk()
    {
        _def.SetSquawkFromForm(_squawkEntry, _sim, _announcer);   // fires XPNDR_SET + announces "Squawk 2222"
        _squawkEntry = "";
        RenderFromSim();
    }

    private void ClearEntry(bool fromButton = false)
    {
        if (_page == "SQWK")
        {
            _squawkEntry = "";
            if (!fromButton) _announcer?.Announce("Cleared");
            RenderFromSim();
            return;
        }
        _ = FullClear(fromButton);   // VHF: held full-clear of the keypad scratchpad
    }

    private void SendIdent()
    {
        _def.SendTransponderIdent(_sim);
        _announcer?.AnnounceImmediate("Ident");
    }

    private async Task FullClear(bool fromButton = false)
    {
        if (_busy) return;
        _busy = true;
        try { _def.SendRmpKeyPress(_rmp, "DIGIT_CLR", _sim); await Task.Delay(1150); }
        finally { _def.SendRmpKeyRelease(_rmp, "DIGIT_CLR", _sim); _busy = false; }
        _vhfEntry = "";
        if (!fromButton) _announcer?.Announce("Cleared");
        Apply(await _disp.ScrapeNowAsync());
    }

    // ---- scrape + live region ----------------------------------------------------------------

    private void StartScrape()
    {
        _firstScrape = true;
        _lastAnnouncedMessage = ""; _lastStandbyAnnounced = "";
        _message = ""; _scrapeVhfRows = new(); _vhfEntry = "";
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
        // ScrapeNowAsync's continuation can resume OFF the UI thread, so Apply (and thus the live-region
        // Announce) could run on a thread-pool thread — where the screen-reader announce silently fails
        // AND _lastAnnouncedStandby still gets updated, so the next UI-thread poll then sees "no change"
        // and never speaks. That was why the VHF autocomplete never announced. Marshal to the UI thread.
        if (InvokeRequired) { try { BeginInvoke(new Action(() => Apply(rows))); } catch { } return; }

        if (rows != null && rows.Count > 0)
        {
            var vhf = rows.FindAll(r => r.StartsWith("VHF", StringComparison.Ordinal));
            if (vhf.Count > 0) _scrapeVhfRows = vhf;

            // Sync our selected-row tracking to the cockpit ONLY on the first scrape (to initialize). After
            // that the user's Ctrl+1/2/3 is authoritative. A per-poll sync RACED the LSK-select registration
            // lag: a poll firing before LSK_2/LSK_3 registered would see VHF1 still selected and reset a
            // fresh VHF2/3 selection back to row 0 — that's the "says VHF standby 1 and won't swap" bug.
            if (_firstScrape)
                for (int k = 0; k < vhf.Count; k++)
                    if (vhf[k].EndsWith(", selected", StringComparison.Ordinal)) { _selectedRowIndex = k; break; }

            var msgRow = rows.Find(r => r.StartsWith("Message: ", StringComparison.Ordinal)) ?? "";
            _message = msgRow.Length > 0 ? msgRow.Substring("Message: ".Length) : "";

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
            _lastAnnouncedMessage = _message;
            return;
        }

        if (_message != _lastAnnouncedMessage)
        {
            _lastAnnouncedMessage = _message;
            if (_message.Length > 0) _announcer?.Announce(_message);
        }
        // The STANDBY is intentionally NOT announced per keystroke (that was chatty autocomplete noise);
        // the loaded standby is announced once on Enter / LSK by LoadStandby().
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
        // 0 Off · 1 Standby · 2 Test · 3 Mode A · 4 Mode C · 5 Mode S (the FBW A380 reports Mode S on the
        // ground/airborne when AUTO; earlier code mislabelled 5 as "Mode C").
        string mode = xst >= 5 ? "Mode S" : xst >= 4 ? "Mode C" : xst >= 3 ? "Mode A" : xst >= 2 ? "Test" : xst >= 1 ? "Standby" : "Off";
        sb.AppendLine($"Squawk: {sq}, transponder {mode}");
        if (_page == "SQWK" && _squawkEntry.Length > 0)
            sb.AppendLine($"Squawk entry: {_squawkEntry.PadRight(4, '_')}");
        if (_message.Length > 0) sb.AppendLine($"Message: {_message}");

        string text = sb.ToString().TrimEnd();
        _display.SetText(text);
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
        // F5 = manual re-scrape (the window otherwise updates live via the 300 ms sim poll
        // + the scrape RowsUpdated event; this forces an immediate fresh read).
        if (keyData == Keys.F5) { _ = InitialScrape(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        TearDown();
        base.OnFormClosed(e);
    }

    // Idempotent teardown shared by real close AND Dispose(). The aircraft-swap
    // cleanup calls Dispose() directly — Form.Dispose() does NOT raise FormClosed,
    // and Close() is cancelled by the hide-on-close guard above, so without this
    // the timers + the form-owned Coherent client survived the swap.
    private bool _tornDown;
    private void TearDown()
    {
        if (_tornDown) return;
        _tornDown = true;
        try { _refreshTimer?.Stop(); _refreshTimer?.Dispose(); } catch { }
        try { _standbyTimer?.Stop(); _standbyTimer?.Dispose(); } catch { }
        try { _simPoll?.Stop(); _simPoll?.Dispose(); } catch { }
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) TearDown();
        base.Dispose(disposing);
    }
}
