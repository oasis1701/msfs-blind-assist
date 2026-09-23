using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>Where an aircraft is relative to one runway.</summary>
internal enum RunwayTrafficKind { None, OnRunway, OnFinal, Landing }

internal readonly record struct RunwayTrafficFix(
    RunwayTrafficKind Kind,
    string Designator,      // OnFinal / Landing: the runway end it is landing on
    double DistanceNm);     // OnFinal: distance to that end's threshold; Landing: 0

/// <summary>A fix attributed to one runway, by index into the shapes the caller passed.</summary>
internal readonly record struct RunwayAssignment(int ShapeIndex, RunwayTrafficFix Fix);

internal static partial class GroundTrafficLogic
{
    // Final-approach classification
    public const double FinalMaxNm = 6.0;
    private const double FinalHeadingToleranceDeg = 30.0;
    private const double FinalLateralBaseM = 300.0;     // cone half-width at the threshold
    private const double FinalLateralSlope = 0.12;       // + per metre out (≈ 7°)
    private const double FinalMaxHeightPerNmFt = 500.0;  // well above a 3° path (318 ft/nm)
    private const double FinalMaxHeightBaseFt = 800.0;

    // Airborne over the pavement: the flare, a displaced threshold, the touchdown zone (R6).
    private const double LandingLateralMarginM = 60.0;
    private const double LandingMaxHeightFt = 300.0;

    /// <summary>
    /// Classifies one aircraft against one runway: on the pavement (on the ground); landing (airborne,
    /// low, over the pavement, aligned with it); on final to either end (airborne, inside the approach
    /// cone, pointing at the runway, not too high); or neither.
    /// <paramref name="heightAboveFieldFt"/> is the traffic altitude minus the field elevation.
    /// On-final distance is measured to the landing end's THRESHOLD (its paired <c>start</c> row, which
    /// sits inside the pavement at a displaced threshold), not to the pavement end.
    /// </summary>
    public static RunwayTrafficFix ClassifyAgainstRunway(
        RunwayShape shape, double lat, double lon, bool onGround,
        double headingTrue, double heightAboveFieldFt)
    {
        var none = new RunwayTrafficFix(RunwayTrafficKind.None, "", 0);
        if (shape.IsDegenerate) return none;

        var (along, lateral) = shape.Project(lat, lon);
        if (onGround)
        {
            return shape.ContainsAlongLateral(along, lateral, 0.0)
                ? new RunwayTrafficFix(RunwayTrafficKind.OnRunway, "", 0)
                : none;
        }

        double axisHdg = shape.HeadingFromEnd1Deg;
        double reciprocalHdg = (axisHdg + 180.0) % 360.0;

        // Over the pavement: landing when low, near the centreline and aligned (R6).
        if (along >= shape.ExtentMinMeters && along <= shape.ExtentMaxMeters)
        {
            if (Math.Abs(lateral) > shape.HalfWidthMeters + LandingLateralMarginM
                || heightAboveFieldFt > LandingMaxHeightFt)
                return none;
            if (Math.Abs(AngleDiff(headingTrue, axisHdg)) <= FinalHeadingToleranceDeg)
                return new RunwayTrafficFix(RunwayTrafficKind.Landing, shape.Name1, 0);
            if (Math.Abs(AngleDiff(headingTrue, reciprocalHdg)) <= FinalHeadingToleranceDeg)
                return new RunwayTrafficFix(RunwayTrafficKind.Landing, shape.Name2, 0);
            return none;
        }

        // Short of end 1, flying toward end 2 → landing on Name1.
        if (along < shape.ExtentMinMeters)
        {
            double outM = shape.ExtentMinMeters - along;
            if (IsOnFinal(outM, lateral, headingTrue, axisHdg, heightAboveFieldFt))
                return new RunwayTrafficFix(RunwayTrafficKind.OnFinal, shape.Name1,
                    ThresholdDistanceNm(shape, along, axisHdg));
        }
        // Beyond end 2, flying toward end 1 → landing on Name2.
        else
        {
            double outM = along - shape.ExtentMaxMeters;
            if (IsOnFinal(outM, lateral, headingTrue, reciprocalHdg, heightAboveFieldFt))
                return new RunwayTrafficFix(RunwayTrafficKind.OnFinal, shape.Name2,
                    ThresholdDistanceNm(shape, along, reciprocalHdg));
        }
        return none;
    }

    /// <summary>
    /// Classifies one aircraft against EVERY runway. On the ground it is on each runway whose pavement
    /// holds it (an intersection is on both). Airborne it is attributed to AT MOST ONE runway — the
    /// on-final/landing fix with the smallest lateral offset (then the smaller heading error) — so an
    /// arrival to a close parallel is never reported against the pilot's runway (R4).
    /// </summary>
    public static IReadOnlyList<RunwayAssignment> ClassifyAgainstRunways(
        IReadOnlyList<RunwayShape> shapes, double lat, double lon, bool onGround,
        double headingTrue, double heightAboveFieldFt)
    {
        var result = new List<RunwayAssignment>();
        if (shapes == null) return result;

        if (onGround)
        {
            for (int i = 0; i < shapes.Count; i++)
            {
                var fix = ClassifyAgainstRunway(shapes[i], lat, lon, true, headingTrue, heightAboveFieldFt);
                if (fix.Kind == RunwayTrafficKind.OnRunway) result.Add(new RunwayAssignment(i, fix));
            }
            return result;
        }

        int best = -1;
        RunwayTrafficFix bestFix = default;
        double bestLateral = double.MaxValue, bestHeadingError = double.MaxValue;
        for (int i = 0; i < shapes.Count; i++)
        {
            var fix = ClassifyAgainstRunway(shapes[i], lat, lon, false, headingTrue, heightAboveFieldFt);
            if (fix.Kind is not (RunwayTrafficKind.OnFinal or RunwayTrafficKind.Landing)) continue;

            double lateral = Math.Abs(shapes[i].Project(lat, lon).Lateral);
            double axis = shapes[i].HeadingFromEnd1Deg;
            double headingError = Math.Min(
                Math.Abs(AngleDiff(headingTrue, axis)),
                Math.Abs(AngleDiff(headingTrue, (axis + 180.0) % 360.0)));
            bool better = lateral < bestLateral - 0.5
                          || (Math.Abs(lateral - bestLateral) <= 0.5 && headingError < bestHeadingError);
            if (better)
            {
                best = i;
                bestFix = fix;
                bestLateral = lateral;
                bestHeadingError = headingError;
            }
        }
        if (best >= 0) result.Add(new RunwayAssignment(best, bestFix));
        return result;
    }

    private static bool IsOnFinal(double outM, double lateral, double headingTrue, double landingHdg,
                                  double heightFt)
    {
        double nm = outM / MetresPerNm;
        if (nm > FinalMaxNm) return false;
        if (Math.Abs(lateral) > FinalLateralBaseM + FinalLateralSlope * outM) return false;
        if (Math.Abs(AngleDiff(headingTrue, landingHdg)) > FinalHeadingToleranceDeg) return false;
        if (heightFt > FinalMaxHeightBaseFt + FinalMaxHeightPerNmFt * nm) return false;
        return true;
    }

    /// <summary>Along-axis distance (nm) from the aircraft to the threshold of the end it lands on.</summary>
    private static double ThresholdDistanceNm(RunwayShape shape, double along, double landingHeadingTrue)
    {
        var anchor = shape.DepartureEndFor(landingHeadingTrue);
        double thresholdAlong = shape.Project(anchor.ThresholdLat, anchor.ThresholdLon).Along;
        return Math.Abs(thresholdAlong - along) / MetresPerNm;
    }

    /// <summary>A runway status sentence opening, "Runway 27" — designators already bare.</summary>
    public static string RunwayLabel(IEnumerable<string> designators)
        => "Runway " + string.Join(" and ", designators);
}
