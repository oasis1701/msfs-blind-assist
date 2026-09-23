namespace MSFSBlindAssist.Services;

/// <summary>How one spoken line of a surroundings lookup is delivered — see
/// <see cref="SurroundingsLookupNotice.Delivery"/>.</summary>
internal enum SurroundingsLookupDelivery
{
    /// <summary><c>AnnounceImmediate</c>: still the press's own moment, so it may interrupt, as every
    /// hotkey answer does.</summary>
    Immediate,
    /// <summary><c>Announce</c>: late enough that what is being spoken may be NEWER than the press —
    /// a taxi instruction, or this lookup's own "Looking around." — so it waits its turn.</summary>
    Queued,
}

/// <summary>
/// How a surroundings lookup (Alt+L, Ctrl+Shift+L) sounds while it is slow: whether it has been
/// slow enough to be worth saying "Looking around." about (<see cref="IsSlowAsync"/>), and whether a
/// line it speaks may still interrupt (<see cref="Delivery"/>).
///
/// <para>A COLD first press at an airport can wait seconds with nothing said: a first-time scenery
/// scan of the package and, on an MSFS 2024 database, a census of the whole Community folder, with
/// the OSM mirror's wait (OnlineFeatureStore.CatalogWait) running beside them rather than after
/// them. A blind pilot has no spinner, so that silence and "the
/// key did nothing" are the same experience — and the window the second hotkey opens can take
/// the foreground many seconds after the press. But the notice is about 0.8 s of speech placed
/// IN FRONT of the answer, so speaking it on a cache hit (every press after the first at one
/// airport) would be noise over the thing they asked for. Hence: only when the answer is
/// genuinely slow, at most once per press, and QUEUED so it can never cut a taxi instruction.</para>
/// </summary>
internal static class SurroundingsLookupNotice
{
    /// <summary>How long the answer may take before the notice is worth its own 0.8 s — and so also
    /// the line between a lookup line that may interrupt and one that must queue
    /// (<see cref="Delivery"/>): the notice is queued at this moment, and a later line interrupting
    /// would cut it off. Under OnlineFeatureStore.CatalogWait with room for the notice itself, so a
    /// lookup waiting only on the mirror still gets it (pinned by SurroundingsLookupNoticeTests).</summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// How one line of a surroundings lookup — its answer, or its error/empty line — is spoken, from
    /// how long ago the KEY was pressed (review item ML-1). Within <see cref="Delay"/> it is still the
    /// press's own moment and interrupts, as every hotkey answer does. From <see cref="Delay"/> on it
    /// is QUEUED: a cold lookup takes 3-10 s, and in that time the pilot can have been given a taxi
    /// instruction ("Stop. Hold short of runway 27L.") that an interrupting answer would cut off
    /// mid-word — and "Looking around.", queued at exactly this delay, is ahead of it too. The one
    /// exception is a SUPPRESSED announcer (a first-detect grace window): a queued line is DROPPED
    /// there, and a pilot who pressed a key must never hear nothing, so it interrupts.
    /// </summary>
    internal static SurroundingsLookupDelivery Delivery(TimeSpan sincePress, bool announcerSuppressed)
        => sincePress < Delay || announcerSuppressed ? SurroundingsLookupDelivery.Immediate : SurroundingsLookupDelivery.Queued;

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
