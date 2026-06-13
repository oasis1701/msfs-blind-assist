namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Pure steering-target geometry for taxi guidance. Extracted from
/// TaxiGuidanceManager so the look-ahead walk and curve scan are
/// probe-testable without SimConnect/UI dependencies — same pattern as
/// Services/DockingGeometry.cs + tools/DockingProbe.
///
/// All functions operate on a route POLYLINE given as parallel lat/lon
/// arrays: point k is route node k (segment k runs node k → node k+1).
/// Equirectangular math — accurate at taxi scales, matches the rest of
/// the taxi stack.
/// </summary>
public static class GuidanceGeometry
{
    private const double MPD = 111132.0;            // metres per degree latitude
    private const double DEGENERATE_SEG_M = 0.01;   // skip segments shorter than this
    private const double DISCRETE_STEP_DEG = 20.0;  // single-junction bend owned by turn announcements

    /// <summary>
    /// Walks <paramref name="lookAheadM"/> metres along the route polyline,
    /// starting from the aircraft's clamped along-track projection on segment
    /// <paramref name="segIdx"/>, and returns that point as the steering
    /// target. Continuous in aircraft position AND in segIdx advancement —
    /// no turn/no-turn classification, no frame-to-frame jumps.
    /// Returns the final node when the remaining route is shorter than the
    /// look-ahead.
    /// </summary>
    public static (double lat, double lon) WalkTarget(
        double[] lats, double[] lons, int segIdx,
        double acLat, double acLon, double lookAheadM)
    {
        int segCount = lats.Length - 1;
        if (segCount < 1) return (acLat, acLon);
        if (segIdx < 0) segIdx = 0;
        if (segIdx >= segCount) return (lats[^1], lons[^1]);
        if (lookAheadM < 0.0) lookAheadM = 0.0;   // negative look-ahead would extrapolate behind the polyline

        // Project the aircraft onto the current segment's axis. Clamp the
        // UPPER bound only: when the manager advances the segment at the
        // 25 m capture radius the aircraft is still BEHIND the new segment's
        // start (unclamped t < 0), and (1 − t)·segLen then correctly includes
        // that behind-distance — the walk start stays at the aircraft, and the
        // target is continuous through every capture. Clamping t to 0 would
        // teleport the walk start to the node and step the target ~25 m.
        double cosLat = Math.Cos(lats[segIdx] * Math.PI / 180.0);
        double ax = (acLon - lons[segIdx]) * MPD * cosLat;
        double ay = (acLat - lats[segIdx]) * MPD;
        double sx = (lons[segIdx + 1] - lons[segIdx]) * MPD * cosLat;
        double sy = (lats[segIdx + 1] - lats[segIdx]) * MPD;
        double segLen = Math.Sqrt(sx * sx + sy * sy);
        double t = segLen < DEGENERATE_SEG_M ? 1.0
                 : Math.Min((ax * sx + ay * sy) / (segLen * segLen), 1.0);

        double budget = lookAheadM;
        double remaining = (1.0 - t) * segLen;

        if (budget <= remaining && segLen >= DEGENERATE_SEG_M)
        {
            double f = t + budget / segLen;
            return (lats[segIdx] + (lats[segIdx + 1] - lats[segIdx]) * f,
                    lons[segIdx] + (lons[segIdx + 1] - lons[segIdx]) * f);
        }
        budget -= remaining;

        for (int i = segIdx + 1; i < segCount; i++)
        {
            double cl = Math.Cos(lats[i] * Math.PI / 180.0);
            double ex = (lons[i + 1] - lons[i]) * MPD * cl;
            double ey = (lats[i + 1] - lats[i]) * MPD;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < DEGENERATE_SEG_M) continue;
            if (budget <= len)
            {
                double f = budget / len;
                return (lats[i] + (lats[i + 1] - lats[i]) * f,
                        lons[i] + (lons[i + 1] - lons[i]) * f);
            }
            budget -= len;
        }
        return (lats[^1], lons[^1]);
    }

    /// <summary>
    /// Signed cumulative bearing change (degrees, right positive) over the
    /// junctions encountered within <paramref name="windowM"/> metres of
    /// route ahead of the aircraft's projection on segment
    /// <paramref name="segIdx"/>. <paramref name="hasDiscreteStep"/> is true
    /// when any single junction in the window bends ≥ 20° — those are owned
    /// by the existing discrete-turn announcements.
    /// </summary>
    public static double CumulativeTurnDeg(
        double[] lats, double[] lons, int segIdx,
        double acLat, double acLon, double windowM, out bool hasDiscreteStep)
    {
        hasDiscreteStep = false;
        int segCount = lats.Length - 1;
        if (segCount < 2 || segIdx >= segCount) return 0.0;
        if (segIdx < 0) segIdx = 0;

        // Distance from the aircraft's projection to the end of the current
        // segment — junctions are only counted within windowM of route ahead.
        // Same upper-bound-only clamp as WalkTarget (see comment there).
        double cosLat = Math.Cos(lats[segIdx] * Math.PI / 180.0);
        double ax = (acLon - lons[segIdx]) * MPD * cosLat;
        double ay = (acLat - lats[segIdx]) * MPD;
        double sx = (lons[segIdx + 1] - lons[segIdx]) * MPD * cosLat;
        double sy = (lats[segIdx + 1] - lats[segIdx]) * MPD;
        double segLen = Math.Sqrt(sx * sx + sy * sy);
        double t = segLen < DEGENERATE_SEG_M ? 1.0
                 : Math.Min((ax * sx + ay * sy) / (segLen * segLen), 1.0);

        double travelled = (1.0 - t) * segLen;   // route distance to first junction

        // Reference bearing: first non-degenerate segment at/after segIdx —
        // a zero-length joint has no meaningful bearing to diff against.
        int b0 = segIdx;
        while (b0 < segCount && SegLenM(lats, lons, b0) < DEGENERATE_SEG_M) b0++;
        if (b0 >= segCount) return 0.0;
        double prevBearing = BearingDeg(lats, lons, b0);
        double sum = 0.0;

        for (int i = b0 + 1; i < segCount && travelled <= windowM; i++)
        {
            double len = SegLenM(lats, lons, i);
            if (len < DEGENERATE_SEG_M) continue;   // degenerate: contributes no junction
            double b = BearingDeg(lats, lons, i);
            double delta = ((b - prevBearing + 540.0) % 360.0) - 180.0;
            sum += delta;
            if (Math.Abs(delta) >= DISCRETE_STEP_DEG) hasDiscreteStep = true;
            prevBearing = b;
            travelled += len;
        }
        return sum;
    }

    /// <summary>
    /// Rollout-anticipated heading error: projects the smoothed error forward
    /// by the aircraft's yaw rate over <paramref name="leadSec"/> so the
    /// steering tone centres BEFORE the nose reaches the target bearing —
    /// absorbing pilot reaction time + airframe yaw inertia. The rate
    /// contribution is clamped so heading-sensor noise can never slam the pan.
    /// Sign convention: error and rate are both right-positive.
    /// </summary>
    public static double ProjectHeadingError(
        double errorDeg, double yawRateDegSec, double leadSec, double maxLeadDeg)
    {
        double lead = Math.Clamp(yawRateDegSec * leadSec, -maxLeadDeg, maxLeadDeg);
        return errorDeg - lead;
    }

    private static double SegLenM(double[] lats, double[] lons, int i)
    {
        double cl = Math.Cos(lats[i] * Math.PI / 180.0);
        double ex = (lons[i + 1] - lons[i]) * MPD * cl;
        double ey = (lats[i + 1] - lats[i]) * MPD;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static double BearingDeg(double[] lats, double[] lons, int i)
    {
        double cl = Math.Cos(lats[i] * Math.PI / 180.0);
        double ex = (lons[i + 1] - lons[i]) * MPD * cl;
        double ey = (lats[i + 1] - lats[i]) * MPD;
        return (Math.Atan2(ex, ey) * 180.0 / Math.PI + 360.0) % 360.0;
    }
}
