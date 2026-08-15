// AccessGSXForm — accessible non-modal UI for the GsxService.
// Mirrors the GSX in-sim menu/tooltip, exposes F5 (open menu) and 0..9 / A..E
// (choose option) keyboard shortcuts. Designed for NVDA/JAWS: plain Label
// + two read-only multiline TextBoxes — the screen reader reads each block
// in one pass when its content refreshes, matching the AccessGSX upstream UX.
//
// Lifecycle: this form is constructed once in MainForm and Hidden (not Closed)
// when the user dismisses it, so the underlying GsxService keeps running for
// background tooltip announcements. Dispose unsubscribes the service events.
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx.Remote;
using MSFSBlindAssist.Utils.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Forms;

public sealed class AccessGSXForm : Form
{
    private readonly GsxService _gsxService;
    private readonly ScreenReaderAnnouncer _announcer;

    // Match AccessGSX upstream prompts so the menu textbox always has something
    // useful in it — never blank — and the user gets reopen instructions
    // without us having to spell them out in an AccessibleDescription.
    private const string MENU_HIDDEN_PROMPT = "GSX Menu hidden. Press F5 to open it.";

    private TextBox _statusTextBox = null!;
    private DisplayListBox _menuList = null!;
    private TextBox _tooltipTextBox = null!;
    private ComboBox _activeServicesCombo = null!;
    private Label _activeServicesLabel = null!;
    private bool _suppressActiveServicesSelectionEvent;
    private GsxSettingsForm? _settingsForm;

    // When the pilot last pressed 'C' (Settings) — the ONLY thing allowed to
    // CREATE the settings window. Under the Remote API, GsxService raises
    // SettingsChanged on every Hello and Snapshot frame (every connect and
    // reconnect — including after the 'D' Restart GSX command, which drops
    // and re-establishes the socket) and on every unprompted /settings patch,
    // not just in answer to OpenSettings(). The old transport raised it only
    // from the pilot's explicit settings.get flow, which is why the create+show
    // path never needed a "did the pilot ask?" gate. Without one, any reconnect
    // after Access GSX has been opened once (hide-not-close keeps these
    // subscriptions alive) pops the settings window open uninvited, its
    // Activate() steals screen-reader focus mid-flight, and dismissing it
    // fires FormClosed -> HideMenu as a side effect. Null when no request is
    // outstanding; consumed (nulled) by the frame that opens the window.
    private DateTime? _settingsRequestedUtc;

    // How long a 'C' press stays a valid reason to open the window when the
    // "settings" response arrives. GSX answers settings.get within a frame
    // or two on a local socket; 10 s is generous cover for a slow Couatl
    // without letting a press from minutes ago be claimed by an unrelated
    // reconnect. (TimeSpan cannot be a C# const — static readonly is the
    // closest equivalent.)
    private static readonly TimeSpan SettingsRequestWindow = TimeSpan.FromSeconds(10);

    // The menu snapshot last rendered/announced by RepopulateMenu. What this
    // genuinely buys is the menu-CLOSED case: DispatchPatch's "menuShown" clears
    // _menuOptions but never reassigns GsxService.Menu, so a stray digit after
    // the menu goes away would otherwise still resolve against the live model.
    // OnMenuHiddenUi resets this instead. It is NOT protection against a menu
    // changing mid-read: GsxService reposts to the UI thread before touching
    // fields, and every Menu reassignment synchronously fires MenuChanged ->
    // RepopulateMenu -> _renderedMenu = menu, so the snapshot and the live menu
    // move in lockstep. Freezing until acted on would need "frozen" semantics in
    // GsxMenuModel/GsxService and carries the opposite risk (silently refusing
    // legitimate presses after a normal menu change) — a design decision, not a
    // mechanical fix.
    private GsxMenuModel _renderedMenu = GsxMenuModel.Empty;

    public AccessGSXForm(GsxService gsxService, ScreenReaderAnnouncer announcer)
    {
        _gsxService = gsxService ?? throw new ArgumentNullException(nameof(gsxService));
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));

        BuildUi();
        WireEvents();

        // Initial render reflects whatever the service already knows about.
        UpdateStatus();
        RepopulateMenu();
        UpdateTooltip();
        OnActiveServicesChangedUi();
    }

    private void BuildUi()
    {
        Text = "Access GSX";
        // Center on the screen so the form doesn't anchor to MainForm —
        // we open it ownerless, so it can be alt-tabbed independently.
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(600, 500);
        MinimumSize = new Size(480, 360);
        KeyPreview = true;
        // Independent taskbar entry so alt-tab between MainForm and the GSX
        // window works naturally. Without this, the GSX form is awkwardly
        // tethered to MainForm in z-order.
        ShowInTaskbar = true;

        // Read-only single-line TextBox (not a Label) so screen readers
        // treat status as a focusable, value-bearing field — matches the
        // upstream AccessGSX UX. Tab reaches it; NVDA/JAWS read the current
        // status on focus. A plain Label has no tab stop and is announced
        // only as adjacent context, which made the status invisible to many
        // screen-reader users.
        _statusTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            Height = 26,
            ReadOnly = true,
            Text = "Status: Disconnected",
            AccessibleName = "GSX status"
        };

        // Navigable list: each GSX menu row is its own accessible item, so
        // arrow keys read one row at a time and the reconcile-in-place
        // update (DisplayListBox) keeps the reading row put across a
        // MenuChanged refresh instead of yanking focus back to the top.
        // The keyboard shortcuts below (1..9, 0, A..E) pick options directly
        // — those same character keys are menu-selection INPUT here, not
        // list navigation, so SuppressTypeAhead stops the native ListBox
        // incremental-search from hijacking them and moving the reading row
        // out from under the user (mirrors FBWA380RmpForm's screen list).
        _menuList = new DisplayListBox
        {
            Dock = DockStyle.Fill,
            SuppressTypeAhead = true,
            AccessibleName = "GSX menu"
            // No AccessibleDescription — the list content itself always
            // contains an actionable prompt ("Press F5 to open it"), so a
            // separate hint would be redundant noise for screen readers.
        };
        _menuList.SetText(MENU_HIDDEN_PROMPT);

        var menuLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(8, 4, 8, 0),
            Text = "&Menu options:",
            AccessibleName = "Menu options label"
        };

        var tooltipLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(8, 4, 8, 0),
            Text = "&Tooltip:",
            AccessibleName = "Tooltip label"
        };

        _tooltipTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "GSX tooltip"
        };

        // Active-services selector — hidden when GSX has zero or one active
        // operation (avoids cluttering tab order for the common case), shown
        // when two or more are running concurrently so the user can pick
        // which one drives the tooltip + auto-announce.
        _activeServicesLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(8, 4, 8, 0),
            Text = "Active &services:",
            AccessibleName = "Active services label",
            Visible = false
        };

        _activeServicesCombo = new ComboBox
        {
            Dock = DockStyle.Top,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Active services",
            Visible = false
        };
        _activeServicesCombo.SelectedIndexChanged += OnActiveServicesComboChanged;

        // Layout: status (top), menu list (center, fills), tooltip (bottom panel).
        // Use a TableLayoutPanel for predictable 60/40 split between menu and tooltip.
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));

        var menuPanel = new Panel { Dock = DockStyle.Fill };
        menuPanel.Controls.Add(_menuList);
        menuPanel.Controls.Add(menuLabel);

        var tooltipPanel = new Panel { Dock = DockStyle.Fill };
        // Stack order (later additions sit higher when docked Top): textbox
        // fills, then tooltip label, then services combo + label above.
        // The two services controls start hidden — they take zero space
        // until ActiveServicesChanged makes them visible.
        tooltipPanel.Controls.Add(_tooltipTextBox);
        tooltipPanel.Controls.Add(tooltipLabel);
        tooltipPanel.Controls.Add(_activeServicesCombo);
        tooltipPanel.Controls.Add(_activeServicesLabel);

        rootLayout.Controls.Add(menuPanel, 0, 0);
        rootLayout.Controls.Add(tooltipPanel, 0, 1);

        Controls.Add(rootLayout);
        Controls.Add(_statusTextBox);

        // KeyPreview = true above routes every keystroke through the form's
        // KeyDown event before the focused control sees it. Subscribing the
        // child TextBoxes too would invoke the same handler a second time
        // (KeyPreview only previews; the focused control still receives the
        // event), causing F5 / number / letter chooses to fire twice.
        KeyDown += AccessGSXForm_KeyDown;
    }

    private void WireEvents()
    {
        _gsxService.StateChanged += OnStateChanged;
        _gsxService.MenuChanged += OnMenuChanged;
        _gsxService.MenuHidden += OnMenuHidden;
        _gsxService.TooltipChanged += OnTooltipChanged;
        _gsxService.AnnouncementReady += OnAnnouncementReady;
        _gsxService.ActiveServicesChanged += OnActiveServicesChanged;
        _gsxService.SettingsChanged += OnSettingsChanged;

        // Hide-not-close — same pattern as HS787FMCForm. Keeps the service
        // subscriptions live so background tooltip announcements still work
        // after the user dismisses the window.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        VisibleChanged += (_, _) =>
        {
            // Form visible → form's own TooltipChanged handler announces;
            //   the service must stay silent to avoid double-speaking.
            // Form hidden → respect the user's "Announce GSX tooltips in
            //   background" setting. If unchecked, the service stays silent
            //   even though the form isn't driving speech anymore.
            // Reading the saved setting here (rather than just !Visible) is
            // what makes the in-flight Hide() path honour the toggle —
            // MainForm sets the initial value but only this handler keeps
            // it correct across show/hide cycles.
            _gsxService.AnnounceWhenFormHidden = !Visible
                && MSFSBlindAssist.Settings.SettingsManager.Current.GsxBackgroundMonitoring;
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Keyboard.
    // ─────────────────────────────────────────────────────────────────────
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        bool control = (keyData & Keys.Control) == Keys.Control;
        bool alt = (keyData & Keys.Alt) == Keys.Alt;
        bool shift = (keyData & Keys.Shift) == Keys.Shift;

        if (keyCode == Keys.C && !control && !alt && !shift)
        {
            // Same shape as HandleF5, and checked AHEAD of the menu gate below:
            // the same drop that makes the API unreachable also empties
            // MenuOptions (ResetSessionModels), so a check inside that gate could
            // never run — 'C' was the one key in this window that answered a
            // dropped socket with dead air. The request stamp is NOT set here:
            // nothing was asked for, so no later frame may claim to be the answer.
            if (!_gsxService.RemoteApiAvailable)
            {
                AnnounceUnavailable();
                return true;
            }

            // Settings are reachable only while a GSX menu is up (the documented
            // "open the menu with F5, then press C" flow) — with the API up and no
            // menu, 'C' falls through to the base handler as before.
            if (_gsxService.MenuOptions.Count == 0)
                return base.ProcessCmdKey(ref msg, keyData);

            // Stamp the request BEFORE anything else: OnSettingsChangedUi opens a
            // window only while this stamp is fresh — see the _settingsRequestedUtc
            // field comment.
            _settingsRequestedUtc = DateTime.UtcNow;

            // Ask GSX for a fresh schema. GsxService.OpenSettings awaits the reply
            // and feeds payload.settings through the frame path, so SettingsChanged
            // WILL follow — refreshing an open window in place, or (if the schema
            // was empty until now) opening one via the stamp above.
            _gsxService.OpenSettings();

            if (_settingsForm is { IsDisposed: false })
            {
                // Already open from an earlier press: just refocus it. The
                // refresh-in-place path never steals focus on its own.
                _settingsForm.ShowForm();
                return true;
            }

            // Not open, and GSX has already published a schema (every snapshot
            // carries one): open it NOW, from what is held, rather than making the
            // pilot wait on the reply. This is a direct answer to a keypress, so
            // no announcement — the screen reader reads the new window itself.
            // The reply then lands as an in-place refresh. Only when nothing has
            // been published yet does the window wait for the reply, on the stamp.
            if (_gsxService.Settings.AllFields().Any())
                OnSettingsChangedUi();
            return true;
        }

        if (keyCode == Keys.F5)
        {
            // F5 is a plain accelerator key with no competing control-level
            // meaning anywhere in this form, so ProcessCmdKey — which runs
            // before any KeyDown event, regardless of which control has
            // focus — is the path that actually executes. See HandleF5's
            // remarks for why AccessGSXForm_KeyDown also mirrors this call.
            HandleF5();
            return true;
        }

        if (keyCode == Keys.Escape)
        {
            // An open active-services dropdown owns Escape (closes the
            // dropdown); only hide the window when nothing is dropped down.
            if (_activeServicesCombo.DroppedDown)
                return base.ProcessCmdKey(ref msg, keyData);
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// F5's action: ask GSX to (re)open its menu — or, when the Remote API
    /// isn't reachable, announce why instead of silently sending nothing (a
    /// pilot pressing F5 into dead air has no way to tell "not connected"
    /// apart from "the keystroke never arrived"). Factored into its own method
    /// and called from both ProcessCmdKey (the path that actually runs for this
    /// key) and AccessGSXForm_KeyDown (a defensive mirror), so the two can never
    /// silently drift apart.
    /// </summary>
    private void HandleF5()
    {
        if (_gsxService.RemoteApiAvailable)
        {
            _gsxService.OpenMenu();
            return;
        }

        AnnounceUnavailable();
    }

    private void AccessGSXForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // F5: see HandleF5. In practice ProcessCmdKey always resolves F5
        // first (see its comment), so this branch is a defensive mirror,
        // not the live path.
        if (e.KeyCode == Keys.F5)
        {
            HandleF5();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // Escape: hide the window without closing — service keeps running.
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        // A..E are GSX's own system block, NOT menu indices — see
        // GsxSystemCommands. No modifiers: every textbox in this form is
        // read-only, so a bare letter is unambiguously a command here. 'C'
        // normally never reaches this handler (ProcessCmdKey intercepts it to
        // open Settings while a menu is available); when no menu is available
        // it lands here, finds a null Command, and is a deliberate silent
        // no-op rather than leaking into a control.
        if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.E && !e.Control && !e.Alt && !e.Shift)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            RunSystemCommand(((char)('A' + (e.KeyCode - Keys.A))).ToString());
            return;
        }

        // 0..9 (top row or numpad) are the menu-choice shortcuts — reserved
        // keys in this form, so they're always swallowed here, even when the
        // resolved index has no current entry, so a stray keystroke never
        // leaks into a focused read-only TextBox (which beeps on unhandled
        // input).
        int paintedIndex = -1;
        if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
        {
            int number = e.KeyCode - Keys.D0;
            // GSX numbering: 1..9 → index 0..8; 0 → index 9.
            paintedIndex = number == 0 ? 9 : number - 1;
        }
        else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
        {
            int number = e.KeyCode - Keys.NumPad0;
            paintedIndex = number == 0 ? 9 : number - 1;
        }

        if (paintedIndex < 0)
            return;

        e.Handled = true;
        // Suppress the keystroke so a read-only TextBox doesn't beep
        // (system-beep on disallowed input is the default for read-only
        // TextBoxes when a typeable character arrives) — regardless of
        // whether the index resolves to a real, enabled menu entry.
        e.SuppressKeyPress = true;

        // Same gate F5 and the A..E commands carry: a digit pressed after
        // the socket dropped must not be dead air. Checked here, before
        // SelectMenuEntry, because that method's own "no entry at this
        // index" exit is deliberately SILENT (a stray digit is not worth
        // speech) — and after a drop the rendered menu is typically still
        // populated, so the pick would otherwise be handed to a service that
        // can no longer send it, with nothing telling the pilot why.
        if (!_gsxService.RemoteApiAvailable)
        {
            AnnounceUnavailable();
            return;
        }

        SelectMenuEntry(paintedIndex);
    }

    /// <summary>
    /// Runs the GSX system command bound to <paramref name="shortcut"/>, or
    /// explains why it cannot. Silent on success — like every other key in this
    /// form, this is a direct user interaction, and what the command does
    /// announces itself (a Customize entry opens a GSX menu; Restart GSX moves
    /// the status line). Unlike the menu keys it is NOT gated on a menu being
    /// open: "Restart GSX" is the recovery for a wedged Couatl, which is exactly
    /// when no menu will open.
    /// </summary>
    private void RunSystemCommand(string shortcut)
    {
        if (GsxSystemCommands.ByShortcut(shortcut) is not { Command: { } command })
            return;   // 'C' — Settings, handled in ProcessCmdKey, not by GSX.

        if (!_gsxService.RemoteApiAvailable)
        {
            AnnounceUnavailable();
            return;
        }

        _gsxService.RunCommand(command);
    }

    /// <summary>
    /// Speaks why GSX cannot be reached. <see cref="GsxService.UnavailableReason"/>
    /// is guaranteed non-empty while disconnected — an empty string handed to the
    /// queued announcer is silently dropped, and this message is the ONLY thing
    /// telling a pilot on an older GSX why nothing responds.
    /// </summary>
    private void AnnounceUnavailable()
    {
        try { _announcer.Announce(_gsxService.UnavailableReason); }
        catch (Exception ex)
        {
            Log.Debug("Forms", $"unavailable announce failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-resolves <paramref name="paintedIndex"/> against <see cref="_renderedMenu"/>
    /// — the menu snapshot this form last rendered and announced — rather than the
    /// live <see cref="GsxService.Menu"/>. Reading the label fresh from the live
    /// service would compare the current menu against itself, an always-true check
    /// that can never refuse anything; the snapshot is what makes this a real gate
    /// at all, and it covers the menu-CLOSED case in particular (see the
    /// <c>_renderedMenu</c> field comment for the limits of that protection).
    ///
    /// Silently does nothing when the index has no entry (menu closed, hidden, or
    /// simply fewer options than the key implies). Announces "unavailable" and
    /// refuses when the entry was presented but disabled. Otherwise hands off to
    /// <see cref="GsxService.PickMenuEntry"/>, which performs its OWN re-resolution
    /// against the LIVE menu before sending anything — so an entry that moved,
    /// vanished, or the whole menu having changed underneath a slow reader is
    /// refused there too, silently. Never announces a successful pick — the
    /// resulting MenuChanged event speaks the new menu, and announcing the pick
    /// itself here would double up on a direct UI interaction, which this project's
    /// screen-reader rule forbids.
    /// </summary>
    private void SelectMenuEntry(int paintedIndex)
    {
        string label = paintedIndex >= 0 && paintedIndex < _renderedMenu.Count
            ? _renderedMenu.Entries[paintedIndex]
            : "";
        if (string.IsNullOrEmpty(label))
            return;

        if (!_renderedMenu.IsSelectable(paintedIndex))
        {
            try { _announcer.Announce("That option is unavailable."); }
            catch (Exception ex)
            {
                Log.Debug("Forms", $"unavailable announce failed: {ex.Message}");
            }
            return;
        }

        _gsxService.PickMenuEntry(paintedIndex, label);
    }

    // ─────────────────────────────────────────────────────────────────────
    // GsxService event handlers. The service raises these on the message-
    // pump (UI) thread because we use HWND-based receive — so direct UI
    // updates are safe — but we still guard against IsHandleCreated/Disposed.
    // ─────────────────────────────────────────────────────────────────────
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(UpdateStatus)); return; }
        UpdateStatus();
    }

    private void OnMenuChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnMenuChangedUi)); return; }
        OnMenuChangedUi();
    }

    private void OnMenuChangedUi()
    {
        // GSX republishes the WHOLE /menu object on every state tick — live at
        // EDDF, roughly 3 times a second while a service runs, because one
        // entry embeds a live counter ("113/143 passengers boarded" -> "114" ->
        // "115", nothing else different). Snapshot what was last RENDERED (and,
        // while the form was visible, announced) before RepopulateMenu
        // overwrites _renderedMenu with the fresh one, so
        // GsxMenuAnnounceResolver can tell a real change from a counter tick.
        // Across a hidden stretch this is "last rendered" only — the list the
        // pilot finds on re-show is current, so a later change is judged
        // against exactly what they can read there.
        GsxMenuModel previouslyAnnounced = _renderedMenu;

        // RepopulateMenu returns exactly what it just wrote into _menuList
        // (a DisplayListBox — its own .Text property reflects the selected
        // item, not the joined content, so we can't read it back from the
        // control). The list itself is ALWAYS repopulated — a silently-ticked
        // counter must still be readable on demand by arrowing through it —
        // only the SPOKEN announcement is gated below.
        //
        // The list AND the _renderedMenu snapshot are refreshed even while the
        // form is HIDDEN: SelectMenuEntry resolves keypresses against the
        // snapshot, and the hidden form must come back showing the current
        // menu the instant it is shown again (there is deliberately NO
        // re-render in VisibleChanged — a RepopulateMenu on show would
        // re-snapshot the LIVE GsxService.Menu, which is exactly the stale
        // model OnMenuHiddenUi's Empty reset exists to keep out of reach after
        // the menu closes; see the _renderedMenu field comment).
        string menuText = RepopulateMenu();

        // Speech while the form is HIDDEN follows the pilot's "Announce GSX
        // tooltips in background" setting — the same rule every other
        // background GSX announcement (service transitions, invoices, the
        // message banner) already obeys through GsxService.AnnounceWhenFormHidden.
        // A menu GSX opens on its own with the window hidden (an operator
        // prompt, a pushback confirmation, the not-parked reposition offer) is a
        // genuine background state change a pilot who opted in must hear; a
        // pilot who did not opt in gets the current list under screen-reader
        // focus the moment they show the window. The connect-time double-speak
        // this used to be blamed on is handled by ShouldAnnounce's empty-current
        // rule, not by this gate.
        if (!Visible && !_gsxService.AnnounceWhenFormHidden) return;

        bool shouldAnnounce = GsxMenuAnnounceResolver.ShouldAnnounce(previouslyAnnounced, _renderedMenu);
        if (shouldAnnounce && !string.IsNullOrWhiteSpace(menuText))
        {
            try { _announcer.Announce(menuText); }
            catch (Exception ex)
            {
                Log.Debug("Forms", $"menu announce failed: {ex.Message}");
            }
        }
    }

    private void OnMenuHidden(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnMenuHiddenUi)); return; }
        OnMenuHiddenUi();
    }

    private void OnMenuHiddenUi()
    {
        // Replace menu content with the same reopen prompt AccessGSX uses.
        // Keeps the textbox useful instead of blank, and obviates a
        // separate AccessibleDescription hint.
        _menuList.SetText(MENU_HIDDEN_PROMPT);
        // Clear the remembered "what was announced" snapshot too, so a
        // leftover digit/letter keypress after the menu closes can't resolve
        // against stale entries and reach PickMenuEntry — see
        // SelectMenuEntry's remarks. (GsxService.Menu itself is refreshed
        // only by a "menu" patch, not by menuShown flipping false, so this
        // local reset is the one place guaranteed to run exactly when the
        // form is told the menu is gone.)
        _renderedMenu = GsxMenuModel.Empty;
    }

    private void OnTooltipChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnTooltipChangedUi)); return; }
        OnTooltipChangedUi();
    }

    private void OnTooltipChangedUi()
    {
        // Live-text only — keeps the tooltip textbox in sync with whatever
        // GSX is currently publishing (ETA, kg loaded, pax count, etc).
        // The auto-announce path runs from OnAnnouncementReady so it only
        // fires on a real delta rather than every text twitch.
        UpdateTooltip();
    }

    private void OnActiveServicesChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnActiveServicesChangedUi)); return; }
        OnActiveServicesChangedUi();
    }

    private void OnActiveServicesChangedUi()
    {
        var names = _gsxService.ActiveServiceNames;
        // Re-populating the items list fires SelectedIndexChanged; suppress
        // it so we don't echo a synthetic selection back into GsxService.
        _suppressActiveServicesSelectionEvent = true;
        try
        {
            _activeServicesCombo.Items.Clear();
            foreach (var name in names)
                _activeServicesCombo.Items.Add(name);

            int targetIndex = -1;
            string? selected = _gsxService.SelectedActiveService
                ?? _gsxService.DefaultActiveServiceName;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                for (int i = 0; i < _activeServicesCombo.Items.Count; i++)
                {
                    if (string.Equals(
                            _activeServicesCombo.Items[i]?.ToString(),
                            selected,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            // Default to row 0 when no explicit selection exists, so the
            // user doesn't have to press Down once before the dropdown
            // has anything highlighted. Done with the suppress flag still
            // on, so GsxService.SelectedActiveService stays null — the
            // service-row picker keeps using GSX-order (which happens to
            // pick the same row 0), so the combo and announcer stay in
            // visual sync without forcing an explicit re-announce.
            if (targetIndex < 0 && _activeServicesCombo.Items.Count > 0)
                targetIndex = 0;

            if (targetIndex >= 0)
                _activeServicesCombo.SelectedIndex = targetIndex;
        }
        finally
        {
            _suppressActiveServicesSelectionEvent = false;
        }

        bool show = names.Count >= 2;
        _activeServicesCombo.Visible = show;
        _activeServicesLabel.Visible = show;
    }

    private void OnActiveServicesComboChanged(object? sender, EventArgs e)
    {
        if (_suppressActiveServicesSelectionEvent) return;
        string? selected = _activeServicesCombo.SelectedItem?.ToString();
        _gsxService.SelectedActiveService = selected;
    }

    private void OnAnnouncementReady(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnAnnouncementReadyUi)); return; }
        OnAnnouncementReadyUi();
    }

    private void OnAnnouncementReadyUi()
    {
        // Form visible → speak the delta. Form hidden → GsxService speaks
        // it itself via the AnnounceWhenFormHidden path, so we stay silent.
        if (!Visible) return;

        string announcement = _gsxService.LastAnnouncementText;
        if (string.IsNullOrWhiteSpace(announcement))
            return;

        try { _announcer.Announce(announcement); }
        catch (Exception ex)
        {
            Log.Debug("Forms", $"tooltip announce failed: {ex.Message}");
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnSettingsChangedUi)); return; }
        OnSettingsChangedUi();
    }

    private void OnSettingsChangedUi()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            // Whatever prompted this frame, the open window absorbs it — a
            // 'C' press made while the window was already up must not leave
            // a live stamp behind for a later reconnect to claim after the
            // pilot has closed it.
            _settingsRequestedUtc = null;

            // GSX can republish the whole settings tree more than once per
            // session (e.g. a reconnect resends it as part of a full
            // snapshot). Refresh the open window in place — recreating it
            // would yank screen-reader focus, re-announce the window, and
            // fire the old form's FormClosed/HideMenu side effects.
            bool hadItems = _settingsForm.HasFields;
            bool rebuilt = _settingsForm.RefreshSchema(_gsxService.Settings);
            if (rebuilt && !hadItems && _settingsForm.HasFields)
            {
                // Background state change (not user-triggered): the window
                // was showing "No GSX settings were available." and GSX has
                // now published real content for it.
                try { _announcer.Announce("GSX settings loaded."); }
                catch (Exception ex)
                {
                    Log.Debug("Forms", $"settings announce failed: {ex.Message}");
                }
            }
            return;
        }

        // No window open. Only a RECENT explicit 'C' press may create one:
        // under the Remote API this event also fires on every Hello and
        // Snapshot frame (every connect/reconnect, e.g. after 'D' Restart
        // GSX) and on every unprompted /settings patch — none of which the
        // pilot asked for, and a window created here Activate()s itself,
        // stealing screen-reader focus mid-flight (see the
        // _settingsRequestedUtc field comment). An unclaimed republish is
        // simply absorbed: the schema is already held in _gsxService.Settings,
        // and the next 'C' press shows it. A stale stamp is dropped rather
        // than left lying around for a later, unrelated frame to claim.
        if (_settingsRequestedUtc is not { } requestedUtc)
            return;
        _settingsRequestedUtc = null;
        if (DateTime.UtcNow - requestedUtc > SettingsRequestWindow)
            return;

        _settingsForm = new GsxSettingsForm(_gsxService, _announcer, _gsxService.Settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            // A 'C' pressed while this window was open left a stamp behind; the
            // window it would have re-opened has just been closed on purpose, so
            // no later frame may cash it in.
            _settingsRequestedUtc = null;
            _gsxService.HideMenu();
            OnMenuHiddenUi();
        };
        _settingsForm.ShowForm();
        // No "opened" announcement: the screen reader announces the newly
        // focused window itself (project rule: never announce direct user
        // interactions).
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI population helpers.
    // ─────────────────────────────────────────────────────────────────────
    private void UpdateStatus()
    {
        string text = _gsxService.StatusText;
        if (_statusTextBox.Text != text)
            _statusTextBox.Text = text;
    }

    /// <summary>Repopulates the menu list and returns the text that was written into it
    /// (either the hidden prompt or the rendered title+options block), so callers that
    /// need to speak the content (OnMenuChangedUi) don't have to read it back from the
    /// control — a DisplayListBox's own .Text reflects the selected item, not the joined
    /// content.</summary>
    private string RepopulateMenu()
    {
        GsxMenuModel menu = _gsxService.Menu;
        // Snapshot what we're about to render/announce — SelectMenuEntry
        // resolves a keypress against this remembered snapshot rather than
        // re-reading _gsxService.Menu fresh, so a keypress is checked
        // against what the pilot actually heard, not against whatever
        // happens to be live right now (see SelectMenuEntry's remarks).
        _renderedMenu = menu;

        // No options means we're in the hidden/initial state. Show the
        // reopen prompt instead of an empty textbox so the user always sees
        // (and the screen reader always reads) something useful.
        if (menu.Count == 0)
        {
            _menuList.SetText(MENU_HIDDEN_PROMPT);
            return MENU_HIDDEN_PROMPT;
        }

        // Render menu as plain multi-line text — same layout as AccessGSX:
        // title on its own line, then each option as "<shortcut>. <text>".
        // The shortcut prefix is the ACTUAL key that selects that option
        // (1-9, 0, then A-E for entries 10-14 — GsxMenuModel.Shortcut),
        // never the raw 0-based array index, so what's read out is exactly
        // what the pilot should press. GSX's own state cue and disabled
        // flag are spelled out in words too — the sighted client renders
        // them as an icon tint, which has no screen-reader equivalent
        // unless we say it here. GsxMenuEntryRenderer (bottom of this file)
        // skips any entry GSX published as blank padding rather than a real
        // option — see its remarks for why, and GsxMenuModel.IsBlank for the
        // live evidence.
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(menu.Title))
        {
            sb.AppendLine(menu.Title);
        }
        foreach (string line in GsxMenuEntryRenderer.RenderLines(menu))
            sb.AppendLine(line);

        // GSX's always-available system block, below the numbered options —
        // exactly where the in-sim menu and every previous AccessGSX build put
        // it. These are not menu entries (see GsxSystemCommands): they are
        // command.run verbs plus the local Settings window, so they are
        // rendered here rather than injected into GsxService.Menu, which stays
        // GSX's own data for the gate selector to walk.
        foreach (var command in GsxSystemCommands.All)
            sb.Append(command.Shortcut).Append(". ").AppendLine(command.Label);

        string text = sb.ToString();
        // Trim the trailing AppendLine newline before handing to the list —
        // otherwise the reconcile would show a spurious blank last row (the
        // announced text below still uses the untrimmed value, matching
        // exactly what was previously read back from the TextBox).
        _menuList.SetText(text.TrimEnd());
        return text;
    }

    private void UpdateTooltip()
    {
        string text = _gsxService.LastTooltip;
        if (_tooltipTextBox.Text != text)
            _tooltipTextBox.Text = text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gsxService.StateChanged -= OnStateChanged;
            _gsxService.MenuChanged -= OnMenuChanged;
            _gsxService.MenuHidden -= OnMenuHidden;
            _gsxService.TooltipChanged -= OnTooltipChanged;
            _gsxService.AnnouncementReady -= OnAnnouncementReady;
            _gsxService.ActiveServicesChanged -= OnActiveServicesChanged;
            _gsxService.SettingsChanged -= OnSettingsChanged;
            if (_settingsForm is { IsDisposed: false })
                _settingsForm.Close();
            // Restore background-announce policy to the user setting when
            // the form goes away entirely (e.g. app shutdown). The service
            // may outlive the form — without this it would stay in
            // form-driven (=false) mode forever and the user's setting
            // would be ignored.
            _gsxService.AnnounceWhenFormHidden =
                MSFSBlindAssist.Settings.SettingsManager.Current.GsxBackgroundMonitoring;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Decides whether a freshly-parsed <see cref="GsxMenuModel"/> differs from the one last
/// ANNOUNCED enough to justify re-speaking the whole menu. GSX republishes the entire
/// <c>/menu</c> object on every state tick — live at EDDF, roughly 3 times a second while
/// a service runs, because one entry embeds a live counter: three consecutive captured
/// payloads differed only in "113/143 passengers boarded" -> "114/143" -> "115/143",
/// every other entry byte-identical. <see cref="AccessGSXForm"/>'s announcer is QUEUED
/// and never interrupts, so speaking the full menu on every one of those doesn't just
/// repeat — it backlogs, burying every other callout in the app behind an unbounded
/// queue. The menu ListBox itself is always repopulated regardless of this verdict (see
/// <see cref="AccessGSXForm.RepopulateMenu"/>) — only the SPOKEN announcement is gated,
/// so a silently-ticked count is still readable on demand.
///
/// Never announces an EMPTY <paramref name="current"/>: <c>GsxService</c> fires
/// <c>MenuChanged</c> on both the Hello and the Snapshot frame of every connect/reconnect
/// (and after every 'D' Restart GSX, which drops and re-establishes the socket), the menu
/// is normally CLOSED at that moment, and <c>RepopulateMenu</c> renders the "GSX Menu
/// hidden. Press F5 to open it." prompt for an empty model — treating each of those as a
/// "first appearance" spoke that prompt twice back-to-back on every connect. Menu-hide is
/// silent by design (<c>OnMenuHiddenUi</c> never speaks); the prompt is there to be READ
/// from the list, and an empty menu has nothing to announce.
///
/// Otherwise announces on: first appearance of a NON-empty menu (<paramref name="previous"/>
/// was empty — covers both a genuinely new menu and a reopen after <c>OnMenuHiddenUi</c>
/// reset the snapshot), a title change, an entry-count change, or any entry changing by
/// more than a run of digits (e.g. "Request Boarding" -> "Boarding no longer possible",
/// "Customize this Parking position" -> "Reset position" — both real availability
/// transitions observed live, neither one GSX flagged via the <c>disabled</c> array).
///
/// GUARD: GSX paginates its own menus at 10 entries, and a "Next Page ▶" entry appears
/// as an ordinary entry (confirmed live at EDDF) — so a paged stand list can plausibly
/// change EVERY entry by digits alone (Gate A11..A14 -> Gate A21..A24). Silently
/// swallowing that would make a page-turn keypress look like it did nothing. So when
/// MORE THAN HALF the entries changed at all, this announces even if every one of those
/// changes is digit-only — a counter tick touches one entry, a page turn touches all of
/// them. Internal, reached by GsxMenuAnnounceResolverTests via InternalsVisibleTo
/// (Properties/InternalsVisibleTo.cs) — same pattern as GsxRangeBoundsResolver in
/// GsxSettingsForm.cs and GsxActiveServiceResolver in GsxService.cs.
/// </summary>
internal static class GsxMenuAnnounceResolver
{
    private static readonly Regex DigitRun = new(@"\d+", RegexOptions.Compiled);

    public static bool ShouldAnnounce(GsxMenuModel previous, GsxMenuModel current)
    {
        // Empty -> empty (every connect/reconnect) and non-empty -> empty (the
        // menu closing) are both silent -- see the class remarks. Checked BEFORE
        // the first-appearance rule, or an empty "first appearance" speaks the
        // hidden prompt.
        if (current.Count == 0) return false;
        if (previous.Count == 0) return true;
        if (!string.Equals(previous.Title, current.Title, StringComparison.Ordinal)) return true;
        if (previous.Count != current.Count) return true;

        int changedCount = 0;
        bool nonDigitChange = false;
        for (int i = 0; i < current.Count; i++)
        {
            string before = previous.Entries[i];
            string after = current.Entries[i];
            if (string.Equals(before, after, StringComparison.Ordinal)) continue;

            changedCount++;
            if (!IsDigitOnlyChange(before, after))
                nonDigitChange = true;
        }

        // Page-turn guard: more than half the entries changed at all -- announce
        // regardless of whether every individual change happens to be digit-only.
        if (changedCount * 2 > current.Count) return true;

        return nonDigitChange;
    }

    /// <summary>
    /// True when <paramref name="before"/> and <paramref name="after"/> are identical
    /// once every run of digits is stripped out — i.e. they differ only in the numbers
    /// embedded in the text, never in the surrounding words. Splitting on digit runs
    /// (rather than comparing digit-count) also tolerates a run changing LENGTH, not
    /// just value — "9/143 passengers boarded" -> "10/143 passengers boarded" is still
    /// purely a counter tick.
    /// </summary>
    private static bool IsDigitOnlyChange(string before, string after)
    {
        string[] beforeParts = DigitRun.Split(before);
        string[] afterParts = DigitRun.Split(after);
        if (beforeParts.Length != afterParts.Length) return false;

        for (int i = 0; i < beforeParts.Length; i++)
        {
            if (!string.Equals(beforeParts[i], afterParts[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

/// <summary>
/// Renders the numbered option lines for a <see cref="GsxMenuModel"/> — one line per
/// entry as "&lt;shortcut&gt;. &lt;text&gt;[ — &lt;state&gt;][ — unavailable]" — skipping
/// any entry GSX published as BLANK padding rather than a real option.
///
/// GSX's parking-search results are a fixed 10-slot menu; unused slots come back as
/// empty strings and are NOT marked disabled — confirmed live at EDDF: searching "A15"
/// returned one real match at index 0, "Back" at index 9, and "" at every slot in
/// between, with <c>disabled</c> staying [false x10] throughout (see
/// <see cref="GsxMenuModel.IsBlank"/>). Rendered without this guard, a screen-reader
/// user tabbing the list hears eight bare numbers with nothing after them ("2. ", "3. "
/// …) — meaningless rows that cannot be described, so they are skipped outright rather
/// than rendered.
///
/// The shortcut is always computed from the entry's REAL index in <paramref name="menu"/>
/// — via <see cref="GsxMenuModel.Shortcut"/> — never a compacted position after skipping
/// blanks. That real index is exactly what <see cref="GsxService.PickMenuEntry"/> sends
/// as <c>menu.pick</c>'s index, so renumbering the remaining entries compactly would print
/// a number that picks the WRONG entry — e.g. in the EDDF capture above, the pilot must
/// still see "1." beside the real gate match and "0." beside "Back" at index 9, with
/// nothing printed for indices 1-8, not "1." and "2." back to back.
///
/// Internal, reached by GsxMenuEntryRendererTests via InternalsVisibleTo
/// (Properties/InternalsVisibleTo.cs) — same pattern as GsxMenuAnnounceResolver above,
/// GsxRangeBoundsResolver in GsxSettingsForm.cs, and GsxActiveServiceResolver in
/// GsxService.cs.
/// </summary>
internal static class GsxMenuEntryRenderer
{
    public static IReadOnlyList<string> RenderLines(GsxMenuModel menu)
    {
        var lines = new List<string>();
        for (int i = 0; i < menu.Count; i++)
        {
            string entry = menu.Entries[i];
            if (GsxMenuModel.IsBlank(entry))
                continue;

            string line = GsxMenuModel.Shortcut(i) + ". " + entry;

            string? suffix = menu.StateSuffix(i);
            if (suffix != null)
                line += " — " + suffix;
            if (menu.Disabled[i])
                line += " — unavailable";

            lines.Add(line);
        }
        return lines;
    }
}
