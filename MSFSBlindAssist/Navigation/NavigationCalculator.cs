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
    /// Calculates touchdown aim point coordinates by projecting a distance from threshold along runway heading.
    /// Used for glideslope calculations to ensure safe touchdown zone (not at threshold).
    /// </summary>
    /// <param name="thresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="thresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="runwayTrueHeading">Runway true heading in degrees</param>
    /// <param name="touchdownDistanceFeet">Distance from threshold to aim point in feet</param>
    /// <returns>Tuple of (latitude, longitude) for touchdown aim point</returns>
    public static (double lat, double lon) CalculateTouchdownAimPoint(
        double thresholdLat,
        double thresholdLon,
        double runwayTrueHeading,
        double touchdownDistanceFeet)
    {
        // Convert feet to nautical miles
        double distanceNM = touchdownDistanceFeet / 6076.12;

        // Convert to radians
        double distanceRad = distanceNM / EARTH_RADIUS_NM;
        double thresholdLatRad = thresholdLat * DEGREES_TO_RADIANS;
        double thresholdLonRad = thresholdLon * DEGREES_TO_RADIANS;
        double headingRad = runwayTrueHeading * DEGREES_TO_RADIANS;

        // Project point along runway heading using destination point formula
        double aimLatRad = Math.Asin(
            Math.Sin(thresholdLatRad) * Math.Cos(distanceRad) +
            Math.Cos(thresholdLatRad) * Math.Sin(distanceRad) * Math.Cos(headingRad)
        );

        double aimLonRad = thresholdLonRad + Math.Atan2(
            Math.Sin(headingRad) * Math.Sin(distanceRad) * Math.Cos(thresholdLatRad),
            Math.Cos(distanceRad) - Math.Sin(thresholdLatRad) * Math.Sin(aimLatRad)
        );

        // Convert back to degrees
        return (aimLatRad * RADIANS_TO_DEGREES, aimLonRad * RADIANS_TO_DEGREES);
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
        // Safety check: Don't make determination if too close (bearing becomes unstable)
        const double MINIMUM_DISTANCE_NM = 0.1; // ~600 feet
        double distanceToThreshold = CalculateDistance(aircraftLat, aircraftLon,
                                                        runwayThresholdLat, runwayThresholdLon);

        if (distanceToThreshold < MINIMUM_DISTANCE_NM)
        {
            return false; // Too close to make reliable bearing determination
        }

        // Check if aircraft is behind the runway start threshold (position only, heading doesn't matter)
        // Calculate bearing from aircraft to threshold
        double bearingToThreshold = CalculateBearing(aircraftLat, aircraftLon,
                                                     runwayThresholdLat, runwayThresholdLon);

        // If bearing to threshold is opposite to localizer heading (difference > 90°),
        // then aircraft is behind threshold (on the departure/wrong side for landing)
        double bearingDifference = Math.Abs(bearingToThreshold - localizerTrueHeading);
        if (bearingDifference > 180) bearingDifference = 360 - bearingDifference;

        // Return true if aircraft position is behind threshold
        return bearingDifference > 90.0;
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
    /// Calculates intercept heading to join the localizer centerline at a specified angle.
    /// Finds the perpendicular point on centerline, projects forward along centerline,
    /// then calculates heading from aircraft to that aim point.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerTrueHeading">Localizer true heading in degrees</param>
    /// <param name="targetInterceptAngle">Desired intercept angle in degrees (e.g., 30, 45, 60)</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>Magnetic heading to fly for specified intercept angle</returns>
    public static double CalculateAngledInterceptHeading(double aircraftLat, double aircraftLon,
                                                          double runwayThresholdLat, double runwayThresholdLon,
                                                          double localizerTrueHeading,
                                                          double targetInterceptAngle,
                                                          double magneticVariation)
    {
        // Get perpendicular distance to centerline
        double perpendicularDistance = CalculateDistanceToLocalizer(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerTrueHeading);

        // If already on centerline, just return the localizer heading
        if (perpendicularDistance < 0.05) // Less than ~300 feet
        {
            return CalculateMagneticBearing(
                aircraftLat, aircraftLon,
                runwayThresholdLat, runwayThresholdLon,
                magneticVariation);
        }

        // Calculate how far ahead along centerline to place aim point
        // Using: distance_ahead = perpendicular_distance / tan(intercept_angle)
        double interceptAngleRad = targetInterceptAngle * DEGREES_TO_RADIANS;
        double distanceAheadNM = perpendicularDistance / Math.Tan(interceptAngleRad);

        // Get perpendicular intercept point on centerline
        var (interceptLat, interceptLon) = CalculatePerpendicularInterceptPoint(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerTrueHeading);

        // Project forward along centerline from perpendicular point to get aim point
        double distanceAheadRad = distanceAheadNM / EARTH_RADIUS_NM;
        double interceptLatRad = interceptLat * DEGREES_TO_RADIANS;
        double interceptLonRad = interceptLon * DEGREES_TO_RADIANS;
        double locHeadingRad = localizerTrueHeading * DEGREES_TO_RADIANS;

        // Calculate aim point coordinates using destination point formula
        double aimLat = Math.Asin(
            Math.Sin(interceptLatRad) * Math.Cos(distanceAheadRad) +
            Math.Cos(interceptLatRad) * Math.Sin(distanceAheadRad) * Math.Cos(locHeadingRad)
        );

        double aimLon = interceptLonRad + Math.Atan2(
            Math.Sin(locHeadingRad) * Math.Sin(distanceAheadRad) * Math.Cos(interceptLatRad),
            Math.Cos(distanceAheadRad) - Math.Sin(interceptLatRad) * Math.Sin(aimLat)
        );

        // Convert back to degrees
        double aimLatDeg = aimLat * RADIANS_TO_DEGREES;
        double aimLonDeg = aimLon * RADIANS_TO_DEGREES;

        // Calculate magnetic heading from aircraft to aim point
        return CalculateMagneticBearing(
            aircraftLat, aircraftLon,
            aimLatDeg, aimLonDeg,
            magneticVariation);
    }

    /// <summary>
    /// Calculates three intercept headings to visualize the localizer centerline.
    /// Provides direct (steep), medium, and shallow intercept options.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="runwayThresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="runwayThresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerTrueHeading">Localizer true heading in degrees</param>
    /// <param name="magneticVariation">Magnetic variation in degrees</param>
    /// <returns>Tuple of (directHeading, mediumHeading, shallowHeading) in magnetic degrees</returns>
    public static (double directHeading, double mediumHeading, double shallowHeading)
        CalculateThreeInterceptHeadings(double aircraftLat, double aircraftLon,
                                        double runwayThresholdLat, double runwayThresholdLon,
                                        double localizerTrueHeading,
                                        double magneticVariation)
    {
        // Define intercept angles
        const double DIRECT_ANGLE = 60.0;   // Steep intercept
        const double MEDIUM_ANGLE = 45.0;   // Moderate intercept
        const double SHALLOW_ANGLE = 30.0;  // Gentle intercept

        // Calculate all three headings
        double directHeading = CalculateAngledInterceptHeading(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerTrueHeading,
            DIRECT_ANGLE,
            magneticVariation);

        double mediumHeading = CalculateAngledInterceptHeading(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerTrueHeading,
            MEDIUM_ANGLE,
            magneticVariation);

        double shallowHeading = CalculateAngledInterceptHeading(
            aircraftLat, aircraftLon,
            runwayThresholdLat, runwayThresholdLon,
            localizerTrueHeading,
            SHALLOW_ANGLE,
            magneticVariation);

        return (directHeading, mediumHeading, shallowHeading);
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
    /// Calculates along-track distance from threshold to aircraft's perpendicular projection on centerline.
    /// This represents the aircraft's progress along the approach path, independent of lateral deviation.
    /// This is critical for vertical guidance - glideslope altitude should be based on progress along
    /// the approach path, not straight-line distance to the threshold.
    /// </summary>
    /// <param name="aircraftLat">Aircraft latitude in degrees</param>
    /// <param name="aircraftLon">Aircraft longitude in degrees</param>
    /// <param name="thresholdLat">Runway threshold latitude in degrees</param>
    /// <param name="thresholdLon">Runway threshold longitude in degrees</param>
    /// <param name="localizerHeading">Localizer true heading in degrees</param>
    /// <returns>Along-track distance in nautical miles (positive = ahead of threshold, negative = behind)</returns>
    public static double CalculateAlongTrackDistance(
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

        // Calculate along-track distance (distance along centerline from threshold to perpendicular point)
        // This uses the great-circle formula to find the signed distance along the approach path.
        // Positive values = aircraft is ahead of threshold (approaching)
        // Negative values = aircraft is behind threshold (passed or extending)
        double alongTrackDistanceRad = Math.Atan2(
            Math.Sin(angularDistance) * Math.Cos(bearingToAircraft - locHeadingRad),
            Math.Cos(angularDistance));

        // Convert from radians to nautical miles
        return alongTrackDistanceRad * EARTH_RADIUS_NM;
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

        // Calculate cross-track distance (perpendicular distance from centerline)
        double crossTrackDistance = Math.Asin(Math.Sin(angularDistance) *
                                               Math.Sin(bearingToAircraft - locHeadingRad));

        // Calculate along-track distance (distance along centerline from threshold to perpendicular point)
        // Using correct great-circle formula
        double alongTrackDistance = Math.Atan2(
            Math.Sin(angularDistance) * Math.Cos(bearingToAircraft - locHeadingRad),
            Math.Cos(angularDistance));

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
