using System.Linq;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible Radio Management Panel (RMP) window for the FlyByWire A380X (Ctrl+Shift+R, input
/// mode). It SCRAPES the live A380X_RMP_1 (Captain) / A380X_RMP_2 (First Officer) Coherent view
/// (coherent-rmp-agent.js, via <see cref="CoherentDisplayClient"/>) into a list of rows you walk
/// with the arrows — VHF1/2/3 active + standby (incl. the live scratchpad while typing), which is
/// transmitting/selected, the transponder and the message line — auto-refreshing (no manual
/// refresh) and announcing changes as a live region.
///
/// Tuning is REALISTIC, driven by the real cockpit keypad H-events:
///   • Type the frequency into the Frequency field — each digit is keyed into the RMP in REAL
///     TIME (so the standby auto-completes as you type, e.g. "8" → 118.000); Backspace = one-digit
///     clear. The FBW RMP auto-completes, so no decimal point.
///   • Enter = LOAD the typed standby (the line key). The swap is NOT automatic — that's the
///     pilot's call.
///   • Swap = exchange active ↔ standby on the selected radio. Clear = full scratchpad clear
///     (needed if a bad entry got stuck — it blocks further digits otherwise).
///   • Radio 1/2/3 (or Ctrl+1/2/3) select the radio the entry/swap act on; page selectors
///     (VHF/HF/TEL/SQWK/NAV/MENU) and the standby-mode Up/Down are buttons too.
/// Captain ↔ First Officer is a combo that re-points the scrape and the H-event index.
/// </summary>
public sealed class FBWA380RmpForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FlyByWireA380Definition _def;
    private readonly SimConnectManager _sim;

    private CoherentDisplayClient _disp = null!;
    private int _rmp = 1;                  // 1 = Captain, 2 = First Officer
    private bool _haveRows;

    // Live-region change tracking (announce the SELECTED row's standby + new messages, not the
    // active freq — the global FBW_RMP_FREQUENCY_ACTIVE auto-announce owns that, so no duplicate).
    private bool _firstScrape = true;
    private string _lastSelectedStandby = "";
    private string _lastMessage = "";

    // The transceiver row the RMP currently has selected (0=row1 … 2=row3), scraped from the
    // ", selected" marker. The Frequency entry + Enter(load) + Swap act on this row's keys.
    private int _selectedRowIndex;
    private bool _busy;                    // a held-clear sequence is running — block re-entry

    private ComboBox _side = null!;
    private ListBox _rows = null!;
    private TextBox _freq = null!;
    private Label _status = null!;
    private System.Windows.Forms.Timer? _refreshTimer;

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
        Size = new Size(640, 640);
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
        _side.SelectedIndexChanged += (_, _) => { _rmp = _side.SelectedIndex == 1 ? 2 : 1; SwitchSide(); };

        _status = new Label { Location = new Point(300, 14), Size = new Size(320, 22), Text = "Connecting…", AccessibleName = "Status" };

        _rows = new ListBox
        {
            Location = new Point(12, 44), Size = new Size(608, 300),
            Font = new Font("Consolas", 11),
            AccessibleName = "RMP display rows — read with the arrow keys",
            IntegralHeight = false
        };

        var freqLabel = new Label
        {
            Text = "&Frequency for the selected radio (digits only — typed in real time, Enter loads standby):",
            Location = new Point(12, 352), Size = new Size(608, 22)
        };
        _freq = new TextBox
        {
            Location = new Point(12, 376), Size = new Size(300, 26),
            AccessibleName = "Frequency — type digits, each is keyed live; Enter loads the standby"
        };
        // Real-time: each digit is fired into the RMP as it is typed; the standby auto-completes.
        _freq.KeyPress += (s, e) =>
        {
            if (e.KeyChar >= '0' && e.KeyChar <= '9')
            {
                _def.SendRmpKey(_rmp, $"DIGIT_{e.KeyChar}", _sim);
                ScheduleRefresh();
                // leave the char in the box as a visual echo of what was typed
            }
            else if (e.KeyChar != (char)Keys.Back)
            {
                e.Handled = true;   // ignore the decimal point and any non-digit
            }
        };
        _freq.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _def.SendRmpKey(_rmp, $"LSK_{_selectedRowIndex + 1}", _sim);  // load standby (manual; no auto-swap)
                _announcer?.AnnounceImmediate("Standby loaded");
                _freq.Clear();
                ScheduleRefresh();
            }
            else if (e.KeyCode == Keys.Back)
            {
                _def.SendRmpKey(_rmp, "DIGIT_CLR", _sim);  // one-digit backspace in the RMP entry
                ScheduleRefresh();
                // let the TextBox delete its own character too (don't suppress)
            }
        };

        int x = 12, yPage = 412, yAct = 448, yMode = 484;
        AddKeyButton("&VHF", "VHF", "VHF page", ref x, yPage);
        AddKeyButton("&HF", "HF", "HF page", ref x, yPage);
        AddKeyButton("&TEL", "TEL", "Telephone page", ref x, yPage);
        AddKeyButton("S&QWK", "SQWK", "Transponder page", ref x, yPage);
        AddKeyButton("&NAV", "NAV", "Nav page", ref x, yPage);
        AddKeyButton("&MENU", "MENU", "Menu", ref x, yPage);

        x = 12;
        AddRadioButton("Radio &1", 0, ref x, yAct);
        AddRadioButton("Radio &2", 1, ref x, yAct);
        AddRadioButton("Radio &3", 2, ref x, yAct);
        AddActionButton("S&wap", "Swapped", () => _def.SendRmpKey(_rmp, $"ADK_{_selectedRowIndex + 1}", _sim), ref x, yAct, 90);
        AddHeldClearButton("C&lear", ref x, yAct, 90);

        x = 12;
        AddKeyButton("Mode &up", "UP", "Mode up", ref x, yMode, 110);
        AddKeyButton("Mode &down", "DOWN", "Mode down", ref x, yMode, 110);

        var close = new Button { Text = "&Close", Location = new Point(12, 552), Size = new Size(100, 32), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        var help = new Label
        {
            Location = new Point(124, 548), Size = new Size(496, 64),
            Text = "Choose the radio (Radio 1/2/3 or Ctrl+1/2/3), type the frequency\n" +
                   "(each digit is keyed live, no decimal point), press Enter to load the\n" +
                   "standby, then Swap to make it active. Clear fully resets the entry."
        };

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _rows, freqLabel, _freq, close, help });
        CancelButton = close;
        Load += (_, _) => _rows.Focus();
    }

    // A page/mode key: fire the H-event, give immediate spoken feedback, refresh.
    private void AddKeyButton(string label, string key, string spoken, ref int x, int y, int width = 70)
    {
        var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(width, 30), AccessibleName = label.Replace("&", "") };
        b.Click += (_, _) =>
        {
            if (_busy) return;
            _def.SendRmpKey(_rmp, key, _sim);
            _announcer?.AnnounceImmediate(spoken);
            ScheduleRefresh();
        };
        Controls.Add(b);
        x += width + 6;
    }

    // Radio 1/2/3 = the line key (LSK): selects the row, or loads the typed standby if already selected.
    private void AddRadioButton(string label, int rowIndex, ref int x, int y, int width = 80)
    {
        var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(width, 30), AccessibleName = $"Radio {rowIndex + 1}, select or load" };
        b.Click += (_, _) => PressLine(rowIndex);
        Controls.Add(b);
        x += width + 6;
    }

    private void PressLine(int rowIndex)
    {
        if (_busy) return;
        bool wasSelected = _selectedRowIndex == rowIndex;
        _def.SendRmpKey(_rmp, $"LSK_{rowIndex + 1}", _sim);
        _announcer?.AnnounceImmediate(wasSelected ? "Standby loaded" : $"Radio {rowIndex + 1} selected");
        ScheduleRefresh();
    }

    private void AddActionButton(string label, string spoken, Action onClick, ref int x, int y, int width = 90)
    {
        var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(width, 30), AccessibleName = label.Replace("&", "") };
        b.Click += (_, _) => { if (_busy) return; onClick(); _announcer?.AnnounceImmediate(spoken); ScheduleRefresh(); };
        Controls.Add(b);
        x += width + 6;
    }

    private void AddHeldClearButton(string label, ref int x, int y, int width = 90)
    {
        var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(width, 30), AccessibleName = "Clear the entry" };
        b.Click += (_, _) => { _ = FullClearAndRefresh(); };
        Controls.Add(b);
        x += width + 6;
    }

    private void StartScrape()
    {
        _haveRows = false;
        _firstScrape = true;
        _lastSelectedStandby = "";
        _lastMessage = "";
        // ~500 ms poll so the list + live-region track typing/actions snappily.
        _disp = new CoherentDisplayClient($"A380X_RMP_{_rmp}", 500, "coherent-rmp-agent.js");
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

    // Debounced re-scrape after an action/keystroke (the RMP updates a frame or two later). One
    // reusable timer so fast typing doesn't spawn many timers.
    private void ScheduleRefresh()
    {
        if (_refreshTimer == null)
        {
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _refreshTimer.Tick += async (_, _) => { _refreshTimer!.Stop(); Apply(await _disp.ScrapeNowAsync()); };
        }
        _refreshTimer.Stop();
        _refreshTimer.Start();
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

        int sel = _rows.SelectedIndex;
        _rows.BeginUpdate();
        _rows.Items.Clear();
        foreach (var r in rows) _rows.Items.Add(r);
        if (sel >= 0 && sel < _rows.Items.Count) _rows.SelectedIndex = sel;
        _rows.EndUpdate();

        int tIdx = 0;
        foreach (var r in rows)
        {
            if (r.IndexOf(": active ", System.StringComparison.Ordinal) < 0) continue;
            if (r.IndexOf(", selected", System.StringComparison.Ordinal) >= 0) { _selectedRowIndex = tIdx; break; }
            tIdx++;
        }

        AnnounceChanges(rows);
    }

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

    private static string Token(string row, string after)
    {
        int i = row.IndexOf(after, System.StringComparison.Ordinal);
        if (i < 0) return "";
        int start = i + after.Length;
        int end = row.IndexOf(", ", start, System.StringComparison.Ordinal);
        return (end < 0 ? row.Substring(start) : row.Substring(start, end - start)).Trim();
    }

    // HOLD Clear past the FBW 1 s timer = FULL scratchpad clear (a tap is a single-digit backspace).
    private async Task FullClearAndRefresh()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _def.SendRmpKeyPress(_rmp, "DIGIT_CLR", _sim);
            await Task.Delay(1150);
        }
        finally { _def.SendRmpKeyRelease(_rmp, "DIGIT_CLR", _sim); _busy = false; }
        _freq.Clear();
        _announcer?.AnnounceImmediate("Entry cleared");
        Apply(await _disp.ScrapeNowAsync());
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Ctrl+1/2/3 = the line keys (select the radio / load the standby), MCDU-style.
        if (keyData == (Keys.Control | Keys.D1)) { PressLine(0); return true; }
        if (keyData == (Keys.Control | Keys.D2)) { PressLine(1); return true; }
        if (keyData == (Keys.Control | Keys.D3)) { PressLine(2); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _refreshTimer?.Stop(); _refreshTimer?.Dispose(); } catch { }
        try { _disp.RowsUpdated -= OnRowsUpdated; _disp.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
