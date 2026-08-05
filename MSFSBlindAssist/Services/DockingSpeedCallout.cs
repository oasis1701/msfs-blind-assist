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
    /// boundary — the same trick <see cref="GroundSpeedAnnouncer"/> uses, tightened for
    /// the 1-knot step. 0.6 (not 0.5) makes the deadband asymmetric about the rounding
    /// boundary, so a speed parked exactly on x.5 cannot oscillate.
    /// </summary>
    public const double HysteresisKts = 0.6;

    /// <summary>-1 = nothing announced yet this approach; the first sample always speaks.</summary>
    private int _lastAnnounced = -1;

    /// <summary>Re-arm for a fresh approach (engage, re-engage, or a retry after backing up).</summary>
    public void Reset() => _lastAnnounced = -1;

    /// <summary>
    /// Feed a ground-speed sample (knots). Returns the phrase to speak, or null when
    /// nothing should be said. Negative/NaN samples are ignored rather than announced.
    /// </summary>
    public string? Update(double groundSpeedKts)
    {
        if (double.IsNaN(groundSpeedKts) || groundSpeedKts < 0) return null;

        int rounded = (int)Math.Round(groundSpeedKts, MidpointRounding.AwayFromZero);

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

    /// <summary>
    /// Terse by design: the docking tone and proximity beeper are already sounding, so a
    /// callout every knot must be as short as possible. "Stopped" rather than "zero knots"
    /// because standing still is a state the pilot acts on, not a measurement.
    /// </summary>
    internal static string Phrase(int knots)
        => knots <= 0 ? "Stopped." : knots == 1 ? "1 knot." : $"{knots} knots.";
}
