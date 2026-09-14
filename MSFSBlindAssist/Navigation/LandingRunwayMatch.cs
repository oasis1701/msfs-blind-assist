using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>How the runway an aircraft actually touched down on relates to the runway its
/// landing-exit plan was made for.</summary>
public enum LandingRunwayVerdict
{
    /// <summary>The aircraft is on the planned runway, rolling its way. Today's behaviour.</summary>
    Matches,

    /// <summary>Same pavement, opposite direction — the plan's exit taxiway is still the
    /// right pavement, measured from the other threshold.</summary>
    ReciprocalEnd,

    /// <summary>The aircraft is on a DIFFERENT runway. The plan's exit belongs to pavement
    /// it is not on, so there is nothing to re-measure.</summary>
    DifferentRunway,

    /// <summary>No runway could be identified under the aircraft, or the correction could
    /// not be resolved to a runway the caller can act on. Keep today's behaviour.</summary>
    Unknown,
}

/// <summary>The verdict plus the runway the aircraft is actually rolling down, non-null for
/// <see cref="LandingRunwayVerdict.ReciprocalEnd"/> and
/// <see cref="LandingRunwayVerdict.DifferentRunway"/> and null otherwise.</summary>
public readonly record struct LandingRunwayResult(LandingRunwayVerdict Verdict, Runway? Actual);

/// <summary>
/// Decides, at touchdown, whether the landing-exit plan's runway is the runway the aircraft
/// is actually on — the question <see cref="Services.LandingExitPlanner"/> did not ask.
///
/// <para>Motivating defect (OMDB, 2026-09-12 and 2026-09-06): the planner's runway combo
/// defaults to the first runway at the airport, which at OMDB is 12L. The pilot planned exit
/// M13 against it and then landed on 30L — a different physical runway, and the reciprocal
/// direction as well. `ActivateGuidance` handed the 12L frame to `BeginLandingRollout`
/// unchecked, so `UpdateLandingRollout`'s very first frame measured `hdgDelta=179.2deg`,
/// `signedAlongPast=+4570ft` and `lateral=1263ft`, concluded `pastExit` AND
/// `exitedLaterally`, and handed off to Taxiing before a single rollout callout had been
/// made. The pilot heard taxiway names where the touchdown call, the 1500/500 ft approach
/// calls and the turn cue should have been.</para>
///
/// <para>The rule is deliberately blunt: a rollout frame is only usable if the aircraft is
/// ON that runway's pavement and rolling ALONG it. Everything else is reported to the
/// caller, which announces what happened rather than silently measuring in a frame that
/// does not describe the aircraft.</para>
///
/// <para>Pure geometry, no sim state — see <c>LandingRunwayMatchTests</c>.</para>
/// </summary>
public static class LandingRunwayMatch
{
    /// <summary>Lateral slop beyond the runway half-width. A wide-body's centre of mass can
    /// sit a few metres off the painted centreline and the navdata's own width is a
    /// rounded figure, so the corridor is widened rather than taken literally. Same
    /// half-width + margin idiom as <see cref="TaxiGraph.GraphWalkToRunwayPavement"/>.</summary>
    internal const double LateralMarginM = 15.0;

    /// <summary>Along-track slop at each end. A touchdown just short of the threshold, or a
    /// rollout that runs to the very end, is still on the runway.</summary>
    internal const double AlongMarginM = 50.0;

    /// <summary>Half-width used when the navdata carries no runway width. 75 ft is the
    /// HALF-width <see cref="TaxiGraph"/>'s centerline builder defaults to for the same
    /// reason (it covers most Code C/D/E runways), so it is not halved again here.</summary>
    private const double DefaultHalfWidthM = 75.0 * 0.3048; // 22.86 m

    /// <summary>
    /// How far the aircraft heading may differ from the runway heading and still count as
    /// rolling THAT way. 90 degrees, not a tight tolerance: this separates "down the runway"
    /// from "down the reciprocal", and touchdown yaw, crosswind crab and a de-crab that has
    /// not finished can all leave a real landing several degrees off. The reported failure
    /// sat at 179.2 degrees, so nothing near the boundary is being decided here.
    /// </summary>
    internal const double SameDirectionMaxDeg = 90.0;

    public static LandingRunwayResult Evaluate(
        double lat, double lon, double headingTrue,
        Runway planned, IReadOnlyList<Runway> allRunways)
    {
        if (planned == null) return new LandingRunwayResult(LandingRunwayVerdict.Unknown, null);

        // On the planned runway's own pavement? That settles it either way: same direction is
        // today's behaviour, opposite direction is the recoverable case.
        if (IsOnPavement(lat, lon, planned))
        {
            if (IsRollingAlong(headingTrue, planned))
                return new LandingRunwayResult(LandingRunwayVerdict.Matches, null);

            var reciprocal = FindBest(lat, lon, headingTrue, allRunways);
            // Never report a correction the caller cannot act on: without the other end's
            // row there is no frame to re-measure the exits from.
            return reciprocal == null
                ? new LandingRunwayResult(LandingRunwayVerdict.Unknown, null)
                : new LandingRunwayResult(LandingRunwayVerdict.ReciprocalEnd, reciprocal);
        }

        var actual = FindBest(lat, lon, headingTrue, allRunways);
        return actual == null
            ? new LandingRunwayResult(LandingRunwayVerdict.Unknown, null)
            : new LandingRunwayResult(LandingRunwayVerdict.DifferentRunway, actual);
    }

    /// <summary>
    /// The runway END the aircraft is on and rolling towards, nearest the centreline first.
    /// Direction is part of the test, not a tie-break: the two ends of one runway occupy the
    /// same pavement, so without it the answer is a coin toss — and the caller measures the
    /// runway-end countdown from whichever end it is handed.
    ///
    /// <para>The planned runway is deliberately NOT excluded, and must not be "excluded"
    /// by REFERENCE: the caller's <c>planned</c> comes from a DIFFERENT <c>GetRunways</c>
    /// call than the list passed here, so the two are equal-valued but never the same
    /// instance and a <c>ReferenceEquals</c> guard is dead code advertising a filter it
    /// does not apply. No filter is needed: both call sites have already ruled the planned
    /// runway out on its own geometry — one because the aircraft is not on its pavement,
    /// the other because it is not rolling its way — and this method re-applies both
    /// tests, so the planned runway cannot win either search.</para>
    /// </summary>
    private static Runway? FindBest(
        double lat, double lon, double headingTrue, IReadOnlyList<Runway> allRunways)
    {
        Runway? best = null;
        double bestCross = double.MaxValue;

        foreach (var rwy in allRunways)
        {
            if (!IsRollingAlong(headingTrue, rwy)) continue;
            if (!IsOnPavement(lat, lon, rwy)) continue;

            double cross = Math.Abs(RunwayFrame.For(rwy, lat).SignedCrossTrack(lat, lon));
            if (cross >= bestCross) continue;
            bestCross = cross;
            best = rwy;
        }

        return best;
    }

    private static bool IsOnPavement(double lat, double lon, Runway rwy)
    {
        var frame = RunwayFrame.For(rwy, lat);
        double lengthM = frame.LengthM;
        if (lengthM < 1.0) return false;

        double halfWidth = rwy.Width > 0 ? rwy.Width * 0.3048 * 0.5 : DefaultHalfWidthM;
        if (Math.Abs(frame.SignedCrossTrack(lat, lon)) > halfWidth + LateralMarginM) return false;

        double along = frame.Along(lat, lon);
        return along >= -AlongMarginM && along <= lengthM + AlongMarginM;
    }

    private static bool IsRollingAlong(double headingTrue, Runway rwy)
        => Math.Abs(NormalizeAngle(headingTrue - rwy.Heading)) <= SameDirectionMaxDeg;

    private static double NormalizeAngle(double degrees)
    {
        double a = degrees % 360.0;
        if (a > 180.0) a -= 360.0;
        if (a < -180.0) a += 360.0;
        return a;
    }
}
