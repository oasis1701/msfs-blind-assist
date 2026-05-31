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
    // The live ECL is read through the SHARED A380X_EWD monitor connection — the
    // ECL renders on the same Coherent view as the E/WD failures, and Coherent GT
    // (Chromium 49) allows only ONE inspector socket per page, so a separate
    // connection would be rejected (which is why the window used to show nothing).
    private readonly CoherentEWDClient? _ewd;
    private ListBox _list = null!;
    private Label _status = null!;
    private List<EclRow> _rows = new();
    private HashSet<string> _lastChecked = new();
    private string _lastAppliedHash = "";   // last row-set rendered — skip identical re-applies
    private bool _haveRows;
    private bool _cursorActive;   // last scrape had a selected (cursor) line
    private bool _busy;           // guard against overlapping ECP pulses
    private bool _weShowedOverlay; // we toggled the C/L overlay ON, so hide it on close

    public FBWA380ChecklistForm(ScreenReaderAnnouncer announcer, SimConnectManager sim, CoherentEWDClient? ewd)
    {
        _announcer = announcer;
        _sim = sim;
        _ewd = ewd;
        if (_ewd != null)
        {
            _ewd.EclRowsUpdated += OnRowsUpdated;
            _ewd.EclActive = true;   // tell the shared monitor to poll + push ECL rows
        }
        BuildUi();
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

        // Only controls with NO keyboard equivalent get a button. The checklist is
        // shown automatically when this window opens and hidden when it closes;
        // arrow Up/Down move the cursor, Enter checks/selects, Backspace steps back —
        // so the old Show/hide, Up, Down and Check/select buttons are gone (they only
        // duplicated the keys and made the workflow confusing).
        var y = 480;
        var abn = new Button { Text = "&Abnormal procedures", Location = new Point(12, y), Size = new Size(180, 30), AccessibleName = "Abnormal procedures" };
        abn.Click += (_, _) => PulseEcp("A32NX_BTN_ABNPROC", "Abnormal procedures");
        var refresh = new Button { Text = "&Refresh", Location = new Point(200, y), Size = new Size(90, 30), AccessibleName = "Refresh" };
        refresh.Click += (_, _) => { _ = RefreshNow(); };
        var close = new Button { Text = "Cl&ose", Location = new Point(298, y), Size = new Size(90, 30), DialogResult = DialogResult.OK, AccessibleName = "Close" };
        close.Click += (_, _) => Close();

        Controls.AddRange(new Control[] { _status, _list, abn, refresh, close });
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
            // The overlay is auto-shown, so Enter opens the highlighted checklist /
            // ticks the highlighted item. (Fallback: if it somehow isn't up, show it.)
            if (_cursorActive) PulseEcp("A32NX_BTN_CHECK_LH", "Check");
            else PulseEcp("A32NX_BTN_CL", "Checklist shown");
            return;
        }
        if (e.KeyCode == Keys.Back)   // Backspace = Clear / step back to the menu
        {
            e.Handled = e.SuppressKeyPress = true;
            PulseEcp("A32NX_BTN_CLR", "Back");
            return;
        }
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
            await Task.Delay(110);
            _sim?.ExecuteCalculatorCode($"0 (>L:{lvar})");
            await Task.Delay(230);
            var rows = await ScrapeEcl();
            // Apply rebuilds the list and moves the selection to the FWS cursor line,
            // which the screen reader reads on its own — so we do NOT also announce it
            // here (that produced the duplicate read). Only fall back to a generic
            // confirmation if the scrape came back empty (nothing to read).
            Apply(rows, announceChecks: true);
            if (_rows.Count == 0) _announcer?.Announce(say);
        }
        catch { _announcer?.Announce(say); }
        finally { _busy = false; }
    }

    private async Task InitialScrape()
    {
        // The checklist is an E/WD overlay that exists only when C/L is toggled on.
        // First give the ECL Coherent connection a moment to come up and poll (a cold
        // open can take a second or two); only if still empty do we toggle the overlay
        // on ourselves. This is what fixes the old "not reachable" message, which only
        // ever meant the overlay was hidden / the read hadn't connected yet.
        List<EclRow> rows = new();
        for (int i = 0; i < 8; i++)
        {
            rows = await ScrapeEcl();
            if (rows.Count > 0) break;
            await Task.Delay(450);
        }
        if (rows.Count == 0)
        {
            _weShowedOverlay = true;
            await PulseClRaw();
            await Task.Delay(500);
            rows = await ScrapeEcl();
        }
        Apply(rows, announceChecks: false);
    }

    // Pulse the C/L button (1 -> 0) to toggle the checklist overlay, without the
    // re-scrape/announce that PulseEcp does. Used for the auto show-on-open and
    // hide-on-close.
    private async Task PulseClRaw()
    {
        try { _sim?.ExecuteCalculatorCode("1 (>L:A32NX_BTN_CL)"); await Task.Delay(140); _sim?.ExecuteCalculatorCode("0 (>L:A32NX_BTN_CL)"); } catch { }
    }

    private async Task RefreshNow()
    {
        Apply(await ScrapeEcl(), announceChecks: false);
        _announcer?.Announce("Refreshed");
    }

    // Scrape the ECL through the shared A380X_EWD monitor connection (never null).
    private async Task<List<EclRow>> ScrapeEcl()
    {
        if (_ewd == null) return new List<EclRow>();
        return (await _ewd.ScrapeEclAsync()) ?? new List<EclRow>();
    }

    private void OnRowsUpdated(List<EclRow> rows) => Apply(rows, announceChecks: true);

    private void Apply(List<EclRow> rows, bool announceChecks)
    {
        if (rows == null || rows.Count == 0)
        {
            if (!_haveRows) _status.Text = "Checklist not reachable. Make sure the A380X is loaded and its displays are powered (battery on), then press Refresh.";
            return;
        }
        // The shared E/WD monitor polls ~1 Hz and re-delivers the same rows; a manual
        // ECP pulse also applies them. If nothing actually changed, do NOT rebuild the
        // list (a rebuild moves the selection and makes the screen reader re-read the
        // current line) — only re-apply when the content or cursor genuinely moved.
        string hash = string.Join("", rows.Select(r => (r.Checked ? "1" : "0") + (r.selected ? "S" : "") + r.text));
        if (hash == _lastAppliedHash) return;
        _lastAppliedHash = hash;
        _haveRows = true;
        _cursorActive = rows.Any(r => r.selected);
        _status.Text =
            "Live Electronic Checklist — normal checklists and any active ECAM procedure. "
            + "Arrow Up/Down move the cursor and read each line; Enter opens the highlighted "
            + "checklist or ticks the highlighted item; Backspace steps back to the menu. "
            + "Items also tick themselves as you perform the action. The Abnormal procedures "
            + "button lists active ECAM procedures.";

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

    // The text the screen reader speaks for one ECL line. Screen-reader-first: no
    // visual cursor marker (the list selection is the cursor), no bracket glyphs.
    // A checklist MENU name (plain item, no colour/checkbox → style "") gets no
    // checked/unchecked suffix; an actual checklist ACTION item or abnormal item
    // does, so you hear whether it still needs doing.
    private static string Format(EclRow r)
    {
        if (r.type == "headline") return r.text;
        bool actionable = r.type == "abnormal" || r.style.Length > 0;
        string state = r.Checked ? ", checked" : (actionable ? ", not checked" : "");
        string note = r.style == "manual" ? ", manual" : r.style == "caution" ? ", caution" : "";
        return $"{r.text}{state}{note}";
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RefreshNow(); return true; }
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        // If we turned the checklist overlay on when opening, turn it back off so the
        // E/WD returns to what it was showing before (engine parameters / memos).
        if (_weShowedOverlay && _sim != null)
        {
            var sim = _sim;
            Task.Run(async () =>
            {
                try { sim.ExecuteCalculatorCode("1 (>L:A32NX_BTN_CL)"); await Task.Delay(140); sim.ExecuteCalculatorCode("0 (>L:A32NX_BTN_CL)"); } catch { }
            });
        }
        // The EWD client is SHARED (owned by MainForm) — only detach + stop the ECL
        // polling, never dispose it.
        try { if (_ewd != null) { _ewd.EclRowsUpdated -= OnRowsUpdated; _ewd.EclActive = false; } } catch { }
        base.OnFormClosed(e);
    }
}
