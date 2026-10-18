using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The presses an executor holds right now (a fire test, the T.O CONFIG test, a cabin call), each
/// with the write that releases it. A hold ends on its own timer (<see cref="End"/>); when the
/// First Officer window closes first, <see cref="ReleaseAll"/> sends every release at once
/// (<see cref="IFoActionExecutor.ReleaseHeldPresses"/>). On app exit MainForm disconnects right
/// after closing the window, and a release left to the hold's own timer then went nowhere, leaving
/// the test held in the sim after MSFSBA had closed. Whoever ends a hold first sends its release,
/// once; a release that throws is logged, never thrown. Thread-safe.
/// </summary>
public sealed class HeldPresses
{
    private readonly object _lock = new();
    private readonly Dictionary<long, Action> _held = new();
    private long _next;

    /// <summary>Records a hold just pressed; <paramref name="release"/> is the write that ends it,
    /// and guards itself (a disconnected sim).</summary>
    public long Begin(Action release)
    {
        lock (_lock)
        {
            long id = ++_next;
            _held[id] = release;
            return id;
        }
    }

    /// <summary>The hold's own end: sends its release, unless <see cref="ReleaseAll"/> already did.</summary>
    public void End(long id)
    {
        Action? release;
        lock (_lock)
        {
            if (!_held.Remove(id, out release)) return;
        }
        Run(release);
    }

    /// <summary>Sends every outstanding release now, each once; returns how many.</summary>
    public int ReleaseAll()
    {
        Action[] releases;
        lock (_lock)
        {
            releases = _held.Values.ToArray();
            _held.Clear();
        }
        foreach (var release in releases) Run(release);
        return releases.Length;
    }

    /// <summary>How many holds are outstanding.</summary>
    public int Count
    {
        get { lock (_lock) return _held.Count; }
    }

    private static void Run(Action release)
    {
        try { release(); }
        catch (Exception ex) { Log.Warn("FO", $"releasing a held press threw: {ex.Message}"); }
    }
}
