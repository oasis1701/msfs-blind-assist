namespace MSFSBlindAssist.Services;

/// <summary>
/// The vertical path's tuning numbers (visual guidance redesign, pieces 4 and 6). They are angles,
/// seconds and sink rates, so the aircraft's own speed scales them and they are the same for every
/// aircraft (the owner, 2026-11-06); what differs between aircraft is in <c>VisualGuidanceProfile</c>.
/// The harness (`approach --vertical`) picks them, starting from these, the spec's values; the flare's
/// four are the owner's starting values (stage 6).
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
    /// <summary>The flare starts this long before touchdown: FlyByWire's flare law starts at about 4.3 s.</summary>
    public double FlareStartS { get; init; } = 4.3;
    /// <summary>
    /// "Prepare to flare" is said this long before the flare starts (the owner, 2026-11-06: from 100 ft to the
    /// flare can take longer than the aircraft's own callouts suggest): about 100 ft at 740 fpm.
    /// </summary>
    public double FlarePrepareLeadS { get; init; } = 4.0;
    /// <summary>
    /// "Flare" is said this long before the flare starts, so the pilot is pulling as the tone rises: about
    /// 65 ft at 740 fpm. It was 2.5 s before touchdown until 2026-11-06, after the tone had started.
    /// </summary>
    public double FlareCallLeadS { get; init; } = 1.0;
    /// <summary>The flare's sink rate at touchdown, fpm: FlyByWire's flare law's.</summary>
    public double TouchdownSinkFpm { get; init; } = 150.0;
    /// <summary>The flare's band: it starts by time only under this wheel height and ends only above it (the landing assist's go-around height).</summary>
    public double FlareEndWheelFt { get; init; } = 100.0;
    /// <summary>
    /// For each degree the aircraft descends steeper than the flare's target (only that way), the flare's cue asks this
    /// many degrees more nose-up, as FlyByWire's flare corrects for the sink error. Without it a jet's
    /// lag (the pilot, the nose, then the flight path: about 3.7 s in the harness) landed it at 518 fpm
    /// against 382 with the old fixed flare; at 1, 246 fpm, with every landing kept. 1.5 and 2 floated
    /// and drifted; the harness picked 1 and the owner agreed (stage 6, 2026-11-06).
    /// </summary>
    public double FlareCorrectionGain { get; init; } = 1.0;

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
/// vertical speed, as the beam angle plus a correction that removes the height error over 17 to 27 s,
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
    /// <summary>The planned descent levels off this long of flight before the join point: the spec's correction time (the harness picked 27 s for the correction itself).</summary>
    public const double LevelOffLeadS = 20;
    /// <summary>"Too low" is said once more after this long, if still that low.</summary>
    public const double TooLowRepeatS = 10;
    /// <summary>"Too low" is armed again once the aircraft is back within this fraction of its margin.</summary>
    public const double TooLowRearmFraction = 0.8;

    private static double Gs(double gsKt) => double.IsFinite(gsKt) ? Math.Max(gsKt, MinGroundSpeedKt) : MinGroundSpeedKt;
    private static double Tan(double deg) => Math.Tan(deg * D2R);

    /// <summary>The glide path angle when the navdata gives no believable one.</summary>
    public const double DefaultPathAngleDeg = 3.0;
    /// <summary>
    /// A navdata angle outside these is not believed (a 0.1° or 30° value would give nonsense guidance;
    /// LOWL 26's PAPI reads 9.0). Real angles fit inside: Aspen's 6.59° ILS is the steepest certified CAT I.
    /// </summary>
    public const double MinPathAngleDeg = 2.0, MaxPathAngleDeg = 8.0;

    /// <summary>
    /// The runway's glide path angle: the ILS glideslope's (<c>ils.gs_pitch</c>: LCY 5.5°, Innsbruck 3.8°)
    /// when it is believable, else the PAPI's or VASI's (<c>runway_end</c>'s light angle: 3.5° and 4.0° are
    /// common where there is no ILS), else 3°. The ILS wins when both are there: it is what the sim's
    /// glideslope flies, and the MSFS 2024 navdata leaves some PAPIs at 3.0 beside a steeper ILS (London
    /// City: PAPI 3.0, ILS 5.5).
    /// </summary>
    public static double PathAngleDeg(double ilsGlideslopeDeg, double papiDeg)
    {
        static bool Believable(double deg) => deg >= MinPathAngleDeg && deg <= MaxPathAngleDeg;
        return Believable(ilsGlideslopeDeg) ? ilsGlideslopeDeg
            : Believable(papiDeg) ? papiDeg
            : DefaultPathAngleDeg;
    }

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
    /// How fast the straight-line distance to the threshold shrinks per mile flown (1 straight at it, 0
    /// square to it, −1 straight away), for <see cref="PlannedHeight"/>'s floor: the cosine of the angle
    /// between the track (<paramref name="relativeTrackDeg"/> from the runway's course) and the bearing
    /// to the threshold, in the approach frame. 0 at the threshold itself.
    /// </summary>
    public static double ClosingFraction(VisualGuidanceApproachPoint p, double relativeTrackDeg)
    {
        double distance = Math.Sqrt(p.AlongNm * p.AlongNm + p.CrossNm * p.CrossNm);
        if (!(distance > 1e-9) || !double.IsFinite(relativeTrackDeg)) return 0.0;
        // The track is (−cos r, sin r) in (along, cross); toward the threshold is (−along, −cross) / distance.
        double r = relativeTrackDeg * D2R;
        return (p.AlongNm * Math.Cos(r) - p.CrossNm * Math.Sin(r)) / distance;
    }

    /// <summary>
    /// The planned descent while vectoring and on the pattern (plan decisions 6 and 7): a line at the
    /// glide path's angle back from a level-off a quarter of a degree under the path at the join point,
    /// measured along the route still to fly (<paramref name="routeToJoinNm"/>), level for the last
    /// <see cref="LevelOffLeadS"/> of flight, never below the floor (<see cref="FloorBelowPathFt"/> under
    /// the path at the aircraft's straight-line distance). Returns the planned height above the
    /// threshold and the feed-forward descent angle: the glide path angle on the sloped part, 0 on its
    /// level part, and on the floor the floor's own slope along the track.
    ///
    /// <paramref name="closingFraction"/> is how fast the straight-line distance to the threshold shrinks
    /// per mile flown: the cosine of the angle between the ground track and the bearing to the threshold.
    /// The floor comes down at atan(tan(gpa) × closing); flying square to the threshold or away from it (a
    /// downwind) it stays or rises, and the plan asks for level, never a climb (VG-26). A jet's plan is
    /// the floor all the way to the join (a 20 s level lead needs 248 ft under the path at 140 kt, the
    /// floor allows 150), so a jet arrives on the floor still descending, not level (stage 5 checkpoint).
    /// </summary>
    public static (double HeightFt, double FeedForwardDeg) PlannedHeight(double routeToJoinNm, double joinAlongNm,
        double distanceToThresholdNm, double closingFraction, double gpaDeg, double biasFt, double gsKt)
    {
        double target = PathHeightFt(joinAlongNm, gpaDeg, biasFt)
            - joinAlongNm * FtPerNm * (Tan(gpaDeg) - Tan(gpaDeg - ArrivalBelowDeg));
        double leadNm = Gs(gsKt) * LevelOffLeadS / 3600.0;
        double sloped = target + Math.Max(0, routeToJoinNm - leadNm) * FtPerNm * Tan(gpaDeg);
        double floor = PathHeightFt(distanceToThresholdNm, gpaDeg, biasFt) - FloorBelowPathFt;
        if (sloped >= floor) return (sloped, routeToJoinNm > leadNm ? gpaDeg : 0.0);
        return (floor, SlopeDeg(gpaDeg, closingFraction));
    }

    /// <summary>
    /// How steeply a line that falls at <paramref name="gpaDeg"/> toward the threshold falls along the
    /// track, degrees, + down: atan(tan(gpa) × closing), level when not closing (never a climb, VG-26) or
    /// when the closing fraction is unknown.
    /// </summary>
    public static double SlopeDeg(double gpaDeg, double closingFraction)
    {
        double closing = double.IsFinite(closingFraction) ? Math.Clamp(closingFraction, 0.0, 1.0) : 0.0;
        return Math.Atan(Tan(gpaDeg) * closing) / D2R;
    }
}
