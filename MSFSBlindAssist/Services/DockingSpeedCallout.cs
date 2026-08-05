namespace MSFSBlindAssist.Services;

/// <summary>
/// One-knot ground-speed callouts for the final gate approach.
///
/// <para><b>Why the global announcer isn't enough.</b>
/// <see cref="GroundSpeedAnnouncer"/> speaks in 5- or 10-knot buckets, so across the
/// entire docking band (0-5 kt) it says almost nothing. Yet that band is precisely
/// where fine speed control decides the park: a widebody arriving at 4-5 kt instead
/// of 1 kt covers the last 8 m in under four seconds and cannot complete the squaring
/// turn, which is a failure mode measured repeatedly in live A380 docking logs.
/// A blind pilot can't poll speed by hotkey with one hand on the tiller and one on
/// the thrust levers, so the number has to come to them.</para>
///
/// <para>Pure and stateful-but-isolated: no announcer, no sim, no settings — it just
/// decides WHAT should be said and WHEN, so the cadence is unit-testable and the
/// caller owns the speech.</para>
/// </summary>
public sealed class DockingSpeedCallout
{
    /// <summary>
    /// The speed must differ from the last announced value by at least this much before
    /// a new callout fires. Kills "1 / 2 / 1 / 2" flutter when the aircraft sits on a
    /// rounding boundary — the same trick <see cref="GroundSpeedAnnouncer"/> uses,
    /// tightened for the 1-knot step.
    ///
    /// <para>0.6 rather than 0.5 is what makes a speed parked ON the boundary silent: at
    /// exactly N.5 the sample rounds up but is only 0.5 from the last announced N, so
    /// nothing is said — at 0.5 it would round up AND clear the deadband, and an
    /// infinitesimal wobble would speak forever. What remains is a residual flutter band
    /// **0.2 kt wide**: from an announced N it takes N+0.6 to say N+1, and from N+1 it
    /// takes N+0.4 to come back, so a speed genuinely swinging between N+0.4 and N+0.6
    /// still speaks twice. That is a jitter-sized guard, not immunity. Widen it only on
    /// live evidence of chatter, and never past ~1.0, where a real one-knot change would
    /// go unspoken.</para>
    /// </summary>
    public const double HysteresisKts = 0.6;

    /// <summary>-1 = nothing announced yet this approach; the first sample always speaks.</summary>
    private int _lastAnnounced = -1;

    /// <summary>
    /// Re-arm with NOTHING known, so the next sample speaks whatever it is. This is the
    /// full-reset path (gate lost, docking disabled mid-approach, taxi-away) where no
    /// other docking speech is in flight. Do NOT call it on engage — see <see cref="Arm"/>.
    /// </summary>
    public void Reset() => _lastAnnounced = -1;

    /// <summary>
    /// Arm for a fresh approach at a KNOWN speed: primes the state SILENTLY so the first
    /// callout is the first genuine CHANGE, not the frame after engage.
    ///
    /// <para>This exists because the engage frame is the one frame docking is already
    /// talking. <c>EngageLocked</c> speaks a multi-second callout carrying the VDGS type,
    /// the distance to stop, the initial steering demand and the jetway/door side — and
    /// the position feed is <c>SIM_FRAME</c>, so the very next sample lands 16-33 ms
    /// later. Re-arming to "nothing known" there made that next sample speak a number on
    /// top of a callout the pilot had heard one syllable of. Speed is worth a lot on the
    /// final approach; it is not worth the engage callout.</para>
    /// </summary>
    public void Arm(double groundSpeedKts)
        => _lastAnnounced = IsUsable(groundSpeedKts) ? Round(groundSpeedKts) : -1;

    /// <summary>
    /// Feed a ground-speed sample (knots). Returns the phrase to speak, or null when
    /// nothing should be said. Negative, NaN and infinite samples are ignored rather than
    /// announced — infinity matters because the int conversion below saturates to
    /// <see cref="int.MinValue"/> in an unchecked context, which would read as "Stopped."
    /// </summary>
    public string? Update(double groundSpeedKts)
    {
        if (!IsUsable(groundSpeedKts)) return null;

        int rounded = Round(groundSpeedKts);

        if (_lastAnnounced < 0)
        {
            // First sample of the approach — speak it, so the pilot starts with a number.
            _lastAnnounced = rounded;
            return Phrase(rounded);
        }

        if (rounded == _lastAnnounced) return null;
        if (Math.Abs(groundSpeedKts - _lastAnnounced) < HysteresisKts) return null;

        _lastAnnounced = rounded;
        return Phrase(rounded);
    }

    /// <summary>A sample we can round and compare: finite and not negative.</summary>
    private static bool IsUsable(double groundSpeedKts)
        => double.IsFinite(groundSpeedKts) && groundSpeedKts >= 0;

    private static int Round(double groundSpeedKts)
        => (int)Math.Round(groundSpeedKts, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Terse by design: the docking tone and proximity beeper are already sounding, so a
    /// callout every knot must be as short as possible. "Stopped" rather than "zero knots"
    /// because standing still is a state the pilot acts on, not a measurement.
    /// </summary>
    private static string Phrase(int knots)
        => knots <= 0 ? "Stopped." : knots == 1 ? "1 knot." : $"{knots} knots.";
}
