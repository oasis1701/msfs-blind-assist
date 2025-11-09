using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Navigation;
public class NavigationCalculator
{
    // Aviation constants
    private const double EARTH_RADIUS_NM = 3440.065; // Earth radius in nautical miles
    private const double EARTH_RADIUS_FEET = 20902231.0; // Earth radius in feet
    private const double DEGREES_TO_RADIANS = Math.PI / 180.0;
    private const double RADIANS_TO_DEGREES = 180.0 / Math.PI;
    private const double LOCALIZER_TOLERANCE_DEGREES = 2.0; // ILS on-centerline tolerance

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

    /// <summary>
    /// Calculates cross-track error (degrees left or right of ILS centerline)
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerHeading">Localizer true heading in degrees</param>
    /// <returns>Degrees off centerline (negative = left, positive = right)</returns>
    public static double CalculateCrossTrackError(double aircraftLat, double aircraftLon,
                                                   double runwayThresholdLat, double runwayThresholdLon,
                                                   double localizerHeading)
    {
        // Calculate bearing from threshold to aircraft
        double bearingToAircraft = CalculateBearing(runwayThresholdLat, runwayThresholdLon,
                                                     aircraftLat, aircraftLon);

        // Calculate angle difference from localizer centerline
        double deviation = bearingToAircraft - localizerHeading;

        // Normalize to -180 to +180 range
        while (deviation > 180) deviation -= 360;
        while (deviation < -180) deviation += 360;

        return deviation;
    }

    /// <summary>
    /// Calculates geometric intercept heading to join ILS centerline.
    /// Uses actual position to calculate the true bearing to the nearest point on the centerline.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerHeading">Localizer true heading in degrees</param>
    /// <param name="crossTrackError">Cross-track error in degrees (from CalculateCrossTrackError) - not used in geometric calculation but kept for API compatibility</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>Magnetic intercept heading in degrees (0-360)</returns>
    public static double CalculateInterceptHeading(double aircraftLat, double aircraftLon,
                                                    double runwayThresholdLat, double runwayThresholdLon,
                                                    double localizerHeading, double crossTrackError,
                                                    double magneticVariation)
    {
        // Calculate the perpendicular intercept point on the localizer centerline
        // This is the point on the extended centerline closest to the aircraft
        var (interceptLat, interceptLon) = CalculatePerpendicularInterceptPoint(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerHeading);

        // Calculate true bearing from aircraft to the intercept point
        // This gives us a heading that geometrically points TO the centerline
        double trueBearing = CalculateBearing(
            aircraftLat, aircraftLon,
            interceptLat, interceptLon);

        // Convert true bearing to magnetic heading
        double magneticInterceptHeading = trueBearing - magneticVariation;

        // Normalize to 0-360
        return (magneticInterceptHeading + 360.0) % 360.0;
    }

    /// <summary>
    /// Calculates glideslope deviation in feet (above or below glideslope)
    /// Uses proper 3D geometry and accounts for Earth's curvature for accuracy at 10-15 NM intercept range.
    /// </summary>
    /// <param name="aircraftAltitudeMSL">Aircraft altitude in feet MSL</param>
    /// <param name="distanceFromThresholdNM">Distance from runway threshold in nautical miles</param>
    /// <param name="glideslopePitch">Glideslope angle in degrees (typically 3.0)</param>
    /// <param name="thresholdElevationMSL">Runway threshold elevation in feet MSL</param>
    /// <param name="glideslopeAntennaLat">Optional glideslope antenna latitude</param>
    /// <param name="glideslopeAntennaLon">Optional glideslope antenna longitude</param>
    /// <param name="glideslopeAntennaAltMSL">Optional glideslope antenna altitude MSL</param>
    /// <param name="aircraftLat">Aircraft latitude (required if using antenna position)</param>
    /// <param name="aircraftLon">Aircraft longitude (required if using antenna position)</param>
    /// <returns>Feet above (+) or below (-) glideslope</returns>
    public static double CalculateGlideslopeDeviation(double aircraftAltitudeMSL,
                                                       double distanceFromThresholdNM,
                                                       double glideslopePitch,
                                                       double thresholdElevationMSL,
                                                       double? glideslopeAntennaLat = null,
                                                       double? glideslopeAntennaLon = null,
                                                       int? glideslopeAntennaAltMSL = null,
                                                       double? aircraftLat = null,
                                                       double? aircraftLon = null)
    {
        double expectedAltitudeMSL;

        // Hybrid approach: use antenna position if available, otherwise use threshold
        if (glideslopeAntennaLat.HasValue && glideslopeAntennaLon.HasValue &&
            glideslopeAntennaAltMSL.HasValue && aircraftLat.HasValue && aircraftLon.HasValue)
        {
            // Calculate horizontal distance using great circle (accounts for Earth's curvature)
            double horizontalDistanceNM = CalculateDistance(aircraftLat.Value, aircraftLon.Value,
                                                             glideslopeAntennaLat.Value, glideslopeAntennaLon.Value);
            double horizontalDistanceFeet = horizontalDistanceNM * 6076.12; // NM to feet

            // Calculate Earth curvature correction at this distance
            // At longer distances, the Earth curves away, making the glideslope appear higher
            double curvatureCorrectionFeet = (horizontalDistanceFeet * horizontalDistanceFeet) / (2.0 * EARTH_RADIUS_FEET);

            // Calculate expected altitude along the glideslope with curvature compensation
            // The glideslope follows the Earth's curve, so we add the curvature correction
            double glideslopeRise = horizontalDistanceFeet * Math.Tan(glideslopePitch * DEGREES_TO_RADIANS);

            expectedAltitudeMSL = glideslopeAntennaAltMSL.Value + glideslopeRise + curvatureCorrectionFeet;
        }
        else
        {
            // Calculate from threshold using standard slope with curvature compensation
            double horizontalDistanceFeet = distanceFromThresholdNM * 6076.12; // Convert NM to feet

            // Calculate Earth curvature correction
            double curvatureCorrectionFeet = (horizontalDistanceFeet * horizontalDistanceFeet) / (2.0 * EARTH_RADIUS_FEET);

            // Calculate expected altitude along the curved glideslope path
            double glideslopeRise = horizontalDistanceFeet * Math.Tan(glideslopePitch * DEGREES_TO_RADIANS);

            expectedAltitudeMSL = thresholdElevationMSL + glideslopeRise + curvatureCorrectionFeet;
        }

        // Return deviation (positive = above glideslope, negative = below)
        return aircraftAltitudeMSL - expectedAltitudeMSL;
    }

    /// <summary>
    /// Checks if aircraft is within ILS localizer range
    /// </summary>
    /// <param name="distanceFromThresholdNM">Distance from runway threshold in nautical miles</param>
    /// <param name="ilsRangeNM">ILS range from database in nautical miles</param>
    /// <returns>True if within range, false otherwise</returns>
    public static bool IsWithinILSRange(double distanceFromThresholdNM, int ilsRangeNM)
    {
        return distanceFromThresholdNM <= ilsRangeNM;
    }

    /// <summary>
    /// Checks if aircraft is within glideslope range
    /// </summary>
    /// <param name="distanceFromThresholdNM">Distance from runway threshold in nautical miles</param>
    /// <param name="glideslopeRangeNM">Glideslope range from database in nautical miles</param>
    /// <returns>True if within range, false otherwise</returns>
    public static bool IsWithinGlideslopeRange(double distanceFromThresholdNM, int glideslopeRangeNM)
    {
        return distanceFromThresholdNM <= glideslopeRangeNM;
    }

    /// <summary>
    /// Checks if aircraft is approaching runway from behind (wrong direction)
    /// Uses both heading and position checks
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="aircraftMagneticHeading">Aircraft magnetic heading in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="runwayEndLat">Runway end latitude in degrees</param>
    /// <param name="runwayEndLon">Runway end longitude in degrees</param>
    /// <param name="localizerTrueHeading">Localizer true heading in degrees</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>True if approaching from behind, false otherwise</returns>
    public static bool IsApproachingFromBehind(double aircraftLat, double aircraftLon,
                                                double aircraftMagneticHeading,
                                                double runwayThresholdLat, double runwayThresholdLon,
                                                double runwayEndLat, double runwayEndLon,
                                                double localizerTrueHeading,
                                                double magneticVariation)
    {
        // Check 1: Is aircraft heading within ±90° of runway reciprocal heading?
        double runwayMagneticHeading = localizerTrueHeading - magneticVariation;
        runwayMagneticHeading = (runwayMagneticHeading + 360.0) % 360.0;

        double reciprocalHeading = (runwayMagneticHeading + 180.0) % 360.0;
        double headingDifference = Math.Abs(aircraftMagneticHeading - reciprocalHeading);

        // Normalize to 0-180 range
        if (headingDifference > 180) headingDifference = 360 - headingDifference;

        bool headingCheck = headingDifference <= 90.0;

        // Check 2: Is aircraft behind the runway start threshold?
        // Calculate bearing from aircraft to threshold
        double bearingToThreshold = CalculateBearing(aircraftLat, aircraftLon,
                                                     runwayThresholdLat, runwayThresholdLon);

        // If bearing to threshold is roughly aligned with localizer heading (within ±90°),
        // then aircraft is in front. If opposite, aircraft is behind.
        double bearingDifference = Math.Abs(bearingToThreshold - localizerTrueHeading);
        if (bearingDifference > 180) bearingDifference = 360 - bearingDifference;

        bool positionCheck = bearingDifference > 90.0;

        // Both checks must be true for "approaching from behind"
        return headingCheck && positionCheck;
    }

    /// <summary>
    /// Checks if aircraft is on ILS localizer centerline (within tolerance)
    /// </summary>
    /// <param name="crossTrackError">Cross-track error in degrees (from CalculateCrossTrackError)</param>
    /// <returns>True if on centerline (within ±2°), false otherwise</returns>
    public static bool IsOnLocalizer(double crossTrackError)
    {
        return Math.Abs(crossTrackError) <= LOCALIZER_TOLERANCE_DEGREES;
    }

    /// <summary>
    /// Calculates extension heading for repositioning when too close to threshold.
    /// Returns the reciprocal of the runway heading to fly away from the runway.
    /// </summary>
    /// <param name="localizerTrueHeading">Localizer true heading in degrees</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>Magnetic heading to fly for extension (reciprocal of runway heading)</returns>
    public static double CalculateExtensionHeading(double localizerTrueHeading, double magneticVariation)
    {
        // Convert localizer true heading to magnetic
        double runwayMagneticHeading = localizerTrueHeading - magneticVariation;
        runwayMagneticHeading = (runwayMagneticHeading + 360.0) % 360.0;

        // Calculate reciprocal (opposite direction)
        double extensionHeading = (runwayMagneticHeading + 180.0) % 360.0;

        return extensionHeading;
    }

    /// <summary>
    /// Calculates intercept heading to join the localizer at a specific target distance from threshold.
    /// Used for aircraft far from the runway (Zone 3) to optimize approach positioning.
    /// Creates a shallow intercept angle (~30°) by aiming for a point closer to threshold.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerTrueHeading">Localizer true heading in degrees</param>
    /// <param name="targetInterceptDistanceNM">Target distance from threshold to intercept (e.g., 12 NM)</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>Magnetic heading to fly to intercept at target distance</returns>
    public static double CalculateTargetedInterceptHeading(double aircraftLat, double aircraftLon,
                                                            double runwayThresholdLat, double runwayThresholdLon,
                                                            double localizerTrueHeading,
                                                            double targetInterceptDistanceNM,
                                                            double magneticVariation)
    {
        // Strategy: To create a shallow intercept angle (~30°), aim for a point on the centerline
        // that's closer to the threshold than the target distance. This naturally creates
        // an intercept angle as the aircraft approaches the target intercept point.

        // Calculate aim point distance (closer to threshold to create intercept angle)
        // Use 2/3 of target distance as aim point to create ~30° approach
        double aimPointDistanceNM = targetInterceptDistanceNM * 0.67;

        // Convert the aim point distance to angular distance
        double aimDistanceRad = aimPointDistanceNM / EARTH_RADIUS_NM;

        // Convert threshold position and localizer heading to radians
        double thresholdLatRad = runwayThresholdLat * DEGREES_TO_RADIANS;
        double thresholdLonRad = runwayThresholdLon * DEGREES_TO_RADIANS;
        double locHeadingRad = localizerTrueHeading * DEGREES_TO_RADIANS;

        // Calculate the aim point on the centerline using destination point formula
        // Project along the localizer heading from threshold
        double aimLat = Math.Asin(
            Math.Sin(thresholdLatRad) * Math.Cos(aimDistanceRad) +
            Math.Cos(thresholdLatRad) * Math.Sin(aimDistanceRad) * Math.Cos(locHeadingRad)
        );

        double aimLon = thresholdLonRad + Math.Atan2(
            Math.Sin(locHeadingRad) * Math.Sin(aimDistanceRad) * Math.Cos(thresholdLatRad),
            Math.Cos(aimDistanceRad) - Math.Sin(thresholdLatRad) * Math.Sin(aimLat)
        );

        // Convert back to degrees
        double aimLatDeg = aimLat * RADIANS_TO_DEGREES;
        double aimLonDeg = aimLon * RADIANS_TO_DEGREES;

        // Calculate magnetic bearing from aircraft to aim point
        // By aiming for a point closer than the target (8 NM vs 12 NM), we create
        // a heading that will intercept the localizer at a shallow angle near the target distance
        double magneticHeading = CalculateMagneticBearing(
            aircraftLat, aircraftLon,
            aimLatDeg, aimLonDeg,
            magneticVariation
        );

        return magneticHeading;
    }

    /// <summary>
    /// Calculates perpendicular distance from aircraft to the ILS localizer centerline.
    /// This is the shortest distance from the aircraft's current position to the extended centerline.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerHeading">Localizer true heading in degrees</param>
    /// <returns>Perpendicular distance to centerline in nautical miles</returns>
    public static double CalculateDistanceToLocalizer(double aircraftLat, double aircraftLon,
                                                       double runwayThresholdLat, double runwayThresholdLon,
                                                       double localizerHeading)
    {
        // Calculate the perpendicular intercept point on the centerline
        // This is the point on the extended centerline closest to the aircraft
        var (interceptLat, interceptLon) = CalculatePerpendicularInterceptPoint(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerHeading);

        // Calculate distance from aircraft to that perpendicular point
        // This gives us the shortest distance to the localizer centerline
        return CalculateDistance(aircraftLat, aircraftLon, interceptLat, interceptLon);
    }

    /// <summary>
    /// Calculates the perpendicular intercept point on the localizer centerline.
    /// This is the point on the extended centerline that is closest to the aircraft's current position.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="thresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="thresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerHeading">Localizer true heading in degrees</param>
    /// <returns>Tuple of (latitude, longitude) for the intercept point on the centerline</returns>
    private static (double, double) CalculatePerpendicularInterceptPoint(
        double aircraftLat, double aircraftLon,
        double thresholdLat, double thresholdLon,
        double localizerHeading)
    {
        // Convert to radians
        double lat1 = thresholdLat * DEGREES_TO_RADIANS;
        double lon1 = thresholdLon * DEGREES_TO_RADIANS;
        double lat2 = aircraftLat * DEGREES_TO_RADIANS;
        double lon2 = aircraftLon * DEGREES_TO_RADIANS;
        double locHeadingRad = localizerHeading * DEGREES_TO_RADIANS;

        // Calculate distance from threshold to aircraft (angular distance)
        double deltaLat = lat2 - lat1;
        double deltaLon = lon2 - lon1;
        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1) * Math.Cos(lat2) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        double angularDistance = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        // Calculate bearing from threshold to aircraft
        double y = Math.Sin(deltaLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) -
                   Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
        double bearingToAircraft = Math.Atan2(y, x);

        // Calculate along-track distance (ATD) - distance along the localizer from threshold
        // to the perpendicular point
        double alongTrackDistance = Math.Asin(Math.Sin(angularDistance) *
                                               Math.Sin(bearingToAircraft - locHeadingRad));

        // Project the along-track distance from threshold along the localizer heading
        // to find the intercept point coordinates
        double interceptLat = Math.Asin(Math.Sin(lat1) * Math.Cos(alongTrackDistance) +
                                         Math.Cos(lat1) * Math.Sin(alongTrackDistance) * Math.Cos(locHeadingRad));

        double interceptLon = lon1 + Math.Atan2(Math.Sin(locHeadingRad) * Math.Sin(alongTrackDistance) * Math.Cos(lat1),
                                                 Math.Cos(alongTrackDistance) - Math.Sin(lat1) * Math.Sin(interceptLat));

        // Convert back to degrees
        return (interceptLat * RADIANS_TO_DEGREES, interceptLon * RADIANS_TO_DEGREES);
    }
}
