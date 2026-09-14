using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>How the runway an aircraft is on relates to the runway a plan was made for.</summary>
public enum LandingRunwayVerdict
{
    /// <summary>The aircraft is on the planned runway end and aligned with it. Today's behaviour.</summary>
    Matches,

    /// <summary>The aircraft is on the planned runway's own other end — the same pavement,
    /// the opposite direction.</summary>
    ReciprocalEnd,

    /// <summary>The aircraft is on, and aligned with, a different runway.</summary>
    DifferentRunway,

    /// <summary>No aligned runway pavement contains the aircraft, so the plan cannot be applied
    /// to this landing.</summary>
    Unknown,
}

/// <summary>The verdict plus the runway the aircraft is actually on: non-null for
/// <see cref="LandingRunwayVerdict.ReciprocalEnd"/> and
/// <see cref="LandingRunwayVerdict.DifferentRunway"/>, null otherwise.</summary>
public readonly record struct LandingRunwayResult(LandingRunwayVerdict Verdict, Runway? Actual);

/// <summary>
/// Decides whether a pre-selected runway is the runway the aircraft is actually on (touchdown)
/// or lined up with (flare), for <see cref="Services.LandingExitPlanner"/> and
/// <see cref="Services.LandingFlareAssistManager"/>.
///
/// <para>Motivating defect (OMDB, issue #234): the planner's runway box defaulted to 12L, the
/// pilot landed on 30L, and the rollout was measured in the 12L frame — reversed 179 degrees
/// and offset onto the other runway — so it handed off to taxi guidance on its first frame.</para>
///
/// <para>The rule: candidates are the runway ends whose pavement contains the aircraft AND whose
/// heading is within <see cref="AlignedMaxDeg"/> of the aircraft's; the best-aligned candidate
/// wins, cross-track breaking ties. Alignment is what separates runways at an intersection, where
/// the aircraft is on both pavements (KDCA 01/04). A crossing runway can never be reported as the
/// reciprocal end: that verdict requires <see cref="IsTwin"/>, i.e. the planned runway's own
/// swapped endpoints (PR #236 review, KPHL 17/27R).</para>
///
/// <para>Pure geometry, no sim state — see <c>LandingRunwayMatchTests</c>.</para>
/// </summary>
public static class LandingRunwayMatch
{
    /// <summary>How far the aircraft heading may differ from a runway's and still count as aligned
    /// with it. Touchdown crab and an unfinished de-crab stay well inside; a runway crossing at a
    /// real angle does not.</summary>
    public const double AlignedMaxDeg = 45.0;

    /// <summary>Along-track slop past the far end of the runway.</summary>
    public const double AfterEndMarginM = 50.0;

    /// <summary>Along-track slop before the runway's start at touchdown.</summary>
    public const double TouchdownBeforeThresholdMarginM = 50.0;

    /// <summary>Along-track slop before the runway's start for an airborne aircraft in the flare,
    /// which can still be short of the pavement.</summary>
    public const double ApproachBeforeThresholdMarginM = 300.0;

    /// <summary>How close each endpoint must be to the planned end's opposite endpoint to count as
    /// its twin. <c>LittleNavMapProvider</c> builds both ends from one row, so real twins match
    /// exactly; this only absorbs data noise.</summary>
    public const double TwinEndpointToleranceM = 30.0;

    /// <summary>Two <see cref="Runway"/> objects describe the same end when their ids match and
    /// their starts are this close. The planned runway comes from a different
    /// <c>GetRunways</c> call, so reference equality never applies.</summary>
    public const double SameEndToleranceM = 1.0;

    public static LandingRunwayResult Evaluate(
        double lat, double lon, double headingTrue,
        Runway planned, IReadOnlyList<Runway> allRunways,
        double beforeThresholdMarginM = TouchdownBeforeThresholdMarginM)
    {
        Runway? best = null;
        double bestDelta = double.MaxValue;
        double bestCross = double.MaxValue;
        bool plannedListed = false;

        foreach (var rwy in allRunways)
        {
            if (IsSameEnd(rwy, planned)) plannedListed = true;
            Consider(rwy);
        }
        if (!plannedListed) Consider(planned);

        if (best == null) return new LandingRunwayResult(LandingRunwayVerdict.Unknown, null);
        if (IsSameEnd(best, planned)) return new LandingRunwayResult(LandingRunwayVerdict.Matches, null);
        return IsTwin(best, planned)
            ? new LandingRunwayResult(LandingRunwayVerdict.ReciprocalEnd, best)
            : new LandingRunwayResult(LandingRunwayVerdict.DifferentRunway, best);

        void Consider(Runway rwy)
        {
            double delta = Math.Abs(NormalizeAngle(headingTrue - rwy.Heading));
            if (delta > AlignedMaxDeg) return;
            if (!IsOnRunway(lat, lon, rwy, beforeThresholdMarginM, out double cross)) return;
            if (delta > bestDelta || (delta == bestDelta && cross >= bestCross)) return;
            best = rwy;
            bestDelta = delta;
            bestCross = cross;
        }
    }

    public static bool IsSameEnd(Runway a, Runway b)
        => string.Equals(a.RunwayID, b.RunwayID, StringComparison.OrdinalIgnoreCase)
           && TaxiGraph.FastDistanceMeters(a.StartLat, a.StartLon, b.StartLat, b.StartLon) <= SameEndToleranceM;

    public static bool IsTwin(Runway candidate, Runway planned)
        => TaxiGraph.FastDistanceMeters(candidate.StartLat, candidate.StartLon, planned.EndLat, planned.EndLon) <= TwinEndpointToleranceM
           && TaxiGraph.FastDistanceMeters(candidate.EndLat, candidate.EndLon, planned.StartLat, planned.StartLon) <= TwinEndpointToleranceM;

    /// <summary>On the runway's pavement: laterally by the shared
    /// <see cref="RunwayVacateResolver.IsOffPavement"/> test (half-width + 15 m), along-track from
    /// <paramref name="beforeThresholdMarginM"/> before its start to <see cref="AfterEndMarginM"/>
    /// past its end.</summary>
    private static bool IsOnRunway(double lat, double lon, Runway rwy, double beforeThresholdMarginM,
                                   out double absCrossTrackM)
    {
        var frame = RunwayFrame.For(rwy, lat);
        absCrossTrackM = Math.Abs(frame.SignedCrossTrack(lat, lon));
        if (frame.LengthM < 1.0) return false;
        if (RunwayVacateResolver.IsOffPavement(absCrossTrackM, rwy)) return false;

        double along = frame.Along(lat, lon);
        return along >= -beforeThresholdMarginM && along <= frame.LengthM + AfterEndMarginM;
    }

    // Private copy by the Navigation convention (see RolloutExitGate.NormalizeAngle).
    private static double NormalizeAngle(double degrees)
    {
        double a = degrees % 360.0;
        if (a > 180.0) a -= 360.0;
        if (a < -180.0) a += 360.0;
        return a;
    }
}
