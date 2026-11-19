using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// Pure builders for the RPN strings the 757 is driven with. The vendor's click code is replayed
/// verbatim from the map; what is built here is only the arithmetic around it: whether a press is
/// owed, how many steps a move takes, and how a long burst is split into calls the calculator
/// accepts. Every number is formatted with the invariant culture, since a comma decimal makes the
/// RPN parser reject the string.
/// </summary>
public static class Bbs757Rpn
{
    /// <summary>Most click steps joined into one calculator call.</summary>
    public const int MaxStepsPerCall = 20;

    /// <summary>Most K-event writes joined into one calculator call.</summary>
    public const int MaxKeysPerCall = 25;

    /// <summary>
    /// The longest joined call, in characters. MobiFlight's command buffer is 1024
    /// (<c>MobiFlightWasmModule.MAX_COMMAND_SIZE</c>) and cuts a longer command off silently; the
    /// unique prefix and MobiFlight's own command name take a few dozen of it. A burst is split by
    /// length as well as by count, so a long vendor step never pushes a call past this.
    /// </summary>
    public const int MaxCallChars = 900;

    /// <summary>Sim values read back as doubles are compared with this slack, never with ==.</summary>
    private const double ValueTolerance = 0.001;

    /// <summary>
    /// The control's press code when it is not already at <paramref name="target"/>, else null. A
    /// toggle's press flips it, so one press is owed for any difference.
    /// </summary>
    public static string? ToggleTo(Bbs757Control c, double current, double target)
        => Same(current, target) ? null : c.Press;

    /// <summary>
    /// The calls that move <paramref name="c"/> from <paramref name="current"/> to
    /// <paramref name="target"/>, each at most <see cref="MaxStepsPerCall"/> steps. A control with
    /// <c>Cw</c> and <c>Ccw</c> takes one step per position, up or down; a control with only
    /// <c>Press</c> cycles forward through its positions, wrapping. Steps count position INDICES, not
    /// raw values (some switches use 0, 4, 7). Empty when already there, when either value is not one
    /// of the positions, or when the control has no way to step.
    /// </summary>
    public static IReadOnlyList<string> StepTo(Bbs757Control c, double current, double target)
    {
        int from = PositionIndex(c, current);
        int to = PositionIndex(c, target);
        if (from < 0 || to < 0 || from == to) return Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(c.Cw) && !string.IsNullOrWhiteSpace(c.Ccw))
            return Burst(to > from ? c.Cw : c.Ccw, Math.Abs(to - from), MaxStepsPerCall);

        if (!string.IsNullOrWhiteSpace(c.Press))
        {
            int n = c.Positions.Count;
            return Burst(c.Press, ((to - from) % n + n) % n, MaxStepsPerCall);
        }

        return Array.Empty<string>();
    }

    /// <summary>Sets an L:var: <c>"1.5 (&gt;L:NAME)"</c>, at most six decimals, invariant.</summary>
    public static string SetLVar(string lvar, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lvar);
        return $"{value.ToString("0.######", CultureInfo.InvariantCulture)} (>L:{lvar})";
    }

    /// <summary>
    /// <paramref name="count"/> writes of the K-event <paramref name="kEvent"/>, joined into calls of
    /// at most <see cref="MaxKeysPerCall"/>. Empty for a count of zero or less.
    /// </summary>
    public static IReadOnlyList<string> KeyBurst(string kEvent, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kEvent);
        return Burst($"(>K:{kEvent})", count, MaxKeysPerCall);
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < ValueTolerance;

    /// <summary>
    /// The index of the position holding <paramref name="value"/> (within the read-back slack), or
    /// -1 when the value is none of the control's positions.
    /// </summary>
    public static int PositionIndex(Bbs757Control c, double value)
    {
        for (int i = 0; i < c.Positions.Count; i++)
            if (Same(c.Positions[i].Value, value)) return i;
        return -1;
    }

    /// <summary>
    /// <paramref name="count"/> copies of <paramref name="code"/>, space-joined in calls of at most
    /// <paramref name="perCall"/> copies and shorter than <see cref="MaxCallChars"/>. A single copy
    /// longer than that still goes, alone: a vendor click is never cut.
    /// </summary>
    private static IReadOnlyList<string> Burst(string code, int count, int perCall)
    {
        // n copies joined take n * (length + 1) - 1 characters: n <= MaxCallChars / (length + 1) keeps them under the cap.
        int fit = Math.Max(1, MaxCallChars / (code.Length + 1));
        int per = Math.Min(perCall, fit);
        var calls = new List<string>();
        for (int left = count; left > 0; left -= per)
            calls.Add(string.Join(" ", Enumerable.Repeat(code, Math.Min(left, per))));
        return calls;
    }
}
