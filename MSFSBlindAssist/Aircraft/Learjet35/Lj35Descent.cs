using System.Globalization;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// The Learjet's top-of-descent readout (output mode Shift+D).
///
/// The GNS 530 has no top-of-descent point to publish: its VNAV page is a vertical-speed-
/// required calculator (target altitude, offset before a waypoint, VS profile), and the
/// Working Title unit writes the stock GPS TARGET ALTITUDE only while that page's target is
/// armed — measured live at FL350 with a 30-waypoint plan: zero. So this is an ESTIMATE, said
/// as one: a three-degree path (three miles per thousand feet, the jet's normal profile) from
/// the present altitude to the target, measured back from the destination along the route the
/// navigator reports. When the GNS has a VNAV target it is used and named; otherwise the
/// target is 1,500 feet, which is where a descent to any airfield near sea level ends and is
/// conservative for a higher one (the pilot hears the assumption, so they can correct it).
/// </summary>
public static class Lj35Descent
{
    /// <summary>Feet lost per nautical mile on a three-degree path.</summary>
    public const double FeetPerMile = 318.0;

    /// <summary>The fallback target when the GNS has none.</summary>
    public const double DefaultTargetFt = 1500.0;

    public static string ComposeTopOfDescent(double altitudeFt, double? gnsTargetAltFt, double? routeToDestinationNm)
    {
        if (routeToDestinationNm == null) return "Distance to destination not computed, so no top of descent.";

        bool hasTarget = gnsTargetAltFt is > 0;
        double target = hasTarget ? gnsTargetAltFt!.Value : DefaultTargetFt;
        double toLose = altitudeFt - target;
        if (toLose <= 500) return "Already at or below the descent target altitude.";

        double descentNm = toLose / FeetPerMile;
        double todNm = routeToDestinationNm.Value - descentNm;
        string basis = hasTarget
            ? $"three degrees to the GNS target of {Alt(target)} feet"
            : $"three degrees to {Alt(target)} feet, no GNS VNAV target set";

        if (todNm <= 0)
            return $"Past top of descent by {Nm(-todNm)}, {basis}.";
        return $"Top of descent in {Nm(todNm)}, {basis}.";
    }

    private static string Nm(double nm) => nm < 10
        ? nm.ToString("0.0", CultureInfo.InvariantCulture) + " miles"
        : nm.ToString("0", CultureInfo.InvariantCulture) + " miles";

    private static string Alt(double ft) => Math.Round(ft / 100.0) * 100 == ft
        ? ft.ToString("0", CultureInfo.InvariantCulture)
        : ft.ToString("0", CultureInfo.InvariantCulture);
}
