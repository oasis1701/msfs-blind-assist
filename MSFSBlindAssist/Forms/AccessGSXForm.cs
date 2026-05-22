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
    private const string MENU_TIMEOUT_PROMPT = "[GSX Menu] Timeout. Press F5 to re-open.";

    private TextBox _statusTextBox = null!;
    private TextBox _menuTextBox = null!;
    private TextBox _tooltipTextBox = null!;

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

        // Read-only multiline TextBox: the screen reader reads the whole
        // menu in one pass each time the text refreshes (which is what we
        // do after MenuChanged), matching the upstream AccessGSX UX. The
        // keyboard shortcuts below (1..9, 0, A..E) pick options; the user
        // doesn't need to navigate the text by line.
        _menuTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "GSX menu",
            // No AccessibleDescription — the textbox content itself always
            // contains an actionable prompt ("Press F5 to open it"), so a
            // separate hint would be redundant noise for screen readers.
            Text = MENU_HIDDEN_PROMPT
        };

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
        menuPanel.Controls.Add(_menuTextBox);
        menuPanel.Controls.Add(menuLabel);

        var tooltipPanel = new Panel { Dock = DockStyle.Fill };
        tooltipPanel.Controls.Add(_tooltipTextBox);
        tooltipPanel.Controls.Add(tooltipLabel);

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
        _gsxService.MenuTimedOut += OnMenuTimedOut;
        _gsxService.TooltipChanged += OnTooltipChanged;

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
    private void AccessGSXForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // F5: ask GSX to open / reopen its menu.
        if (e.KeyCode == Keys.F5)
        {
            _gsxService.OpenMenu();
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

        // 0..9 (top row or numpad) and A..E choose menu options. Only fire
        // when there's a menu open — otherwise the keystrokes are no-ops so
        // the user doesn't accidentally choose a stale option.
        if (_gsxService.MenuOptions.Count == 0)
            return;

        int choice = -1;
        if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
        {
            int number = e.KeyCode - Keys.D0;
            // GSX numbering: 1..9 → choice 0..8; 0 → choice 9.
            choice = number == 0 ? 9 : number - 1;
        }
        else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
        {
            int number = e.KeyCode - Keys.NumPad0;
            choice = number == 0 ? 9 : number - 1;
        }
        else if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.E)
        {
            // No modifiers — typing into the tooltip textbox is read-only, so
            // a bare letter is unambiguously a menu choice here.
            if (!e.Control && !e.Alt && !e.Shift)
                choice = (e.KeyCode - Keys.A) + 10;
        }

        if (choice >= 0)
        {
            _gsxService.Choose(choice);
            e.Handled = true;
            // Suppress the keystroke so the read-only TextBox doesn't beep
            // (system-beep on disallowed input is the default for read-only
            // TextBoxes when a typeable character arrives).
            e.SuppressKeyPress = true;
        }
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
        RepopulateMenu();
        // Speak the rendered menu in one pass — matches the upstream
        // AccessGSX "speak menu" behavior. The text in _menuTextBox is the
        // title + every option, so a single Announce gives the user the
        // full picture without having to navigate line-by-line.
        if (!string.IsNullOrWhiteSpace(_menuTextBox.Text))
        {
            try { _announcer.Announce(_menuTextBox.Text); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccessGSXForm] menu announce failed: {ex.Message}");
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
        _menuTextBox.Text = MENU_HIDDEN_PROMPT;
    }

    private void OnMenuTimedOut(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnMenuTimedOutUi)); return; }
        OnMenuTimedOutUi();
    }

    private void OnMenuTimedOutUi()
    {
        // MenuHidden fires first and writes the regular hide prompt; overwrite
        // with the timeout-specific version so the user sees they need to
        // re-open rather than that GSX closed the menu on demand.
        _menuTextBox.Text = MENU_TIMEOUT_PROMPT;
        try { _announcer.Announce("GSX menu timeout"); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccessGSXForm] timeout announce failed: {ex.Message}");
        }
    }

    private void OnTooltipChanged(object? sender, EventArgs e)
    {
        if (!IsHandleCreated || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(OnTooltipChangedUi)); return; }
        OnTooltipChangedUi();
    }

    private void OnTooltipChangedUi()
    {
        UpdateTooltip();
        // Form is visible (the background-announce path in GsxService only
        // fires when AnnounceWhenFormHidden is true). Speak the tooltip so
        // the user hears it without having to focus the tooltip TextBox.
        if (Visible)
        {
            try { _announcer.Announce(_gsxService.LastTooltip); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccessGSXForm] tooltip announce failed: {ex.Message}");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI population helpers.
    // ─────────────────────────────────────────────────────────────────────
    private void UpdateStatus()
    {
        _statusTextBox.Text = _gsxService.StatusText;
    }

    private void RepopulateMenu()
    {
        // No options means we're in the hidden/initial state. Show the
        // reopen prompt instead of an empty textbox so the user always sees
        // (and the screen reader always reads) something useful.
        if (_gsxService.MenuOptions.Count == 0)
        {
            _menuTextBox.Text = MENU_HIDDEN_PROMPT;
            return;
        }
        // Render menu as plain multi-line text — same layout as AccessGSX:
        // title on its own line, then each option as "key - text". The
        // screen reader reads the whole block on Announce.
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(_gsxService.MenuTitle))
        {
            sb.AppendLine(_gsxService.MenuTitle);
        }
        foreach (var option in _gsxService.MenuOptions)
        {
            sb.Append(option.Key.PadLeft(2)).Append(" - ").AppendLine(option.Text);
        }
        _menuTextBox.Text = sb.ToString();
    }

    private void UpdateTooltip()
    {
        _tooltipTextBox.Text = _gsxService.LastTooltip;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gsxService.StateChanged -= OnStateChanged;
            _gsxService.MenuChanged -= OnMenuChanged;
            _gsxService.MenuHidden -= OnMenuHidden;
            _gsxService.MenuTimedOut -= OnMenuTimedOut;
            _gsxService.TooltipChanged -= OnTooltipChanged;
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
