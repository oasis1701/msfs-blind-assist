using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.MD11;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.MD11;

/// <summary>
/// Accessible MCDU for the TFDi Design MD-11.
///
/// WHERE THE TEXT COMES FROM. Not a DOM scrape — the MD-11's screens are WASM-rendered canvases
/// with no HTML behind them, which is why the Coherent transport used for the PMDG / FBW / HS787
/// CDUs cannot work here. TFDi instead export all three MCDUs as plain text over the SimConnect
/// client data area <c>MD11MCDU</c>; <see cref="Md11McduDataManager"/> decodes it. "No DOM" and
/// "unreadable" are different claims, and conflating them once cost this aircraft its CDU.
///
/// THREE UNITS, unlike every other CDU in this app. The MD-11 has Left (Captain), Center and Right
/// (First Officer) MCDUs, and the export carries all three independently — so this form has a unit
/// selector rather than being hardwired to the Captain's. The three key sets are identical
/// (74 nodes each), so one layout serves all three; only the node-id prefix changes.
///
/// WHY IT POLLS. The manager only exists once SimConnect is connected and is replaced on every
/// reconnect (one per connection), so subscribing to its event would bind this form to one
/// instance and silently go deaf if the user opened the window before connecting, or across a
/// reconnect. Reading the manager's cached screen on a
/// timer is immune to both, costs a reference compare per tick when nothing has changed, and is
/// what the PMDG CDU form already does. The underlying request is ON_SET/CHANGED, so the sim only
/// delivers on a real change regardless.
/// </summary>
public class Md11McduForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly TFDiMD11Definition _definition;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr previousWindow = IntPtr.Zero;

    private ComboBox unitSelector = null!;
    private DisplayListBox mcduDisplay = null!;
    private TextBox scratchpadInput = null!;

    /// <summary>
    /// Connection state and lit annunciators, as a read-only TEXT BOX in the tab order. It was a
    /// Label: not in the tab order — a screen reader reaches one only with its review cursor —
    /// and with an AccessibleName that shadowed the very text it existed to show (CLAUDE.md:
    /// status readouts are read-only TextBoxes; the iFly CDU window's status is the same control).
    /// </summary>
    private TextBox statusBox = null!;

    private System.Windows.Forms.Timer? _pollTimer;

    private Md11McduUnit _unit = Md11McduUnit.Left;
    private Md11McduScreen? _screen;
    private object? _lastRendered;

    /// <summary>
    /// The last title adopted and the row the cursor was last on, PER UNIT. One slot for all
    /// three compared a unit's title against whichever unit was shown before it, so every switch
    /// read as a page change and threw the cursor to line 1: see <see cref="Md11McduUnitMemory"/>.
    /// </summary>
    private readonly Md11McduUnitMemory _memory = new();

    /// <summary>
    /// When each UNIT went continuously blank, or null while it has content — one clock per
    /// unit, all three ticked on every poll (<see cref="TickBlankClocks"/>), because the clock
    /// belongs to the unit and not to the window: a switch to a unit that has been blank for a
    /// while must judge it settled at once, not hold the previous unit's page under the new
    /// unit's name for the settle time.
    ///
    /// The MD-11 ERASES its CDU before redrawing, so a blank frame arrives on every page change
    /// and the new page follows 202-438 ms later (37 measured page changes). This is what lets a
    /// repaint be held rather than believed. See <see cref="Md11McduPresence.BlankSettleMs"/>.
    /// </summary>
    private readonly DateTime?[] _blankSince = new DateTime?[3];

    /// <summary>
    /// The scratchpad read-back, shared with the iFly CDU (<see cref="CduScratchpadAnnouncer"/>) and
    /// built by <see cref="Md11McduScratchpad.CreateReadBack"/>, the construction its tests use:
    /// fed on every poll tick that shows a page, silent on its first sample after a re-seed,
    /// held while a typing or CLR burst is being written so only the settled text is read, spoken
    /// only once a change has read the same on two polls in a row (so a one-poll redraw flicker
    /// never is), and "Scratchpad cleared" for an emptied pad. It replaced a 300 ms debounce timer
    /// that repeated the Delete clear's own "Scratchpad cleared" and let a half-typed entry through.
    /// </summary>
    private readonly CduScratchpadAnnouncer _scratchpad = Md11McduScratchpad.CreateReadBack();

    /// <summary>True while <see cref="ClearScratchpadAsync"/> runs; a second Delete in that time is ignored.</summary>
    private bool _clearing;

    private string _lastAnnouncedFlags = "";

    /// <summary>
    /// What each item of the list currently stands for (title / label n / line n / scratchpad),
    /// or null while the list shows an advisory. This — not the item index — is how the cursor
    /// is put back after a redraw: see <see cref="Md11McduRows.Restore"/>.
    /// </summary>
    private IReadOnlyList<Md11McduRow>? _rows;

    public Md11McduForm(TFDiMD11Definition definition, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _definition = definition;
        _sim = sim;
        _announcer = announcer;

        InitializeComponent();
        SetupAccessibility();
        SetupEventHandlers();
    }

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void InitializeComponent()
    {
        this.SuspendLayout();

        this.Text = "MD-11 MCDU";
        this.ClientSize = new Size(620, 780);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;

        int y = 10;

        unitSelector = new ComboBox
        {
            Location = new Point(10, y),
            Size = new Size(220, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU unit",
            AccessibleDescription = "Which of the three MD-11 MCDUs to display and control."
        };
        unitSelector.Items.AddRange(new object[] { "Left (Captain)", "Center", "Right (First Officer)" });
        unitSelector.SelectedIndex = 0;

        statusBox = new TextBox
        {
            Text = "MCDU: waiting for data",
            Location = new Point(240, y),
            Size = new Size(370, 25),
            ReadOnly = true,
            TabStop = true,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "MCDU status",
            AccessibleDescription = "Connection state and lit MCDU annunciators."
        };
        y += 32;

        // The shared read-only display list every live display window uses (one configuration,
        // one reconcile path — DisplayList.UpdateInPlace behind SetLines). The font, colours and
        // height below are this window's own and override the shared defaults; type-ahead stays
        // on, as it was on the plain ListBox, because no character key is MCDU input here.
        mcduDisplay = new DisplayListBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 290),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Current MCDU screen. Use arrow keys to read lines.",
            IntegralHeight = false
        };
        y += 300;

        scratchpadInput = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 25),
            AccessibleName = "MCDU Input",
            AccessibleDescription = "Type text and press Enter to send it to the MCDU scratchpad."
        };
        y += 34;

        int btnWidth = 116, btnHeight = 30, btnSpacing = 5, perRow = 5;
        var buttons = new List<Control>();
        for (int i = 0; i < Md11McduKeys.PageButtons.Length; i++)
        {
            var (label, key) = Md11McduKeys.PageButtons[i];
            var btn = new Button
            {
                Text = label,
                Location = new Point(10 + (i % perRow) * (btnWidth + btnSpacing),
                                     y + (i / perRow) * (btnHeight + btnSpacing)),
                Size = new Size(btnWidth, btnHeight),
            };
            btn.Click += (s, e) => { if (PressKey(key) && key == "CLR") HoldScratchpadReadBack(1); };
            buttons.Add(btn);
        }

        this.Controls.Add(unitSelector);
        this.Controls.Add(statusBox);
        this.Controls.Add(mcduDisplay);
        this.Controls.Add(scratchpadInput);
        foreach (var b in buttons) this.Controls.Add(b);

        // The display and the input first — the window opens on the display — then the two
        // controls about the unit itself, which share the top row: its status, then the selector.
        int tabIdx = 0;
        mcduDisplay.TabIndex = tabIdx++;
        scratchpadInput.TabIndex = tabIdx++;
        statusBox.TabIndex = tabIdx++;
        unitSelector.TabIndex = tabIdx++;
        foreach (var b in buttons) b.TabIndex = tabIdx++;

        this.ResumeLayout(false);
    }

    private void SetupAccessibility()
    {
        this.AccessibleName = "MD-11 MCDU";
        this.AccessibleDescription = "TFDi Design MD-11 MCDU display and controls";

        // Hide on close so the unit selection, the current page and the window position survive
        // between opens — but let a real process shutdown through. The house wiring gates on
        // CloseReason: an unconditional cancel here made Application.Exit abort AFTER MainForm's
        // own teardown had run, and the updater's restart then waited on an exe that never closed.
        MonitorManagerShared.HideOnClose(this, () =>
        {
            if (previousWindow != IntPtr.Zero) SetForegroundWindow(previousWindow);
        });

        // 250 ms: fast enough that a keypress feels immediate, slow enough to be free. A tick
        // where nothing arrived costs one reference compare.
        //
        // NOT started here. ShowForm starts it and OnVisibleChanged stops it on hide, as every
        // other CDU window in this app does: a hidden window has nobody to read to, and a poll
        // that kept running after Escape spoke every page title, every MSG lamp and every
        // scratchpad change over the cockpit for as long as the window stayed closed.
        _pollTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _pollTimer.Tick += (s, e) => Poll();
    }

    private void SetupEventHandlers()
    {
        unitSelector.SelectedIndexChanged += (s, e) =>
        {
            // Leave the unit being switched AWAY from with its cursor remembered, and drop its
            // rows: CursorRow() reads the list's selection against _rows, so the next Render
            // would otherwise hand the OLD unit's row identity to the new unit as "where the
            // cursor was". The new unit's own remembered row is what Render restores instead.
            _memory.RememberCursor(_unit, CursorRow());
            _rows = null;
            _unit = (Md11McduUnit)unitSelector.SelectedIndex;
            // Re-render the newly selected unit at once rather than waiting for the next tick,
            // with the title announce suppressed: the screen reader already spoke the combo
            // change, and re-announcing the page title on top of it is exactly the
            // double-announce the panel rules forbid. ResyncToSelectedUnit adopts the new unit's
            // title silently so a LATER genuine page change still fires.
            ResyncToSelectedUnit();
        };

        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;
        mcduDisplay.KeyDown += McduDisplay_KeyDown;
        this.KeyDown += Form_KeyDown;
    }

    /// <summary>
    /// Adopts the selected unit's CURRENT screen as the baseline without speaking any of it, and
    /// replaces the list at once with what that unit shows: its page, its advisory, or — for a
    /// blank that has not settled yet — the waiting row (<see cref="Md11McduPresence.DecideOnResync"/>).
    /// The list never goes on showing the page the window was following a moment ago.
    ///
    /// Shared by the unit switch and by <see cref="ShowForm"/> — the two moments the window starts
    /// following a screen it was not following a moment ago. After a switch the cached page
    /// belongs to the PREVIOUS unit; after a re-show it is whatever was there when the poll
    /// stopped on hide. In both, the first thing spoken must be a change that happens AFTER this
    /// moment, never a replay of what changed in between: the screen reader reads the window and
    /// the focused row, and the page title is row 0.
    /// </summary>
    private void ResyncToSelectedUnit()
    {
        // _screen / _lastRendered are the page the window WAS following, not the one it is about
        // to read. Left in place on a unit switch, Render drew the previous unit's page under the
        // new unit's name and the next poll then announced the new unit's title on top of the
        // combo — the double-announce the unit switch exists to prevent.
        _screen = null;
        _lastRendered = null;

        // The scratchpad read-back is re-seeded SILENTLY on what this unit shows — Reset, then the
        // seed a first sample after Reset always is. Read against the previous unit's text, or
        // against what the pad held when the window was hidden, it would announce a change nobody
        // made now, or "Scratchpad cleared" for a scratchpad nobody cleared. Only a PAGE seeds it:
        // a blank or never-delivered unit is seeded by its first page, through Poll. The
        // annunciator flags likewise, or a unit whose MSG was already lit would announce "MSG" as
        // though it had just come on.
        var current = _sim.Md11McduDataManager?.GetScreen(_unit);
        _scratchpad.Reset();
        if (current != null && Md11McduPresence.Classify(current) == Md11McduPresenceState.Content)
            _scratchpad.OnPoll(current.Scratchpad.Trim(), DateTime.UtcNow);
        _lastAnnouncedFlags = current == null ? "" : FlagsOf(current);

        // Replace the list NOW with this unit's own state. Waiting for the next tick kept the
        // PREVIOUS unit's rows on screen under this unit's name — for up to the blank settle on a
        // unit caught mid-erase, since its blank clock runs only while the window polls — and a
        // first open onto a blank unit showed an empty list for as long. The clocks are ticked
        // first so a blank that has already settled is believed at once; one that has not gets
        // the waiting row, and Poll takes over from there (its HoldLastPage now holds the waiting
        // row). On a RE-SHOW nothing can be settled yet — ShowForm clears all three clocks before
        // calling this, because a clock stopped while the window was hidden cannot say how long a
        // unit has been blank. Still nothing is SPOKEN: every branch is silent, and a unit caught
        // mid-erase shows the waiting row, never a spurious "blank".
        var manager = _sim.Md11McduDataManager;
        if (manager != null) TickBlankClocks(manager);
        var presence = Md11McduPresence.Classify(current);
        var since = _blankSince[(int)_unit];
        var blankFor = since == null ? TimeSpan.Zero : DateTime.UtcNow - since.Value;

        switch (Md11McduPresence.DecideOnResync(presence, blankFor))
        {
            case Md11McduDisplayAction.ShowContent:
                Render(silentTitle: true);
                break;
            case Md11McduDisplayAction.ShowAdvisory:
                // A never-delivered unit or a settled blank: the rows Poll would show, at once.
                // Re-drawn identically by the next tick, and silent — the flags were re-baselined above.
                RenderAdvisory(presence, current);
                break;
            default:
                RenderWaiting();
                break;
        }
    }

    // ---------------------------------------------------------------------------------
    // Key actuation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Presses one MCDU key on the SELECTED unit. <paramref name="key"/> is the node-id suffix
    /// ("INIT", "LSK_3L", "A", "7", …); the unit prefix is applied here so callers never build a
    /// node id by hand.
    ///
    /// A press that cannot be delivered is SPOKEN, never swallowed — see PressControl. The pilot
    /// cannot see the screen, so a dropped key would otherwise look exactly like a working one.
    /// </summary>
    private bool PressKey(string key) => PressKey(_unit, key);

    /// <summary>
    /// <see cref="PressKey(string)"/> on a NAMED unit — for the scratchpad clear, which must keep
    /// pressing the unit it started on even if the selector moves while it runs. True when the
    /// press was queued.
    /// </summary>
    private bool PressKey(Md11McduUnit unit, string key)
    {
        if (_definition.PressControl(Md11McduKeys.NodeId(unit, key))) return true;
        _announcer.Announce($"{key} key unavailable");
        return false;
    }

    private void McduDisplay_KeyDown(object? sender, KeyEventArgs e)
    {
        // Backspace = a single CLR — delete one character, matching the hardware key (verified
        // live: one CLR press removes one character, and a held CLR removes only one too). Only
        // from the display, because the scratchpad box needs Backspace to edit its own text.
        if (e.KeyCode == Keys.Back)
        {
            // Holds the read-back for its own write like any burst, so a held Backspace (key
            // repeat) reads the settled scratchpad once instead of every character on the way.
            if (PressKey("CLR")) HoldScratchpadReadBack(1);
            e.Handled = true; e.SuppressKeyPress = true;
        }
        // Delete = clear the WHOLE scratchpad. Backspace is a single CLR (one character); Delete is
        // the accessible shortcut for "empty it", which the hardware has no single key for. Scoped
        // to the display for the same reason Backspace is: bound form-wide (KeyPreview), Delete
        // pressed inside the MCDU Input box never reached the TextBox — a forward-delete while
        // editing typed text fired the CLR loop at the aircraft's scratchpad and announced
        // "Scratchpad cleared" over the edit. The loop runs as a Task with a fault continuation so
        // anything it throws after its first await (a bus disposed by an aircraft switch mid-loop)
        // lands in debug.log instead of the unhandled-exception handler.
        else if (e.KeyCode == Keys.Delete)
        {
            ClearScratchpadAsync().ContinueWith(
                t => Log.Error("MD11", "Scratchpad clear faulted", t.Exception?.GetBaseException()),
                TaskContinuationOptions.OnlyOnFaulted);
            e.Handled = true; e.SuppressKeyPress = true;
        }
        // Plain Right Arrow pages forward, as on the FBW form — only while the screen has focus
        // (the scratchpad box needs Left/Right for its caret); Alt+Right still works window-wide.
        // Plain Left stays unbound on purpose: the MD-11 has no PREV PAGE key, and a ListBox
        // ignores Left, so nothing is faked.
        else if (e.KeyCode == Keys.Right && !e.Alt && !e.Control)
        {
            PressKey("NEXTPAGE");
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// Clears the whole MCDU scratchpad in one keystroke.
    ///
    /// The MD-11's CLR key deletes ONE character per press — there is no clear-all key, and a held
    /// CLR still removes only one (both verified live). So this fires CLR repeatedly, reading the
    /// exported scratchpad back between presses and STOPPING the instant it is empty. Reading back
    /// is what makes it safe for both cases: typed text (N characters → N presses) and a scratchpad
    /// MESSAGE (one press clears it), without over-deleting into whatever the FMS shows next.
    ///
    /// A clear that works says NOTHING itself: every press holds the scratchpad read-back, which
    /// speaks "Scratchpad cleared" once the pad has settled empty. It used to say so here too, and
    /// the read-back said it again — and it said it even when the presses ran out with text still
    /// there. What it does say is <see cref="Md11McduScratchpad.ClearVerdict"/>'s. It keeps
    /// pressing the unit it STARTED on, and a second Delete while it runs is ignored: two loops
    /// would each read the other's presses as their own progress and over-delete.
    /// </summary>
    private async Task ClearScratchpadAsync()
    {
        if (_clearing) return;
        var manager = _sim.Md11McduDataManager;
        if (manager == null) { _announcer.Announce("Not connected"); return; }

        _clearing = true;
        try
        {
            var unit = _unit;
            for (var presses = 0; ; presses++)
            {
                var screen = manager.GetScreen(unit);
                // A PAGE, read: a never-delivered unit, or a blank frame (a page change's erase
                // reads as an empty scratchpad that is not one), cannot say the pad is empty.
                var readable = Md11McduPresence.Classify(screen) == Md11McduPresenceState.Content;
                var empty = readable && string.IsNullOrWhiteSpace(screen!.Scratchpad);

                // Capped at the scratchpad width plus a margin (MaxClearPresses) — a backstop
                // against an unreadable feed, so a stuck read can never become an unbounded CLR
                // storm at the aircraft. The read above runs once more after the last press.
                if (!readable || empty || presses >= Md11McduScratchpad.MaxClearPresses)
                {
                    if (Md11McduScratchpad.ClearVerdict(readable, empty, presses) is { } verdict)
                        _announcer.Announce(verdict);
                    return;
                }

                if (!PressKey(unit, "CLR")) return;   // PressKey spoke "CLR key unavailable" — once, not per iteration
                HoldScratchpadReadBack(1);

                // Long enough for the press to register AND the CHANGED client-data delivery to land,
                // so the next iteration reads the post-delete scratchpad rather than the stale one.
                await Task.Delay(150);
                if (IsDisposed) return;   // an aircraft switch disposed the window mid-clear: say nothing more
            }
        }
        finally
        {
            _clearing = false;
        }
    }

    /// <summary>
    /// The window's form-wide keys. <c>KeyPreview</c> is on, so EVERY chord arrives here before the
    /// focused control sees it — which is why the decision itself lives in <see cref="KeyRouting"/>,
    /// where it can be read against the focused control and pinned by tests. This method only
    /// carries it out.
    /// </summary>
    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        var combo = ActiveControl as ComboBox;
        var focus = combo == null ? KeyRouting.ComboFocus.None
                  : combo.DroppedDown ? KeyRouting.ComboFocus.Dropped
                  : KeyRouting.ComboFocus.Closed;

        // The LSK layout is read on every press so a change in FMC Settings takes effect live,
        // matching every other CDU form in this app.
        var action = KeyRouting.Resolve(e.KeyCode, e.Alt, e.Control, e.Shift, focus,
            MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys);

        // Pass means the window wants nothing: leave Handled alone so the chord reaches the
        // focused control. Suppressing one here is what took Alt+Down away from the unit combo.
        if (action.Kind == KeyRouting.ActionKind.Pass) return;

        e.Handled = true; e.SuppressKeyPress = true;

        switch (action.Kind)
        {
            case KeyRouting.ActionKind.Press:
                PressKey(action.Key);
                break;

            // Close() runs the hide-on-close handler, which also hands focus back to the window
            // the pilot came from.
            case KeyRouting.ActionKind.Close:
                Close();
                break;

            // The unit is announced only when focus is elsewhere: on the unit combo itself the
            // screen reader already reads the combo's new value, and speaking it again is the
            // double announcement the panel rules forbid.
            case KeyRouting.ActionKind.SelectUnit:
                unitSelector.SelectedIndex = action.Unit;
                if (!unitSelector.Focused)
                    _announcer.Announce(unitSelector.SelectedItem?.ToString() ?? "");
                break;

            case KeyRouting.ActionKind.FocusScratchpad:
                scratchpadInput.Focus();
                break;

            case KeyRouting.ActionKind.FocusDisplay:
                mcduDisplay.Focus();
                break;
        }
    }

    /// <summary>
    /// The whole form-wide binding table, as a pure function of the chord, what kind of control has
    /// focus, and the LSK-layout setting.
    ///
    /// It is out of the handler because of the defect it exists to prevent. <c>KeyPreview</c> is on,
    /// so every chord reaches the form BEFORE the focused control — and the slew bindings copied
    /// from the FBW MCDU form (Alt+Up / Alt+Down, PageUp / PageDown) collide with a control no other
    /// CDU window in this app has: a combo box. Alt+Down is THE gesture for opening one, and with no
    /// guard the window answered it by slewing the aircraft's MCDU and suppressing the key, so a
    /// pilot who cannot reach for the mouse could not open the unit selector at all and got an
    /// aircraft command they never asked for. The form's own Escape branch had always guarded on
    /// <c>ActiveControl is ComboBox { DroppedDown: true }</c> — a state Alt+Down could no longer
    /// produce, which is the corroborating evidence.
    ///
    /// Two rules, and every binding below obeys both:
    ///
    ///   * A chord the FOCUSED control needs for itself is never taken (<see cref="ComboBoxNeedsKey"/>).
    ///   * A binding matches its OWN chord and nothing more. Ctrl+PageDown used to slew, and on a
    ///     layout where AltGr arrives as Ctrl+Alt every Alt binding was reachable from a character
    ///     key. An unlisted modifier now falls through to <see cref="ActionKind.Pass"/>.
    ///
    /// Only the combo needs the first rule. The scratchpad is a single-line TextBox and uses none of
    /// the chords bound here — Backspace and Delete are deliberately bound on the DISPLAY only (see
    /// <see cref="McduDisplay_KeyDown"/>), and Shift+F10, the context menu a screen-reader user
    /// opens its edit commands with, is excluded by the second rule. The display list keeps its own
    /// arrows and type-ahead; PageUp / PageDown slewing the MCDU rather than paging that list is
    /// this window's deliberate binding, unchanged here.
    /// </summary>
    public static class KeyRouting
    {
        /// <summary>Where the chord found the unit combo.</summary>
        public enum ComboFocus
        {
            /// <summary>Focus is elsewhere — the display, the scratchpad, the status box, a button.</summary>
            None,
            /// <summary>The combo has focus, list closed.</summary>
            Closed,
            /// <summary>The combo has focus with its list dropped down.</summary>
            Dropped,
        }

        public enum ActionKind
        {
            /// <summary>The window wants nothing: the chord reaches the focused control untouched.</summary>
            Pass,
            Press,
            Close,
            SelectUnit,
            FocusScratchpad,
            FocusDisplay,
        }

        /// <summary>
        /// One decision. <see cref="Key"/> is an MCDU node-id SUFFIX ("UP", "LSK_3L") for
        /// <see cref="ActionKind.Press"/>; <see cref="Unit"/> is the selector index for
        /// <see cref="ActionKind.SelectUnit"/>. Both are unset otherwise.
        /// </summary>
        public readonly record struct Action(ActionKind Kind, string Key = "", int Unit = -1)
        {
            public static readonly Action Pass = new(ActionKind.Pass);
            public static readonly Action Close = new(ActionKind.Close);
            public static readonly Action FocusScratchpad = new(ActionKind.FocusScratchpad);
            public static readonly Action FocusDisplay = new(ActionKind.FocusDisplay);
            public static Action Press(string key) => new(ActionKind.Press, key);
            public static Action SelectUnit(Md11McduUnit unit) => new(ActionKind.SelectUnit, Unit: (int)unit);
        }

        /// <summary>
        /// What the window does with one chord. Order matters and is the order a pilot expects: what
        /// the focused control needs first, then the window's own keys in the order they were bound.
        /// </summary>
        public static Action Resolve(Keys keyCode, bool alt, bool control, bool shift,
                                     ComboFocus combo, bool useAlternateLskKeys)
        {
            // Rule one. This covers Escape on a dropped list too, which is what the old inline
            // guard did on its own.
            if (combo != ComboFocus.None
                && ComboBoxNeedsKey(keyCode, alt, control, shift, droppedDown: combo == ComboFocus.Dropped))
                return Action.Pass;

            // Escape closes the window, as on the FCP window and the A380 MCDU / iFly CDU windows.
            if (keyCode == Keys.Escape && NoModifier(alt, control, shift)) return Action.Close;

            // Line-select keys — two layouts, switchable in FMC Settings:
            //   Default:   Ctrl+1..6 = L1..L6, Alt+1..6 = R1..R6
            //   Alternate: F1..F6    = L1..L6, F7..F12  = R1..R6
            if (useAlternateLskKeys)
            {
                if (NoModifier(alt, control, shift) && keyCode >= Keys.F1 && keyCode <= Keys.F6)
                    return Action.Press(Md11McduKeys.Lsk(keyCode - Keys.F1 + 1, right: false));
                if (NoModifier(alt, control, shift) && keyCode >= Keys.F7 && keyCode <= Keys.F12)
                    return Action.Press(Md11McduKeys.Lsk(keyCode - Keys.F7 + 1, right: true));
            }
            else
            {
                if (control && !alt && !shift && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                    return Action.Press(Md11McduKeys.Lsk(keyCode - Keys.D1 + 1, right: false));
                if (alt && !control && !shift && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                    return Action.Press(Md11McduKeys.Lsk(keyCode - Keys.D1 + 1, right: true));
            }

            // Slew. The MD-11 has UP/DOWN slew keys and a single NEXT PAGE key — there is no PREV
            // PAGE on this aircraft, so Alt+Left is deliberately unbound rather than faked. Plain
            // PageUp / PageDown stay the slew and must never move behind a modifier: every CDU
            // window in this app binds the unmodified page keys to the content being read.
            if (NoModifier(alt, control, shift))
            {
                if (keyCode == Keys.PageUp) return Action.Press("UP");
                if (keyCode == Keys.PageDown) return Action.Press("DOWN");
            }

            if (AltOnly(alt, control, shift))
            {
                switch (keyCode)
                {
                    case Keys.Up: return Action.Press("UP");
                    case Keys.Down: return Action.Press("DOWN");
                    case Keys.Right: return Action.Press("NEXTPAGE");
                    // Alt+S = focus scratchpad, Alt+Home = focus display.
                    case Keys.S: return Action.FocusScratchpad;
                    case Keys.Home: return Action.FocusDisplay;
                }
            }

            // Alt+Shift+F = SEC FPLN (Alt+F is Fpln) — same chord as the Fenix/FBW forms.
            if (alt && shift && !control && keyCode == Keys.F) return Action.Press("SEC_FPLN");

            // Ctrl+Shift+L/C/R — switch unit without leaving the keyboard. Deliberately still live
            // while the combo itself has focus: it is not one of the list's own gestures, and a
            // pilot on the selector pressing it is asking for exactly what it does.
            if (control && shift && !alt)
            {
                switch (keyCode)
                {
                    case Keys.L: return Action.SelectUnit(Md11McduUnit.Left);
                    case Keys.C: return Action.SelectUnit(Md11McduUnit.Center);
                    case Keys.R: return Action.SelectUnit(Md11McduUnit.Right);
                }
            }

            return Action.Pass;
        }

        /// <summary>
        /// True when a focused combo box needs this chord for its own list, so the window must not
        /// take it.
        ///
        /// This is the DropDownList keyboard interface in full, not only the chords bound today, so
        /// that a binding added later is checked against it automatically. Alt+Down / Alt+Up open
        /// and close the list and are what the defect above cost; F4 is the other open/close gesture
        /// (and is L4 in the alternate LSK layout, the one F-key that must not press an LSK from
        /// here); PageUp / PageDown, Home / End and the arrows move the selection; a character jumps
        /// to a matching item; and Escape closes a DROPPED list — on a closed one it belongs to the
        /// window, which is what the form's original inline guard said.
        /// </summary>
        public static bool ComboBoxNeedsKey(Keys keyCode, bool alt, bool control, bool shift, bool droppedDown)
        {
            if (control) return false;                                  // no list gesture carries Ctrl
            if (alt) return !shift && keyCode is Keys.Up or Keys.Down;  // open / close the list
            if (IsTypeAhead(keyCode)) return true;                      // jumps to an item, shifted or not
            if (shift) return false;

            return keyCode switch
            {
                Keys.F4 => true,
                Keys.PageUp or Keys.PageDown => true,
                Keys.Home or Keys.End => true,
                Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
                Keys.Space => true,
                Keys.Return or Keys.Escape => droppedDown,
                _ => false,
            };
        }

        /// <summary>A key that types a character, which a combo box list uses to jump to an item.</summary>
        private static bool IsTypeAhead(Keys keyCode) =>
            (keyCode >= Keys.A && keyCode <= Keys.Z)
            || (keyCode >= Keys.D0 && keyCode <= Keys.D9)
            || (keyCode >= Keys.NumPad0 && keyCode <= Keys.NumPad9);

        private static bool NoModifier(bool alt, bool control, bool shift) => !alt && !control && !shift;

        private static bool AltOnly(bool alt, bool control, bool shift) => alt && !control && !shift;
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return) return;

        // A refused entry stays in the box: the refusal names the character to remove, or the key
        // that cannot be pressed, and the pilot edits and presses Enter again rather than retyping
        // the whole line.
        if (SendTextToMcdu(scratchpadInput.Text.ToUpperInvariant()))
            scratchpadInput.Clear();
        e.Handled = true; e.SuppressKeyPress = true;
    }

    /// <summary>
    /// Types a string into the scratchpad, one key at a time — the MCDU has no "set text" input.
    /// Returns false, having pressed NOTHING, when the entry holds a character the MCDU keyboard
    /// lacks, or a key that cannot be delivered right now (TFDiMD11Definition.CanPress) — ONE
    /// sentence either way, and the entry stays in the box.
    ///
    /// The whole entry is validated first (Md11McduKeys.RefusalFor), for the same reason PressKey
    /// speaks an undeliverable press: skipping the one character and sending the rest would put
    /// "N123" or "KJFKKLAX" in the scratchpad with nothing spoken, and the pilot cannot see that
    /// it is not what they typed. Refusing whole and naming the character lets them fix the text.
    ///
    /// No delay here on purpose: Md11EventBus owns pacing (it is a single shared CEVENT slot the
    /// aircraft itself also uses) and serializes every press through one queue. A second pacing
    /// layer in the form would just make typing slower without making it safer. The scratchpad is
    /// 24 columns, so a full line is well inside the bus's queue bound.
    /// </summary>
    private bool SendTextToMcdu(string text)
    {
        var refusal = Md11McduKeys.RefusalFor(text);
        if (refusal != null)
        {
            Log.Debug("MD11", $"MCDU scratchpad entry refused, untypeable '{Md11McduKeys.UntypeableCharacters(text)}' in \"{text}\"");
            _announcer.Announce(refusal);
            return false;
        }

        // Every key is checked BEFORE the first is pressed. Key by key, a key that could not be
        // delivered spoke "{key} key unavailable" while the rest of the entry went in — and the
        // caller then cleared the box, so the pilot was left to retype a line they could not see
        // had gone in wrong.
        var unit = _unit;
        var undeliverable = Md11McduKeys.FirstUndeliverableKey(text, key => _definition.CanPress(Md11McduKeys.NodeId(unit, key)));
        if (undeliverable != null)
        {
            Log.Debug("MD11", $"MCDU scratchpad entry refused, {undeliverable} key cannot be pressed on the {unit} MCDU: \"{text}\"");
            _announcer.Announce(Md11McduKeys.UndeliverableRefusal(undeliverable));
            return false;
        }

        // Hold the read-back until the whole entry has been written, so it is read once, settled,
        // and no half-typed prefix of it is.
        HoldScratchpadReadBack(text.Length);
        foreach (char c in text)
            PressKey(unit, Md11McduKeys.ForChar(c)!);
        return true;
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    private void Poll()
    {
        var manager = _sim.Md11McduDataManager;
        if (manager == null)
        {
            statusBox.Text = "MCDU: not connected";
            return;
        }

        TickBlankClocks(manager);   // every unit's clock, ahead of the current unit's own judgement and its no-data return
        var screen = manager.GetScreen(_unit);
        if (screen == null)
        {
            // Nothing has ever been delivered for this unit. Show the no-data advisory rather
            // than an EMPTY list: Md11McduPresence has always had a row for this state, but this
            // early return used to skip Render and leave the pilot arrowing through nothing —
            // which is what an in-flight start looked like before the manager's start-up
            // snapshot, and what a feed that is genuinely absent still looks like.
            RenderAdvisory(Md11McduPresenceState.NoData, screen: null);
            return;
        }

        // The blank judgement has to run BEFORE the reference shortcut below. A unit that stays
        // blank delivers ONE frame and then nothing more (the request is CHANGED-only), so the
        // "has it stayed blank?" question can only be answered by this timer tick, never by a
        // delivery. Judging it after the shortcut would mean a persistent blank was never
        // reported at all.
        var presence = Md11McduPresence.Classify(screen);
        var since = _blankSince[(int)_unit];
        var blankFor = since == null ? TimeSpan.Zero : DateTime.UtcNow - since.Value;

        // Hold the page through a repaint: draw nothing, say nothing, leave the list and the
        // screen-reader cursor exactly where they are.
        if (Md11McduPresence.Decide(presence, blankFor) == Md11McduDisplayAction.HoldLastPage) return;

        // The manager only builds a new screen object when the sim delivers, and the underlying
        // request is CHANGED-only — so an unchanged reference means nothing happened.
        if (!ReferenceEquals(screen, _lastRendered))
        {
            _screen = screen;
            Render(silentTitle: false);
        }

        // The scratchpad read-back runs on EVERY tick that shows a page, not behind the reference
        // shortcut above: a typing or CLR hold can end on a tick that delivered nothing, and the
        // settled text must still be read then (CduScratchpadAnnouncer). After Render, so a page
        // change's title is still spoken before the scratchpad, as it always was. A page only: an
        // erase frame's empty scratchpad is a repaint (held above), and a settled blank reaches
        // the pilot as the advisory row, never as "Scratchpad cleared".
        if (presence == Md11McduPresenceState.Content) AnnounceScratchpad(screen);
    }

    private void Render(bool silentTitle)
    {
        var manager = _sim.Md11McduDataManager;
        var screen = _screen ?? manager?.GetScreen(_unit);
        if (screen == null) return;

        _screen = screen;
        _lastRendered = screen;

        // A unit with nothing on it renders as ONE informative row, not as "Title:", "1:" ... "6:",
        // "Scratchpad:" over a status line reading "connected". Those eight blank rows plus that
        // reassuring status are how a working feed came to be reported as "my mcdu is not showing
        // up in the app" — the window is indistinguishable from a broken one, and the usual
        // announce is gated on a non-empty title so nothing is said either.
        var presence = Md11McduPresence.Classify(screen);
        if (presence != Md11McduPresenceState.Content)
        {
            // Reached only for a SETTLED blank — Poll holds every transient repaint before it
            // gets here, and shows the never-delivered case itself.
            RenderAdvisory(presence, screen);
            return;
        }

        // The rows are built with their identities (Md11McduRows), because the list is not
        // positionally stable: blank label rows are dropped, so "6:" is a different item on the
        // F-PLN page than on the MENU page. The cursor is remembered as the ROW it was on and put
        // back on that row below — never by index, which handed a pilot reading line 6 the
        // scratchpad after a few slews.
        var rows = Md11McduRows.Build(screen);
        var cursor = CursorRow() ?? _memory.Cursor(_unit);   // content back after an advisory or a unit switch: this unit's own last row

        // Shared in-place reconcile. Its content-based restore cannot follow an LSK line (the
        // number is part of the text, so a slewed line is a different string); this form's own
        // row restore runs below and overrides it.
        mcduDisplay.SetLines(rows.Select(r => r.Text).ToList());
        _rows = rows;

        UpdateStatus(screen);

        var title = screen.Title.Trim();
        // Judged against THIS unit's own last title: a switch to another unit and back is not a
        // page change on either of them, and each keeps the line its pilot was reading.
        var change = _memory.Adopt(_unit, title);
        if (change.TitleChanged)
        {
            // Two decisions, not one. A changed title is ANNOUNCED — the pilot cannot see that
            // the key worked. But the cursor goes to line 1 only when the PAGE changed: MD-11
            // titles carry their page counter ("ACT F-PLN     1/2"), and a slew across a page
            // boundary changes the text while the pilot is still reading the same page.
            if (!silentTitle) _announcer.Announce(title);

            if (change.PageChanged)
            {
                SelectPageStart();
            }
            else
            {
                RestoreCursor(cursor);
            }
        }
        else
        {
            RestoreCursor(cursor);
        }

        // Never leave the list with nothing selected (a first-ever render of a frame whose title
        // row is empty adopts no title and restores no cursor): the screen reader would announce
        // an empty list and Space/Enter would act on nothing. Line 1, as a page change lands.
        if (mcduDisplay.SelectedIndex < 0) SelectPageStart();
    }

    /// <summary>
    /// Feeds the scratchpad read-back one sample and speaks what it returns: the settled text, or
    /// <see cref="Md11McduScratchpad.ClearedText"/> for an emptied pad. Nothing for its silent
    /// first sample, an unchanged pad, or a change inside a hold (<see cref="HoldScratchpadReadBack"/>).
    /// </summary>
    private void AnnounceScratchpad(Md11McduScreen screen)
    {
        if (_scratchpad.OnPoll(screen.Scratchpad.Trim(), DateTime.UtcNow) is { } say)
            _announcer.Announce(say);
    }

    /// <summary>
    /// Holds the scratchpad read-back until <paramref name="keyPresses"/> more keys have been
    /// written and have had time to land (<see cref="Md11McduScratchpad.HoldUntil"/>: extends a
    /// hold still running, never shortens one), so a burst is read once, settled, and none of its
    /// intermediate text is.
    /// </summary>
    private void HoldScratchpadReadBack(int keyPresses) =>
        _scratchpad.SuppressUntil = Md11McduScratchpad.HoldUntil(_scratchpad.SuppressUntil, DateTime.UtcNow, keyPresses);

    /// <summary>
    /// Advances every unit's blank clock from its latest frame: started when the unit reads blank,
    /// cleared when it has content or has never delivered. Three classifications of cached objects
    /// per 250 ms tick — the same three RenderAdvisory already walks.
    /// </summary>
    private void TickBlankClocks(Md11McduDataManager manager)
    {
        var now = DateTime.UtcNow;
        foreach (var u in new[] { Md11McduUnit.Left, Md11McduUnit.Center, Md11McduUnit.Right })
        {
            var s = manager.GetScreen(u);
            if (s != null && Md11McduPresence.Classify(s) == Md11McduPresenceState.Blank) _blankSince[(int)u] ??= now;
            else _blankSince[(int)u] = null;
        }
    }

    /// <summary>The row the cursor is on, or null when the list shows an advisory or nothing is selected.</summary>
    private Md11McduRow? CursorRow()
    {
        int i = mcduDisplay.SelectedIndex;
        return _rows != null && i >= 0 && i < _rows.Count ? _rows[i] : null;
    }

    /// <summary>
    /// Puts the cursor on line 1 — its VALUE row, by identity (<see cref="Md11McduRows.PageStart"/>),
    /// never list item 1, which on most pages is line 1's label.
    /// </summary>
    private void SelectPageStart()
    {
        if (_rows == null) return;
        int index = Md11McduRows.PageStart(_rows);
        if (index >= 0 && index < mcduDisplay.Items.Count && mcduDisplay.SelectedIndex != index)
            mcduDisplay.SelectedIndex = index;
    }

    /// <summary>Puts the cursor back on the row it was on before the redraw, if that row still exists.</summary>
    private void RestoreCursor(Md11McduRow? previous)
    {
        if (_rows == null) return;
        int index = Md11McduRows.Restore(_rows, previous);
        if (index >= 0 && index < mcduDisplay.Items.Count && mcduDisplay.SelectedIndex != index)
            mcduDisplay.SelectedIndex = index;
    }

    /// <summary>
    /// Replaces the list with the ONE waiting row (<see cref="Md11McduPresence.Waiting"/>): the
    /// resync's answer for a unit whose blank has not settled — shown instead of the page the
    /// window was following before, which belongs to another unit, or (on a first open) instead
    /// of an empty list. Poll replaces it with the unit's page when one arrives, or with the
    /// blank advisory once the blank settles. Nothing is spoken: the row is read when focus is on
    /// the list, the channel the advisory rows use.
    /// </summary>
    private void RenderWaiting()
    {
        if (_rows != null) _memory.RememberCursor(_unit, CursorRow());   // a re-show keeps this unit's own row remembered
        mcduDisplay.SetLines(new[] { Md11McduPresence.Waiting(_unit) });
        _rows = null;
        if (mcduDisplay.SelectedIndex < 0 && mcduDisplay.Items.Count > 0) mcduDisplay.SelectedIndex = 0;
        statusBox.Text = $"MCDU: {_unit} waiting";
    }

    /// <summary>
    /// Replaces the page with the blank / no-data advisory rows.
    ///
    /// <paramref name="screen"/> is null only for <see cref="Md11McduPresenceState.NoData"/> —
    /// nothing has been delivered, so there are no annunciator flags to show either.
    /// </summary>
    private void RenderAdvisory(Md11McduPresenceState presence, Md11McduScreen? screen)
    {
        var manager = _sim.Md11McduDataManager;
        var withContent = new List<Md11McduUnit>(3);
        foreach (var u in new[] { Md11McduUnit.Left, Md11McduUnit.Center, Md11McduUnit.Right })
            if (Md11McduPresence.Classify(manager?.GetScreen(u)) == Md11McduPresenceState.Content)
                withContent.Add(u);

        var advisory = Md11McduPresence.Describe(_unit, presence, withContent).ToList();
        if (_rows != null) _memory.RememberCursor(_unit, CursorRow());   // only on the way OVER, never on a later tick
        mcduDisplay.SetLines(advisory);
        _rows = null;

        // The advisory must be UNDER the cursor, not one Down-press away: a list populated with
        // nothing selected makes the screen reader announce an empty list, and the one row that
        // explains the situation stays unheard until the pilot presses Down.
        if (mcduDisplay.SelectedIndex < 0 && mcduDisplay.Items.Count > 0) mcduDisplay.SelectedIndex = 0;

        // UpdateStatus owns the MSG/FAIL announcement and WRITES statusBox, so it runs
        // BEFORE the status is set here — reversed, it would overwrite "blank" with
        // "connected", which is the exact false reassurance this branch exists to remove.
        if (screen != null) UpdateStatus(screen);
        statusBox.Text = presence == Md11McduPresenceState.Blank
            ? $"MCDU: {_unit} blank"
            : $"MCDU: {_unit} no data";

        // NOTHING is spoken here, deliberately. The advisory is a LIST ROW: the screen reader
        // reads it when the pilot's focus is on the display, which is the same channel the
        // monitor manager uses for its filter state. Speaking it instead put "MCDU is blank"
        // over the cockpit on every repaint.
        //
        // The title latch is deliberately NOT cleared either. Clearing it made the page title
        // re-announce every time content came back — the "then something there" half of the
        // spam. Left alone, a CDU that goes dark and returns to the SAME page stays silent
        // (nothing changed for the pilot) while a return to a DIFFERENT page announces itself
        // through the ordinary title-change path.
    }

    /// <summary>
    /// The four MCDU annunciators come from the export's own flags, not from the four *_LT lamp
    /// L:vars — same fact, one fewer data definition each, and always in step with the text they
    /// arrived with.
    ///
    /// MSG lighting up is a real event with NO text change behind it, so it is announced rather
    /// than only shown: it is how the FMS says "read the scratchpad", and a blind pilot has no
    /// other way to notice it.
    /// </summary>
    private void UpdateStatus(Md11McduScreen screen)
    {
        var lit = LitFlags(screen);
        var flags = string.Join(", ", lit);
        statusBox.Text = lit.Count == 0 ? "MCDU: connected" : "MCDU: " + flags;

        if (flags != _lastAnnouncedFlags)
        {
            // Announce only what came ON. A lamp going out is not something a pilot needs told.
            var previous = _lastAnnouncedFlags;
            _lastAnnouncedFlags = flags;
            var added = lit.Where(f => !previous.Contains(f, StringComparison.Ordinal)).ToList();
            if (added.Count > 0) _announcer.Announce(string.Join(", ", added));
        }
    }

    private static List<string> LitFlags(Md11McduScreen screen)
    {
        var lit = new List<string>(4);
        if (screen.Msg) lit.Add("MSG");
        if (screen.Fail) lit.Add("FAIL");
        if (screen.Dspy) lit.Add("DSPY");
        if (screen.Ofst) lit.Add("OFST");
        return lit;
    }

    /// <summary>The annunciator string UpdateStatus compares against — what a unit switch re-baselines to.</summary>
    private static string FlagsOf(Md11McduScreen screen) => string.Join(", ", LitFlags(screen));

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

    public void ShowForm()
    {
        previousWindow = GetForegroundWindow();

        // Only when the window was actually hidden. Shift+M on an already-open window is a
        // re-show: it is following the feed already, so re-syncing there would re-seed the
        // scratchpad read-back and swallow the settled entry the pilot's own typing is waiting to
        // hear, and would log a "poll started" for a poll that never stopped.
        if (!Visible)
        {
            // Every unit's blank clock restarts with the poll, BEFORE the re-sync reads them.
            // TickBlankClocks stamps a start only where it finds none (??=), so a re-show used to
            // inherit the start from before the hide — and a unit that was mid-erase at the last
            // pre-hide tick and is mid-erase again NOW then read as a SETTLED blank: the re-sync
            // put "… MCDU is blank - nothing on this screen." under the cursor, ShowForm focused
            // the list, and the screen reader read that false sentence aloud on activation, with
            // the real page overwriting it 0.2-0.4 s later. The clock measures how long a WATCHED
            // unit has stayed blank, and while the window was hidden nothing was watching.
            Array.Clear(_blankSince);

            // Catch up on whatever changed while the window was hidden WITHOUT speaking it, and
            // only then start following the feed. The poll was stopped on hide
            // (OnVisibleChanged), so the page the pilot missed is read from the list under their
            // cursor, not replayed as announcements over the screen reader's own read.
            var before = _memory.LastTitle(_unit);
            ResyncToSelectedUnit();
            _pollTimer?.Start();
            Log.Debug("MD11", $"MCDU window shown: poll started ({_unit})");

            // Which page this IS is information the pilot had no other way to get: the re-sync is
            // silent by design, and a first open — or a re-show onto a page the FMS moved to while
            // the window was closed — would otherwise land the cursor on an arbitrary content
            // line. Put it on the title row instead, so the screen reader's own read of the
            // focused row names the page. No app announcement: a same-page re-show keeps the
            // row-identity restore and puts the pilot back on the line they left.
            if (_rows != null
                && !Md11McduTitle.SamePage(before, _memory.LastTitle(_unit))
                && mcduDisplay.Items.Count > 0)
            {
                mcduDisplay.SelectedIndex = 0;
            }
        }

        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        this.ActiveControl = mcduDisplay;
        mcduDisplay.Focus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) return;

        // Nothing to read to while hidden, so the poll stops — and with it the scratchpad
        // read-back, which runs only from the poll and has no timer of its own left to fire after
        // the hide (the debounce it replaced did, and spoke to a window nobody had open). ShowForm
        // restarts the poll after re-syncing silently, so nothing that changed in between is
        // replayed.
        _pollTimer?.Stop();
        Log.Debug("MD11", "MCDU window hidden: poll stopped");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Close() is cancelled by the hide-on-close guard above, so OnFormClosed never runs —
            // teardown has to live here or the poll timer outlives the aircraft switch.
            _pollTimer?.Stop();
            _pollTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
