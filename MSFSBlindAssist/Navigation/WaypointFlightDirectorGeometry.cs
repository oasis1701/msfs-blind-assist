namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Altitude crossing constraint carried by a tracked waypoint slot.
/// Defined here (shared between <see cref="WaypointTracker"/> and
/// <see cref="WaypointFlightDirectorGeometry"/>).
/// </summary>
public enum AltitudeConstraintType
{
    None,
    At,
    AtOrAbove,
    AtOrBelow,
    Between
}

/// <summary>
/// Pure, stateless command math for the synthetic Waypoint Flight Director.
///
/// Everything here is a deterministic function of the inputs — no audio, no SimConnect, no
/// aircraft state — so it is unit-probe-testable (see <c>tools/WaypointFdProbe</c>), exactly like
/// <c>DockingGeometry</c>. The stateful <c>WaypointFlightDirectorManager</c> calls these per frame
/// and renders the results to its two tones.
///
/// Sign conventions (right-positive, "standard"):
///   - Track/bearing/heading are degrees true-or-magnetic 0..360 (caller keeps them consistent —
///     the manager uses magnetic bearing vs the magnetic GPS ground track).
///   - Commanded bank: positive = roll right. Commanded pitch: positive = nose up.
/// The manager converts to the AudioToneGenerator's pan/frequency the same way VisualGuidanceManager
/// does (desired-tone pan = commanded bank; actual-tone pan = StandardBank(simconnectBank)).
/// </summary>
public static class WaypointFlightDirectorGeometry
{
    public const double FeetPerNauticalMile = 6076.12;
    public const double EarthRadiusNm = 3440.065;
    private const double Rad2Deg = 180.0 / System.Math.PI;
    private const double Deg2Rad = System.Math.PI / 180.0;

    /// <summary>Normalises an angle (degrees) to the range (-180, +180].</summary>
    public static double NormalizeSigned(double degrees)
    {
        double d = degrees % 360.0;
        if (d > 180.0) d -= 360.0;
        if (d <= -180.0) d += 360.0;
        return d;
    }

    /// <summary>
    /// Lateral track error: how far the wind-corrected ground track is off the bearing to the fix.
    /// Positive = the fix is to the RIGHT of the current track (roll right to correct).
    /// Using ground track (not heading) means nulling this flies a straight, wind-corrected path.
    /// </summary>
    public static double TrackError(double bearingToFixDeg, double groundTrackDeg)
        => NormalizeSigned(bearingToFixDeg - groundTrackDeg);

    /// <summary>
    /// Proportional roll law with rate-lead anticipation, clamped to the bank cap.
    /// <paramref name="yawRateDegPerSec"/> is the current turn rate (positive = turning right);
    /// subtracting <c>yawRate*lead</c> rolls out of a turn before the track aligns, killing overshoot
    /// (same idea as the taxi-tone turn lead). All gains/caps come from the per-aircraft profile.
    /// </summary>
    public static double CommandedBankDeg(double trackErrorDeg, double yawRateDegPerSec,
                                          double kRoll, double bankRateLeadSec, double maxBankDeg)
    {
        double lead = yawRateDegPerSec * bankRateLeadSec;
        double cmd = kRoll * (trackErrorDeg - lead);
        return System.Math.Clamp(cmd, -maxBankDeg, maxBankDeg);
    }

    /// <summary>
    /// Required flight-path angle (degrees, positive = climb) to reach <paramref name="targetAltFt"/>
    /// from the current MSL altitude over the remaining horizontal distance. Guarded for tiny
    /// distances (returns 0 inside ~0.05 NM so the command doesn't blow up overhead the fix).
    /// </summary>
    public static double RequiredFpaDeg(double targetAltFt, double altMslFt, double distToFixNm)
    {
        if (distToFixNm < 0.05) return 0.0;
        double distFt = distToFixNm * FeetPerNauticalMile;
        return System.Math.Atan2(targetAltFt - altMslFt, distFt) * Rad2Deg;
    }

    /// <summary>
    /// Commanded pitch ≈ FPA + AoA in coordinated flight. Live INCIDENCE ALPHA encodes weight/flap/
    /// speed, so this is aircraft-agnostic with no performance model (the VG nominal-pitch trick).
    /// Clamped to the pitch cap.
    /// </summary>
    public static double CommandedPitchDeg(double requiredFpaDeg, double aoaDeg, double maxPitchDeg)
        => System.Math.Clamp(requiredFpaDeg + aoaDeg, -maxPitchDeg, maxPitchDeg);

    /// <summary>
    /// Predicted altitude (MSL) at the fix if the current vertical speed and ground speed hold.
    /// Used to evaluate "satisfied?" for AT_OR_ABOVE / AT_OR_BELOW / BETWEEN constraints.
    /// </summary>
    public static double ProjectedCrossingAltFt(double altMslFt, double vsFpm,
                                                double distToFixNm, double groundSpeedKts)
    {
        if (groundSpeedKts < 1.0) return altMslFt;
        double minutesToFix = (distToFixNm / groundSpeedKts) * 60.0;
        return altMslFt + vsFpm * minutesToFix;
    }

    /// <summary>
    /// Resolves a slot's altitude constraint into "should the vertical tone command anything, and
    /// toward what target altitude". A neutral (inactive) result means the constraint is already
    /// satisfied — the vertical tone goes to nominal and the FD does not nag.
    ///   AT            → always command toward <paramref name="lowerFt"/>.
    ///   AT_OR_ABOVE   → neutral while projected ≥ lower; else command up to lower.
    ///   AT_OR_BELOW   → neutral while projected ≤ lower; else command down to lower.
    ///   BETWEEN       → neutral inside [lower, upper]; else command toward the violated bound.
    /// </summary>
    public static (bool active, double targetAltFt) ResolveVerticalTarget(
        AltitudeConstraintType constraint, double? lowerFt, double? upperFt, double projectedCrossingAltFt)
    {
        switch (constraint)
        {
            case AltitudeConstraintType.At:
                return lowerFt.HasValue ? (true, lowerFt.Value) : (false, 0.0);

            case AltitudeConstraintType.AtOrAbove:
                if (!lowerFt.HasValue) return (false, 0.0);
                return projectedCrossingAltFt >= lowerFt.Value ? (false, 0.0) : (true, lowerFt.Value);

            case AltitudeConstraintType.AtOrBelow:
                if (!lowerFt.HasValue) return (false, 0.0);
                return projectedCrossingAltFt <= lowerFt.Value ? (false, 0.0) : (true, lowerFt.Value);

            case AltitudeConstraintType.Between:
                if (!lowerFt.HasValue || !upperFt.HasValue) return (false, 0.0);
                double lo = System.Math.Min(lowerFt.Value, upperFt.Value);
                double hi = System.Math.Max(lowerFt.Value, upperFt.Value);
                if (projectedCrossingAltFt < lo) return (true, lo);
                if (projectedCrossingAltFt > hi) return (true, hi);
                return (false, 0.0);

            default:
                return (false, 0.0);
        }
    }

    /// <summary>
    /// Signed cross-track distance (NM) of the aircraft from a course line through a reference fix,
    /// given the great-circle distance + TRUE bearing FROM the fix TO the aircraft and the course's
    /// TRUE bearing. Positive = aircraft is to the RIGHT of the course line; negative = left.
    /// (Great-circle cross-track formula; sign matches "right of course".)
    /// </summary>
    public static double CrossTrackNm(double distFixToAcNm, double bearingFixToAcTrueDeg, double courseTrueDeg)
    {
        double d13 = distFixToAcNm / EarthRadiusNm;                       // angular distance (radians)
        double dTheta = (bearingFixToAcTrueDeg - courseTrueDeg) * Deg2Rad;
        return System.Math.Asin(System.Math.Sin(d13) * System.Math.Sin(dTheta)) * EarthRadiusNm;
    }

    /// <summary>
    /// Desired track (degrees, same north reference as <paramref name="courseDeg"/>) to capture and
    /// hold a course line: bias off the course toward the line by an intercept angle that grows with
    /// the cross-track error (capped). Right of course (xt&gt;0) → fly left of the course; left of
    /// course → fly right. Nulling the resulting track error flies onto and then along the course
    /// (wind-corrected, since the manager compares this to GPS ground track). This is the ILS
    /// localizer-capture idiom generalised to any fix + course.
    /// </summary>
    public static double CourseInterceptTrackDeg(double courseDeg, double crossTrackNm,
                                                 double maxInterceptDeg, double interceptDegPerNm)
    {
        double intercept = System.Math.Min(maxInterceptDeg, System.Math.Abs(crossTrackNm) * interceptDegPerNm);
        double desired = courseDeg - System.Math.Sign(crossTrackNm) * intercept;
        return (desired % 360.0 + 360.0) % 360.0;
    }

    /// <summary>
    /// Fix has been reached: either inside the capture radius, OR the fix has passed abeam
    /// (bearing now more than ~90° off track = station passage). The abeam test sequences a
    /// slight miss that never enters the capture radius.
    /// </summary>
    public static bool HasArrived(double distToFixNm, double bearingToFixDeg, double groundTrackDeg,
                                  double captureRadiusNm)
    {
        if (distToFixNm <= captureRadiusNm) return true;
        return System.Math.Abs(NormalizeSigned(bearingToFixDeg - groundTrackDeg)) > 90.0;
    }
}
