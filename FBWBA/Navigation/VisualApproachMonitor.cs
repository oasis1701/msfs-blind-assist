using System;
using FBWBA.Database.Models;
using FBWBA.SimConnect;

namespace FBWBA.Navigation
{
    public class VisualApproachMonitor
    {
        // Constants for guidance thresholds
        private const double LATERAL_TOLERANCE_DEGREES = 0.5; // ±0.5° for "Aligned"
        private const double VERTICAL_TOLERANCE_FEET = 50.0;  // ±50 feet for "On slope"
        private const double GLIDESLOPE_FEET_PER_NM = 320.0;  // 3-degree glideslope
        private const double CRITICAL_ALTITUDE_AGL = 1000.0;  // Switch to 1-second updates
        private const double SAFETY_ALTITUDE_AGL = 50.0;      // Stop monitoring below this
        private const double MAX_DEVIATION_DEGREES = 5.0;     // Stop if too far off course

        /// <summary>
        /// Calculates simple visual approach guidance
        /// </summary>
        public static VisualApproachGuidance CalculateGuidance(
            SimConnectManager.AircraftPosition aircraft,
            Runway runway,
            Airport airport)
        {
            // Calculate basic navigation data
            double distanceToThreshold = NavigationCalculator.CalculateDistance(
                aircraft.Latitude, aircraft.Longitude, runway.StartLat, runway.StartLon);

            double lateralDeviation = CalculateLateralDeviation(aircraft, runway);
            double verticalDeviation = CalculateVerticalDeviation(aircraft, runway, airport, distanceToThreshold);
            double agl = aircraft.Altitude - airport.Altitude;

            // Determine simple states
            LateralState lateral = GetLateralState(lateralDeviation);
            VerticalState vertical = GetVerticalState(verticalDeviation);

            // Check if monitoring should continue
            bool shouldContinue = ShouldContinueMonitoring(agl, lateralDeviation, distanceToThreshold);

            // Determine update frequency
            int updateIntervalMs = GetUpdateInterval(agl);

            return new VisualApproachGuidance
            {
                LateralState = lateral,
                VerticalState = vertical,
                LateralDeviation = lateralDeviation,
                VerticalDeviation = verticalDeviation,
                DistanceToThreshold = distanceToThreshold,
                AGL = agl,
                ShouldContinue = shouldContinue,
                UpdateIntervalMs = updateIntervalMs
            };
        }

        /// <summary>
        /// Calculates lateral deviation from runway centerline in degrees
        /// </summary>
        private static double CalculateLateralDeviation(SimConnectManager.AircraftPosition aircraft, Runway runway)
        {
            // Calculate bearing from aircraft to runway threshold
            double bearingToRunway = NavigationCalculator.CalculateBearing(
                aircraft.Latitude, aircraft.Longitude, runway.StartLat, runway.StartLon);

            // Calculate angular difference from runway heading
            double deviation = bearingToRunway - runway.HeadingMag;

            // Normalize to -180 to +180 range
            while (deviation > 180) deviation -= 360;
            while (deviation < -180) deviation += 360;

            return deviation;
        }

        /// <summary>
        /// Calculates vertical deviation from 3-degree glideslope in feet
        /// </summary>
        private static double CalculateVerticalDeviation(
            SimConnectManager.AircraftPosition aircraft,
            Runway runway,
            Airport airport,
            double distanceNM)
        {
            // Calculate required altitude for 3-degree glideslope
            double requiredAltitude = (distanceNM * GLIDESLOPE_FEET_PER_NM) + airport.Altitude;

            // Return deviation (positive = above, negative = below)
            return aircraft.Altitude - requiredAltitude;
        }

        /// <summary>
        /// Determines lateral guidance state
        /// </summary>
        private static LateralState GetLateralState(double deviationDegrees)
        {
            if (Math.Abs(deviationDegrees) <= LATERAL_TOLERANCE_DEGREES)
                return LateralState.Aligned;
            else if (deviationDegrees > 0)
                return LateralState.Right; // Aircraft is to the right of centerline
            else
                return LateralState.Left;  // Aircraft is to the left of centerline
        }

        /// <summary>
        /// Determines vertical guidance state
        /// </summary>
        private static VerticalState GetVerticalState(double deviationFeet)
        {
            if (Math.Abs(deviationFeet) <= VERTICAL_TOLERANCE_FEET)
                return VerticalState.OnSlope;
            else if (deviationFeet > 0)
                return VerticalState.Up;   // Aircraft is above glideslope
            else
                return VerticalState.Down; // Aircraft is below glideslope
        }

        /// <summary>
        /// Determines if monitoring should continue based on safety criteria
        /// </summary>
        private static bool ShouldContinueMonitoring(double agl, double lateralDeviationDegrees, double distanceNM)
        {
            // Stop if too low
            if (agl < SAFETY_ALTITUDE_AGL)
                return false;

            // Stop if way off course
            if (Math.Abs(lateralDeviationDegrees) > MAX_DEVIATION_DEGREES)
                return false;

            // Stop if distance is increasing (missed approach)
            // Note: This would need tracking of previous distance - simplified for now
            return true;
        }

        /// <summary>
        /// Determines update interval based on altitude
        /// </summary>
        private static int GetUpdateInterval(double agl)
        {
            if (agl <= CRITICAL_ALTITUDE_AGL)
                return 1000; // 1 second below 1000ft AGL
            else
                return 3000; // 3 seconds above 1000ft AGL
        }

        /// <summary>
        /// Formats the guidance into a simple announcement
        /// </summary>
        public static string FormatGuidanceAnnouncement(VisualApproachGuidance guidance)
        {
            string lateral = GetLateralText(guidance.LateralState);
            string vertical = GetVerticalText(guidance.VerticalState);

            // Combine if both states are not aligned/on slope
            if (guidance.LateralState != LateralState.Aligned || guidance.VerticalState != VerticalState.OnSlope)
            {
                if (guidance.LateralState == LateralState.Aligned)
                    return vertical; // Only vertical correction needed
                else if (guidance.VerticalState == VerticalState.OnSlope)
                    return lateral;  // Only lateral correction needed
                else
                    return $"{lateral}, {vertical}"; // Both corrections needed
            }
            else
            {
                return "Aligned, on slope"; // Perfect position
            }
        }

        private static string GetLateralText(LateralState state)
        {
            switch (state)
            {
                case LateralState.Left: return "Left";
                case LateralState.Right: return "Right";
                case LateralState.Aligned: return "Aligned";
                default: return "Aligned";
            }
        }

        private static string GetVerticalText(VerticalState state)
        {
            switch (state)
            {
                case VerticalState.Up: return "Up";
                case VerticalState.Down: return "Down";
                case VerticalState.OnSlope: return "On slope";
                default: return "On slope";
            }
        }
    }

    public class VisualApproachGuidance
    {
        public LateralState LateralState { get; set; }
        public VerticalState VerticalState { get; set; }
        public double LateralDeviation { get; set; }      // Degrees off centerline
        public double VerticalDeviation { get; set; }     // Feet above/below glideslope
        public double DistanceToThreshold { get; set; }   // Nautical miles
        public double AGL { get; set; }                   // Feet above ground level
        public bool ShouldContinue { get; set; }          // Continue monitoring?
        public int UpdateIntervalMs { get; set; }         // Update frequency in milliseconds
    }

    public enum LateralState
    {
        Left,      // Turn right to correct
        Right,     // Turn left to correct
        Aligned    // On centerline
    }

    public enum VerticalState
    {
        Up,        // Descend to correct
        Down,      // Climb to correct
        OnSlope    // On glideslope
    }
}