using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What became of a click sequence (<see cref="CoherentBbs757EfbClient.RunAsync"/>).</summary>
public enum Bbs757EfbRunResult
{
    /// <summary>Every click made, every delay after it passed.</summary>
    Done,
    /// <summary>No view or agent within a few seconds, or the page went away: nothing, or not all, was clicked.</summary>
    Unreachable,
    /// <summary>The tablet is not the size this version's positions were measured on: nothing was clicked.</summary>
    LayoutUnknown,
    /// <summary>Started but not completed, not answered, or refused for another reason (another run going).</summary>
    NotFinished,
}

/// <summary>
/// The BlueBird 757's EFB tablet, pressed by scripted clicks over the MSFS Coherent GT remote
/// debugger (127.0.0.1:19999). The tablet (the WASM gauge <c>efb_l</c>) lives in the cockpit view
/// whose title holds "BBSNavigraphMap"; <c>Resources/coherent-bbs757-efb-agent.js</c> is installed
/// there (window.__MSFSBA_BBS757EFB) and clicks through the tablet's own mouse handler. The agent
/// clicks wherever it is asked to: neither the client nor the agent refuses a click for the place it
/// lands on (the owner's ruling of 2026-10-29); the press pipeline checks what it taps before it taps.
///
/// Shaped like <see cref="CoherentBbs757TabletClient"/>, without its screen polling: the view is
/// resolved BY TITLE on every (re)connect (its VCockpit number changes on each aircraft load); ONE
/// client per view ([DBG-9]): MainForm owns a single lazily created instance, and the client CLAIMS
/// the view (<see cref="CoherentViewOwnership"/>) from Start to Stop, reconnect gaps included. A
/// still-open socket whose agent went missing gets the agent re-installed on it; any other socket
/// is aborted and disposed before a new one connects ([A380C-2]). An install the page answers
/// NOT_READY (an aircraft reload: the view's scripts or the tablet gauge not there yet) is never
/// taken as installed: it is retried on the same socket, then the view is resolved afresh.
///
/// The loop only keeps the link: it connects, installs, and while the EFB window is shown checks
/// the agent every <see cref="KeepAliveMs"/> ms so the window's status stays true; hidden, it sends
/// nothing. Showing the window pings at once, and that ping or a run's start going unanswered
/// drops the socket straight away (a half-open socket after a reload). <see cref="RunAsync"/>
/// uses the loop's socket and never connects itself, so it cannot race the loop's connection setup
/// ([A380C-3]); it wakes the loop and waits a few seconds for the agent when the link is down.
/// Sequences run one at a time, in the page; a run given up on is canceled there before the next
/// may start, and one still going at Dispose is canceled first.
/// Restart is not supported: dispose and create a new instance.
/// </summary>
public sealed class CoherentBbs757EfbClient : IDisposable
{
    private const string DebuggerBase = "http://127.0.0.1:19999";
    public const string ViewNeedle = "BBSNavigraphMap";
    private const string AgentFile = "coherent-bbs757-efb-agent.js";
    internal const string InstallMarker = "MSFSBA_BBS757_EFB_INSTALLED";
    private const string NoAgent = "no-agent";

    /// <summary>
    /// The agent version the client asks for (its VERSION): 7 is the first without the refusal of a
    /// click near the AIRAC page's UPDATE spot and the run guards it needed. An older agent on the
    /// page answers "no agent" to a run and is replaced by the install.
    /// </summary>
    internal const int AgentVersion = 11;   // 4: mirror(on), 5: wheel (gone), 6: readVars(names), 7: no UPDATE-spot refusal or run guards, 8: drag(x, y0, y1) replaces wheel, 9: drag answers with its run, 11: drag eases out (no fling; 10 was a live trial)

    // How long Dispose waits for its best-effort cancel of a run still going in the page: short,
    // since it runs on the UI thread during a profile switch or app exit.
    private const int TeardownCancelWaitMs = 500;

    // The agent's ping, answered "no agent" by a missing agent or one older than AgentVersion, so
    // the keep-alive has an old agent replaced before a run needs it.
    private static readonly string PingExpression =
        $"window.__MSFSBA_BBS757EFB && __MSFSBA_BBS757EFB.version >= {AgentVersion} ? JSON.stringify(__MSFSBA_BBS757EFB.ping()) : '{NoAgent}'";

    private const int KeepAliveMs = 3000;
    private const int IdleIntervalMs = 5000;
    private const int ReconnectDelayMs = 2000;
    private const int EvalTimeoutMs = 5000;
    private const int ConnectTimeoutMs = 4000;
    // Consecutive evals answered by nothing before the socket is presumed dead (a half-open
    // socket never sends a Close frame).
    private const int DeadSocketEvalFailures = 3;
    // Installs answered without the marker before the view is resolved again from the page list.
    private const int InstallRetriesPerSocket = 5;
    // How long a run waits for the loop to have the agent installed.
    private const int AgentWaitMs = 6000;
    // How long past its planned length a run may still be reported running before it is given up.
    private const int RunSlackMs = 5000;
    private const int StatusPollMs = 250;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    // One sequence at a time: two would interleave their clicks on the tablet.
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly WakeableDelay _wake = new();
    private readonly Func<string> _readAgent;

    private CancellationTokenSource? _cts;
    // _cts.Token captured at Start: reading .Token off a disposed source throws, and a UI-thread
    // run can still arrive during teardown.
    private CancellationToken _loopToken = CancellationToken.None;
    private volatile ClientWebSocket? _ws;
    private string _agentJs = "";
    private int _msgId;
    private volatile bool _agentInstalled;
    private volatile bool _active;
    private int _emptyEvalStreak;
    private int _installFailures;
    // Set when the agent says the tablet canvas is not the measured size; cleared by an install.
    private volatile bool _layoutUnknown;
    private IDisposable? _viewClaim;
    private bool _disposed;
    // Set by SetActive(true): the loop's next keep-alive is the window's first look at the link,
    // and a silent answer to it drops the socket at once (a half-open one after a reload).
    private int _pingOnShow;
    // Whether the last run's start went out and was not refused: it may have clicked.
    private volatile bool _lastRunMayHaveClicked;

    public CoherentBbs757EfbClient()
        : this(() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", AgentFile)))
    {
    }

    /// <param name="readAgent">Reads the page agent's source (the Resources file by default).</param>
    internal CoherentBbs757EfbClient(Func<string> readAgent)
    {
        _readAgent = readAgent;
    }

    /// <summary>True while the socket is open and the agent installed: a sequence can be sent.</summary>
    public bool IsReady
    {
        get
        {
            var ws = _ws;
            return ws != null && ws.State == WebSocketState.Open && _agentInstalled;
        }
    }

    /// <summary>
    /// Whether the last <see cref="RunAsync"/> may have clicked anything: its start went out and the
    /// agent did not refuse it (an unanswered start counts, since it may have run). False for a run
    /// stopped before its start, or refused by the agent: nothing was clicked then.
    /// </summary>
    public bool LastRunMayHaveClicked => _lastRunMayHaveClicked;

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
        // Without its agent the client cannot click, so it never opens a socket and claims nothing.
        if (string.IsNullOrEmpty(_agentJs)) return;

        _viewClaim = CoherentViewOwnership.Claim(ViewNeedle, onOneShotDone: _wake.Wake);
        _ = Task.Run(() => RunLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        var ws = _ws;
        _ws = null;
        CloseSocket(ws);
        _agentInstalled = false;
        _viewClaim?.Dispose();
        _viewClaim = null;
    }

    /// <summary>
    /// Check the link every few seconds while the EFB window is shown; send nothing while it is
    /// hidden. Showing it pings at once, and a ping no one answers then drops the socket straight
    /// away: hidden, the client sent nothing, so a socket left half-open by a reload is found here
    /// rather than by the pilot's first action failing.
    /// </summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            Interlocked.Exchange(ref _pingOnShow, 1);
            _wake.Wake();
        }
    }

    /// <summary>
    /// Runs one click sequence on the tablet and waits until it has finished (every click made and
    /// its delay after it passed): <see cref="Bbs757EfbRunResult.Done"/>, or why not. A run whose
    /// start found no agent or canvas, so nothing was clicked, is tried once more after a
    /// re-install. A start sent but not answered is never sent again: it may have run. A run given
    /// up on is canceled in the page before the next one may start.
    /// </summary>
    public async Task<Bbs757EfbRunResult> RunAsync(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq, CancellationToken ct)
    {
        _lastRunMayHaveClicked = false;
        // Not started, or started without its agent (no loop runs then): nothing could ever click.
        if (seq.Count == 0 || _viewClaim == null || _disposed) return Bbs757EfbRunResult.Unreachable;
        string json = Bbs757EfbLayout.ToJson(seq);
        int plannedMs = Bbs757EfbLayout.TotalMs(seq);
        const int attempts = 2;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _loopToken);
            var token = linked.Token;
            await _runLock.WaitAsync(token);
            try
            {
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    if (!await WaitForAgentAsync(TimeSpan.FromMilliseconds(AgentWaitMs), token))
                        return _layoutUnknown ? Bbs757EfbRunResult.LayoutUnknown : Bbs757EfbRunResult.Unreachable;

                    // Only an agent of this version or later may run it: an older one answers "no
                    // agent" and is replaced by the re-install.
                    string raw = await EvalAsync(
                        $"window.__MSFSBA_BBS757EFB && __MSFSBA_BBS757EFB.version >= {AgentVersion} ? JSON.stringify(__MSFSBA_BBS757EFB.clickSeq({JsonSerializer.Serialize(json)})) : '{NoAgent}'",
                        token);
                    if (raw.Length == 0)
                    {
                        // It may have run. And the socket is presumed dead at once: a half-open
                        // one after a reload otherwise fails two more actions before it is dropped.
                        _lastRunMayHaveClicked = true;
                        NoteUnansweredEval(dropNow: true);
                        Log.Debug("BBS757", "EFB sequence: no answer; not sent again, reconnecting.");
                        return Bbs757EfbRunResult.NotFinished;
                    }
                    if (raw == NoAgent)
                    {
                        AgentUnusable();
                        continue;
                    }

                    var start = Parse(raw);
                    if (start == null)
                    {
                        _lastRunMayHaveClicked = true;   // an answer that cannot be read: it may have run
                        return Bbs757EfbRunResult.NotFinished;
                    }
                    if (!start.ok)
                    {
                        Log.Debug("BBS757", $"EFB sequence refused: {start.error}");
                        var refused = ResultForRefusal(start.error);
                        if (refused == Bbs757EfbRunResult.LayoutUnknown) _layoutUnknown = true;
                        if (start.error != null && start.error.StartsWith("no-canvas", StringComparison.Ordinal))
                        {
                            AgentUnusable();
                            if (refused == Bbs757EfbRunResult.Unreachable) continue;
                        }
                        return refused;
                    }
                    _lastRunMayHaveClicked = true;
                    if (start.error != null)
                    {
                        Log.Debug("BBS757", $"EFB sequence stopped at its first click: {start.error}");
                        if (IsSizeProblem(start.error)) { _layoutUnknown = true; return Bbs757EfbRunResult.LayoutUnknown; }
                        return Bbs757EfbRunResult.NotFinished;
                    }
                    try
                    {
                        return await WaitForRunAsync(start.id, plannedMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // The window or the client is going away mid-run: stop the tablet's run
                        // before the lock is let go, so one run at a time still holds.
                        if (!_disposed && IsReady) await CancelRunAsync();
                        throw;
                    }
                }
                return Bbs757EfbRunResult.Unreachable;
            }
            finally
            {
                _runLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return Bbs757EfbRunResult.NotFinished;
        }
        catch (ObjectDisposedException)
        {
            return Bbs757EfbRunResult.NotFinished;
        }
    }

    /// <summary>
    /// Turns the tablet reader's mirror on or off (the agent's mirror(): the tablet's live image laid
    /// over the captain's ND, so the ND pop-out shows the tablet; <see cref="Bbs757EfbPopout"/>).
    /// True when the agent did it, false when it answered that it could not (no gauge, no image),
    /// null when the view or agent could not be reached. It never connects ([A380C-3]); it may run
    /// while no click run is going and takes no run's place.
    /// </summary>
    public async Task<bool?> SetMirrorAsync(bool on, CancellationToken ct)
    {
        using var doc = await CallAgentAsync("mirror", on ? "true" : "false", ct);
        if (doc == null) return null;
        bool ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        if (!ok) Log.Debug("BBS757", $"EFB mirror({on}) refused: {Shorten(doc.RootElement.GetRawText())}");
        return ok;
    }

    /// <summary>
    /// BlueBird L:vars read in the tablet's page (the agent's readVars: SimVar.GetSimVarValue, no
    /// SimConnect definition). Names without the "L:" prefix, letters, digits and underscores, at most
    /// 64. A value is null when the page could not read it; the whole answer is null when the agent
    /// could not be reached or refused the names.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, double?>?> ReadVarsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        if (names.Count == 0 || names.Count > 64 || names.Any(n => !System.Text.RegularExpressions.Regex.IsMatch(n, "^[A-Za-z0-9_]{1,80}$")))
            return null;
        using var doc = await CallAgentAsync("readVars", JsonSerializer.Serialize(names), ct);
        if (doc == null || !doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True) return null;
        if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object) return null;
        var o = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (var n in names)
            o[n] = values.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        return o;
    }

    /// <summary>
    /// A drag at a tablet-local x from y0 to y1 (this build scrolls on a drag), as a run of the agent's: one at a time with the click
    /// runs (under the run lock, so the tap that follows never finds it still going and refused as busy), started in the page and
    /// waited for by its status, with the click runs' slack (the agent's timers can fire at the gauge's frame rate, so it can take
    /// longer than its planned 1,400 ms). True only once the run finished with every move made and the mouse let go; false when the
    /// agent refused it (busy, a point off the canvas), it stopped part way, or the view could not be reached. A cancel stops the
    /// run in the page before the lock is let go, and answers false.
    /// </summary>
    public async Task<bool> DragAsync(int x, int y0, int y1, CancellationToken ct)
    {
        if (_viewClaim == null || _disposed) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string args = string.Join(",", x.ToString(inv), y0.ToString(inv), y1.ToString(inv));
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _loopToken);
            var token = linked.Token;
            await _runLock.WaitAsync(token);
            try
            {
                if (!await WaitForAgentAsync(TimeSpan.FromMilliseconds(AgentWaitMs), token)) return false;
                string raw = await EvalAsync(
                    $"window.__MSFSBA_BBS757EFB && __MSFSBA_BBS757EFB.version >= {AgentVersion} && typeof __MSFSBA_BBS757EFB.drag === 'function' ? JSON.stringify(__MSFSBA_BBS757EFB.drag({args})) : '{NoAgent}'",
                    token);
                if (raw.Length == 0) { NoteUnansweredEval(); return false; }
                if (raw == NoAgent) { AgentUnusable(); return false; }
                if (DragRun(raw) is not { } run) return false;
                try
                {
                    return await WaitForRunAsync(run.Id, run.PlannedMs, token) == Bbs757EfbRunResult.Done;
                }
                catch (OperationCanceledException)
                {
                    // The press is going away mid-drag: stop the tablet's run (it lets go of the mouse) before the lock is let go.
                    if (!_disposed && IsReady) await CancelRunAsync();
                    throw;
                }
            }
            finally
            {
                _runLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>The agent's drag takes 20 moves 50 ms apart and a 400 ms hold: how long its run is planned to last when its answer
    /// does not say.</summary>
    internal const int DragPlannedMs = 1400;

    /// <summary>The run a drag's answer started (its id, and how long it is planned to last); null when the agent refused it
    /// (<c>{ok:false, error}</c>: busy, a point off the canvas, no canvas), when it stopped at its start (an error with the run), or
    /// when the answer cannot be read. Internal for the tests.</summary>
    internal static (int Id, int PlannedMs)? DragRun(string raw)
    {
        var start = Parse(raw);
        if (start == null || !start.ok || start.error != null)
        {
            Log.Debug("BBS757", $"EFB drag not started: {(start == null ? "unreadable answer" : start.error ?? "refused")}");
            return null;
        }
        return (start.id, start.totalMs > 0 ? start.totalMs : DragPlannedMs);
    }

    /// <summary>
    /// Calls one of the agent's functions with literal JS arguments and parses its JSON answer. Null
    /// when the view or agent cannot be reached (the loop re-installs it), or the answer is not JSON.
    /// Never connects ([A380C-3]) and takes no run's place: the reader's variable reads and the mirror
    /// share the loop's socket between click runs (a drag is a run of its own, <see cref="DragAsync"/>).
    /// </summary>
    private async Task<JsonDocument?> CallAgentAsync(string function, string args, CancellationToken ct)
    {
        if (_viewClaim == null || _disposed) return null;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _loopToken);
            if (!await WaitForAgentAsync(TimeSpan.FromMilliseconds(AgentWaitMs), linked.Token)) return null;
            string raw = await EvalAsync(
                $"window.__MSFSBA_BBS757EFB && __MSFSBA_BBS757EFB.version >= {AgentVersion} && typeof __MSFSBA_BBS757EFB.{function} === 'function' ? JSON.stringify(__MSFSBA_BBS757EFB.{function}({args})) : '{NoAgent}'",
                linked.Token);
            if (raw.Length == 0) { NoteUnansweredEval(); return null; }
            if (raw == NoAgent) { AgentUnusable(); return null; }
            return JsonDocument.Parse(raw);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Waits out a started run, then asks the agent how far it got. A run still going after its
    /// slack, or whose status no one answers, is given up on and canceled in the page.
    /// </summary>
    private async Task<Bbs757EfbRunResult> WaitForRunAsync(int id, int plannedMs, CancellationToken ct)
    {
        await Task.Delay(plannedMs + StatusPollMs, ct);
        var until = DateTime.UtcNow.AddMilliseconds(RunSlackMs);
        while (true)
        {
            string raw = await EvalAsync(
                $"window.__MSFSBA_BBS757EFB ? JSON.stringify(__MSFSBA_BBS757EFB.status({id})) : '{NoAgent}'", ct);
            if (raw.Length == 0)
            {
                NoteUnansweredEval();
                await CancelRunAsync();
                return Bbs757EfbRunResult.NotFinished;
            }
            if (raw == NoAgent) { AgentUnusable(); return Bbs757EfbRunResult.Unreachable; }   // the page went away mid-run

            var status = Parse(raw);
            if (status == null || !status.ok) return Bbs757EfbRunResult.NotFinished;
            if (!status.running)
            {
                if (status.error == null && status.done == status.n) return Bbs757EfbRunResult.Done;
                // Why it stopped, never how far it got: a typing run's clicks are keys, so a count gives away the text's length (D2).
                Log.Debug("BBS757", $"EFB sequence stopped: {status.error}");
                if (IsSizeProblem(status.error)) { _layoutUnknown = true; return Bbs757EfbRunResult.LayoutUnknown; }
                return Bbs757EfbRunResult.NotFinished;
            }
            if (DateTime.UtcNow >= until)
            {
                Log.Debug("BBS757", "EFB sequence still running: canceled.");
                await CancelRunAsync();
                return Bbs757EfbRunResult.NotFinished;
            }
            await Task.Delay(StatusPollMs, ct);
        }
    }

    /// <summary>Stops the page's run in progress, best effort (never throws, at most one eval timeout).</summary>
    private async Task CancelRunAsync()
    {
        try { await EvalAsync("window.__MSFSBA_BBS757EFB ? JSON.stringify(__MSFSBA_BBS757EFB.cancel()) : ''", CancellationToken.None); }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
    }

    /// <summary>What the window is told when the agent refused a run's start for <paramref name="error"/>.</summary>
    internal static Bbs757EfbRunResult ResultForRefusal(string? error)
    {
        if (IsSizeProblem(error)) return Bbs757EfbRunResult.LayoutUnknown;
        if (error != null && error.StartsWith("no-canvas", StringComparison.Ordinal)) return Bbs757EfbRunResult.Unreachable;
        return Bbs757EfbRunResult.NotFinished;   // busy, bad-seq, …
    }

    /// <summary>
    /// Whether an agent answer says the tablet canvas is not the size the positions were measured
    /// on (an install's NOT_READY, a refusal's or a click's "no-canvas", a ping's problem).
    /// </summary>
    internal static bool IsSizeProblem(string? answer) =>
        answer != null && (answer.StartsWith("size ", StringComparison.Ordinal)
                           || answer.Contains(":size ", StringComparison.Ordinal)
                           || answer.Contains(": size ", StringComparison.Ordinal));

    private static RunAnswer? Parse(string raw)
    {
        try { return JsonSerializer.Deserialize<RunAnswer>(raw); }
        catch (JsonException) { return null; }
    }

    /// <summary>Waits until the loop has the agent installed on an open socket, waking it meanwhile.</summary>
    private async Task<bool> WaitForAgentAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (!ct.IsCancellationRequested)
        {
            if (IsReady) return true;
            if (DateTime.UtcNow >= until) return false;
            _wake.Wake();
            await Task.Delay(100, ct);
        }
        return false;
    }

    /// <summary>The agent stopped being usable: install it again on the loop's next pass.</summary>
    private void AgentUnusable()
    {
        _agentInstalled = false;
        _wake.Wake();
    }

    /// <summary>
    /// An eval no one answered: after a few in a row the loop drops the socket, or on its next pass
    /// when <paramref name="dropNow"/> (an unanswered run start, or the ping when the window is shown).
    /// </summary>
    private void NoteUnansweredEval(bool dropNow = false)
    {
        if (dropNow) Interlocked.Exchange(ref _emptyEvalStreak, DeadSocketEvalFailures);
        else Interlocked.Increment(ref _emptyEvalStreak);
        _wake.Wake();
    }

    // ---- connection loop ------------------------------------------------

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Volatile.Read(ref _emptyEvalStreak) >= DeadSocketEvalFailures)
                {
                    Log.Debug("BBS757", "EFB client: no eval answered, reconnecting.");
                    DropSocket();
                }
                if (!await EnsureConnected(ct))
                {
                    await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct);
                    continue;
                }
                if (_active) await KeepAlive(ct, firstSinceShown: Interlocked.Exchange(ref _pingOnShow, 0) == 1);
                await _wake.WaitAsync(TimeSpan.FromMilliseconds(_active ? KeepAliveMs : IdleIntervalMs), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Debug("BBS757", $"EFB client loop: {ex.Message}");
                DropSocket();
                try { await _wake.WaitAsync(TimeSpan.FromMilliseconds(ReconnectDelayMs), ct); } catch { break; }
            }
        }
    }

    /// <summary>
    /// While the window is shown: is the agent still there, and can it still click? The first ping
    /// after the window is shown (<paramref name="firstSinceShown"/>) drops a silent socket at once.
    /// </summary>
    private async Task KeepAlive(CancellationToken ct, bool firstSinceShown)
    {
        string raw = await EvalAsync(PingExpression, ct);
        if (raw.Length == 0)
        {
            if (firstSinceShown)
            {
                Log.Debug("BBS757", "EFB client: no answer when the window was shown, reconnecting.");
                NoteUnansweredEval(dropNow: true);
            }
            else
            {
                Interlocked.Increment(ref _emptyEvalStreak);
            }
            return;
        }
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
        if (raw == NoAgent)
        {
            Log.Debug("BBS757", "EFB agent missing, re-installing on the open socket.");
            AgentUnusable();
            return;
        }
        try
        {
            var ping = JsonSerializer.Deserialize<PingAnswer>(raw);
            if (ping == null || !ping.ok) return;
            if (!ping.ready)
            {
                // The tablet gauge went (a reload in progress) or is another size: the re-install
                // answers NOT_READY until it is back.
                Log.Debug("BBS757", $"EFB agent cannot click now ({ping.problem}), re-installing.");
                if (IsSizeProblem(ping.problem)) _layoutUnknown = true;
                AgentUnusable();
            }
        }
        catch (JsonException) { }
    }

    /// <summary>Abort AND dispose: Abort alone leaks the socket's native handle on every reconnect.</summary>
    private static void CloseSocket(ClientWebSocket? ws)
    {
        if (ws == null) return;
        try { ws.Abort(); } catch { }
        try { ws.Dispose(); } catch { }
    }

    /// <summary>The one teardown path: close the socket and fail the evals waiting on it.</summary>
    private void DropSocket()
    {
        var ws = _ws;
        _ws = null;
        _agentInstalled = false;
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
        _installFailures = 0;
        CloseSocket(ws);
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
    }

    /// <summary>
    /// An install the page answered without the marker (its scripts or the tablet gauge not ready,
    /// after an aircraft reload): the socket is kept and the install tried again on it next pass,
    /// until <see cref="InstallRetriesPerSocket"/> answers, after which the view is resolved afresh.
    /// </summary>
    private bool KeepSocketAfterUnreadyInstall(string answer)
    {
        Log.Debug("BBS757", $"EFB agent install answered: {Shorten(answer)}");
        if (IsSizeProblem(answer)) _layoutUnknown = true;
        return ++_installFailures < InstallRetriesPerSocket;
    }

    private void AgentInstalled()
    {
        _agentInstalled = true;
        _layoutUnknown = false;
        _installFailures = 0;
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
    }

    private async Task<bool> EnsureConnected(CancellationToken ct)
    {
        if (_disposed) return false;
        var current = _ws;
        bool open = current != null && current.State == WebSocketState.Open;
        if (open && _agentInstalled) return true;

        // Socket still open but the agent went missing (the page re-evaluated) or cannot click,
        // or its install was not ready: re-install on the SAME socket rather than reconnecting. A
        // page that answers without the marker keeps the socket for a few tries; one that does not
        // answer at all is torn down.
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
        Interlocked.Exchange(ref _emptyEvalStreak, 0);
        _ = Task.Run(() => ReceiveLoop(ws, ct));

        string install = await EvalAsync(_agentJs, ct);
        if (install.IndexOf(InstallMarker, StringComparison.Ordinal) < 0)
        {
            // Not ready (kept: the next pass re-installs on this socket) or no answer.
            if (install.Length > 0) KeepSocketAfterUnreadyInstall(install);
            return false;
        }
        Log.Info("BBS757", $"EFB agent installed on view '{ViewNeedle}' (page {pageId.Value}).");
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
                    if (int.TryParse(idEl.GetString(), System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The debugger is not up, or the 757 is not loaded: routine.
            Log.Debug("BBS757", $"EFB ResolvePageId: {ex.Message}");
        }
        return null;
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
        var buf = new byte[65536];
        // Accumulate the bytes and decode once at EndOfMessage: decoding each read separately
        // corrupts a multibyte character split across reads.
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
        catch (Exception ex) { Log.Debug("BBS757", $"EFB client receive: {ex.Message}"); }
        finally
        {
            // This socket is gone: the loop reconnects on its next pass, which this wakes.
            if (ReferenceEquals(_ws, ws))
            {
                _agentInstalled = false;
                _wake.Wake();
            }
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

    /// <summary>
    /// A run still holding the run lock at teardown (the window was closed by a profile switch or
    /// app exit mid-run) keeps clicking in the page after the socket goes: its cancel is sent first,
    /// best effort, waiting at most <see cref="TeardownCancelWaitMs"/>. Run on the pool, so the wait
    /// on the UI thread cannot deadlock on a continuation posted back to it.
    /// </summary>
    private void CancelRunOnTeardown()
    {
        if (_runLock.CurrentCount != 0 || !IsReady) return;   // no run in progress, or no link to send on
        try
        {
            if (!Task.Run(CancelRunAsync).Wait(TeardownCancelWaitMs))
                Log.Debug("BBS757", "EFB client: run cancel at teardown not confirmed in time.");
        }
        catch (AggregateException ex)
        {
            Log.Debug("BBS757", $"EFB client: run cancel at teardown failed: {ex.InnerException?.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        CancelRunOnTeardown();
        _disposed = true;
        Stop();
        try { _cts?.Dispose(); } catch { }
        try { _http.Dispose(); } catch { }
        foreach (var kv in _pending) kv.Value.TrySetCanceled();
        _pending.Clear();
        // _sendLock and _runLock are deliberately not disposed: the loop and a run are not joined
        // and may be waiting on them; Stop() canceled the token, which releases the waiters.
    }

    private sealed class RunAnswer
    {
        public bool ok { get; set; }
        public int id { get; set; }
        public int n { get; set; }
        public int done { get; set; }
        public bool running { get; set; }
        public string? error { get; set; }
        public int totalMs { get; set; }
    }

    private sealed class PingAnswer
    {
        public bool ok { get; set; }
        public bool ready { get; set; }
        public string? problem { get; set; }
    }
}
