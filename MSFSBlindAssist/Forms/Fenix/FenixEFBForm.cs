using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// Hosts the Fenix A320 EFB web UI (http://127.0.0.1:8083/?target=external#/efb) in a
/// WebView2.
///
/// The v3 EFB page is NOT screen-reader accessible on its own. This window
/// adds the accessibility layer on top of the live page and catches its dead ends
/// (pages a screen reader cannot use, a failed load) instead of leaving a blind user
/// in them. The URL is reachable whenever the Fenix A320 is loaded in the sim.
///
/// On a navigation failure (aircraft still loading, server not up, transient
/// hiccup) the form swaps the browser for an accessible retry panel, with the failure
/// text in a tabbable read-only TextBox, and speaks the failure, so a blind user gets
/// clear feedback instead of WebView2's raw error page.
/// </summary>
public class FenixEFBForm : Form
{
    // The external target, named explicitly (the owner's decision). The page's own
    // target detection already falls back to external for WebView2; pinning it removes
    // that reliance (Tab and the paging keys reach the page, no simulated sleep).
    // 127.0.0.1, never localhost ([A320-40]) - the gateway listens on IPv4
    // only, and a client that tries ::1 first pays for the refusal on every new connection.
    internal const string EfbUrl = "http://127.0.0.1:8083/?target=external#/efb";

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly ScreenReaderAnnouncer _announcer;
    // A dead end is caught once: the router may change the address twice on its way into the MCDU.
    private readonly FenixEfbDeadEndGate _deadEndGate = new();

    private WebView2 _webView = null!;
    private Panel _errorPanel = null!;
    private TextBox _errorBox = null!;
    private Button _retryButton = null!;
    private bool _webViewReady;
    private IntPtr _previousWindow = IntPtr.Zero;

    public FenixEFBForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        Text = "Fenix EFB";
        AccessibleName = "Fenix EFB";
        Width = 1000;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        // Hide instead of dispose on close, so reopening returns the user to the
        // EFB page they were on (and avoids re-initializing WebView2 every time).
        // The form is disposed for real on aircraft swap (MainForm.SwitchAircraft,
        // which calls Dispose() directly and bypasses this handler). Any
        // user-initiated close — the X button / Alt+F4 (UserClosing) or our own
        // Escape Close() (CloseReason.None) — just hides; only real app/OS shutdown
        // is allowed to tear the window down.
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

        BuildUi();
        _ = InitWebViewAsync();
    }

    private void BuildUi()
    {
        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        Controls.Add(_webView);

        _retryButton = new Button
        {
            Text = "&Retry",
            AutoSize = true,
            Location = new System.Drawing.Point(12, 120),
        };
        _retryButton.Click += (_, _) => Navigate();

        // Read-only multiline TextBox, not a Label: the status text must be a
        // tab-stoppable control so a screen reader can review the failure text on
        // demand ([VAT-13]); same build as IFlyEfbForm's.
        _errorBox = new TextBox
        {
            Location = new System.Drawing.Point(12, 12),
            Size = new System.Drawing.Size(700, 100),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Fenix EFB status message",
            Text = NotReachableMessage,
        };

        _errorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AccessibleName = "Fenix EFB status",
        };
        _errorPanel.Controls.Add(_errorBox);
        _errorPanel.Controls.Add(_retryButton);
        Controls.Add(_errorPanel);
    }

    // Names the address the window tried, so the failure text says where it looked.
    internal const string NotReachableMessage =
        "Fenix EFB is not reachable at http://127.0.0.1:8083. " +
        "Make sure the Fenix A320 is fully loaded, then press Retry.";

    private async Task InitWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            var s = _webView.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = false;
            s.IsZoomControlEnabled = false;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            // Fires for in-page fragment changes too (IsNewDocument false), which is how the
            // single-page EFB moves between its pages: the one hook that sees the MCDU and FAP.
            _webView.CoreWebView2.SourceChanged += OnSourceChanged;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            // Escape from INSIDE the web content never reaches ProcessCmdKey (the
            // WebView2 browser process owns that focus), and the WinForms WebView2
            // wrapper doesn't expose the controller's AcceleratorKeyPressed event.
            // So a tiny injected capture-phase listener posts a close message: Escape
            // closes the window, except while a CDK overlay pane with content is open
            // (a "More options" menu, a drop-down panel, the Panel States dialog), where
            // the page's own overlay closes on Escape instead.
            // ProcessCmdKey still covers Escape on the error panel.
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.addEventListener('keydown',function(e){" +
                "if(e.key==='Escape'&&!document.querySelector('.cdk-overlay-pane > *')){window.chrome.webview.postMessage('msfsba-efb-close');}}," +
                "true);");
            // The accessibility layer: registered with the document-created injection, so it
            // is in place before the first page script runs and again for every new document. Read once,
            // here; a missing file only costs the layer, never the page.
            string? layer = ReadLayerScript();
            if (layer != null)
            {
                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(layer);
            }
            _webViewReady = true;
            Navigate();
        }
        catch (Exception ex)
        {
            ShowError("The browser component could not start. " + ex.Message);
        }
    }

    /// <summary>
    /// The layer script, copied next to the executable by the project file. Null (and a log line) when
    /// it is missing or unreadable: the EFB then loads as the page is, which a screen reader cannot use.
    /// </summary>
    private static string? ReadLayerScript()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "fenix-efb-accessibility.js");
        try
        {
            string text = File.ReadAllText(path);
            if (!string.IsNullOrWhiteSpace(text)) return text;
            Log.Warn("FenixEFB", $"Accessibility layer not loaded ({path}): the file is empty");
            return null;
        }
        catch (Exception ex)
        {
            Log.Warn("FenixEFB", $"Accessibility layer not loaded ({path}): {ex.Message}");
            return null;
        }
    }

    private void Navigate()
    {
        if (!_webViewReady)
        {
            ShowError("The browser component is not ready yet. Please try again.");
            return;
        }
        // Optimistically show the browser; OnNavigationCompleted flips to the
        // error panel if the load fails.
        ShowBrowser();
        _webView.CoreWebView2.Navigate(EfbUrl);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        // Fired on the UI thread. Close() runs the hide-and-restore FormClosing path.
        if (e.TryGetWebMessageAsString() == "msfsba-efb-close")
        {
            Close();
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        // Fired on the UI thread. The MCDU and FAP pages cannot be read with a screen reader:
        // step back to the EFB home and say where the working windows are.
        string source = _webView.CoreWebView2.Source;
        FenixEfbRoute route = FenixEfbRoutes.Classify(source);
        string? message = FenixEfbRoutes.DeadEndMessage(route);
        if (message == null) return;
        if (!_deadEndGate.ShouldCatch(route, Environment.TickCount64))
        {
            Log.Debug("FenixEFB", $"EFB page {source} is the {route} caught under {FenixEfbDeadEndGate.WindowMs} ms ago; left to that catch");
            return;
        }

        Log.Debug("FenixEFB", $"EFB page {source} is the unreadable {route}; back to the EFB home");
        _webView.CoreWebView2.Navigate(EfbUrl);
        _announcer.AnnounceImmediate(message);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            ShowBrowser();
            _webView.Focus();
        }
        else
        {
            ShowError(NotReachableMessage);
        }
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

    /// <summary>Show (or re-show) and focus the window. Reused across opens.</summary>
    public void ShowForm()
    {
        // Remember who had focus so we can restore it when the window is dismissed.
        _previousWindow = GetForegroundWindow();
        if (!Visible) Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        if (_webViewReady && _webView.Visible)
        {
            _webView.Focus();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
