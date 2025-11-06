using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Navigation;
public class NavigationCalculator
{
    // Aviation constants
    private const double EARTH_RADIUS_NM = 3440.065; // Earth radius in nautical miles
    private const double DEGREES_TO_RADIANS = Math.PI / 180.0;
    private const double RADIANS_TO_DEGREES = 180.0 / Math.PI;

    /// <summary>
    /// Calculates the true bearing from point A to point B
    /// </summary>
    public static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        double lat1Rad = lat1 * DEGREES_TO_RADIANS;
        double lat2Rad = lat2 * DEGREES_TO_RADIANS;
        double deltaLonRad = (lon2 - lon1) * DEGREES_TO_RADIANS;

        double y = Math.Sin(deltaLonRad) * Math.Cos(lat2Rad);
        double x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                   Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(deltaLonRad);

        double bearingRad = Math.Atan2(y, x);
        double bearingDeg = bearingRad * RADIANS_TO_DEGREES;

        // Normalize to 0-360 degrees
        return (bearingDeg + 360.0) % 360.0;
    }

    /// <summary>
    /// Calculates the magnetic bearing from point A to point B
    /// </summary>
    /// <param name="lat1">Starting latitude in degrees</param>
    /// <param name="lon1">Starting longitude in degrees</param>
    /// <param name="lat2">Destination latitude in degrees</param>
    /// <param name="lon2">Destination longitude in degrees</param>
    /// <param name="magneticVariation">Magnetic variation in degrees (East positive, West negative)</param>
    /// <returns>Magnetic bearing in degrees (0-360)</returns>
    public static double CalculateMagneticBearing(double lat1, double lon1, double lat2, double lon2, double magneticVariation)
    {
        double trueBearing = CalculateBearing(lat1, lon1, lat2, lon2);

        // Convert true to magnetic: Magnetic = True - Variation
        double magneticBearing = trueBearing - magneticVariation;

        // Normalize to 0-360
        return (magneticBearing + 360.0) % 360.0;
    }

    /// <summary>
    /// Calculates the distance between two points in nautical miles
    /// </summary>
    public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double lat1Rad = lat1 * DEGREES_TO_RADIANS;
        double lat2Rad = lat2 * DEGREES_TO_RADIANS;
        double deltaLatRad = (lat2 - lat1) * DEGREES_TO_RADIANS;
        double deltaLonRad = (lon2 - lon1) * DEGREES_TO_RADIANS;

        double a = Math.Sin(deltaLatRad / 2) * Math.Sin(deltaLatRad / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLonRad / 2) * Math.Sin(deltaLonRad / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EARTH_RADIUS_NM * c;
    }
}
