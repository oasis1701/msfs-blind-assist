namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// Remembers the position MSFSBA just commanded for each control, so the next decision about that
/// control uses it instead of the SimConnect cache. The cache is fed by the 1 Hz continuous batch,
/// so for up to a second after a write it still holds the old position; two picks in that window
/// would both be planned from the stale value, and for a toggle (whose Set FLIPS the switch) the
/// second pick would flip it the wrong way. Same idea as the A380's commanded values. Pure: the
/// caller passes the clock.
///
/// The key is the STATE VARIABLE the control flips, not the row that fired the pick
/// (<c>IniA300Definition.CommandKey</c>): rows with their own Set events can flip one variable (the
/// courier and crew oxygen supply), and a hold kept per row left the second row planning from its own
/// stale cache.
/// </summary>
public sealed class A300CommandedState
{
    /// <summary>Long enough for the write to land and the next batch to deliver it.</summary>
    public const long HoldMs = 2500;

    private readonly Dictionary<string, (double Value, long AtMs)> _commanded = new(StringComparer.Ordinal);

    public void Record(string key, double value, long nowMs) => _commanded[key] = (value, nowMs);

    /// <summary>The commanded value while it is fresh, otherwise <paramref name="cached"/>.</summary>
    public double? Resolve(string key, double? cached, long nowMs) =>
        _commanded.TryGetValue(key, out var c) && nowMs - c.AtMs <= HoldMs ? c.Value : cached;

    public void Clear() => _commanded.Clear();
}
