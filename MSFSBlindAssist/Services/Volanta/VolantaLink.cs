using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Volanta;

public enum VolantaLinkStatus
{
    /// <summary>Not started.</summary>
    Stopped,
    /// <summary>Nothing answers on <see cref="VolantaLink.DebugPort"/>: Volanta is closed, or was started
    /// without live map access.</summary>
    NoAccess,
    /// <summary>The port answers, but the browser behind it is not Volanta: another program's debugging port
    /// holds the number, so a Volanta started with the switch could not open it, and a restart would not help
    /// until that program is closed.</summary>
    PortTaken,
    /// <summary>The port answers but Volanta's main page is not there yet (Volanta still starting).</summary>
    WaitingForPage,
    Connected,
}

/// <summary>Everything the link has read so far. Immutable: <see cref="VolantaLink.Snapshot"/> swaps a new one
/// in for each change. Received times are this PC's clock, for "data N seconds old".</summary>
public sealed record VolantaLinkSnapshot
{
    public VolantaLinkStatus Status { get; init; } = VolantaLinkStatus.Stopped;
    public VolantaFeed<VolantaFlight>? VolantaFlights { get; init; }
    public DateTimeOffset? VolantaFlightsReceived { get; init; }
    public VolantaFeed<VolantaFlight>? NetworkFlights { get; init; }
    public DateTimeOffset? NetworkFlightsReceived { get; init; }
    public VolantaFeed<VolantaController>? Controllers { get; init; }
    public DateTimeOffset? ControllersReceived { get; init; }
    /// <summary>Volanta's own last report of the pilot's position, as reported (not moved on).</summary>
    public VolantaOwnPosition? OwnVolantaPosition { get; init; }
    public VolantaOwnFlight? OwnFlight { get; init; }
    /// <summary>Volanta's window is minimized (or hidden to the tray), so its map has stopped downloading
    /// traffic; null until first checked.</summary>
    public bool? VolantaPaused { get; init; }
    /// <summary>The logged-in Volanta user's id, which is the user id on the pilot's own entry in Volanta's
    /// flight file; null until read.</summary>
    public string? OwnUserId { get; init; }
    /// <summary>The pilot's Volanta friends; null when neither Volanta's download nor a saved copy was seen.</summary>
    public IReadOnlyList<VolantaFriend>? Friends { get; init; }
    /// <summary>When Volanta's own friends download was caught; null while the list is the saved copy.</summary>
    public DateTimeOffset? FriendsReceived { get; init; }
    /// <summary>When the saved copy in use was saved; null once Volanta's download replaced it.</summary>
    public DateTimeOffset? FriendsSavedAt { get; init; }
}

/// <summary>
/// Reads Volanta's live map from Volanta itself. Volanta, started with live map access
/// (<see cref="VolantaLauncher"/>), answers on a local Chromium debugging port; the link attaches to its main
/// page, turns on network events, and, each time Volanta finishes downloading one of its traffic files or its
/// friends list, asks the page for that body. It also reads Volanta's hub messages for the pilot's own flight
/// and position and for friend changes, and every few seconds asks Volanta whether its window is minimized. It
/// never requests anything from Volanta's servers itself: they refuse every outside client (docs/volanta.md).
/// Reconnects on its own while started.
/// </summary>
public sealed class VolantaLink : IDisposable
{
    public const int DebugPort = 9223;
    private const string FeedHost = "webassets.volanta.app";
    private const string ApiHost = "api.volanta.app";
    private const string FriendsPath = "/api/v1/Relationships/Friends";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);
    /// <summary>Right after this app starts Volanta, look for its page this often: Volanta downloads the friends
    /// list once, about 0.6 s after its page appears (measured 2026-10-15), and only a link already attached
    /// sees it.</summary>
    private static readonly TimeSpan LaunchRetryDelay = TimeSpan.FromMilliseconds(150);
    /// <summary>How long after this app starts Volanta the link looks for its page quickly; the window also
    /// speaks once if Volanta has not connected by then (it may have updated and restarted itself without the
    /// switch, or the port may be taken).</summary>
    public static readonly TimeSpan LaunchWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WindowCheckInterval = TimeSpan.FromSeconds(5);
    /// <summary>Asks Volanta whether its window is minimized or hidden, and which user is logged in. The user id is
    /// read from the NAME of the storage key Volanta keeps per user for its "what's new" notice; no stored value,
    /// and never the login token, is read.</summary>
    private const string WindowCheckScript =
        "(() => { try { const w = window.electron && window.electron.currentWindow; "
        + "const s = !w ? '' : ((w.isMinimized() || !w.isVisible()) ? 'paused' : 'live'); "
        + "const p = '" + UserKeyPrefix + "'; let u = ''; "
        + "for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i) || ''; "
        + "if (k.startsWith(p)) { u = k.slice(p.length); break; } } "
        + "return JSON.stringify({ s: s, u: u }); } catch (e) { return ''; } })()";
    private const string UserKeyPrefix = "last-read-whats-new-version:";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    internal enum FeedKind { VolantaFlights, NetworkFlights, Controllers, Friends }

    private readonly object _gate = new();
    private readonly VolantaFriendsCache _friendsCache;
    private VolantaLinkSnapshot _snapshot = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _launchUntilTicks;

    public VolantaLink(VolantaFriendsCache? friendsCache = null) => _friendsCache = friendsCache ?? new VolantaFriendsCache();

    /// <summary>This app is about to start Volanta: look for its page quickly for a while, so the link is
    /// attached before Volanta downloads the friends list.</summary>
    public void ExpectLaunch() => Interlocked.Exchange(ref _launchUntilTicks, (DateTime.UtcNow + LaunchWindow).Ticks);

    private TimeSpan CurrentRetryDelay
        => DateTime.UtcNow.Ticks < Interlocked.Read(ref _launchUntilTicks) ? LaunchRetryDelay : RetryDelay;

    /// <summary>Raised on a background thread after any change to <see cref="Snapshot"/>.</summary>
    public event EventHandler? Changed;

    public VolantaLinkSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loop = Task.Run(() => RunAsync(ct));
        }
        // Until Volanta's own download is seen, the saved copy says who the friends are.
        if (Snapshot.Friends == null && _friendsCache.Load() is { } saved)
            Update(s => s.Friends != null ? s : s with { Friends = saved.Friends, FriendsSavedAt = saved.SavedAt });
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate) { cts = _cts; _cts = null; _loop = null; }
        if (cts == null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        Update(s => s with { Status = VolantaLinkStatus.Stopped });
    }

    public void Dispose() => Stop();

    private void Update(Func<VolantaLinkSnapshot, VolantaLinkSnapshot> change)
    {
        lock (_gate) _snapshot = change(_snapshot);
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Warn("Volanta", $"Changed handler threw: {ex.Message}"); }
    }

    private void SetStatus(VolantaLinkStatus status)
    {
        lock (_gate)
        {
            // A loop still winding down after Stop must not overwrite Stopped.
            if (_cts == null && status != VolantaLinkStatus.Stopped) return;
            if (_snapshot.Status == status) return;
        }
        Log.Info("Volanta", $"Live map link: {status}");
        Update(s => s with { Status = status });
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string? wsUrl = await FindPageAsync(ct).ConfigureAwait(false);
                if (wsUrl != null) await ReadPageAsync(wsUrl, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Log.Debug("Volanta", $"Live map link dropped: {ex.Message}"); }

            if (ct.IsCancellationRequested) break;
            if (Snapshot.Status == VolantaLinkStatus.Connected) SetStatus(VolantaLinkStatus.WaitingForPage);
            try { await Task.Delay(CurrentRetryDelay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Whether the browser answering on <see cref="DebugPort"/> is Volanta: null when nothing answers,
    /// false when another program's debugging port holds the number (<see cref="VolantaLinkStatus.PortTaken"/>),
    /// so it is never mistaken for Volanta. Chromium names the app in the user agent it reports there
    /// ("Volanta/1.19.2").</summary>
    public static async Task<bool?> IsVolantaOnPortAsync(CancellationToken ct = default)
    {
        try
        {
            string version = await Http.GetStringAsync($"http://127.0.0.1:{DebugPort}/json/version", ct).ConfigureAwait(false);
            return version.Contains("Volanta/", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>The debugger socket of Volanta's main page, or null (with the status saying why).</summary>
    private async Task<string?> FindPageAsync(CancellationToken ct)
    {
        string list;
        try { list = await Http.GetStringAsync($"http://127.0.0.1:{DebugPort}/json/list", ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            SetStatus(VolantaLinkStatus.NoAccess);
            return null;
        }
        string? url = FindVolantaPage(list);
        if (url == null)
        {
            // No Volanta page: Volanta still starting, or not Volanta at all on the port.
            bool? volanta = await IsVolantaOnPortAsync(ct).ConfigureAwait(false);
            SetStatus(volanta == false ? VolantaLinkStatus.PortTaken : VolantaLinkStatus.WaitingForPage);
        }
        return url;
    }

    /// <summary>The main page's debugger socket URL from a /json/list answer: the page Volanta serves from its
    /// own local web server (on 127.0.0.1, port 17580 in 1.19.2; any loopback page is taken, so a port change
    /// never leaves the link waiting), whichever Volanta screen it shows.</summary>
    internal static string? FindVolantaPage(string listJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(listJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                if (!t.TryGetProperty("type", out var type) || type.GetString() != "page") continue;
                if (!t.TryGetProperty("url", out var url) || !IsVolantaPageUrl(url.GetString())) continue;
                if (t.TryGetProperty("webSocketDebuggerUrl", out var ws) && ws.GetString() is { Length: > 0 } s) return s;
            }
        }
        catch (JsonException) { }
        return null;
    }

    private static bool IsVolantaPageUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback;

    private async Task ReadPageAsync(string wsUrl, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(4000);
            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token).ConfigureAwait(false);
        }

        using var pageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendLock = new SemaphoreSlim(1, 1);
        int nextId = 0;
        var pendingRequests = new Dictionary<string, FeedKind>();   // network requestId → which file
        var bodyCalls = new Dictionary<int, FeedKind>();            // our getResponseBody call id → which file
        int windowCheckId = -1;

        async Task<int> SendAsync(string method, object? parameters)
        {
            int id = Interlocked.Increment(ref nextId);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { } });
            await sendLock.WaitAsync(pageCts.Token).ConfigureAwait(false);
            try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, pageCts.Token).ConfigureAwait(false); }
            finally { sendLock.Release(); }
            return id;
        }

        // Small buffers: the body is read the moment Volanta's download finishes, so Chromium need not keep
        // more than the latest few files.
        await SendAsync("Network.enable", new { maxTotalBufferSize = 20_000_000, maxResourceBufferSize = 5_000_000 })
            .ConfigureAwait(false);
        SetStatus(VolantaLinkStatus.Connected);

        var windowChecks = Task.Run(async () =>
        {
            while (!pageCts.IsCancellationRequested)
            {
                int id = await SendAsync("Runtime.evaluate", new { expression = WindowCheckScript, returnByValue = true })
                    .ConfigureAwait(false);
                Volatile.Write(ref windowCheckId, id);
                await Task.Delay(WindowCheckInterval, pageCts.Token).ConfigureAwait(false);
            }
        }, pageCts.Token);

        try
        {
            var buffer = new byte[65536];
            using var message = new MemoryStream();
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                // One CDP message can be megabytes (a response body) in many frames: collect the bytes to
                // EndOfMessage and decode once, or a character split across two reads is corrupted.
                message.SetLength(0);
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, res.Count);
                } while (!res.EndOfMessage);

                using var doc = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int replyId))
                {
                    if (bodyCalls.Remove(replyId, out var kind)) TakeBody(kind, root);
                    else if (replyId == Volatile.Read(ref windowCheckId)) TakeWindowState(root);
                    continue;
                }
                if (!root.TryGetProperty("method", out var methodEl) || !root.TryGetProperty("params", out var p)) continue;
                switch (methodEl.GetString())
                {
                    case "Network.responseReceived":
                        if (p.TryGetProperty("response", out var response) && FeedKindFor(response) is FeedKind k
                            && p.TryGetProperty("requestId", out var rid) && rid.GetString() is { } requestId)
                            pendingRequests[requestId] = k;
                        break;
                    case "Network.loadingFinished":
                        if (p.TryGetProperty("requestId", out var fid) && fid.GetString() is { } finished
                            && pendingRequests.Remove(finished, out var fk))
                            bodyCalls[await SendAsync("Network.getResponseBody", new { requestId = finished }).ConfigureAwait(false)] = fk;
                        break;
                    case "Network.loadingFailed":
                        if (p.TryGetProperty("requestId", out var failed) && failed.GetString() is { } failedId)
                            pendingRequests.Remove(failedId);
                        break;
                    case "Network.webSocketFrameReceived":
                        if (p.TryGetProperty("response", out var frame) && frame.TryGetProperty("payloadData", out var data))
                            TakeHubFrame(data.GetString() ?? "");
                        break;
                }
            }
        }
        finally
        {
            pageCts.Cancel();
            try { await windowChecks.ConfigureAwait(false); } catch { /* cancelled with the page */ }
            pageCts.Dispose();
        }
    }

    /// <summary>Which of Volanta's three traffic files a finished response is, or null for anything else.</summary>
    private static FeedKind? FeedKindFor(JsonElement response)
    {
        if (!response.TryGetProperty("url", out var u) || u.GetString() is not { } url) return null;
        if (response.TryGetProperty("status", out var st) && st.TryGetInt32(out int status) && status != 200) return null;
        return FeedKindForUrl(url);
    }

    /// <summary>The file a URL names: one of the traffic files on Volanta's traffic host, or Volanta's friends
    /// list on its API host (nothing else from there). Volanta may switch between its full and "slim" flight file
    /// by a feature flag; both are the Volanta users.</summary>
    internal static FeedKind? FeedKindForUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase))
            return string.Equals(uri.AbsolutePath.TrimEnd('/'), FriendsPath, StringComparison.OrdinalIgnoreCase)
                ? FeedKind.Friends : null;
        if (!string.Equals(uri.Host, FeedHost, StringComparison.OrdinalIgnoreCase)) return null;
        string file = uri.AbsolutePath.TrimStart('/');
        if (file.StartsWith("volanta-flight-positions", StringComparison.OrdinalIgnoreCase)) return FeedKind.VolantaFlights;
        if (file.StartsWith("network-flight-positions", StringComparison.OrdinalIgnoreCase)) return FeedKind.NetworkFlights;
        if (file.StartsWith("network-controllers", StringComparison.OrdinalIgnoreCase)) return FeedKind.Controllers;
        return null;
    }

    private void TakeBody(FeedKind kind, JsonElement reply)
    {
        if (!reply.TryGetProperty("result", out var result) || !result.TryGetProperty("body", out var bodyEl)) return;
        string body = bodyEl.GetString() ?? "";
        if (result.TryGetProperty("base64Encoded", out var b64) && b64.ValueKind == JsonValueKind.True)
        {
            try { body = Encoding.UTF8.GetString(Convert.FromBase64String(body)); }
            catch (FormatException) { return; }
        }
        var now = DateTimeOffset.UtcNow;
        switch (kind)
        {
            case FeedKind.VolantaFlights:
                if (VolantaFeedParser.ParseVolantaFlights(body) is { } vf)
                    Update(s => s with { VolantaFlights = vf, VolantaFlightsReceived = now });
                break;
            case FeedKind.NetworkFlights:
                if (VolantaFeedParser.ParseNetworkFlights(body) is { } nf)
                    Update(s => s with { NetworkFlights = nf, NetworkFlightsReceived = now });
                break;
            case FeedKind.Controllers:
                if (VolantaFeedParser.ParseControllers(body) is { } cf)
                    Update(s => s with { Controllers = cf, ControllersReceived = now });
                break;
            case FeedKind.Friends:
                // The browser's preflight for the same address has no body: it parses to null and is skipped.
                if (VolantaFeedParser.ParseFriends(body) is { } friends)
                {
                    Log.Info("Volanta", $"Friends list caught: {friends.Count} friends");
                    Update(s => s with { Friends = friends, FriendsReceived = now, FriendsSavedAt = null });
                    _friendsCache.Save(friends, now);
                }
                break;
        }
    }

    private void TakeHubFrame(string payload)
    {
        var update = VolantaFeedParser.ParseHubFrame(payload);
        if (!update.Any) return;
        IReadOnlyList<VolantaFriend>? friendsAfter = null;
        Update(s =>
        {
            var friends = s.Friends == null ? null : VolantaFriends.Apply(s.Friends, update, s.OwnUserId);
            friendsAfter = friends;
            return s with
            {
                OwnVolantaPosition = update.Position ?? s.OwnVolantaPosition,
                OwnFlight = update.Flight ?? s.OwnFlight,
                Friends = friends,
            };
        });
        if (friendsAfter != null && VolantaFriends.ChangesMembership(update)) _friendsCache.Save(friendsAfter, DateTimeOffset.UtcNow);
    }

    private void TakeWindowState(JsonElement reply)
    {
        if (!reply.TryGetProperty("result", out var r) || !r.TryGetProperty("result", out var inner)
            || !inner.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.String) return;
        var (paused, userId) = ParseWindowCheck(v.GetString() ?? "");
        var current = Snapshot;
        string? ownUserId = userId ?? current.OwnUserId;
        if (paused != current.VolantaPaused || ownUserId != current.OwnUserId)
            Update(s => s with { VolantaPaused = paused, OwnUserId = ownUserId });
    }

    /// <summary>The window check's answer: paused or live (null when Volanta did not say), and the logged-in
    /// user's id when it looks like one (a GUID, as Volanta's user ids are).</summary>
    internal static (bool? Paused, string? UserId) ParseWindowCheck(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            string s = root.TryGetProperty("s", out var se) ? se.GetString() ?? "" : "";
            string u = root.TryGetProperty("u", out var ue) ? ue.GetString() ?? "" : "";
            bool? paused = s switch { "paused" => true, "live" => false, _ => null };
            return (paused, Guid.TryParse(u, out _) ? u : null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return (null, null); }
    }
}
