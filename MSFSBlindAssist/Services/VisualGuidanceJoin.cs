using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services;

/// <summary>
/// A position in the approach frame: <paramref name="AlongNm"/> nm back from the threshold on the
/// extended centreline (+ on the approach side, − past the threshold); <paramref name="CrossNm"/> nm
/// to the side, + right of the extended centreline facing the runway.
/// </summary>
internal readonly record struct VisualGuidanceApproachPoint(double AlongNm, double CrossNm);

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
    public static VisualGuidanceApproachPoint Locate(double lat, double lon, Runway runway)
    {
        double phi1 = runway.StartLat * D2R, phi2 = lat * D2R, dLambda = (lon - runway.StartLon) * D2R;
        double a = Math.Sin((phi2 - phi1) / 2) * Math.Sin((phi2 - phi1) / 2)
                 + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        double delta = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double theta = Math.Atan2(Math.Sin(dLambda) * Math.Cos(phi2),
            Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda));
        double rel = theta - runway.Heading * D2R;
        double cross = Math.Asin(Math.Sin(delta) * Math.Sin(rel));
        double alongPastThreshold = Math.Atan2(Math.Sin(delta) * Math.Cos(rel), Math.Cos(delta));
        return new VisualGuidanceApproachPoint(-alongPastThreshold * EarthRadiusNm, cross * EarthRadiusNm);
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
    /// The capture law: the track to fly relative to the leg, from the cross-track looked
    /// <paramref name="rollInS"/> ahead, damped by the closing rate, clamped to ±<paramref name="clampDeg"/>.
    /// With a gain of 30°/nm and no roll-in this is today's arc capture exactly.
    /// </summary>
    public static double DesiredLegTrackDeg(double legCrossNm, double crossRateNmS, double gsKt, double clampDeg,
        double captureGain, double rollInS)
    {
        double clamp = double.IsFinite(clampDeg) ? Math.Max(0, clampDeg) : 0;
        double xLead = legCrossNm + crossRateNmS * rollInS;
        double raw = -CaptureGainDegPerNm(gsKt, captureGain) * xLead - RateDampingDeg * crossRateNmS;
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
        double captureGain, double bankPerDeg, double rollInS)
    {
        if (!(interceptDeg >= 5) || !(gsKt >= MinGroundSpeedKt)) return null;
        double closingNmS = gsKt * Math.Sin(interceptDeg * D2R) / 3600.0;
        if (!(closingNmS > 0)) return null;
        double t = (Math.Abs(legCrossNm) - TurnCueCrossNm(clampDeg, gsKt, captureGain, bankPerDeg)) / closingNmS - rollInS;
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
            for (double angle = VectorMinAngleDeg; angle <= VectorMaxAngleDeg; angle += 1)
                if (p.AlongNm - AlongToEstablishNm(cross, angle, gsKt, captureGain) >= profile.MinimumFinalNm)
                    return new(VisualGuidanceJoinState.Vectoring, angle, new[] { final });
            // Inside the minimum final, close to the line and heading in: straight onto final, never a
            // pattern that leads the pilot away from a runway they are about to land on.
            if (Math.Abs(rel) < 90 && cross <= TurnCueCrossNm(VectorMinAngleDeg, gsKt, captureGain, 1.0))
                return new(VisualGuidanceJoinState.Vectoring, VectorMinAngleDeg, new[] { final });
        }

        // The simple pattern: a downwind on the aircraft's side, a base, final. It mainly serves IFR
        // handovers today ("cleared visual approach" from the far side of the field); full VFR circuits
        // come with the general aviation work (the redesign's piece 7).
        int side = p.CrossNm >= 0 ? 1 : -1;
        double offset = Math.Max(Math.Max(cross, 2 * TurnRadiusNm(gsKt, profile.MaxApproachBankDeg)), MinDownwindNm);
        double baseAlong = profile.MinimumFinalNm + AlongToEstablishNm(offset, PatternClampDeg, gsKt, captureGain);
        return new(VisualGuidanceJoinState.Pattern, PatternClampDeg, new[]
        {
            new VisualGuidanceLeg(VisualGuidanceLegKind.Downwind, 0, side * offset, 180, side),
            new VisualGuidanceLeg(VisualGuidanceLegKind.Base, baseAlong, 0, -90 * side, 0),
            final,
        });
    }
}
