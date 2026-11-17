using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// A position in the approach frame: <paramref name="AlongNm"/> nm back from the threshold on the
/// extended centreline (+ on the approach side, − past the threshold); <paramref name="CrossNm"/> nm
/// to the side, + right of the extended centreline facing the runway.
/// </summary>
internal readonly record struct VisualGuidanceApproachPoint(double AlongNm, double CrossNm);

/// <summary>
/// Where the approach frame starts: the runway's painted landing threshold, past a displaced threshold
/// rather than at the runway end (<see cref="NavigationCalculator.LandingThreshold"/>), and its true
/// course. Every distance, along-track position and path height visual guidance uses is measured from it.
/// </summary>
internal readonly record struct VisualGuidanceThreshold(double Lat, double Lon, double CourseTrueDeg)
{
    public static VisualGuidanceThreshold Of(Runway runway)
    {
        var (lat, lon) = NavigationCalculator.LandingThreshold(runway);
        return new VisualGuidanceThreshold(lat, lon, runway.Heading);
    }
}

/// <summary>
/// Where the aircraft was when visual guidance decided how to reach final: already on it; on a
/// heading (ATC's or the pilot's) that crosses it, spoken "Joining final"; or somewhere BA has to
/// choose the route, straight in ("Vectoring to final") or by the simple pattern from the far side.
/// </summary>
internal enum VisualGuidanceJoinState { OnFinal, Joining, Vectoring, Pattern }

internal enum VisualGuidanceLegKind { Final, Downwind, Base }

/// <summary>
/// A straight line the guidance steers onto: through the approach-frame point
/// (<paramref name="AlongNm"/>, <paramref name="CrossNm"/>), flown in <paramref name="DirectionDeg"/>
/// relative to the final course (0 the landing direction, + to the right, 180 outbound).
/// <paramref name="ReversedTurnSign"/> (+1 right, −1 left, 0 none) is the way to turn when the
/// aircraft flies exactly opposite to the leg.
/// </summary>
internal readonly record struct VisualGuidanceLeg(VisualGuidanceLegKind Kind, double AlongNm, double CrossNm,
    double DirectionDeg, int ReversedTurnSign);

/// <summary>
/// The decision: the legs in order, the last always final. <paramref name="ClampDeg"/> is the final
/// leg's intercept limit: 30 on final, the chosen angle when vectoring, 90 for the pattern, and NaN
/// when joining, meaning the intercept being flown until the turn onto final.
/// </summary>
internal sealed record VisualGuidanceJoinPlan(VisualGuidanceJoinState State, double ClampDeg, IReadOnlyList<VisualGuidanceLeg> Legs);

/// <summary>
/// Joining final (visual guidance redesign, piece 3): the approach-frame geometry, the decision BA
/// makes when the pilot presses Ctrl+V, the capture law that turns the aircraft onto any straight
/// leg, and the predictions F and the countdown speak. Pure: no state, no clock.
///
/// The capture law has the shape of FlyByWire's A320 localizer capture (flybywiresim/aircraft,
/// GPL-3.0, FmgcOuterLoops.cpp; notes in the redesign's spec folder): a desired intercept angle of
/// gain × offset ÷ ground speed, clamped to the intercept being flown, so far from the line it holds
/// the heading and asks for no turn. We take the idea and its numbers, credited here; the code is ours.
/// </summary>
internal static class VisualGuidanceJoin
{
    /// <summary>The capture gain, deg·kt/nm: FlyByWire's k1, 73° of intercept per nm of offset at 140 kt.</summary>
    public const double DefaultCaptureGain = 10259.0;
    /// <summary>Below this ground speed the gain and the closing speed use this, so a stopped or slewed aircraft gets finite numbers.</summary>
    public const double MinGroundSpeedKt = 40;
    /// <summary>Pressed in a turn, BA decides once the bank is under this.</summary>
    public const double DecideBankDeg = 5;
    /// <summary>"Turn onto final" (and "Turn base") when the capture law first asks this much bank toward the leg, as FlyByWire engages LOC*.</summary>
    public const double TurnCueBankDeg = 5;
    public const double EstablishedCrossNm = 0.1;
    public const double EstablishedTrackDeg = 5;
    public const double EstablishedHoldS = 3;
    /// <summary>The steepest track to final that still counts as joining (the spec says about 90°, FlyByWire 115°).</summary>
    public const double JoinMaxAngleDeg = 100;
    public const double VectorMinAngleDeg = 30;
    /// <summary>After the turn onto final the intercept limit is at least this, so a shallow join can still correct.</summary>
    public const double PostTurnMinClampDeg = 30;
    public const double OnFinalClampDeg = 30;
    /// <summary>"Heading does not cross final" once the track has not crossed final for this long.</summary>
    public const double NotCrossingHoldS = 3;
    public const double MinDownwindNm = 1.0;
    /// <summary>Under this intercept the aircraft is not really converging, and there is no time to the turn.</summary>
    public const double MinTimedInterceptDeg = 5;
    private const double VectorMaxAngleDeg = 90;
    private const double PatternClampDeg = 90;
    // Today's ARC_RATE_DAMPING (degrees of desired track per nm/s of closing), kept.
    private const double RateDampingDeg = 12.0;
    private const double EarthRadiusNm = 3440.065;
    private const double G = 9.80665;
    private const double MetresPerNm = 1852.0;
    private const double D2R = Math.PI / 180.0;

    /// <summary>Degrees to (−180, 180].</summary>
    public static double Normalize(double deg)
    {
        double r = deg % 360.0;
        if (r > 180.0) r -= 360.0;
        else if (r <= -180.0) r += 360.0;
        return r;
    }

    /// <summary>
    /// Along and cross of a position, as great-circle along-track and cross-track distances from the
    /// threshold on the runway's true course: the reference the lateral law steers to.
    /// </summary>
    public static VisualGuidanceApproachPoint Locate(double lat, double lon, VisualGuidanceThreshold threshold)
    {
        double phi1 = threshold.Lat * D2R, phi2 = lat * D2R, dLambda = (lon - threshold.Lon) * D2R;
        double a = Math.Sin((phi2 - phi1) / 2) * Math.Sin((phi2 - phi1) / 2)
                 + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        double delta = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double theta = Math.Atan2(Math.Sin(dLambda) * Math.Cos(phi2),
            Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda));
        double rel = theta - threshold.CourseTrueDeg * D2R;
        double cross = Math.Asin(Math.Sin(delta) * Math.Sin(rel));
        double alongPastThreshold = Math.Atan2(Math.Sin(delta) * Math.Cos(rel), Math.Cos(delta));
        return new VisualGuidanceApproachPoint(-alongPastThreshold * EarthRadiusNm, cross * EarthRadiusNm);
    }

    /// <summary>The share of the aircraft's own turn rate at its largest approach bank the capture law may ask for (<see cref="TurnLimitedCaptureGain"/>).</summary>
    public const double TurnRateMargin = 0.9;

    /// <summary>
    /// The capture gain the aircraft can follow at <paramref name="gsKt"/>. Coming off a 90° intercept the
    /// law asks a turn of gain ÷ 3600 degrees a second whatever the speed (2.85°/s at the default), but a
    /// turn at <paramref name="maxBankDeg"/> gives g·tan(bank) ÷ V, so above about 180 kt at 25° the
    /// aircraft cannot turn as fast as the law asks and overshoots the line (the first sim flight,
    /// 2026-11-06: 1.97°/s at 241-265 kt on the pattern's downwind). The gain is capped at
    /// <see cref="TurnRateMargin"/> of what the bank gives, so a faster aircraft starts its turn further
    /// out; at approach speed the full gain stands.
    /// </summary>
    public static double TurnLimitedCaptureGain(double captureGain, double gsKt, double maxBankDeg)
    {
        double gs = Math.Max(double.IsFinite(gsKt) ? gsKt : 0, MinGroundSpeedKt);
        double bank = Math.Clamp(double.IsFinite(maxBankDeg) ? maxBankDeg : 25, 1, 60);
        double turnRateDegS = G * Math.Tan(bank * D2R) / (gs * MetresPerNm / 3600.0) / D2R;
        return Math.Min(captureGain, TurnRateMargin * 3600.0 * turnRateDegS);
    }

    /// <summary>Degrees of desired intercept per nm of offset: the gain over the ground speed (at least <see cref="MinGroundSpeedKt"/>).</summary>
    public static double CaptureGainDegPerNm(double gsKt, double captureGain)
        => captureGain / Math.Max(double.IsFinite(gsKt) ? gsKt : 0, MinGroundSpeedKt);

    /// <summary>The signed distance from a leg, nm, + right of it facing along it.</summary>
    public static double LegCrossNm(in VisualGuidanceLeg leg, VisualGuidanceApproachPoint p)
    {
        // Components (inbound, right): the position is (−along, cross); the leg's right normal is (−sin D, cos D).
        double d = leg.DirectionDeg * D2R;
        double dx = -p.AlongNm - -leg.AlongNm, dy = p.CrossNm - leg.CrossNm;
        return dx * -Math.Sin(d) + dy * Math.Cos(d);
    }

    /// <summary>The angle being flown toward a leg, + converging; 0 on the leg.</summary>
    public static double InterceptDeg(double legCrossNm, double relTrackToLegDeg)
        => legCrossNm == 0 ? 0 : -Math.Sign(legCrossNm) * Normalize(relTrackToLegDeg);

    /// <summary>
    /// The capture law: the track to fly relative to the leg, from the cross-track, damped by the
    /// closing rate, clamped to ±<paramref name="clampDeg"/>. With a gain of 30°/nm this is the arc
    /// capture from before stage 4 exactly. (A roll-in time that looked ahead along the track was
    /// swept in the approach harness at 0, 2 and 4 s; 0 was the only one that passed, so it went.)
    /// </summary>
    public static double DesiredLegTrackDeg(double legCrossNm, double crossRateNmS, double gsKt, double clampDeg,
        double captureGain)
    {
        double clamp = double.IsFinite(clampDeg) ? Math.Max(0, clampDeg) : 0;
        double raw = -CaptureGainDegPerNm(gsKt, captureGain) * legCrossNm - RateDampingDeg * crossRateNmS;
        return Math.Clamp(raw, -clamp, clamp);
    }

    /// <summary>
    /// The cross-track at which the capture law first asks <see cref="TurnCueBankDeg"/> of bank toward
    /// the leg while the aircraft holds an intercept of <paramref name="clampDeg"/>.
    /// <paramref name="bankPerDeg"/> is the bank per degree of track error the guidance uses.
    /// </summary>
    public static double TurnCueCrossNm(double clampDeg, double gsKt, double captureGain, double bankPerDeg)
    {
        if (!(bankPerDeg > 0) || !double.IsFinite(clampDeg)) return 0;
        return Math.Max(0, clampDeg - TurnCueBankDeg / bankPerDeg) / CaptureGainDegPerNm(gsKt, captureGain);
    }

    /// <summary>
    /// Seconds until the turn cue, flying straight at <paramref name="interceptDeg"/>; null when the
    /// intercept is under 5°, the ground speed under <see cref="MinGroundSpeedKt"/>, or the cue is
    /// already past. Leaves out the rate-damping term (about 0.1 s).
    /// </summary>
    public static double? SecondsToTurn(double legCrossNm, double interceptDeg, double clampDeg, double gsKt,
        double captureGain, double bankPerDeg)
    {
        if (!(interceptDeg >= MinTimedInterceptDeg) || !(gsKt >= MinGroundSpeedKt)) return null;
        double closingNmS = gsKt * Math.Sin(interceptDeg * D2R) / 3600.0;
        if (!(closingNmS > 0)) return null;
        double t = (Math.Abs(legCrossNm) - TurnCueCrossNm(clampDeg, gsKt, captureGain, bankPerDeg)) / closingNmS;
        return t >= 0 && double.IsFinite(t) ? t : null;
    }

    /// <summary>
    /// How far along the leg (nm) the capture law carries the aircraft from |<paramref name="fromCrossNm"/>|
    /// at <paramref name="interceptDeg"/> until it is within <see cref="EstablishedCrossNm"/>: straight
    /// until the law's angle drops under the intercept, then the law's exponential curve (along-course
    /// distance is the integral of cot of the track angle). 0 when already within; NaN for an intercept
    /// that does not converge.
    /// </summary>
    public static double AlongToEstablishNm(double fromCrossNm, double interceptDeg, double gsKt, double captureGain)
    {
        double x = Math.Abs(fromCrossNm);
        if (x <= EstablishedCrossNm) return 0;
        if (!(interceptDeg > 0)) return double.NaN;
        double gainDeg = CaptureGainDegPerNm(gsKt, captureGain);
        double c = gainDeg * D2R;                 // rad of track per nm
        double d = interceptDeg / gainDeg;        // nm where the law's angle equals the intercept
        double theta = interceptDeg * D2R;
        // A shallow intercept the law never drops under before 0.1 nm: straight all the way.
        if (d <= EstablishedCrossNm) return (x - EstablishedCrossNm) / Math.Tan(theta);
        double curve0 = Math.Log(Math.Sin(c * EstablishedCrossNm));
        return x > d
            ? (x - d) / Math.Tan(theta) + (Math.Log(Math.Sin(theta)) - curve0) / c
            : (Math.Log(Math.Sin(c * x)) - curve0) / c;
    }

    /// <summary>
    /// Where the turn from <paramref name="relTrackDeg"/> onto <paramref name="targetRelDeg"/> (both
    /// relative to the final course) takes the aircraft, in nm toward the threshold
    /// (<c>InboundNm</c>) and to the right of the course (<c>RightNm</c>). The aircraft rolls in at
    /// <paramref name="bankRateDegS"/> to at most <paramref name="maxBankDeg"/> and rolls out at the
    /// same rate so as to finish on the target, turning the short way, as the capture law does. Each
    /// aircraft's own speed and bank settings, so a faster or slower-rolling aircraft covers more ground.
    /// </summary>
    public static (double InboundNm, double RightNm) TurnOntoCut(double relTrackDeg, double targetRelDeg, double gsKt,
        double maxBankDeg, double bankRateDegS)
    {
        double delta = Normalize(targetRelDeg - relTrackDeg) * D2R;
        if (Math.Abs(delta) < 1e-9) return (0.0, 0.0);
        double gs = Math.Max(double.IsFinite(gsKt) ? gsKt : 0, MinGroundSpeedKt);
        double speedMs = gs * MetresPerNm / 3600.0, speedNmS = gs / 3600.0;
        double rate = Math.Max(double.IsFinite(bankRateDegS) ? bankRateDegS : 0, 0.1) * D2R;
        double maxBank = Math.Clamp(double.IsFinite(maxBankDeg) ? maxBankDeg : 25, 1, 60) * D2R;
        double direction = Math.Sign(delta), remaining = Math.Abs(delta);
        double heading = relTrackDeg * D2R, bank = 0, inbound = 0, right = 0;
        const double Dt = 0.05;
        for (int i = 0; i < 100_000 && remaining > 1e-9; i++)
        {
            // The heading still turned while rolling out from this bank at the same rate: ∫ g·tan(φ)/V.
            double rollOutTurn = G / speedMs * -Math.Log(Math.Cos(bank)) / rate;
            bank = remaining <= rollOutTurn ? Math.Max(0, bank - rate * Dt) : Math.Min(maxBank, bank + rate * Dt);
            double turn = Math.Min(remaining, G * Math.Tan(bank) / speedMs * Dt);
            if (bank == 0 && turn == 0) break;
            heading += direction * turn;
            remaining -= turn;
            inbound += speedNmS * Math.Cos(heading) * Dt;
            right += speedNmS * Math.Sin(heading) * Dt;
        }
        return (inbound, right);
    }

    /// <summary>
    /// Where the aircraft will be lined up on final, nm from the threshold, if it turns from
    /// <paramref name="relTrackDeg"/> onto a cut of <paramref name="cutDeg"/> toward final
    /// (<see cref="TurnOntoCut"/>, with the profile's largest approach bank and bank rate), then flies the
    /// capture law (<see cref="AlongToEstablishNm"/>). Already on the cut, there is no turn to count.
    /// </summary>
    public static double JoinAlongNm(VisualGuidanceApproachPoint p, double relTrackDeg, double cutDeg, double gsKt,
        double captureGain, VisualGuidanceProfile profile)
    {
        int side = p.CrossNm >= 0 ? 1 : -1;
        var (inbound, right) = TurnOntoCut(relTrackDeg, -side * cutDeg, gsKt, profile.MaxApproachBankDeg, profile.MaxBankRateDegPerSec);
        // A turn that takes the aircraft across the line leaves nothing to capture.
        double crossAfter = Math.Max(0, side * (p.CrossNm + right));
        return p.AlongNm - inbound - AlongToEstablishNm(crossAfter, cutDeg, gsKt, captureGain);
    }

    /// <summary>
    /// The route still to fly to the join point, nm, for the planned descent (stage 5), in the flat
    /// approach frame. With one leg (vectoring, final), the straight line to (<paramref name="joinAlongNm"/>, 0).
    /// With several (the pattern): from the aircraft to the active leg's line and along it to its corner
    /// with the next leg (a corner is where two legs' lines meet), corner to corner, then along final to
    /// the join point; on the pattern's final leg, to the line and along it, nothing once inside the join
    /// point. On a leg's line this is the straight line to its corner; off it, it is what the leg before
    /// counted, so the route does not jump when a leg moves on half a mile before its corner (a jump
    /// stepped the plan down 100 to 200 ft at once on the light aircraft's pattern, stage 5 Task 5).
    /// </summary>
    public static double RouteToJoinNm(VisualGuidanceJoinPlan plan, int activeLegIndex, VisualGuidanceApproachPoint p,
        double joinAlongNm)
    {
        int last = plan.Legs.Count - 1;
        if (last <= 0)
            return Math.Sqrt((joinAlongNm - p.AlongNm) * (joinAlongNm - p.AlongNm) + p.CrossNm * p.CrossNm);

        int active = Math.Clamp(activeLegIndex, 0, last);
        var leg = plan.Legs[active];
        var end = active < last ? Corner(leg, plan.Legs[active + 1]) : new VisualGuidanceApproachPoint(joinAlongNm, 0);
        // The leg's direction in (along, cross): inbound is −along, right is +cross.
        double dx = -Math.Cos(leg.DirectionDeg * D2R), dy = Math.Sin(leg.DirectionDeg * D2R);
        double offAlong = p.AlongNm - leg.AlongNm, offCross = p.CrossNm - leg.CrossNm;
        double sideways = Math.Abs(offAlong * dy - offCross * dx);
        // Along the line from the aircraft's foot on it to the end: past a corner it must come back; past
        // the join point on final there is nothing left.
        double ahead = (end.AlongNm - p.AlongNm) * dx + (end.CrossNm - p.CrossNm) * dy;
        double total = sideways + (active < last ? Math.Abs(ahead) : Math.Max(0, ahead));

        var from = end;
        for (int i = active + 1; i < last; i++)
        {
            var corner = Corner(plan.Legs[i], plan.Legs[i + 1]);
            total += Math.Sqrt((corner.AlongNm - from.AlongNm) * (corner.AlongNm - from.AlongNm)
                + (corner.CrossNm - from.CrossNm) * (corner.CrossNm - from.CrossNm));
            from = corner;
        }
        if (active < last)
            total += Math.Sqrt((joinAlongNm - from.AlongNm) * (joinAlongNm - from.AlongNm) + from.CrossNm * from.CrossNm);
        return total;
    }

    // Where two legs' lines meet; the second leg's own point when they are parallel.
    private static VisualGuidanceApproachPoint Corner(in VisualGuidanceLeg a, in VisualGuidanceLeg b)
    {
        // A leg's direction in (along, cross): inbound is −along, right is +cross.
        double ax = -Math.Cos(a.DirectionDeg * D2R), ay = Math.Sin(a.DirectionDeg * D2R);
        double bx = -Math.Cos(b.DirectionDeg * D2R), by = Math.Sin(b.DirectionDeg * D2R);
        double det = ax * by - ay * bx;
        if (Math.Abs(det) < 1e-9) return new(b.AlongNm, b.CrossNm);
        double t = ((b.AlongNm - a.AlongNm) * by - (b.CrossNm - a.CrossNm) * bx) / det;
        return new(a.AlongNm + t * ax, a.CrossNm + t * ay);
    }

    /// <summary>
    /// The pattern's turn onto final, nm along final from the base: the wider of the capture law's curve
    /// from the 90° base (<see cref="AlongToEstablishNm"/>) and the aircraft's own turn onto final at its
    /// speed and bank settings (<see cref="TurnOntoCut"/>), so the base leaves room for whichever the
    /// aircraft flies. At 140 kt with 25° of bank the law's curve is the wider (1.61 nm against 0.76); an
    /// aircraft limited to a shallow bank at a high speed needs its own turn's room. The turn onto the
    /// base needs no room along final: it is flown beyond the base, and the downwind is at least two turn
    /// radii out (<see cref="Decide"/>).
    /// </summary>
    public static double PatternTurnOntoFinalNm(double downwindOffsetNm, int side, double gsKt, double captureGain,
        VisualGuidanceProfile profile)
    {
        double curve = AlongToEstablishNm(downwindOffsetNm, PatternClampDeg, gsKt, captureGain);
        double ownTurn = TurnOntoCut(-90.0 * side, 0, gsKt, profile.MaxApproachBankDeg, profile.MaxBankRateDegPerSec).InboundNm;
        return Math.Max(double.IsFinite(curve) ? curve : 0, ownTurn);
    }

    /// <summary>
    /// The narrowest downwind that lets the aircraft finish its turn onto the base before the turn onto
    /// final comes: the sideways room of its own base turn at its speed and bank (<see cref="TurnOntoCut"/>)
    /// plus the distance from final where the turn onto final starts on a 90° base
    /// (<see cref="TurnCueCrossNm"/>, at one degree of bank per degree, as the close-in vectoring test in
    /// <see cref="Decide"/> takes it; the speed factor moves it by about 0.01 nm). About 1.92 nm at 140 kt
    /// with 25° of bank. Narrower, the two turns ran into one U-turn at full bank and swung 500 to 640 ft past
    /// the centreline in the approach harness (the owner chose this width, 2026-11-05).
    /// </summary>
    public static double PatternLeastDownwindNm(int side, double gsKt, double captureGain, VisualGuidanceProfile profile)
        => Math.Abs(TurnOntoCut(180, -90.0 * side, gsKt, profile.MaxApproachBankDeg, profile.MaxBankRateDegPerSec).RightNm)
           + TurnCueCrossNm(PatternClampDeg, gsKt, captureGain, 1.0);

    /// <summary>
    /// Where the pattern lines the aircraft up on final, nm from the threshold: the base's distance less
    /// the turn onto final (<see cref="PatternTurnOntoFinalNm"/>) at this ground speed, the same sum that
    /// placed the base. NaN for any other plan.
    /// </summary>
    public static double PatternJoinAlongNm(VisualGuidanceJoinPlan plan, double gsKt, double captureGain, VisualGuidanceProfile profile)
    {
        if (plan.State != VisualGuidanceJoinState.Pattern || plan.Legs.Count < 3) return double.NaN;
        var downwind = plan.Legs[0];
        return plan.Legs[1].AlongNm - PatternTurnOntoFinalNm(Math.Abs(downwind.CrossNm), downwind.CrossNm >= 0 ? 1 : -1,
            gsKt, captureGain, profile);
    }

    /// <summary>
    /// Whether the pattern's next leg is due: the capture law onto it, holding the intercept being flown
    /// toward it (at most the pattern's 90°), first asks <see cref="TurnCueBankDeg"/> of bank toward it,
    /// that is, converging and within <see cref="TurnCueCrossNm"/>. Downwind to base is "Turn base"; base to
    /// final is the turn onto final.
    /// </summary>
    public static bool LegIsDue(in VisualGuidanceLeg leg, VisualGuidanceApproachPoint p, double relTrackDeg, double gsKt,
        double captureGain, double bankPerDeg)
    {
        double cross = LegCrossNm(leg, p);
        double intercept = InterceptDeg(cross, Normalize(relTrackDeg - leg.DirectionDeg));
        return intercept > 0
            && Math.Abs(cross) <= TurnCueCrossNm(Math.Min(intercept, PatternClampDeg), gsKt, captureGain, bankPerDeg);
    }

    /// <summary>
    /// On the pattern's downwind or base: the heading error to turn by (degrees, + right) onto
    /// <paramref name="leg"/>, from the capture law onto it with the pattern's 90° limit, its closing rate
    /// worked out from the ground speed and the track. On the downwind the turn never goes through the
    /// heading straight at the centreline (90° from the downwind, the <see cref="VisualGuidanceLeg.ReversedTurnSign"/>
    /// side): when the short way would, past its first <see cref="ShortWayAtTheFieldDeg"/>, it turns the
    /// other way, away from the runway (the owner's choice,
    /// 2026-11-05: an aircraft cutting in at 45° 2 nm beside final turned 135° toward the runway onto its
    /// downwind, which gave up a mile of the room and swung it 3,900 ft past the centreline). Flying the
    /// landing direction, that is the reversed-turn way, so a reversal never swings across the field.
    /// </summary>
    public static double LegHeadingErrorDeg(in VisualGuidanceLeg leg, VisualGuidanceApproachPoint p, double relTrackDeg,
        double gsKt, double captureGain)
    {
        double cross = LegCrossNm(leg, p);
        double relToLeg = Normalize(relTrackDeg - leg.DirectionDeg);
        double gs = double.IsFinite(gsKt) ? Math.Max(gsKt, 0) : 0;
        double closingNmS = gs * Math.Sin(relToLeg * D2R) / 3600.0;
        double desired = DesiredLegTrackDeg(cross, closingNmS, gsKt, PatternClampDeg, captureGain);
        double error = Normalize(desired - relToLeg);
        if (leg.ReversedTurnSign == 0 || error == 0) return error;
        // How far the turn, the short way, has to go before it points straight at the centreline. Within the
        // first ShortWayAtTheFieldDeg it keeps the short way: an aircraft already pointing at the field turns
        // away from pointing at it, and the room that costs is at most R(1 − cos 20°), 0.04 nm at 140 kt (the
        // final review: at exactly straight at the centreline, a tenth of a degree chose a 90° or a 270° turn).
        double atCentreline = 90.0 * leg.ReversedTurnSign;
        double toCentreline = error > 0 ? Mod360(atCentreline - relToLeg) : Mod360(relToLeg - atCentreline);
        return toCentreline >= ShortWayAtTheFieldDeg && toCentreline < Math.Abs(error) ? -Math.Sign(error) * 180.0 : error;
    }

    /// <summary>
    /// The turn onto the downwind keeps the short way if it would point the aircraft straight at the
    /// centreline within this many degrees of its start (<see cref="LegHeadingErrorDeg"/>).
    /// </summary>
    public const double ShortWayAtTheFieldDeg = 20;

    private static double Mod360(double deg) => (deg % 360.0 + 360.0) % 360.0;

    /// <summary>Turn radius, nm, at <paramref name="gsKt"/> and <paramref name="bankDeg"/> of bank.</summary>
    public static double TurnRadiusNm(double gsKt, double bankDeg)
    {
        double v = gsKt * MetresPerNm / 3600.0;
        return v * v / (G * Math.Tan(bankDeg * D2R)) / MetresPerNm;
    }

    /// <summary>
    /// True when the track, extended ahead, crosses the final course converging at most
    /// <see cref="JoinMaxAngleDeg"/> from it, at <paramref name="crossAlongNm"/> nm out, at least
    /// <paramref name="minFinalNm"/> from the threshold.
    /// </summary>
    public static bool CrossesFinal(VisualGuidanceApproachPoint p, double relTrackDeg, double minFinalNm, out double crossAlongNm)
    {
        crossAlongNm = double.NaN;
        double rel = Normalize(relTrackDeg);
        if (!(Math.Abs(rel) <= JoinMaxAngleDeg)) return false;
        double s = Math.Sin(rel * D2R);
        if (!(p.CrossNm * s < 0)) return false;
        crossAlongNm = p.AlongNm + p.CrossNm * Math.Cos(rel * D2R) / s;
        return crossAlongNm >= minFinalNm;
    }

    /// <summary>
    /// True when the track, extended ahead, meets the extended centreline on the approach side, at any
    /// angle and however close in: converging, with the crossing point before the threshold. While
    /// joining, the turn onto final still comes then, even when the crossing point has slipped inside the
    /// minimum final or past <see cref="JoinMaxAngleDeg"/> (those only decide the spoken "Heading does not
    /// cross final" and F's sentence); otherwise the cue would wait until 0.05 nm and then ask for a
    /// sudden steep turn (stage-4 checkpoint review).
    /// </summary>
    public static bool MeetsFinalAhead(VisualGuidanceApproachPoint p, double relTrackDeg)
    {
        double rel = Normalize(relTrackDeg);
        double s = Math.Sin(rel * D2R);
        if (!(p.CrossNm * s < 0)) return false;
        return p.AlongNm + p.CrossNm * Math.Cos(rel * D2R) / s > 0;
    }

    /// <summary>
    /// What BA does from here: on final; joining on the heading being flown; vectoring straight in at
    /// the shallowest angle from 30° that still leaves the minimum final; or the simple pattern.
    /// </summary>
    public static VisualGuidanceJoinPlan Decide(VisualGuidanceApproachPoint p, double relTrackDeg, double gsKt,
        double captureGain, VisualGuidanceProfile profile)
    {
        var final = new VisualGuidanceLeg(VisualGuidanceLegKind.Final, 0, 0, 0, 0);
        double rel = Normalize(relTrackDeg);
        double cross = Math.Abs(p.CrossNm);

        if (p.AlongNm > 0 && cross <= EstablishedCrossNm && Math.Abs(rel) <= EstablishedTrackDeg)
            return new(VisualGuidanceJoinState.OnFinal, OnFinalClampDeg, new[] { final });

        if (CrossesFinal(p, rel, profile.MinimumFinalNm, out _))
            return new(VisualGuidanceJoinState.Joining, double.NaN, new[] { final });

        if (p.AlongNm > 0)
        {
            // The shallowest cut that, counting the turn onto it, lines the aircraft up at least the
            // minimum final out (the owner's choice at the stage-4 checkpoint, 2026-11-04: from parallel
            // the turn alone took an airliner 0.7 nm along final).
            for (double angle = VectorMinAngleDeg; angle <= VectorMaxAngleDeg; angle += 1)
                if (JoinAlongNm(p, rel, angle, gsKt, captureGain, profile) >= profile.MinimumFinalNm)
                    return new(VisualGuidanceJoinState.Vectoring, angle, new[] { final });
            // Inside the minimum final, close to the line and heading in: straight onto final, never a
            // pattern that leads the pilot away from a runway they are about to land on.
            if (Math.Abs(rel) < 90 && cross <= TurnCueCrossNm(VectorMinAngleDeg, gsKt, captureGain, 1.0))
                return new(VisualGuidanceJoinState.Vectoring, VectorMinAngleDeg, new[] { final });
            // For the same reason, inside the minimum final and heading in, further off the line: the
            // shallowest cut that still lines the aircraft up before the threshold, as long as the turn onto it
            // does not carry the aircraft across the line (the final review: from 1.5 nm out and 0.4 nm beside
            // final the pattern turned a descending aircraft away from the runway). Only where none does, the
            // pattern.
            if (p.AlongNm < profile.MinimumFinalNm && Math.Abs(rel) < 90)
            {
                int toward = p.CrossNm >= 0 ? 1 : -1;
                for (double angle = VectorMinAngleDeg; angle <= VectorMaxAngleDeg; angle += 1)
                {
                    var turn = TurnOntoCut(rel, -toward * angle, gsKt, profile.MaxApproachBankDeg, profile.MaxBankRateDegPerSec);
                    if (toward * (p.CrossNm + turn.RightNm) > 0
                        && JoinAlongNm(p, rel, angle, gsKt, captureGain, profile) > 0)
                        return new(VisualGuidanceJoinState.Vectoring, angle, new[] { final });
                }
            }
        }

        // The simple pattern: a downwind on the aircraft's side, a base, final. It mainly serves IFR
        // handovers today ("cleared visual approach" from the far side of the field); full VFR circuits
        // come with the general aviation work (the redesign's piece 7).
        int side = p.CrossNm >= 0 ? 1 : -1;
        return PatternPlan(side, cross, gsKt, captureGain, profile);
    }

    /// <summary>
    /// The simple pattern on <paramref name="side"/> for this ground speed: a downwind at least
    /// <paramref name="leastOffsetNm"/> out and wide enough for the aircraft's own turns, and a base placed
    /// so the aircraft lines up the minimum final out, counting the turn onto final from its own speed and
    /// bank (<see cref="PatternTurnOntoFinalNm"/>), as vectoring counts the turn onto its cut.
    /// </summary>
    private static VisualGuidanceJoinPlan PatternPlan(int side, double leastOffsetNm, double gsKt, double captureGain,
        VisualGuidanceProfile profile)
    {
        double offset = Math.Max(Math.Max(leastOffsetNm, 2 * TurnRadiusNm(gsKt, profile.MaxApproachBankDeg)),
            Math.Max(MinDownwindNm, PatternLeastDownwindNm(side, gsKt, captureGain, profile)));
        double baseAlong = profile.MinimumFinalNm + PatternTurnOntoFinalNm(offset, side, gsKt, captureGain, profile);
        return new(VisualGuidanceJoinState.Pattern, PatternClampDeg, new[]
        {
            new VisualGuidanceLeg(VisualGuidanceLegKind.Downwind, 0, side * offset, 180, side),
            new VisualGuidanceLeg(VisualGuidanceLegKind.Base, baseAlong, 0, -90 * side, 0),
            new VisualGuidanceLeg(VisualGuidanceLegKind.Final, 0, 0, 0, 0),
        });
    }

    /// <summary>
    /// The pattern for the speed now (VG-28), while the downwind is still the leg flown: the downwind
    /// widens when the aircraft's turns now need more room, never narrows (the aircraft may already be on
    /// it), and the base moves to where the turn onto final at this speed lines up the minimum final out,
    /// in as the aircraft slows, out if it speeds up. Any other plan comes back as it is.
    /// </summary>
    public static VisualGuidanceJoinPlan ResizePattern(VisualGuidanceJoinPlan plan, double gsKt, double captureGain,
        VisualGuidanceProfile profile)
    {
        if (plan.State != VisualGuidanceJoinState.Pattern || plan.Legs.Count < 3) return plan;
        double offset = plan.Legs[0].CrossNm;
        return PatternPlan(offset >= 0 ? 1 : -1, Math.Abs(offset), gsKt, captureGain, profile);
    }
}
