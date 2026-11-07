namespace MSFSBlindAssist.Services;

/// <summary>
/// The flare (visual guidance redesign, piece 6, as designed with the owner on 2026-11-06): pure
/// arithmetic, no state, no clock. Heights are wheel heights, ft; a sink rate is fpm + down; a descent
/// angle is + down, as the path law's is, so the manager plays desired pitch = learned level attitude −
/// <see cref="DescentDeg"/> of <see cref="TargetSinkFpm"/>.
///
/// The flare starts when the time to touchdown first reaches <see cref="VisualGuidanceVerticalTuning.FlareStartS"/>
/// (under <see cref="VisualGuidanceVerticalTuning.FlareEndWheelFt"/>) or at the profile's flare height,
/// whichever comes first; "Prepare to flare" is due <see cref="VisualGuidanceVerticalTuning.FlarePrepareLeadS"/>
/// before that start and "Flare" <see cref="VisualGuidanceVerticalTuning.FlareCallLeadS"/> before it
/// (<see cref="TimeToStartS"/>). The target sink eases from the aircraft's own at the flare's start to
/// <see cref="VisualGuidanceVerticalTuning.TouchdownSinkFpm"/> at touchdown through the landing assist's own
/// <see cref="LandingGuidanceLaws.FlareTargetVsFpm"/>, so the target is always a descent. The cue adds a lead while the aircraft still sinks faster than the
/// target, so it can briefly sit above the level attitude then; floating or ballooning it is the target's
/// own attitude, asking gently back down (<see cref="CueDeg"/>).
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

    /// <summary>
    /// Seconds until the flare starts at this sink (<see cref="Starts"/>): 0 once it would start, +∞ when
    /// level, climbing or not a number. By time it needs both touchdown <see cref="VisualGuidanceVerticalTuning.FlareStartS"/>
    /// away and the wheels under the flare's band; by height, the wheels at the flare height.
    /// </summary>
    public static double TimeToStartS(double wheelFt, double sinkFpm, double backstopFt, VisualGuidanceVerticalTuning t)
    {
        if (Starts(wheelFt, sinkFpm, backstopFt, t)) return 0.0;
        if (!double.IsFinite(wheelFt) || !double.IsFinite(sinkFpm) || sinkFpm <= 0) return double.PositiveInfinity;
        double ftPerS = sinkFpm / 60.0;
        double byTime = Math.Max(TimeToTouchdownS(wheelFt, sinkFpm) - t.FlareStartS, (wheelFt - t.FlareEndWheelFt) / ftPerS);
        double byHeight = (wheelFt - backstopFt) / ftPerS;
        return Math.Max(0.0, Math.Min(byTime, byHeight));
    }

    /// <summary>"Prepare to flare" is due: the flare <see cref="VisualGuidanceVerticalTuning.FlarePrepareLeadS"/> or less away.</summary>
    public static bool PrepareDue(double wheelFt, double sinkFpm, double backstopFt, VisualGuidanceVerticalTuning t)
        => TimeToStartS(wheelFt, sinkFpm, backstopFt, t) <= t.FlarePrepareLeadS;

    /// <summary>"Flare" is due: the flare <see cref="VisualGuidanceVerticalTuning.FlareCallLeadS"/> or less away, or started.</summary>
    public static bool CallDue(double wheelFt, double sinkFpm, double backstopFt, VisualGuidanceVerticalTuning t)
        => TimeToStartS(wheelFt, sinkFpm, backstopFt, t) <= t.FlareCallLeadS;

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
    /// <paramref name="gain"/> times how much steeper the aircraft still descends than the target (its
    /// smoothed descent, never the raw vertical speed: VG-25), so the cue leads the aircraft's lag as
    /// FlyByWire's flare does. Only that way: floating or ballooning (shallower than the target, or
    /// climbing) the cue is the target's own attitude, asking gently back down. Both ways, a balloon at
    /// 30 ft asked 4.7° below the level attitude, a push near the ground (the stage 6 final review). At the
    /// flare's start the two descents are equal, so the cue does not jump; with no reading of the descent
    /// there is no correction.
    /// </summary>
    public static double CueDeg(double levelAttitudeDeg, double targetDescentDeg, double actualDescentDeg, double gain)
        => levelAttitudeDeg - targetDescentDeg
           + (double.IsFinite(actualDescentDeg) && double.IsFinite(gain) ? gain * Math.Max(0.0, actualDescentDeg - targetDescentDeg) : 0.0);
}
