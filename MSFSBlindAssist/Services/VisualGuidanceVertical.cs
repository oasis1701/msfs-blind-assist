namespace MSFSBlindAssist.Services;

/// <summary>
/// The vertical path's six tuning numbers (visual guidance redesign, piece 4). They are angles and
/// seconds, so the aircraft's own speed scales them and they are the same for every aircraft (the
/// owner, 2026-11-06); what differs between aircraft is in <c>VisualGuidanceProfile</c>. The harness
/// (`approach --vertical`) picks them, starting from these, the spec's values.
/// </summary>
internal sealed record VisualGuidanceVerticalTuning
{
    /// <summary>
    /// The height error is removed over about this long (FlyByWire's glideslope: 17 to 27 s). The
    /// approach harness picked 27 over the spec's 20 (stage 5): at 20 a light aircraft's cue moved 0.31°
    /// per degree the pilot pitched, 10 s on, against the 0.3 allowed; at 27, 0.20.
    /// </summary>
    public double CorrectionTimeConstantS { get; init; } = 27.0;
    /// <summary>The target path angle stays within this of the runway's glide path angle on the steep side.</summary>
    public double PathLimitDeg { get; init; } = 2.0;
    /// <summary>Under this wheel height the correction stops and the cue holds the glide path angle.</summary>
    public double CorrectionFloorWheelFt { get; init; } = 60.0;
    /// <summary>The trim learner's time constant: a push or pull barely moves it.</summary>
    public double TrimTimeConstantS { get; init; } = 6.0;
    /// <summary>The flight path angle's smoothing time constant.</summary>
    public double PathAngleTimeConstantS { get; init; } = 1.0;
    /// <summary>While joining, under this vertical speed counts as level.</summary>
    public double LevelVsFpm { get; init; } = 200.0;
    /// <summary>While joining, level this long holds the altitude.</summary>
    public double LevelHoldS { get; init; } = 5.0;
    /// <summary>While holding, a pitch this far from the cue ends the hold: the pilot means it.</summary>
    public double HoldBreakPitchDeg { get; init; } = 1.5;

    public static VisualGuidanceVerticalTuning Default { get; } = new();
}

/// <summary>The vertical cue's mode: the aircraft's own pitch, the joining hold, the planned descent, the path, the flare.</summary>
internal enum VisualGuidanceVerticalMode { Follow, Hold, Plan, Path, Flare }

/// <summary>
/// The vertical path (visual guidance redesign, piece 4): pure arithmetic, no state, no clock. The cue
/// is a target descent angle; the manager plays desired pitch = learned trim − target descent angle.
/// Heights are above the threshold; a height error is + above its reference; a descent angle is +
/// descending, as the runway's glide path angle is (3.0). So above the reference means a larger
/// descent angle and a lower desired pitch (VG-24).
///
/// The law has the shape of FlyByWire's A320 glideslope track (flybywiresim/aircraft, GPL-3.0,
/// FmgcOuterLoops.cpp; notes in the redesign's spec folder): it steers a flight path angle, not a
/// vertical speed, as the beam angle plus a correction that removes the height error over about 20 s,
/// limited to 2° of the beam angle, and holds the beam angle under 60 ft. We take the idea and its
/// numbers, credited here; the code is ours.
/// </summary>
internal static class VisualGuidanceVertical
{
    private const double FtPerNm = 6076.12;
    private const double FtPerSecPerKt = 1.68781;
    private const double FpmPerKt = 101.269;
    private const double D2R = Math.PI / 180.0;

    /// <summary>Below this ground speed the angles and the correction use this, so a stopped or slewed aircraft gets finite numbers.</summary>
    public const double MinGroundSpeedKt = 40;
    /// <summary>Within this of the path, F and the mile calls say "on path".</summary>
    public const double OnPathFt = 30;
    /// <summary>The warnings never come within this of the path, twice the "on path" band (plan decision 13).</summary>
    public const double WarningFloorFt = 60;
    /// <summary>"Too low" below the path by this angle: about where the PAPI shows red.</summary>
    public const double TooLowDeg = 0.5;
    /// <summary>The planned descent arrives this far below the path at the join point (plan decision 6).</summary>
    public const double ArrivalBelowDeg = 0.25;
    /// <summary>Before established, BA never plans the aircraft more than this below the path at its distance (the owner, 2026-11-02).</summary>
    public const double FloorBelowPathFt = 150;
    /// <summary>The planned descent levels off this long of flight before the join point: one correction time constant.</summary>
    public const double LevelOffLeadS = 20;
    /// <summary>"Too low" is said once more after this long, if still that low.</summary>
    public const double TooLowRepeatS = 10;
    /// <summary>"Too low" is armed again once the aircraft is back within this fraction of its margin.</summary>
    public const double TooLowRearmFraction = 0.8;

    private static double Gs(double gsKt) => double.IsFinite(gsKt) ? Math.Max(gsKt, MinGroundSpeedKt) : MinGroundSpeedKt;
    private static double Tan(double deg) => Math.Tan(deg * D2R);

    /// <summary>The path's height above the threshold at <paramref name="alongNm"/> out, with the profile's glidepath bias.</summary>
    public static double PathHeightFt(double alongNm, double glidePathDeg, double biasFt)
        => alongNm * FtPerNm * Tan(glidePathDeg) + biasFt;

    /// <summary>The flight path angle, degrees, + climbing.</summary>
    public static double FlightPathAngleDeg(double vsFpm, double gsKt)
        => Math.Atan((double.IsFinite(vsFpm) ? vsFpm : 0) / (Gs(gsKt) * FpmPerKt)) / D2R;

    /// <summary>The angle (degrees, + steeper) that removes <paramref name="heightErrorFt"/> over <paramref name="timeConstantS"/>.</summary>
    public static double CorrectionDeg(double heightErrorFt, double gsKt, double timeConstantS)
        => Math.Atan((double.IsFinite(heightErrorFt) ? heightErrorFt : 0) / (Math.Max(timeConstantS, 0.1) * Gs(gsKt) * FtPerSecPerKt)) / D2R;

    /// <summary>
    /// The descent angle to fly (degrees, + down): the feed-forward angle plus the correction,
    /// between <paramref name="minDescentDeg"/> and <paramref name="maxDescentDeg"/>.
    /// </summary>
    public static double TargetDescentDeg(double feedForwardDeg, double heightErrorFt, double gsKt, double timeConstantS,
        double minDescentDeg, double maxDescentDeg)
        => Math.Clamp(feedForwardDeg + CorrectionDeg(heightErrorFt, gsKt, timeConstantS), minDescentDeg, maxDescentDeg);

    /// <summary>
    /// How far below the path is "Too low" at <paramref name="alongNm"/> out: half a degree, never under
    /// <see cref="WarningFloorFt"/>. "Too high" adds the same margin at the gate (plan decision 3).
    /// </summary>
    public static double TooLowMarginFt(double alongNm, double gpaDeg)
        => Math.Max(WarningFloorFt, Math.Max(0, alongNm) * FtPerNm * (Tan(gpaDeg) - Tan(gpaDeg - TooLowDeg)));

    /// <summary>Where the path passes the stable gate, nm out.</summary>
    public static double GateAlongNm(double gateFt, double gpaDeg, double biasFt)
        => Math.Max(0, (gateFt - biasFt) / (FtPerNm * Tan(gpaDeg)));

    /// <summary>
    /// True when even the steepest cue (the glide path angle + <paramref name="limitDeg"/>) cannot bring
    /// the aircraft within the half degree of the path by the stable gate (plan decision 3). Inside the
    /// gate the margin is the half degree at the aircraft's own distance.
    /// </summary>
    public static bool TooHigh(double alongNm, double heightErrorFt, double gpaDeg, double biasFt, double gateFt, double limitDeg)
    {
        double gateAlong = GateAlongNm(gateFt, gpaDeg, biasFt);
        double removable = Math.Max(0, alongNm - gateAlong) * FtPerNm * (Tan(gpaDeg + limitDeg) - Tan(gpaDeg));
        return heightErrorFt > removable + TooLowMarginFt(Math.Min(alongNm, gateAlong), gpaDeg);
    }

    /// <summary>
    /// The planned descent while vectoring and on the pattern (plan decisions 6 and 7): a line at the
    /// glide path's angle back from a level-off a quarter of a degree under the path at the join point,
    /// measured along the route still to fly (<paramref name="routeToJoinNm"/>), level for the last
    /// <see cref="LevelOffLeadS"/> of flight, never below the floor (<see cref="FloorBelowPathFt"/> under
    /// the path at the aircraft's straight-line distance). Returns the planned height above the
    /// threshold and the feed-forward descent angle: the glide path angle on the sloped part, 0 level.
    /// </summary>
    public static (double HeightFt, double FeedForwardDeg) PlannedHeight(double routeToJoinNm, double joinAlongNm,
        double distanceToThresholdNm, double gpaDeg, double biasFt, double gsKt)
    {
        double target = PathHeightFt(joinAlongNm, gpaDeg, biasFt)
            - joinAlongNm * FtPerNm * (Tan(gpaDeg) - Tan(gpaDeg - ArrivalBelowDeg));
        double leadNm = Gs(gsKt) * LevelOffLeadS / 3600.0;
        double sloped = target + Math.Max(0, routeToJoinNm - leadNm) * FtPerNm * Tan(gpaDeg);
        double floor = PathHeightFt(distanceToThresholdNm, gpaDeg, biasFt) - FloorBelowPathFt;
        return sloped >= floor && routeToJoinNm > leadNm ? (sloped, gpaDeg) : (Math.Max(sloped, floor), 0.0);
    }
}
