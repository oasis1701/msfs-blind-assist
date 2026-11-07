namespace VirtualPilot.Approach;

/// <summary>
/// A pilot obeying ATC: from each turn's time on, the pilot ignores the tones and turns to the heading
/// course + <c>ToRelHeadingDeg</c> (relative to the centreline's direction abeam the aircraft) at a
/// bank of twice the heading error, at most 20°, and once within 1° of it the turn is over: the pilot
/// rolls level and follows the tones again. Turns run one at a time, in their order; a turn whose
/// time comes while an earlier one is still under way waits for it.
/// </summary>
sealed class ApproachScriptedTurns
{
    public const double MaxBankDeg = 20.0, BankPerDegError = 2.0, DoneWithinDeg = 1.0;

    private readonly IReadOnlyList<(double AtS, double ToRelHeadingDeg)> _turns;
    private int _next;

    public ApproachScriptedTurns(IReadOnlyList<(double AtS, double ToRelHeadingDeg)> turns) { _turns = turns; }

    /// <summary>
    /// The bank (right positive) the pilot flies at <paramref name="t"/> with the aircraft heading
    /// <paramref name="headingRelDeg"/> from the course, or null when no turn is under way.
    /// </summary>
    public double? BankFor(double t, double headingRelDeg)
    {
        if (_next >= _turns.Count || t < _turns[_next].AtS) return null;
        double error = ((_turns[_next].ToRelHeadingDeg - headingRelDeg) % 360 + 540) % 360 - 180;
        if (Math.Abs(error) <= DoneWithinDeg)
        {
            _next++;
            return null;
        }
        return Math.Clamp(BankPerDegError * error, -MaxBankDeg, MaxBankDeg);
    }
}
