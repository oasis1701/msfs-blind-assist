namespace MSFSBlindAssist.Services;

/// <summary>
/// The door and cargo lines the pilot has just pressed in the Cabin window, so each change is said once
/// (ruling R18, CORE-7): the window reads its own press back, and while that read-back waits the app-wide door
/// announcement (<see cref="FenixCabinReadyMonitor"/>) leaves that door alone. A line is keyed as the Doors page
/// keys it (<c>door.d1l</c>, <c>cargo.forward</c>, the <see cref="FenixCabinEvent.LineKey"/> of a door event).
/// </summary>
/// <remarks>
/// <para>The window marks a line as its write goes and keeps the mark for the read-back's whole window, also
/// once the read-back has spoken: the announcement's own poll can see the change a second later than the
/// window's, and would then say it again. A press the Fenix refuses, or whose window closes before its answer
/// comes, withdraws its mark, so the change, if any, is still said once.</para>
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
