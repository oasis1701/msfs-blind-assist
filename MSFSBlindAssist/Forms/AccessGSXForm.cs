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

        if (keyCode == Keys.C && !control && !alt && !shift && _gsxService.MenuOptions.Count > 0)
        {
            _gsxService.OpenSettings();
            // OpenSettings is a fire-and-forget Remote API send — the
            // "settings" response (and therefore SettingsChanged /
            // OnSettingsChangedUi, which creates or shows _settingsForm)
            // arrives asynchronously on a later WebSocket frame, not
            // synchronously here. This only refocuses a window that is
            // ALREADY open from an earlier press; the refresh-in-place path
            // deliberately never steals focus on its own for a background
            // republish.
            if (_settingsForm is { IsDisposed: false })
                _settingsForm.ShowForm();
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
        // RepopulateMenu returns exactly what it just wrote into _menuList
        // (a DisplayListBox — its own .Text property reflects the selected
        // item, not the joined content, so we can't read it back from the
        // control). Speak the rendered menu in one pass — matches the
        // upstream AccessGSX "speak menu" behavior: title + every option, so
        // a single Announce gives the user the full picture without having
        // to navigate line-by-line.
        string menuText = RepopulateMenu();
        if (!string.IsNullOrWhiteSpace(menuText))
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

        _settingsForm = new GsxSettingsForm(_gsxService, _announcer, _gsxService.Settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
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
        // unless we say it here.
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(menu.Title))
        {
            sb.AppendLine(menu.Title);
        }
        for (int i = 0; i < menu.Count; i++)
        {
            sb.Append(GsxMenuModel.Shortcut(i)).Append(". ").Append(menu.Entries[i]);

            string? suffix = menu.StateSuffix(i);
            if (suffix != null)
                sb.Append(" — ").Append(suffix);
            if (menu.Disabled[i])
                sb.Append(" — unavailable");

            sb.AppendLine();
        }

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
