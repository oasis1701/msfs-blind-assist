namespace MSFSBlindAssist.Services;

/// <summary>How another aircraft is moving relative to the own aircraft's heading.</summary>
public enum TrafficMotion
{
    Stopped,
    SameDirection,
    HeadOn,          // opposite direction AND ahead of us: coming toward us
    OppositeDirection,
    CrossingLeftToRight,
    CrossingRightToLeft,
}

/// <summary>A timed position sample of one traffic aircraft.</summary>
internal readonly record struct PositionFix(double Lat, double Lon, DateTime Utc);

/// <summary>How traffic moves relative to the pilot's ROUTE at the point it occupies.</summary>
public enum RouteRelativeMotion { Stopped, Along, Toward, Crossing }

internal static partial class GroundTrafficLogic
{
    // Motion classification bands (degrees between the two headings)
    private const double SameDirectionDeg = 35.0;
    private const double OppositeDirectionDeg = 145.0;
    private const double StoppedKts = 2.0;

    /// <summary>
    /// How the traffic is moving relative to us. <paramref name="relBearingDeg"/> is where it is
    /// (0 = dead ahead, clockwise) — it separates "coming toward us" from merely opposite.
    /// </summary>
    public static TrafficMotion ClassifyMotion(double ownHeadingTrue, double trafficHeadingTrue,
                                               double trafficGsKts, double relBearingDeg)
    {
        if (trafficGsKts < StoppedKts) return TrafficMotion.Stopped;
        double d = AngleDiff(trafficHeadingTrue, ownHeadingTrue);
        double abs = Math.Abs(d);
        if (abs <= SameDirectionDeg) return TrafficMotion.SameDirection;
        if (abs >= OppositeDirectionDeg)
        {
            double relAbs = Math.Abs(AngleDiff(relBearingDeg, 0.0));
            return relAbs <= 60.0 ? TrafficMotion.HeadOn : TrafficMotion.OppositeDirection;
        }
        // Heading to our right of our own direction = moving from our left toward our right.
        return d > 0 ? TrafficMotion.CrossingLeftToRight : TrafficMotion.CrossingRightToLeft;
    }

    public static string DescribeMotion(TrafficMotion m) => m switch
    {
        TrafficMotion.Stopped => "stopped",
        TrafficMotion.SameDirection => "same direction",
        TrafficMotion.HeadOn => "head-on",
        TrafficMotion.OppositeDirection => "opposite direction",
        TrafficMotion.CrossingLeftToRight => "crossing left to right",
        TrafficMotion.CrossingRightToLeft => "crossing right to left",
        _ => "",
    };

    // ── Direction of travel (R14) ───────────────────────────────────────────────────────
    public const double ReversingMinMoveM = 3.0;
    public const double ReversingMaxGapSec = 5.0;
    public const double ReversingMinGsKts = 0.5;
    public const double ReversingAngleDeg = 120.0;
    public const double AlongRouteMaxDeg = 45.0;
    public const double TowardRouteMinDeg = 135.0;

    /// <summary>
    /// The direction the aircraft is actually travelling: its nose heading, unless the last two
    /// samples (≤ <see cref="ReversingMaxGapSec"/> apart, ≥ <see cref="ReversingMinMoveM"/> apart, at
    /// ≥ <see cref="ReversingMinGsKts"/>) show a track more than <see cref="ReversingAngleDeg"/> from
    /// the nose — tail-first, i.e. a pushback — in which case the track. SimConnect gives only the nose
    /// heading and an unsigned ground speed; without this a pushback toward the pilot reads as "same
    /// direction" and its closest approach is predicted back into the stand.
    /// </summary>
    public static double EffectiveDirection(double headingTrue, double gsKts, PositionFix? previous, PositionFix current)
    {
        if (previous is not { } p || gsKts < ReversingMinGsKts) return headingTrue;
        double dt = (current.Utc - p.Utc).TotalSeconds;
        if (dt <= 0.0 || dt > ReversingMaxGapSec) return headingTrue;
        var (dx, dy) = ToLocal(p.Lat, p.Lon, current.Lat, current.Lon);
        if (Math.Sqrt(dx * dx + dy * dy) < ReversingMinMoveM) return headingTrue;
        double track = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0;
        return Math.Abs(AngleDiff(track, headingTrue)) > ReversingAngleDeg ? track : headingTrue;
    }

    /// <summary>
    /// Motion relative to the route leg the traffic occupies: Along (within 45° of the leg's
    /// direction — moving the way the pilot will go), Toward (within 45° of the reverse), Crossing,
    /// or Stopped (below <see cref="StoppedKts"/>). "Coming toward you" on the route is Toward, never
    /// "opposite to the pilot's own heading" (R14).
    /// </summary>
    public static RouteRelativeMotion ClassifyAlongRoute(double directionTrue, double gsKts, double segmentBearingDeg)
    {
        if (gsKts < StoppedKts) return RouteRelativeMotion.Stopped;
        double d = Math.Abs(AngleDiff(directionTrue, segmentBearingDeg));
        if (d <= AlongRouteMaxDeg) return RouteRelativeMotion.Along;
        if (d >= TowardRouteMinDeg) return RouteRelativeMotion.Toward;
        return RouteRelativeMotion.Crossing;
    }
}
