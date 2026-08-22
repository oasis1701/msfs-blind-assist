namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The state-aware decision for whether a guarded control's cover must be lifted before the
/// control underneath can be actuated (the 29 guarded MD-11 controls: engine fire handles, cargo
/// smoke agents, fuel dump, battery, generator drives, oxygen masks, ditching, …).
///
/// This is pure logic so it can be unit-tested without a sim; the actual read/fire/settle lives in
/// the definition (<c>EnsureGuardOpenAsync</c>). The guiding rule is <b>never make it worse than
/// today</b>: if the guard's state cannot be read we <see cref="Action.LeaveAlone"/> it rather than
/// toggle blind — toggling a guard we cannot see could close an already-open one and break a
/// control that would otherwise have worked.
/// </summary>
public static class Md11Guard
{
    /// <summary>MSFS guard covers animate 0 = closed → 1 = open; at or above this counts as open.</summary>
    public const double OpenThreshold = 0.5;

    public enum Action
    {
        /// <summary>State unknown — do not touch the guard; let the actuation proceed as it would today.</summary>
        LeaveAlone,
        /// <summary>Already open — actuate without toggling (so a second press can't re-close it).</summary>
        AlreadyOpen,
        /// <summary>Closed — lift the cover, then actuate.</summary>
        Open,
    }

    /// <summary>
    /// Decides what to do given the guard's read-back state (null = unreadable).
    /// </summary>
    public static Action Decide(double? state, double threshold = OpenThreshold)
    {
        if (state == null) return Action.LeaveAlone;
        return state.Value >= threshold ? Action.AlreadyOpen : Action.Open;
    }
}
