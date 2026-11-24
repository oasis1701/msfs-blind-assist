using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Accessibility;

/// <summary>A screen reader Prism can talk to: its registry id, name and priority (higher is tried first).</summary>
internal sealed record ScreenReaderCandidate(ulong Id, string Name, int Priority);

/// <summary>One live connection to a screen reader. Speak and Braille throw when the reader did not take the call;
/// they are called only when the reader says it can (<see cref="CanSpeak"/>, <see cref="CanBraille"/>, from Prism's
/// own feature flags for that reader).</summary>
internal interface IScreenReaderBackend : IDisposable
{
    string Name { get; }
    bool CanSpeak { get; }
    bool CanBraille { get; }
    void Speak(string text, bool interrupt);
    void Braille(string text);
}

/// <summary>The screen readers there are (speech engines left out), highest priority first, and a way to start one.</summary>
internal interface IScreenReaderBackends
{
    IReadOnlyList<ScreenReaderCandidate> Candidates { get; }

    /// <summary>A fresh connection to <paramref name="candidate"/>, or null when that reader is not running.</summary>
    IScreenReaderBackend? TryCreate(ScreenReaderCandidate candidate);
}

/// <summary>
/// Which screen reader MSFS Blind Assist's announcements go to, and the speaking and brailling of each one, through
/// Prism (<see cref="PrismScreenReaderBackends"/>, every call on one thread: <see cref="SingleThreadScreenReaderBackends"/>).
/// Readers are tried in Prism's priority order (NVDA, then PC-Talker, ZDSR and Boy PC Reader, then JAWS and the
/// rest); speech engines are never used here, so SAPI stays the announcer's own System.Speech voice.
///
/// <para>A reader is kept while it takes messages. When a message fails on a connection that spoke before, the
/// reader has usually restarted (Prism's NVDA connection does not reconnect), so one fresh connection to that same
/// reader is given the message. When that fails too, or the reader has not spoken yet (one just started, as NVDA in
/// its first seconds), the other readers are tried for that same message, each failing one left out, so it is not
/// lost.</para>
///
/// <para>Otherwise readers are looked for again at most every <see cref="RescanIntervalMs"/>. While none is in use,
/// all of them (every <see cref="SlowRescanIntervalMs"/> once none has run for <see cref="NoReaderSlowAfterMs"/>:
/// Prismatoid 0.4.0 leaks about 1 KB per reader that is not running). While one is in use, only those above it: a
/// reader that has started is given the message first, and the one in use is closed only once that reader has
/// spoken (closing a connection can drop what the reader has queued), so NVDA started after ZDSR takes over
/// without costing ZDSR anything while NVDA is still starting. A better reader that keeps failing is tried after
/// 5, 10, 20, 40 and then every 60 seconds. Prism's own availability watcher is not used: measured 2026-11-24, it
/// reported NVDA closing and starting but never ZDSR.</para>
///
/// <para>Every spoken message is also brailled. A braille failure never fails the message: ZDSR with no braille
/// display answers 3, which Prism reports as an error, and the speech was still heard.</para>
/// </summary>
public sealed class ScreenReaderOutput
{
    /// <summary>How long a "no better reader" finding is trusted before readers are looked for again.</summary>
    internal const long RescanIntervalMs = 5000;

    /// <summary>The interval once no reader has run for <see cref="NoReaderSlowAfterMs"/>. Opening Settings
    /// (<see cref="DetectNow"/>) brings the short one back.</summary>
    internal const long SlowRescanIntervalMs = 30_000;

    /// <summary>How long no reader has to have run before the slower look.</summary>
    internal const long NoReaderSlowAfterMs = 120_000;

    /// <summary>The longest wait before a reader above the one in use, which started but did not speak, is tried again.</summary>
    internal const long MaxTrialWaitMs = 60_000;

    private readonly object _gate = new();
    private readonly Func<IScreenReaderBackends> _sourceFactory;
    private readonly Func<long> _clockMs;
    private readonly HashSet<string> _brailleFailuresLogged = new(StringComparer.Ordinal);
    private readonly HashSet<string> _speechFailuresLogged = new(StringComparer.Ordinal);
    // Readers above the one in use that started but did not speak: how often in a row, and when to try again.
    private readonly Dictionary<ScreenReaderCandidate, (int Failures, long NotBeforeMs)> _trialWaits = new();

    private IScreenReaderBackends? _source;
    private bool _sourceFailed;
    private IScreenReaderBackend? _active;
    private ScreenReaderCandidate? _activeCandidate;
    // Whether the connection in use has spoken: only then is a failure a reader that restarted, worth a fresh connection.
    private bool _activeSpoke;
    private long _nextRescanMs = long.MinValue;
    // When a look first found no reader running, null while one is in use.
    private long? _noReaderSinceMs;

    // Prism's readers, every call on one thread of its own: JAWS is reached over COM, and announcements come from
    // the UI thread and from background timers alike.
    private static readonly Lazy<ScreenReaderOutput> SharedInstance =
        new(() => new ScreenReaderOutput(
            () => new SingleThreadScreenReaderBackends(() => new PrismScreenReaderBackends()),
            () => Environment.TickCount64));

    /// <summary>The one instance of the process: the announcer speaks through it and Settings shows its reader.</summary>
    public static ScreenReaderOutput Shared => SharedInstance.Value;

    internal ScreenReaderOutput(Func<IScreenReaderBackends> sourceFactory, Func<long> clockMs)
    {
        _sourceFactory = sourceFactory;
        _clockMs = clockMs;
    }

    /// <summary>Whether Prism has started (it starts on first use); false before that, and for good when it could
    /// not (prism.dll missing or broken), so Settings can tell "no reader" from "could not look".</summary>
    public bool Started
    {
        get { lock (_gate) { return _source != null; } }
    }

    /// <summary>The reader in use, or null when none is.</summary>
    public string? ActiveReaderName
    {
        get { lock (_gate) { return _activeCandidate?.Name; } }
    }

    /// <summary>Speaks <paramref name="text"/> through the screen reader (interrupting what it says when
    /// <paramref name="interrupt"/>) and brailles it. False when no screen reader took it, so the caller speaks it
    /// some other way.</summary>
    public bool TrySpeak(string text, bool interrupt)
    {
        if (string.IsNullOrEmpty(text)) return false;
        lock (_gate)
        {
            var source = Source();
            if (source == null) return false;

            // Every reader that fails this message is left out of the next try, so a third running reader still
            // takes it before SAPI does; the list grows each pass, so the loop below ends.
            HashSet<ScreenReaderCandidate>? failed = null;
            long now = _clockMs();
            if (now >= _nextRescanMs)
            {
                if (_active == null) StartFirstRunning(source, now, failed);
                else if (SpeakThroughBetterReader(source, now, text, interrupt, ref failed)) return true;
            }

            while (_active != null)
            {
                try
                {
                    _active.Speak(text, interrupt);
                }
                catch (Exception ex)
                {
                    var candidate = _activeCandidate!;
                    bool spokeBefore = _activeSpoke;
                    Drop();
                    if (spokeBefore)
                    {
                        // It spoke before, so the reader most likely restarted and only this connection is gone.
                        var fresh = Start(source, candidate);
                        if (fresh != null)
                        {
                            Log.Debug("Accessibility", $"{candidate.Name} did not take speech ({ex.Message}); connected to it again");
                            _active = fresh;
                            _activeCandidate = candidate;
                            continue;
                        }
                    }
                    LogSpeechFailure(candidate, ex);
                    (failed ??= new()).Add(candidate);
                    StartFirstRunning(source, now, failed);
                    continue;
                }
                Spoke(text);
                return true;
            }
            return false;
        }
    }

    /// <summary>Looks for readers now, whatever the throttle says, and names the one in use (null when none):
    /// for the Settings status box. Unlike the look before a message, it checks the reader in use is still
    /// running: a reader that closed since the last message is not named. A reader above the one in use that has
    /// started is not named yet: it is given the next message, and named once it has spoken.</summary>
    public string? DetectNow()
    {
        lock (_gate)
        {
            var source = Source();
            if (source == null) return null;
            long now = _clockMs();

            if (_activeCandidate != null)
            {
                var probe = Start(source, _activeCandidate);
                if (probe == null)
                {
                    Log.Info("Accessibility", $"{_activeCandidate.Name} is no longer running");
                    Drop();
                }
                else
                {
                    // Still running: keep the connection in use (closing it can drop its queued speech), close the probe.
                    Close(probe, _activeCandidate.Name);
                }
            }

            if (_activeCandidate == null)
            {
                // Opening Settings is the pilot looking for their reader: the short interval again.
                _noReaderSinceMs = null;
                StartFirstRunning(source, now, skip: null);
                return _activeCandidate?.Name;
            }

            // A reader above the one in use gets the next message at once, whatever it did before.
            foreach (var candidate in source.Candidates)
            {
                if (candidate.Priority <= _activeCandidate.Priority) break;
                _trialWaits.Remove(candidate);
            }
            _nextRescanMs = now;
            return _activeCandidate.Name;
        }
    }

    private IScreenReaderBackends? Source()
    {
        if (_source != null || _sourceFailed) return _source;
        try
        {
            _source = _sourceFactory();
        }
        catch (Exception ex)
        {
            // Never retried: a missing or broken prism.dll does not mend itself while the app runs.
            _sourceFailed = true;
            Log.Warn("Accessibility", $"Screen reader output (Prism) could not start, so SAPI speaks: {ex.GetType().Name}: {ex.Message}");
        }
        return _source;
    }

    // With no reader in use: the first reader that starts, leaving out those in skip, becomes the one in use.
    // Sets when to look next.
    private void StartFirstRunning(IScreenReaderBackends source, long now, HashSet<ScreenReaderCandidate>? skip)
    {
        foreach (var candidate in source.Candidates)
        {
            if (skip != null && skip.Contains(candidate)) continue;
            var started = Start(source, candidate);
            if (started == null) continue;
            Adopt(started, candidate);
            _nextRescanMs = now + RescanIntervalMs;
            return;
        }
        _noReaderSinceMs ??= now;
        _nextRescanMs = now + (now - _noReaderSinceMs.Value >= NoReaderSlowAfterMs ? SlowRescanIntervalMs : RescanIntervalMs);
    }

    // With a reader in use: each reader above it that has started is given the message. The first that speaks
    // replaces the one in use (true: the message is spoken); one that does not is closed, added to failed and
    // tried again later, and the reader in use is untouched.
    private bool SpeakThroughBetterReader(IScreenReaderBackends source, long now, string text, bool interrupt,
        ref HashSet<ScreenReaderCandidate>? failed)
    {
        _nextRescanMs = now + RescanIntervalMs;
        foreach (var candidate in source.Candidates)
        {
            if (candidate.Priority <= _activeCandidate!.Priority) break;
            if (_trialWaits.TryGetValue(candidate, out var wait) && now < wait.NotBeforeMs) continue;
            var started = Start(source, candidate);
            if (started == null)
            {
                _trialWaits.Remove(candidate);   // not running: when it starts, it is tried at once
                continue;
            }
            try
            {
                started.Speak(text, interrupt);
            }
            catch (Exception ex)
            {
                Close(started, candidate.Name);
                int failures = wait.Failures + 1;
                long waitMs = Math.Min(RescanIntervalMs << Math.Min(failures - 1, 10), MaxTrialWaitMs);
                _trialWaits[candidate] = (failures, now + waitMs);
                LogSpeechFailure(candidate, ex);
                (failed ??= new()).Add(candidate);
                continue;
            }
            Adopt(started, candidate);
            Spoke(text);
            return true;
        }
        return false;
    }

    // The reader in use took the message: remember it speaks, and braille the message.
    private void Spoke(string text)
    {
        _activeSpoke = true;
        _speechFailuresLogged.Remove(_activeCandidate!.Name);
        _trialWaits.Remove(_activeCandidate);
        Braille(text);
    }

    // Once per run of failures for a reader: one that keeps starting but not speaking would otherwise log at
    // every look.
    private void LogSpeechFailure(ScreenReaderCandidate candidate, Exception ex)
    {
        if (_speechFailuresLogged.Add(candidate.Name))
            Log.Info("Accessibility", $"{candidate.Name} did not take speech ({ex.Message}); trying the next screen reader");
    }

    // A fresh connection to a reader that is running and takes speech, or null.
    private static IScreenReaderBackend? Start(IScreenReaderBackends source, ScreenReaderCandidate candidate)
    {
        IScreenReaderBackend? started;
        try
        {
            started = source.TryCreate(candidate);
        }
        catch (Exception ex)
        {
            Log.Debug("Accessibility", $"{candidate.Name} could not be started: {ex.Message}");
            return null;
        }
        if (started == null) return null;
        if (!started.CanSpeak)
        {
            // Prism's feature flags say this reader takes no speech: never ours, whatever else it does.
            Log.Debug("Accessibility", $"{candidate.Name} is running but takes no speech; skipped");
            Close(started, candidate.Name);
            return null;
        }
        return started;
    }

    private void Adopt(IScreenReaderBackend started, ScreenReaderCandidate candidate)
    {
        if (_activeCandidate != null) Log.Info("Accessibility", $"{candidate.Name} started; announcements move to it from {_activeCandidate.Name}");
        else Log.Info("Accessibility", $"Announcements go to {candidate.Name}");
        Drop();
        _active = started;
        _activeCandidate = candidate;
        _noReaderSinceMs = null;
    }

    private static void Close(IScreenReaderBackend backend, string name)
    {
        try { backend.Dispose(); } catch (Exception ex) { Log.Debug("Accessibility", $"Closing {name}: {ex.Message}"); }
    }

    private void Drop()
    {
        try { _active?.Dispose(); } catch (Exception ex) { Log.Debug("Accessibility", $"Closing {_activeCandidate?.Name}: {ex.Message}"); }
        _active = null;
        _activeCandidate = null;
        _activeSpoke = false;
    }

    private void Braille(string text)
    {
        if (!_active!.CanBraille) return;   // the reader says it has no braille: nothing to send
        try
        {
            _active!.Braille(text);
        }
        catch (Exception ex)
        {
            // ZDSR with no braille display, or a reader without braille: the speech was heard, which is what counts.
            if (_brailleFailuresLogged.Add(_activeCandidate!.Name))
                Log.Debug("Accessibility", $"{_activeCandidate.Name} did not take braille ({ex.Message}); it is still sent each time");
        }
    }
}
