using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>Where an aircraft is relative to one runway.</summary>
internal enum RunwayTrafficKind { None, OnRunway, OnFinal }

internal readonly record struct RunwayTrafficFix(
    RunwayTrafficKind Kind,
    string Designator,      // for OnFinal: the runway end it is landing on
    double DistanceNm);     // for OnFinal: distance to that threshold

internal static partial class GroundTrafficLogic
{
    // Final-approach classification
    public const double FinalMaxNm = 6.0;
    private const double FinalHeadingToleranceDeg = 30.0;
    private const double FinalLateralBaseM = 300.0;     // cone half-width at the threshold
    private const double FinalLateralSlope = 0.12;       // + per metre out (≈ 7°)
    private const double FinalMaxHeightPerNmFt = 500.0;  // well above a 3° path (318 ft/nm)
    private const double FinalMaxHeightBaseFt = 800.0;

    /// <summary>
    /// Classifies one aircraft against one runway: on the pavement, on final to either end
    /// (airborne, inside the approach cone, pointing at the runway, not too high), or neither.
    /// <paramref name="heightAboveFieldFt"/> is the traffic altitude minus the field elevation.
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

        double axisHdg = NavigationCalculator.CalculateBearing(shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2);

        // Short of end 1, flying toward end 2 → landing on Name1.
        if (along < shape.ExtentMinMeters)
        {
            double outM = shape.ExtentMinMeters - along;
            if (IsOnFinal(outM, lateral, headingTrue, axisHdg, heightAboveFieldFt))
                return new RunwayTrafficFix(RunwayTrafficKind.OnFinal, shape.Name1, outM / MetresPerNm);
        }
        // Beyond end 2, flying toward end 1 → landing on Name2.
        else if (along > shape.ExtentMaxMeters)
        {
            double outM = along - shape.ExtentMaxMeters;
            if (IsOnFinal(outM, lateral, headingTrue, (axisHdg + 180.0) % 360.0, heightAboveFieldFt))
                return new RunwayTrafficFix(RunwayTrafficKind.OnFinal, shape.Name2, outM / MetresPerNm);
        }
        return none;
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

    /// <summary>A runway status sentence opening, "Runway 27" — designators already bare.</summary>
    public static string RunwayLabel(IEnumerable<string> designators)
        => "Runway " + string.Join(" and ", designators);
}
