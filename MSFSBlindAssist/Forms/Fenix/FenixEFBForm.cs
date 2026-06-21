using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// Hosts the Fenix A320 EFB web UI (http://localhost:8083/#/efb) in a WebView2.
///
/// Unlike the FlyByWire flyPad (FbwEfbForm), the Fenix EFB website is ALREADY
/// screen-reader accessible when opened in a browser, so there is no scraping,
/// no accessible-HTML shim, and no Coherent/SimConnect client here — we simply
/// host the live site. The URL is reachable whenever the Fenix A320 is loaded
/// in the sim.
///
/// On a navigation failure (aircraft still loading, server not up, transient
/// hiccup) the form swaps the browser for an accessible retry panel and speaks
/// the failure, so a blind user gets clear feedback instead of WebView2's raw
/// error page.
/// </summary>
public class FenixEFBForm : Form
{
    private const string EfbUrl = "http://localhost:8083/#/efb";

    private readonly ScreenReaderAnnouncer _announcer;

    private WebView2 _webView = null!;
    private Panel _errorPanel = null!;
    private Label _errorLabel = null!;
    private Button _retryButton = null!;
    private bool _webViewReady;

    public FenixEFBForm(ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        Text = "Fenix A320 EFB";
        AccessibleName = "Fenix A320 EFB";
        Width = 1000;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

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
            Location = new System.Drawing.Point(12, 60),
        };
        _retryButton.Click += (_, _) => Navigate();

        _errorLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(700, 0),
            Location = new System.Drawing.Point(12, 12),
            Text = NotReachableMessage,
        };

        _errorPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            AccessibleName = "Fenix EFB status",
        };
        _errorPanel.Controls.Add(_errorLabel);
        _errorPanel.Controls.Add(_retryButton);
        Controls.Add(_errorPanel);
    }

    private const string NotReachableMessage =
        "Fenix EFB is not reachable at http://localhost:8083. " +
        "Make sure the Fenix A320 is fully loaded, then press Retry.";

    private async Task InitWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webViewReady = true;
            Navigate();
        }
        catch (Exception ex)
        {
            ShowError("The browser component could not start. " + ex.Message);
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
        _errorLabel.Text = message;
        _webView.Visible = false;
        _errorPanel.Visible = true;
        _retryButton.Focus();
        _announcer.AnnounceImmediate(message);
    }

    /// <summary>Show (or re-show) and focus the window. Reused across opens.</summary>
    public void ShowForm()
    {
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
