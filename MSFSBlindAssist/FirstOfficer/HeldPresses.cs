using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The presses an executor holds right now (a fire test, the T.O CONFIG test, a cabin call), each
/// with the write that releases it, and what a running test still owes the aircraft (the 777 weather
/// radar test's WX mode and overlay off). A hold ends on its own timer (<see cref="End"/>); when the
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

    /// <summary>The hold's owner sends the release itself (awaited and paced, the 777 weather radar
    /// test's restore): drops it unsent. True when it was still owed, false when
    /// <see cref="ReleaseAll"/> already sent it.</summary>
    public bool TakeOver(long id)
    {
        lock (_lock) return _held.Remove(id);
    }

    /// <summary>Sends every outstanding release now, each once, the latest hold first (nested holds
    /// unwind: a test button held inside the weather radar test is released before its restore);
    /// returns how many.</summary>
    public int ReleaseAll()
    {
        Action[] releases;
        lock (_lock)
        {
            releases = _held.OrderByDescending(h => h.Key).Select(h => h.Value).ToArray();
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
