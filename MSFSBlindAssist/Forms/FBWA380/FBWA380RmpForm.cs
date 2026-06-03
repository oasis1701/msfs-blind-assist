using System.Linq;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible Radio Management Panel (RMP) window for the FlyByWire A380X — replaces the old
/// per-key button panel (which was impractical to operate one digit at a time). It is a
/// SCRAPED window: it reads the live A380X_RMP_1 (Captain) / A380X_RMP_2 (First Officer)
/// Coherent view via <see cref="CoherentDisplayClient"/> + coherent-rmp-agent.js, showing the
/// VHF1/2/3 active + standby frequencies (incl. the live scratchpad while typing), the
/// transmit/selected markers, the transponder and the message line as a list the user walks
/// with the UP/DOWN arrows.
///
/// To tune: pick the radio (Select 1/2/3), type the frequency (digits only — the FBW RMP
/// auto-completes, so "8" → 118.000, "11850" → 118.500) and press Enter / Set standby, or
/// Set active to set-and-swap in one step. Each of these runs the FULL real sequence MSFSBA
/// verified the RMP needs: HOLD Clear for a full scratchpad clear (an invalid leftover entry
/// otherwise blocks all digits), type the digit H-events, LSK to LOAD the standby, then ADK to
/// SWAP it active. Swap/Clear and the page selectors (VHF/HF/TEL/SQWK/NAV/MENU) and the standby
/// mode Up/Down are also buttons. Captain ↔ First Officer is a combo that re-points the scrape
/// and the H-event index. The result is announced as a live region as it commits.
/// </summary>
public sealed class FBWA380RmpForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FlyByWireA380Definition _def;
    private readonly SimConnectManager _sim;

    private CoherentDisplayClient _disp = null!;
    private int _rmp = 1;                 // 1 = Captain, 2 = First Officer
    private bool _haveRows;

    // Live-region change tracking: when the SELECTED row's standby auto-completes/changes as the
    // user types (e.g. "121.950" → "118.000"), or a new RMP message appears, announce it so the
    // pilot hears the result without re-reading the list. The active-frequency change is NOT
    // announced here (the global FBW_RMP_FREQUENCY_ACTIVE auto-announce owns that — no duplicate).
    private bool _firstScrape = true;
    private string _lastSelectedStandby = "";
    private string _lastMessage = "";

    // The transceiver row the RMP currently has selected (0=row1/VHF1 … 2=row3/VHF3), scraped from
    // the ", selected" marker. Frequency entry + swap act on this row's LSK_/ADK_ keys.
    private int _selectedRowIndex;
    private bool _busy;            // a tune sequence is in progress — block re-entry

    private ComboBox _side = null!;
    private ListBox _rows = null!;
    private TextBox _freq = null!;
    private Label _status = null!;

    public FBWA380RmpForm(ScreenReaderAnnouncer announcer, FlyByWireA380Definition def, SimConnectManager sim)
    {
        _announcer = announcer;
        _def = def;
        _sim = sim;
        BuildUi();
        StartScrape();
    }

    private void BuildUi()
    {
        Text = "A380 Radio Management Panel";
        Size = new Size(640, 660);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        var sideLabel = new Label { Text = "&Radio panel:", Location = new Point(12, 14), Size = new Size(90, 22) };
        _side = new ComboBox
        {
            Location = new Point(104, 11), Size = new Size(180, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Radio panel side"
        };
        _side.Items.AddRange(new object[] { "Captain (RMP 1)", "First Officer (RMP 2)" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += (_, _) =>
        {
            _rmp = _side.SelectedIndex == 1 ? 2 : 1;
            SwitchSide();
        };

        _status = new Label
        {
            Location = new Point(300, 14), Size = new Size(320, 22),
            Text = "Connecting…", AccessibleName = "Status"
        };

        _rows = new ListBox
        {
            Location = new Point(12, 44), Size = new Size(608, 300),
            Font = new Font("Consolas", 11),
            AccessibleName = "RMP display rows",
            IntegralHeight = false
        };

        var freqLabel = new Label
        {
            Text = "&Frequency for the selected radio (digits only, e.g. 11850 for 118.500):",
            Location = new Point(12, 356), Size = new Size(608, 22)
        };
        _freq = new TextBox
        {
            Location = new Point(12, 380), Size = new Size(200, 26),
            AccessibleName = "Frequency — digits only, then Enter to set standby"
        };
        // Enter = Set standby (the realistic step). Tune the SELECTED radio's standby.
        _freq.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await TuneSelected(makeActive: false);
            }
        };

        var setStby = new Button { Text = "&Set standby", Location = new Point(220, 379), Size = new Size(110, 28), AccessibleName = "Set standby on the selected radio" };
        setStby.Click += async (_, _) => await TuneSelected(makeActive: false);
        var setActive = new Button { Text = "Set &active", Location = new Point(336, 379), Size = new Size(110, 28), AccessibleName = "Set active now on the selected radio (sets standby then swaps)" };
        setActive.Click += async (_, _) => await TuneSelected(makeActive: true);

        // Action buttons fire the real cockpit keypad H-events on the selected RMP.
        int x = 12, yPage = 420, yRow = 456, ySwap = 492;
        AddKeyButton("&VHF", "VHF", ref x, yPage);
        AddKeyButton("&HF", "HF", ref x, yPage);
        AddKeyButton("&TEL", "TEL", ref x, yPage);
        AddKeyButton("S&QWK", "SQWK", ref x, yPage);
        AddKeyButton("&NAV", "NAV", ref x, yPage);
        AddKeyButton("&MENU", "MENU", ref x, yPage);

        x = 12;
        // Select which transceiver row the frequency entry + swap act on (page-agnostic: on the
        // HF/TEL page these select the HF/TEL rows). Guarded so they only fire when not already selected.
        AddSelectButton("Select &1", 0, ref x, yRow, 95);
        AddSelectButton("Select &2", 1, ref x, yRow, 95);
        AddSelectButton("Select &3", 2, ref x, yRow, 95);
        AddActionButton("S&wap", () => SwapSelected(), ref x, yRow, 90);
        AddActionButton("C&lear", () => { _ = FullClearAndRefresh(); }, ref x, yRow, 90);

        x = 12;
        AddKeyButton("&Up (mode)", "UP", ref x, ySwap, 110);
        AddKeyButton("&Down (mode)", "DOWN", ref x, ySwap, 110);

        var refresh = new Button { Text = "Re&fresh", Location = new Point(12, 560), Size = new Size(100, 32), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { _ = RefreshNow(); };
        var close = new Button { Text = "&Close", Location = new Point(120, 560), Size = new Size(100, 32), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        var help = new Label
        {
            Location = new Point(232, 556), Size = new Size(388, 64),
            Text = "To tune: choose the radio (Select 1/2/3), type the frequency,\n" +
                   "then Enter or Set standby. Use Set active to set it and swap to\n" +
                   "active in one step, or Swap to swap standby and active."
        };

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _rows, freqLabel, _freq, setStby, setActive, refresh, close, help });
        CancelButton = close;
        Load += (_, _) => _rows.Focus();
    }

    private void AddKeyButton(string label, string key, ref int x, int y, int width = 70)
    {
        var b = new Button
        {
            Text = label, Location = new Point(x, y), Size = new Size(width, 30),
            AccessibleName = label.Replace("&", "")
        };
        b.Click += (_, _) =>
        {
            if (_busy) return;
            _def.SendRmpKey(_rmp, key, _sim);
            ScheduleRefresh();
        };
        Controls.Add(b);
        x += width + 6;
    }

    private void AddActionButton(string label, Action onClick, ref int x, int y, int width = 90)
    {
        var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(width, 30), AccessibleName = label.Replace("&", "") };
        b.Click += (_, _) => { if (!_busy) onClick(); };
        Controls.Add(b);
        x += width + 6;
    }

    private void AddSelectButton(string label, int rowIndex, ref int x, int y, int width = 95)
    {
        var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(width, 30), AccessibleName = $"Select radio {rowIndex + 1}" };
        b.Click += (_, _) =>
        {
            if (_busy) return;
            if (_selectedRowIndex == rowIndex) { _announcer?.Announce($"Radio {rowIndex + 1} already selected"); return; }
            // LSK on a NOT-selected row selects it (it only loads when already selected).
            _def.SendRmpKey(_rmp, $"LSK_{rowIndex + 1}", _sim);
            ScheduleRefresh();
        };
        Controls.Add(b);
        x += width + 6;
    }

    private void SwapSelected()
    {
        // ADK on the selected row swaps active/standby (it would only select if not selected).
        _def.SendRmpKey(_rmp, $"ADK_{_selectedRowIndex + 1}", _sim);
        ScheduleRefresh();
    }

    // Tune the SELECTED radio: FULL-clear any stuck entry, type the digits (the RMP auto-completes),
    // LSK to load the standby, then optionally ADK to swap it active. This is the sequence the FBW
    // RMP actually requires — a plain "type the digits" does nothing because an invalid scratchpad
    // blocks input and an un-loaded entry is never committed (live-verified).
    private async Task TuneSelected(bool makeActive)
    {
        if (_busy) return;
        string digits = new string((_freq.Text ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { _announcer?.Announce("Type a frequency first"); return; }

        _busy = true;
        _freq.Enabled = false;
        _announcer?.Announce(makeActive ? "Setting active…" : "Setting standby…");
        try
        {
            int row = _selectedRowIndex + 1;             // 1-based LSK/ADK
            await FullClearAsync();
            foreach (char c in digits)
            {
                _def.SendRmpKey(_rmp, $"DIGIT_{c}", _sim);
                await Task.Delay(130);                   // let each digit (incl. repeats) register
            }
            await Task.Delay(120);
            _def.SendRmpKey(_rmp, $"LSK_{row}", _sim);    // load the standby
            await Task.Delay(350);
            if (makeActive)
            {
                _def.SendRmpKey(_rmp, $"ADK_{row}", _sim); // swap standby <-> active
                await Task.Delay(350);
            }
            _freq.Clear();
            Apply(await _disp.ScrapeNowAsync());          // silent refresh; live region announces the result
        }
        catch { _announcer?.Announce("Radio entry failed"); }
        finally { _busy = false; _freq.Enabled = true; _freq.Focus(); }
    }

    // HOLD the Clear key past the FBW 1-second timer so it does a FULL scratchpad clear (a tap is
    // only a single-digit backspace). Required before typing — an invalid entry blocks all digits.
    private async Task FullClearAsync()
    {
        try
        {
            _def.SendRmpKeyPress(_rmp, "DIGIT_CLR", _sim);
            await Task.Delay(1150);
        }
        finally { _def.SendRmpKeyRelease(_rmp, "DIGIT_CLR", _sim); }
        await Task.Delay(200);
    }

    private async Task FullClearAndRefresh()
    {
        if (_busy) return;
        _busy = true;
        try { await FullClearAsync(); Apply(await _disp.ScrapeNowAsync()); }
        finally { _busy = false; }
    }

    private void StartScrape()
    {
        _haveRows = false;
        _firstScrape = true;           // re-baseline the change announcer on (re)connect / side switch
        _lastSelectedStandby = "";
        _lastMessage = "";
        _disp = new CoherentDisplayClient($"A380X_RMP_{_rmp}", 800, "coherent-rmp-agent.js");
        _disp.RowsUpdated += OnRowsUpdated;
        _disp.Start();
        _ = InitialScrape();
    }

    private void SwitchSide()
    {
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        _status.Text = _rmp == 1 ? "Captain RMP — connecting…" : "First Officer RMP — connecting…";
        StartScrape();
    }

    private async Task InitialScrape() => Apply(await _disp.ScrapeNowAsync());

    private async Task RefreshNow()
    {
        Apply(await _disp.ScrapeNowAsync());
        _announcer?.Announce("Refreshed");
    }

    // Re-scrape shortly after an action so the list reflects the new state (the RMP
    // updates a frame or two later — same pattern the MFD form uses after a click).
    private void ScheduleRefresh()
    {
        var t = new System.Windows.Forms.Timer { Interval = 350 };
        t.Tick += async (s, _) => { t.Stop(); t.Dispose(); await RefreshNow(); };
        t.Start();
    }

    private void OnRowsUpdated(List<string> rows) => Apply(rows);

    private void Apply(List<string>? rows)
    {
        if (rows == null || rows.Count == 0)
        {
            if (!_haveRows) _status.Text = "RMP view not reachable — is the A380X loaded and the RMP powered?";
            return;
        }
        _haveRows = true;
        _status.Text = _rmp == 1 ? "Live — Captain RMP" : "Live — First Officer RMP";

        // Preserve the user's row position across refreshes (don't yank the screen reader to top).
        int sel = _rows.SelectedIndex;
        _rows.BeginUpdate();
        _rows.Items.Clear();
        foreach (var r in rows) _rows.Items.Add(r);
        if (sel >= 0 && sel < _rows.Items.Count) _rows.SelectedIndex = sel;
        _rows.EndUpdate();

        // Track which transceiver row the RMP has selected (its position among the "X: active …"
        // rows), so the frequency entry + swap fire the correct LSK_/ADK_ index.
        int tIdx = 0;
        foreach (var r in rows)
        {
            if (r.IndexOf(": active ", System.StringComparison.Ordinal) < 0) continue;
            if (r.IndexOf(", selected", System.StringComparison.Ordinal) >= 0) { _selectedRowIndex = tIdx; break; }
            tIdx++;
        }

        AnnounceChanges(rows);
    }

    // Live-region announcing: speak the selected row's standby when it auto-completes/changes,
    // and any new RMP message. Silent on the first scrape (baseline). The active frequency is
    // intentionally NOT announced here — the global active-freq auto-announce already does it.
    private void AnnounceChanges(List<string> rows)
    {
        string selRow = rows.Find(r => r.IndexOf(", selected", System.StringComparison.Ordinal) >= 0) ?? "";
        string standby = Token(selRow, "standby ");
        string msgRow = rows.Find(r => r.StartsWith("Message: ", System.StringComparison.Ordinal)) ?? "";
        string message = msgRow.Length > 0 ? msgRow.Substring("Message: ".Length) : "";

        if (!_firstScrape)
        {
            if (standby.Length > 0 && standby != _lastSelectedStandby)
                _announcer?.Announce($"Standby {standby}");
            if (message.Length > 0 && message != _lastMessage)
                _announcer?.Announce(message);
        }
        _lastSelectedStandby = standby;
        _lastMessage = message;
        _firstScrape = false;
    }

    // Extract the value following a label up to the next ", " (or end of line).
    private static string Token(string row, string after)
    {
        int i = row.IndexOf(after, System.StringComparison.Ordinal);
        if (i < 0) return "";
        int start = i + after.Length;
        int end = row.IndexOf(", ", start, System.StringComparison.Ordinal);
        return (end < 0 ? row.Substring(start) : row.Substring(start, end - start)).Trim();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RefreshNow(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
