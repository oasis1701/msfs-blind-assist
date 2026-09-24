using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A220;

namespace MSFSBlindAssist.Forms.A220;

/// <summary>
/// Accessible Synaptic A220 FMS window (Shift+M). Pro Line Fusion is
/// scratchpad-first: type on the MKP, then "click" a target field — so this form
/// presents the CURRENT FMS window as list rows (fields as "label: value", soft
/// keys/tiles as activatable rows), a scratchpad box whose Commit types the text
/// via H:A220_KBD_* key events and clicks the selected field's value node in the
/// DisplayUnits view, EXEC/CANCEL via the documented L:A22X Flight Plan
/// Execute/Cancel flags, and the four ruling-approved guided flows (SimBrief
/// uplink, wind request, FUEL page, PERF DEP SET VSPEEDS) as scripted sequences of
/// the same primitives with per-step verification.
///
/// Screen-reader rules: list navigation and button presses are never announced;
/// only async results (commit read-back, EXEC state, flow steps) and errors speak.
/// The aircraft's hardware-keyboard Entry Mode is never touched.
/// </summary>
public sealed class A220FmsForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly SynapticA220Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Label _breadcrumb;
    private readonly ListBox _list;
    private readonly TextBox _scratchpad;
    private readonly ComboBox _pageSelector;
    private readonly System.Windows.Forms.Timer _pageNavTimer;
    private IntPtr _previousWindow = IntPtr.Zero;
    private bool _busy;
    private bool _refreshing;

    // Live auto-refresh (the A380 MCDU-form idiom): poll while visible, re-render
    // only when the page actually changed, and announce mode/EXEC transitions so
    // the pilot hears ACT→MOD without refocusing. Announcements are suppressed
    // for a short window after our own actions (those paths speak already).
    private readonly System.Windows.Forms.Timer _pollTimer;
    private string _lastSignature = "";
    private string? _lastModeCore;
    private long _lastActionTicks;

    private sealed class Row
    {
        public enum Kinds { Field, Button, Text }
        public Kinds Kind;
        public string Display = "";
        public string Label = "";        // field label or button text (click target)
        public int Occurrence;           // nth same-text label, for clickFmsField/clickWinText
        /// <summary>≥0 on a flight-plan discontinuity row: which discontinuity it is,
        /// for the agent's deleteDiscontinuityClick. -1 on every other row.</summary>
        public int DiscoIndex = -1;
        /// <summary>≥0 on a waypoint revision-menu row: the item's index for the
        /// agent's taskMenuClick. -1 on every other row.</summary>
        public int MenuIndex = -1;
        /// <summary>≥0 on an open-dropdown option row: the option's index for the
        /// agent's dropdownClick. -1 on every other row.</summary>
        public int OptionIndex = -1;
        /// <summary>Dialog rows only: the dialog's DONE/CNCL button row.</summary>
        public bool IsDialogDone;
        /// <summary>Revision-menu rows only: the aircraft draws this item dimmed
        /// (not available in the current flight phase/context).</summary>
        public bool Disabled;
        /// <summary>Field rows: the value is the face of a CLOSED dropdown, so Enter
        /// OPENS the option list instead of committing the scratchpad into it.</summary>
        public bool IsDropdown;
        /// <summary>Revision-menu rows only: the item holds an INLINE entry field
        /// and no action of its own (PB/D WPT, ALONG TRK WPT), so pressing it just
        /// closes the menu. Refused with the reason rather than dead-pressed.</summary>
        public bool NeedsInlineValue;
        /// <summary>Legs-page waypoint rows: Enter opens the aircraft's revision
        /// (task) menu via the row's fix-symbol icon (agent openLegMenu). The
        /// ident text itself must NEVER be clicked — it is a Fusion scratchpad
        /// Input, and a click submits/copies the scratchpad (proven live: a held
        /// "GENOS" overwrote a real waypoint).</summary>
        public bool LegMenu;
        /// <summary>The AIRCRAFT's own leg index for this row (≥0 on legs-page
        /// rows, −1 elsewhere), read from React's fibers. Every legs-page action
        /// is addressed by this, never by ident text + occurrence + geometry:
        /// duplicate idents and a list that shifts under an edit made the old
        /// addressing open the wrong waypoint's menu (live 2026-07-30).</summary>
        public int LegIdx = -1;
        /// <summary>Dialog button rows whose label ALSO exists on the page beneath
        /// the floating dialog (the Direct-To dialog lists the same idents as the
        /// legs list under it): click via the dialog-scoped clickDialogText, never
        /// the window-wide clickWinText, or the occurrence count can land on the
        /// page copy.</summary>
        public bool DialogText;
        /// <summary>The Direct-To dialog's typed-waypoint entry row: Commit types
        /// the scratchpad and clicks the entry box (agent clickDirectToEntry —
        /// found structurally, the one gray-arrow row).</summary>
        public bool IsDirectToEntry;
        /// <summary>A field with no label of its own (the FUEL page's contingency
        /// percent): committed by its value's position (agent clickFmsAt).</summary>
        public double? ClickX, ClickY;
        /// <summary>The entry range the aircraft's Input declares, spoken when an
        /// entry is refused.</summary>
        public double? Min, Max;
        /// <summary>≥0 on a Direct-To dialog altitude row: the leg's VERT →
        /// occurrence, for agent clickDirectToVertAlt.</summary>
        public int VertAltOcc = -1;
    }

    private List<Row> _rows = new();

    /// <summary>Index of the row currently held still under the cursor by ApplyRows
    /// (its text is deliberately one poll or more out of date), or -1. Flushed the
    /// instant the selection moves off it.</summary>
    private int _frozenIndex = -1;

    /// <summary>Which aircraft overlay currently owns the screen. Menus, dropdowns
    /// and dialogs are MODAL in the real cockpit (a backdrop swallows every click
    /// outside), so the form mirrors that: the list shows only the overlay, and
    /// Escape dismisses the overlay before it ever closes the window.</summary>
    private enum Overlay { None, Menu, Dropdown, Dialog }
    private Overlay _overlay = Overlay.None;
    private bool _menuOpen => _overlay == Overlay.Menu;

    /// <summary>Last FMS rejection message spoken, so the amber box (which the
    /// aircraft clears after 3 s) is announced once per occurrence, not per poll.</summary>
    private string _lastFmsError = "";

    // "Go to page" targets: the five page tiles plus the SEC/ACT mode tabs. The
    // parser deliberately consumes the top mode text as chrome (never a row), so
    // without these entries a pilot has no way to reach the secondary flight plan.
    private static readonly (string Display, string Click)[] NavTargets =
        A220FmsScreenParsing.FmsTiles.Select(t => (Display: t, Click: t))
            .Append((Display: "SEC — secondary flight plan", Click: "SEC"))
            .Append((Display: "ACT — active flight plan", Click: "ACT"))
            .ToArray();

    public A220FmsForm(SynapticA220Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;

        Text = "A220 FMS";
        Size = new Size(780, 700);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _breadcrumb = new Label
        {
            Location = new Point(12, 10),
            Size = new Size(480, 24),
            Text = "Loading…",
            AccessibleName = "FMS window"
        };
        Controls.Add(_breadcrumb);

        // "Go to page" combo (the A380 MCDU-form idiom): a closed DropDownList fires
        // SelectedIndexChanged per arrow keystroke, so navigation is debounced — it
        // only fires once the user STOPS arrowing.
        _pageSelector = new ComboBox
        {
            Location = new Point(500, 8),
            Size = new Size(250, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Go to FMS page",
            AccessibleDescription = "Jump to an FMS page tile. Choose a page to navigate to it."
        };
        foreach (var page in NavTargets) _pageSelector.Items.Add(page.Display);
        _pageNavTimer = new System.Windows.Forms.Timer { Interval = 650 };
        _pageNavTimer.Tick += (_, _) =>
        {
            _pageNavTimer.Stop();
            int i = _pageSelector.SelectedIndex;
            if (i >= 0 && i < NavTargets.Length)
                _ = NavigateAsync(NavTargets[i].Click);
        };
        _pageSelector.SelectedIndexChanged += (_, _) => { _pageNavTimer.Stop(); _pageNavTimer.Start(); };
        Controls.Add(_pageSelector);

        _list = new ListBox
        {
            Location = new Point(12, 40),
            Size = new Size(740, 420),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            IntegralHeight = false,
            AccessibleName = "FMS page rows"
        };
        _list.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) e.IsInputKey = true; };
        // The row we held still under the cursor gets its real text back as soon as
        // the cursor leaves it (see ApplyRows/FlushFrozenRow).
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_frozenIndex >= 0 && _frozenIndex != _list.SelectedIndex) FlushFrozenRow();
        };
        // Focus leaving the list ends the freeze too — nothing is being read out of
        // it any more, so there is no reason to keep a row stale.
        _list.LostFocus += (_, _) => { if (_frozenIndex >= 0) FlushFrozenRow(); };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { _ = ActivateSelectedAsync(); e.Handled = true; e.SuppressKeyPress = true; }
            // Delete on a flight-plan discontinuity removes it (the A380 form's
            // Delete-clears-this-row idiom). Delete is deliberately NOT bound to
            // anything else here — clearing an arbitrary FMS field by keystroke is
            // a bigger, separate change.
            else if (e.KeyCode == Keys.Delete)
            {
                var sel = SelectedRow();
                if (sel is { DiscoIndex: >= 0 }) _ = DeleteLegRowAsync(sel);
                else _announcer.AnnounceImmediate("Delete works on a flight plan discontinuity row.");
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            // Airbus-form CLR idiom (A380 precedent): Backspace clears the staging
            // scratchpad (box + the aircraft's shared scratchpad) without leaving
            // the list. The aircraft side goes through the store reset, NOT the
            // MKP CLEAR key — CLEAR is a one-char backspace that arms --DELETE--
            // on an empty scratchpad.
            else if (e.KeyCode == Keys.Back)
            {
                MarkAction();
                _scratchpad.Clear();
                _ = _def.ClearScratchpadAsync();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        _list.DoubleClick += (_, _) => _ = ActivateSelectedAsync();
        Controls.Add(_list);

        var spLabel = new Label { Location = new Point(12, 470), Size = new Size(110, 23), Text = "&Scratchpad:" };
        Controls.Add(spLabel);
        _scratchpad = new TextBox
        {
            Location = new Point(124, 468),
            Size = new Size(220, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            AccessibleName = "Scratchpad text"
        };
        _scratchpad.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { _ = CommitToSelectedFieldAsync(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        Controls.Add(_scratchpad);

        int bx = 12, by = 502;
        Button Add(string text, EventHandler onClick, int width = 130)
        {
            var b = new Button
            {
                Location = new Point(bx, by),
                Size = new Size(width, 30),
                Text = text,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            b.Click += onClick;
            Controls.Add(b);
            bx += width + 8;
            if (bx > 640) { bx = 12; by += 36; }
            return b;
        }

        // &C belongs to CLEAR (user ask 2026-07-30: an invalid-entry message /
        // stale text blocks every later entry, so clearing must be one keystroke,
        // Alt+C). Commit keeps Alt+M; Enter in the scratchpad box also commits.
        Add("Co&mmit to field", (_, _) => _ = CommitToSelectedFieldAsync(), 140);
        Add("&Clear scratchpad", (_, _) =>
        {
            _scratchpad.Clear();
            _ = ClearAircraftScratchpadAsync();
        }, 140);
        Add("E&XEC", (_, _) => _ = ExecAsync(), 90);
        Add("Ca&ncel mod", (_, _) => _ = CancelModAsync(), 110);
        Add("&Refresh", (_, _) => _ = RefreshAsync(), 100);
        bx = 12; by += 36;
        // Page navigation lives in the "Go to page" combo (top right) + Ctrl+1..5;
        // DIR is the MKP quick-access key (Ctrl+D).
        Add("&Open FMS window", (_, _) => _ = OpenFmsWindowAsync(), 150);
        Add("&DIR (Direct-to)", (_, _) => { _def.SendMkpKey("DIR"); _ = DelayedRefreshAsync(); }, 130);
        bx = 12; by += 36;
        Add("SimBrief &uplink", (_, _) => _ = RunUplinkFlowAsync(), 140);
        Add("&Wind request", (_, _) => _ = RunFlowAsync("Wind request",
            new[] { "FPLN", "WIND/TEMP", "FPLN WIND REQ" }), 130);
        Add("&Fuel page", (_, _) => _ = RunFlowAsync("FUEL page",
            new[] { "FPLN", "FUEL" }), 110);
        Add("Set &V-speeds", (_, _) => _ = RunFlowAsync("PERF DEP SET VSPEEDS",
            new[] { "PERF", "DEP", "SET VSPEEDS" }), 130);

        var hint = new Label
        {
            Location = new Point(12, by + 40),
            Size = new Size(740, 40),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Enter on a row: click it (edit boxes commit the scratchpad).  Enter on a waypoint: its revision "
                + "menu (direct to, hold, delete…).  Scratchpad + Commit on a waypoint row: enter that waypoint there — or, typed like /9000A, "
                + "250/ or 250/FL120, set that waypoint's speed/altitude constraint (A above, B below).  "
                + "Alt+C or Backspace: clear the scratchpad (also clears a stuck invalid entry).  "
                + "Delete on a discontinuity row: remove it, then EXEC.  "
                + "Go to page combo or Ctrl+1-7: DBASE/POS/FPLN/PERF/ROUTE/SEC/ACT.  "
                + "PageUp/PageDown or Alt+Up/Alt+Down: previous/next page of a long list (inside a dialog: the dialog's own list).  "
                + "Ctrl+D: Direct-to.  F5: refresh.  Escape: close menu, then window.",
            AccessibleName = "Keyboard help"
        };
        Controls.Add(hint);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F5) { _ = RefreshAsync(); e.Handled = true; }
            // Page the FMS window, the same chord every other CDU form in this app
            // uses (PageUp/PageDown or Alt+Up/Alt+Down). These fire the MKP's own
            // PREV/NEXT page keys — NOT its UP/DOWN cursor keys, which move the
            // aircraft's line cursor and are a different control entirely.
            else if (e.KeyCode == Keys.PageUp || (e.Alt && e.KeyCode == Keys.Up))
            {
                if (_overlay == Overlay.Dialog) _ = PageDialogAsync(-1); else PageFms("PREV");
                e.Handled = true; e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.PageDown || (e.Alt && e.KeyCode == Keys.Down))
            {
                if (_overlay == Overlay.Dialog) _ = PageDialogAsync(1); else PageFms("NEXT");
                e.Handled = true; e.SuppressKeyPress = true;
            }
            // While any overlay is open, Escape dismisses the OVERLAY (matching the
            // aircraft's own cancel), not the window. A second Escape closes it.
            else if (e.KeyCode == Keys.Escape && _overlay != Overlay.None)
            { _ = DismissOverlayAsync(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.Control && e.KeyCode is >= Keys.D1 and <= Keys.D7)
            {
                int i = e.KeyCode - Keys.D1;
                if (i < NavTargets.Length) _ = NavigateAsync(NavTargets[i].Click);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.D)
            {
                _def.SendMkpKey("DIR");
                _ = DelayedRefreshAsync();
                e.Handled = true;
            }
        };

        _pollTimer = new System.Windows.Forms.Timer { Interval = 1300 };
        _pollTimer.Tick += (_, _) => { if (Visible && !_busy && !_refreshing) _ = RefreshAsync(announceChanges: true); };

        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            _pollTimer.Stop();
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        Activate();
        _list.Focus();
        _pollTimer.Start();
        _ = RefreshAsync();
    }

    private async Task DelayedRefreshAsync()
    {
        MarkAction();
        await Task.Delay(900);
        await RefreshAsync();
    }

    private void MarkAction() => _lastActionTicks = Environment.TickCount64;

    /// <summary>The Alt+C clear: empty the aircraft's scratchpad through the store
    /// reset and confirm out loud — a stale invalid entry silently blocking every
    /// later commit is exactly what this exists to dig the pilot out of.</summary>
    private async Task ClearAircraftScratchpadAsync()
    {
        MarkAction();
        bool ok = await _def.ClearScratchpadAsync();
        _announcer.AnnounceImmediate(ok ? "Scratchpad cleared." : "Scratchpad clear not confirmed — try again.");
    }

    // ---- scrape → rows ------------------------------------------------------

    private async Task<A220FmsScreenParsing.FmsModel?> ScrapeAsync()
    {
        string? raw = await _def.DisplaysAgentCallAsync("fms()");
        if (string.IsNullOrEmpty(raw)) return null;
        var win = A220FmsScreenParsing.ParseAgentTokens(raw);
        if (win == null) return null;
        if (!win.Ok)
            return new A220FmsScreenParsing.FmsModel { Side = "", Mode = win.Reason ?? "unavailable" };
        return A220FmsScreenParsing.ParseFms(win.Tokens, win.Boxes);
    }

    private async Task RefreshAsync(bool announceChanges = false)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            // Aircraft overlays (revision menu, open dropdown, dialog) are MODAL in
            // the real cockpit — a backdrop swallows every click outside them — so
            // they are modal here too: while one is open the list shows only it.
            if (await TryRenderOverlayAsync()) return;

            var model = await ScrapeAsync();
            if (IsDisposed) return;
            if (model == null)
            {
                _breadcrumb.Text = "FMS not available (simulator or DisplayUnits view not reachable)";
                _list.Items.Clear();
                _rows.Clear();
                _lastSignature = "";
                return;
            }
            if (model.Side.Length == 0)
            {
                _breadcrumb.Text = "No FMS window is open on the displays — use Open FMS window";
                _list.Items.Clear();
                _rows.Clear();
                _lastSignature = "";
                return;
            }

            // Interactive rows speak their KIND, but ONLY where it's true: "edit box"
            // is reserved for fields whose value sits in a drawn value-box frame
            // (typeable); other labelled values read as plain "label: value" rows
            // (still Enter-clickable — some open pages). Page tiles live in the
            // "Go to page" combo, not the list. No row numbering (user ruling).
            var rows = new List<Row>();
            // Aircraft leg indices for this page (see Row.LegIdx). Paired by
            // CONTENT via AlignLegIndices — a positional zip shifted every index
            // by two on a live plan (2026-07-30) because the fiber walk and the
            // grouper legitimately disagree about which rows exist. A miss here
            // degrades to text addressing rather than blocking the page.
            var fiberLegs = await ReadFiberLegsAsync();
            var aligned = A220FmsLegParsing.AlignLegIndices(model.Legs, fiberLegs);
            // ROUTE ▸ LEGS: the route reads FIRST and as ONE row per leg — the
            // page's whole point is the sequence of waypoints, so it must not sit
            // below the soft keys. Section headers and discontinuities are plain
            // text rows (nothing to activate); every waypoint is clickable by name
            // + occurrence (a waypoint repeats across STAR / transition / missed
            // approach / hold, so the occurrence is what disambiguates).
            for (int li = 0; li < model.Legs.Count; li++)
            {
                var leg = model.Legs[li];
                string text = A220FmsLegParsing.Describe(leg);
                bool actionable = leg.Kind is A220FmsLegParsing.LegKind.Leg
                    or A220FmsLegParsing.LegKind.Hold or A220FmsLegParsing.LegKind.Origin;
                int idx = aligned[li];
                rows.Add(new Row
                {
                    Kind = actionable ? Row.Kinds.Button : Row.Kinds.Text,
                    Display = actionable ? $"{text}, button" : text,
                    Label = leg.Waypoint,
                    Occurrence = leg.Occurrence,
                    DiscoIndex = leg.DiscoIndex,
                    LegMenu = actionable,
                    LegIdx = idx
                });
            }
            // Fields, buttons and unclaimed lines interleave back into READING
            // ORDER (each carries its screen y): the page then reads top-to-bottom
            // like the real screen — sub-tabs first, a heading directly above its
            // table, the THRUST…/MSG… band last. The old fields→buttons→orphans
            // grouping scattered related rows ("DATA BASES" ended up far from its
            // table), which was much of the reported "still a bit of a mess".
            var pageRows = new List<(double Y, double X, Row Row)>();
            foreach (var f in model.Fields)
                pageRows.Add((f.Y, f.X, FieldRow(f)));
            var buttonSeen = new Dictionary<string, int>();
            foreach (var b in model.ButtonRows)
            {
                int occ = buttonSeen.TryGetValue(b.Text, out int o) ? o : 0;
                buttonSeen[b.Text] = occ + 1;
                pageRows.Add((b.Y, b.X, new Row { Kind = Row.Kinds.Button, Display = $"{SpokenButton(b.Text)}, button", Label = b.Text.TrimEnd('…'), Occurrence = occ }));
            }
            // Text no row above claimed (bare numbers, unpaired headings) — plain
            // rows at their own screen position, NOT a duplicated full-page dump.
            foreach (var line in model.OrphanRows)
                pageRows.Add((line.Y, 0, new Row { Kind = Row.Kinds.Text, Display = line.Text }));
            rows.AddRange(pageRows.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => p.Row));

            string modeCore = $"{model.Side} {model.Mode}";
            string breadcrumb = modeCore
                + (model.ExecAvailable ? " — modification pending (EXEC available)" : "");
            // Selection is restored by CONTENT, not index (the A380 MCDU form's
            // rule) - see ApplyRows. A bare index restore is right only while the
            // page is unchanged; after navigating, index N is an unrelated row, so
            // the reader would silently land on something the user never chose.
            ApplyRows(rows, breadcrumb);

            // Transition announcements from the background poll only, and never
            // inside the post-action window (those paths already speak).
            //
            // The EXEC prompt is announced ONCE, by the aircraft definition's
            // A22X_FPLN_MODIFIED monitor ("Flight plan modified. Press EXEC to
            // activate.") — that var is the physical EXEC key's own lamp, so it
            // fires whether or not this window is open. This form must NOT also
            // speak it: entering MOD would otherwise say the same thing three ways
            // ("FMS1 MOD", "Modification pending — EXEC available", and the
            // monitor). So the mode callout here skips the MOD transition and
            // covers only the ACT↔SEC switch, which nothing else reports.
            // ExecAvailable still drives the breadcrumb text above.
            bool quiet = Environment.TickCount64 - _lastActionTicks < 2500;
            if (announceChanges && !quiet
                && _lastModeCore != null && modeCore != _lastModeCore && model.Mode != "MOD")
                _announcer.Announce($"FMS {modeCore}");
            _lastModeCore = modeCore;
        }
        finally { _refreshing = false; }
    }

    /// <summary>
    /// The spoken row for a field. A CLOSED DROPDOWN must say it is a chooser and
    /// how many options it holds: the aircraft renders only the current value while
    /// closed, so it is visually indistinguishable from a data row, and a pilot who
    /// cannot see the chevron would never know the choice exists. "edit box" stays
    /// reserved for genuinely typeable fields — a chooser is not one, whatever frame
    /// the renderer draws around it.
    /// </summary>
    private static Row FieldRow(A220FmsScreenParsing.FmsField f) => new()
    {
        Kind = Row.Kinds.Field,
        Display = DescribeField(f),
        Label = f.Label,
        Occurrence = f.Occurrence,
        IsDropdown = f.IsDropdown,
        ClickX = f.ClickX, ClickY = f.ClickY,
        Min = f.Min, Max = f.Max
    };

    private static string DescribeField(A220FmsScreenParsing.FmsField f)
    {
        string value = A220FmsScreenParsing.SpokenFieldValue(f.Value);
        if (f.IsDropdown)
        {
            // NO option count here on purpose: the props array can include options
            // the aircraft HIDES (the plan selector reports 3 — ACT/SEC/MOD — but
            // opens with 2), and a count that contradicts what then opens is worse
            // than none. The open list announces the true count itself.
            return $"{f.Name}, dropdown: {value}, Enter opens the list";
        }
        return f.Editable
            ? $"{f.Name}, edit box: {value}"
            : $"{f.Name}: {value}";
    }

    /// <summary>
    /// The aircraft's own leg indices for the legs-page rows, in screen order
    /// (agent `legs()`, read from React fibers). Empty when the page isn't a legs
    /// table or the read fails — callers then fall back to text addressing, so a
    /// DOM change degrades the actions rather than blanking the page.
    /// </summary>
    /// <summary>The agent's fiber walk of the legs list, screen order: the
    /// aircraft's own leg index + the row's ident text (empty for a
    /// ▯-placeholder / discontinuity slot). Consumed by
    /// <see cref="A220FmsLegParsing.AlignLegIndices"/> — never zipped
    /// positionally against the grouper's rows.</summary>
    private async Task<List<A220FmsLegParsing.FiberLegRow>> ReadFiberLegsAsync()
    {
        var list = new List<A220FmsLegParsing.FiberLegRow>();
        string? raw = await _def.DisplaysAgentCallAsync("legs()");
        if (string.IsNullOrEmpty(raw)) return list;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return list;
            if (!root.TryGetProperty("rows", out var rows)) return list;
            foreach (var r in rows.EnumerateArray())
                if (r.TryGetProperty("idx", out var i) && i.TryGetInt32(out int v))
                    list.Add(new A220FmsLegParsing.FiberLegRow(
                        v, r.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""));
        }
        catch (System.Text.Json.JsonException) { list.Clear(); }
        return list;
    }

    // ---- waypoint revision (task) menu --------------------------------------

    /// <summary>Cockpit glyph labels → spoken labels. The direct-to item is a bare
    /// arrow glyph on the real menu ("→…"), which a screen reader renders as
    /// nothing useful; everything else is kept verbatim (faithful to the cockpit).</summary>
    private static string SpokenMenuLabel(string t) => t switch
    {
        "→…" or "→" => "Direct to",
        _ => t.TrimEnd('…'),
    };

    /// <summary>Same glyph mapping for PAGE/dialog soft keys: the route page pins a
    /// "→…" key that opens the aircraft's Direct-To dialog — the ONE place a pilot
    /// can go direct (or vertical-direct) to ANY leg, including the procedural
    /// approach fixes (CI/FI/RW…) whose per-leg revision menu the FMS doesn't offer
    /// (fix-type gated, proven live 2026-07-30). A bare "→…, button" row reads as
    /// nothing useful, hiding that whole capability.</summary>
    private static string SpokenButton(string t) => t is "→…" or "→" ? "Direct to…" : t;

    /// <summary>Overlay state read from the agent in ONE call per poll.</summary>
    private sealed class OverlayState
    {
        public string Kind = "none";
        public string Header = "";
        public string? Done;
        public string Error = "";
        public double X, Y, W, H;
        public List<(string Label, bool Disabled, bool Checkbox, bool Checked, bool Selected,
            bool InlineInput)> Items = new();
    }

    private async Task<OverlayState> ReadOverlayAsync()
    {
        var st = new OverlayState();
        string? raw = await _def.DisplaysAgentCallAsync("overlay()");
        if (string.IsNullOrEmpty(raw)) return st;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            st.Kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "none" : "none";
            st.Header = root.TryGetProperty("header", out var h) ? h.GetString() ?? "" : "";
            st.Done = root.TryGetProperty("done", out var dn) ? dn.GetString() : null;
            st.Error = root.TryGetProperty("err", out var er) ? er.GetString() ?? "" : "";
            if (root.TryGetProperty("x", out var x)) st.X = x.GetDouble();
            if (root.TryGetProperty("y", out var y)) st.Y = y.GetDouble();
            if (root.TryGetProperty("w", out var w)) st.W = w.GetDouble();
            if (root.TryGetProperty("h", out var hh)) st.H = hh.GetDouble();
            if (root.TryGetProperty("items", out var arr))
                foreach (var it in arr.EnumerateArray())
                    st.Items.Add((
                        it.GetProperty("t").GetString() ?? "",
                        it.TryGetProperty("d", out var d) && d.GetBoolean(),
                        it.TryGetProperty("cb", out var cb) && cb.GetBoolean(),
                        it.TryGetProperty("ck", out var ck) && ck.GetBoolean(),
                        it.TryGetProperty("sel", out var sl) && sl.GetBoolean(),
                        it.TryGetProperty("inp", out var ip) && ip.GetBoolean()));
        }
        catch (System.Text.Json.JsonException) { st.Kind = "none"; }
        return st;
    }

    /// <summary>
    /// Speak the FMS's OWN rejection message when it appears. The aircraft renders
    /// it in an amber box under the field for exactly 3 seconds and nowhere else —
    /// it is the only place the FMS says WHY it refused ("NOT IN DATA BASE",
    /// "INVALID ENTRY", "INVALID DELETE", "SELECT CONSTRAINT"…). Without this, a
    /// rejected entry read as a bare "field unchanged" and the pilot had no way to
    /// learn the reason a sighted pilot can simply see.
    /// </summary>
    private void AnnounceFmsError(string error)
    {
        string e = error.Trim();
        if (e.Length == 0) { _lastFmsError = ""; return; }
        if (string.Equals(e, _lastFmsError, StringComparison.OrdinalIgnoreCase)) return;
        _lastFmsError = e;
        _announcer.AnnounceImmediate($"FMS message: {e}.");
    }

    /// <summary>
    /// If a modal aircraft overlay owns the screen (waypoint revision menu, open
    /// dropdown, or dialog), render it as the ONLY list content and return true.
    /// Each is modal in the real cockpit, so each is modal here; Escape dismisses
    /// it. Dialogs keep the normal field/button parse but CLIPPED to the dialog's
    /// own rectangle, so a constraint editor never interleaves with the page
    /// underneath it, and its DONE/CNCL is a stated row rather than a stray button.
    /// </summary>
    private async Task<bool> TryRenderOverlayAsync()
    {
        var st = await ReadOverlayAsync();
        if (IsDisposed) return false;
        AnnounceFmsError(st.Error);

        switch (st.Kind)
        {
            case "menu" when st.Items.Count > 0:
                return RenderMenu(st);
            case "dropdown" when st.Items.Count > 1:
                return RenderDropdown(st);
            case "dialog" when st.W > 0 && st.H > 0:
                return await RenderDialogAsync(st);
            default:
                _overlay = Overlay.None;
                _menuHeader = "";
                return false;
        }
    }

    /// <summary>Position/page count of the open dialog's PAGED list (agent
    /// dialogPages — the Direct-To and FIX dialogs render a fixed number of slots
    /// and swap their content, so later entries are not on screen at all until
    /// the list is paged). 1 page when the dialog has none.</summary>
    private int _dialogPage, _dialogPages = 1;

    private async Task ReadDialogPagesAsync()
    {
        _dialogPage = 0; _dialogPages = 1;
        string? raw = await _def.DisplaysAgentCallAsync("dialogPages()");
        if (string.IsNullOrEmpty(raw)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                _dialogPage = p.GetProperty("pos").GetInt32();
                _dialogPages = Math.Max(1, p.GetProperty("pages").GetInt32());
                break;
            }
        }
        catch (System.Text.Json.JsonException) { }
    }

    /// <summary>PageUp/PageDown inside a dialog: page ITS list (the aircraft's own
    /// scroll-bar arrows), not the MKP's PREV/NEXT, which page the window behind it.</summary>
    private async Task PageDialogAsync(int dir)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            string? r = await _def.DisplaysAgentCallAsync($"dialogPage({dir})");
            if (r == null || !r.StartsWith("PAGED", StringComparison.Ordinal))
            {
                _announcer.AnnounceImmediate(r switch
                {
                    "EDGE" => dir > 0 ? "Last page." : "First page.",
                    "NONE" => "This dialog has only one page.",
                    _ => $"Could not page the dialog — {DescribeClickFailure(r)}"
                });
                return;
            }
            _frozenIndex = -1;
            _lastSignature = "";
            await Task.Delay(300);
            await RefreshAsync();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _announcer.AnnounceImmediate($"Page {_dialogPage + 1} of {_dialogPages}.");
        }
        finally { _busy = false; }
    }

    /// <summary>Header of the currently rendered revision menu (the fix ident),
    /// "" when no menu is up — OpenLegMenuAsync verifies the menu that appeared
    /// belongs to the waypoint the user pressed Enter on.</summary>
    private string _menuHeader = "";

    private bool RenderMenu(OverlayState st)
    {
        bool justOpened = _overlay != Overlay.Menu;
        _overlay = Overlay.Menu;
        _menuHeader = st.Header;
        var rows = new List<Row>();
        for (int i = 0; i < st.Items.Count; i++)
        {
            var (label, disabled, checkbox, isChecked, _, inlineInput) = st.Items[i];
            string spoken = SpokenMenuLabel(label);
            // An item with an INLINE ENTRY FIELD (PB/D WPT wants a bearing and
            // distance, ALONG TRK WPT a distance) carries no action of its own —
            // pressing it just shuts the menu. Saying so is the honest render; a
            // "button" that silently does nothing is the worse failure.
            bool deadPress = inlineInput && !disabled && !checkbox;
            string display = disabled
                ? $"{spoken}, not available"
                : checkbox
                    ? $"{spoken}, checkbox, {(isChecked ? "checked" : "not checked")}"
                    : deadPress
                        ? $"{spoken}, needs a value typed on the aircraft display — cannot be set from here yet"
                        : spoken == "DONE" ? "DONE — close menu" : $"{spoken}, button";
            rows.Add(new Row
            {
                Kind = disabled || deadPress ? Row.Kinds.Text : Row.Kinds.Button,
                Display = display,
                Label = spoken,
                NeedsInlineValue = deadPress,
                MenuIndex = i,
                Disabled = disabled
            });
        }
        string breadcrumb = st.Header.Length > 0 ? $"Revision menu — {st.Header}" : "Revision menu";
        ApplyRows(rows, breadcrumb);
        if (justOpened)
        {
            int usable = rows.Count(r => !r.Disabled);
            _announcer.AnnounceImmediate(st.Header.Length > 0
                ? $"Revision menu for {st.Header}. {usable} options. Escape closes the menu."
                : $"Revision menu. {usable} options. Escape closes the menu.");
        }
        return true;
    }

    /// <summary>An OPEN Fusion dropdown (altitude constraint type, flight phase,
    /// the ACT/SEC plan selector…). Only the current value is on screen while it is
    /// closed, so the open list is the only chance to hear the choices — each reads
    /// with its position and which one is current.</summary>
    private bool RenderDropdown(OverlayState st)
    {
        bool justOpened = _overlay != Overlay.Dropdown;
        _overlay = Overlay.Dropdown;
        var rows = new List<Row>();
        int total = st.Items.Count;
        for (int i = 0; i < total; i++)
        {
            var (label, _, _, _, selected, _) = st.Items[i];
            bool pager = label is "PREV" or "NEXT";
            string display = pager
                ? $"{label} page, button"
                : $"{label}, {i + 1} of {total}{(selected ? ", current" : "")}";
            rows.Add(new Row { Kind = Row.Kinds.Button, Display = display, Label = label, OptionIndex = i });
        }
        string? current = st.Items.FirstOrDefault(it => it.Selected).Label;
        ApplyRows(rows, current is { Length: > 0 } ? $"Dropdown — current: {current}" : "Dropdown");
        if (justOpened)
            _announcer.AnnounceImmediate(
                $"Dropdown open. {total} options{(current is { Length: > 0 } ? $", currently {current}" : "")}. Escape cancels.");
        return true;
    }

    /// <summary>A Fusion dialog (CROSSING…, HOLD…, FIX…, DEP/ARR, the duplicate-fix
    /// picker). Parsed exactly like a page but CLIPPED to the dialog's rect, then
    /// given its own breadcrumb and an explicit DONE/CNCL row.</summary>
    private async Task<bool> RenderDialogAsync(OverlayState st)
    {
        // The dialog's own subtree, NOT a rectangle clip of the page: a dialog
        // floats over the list, so a geometric clip pulls in whatever page text
        // happens to lie beneath it (live: an INTC CRS dialog picked up RNP,
        // DISCONTINUITY, FIX…, DTG from the legs list behind it — the "messed up
        // format" report). Falls back to the clip if the scoped read fails, so a
        // renderer change degrades the rows rather than blanking the dialog.
        string? raw = await _def.DisplaysAgentCallAsync("fmsDialogTokens()");
        if (IsDisposed) return false;
        var win = string.IsNullOrEmpty(raw) ? null : A220FmsScreenParsing.ParseAgentTokens(raw);
        List<A220FmsScreenParsing.WinToken> tokens;
        List<A220FmsScreenParsing.WinBox> boxes;
        if (win is { Ok: true } && win.Tokens.Count > 0)
        {
            tokens = win.Tokens;
            boxes = win.Boxes;
        }
        else
        {
            string? pageRaw = await _def.DisplaysAgentCallAsync("fms()");
            if (IsDisposed || string.IsNullOrEmpty(pageRaw)) return false;
            var page = A220FmsScreenParsing.ParseAgentTokens(pageRaw);
            if (page is not { Ok: true }) return false;
            (tokens, boxes) = A220FmsScreenParsing.ClipTo(page.Tokens, page.Boxes, st.X, st.Y, st.W, st.H);
        }
        if (tokens.Count == 0) return false;
        await ReadDialogPagesAsync();

        // The Direct-To dialog gets its OWN renderer: the generic field/button
        // pass turns its [→][ident] [VERT →][alt] table into soup (glyph rows,
        // orphan altitudes, pseudo-fields — the "confusing dialog" user report,
        // 2026-07-30). One meaningful row per action instead, clicked
        // dialog-scoped so a duplicate ident can't hit the legs list beneath.
        if (st.Header.StartsWith("→", StringComparison.Ordinal)
            && RenderDirectToDialog(tokens, boxes))
            return true;

        var model = A220FmsScreenParsing.ParseFms(tokens, boxes);

        bool justOpened = _overlay != Overlay.Dialog;
        _overlay = Overlay.Dialog;

        var rows = new List<Row>();
        foreach (var f in model.Fields)
            rows.Add(FieldRow(f));
        var seen = new Dictionary<string, int>();
        foreach (var btn in model.ButtonRows)
        {
            string b = btn.Text;
            // The flight-plan tag in the dialog's HEADER band ("… MOD FPLN") is the
            // plan the dialog edits, not a control of the dialog.
            if (b is "ACT" or "MOD" or "SEC" && btn.Y - st.Y < 40) continue;
            // DONE/CNCL is the dialog's own dismiss control — a stated row, and the
            // one Escape triggers, never just another anonymous button.
            if (b is "DONE" or "CNCL")
            {
                rows.Add(new Row
                {
                    Kind = Row.Kinds.Button,
                    Display = b == "CNCL" ? "CNCL — cancel and close" : "DONE — apply and close",
                    Label = b,
                    IsDialogDone = true
                });
                continue;
            }
            int occ = seen.TryGetValue(b, out int o) ? o : 0;
            seen[b] = occ + 1;
            // A repeated bare choice button ("SELECT" twice in the SELECT
            // CONSTRAINT dialog) is meaningless spoken alone — the pilot cannot
            // tell the options apart. Name it by the value drawn on its own row
            // (the constraint the button would pick), falling back to a position.
            string label = b.TrimEnd('…');
            // A LIST column (the DEPARTURES dialog's runways / SIDs / transitions):
            // name each entry by its column heading and say which one is selected
            // (the aircraft draws it cyan) — otherwise "RW25, GIRL1Y, GIRL3X" is a
            // run of bare names with nothing saying which is a runway.
            string column = A220FmsScreenParsing.DialogColumnOf(tokens, model.ButtonRows, btn, st.Y);
            string display = $"{(column.Length > 0 ? column + " " : "")}{SpokenButton(b)}"
                             + $"{(btn.Color == "cyan" ? ", selected" : "")}, button";
            int sameCount = model.Buttons.Count(x => x == b);
            if (sameCount > 1)
            {
                string? what = A220FmsScreenParsing.DialogChoiceValueFor(tokens, b, occ);
                string which = $"option {occ + 1} of {sameCount}";
                display = what is { Length: > 0 }
                    ? $"{b} {what}, {which}, button"
                    : $"{b}, {which}, button, no value shown on this line";
            }
            rows.Add(new Row { Kind = Row.Kinds.Button, Display = display, Label = label, Occurrence = occ });
        }
        // Some dialogs are built around a GRAPHIC. The INTC CRS course rose draws
        // compass ticks and every crossing airway as loose labels (live: ~30 of
        // them — 0/9/18/27, N129, G80, UL609, "+"…), which as one row each buries
        // the two things the pilot came for (the CRS box and DONE). They are not
        // hidden — they are collapsed into ONE row, so the dialog stays honest and
        // still navigable. Few orphans stay as individual rows (a status line like
        // "SELECTION REQUIRED" must read on its own).
        var orphans = model.OrphanLines.Where(l => l.Trim().Length > 0).ToList();
        if (orphans.Count > 4)
        {
            rows.Add(new Row
            {
                Kind = Row.Kinds.Text,
                Display = $"Other labels ({orphans.Count}): {string.Join(", ", orphans)}"
            });
        }
        else
        {
            foreach (string line in orphans)
                rows.Add(new Row { Kind = Row.Kinds.Text, Display = line });
        }

        if (_dialogPages > 1)
            rows.Add(new Row
            {
                Kind = Row.Kinds.Text,
                Display = $"Page {_dialogPage + 1} of {_dialogPages} — Page Down for more, Page Up to go back"
            });

        string title = st.Header.Length > 0 ? st.Header : "Dialog";
        // The Direct-To dialog's on-screen header is the bare arrow glyph
        // ("→ FPLN") — name it for the screen reader.
        if (title.StartsWith("→", StringComparison.Ordinal))
            title = "Direct to" + title["→".Length..];
        ApplyRows(rows, $"{title} — dialog");
        if (justOpened)
        {
            // NAME the editable fields rather than counting them. A follow-up
            // dialog is the pilot's only chance to set what it asks for (the
            // direct-to course, a hold's inbound track and turn direction), and
            // "3 fields" does not tell them a course can be typed here.
            var editable = model.Fields.Where(f => f.Editable && !f.IsDropdown)
                .Select(f => f.Label).ToList();
            var choosers = model.Fields.Where(f => f.IsDropdown).Select(f => f.Label).ToList();
            string what = editable.Count > 0
                ? $"Type in: {string.Join(", ", editable)}."
                : $"{model.Fields.Count} field{(model.Fields.Count == 1 ? "" : "s")}, none typeable.";
            if (choosers.Count > 0) what += $" Choose from: {string.Join(", ", choosers)}.";
            _announcer.AnnounceImmediate(
                $"{title} dialog. {what} Scratchpad then Commit sets a field. " +
                $"Escape {(st.Done == "CNCL" ? "cancels" : "closes")}.");
        }
        return true;
    }

    /// <summary>
    /// The Direct-To dialog as a blind pilot needs it: one row per plan leg
    /// ("Direct to KEA, 2 of 4, button"), a vertical-direct row only where the
    /// aircraft enables one (with its crossing altitude), the typed-ident entry,
    /// and the OFFSET/CRS fields. Returns false when the token structure isn't
    /// recognized so the generic renderer still shows SOMETHING.
    /// </summary>
    private bool RenderDirectToDialog(
        List<A220FmsScreenParsing.WinToken> tokens, List<A220FmsScreenParsing.WinBox> boxes)
    {
        var dt = A220FmsScreenParsing.ParseDirectTo(tokens, boxes);
        if (dt.Legs.Count == 0) return false;

        bool justOpened = _overlay != Overlay.Dialog;
        _overlay = Overlay.Dialog;
        var rows = new List<Row>();
        if (dt.HasEntry)
            rows.Add(new Row
            {
                Kind = Row.Kinds.Field,
                Display = "Direct to a typed waypoint, edit box — type the ident in the scratchpad box, then Commit",
                Label = "typed waypoint",
                IsDirectToEntry = true
            });
        foreach (var f in dt.Fields)
            rows.Add(FieldRow(f));
        var counts = dt.Legs.GroupBy(l => l.Ident)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var leg in dt.Legs)
        {
            string which = counts[leg.Ident] > 1 ? $", {leg.IdentOcc + 1} of {counts[leg.Ident]}" : "";
            string active = leg.Active ? ", active leg" : "";
            rows.Add(new Row
            {
                Kind = Row.Kinds.Button,
                Display = $"Direct to {leg.Ident}{which}{active}, button",
                Label = leg.Ident,
                Occurrence = leg.IdentOcc,
                DialogText = true
            });
            // The altitude beside VERT → is an entry box: typing one sets an AT
            // crossing on the leg and is what ENABLES vertical direct on a leg that
            // has none. Without this row a pilot could only vertical-direct to legs
            // that already carried a constraint.
            if (leg.HasAltBox)
                rows.Add(new Row
                {
                    Kind = Row.Kinds.Field,
                    Display = $"{leg.Ident}{which} altitude, edit box: "
                        + (leg.VertAlt.Length > 0 ? leg.VertAlt : "blank")
                        + (leg.VertEnabled ? "" : ", type one to allow vertical direct"),
                    Label = $"{leg.Ident} altitude",
                    VertAltOcc = leg.VertOcc
                });
            if (leg.VertEnabled)
                rows.Add(new Row
                {
                    Kind = Row.Kinds.Button,
                    Display = $"Vertical direct to {leg.Ident}{which}"
                        + (leg.VertAlt.Length > 0 ? $", cross at {leg.VertAlt}" : "")
                        + ", button",
                    Label = "VERT →",
                    Occurrence = leg.VertOcc,
                    DialogText = true
                });
        }
        // Past the fourth leg the dialog PAGES (it renders five slots and swaps
        // their content) — say so, or a pilot on a real route never learns the
        // later legs exist.
        if (_dialogPages > 1)
            rows.Add(new Row
            {
                Kind = Row.Kinds.Text,
                Display = $"Page {_dialogPage + 1} of {_dialogPages} — Page Down for later legs, Page Up for earlier"
            });
        rows.Add(new Row
        {
            Kind = Row.Kinds.Button,
            Display = "DONE — close the dialog",
            Label = "DONE",
            IsDialogDone = true
        });

        ApplyRows(rows, _dialogPages > 1 ? $"Direct to — dialog, page {_dialogPage + 1} of {_dialogPages}" : "Direct to — dialog");
        if (justOpened)
            _announcer.AnnounceImmediate(
                $"Direct to dialog. {(_dialogPages > 1 ? $"Page 1 of {_dialogPages}, " : "")}{dt.Legs.Count} legs shown"
                + " — Enter on a leg goes direct to it; an altitude row plus Vertical direct flies a path down or up to it"
                + (dt.Fields.Any(f => f.Label == "CRS" && f.Editable)
                    ? "; the CRS row sets the intercept course"
                    : "")
                + ". Escape closes.");
        return true;
    }

    /// <summary>Press a Direct-To dialog row through the DIALOG's own text nodes
    /// (agent clickDialogText, reading order) — the same idents exist on the legs
    /// page beneath the floating dialog, so a window-wide click can hit the page
    /// copy. Speaks what was pressed; the EXEC-lamp monitor adds the
    /// "Flight plan modified" prompt when the aircraft raises the MOD.</summary>
    private async Task ClickDialogTextAsync(Row row)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            string? r = await _def.DisplaysAgentCallAsync(
                $"clickDialogText({A220DisplaysClient.JsString(row.Label)},{row.Occurrence})");
            if (r != "CLICKED")
            {
                _announcer.AnnounceImmediate(
                    $"Could not press that row — {DescribeClickFailure(r)}");
                await RefreshAsync();
                return;
            }
            await Task.Delay(700);
            await RefreshAsync();
            string said = row.Display.EndsWith(", button", StringComparison.Ordinal)
                ? row.Display[..^", button".Length] : row.Display;
            _announcer.AnnounceImmediate($"{said}.");
        }
        finally { _busy = false; }
    }

    /// <summary>Commit the scratchpad into the Direct-To dialog's typed-waypoint
    /// entry (agent clickDirectToEntry — the one gray-arrow row, found
    /// structurally). Empty scratchpad is refused: the click would submit
    /// nothing useful and the Input semantics are copy/submit.</summary>
    private async Task CommitDirectToEntryAsync()
    {
        if (_busy) return;
        string text = _scratchpad.Text.Trim();
        if (text.Length == 0)
        {
            _announcer.AnnounceImmediate("Type the waypoint ident in the scratchpad box first.");
            return;
        }
        MarkAction();
        _busy = true;
        try
        {
            string? sent = await TypeScratchpadVerifiedAsync(text);
            if (sent == null) return;
            string? r = await _def.DisplaysAgentCallAsync("clickDirectToEntry()");
            if (r != "CLICKED")
            {
                _announcer.AnnounceImmediate(
                    $"Could not reach the waypoint entry — {DescribeClickFailure(r)}");
                return;
            }
            await Task.Delay(900);
            await RefreshAsync();   // speaks the FMS's own amber error if it refused
            _announcer.AnnounceImmediate(
                $"{sent} sent as direct-to. Check the rows, then press EXEC to activate.");
            _scratchpad.Clear();
        }
        finally { _busy = false; }
    }

    /// <summary>Commit the scratchpad into a Direct-To dialog leg's altitude box
    /// (agent clickDirectToVertAlt): the aircraft sets an AT crossing on that leg,
    /// which is what enables VERT → there.</summary>
    private async Task CommitDirectToAltitudeAsync(Row row)
    {
        if (_busy) return;
        string text = _scratchpad.Text.Trim();
        if (text.Length == 0)
        {
            _announcer.AnnounceImmediate("Type the altitude in the scratchpad box first, for example 6000 or FL120.");
            return;
        }
        MarkAction();
        _busy = true;
        try
        {
            string? sent = await TypeScratchpadVerifiedAsync(text);
            if (sent == null) return;
            string? r = await _def.DisplaysAgentCallAsync($"clickDirectToVertAlt({row.VertAltOcc})");
            if (r != "CLICKED")
            {
                await _def.ClearScratchpadAsync();
                _announcer.AnnounceImmediate($"Could not reach the {row.Label} box — {DescribeClickFailure(r)}");
                return;
            }
            await Task.Delay(900);
            var sp = await _def.ScratchpadStateAsync();
            await _def.ClearScratchpadAsync();
            MarkAction();
            await RefreshAsync();
            var now = _rows.FirstOrDefault(r2 => r2.VertAltOcc == row.VertAltOcc);
            if (sp is { Kind: "TEXT" } s && s.Text.Length > 0 && s.Error.Length > 0)
            {
                _lastFmsError = s.Error.Trim();
                _announcer.AnnounceImmediate($"{row.Label} not accepted: {s.Error.Trim()}.");
            }
            else
                _announcer.AnnounceImmediate(now?.Display ?? $"{row.Label} sent.");
            _scratchpad.Clear();
        }
        finally { _busy = false; }
    }

    /// <summary>Re-render the list only when the content actually changed (shared by
    /// every overlay renderer and the page renderer's own signature check).</summary>
    private void ApplyRows(List<Row> rows, string breadcrumb)
    {
        //  separator: a row's text can never contain it, so two different
        // row splits can never produce the same signature.
        string signature = breadcrumb + "" + string.Join("", rows.Select(r => r.Display));
        if (signature == _lastSignature) { _rows = rows; return; }
        _lastSignature = signature;
        int keepIndex = _list.SelectedIndex;
        var keepRow = keepIndex >= 0 && keepIndex < _rows.Count ? _rows[keepIndex] : null;
        var oldRows = _rows;
        _rows = rows;
        int restored = keepRow == null ? -1 : rows.FindIndex(r => r.Display == keepRow.Display);
        // A field row's DISPLAY changes while its value is being edited (a Fusion
        // input renders the live scratchpad text in place, and a commit rewrites
        // the value), so the exact-text restore threw the reader to the TOP of
        // the page mid-entry (user report 2026-07-30). Fall back to the row's
        // IDENTITY, then — when the page is clearly still the same one (most old
        // rows survive) — to the old index; only a genuine page change goes to
        // the top.
        if (restored < 0 && keepRow != null && keepRow.Label.Length > 0)
            restored = rows.FindIndex(r => r.Kind == keepRow.Kind
                                           && r.Label == keepRow.Label
                                           && r.Occurrence == keepRow.Occurrence);
        if (restored < 0 && keepIndex >= 0 && rows.Count > 0 && oldRows.Count > 0)
        {
            int surviving = oldRows.Count(r => rows.Any(n => n.Display == r.Display));
            if (surviving * 2 >= oldRows.Count)
                restored = Math.Min(keepIndex, rows.Count - 1);
        }
        // Reconcile the ListBox IN PLACE — never Clear()+re-Add. A full rebuild
        // destroys and recreates the focused item, so the screen reader re-reads it
        // on EVERY poll, and on the legs page the poll signature changes every time
        // (DTG/ETA tick), so it re-read the waypoint under the cursor once a second
        // (user report 2026-09-22). Same lesson as the flyPad DOM reconcile.
        //
        // The row the user is SITTING ON is left untouched while the list has focus,
        // unless its IDENTITY changed: rewriting the selected item's text is itself
        // enough to make the reader speak it again, and a ticking distance is not
        // worth that. A real change — the plan sequencing so this slot becomes a
        // different waypoint — still rewrites, and SHOULD be heard. The frozen row's
        // text is flushed the moment the selection leaves it, so it can only ever be
        // stale while it is the one row the user is already being told about.
        bool freeze = _list.Focused && restored >= 0 && keepRow != null
                      && SameRowIdentity(keepRow, rows[restored]);
        _list.BeginUpdate();
        while (_list.Items.Count > rows.Count) _list.Items.RemoveAt(_list.Items.Count - 1);
        while (_list.Items.Count < rows.Count) _list.Items.Add(rows[_list.Items.Count].Display);
        for (int i = 0; i < rows.Count; i++)
        {
            if (freeze && i == restored) continue;
            if (!string.Equals((string)_list.Items[i], rows[i].Display, StringComparison.Ordinal))
                _list.Items[i] = rows[i].Display;
        }
        _list.EndUpdate();
        _frozenIndex = freeze ? restored : -1;

        // Only MOVE the selection when it actually has to move: assigning the same
        // index still raises SelectedIndexChanged, which is another re-read.
        if (restored >= 0) { if (_list.SelectedIndex != restored) _list.SelectedIndex = restored; }
        else if (_list.Items.Count > 0) { if (_list.SelectedIndex != 0) _list.SelectedIndex = 0; }
        _breadcrumb.Text = breadcrumb;
    }

    /// <summary>Fire an MKP page key (PREV/NEXT) and re-read the window. The cursor is
    /// sent back to the top of the list because it IS a new page — keeping the old index
    /// would leave the reader mid-page on unrelated rows. ApplyRows only moves the
    /// selection when it has to, so this is the one place that deliberately resets it.</summary>
    private void PageFms(string key)
    {
        if (_busy) return;
        _frozenIndex = -1;            // the page is changing; nothing to hold still
        _lastSignature = "";          // force a rebuild even if the new page looks similar
        _def.SendMkpKey(key);
        _ = DelayedRefreshAsync();
    }

    /// <summary>Is this the SAME row, ignoring its volatile text? Identity is what the
    /// actions are addressed by (LegIdx for a legs row, Label+Occurrence elsewhere), so
    /// a row that keeps its identity is the same waypoint/field with a new number in it.</summary>
    private static bool SameRowIdentity(Row a, Row b)
        => a.Kind == b.Kind && a.LegIdx == b.LegIdx
           && a.Occurrence == b.Occurrence
           && string.Equals(a.Label, b.Label, StringComparison.Ordinal);

    /// <summary>Put the real text back on a row that was held still under the cursor.
    /// Called as soon as the selection leaves it, so the user never arrows onto a
    /// stale row — every row they move TO was being refreshed all along.</summary>
    private void FlushFrozenRow()
    {
        int i = _frozenIndex;
        _frozenIndex = -1;
        if (i < 0 || i >= _list.Items.Count || i >= _rows.Count) return;
        if (!string.Equals((string)_list.Items[i], _rows[i].Display, StringComparison.Ordinal))
            _list.Items[i] = _rows[i].Display;
    }

    /// <summary>Activate a revision-menu option by its aircraft-side index. The
    /// aircraft closes the menu itself on any non-checkbox option; the follow-up
    /// refresh then shows either the page or the option's own dialog (HOLD,
    /// CROSSING…), and the FPLN-modified monitor speaks the EXEC prompt when the
    /// plan actually changed.</summary>
    private async Task ActivateMenuItemAsync(Row row)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            if (row.NeedsInlineValue)
            {
                // Pressing it would close the menu and change nothing (the item
                // holds an entry field and no action of its own) — refusing with
                // the reason beats a silent dead press.
                _announcer.AnnounceImmediate(
                    $"{row.Label} needs a value typed on the aircraft display first; "
                    + "it cannot be set from this window yet. Escape closes the menu.");
                return;
            }
            if (row.Disabled)
            {
                _announcer.AnnounceImmediate($"{row.Label} is not available right now.");
                return;
            }
            string? r = await _def.DisplaysAgentCallAsync($"taskMenuClick({row.MenuIndex})");
            if (r != "CLICKED")
            {
                _announcer.AnnounceImmediate(r switch
                {
                    "NO_MENU" or "NOT_FOUND" => "The menu has closed. Refreshing.",
                    "DISABLED" => $"{row.Label} is not available right now.",
                    _ => $"Could not activate {row.Label} — {DescribeClickFailure(r)}"
                });
                await RefreshAsync();
                return;
            }
            await Task.Delay(700);
            await RefreshAsync();
            // Confirm the press landed. If the menu stayed open it was a checkbox
            // toggle (the refreshed row text carries the new state); a closed menu
            // means the option ran — the plan-modified monitor adds the EXEC
            // prompt when the aircraft raised a MOD.
            if (!_menuOpen) _announcer.AnnounceImmediate($"{row.Label}.");
        }
        finally { _busy = false; }
    }

    /// <summary>Pick an option from the open dropdown. The aircraft closes the list
    /// itself on select, so the follow-up refresh shows the page (or the dialog)
    /// with the new value.</summary>
    private async Task SelectDropdownOptionAsync(Row row)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            string? r = await _def.DisplaysAgentCallAsync($"dropdownClick({row.OptionIndex})");
            if (r != "CLICKED")
            {
                _announcer.AnnounceImmediate(r switch
                {
                    "NO_DROPDOWN" or "NOT_FOUND" => "The dropdown has closed. Refreshing.",
                    _ => $"Could not select {row.Label} — {DescribeClickFailure(r)}"
                });
                await RefreshAsync();
                return;
            }
            await Task.Delay(600);
            await RefreshAsync();
            if (row.Label is not ("PREV" or "NEXT")) _announcer.AnnounceImmediate($"{row.Label} selected.");
        }
        finally { _busy = false; }
    }

    /// <summary>Press an open dialog's own DONE (apply) or CNCL (cancel) button.</summary>
    private async Task DismissDialogAsync()
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            string? r = await _def.DisplaysAgentCallAsync("dialogDone()");
            await Task.Delay(600);
            await RefreshAsync();
            if (r != null && r.StartsWith("CLICKED", StringComparison.Ordinal))
                _announcer.AnnounceImmediate(r.EndsWith("CNCL", StringComparison.Ordinal)
                    ? "Dialog cancelled." : "Dialog closed.");
            else
                _announcer.AnnounceImmediate($"Could not close the dialog — {DescribeClickFailure(r)}");
        }
        finally { _busy = false; }
    }

    /// <summary>Escape on any open overlay: dismiss it the way the aircraft does —
    /// a dialog through its own DONE/CNCL button, a menu or dropdown by clicking the
    /// aircraft's backdrop (its click-away cancel).</summary>
    private async Task DismissOverlayAsync()
    {
        if (_overlay == Overlay.Dialog)
        {
            var doneRow = _rows.FirstOrDefault(r => r.IsDialogDone);
            if (doneRow != null) { await DismissDialogAsync(); return; }
        }
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            var was = _overlay;
            // Each overlay has its OWN cancel. taskMenuClose needs the task menu's
            // frame and returns NO_MENU for a dropdown, so Escape on an open option
            // list could never close it (found 2026-07-30) — dropdowns dismiss via
            // their own backdrop click.
            await _def.DisplaysAgentCallAsync(
                was == Overlay.Dropdown ? "dropdownClose()" : "taskMenuClose()");
            await Task.Delay(400);
            await RefreshAsync();
            if (_overlay == was)
                _announcer.AnnounceImmediate("Could not close it — press Escape again to close the window.");
            else
                _announcer.AnnounceImmediate(was == Overlay.Dropdown ? "Dropdown cancelled." : "Menu closed.");
        }
        finally { _busy = false; }
    }

    // ---- actions ------------------------------------------------------------

    private Row? SelectedRow()
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Count ? _rows[_list.SelectedIndex] : null;

    private async Task ActivateSelectedAsync()
    {
        var row = SelectedRow();
        if (row == null || _busy) return;
        if (row.MenuIndex >= 0)
        {
            await ActivateMenuItemAsync(row);
            return;
        }
        if (row.OptionIndex >= 0)
        {
            await SelectDropdownOptionAsync(row);
            return;
        }
        if (row.IsDialogDone)
        {
            await DismissDialogAsync();
            return;
        }
        if (row.LegMenu)
        {
            await OpenLegMenuAsync(row);
            return;
        }
        switch (row.Kind)
        {
            case Row.Kinds.Field:
                await CommitToSelectedFieldAsync();
                break;
            case Row.Kinds.Button when row.DialogText:
                await ClickDialogTextAsync(row);
                break;
            case Row.Kinds.Button:
                await ClickTextAsync(row.Label, row.Occurrence);
                break;
        }
    }

    /// <summary>
    /// Enter on a legs-page waypoint: open the aircraft's revision (task) menu.
    /// The agent clicks the row's fix-symbol icon (canvas x≈144) — the ONLY
    /// element that opens the menu. The waypoint ident is a scratchpad Input
    /// (click = copy/submit the scratchpad into the leg), which is why the old
    /// clickWinText path read as "Enter does nothing" and could silently insert
    /// whatever the scratchpad held into the flight plan.
    /// </summary>
    private async Task OpenLegMenuAsync(Row row)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            // Address by the AIRCRAFT's leg index whenever we have it (calls the
            // row's own React onClick — immune to duplicate idents and to the list
            // shifting); fall back to the ident only if the fiber read failed.
            string call = row.LegIdx >= 0
                ? $"openLegMenuAt({row.LegIdx})"
                : $"openLegMenu({A220DisplaysClient.JsString(row.Label)},{row.Occurrence})";
            string? r = await _def.DisplaysAgentCallAsync(call);
            if (r != "CLICKED")
            {
                _announcer.AnnounceImmediate(r switch
                {
                    "NO_ICON" => $"{row.Label} has no revision menu on this row.",
                    "NO_ROW" or "NOT_FOUND" => $"{row.Label} is no longer on the page. Refreshing.",
                    _ => $"Could not open the revision menu — {DescribeClickFailure(r)}"
                });
                await RefreshAsync();
                return;
            }
            await Task.Delay(600);
            await RefreshAsync();
            // Direct props.onClick produced no menu. Retry once with synthetic
            // pointer/mouse events at the SAME fiber-addressed icon before
            // concluding anything — defense in depth, not a different theory.
            if (_overlay != Overlay.Menu && row.LegIdx >= 0)
            {
                r = await _def.DisplaysAgentCallAsync($"openLegMenuAtMouse({row.LegIdx})");
                if (r == "CLICKED")
                {
                    await Task.Delay(600);
                    await RefreshAsync();
                }
            }
            // RefreshAsync announces "Revision menu for X, N options" when the
            // overlay landed; anything else deserves an honest failure, never
            // silence (silence after Enter is exactly the old bug's shape).
            if (_overlay != Overlay.Menu)
            {
                // Both click paths ran the icon's own handler and NOTHING mounted
                // anywhere in the document — proven live 2026-07-30 (LGAV 03L):
                // the FMS refuses revision menus on procedural approach legs
                // (CI03L / FI03L, the course- and final-intercept pseudo-fixes),
                // while an off-screen real fix (KEA) opens fine — so this is the
                // aircraft declining THIS leg, not a scroll or click failure.
                Utils.Logging.Log.Debug("a220_fms",
                    $"revision menu did not open: label={row.Label} legIdx={row.LegIdx} " +
                    $"fallback={r} diag={(row.LegIdx >= 0 ? await _def.DisplaysAgentCallAsync($"legRowDiag({row.LegIdx})") : "n/a")}");
                _announcer.AnnounceImmediate(
                    $"The aircraft offers no revision menu for {row.Label}. "
                    + "Procedural legs, such as approach course and final intercepts, cannot be revised.");
                return;
            }
            // Wrong-waypoint guard (in-sim report 2026-07-30: duplicate-ident
            // rows sometimes opened another leg's menu): if the menu that appeared
            // isn't this waypoint's, close it rather than let a DELETE land on the
            // wrong leg, and say exactly what happened. The header is the ident
            // PLUS any navaid detail ("KEA 115.00" for a VOR — live-verified), so
            // the test is on the FIRST TOKEN; an exact compare would slam valid
            // menus shut on every VOR/NDB fix.
            string headerIdent = _menuHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            if (headerIdent.Length > 0
                && !string.Equals(headerIdent, row.Label, StringComparison.OrdinalIgnoreCase))
            {
                await DismissOverlayAsync();
                _announcer.AnnounceImmediate(
                    $"The aircraft opened the menu for {headerIdent}, not {row.Label} — closed it. "
                    + "The page may have shifted; refresh with F5 and try again.");
            }
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// Remove a flight-plan discontinuity by invoking the FMS's OWN `onDelete`
    /// handler for that leg (`setLegFix(idx, null)`), reached through React's
    /// fibers and addressed by the aircraft's leg index.
    ///
    /// This replaces the old arm-the-scratchpad-then-synthesize-a-click dance
    /// (2026-07-30). That path was unreliable in both directions: the click could
    /// land on a neighbouring row, and an un-armed click ran the Input's
    /// copy/submit branch, silently writing the scratchpad into the flight plan.
    /// Calling the handler cannot hit the wrong row, needs no scratchpad state,
    /// and still goes through the FMS's normal rules — so it may raise a MOD to
    /// EXEC, refuse via the amber box, or open the aircraft's SELECT CONSTRAINT
    /// dialog when the legs either side carry constraints (a real prompt seen
    /// live, previously mis-reported as "the delete did not go through").
    ///
    /// Nothing is assumed: the page is re-read and the outcome — removed,
    /// awaiting a choice, or refused — is spoken from what the page actually says.
    /// </summary>
    private async Task DeleteLegRowAsync(Row row)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            if (row.LegIdx < 0)
            {
                Utils.Logging.Log.Debug("a220_fms",
                    $"disco delete blocked, row unaligned: label={row.Label} disco#{row.DiscoIndex} "
                    + $"legs={await _def.DisplaysAgentCallAsync("legs()")}");
                _announcer.AnnounceImmediate(
                    "Cannot delete this row — it could not be matched to the aircraft's leg list. Refresh with F5 and try again.");
                return;
            }
            int discosBefore = _rows.Count(r2 => r2.DiscoIndex >= 0);

            // wantDisco=true: the agent must find the gap's own ▯-slot at this
            // legIdx and refuses (WRONG_ROW) rather than run a real waypoint's
            // delete handler — the aircraft can give a discontinuity the same
            // legIdx as a neighbouring waypoint, which silently no-ops the delete.
            string? r = await _def.DisplaysAgentCallAsync($"deleteLegAt({row.LegIdx}, true)");
            if (r == null || !r.StartsWith("CALLED", StringComparison.Ordinal))
            {
                Utils.Logging.Log.Debug("a220_fms",
                    $"deleteLegAt refused: legIdx={row.LegIdx} r={r} legs={await _def.DisplaysAgentCallAsync("legs()")}");
                if (r != null && r.StartsWith("WRONG_ROW|", StringComparison.Ordinal))
                    _announcer.AnnounceImmediate(
                        $"The delete was not sent — the aircraft's row numbering points at "
                        + $"{r["WRONG_ROW|".Length..]}, not the discontinuity. Refreshing; try again.");
                else
                    _announcer.AnnounceImmediate(r switch
                    {
                        "NO_DELETE" => "This row cannot be deleted — the aircraft offers no delete for it.",
                        "NO_ROW" => "That row is no longer on the page. Refreshing.",
                        _ => $"Could not delete — {DescribeClickFailure(r)}"
                    });
                await RefreshAsync();
                return;
            }

            await Task.Delay(900);
            await RefreshAsync();

            // The aircraft may be asking WHICH constraint to keep: that dialog is
            // now the list content, so hand the pilot over to it instead of
            // claiming a result neither of us knows yet.
            if (_overlay == Overlay.Dialog)
            {
                _announcer.AnnounceImmediate(
                    "The FMS needs a choice before it can remove that gap — the dialog is open in the list.");
                return;
            }

            int discosAfter = _rows.Count(r2 => r2.DiscoIndex >= 0);
            bool removed = discosAfter < discosBefore;
            // The amber box is the only place the FMS says WHY it refused; the
            // refresh above may have spoken it already (AnnounceFmsError dedupes),
            // but fold it into the verdict so the reason and the outcome arrive
            // as one sentence, and log the whole picture either way.
            string err = removed ? "" : await _def.DisplaysAgentCallAsync("fmsError()") ?? "";
            if (!removed)
                Utils.Logging.Log.Debug("a220_fms",
                    $"disco delete no-op: legIdx={row.LegIdx} r={r} before={discosBefore} after={discosAfter} "
                    + $"overlay={_overlay} err={err} legs={await _def.DisplaysAgentCallAsync("legs()")}");
            _announcer.AnnounceImmediate(removed
                ? "Discontinuity deleted. Press EXEC to activate the change."
                : err.Length > 0
                    ? $"The FMS refused the delete: {err}."
                    : "The delete did not go through — the discontinuity is still shown.");
        }
        finally { _busy = false; }
    }

    /// <summary>"Go to page" dispatch: page tiles are a plain text click; SEC/ACT
    /// go through the flight-plan dropdown gesture.</summary>
    private Task NavigateAsync(string target)
        => target is "SEC" or "ACT" ? SelectFlightPlanModeAsync(target) : ClickTextAsync(target, 0);

    /// <summary>
    /// Switch the ACT/SEC flight-plan selector. It is a DROPDOWN widget in the
    /// FMS window's tab row — only the CURRENT mode's label exists as text until
    /// it is clicked open (DisplayUnits renderer: options ACT/SEC + hidden MOD,
    /// onSelect=selectFlightPlan, disabled while a MOD is pending). So this is a
    /// two-click gesture: open the dropdown via the current mode's label, then
    /// click the target option (document-wide fallback, since the popup layer may
    /// mount outside the FMS window's SVG group).
    /// Returns true when the FMS is on the target plan when done.
    /// </summary>
    private async Task<bool> SelectFlightPlanModeAsync(string target)
    {
        if (_busy) return false;
        MarkAction();
        _busy = true;
        try
        {
            var model = await ScrapeAsync();
            if (model == null || model.Side.Length == 0)
            {
                _announcer.AnnounceImmediate("No FMS window is open on the displays — use Open FMS window.");
                return false;
            }
            string mode = model.Mode;
            if (mode == target)
            {
                _announcer.AnnounceImmediate($"Already on the {target} flight plan.");
                return true;
            }
            if (mode == "MOD")
            {
                _announcer.AnnounceImmediate(
                    "A modification is pending — the plan selector is locked. EXEC or Cancel mod first.");
                return false;
            }
            string? opened = await _def.DisplaysAgentCallAsync(
                $"clickWinText(\"fms\",{A220DisplaysClient.JsString(mode)},0)");
            if (opened == null || !opened.StartsWith("CLICKED", StringComparison.Ordinal))
            {
                _announcer.AnnounceImmediate(
                    $"Could not open the plan selector — {DescribeClickFailure(opened)}");
                return false;
            }
            await Task.Delay(500);
            string? picked = await _def.DisplaysAgentCallAsync(
                $"clickWinText(\"fms\",{A220DisplaysClient.JsString(target)},0)");
            if (picked == null || !picked.StartsWith("CLICKED", StringComparison.Ordinal))
                picked = await _def.DisplaysAgentCallAsync(
                    $"clickAnyText({A220DisplaysClient.JsString(target)},0)");
            if (picked == null || !picked.StartsWith("CLICKED", StringComparison.Ordinal))
            {
                _announcer.AnnounceImmediate(
                    $"Plan selector opened, but {target} was not clickable — {DescribeClickFailure(picked)}");
                await RefreshAsync();
                return false;
            }
            await Task.Delay(700);
            await RefreshAsync();
            _announcer.AnnounceImmediate($"{target} flight plan selected.");
            return true;
        }
        finally { _busy = false; }
    }

    /// <summary>SimBrief uplink: first ensure the SEC plan is selected (dropdown
    /// gesture — the old scripted "SEC" click could never work, the option text
    /// doesn't exist until the dropdown is open), then the proven click flow.</summary>
    private async Task RunUplinkFlowAsync()
    {
        if (!await SelectFlightPlanModeAsync("SEC")) return;
        await RunFlowAsync("SimBrief route uplink",
            new[] { "FPLN", "SEC INIT", "FPLN UPLINK", "SIMBRIEF", "SEND" });
    }

    private async Task ClickTextAsync(string label, int occurrence)
    {
        MarkAction();
        _busy = true;
        try
        {
            string? result = await _def.DisplaysAgentCallAsync(
                $"clickWinText(\"fms\",{A220DisplaysClient.JsString(label)},{occurrence})");
            if (result == null || !result.StartsWith("CLICKED", StringComparison.Ordinal))
            {
                _announcer.AnnounceImmediate($"Could not press {label} — {DescribeClickFailure(result)}");
                return;
            }
            await Task.Delay(700);
            await RefreshAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// The scratchpad-first commit: CLEAR the MKP scratchpad, type the box text via
    /// H:A220_KBD_* events, click the selected field's value node, then read back
    /// what the field shows now — the only confirmation that the commit landed.
    /// With an empty box this just clicks the field (push-target fields like
    /// DEPARTURES… open pages).
    /// </summary>
    private async Task CommitToSelectedFieldAsync()
    {
        if (_busy) return;
        var row = SelectedRow();
        if (row is { LegMenu: true })
        {
            await CommitScratchpadToLegAsync(row);
            return;
        }
        if (row is { IsDirectToEntry: true })
        {
            await CommitDirectToEntryAsync();
            return;
        }
        if (row is { VertAltOcc: >= 0 })
        {
            await CommitDirectToAltitudeAsync(row);
            return;
        }
        if (row is not { Kind: Row.Kinds.Field })
        {
            _announcer.AnnounceImmediate("Select a field row first.");
            return;
        }
        MarkAction();
        _busy = true;
        try
        {
            // A dropdown is a CHOOSER: clicking it opens the option list, which the
            // overlay renderer then presents (with each option's position and which
            // is current). Typing the scratchpad at it would be meaningless, so the
            // staged text is deliberately left alone rather than sent into it.
            string text = row.IsDropdown ? "" : _scratchpad.Text.Trim();
            if (text.Length > 0 && await TypeScratchpadVerifiedAsync(text) == null)
                return;

            // Inside a dialog the click MUST be dialog-scoped: the window-wide
            // search counts occurrences differently from the dialog rows the pilot
            // picked from, and can hit a page token showing through beneath the
            // floating dialog — writing the value somewhere the pilot never chose.
            string fieldCall = row.ClickX is double cx && row.ClickY is double cy
                ? $"clickFmsAt({cx.ToString(System.Globalization.CultureInfo.InvariantCulture)},{cy.ToString(System.Globalization.CultureInfo.InvariantCulture)})"
                : _overlay == Overlay.Dialog
                ? $"clickDialogField({A220DisplaysClient.JsString(row.Label)},{row.Occurrence})"
                : $"clickFmsField({A220DisplaysClient.JsString(row.Label)},{row.Occurrence})";
            string? result = await _def.DisplaysAgentCallAsync(fieldCall);
            Utils.Logging.Log.Debug("a220_fms",
                $"field commit: label='{row.Label}' occ={row.Occurrence} text='{text}' click={result ?? "null"}");
            if (result == null || !result.StartsWith("CLICKED", StringComparison.Ordinal))
            {
                _announcer.AnnounceImmediate($"Could not click the {row.Label} field — {DescribeClickFailure(result)}");
                return;
            }

            await Task.Delay(900);
            if (row.IsDropdown)
            {
                // The list should now be open; RenderDropdown announces the options.
                // If it is not, say so — a chooser that silently refuses to open is
                // exactly the kind of dead end this pass exists to remove.
                var was = _overlay;
                await RefreshAsync();
                if (_overlay != Overlay.Dropdown)
                    _announcer.AnnounceImmediate(was == Overlay.Dropdown
                        ? $"{row.Label} list closed."
                        : $"{row.Label} did not open its list.");
                return;
            }
            if (_overlay == Overlay.Dialog)
            {
                // Read the value back from the DIALOG, not the page: ScrapeAsync
                // reads the page behind it, so a dialog commit always fell through
                // to the vague "page changed" and the pilot never heard the course
                // they had just set.
                string? before = _rows
                    .FirstOrDefault(r2 => r2.Kind == Row.Kinds.Field
                                          && r2.Label == row.Label
                                          && r2.Occurrence == row.Occurrence)?.Display;
                var spDlg = await _def.ScratchpadStateAsync();
                if (text.Length > 0) await _def.ClearScratchpadAsync();
                string dlgErr = spDlg?.Error ?? "";
                if (text.Length > 0 && spDlg is { Kind: "TEXT", Text.Length: 0 }) dlgErr = "";
                await RefreshAsync();
                var now = _rows.FirstOrDefault(r2 => r2.Kind == Row.Kinds.Field
                                                     && r2.Label == row.Label
                                                     && r2.Occurrence == row.Occurrence);
                _announcer.AnnounceImmediate(dlgErr.Length > 0
                    ? RefusalMessage(row, dlgErr, now?.Display)
                    : now == null
                        ? $"{row.Label} committed; the dialog changed."
                        : now.Display == before
                            ? $"{row.Label} unchanged — the FMS may have refused the entry."
                            : now.Display);
                _scratchpad.Clear();
                return;
            }
            // A REFUSED entry does two things a success does not: the FMS
            // broadcasts its reason ("INVALID ENTRY"…), and it leaves the typed
            // text sitting on the aircraft scratchpad (the Input only clears it
            // on success) — where it would concatenate under the NEXT entry.
            // Speak the reason, then always leave the scratchpad empty. The field
            // is read back AFTER the clear: a refused Input keeps echoing the
            // scratchpad, and the agent reports its real value only once the
            // echo is gone.
            var sp = await _def.ScratchpadStateAsync();
            if (text.Length > 0) await _def.ClearScratchpadAsync();
            var model = await ScrapeAsync();
            var updated = model?.Fields.FirstOrDefault(f => f.Label == row.Label && f.Occurrence == row.Occurrence);
            Utils.Logging.Log.Debug("a220_fms",
                $"field readback: label='{row.Label}' value='{updated?.Value ?? "(row gone)"}' "
                + $"sp={sp?.Kind ?? "null"}:'{sp?.Text}' err='{sp?.Error}'");
            // The store keeps its last error for ~3 s, so an error alone could be a
            // PREVIOUS refusal: this entry was refused only if the typed text is
            // also still on the scratchpad (a success clears it).
            string err = sp?.Error ?? "";
            if (text.Length > 0 && sp is { Kind: "TEXT", Text.Length: 0 }) err = "";
            if (err.Length > 0)
            {
                // ONE utterance: the refusal used to be spoken and then cut off by
                // the "X now …" read-back straight after it (both interrupt).
                _announcer.AnnounceImmediate(RefusalMessage(row, err,
                    updated == null ? null : $"{updated.Name} is {A220FmsScreenParsing.SpokenFieldValue(updated.Value)}"));
            }
            else if (updated != null)
            {
                string exec = model!.ExecAvailable ? " Modification pending — EXEC to activate." : "";
                _announcer.AnnounceImmediate(
                    $"{updated.Name} now {A220FmsScreenParsing.SpokenFieldValue(updated.Value)}.{exec}");
            }
            else
            {
                _announcer.AnnounceImmediate("Field committed; page changed.");
            }
            _scratchpad.Clear();
            await RefreshAsync();
        }
        finally { _busy = false; }
    }

    /// <summary>The one sentence for a refused entry: the FMS's own reason, the range
    /// the field accepts when the aircraft declares one (the FMS only ever says
    /// "INVALID ENTRY" — the pilot otherwise has to guess what it wanted), and what
    /// the field holds now.</summary>
    private string RefusalMessage(Row row, string error, string? nowText)
    {
        _lastFmsError = error.Trim();     // the poll must not repeat it
        string name = row.Display.Split(',')[0];
        string range = row.Min is double mn && row.Max is double mx
            ? $" {name} accepts {Num(mn)} to {Num(mx)}." : "";
        string now = nowText is { Length: > 0 } ? $" {nowText.TrimEnd('.')}." : "";
        return $"{name} not accepted: {error.Trim()}.{range}{now}";

        static string Num(double v) => v.ToString(v % 1 == 0 ? "0" : "0.##",
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stage <paramref name="text"/> on the aircraft's scratchpad for a commit
    /// click: reset the shared store to empty (agent resetScratchpad — the MKP
    /// CLEAR key is a ONE-CHARACTER backspace that arms --DELETE-- on an empty
    /// scratchpad, so it can never serve as a clear), type via MKP key events,
    /// then VERIFY through the agent's tap that the buffer holds exactly what
    /// was typed. Without the reset+verify, a refused entry's leftover text
    /// concatenated under every later entry and the FMS answered "INVALID
    /// ENTRY" forever (live 2026-07-30, FUEL page). Returns the text as sent,
    /// or null after announcing why it stopped.
    /// </summary>
    private async Task<string?> TypeScratchpadVerifiedAsync(string text)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await _def.ClearScratchpadAsync();
            string sent = await _def.SendScratchpadTextAsync(text);
            if (sent.Length == 0)
            {
                _announcer.AnnounceImmediate("No typeable characters in the scratchpad text.");
                return null;
            }
            await Task.Delay(250);
            var st = await _def.ScratchpadStateAsync();
            if (st == null) return sent;                       // agent unreachable — proceed blind
            var s = st.Value;
            if (s.Kind == "UNKNOWN") return sent;              // no tap data — proceed
            string expected = SynapticA220Definition.PredictScratchpadBuffer(sent);
            if (s.Kind == "TEXT" && s.Text == expected) return sent;
            Utils.Logging.Log.Debug("a220_fms",
                $"scratchpad verify mismatch attempt={attempt} expected='{expected}' got={s.Kind}:'{s.Text}'");
        }
        await _def.ClearScratchpadAsync();
        _announcer.AnnounceImmediate(
            "The aircraft scratchpad did not take the typed text — it has been cleared; try again.");
        return null;
    }

    /// <summary>
    /// Scratchpad → a LEGS waypoint line: the cockpit's waypoint-entry gesture.
    /// The leg ident is a Fusion Input; clicking it with text in the aircraft
    /// scratchpad SUBMITS that text into the leg (the FMS raises a MOD, or its
    /// amber error box says why not — the refresh poll speaks that box). With an
    /// EMPTY scratchpad the same click silently COPIES the ident instead — the
    /// exact loop behind the 2026-07-30 plan corruption — so empty is refused
    /// here and Enter on the row opens the revision menu instead.
    /// </summary>
    private async Task CommitScratchpadToLegAsync(Row row)
    {
        string text = _scratchpad.Text.Trim();
        if (text.Length == 0)
        {
            _announcer.AnnounceImmediate(
                "Type a waypoint in the scratchpad first. Enter on the row opens its revision menu.");
            return;
        }
        if (IsConstraintEntry(text))
        {
            await CommitConstraintToLegAsync(row, text);
            return;
        }
        MarkAction();
        _busy = true;
        try
        {
            string typed = text.ToUpperInvariant();
            int before = _rows.Count(r2 => r2.LegMenu && r2.Label == typed);

            string? sent = await TypeScratchpadVerifiedAsync(text);
            if (sent == null) return;

            string? result = await _def.DisplaysAgentCallAsync(
                $"clickLegIdent({A220DisplaysClient.JsString(row.Label)},{row.Occurrence})");
            if (result != "CLICKED")
            {
                _announcer.AnnounceImmediate(
                    $"Could not reach the {row.Label} leg — {DescribeClickFailure(result)}");
                return;
            }

            await Task.Delay(900);
            await RefreshAsync();   // re-renders MOD state; speaks the FMS's own amber error if refused
            int after = _rows.Count(r2 => r2.LegMenu && r2.Label == typed);
            // A refused submit leaves the typed text on the aircraft scratchpad —
            // clear it so it can never concatenate under the next entry.
            await _def.ClearScratchpadAsync();
            _announcer.AnnounceImmediate(after > before
                ? $"{sent} entered at the {row.Label} line. Press EXEC to activate the modification."
                : $"{sent} sent to the {row.Label} line — the FMS may have refused it; check the rows.");
            _scratchpad.Clear();
        }
        finally { _busy = false; }
    }

    /// <summary>
    /// A speed/altitude CONSTRAINT entry, in the real FMS's own scratchpad format:
    /// "/9000A" (at or above), "/9000B" (at or below), "/9000" (at), "/FL120",
    /// "250/" (speed), "250/9000". A waypoint ident always starts with a LETTER and
    /// a place/bearing/distance entry is letter-first too, so anything that starts
    /// with a digit or a slash and holds a slash can only be a constraint.
    /// </summary>
    internal static bool IsConstraintEntry(string text)
        => System.Text.RegularExpressions.Regex.IsMatch(text.Trim().ToUpperInvariant(),
            @"^(\d{1,3})?/((FL)?\d{1,5}[AB]?)?$")
           && text.Trim() != "/";

    /// <summary>
    /// Scratchpad → the LEGS row's speed/altitude constraint field (the green
    /// "↑250/4000A" column), the cockpit's own crossing-restriction gesture. It is
    /// the ONLY way to put a crossing altitude on a terminal (procedure) waypoint:
    /// the aircraft opens no revision menu — so no CROSSING… — for those (live
    /// 2026-09-24, EGNT GIRL1Y: NTS08 and NTW03 have no menu, GIRLI does).
    /// Addressed by the aircraft's own legIdx, never by text.
    /// </summary>
    private async Task CommitConstraintToLegAsync(Row row, string text)
    {
        if (row.LegIdx < 0)
        {
            _announcer.AnnounceImmediate(
                $"Could not identify the {row.Label} leg on the aircraft — refresh with F5 and try again.");
            return;
        }
        MarkAction();
        _busy = true;
        try
        {
            string? sent = await TypeScratchpadVerifiedAsync(text);
            if (sent == null) return;
            string? r = await _def.DisplaysAgentCallAsync($"clickLegConstraintAt({row.LegIdx})");
            if (r != "CLICKED")
            {
                await _def.ClearScratchpadAsync();
                _announcer.AnnounceImmediate(
                    $"Could not reach the {row.Label} constraint — {DescribeClickFailure(r)}");
                return;
            }
            await Task.Delay(900);
            var sp = await _def.ScratchpadStateAsync();
            await _def.ClearScratchpadAsync();
            MarkAction();
            await RefreshAsync();
            var now = _rows.FirstOrDefault(r2 => r2.LegIdx == row.LegIdx && r2.LegMenu);
            string nowText = now?.Display.Replace(", button", "") ?? row.Label;
            if (sp is { Kind: "TEXT" } s && s.Text.Length > 0 && s.Error.Length > 0)
            {
                _lastFmsError = s.Error.Trim();
                _announcer.AnnounceImmediate($"{row.Label} constraint not accepted: {s.Error.Trim()}. {nowText}.");
            }
            else
                _announcer.AnnounceImmediate($"{nowText}. Press EXEC to activate the modification.");
            _scratchpad.Clear();
        }
        finally { _busy = false; }
    }

    private async Task ExecAsync()
    {
        MarkAction();
        _def.PressFplnExec();
        await Task.Delay(1200);
        bool modified = _def.FlightPlanModified();
        _announcer.AnnounceImmediate(modified
            ? "Still modified — EXEC did not complete."
            : "Executed.");
        await RefreshAsync();
    }

    private async Task CancelModAsync()
    {
        MarkAction();
        _def.PressFplnCancel();
        await Task.Delay(1200);
        bool modified = _def.FlightPlanModified();
        _announcer.AnnounceImmediate(modified
            ? "Still modified — cancel did not complete."
            : "Modification cancelled.");
        await RefreshAsync();
    }

    private async Task OpenFmsWindowAsync()
    {
        MarkAction();
        // CTP FMS format key (captain side) — proven to open the FMS window.
        _def.SendCtpKey("FMS");
        await Task.Delay(1200);
        await RefreshAsync();
        if (_rows.Count == 0)
            _announcer.AnnounceImmediate("FMS window did not appear — is the aircraft powered?");
    }

    /// <summary>
    /// Guided flow = scripted sequence of soft-key clicks with per-step
    /// verification (the agent reports NOT_FOUND when a label isn't on screen —
    /// that is the honest failure, spoken, never papered over).
    /// </summary>
    private async Task RunFlowAsync(string name, string[] clickLabels)
    {
        if (_busy) return;
        MarkAction();
        _busy = true;
        try
        {
            foreach (string label in clickLabels)
            {
                string? result = await _def.DisplaysAgentCallAsync(
                    $"clickWinText(\"fms\",{A220DisplaysClient.JsString(label)},0)");
                if (result == null || !result.StartsWith("CLICKED", StringComparison.Ordinal))
                {
                    _announcer.AnnounceImmediate(
                        $"{name}: stopped — {label} is not on the screen ({DescribeClickFailure(result)}). " +
                        "Continue from the page rows.");
                    await RefreshAsync();
                    return;
                }
                await Task.Delay(900);
            }
            await RefreshAsync();
            _announcer.AnnounceImmediate($"{name}: all steps pressed. Review the page rows for the result.");
        }
        finally { _busy = false; }
    }

    private static string DescribeClickFailure(string? result) => result switch
    {
        null or "" => "display connection unavailable",
        "NO_WINDOW" => "no FMS window is open",
        "NOT_FOUND" => "not shown on the current page",
        "NO_VALUE" => "no value slot under that label",
        "NO_TARGET" => "the display refused the click",
        _ => result
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pollTimer.Dispose(); _pageNavTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
