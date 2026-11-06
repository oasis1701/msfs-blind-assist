namespace MSFSBlindAssist.Services;

/// <summary>
/// The flare (visual guidance redesign, piece 6, as designed with the owner on 2026-11-06): pure
/// arithmetic, no state, no clock. Heights are wheel heights, ft; a sink rate is fpm + down; a descent
/// angle is + down, as the path law's is, so the manager plays desired pitch = learned level attitude −
/// <see cref="DescentDeg"/> of <see cref="TargetSinkFpm"/>.
///
/// The flare starts when the time to touchdown first reaches <see cref="VisualGuidanceVerticalTuning.FlareStartS"/>
/// (under <see cref="VisualGuidanceVerticalTuning.FlareEndWheelFt"/>) or at the profile's flare height,
/// whichever comes first; "Flare" is due at <see cref="VisualGuidanceVerticalTuning.FlareCallS"/> or the
/// same height. The target sink eases from the aircraft's own at the flare's start to
/// <see cref="VisualGuidanceVerticalTuning.TouchdownSinkFpm"/> at touchdown through the landing assist's own
/// <see cref="LandingGuidanceLaws.FlareTargetVsFpm"/>, so it is always a descent and never asks for a climb.
/// The shape (an exponential sink-rate flare from about 4.3 s, 150 fpm at touchdown) is FlyByWire's A320
/// flare law's (flybywiresim/aircraft, GPL-3.0, FmgcOuterLoops.cpp; notes in the redesign's spec
/// folder); we take the idea and its numbers, credited here; the code is ours.
/// </summary>
internal static class VisualGuidanceFlare
{
    private const double FpmPerKt = 101.269;
    private const double D2R = Math.PI / 180.0;

    private static double Gs(double gsKt)
        => double.IsFinite(gsKt) ? Math.Max(gsKt, VisualGuidanceVertical.MinGroundSpeedKt) : VisualGuidanceVertical.MinGroundSpeedKt;

    /// <summary>The sink rate (fpm, + down) of a flight path angle (degrees, + climbing) at the ground speed (at least 40 kt).</summary>
    public static double SinkFpm(double pathAngleDeg, double gsKt)
        => double.IsFinite(pathAngleDeg) ? -Math.Tan(pathAngleDeg * D2R) * Gs(gsKt) * FpmPerKt : 0.0;

    /// <summary>Seconds to touchdown at this sink: +∞ when level, climbing or not a number; 0 at or under 0 ft.</summary>
    public static double TimeToTouchdownS(double wheelFt, double sinkFpm)
    {
        if (wheelFt <= 0) return 0.0;
        if (!double.IsFinite(wheelFt) || !double.IsFinite(sinkFpm) || sinkFpm <= 0) return double.PositiveInfinity;
        return wheelFt / (sinkFpm / 60.0);
    }

    /// <summary>The flare starts: at the flare height, or under the flare's band with touchdown <see cref="VisualGuidanceVerticalTuning.FlareStartS"/> away.</summary>
    public static bool Starts(double wheelFt, double sinkFpm, double backstopFt, VisualGuidanceVerticalTuning t)
        => wheelFt <= backstopFt
           || (wheelFt <= t.FlareEndWheelFt && TimeToTouchdownS(wheelFt, sinkFpm) <= t.FlareStartS);

    /// <summary>"Flare" is due: at the flare height, or with touchdown <see cref="VisualGuidanceVerticalTuning.FlareCallS"/> away.</summary>
    public static bool CallDue(double wheelFt, double sinkFpm, double backstopFt, VisualGuidanceVerticalTuning t)
        => wheelFt <= backstopFt || TimeToTouchdownS(wheelFt, sinkFpm) <= t.FlareCallS;

    /// <summary>
    /// The profile's time constant, set once at the flare's start, so the target starts at the aircraft's
    /// own sink: h0 × 60 / (s0 − touchdown); +∞ when already sinking at the touchdown rate or less (the
    /// target is then the touchdown rate from the start) or on the ground.
    /// </summary>
    public static double TauS(double wheel0Ft, double sink0Fpm, double touchdownFpm)
        => sink0Fpm > touchdownFpm && wheel0Ft > 0 && double.IsFinite(wheel0Ft) && double.IsFinite(sink0Fpm)
            ? wheel0Ft * 60.0 / (sink0Fpm - touchdownFpm)
            : double.PositiveInfinity;

    /// <summary>The target sink (fpm, + down) at this wheel height: from the start's sink down to the touchdown rate at 0 ft.</summary>
    public static double TargetSinkFpm(double wheelFt, double tauS, double touchdownFpm)
        => -LandingGuidanceLaws.FlareTargetVsFpm(double.IsFinite(wheelFt) ? Math.Max(0, wheelFt) : 0, tauS, touchdownFpm);

    /// <summary>The descent angle (degrees, + down) of a sink rate at the ground speed (at least 40 kt).</summary>
    public static double DescentDeg(double sinkFpm, double gsKt)
        => Math.Atan((double.IsFinite(sinkFpm) ? sinkFpm : 0) / (Gs(gsKt) * FpmPerKt)) / D2R;

    /// <summary>
    /// The flare's cue, degrees nose up: the level attitude minus the target's descent angle, plus
    /// <paramref name="gain"/> times how much steeper the aircraft descends than the target (its smoothed
    /// descent, never the raw vertical speed: VG-25), so the cue leads the aircraft's lag as FlyByWire's
    /// flare does. At the flare's start the two are equal, so the cue does not jump; with no reading of the
    /// descent there is no correction.
    /// </summary>
    public static double CueDeg(double levelAttitudeDeg, double targetDescentDeg, double actualDescentDeg, double gain)
        => levelAttitudeDeg - targetDescentDeg
           + (double.IsFinite(actualDescentDeg) && double.IsFinite(gain) ? gain * (actualDescentDeg - targetDescentDeg) : 0.0);
}
