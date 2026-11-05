using System.Globalization;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>What the Primary flight display window shows: its rows, and the reading they come from (null unless the
/// reader is running).</summary>
public sealed record PfdView(IReadOnlyList<string> Lines, PfdSample? Sample, Pmdg737PfdLayout? Layout);

/// <summary>
/// Reads the PMDG 737 Captain's primary flight display from PMDG's module memory and announces its messages by
/// themselves. The navigation display monitor's state machine, copied (not shared): ticked every 500 ms on the UI
/// thread, it waits for the simulator's airspeed and altitude, has the shared module session analyse PMDG's file and
/// locate its memory (through <see cref="IPfdEnvironment"/>, off the UI thread, polled and never continued), trusts a
/// candidate only once a reading passes the one gate (PMDG's IAS and altitude against the simulator's within 3 s, a
/// value behind its raised flag skipped; no corrupt reading from the sampler), then speaks each message change once it
/// has held for 500 ms. The first good reading after Start or Restart is a silent baseline; nothing is compared while
/// the displays are dark or the Captain's PFD is on no screen, nor for 10 s after the displays light up. Every way the
/// reader stops is spoken once, "resumed" is followed by whatever changed meanwhile, and the window (<see cref="View"/>)
/// shows "starting" or "stopped", never a stale reading; while it is open the sampler reads the full set. One Ctrl+M row
/// mutes all of it. Nothing interrupts. Every 5 s, and on every reading half a tolerance off or failing, it logs the
/// check's numbers (<see cref="CheckLine"/>), the instrument the tolerances are measured with. Errors never escape a
/// tick: they are logged and end in "unavailable", and Restart recovers.
/// </summary>
public sealed class Pmdg737PfdMonitor : IDisposable
{
    /// <summary>The Ctrl+M row "PFD messages": messages and the status lines.</summary>
    public const string MuteKey = "MSFSBA_PMDG737_PFD_MESSAGES";

    internal const int TickMs = 500;
    internal const long SettleMs = 500;
    internal const long LocateRetryMs = 5_000;
    internal const long LocateGiveUpMs = 120_000;
    internal const long SlowRetryMs = 30_000;
    internal const long LostStopMs = 10_000;
    internal const long MismatchToleranceMs = 2_000;
    internal const int CorruptTickLimit = 10;

    /// <summary>After the displays light up, what settles meanwhile is absorbed into the baseline silently: power-up
    /// is never spoken as news.</summary>
    internal const long LightUpQuietMs = 10_000;

    /// <summary>How often the check's numbers are logged while nothing is off.</summary>
    internal const long CheckLogMs = 5_000;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private enum State { Stopped, WaitingForSimulator, Analysing, Locating, Verifying, Running, Mismatched, Unavailable }

    private IPfdEnvironment? _env;
    private System.Windows.Forms.Timer? _timer;
    private State _state = State.Stopped;
    private bool _inTick;

    // The file analysis and the memory search: started here, run off the UI thread, polled each tick.
    private IReadOnlyList<Pmdg737PfdLayout>? _layouts;
    private Task<PfdAnalysis>? _analysis;
    private Task<PfdLocateResult>? _locate;
    private long _locateStartMs;
    private long? _lastAttemptMs;
    private int _attempts;
    private bool _slowRetry; // the 120 s window ran out and was spoken: search every 30 s until found
    private long? _lostAtMs; // when the memory was lost while running, until it verifies again (the 10 s stop)

    // The candidate being read and those not tried yet. The monitor owns them all and disposes each once.
    private IPfdSampler? _sampler;
    private readonly Queue<IPfdSampler> _candidates = new();
    private long _candidateStartMs;
    private readonly Queue<(long AtMs, PfdSample Sample)> _history = new();
    private bool _windowOpen;
    private bool _keepFull;

    // Running.
    private Pmdg737PfdLayout? _layout;
    private PfdSample? _spoken;  // what the pilot last heard (or would have, unmuted)
    private PfdSample? _current; // the latest good reading, for the window
    private long? _changeAtMs;
    private bool _wasLit;         // the displays were lit at the last good reading
    private long? _quietUntilMs;  // the displays lit up: changes until then become the baseline silently
    private long _lastPassMs;
    private long _lastCheckMs;
    private int _corruptTicks;
    private bool _everRunning;
    private string? _stopReason; // the reason in the stop line the pilot heard (or would have, unmuted), until it runs again
    // A stop or unavailable line was said (or would have been, unmuted) and no "resumed" since; survives Restart, so a
    // stop heard before a flight reload is answered.
    private bool _resumeOwed;
    private string _reason = FmaReasons.InternalError; // why unavailable, or the search window's last failure
    // Since when, and why, the reader that ran has shown nothing (lost memory or a mismatch): the window says "stopped"
    // meanwhile, and Output Alt+P falls back to the AI read once it lasts LostStopMs.
    private long? _downSinceMs;
    private string _downReason = FmaReasons.MemoryNotFound;

    internal string StateName => _state.ToString();

    /// <summary>The latest good reading while running (for the window's buttons and the speed reference setter), else
    /// null: never a stale one.</summary>
    public PfdSample? Latest => _state == State.Running ? _current : null;

    /// <summary>While the window is open the sampler reads every value the rows need, not only the messages and the
    /// check's.</summary>
    public bool WindowOpen
    {
        get => _windowOpen;
        set
        {
            _windowOpen = value;
            if (_sampler is not null) _sampler.Full = Full;
        }
    }

    /// <summary>The full set also while the window is closed, for as long as a setter reads its values (the Captain's
    /// minimums from the Ctrl+P panel).</summary>
    public bool KeepFull
    {
        get => _keepFull;
        set
        {
            _keepFull = value;
            if (_sampler is not null) _sampler.Full = Full;
        }
    }

    private bool Full => _windowOpen || _keepFull;

    /// <summary>Starts reading through <paramref name="env"/>. On a monitor that is already started it swaps in the
    /// environment and restarts; the layouts are kept only for the same environment.</summary>
    public void Start(IPfdEnvironment env, bool startTimer = true)
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

    /// <summary>Flight reload: drops the memory and searches again, keeping the derived layouts. The window says
    /// "starting" until the new flight's first good reading, its silent baseline.</summary>
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
    /// The window's rows: running, those of the latest good reading (the FMA rows from <paramref name="fmaColumns"/>,
    /// or <paramref name="fmaReadout"/> while the FMA reader is not running); unavailable, why; stopped (a mismatch,
    /// lost memory, or a search window that ran out), that it is trying again; otherwise "starting". Never a reading
    /// the gate has not passed, never a stale one.
    /// </summary>
    public PfdView View(IReadOnlyList<string>? fmaColumns, string fmaReadout)
    {
        if (_state == State.Running && _current is { } s && _layout is { } l)
            return new PfdView(Pmdg737PfdText.Rows(s, l, fmaColumns, fmaReadout), s, l);
        string line = _state switch
        {
            State.Unavailable => Pmdg737PfdSpeech.ListUnavailable(_reason),
            State.Mismatched => Pmdg737PfdSpeech.ListStopped(FmaReasons.SimulatorMismatch),
            State.Stopped or State.WaitingForSimulator => Pmdg737PfdSpeech.Starting,
            _ when _downSinceMs is not null => Pmdg737PfdSpeech.ListStopped(_downReason),
            _ when _slowRetry => Pmdg737PfdSpeech.ListStopped(_reason),
            _ => Pmdg737PfdSpeech.Starting,
        };
        return new PfdView(new[] { line }, null, null);
    }

    /// <summary>
    /// Whether Output Alt+P goes straight to the AI read instead of opening the window: the classic EFIS look on the
    /// Captain's screen in the latest good reading, then <paramref name="sayFirst"/> is <see cref="Pmdg737PfdSpeech.KeyClassic"/>,
    /// said before the AI read; the reader unavailable, its search window run out, or stopped for <see cref="LostStopMs"/>
    /// or longer, saying nothing first (WASM-29). False while it starts, runs, or is stopped for less (the window opens and
    /// says so).
    /// </summary>
    public bool KeyFallsBackToAi(out string? sayFirst)
    {
        sayFirst = null;
        if (_state == State.Unavailable) return true;
        if (_env is null || _state == State.WaitingForSimulator) return false;
        if (_state == State.Running && _current is { } s && _layout is { } l && Pmdg737PfdCrossCheck.CaptainPfd(s, l.Units) == PfdShown.Classic)
        {
            sayFirst = Pmdg737PfdSpeech.KeyClassic;
            return true;
        }
        if (_downSinceMs is { } down && _env.NowMs - down >= LostStopMs) return true;
        return _slowRetry;
    }

    /// <summary>The check's numbers for one reading: PMDG's and the simulator's IAS and altitude, their differences,
    /// and whether each tape's flag is up. Invariant numbers.</summary>
    internal static string CheckLine(PfdSample s, PfdSimSnapshot sim, Pmdg737PfdLayout l) =>
        "check: ias " + Pair(s.Ias, sim.IasKnots) + ", alt " + Pair(s.Altitude, sim.AltitudeFt)
        + ", spd flag " + YesNo(Pmdg737PfdCrossCheck.SpeedFlagged(s, l)) + ", alt flag " + YesNo(Pmdg737PfdCrossCheck.AltitudeFlagged(s, l));

    private static string Pair(double pmdg, double? sim) =>
        "pmdg " + pmdg.ToString("0.0", Inv) + " sim " + (sim is { } v ? v.ToString("0.0", Inv) : "none")
        + " diff " + (sim is { } w ? (pmdg - w).ToString("0.0", Inv) : "none");

    private static string YesNo(bool b) => b ? "yes" : "no";

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

    private void Step(IPfdEnvironment env)
    {
        if (env.ReadSimulator() is not { IasKnots: not null, AltitudeFt: not null } sim)
        {
            // Disconnected, or a value not delivered yet: drop the memory, keep the layouts (and an analysis in flight).
            if (_state != State.WaitingForSimulator)
            {
                ReleaseReading();
                _lostAtMs = null; // waiting for the simulator is silent
                _downSinceMs = null;
                Go(State.WaitingForSimulator, Pmdg737PfdSpeech.WaitingForSimulator);
            }
            return;
        }
        SayIfLostTooLong(env.NowMs);
        switch (_state)
        {
            case State.WaitingForSimulator: OnWaitingForSimulator(env); break;
            case State.Analysing: OnAnalysing(); break;
            case State.Locating: OnLocating(env); break;
            case State.Verifying: OnVerifying(env, sim); break;
            case State.Running: OnRunning(env, sim); break;
            case State.Mismatched: OnMismatched(env, sim); break;
        }
    }

    private void OnWaitingForSimulator(IPfdEnvironment env)
    {
        if (_layouts is not null)
        {
            BeginLocating(newWindow: true, "simulator data ready");
            return;
        }
        _analysis ??= env.AnalyseAsync();
        Go(State.Analysing, "simulator data ready; analysing PMDG's file");
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

    /// <summary><paramref name="newWindow"/> starts a new 120 s search window; attempts are always at least 5 s apart
    /// (only Start and Restart reset that).</summary>
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

    private void OnLocating(IPfdEnvironment env)
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
        _sampler.Full = Full;
        _candidateStartMs = _env!.NowMs;
        _history.Clear();
        Go(State.Verifying, reason);
    }

    private void OnVerifying(IPfdEnvironment env, PfdSimSnapshot sim)
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
            if (Pmdg737PfdCrossCheck.Passes(sim, _history, now, _sampler.Layout))
            {
                while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue());
                EnterRunning(sample, sim, "the memory agrees with the simulator");
                return;
            }
        }
        if (now - _candidateStartMs >= MismatchToleranceMs) DropCandidate(readFailed: false);
    }

    /// <summary>The candidate failed: the next one is tried, else a new search in the same window, which remembers why
    /// (a failed read is "memory not found", a disagreement "values do not match the simulator").</summary>
    private void DropCandidate(bool readFailed)
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        _reason = readFailed ? FmaReasons.MemoryNotFound : FmaReasons.SimulatorMismatch;
        string why = readFailed ? "the memory read failed" : "no good reading (matching the simulator, no unexpected value) for 2 s";
        if (_candidates.Count > 0)
            StartNextCandidate($"{why}; trying the next candidate");
        else
            BeginLocating(newWindow: false, $"{why}; searching again");
    }

    private void EnterRunning(PfdSample sample, PfdSimSnapshot sim, string reason)
    {
        _layout = _sampler!.Layout;
        // The first good reading after Start or Restart is the baseline; later in the session, what changed while the
        // reader was not running is told after "resumed", never swallowed.
        var missed = _everRunning && _spoken is { } before
            ? Pmdg737PfdSpeech.Changes(before, sample, _layout)
            : Array.Empty<string>();
        bool heardStop = _resumeOwed;
        _spoken = _current = sample;
        _changeAtMs = null;
        _wasLit = Pmdg737PfdCrossCheck.DisplaysLit(sample, _layout);
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
        LogCheck(_lastPassMs, sample, sim);
        if (heardStop) Say(Pmdg737PfdSpeech.Resumed);
        foreach (var line in missed) Say(line);
    }

    private void OnRunning(IPfdEnvironment env, PfdSimSnapshot sim)
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
                EnterUnavailable(why, Pmdg737PfdSpeech.Stopped(why), Invariant($"{why} for {CorruptTickLimit} readings: {corrupt}"));
            }
            return;
        }
        _corruptTicks = 0;

        // Only a good reading is shown or spoken; any other only counts toward the mismatch stop. Each value is compared
        // only while PMDG draws it: with the displays dark or the Captain's PFD on no screen the reading counts (the
        // window says where the PFD is), logged every 5 s only.
        bool drawn = Pmdg737PfdSpeech.Visible(sample!, _layout!);
        bool passes = !drawn || Pmdg737PfdCrossCheck.Passes(sim, _history, now, _layout!);
        if ((drawn && (!passes || Off(sample!, sim))) || now - _lastCheckMs >= CheckLogMs) LogCheck(now, sample!, sim);
        if (passes)
        {
            _lastPassMs = now;
        }
        else
        {
            if (now - _lastPassMs >= MismatchToleranceMs)
            {
                _changeAtMs = null;
                _downSinceMs ??= now;
                _downReason = FmaReasons.SimulatorMismatch;
                Go(State.Mismatched, "the memory has not matched the simulator for 2 s");
                SayStopped(FmaReasons.SimulatorMismatch);
            }
            return; // an open settle window keeps its start; the next good reading speaks
        }
        _current = sample;
        SpeakSettledChange(now);
    }

    private void OnMismatched(IPfdEnvironment env, PfdSimSnapshot sim)
    {
        long now = env.NowMs;
        if (!TryReadRunning(now, out var sample, out var corrupt) || corrupt is not null) return;
        if (Pmdg737PfdCrossCheck.Passes(sim, _history, now, _layout!)) EnterRunning(sample!, sim, "the memory agrees with the simulator again");
        else if (now - _lastCheckMs >= CheckLogMs) LogCheck(now, sample!, sim);
    }

    /// <summary>A reading at least half a tolerance off the simulator in a value whose flag is down (both at or below
    /// the speed tape's floor counts as on).</summary>
    private bool Off(PfdSample s, PfdSimSnapshot sim) =>
        (!Pmdg737PfdCrossCheck.SpeedFlagged(s, _layout!)
            && !Pmdg737PfdCrossCheck.IasWithin(s.Ias, sim.IasKnots!.Value, Pmdg737PfdCrossCheck.IasToleranceKt / 2, _layout!))
        || (!Pmdg737PfdCrossCheck.AltitudeFlagged(s, _layout!)
            && !Pmdg737PfdCrossCheck.AltitudeWithin(s.Altitude, sim.AltitudeFt!.Value, Pmdg737PfdCrossCheck.AltitudeToleranceFt / 2, _layout!));

    private void LogCheck(long now, PfdSample s, PfdSimSnapshot sim)
    {
        _lastCheckMs = now;
        Log(CheckLine(s, sim, _layout!));
    }

    /// <summary>A failed read means the memory moved or went away: search again, silently at first. From Running, a
    /// search that has not verified within 10 s says "stopped"; from Mismatched it was said. A corrupt reading is a read
    /// (the memory is there): it comes back with its reason and no sample.</summary>
    private bool TryReadRunning(long now, out PfdSample? sample, out string? corrupt)
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
    /// Speaks what changed once it has held for 500 ms. A change and back inside the window says nothing; the baseline
    /// advances even when muted, and with nothing to say: while the displays are dark or the Captain's PFD is on no
    /// screen, and for <see cref="LightUpQuietMs"/> after the displays light up, so power-up is silent.
    /// </summary>
    private void SpeakSettledChange(long now)
    {
        bool lit = Pmdg737PfdCrossCheck.DisplaysLit(_current!, _layout!);
        if (lit && !_wasLit) _quietUntilMs = now + LightUpQuietMs;
        _wasLit = lit;
        if (!Pmdg737PfdSpeech.Visible(_current!, _layout!) || (_quietUntilMs is { } quietUntil && now < quietUntil))
        {
            _spoken = _current;
            _changeAtMs = null;
            return;
        }
        _quietUntilMs = null;

        var changes = Pmdg737PfdSpeech.Changes(_spoken!, _current!, _layout!);
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

    private void Remember(long now, PfdSample sample)
    {
        _history.Enqueue((now, sample));
        while (now - _history.Peek().AtMs > Pmdg737PfdCrossCheck.WindowMs) _history.Dequeue();
    }

    private void BeginSession(string reason)
    {
        ResetSession();
        _lastAttemptMs = null;
        Go(State.WaitingForSimulator, reason);
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
        _everRunning ? Pmdg737PfdSpeech.Stopped(reason) : Pmdg737PfdSpeech.Unavailable(reason);

    /// <summary>Speaks the stop line once per reason until the reader runs again (which then says "resumed").</summary>
    private void SayStopped(string reason)
    {
        if (_stopReason == reason) return;
        _stopReason = reason;
        _resumeOwed = true;
        Say(StopLine(reason));
    }

    /// <summary>Final until Restart or Start. Spoken once on entry: <paramref name="spoken"/> when a "stopped" line
    /// says it instead, which a reload then answers with "resumed".</summary>
    private void EnterUnavailable(string reason, string? spoken = null, string? logReason = null)
    {
        ReleaseReading();
        _reason = reason;
        if (spoken is not null) _resumeOwed = true;
        Go(State.Unavailable, logReason ?? reason);
        Say(spoken ?? Pmdg737PfdSpeech.Unavailable(reason));
    }

    /// <summary>No layout file loaded (the module analysis has no builds): unavailable as <see cref="EnterUnavailable"/>,
    /// the reason logged and in the window, but the line is the four display readers' one
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

    /// <summary>Speaks one line unless the "PFD messages" row is muted; always the non-interrupting announce.</summary>
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

    private static void DisposeAbandoned(Task<PfdLocateResult> task, IPfdEnvironment? env)
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

    private void DisposeQuietly(IPfdSampler? sampler) => DisposeQuietly(sampler, _env);

    private static void DisposeQuietly(IPfdSampler? sampler, IPfdEnvironment? env)
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

    private static void TryLog(IPfdEnvironment? env, string line)
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
