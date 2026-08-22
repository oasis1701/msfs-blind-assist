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
    // Matches the manager's "len < 1.0 -> bearing 0.0" rule: a sub-metre segment
    // (a route-start snap stub, a duplicated node) has no usable axis. Projecting
    // onto one turns the look-ahead walk into on-axis extrapolation: at KLAS
    // (2026-08-20) a restarted route began with a ~0.5 m snap segment, the walk
    // interpolated WITHIN it (|t| ~ 100), and the steering target slid around a
    // few metres from the aircraft instead of leading 50 m up the route — the
    // pilot orbited it until they gave up. Sub-metre segments are skipped whole.
    private const double DEGENERATE_SEG_M = 1.0;    // skip segments shorter than this
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

        // Skip degenerate segments BEFORE projecting, so the projection always has a
        // real axis to run along. A sub-metre segment is a point: it has no direction
        // to project onto, and the two ways of pretending otherwise both break the
        // walk. Projecting ONTO it extrapolates along a phantom axis (|t| in the
        // hundreds), which is how a restarted route's snap stub produced a target
        // sliding a few metres from the aircraft instead of leading up the route.
        // Forcing t = 1 for it instead discards the aircraft's behind-distance, so
        // the whole look-ahead is walked from the stub's far node and the target
        // steps ~25 m in one frame — the same "the walk start teleported" failure
        // the unclamped-t rule below exists to prevent. Advancing past it and
        // projecting onto the next REAL segment does neither.
        while (segIdx < segCount && SegLenM(lats, lons, segIdx) < DEGENERATE_SEG_M) segIdx++;
        if (segIdx >= segCount) return (lats[^1], lons[^1]);

        // Project the aircraft onto the current segment's axis. Clamp the
        // UPPER bound only: when the manager advances the segment at the
        // 25 m capture radius the aircraft is still BEHIND the new segment's
        // start (unclamped t < 0), and (1 − t)·segLen then correctly includes
        // that behind-distance — the walk start stays at the aircraft, and the
        // target is continuous through every capture. Clamping t to 0 would
        // teleport the walk start to the node and step the target ~25 m.
        Project(lats, lons, segIdx, acLat, acLon, out double t, out _, out double segLen);
        t = Math.Min(t, 1.0);

        double budget = lookAheadM;
        double remaining = (1.0 - t) * segLen;

        if (budget <= remaining)
        {
            double f = t + budget / segLen;
            // Never target a point BEHIND the segment start. The unclamped t is
            // what keeps the target continuous through a normal 25 m capture
            // (aircraft ~25 m behind the new segment with a >=50 m look-ahead,
            // f stays positive) — but an aircraft further behind the start than
            // the whole look-ahead drives f negative, and the extrapolated
            // point steers the pilot backwards along the axis. Clamp to the
            // start: "go to where the route resumes".
            if (f < 0.0) f = 0.0;
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
    /// True when the aircraft has demonstrably LEFT segment <paramref name="segIdx"/>
    /// past its far end and is travelling alongside a LATER segment: its along-track
    /// projection on the current segment sits at/past the end (t ≥ 1 — or the segment
    /// is degenerate), while its projection onto the next non-degenerate segment is
    /// interior (0 ≤ t ≤ 1) with cross-track within <paramref name="maxCrossM"/>.
    ///
    /// Exists to break the endpoint-tie pin (KLAS 26R, 2026-08-20): the manager's
    /// nearest-ENDPOINT advance shares the junction node between the passed segment
    /// and the next one, so the two tie forever and strict-improvement keeps the
    /// stale index — while on a long (345 m) next segment the aircraft can be
    /// squarely ON the route yet 150+ m from every endpoint, so neither the 25 m
    /// capture nor the endpoint scan can ever advance it. The walk target then
    /// freezes at (stale segment end + look-ahead) and the tone orbits the pilot
    /// around a fixed point. This projection test is the evidence the endpoint
    /// scan cannot see.
    /// </summary>
    public static bool HasPassedOntoNextSegment(
        double[] lats, double[] lons, int segIdx,
        double acLat, double acLon, double maxCrossM)
    {
        int segCount = lats.Length - 1;
        if (segIdx < 0 || segIdx >= segCount) return false;

        // Current segment: only "passed" counts. A degenerate current segment has
        // no axis to be inside of — treat it as passed (the restarted-route snap
        // stub) and let the next-segment test carry the evidence.
        // (Project returning false IS the degenerate case, so the short-circuit
        // falls through to the next-segment test exactly as intended.)
        if (Project(lats, lons, segIdx, acLat, acLon, out double curT, out _, out _)
            && curT < 1.0)
        {
            return false;
        }

        // Next non-degenerate segment: interior projection, bounded cross-track.
        int next = segIdx + 1;
        while (next < segCount && SegLenM(lats, lons, next) < DEGENERATE_SEG_M) next++;
        if (next >= segCount) return false;

        if (!Project(lats, lons, next, acLat, acLon, out double t, out double crossM, out _))
            return false;

        return t >= 0.0 && t <= 1.0 && crossM <= maxCrossM;
    }

    /// <summary>Projects the aircraft onto segment <paramref name="i"/>'s axis:
    /// UNCLAMPED along-track fraction (negative behind the start, &gt;1 past the end),
    /// absolute cross-track metres, and the segment's own length. False — with every
    /// out zeroed — when the segment is too short to have an axis at all, which is
    /// the one case no caller may treat as a position on the route.
    ///
    /// One projection, four callers (this file had four hand-rolled copies of the
    /// same cos/dx/dy/dot block): a correction to the equirectangular math now lands
    /// everywhere at once rather than in whichever copy someone remembered.</summary>
    private static bool Project(
        double[] lats, double[] lons, int i, double acLat, double acLon,
        out double t, out double crossM, out double lenM)
    {
        double cl = Math.Cos(lats[i] * Math.PI / 180.0);
        double ex = (lons[i + 1] - lons[i]) * MPD * cl;
        double ey = (lats[i + 1] - lats[i]) * MPD;
        lenM = Math.Sqrt(ex * ex + ey * ey);
        if (lenM < DEGENERATE_SEG_M)
        {
            t = 0.0;
            crossM = 0.0;
            return false;
        }

        double px = (acLon - lons[i]) * MPD * cl;
        double py = (acLat - lats[i]) * MPD;
        t = (px * ex + py * ey) / (lenM * lenM);
        crossM = Math.Abs(px * ey - py * ex) / lenM;
        return true;
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

        // Skip degenerate segments before projecting, for the same reason WalkTarget
        // does (see there): a sub-metre segment is a point, with neither an axis to
        // project onto nor a bearing to diff against. This also makes the window
        // start honest for an aircraft sitting BEHIND such a stub — forcing t = 1
        // reported travelled = 0 and began the window at the stub's far node.
        while (segIdx < segCount && SegLenM(lats, lons, segIdx) < DEGENERATE_SEG_M) segIdx++;
        if (segIdx >= segCount) return 0.0;

        // Distance from the aircraft's projection to the end of the current
        // segment — junctions are only counted within windowM of route ahead.
        // Same upper-bound-only clamp as WalkTarget (see comment there).
        Project(lats, lons, segIdx, acLat, acLon, out double t, out _, out double segLen);
        t = Math.Min(t, 1.0);

        double travelled = (1.0 - t) * segLen;   // route distance to first junction

        int b0 = segIdx;
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
