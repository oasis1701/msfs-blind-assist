using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// What D and Shift+D say: the route distance to the destination and the distance to the top of descent, in the words of the
/// PMDGs and the FBW Airbuses ("541 miles to destination: 01:16:44"). The stock GPS flight plan variables stay empty on the
/// A300, so both come from its own VNAV L:vars. Measured in flight on 2026-10-14 (package 1.0.11):
/// <list type="bullet">
/// <item><c>FG_VNAV_DIST_DEST</c> is the route distance to the destination (its current leg plus the next, to 0.0001 NM): 541
/// in cruise, 24.6 at 8,000 feet in the descent.</item>
/// <item><c>INI_TD_DIST_TOTAL</c> is the distance from the aircraft to the top of descent: it fell at ground speed in cruise
/// (443, 239, 118) and is negative once the aircraft is past it (-81.2 at 8,000 feet, 24.6 miles from the destination).</item>
/// <item><c>INI_TD_DIST</c> is not used: it is a figure within a leg (97.7, then 13.2, then 1.2), and equals the destination
/// distance less <c>INI_TD_DIST_TOTAL</c> only while the top of descent sat on the leg that ends at the destination.</item>
/// </list>
/// Not measured: the value with no top of descent worked out, taken to be exactly 0, as an L:var nothing has written reads.
/// With no route loaded every variable reads 0, which is no distance: the phrases say "not available" then, never "0 miles".
/// The time is <see cref="BaseAircraftDefinition.FormatEtaFromDistance"/>, as on the PMDGs. Pure.
/// </summary>
public static class A300RouteDistances
{
    public const string DestinationKey = "A300_RO_DIST_DEST";
    public const string DestinationVar = "FG_VNAV_DIST_DEST";
    public const string TopOfDescentKey = "A300_RO_DIST_TD";
    public const string TopOfDescentVar = "INI_TD_DIST_TOTAL";

    /// <summary>Half a mile, in miles: a destination nearer than this is no route to speak of (no route reads 0), and a top of
    /// descent this close or behind is past (the FBW Airbuses' limit for the top of descent).</summary>
    private const double MinimumNm = 0.5;

    /// <summary>D: "541 miles to destination: 01:16:44", the time left out at a ground speed too low for it to mean anything.</summary>
    public static string Destination(double destinationNm, double groundSpeedKnots) =>
        HasRoute(destinationNm)
            ? Miles(destinationNm, "destination", groundSpeedKnots)
            : "Distance to destination not available";

    /// <summary>
    /// Shift+D: "443 miles to top of descent: 01:02:53". No route (the destination distance is what says there is one) is
    /// "not available"; a top of descent reading exactly 0 has not been worked out yet; one half a mile ahead or less, or behind
    /// (a negative reading), is past it.
    /// </summary>
    public static string TopOfDescent(double destinationNm, double toTopOfDescentNm, double groundSpeedKnots)
    {
        if (!HasRoute(destinationNm) || !double.IsFinite(toTopOfDescentNm))
            return "Top of descent not available";
        if (toTopOfDescentNm == 0)
            return "Top of descent not yet computed";
        return toTopOfDescentNm <= MinimumNm
            ? "Past top of descent"
            : Miles(toTopOfDescentNm, "top of descent", groundSpeedKnots);
    }

    /// <summary>Whether the destination distance is one: at least <see cref="MinimumNm"/>. Written so that a value that is no
    /// number (NaN, infinity) is none, and is never spoken.</summary>
    private static bool HasRoute(double destinationNm) => double.IsFinite(destinationNm) && destinationNm >= MinimumNm;

    /// <summary>"541 miles to destination: 01:16:44". The miles round half away from zero: "F0" rounds an exact half to even,
    /// which would say "0 miles" at exactly <see cref="MinimumNm"/>, the least that passes for a distance.</summary>
    private static string Miles(double nm, string to, double groundSpeedKnots) =>
        $"{Math.Round(nm, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture)} miles to {to}" +
        BaseAircraftDefinition.FormatEtaFromDistance(nm, groundSpeedKnots);
}
