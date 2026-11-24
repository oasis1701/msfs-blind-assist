using MSFSBlindAssist.Utils.Logging;
using Prismatoid;

namespace MSFSBlindAssist.Accessibility;

/// <summary>
/// The screen readers Prism (the Prismatoid package, its native prism.dll) can reach, for
/// <see cref="ScreenReaderOutput"/>. One Prism context for the life of the process, created on first use.
///
/// <para>Prism's speech engines (SAPI, OneCore) and its UI Automation backend are left out: the announcer keeps its
/// own System.Speech voice for SAPI, and UIA needs explicit selection and a window, and cannot tell whether anything
/// is listening. Everything else Prism registers is a screen reader (NVDA, JAWS, ZDSR, PC-Talker, Boy PC Reader,
/// ZoomText, Sense Reader, System Access, Window-Eyes) and is tried in Prism's priority order.</para>
///
/// <para>Readers load what they need from their own installation: ZDSR's ZDSRAPI_x64.dll is found through ZDSR's
/// registry path, NVDA is reached over its own RPC endpoint (no nvdaControllerClient.dll), JAWS over COM. Nothing of
/// theirs is shipped.</para>
/// </summary>
internal sealed class PrismScreenReaderBackends : IScreenReaderBackends
{
    private static readonly HashSet<PrismBackendId> NotScreenReaders = new()
    {
        PrismBackendId.Sapi, PrismBackendId.OneCore, PrismBackendId.Uia,
    };

    private readonly PrismContext _context;

    public PrismScreenReaderBackends()
    {
        RouteLog();
        _context = new PrismContext();
        Candidates = _context.AvailableBackends
            .Where(info => !NotScreenReaders.Contains(info.Id))
            .OrderByDescending(info => info.Priority)
            .Select(info => new ScreenReaderCandidate(info.Id.Value, info.Name, info.Priority))
            .ToList();
        Log.Info("Accessibility", "Screen readers through Prism, in order: " + string.Join(", ", Candidates.Select(c => c.Name)));
    }

    public IReadOnlyList<ScreenReaderCandidate> Candidates { get; }

    public IScreenReaderBackend? TryCreate(ScreenReaderCandidate candidate)
    {
        try
        {
            // A fresh instance every time, never the context's cached one: Prism's NVDA connection does not
            // reconnect, so after NVDA restarts only a new instance speaks.
            return new Reader(candidate.Name, _context.CreateBackend(new PrismBackendId(candidate.Id)));
        }
        catch (PrismException ex)
        {
            // Not running is the usual answer and not worth a line; anything else is.
            if (ex.Error != Prismatoid.NativeInterop.PrismError.PRISM_ERROR_BACKEND_NOT_AVAILABLE)
                Log.Debug("Accessibility", $"Prism could not start {candidate.Name}: {ex.Error} {ex.Message}");
            return null;
        }
    }

    // Prism's warnings and errors go to debug.log, each distinct line once: a rescan with no reader running repeats
    // the same "left unresolved" warning for every reader that is not installed.
    private static void RouteLog()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        PrismLog.SetLevel(PrismLogLevel.Warn);
        PrismLog.SetHandler((level, source, message) =>
        {
            string line = $"Prism {level} {source}: {message}";
            lock (seen)
            {
                if (seen.Count >= 200 || !seen.Add(line)) return;   // capped, in case Prism's wording ever varies
            }
            Log.Debug("Accessibility", line);
        });
    }

    private sealed class Reader : IScreenReaderBackend
    {
        private readonly IPrismBackend _backend;

        public Reader(string name, IPrismBackend backend)
        {
            Name = name;
            _backend = backend;
            // What this reader's backend says it can do, read once (Prism computes it at creation): NVDA, ZDSR and
            // JAWS all speak and braille; a reader without braille is never sent any.
            var features = backend.Features;
            CanSpeak = features.HasFlag(PrismBackendFeature.SupportsSpeak);
            CanBraille = features.HasFlag(PrismBackendFeature.SupportsBraille);
        }

        public string Name { get; }
        public bool CanSpeak { get; }
        public bool CanBraille { get; }

        public void Speak(string text, bool interrupt) => _backend.Speak(text, interrupt);

        // Braille and Speak separately, never Prism's Output: Output fails as a whole when braille does, and ZDSR
        // with no braille display fails every braille call.
        public void Braille(string text) => _backend.Braille(text);

        public void Dispose() => _backend.Dispose();
    }
}
