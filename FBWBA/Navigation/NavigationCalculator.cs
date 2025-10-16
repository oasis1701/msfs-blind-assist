using System;
using FBWBA.Database.Models;
using FBWBA.SimConnect;

namespace FBWBA.Navigation
{
    public class NavigationCalculator
    {
        // Aviation constants
        private const double EARTH_RADIUS_NM = 3440.065; // Earth radius in nautical miles
        private const double DEGREES_TO_RADIANS = Math.PI / 180.0;
        private const double RADIANS_TO_DEGREES = 180.0 / Math.PI;
        private const double GLIDESLOPE_FEET_PER_NM = 320.0; // 3-degree glideslope: 320 feet per nautical mile
        private const double STANDARD_INTERCEPT_ANGLE = 30.0; // Standard 30-degree intercept angle

        /// <summary>
        /// Calculates the runway endpoint coordinates based on start position, heading, and length
        /// </summary>
        public static (double endLat, double endLon) CalculateRunwayEndpoint(Runway runway)
        {
            double headingRad = runway.HeadingMag * DEGREES_TO_RADIANS;
            double distanceNM = runway.Length / 6076.115; // Convert meters to nautical miles

            double startLatRad = runway.StartLat * DEGREES_TO_RADIANS;
            double startLonRad = runway.StartLon * DEGREES_TO_RADIANS;

            // Calculate endpoint using great circle navigation
            double endLatRad = Math.Asin(
                Math.Sin(startLatRad) * Math.Cos(distanceNM / EARTH_RADIUS_NM) +
                Math.Cos(startLatRad) * Math.Sin(distanceNM / EARTH_RADIUS_NM) * Math.Cos(headingRad)
            );

            double endLonRad = startLonRad + Math.Atan2(
                Math.Sin(headingRad) * Math.Sin(distanceNM / EARTH_RADIUS_NM) * Math.Cos(startLatRad),
                Math.Cos(distanceNM / EARTH_RADIUS_NM) - Math.Sin(startLatRad) * Math.Sin(endLatRad)
            );

            return (endLatRad * RADIANS_TO_DEGREES, endLonRad * RADIANS_TO_DEGREES);
        }

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
        /// Calculates the perpendicular distance from aircraft to the extended centerline
        /// </summary>
        public static double CalculatePerpendicularDistance(SimConnectManager.AircraftPosition aircraft, Runway runway)
        {
            // Calculate runway endpoint
            var (endLat, endLon) = CalculateRunwayEndpoint(runway);

            // Convert to radians
            double acLatRad = aircraft.Latitude * DEGREES_TO_RADIANS;
            double acLonRad = aircraft.Longitude * DEGREES_TO_RADIANS;
            double rwStartLatRad = runway.StartLat * DEGREES_TO_RADIANS;
            double rwStartLonRad = runway.StartLon * DEGREES_TO_RADIANS;
            double rwEndLatRad = endLat * DEGREES_TO_RADIANS;
            double rwEndLonRad = endLon * DEGREES_TO_RADIANS;

            // Calculate cross-track distance using spherical geometry
            double dXt = Math.Asin(Math.Sin(CalculateDistance(runway.StartLat, runway.StartLon, aircraft.Latitude, aircraft.Longitude) / EARTH_RADIUS_NM) *
                         Math.Sin((CalculateBearing(runway.StartLat, runway.StartLon, aircraft.Latitude, aircraft.Longitude) - runway.HeadingMag) * DEGREES_TO_RADIANS)) * EARTH_RADIUS_NM;

            return Math.Abs(dXt);
        }

        /// <summary>
        /// Calculates a point at specified distance and bearing from origin
        /// </summary>
        public static (double lat, double lon) CalculatePointAtDistance(double originLat, double originLon, double bearing, double distanceNM)
        {
            double bearingRad = bearing * DEGREES_TO_RADIANS;
            double originLatRad = originLat * DEGREES_TO_RADIANS;
            double originLonRad = originLon * DEGREES_TO_RADIANS;

            double destLatRad = Math.Asin(
                Math.Sin(originLatRad) * Math.Cos(distanceNM / EARTH_RADIUS_NM) +
                Math.Cos(originLatRad) * Math.Sin(distanceNM / EARTH_RADIUS_NM) * Math.Cos(bearingRad)
            );

            double destLonRad = originLonRad + Math.Atan2(
                Math.Sin(bearingRad) * Math.Sin(distanceNM / EARTH_RADIUS_NM) * Math.Cos(originLatRad),
                Math.Cos(distanceNM / EARTH_RADIUS_NM) - Math.Sin(originLatRad) * Math.Sin(destLatRad)
            );

            return (destLatRad * RADIANS_TO_DEGREES, destLonRad * RADIANS_TO_DEGREES);
        }

        /// <summary>
        /// Calculates the Final Approach Fix (FAF) position at 14nm from runway threshold
        /// </summary>
        public static (double lat, double lon) CalculateFinalApproachFix(Runway runway)
        {
            // FAF is positioned 14nm from threshold on the reciprocal of runway heading
            double reciprocalHeading = (runway.HeadingMag + 180.0) % 360.0;
            return CalculatePointAtDistance(runway.StartLat, runway.StartLon, reciprocalHeading, 14.0);
        }

        /// <summary>
        /// Determines which side (left or right) to set up the intercept from
        /// Returns -1 for left, +1 for right
        /// </summary>
        public static int DetermineBestInterceptSide(SimConnectManager.AircraftPosition aircraft, Runway runway)
        {
            // Calculate bearing from runway threshold to aircraft
            double bearingToAircraft = CalculateBearing(runway.StartLat, runway.StartLon, aircraft.Latitude, aircraft.Longitude);

            // Calculate relative bearing (where aircraft is relative to runway heading)
            double relativeBearing = (bearingToAircraft - runway.HeadingMag + 360.0) % 360.0;

            // If aircraft is on the right side of centerline (0-180°), set up on right
            // If aircraft is on the left side of centerline (180-360°), set up on left
            return (relativeBearing > 0 && relativeBearing <= 180) ? 1 : -1;
        }

        /// <summary>
        /// Calculates the Intercept Setup Point with lateral offset from centerline
        /// </summary>
        public static (double lat, double lon) CalculateInterceptSetupPoint(Runway runway, int side, double lateralOffsetNM = 8.0, double distanceFromThresholdNM = 17.0)
        {
            // First, calculate a point 17nm from threshold on reciprocal heading
            double reciprocalHeading = (runway.HeadingMag + 180.0) % 360.0;
            var (pointOnCenterlineLat, pointOnCenterlineLon) = CalculatePointAtDistance(
                runway.StartLat, runway.StartLon, reciprocalHeading, distanceFromThresholdNM);

            // Then offset perpendicular to runway heading
            // For right side: add 90°, for left side: subtract 90°
            double perpendicularHeading = (runway.HeadingMag + (side * 90.0) + 360.0) % 360.0;

            return CalculatePointAtDistance(pointOnCenterlineLat, pointOnCenterlineLon, perpendicularHeading, lateralOffsetNM);
        }

        /// <summary>
        /// Calculates the 30-degree intercept heading from setup point to centerline
        /// </summary>
        public static double Calculate30DegreeInterceptHeading(Runway runway, int side)
        {
            // 30-degree intercept: runway heading ± 30 degrees
            // If on right side (side = 1), subtract 30 (turn left to intercept)
            // If on left side (side = -1), add 30 (turn right to intercept)
            double interceptHeading = runway.HeadingMag - (side * 30.0);
            return (interceptHeading + 360.0) % 360.0;
        }

        /// <summary>
        /// Determines the current guidance stage based on aircraft position
        /// </summary>
        public static ILSGuidanceState DetermineGuidanceStage(
            SimConnectManager.AircraftPosition aircraft,
            double distanceToSetupPoint,
            double distanceToCenterline,
            double distanceToThreshold,
            Runway runway)
        {
            // Check if too far for guidance
            if (distanceToThreshold > 100.0)
            {
                return ILSGuidanceState.TooFar;
            }

            // Check if established on localizer
            if (IsOnLocalizer(aircraft, runway) && distanceToCenterline < 0.5)
            {
                return ILSGuidanceState.Established;
            }

            // Stage 1: Vectoring to setup point
            if (distanceToSetupPoint > 3.0)
            {
                return ILSGuidanceState.VectoringToSetup;
            }

            // Stage 2: Near setup point, turn to intercept heading
            if (distanceToSetupPoint <= 3.0 && distanceToCenterline > 1.0)
            {
                return ILSGuidanceState.TurningToIntercept;
            }

            // Stage 3: On intercept heading, closing to centerline
            return ILSGuidanceState.Intercepting;
        }

        /// <summary>
        /// Calculates the required glideslope altitude at current distance from runway
        /// </summary>
        public static double CalculateGlideSlopeAltitude(SimConnectManager.AircraftPosition aircraft, Runway runway, Airport airport)
        {
            double distanceToThreshold = CalculateDistance(aircraft.Latitude, aircraft.Longitude, runway.StartLat, runway.StartLon);
            return (distanceToThreshold * GLIDESLOPE_FEET_PER_NM) + airport.Altitude;
        }

        /// <summary>
        /// Checks if aircraft is established on the localizer (within ±5 degrees of course)
        /// </summary>
        public static bool IsOnLocalizer(SimConnectManager.AircraftPosition aircraft, Runway runway)
        {
            double headingDifference = Math.Abs(aircraft.HeadingMagnetic - runway.HeadingMag);

            // Handle crossing 360/0 boundary
            if (headingDifference > 180)
            {
                headingDifference = 360 - headingDifference;
            }

            return headingDifference <= 5.0;
        }

        /// <summary>
        /// Calculates complete ILS guidance information using simplified direct-to centerline approach
        /// </summary>
        public static ILSGuidance CalculateILSGuidance(SimConnectManager.AircraftPosition aircraft, Runway runway, Airport airport)
        {
            // Calculate basic distances and parameters
            double distanceToThreshold = CalculateDistance(aircraft.Latitude, aircraft.Longitude, runway.StartLat, runway.StartLon);
            double perpendicularDistance = CalculatePerpendicularDistance(aircraft, runway);
            double requiredGSAltitude = CalculateGlideSlopeAltitude(aircraft, runway, airport);
            double glideSlopeDeviation = aircraft.Altitude - requiredGSAltitude;
            bool onLocalizer = IsOnLocalizer(aircraft, runway);

            // Simplified: Calculate point 12nm behind threshold on extended centerline
            double reciprocalHeading = (runway.HeadingMag + 180.0) % 360.0;
            var (centerlinePointLat, centerlinePointLon) = CalculatePointAtDistance(
                runway.StartLat, runway.StartLon, reciprocalHeading, 12.0);

            // Calculate direct bearing to centerline point
            double guidanceHeading = CalculateBearing(
                aircraft.Latitude, aircraft.Longitude, centerlinePointLat, centerlinePointLon);

            double distanceToCenterlinePoint = CalculateDistance(
                aircraft.Latitude, aircraft.Longitude, centerlinePointLat, centerlinePointLon);

            // Determine turn direction
            double headingDiff = guidanceHeading - aircraft.HeadingMagnetic;
            if (headingDiff > 180) headingDiff -= 360;
            if (headingDiff < -180) headingDiff += 360;
            string turnDirection = headingDiff > 0 ? "right" : "left";

            // Determine simple state
            ILSGuidanceState state;
            if (distanceToThreshold > 100.0)
                state = ILSGuidanceState.TooFar;
            else if (onLocalizer && perpendicularDistance < 0.5)
                state = ILSGuidanceState.Established;
            else
                state = ILSGuidanceState.VectoringToSetup; // Simplified - always vectoring

            return new ILSGuidance
            {
                State = state,
                InterceptHeading = guidanceHeading,
                DistanceToCenterline = perpendicularDistance,
                DistanceToThreshold = distanceToThreshold,
                GlideSlopeDeviation = glideSlopeDeviation,
                RequiredAltitude = requiredGSAltitude,
                CurrentHeading = guidanceHeading,
                IsOnLocalizer = onLocalizer,
                FAFLatitude = centerlinePointLat,
                FAFLongitude = centerlinePointLon,
                SetupPointLatitude = centerlinePointLat,
                SetupPointLongitude = centerlinePointLon,
                DistanceToSetupPoint = distanceToCenterlinePoint,
                DistanceToFAF = distanceToCenterlinePoint,
                InterceptSide = 0, // Not used in simplified approach
                TurnDirection = turnDirection
            };
        }
    }

    public class ILSGuidance
    {
        public ILSGuidanceState State { get; set; }
        public double InterceptHeading { get; set; }
        public double DistanceToCenterline { get; set; }
        public double DistanceToThreshold { get; set; }
        public double GlideSlopeDeviation { get; set; }
        public double RequiredAltitude { get; set; }
        public double CurrentHeading { get; set; }
        public bool IsOnLocalizer { get; set; }

        // Properties for simplified direct-to centerline approach
        public double FAFLatitude { get; set; }
        public double FAFLongitude { get; set; }
        public double SetupPointLatitude { get; set; }
        public double SetupPointLongitude { get; set; }
        public double DistanceToSetupPoint { get; set; }
        public double DistanceToFAF { get; set; }
        public int InterceptSide { get; set; }  // Not used in simplified approach
        public string TurnDirection { get; set; } // "left" or "right"
    }

    public enum ILSGuidanceState
    {
        VectoringToSetup,      // Stage 1: Flying to intercept setup point
        TurningToIntercept,    // Stage 2: Near setup point, turn to intercept heading
        Intercepting,          // Stage 3: On intercept heading, closing to centerline
        Established,           // On localizer course
        TooFar                 // Beyond 100nm - warning only
    }
}