using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The sphere sums D and Shift+D make along the Fenix's route (<see cref="FenixChannelRouteDistances"/>), all in
/// nautical miles on the app's spherical earth (<see cref="NavigationCalculator"/>, radius 3440.065 NM).
/// </summary>
internal static class FenixChannelRouteGeometry
{
    // NavigationCalculator's earth radius, so an angle and a distance convert as its own distances do.
    private const double EarthRadiusNm = 3440.065;

    private const double ToRadians = Math.PI / 180.0;

    /// <summary>The great-circle distance from <paramref name="a"/> to <paramref name="b"/>: the app's own formula.</summary>
    public static double DistanceNm(FenixLatLon a, FenixLatLon b)
        => NavigationCalculator.CalculateDistance(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

    /// <summary>
    /// Where <paramref name="p"/> lies against the great circle from <paramref name="from"/> to <paramref name="to"/>
    /// (the aviation formulary's along-track and cross-track distances): <c>AlongNm</c> from <paramref name="from"/>
    /// towards <paramref name="to"/>, negative behind <paramref name="from"/>; <c>CrossNm</c> off the great circle,
    /// negative to the left of it, positive to the right.
    /// </summary>
    public static (double AlongNm, double CrossNm) Project(FenixLatLon from, FenixLatLon to, FenixLatLon p)
    {
        double d13 = DistanceNm(from, p) / EarthRadiusNm;
        double course13 = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, p.Latitude, p.Longitude);
        double course12 = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        double turn = (course13 - course12) * ToRadians;

        double cross = Math.Asin(Math.Sin(d13) * Math.Sin(turn));
        // The right spherical triangle's tan(along) = tan(d13) cos(turn), signed: negative when p is behind `from`.
        double along = Math.Atan2(Math.Sin(d13) * Math.Cos(turn), Math.Cos(d13));
        return (along * EarthRadiusNm, cross * EarthRadiusNm);
    }

    /// <summary>
    /// A turn piece's length: <paramref name="radiusNm"/> times the smaller angle between the two courses (degrees),
    /// in radians. A course change of 350 to 10 is a 20 degree turn.
    /// </summary>
    public static double ArcNm(double radiusNm, double courseIn, double courseOut)
    {
        double change = Math.Abs(courseOut - courseIn) % 360.0;
        if (change > 180.0) change = 360.0 - change;
        return radiusNm * change * ToRadians;
    }
}
