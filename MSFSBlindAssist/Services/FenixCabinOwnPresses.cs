namespace MSFSBlindAssist.Services;

/// <summary>
/// The door and cargo lines the pilot has just pressed in the Cabin window, so each change is said once
/// (CORE-7): the window reads its own press back, and while that read-back waits the app-wide door
/// announcement (<see cref="FenixCabinReadyMonitor"/>) leaves that door alone. A line is keyed as the Doors page
/// keys it (<c>door.d1l</c>, <c>cargo.forward</c>, the <see cref="FenixCabinEvent.LineKey"/> of a door event).
/// </summary>
/// <remarks>
/// <para>The mark lives exactly as long as the window's read-back can still speak:
/// the window marks a line as its write goes; once the read-back has spoken it holds the line
/// for a short grace only (the announcement's own poll can see the change a poll later than the window's, and
/// would then say it again); a read-back that ends silently, a press the Fenix refuses, a send path that
/// throws, or the window closing withdraws the mark at once, so the change, if any, is still said once.</para>
/// <para>Thread-safe: the window marks on the UI thread, the announcement asks on a pool thread. Both pass
/// <see cref="DateTime.UtcNow"/>; the tests pass their own clock.</para>
/// </remarks>
public sealed class FenixCabinOwnPresses
{
    /// <summary>The one registry the Cabin window and the app-wide announcements share.</summary>
    public static FenixCabinOwnPresses Shared { get; } = new();

    private readonly object _gate = new();
    // The end of each line's mark: pending while now is before it. Never pruned: there are at most a dozen
    // door and cargo lines, and a query then has no side effect.
    private readonly Dictionary<string, DateTime> _ends = new(StringComparer.Ordinal);

    /// <summary>Marks the line as the pilot's own press until <paramref name="now"/> + <paramref name="window"/>,
    /// replacing an earlier mark of it. Returns that end, which <see cref="Withdraw"/> takes.</summary>
    public DateTime Mark(string lineKey, DateTime now, TimeSpan window)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineKey);
        DateTime end = now + window;
        lock (_gate) _ends[lineKey] = end;
        return end;
    }

    /// <summary>Ends the mark that ended at <paramref name="end"/> (a press the Fenix did not take); a later
    /// press's mark of the same line stays.</summary>
    public void Withdraw(string lineKey, DateTime end)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineKey);
        lock (_gate)
        {
            if (_ends.TryGetValue(lineKey, out DateTime current) && current == end)
                _ends.Remove(lineKey);
        }
    }

    /// <summary>True while the line's mark lasts: from the press until just before its end.</summary>
    public bool IsPending(string lineKey, DateTime now)
    {
        lock (_gate)
            return _ends.TryGetValue(lineKey, out DateTime end) && now < end;
    }
}
