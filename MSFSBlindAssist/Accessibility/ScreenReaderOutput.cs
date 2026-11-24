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
/// Prism (<see cref="PrismScreenReaderBackends"/>). Readers are tried in Prism's priority order (NVDA, then ZDSR,
/// PC-Talker and Boy PC Reader, then JAWS and the rest); speech engines are never used here, so SAPI stays the
/// announcer's own System.Speech voice.
///
/// <para>A reader is kept while it takes messages. When a message fails (the reader closed), the next reader is
/// tried for that same message, so it is not lost. Otherwise readers are looked for again at most every
/// <see cref="RescanIntervalMs"/>: all of them while none is in use, and only those above the one in use while one is
/// (NVDA started after ZDSR takes over). Prism's own availability watcher is not used: measured 2026-11-24, it
/// reported NVDA closing and starting but never ZDSR.</para>
///
/// <para>Every spoken message is also brailled. A braille failure never fails the message: ZDSR with no braille
/// display answers 3, which Prism reports as an error, and the speech was still heard.</para>
/// </summary>
public sealed class ScreenReaderOutput
{
    /// <summary>How long a "no better reader" finding is trusted before readers are looked for again.</summary>
    internal const long RescanIntervalMs = 5000;

    private readonly object _gate = new();
    private readonly Func<IScreenReaderBackends> _sourceFactory;
    private readonly Func<long> _clockMs;
    private readonly HashSet<string> _brailleFailuresLogged = new(StringComparer.Ordinal);

    private IScreenReaderBackends? _source;
    private bool _sourceFailed;
    private IScreenReaderBackend? _active;
    private ScreenReaderCandidate? _activeCandidate;
    private long _nextRescanMs = long.MinValue;

    private static readonly Lazy<ScreenReaderOutput> SharedInstance =
        new(() => new ScreenReaderOutput(() => new PrismScreenReaderBackends(), () => Environment.TickCount64));

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

            long now = _clockMs();
            if (now >= _nextRescanMs) Rescan(source, now);

            ScreenReaderCandidate? failed = null;
            while (_active != null)
            {
                try
                {
                    _active.Speak(text, interrupt);
                }
                catch (Exception ex)
                {
                    Log.Info("Accessibility", $"{_activeCandidate!.Name} did not take speech ({ex.Message}); trying the next screen reader");
                    if (failed != null)
                    {
                        // A second failure in one message: give it to SAPI, look again at the next rescan.
                        Drop();
                        return false;
                    }
                    failed = _activeCandidate;
                    Drop();
                    Rescan(source, now, skip: failed);
                    continue;
                }
                Braille(text);
                return true;
            }
            return false;
        }
    }

    /// <summary>Looks for readers now, whatever the throttle says, and names the one in use (null when none):
    /// for the Settings status box.</summary>
    public string? DetectNow()
    {
        lock (_gate)
        {
            var source = Source();
            if (source == null) return null;
            Rescan(source, _clockMs());
            return _activeCandidate?.Name;
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

    // Starts the best reader above the one in use (any, when none is), replacing it; then trusts that for a while.
    private void Rescan(IScreenReaderBackends source, long now, ScreenReaderCandidate? skip = null)
    {
        _nextRescanMs = now + RescanIntervalMs;
        foreach (var candidate in source.Candidates)
        {
            if (_activeCandidate != null && candidate.Priority <= _activeCandidate.Priority) break;
            if (candidate == skip) continue;
            IScreenReaderBackend? started;
            try
            {
                started = source.TryCreate(candidate);
            }
            catch (Exception ex)
            {
                Log.Debug("Accessibility", $"{candidate.Name} could not be started: {ex.Message}");
                started = null;
            }
            if (started == null) continue;
            if (!started.CanSpeak)
            {
                // Prism's feature flags say this reader takes no speech: never ours, whatever else it does.
                Log.Debug("Accessibility", $"{candidate.Name} is running but takes no speech; skipped");
                try { started.Dispose(); } catch (Exception ex) { Log.Debug("Accessibility", $"Closing {candidate.Name}: {ex.Message}"); }
                continue;
            }

            if (_activeCandidate != null) Log.Info("Accessibility", $"{candidate.Name} started; announcements move to it from {_activeCandidate.Name}");
            else Log.Info("Accessibility", $"Announcements go to {candidate.Name}");
            Drop();
            _active = started;
            _activeCandidate = candidate;
            return;
        }
    }

    private void Drop()
    {
        try { _active?.Dispose(); } catch (Exception ex) { Log.Debug("Accessibility", $"Closing {_activeCandidate?.Name}: {ex.Message}"); }
        _active = null;
        _activeCandidate = null;
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
