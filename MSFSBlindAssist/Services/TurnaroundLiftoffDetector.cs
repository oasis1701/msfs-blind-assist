namespace MSFSBlindAssist.Services;

/// <summary>
/// Detects the liftoff edge of a NEW flight in a same-session turnaround: a
/// touchdown edge followed by at least <see cref="MinGroundDwell"/> on the
/// ground, then a liftoff edge. Used by MainForm to re-baseline the
/// route-advisory proximity tracker per flight (spec 2026-07-14 post-note):
/// an advisory key that survives the turnaround in the AS feed would otherwise
/// keep flight 1's everInside latch and lose flight 2's 100 nm approach call.
/// Arming on the TOUCHDOWN edge (not mere ground dwell) means: the session's
/// first departure never fires (tracker is fresh anyway — and a long taxi-out
/// must not double-announce an already-approached advisory at rotation);
/// touch-and-goes and oleo-bounce flickers (dwell &lt; 5 min) never fire; and
/// MainForm's default _lastOnGround=true (which fabricates a liftoff edge when
/// a session starts airborne) is harmless — no touchdown was ever observed.
/// Pure: caller supplies the clock.
/// </summary>
internal sealed class TurnaroundLiftoffDetector
{
    public static readonly TimeSpan MinGroundDwell = TimeSpan.FromMinutes(5);

    private DateTime? _touchdownAtUtc;

    /// <summary>Feed every SIM_ON_GROUND edge sample; true = a turnaround
    /// liftoff — the caller should reset per-flight announcement state.</summary>
    public bool ObserveEdge(bool justTouchedDown, bool justLiftedOff, DateTime utcNow)
    {
        if (justTouchedDown)
        {
            _touchdownAtUtc = utcNow;
            return false;
        }
        if (!justLiftedOff) return false;
        bool turnaround = _touchdownAtUtc is { } down && utcNow - down >= MinGroundDwell;
        _touchdownAtUtc = null;    // a liftoff consumes the touchdown either way
        return turnaround;
    }

    /// <summary>Connect / aircraft switch: the proximity tracker is reset there
    /// anyway; a stale touchdown stamp must not fire a bonus reset later.</summary>
    public void Reset() => _touchdownAtUtc = null;
}
