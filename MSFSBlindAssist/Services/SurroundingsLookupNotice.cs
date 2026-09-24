namespace MSFSBlindAssist.Services;

/// <summary>How one spoken line of a surroundings lookup is delivered (<see cref="SurroundingsLookupNotice.Delivery"/>).</summary>
internal enum SurroundingsLookupDelivery
{
    /// <summary><c>AnnounceImmediate</c>: still the press's own moment.</summary>
    Immediate,
    /// <summary><c>Announce</c>: late enough that what is being spoken may be newer than the press.</summary>
    Queued,
}

/// <summary>
/// How a slow surroundings lookup (Alt+L, Ctrl+Shift+L) sounds. A cold first press can take seconds
/// (scenery scan, census, OSM wait, taxi graph) and a blind pilot has no spinner, so after
/// <see cref="Delay"/> a queued "Looking around." is spoken — only then, since on a cache hit it
/// would be noise in front of the answer.
/// </summary>
internal static class SurroundingsLookupNotice
{
    /// <summary>When the notice speaks, and from when a lookup line queues instead of interrupting.
    /// Under OnlineFeatureStore.CatalogWait so a lookup waiting on the mirror still gets it (pinned).</summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Within <see cref="Delay"/> of the press a line interrupts, as every hotkey answer does; later it
    /// is queued, because an interrupting answer 3-10 s late would cut off a taxi instruction given
    /// meanwhile. A suppressed announcer drops queued lines, so there it still interrupts.
    /// </summary>
    internal static SurroundingsLookupDelivery Delivery(TimeSpan sincePress, bool announcerSuppressed)
        => sincePress < Delay || announcerSuppressed ? SurroundingsLookupDelivery.Immediate : SurroundingsLookupDelivery.Queued;

    /// <summary>
    /// True when <paramref name="work"/> had not finished within <paramref name="delay"/>. Swallows the
    /// work's own failure: the caller awaits the same task next, inside its own catch.
    /// <c>WaitAsync</c>, not <c>WhenAny(work, Task.Delay(…))</c>, whose timer nothing cancels.
    /// </summary>
    internal static async Task<bool> IsSlowAsync(Task work, TimeSpan delay)
    {
        try { await work.WaitAsync(delay).ConfigureAwait(false); }
        catch (TimeoutException) { return true; }
        catch { /* the caller owns this failure */ }
        return false;
    }

    /// <summary>What is left of <see cref="Delay"/>, counted from the press; never negative.</summary>
    internal static TimeSpan NoticeWait(TimeSpan sincePress) => sincePress >= Delay ? TimeSpan.Zero : Delay - sincePress;
}
