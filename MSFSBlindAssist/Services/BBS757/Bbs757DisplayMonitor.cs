using MSFSBlindAssist.Services.WasmMemory;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>A 757 display monitor without its reading type, so one registry holds every reader's.</summary>
public interface IBbs757DisplayMonitor : IDisposable
{
    /// <summary>Starts reading through <paramref name="env"/> on the monitor's own timer.</summary>
    void Start(IBbs757DisplayEnvironment env);

    /// <summary>Flight reload: drops the memory and searches again; the new flight's first good reading is a silent
    /// baseline.</summary>
    void Restart();

    /// <summary>Aircraft switch or exit: stops the timer and releases everything.</summary>
    void Stop();

    /// <summary>Whether the reader's display key goes to the AI read instead of the reader's window (S15).</summary>
    bool KeyFallsBackToAi();

    /// <summary>The display window's rows: the reader's while running, else one status row.</summary>
    IReadOnlyList<string> View();

    /// <summary>The reader's simulator inputs (<see cref="IBbs757DisplayReader{TLayout, TReading}.SimInputs"/>), which
    /// the registry records so the definition puts each on SimConnect's batch: never listed apart from the reader.</summary>
    IReadOnlyList<Bbs757SimInput> SimInputs { get; }
}

/// <summary>
/// Reads one BlueBird 757 display from BBS752.wasm's memory and speaks its changes: the PMDG 737 FMA monitor's state
/// machine and timings (S1), made generic over an <see cref="IBbs757DisplayReader{TLayout, TReading}"/>. Ticked every
/// <see cref="IBbs757DisplayReader{TLayout, TReading}.TickMs"/> on the UI thread, it waits while the simulator is not up
/// (disconnected, no flight, another aircraft), has the shared module session analyse the installed files and locate the
/// module's memory (through <see cref="IBbs757DisplayEnvironment"/>, off the UI thread, polled with
/// <see cref="Task.IsCompleted"/> and never continued), and runs on a candidate's first reading that is not corrupt.
/// A reading is trusted on structure alone (B757-30): the layout the generator derived for this exact BBS752.wasm, the
/// sampler finding its objects by vptr with every read inside the module, and the reading not
/// <see cref="IBbs757Reading.Corrupt"/>. No simulator value ever judges it; a reader's
/// <see cref="IBbs757DisplayReader{TLayout, TReading}.SimInputs"/> only feed its sampler (S10). Each change is spoken once
/// it has settled for 300 ms. No change is spoken while the display is dark or for 10 s after it powers up (status lines
/// are: "resumed" as soon as a dark candidate runs after a heard stop, "stopped" when memory is lost), and the first
/// good reading after Start or Restart, after any wait for the simulator, and after the display was dark or powering up,
/// is a silent baseline (S4). Silence must never pass for "no change": every way the reader stops is spoken once (the
/// memory lost for 10 s, ten corrupt readings, the search window running out, a final reason), "resumed" carries whatever
/// changed meanwhile, and <see cref="View"/> repeats the stop line the pilot heard. Memory not found is never final: after
/// the 120 s search window the reader keeps searching every 30 s. The status lines are muted by the reader's Ctrl+M row,
/// each change line by its own; a mute silences speech, never the window. Errors never escape a tick: they are logged and
/// end in Unavailable, and Restart recovers.
/// </summary>
public sealed class Bbs757DisplayMonitor<TLayout, TReading> : IBbs757DisplayMonitor
    where TLayout : class
    where TReading : IBbs757Reading
{
    internal const long SettleMs = 300;
    internal const long LocateRetryMs = 5_000;
    internal const long LocateGiveUpMs = 120_000;
    internal const long SlowRetryMs = 30_000;
    internal const long LostStopMs = 10_000;

    /// <summary>How long a candidate has, from the moment it is tried (a tick before its first read), to give a reading
    /// that is not corrupt before it is dropped.</summary>
    internal const long VerifyMs = 2_000;

    internal const int CorruptTickLimit = 10;

    /// <summary>After the display powers up, nothing is spoken for this long, so power-up is never spoken (S4).</summary>
    internal const long LitQuietMs = 10_000;

    private enum State { Stopped, WaitingForSimulator, Analyzing, Locating, Verifying, Running, Unavailable }

    /// <summary>What a reading is: torn or out of range, dark or powering up, or good.</summary>
    private enum Verdict { Corrupt, Quiet, Good }

    private readonly IBbs757DisplayReader<TLayout, TReading> _reader;
    private IBbs757DisplayEnvironment? _env;
    private System.Windows.Forms.Timer? _timer;
    private State _state = State.Stopped;
    private bool _inTick;

    // The file analysis and the memory search: started here, run off the UI thread, polled each tick.
    private IReadOnlyList<TLayout>? _layouts;
    private Task<ModuleAnalysis>? _analysis;
    private Task<ModuleLocation>? _locate;
    private long _locateStartMs;
    private long? _lastAttemptMs;
    private int _attempts;
    private bool _slowRetry; // the 120 s window ran out and was spoken: search every 30 s until found
    private long? _lostAtMs; // when the memory was lost while running, until it runs again (the 10 s stop)
    private long? _downSinceMs; // the memory lost while running since then, until it runs again

    // The candidate being read and those waiting. The monitor owns them all and disposes each once.
    private IBbs757DisplaySampler<TReading>? _sampler;
    private readonly Queue<IBbs757DisplaySampler<TReading>> _candidates = new();
    private long _candidateStartMs;

    // The display's power (S4) as the sampler being read sees it, from its readings that are not corrupt.
    private bool? _lastLit;      // at the last such reading; null when nothing is known, or with no power state
    private long? _litSinceMs;   // it powered up then, and is still in its quiet
    private bool _rebaseline;    // a quiet reading, or a wait for the simulator, was seen: the next good reading is a silent baseline

    // Running.
    private TReading _spoken = default!;  // what the pilot last heard (or would have, unmuted)
    private TReading _current = default!; // the latest good reading, for the window
    private long? _changeAtMs;
    private int _corruptTicks;
    private bool _everRunning;
    private string? _stopReason; // the reason in the stop line the pilot heard (or would have, unmuted), until it runs again
    // A stop or unavailable line was said (or would have been, unmuted) and no "resumed" since. Unlike _stopReason it
    // survives Restart: after a flight reload the new flight's first reading is a silent baseline, and without this the
    // "stopped" the pilot heard was never answered. Start with another environment, and Stop, forget it.
    private bool _resumeOwed;
    private string _reason = Bbs757DisplayReasons.InternalError; // why unavailable, or the search window's last failure

    // The reader's simulator inputs (S10), and their keys as each tick reads the simulator with them.
    private readonly IReadOnlyList<Bbs757SimInput> _inputs;
    private readonly IReadOnlyList<string> _inputKeys;

    public Bbs757DisplayMonitor(IBbs757DisplayReader<TLayout, TReading> reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _inputs = Array.AsReadOnly(reader.SimInputs.ToArray());
        _inputKeys = Array.AsReadOnly(_inputs.Select(i => i.Key).ToArray());
    }

    internal string StateName => _state.ToString();

    public IReadOnlyList<Bbs757SimInput> SimInputs => _inputs;

    /// <summary>The latest good reading while running and the display shows it, else default: never the last lit reading
    /// while the display is dark or powering up.</summary>
    public TReading? Latest => ShowsReading ? _current : default;

    /// <summary>The newest reading that is not corrupt while running, whatever the display's power (unlike
    /// <see cref="Latest"/>, also while it is dark or powering up), else default. For what a reader reads beside its
    /// display's picture, such as the FMC's reference speeds the ADI reader carries: never for a row or a spoken change.</summary>
    public TReading? Newest => _state == State.Running ? _newest : default;

    // The newest reading Judge found not corrupt, lit or not; handed out by Newest only while running.
    private TReading _newest = default!;

    /// <summary>Why <see cref="Latest"/> is null, in the words of the status rows without the reader's name, or null while
    /// it holds a reading or the monitor was never started: "display dark" while it runs and the display is dark, the final
    /// or stop reason once there is one, "waiting for the simulator", else "starting" (searching, or the display powering
    /// up). For a key that answers at once with the reason, such as Ctrl+W on the EHSI.</summary>
    public string? NoReadingReason => _state switch
    {
        State.Stopped => null,
        _ when ShowsReading => null,
        State.Unavailable => _reason,
        _ when _stopReason is { } heard => heard,
        State.Running or State.Verifying when _lastLit == false => "display dark",
        State.WaitingForSimulator => "waiting for the simulator",
        _ => "starting",
    };

    /// <summary>The reader's rows of the latest good reading while running and the display shows it, else null.</summary>
    public IReadOnlyList<string>? RowsWhileRunning() => ShowsReading ? _reader.Rows(_current) : null;

    /// <summary>The reader's rows while running; otherwise one row saying why not. Never a corrupt reading, and never
    /// muted. Once a stop line has been said it repeats it until the reader runs again, never "starting" after the pilot
    /// heard "stopped". While it runs and the newest reading is dark, "not powered"; while it powers up (lit, still quiet),
    /// "starting": neither is spoken (S4).</summary>
    public IReadOnlyList<string> View() => RowsWhileRunning() ?? new[] { StatusRow() };

    /// <summary>Running, and the newest reading that is not corrupt is neither dark nor powering up: what the display
    /// shows is the latest good reading. A reader with no power state (the FMA) always shows it while running.</summary>
    private bool ShowsReading => _state == State.Running && _lastLit != false && _litSinceMs is null;

    /// <summary>
    /// Whether the reader's display key goes straight to the AI read (S15) instead of opening the window: not started,
    /// unavailable, its search window ran out, or the memory lost while running for <see cref="LostStopMs"/> or longer.
    /// False while it waits for the simulator, starts, runs, or has been searching again for less: the window opens and
    /// says so.
    /// </summary>
    public bool KeyFallsBackToAi()
    {
        if (_state is State.Stopped or State.Unavailable) return true;
        if (_env is null || _state == State.WaitingForSimulator) return false;
        if (_downSinceMs is { } down && _env.NowMs - down >= LostStopMs) return true;
        return _slowRetry;
    }

    public void Start(IBbs757DisplayEnvironment env) => Start(env, startTimer: true);

    /// <summary>Starts reading through <paramref name="env"/>. On a monitor that is already started it swaps in the
    /// environment and restarts; the layouts are kept only for the same environment.</summary>
    public void Start(IBbs757DisplayEnvironment env, bool startTimer)
    {
        ArgumentNullException.ThrowIfNull(env);
        bool wasStopped = _state == State.Stopped;
        ReleaseReading();
        if (!ReferenceEquals(env, _env))
        {
            // Another environment may hold another session: its files are analysed afresh, and a stop said for the old
            // one is not answered on the new one.
            AbandonAnalysis();
            _layouts = null;
            _resumeOwed = false;
        }
        _env = env;
        BeginSession(wasStopped ? "started" : "started again");
        if (startTimer && _timer is null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = _reader.TickMs };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    /// <summary>Flight reload: drops the memory and searches again, keeping the derived layouts. A stop the pilot heard is
    /// kept, so the new flight's first good reading says "resumed" (alone: it is a baseline).</summary>
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

    private string StatusRow() => _state switch
    {
        State.Unavailable => Bbs757DisplaySpeech.Unavailable(_reader.Name, _reason),
        _ when _stopReason is { } heard => StopLine(heard),
        State.WaitingForSimulator => Bbs757DisplaySpeech.Waiting(_reader.Name),
        State.Running or State.Verifying when _lastLit == false => Bbs757DisplaySpeech.Unpowered(_reader.Name),
        _ => Bbs757DisplaySpeech.Starting(_reader.Name), // starting, or running while the display powers up
    };

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

    private void Step(IBbs757DisplayEnvironment env)
    {
        // Null while the simulator is not up, whether or not the reader takes any input: the silent wait.
        var sim = env.ReadSimulator(_inputKeys);
        if (sim is null)
        {
            // Disconnected, or another aircraft loaded: drop the memory, keep the layouts (and an analysis in flight).
            if (_state != State.WaitingForSimulator)
            {
                ReleaseReading();
                _lostAtMs = null; // waiting for the simulator is silent
                _downSinceMs = null;
                // Another flight may follow (seen only as this wait, with no reload): its first good reading is a
                // silent baseline, never the difference between the flights. A stop heard before is still answered
                // by "resumed" (_resumeOwed), alone.
                _rebaseline = true;
                Go(State.WaitingForSimulator, "the simulator is not available");
            }
            return;
        }
        SayIfLostTooLong(env.NowMs);
        switch (_state)
        {
            case State.WaitingForSimulator: OnWaitingForSimulator(env); break;
            case State.Analyzing: OnAnalyzing(); break;
            case State.Locating: OnLocating(env); break;
            case State.Verifying: OnVerifying(env, sim.Value); break;
            case State.Running: OnRunning(env, sim.Value); break;
        }
    }

    private void OnWaitingForSimulator(IBbs757DisplayEnvironment env)
    {
        if (_layouts is not null)
        {
            BeginLocating(newWindow: true, "the simulator is ready");
            return;
        }
        _analysis ??= env.AnalyseAsync();
        Go(State.Analyzing, "the simulator is ready; analyzing BlueBird's files");
    }

    private void OnAnalyzing()
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
        var layouts = _reader.LayoutsIn(analysis);
        if (layouts.Count == 0)
        {
            // A layout file that loaded but lacks this reader's layout is its own line; no layout file at all (no builds)
            // is the 757 readers' one line (S14).
            if (analysis.Builds.Count > 0)
                EnterUnavailable(Bbs757DisplayReasons.FileNotRecognised);
            else
                EnterNoLayoutFile(Bbs757DisplayReasons.FromSession(analysis.Failure ?? Bbs757DisplayReasons.FileNotRecognised));
            return;
        }
        _layouts = layouts.ToArray();
        BeginLocating(newWindow: true, Invariant($"{_layouts.Count} layout(s) derived"));
    }

    /// <summary>
    /// <paramref name="newWindow"/> starts a new 120 s search window; false keeps the current one. Attempts are always at
    /// least 5 s apart (only Start and Restart reset that), so a reading that keeps failing cannot turn into a busy search
    /// of the simulator's memory.
    /// </summary>
    private void BeginLocating(bool newWindow, string reason)
    {
        ReleaseReading();
        if (newWindow)
        {
            _locateStartMs = _env!.NowMs;
            _attempts = 0;
            _slowRetry = false;
            _reason = Bbs757DisplayReasons.MemoryNotFound;
        }
        Go(State.Locating, reason);
        OnLocating(_env!);
    }

    private void OnLocating(IBbs757DisplayEnvironment env)
    {
        long now = env.NowMs;
        if (_locate is null)
        {
            if (!_slowRetry && now - _locateStartMs >= LocateGiveUpMs)
            {
                // Not final: a long load, or a reload, can outlast the window. Say so once, then keep searching, slowly
                // and silently, until found, Stop or Restart.
                _slowRetry = true;
                Log(Invariant($"{_reason} after {_attempts} attempt(s); searching every {SlowRetryMs / 1000} s"));
                SayStopped(_reason);
            }
            if (_lastAttemptMs is { } last && now - last < (_slowRetry ? SlowRetryMs : LocateRetryMs)) return;
            _lastAttemptMs = now;
            _attempts++;
            _locate = env.LocateAsync();
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
        var location = task.Result;
        var found = SamplersIn(location);
        if (found.Count > 0)
        {
            // Tried in this order; each starts from what is known of the display's power now (nothing changes it while a
            // candidate only reads corrupt, and a failed read or a reading that is not corrupt ends the trying).
            foreach (var sampler in found) _candidates.Enqueue(sampler);
            StartNextCandidate(Invariant($"{found.Count} candidate(s) found"));
            return;
        }
        if (location.Failure is not null)
        {
            EnterUnavailable(Bbs757DisplayReasons.FromSession(location.Failure)); // no access is final
            return;
        }
        // Not found yet: the next attempt starts 5 s (30 s once the window ran out) after this one did.
        _reason = Bbs757DisplayReasons.MemoryNotFound;
    }

    /// <summary>One sampler per layout of this reader in each located build, each on its own lease. A handle closed
    /// meanwhile (a reload or switch racing the search) is "not found yet".</summary>
    private List<IBbs757DisplaySampler<TReading>> SamplersIn(ModuleLocation location)
    {
        var samplers = new List<IBbs757DisplaySampler<TReading>>();
        try
        {
            foreach (var layout in _layouts!)
            {
                foreach (var module in location.Found)
                {
                    IBbs757DisplaySampler<TReading>? sampler;
                    try
                    {
                        sampler = _reader.SamplerFor(module, layout);
                    }
                    catch (ObjectDisposedException)
                    {
                        Log("the simulator handle closed during the memory search");
                        continue;
                    }
                    if (sampler is not null) samplers.Add(sampler);
                }
            }
        }
        catch
        {
            foreach (var sampler in samplers) DisposeQuietly(sampler);
            throw;
        }
        return samplers;
    }

    private void StartNextCandidate(string reason)
    {
        _sampler = _candidates.Dequeue();
        _candidateStartMs = _env!.NowMs;
        Go(State.Verifying, reason);
    }

    /// <summary>
    /// The candidate's first reading that is not corrupt runs it, whatever the display's power: dark or powering up, the
    /// window says so and nothing is spoken until the power-up quiet ends (S4). The others are disposed: the first in
    /// queue order that reads wins. A failed read drops it at once; a candidate that has read only corrupt readings for
    /// <see cref="VerifyMs"/> is dropped too.
    /// </summary>
    private void OnVerifying(IBbs757DisplayEnvironment env, Bbs757SimValues sim)
    {
        long now = env.NowMs;
        if (!_sampler!.TryRead(sim, out var reading))
        {
            DropCandidate(readFailed: true);
            return;
        }
        var verdict = Judge(now, reading);
        if (verdict != Verdict.Corrupt)
        {
            while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue());
            EnterRunning(reading, quiet: verdict == Verdict.Quiet, "a reading that is not corrupt");
            return;
        }
        if (now - _candidateStartMs >= VerifyMs) DropCandidate(readFailed: false);
    }

    /// <summary>
    /// The candidate failed. A wrong layout usually fails the read itself (its pointer lies outside the module), so the
    /// next candidate is tried before searching again: a new search would return the same ones. With none left, it
    /// searches again in the same window, which remembers why: a failed read is "memory not found", only corrupt
    /// readings "unexpected values".
    /// </summary>
    private void DropCandidate(bool readFailed)
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        _reason = readFailed ? Bbs757DisplayReasons.MemoryNotFound : Bbs757DisplayReasons.UnexpectedValues;
        string why = readFailed ? "the memory read failed" : "no reading that is not corrupt for 2 s";
        if (_candidates.Count > 0)
            StartNextCandidate($"{why}; trying the next candidate");
        else
            BeginLocating(newWindow: false, $"{why}; searching again");
    }

    /// <summary>
    /// Runs on <paramref name="reading"/>, which is not corrupt; <paramref name="quiet"/> when it is dark or powering up.
    /// The first good reading after Start or Restart, after a wait for the simulator, or after the display was dark or
    /// powering up, is the baseline, never spoken as a change; only "resumed" when a stop heard (before a reload or a wait
    /// too) is unanswered. Later, what changed while the reader was not running is told, never swallowed: after
    /// "resumed", whose change parts take no prefix of their own, or with its prefix after a quick, silent search, or
    /// when the status row is muted.
    /// </summary>
    private void EnterRunning(TReading reading, bool quiet, string reason)
    {
        var env = _env!;
        bool heardStop = _resumeOwed; // in this session, or before a reload
        bool sayResumed = heardStop && !env.IsMuted(_reader.MuteKey);
        var missed = _everRunning && !_rebaseline && !Same(_spoken, reading)
            ? _reader.Changes(_spoken, reading, withPrefix: !sayResumed)
            : Array.Empty<Bbs757SpokenLine>();
        _spoken = _current = reading;
        // A quiet reading is never the baseline: the first good reading after the quiet is. Kept here, never left to
        // the next quiet tick to set again: a power-up quiet can end at the very next reading.
        _rebaseline = quiet;
        _changeAtMs = null;
        _corruptTicks = 0;
        _everRunning = true;
        _stopReason = null;
        _resumeOwed = false;
        _slowRetry = false;
        _lostAtMs = null;
        _downSinceMs = null;
        Go(State.Running, reason);
        if (sayResumed)
        {
            // The pilot heard "stopped" or "unavailable": tell them it reads again, however it got here, with the
            // unmuted rows' changes.
            var parts = missed.Where(line => !env.IsMuted(line.MuteKey)).Select(line => line.Text);
            env.Speak(string.Join(", ", parts.Prepend(Bbs757DisplaySpeech.Resumed(_reader.Name))));
        }
        else
        {
            foreach (var line in missed) Say(line);
        }
    }

    /// <summary>Only a corrupt reading (ten in a row: unavailable until a reload) or a failed read (the memory lost)
    /// stops a running reader; a quiet one waits; every other reading is good.</summary>
    private void OnRunning(IBbs757DisplayEnvironment env, Bbs757SimValues sim)
    {
        long now = env.NowMs;
        if (!TryReadRunning(now, sim, out var reading)) return;
        var verdict = Judge(now, reading);
        if (verdict == Verdict.Corrupt)
        {
            // Never shown or spoken; ten in a row stop the reader until a reload, which then says "resumed".
            if (_corruptTicks++ == 0) Log("a corrupt reading");
            if (_corruptTicks >= CorruptTickLimit)
            {
                var why = Bbs757DisplayReasons.UnexpectedValues;
                EnterUnavailable(why, Bbs757DisplaySpeech.Stopped(_reader.Name, why),
                    Invariant($"{why} for {CorruptTickLimit} readings"));
            }
            return;
        }
        _corruptTicks = 0;
        if (verdict == Verdict.Quiet)
        {
            // Dark or powering up: neither shown nor spoken, and an open settle window closes unspoken.
            _changeAtMs = null;
            return;
        }
        _current = reading;
        if (_rebaseline)
        {
            // The first good reading after the display was dark or powering up.
            _spoken = reading;
            _rebaseline = false;
            _changeAtMs = null;
            return;
        }
        SpeakSettledChange(now);
    }

    /// <summary>A failed read means the memory moved or went away: search again, silently at first; a search that has
    /// not found the memory again within 10 s says "stopped".</summary>
    private bool TryReadRunning(long now, Bbs757SimValues sim, out TReading reading)
    {
        if (_sampler!.TryRead(sim, out reading)) return true;
        _lostAtMs = now;
        _downSinceMs ??= now;
        BeginLocating(newWindow: true, "the memory read failed; searching again");
        return false;
    }

    /// <summary>
    /// What a reading is (S4): corrupt (torn or out of range) counts for nothing and leaves the power state as it was; a
    /// reading while the display is unpowered, or less than <see cref="LitQuietMs"/> after it powered up, is quiet:
    /// neither shown nor spoken, and the next good reading becomes the baseline silently. Any other is good. No simulator
    /// value takes part (B757-30).
    /// </summary>
    private Verdict Judge(long now, TReading reading)
    {
        if (reading.Corrupt) return Verdict.Corrupt;
        _newest = reading;
        if (IsQuiet(now, reading))
        {
            _rebaseline = true;
            return Verdict.Quiet;
        }
        return Verdict.Good;
    }

    /// <summary>Whether the display is unpowered or powered up less than <see cref="LitQuietMs"/> ago. A display already lit
    /// when nothing is known of its power (the session's first reading) counts at once: its power-up was not seen, and
    /// that reading is a silent baseline.</summary>
    private bool IsQuiet(long now, TReading reading)
    {
        bool? lit = _reader.Lit(reading);
        if (lit is not { } on)
        {
            _lastLit = null;
            _litSinceMs = null;
            return false;
        }
        if (!on)
        {
            _lastLit = false;
            _litSinceMs = null;
            return true;
        }
        if (_lastLit == false) _litSinceMs = now; // it powered up
        _lastLit = true;
        if (_litSinceMs is not { } since) return false;
        if (now - since < LitQuietMs) return true;
        _litSinceMs = null;
        return false;
    }

    /// <summary>Speaks what changed once it has held for 300 ms. A change and back inside the window says nothing; the
    /// baseline advances even when muted, and when nothing speakable changed.</summary>
    private void SpeakSettledChange(long now)
    {
        if (Same(_spoken, _current))
        {
            _changeAtMs = null;
            return;
        }
        var changes = _reader.Changes(_spoken, _current, withPrefix: true);
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

    private static bool Same(TReading a, TReading b) => EqualityComparer<TReading>.Default.Equals(a, b);

    private void BeginSession(string reason)
    {
        ResetSession();
        _lastAttemptMs = null;
        Go(State.WaitingForSimulator, reason);
    }

    /// <summary>A new session: its first good reading is a baseline, the display's power is not known yet, and nothing
    /// was said in it yet (a stop heard before a reload is <see cref="_resumeOwed"/>'s, kept apart).</summary>
    private void ResetSession()
    {
        _everRunning = false;
        _stopReason = null;
        _slowRetry = false;
        _lostAtMs = null;
        _downSinceMs = null;
        _rebaseline = false;
        _lastLit = null;
        _litSinceMs = null;
        _spoken = _current = default!;
    }

    /// <summary>A search after the memory was lost while running that has not found it again within 10 s says "stopped",
    /// so a long search never passes for "no change"; recovery says "resumed" with what changed.</summary>
    private void SayIfLostTooLong(long now)
    {
        if (_lostAtMs is not { } lost || now - lost < LostStopMs) return;
        _lostAtMs = null;
        Log(Invariant($"the memory has not been found again within {LostStopMs / 1000} s of being lost"));
        SayStopped(Bbs757DisplayReasons.MemoryNotFound);
    }

    /// <summary>The stop line, "stopped" once the reader has run in this session, else "unavailable".</summary>
    private string StopLine(string reason) =>
        _everRunning ? Bbs757DisplaySpeech.Stopped(_reader.Name, reason) : Bbs757DisplaySpeech.Unavailable(_reader.Name, reason);

    /// <summary>Speaks the stop line once per reason until the reader runs again (which then says "resumed"), so the same
    /// line is never repeated while a new reason is still told.</summary>
    private void SayStopped(string reason)
    {
        if (_stopReason == reason) return;
        _stopReason = reason;
        _resumeOwed = true;
        SayStatus(StopLine(reason));
    }

    /// <summary>Final until Restart or Start. Spoken once on entry: <paramref name="spoken"/> when a "stopped" line says
    /// it instead, which a reload then answers with "resumed".</summary>
    private void EnterUnavailable(string reason, string? spoken = null, string? logReason = null)
    {
        ReleaseReading();
        _reason = reason;
        if (spoken is not null) _resumeOwed = true;
        Go(State.Unavailable, logReason ?? reason);
        SayStatus(spoken ?? Bbs757DisplaySpeech.Unavailable(_reader.Name, reason));
    }

    /// <summary>No layout file loaded (the module analysis has no builds): unavailable as <see cref="EnterUnavailable"/>,
    /// the reason logged and in the window, but the line is the 757 readers' one <see cref="Bbs757DisplayFilesNotice.Line"/>,
    /// said once per module session by the first of them whose status row is not muted (S14).</summary>
    private void EnterNoLayoutFile(string reason)
    {
        ReleaseReading();
        _reason = reason;
        Go(State.Unavailable, reason);
        _env!.FilesNotice.Say(_env.IsMuted(_reader.MuteKey), _env.Speak);
    }

    private void FailTask(string what, Task task) =>
        Fail(task.Exception ?? (Exception)new OperationCanceledException($"{what} was cancelled"));

    private void Fail(Exception ex)
    {
        try
        {
            Log($"internal error: {ex}");
            EnterUnavailable(Bbs757DisplayReasons.InternalError);
        }
        catch (Exception again)
        {
            Log($"internal error while stopping: {again}");
            _reason = Bbs757DisplayReasons.InternalError;
            _state = State.Unavailable;
        }
    }

    /// <summary>Speaks one line unless its Ctrl+M row is muted; always the non-interrupting announce (S5).</summary>
    private void Say(Bbs757SpokenLine line)
    {
        if (!_env!.IsMuted(line.MuteKey)) _env.Speak(line.Text);
    }

    /// <summary>Speaks a status line ("unavailable", "stopped", "resumed") unless the reader's own row is muted.</summary>
    private void SayStatus(string text) => Say(new Bbs757SpokenLine(_reader.MuteKey, text));

    private void Go(State next, string reason)
    {
        var from = _state;
        _state = next;
        Log($"{from} -> {next}: {reason}");
    }

    private void Log(string line) => TryLog(_env, _reader.LogChannel, line);

    /// <summary>Disposes the sampler and every candidate, and abandons a memory search in flight (its location is the
    /// session's, which releases it; the monitor makes samplers only from a search it polled).</summary>
    private void ReleaseReading()
    {
        DisposeQuietly(_sampler);
        _sampler = null;
        while (_candidates.Count > 0) DisposeQuietly(_candidates.Dequeue());
        _changeAtMs = null;

        var locate = _locate;
        _locate = null;
        if (locate is not null) LogIfFaulted(locate, "abandoned memory search");
    }

    private void AbandonAnalysis()
    {
        var analysis = _analysis;
        _analysis = null;
        if (analysis is not null) LogIfFaulted(analysis, "abandoned file analysis");
    }

    private void LogIfFaulted(Task task, string what)
    {
        var env = _env;
        string channel = _reader.LogChannel;
        task.ContinueWith(t =>
        {
            if (t.IsFaulted) TryLog(env, channel, $"{what} failed: {t.Exception}");
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void DisposeQuietly(IBbs757DisplaySampler<TReading>? sampler)
    {
        if (sampler is null) return;
        try
        {
            sampler.Dispose();
        }
        catch (Exception ex)
        {
            Log($"disposing a sampler failed: {ex}");
        }
    }

    private static void TryLog(IBbs757DisplayEnvironment? env, string channel, string line)
    {
        try
        {
            env?.Log(channel, line);
        }
        catch
        {
            // The log itself failed: there is nowhere left to report it.
        }
    }
}
