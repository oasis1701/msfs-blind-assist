namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// What a pick on a guarded control should send: <see cref="Refused"/> when nothing safe can be sent
/// (the pilot hears "{label} unavailable"), else the calculator calls in order. <see cref="Calls"/> is
/// empty when the control already reads the pick and its guard is where the pick needs it; that is not
/// a refusal, and the pilot hears nothing.
/// </summary>
public sealed record Bbs757GuardedPlan(bool Refused, IReadOnlyList<string> Calls);

/// <summary>
/// The decisions around a guarded 757 control (the emergency lights, the stab trim cutouts), kept pure
/// so they are pinned without a sim. BlueBird's guard cover must be open before a click on its control
/// takes, and closing the guard writes the control's <c>CloseSets</c> position itself (emergency lights
/// ARMED, stab trim NORMAL). So:
/// <list type="bullet">
/// <item>The position BlueBird sets on closing is reached by closing the guard, never by leaving it
/// open; any other position needs the guard open first.</item>
/// <item>After closing, the app writes <c>CloseSets</c> itself as well. If BlueBird's close already
/// wrote it the write changes nothing; if a guard's click does not, the control still lands.</item>
/// <item>The guard is a toggle: its press flips it. It is pressed only after its state is read and
/// differs from the wish, and an unread guard or control presses and writes nothing.</item>
/// </list>
/// </summary>
public static class Bbs757Guarded
{
    /// <summary>
    /// The pause between two calls of one plan, in milliseconds: the guard's animation and click must
    /// land before the state write follows.
    /// </summary>
    public const int StepGapMs = 300;

    private static readonly Bbs757GuardedPlan Refusal = new(true, Array.Empty<string>());
    private static readonly Bbs757GuardedPlan Nothing = new(false, Array.Empty<string>());

    /// <summary>
    /// The calls that move <paramref name="control"/> from the delivered <paramref name="current"/>
    /// reading to <paramref name="target"/>, given the delivered <paramref name="guardState"/> reading
    /// of its <paramref name="guard"/>. Refused when either reading is missing or not a number, when the
    /// control has no state var, when <paramref name="target"/> is none of its positions, when it has no
    /// <see cref="Bbs757Control.GuardClosed"/> value, or when the guard cannot be toggled (no press, or
    /// no position other than closed to toggle to).
    /// </summary>
    public static Bbs757GuardedPlan Plan(Bbs757Control control, Bbs757Control guard,
        double? current, double? guardState, double target)
    {
        if (current is not double cur || !double.IsFinite(cur)) return Refusal;
        if (guardState is not double guardAt || !double.IsFinite(guardAt)) return Refusal;
        if (string.IsNullOrWhiteSpace(control.StateVar)) return Refusal;
        if (!double.IsFinite(target)) return Refusal;
        int to = Bbs757Rpn.PositionIndex(control, target);
        if (to < 0) return Refusal;
        if (control.GuardClosed is not double closed) return Refusal;
        if (string.IsNullOrWhiteSpace(guard.Press)) return Refusal;

        // The guard's open value: its first position that does not read closed. ToggleTo answers null
        // only for "already there" here, since the guard's press is checked above.
        Bbs757Position? open = guard.Positions.FirstOrDefault(p => Bbs757Rpn.ToggleTo(guard, p.Value, closed) != null);
        if (open == null) return Refusal;

        // The press that closes the guard; null when the guard already reads closed.
        string? closeGuard = Bbs757Rpn.ToggleTo(guard, guardAt, closed);
        bool guardOpen = closeGuard != null;

        bool targetIsCloseSets = control.CloseSets is double sets && Bbs757Rpn.PositionIndex(control, sets) == to;
        bool atTarget = Bbs757Rpn.PositionIndex(control, cur) == to;
        string write = Bbs757Rpn.SetLVar(control.StateVar, target);

        if (targetIsCloseSets)
        {
            if (guardOpen) return new Bbs757GuardedPlan(false, new[] { closeGuard!, write });
            return atTarget ? Nothing : new Bbs757GuardedPlan(false, new[] { write });
        }

        if (!guardOpen)
        {
            string? openGuard = Bbs757Rpn.ToggleTo(guard, guardAt, open.Value);
            return openGuard == null ? Refusal : new Bbs757GuardedPlan(false, new[] { openGuard, write });
        }

        return atTarget ? Nothing : new Bbs757GuardedPlan(false, new[] { write });
    }
}
