using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Coherent GT accepts ONE inspector socket per view: a second connection is closed, and the
/// client it displaces loses its agent or its answer. This registry makes that rule structural
/// instead of a check each caller has to remember and get the timing of right:
///
///  • a PERSISTENT client (<see cref="CoherentA32nxMcduClient"/>) claims its view for its whole
///    lifetime — reconnect gaps included, which is exactly when a "does it hold a socket right
///    now?" check said no and let a one-shot in beside the reconnect;
///  • a ONE-SHOT eval (<see cref="CoherentEvalClient"/>) is refused on a claimed view, and while it
///    runs it is registered, so the persistent client does not open its socket on top of it — and
///    the owner is told the moment the last one ends, so it connects then, not a reconnect pass later.
///
/// Views are compared by their title needle, ignoring case. Process-wide and thread-safe: the
/// persistent client's loop runs on the thread pool, one-shots on the UI thread's continuations.
/// </summary>
internal static class CoherentViewOwnership
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Claims = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> OneShots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Action>> Owners = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Claim a view for a persistent client; dispose to release it. <paramref name="onOneShotDone"/>
    /// is called when the last one-shot eval on the view ends — one that began before the claim —
    /// so the owner can open its socket at once instead of on its next reconnect pass. It runs on
    /// the thread that ended the one-shot, outside the registry's lock.
    /// </summary>
    public static IDisposable Claim(string viewNeedle, Action? onOneShotDone = null)
    {
        lock (Gate)
        {
            Increment(Claims, viewNeedle);
            if (onOneShotDone != null)
            {
                if (!Owners.TryGetValue(viewNeedle, out var owners)) { Owners[viewNeedle] = owners = new List<Action>(); }
                owners.Add(onOneShotDone);
            }
        }
        return new Release(() =>
        {
            lock (Gate)
            {
                Decrement(Claims, viewNeedle);
                if (onOneShotDone != null && Owners.TryGetValue(viewNeedle, out var owners))
                {
                    owners.Remove(onOneShotDone);
                    if (owners.Count == 0) { Owners.Remove(viewNeedle); }
                }
            }
        });
    }

    public static bool IsClaimed(string viewNeedle)
    {
        lock (Gate) { return Claims.ContainsKey(viewNeedle); }
    }

    /// <summary>
    /// Enter a view for a one-shot eval; dispose when the eval is done. Null when a persistent
    /// client owns the view — the caller must not open a socket on it.
    /// </summary>
    public static IDisposable? TryEnterOneShot(string viewNeedle)
    {
        lock (Gate)
        {
            if (Claims.ContainsKey(viewNeedle)) { return null; }
            Increment(OneShots, viewNeedle);
        }
        return new Release(() =>
        {
            Action[] toTell;
            lock (Gate)
            {
                Decrement(OneShots, viewNeedle);
                toTell = !OneShots.ContainsKey(viewNeedle) && Owners.TryGetValue(viewNeedle, out var owners)
                    ? owners.ToArray()
                    : Array.Empty<Action>();
            }
            foreach (var tell in toTell)
            {
                // An owner's wake-up must never fail the one-shot that has just finished.
                try { tell(); }
                catch (Exception ex) { Log.Debug("SimConnect", $"CoherentViewOwnership: owner of '{viewNeedle}' threw on wake-up: {ex.Message}"); }
            }
        });
    }

    /// <summary>True while a one-shot eval holds (or is opening) a socket on the view.</summary>
    public static bool OneShotInFlight(string viewNeedle)
    {
        lock (Gate) { return OneShots.ContainsKey(viewNeedle); }
    }

    private static void Increment(Dictionary<string, int> counts, string key)
        => counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

    private static void Decrement(Dictionary<string, int> counts, string key)
    {
        if (!counts.TryGetValue(key, out int n)) { return; }
        if (n <= 1) { counts.Remove(key); } else { counts[key] = n - 1; }
    }

    /// <summary>Runs its release once, however many times it is disposed.</summary>
    private sealed class Release : IDisposable
    {
        private Action? _release;
        public Release(Action release) => _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
