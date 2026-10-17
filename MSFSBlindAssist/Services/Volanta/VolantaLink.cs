using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Volanta;

public enum VolantaLinkStatus
{
    /// <summary>Not started.</summary>
    Stopped,
    /// <summary>The web map could not start: no WebView2 runtime, or its browser profile could not be used.</summary>
    NoAccess,
    /// <summary>The web map's page is loading, or is being loaded again after it failed.</summary>
    WaitingForPage,
    Connected,
}

/// <summary>Everything the link has read so far. Immutable: <see cref="VolantaLink.Snapshot"/> swaps a new one
/// in for each change. Received times are this PC's clock, for "data N seconds old".</summary>
public sealed record VolantaLinkSnapshot
{
    public VolantaLinkStatus Status { get; init; } = VolantaLinkStatus.Stopped;
    /// <summary>When the web map's page came up, or was last live again after a pause (VOL-12); null while not
    /// <see cref="VolantaLinkStatus.Connected"/>. The received times below can be older (a file from before a pause
    /// or a reload): compare them with this to tell "nothing since connecting" (VOL-2).</summary>
    public DateTimeOffset? ConnectedAt { get; init; }
    public VolantaFeed<VolantaFlight>? VolantaFlights { get; init; }
    public DateTimeOffset? VolantaFlightsReceived { get; init; }
    public VolantaFeed<VolantaFlight>? NetworkFlights { get; init; }
    public DateTimeOffset? NetworkFlightsReceived { get; init; }
    public VolantaFeed<VolantaController>? Controllers { get; init; }
    public DateTimeOffset? ControllersReceived { get; init; }
    /// <summary>Volanta's own last report of the pilot's position, as reported (not moved on).</summary>
    public VolantaOwnPosition? OwnVolantaPosition { get; init; }
    public VolantaOwnFlight? OwnFlight { get; init; }
    /// <summary>The logged-in Volanta user's id, which is the user id on the pilot's own entry in Volanta's
    /// flight file; null until read, and null whenever it cannot be told for sure (VOL-4).</summary>
    public string? OwnUserId { get; init; }
    /// <summary>The pilot's Volanta friends; null until the page, logged in, downloads them (VOL-6).</summary>
    public IReadOnlyList<VolantaFriend>? Friends { get; init; }
    /// <summary>When the friends list was last downloaded.</summary>
    public DateTimeOffset? FriendsReceived { get; init; }
}

/// <summary>
/// What Volanta's live map has shown so far, as one snapshot the window reads. The web map
/// (<c>VolantaWebMap</c>: Volanta's public map page in a browser the pilot never sees) hands in each file the page
/// finished downloading (<see cref="IngestBody"/>: the three traffic files, the friends list, the pilot's active
/// flight), the page's hub frames (<see cref="IngestHubFrame"/>: the pilot's own position and flight, friend
/// changes) and its storage check (<see cref="IngestStorageCheck"/>: who is logged in), and says when the page is up
/// (<see cref="SetSourceStatus"/>). Pure apart from its log lines: no browser, network or file here.
/// </summary>
public sealed class VolantaLink : IDisposable
{
    private const string FeedHost = "webassets.volanta.app";
    private const string ApiHost = "api.volanta.app";
    private const string FriendsPath = "/api/v1/Relationships/Friends";
    /// <summary>The logged-in pilot's active flight, which Volanta's page asks for as soon as it is logged in (read in
    /// 1.19.2's code, 2026-10-17: "Found active flight {id}"). Its id is the id of the pilot's own entry in Volanta's
    /// flight file, so the pilot's flight is left out from the first list (VOL-4); it carries no login token.</summary>
    private const string ActiveFlightPath = "/api/v1/Flights/Active";

    internal enum FeedKind { VolantaFlights, NetworkFlights, Controllers, Friends, ActiveFlight }

    private readonly object _gate = new();
    private VolantaLinkSnapshot _snapshot = new();
    private bool _started;

    /// <summary>Raised after any change to <see cref="Snapshot"/>, on whichever thread made it (the web map parses
    /// big files off the UI thread).</summary>
    public event EventHandler? Changed;

    public VolantaLinkSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    private bool Started { get { lock (_gate) return _started; } }

    /// <summary>Starts taking what the web map hands in; the web map says when its page is up.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        SetStatus(VolantaLinkStatus.WaitingForPage);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            _started = false;
        }
        Log.Info("Volanta", "Live map link: Stopped");
        Update(s => s with { Status = VolantaLinkStatus.Stopped, ConnectedAt = null });
    }

    public void Dispose() => Stop();

    /// <summary>The web map's state: <see cref="VolantaLinkStatus.Connected"/> once its page is up.</summary>
    internal void SetSourceStatus(VolantaLinkStatus status)
    {
        if (Started) SetStatus(status);
    }

    /// <summary>The web map was paused (it downloads only while the Volanta window is in the foreground, VOL-12)
    /// and is live again: "since connecting" counts from now, so the last file, old by the length of the pause, is
    /// not called stale before anything is overdue.</summary>
    internal void Resumed()
    {
        if (!Started) return;
        Update(s => s.Status == VolantaLinkStatus.Connected ? s with { ConnectedAt = DateTimeOffset.UtcNow } : s);
    }

    /// <summary>The pilot logged out of Volanta in the web map: the friends list, the logged-in user and the pilot's
    /// own flight were theirs, and go with the login.</summary>
    internal void LoggedOut()
    {
        if (!Started) return;
        Update(s => s with
        {
            Friends = null, FriendsReceived = null, OwnUserId = null, OwnFlight = null, OwnVolantaPosition = null,
        });
    }

    /// <summary>A file the web map's page finished downloading, already decoded to text.</summary>
    internal void IngestBody(FeedKind kind, string body)
    {
        if (Started) TakeBodyText(kind, body);
    }

    internal void IngestHubFrame(string payload)
    {
        if (Started) TakeHubFrame(payload);
    }

    /// <summary>The answer of the storage check run in the web map's page (<see cref="ParseStorageCheck"/>).</summary>
    internal void IngestStorageCheck(string value)
    {
        if (!Started) return;
        var (usersRead, userId, _) = ParseStorageCheck(value);
        if (!usersRead) return;
        // When the check read the per-user keys its answer stands, none or ambiguous included: an id kept from
        // before could be another account's.
        if (Snapshot.OwnUserId == userId) return;
        Update(s => s with { OwnUserId = userId });
    }

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
            if (_snapshot.Status == status) return;
        }
        Log.Info("Volanta", $"Live map link: {status}");
        var connectedAt = status == VolantaLinkStatus.Connected ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        Update(s => s with { Status = status, ConnectedAt = connectedAt });
    }

    /// <summary>The file a URL names: one of the traffic files on Volanta's traffic host, or, on its API host,
    /// Volanta's friends list or the logged-in pilot's active flight (nothing else from there). Volanta may switch
    /// between its full and "slim" flight file by a feature flag; both are the Volanta users.</summary>
    internal static FeedKind? FeedKindForUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase))
        {
            string path = uri.AbsolutePath.TrimEnd('/');
            if (string.Equals(path, FriendsPath, StringComparison.OrdinalIgnoreCase)) return FeedKind.Friends;
            if (string.Equals(path, ActiveFlightPath, StringComparison.OrdinalIgnoreCase)) return FeedKind.ActiveFlight;
            return null;
        }
        if (!string.Equals(uri.Host, FeedHost, StringComparison.OrdinalIgnoreCase)) return null;
        string file = uri.AbsolutePath.TrimStart('/');
        if (file.StartsWith("volanta-flight-positions", StringComparison.OrdinalIgnoreCase)) return FeedKind.VolantaFlights;
        if (file.StartsWith("network-flight-positions", StringComparison.OrdinalIgnoreCase)) return FeedKind.NetworkFlights;
        if (file.StartsWith("network-controllers", StringComparison.OrdinalIgnoreCase)) return FeedKind.Controllers;
        return null;
    }

    /// <summary>The text of a Network.getResponseBody result ({"body", "base64Encoded"}), or null.</summary>
    internal static string? BodyFromResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("body", out var bodyEl)
            || bodyEl.ValueKind != JsonValueKind.String) return null;
        string body = bodyEl.GetString() ?? "";
        if (!result.TryGetProperty("base64Encoded", out var b64) || b64.ValueKind != JsonValueKind.True) return body;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(body)); }
        catch (FormatException) { return null; }
    }

    private void TakeBodyText(FeedKind kind, string body)
    {
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
                    Update(s => s with { Friends = friends, FriendsReceived = now });
                }
                break;
            case FeedKind.ActiveFlight:
                // No active flight answers with nothing to parse (or not 200, never read): the last one stands
                // until the hub says otherwise.
                if (VolantaFeedParser.ParseActiveFlight(body) is { } active)
                {
                    Log.Info("Volanta", $"Active flight caught: {active.FlightId}");
                    Update(s => s with { OwnFlight = active });
                }
                break;
        }
    }

    private void TakeHubFrame(string payload)
    {
        var update = VolantaFeedParser.ParseHubFrame(payload);
        if (!update.Any) return;
        Update(s => s with
        {
            OwnVolantaPosition = update.Position ?? s.OwnVolantaPosition,
            OwnFlight = update.Flight ?? s.OwnFlight,
            Friends = s.Friends == null ? null : VolantaFriends.Apply(s.Friends, update, s.OwnUserId),
        });
    }

    /// <summary>
    /// The storage check's answer (<c>{"u": [...], "a": true}</c>): whether it read the per-user keys; the
    /// logged-in user's id, the one GUID among the per-user key NAMES Volanta keeps for its "what's new" notice
    /// (Volanta's user ids are GUIDs), null when there is none or more than one (two Volanta accounts used in this
    /// browser: either could be the one logged in, so neither is trusted); and whether a login is stored at all (the
    /// login key's NAME, never its value, VOL-13), null when not said. A lone string, as the first version sent, is
    /// one key.
    /// </summary>
    internal static (bool UsersRead, string? UserId, bool? LoggedIn) ParseStorageCheck(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (false, null, null);
            bool? loggedIn = root.TryGetProperty("a", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? a.ValueKind == JsonValueKind.True : null;
            if (!root.TryGetProperty("u", out var ue)) return (false, null, loggedIn);
            var keys = ue.ValueKind switch
            {
                JsonValueKind.Array => ue.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString() ?? "").ToList(),
                JsonValueKind.String => new List<string> { ue.GetString() ?? "" },
                _ => null,
            };
            if (keys == null) return (false, null, loggedIn);
            var ids = keys.Where(k => Guid.TryParse(k, out _)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return (true, ids.Count == 1 ? ids[0] : null, loggedIn);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return (false, null, null); }
    }
}
