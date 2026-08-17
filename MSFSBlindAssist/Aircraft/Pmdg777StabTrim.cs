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
    /// PMDG's own <c>flight_model.cfg</c> corroborates the RANGE — <c>elevator_trim_limit = 11.0
    /// // 4/11 Code controlled</c>, i.e. the stabiliser travels −4° to +11°, with the measured
    /// stops sitting a quarter degree inside each end exactly as "code controlled" implies. Note
    /// that 4 is DEGREES, not units; the two numbers are in different currencies and conflating
    /// them is what made this hard to pin down in the first place.
    /// </para>
    /// <para>
    /// A quarter-unit ambiguity was open for a while — 3.75 against the 4 in PMDG's comment,
    /// which would make every announcement a quarter unit light — and it has since been CLOSED
    /// against the indicator itself: the gauge was read at four settings spanning the useful
    /// range (3.75, 4.00, 4.50, 6.00) and agreed with the announced units at every one. 3.75 and
    /// 4.50 carry the weight, because both fall BETWEEN whole graduations, and an offset of 4.0
    /// would have shown 4.00 against a gauge reading 4.25.
    /// </para>
    /// <para>
    /// Weigh that for what it is: an enlarged screenshot read by image analysis, not an
    /// instrument calibration. It is strong corroboration of the offset and it is not proof.
    /// Should better evidence ever contradict it, this is still the ONE constant to change.
    /// </para>
    /// </summary>
    public const double UnitsOffset = 3.75;

    /// <summary>
    /// Announcement granularity, in units. The indicator is graduated in quarter units, so a
    /// finer step would speak changes the aircraft does not display and turn a slow trim wheel
    /// into a stream of speech. Quantising here rather than debouncing on degrees is also what
    /// makes the callout land on the same values the FMC quotes.
    /// </summary>
    public const double UnitsStep = 0.25;

    /// <summary>Stabiliser trim in units, snapped to <see cref="UnitsStep"/>.</summary>
    public static double UnitsFromDegrees(double degrees)
    {
        double units = Math.Round((degrees + UnitsOffset) / UnitsStep,
                                  MidpointRounding.AwayFromZero) * UnitsStep;
        // Normalise negative zero: -0.0 == 0.0 compares true but formats as "-0.00", which at the
        // bottom stop would announce a negative trim on a scale that has no negative end.
        return units == 0.0 ? 0.0 : units;
    }

    /// <summary>
    /// The spoken phrase. No "up"/"down": the sign is already in the number on a 0–14.5 scale,
    /// and a direction word invites the pilot to hear it as a relative change rather than the
    /// absolute position the FMC and the indicator both state.
    /// </summary>
    public static string Describe(double degrees)
        => $"Trim {UnitsFromDegrees(degrees):F2} units";
}
