namespace MSFSBlindAssist.Services;

/// <summary>
/// Whether a surroundings lookup (Alt+L, Ctrl+Shift+L) has been slow enough to be worth saying
/// "Looking around." about.
///
/// <para>A COLD first press at an airport can wait 3-10 s with nothing said: up to 3 s for the
/// OSM mirror, plus a first-time scenery scan of the package and, on an MSFS 2024 database, a
/// census of the whole Community folder. A blind pilot has no spinner, so that silence and "the
/// key did nothing" are the same experience — and the window the second hotkey opens can take
/// the foreground many seconds after the press. But the notice is about 0.8 s of speech placed
/// IN FRONT of the answer, so speaking it on a cache hit (every press after the first at one
/// airport) would be noise over the thing they asked for. Hence: only when the answer is
/// genuinely slow, at most once per press, and QUEUED so it can never cut a taxi instruction.</para>
/// </summary>
internal static class SurroundingsLookupNotice
{
    /// <summary>How long the answer may take before the notice is worth its own 0.8 s. Under the
    /// OSM tier's own 3 s bound, so a lookup waiting only on the mirror still gets it.</summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// True when <paramref name="work"/> had not finished within <paramref name="delay"/>. Never
    /// throws <paramref name="work"/>'s own exception and never leaves it unobserved: the caller
    /// awaits the same task immediately afterwards, inside its own catch, and a failure surfacing
    /// HERE would replace "Surroundings lookup failed." with an unhandled pool-thread exception.
    /// <para><c>WaitAsync</c>, not <c>WhenAny(work, Task.Delay(…))</c>, which arms a timer nothing
    /// cancels when the work wins.</para>
    /// </summary>
    internal static async Task<bool> IsSlowAsync(Task work, TimeSpan delay)
    {
        try { await work.WaitAsync(delay).ConfigureAwait(false); }
        catch (TimeoutException) { return true; }
        catch { /* the caller owns this failure */ }
        return false;
    }
}
