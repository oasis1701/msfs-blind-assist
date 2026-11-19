using System.Globalization;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The BlueBird 757's hidden helper view ("VCockpitNN - BBSTablet") over the MSFS Coherent GT
/// remote debugger (127.0.0.1:19999): both CDU screens, CDU key presses and the Navigraph device
/// code. Shaped like <see cref="CoherentHS787CduClient"/>: the view is resolved BY TITLE on every
/// (re)connect (its VCockpit number changes on each aircraft load), and
/// <c>Resources/coherent-bbs757-tablet-agent.js</c> is installed into it (window.__MSFSBA_BBS757).
///
/// ONE client per view ([DBG-9]): the 757's definition owns a single instance
/// (<c>BBS757Definition.EnsureTabletClient</c>, started on first use, disposed on a switch away) that
/// the CDU window, the EFB window and the background page service (<see cref="Bbs757CduPages"/>)
/// share; the windows subscribe and unsubscribe, never dispose it. The client CLAIMS its view
/// (<see cref="CoherentViewOwnership"/>) from Start to Stop, reconnect gaps included, as
/// <see cref="CoherentA32nxMcduClient"/> does. A still-open socket whose agent went
/// missing gets the agent re-installed on it; any other socket is aborted and disposed before a
/// new one connects ([A380C-2]). An agent is only ever taken as installed while it can hear the
/// Comm Bus: an install the page answers NOT_READY (an aircraft reload, the view's scripts not run
/// yet) is retried on the same socket, and a scrape that shows no listener, or one still not ready
/// after <see cref="ListenerReadyTimeoutMs"/>, installs it again (<see cref="NeedsReinstall"/>).
/// Whenever the link is lost the queued keys are dropped, never sent later to a page nobody has
/// checked (<see cref="KeysDropped"/>). On-demand calls (<see cref="GetNavigraphAsync"/>,
/// <see cref="ScrapeAsync"/>, key presses) only ever use the loop's socket and never connect
/// themselves, so they cannot race the loop's connection setup ([A380C-3]). Every scrape answers
/// the agent's version: one older than <see cref="AgentVersion"/> (an app update over a loaded
/// flight) is installed again on the open socket.
///
/// Screens: the agent keeps each CDU's last Comm Bus frame (they arrive only on change); the loop
/// scrapes the ACTIVE CDU only, every <see cref="PollIntervalMs"/> ms while the CDU window is
/// shown and every <see cref="IdleIntervalMs"/> ms while it is hidden, and raises
/// <see cref="ScreenUpdated"/> when that CDU's frame changed. <see cref="ScrapeAsync"/> reads
/// either CDU once, on demand.
///
/// Keys: one queue for both CDUs, so presses never overlap. Each key carries its hold, the caller's:
/// <see cref="KeyHoldMs"/> by default, since a CLR held about a second clears the whole scratchpad;
/// only the long CLR is held 1300 ms (<see cref="Bbs757CduKeys.ClearAllHoldMs"/>), to do just that: the page
/// service's wipe (<see cref="Bbs757CduPages.WipeScratchpadAsync"/>) and the CDU window's Alt+C
/// (<see cref="Bbs757CduKeys.ClearScratchpad"/>). Holds
/// are clamped to <see cref="MinHoldMs"/>-<see cref="MaxHoldMs"/> here and in the agent. Each key
/// carries its own gap too, the pause after its release before the next key: <see cref="KeyGapMs"/> unless
/// the caller gives another (only the CDU window's typed entries do, <see cref="Bbs757CduTyping.GapMs"/>).
/// A gap is clamped to <see cref="MinGapMs"/>-<see cref="MaxGapMs"/>; <see cref="MinGapMs"/> (50 ms) is a
/// floor, not a safe gap.
/// Restart is not supported: dispose and create a new instance.
/// </summary>
public sealed class CoherentBbs757TabletClient : IDisposable, IBbs757CduLink
{
    private const string DebuggerBase = "http://127.0.0.1:19999";
    public const string ViewNeedle = "BBSTablet";
    private const string AgentFile = "coherent-bbs757-tablet-agent.js";
    private const string InstallMarker = "MSFSBA_BBS757_TABLET_INSTALLED";
    private const string NoAgent = "no-agent";

    private const int PollIntervalMs = 350;
    private const int IdleIntervalMs = 1000;
    private const int ReconnectDelayMs = 2000;
    private const int EvalTimeoutMs = 5000;
    private const int ConnectTimeoutMs = 4000;
    // Consecutive evals answered by nothing before the socket is presumed dead (a half-open
    // socket never sends a Close frame).
    private const int DeadSocketEvalFailures = 3;
    // A scrape answered within this long keeps the client readable.
    private const int ReadableWindowMs = 5000;
    // Installs answered with something other than the marker (the view's scripts have not run
    // yet) before the view is resolved again from the page list.
    private const int InstallRetriesPerSocket = 5;
    // How long an on-demand scrape waits, for the loop's agent and for its answer together.
    private const int OnDemandScrapeTimeoutMs = 4000;

    /// <summary>
    /// The agent version this client needs (the agent's VERSION, answered with every scrape): 4 is
    /// the first whose press takes the hold, 5 the first with the watch list. A scrape answered by
    /// an agent without a version or an older one (an app update over a loaded flight) has the agent
    /// installed again (<see cref="AgentIsCurrent"/>); the install replaces an agent of another version.
    /// </summary>
    internal const int AgentVersion = 5;

    /// <summary>How often the watched L:vars are read while the watch list is not empty (plan 13).</summary>
    internal const int WatchIntervalMs = 500;

    /// <summary>
    /// How long an installed agent's Comm Bus listener may stay not ready before it is installed
    /// again (the agent itself re-registers after 4 s, so a re-install after this does, up to its
    /// three registrations per page; a view still deaf after them keeps its last agent).
    /// </summary>
    internal const int ListenerReadyTimeoutMs = 5000;

    /// <summary>How long a key is held down unless its caller says otherwise (the agent's HOLD_MS).</summary>
    public const int KeyHoldMs = 250;

    /// <summary>The shortest hold a key may have (the agent's MIN_HOLD_MS).</summary>
    internal const int MinHoldMs = 100;

    /// <summary>The longest hold a key may have (the agent's MAX_HOLD_MS).</summary>
    internal const int MaxHoldMs = 1500;

    /// <summary>The standard pause after a key's release before the next key; a key may carry its own (typed entries: <see cref="Bbs757CduTyping.GapMs"/>).</summary>
    public const int KeyGapMs = 400;

    /// <summary>
    /// The shortest pause a key may have after its release: a floor, not a safe gap. A quiet sim typed 5/5
    /// down to 50, a busier one lost a repeated digit at 100 (4 in 10): typed entries use
    /// <see cref="Bbs757CduTyping.GapMs"/> (plan 08, [B757-58]).
    /// </summary>
    internal const int MinGapMs = 50;

    /// <summary>The longest pause a key may have after its release.</summary>
    internal const int MaxGapMs = 1000;

    /// <summary>
    /// One key's whole cycle in the queue at the default hold: hold, release, gap. A key with its
    /// own hold takes that hold plus <see cref="KeyGapMs"/>, a key with its own gap its hold plus that gap
    /// (<see cref="CycleMs"/>).
    /// </summary>
    public const int KeyCycleMs = KeyHoldMs + KeyGapMs;

    /// <summary>A CDU's screen changed (1 captain, 2 first officer); posted to the UI context.</summary>
    public event Action<int, Bbs757CduScreen>? ScreenUpdated;

    /// <summary>
    /// The agent no longer holds a screen for this CDU, though one was delivered (the aircraft was
    /// reloaded and the agent installed afresh); posted to the UI context. Its screen arrives again
    /// on its next change.
    /// </summary>
    public event Action<int>? ScreenLost;

    /// <summary>
    /// Queued keys were thrown away because the link to the tablet view was lost (the socket
    /// dropped, or the agent was gone or deaf): a key meant for the page the pilot was reading is
    /// never sent later to a page nobody has checked. Raised once per outage with the number of
    /// keys dropped; posted to the UI context.
    /// </summary>
    public event Action<int>? KeysDropped;

    /// <summary>
    /// Watched L:vars (<see cref="SetWatchList"/>) whose value changed since the last read, by name
    /// without <c>L:</c>; posted to the UI context. The first read after the list reaches an agent
    /// (a new list, or a new agent after an aircraft reload) is the agent's silent baseline, so a
    /// reinstall never raises the whole cockpit as changes.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, double>>? LvarsChanged;

    private readonly SynchronizationContext? _syncContext;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly WakeableDelay _wake = new();
    // Read by the key pump and drained by DropQueuedKeys, so not single-reader. Each key carries
    // its clamped hold and the clamped pause after its release.
    private readonly Channel<(int Cdu, string Key, int HoldMs, int GapMs)> _keys = Channel.CreateUnbounded<(int, string, int, int)>(
        new UnboundedChannelOptions { SingleReader = false });
    private readonly Func<string> _readAgent;

    private CancellationTokenSource? _cts;
    // _cts.Token captured at Start: reading .Token off a disposed source throws, and a UI-thread
    // call (a key, the Navigraph read) can still arrive during teardown.
    private CancellationToken _loopToken = CancellationToken.None;
    private volatile ClientWebSocket? _ws;
    private string _agentJs = "";
    private int _msgId;
    private volatile bool _agentInstalled;
    private volatile bool _active;
    private volatile int _activeCdu = 1;
    private int _emptyEvalStreak;
    private int _installFailures;
    private DateTime? _notReadySinceUtc;
    private int _pendingKeys;
    // 1 when KeysDropped has been raised, 0 again when the agent is installed again. Taken with
    // Interlocked.Exchange: the key pump and the loop can both drop keys at once, and the event
    // must be raised once per outage.
    private int _keysDroppedRaised;
    private long _lastGoodScrapeTicks;
    // Per CDU (index 1 and 2): the agent tag of the frame last delivered, whether the agent
    // holds a frame, and whether one was ever delivered.
    private readonly string[] _tags = { "", "", "" };
    private readonly bool[] _hasFrame = new bool[3];
    private readonly bool[] _delivered = new bool[3];
    private IDisposable? _viewClaim;
    private bool _disposed;
    // The watch list (immutable once set; swapped whole) and the list the agent last took. A list
    // that is not the one sent goes out before the next read; a new agent has none.
    private volatile string[] _watchNames = Array.Empty<string>();
    private volatile string[]? _watchSent;
    private DateTime _lastWatchUtc = DateTime.MinValue;

    /// <summary>Replaces <see cref="EvalAsync"/> in tests (the watch's calls, without a socket).</summary>
    internal Func<string, Task<string>>? EvalForTests { get; set; }

    public CoherentBbs757TabletClient()
        : this(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", AgentFile)))
    {
    }

    /// <param name="readAgent">Reads the page agent's source (the Resources file by default).</param>
    internal CoherentBbs757TabletClient(Func<string> readAgent)
    {
        _readAgent = readAgent;
        _syncContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// The CDU the loop scrapes, 1 (captain) or 2 (first officer). Setting it asks for that CDU's
    /// current screen at once.
    /// </summary>
    public int ActiveCdu
    {
        get => _activeCdu;
        set
        {
            int cdu = value == 2 ? 2 : 1;
            _activeCdu = cdu;
            _tags[cdu] = "";
            _wake.Wake();
        }
    }

    /// <summary>
    /// True while the socket is open, the agent installed and a scrape answered in the last few
    /// seconds: keys and reads can reach the tablet view.
    /// </summary>
    public bool IsReadable
    {
        get
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open || !_agentInstalled) return false;
            long ticks = Interlocked.Read(ref _lastGoodScrapeTicks);
            return ticks != 0 && (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds < ReadableWindowMs;
        }
    }

    /// <summary>Whether the agent holds a screen for this CDU (as of the last scrape of it).</summary>
    public bool HasFrame(int cdu) => _hasFrame[cdu == 2 ? 2 : 1];

    /// <summary>Keys queued or still in their cycle.</summary>
    public int PendingKeyCount => Volatile.Read(ref _pendingKeys);

    public void Start()
    {
        if (_cts != null) return;   // restart is not a supported lifecycle: dispose and recreate
        _cts = new CancellationTokenSource();
        _loopToken = _cts.Token;
        try
        {
            _agentJs = _readAgent();
            if (string.IsNullOrEmpty(_agentJs)) Log.Warn("BBS757", $"{AgentFile} is empty.");
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"Could not load {AgentFile}: {ex.Message}");
        }
        // Without its agent the client can neither read nor press, so it never opens a socket
        // and claims nothing.
        if (string.IsNullOrEmpty(_agentJs)) return;

        _viewClaim = CoherentViewOwnership.Claim(ViewNeedle, onOneShotDone: _wake.Wake);
        _ = Task.Run(() => RunLoop(_cts.Token));
        _ = Task.Run(() => KeyPump(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _keys.Writer.TryComplete();
        var ws = _ws;
        _ws = null;
        CloseSocket(ws);
        _agentInstalled = false;
        _viewClaim?.Dispose();
        _viewClaim = null;
    }

    /// <summary>Scrape at the visible rate while the CDU window is shown, at the idle rate while hidden.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            _tags[_activeCdu] = "";
            _wake.Wake();
        }
    }

    /// <summary>
    /// Queues one key for a CDU (<c>L1</c>, <c>LEGS</c>, <c>KEYA</c>, <c>KEYCLR</c> …, the cockpit's
    /// own names, <see cref="Bbs757CduKeys"/>), held <see cref="KeyHoldMs"/>. Keys go out one at a
    /// time, <see cref="KeyCycleMs"/> apart. When the link is lost every queued key is dropped
    /// (<see cref="KeysDropped"/>); the window refuses aloud before queueing when
    /// <see cref="IsReadable"/> is false.
    /// </summary>
    public void PressKey(int cdu, string key) => PressKey(cdu, key, KeyHoldMs);

    /// <summary>
    /// Queues one key held <paramref name="holdMs"/> (clamped to <see cref="MinHoldMs"/>-<see cref="MaxHoldMs"/>);
    /// the next key goes out <see cref="KeyGapMs"/> after its release. Only the long CLR (the page service's wipe and
    /// the CDU window's Alt+C) holds a key longer than <see cref="KeyHoldMs"/>: a CLR held about a second clears the whole scratchpad.
    /// </summary>
    public void PressKey(int cdu, string key, int holdMs) => PressKey(cdu, key, holdMs, KeyGapMs);

    /// <summary>
    /// Queues one key held <paramref name="holdMs"/> (clamped to <see cref="MinHoldMs"/>-<see cref="MaxHoldMs"/>),
    /// the next key going out <paramref name="gapMs"/> after its release (clamped to
    /// <see cref="MinGapMs"/>-<see cref="MaxGapMs"/>). Only the CDU window's typed entries use a shorter
    /// cycle (<see cref="Bbs757CduTyping"/>, measured, plan 08); every other key keeps the standard one.
    /// </summary>
    public void PressKey(int cdu, string key, int holdMs, int gapMs)
    {
        if ((cdu != 1 && cdu != 2) || !Bbs757CduKeys.IsKey(key))
        {
            Log.Debug("BBS757", $"CDU key {cdu}/{key}: not a key, not sent.");
            return;
        }
        Interlocked.Increment(ref _pendingKeys);
        if (!_keys.Writer.TryWrite((cdu, key, ClampHold(holdMs), ClampGap(gapMs)))) Interlocked.Decrement(ref _pendingKeys);
    }

    /// <summary>A key's hold within <see cref="MinHoldMs"/>-<see cref="MaxHoldMs"/>, as the agent clamps it.</summary>
    private static int ClampHold(int holdMs) => Math.Clamp(holdMs, MinHoldMs, MaxHoldMs);

    /// <summary>A key's pause after release within <see cref="MinGapMs"/>-<see cref="MaxGapMs"/>.</summary>
    private static int ClampGap(int gapMs) => Math.Clamp(gapMs, MinGapMs, MaxGapMs);

    /// <summary>One key's whole cycle in the queue: its hold plus the pause after it, both clamped. Pure.</summary>
    internal static int CycleMs(int holdMs, int gapMs) => ClampHold(holdMs) + ClampGap(gapMs);

    /// <summary>Takes every queued key off the queue, as queued (tests: what <see cref="PressKey(int, string, int, int)"/> put there).</summary>
    internal IReadOnlyList<(int Cdu, string Key, int HoldMs, int GapMs)> DrainQueuedKeysForTests()
    {
        var o = new List<(int, string, int, int)>();
        while (_keys.Reader.TryRead(out var k)) { o.Add(k); Interlocked.Decrement(ref _pendingKeys); }
        return o;
    }

    /// <summary>
    /// The agent call that presses one key for <paramref name="holdMs"/> (clamped), e.g.
    /// <c>__MSFSBA_BBS757.press(2,"KEYCLR",1300)</c>. The key must be one of <see cref="Bbs757CduKeys"/>. Pure.
    /// </summary>
    internal static string PressExpression(int cdu, string key, int holdMs) =>
        "__MSFSBA_BBS757.press(" + cdu.ToString(CultureInfo.InvariantCulture) + "," + JsonSerializer.Serialize(key)
        + "," + ClampHold(holdMs).ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>Whether a scrape's agent version is the one this client needs (missing or older is not). Pure.</summary>
    internal static bool AgentIsCurrent(int? version) => version >= AgentVersion;

    /// <summary>The agent call that scrapes one CDU, its frame sent only when its tag differs from <paramref name="since"/>.</summary>
    private static string ScrapeExpression(int cdu, string since) =>
        $"window.__MSFSBA_BBS757 ? JSON.stringify(__MSFSBA_BBS757.scrape({cdu},{JsonSerializer.Serialize(since)})) : '{NoAgent}'";

    /// <summary>
    /// One CDU's screen now, read on demand whichever CDU the loop is scraping (the page service
    /// reads the first officer's this way). It asks for the whole frame and leaves the loop's change
    /// tags and agent state alone. It waits at most <see cref="OnDemandScrapeTimeoutMs"/> in all, for
    /// the loop's agent and for the answer, and never connects ([A380C-3]). Null when the view, the
    /// agent or the CDU's frame is missing, the agent is older than <see cref="AgentVersion"/> or
    /// cannot hear the Comm Bus, or <paramref name="ct"/> fires; never throws.
    /// </summary>
    public async Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct)
    {
        if ((cdu != 1 && cdu != 2) || _disposed) return null;
        try
        {
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct, _loopToken);
            bound.CancelAfter(OnDemandScrapeTimeoutMs);
            if (!await WaitForAgentAsync(TimeSpan.FromMilliseconds(OnDemandScrapeTimeoutMs), bound.Token)) return null;
            // Sent under the loop's token alone: .NET aborts a WebSocket whose send is canceled, and
            // the socket is the loop's. A caller that gives up stops waiting; the eval ends on its
            // own timeout.
            string raw = await EvalAsync(ScrapeExpression(cdu, ""), _loopToken).WaitAsync(bound.Token);
            if (raw.Length == 0) return null;
            if (raw == NoAgent) { _wake.Wake(); return null; }   // the loop installs it again
            var result = JsonSerializer.Deserialize<ScrapeResult>(raw);
            if (result == null || !result.ok) return null;
            if (!AgentIsCurrent(result.version)) { _wake.Wake(); return null; }
            if (!result.listening || !result.ready || !result.hasFrame || result.frame == null) return null;
            return Bbs757CduFrame.Parse(result.frame);
        }
        catch (Exception ex)
        {
            // The frame itself is never logged.
            Log.Debug("BBS757", $"CDU {cdu} on-demand read failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// When a Navigraph reading (<see cref="GetNavigraphAsync"/>, answered by the tablet) last found
    /// the pilot signed out, UTC; null if none has. BlueBird looks for a newer AIRAC cycle only when
    /// the flight loads, so a signed-out reading taken after the current flight loaded means that
    /// check ran signed out (<see cref="Bbs757EfbLayout.SignedOutDuringLoad"/>).
    /// </summary>
    public DateTime? LastSignedOutUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastSignedOutTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    private long _lastSignedOutTicks;

    /// <summary>
    /// The Navigraph sign-in state BlueBird's tablet holds: signed in or not, and the pending
    /// device sign-in's user code and address. Never a token. (false, null, null) when the view
    /// or its agent cannot be read within a few seconds; callers that must tell that apart check
    /// <see cref="IsReadable"/> first. A signed-out answer is time-stamped (<see cref="LastSignedOutUtc"/>).
    /// </summary>
    public async Task<(bool SignedIn, string? UserCode, string? Uri)> GetNavigraphAsync()
    {
        try
        {
            if (!await WaitForAgentAsync(TimeSpan.FromSeconds(6), _loopToken)) return (false, null, null);
            string raw = await EvalAsync("window.__MSFSBA_BBS757 ? JSON.stringify(__MSFSBA_BBS757.navigraph()) : ''", _loopToken);
            if (string.IsNullOrEmpty(raw)) return (false, null, null);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return (false, null, null);
            bool signedIn = root.TryGetProperty("signedIn", out var s) && s.ValueKind == JsonValueKind.True;
            if (!signedIn) Interlocked.Exchange(ref _lastSignedOutTicks, DateTime.UtcNow.Ticks);
            string? code = root.TryGetProperty("userCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            string? uri = root.TryGetProperty("uri", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            return (signedIn, code, uri);
        }
        catch (Exception ex) when (ex is JsonException or OperationCanceledException or ObjectDisposedException)
        {
            // The answer itself is never logged: it can hold the sign-in code.
            Log.Debug("BBS757", $"Navigraph read failed: {ex.GetType().Name}");
            return (false, null, null);
        }
    }

    /// <summary>
    /// Waits until the loop has the agent installed on an open socket, waking it meanwhile. False at
    /// once on a client never started; <paramref name="ct"/> (the loop's token, or one linked to it)
    /// ends the wait.
    /// </summary>
    private async Task<bool> WaitForAgentAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (!ct.IsCancellationRequested)
        {
            var ws = _ws;
            if (ws != null && ws.State == WebSocketState.Open && _agentInstalled) return true;
            if (_cts == null || DateTime.UtcNow >= until) return false;
            _wake.Wake();
            await Task.Delay(100, ct);
        }
        return false;
    }

    // ---- watched L:vars (plan 13) ------------------------------------

    /// <summary>
    /// The L:vars to watch, by name without <c>L:</c> (empty stops the watch). The list goes to the
    /// agent before its next read, every <see cref="WatchIntervalMs"/>, on the loop's socket; it is
    /// sent again whenever the agent is installed afresh.
    /// </summary>
    public void SetWatchList(IReadOnlyCollection<string> names)
    {
        _watchNames = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.Ordinal).ToArray();
        _watchSent = null;
        _wake.Wake();
    }

    /// <summary>The agent call that sets the watch list: the names as one JSON array literal. Pure.</summary>
    internal static string WatchExpression(IReadOnlyCollection<string> names) =>
        "window.__MSFSBA_BBS757 && __MSFSBA_BBS757.watch ? __MSFSBA_BBS757.watch("
        + JsonSerializer.Serialize(names) + $") : '{NoAgent}'";

    private const string ChangesExpression =
        "window.__MSFSBA_BBS757 && __MSFSBA_BBS757.changes ? JSON.stringify(__MSFSBA_BBS757.changes()) : '" + NoAgent + "'";

    /// <summary>
    /// One read of the watched L:vars: sends the list first when the agent has not taken it, then
    /// raises <see cref="LvarsChanged"/> with any changes. An agent that answers it watches nothing
    /// (the page re-evaluated) has the list sent again next time. Nothing at all with no list.
    /// </summary>
    internal async Task PollWatchAsync(CancellationToken ct)
    {
        var names = _watchNames;
        if (names.Length == 0) return;
        if (!ReferenceEquals(_watchSent, names))
        {
            string set = await EvalOrTestAsync(WatchExpression(names), ct);
            if (set != "ok")
            {
                if (set.Length > 0) Log.Debug("BBS757", $"Watch list not taken: {Shorten(set)}");
                return;
            }
            _watchSent = names;
        }
        string raw = await EvalOrTestAsync(ChangesExpression, ct);
        if (raw.Length == 0 || raw == NoAgent) return;
        WatchResult? result;
        try { result = JsonSerializer.Deserialize<WatchResult>(raw); }
        catch (JsonException) { return; }
        if (result == null || !result.ok) return;
        if (!result.watching)
        {
            _watchSent = null;
            return;
        }
        if (result.changes == null || result.changes.Count == 0) return;
        var changes = result.changes;
        PostToUI(() => LvarsChanged?.Invoke(changes));
    }

    private Task<string> EvalOrTestAsync(string expression, CancellationToken ct) =>
        EvalForTests != null ? EvalForTests(expression) : EvalAsync(expression, ct);

    // ---- key queue --------------------------------------------------

    private async Task KeyPump(CancellationToken ct)
    {
        try
        {
            await foreach (var (cdu, key, holdMs, gapMs) in _keys.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var ws = _ws;
                    if (ws == null || ws.State != WebSocketState.Open || !_agentInstalled)
                    {
                        // The link went while this key waited: it and the rest are dropped.
                        DropQueuedKeys(alreadyDropped: 1);
                        continue;
                    }
                    string expr = $"window.__MSFSBA_BBS757 ? {PressExpression(cdu, key, holdMs)} : '{NoAgent}'";
                    string result = await EvalAsync(expr, ct);
                    if (result != "ok")
                        Log.Debug("BBS757", $"CDU {cdu} key {key}: {(result.Length == 0 ? "no answer" : result)}");
                    if (result == NoAgent)
                    {
                        _agentInstalled = false;
                        DropQueuedKeys(alreadyDropped: 1);
                        continue;
                    }
                    // Timed from the answer: the press ran before it, so its release (its hold
                    // later in the page) is at least the key's gap before the next key.
                    await Task.Delay(holdMs + gapMs, ct);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingKeys);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("BBS757", $"CDU key pump: {ex.Message}"); }
    }

    /// <summary>
    /// Throws away every queued key (the link to the page they were meant for is gone) and, once
    /// per outage, raises <see cref="KeysDropped"/>. <paramref name="alreadyDropped"/> counts a key
    /// the pump had already taken off the queue; its own count is the pump's to settle.
    /// </summary>
    private void DropQueuedKeys(int alreadyDropped = 0)
    {
        int dropped = alreadyDropped;
        while (_keys.Reader.TryRead(out _))
        {
            dropped++;
            Interlocked.Decrement(ref _pendingKeys);
        }
        if (dropped == 0) return;
        Log.Debug("BBS757", $"Tablet link lost: {dropped} CDU key(s) dropped, not sent.");
        if (Interlocked.Exchange(ref _keysDroppedRaised, 1) == 1) return;
        PostToUI(() => KeysDropped?.Invoke(dropped));
    }

    /// <summary>
    /// Whether a scrape's answer shows an agent that cannot hear the Comm Bus, so it must be
    /// installed again: no listener at all, or a listener still not ready
    /// <see cref="ListenerReadyTimeoutMs"/> after this client first saw it not ready
    /// (<paramref name="notReadySinceUtc"/>, null while it was ready). Pure.
    /// </summary>
    internal static bool NeedsReinstall(bool listening, bool ready, DateTime? notReadySinceUtc, DateTime nowUtc)
    {
        if (!listening) return true;
        if (ready || notReadySinceUtc == null) return false;
        return (nowUtc - notReadySinceUtc.Value).TotalMilliseconds >= ListenerReadyTimeoutMs;
    }

    /// <summary>The agent stopped being usable: install it again on the next pass, and drop the queued keys.</summary>
    private void AgentUnusable()
    {
        _agentInstalled = false;
        _notReadySinceUtc = null;
        DropQueuedKeys();
    }

    // ---- connection + poll loop -------------------------------------

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await EnsureConnected(ct))
                {
                    await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct);
                    continue;
                }
                await PollOnce(ct);
                if (_emptyEvalStreak >= DeadSocketEvalFailures)
                {
                    Log.Debug("BBS757", "Tablet client: no eval answered, reconnecting.");
                    DropSocket();
                    continue;
                }
                bool watching = _watchNames.Length > 0;
                if (watching && (DateTime.UtcNow - _lastWatchUtc).TotalMilliseconds >= WatchIntervalMs - 50)
                {
                    _lastWatchUtc = DateTime.UtcNow;
                    await PollWatchAsync(ct);
                }
                int interval = _active ? PollIntervalMs : IdleIntervalMs;
                if (watching) interval = Math.Min(interval, WatchIntervalMs);
                await _wake.WaitAsync(TimeSpan.FromMilliseconds(interval), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Debug("BBS757", $"Tablet client loop: {ex.Message}");
                DropSocket();
                try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct); } catch { break; }
            }
        }
    }

    /// <summary>Abort AND dispose: Abort alone leaks the socket's native handle on every reconnect.</summary>
    private static void CloseSocket(ClientWebSocket? ws)
    {
        if (ws == null) return;
        try { ws.Abort(); } catch { }
        try { ws.Dispose(); } catch { }
    }

    /// <summary>
    /// The one teardown path: close the socket, fail the evals waiting on it, drop the queued keys,
    /// re-push on reconnect.
    /// </summary>
    private void DropSocket()
    {
        var ws = _ws;
        _ws = null;
        _agentInstalled = false;
        _emptyEvalStreak = 0;
        _installFailures = 0;
        _notReadySinceUtc = null;
        CloseSocket(ws);
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        _tags[1] = "";
        _tags[2] = "";
        DropQueuedKeys();
    }

    /// <summary>The agent answered with its marker: the link is usable again, and the watch list goes to it afresh.</summary>
    internal void AgentInstalled()
    {
        _agentInstalled = true;
        _watchSent = null;
        _installFailures = 0;
        _notReadySinceUtc = null;
        Interlocked.Exchange(ref _keysDroppedRaised, 0);
        _tags[1] = "";
        _tags[2] = "";
    }

    /// <summary>
    /// An install the page answered without the marker (its scripts have not run yet, after an
    /// aircraft reload): the socket is kept and the install tried again on it next pass, until
    /// <see cref="InstallRetriesPerSocket"/> answers, after which the view is resolved afresh.
    /// </summary>
    private bool KeepSocketAfterUnreadyInstall(string answer)
    {
        Log.Debug("BBS757", $"Tablet agent install answered: {Shorten(answer)}");
        return ++_installFailures < InstallRetriesPerSocket;
    }

    private async Task<bool> EnsureConnected(CancellationToken ct)
    {
        if (_disposed) return false;
        var current = _ws;
        bool open = current != null && current.State == WebSocketState.Open;
        if (open && _agentInstalled) return true;

        // Socket still open but the agent went missing (the page re-evaluated) or deaf, or its
        // install was not ready or timed out: re-install on the SAME socket rather than
        // reconnecting. A page that answers without the marker keeps the socket for a few tries;
        // one that does not answer at all is torn down.
        if (open)
        {
            string reinstall = await EvalAsync(_agentJs, ct);
            if (reinstall.IndexOf(InstallMarker, StringComparison.Ordinal) >= 0)
            {
                AgentInstalled();
                return true;
            }
            if (reinstall.Length > 0 && KeepSocketAfterUnreadyInstall(reinstall)) return false;
        }

        // ONE inspector socket per view: tear down before reconnecting.
        if (_ws != null) DropSocket();

        // A one-shot eval that began before the claim may still hold the view; it wakes the loop
        // as it ends.
        if (CoherentViewOwnership.OneShotInFlight(ViewNeedle)) return false;

        int? pageId = await ResolvePageId(ct);
        if (pageId == null) return false;

        var ws = new ClientWebSocket();
        var url = new Uri($"ws://127.0.0.1:19999/devtools/inspector/{pageId.Value}");
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeoutMs);
            await ws.ConnectAsync(url, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            CloseSocket(ws);   // a connect timeout, not shutdown
            return false;
        }
        catch
        {
            CloseSocket(ws);
            throw;
        }
        _ws = ws;
        // Stop() may have run while connecting; it saw nothing of this socket.
        if (ct.IsCancellationRequested)
        {
            CloseSocket(ws);
            if (ReferenceEquals(_ws, ws)) _ws = null;
            return false;
        }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        _emptyEvalStreak = 0;
        _ = Task.Run(() => ReceiveLoop(ws, ct));

        string install = await EvalAsync(_agentJs, ct);
        if (install.IndexOf(InstallMarker, StringComparison.Ordinal) < 0)
        {
            // Not ready (kept: the next pass re-installs on this socket) or no answer.
            if (install.Length > 0) KeepSocketAfterUnreadyInstall(install);
            return false;
        }
        Log.Info("BBS757", $"Tablet agent installed on view '{ViewNeedle}' (page {pageId.Value}).");
        AgentInstalled();
        return true;
    }

    private static string Shorten(string s) => s.Length <= 200 ? s : s.Substring(0, 200);

    private async Task<int?> ResolvePageId(CancellationToken ct)
    {
        try
        {
            string json = await _http.GetStringAsync($"{DebuggerBase}/pagelist.json", ct);
            using var doc = JsonDocument.Parse(json);
            foreach (var view in doc.RootElement.EnumerateArray())
            {
                if (!view.TryGetProperty("title", out var titleEl)) continue;
                string title = titleEl.GetString() ?? "";
                if (title.IndexOf(ViewNeedle, StringComparison.OrdinalIgnoreCase) >= 0
                    && view.TryGetProperty("id", out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number) return idEl.GetInt32();
                    if (int.TryParse(idEl.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The debugger is not up, or the 757 is not loaded: routine.
            Log.Debug("BBS757", $"Tablet ResolvePageId: {ex.Message}");
        }
        return null;
    }

    private async Task PollOnce(CancellationToken ct)
    {
        int cdu = _activeCdu;
        string since = _tags[cdu];
        string raw = await EvalAsync(ScrapeExpression(cdu, since), ct);
        if (string.IsNullOrEmpty(raw)) { _emptyEvalStreak++; return; }
        _emptyEvalStreak = 0;
        if (raw == NoAgent)
        {
            // The page re-evaluated: re-install on this socket on the next pass.
            Log.Debug("BBS757", "Tablet agent missing, re-installing on the open socket.");
            AgentUnusable();
            return;
        }

        ScrapeResult? result;
        try { result = JsonSerializer.Deserialize<ScrapeResult>(raw); }
        catch (JsonException) { return; }
        if (result == null || !result.ok) return;

        // An agent of an older version (an app update over a loaded flight) would ignore the hold
        // of a key: install the current one, which replaces it.
        if (!AgentIsCurrent(result.version))
        {
            Log.Debug("BBS757", $"Tablet agent version {result.version?.ToString(CultureInfo.InvariantCulture) ?? "none"}, needs {AgentVersion}: re-installing.");
            AgentUnusable();
            return;
        }

        // An agent that cannot hear the Comm Bus would freeze CDU 1 on its seed and never get
        // CDU 2: install it again (the agent registers a new listener then).
        var now = DateTime.UtcNow;
        if (result.ready) _notReadySinceUtc = null;
        else _notReadySinceUtc ??= now;
        if (NeedsReinstall(result.listening, result.ready, _notReadySinceUtc, now))
        {
            Log.Debug("BBS757", $"Tablet agent deaf (listening {result.listening}, ready {result.ready}), re-installing.");
            AgentUnusable();
            return;
        }

        Interlocked.Exchange(ref _lastGoodScrapeTicks, now.Ticks);
        _hasFrame[cdu] = result.hasFrame;

        if (!result.hasFrame)
        {
            _tags[cdu] = result.tag ?? "";
            if (_delivered[cdu])
            {
                _delivered[cdu] = false;
                PostToUI(() => ScreenLost?.Invoke(cdu));
            }
            return;
        }
        if (result.frame == null) return;   // unchanged since the tag we sent

        _tags[cdu] = result.tag ?? "";
        _delivered[cdu] = true;
        var screen = Bbs757CduFrame.Parse(result.frame);
        PostToUI(() => ScreenUpdated?.Invoke(cdu, screen));
    }

    // ---- Runtime.evaluate over the inspector socket -----------------

    private async Task<string> EvalAsync(string expression, CancellationToken ct)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return "";

        int id = Interlocked.Increment(ref _msgId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var msg = JsonSerializer.Serialize(new
        {
            id,
            method = "Runtime.evaluate",
            @params = new { expression, returnByValue = true }
        });

        byte[] bytes = Encoding.UTF8.GetBytes(msg);
        try
        {
            await _sendLock.WaitAsync(ct);
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _pending.TryRemove(id, out _);
            return "";
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(EvalTimeoutMs);
        using (timeout.Token.Register(() => tcs.TrySetCanceled()))
        {
            try { return ExtractValue(await tcs.Task); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ""; }
            finally { _pending.TryRemove(id, out _); }
        }
    }

    private static string ExtractValue(JsonElement root)
    {
        // {"id":N,"result":{"result":{"type":"string","value":"..."},"wasThrown":false}}
        if (root.TryGetProperty("result", out var outer)
            && outer.TryGetProperty("result", out var inner)
            && inner.TryGetProperty("value", out var val))
            return val.ValueKind == JsonValueKind.String ? (val.GetString() ?? "") : val.ToString();
        return "";
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buf = new byte[131072];
        // Accumulate the bytes and decode once at EndOfMessage: decoding each read separately
        // corrupts a multibyte character (the CDU's degree sign) split across reads.
        var ms = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult res;
                do
                {
                    res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (res.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(buf, 0, res.Count);
                } while (!res.EndOfMessage);
                DispatchMessage(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("BBS757", $"Tablet client receive: {ex.Message}"); }
        finally
        {
            // This socket is gone; the loop's next pass sees it closed and reconnects.
            if (ReferenceEquals(_ws, ws)) _agentInstalled = false;
        }
    }

    private void DispatchMessage(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id)
                && _pending.TryGetValue(id, out var tcs))
                tcs.TrySetResult(root.Clone());   // clone: the value must outlive the JsonDocument
        }
        catch (JsonException) { /* malformed frame: ignore */ }
    }

    private void PostToUI(Action action)
    {
        if (_syncContext != null) _syncContext.Post(_ => action(), null);
        else action();
    }

    /// <summary>True once <see cref="Dispose"/> has run.</summary>
    internal bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        // _sendLock is deliberately not disposed: the loop is not joined and may be waiting on
        // it; Stop() canceled the token, which releases the waiter (as the sibling clients do).
    }

    private sealed class WatchResult
    {
        public bool ok { get; set; }
        public bool watching { get; set; }
        public int seq { get; set; }
        public Dictionary<string, double>? changes { get; set; }
    }

    private sealed class ScrapeResult
    {
        public bool ok { get; set; }
        public int? version { get; set; }
        public int cdu { get; set; }
        public bool listening { get; set; }
        public bool ready { get; set; }
        public bool hasFrame { get; set; }
        public string? tag { get; set; }
        public string? frame { get; set; }
    }
}
