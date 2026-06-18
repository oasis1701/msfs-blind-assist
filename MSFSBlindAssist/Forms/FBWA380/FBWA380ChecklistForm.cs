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
    // True once any Apply has established the checked-items baseline. The old
    // `_lastChecked.Count > 0` proxy conflated "no baseline yet" with "baseline =
    // zero checked", silencing the FIRST item to tick on a fresh checklist (and
    // again after every RESET / navigation into an all-unchecked checklist).
    private bool _baselineApplied;
    private string _lastContentHash = "";   // last row CONTENT rendered (text+checked, NOT selection)
    private int _lastSelIdx = -1;            // last FWS cursor row — a pure cursor move skips the list rebuild
    private bool _haveRows;
    private bool _cursorActive;   // last scrape had a selected (cursor) line
    private bool _busy;           // guard against overlapping ECP pulses
    // True while the ECL overlay is actually showing rows. The FWS AUTO-HIDES the
    // overlay when a checklist is completed (checking "C/L COMPLETE" fires
    // showChecklistRequested.set(false) — on the real A380 finishing a checklist
    // closes the ECL). When that happens the scrape goes empty, the frozen
    // "C/L COMPLETE" stays on screen, AND a Backspace (CLR) becomes a no-op because
    // the FWS only honours CLR while checklistShown is true. We detect the hide
    // (was-shown → empty) and re-show the overlay via C/L so the user lands back on
    // the live checklist MENU instead of being stuck. Bounded: only fires on the
    // was-shown→empty edge, so an unpowered/never-connected display never loops.
    private bool _overlayShown;
    private bool _reshowing;      // guard against overlapping re-show attempts
    private bool _closing;        // set on close so a late empty push doesn't re-show
    // Pending ECP presses that arrived while a pulse+scrape was still running. We
    // QUEUE them (in order, capped) and drain them when the current pulse finishes,
    // instead of silently dropping them — that silent drop was why Backspace (Clear)
    // felt "very unreliable", especially when tapping it to leave a C/L COMPLETE
    // checklist faster than the ~300 ms pulse+scrape cycle.
    private readonly Queue<(string lvar, string say)> _pending = new();
    private const int MaxPending = 8;
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
        // Claim Up/Down/Enter/Backspace so the ListBox delivers them to KeyDown
        // instead of swallowing them (Enter = accept, Backspace = type-ahead) — they
        // drive the real ECL cursor / check / step-back.
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode is Keys.Up or Keys.Down or Keys.Enter or Keys.Back) e.IsInputKey = true; };
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
    // Entry point from the key handler. If a pulse is already running, enqueue this
    // press (capped) so it runs next instead of being dropped; otherwise start the
    // drain loop. This keeps Backspace/Up/Down responsive even when tapped quickly.
    private void PulseEcp(string lvar, string say)
    {
        if (_busy)
        {
            if (_pending.Count < MaxPending) _pending.Enqueue((lvar, say));
            return;
        }
        _ = DrainPulses(lvar, say);
    }

    // Pulse the given ECP button, then keep draining any presses that queued up while we
    // were busy — preserving order and count. KEY ANTI-LAG RULE: a Coherent scrape is a
    // round-trip (~tens to hundreds of ms), so we scrape ONCE per BURST, not once per
    // press. When more presses are already queued we fire them back-to-back with only the
    // short FWS-register gap and skip the scrape; only the LAST press in the burst scrapes
    // + reads. So tapping Down five times quickly lands on the final line and reads it
    // once, instead of crawling through five scrape+announce cycles a quarter-second apart
    // (the "laggy as heck" symptom). Spaced-out single presses still each scrape + read.
    private async Task DrainPulses(string lvar, string say)
    {
        _busy = true;
        try
        {
            while (true)
            {
                bool moreQueued = _pending.Count > 0;

                // Pulse the momentary ECP button (1 -> 0). The FWS latches the press from a
                // high-frequency input buffer, so a short high time is plenty.
                _sim?.ExecuteCalculatorCode($"1 (>L:{lvar})");
                await Task.Delay(45);
                _sim?.ExecuteCalculatorCode($"0 (>L:{lvar})");

                if (moreQueued)
                {
                    // Intermediate press in a burst: just give the FWS time to consume this
                    // press (and reset its input buffer) before the next one, then fire the
                    // next without a scrape. No read here — we only read where we land.
                    await Task.Delay(85);
                    (lvar, say) = _pending.Dequeue();
                    continue;
                }

                // Last press in the burst: let the E/WD re-render, then scrape once and read
                // the now-selected line. Apply moves the selection to the FWS cursor line,
                // which the screen reader reads on its own — so we don't also announce here
                // (that produced a duplicate read); only fall back to a generic confirmation
                // if the scrape came back empty.
                await Task.Delay(85);
                var rows = await ScrapeEcl();
                Apply(rows, announceChecks: true);
                if (_rows.Count == 0) _announcer?.Announce(say);

                // A press may have queued during the render-wait + scrape — keep draining.
                if (_pending.Count == 0) break;
                (lvar, say) = _pending.Dequeue();
            }
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

    // The ECL overlay was auto-hidden by the FWS (a checklist completed). Pulse C/L —
    // which always navigateToChecklist(0) + re-shows — so the user returns to the live
    // checklist menu, then re-scrape so the menu reads. Without this the form is stuck
    // on a frozen "C/L COMPLETE" and Backspace can't escape it (CLR is a no-op once the
    // FWS has hidden the overlay).
    private async Task ReShowChecklistMenu()
    {
        _reshowing = true;
        try
        {
            // Was the last thing on screen a completed checklist? (heuristic for the
            // announce wording — completion vs the user simply backing out of the menu.)
            bool wasComplete = _rows.Any(r => !string.IsNullOrEmpty(r.text) && r.text.IndexOf("COMPLETE", StringComparison.OrdinalIgnoreCase) >= 0);
            _weShowedOverlay = true;
            await PulseClRaw();          // C/L → navigateToChecklist(0) + show → the menu
            await Task.Delay(450);
            var rows = await ScrapeEcl();
            if (IsDisposed || _closing) return;
            if (rows.Count > 0)
            {
                _announcer?.Announce(wasComplete ? "Checklist complete. Back to the checklist menu." : "Checklist menu.");
                Apply(rows, announceChecks: false, force: true);
            }
        }
        catch { }
        finally { _reshowing = false; }
    }

    private async Task RefreshNow()
    {
        // Explicit refresh: force a re-render even if the rows are unchanged, so the
        // current line is re-read (otherwise the hash de-dup would make it a no-op).
        Apply(await ScrapeEcl(), announceChecks: false, force: true);
        _announcer?.Announce("Refreshed");
    }

    // Scrape the ECL through the shared A380X_EWD monitor connection (never null).
    private async Task<List<EclRow>> ScrapeEcl()
    {
        if (_ewd == null) return new List<EclRow>();
        return (await _ewd.ScrapeEclAsync()) ?? new List<EclRow>();
    }

    private void OnRowsUpdated(List<EclRow> rows) => Apply(rows, announceChecks: true);

    private void Apply(List<EclRow> rows, bool announceChecks, bool force = false)
    {
        // The shared EWD client raises EclRowsUpdated via a queued SynchronizationContext
        // post — an in-flight push can land AFTER the aircraft-swap cleanup disposed this
        // form. Touching disposed controls below is then undefined-ish (and any announce
        // would be a ghost callout).
        if (IsDisposed) return;
        if (rows == null || rows.Count == 0)
        {
            if (!_haveRows)
            {
                _status.Text = "Checklist not reachable. Make sure the A380X is loaded and its displays are powered (battery on), then press Refresh.";
            }
            else if (_overlayShown && !_closing && !_busy && !_reshowing)
            {
                // We had checklist rows and now the scrape is empty — the FWS auto-hid
                // the overlay (a checklist was completed). Re-show it so the user goes
                // back to the live menu instead of a frozen "C/L COMPLETE".
                _overlayShown = false;   // edge-trigger: don't re-show again until rows return
                _ = ReShowChecklistMenu();
            }
            return;
        }
        _overlayShown = true;
        // The shared E/WD monitor polls ~1 Hz and re-delivers the same rows; a manual
        // ECP pulse also applies them. If nothing actually changed, do NOT rebuild the
        // list (a rebuild moves the selection and makes the screen reader re-read the
        // current line) — only re-apply when the content or cursor genuinely moved.
        string hash = string.Join("", rows.Select(r => (r.Checked ? "1" : "0") + r.text));
        int selIdx = rows.FindIndex(r => r.selected);
        bool contentChanged = force || hash != _lastContentHash;
        bool cursorChanged = selIdx != _lastSelIdx;
        if (!contentChanged && !cursorChanged) return;
        _lastContentHash = hash;
        _lastSelIdx = selIdx;
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
        var present = new HashSet<string>(rows.Where(r => !string.IsNullOrEmpty(r.text)).Select(r => r.text));
        if (announceChecks && _baselineApplied)
        {
            // Newly ticked (sensed auto-completion as the pilot performs the action,
            // e.g. SEAT BELTS as the signs go on)...
            foreach (var t in nowChecked)
                if (!_lastChecked.Contains(t)) _announcer?.Announce($"{t}, checked");
            // ...and newly un-ticked while STILL on screen (a sensed item that toggles
            // back, e.g. the seatbelt signs going off) — guard on `present` so an item
            // that simply scrolled away / switched checklists is not called "unchecked".
            foreach (var t in _lastChecked)
                if (!nowChecked.Contains(t) && present.Contains(t)) _announcer?.Announce($"{t}, unchecked");
        }
        _lastChecked = nowChecked;
        _baselineApplied = true;

        _rows = rows;
        int keep = _list.SelectedIndex;
        // Rebuild the ListBox items ONLY when the content actually changed. A pure cursor
        // move (Up/Down with identical content) skips the Clear/re-add entirely — just the
        // SelectedIndex moves below — so the screen reader follows the cursor smoothly
        // instead of the whole list being torn down and rebuilt under it on every arrow.
        if (contentChanged)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in rows) _list.Items.Add(Format(r));
            _list.EndUpdate();
        }

        // Mirror the FWS cursor: land the list selection on the selected line so the
        // screen reader follows the real ECL cursor; else preserve the prior row.
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
        _closing = true;   // a late empty push must not trigger a re-show now
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
