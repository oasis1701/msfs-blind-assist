using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Volanta;

/// <summary>
/// Volanta's own login page, on the web map's browser profile (<see cref="VolantaWebMap"/>), so the pilot
/// can log in to Volanta for the web map. The pilot types the login into Volanta's page; this window never reads
/// it. Its own top-level window, so the pilot can Alt+Tab to it. It closes itself once Volanta has stored the login
/// (<see cref="ClosedOnLogin"/>, the owner's request, 2026-10-17), checked every second by the NAME of Volanta's
/// storage key, never its value (VOL-13); Escape or Alt+F4 closes it sooner. The web map then reloads to pick up the
/// login.
/// </summary>
internal sealed class VolantaWebMapLoginForm : Form
{
    private const string CloseMessage = "msfsba-volanta-login-close";

    /// <summary>Whether Volanta has stored a login: the key's name only (Volanta's own <c>authToken</c>).</summary>
    private const string LoginStoredScript =
        "(() => { try { for (let i = 0; i < localStorage.length; i++) if (localStorage.key(i) === 'authToken') return true; } "
        + "catch (e) {} return false; })()";

    private readonly System.Windows.Forms.Timer _loginCheck = new() { Interval = 1000 };
    private bool _checking;

    /// <summary>The window closed itself because the pilot logged in.</summary>
    public bool ClosedOnLogin { get; private set; }

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, AccessibleName = "Volanta login page" };
    private readonly CoreWebView2Environment _env;
    private readonly string _url;
    private readonly Action<CoreWebView2, CoreWebView2Environment> _blockTrackers;
    private bool _started;

    public VolantaWebMapLoginForm(CoreWebView2Environment env, string url, Action<CoreWebView2, CoreWebView2Environment> blockTrackers)
    {
        _env = env;
        _url = url;
        _blockTrackers = blockTrackers;
        Text = "Log in to Volanta for the web map (closes by itself once you are logged in; Escape closes it)";
        Width = 1000;
        Height = 750;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Controls.Add(_web);
        Shown += async (_, _) => await StartAsync();
        _loginCheck.Tick += async (_, _) => await CheckLoginAsync();
        FormClosed += (_, _) =>
        {
            _loginCheck.Stop();
            _loginCheck.Dispose();
        };
    }

    private async Task CheckLoginAsync()
    {
        if (_checking || IsDisposed || _web.CoreWebView2 is not { } core) return;
        _checking = true;
        try
        {
            if (await core.ExecuteScriptAsync(LoginStoredScript) == "true" && !IsDisposed)
            {
                Log.Info("VolantaWebMap", "Login detected in the login window: closing it");
                ClosedOnLogin = true;
                Close();
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Debug("VolantaWebMap", $"Login check failed: {ex.Message}");
        }
        finally { _checking = false; }
    }

    /// <summary>Shows the window and puts the screen reader in the page.</summary>
    public void ShowAndFocus()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        _web.Focus();
    }

    private async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        try
        {
            await _web.EnsureCoreWebView2Async(_env);
            // Escape works from the first moment: the window may have closed while the browser started.
            if (IsDisposed || _web.CoreWebView2 is not { } core) return;
            core.Settings.AreDevToolsEnabled = false;
            _blockTrackers(core, _env);
            // Escape inside the page never reaches this window's KeyDown (the browser owns that focus), so a small
            // listener posts a close message, as the EFB windows do.
            core.WebMessageReceived += (_, e) => { if (e.TryGetWebMessageAsString() == CloseMessage) Close(); };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.addEventListener('keydown',function(e){if(e.key==='Escape'){window.chrome.webview.postMessage('"
                + CloseMessage + "');}},true);");
            if (IsDisposed) return;
            core.Navigate(_url);
            _web.Focus();
            _loginCheck.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException or ObjectDisposedException)
        {
            Log.Warn("VolantaWebMap", $"Login window could not start: {ex.Message}");
            if (!IsDisposed) Close();
        }
    }
}
