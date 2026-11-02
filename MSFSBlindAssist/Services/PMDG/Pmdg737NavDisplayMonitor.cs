using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>What the Navigation display window shows: its rows, and the reading they come from (null unless
/// the reader is running).</summary>
public sealed record NavDisplayView(IReadOnlyList<string> Lines, NavDisplaySample? Sample, Pmdg737NavDisplayLayout? Layout);

/// <summary>
/// Reads the PMDG 737 Captain's navigation display from PMDG's module memory and announces its messages and
/// active waypoint changes by themselves. The engine display monitor's state machine, copied (not shared):
/// ticked every 500 ms on the UI thread, it waits for the PMDG SDK, has the shared module session analyse
/// PMDG's file and locate its memory (through <see cref="INavDisplayEnvironment"/>, off the UI thread, polled
/// and never continued), trusts a candidate only once a reading passes the one gate (the display units'
/// fingerprint, the Captain's EFIS mode and range against the SDK, no corrupt reading from the sampler),
/// then speaks each change once it has held for 500 ms. The first good reading after Start or Restart is a
/// silent baseline; nothing is compared while the displays are dark, nor for 10 s after they light up. Every
/// way the reader stops is spoken once, "resumed" is followed by whatever changed meanwhile, and the window
/// (<see cref="View"/>) shows "starting" or "stopped", never a stale reading. One Ctrl+M row mutes all of it.
/// Nothing interrupts. Errors never escape a tick: they are logged and end in "unavailable", and Restart
/// recovers.
/// </summary>
public sealed class Pmdg737NavDisplayMonitor : IDisposable
{
    /// <summary>The Ctrl+M row "ND messages": messages, waypoint changes and the status lines.</summary>
    public const string MuteKey = "MSFSBA_PMDG737_ND_MESSAGES";

    internal const int TickMs = 500;
    internal const long SettleMs = 500;
    internal const long LocateRetryMs = 5_000;
    internal const long LocateGiveUpMs = 120_000;
    internal const long SlowRetryMs = 30_000;
    internal const long LostStopMs = 10_000;
    internal const long MismatchToleranceMs = 2_000;
    internal const int CorruptTickLimit = 10;

    /// <summary>After the displays light up, what settles meanwhile (the FMC coming up a moment after the
    /// display units) is absorbed into the baseline silently: power-up is never spoken as news.</summary>
    internal const long LightUpQuietMs = 10_000;

    private enum State { Stopped, WaitingForSdk, Analysing, Locating, Verifying, Running, Mismatched, Unavailable }

    private INavDisplayEnvironment? _env;
    private System.Windows.Forms.Timer? _timer;
    private State _state = State.Stopped;
    private bool _inTick;

    // The file analysis and the memory search: started here, run off the UI thread, polled each tick.
    private IReadOnlyList<Pmdg737NavDisplayLayout>? _layouts;
    private Task<NavDisplayAnalysis>? _analysis;
    private Task<NavDisplayLocateResult>? _locate;
    private long _locateStartMs;
    private long? _lastAttemptMs;
    private int _attempts;
    private bool _slowRetry; // the 120 s window ran out and was spoken: search every 30 s until found
    private long? _lostAtMs; // when the memory was lost while running, until it verifies again (the 10 s stop)

    // The candidate being read and those not tried yet. The monitor owns them all and disposes each once.
    private INavDisplaySampler? _sampler;
    private readonly Queue<INavDisplaySampler> _candidates = new();
    private long _candidateStartMs;
    private readonly Queue<(long AtMs, NavDisplaySample Sample)> _history = new();

    // Running.
    private Pmdg737NavDisplayLayout? _layout;
    private NavDisplaySample? _spoken;  // what the pilot last heard (or would have, unmuted)
    private NavDisplaySample? _current; // the latest good reading, for the window and Ctrl+W
    private long? _changeAtMs;
    private bool _wasLit;         // the displays were lit at the last good reading
    private long? _quietUntilMs;  // the displays lit up: changes until then become the baseline silently
    private long _lastPassMs;
    private int _corruptTicks;
    private bool _everRunning;
    private string? _stopReason; // the reason in the stop line the pilot heard (or would have, unmuted), until it runs again
    // A stop or unavailable line was said (or would have been, unmuted) and no "resumed" since; survives
    // Restart, so a stop heard before a flight reload is answered.
    private bool _resumeOwed;
    private string _reason = FmaReasons.InternalError; // why unavailable, or the search window's last failure
    // Since when, and why, the reader that ran has shown nothing (lost memory or a mismatch): the window says
    // "stopped" meanwhile, and Output Alt+N falls back to the AI read once it lasts LostStopMs.
    private long? _downSinceMs;
    private string _downReason = FmaReasons.MemoryNotFound;

    internal string StateName => _state.ToString();

    /// <summary>Starts reading through <paramref name="env"/>. On a monitor that is already started it
    /// swaps in the environment and restarts; the layouts are kept only for the same environment.</summary>
    public void Start(INavDisplayEnvironment env, bool startTimer = true)
    {
        ArgumentNullException.ThrowIfNull(env);
        bool wasStopped = _state == State.Stopped;
        ReleaseReading();
        if (!ReferenceEquals(env, _env))
        {
            AbandonAnalysis();
            _layouts = null;
            _resumeOwed = false;
        }
        _env = env;
        BeginSession(wasStopped ? "started" : "started again");
        if (startTimer && _timer is null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = TickMs };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    /// <summary>Flight reload: drops the memory and searches again, keeping the derived layouts. The window
    /// says "starting" until the new flight's first good reading, its silent baseline.</summary>
    public void Restart()
    {
        if (_state == State.Stopped || _env is null) return;
        ReleaseReading();
        BeginSession("restarted");
    }

    /// <summary>Aircraft switch or exit: stops the timer and releases everything.</summary>
    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer.Dispose();
            _timer = null;
        }
        if (_env is null) return;
        ReleaseReading();
        AbandonAnalysis();
        _layouts = null;
        ResetSession();
        _resumeOwed = false;
        Go(State.Stopped, "stopped");
        _env = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The window's rows: running, those of the latest good reading; unavailable, why; stopped (a mismatch,
    /// lost memory, or a search window that ran out), that it is trying again; otherwise "starting". Never a
    /// reading the gate has not passed, never a stale one.
    /// </summary>
    public NavDisplayView View()
    {
        if (_state == State.Running && _current is { } s && _layout is { } l)
            return new NavDisplayView(Pmdg737NavDisplayText.Lines(s, l), s, l);
        string line = _state switch
        {
            State.Unavailable => Pmdg737NavDisplaySpeech.ListUnavailable(_reason),
            State.Mismatched => Pmdg737NavDisplaySpeech.ListStopped(FmaReasons.Mismatch),
            State.Stopped or State.WaitingForSdk => Pmdg737NavDisplaySpeech.Starting,
            _ when _downSinceMs is not null => Pmdg737NavDisplaySpeech.ListStopped(_downReason),
            _ when _slowRetry => Pmdg737NavDisplaySpeech.ListStopped(_reason),
            _ => Pmdg737NavDisplaySpeech.Starting,
        };
        return new NavDisplayView(new[] { line }, null, null);
    }

    /// <summary>
    /// Whether Output Alt+N goes straight to the AI read, saying nothing first (WASM-29), instead of opening the window:
    /// the reader is unavailable, its search window ran out, or it has been stopped for <see cref="LostStopMs"/> or
    /// longer. False while it starts, runs, or is stopped for less (the window opens and says so).
    /// </summary>
    public bool KeyFallsBackToAi()
    {
        if (_state == State.Unavailable) return true;
        if (_env is null || _state == State.WaitingForSdk) return false;
        if (_downSinceMs is { } down && _env.NowMs - down >= LostStopMs) return true;
        return _slowRetry;
    }

    /// <summary>
    /// Output Ctrl+W's answer, from the latest good reading whether or not a display shows the ND (the corner
    /// is the FMC's): the top-right corner as printed, or "No active waypoint". Otherwise why there is none:
    /// "starting" while it starts, else unavailable with the reason (waiting for the PMDG SDK data, a stop; never a
    /// stale waypoint).
    /// </summary>
    public string WaypointLine()
    {
        if (_state == State.Running && _current is { } s && _layout is { } l)
            return Pmdg737NavDisplayText.TopRight(s, l) ?? Pmdg737NavDisplaySpeech.NoActiveWaypoint;
        return _state switch
        {
            State.Unavailable => Pmdg737NavDisplaySpeech.WaypointUnavailable(_reason),
            State.Mismatched => Pmdg737NavDisplaySpeech.WaypointUnavailable(FmaReasons.Mismatch),
            State.WaitingForSdk => Pmdg737NavDisplaySpeech.WaypointUnavailable(Pmdg737NavDisplaySpeech.WaitingForSdk),
            State.Stopped => Pmdg737NavDisplaySpeech.Starting,
            _ when _downSinceMs is not null => Pmdg737NavDisplaySpeech.WaypointUnavailable(_downReason),
            _ when _slowRetry => Pmdg737NavDisplaySpeech.WaypointUnavailable(_reason),
            _ => Pmdg737NavDisplaySpeech.Starting,
        };
    }

    private void OnTimerTick(object? sender, EventArgs e) => Tick();

    internal void Tick()
    {
        // Unavailable holds until Restart or Start; the timer keeps running so either can recover.
        if (_inTick || _env is null || _state is State.Stopped or State.Unavailable) return;
        _inTick = true;
        try
        {
            Step(_env);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
        finally
        {
            _inTick = false;
        }
    }

    private void Step(INavDisplayEnvironment env)
    {
        var sdk = env.ReadSdk();
        if (sdk is null)
        {
            // The aircraft may be gone: drop its memory, keep the layouts (and an analysis in flight).
            if (_state != State.WaitingForSdk)
            {
                ReleaseReading();
                _lostAtMs = null; // waiting for the SDK is silent
                _downSinceMs = null;
                Go(State.WaitingForSdk, "PMDG SDK data not available");
            }
            return;
        }
        SayIfLostTooLong(env.NowMs);
        switch (_state)
        {
            case State.WaitingForSdk: OnWaitingForSdk(env); break;
            case State.Analysing: OnAnalysing(); break;
            case State.Locating: OnLocating(env); break;
            case State.Verifying: OnVerifying(env, sdk.Value); break;
            case State.Running: OnRunning(env, sdk.Value); break;
            case State.Mismatched: OnMismatched(env, sdk.Value); break;
        }
    }

    private void OnWaitingForSdk(INavDisplayEnvironment env)
    {
        if (_layouts is not null)
        {
            BeginLocating(newWindow: true, "PMDG SDK ready");
            return;
        }
        _analysis ??= env.AnalyseAsync();
        Go(State.Analysing, "PMDG SDK ready; analysing PMDG's file");
    }

    private void OnAnalysing()
    {
        var task = _analysis!;
        if (!task.IsCompleted) return;
        _analysis = null;
        if (!task.IsCompletedSuccessfully)
        {
            FailTask("the file analysis", task);
            return;
        }
        var analysis = task.Result;
        if (analysis.Layouts.Count == 0)
        {
            // A file that loaded but lacks this reader's layout is its own line; no file loaded is the shared one (WASM-29).
            string reason = analysis.Failure ?? FmaReasons.FileNotRecognised;
            if (analysis.LayoutFileLoaded) EnterUnavailable(reason);
            else EnterNoLayoutFile(reason);
            return;
        }
        _layouts = analysis.Layouts;
        BeginLocating(newWindow: true, Invariant($"{analysis.Layouts.Count} layout(s) derived"));
    }

    /// <summary><paramref name="newWindow"/> starts a new 120 s search window; attempts are always at least
    /// 5 s apart (only Start and Restart reset that).</summary>
    private void BeginLocating(bool newWindow, string reason)
    {
        ReleaseReading();
        if (newWindow)
        {
            _locateStartMs = _env!.NowMs;
            _attempts = 0;
            _slowRetry = false;
            _reason = FmaReasons.MemoryNotFound;
        }
        Go(State.Locating, reason);
        OnLocating(_env!);
    }

    private void OnLocating(INavDisplayEnvironment env)
    {
        long now = env.NowMs;
        if (_locate is null)
        {
            if (!_slowRetry && now - _locateStartMs >= LocateGiveUpMs)
            {
                // Not final: say so once, then keep searching, slowly and silently.
                _slowRetry = true;
                Log(Invariant($"{_reason} after {_attempts} attempt(s); searching every {SlowRetryMs / 1000} s"));
                SayStopped(_reason);
            }
            if (_lastAttemptMs is { } last && now - last < (_slowRetry ? SlowRetryMs : LocateRetryMs)) return;
            _lastAttemptMs = now;
            _attempts++;
            _locate = env.LocateAsync(_layouts!);
            return;
        }
        if (!_locate.IsCompleted) return;
        var task = _locate;
        _locate = null;
        if (!task.IsCompletedSuccessfully)
        {
            FailTask("the memory search", task);
            return;
        }
        var result = task.Result;
        if (result.Candidates.Count > 0)
        {
            foreach (var c in result.Candidates) _candidates.Enqueue(c);
            StartNextCandidate(Invariant($"{result.Candidates.Count} candidate(s) found"));
            return;
        }
        if (result.Failure is not null)
        {
            EnterUnavailable(result.Failure); // NoAccess is final
            return;
        }
        _reason = FmaReasons.MemoryNotFound;
    }

    private void StartNextCandidate(string reason)
    {
        _sampler = _candidates.Dequeue();
        _candidateStartMs = _env!.NowMs;
        _history.Clear();
        Go(State.Verifying, reason);
    }

    private void OnVerifying(INavDisplayEnvironment env, NavDisplaySdkSnapshot sdk)
    {
        long now = env.NowMs;
        if (!_sampler!.TryRead(out var sample, out var corrupt))
        {
            if (corrupt is null)
            {
                DropCandidate(readFailed: true);
                return;
            }
        }
        else
        {
            Remember(now, sample);
            if (Passes(sdk, sample, now))
            {
                while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue());
                EnterRunning(sample, "the memory agrees with the PMDG SDK");
                return;
            }
        }
        if (now - _candidateStartMs >= MismatchToleranceMs) DropCandidate(readFailed: false);
    }

    /// <summary>The candidate failed: the next one is tried, else a new search in the same window, which
    /// remembers why (a failed read is "memory not found", a disagreement "values do not match").</summary>
    private void DropCandidate(bool readFailed)
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        _reason = readFailed ? FmaReasons.MemoryNotFound : FmaReasons.Mismatch;
        string why = readFailed ? "the memory read failed" : "no good reading (matching the PMDG SDK, no unexpected value) for 2 s";
        if (_candidates.Count > 0)
            StartNextCandidate($"{why}; trying the next candidate");
        else
            BeginLocating(newWindow: false, $"{why}; searching again");
    }

    private void EnterRunning(NavDisplaySample sample, string reason)
    {
        _layout = _sampler!.Layout;
        // The first good reading after Start or Restart is the baseline; later in the session, what changed
        // while the reader was not running is told after "resumed", never swallowed.
        var missed = _everRunning && _spoken is { } before
            ? Pmdg737NavDisplaySpeech.Changes(before, sample, _layout)
            : Array.Empty<string>();
        bool heardStop = _resumeOwed;
        _spoken = _current = sample;
        _changeAtMs = null;
        _wasLit = Pmdg737NavDisplayCrossCheck.DisplaysLit(sample, _layout);
        _quietUntilMs = null;
        _lastPassMs = _env!.NowMs;
        _corruptTicks = 0;
        _everRunning = true;
        _stopReason = null;
        _resumeOwed = false;
        _slowRetry = false;
        _lostAtMs = null;
        _downSinceMs = null;
        Go(State.Running, reason);
        if (heardStop) Say(Pmdg737NavDisplaySpeech.Resumed);
        foreach (var line in missed) Say(line);
    }

    private void OnRunning(INavDisplayEnvironment env, NavDisplaySdkSnapshot sdk)
    {
        long now = env.NowMs;
        if (!TryReadRunning(now, out var sample, out var corrupt)) return;

        // A corrupt reading is never shown or spoken and only counts toward the unexpected-values stop.
        if (corrupt is not null)
        {
            if (_corruptTicks++ == 0) Log($"unexpected value: {corrupt}");
            if (_corruptTicks >= CorruptTickLimit)
            {
                var why = FmaReasons.UnexpectedValues;
                EnterUnavailable(why, Pmdg737NavDisplaySpeech.Stopped(why), Invariant($"{why} for {CorruptTickLimit} readings: {corrupt}"));
            }
            return;
        }
        _corruptTicks = 0;

        // Only a good reading is shown or spoken; any other only counts toward the mismatch stop.
        if (Passes(sdk, sample!, now))
        {
            _lastPassMs = now;
        }
        else
        {
            if (now - _lastPassMs >= MismatchToleranceMs)
            {
                _changeAtMs = null;
                _downSinceMs ??= now;
                _downReason = FmaReasons.Mismatch;
                Go(State.Mismatched, "the memory has not matched the PMDG SDK for 2 s");
                SayStopped(FmaReasons.Mismatch);
            }
            return; // an open settle window keeps its start; the next good reading speaks
        }
        _current = sample;
        SpeakSettledChange(now);
    }

    private void OnMismatched(INavDisplayEnvironment env, NavDisplaySdkSnapshot sdk)
    {
        long now = env.NowMs;
        if (!TryReadRunning(now, out var sample, out var corrupt) || corrupt is not null) return;
        if (Passes(sdk, sample!, now)) EnterRunning(sample!, "the memory agrees with the PMDG SDK again");
    }

    /// <summary>A failed read means the memory moved or went away: search again, silently at first. From
    /// Running, a search that has not verified within 10 s says "stopped"; from Mismatched it was said. A
    /// corrupt reading is a read (the memory is there): it comes back with its reason and no sample.</summary>
    private bool TryReadRunning(long now, out NavDisplaySample? sample, out string? corrupt)
    {
        if (!_sampler!.TryRead(out var read, out corrupt))
        {
            sample = null;
            if (corrupt is not null) return true;
            if (_state == State.Running)
            {
                _lostAtMs = now;
                _downSinceMs ??= now;
                _downReason = FmaReasons.MemoryNotFound;
            }
            BeginLocating(newWindow: true, "the memory read failed; searching again");
            return false;
        }
        sample = read;
        Remember(now, read);
        return true;
    }

    /// <summary>
    /// Speaks what changed once it has held for 500 ms. A change and back inside the window says nothing;
    /// the baseline advances even when muted, and with nothing to say: while the displays are dark and for
    /// <see cref="LightUpQuietMs"/> after they light up, so power-up is silent.
    /// </summary>
    private void SpeakSettledChange(long now)
    {
        bool lit = Pmdg737NavDisplayCrossCheck.DisplaysLit(_current!, _layout!);
        if (lit && !_wasLit) _quietUntilMs = now + LightUpQuietMs;
        _wasLit = lit;
        if (!lit || (_quietUntilMs is { } quietUntil && now < quietUntil))
        {
            _spoken = _current;
            _changeAtMs = null;
            return;
        }
        _quietUntilMs = null;

        var changes = Pmdg737NavDisplaySpeech.Changes(_spoken!, _current!, _layout!);
        if (changes.Count == 0)
        {
            _spoken = _current;
            _changeAtMs = null;
            return;
        }
        _changeAtMs ??= now;
        if (now - _changeAtMs < SettleMs) return;
        _spoken = _current;
        _changeAtMs = null;
        foreach (var line in changes) Say(line);
    }

    /// <summary>The gate's two checks against what the app trusts: the EFIS object's units carry their own indexes, and
    /// the Captain's EFIS mode and range match the SDK within the window.</summary>
    private bool Passes(NavDisplaySdkSnapshot sdk, NavDisplaySample sample, long now) =>
        Pmdg737NavDisplayCrossCheck.Fingerprint(sample) && Pmdg737NavDisplayCrossCheck.Passes(sdk, _history, now);

    private void Remember(long now, NavDisplaySample sample)
    {
        _history.Enqueue((now, sample));
        while (now - _history.Peek().AtMs > Pmdg737NavDisplayCrossCheck.WindowMs) _history.Dequeue();
    }

    private void BeginSession(string reason)
    {
        ResetSession();
        _lastAttemptMs = null;
        Go(State.WaitingForSdk, reason);
    }

    /// <summary>A new session: its first good reading is a baseline, and nothing was said in it yet.</summary>
    private void ResetSession()
    {
        _everRunning = false;
        _stopReason = null;
        _slowRetry = false;
        _lostAtMs = null;
        _downSinceMs = null;
        _spoken = _current = null;
    }

    /// <summary>A search after the memory was lost while running that has not verified within 10 s says "stopped".</summary>
    private void SayIfLostTooLong(long now)
    {
        if (_lostAtMs is not { } lost || now - lost < LostStopMs) return;
        _lostAtMs = null;
        Log(Invariant($"the memory has not verified within {LostStopMs / 1000} s of being lost"));
        SayStopped(FmaReasons.MemoryNotFound);
    }

    /// <summary>The stop line, "stopped" once the reader has run in this session, else "unavailable".</summary>
    private string StopLine(string reason) =>
        _everRunning ? Pmdg737NavDisplaySpeech.Stopped(reason) : Pmdg737NavDisplaySpeech.Unavailable(reason);

    /// <summary>Speaks the stop line once per reason until the reader runs again (which then says "resumed").</summary>
    private void SayStopped(string reason)
    {
        if (_stopReason == reason) return;
        _stopReason = reason;
        _resumeOwed = true;
        Say(StopLine(reason));
    }

    /// <summary>Final until Restart or Start. Spoken once on entry: <paramref name="spoken"/> when a "stopped"
    /// line says it instead, which a reload then answers with "resumed".</summary>
    private void EnterUnavailable(string reason, string? spoken = null, string? logReason = null)
    {
        ReleaseReading();
        _reason = reason;
        if (spoken is not null) _resumeOwed = true;
        Go(State.Unavailable, logReason ?? reason);
        Say(spoken ?? Pmdg737NavDisplaySpeech.Unavailable(reason));
    }

    /// <summary>No layout file loaded (the module analysis has no builds): unavailable as <see cref="EnterUnavailable"/>,
    /// the reason logged and in the window and Ctrl+W, but the line is the four display readers' one
    /// <see cref="Pmdg737DisplaySpeech.FilesNotLoaded"/>, said once per module session by the first of them that is not
    /// muted (WASM-29).</summary>
    private void EnterNoLayoutFile(string reason)
    {
        ReleaseReading();
        _reason = reason;
        Go(State.Unavailable, reason);
        _env!.FilesNotice.Say(_env.IsMuted(), _env.Speak);
    }

    private void FailTask(string what, Task task) =>
        Fail(task.Exception ?? (Exception)new OperationCanceledException($"{what} was cancelled"));

    private void Fail(Exception ex)
    {
        try
        {
            Log($"internal error: {ex}");
            EnterUnavailable(FmaReasons.InternalError);
        }
        catch (Exception again)
        {
            Log($"internal error while stopping: {again}");
            _reason = FmaReasons.InternalError;
            _state = State.Unavailable;
        }
    }

    /// <summary>Speaks one line unless the "ND messages" row is muted; always the non-interrupting announce.</summary>
    private void Say(string line)
    {
        if (!_env!.IsMuted()) _env.Speak(line);
    }

    private void Go(State next, string reason)
    {
        var from = _state;
        _state = next;
        Log($"{from} -> {next}: {reason}");
    }

    private void Log(string line) => TryLog(_env, line);

    /// <summary>Disposes the sampler and every candidate, and abandons a memory search in flight.</summary>
    private void ReleaseReading()
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue());
        _history.Clear();
        _changeAtMs = null;

        var locate = _locate;
        _locate = null;
        if (locate is null) return;
        // Its samplers may arrive after this; nothing else will dispose them.
        var env = _env;
        locate.ContinueWith(t => DisposeAbandoned(t, env), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void AbandonAnalysis()
    {
        var analysis = _analysis;
        _analysis = null;
        if (analysis is null) return;
        var env = _env;
        analysis.ContinueWith(t =>
        {
            if (t.IsFaulted) TryLog(env, $"abandoned file analysis failed: {t.Exception}");
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void DisposeAbandoned(Task<NavDisplayLocateResult> task, INavDisplayEnvironment? env)
    {
        try
        {
            if (task.IsFaulted) TryLog(env, $"abandoned memory search failed: {task.Exception}");
            else if (task.IsCompletedSuccessfully)
                foreach (var c in task.Result.Candidates) DisposeQuietly(c, env);
        }
        catch (Exception ex)
        {
            TryLog(env, $"disposing an abandoned search's samplers failed: {ex}");
        }
    }

    private void DisposeQuietly(INavDisplaySampler? sampler) => DisposeQuietly(sampler, _env);

    private static void DisposeQuietly(INavDisplaySampler? sampler, INavDisplayEnvironment? env)
    {
        if (sampler is null) return;
        try
        {
            sampler.Dispose();
        }
        catch (Exception ex)
        {
            TryLog(env, $"disposing a sampler failed: {ex}");
        }
    }

    private static void TryLog(INavDisplayEnvironment? env, string line)
    {
        try
        {
            env?.Log(line);
        }
        catch
        {
            // The log itself failed: there is nowhere left to report it.
        }
    }
}
