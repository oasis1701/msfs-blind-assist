namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// When the A300's light changes are spoken ([A300-24]): as one sentence (<see cref="A300LampCallouts"/>)
/// at the first batch end once they are due, netted per light, so a light that went on and back off since
/// the last sentence says nothing.
///
/// The light power flags and the fault flags reach MSFSBA on separate once-a-second subscriptions, so the
/// aircraft's own order is lost, both ways (measured 2026-10-09): on external power on, the cabin regulator 2
/// fault cleared 500 ms BEFORE the AC light power rose and MSFSBA heard the power first; on external power
/// off, the rudder travel limiter 2 fault arrived a second before the power flag. Either way MSFSBA said a
/// light on that the cockpit never lit, then off. So a light's first change since the last sentence waits
/// <see cref="GatherMs"/> from its own arrival (one subscription period, for a power flag still on its way;
/// a further change of a light already waiting adds none, or a light flashing through a power settle would
/// keep the sentence back as long as it flashed), and a power change holds everything <see cref="SettleMs"/>.
/// Like the fleet, a power change still speaks every light it lights or darkens, once, when the power has
/// settled. A change the definition does not keep still tells this class to <see cref="Forget"/> the light,
/// so an earlier opposite change is not spoken stale.
///
/// A light going dark is spoken only once it has stayed dark <see cref="OffHoldMs"/>, the iFly 737's flash
/// filter (design decision, 2026-10-10): a light that lights again inside the hold says nothing, so a flashing light
/// (the autoland warning light) reads as one "on" at its first batch end, and "off" once it stops. An off
/// while a power change settles is not held: it nets in the power's own sentence. Pure.
/// </summary>
public sealed class A300LampSpeech
{
    /// <summary>Two subscription periods and a batch end: long enough for a fault that changed with
    /// the power to arrive after it.</summary>
    public const int SettleMs = 2500;

    /// <summary>How long a light's first change since the last sentence waits from its own arrival: one
    /// subscription period and a little, so a power flag arriving just after it still nets with it.</summary>
    public const int GatherMs = 1500;

    /// <summary>How long a light must stay dark before its "off" is spoken: longer than a flash's dark
    /// phase and two subscription periods (design decision, 2026-10-10: about 3 s).</summary>
    public const int OffHoldMs = 3000;

    private readonly List<A300LampChange> _pending = new();
    private readonly Dictionary<string, long> _heldOff = new(StringComparer.Ordinal);
    private long _holdUntil = long.MinValue;

    /// <summary>A power change's sentence is still to come: until it is spoken, an off nets in it unheld.</summary>
    private bool _powerSettling;

    /// <summary>Takes a change; one of a light with none pending extends the gather period to its own
    /// <see cref="GatherMs"/>. An off is held (<see cref="OffHoldMs"/>), and a light lighting again while its off is
    /// held is dropped.</summary>
    public void Add(A300LampChange change, long now)
    {
        if (change.On && _heldOff.Remove(change.Name))
            return;
        if (!change.On && !_powerSettling)
        {
            _heldOff[change.Name] = now;
            return;
        }
        Queue(change, now);
    }

    /// <summary>A bus's light power changed: hold the pending changes until it settles.</summary>
    public void NotePowerChange(long now)
    {
        _holdUntil = Math.Max(_holdUntil, now + SettleMs);
        _powerSettling = true;
    }

    /// <summary>The sentence to speak now, or null: nothing pending, still gathering or settling, or it all
    /// netted out. Clears what it returns.</summary>
    public string? Flush(long now)
    {
        // A held off has waited already: it joins without a gather period of its own.
        foreach (var (name, _) in _heldOff.Where(h => now - h.Value >= OffHoldMs).OrderBy(h => h.Value).ToList())
        {
            _heldOff.Remove(name);
            _pending.Add(new A300LampChange(name, false));
        }
        if (now < _holdUntil)
            return null;
        _powerSettling = false;   // its sentence, if any, is this one
        if (_pending.Count == 0)
            return null;
        var net = Net(_pending);
        _pending.Clear();
        return net.Count == 0 ? null : A300LampCallouts.Compose(net);
    }

    /// <summary>
    /// A change the definition does not keep (muted, a just-pressed button's own light, suppressed) leaves the
    /// light's earlier change, still held or pending, wrong: that light is not where the change left it. Spoken
    /// later it would be false ("Engine page light off" while it is lit) or, netted with the later change, would
    /// flip the parity and silence a light that really changed. So the light's held off and every pending change
    /// of it are dropped; other lights are untouched.
    /// </summary>
    public void Forget(string name)
    {
        _heldOff.Remove(name);
        _pending.RemoveAll(change => string.Equals(change.Name, name, StringComparison.Ordinal));
    }

    public void Clear()
    {
        _pending.Clear();
        _heldOff.Clear();
        _holdUntil = long.MinValue;
        _powerSettling = false;
    }

    private void Queue(A300LampChange change, long now)
    {
        // A light's first change since the last sentence has its own gather period, not only the very first change:
        // a power flag or a fault still on its way after it must find it unspoken to net with it. A light that
        // already has a change pending adds none: while a power change settles an off is queued, not held, so a light
        // flashing through the settle would queue a change every few hundred milliseconds and, with each one moving
        // the hold on, keep the sentence back for as long as it flashed (the autoland light: 22 s instead of 3 s).
        if (!_pending.Exists(p => string.Equals(p.Name, change.Name, StringComparison.Ordinal)))
            _holdUntil = Math.Max(_holdUntil, now + GatherMs);
        _pending.Add(change);
    }

    /// <summary>Each light's last change, kept only when the light changed an odd number of times (it ends
    /// where it did not start), in the order of those last changes.</summary>
    public static IReadOnlyList<A300LampChange> Net(IReadOnlyList<A300LampChange> changes)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < changes.Count; i++)
        {
            count[changes[i].Name] = count.GetValueOrDefault(changes[i].Name) + 1;
            last[changes[i].Name] = i;
        }
        return last.Where(l => count[l.Key] % 2 == 1).OrderBy(l => l.Value).Select(l => changes[l.Value]).ToList();
    }
}
