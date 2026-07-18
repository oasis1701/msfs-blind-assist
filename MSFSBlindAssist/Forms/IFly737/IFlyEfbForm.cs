using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.IFly737;

/// <summary>
/// iFly 737 MAX8 EFB tablet window (Shift+T).
///
/// The SP1 EFB is served over HTTP by the external iFly EFB process
/// (Data\Tool\EFB\iFly-EFB.exe) — the same server the iFly manual documents for
/// iPad use ("connect to http://&lt;pc&gt;:8084 ... 8084 is the port used by the
/// EFB"). Hosting it in WebView2 gives the screen reader native browse mode
/// over whatever HTML the EFB serves. The port (default 8084; iFly Manager
/// hotfix 1.1.0.1 added a user-configurable EFB port) is a settings-file-only
/// knob (UserSettings.IFlyEfbPort) — there is no in-app port field or Connect
/// button; the form auto-loads on open, the same pattern as the Fenix EFB
/// (Forms/Fenix/FenixEFBForm.cs). On a navigation failure (EFB process not
/// running, aircraft still loading, wrong port) the form swaps the browser for
/// an accessible retry panel and speaks the failure, so a blind user gets
/// clear feedback instead of WebView2's raw error page.
/// </summary>
public class IFlyEfbForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Injected after every successful navigation (verified live against the served
    /// EFB v1.1, 2026-07 — the pages are REAL semantic HTML: inputs/buttons/selects/
    /// labels, zero canvas). Screen-reader fixes:
    /// (1) The EFB creates an UNLABELED full-screen black "click to unlock" overlay
    ///     (#lockScreenOverlay) on EVERY load — an invisible wall for a blind user.
    ///     Auto-dismiss it the way its own click handler does (window.unlockScreen,
    ///     falling back to removing the node), retrying briefly because the overlay
    ///     and the unlock function are created asynchronously after load.
    /// (2) The home-screen app icons, the #homeButton (back to main page), and the
    ///     Ground Services category sidebar tabs are clickable DIVs (no role, not
    ///     tabbable) — stamp role="button" + tabindex, translate Enter/Space to a
    ///     click, and keep doing so as the SPA swaps views (MutationObserver).
    ///     Alt+Home anywhere in the page = Home.
    /// (3) Doors page: the per-door buttons carry their identity only in data-door,
    ///     and the arm buttons' only name is a CHINESE tooltip — English aria-labels.
    /// (4) Ground Services: the ground VEHICLES (GPU, air start, fuel truck, stairs,
    ///     catering, cargo...) are toggled by clicking the graphical SVG aircraft
    ///     diagram — but every vehicle also has a REAL sim-wired checkbox the module
    ///     keeps hidden ("功能已集成到SVG" — kept for data binding). Resurrect that
    ///     panel: label + unhide the checkboxes and add a "Ground Vehicles" category
    ///     tab so they're reachable like every other section. The SVG click path
    ///     just toggles these same checkboxes, so behaviour is identical.
    /// (5) My Plane setting rows (all pages — Other Systems, CDU, Units, Engine...):
    ///     segmented choices are bare '.radio-group' > '.radio-option' DIVs (module
    ///     click handler = e.target.closest('.radio-option'), so a DOM click works,
    ///     but they read as one jumbled string and can't be focused). Stamp
    ///     role=radio + tabindex + aria-checked (synced from the 'selected'/'active'
    ///     class by the observer), wrap in role=radiogroup labeled from the row's
    ///     '.setting-label'; Enter/Space clicks. Number inputs get an aria-label
    ///     composed of the row label + range hint + unit ("Custom Alignment Time,
    ///     0-2000 s"); the '.toggle-switch' checkboxes get the row label;
    ///     '.section-title' DIVs become headings for quick navigation.
    /// </summary>
    private const string AccessibilityShim = """
        (function () {
          if (window.__msfsbaIflyShim) return; window.__msfsbaIflyShim = true;
          function unlock() {
            var o = document.getElementById('lockScreenOverlay');
            if (!o) return;
            try { if (typeof window.unlockScreen === 'function') window.unlockScreen(); } catch (e) { }
            try { if (o.parentNode) o.parentNode.removeChild(o); } catch (e) { }
          }
          var tries = 0;
          var timer = setInterval(function () { unlock(); if (++tries > 20) clearInterval(timer); }, 250);

          function stampButton(el, label) {
            if (!el || el.getAttribute('data-msfsba-btn')) return;
            el.setAttribute('data-msfsba-btn', '1');
            if (!el.getAttribute('role')) el.setAttribute('role', 'button');
            if (!el.hasAttribute('tabindex')) el.setAttribute('tabindex', '0');
            if (label && !el.getAttribute('aria-label')) el.setAttribute('aria-label', label);
          }

          var VEHICLE_NAMES = {
            electric: 'Ground power unit',
            pneumatic: 'Air start unit',
            air_conditioning: 'Air conditioning cart',
            fueling: 'Fuel truck',
            vacuum_lavatory_service: 'Lavatory service truck',
            potable_water: 'Potable water truck',
            fwd_airstair: 'Forward airstair',
            aft_airstair: 'Aft airstair',
            fwd_galley_truck: 'Forward catering truck',
            aft_galley_truck: 'Aft catering truck',
            fwd_bulk_cargo_trainloader: 'Forward cargo loader',
            aft_bulk_cargo_trainloader: 'Aft cargo loader'
          };

          function enhanceGroundVehicles() {
            var section = document.getElementById('section-ground_vehicles');
            if (!section || section.getAttribute('data-msfsba-gv')) return;
            section.setAttribute('data-msfsba-gv', '1');
            // The module hides the section, its card, and every row inline; the
            // checkboxes inside are real and sim-wired (the SVG clicks toggle them).
            section.style.display = '';
            var card = section.querySelector('.mp-card');
            if (card) card.style.display = '';
            var rows = section.querySelectorAll('.mp-form-row');
            for (var i = 0; i < rows.length; i++) {
              var input = rows[i].querySelector('input[type="checkbox"]');
              if (!input) continue;
              var name = VEHICLE_NAMES[input.id] || input.id.replace(/_/g, ' ');
              rows[i].style.display = '';
              input.setAttribute('aria-label', name);
              if (!rows[i].querySelector('.msfsba-gv-label')) {
                var span = document.createElement('span');
                span.className = 'msfsba-gv-label';
                span.style.marginLeft = '8px';
                span.textContent = name;
                rows[i].appendChild(span);
              }
            }
            // Add a "Ground Vehicles" tab to the category sidebar so the section is
            // reachable like Chocks / Safety Pins / etc. The module's own tab handler
            // was bound before this tab existed, so it carries its own click logic
            // (same class flips the module's handlers perform).
            var sidebar = document.querySelector('.ground-support-category-sidebar');
            if (sidebar && !document.getElementById('msfsba-gv-cat')) {
              var item = document.createElement('div');
              item.className = 'ground-support-category-item';
              item.id = 'msfsba-gv-cat';
              item.setAttribute('data-category-id', 'ground_vehicles');
              item.textContent = 'Ground Vehicles';
              item.addEventListener('click', function () {
                var cats = document.querySelectorAll('.ground-support-category-item');
                for (var c = 0; c < cats.length; c++) cats[c].classList.remove('active');
                var secs = document.querySelectorAll('.ground-support-controls-content .mp-section');
                for (var s = 0; s < secs.length; s++) secs[s].classList.remove('active');
                item.classList.add('active');
                section.classList.add('active');
              });
              sidebar.insertBefore(item, sidebar.firstChild);
            }
          }

          function textOf(el) { return el ? (el.textContent || '').replace(/\s+/g, ' ').trim() : ''; }

          function rowLabelFor(el) {
            var item = el.closest ? el.closest('.setting-item') : null;
            var name = item ? textOf(item.querySelector('.setting-label')) : '';
            if (!name) {
              var sec = el.closest ? el.closest('.myplane-section') : null;
              if (sec) name = textOf(sec.querySelector('.section-title'));
            }
            return name;
          }

          // Segmented option rows: '.radio-group' of '.radio-option' DIVs. Stamp
          // real radio semantics and keep aria-checked in sync with the module's
          // 'selected'/'active' class (re-run by the observer on class changes).
          function enhanceRadioGroups() {
            var groups = document.querySelectorAll('.radio-group');
            for (var g = 0; g < groups.length; g++) {
              var group = groups[g];
              if (!group.getAttribute('data-msfsba-rg')) {
                group.setAttribute('data-msfsba-rg', '1');
                group.setAttribute('role', 'radiogroup');
                var name = rowLabelFor(group);
                if (name && !group.getAttribute('aria-label')) group.setAttribute('aria-label', name);
              }
              var opts = group.querySelectorAll('.radio-option');
              for (var o = 0; o < opts.length; o++) {
                var opt = opts[o];
                if (!opt.getAttribute('data-msfsba-radio')) {
                  opt.setAttribute('data-msfsba-radio', '1');
                  opt.setAttribute('role', 'radio');
                  if (!opt.hasAttribute('tabindex')) opt.setAttribute('tabindex', '0');
                }
                var checked = (opt.classList.contains('selected') || opt.classList.contains('active')) ? 'true' : 'false';
                if (opt.getAttribute('aria-checked') !== checked) opt.setAttribute('aria-checked', checked);
                var disabled = opt.classList.contains('disabled') ? 'true' : null;
                if (disabled) opt.setAttribute('aria-disabled', 'true');
                else if (opt.getAttribute('aria-disabled')) opt.removeAttribute('aria-disabled');
              }
            }
          }

          // Label bare inputs from their setting row: number fields get
          // "<row label>, <range hint> <unit>", toggle checkboxes get the row label.
          function labelSettingRows() {
            var items = document.querySelectorAll('.setting-item');
            for (var i = 0; i < items.length; i++) {
              var item = items[i];
              var name = textOf(item.querySelector('.setting-label'));
              if (!name) continue;
              var hint = textOf(item.querySelector('.input-range-hint'));
              var unit = textOf(item.querySelector('.input-unit'));
              var ctrls = item.querySelectorAll('input, select, textarea');
              for (var c = 0; c < ctrls.length; c++) {
                var ctrl = ctrls[c];
                if (ctrl.getAttribute('data-msfsba-lab') || ctrl.getAttribute('aria-label')) continue;
                ctrl.setAttribute('data-msfsba-lab', '1');
                var label = name;
                if (ctrl.type !== 'checkbox') {
                  if (hint) label += ', ' + hint;
                  if (unit) label += ' ' + unit;
                }
                ctrl.setAttribute('aria-label', label);
              }
            }
            var titles = document.querySelectorAll('.section-title');
            for (var t = 0; t < titles.length; t++) {
              if (titles[t].getAttribute('role')) continue;
              titles[t].setAttribute('role', 'heading');
              titles[t].setAttribute('aria-level', '2');
            }
          }

          function enhance() {
            var icons = document.querySelectorAll('.app-icon');
            for (var i = 0; i < icons.length; i++) stampButton(icons[i], null);
            // The Home button (back to the main page) is a title-only DIV.
            stampButton(document.getElementById('homeButton'), 'Home');
            // Ground Services category sidebar tabs are clickable DIVs with text.
            var cats = document.querySelectorAll('.ground-support-category-item');
            for (var k = 0; k < cats.length; k++) stampButton(cats[k], null);
            // Doors page labels.
            var doors = document.querySelectorAll('button[data-door]');
            for (var j = 0; j < doors.length; j++) {
              var b = doors[j];
              if (b.getAttribute('data-msfsba-labeled')) continue;
              b.setAttribute('data-msfsba-labeled', '1');
              var name = (b.getAttribute('data-door') || '').replace(/_/g, ' ').toLowerCase();
              var isArm = b.className.indexOf('arm-button') >= 0;
              b.setAttribute('aria-label', name + (isArm ? ' arm disarm' : ' door'));
            }
            enhanceGroundVehicles();
            enhanceRadioGroups();
            labelSettingRows();
          }
          enhance();

          document.addEventListener('keydown', function (e) {
            // Escape/F5 from INSIDE the web content never reach ProcessCmdKey/KeyDown
            // (the WebView2 browser process owns that focus) — post a message the
            // .NET side bridges to close/reload. Capture-phase (true) guarantees this
            // fires even if the page handles the key itself.
            if (e.key === 'Escape') {
              window.chrome.webview.postMessage('msfsba-efb-close');
              e.preventDefault();
              return;
            }
            if (e.key === 'F5') {
              window.chrome.webview.postMessage('msfsba-efb-reload');
              e.preventDefault();
              return;
            }
            // Alt+Home anywhere = back to the EFB main page.
            if (e.altKey && e.key === 'Home') {
              var home = document.getElementById('homeButton');
              if (home) { home.click(); e.preventDefault(); }
              return;
            }
            // Enter/Space activate the div-buttons and radio options we stamped.
            if ((e.key === 'Enter' || e.key === ' ') && e.target && e.target.getAttribute &&
                (e.target.getAttribute('data-msfsba-btn') || e.target.getAttribute('data-msfsba-radio'))) {
              e.target.click();
              e.preventDefault();
            }
          }, true);

          try {
            // attributeFilter 'class' keeps radio aria-checked synced when the
            // module flips 'selected'/'active'; enhance() only writes attributes
            // when they differ (and never touches class), so this cannot loop.
            new MutationObserver(function () { enhance(); })
              .observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
          } catch (e) { }
        })();
        """;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly WebView2 _webView;
    private readonly Panel _errorPanel;
    private readonly TextBox _errorBox;
    private readonly Button _retryButton;
    private bool _webViewReady;
    private bool _initStarted;
    private IntPtr _previousWindow = IntPtr.Zero;

    public IFlyEfbForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        Text = "iFly 737 EFB";
        Size = new Size(1100, 850);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AccessibleName = "iFly EFB",
        };
        Controls.Add(_webView);

        _retryButton = new Button
        {
            Text = "&Retry",
            AutoSize = true,
            Location = new Point(12, 120),
        };
        _retryButton.Click += (_, _) => Navigate();

        // Read-only multiline TextBox, not a Label — readouts must be a tab-stoppable
        // control so NVDA/JAWS can review the failure text on demand (repo rule).
        _errorBox = new TextBox
        {
            Location = new Point(12, 12),
            Size = new Size(700, 100),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "iFly EFB status message",
        };

        _errorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AccessibleName = "iFly EFB status",
        };
        _errorPanel.Controls.Add(_errorBox);
        _errorPanel.Controls.Add(_retryButton);
        Controls.Add(_errorPanel);

        // Covers Escape/F5 when WinForms chrome (the error panel / Retry button) has
        // focus. When the webview's page content has focus, keystrokes are owned by
        // the WebView2 browser process and never reach here — the injected
        // AccessibilityShim's capture-phase listener bridges those via
        // OnWebMessageReceived instead.
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.F5)
            {
                if (_errorPanel.Visible)
                {
                    Navigate();
                }
                else
                {
                    try { _webView.Reload(); } catch { }
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        // Hide instead of dispose on close, so reopening returns the user to the EFB
        // page they were on (and avoids re-initializing WebView2 every time). The
        // form is disposed for real on aircraft swap (MainForm.IFly737.cs, which
        // calls Dispose() directly and bypasses this handler). Any user-initiated
        // close — the X button / Alt+F4 (UserClosing), our own Escape Close()
        // (CloseReason.None), or the webview close bridge — just hides; only real
        // app/OS shutdown is allowed to tear the window down.
        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }
            e.Cancel = true;
            Hide();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    public async void ShowForm()
    {
        // Remember who had focus so we can restore it when the window is dismissed.
        _previousWindow = GetForegroundWindow();
        Show();
        Activate();

        if (_webViewReady)
        {
            // Subsequent opens: just re-focus the already-loaded browser, unless the
            // last attempt failed — then silently retry rather than leaving the user
            // staring at a stale error message from a prior open.
            if (_errorPanel.Visible) Navigate();
            else _webView.Focus();
            return;
        }

        if (_initStarted) return; // init already running from a previous ShowForm() call; it will Navigate() itself once ready.

        _initStarted = true;
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webViewReady = true;
            Navigate();
        }
        catch (Exception ex)
        {
            _initStarted = false;
            ShowError($"The browser component could not start. {ex.Message}");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Fired on the UI thread. Close() runs the hide-and-restore FormClosing path.
        var message = e.TryGetWebMessageAsString();
        if (message == "msfsba-efb-close")
        {
            Close();
        }
        else if (message == "msfsba-efb-reload")
        {
            try { _webView.CoreWebView2.Reload(); } catch { }
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            ShowBrowser();
            try { await _webView.CoreWebView2.ExecuteScriptAsync(AccessibilityShim); }
            catch { }
            _announcer.Announce("iFly EFB loaded.");
            _webView.Focus();
        }
        else
        {
            ShowError(
                $"iFly EFB is not reachable at http://localhost:{SettingsManager.Current.IFlyEfbPort}. " +
                "Make sure the simulator is running with the iFly 737 MAX loaded and the EFB is enabled " +
                "(port 8084 by default — if your iFly install allows changing it, the port in MSFS Blind " +
                "Assist's settings file must match). Then press Retry.");
        }
    }

    private void Navigate()
    {
        if (!_webViewReady)
        {
            ShowError("The browser component is not ready yet. Please try again.");
            return;
        }
        int port = SettingsManager.Current.IFlyEfbPort;
        // Optimistically show the browser; OnNavigationCompleted flips to the error
        // panel if the load fails.
        ShowBrowser();
        _webView.CoreWebView2.Navigate($"http://localhost:{port}/");
    }

    private void ShowBrowser()
    {
        _errorPanel.Visible = false;
        _webView.Visible = true;
    }

    private void ShowError(string message)
    {
        _errorBox.Text = message;
        _webView.Visible = false;
        _errorPanel.Visible = true;
        _retryButton.Focus();
        _announcer.AnnounceImmediate(message);
    }
}
