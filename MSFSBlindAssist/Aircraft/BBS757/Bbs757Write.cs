namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>What a panel pick should do to the 757.</summary>
public enum Bbs757WriteOutcome
{
    /// <summary>The control already reads the picked position: nothing is sent.</summary>
    AtPosition,

    /// <summary>Send <see cref="Bbs757WritePlan.Calls"/>, one calculator call each, in order.</summary>
    Writes,

    /// <summary>Nothing safe can be sent: the pilot is told "{label} unavailable".</summary>
    Refused,
}

/// <summary>A pick's outcome and the calculator calls that carry it out (empty unless <see cref="Bbs757WriteOutcome.Writes"/>).</summary>
public sealed record Bbs757WritePlan(Bbs757WriteOutcome Outcome, IReadOnlyList<string> Calls)
{
    public static readonly Bbs757WritePlan AtPosition = new(Bbs757WriteOutcome.AtPosition, Array.Empty<string>());
    public static readonly Bbs757WritePlan Refused = new(Bbs757WriteOutcome.Refused, Array.Empty<string>());
    public static Bbs757WritePlan Of(IReadOnlyList<string> calls) => new(Bbs757WriteOutcome.Writes, calls);
}

/// <summary>A momentary press: the down code, the up code (null when the control has none) and the hold between them.</summary>
public sealed record Bbs757Press(string Down, string? Up, int HoldMs);

/// <summary>
/// The decisions around <see cref="Bbs757Rpn"/> that every 757 panel write makes, kept pure so they
/// are pinned without a sim:
/// <list type="bullet">
/// <item>An unread, NaN or unknown reading never presses. A toggle's press FLIPS it, so pressing on
/// a reading that is not one of its positions could land anywhere; the pilot hears "unavailable"
/// instead and nothing is sent.</item>
/// <item>A target that is not one of the control's positions is refused the same way.</item>
/// <item>Toggles, guards and selects replay their press once (<see cref="Bbs757Rpn.ToggleTo"/>);
/// switches step by position index (<see cref="Bbs757Rpn.StepTo"/>).</item>
/// <item>A control with no click code at all (some switches have no press, cw or ccw) is written
/// through its state var, an absolute write (<see cref="Bbs757Rpn.SetLVar"/>), so any delivered
/// reading will do (ruling R8 of plan 01).</item>
/// </list>
/// </summary>
public static class Bbs757Write
{
    /// <summary>How long a momentary button is held between its press and release code.</summary>
    public const int ButtonHoldMs = 200;

    /// <summary>The hold for a held button whose template named no time (the generator's own default).</summary>
    public const int DefaultHeldMs = 1500;

    /// <summary>
    /// How long after an unlock press a fire handle relocks. BlueBird's unlock click sets the handle's
    /// <c>L:BBS757_FIREUNLOCKn</c> and starts an <c>O:</c> timer whose update loop clears it again 1.5 s
    /// later (BBS_752_BASE_INTERIOR.xml, the fire handle unlock components). The calculator the app
    /// presses through cannot start an <c>O:</c> timer, so no loop ever runs and the handle would stay
    /// unlocked forever (measured 2026-10-26): the app sends the relock itself, this long after the press.
    /// </summary>
    public const int FireHandleRelockMs = 1500;

    /// <summary>The state vars of the engine 1, engine 2 and APU fire handle unlock buttons.</summary>
    private static readonly HashSet<string> FireUnlockStateVars = new(StringComparer.Ordinal)
    {
        "BBS757_FIREUNLOCK1", "BBS757_FIREUNLOCK2", "BBS757_FIREUNLOCK3",
    };

    /// <summary>True for the push kinds a panel shows as a button: a momentary or a held button.</summary>
    public static bool IsMomentary(Bbs757Control c) => c.Kind is Bbs757Kinds.Button or Bbs757Kinds.Held;

    /// <summary>
    /// True when the panel can show this control's state: a state var AND at least two named
    /// positions. The MCP buttons pulse a <c>_PRESS</c> var, which is a state var but no position a
    /// pilot would name, so a press of one is confirmed with "{label} pressed" (CORE-7).
    /// </summary>
    public static bool HasReadableState(Bbs757Control c) =>
        !string.IsNullOrWhiteSpace(c.StateVar) && c.Positions.Count >= 2;

    /// <summary>
    /// The calls that move a positional control (toggle, guard, select, switch) from the delivered
    /// <paramref name="current"/> reading to <paramref name="target"/>; null means nothing was
    /// delivered.
    /// </summary>
    public static Bbs757WritePlan ToPosition(Bbs757Control c, double? current, double target)
    {
        if (current is not double cur || !double.IsFinite(cur) || !double.IsFinite(target))
            return Bbs757WritePlan.Refused;
        int to = Bbs757Rpn.PositionIndex(c, target);
        if (to < 0) return Bbs757WritePlan.Refused;
        int from = Bbs757Rpn.PositionIndex(c, cur);

        bool canClick = c.Kind == Bbs757Kinds.Switch
            ? !string.IsNullOrWhiteSpace(c.Press) || (!string.IsNullOrWhiteSpace(c.Cw) && !string.IsNullOrWhiteSpace(c.Ccw))
            : !string.IsNullOrWhiteSpace(c.Press);   // checked first: ToggleTo answers null for a missing press too

        if (!canClick)
        {
            if (string.IsNullOrWhiteSpace(c.StateVar)) return Bbs757WritePlan.Refused;
            if (from == to) return Bbs757WritePlan.AtPosition;
            return Bbs757WritePlan.Of(new[] { Bbs757Rpn.SetLVar(c.StateVar, c.Positions[to].Value) });
        }

        if (from < 0) return Bbs757WritePlan.Refused;   // a reading off the positions never steps
        if (from == to) return Bbs757WritePlan.AtPosition;

        if (c.Kind == Bbs757Kinds.Switch)
        {
            var steps = Bbs757Rpn.StepTo(c, cur, target);
            return steps.Count > 0 ? Bbs757WritePlan.Of(steps) : Bbs757WritePlan.Refused;
        }

        string? press = Bbs757Rpn.ToggleTo(c, cur, target);
        return press != null ? Bbs757WritePlan.Of(new[] { press }) : Bbs757WritePlan.Refused;
    }

    /// <summary>The press a button or held button replays, or null when it has no press code.</summary>
    public static Bbs757Press? PressFor(Bbs757Control c)
    {
        if (string.IsNullOrWhiteSpace(c.Press)) return null;
        int hold = c.Kind == Bbs757Kinds.Held
            ? (c.HoldMs > 0 ? c.HoldMs : DefaultHeldMs)
            : ButtonHoldMs;
        return new Bbs757Press(c.Press, string.IsNullOrWhiteSpace(c.Release) ? null : c.Release, hold);
    }

    /// <summary>
    /// The write that locks a fire handle again after the app pressed its unlock button, and in
    /// <paramref name="delayMs"/> how long after the press to send it (<see cref="FireHandleRelockMs"/>);
    /// null, and 0, for every other control. The three unlock buttons (engine 1, engine 2, APU) are
    /// known by their state var, <c>BBS757_FIREUNLOCK1</c> to <c>3</c> (the APU's node is
    /// <c>B757_FIREUNLOCK3</c>), and only as momentary buttons: a toggle or switch on the same var
    /// would be flipped by a relock the pilot never asked for. The write is the same unconditional
    /// 0 the cockpit's own timer makes (<see cref="Bbs757Rpn.SetLVar"/>).
    /// </summary>
    public static string? RelockAfterUnlock(Bbs757Control c, out int delayMs)
    {
        delayMs = 0;
        if (!IsMomentary(c) || string.IsNullOrWhiteSpace(c.StateVar) || !FireUnlockStateVars.Contains(c.StateVar))
            return null;
        delayMs = FireHandleRelockMs;
        return Bbs757Rpn.SetLVar(c.StateVar, 0);
    }
}
