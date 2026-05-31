using System.Text;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA380;

/// <summary>
/// Accessible A380X Electronic Checklist (ECL) window — the live normal checklists
/// and abnormal-procedure menu, read from the E/WD Coherent view via
/// <see cref="CoherentEclClient"/> (no injection). The list reads every line with
/// its completion state; items auto-announce as the FWS senses them complete (e.g.
/// "PARK BRK ON" ticks as you set the park brake).
///
/// The ECL is driven by the FWS, not by DOM clicks, so the ACTIONS are the real
/// ECP buttons pulsed as L-vars: C/L (show/hide the checklist), CHECK (tick the
/// FWS-selected item), UP/DOWN (move the FWS selection), CLEAR, ABN PROC. Selecting
/// a specific checklist FROM the menu uses the cockpit KCCU cursor (a known FBW
/// external-tool limitation) — but checklists auto-display by flight phase and C/L
/// shows the active one, after which UP/DOWN/CHECK work from here.
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
        Size = new Size(700, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        _status = new Label
        {
            Location = new Point(12, 10), Size = new Size(660, 38),
            Text = "Connecting to the Electronic Checklist…", AccessibleName = "Status"
        };
        _list = new ListBox
        {
            Location = new Point(12, 52), Size = new Size(660, 420),
            Font = new Font("Consolas", 11), AccessibleName = "Checklist lines",
            IntegralHeight = false
        };

        // ECP controls — each pulses the matching real ECP button L-var.
        var y = 480;
        Button Btn(string text, int x, int w, string lvar, string say) {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 30), AccessibleName = text.Replace("&", "") };
            b.Click += (_, _) => PulseEcp(lvar, say);
            Controls.Add(b);
            return b;
        }
        Btn("&Show / Hide (C/L)", 12, 130, "A32NX_BTN_CL", "Checklist toggled");
        Btn("&Check item", 146, 95, "A32NX_BTN_CHECK_LH", "Check");
        Btn("&Up", 245, 60, "A32NX_BTN_UP", "Up");
        Btn("&Down", 309, 60, "A32NX_BTN_DOWN", "Down");
        Btn("C&lear", 373, 70, "A32NX_BTN_CLR", "Clear");
        Btn("&Abnormal", 447, 95, "A32NX_BTN_ABNPROC", "Abnormal procedures");

        var refresh = new Button { Text = "&Refresh", Location = new Point(12, y + 36), Size = new Size(90, 30), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { _ = RefreshNow(); };
        var close = new Button { Text = "Cl&ose", Location = new Point(110, y + 36), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _status, _list, refresh, close });
        CancelButton = close;
        Load += (_, _) => _list.Focus();
    }

    // Pulse a momentary ECP button L-var (1 -> 0) through the reliable MobiFlight
    // calculator path; the FWS reads it and drives the checklist. Re-scrape after a
    // short delay so the user hears the result.
    private async void PulseEcp(string lvar, string say)
    {
        try
        {
            _sim?.ExecuteCalculatorCode($"1 (>L:{lvar})");
            await Task.Delay(140);
            _sim?.ExecuteCalculatorCode($"0 (>L:{lvar})");
            await Task.Delay(300);
            var rows = await _ecl.ScrapeNowAsync();
            Apply(rows, announceChecks: true);
            // Announce the FWS-selected line (what UP/DOWN/CHECK now act on), else
            // a generic confirmation.
            var sel = _rows.FirstOrDefault(r => r.selected);
            _announcer?.Announce(sel != null && !string.IsNullOrEmpty(sel.text) ? sel.text : say);
        }
        catch { _announcer?.Announce(say); }
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
        _status.Text = "Live Electronic Checklist — normal checklists and any active ECAM procedure. "
            + "Arrow through the lines to read them; perform each action on the system panels. "
            + "C/L shows or hides the checklist. (Ticking individual items and picking a checklist "
            + "from the menu use the cockpit cursor — a FlyByWire limitation.)";

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
        if (keep >= 0 && keep < _list.Items.Count) _list.SelectedIndex = keep;
        else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
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
