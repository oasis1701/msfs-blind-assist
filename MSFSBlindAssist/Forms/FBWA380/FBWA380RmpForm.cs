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
/// Input is by a single FREQUENCY edit field (type the digits + Enter and MSFSBA fires the
/// keypad digit H-events — the FBW RMP auto-completes, so "8" → 118.000, "11850" → 118.500),
/// plus a small set of action buttons that fire the real cockpit keypad H-events: the page
/// selectors (VHF/HF/TEL/SQWK), the per-row line keys (VHF 1/2/3 = select the row, press again
/// to load the typed standby), the swap keys (swap active/standby) and Clear. Captain ↔ First
/// Officer is a combo that re-points the scrape and the H-event index.
/// </summary>
public sealed class FBWA380RmpForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly FlyByWireA380Definition _def;
    private readonly SimConnectManager _sim;

    private CoherentDisplayClient _disp = null!;
    private int _rmp = 1;                 // 1 = Captain, 2 = First Officer
    private bool _haveRows;

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
            Text = "&Frequency entry (type digits, Enter to type into the selected row):",
            Location = new Point(12, 356), Size = new Size(608, 22)
        };
        _freq = new TextBox
        {
            Location = new Point(12, 380), Size = new Size(200, 26),
            AccessibleName = "Frequency entry — type digits then press Enter"
        };
        _freq.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _def.SendRmpKeypad(_freq.Text, _rmp, _sim, _announcer);
                _freq.SelectAll();
                ScheduleRefresh();
            }
        };

        // Action buttons fire the real cockpit keypad H-events on the selected RMP.
        int x = 12, yPage = 416, yRow = 452, ySwap = 488;
        AddKeyButton("&VHF", "VHF", ref x, yPage);
        AddKeyButton("&HF", "HF", ref x, yPage);
        AddKeyButton("&TEL", "TEL", ref x, yPage);
        AddKeyButton("S&QWK", "SQWK", ref x, yPage);
        AddKeyButton("&NAV", "NAV", ref x, yPage);
        AddKeyButton("&MENU", "MENU", ref x, yPage);

        x = 12;
        // LSK_n: first press selects row n, second press LOADS the typed standby into it.
        AddKeyButton("VHF &1 (select / load)", "LSK_1", ref x, yRow, 150);
        AddKeyButton("VHF &2 (select / load)", "LSK_2", ref x, yRow, 150);
        AddKeyButton("VHF &3 (select / load)", "LSK_3", ref x, yRow, 150);
        AddKeyButton("C&lear", "DIGIT_CLR", ref x, yRow, 90);

        x = 12;
        // ADK_n: first press selects row n, second press SWAPS active/standby.
        AddKeyButton("S&wap 1", "ADK_1", ref x, ySwap, 95);
        AddKeyButton("Swap &2", "ADK_2", ref x, ySwap, 95);
        AddKeyButton("Swap 3", "ADK_3", ref x, ySwap, 95);
        AddKeyButton("&Up (mode)", "UP", ref x, ySwap, 110);
        AddKeyButton("&Down (mode)", "DOWN", ref x, ySwap, 110);

        var refresh = new Button { Text = "Re&fresh", Location = new Point(12, 560), Size = new Size(100, 32), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { _ = RefreshNow(); };
        var close = new Button { Text = "&Close", Location = new Point(120, 560), Size = new Size(100, 32), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        var help = new Label
        {
            Location = new Point(232, 560), Size = new Size(388, 56),
            Text = "Tip: pick the row (VHF 1/2/3), type the frequency + Enter, then\n" +
                   "press that row again to load standby, then Swap to make it active."
        };

        Controls.AddRange(new Control[] { sideLabel, _side, _status, _rows, freqLabel, _freq, refresh, close, help });
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
            _def.SendRmpKey(_rmp, key, _sim);
            ScheduleRefresh();
        };
        Controls.Add(b);
        x += width + 6;
    }

    private void StartScrape()
    {
        _haveRows = false;
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
