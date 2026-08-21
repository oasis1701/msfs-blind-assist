namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Converts the stock <c>ELEVATOR TRIM POSITION</c> (degrees) into the PMDG 777's stabiliser
/// trim UNITS — the scale the cockpit indicator carries and the FMC TAKEOFF page quotes.
///
/// <para>
/// Degrees are the wrong currency for this aeroplane. The FMC asks for a trim setting in units
/// and the control-stand indicator is marked in units, so a pilot handed degrees has to convert
/// in their head during the takeoff setup — and getting it wrong is an over-rotation. The 737
/// never had this problem because PMDG publishes its trim as an L-var already in units
/// (<c>ElevTrimTT</c>); the 777 exposes no such field, so the conversion has to be done here.
/// </para>
/// </summary>
public static class Pmdg777StabTrim
{
    /// <summary>
    /// Units = degrees + this. MEASURED, not derived: the pilot ran the stabiliser to both stops
    /// and read the stock SimVar at each end — −3.75° at full nose-down, +10.75° at full nose-up
    /// — which maps to a 0.00–14.50 unit scale, and independently validated the offset by flying
    /// takeoffs at (FMC units − 3.75) until the over-rotation stopped.
    ///
    /// <para>
    /// PMDG's own <c>flight_model.cfg</c> (<c>elevator_trim_limit = 11.0 // 4/11 Code controlled</c>)
    /// fixes only the TRAVEL — a code-controlled −4° to +11° — and cannot settle the offset: that
    /// range fits 3.75 (stops a quarter degree inside each end) and the rival 4.0 (a clean 0–15
    /// scale) equally well. Note that 4 is DEGREES, not units; conflating the two currencies is
    /// what made this hard to pin down in the first place. What discriminates is the two stop
    /// readings above plus the indicator itself, read at 3.75, 4.00, 4.50 and 6.00 units and
    /// agreeing with the announced value at every one — 3.75 and 4.50 carry the weight, because
    /// both fall BETWEEN whole graduations, and an offset of 4.0 would have shown 4.00 against a
    /// gauge reading 4.25. That read is an enlarged screenshot interpreted by image analysis:
    /// strong corroboration, not an instrument calibration. Should better evidence ever
    /// contradict it, this is the ONE constant to change (the stop tests pin it, so they follow).
    /// </para>
    /// </summary>
    public const double UnitsOffset = 3.75;

    /// <summary>Announcement granularity: the indicator is graduated in quarter units.</summary>
    public const double UnitsStep = 0.25;

    /// <summary>
    /// Stabiliser trim in units, snapped to the nearest <see cref="UnitsStep"/> — the spoken
    /// value always lands on a graduation, and any degree change that crosses a quarter-unit
    /// boundary is a new value, however small (there is no deadband). Clamped at the bottom: a
    /// reading a hair under −3.75° rounds to −0.0, and one past the nose-down stop would otherwise
    /// speak a negative trim on a scale that has no negative end. The top is deliberately NOT
    /// clamped — a reading past 14.50 is the one signal that PMDG's stop, or this offset, has moved.
    /// </summary>
    public static double UnitsFromDegrees(double degrees)
    {
        double units = Math.Round((degrees + UnitsOffset) / UnitsStep,
                                  MidpointRounding.AwayFromZero) * UnitsStep;
        return Math.Max(0.0, units); // +0.0 beats -0.0 here (IEEE 754 maximum), so "-0.00" cannot escape
    }

    /// <summary>
    /// The spoken phrase. No "up"/"down": the sign is already in the number on a 0–14.5 scale,
    /// and a direction word invites the pilot to hear it as a relative change rather than the
    /// absolute position the FMC and the indicator both state. Invariant culture, like every other
    /// tested spoken-number formatter in this app — "5,25" is a different number through a screen
    /// reader. (The PMDG 737's own callout is "Trim 5.3": one decimal for its 0.1-unit step and no
    /// unit word; the 777 carries two decimals for its quarter-unit step and the word "units" — a
    /// per-type wording, not a fleet convention.)
    /// </summary>
    public static string Describe(double degrees) => DescribeUnits(UnitsFromDegrees(degrees));

    /// <summary>Formats an already-snapped units value, so a caller keys its debounce on the very value it speaks.</summary>
    public static string DescribeUnits(double units)
        => FormattableString.Invariant($"Trim {units:F2} units");
}
