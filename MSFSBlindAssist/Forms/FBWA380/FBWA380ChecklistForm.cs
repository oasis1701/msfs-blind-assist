using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible A380X Electronic Checklist (ECL) window — the live normal checklists
/// and active abnormal procedures, read from the E/WD Coherent view via
/// <see cref="CoherentEclClient"/> (no injection) and driven by the real ECP
/// push-button L-vars.
///
/// The ECL is driven by the FWS, not by DOM clicks, so the actions are the cockpit
/// ECP buttons pulsed as L-vars — and they fully drive the checklist (verified live
/// against FwsCore/FwsNormalChecklists):
///   • C/L      (A32NX_BTN_CL)        show / hide the checklist overlay
///   • UP/DOWN  (A32NX_BTN_UP/DOWN)   move the FWS line cursor (menu AND items)
///   • CHECK    (A32NX_BTN_CHECK_LH)  on the menu  → open the selected checklist
///                                    in a checklist → tick the selected manual item
///   • CLR      (A32NX_BTN_CLR)       step back to the parent menu
///   • ABN PROC (A32NX_BTN_ABNPROC)   open the abnormal-procedure list
/// Sensed items tick themselves as the FWS detects the action (e.g. PARK BRK ON),
/// and the cursor auto-advances. The list box mirrors the FWS cursor: arrowing
/// Up/Down moves the real ECL cursor and reads the new line; Enter checks/selects.
/// </summary>
public sealed class FBWA380ChecklistForm : Form
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly CoherentEclClient _ecl;
    private ListBox _list = null!;
    private Label _status = null!;
    private List<EclRow> _rows = new();
    private HashSet<string> _lastChecked = new();
    private bool _haveRows;
    private bool _cursorActive;   // last scrape had a selected (cursor) line
    private bool _busy;           // guard against overlapping ECP pulses

    public FBWA380ChecklistForm(ScreenReaderAnnouncer announcer, SimConnectManager sim)
    {
        _announcer = announcer;
        _sim = sim;
        _ecl = new CoherentEclClient();
        _ecl.RowsUpdated += OnRowsUpdated;
        BuildUi();
        _ecl.Start();
        _ = InitialScrape();
    }

    private void BuildUi()
    {
        Text = "A380 Checklists";
        Size = new Size(720, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        _status = new Label
        {
            Location = new Point(12, 10), Size = new Size(684, 54),
            Text = "Connecting to the Electronic Checklist…", AccessibleName = "Status"
        };
        _list = new ListBox
        {
            Location = new Point(12, 68), Size = new Size(684, 404),
            Font = new Font("Consolas", 11), AccessibleName = "Checklist lines",
            IntegralHeight = false
        };
        // Arrow keys drive the real ECL cursor (when a checklist is shown), so the
        // selection always mirrors the cockpit cursor — Enter then checks/selects it.
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down) e.IsInputKey = true; };
        _list.KeyDown += OnListKeyDown;

        // ECP controls — each pulses the matching real ECP button L-var.
        var y = 480;
        Button Btn(string text, int x, int w, string lvar, string say)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30), AccessibleName = text.Replace("&", "") };
            b.Click += (_, _) => PulseEcp(lvar, say);
            Controls.Add(b);
            return b;
        }
        Btn("&Show / hide (C/L)", 12, 140, "A32NX_BTN_CL", "Checklist toggled");
        Btn("Cursor &Up", 158, 90, "A32NX_BTN_UP", "Up");
        Btn("Cursor &Down", 252, 100, "A32NX_BTN_DOWN", "Down");
        Btn("&Check / select", 356, 110, "A32NX_BTN_CHECK_LH", "Check");
        Btn("C&lear / back", 470, 100, "A32NX_BTN_CLR", "Clear");
        Btn("&Abnormal", 574, 122, "A32NX_BTN_ABNPROC", "Abnormal procedures");

        var refresh = new Button { Text = "&Refresh", Location = new Point(12, y + 36), Size = new Size(90, 30), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { _ = RefreshNow(); };
        var close = new Button { Text = "Cl&ose", Location = new Point(110, y + 36), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _status, _list, refresh, close });
        CancelButton = close;
        Load += (_, _) => _list.Focus();
    }

    // In the list, Up/Down move the real ECL cursor (if a checklist is shown) and
    // Enter checks/selects the current line. Before a checklist is shown, Up/Down
    // just read the mirrored lines and Enter shows the checklist to begin.
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = e.SuppressKeyPress = true;
            if (_cursorActive) PulseEcp("A32NX_BTN_CHECK_LH", "Check");
            else PulseEcp("A32NX_BTN_CL", "Checklist shown");
            return;
        }
        if (!_cursorActive) return;   // let the list read normally until a checklist is up
        if (e.KeyCode == Keys.Down) { e.Handled = e.SuppressKeyPress = true; PulseEcp("A32NX_BTN_DOWN", "Down"); }
        else if (e.KeyCode == Keys.Up) { e.Handled = e.SuppressKeyPress = true; PulseEcp("A32NX_BTN_UP", "Up"); }
    }

    // Pulse a momentary ECP button L-var (1 -> 0) through the reliable MobiFlight
    // calculator path; the FWS reads it and drives the checklist. Re-scrape after a
    // short delay so the user hears the result on the now-selected line.
    private async void PulseEcp(string lvar, string say)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _sim?.ExecuteCalculatorCode($"1 (>L:{lvar})");
            await Task.Delay(140);
            _sim?.ExecuteCalculatorCode($"0 (>L:{lvar})");
            await Task.Delay(320);
            var rows = await _ecl.ScrapeNowAsync();
            Apply(rows, announceChecks: true);
            // Announce the line the FWS cursor now sits on (what UP/DOWN/CHECK act on);
            // fall back to a generic confirmation if no line is selected.
            var sel = _rows.FirstOrDefault(r => r.selected);
            _announcer?.Announce(sel != null && !string.IsNullOrEmpty(sel.text) ? Speakable(sel) : say);
        }
        catch { _announcer?.Announce(say); }
        finally { _busy = false; }
    }

    private async Task InitialScrape() => Apply(await _ecl.ScrapeNowAsync(), announceChecks: false);

    private async Task RefreshNow()
    {
        Apply(await _ecl.ScrapeNowAsync(), announceChecks: false);
        _announcer?.Announce("Refreshed");
    }

    private void OnRowsUpdated(List<EclRow> rows) => Apply(rows, announceChecks: true);

    private void Apply(List<EclRow> rows, bool announceChecks)
    {
        if (rows == null || rows.Count == 0)
        {
            if (!_haveRows) _status.Text = "Checklist not reachable — is the A380X loaded and the E/WD powered?";
            return;
        }
        _haveRows = true;
        _cursorActive = rows.Any(r => r.selected);
        _status.Text =
            "Live Electronic Checklist — normal checklists and any active ECAM procedure. "
            + "Press Show / hide (C/L) to bring up the checklist, then arrow Up/Down to move "
            + "the cursor and read each line; press Enter (or Check / select) to open the "
            + "highlighted checklist or tick the highlighted item. Items also tick themselves "
            + "as you perform the action. Clear / back returns to the menu.";

        // Announce items that NEWLY became checked (sensed auto-completion as the
        // pilot performs actions) — the core accessibility win.
        var nowChecked = new HashSet<string>(rows.Where(r => r.Checked && !string.IsNullOrEmpty(r.text)).Select(r => r.text));
        if (announceChecks && _lastChecked.Count > 0)
        {
            foreach (var t in nowChecked)
                if (!_lastChecked.Contains(t)) _announcer?.Announce($"{t}, checked");
        }
        _lastChecked = nowChecked;

        _rows = rows;
        int keep = _list.SelectedIndex;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in rows) _list.Items.Add(Format(r));
        _list.EndUpdate();

        // Mirror the FWS cursor: land the list selection on the selected line so the
        // screen reader follows the real ECL cursor; else preserve the prior row.
        int selIdx = rows.FindIndex(r => r.selected);
        if (selIdx >= 0) _list.SelectedIndex = selIdx;
        else if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private static string Speakable(EclRow r)
    {
        string box = r.Checked ? ", checked" : (r.type is "item" or "abnormal" ? ", not checked" : "");
        string note = r.style == "manual" ? ", manual" : r.style == "action" ? ", action" : r.style == "caution" ? ", caution" : "";
        return $"{r.text}{box}{note}";
    }

    private static string Format(EclRow r)
    {
        if (r.type == "headline") return r.text;
        string box = r.Checked ? "[done] " : (r.type == "item" || r.type == "abnormal" ? "[ ] " : "");
        string sel = r.selected ? "> " : "";
        string note = r.style == "manual" ? "  (manual)" : r.style == "action" ? "  (action)" : r.style == "caution" ? "  (caution)" : "";
        return $"{sel}{box}{r.text}{note}";
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RefreshNow(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _ecl.RowsUpdated -= OnRowsUpdated; _ecl.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
}
