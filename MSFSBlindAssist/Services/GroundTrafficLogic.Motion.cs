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
}
