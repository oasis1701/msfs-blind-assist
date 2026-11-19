using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Announces the PMDG 737's flight mode annunciator, read read-only from PMDG's module memory. A state
/// machine ticked every 100 ms on the UI thread: it waits for the PMDG SDK, analyses PMDG's file and
/// locates its memory (both through <see cref="IFmaEnvironment"/>, off the UI thread, polled with
/// <see cref="Task.IsCompleted"/> and never continued), trusts a candidate only once it agrees with the
/// SDK (<see cref="Pmdg737FmaCrossCheck"/>), then speaks each FMA change once it has settled for 300 ms.
/// A wrong mode is worse than silence, and silence must never pass for "no change": every way the reader
/// stops is spoken once, "resumed" carries whatever changed meanwhile, and <see cref="DescribeForReadout"/>
/// (the PFD window's FMA row) always says why it cannot read. Memory not found and values that do not match are
/// never final: after the 120 s search window the reader keeps searching every 30 s. The mute
/// (<see cref="IFmaEnvironment.IsMuted"/>) silences all of the monitor's own speech, never the window's row.
/// Errors never escape a tick: they are logged and end in "unavailable", and Restart recovers.
/// </summary>
public sealed class Pmdg737FmaMonitor : IDisposable
{
    /// <summary>The Ctrl+M row that mutes the FMA announcements.</summary>
    public const string MuteKey = "MSFSBA_PMDG737_FMA";

    internal const int TickMs = 100;
    internal const long SettleMs = 300;
    internal const long LocateRetryMs = 5_000;
    internal const long LocateGiveUpMs = 120_000;
    internal const long SlowRetryMs = 30_000;
    internal const long LostStopMs = 10_000;
    internal const long MismatchToleranceMs = 2_000;
    internal const int CorruptTickLimit = 10;

    private enum State { Stopped, WaitingForSdk, Analysing, Locating, Verifying, Running, Mismatched, Unavailable }

    private IFmaEnvironment? _env;
    private System.Windows.Forms.Timer? _timer;
    private State _state = State.Stopped;
    private bool _inTick;

    // The file analysis and the memory search: started here, run off the UI thread, polled each tick.
    private IReadOnlyList<Pmdg737FmaLayout>? _layouts;
    private Task<FmaAnalysis>? _analysis;
    private Task<FmaLocateResult>? _locate;
    private long _locateStartMs;
    private long? _lastAttemptMs;
    private int _attempts;
    private bool _slowRetry; // the 120 s window ran out and was spoken: search every 30 s until found
    private long? _lostAtMs; // when the memory was lost while running, until it verifies again (the 10 s stop)

    // The candidate being read and those not tried yet. The monitor owns them all and disposes each once.
    private IFmaSampler? _sampler;
    private readonly Queue<IFmaSampler> _candidates = new();
    private long _candidateStartMs;
    private readonly Queue<(long AtMs, FmaSample Sample)> _history = new();

    // Running.
    private Pmdg737FmaLayout? _layout;
    private FmaState _spoken;  // what the pilot last heard (or would have, unmuted)
    private FmaState _current; // the latest good reading, for the readout (still the last good one while stopped)
    private long? _changeAtMs;
    private long _lastPassMs;
    private int _corruptTicks;
    private bool _everRunning;
    private string? _stopReason; // the reason in the stop line the pilot heard (or would have, unmuted), until it runs again
    // A stop or unavailable line was said (or would have been, unmuted) and no "resumed" since. Unlike
    // _stopReason it survives Restart: after a flight reload the new flight's first reading is a silent
    // baseline, and without this the "stopped" the pilot heard was never answered. Start with another
    // environment, and Stop, forget it.
    private bool _resumeOwed;
    private string _reason = FmaReasons.InternalError; // why unavailable, or the search window's last failure
    private readonly HashSet<(string Column, int Value)> _loggedNoWord = new();

    internal string StateName => _state.ToString();

    /// <summary>Starts reading through <paramref name="env"/>. On a monitor that is already started it
    /// swaps in the environment and restarts; the layouts are kept only for the same environment.</summary>
    public void Start(IFmaEnvironment env, bool startTimer = true)
    {
        ArgumentNullException.ThrowIfNull(env);
        bool wasStopped = _state == State.Stopped;
        ReleaseReading();
        if (!ReferenceEquals(env, _env))
        {
            // Another environment may point at another sim: its files are analysed afresh, and a stop
            // said for the old one is not answered on the new one.
            AbandonAnalysis();
            _layouts = null;
            _loggedNoWord.Clear();
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

    /// <summary>Flight reload: drops the memory and searches again, keeping the derived layouts. A stop the
    /// pilot heard is kept, so the new flight's first reading says "resumed" (alone: it is a baseline).</summary>
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

    /// <summary>The whole FMA, or why it cannot be read (the PFD window's row while the columns are not read;
    /// Output P read it until 2026-10-30). Never muted. Once a stop line has been
    /// said, it repeats it until the reader runs again, never "starting" after the pilot heard
    /// "stopped"; after a mismatch it adds the last good reading, which may be stale, so only as "last
    /// known", never as the current FMA.</summary>
    public string DescribeForReadout() => _state switch
    {
        State.Running => Pmdg737FmaSpeech.DescribeAll(_current, _layout!),
        State.Unavailable => Pmdg737FmaSpeech.Unavailable(_reason),
        State.Mismatched => HeardStopLine(FmaReasons.Mismatch),
        _ when _stopReason is { } heard => HeardStopLine(heard),
        State.WaitingForSdk => Pmdg737FmaSpeech.WaitingForSdk,
        _ => Pmdg737FmaSpeech.Starting,
    };

    /// <summary>The PFD window's FMA rows: the four columns of the latest good reading while running, else null (the
    /// window then shows <see cref="DescribeForReadout"/>).</summary>
    public IReadOnlyList<string>? ColumnTexts() => _state == State.Running ? Pmdg737FmaSpeech.Columns(_current, _layout!) : null;

    /// <summary>The stop line the pilot heard, for the readout: after a mismatch, with the last good FMA.</summary>
    private string HeardStopLine(string reason) =>
        _everRunning && reason == FmaReasons.Mismatch
            ? Pmdg737FmaSpeech.Stopped(reason) + ". Last known: " + Pmdg737FmaSpeech.DescribeAll(_current, _layout!)
            : StopLine(reason);

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

    private void Step(IFmaEnvironment env)
    {
        var sdk = env.ReadSdk();
        if (sdk is null)
        {
            // The aircraft may be gone: drop its memory, keep the layouts (and an analysis in flight).
            if (_state != State.WaitingForSdk)
            {
                ReleaseReading();
                _lostAtMs = null; // waiting for the SDK is silent
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

    private void OnWaitingForSdk(IFmaEnvironment env)
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

    /// <summary>
    /// <paramref name="newWindow"/> starts a new 120 s search window; false keeps the current one. Attempts
    /// are always at least 5 s apart (only Start and Restart reset that), so a reading that keeps failing
    /// cannot turn into a busy search of the sim's memory.
    /// </summary>
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

    private void OnLocating(IFmaEnvironment env)
    {
        long now = env.NowMs;
        if (_locate is null)
        {
            if (!_slowRetry && now - _locateStartMs >= LocateGiveUpMs)
            {
                // Not final: a long load, or a reload whose SDK data lags, can outlast the window. Say so
                // once, then keep searching, slowly and silently, until found, Stop or Restart.
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
        // Not found yet: the next attempt starts 5 s (30 s once the window ran out) after this one did.
        _reason = FmaReasons.MemoryNotFound;
    }

    private void StartNextCandidate(string reason)
    {
        _sampler = _candidates.Dequeue();
        _candidateStartMs = _env!.NowMs;
        _history.Clear();
        Go(State.Verifying, reason);
    }

    private void OnVerifying(IFmaEnvironment env, FmaSdkSnapshot sdk)
    {
        long now = env.NowMs;
        if (!_sampler!.TryRead(out var sample))
        {
            DropCandidate(readFailed: true);
            return;
        }
        Remember(now, sample);
        if (IsGood(sdk, sample, now, _sampler.Layout))
        {
            while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue());
            EnterRunning(sample, "the memory agrees with the PMDG SDK");
            return;
        }
        if (now - _candidateStartMs >= MismatchToleranceMs) DropCandidate(readFailed: false);
    }

    /// <summary>
    /// The candidate failed. A wrong layout usually fails the read itself (its pointer lies outside the
    /// module), so the next candidate is tried before searching again: a new search would return the same
    /// ones. With none left, it searches again in the same window, which remembers why for the line spoken
    /// when the window runs out: a failed read is "memory not found", a disagreement "values do not match".
    /// </summary>
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

    private void EnterRunning(FmaSample sample, string reason)
    {
        _layout = _sampler!.Layout;
        // The first trusted reading after Start or Restart is the baseline: never spoken as a change (Output
        // P gives it), only "resumed" when a stop heard before a reload is unanswered. Later in the session,
        // what changed while the reader was not running is told, never swallowed.
        string? missed = _everRunning ? Pmdg737FmaSpeech.DescribeChange(_spoken, sample.Fma, _layout) : null;
        bool heardStop = _resumeOwed; // in this session, or before a reload
        _spoken = _current = sample.Fma;
        _changeAtMs = null;
        _lastPassMs = _env!.NowMs;
        _corruptTicks = 0;
        _everRunning = true;
        _stopReason = null;
        _resumeOwed = false;
        _slowRetry = false;
        _lostAtMs = null;
        Go(State.Running, reason);
        if (heardStop)
            // The pilot heard "stopped" or "unavailable": tell them it reads again, however it got here.
            // "resumed" already says FMA, so its change list takes no "FMA: " of its own.
            Say(missed is null ? Pmdg737FmaSpeech.Resumed : Pmdg737FmaSpeech.Resumed + ", " + missed);
        else if (missed is not null)
            Say(Pmdg737FmaSpeech.ChangeAnnouncement(missed)); // after a quick, silent search
    }

    private void OnRunning(IFmaEnvironment env, FmaSdkSnapshot sdk)
    {
        long now = env.NowMs;
        if (!TryReadRunning(now, out var sample)) return;

        // Only a good reading (it agrees with the SDK and no value is corrupt) is shown or spoken; any
        // other only counts toward the two stop limits.
        bool agrees = Passes(sdk, sample, now);
        if (agrees)
        {
            _lastPassMs = now;
        }
        else if (now - _lastPassMs >= MismatchToleranceMs)
        {
            _changeAtMs = null;
            Go(State.Mismatched, "the memory has not matched the PMDG SDK for 2 s");
            SayStopped(FmaReasons.Mismatch);
            return;
        }

        var corrupt = ScanValues(sample.Fma, _layout!, logNoWord: agrees);
        if (corrupt is not null)
        {
            // Ten in a row stop the reader.
            if (_corruptTicks++ == 0) Log($"unexpected value: {corrupt}");
            if (_corruptTicks >= CorruptTickLimit)
            {
                var reason = FmaReasons.UnexpectedValues;
                EnterUnavailable(reason, Pmdg737FmaSpeech.Stopped(reason),
                    Invariant($"{reason} for {CorruptTickLimit} readings: {corrupt}"));
            }
            return;
        }
        _corruptTicks = 0;
        if (!agrees) return; // an open settle window keeps its start; the next good reading speaks
        _current = sample.Fma;
        SpeakSettledChange(now);
    }

    private void OnMismatched(IFmaEnvironment env, FmaSdkSnapshot sdk)
    {
        long now = env.NowMs;
        if (!TryReadRunning(now, out var sample)) return;
        // The first good reading resumes, and "resumed" carries what changed while stopped.
        if (IsGood(sdk, sample, now, _layout!)) EnterRunning(sample, "the memory agrees with the PMDG SDK again");
    }

    /// <summary>A failed read means the memory moved or went away: search again, silently at first. From
    /// Running, a search that has not verified within 10 s says "stopped"; from Mismatched it was said.</summary>
    private bool TryReadRunning(long now, out FmaSample sample)
    {
        if (!_sampler!.TryRead(out sample))
        {
            if (_state == State.Running) _lostAtMs = now;
            BeginLocating(newWindow: true, "the memory read failed; searching again");
            return false;
        }
        Remember(now, sample);
        return true;
    }

    private void SpeakSettledChange(long now)
    {
        if (_current == _spoken)
        {
            _changeAtMs = null; // changed and changed back inside the settle: nothing to say
            return;
        }
        _changeAtMs ??= now;
        if (now - _changeAtMs < SettleMs) return;
        var text = Pmdg737FmaSpeech.DescribeChange(_spoken, _current, _layout!);
        _spoken = _current; // the baseline advances even when muted or nothing speakable changed
        _changeAtMs = null;
        if (text is not null) Say(Pmdg737FmaSpeech.ChangeAnnouncement(text));
    }

    /// <summary>The corrupt values, or null; with <paramref name="logNoWord"/>, logs each value with no word
    /// once per (column, value) (only for a reading that agrees with the SDK, through the trusted layout).</summary>
    private string? ScanValues(FmaState fma, Pmdg737FmaLayout l, bool logNoWord)
    {
        List<string>? corrupt = null;
        foreach (var (label, words, value) in new[]
        {
            ("A/T", l.AtWords, fma.At),
            ("Roll", l.RollWords, fma.Roll),
            ("Roll armed", l.RollWords, fma.RollArmed),
            ("Pitch", l.PitchWords, fma.Pitch),
            ("Pitch armed", l.PitchWords, fma.PitchArmed),
            ("AFDS", l.StatusWords, fma.Status),
        })
        {
            switch (Pmdg737FmaSpeech.Lookup(words, value).Kind)
            {
                case FmaWordKind.Corrupt:
                    (corrupt ??= new List<string>()).Add(Invariant($"{label} = {value}"));
                    break;
                case FmaWordKind.NoWord when logNoWord && _loggedNoWord.Add((label, value)):
                    Log(Invariant($"value with no word, spoken as unrecognised: {label} = {value}"));
                    break;
            }
        }
        return corrupt is null ? null : string.Join(", ", corrupt);
    }

    /// <summary>The cross-check: the display pointer is the autopilot pointer, and the SDK agrees with memory.</summary>
    private bool Passes(FmaSdkSnapshot sdk, FmaSample sample, long now) =>
        sample.PointersAgree && Pmdg737FmaCrossCheck.Passes(sdk, _history, now);

    /// <summary>The one integrity gate: only a good reading may become the baseline, be shown in the PFD window or
    /// be spoken. Good = it passes the cross-check and no value is corrupt.</summary>
    private bool IsGood(FmaSdkSnapshot sdk, FmaSample sample, long now, Pmdg737FmaLayout layout) =>
        Passes(sdk, sample, now) && ScanValues(sample.Fma, layout, logNoWord: false) is null;

    private void Remember(long now, FmaSample sample)
    {
        _history.Enqueue((now, sample));
        while (now - _history.Peek().AtMs > Pmdg737FmaCrossCheck.WindowMs) _history.Dequeue();
    }

    private void BeginSession(string reason)
    {
        ResetSession();
        _lastAttemptMs = null;
        Go(State.WaitingForSdk, reason);
    }

    /// <summary>A new session: its first good reading is a baseline, never a change list, and nothing was
    /// said in it yet (a stop heard before a reload is <see cref="_resumeOwed"/>'s, kept apart).</summary>
    private void ResetSession()
    {
        _everRunning = false;
        _stopReason = null;
        _slowRetry = false;
        _lostAtMs = null;
    }

    /// <summary>A search after the memory was lost while running that has not verified within 10 s says
    /// "stopped", so a long search never passes for "no change"; recovery says "resumed" with what changed.</summary>
    private void SayIfLostTooLong(long now)
    {
        if (_lostAtMs is not { } lost || now - lost < LostStopMs) return;
        _lostAtMs = null;
        Log(Invariant($"the memory has not verified within {LostStopMs / 1000} s of being lost"));
        SayStopped(FmaReasons.MemoryNotFound);
    }

    /// <summary>The stop line, "stopped" once the reader has run in this session, else "unavailable".</summary>
    private string StopLine(string reason) =>
        _everRunning ? Pmdg737FmaSpeech.Stopped(reason) : Pmdg737FmaSpeech.Unavailable(reason);

    /// <summary>Speaks the stop line once per reason until the reader runs again (which then says
    /// "resumed"), so the same line is never repeated while a new reason is still told.</summary>
    private void SayStopped(string reason)
    {
        if (_stopReason == reason) return;
        _stopReason = reason;
        _resumeOwed = true;
        Say(StopLine(reason));
    }

    /// <summary>Final until Restart or Start. Spoken once on entry: <paramref name="spoken"/> when a
    /// "stopped" line says it instead, which a reload then answers with "resumed".</summary>
    private void EnterUnavailable(string reason, string? spoken = null, string? logReason = null)
    {
        ReleaseReading();
        _reason = reason;
        if (spoken is not null) _resumeOwed = true;
        Go(State.Unavailable, logReason ?? reason);
        Say(spoken ?? Pmdg737FmaSpeech.Unavailable(reason));
    }

    /// <summary>No layout file loaded (the module analysis has no builds): unavailable as <see cref="EnterUnavailable"/>,
    /// the reason logged and in the readout, but the line is the four display readers' one
    /// <see cref="Pmdg737DisplaySpeech.FilesNotLoaded"/>, said once per module session by the first of them that is not
    /// muted (WASM-29).</summary>
    private void EnterNoLayoutFile(string reason)
    {
        ReleaseReading();
        _reason = reason;
        Go(State.Unavailable, reason);
        _env!.FilesNotice.Say(_env.IsMuted, _env.Speak);
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

    private void Say(string text)
    {
        if (!_env!.IsMuted) _env.Speak(text);
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

    private static void DisposeAbandoned(Task<FmaLocateResult> task, IFmaEnvironment? env)
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

    private void DisposeQuietly(IFmaSampler? sampler) => DisposeQuietly(sampler, _env);

    private static void DisposeQuietly(IFmaSampler? sampler, IFmaEnvironment? env)
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

    private static void TryLog(IFmaEnvironment? env, string line)
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
