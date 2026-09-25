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

/// <summary>A traffic sample: where, when, and — when known — the altitude (feet MSL).</summary>
internal readonly record struct PositionFix(double Lat, double Lon, DateTime Utc, double AltitudeFt = double.NaN);

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
    /// The direction the aircraft is actually travelling: its nose heading, unless
    /// <paramref name="previous"/> (the track baseline — <see cref="TrackAnchor"/> picks it from the
    /// aircraft's recent samples) and <paramref name="current"/> (≤ <see cref="ReversingMaxGapSec"/>
    /// apart, ≥ <see cref="ReversingMinMoveM"/> apart, at ≥ <see cref="ReversingMinGsKts"/>) show a
    /// track more than <see cref="ReversingAngleDeg"/> from the nose — tail-first, i.e. a pushback — in
    /// which case the track. SimConnect gives only the nose heading and an unsigned ground speed;
    /// without this a pushback toward the pilot reads as "same direction" and its closest approach is
    /// predicted back into the stand.
    /// </summary>
    public static double EffectiveDirection(double headingTrue, double gsKts, PositionFix? previous, PositionFix current)
    {
        if (previous is not { } p || gsKts < ReversingMinGsKts) return headingTrue;
        double dt = (current.Utc - p.Utc).TotalSeconds;
        if (dt <= 0.0 || dt > ReversingMaxGapSec) return headingTrue;
        var (dx, dy) = ToLocal(p.Lat, p.Lon, current.Lat, current.Lon);
        if (Math.Sqrt(dx * dx + dy * dy) < ReversingMinMoveM) return headingTrue;
        double track = LocalBearingDeg(dx, dy);
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

    // ── Track history (PR #247 B1 review) ──────────────────────────────────────────────
    // One previous sample is not enough at the 1 s cadence: a 2-4 kt pushback covers 1-2 m per
    // second, under ReversingMinMoveM, and a TCAS sweep can arrive milliseconds after the monitor's
    // own. Keep a few seconds of samples and choose the baseline from them.

    /// <summary>Samples more than this much older than the newest are dropped.</summary>
    public const double TrackHistorySeconds = 10.0;
    /// <summary>At most this many samples are kept per aircraft.</summary>
    public const int TrackHistoryMax = 12;
    /// <summary>A climb rate needs at least this long between its two samples.</summary>
    public const double ClimbMinSpanSec = 0.5;

    /// <summary>
    /// Appends <paramref name="sample"/> (an aircraft's PREVIOUS position, pushed just before its new
    /// one is stored), ignoring one not newer than the last, then drops samples more than
    /// <see cref="TrackHistorySeconds"/> older than it and the oldest beyond <see cref="TrackHistoryMax"/>.
    /// </summary>
    public static void AddToHistory(List<PositionFix> history, PositionFix sample)
    {
        if (history.Count > 0 && sample.Utc <= history[^1].Utc) return;
        history.Add(sample);
        var cutoff = sample.Utc.AddSeconds(-TrackHistorySeconds);
        int stale = 0;
        while (stale < history.Count && history[stale].Utc < cutoff) stale++;
        if (stale > 0) history.RemoveRange(0, stale);
        if (history.Count > TrackHistoryMax) history.RemoveRange(0, history.Count - TrackHistoryMax);
    }

    /// <summary>The newest sample aged between <paramref name="minSec"/> and <paramref name="maxSec"/> at <paramref name="utc"/>, or null.</summary>
    public static PositionFix? NewestSampleAged(IReadOnlyList<PositionFix> history, DateTime utc, double minSec, double maxSec)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            double dt = (utc - history[i].Utc).TotalSeconds;
            if (dt < minSec) continue;
            return dt <= maxSec ? history[i] : null;
        }
        return null;
    }

    /// <summary>
    /// The baseline <see cref="EffectiveDirection"/> measures the track from: the NEWEST sample at least
    /// <see cref="ReversingMinMoveM"/> from <paramref name="current"/> and no more than
    /// <see cref="ReversingMaxGapSec"/> older than it; null when none qualifies (the nose is then used).
    /// </summary>
    public static PositionFix? TrackAnchor(IReadOnlyList<PositionFix> history, PositionFix current)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var h = history[i];
            double dt = (current.Utc - h.Utc).TotalSeconds;
            if (dt <= 0.0) continue;
            if (dt > ReversingMaxGapSec) break;
            var (dx, dy) = ToLocal(h.Lat, h.Lon, current.Lat, current.Lon);
            if (Math.Sqrt(dx * dx + dy * dy) >= ReversingMinMoveM) return h;
        }
        return null;
    }

    /// <summary>
    /// Vertical speed (feet per minute) from the newest sample at least <see cref="ClimbMinSpanSec"/>
    /// and at most <see cref="TrackHistorySeconds"/> older than <paramref name="current"/>; null when
    /// none qualifies or either altitude is unknown.
    /// </summary>
    public static double? ClimbFpm(IReadOnlyList<PositionFix> history, PositionFix current)
    {
        if (!double.IsFinite(current.AltitudeFt)) return null;
        if (NewestSampleAged(history, current.Utc, ClimbMinSpanSec, TrackHistorySeconds) is not { } h
            || !double.IsFinite(h.AltitudeFt)) return null;
        double dt = (current.Utc - h.Utc).TotalSeconds;
        return (current.AltitudeFt - h.AltitudeFt) / dt * 60.0;
    }
}
