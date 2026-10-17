using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using MSFSBlindAssist.Services.Volanta;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Volanta;

/// <summary>
/// Volanta's public web map (<see cref="MapUrl"/>) in a browser the pilot never sees, feeding the window's
/// <see cref="VolantaLink"/>. Most blind pilots keep the Volanta app in the notification area to log their flights,
/// where its own map downloads nothing, so the window reads Volanta's web map instead (chosen 2026-10-17 with the
/// PR's author, replacing a debugging port on the Volanta app). The page downloads Volanta's three traffic files,
/// and, logged in, the friends list and the pilot's active flight; this reads each one as the page finishes it,
/// through WebView2's own DevTools access, and hands it to the link, as it does the page's hub frames.
///
/// It downloads only while the Volanta window is in the foreground (VOL-12): <see cref="SetActive"/> sets the
/// browser's own visibility, which Volanta's page follows (its polling runs only while the page is "visible",
/// VOL-11). The browser is a <see cref="CoreWebView2Controller"/> in a window that is never shown, so its visibility
/// is exactly what is set here. Measured with a probe, logged out, 2026-10-17: paused, the page went "hidden" at once
/// and downloaded nothing; resumed, it fetched the Volanta users within 0.3 s, the network pilots within 1.1 s and
/// ATC within 3 s; about 20 % of one CPU core live and 1 to 5 % paused.
///
/// Live, the page polls the Volanta users every 6 s (about 790 KB over the network, 3 MB unpacked), the network
/// pilots every 16 s (about 240 KB) and ATC every 60 s (about 50 KB): about 415 MB an hour while live, plus about
/// 15 MB to load the page. Its ad and analytics trackers are blocked, which changed none of it.
///
/// Logging in: <see cref="ShowLogin"/> opens Volanta's login page in a window of its own
/// (<see cref="VolantaWebMapLoginForm"/>) on the same browser profile (<see cref="ProfileFolder"/>), so the login
/// is kept; that window closes itself once the login is stored, and the map page reloads to pick it up.
/// <see cref="LogOutAsync"/> does what Volanta's own logout does (VOL-13).
/// </summary>
internal sealed class VolantaWebMap : IDisposable
{
    public const string MapUrl = "https://fly.volanta.app/map";
    public const string LoginUrl = "https://fly.volanta.app/auth/login";

    /// <summary>The browser's visibility is ours alone: never let Windows' occlusion check hide the page because
    /// its window is never shown. Hidden pages are otherwise throttled as usual, which is what a pause wants.</summary>
    private const string BrowserArguments = "--disable-features=CalculateNativeWinOcclusion";

    /// <summary>Which users have logged in to the page (the NAMES of the per-user keys Volanta keeps for its "what's
    /// new" notice, VOL-4), and whether a login is stored at all (the login key's NAME): key names only, never a
    /// stored value, and never the login token (VOL-13). Read by <see cref="VolantaLink.ParseStorageCheck"/>.</summary>
    private const string StorageCheckScript =
        "(() => { try { const p = 'last-read-whats-new-version:'; const u = []; let a = false; "
        + "for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i) || ''; "
        + "if (k.startsWith(p)) u.push(k.slice(p.length)); if (k === 'authToken') a = true; } "
        + "return JSON.stringify({ u: u, a: a }); } catch (e) { return ''; } })()";

    /// <summary>The page's ad, analytics and session-recording services (measured on the logged-out map,
    /// 2026-10-16). None of them is needed to show the map or log in; Google and Facebook sign-in pages are not
    /// on the list.</summary>
    private static readonly string[] TrackerHosts =
    {
        "facebook.net", "doubleclick.net", "googleadservices.com", "google-analytics.com", "googletagmanager.com",
        "analytics.google.com", "klaviyo.com", "reddit.com", "posthog.com", "sentry.io",
        "capi-automation.s3.us-east-2.amazonaws.com", "events.mapbox.com",
    };

    /// <summary>The kept browser profile: the pilot's Volanta login lives here.</summary>
    public static string ProfileFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MSFSBlindAssist", "VolantaWebMap");

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(15);

    private readonly VolantaLink _link;
    /// <summary>The browser's parent window: created, never shown.</summary>
    private readonly Form _host = new() { ShowInTaskbar = false, Text = "Volanta web map host" };
    private readonly Dictionary<string, (VolantaLink.FeedKind? Kind, DateTimeOffset At)> _requests = new();
    private readonly Dictionary<VolantaLink.FeedKind, string> _lastBody = new();
    private readonly System.Windows.Forms.Timer _checkTimer = new() { Interval = 5000 };
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private VolantaWebMapLoginForm? _login;
    private int _checks;
    private bool _pageUp, _disposed, _active = true, _retryPending;

    public VolantaWebMapStats Stats { get; } = new();
    /// <summary>Whether the page has a login stored; null until first checked.</summary>
    public bool? LoggedIn { get; private set; }
    /// <summary>Why the page is not up, when it is not; null otherwise.</summary>
    public string? Problem { get; private set; }
    /// <summary>Whether the page is live (downloading), as last set by <see cref="SetActive"/>.</summary>
    public bool Active => _active;
    /// <summary>Raised on the UI thread when <see cref="LoggedIn"/>, <see cref="Problem"/> or <see cref="Active"/>
    /// changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on the UI thread when the login window closed itself because the pilot logged in.</summary>
    public event EventHandler? LoginCompleted;

    /// <summary>The browser's window was destroyed under it (the app closing its windows on exit): the controller is
    /// gone, and nothing may touch it any more.</summary>
    private bool _hostGone;

    public VolantaWebMap(VolantaLink link)
    {
        _link = link;
        _host.HandleDestroyed += (_, _) =>
        {
            _hostGone = true;
            _checkTimer.Stop();
        };
        // Here, not in StartAsync: a browser started again after its process died must not add a second check.
        _checkTimer.Tick += async (_, _) => await CheckStorageAsync();
    }

    private bool Usable => !_disposed && !_hostGone && _controller != null;

    /// <summary>Starts the browser and the map, live. Call on the UI thread.</summary>
    public async Task StartAsync()
    {
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            _env = await CoreWebView2Environment.CreateAsync(null, ProfileFolder, new CoreWebView2EnvironmentOptions(BrowserArguments));
            _controller = await _env.CreateCoreWebView2ControllerAsync(_host.Handle);
            if (_disposed || _hostGone) { _controller.Close(); return; }
            _controller.Bounds = new Rectangle(0, 0, 1280, 800);
            _controller.IsVisible = _active;
            var core = _controller.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            BlockTrackers(core, _env);
            // A new page load (a reload after the login window or a log out, a retry) sends its files anew, and the
            // first copy of each must be taken even when it matches the last copy taken before: measured
            // 2026-10-17, the friends list came again after a log out and a new login and was thrown away as the
            // cached second copy, so the Friends tab stayed empty.
            core.NavigationStarting += (_, _) => _lastBody.Clear();
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += (_, e) => OnProcessFailed(e.ProcessFailedKind);
            await core.CallDevToolsProtocolMethodAsync("Network.enable",
                "{\"maxTotalBufferSize\":20000000,\"maxResourceBufferSize\":8000000}");
            core.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += OnResponseReceived;
            core.GetDevToolsProtocolEventReceiver("Network.loadingFinished").DevToolsProtocolEventReceived += OnLoadingFinished;
            core.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived += OnLoadingFailed;
            core.GetDevToolsProtocolEventReceiver("Network.webSocketFrameReceived").DevToolsProtocolEventReceived += OnHubFrame;
            Log.Info("VolantaWebMap", $"Opening {MapUrl} (profile {ProfileFolder})");
            core.Navigate(MapUrl);
            _checkTimer.Start();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or COMException
            or ArgumentException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("VolantaWebMap", $"Could not start: {ex.Message}");
            SetProblem($"The web map could not start: {ex.Message}");
            _link.SetSourceStatus(VolantaLinkStatus.NoAccess);
        }
    }

    /// <summary>Live or paused. Paused, the page is hidden, so Volanta's own code stops downloading; live again,
    /// it fetches what it missed straight away, and the link starts its "nothing new for a while" count from now,
    /// so the lists are not called stale for a pause the pilot chose.</summary>
    public void SetActive(bool active)
    {
        if (active == _active || _disposed || _hostGone) return;
        _active = active;
        // Measured 2026-10-17: the pause timer fired after the app closed the browser's window on exit, and setting
        // IsVisible on the closed controller threw.
        try { if (_controller != null) _controller.IsVisible = active; }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            Log.Debug("VolantaWebMap", $"Visibility not set, the browser is closed: {ex.Message}");
            return;
        }
        Log.Info("VolantaWebMap", active ? "Live: the Volanta window is in the foreground" : "Paused: the Volanta window is in the background");
        if (active) _link.Resumed();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens Volanta's login page in a window of its own on the same profile. The window closes itself as
    /// soon as the login is stored (<see cref="VolantaWebMapLoginForm.ClosedOnLogin"/>), raising
    /// <see cref="LoginCompleted"/>; closed either way, the map page reloads to pick up the login. False when it
    /// cannot open: the web map is not running.</summary>
    public bool ShowLogin()
    {
        if (_env == null || !Usable) return false;
        if (_login is { IsDisposed: false })
        {
            _login.ShowAndFocus();
            return true;
        }
        var login = new VolantaWebMapLoginForm(_env, LoginUrl, BlockTrackers);
        _login = login;
        login.FormClosed += (_, _) =>
        {
            _login = null;
            if (!Usable) return;
            Log.Info("VolantaWebMap", login.ClosedOnLogin ? "Logged in: login window closed itself, reloading the map"
                : "Login window closed: reloading the map");
            Reload();
            if (!login.ClosedOnLogin) return;
            SetLoggedIn(true);
            LoginCompleted?.Invoke(this, EventArgs.Empty);
        };
        login.ShowAndFocus();
        return true;
    }

    /// <summary>Logs out of Volanta in the web map, as Volanta's own logout does (read in 1.19.2's code: it only
    /// clears its stored session in the browser, no request): the profile's site storage and cookies are cleared,
    /// the link forgets the friends list and the pilot's own flight, and the map reloads logged out. False when
    /// it could not.</summary>
    public async Task<bool> LogOutAsync()
    {
        if (!Usable || _controller?.CoreWebView2 is not { } core) return false;
        try
        {
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllDomStorage | CoreWebView2BrowsingDataKinds.Cookies);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            Log.Warn("VolantaWebMap", $"Log out failed: {ex.Message}");
            return false;
        }
        Log.Info("VolantaWebMap", "Logged out: site storage and cookies cleared, reloading the map");
        _link.LoggedOut();
        _lastBody.Clear();   // the next login's files must all be taken (see NavigationStarting)
        SetLoggedIn(false);
        Reload();
        return true;
    }

    private void Reload()
    {
        if (!Usable) return;
        // Down until the reload completes, so one that fails is tried again (OnNavigationCompleted).
        _pageUp = false;
        try { _controller!.CoreWebView2.Reload(); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            Log.Warn("VolantaWebMap", $"Reload failed: {ex.Message}");
            _ = RetryAsync();
        }
    }

    private void SetLoggedIn(bool? loggedIn)
    {
        if (loggedIn == LoggedIn) return;
        LoggedIn = loggedIn;
        Log.Info("VolantaWebMap", $"Logged in: {loggedIn?.ToString() ?? "unknown"}");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void BlockTrackers(CoreWebView2 core, CoreWebView2Environment env)
    {
        // Only addresses that can be a tracker's wait for this handler on the UI thread: a filter of "*" held every
        // request the page makes, the 3 MB traffic files among them, until the UI thread was free.
        foreach (string filter in TrackerFilters())
            core.AddWebResourceRequestedFilter(filter, CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && IsTracker(uri))
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
    }

    /// <summary>Whether a request goes to one of the page's ad or analytics services (<see cref="TrackerHosts"/>,
    /// and the Facebook and Google tracking paths on their main hosts), which the web map blocks.</summary>
    internal static bool IsTracker(Uri uri)
    {
        string host = uri.Host;
        foreach (string h in TrackerHosts)
            if (host.Equals(h, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))
                return true;
        string path = uri.AbsolutePath;
        if ((host.Equals("www.facebook.com", StringComparison.OrdinalIgnoreCase) || host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase))
            && path.StartsWith("/tr", StringComparison.OrdinalIgnoreCase)) return true;
        bool google = host.StartsWith("www.google.", StringComparison.OrdinalIgnoreCase) || host.StartsWith("google.", StringComparison.OrdinalIgnoreCase);
        return google && (path.StartsWith("/ccm/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/ads/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/pagead/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The request filters that hand a request to <see cref="IsTracker"/>: every address that can be one of
    /// its trackers, and a few more (a filter's "*" is looser than its checks), so IsTracker still decides.</summary>
    internal static IEnumerable<string> TrackerFilters()
    {
        foreach (string h in TrackerHosts)
        {
            yield return $"*://{h}/*";
            yield return $"*://*.{h}/*";
        }
        yield return "*://facebook.com/tr*";
        yield return "*://www.facebook.com/tr*";
        foreach (string path in new[] { "/ccm/", "/ads/", "/pagead/" })
        {
            yield return $"*://google.*{path}*";
            yield return $"*://www.google.*{path}*";
        }
    }

    /// <summary>A key that tells two downloads of a file apart cheaply: the page asks for the Volanta users' file
    /// twice in a row, the second straight from its cache, and parsing 3 MB twice is wasted. Taken from the DevTools
    /// answer that carries the file, so it is worked out on the UI thread without reading the file. The keys kept are
    /// forgotten at every page load and log out, so a new load's first copy is always taken.</summary>
    internal static string BodyKey(string body)
        => body.Length <= 512 ? body : string.Concat(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", body.AsSpan(0, 256), body.AsSpan(body.Length - 256));

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Log.Info("VolantaWebMap", $"Navigation completed: ok={e.IsSuccess} status={e.HttpStatusCode} error={e.WebErrorStatus}");
        if (e.IsSuccess)
        {
            _pageUp = true;
            SetProblem(null);
            _link.SetSourceStatus(VolantaLinkStatus.Connected);
            // A page that came up again (a reload after the login window or a log out) counts from now (VOL-2): the
            // files kept from before it are not new.
            _link.Resumed();
            if (!_active) _ = NudgeHiddenAsync();
        }
        // A cancelled load gave way to a newer one, whose own completion decides.
        else if (!_pageUp && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            SetProblem($"Volanta's web map did not load ({e.WebErrorStatus}). Trying again in 15 seconds.");
            _ = RetryAsync();
        }
    }

    /// <summary>A browser process failed. The browser itself gone, the controller went with it: a new one is started.
    /// The page's renderer gone or hung: the page is loaded again. Any other (the GPU, a utility, an iframe's
    /// renderer) WebView2 restarts by itself, and the page carries on.</summary>
    private void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        Log.Warn("VolantaWebMap", $"Browser process failed: {kind}");
        if (_disposed || _hostGone) return;
        switch (kind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                SetProblem("The web map's browser stopped. Restarting it.");
                _ = RestartBrowserAsync();
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                SetProblem("The web map's page stopped. Loading it again.");
                _ = RetryAsync();
                break;
        }
    }

    /// <summary>Starts a new browser after the old one's process died: its controller cannot load anything again.</summary>
    private async Task RestartBrowserAsync()
    {
        _pageUp = false;
        _link.SetSourceStatus(VolantaLinkStatus.WaitingForPage);
        var dead = _controller;
        _controller = null;
        _requests.Clear();
        try { dead?.Close(); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        await Task.Delay(RetryAfter);
        if (_disposed || _hostGone) return;
        await StartAsync();
    }

    /// <summary>A page that finished loading while paused is told at once that it is hidden (<see cref="HiddenNudgeScript"/>).</summary>
    private async Task NudgeHiddenAsync()
    {
        try
        {
            if (_controller?.CoreWebView2 is { } core) await core.ExecuteScriptAsync(HiddenNudgeScript);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Debug("VolantaWebMap", $"Hidden nudge failed: {ex.Message}");
        }
    }

    /// <summary>Loads the map again in <see cref="RetryAfter"/>: one wait at a time, and none once the page came up
    /// meanwhile (another load succeeded).</summary>
    private async Task RetryAsync()
    {
        _pageUp = false;
        _link.SetSourceStatus(VolantaLinkStatus.WaitingForPage);
        if (_retryPending) return;
        _retryPending = true;
        try { await Task.Delay(RetryAfter); }
        finally { _retryPending = false; }
        if (_disposed || _hostGone || _pageUp) return;
        try { _controller?.CoreWebView2.Navigate(MapUrl); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            Log.Warn("VolantaWebMap", $"Retry failed: {ex.Message}");
        }
    }

    private void OnResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var p = doc.RootElement;
            if (!p.TryGetProperty("requestId", out var idEl) || idEl.GetString() is not { } id
                || !p.TryGetProperty("response", out var response)) return;
            string url = response.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            int status = response.TryGetProperty("status", out var st) && st.TryGetInt32(out int s) ? s : 0;
            var kind = status == 200 ? VolantaLink.FeedKindForUrl(url) : null;
            _requests[id] = (kind, DateTimeOffset.UtcNow);
            // Requests that never finish (a page reload) must not pile up.
            if (_requests.Count > 2000)
                foreach (string old in _requests.Where(r => DateTimeOffset.UtcNow - r.Value.At > TimeSpan.FromMinutes(2)).Select(r => r.Key).ToList())
                    _requests.Remove(old);
        }
        catch (JsonException) { }
    }

    private void OnLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (doc.RootElement.TryGetProperty("requestId", out var id) && id.GetString() is { } requestId) _requests.Remove(requestId);
        }
        catch (JsonException) { }
    }

    private async void OnLoadingFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string requestId;
        long bytes;
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            var p = doc.RootElement;
            if (!p.TryGetProperty("requestId", out var idEl) || idEl.GetString() is not { } id) return;
            requestId = id;
            bytes = p.TryGetProperty("encodedDataLength", out var len) && len.TryGetDouble(out double d) ? (long)d : 0;
        }
        catch (JsonException) { return; }
        if (!_requests.Remove(requestId, out var request)) return;
        Stats.Add(DateTimeOffset.UtcNow, VolantaWebMapStats.KindOf(request.Kind), bytes);
        if (request.Kind is not VolantaLink.FeedKind kind || _controller?.CoreWebView2 is not { } core) return;

        string result;
        try
        {
            result = await core.CallDevToolsProtocolMethodAsync("Network.getResponseBody",
                JsonSerializer.Serialize(new { requestId }));
        }
        catch (Exception ex)   // an async void handler: nothing may escape it
        {
            Log.Debug("VolantaWebMap", $"{kind} body not readable: {ex.Message}");
            return;
        }
        // The answer carries the file whole, so its key tells the cached second copy too, without reading it here.
        string key = BodyKey(result);
        if (_lastBody.TryGetValue(kind, out var last) && last == key) return;   // the cached second copy
        _lastBody[kind] = key;
        // Reading the answer and parsing the file (3 MB) takes tens of milliseconds: off the UI thread.
        _ = Task.Run(() =>
        {
            try
            {
                string? body;
                using (var doc = JsonDocument.Parse(result)) body = VolantaLink.BodyFromResult(doc.RootElement);
                if (body == null) return;
                Log.Debug("VolantaWebMap", $"{kind}: {bytes / 1024} KB over the network, {body.Length / 1024} KB of text");
                _link.IngestBody(kind, body);
            }
            catch (Exception ex) { Log.Warn("VolantaWebMap", $"{kind} not taken: {ex.Message}"); }
        });
    }

    private void OnHubFrame(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
            if (doc.RootElement.TryGetProperty("response", out var frame) && frame.TryGetProperty("payloadData", out var data)
                && data.GetString() is { Length: > 0 } payload)
                _link.IngestHubFrame(payload);
        }
        catch (JsonException) { }
    }

    /// <summary>Volanta's page starts out assuming it is visible and learns otherwise only from a visibilitychange
    /// event, which a page loaded while hidden (a reload after the login window, a retry) never gets: it would
    /// poll through the pause. So while paused, the page is told again, every check, that it is hidden. Measured
    /// with a probe, 2026-10-17: a page loaded hidden polled every 6 s until this event, then not at all (Volanta
    /// 1.19.2's map service sets <c>isTabVisible</c> only in that handler).</summary>
    private const string HiddenNudgeScript =
        "document.visibilityState === 'hidden' && document.dispatchEvent(new Event('visibilitychange'))";

    private async Task CheckStorageAsync()
    {
        if (!Usable || _controller?.CoreWebView2 is not { } core || !_pageUp) return;
        try
        {
            if (!_active) await core.ExecuteScriptAsync(HiddenNudgeScript);
            string result = await core.ExecuteScriptAsync(StorageCheckScript);
            // ExecuteScriptAsync answers in JSON: the script's string comes back quoted.
            string value = JsonSerializer.Deserialize<string>(result) ?? "";
            _link.IngestStorageCheck(value);
            if (VolantaLink.ParseStorageCheck(value).LoggedIn is bool loggedIn) SetLoggedIn(loggedIn);
        }
        catch (Exception ex) when (ex is COMException or JsonException or InvalidOperationException or NotSupportedException)
        {
            Log.Debug("VolantaWebMap", $"Storage check failed: {ex.Message}");
        }
        if (++_checks % 12 == 0 && _active) Log.Info("VolantaWebMap", Stats.Summary(DateTimeOffset.UtcNow));
    }

    private void SetProblem(string? problem)
    {
        if (problem == Problem) return;
        Problem = problem;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the login window and the browser, with the Volanta window. Call on the UI thread.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _checkTimer.Stop();
        _checkTimer.Dispose();
        if (_login is { IsDisposed: false } login) { login.Close(); login.Dispose(); }
        if (!_hostGone)
        {
            try { _controller?.Close(); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException) { }
        }
        _host.Dispose();
    }
}
