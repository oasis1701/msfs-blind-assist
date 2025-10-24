using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Navigation;

public class VisualApproachMonitor
    {
        // Constants for guidance thresholds
        private const double LATERAL_TOLERANCE_NM = 0.15;       // ±0.15nm (900ft) for "Aligned"
        private const double VERTICAL_TOLERANCE_FEET = 50.0;    // ±50 feet for "On slope"
        private const double GLIDESLOPE_FEET_PER_NM = 320.0;    // 3-degree glideslope
        private const double CRITICAL_ALTITUDE_AGL = 1000.0;    // Switch to 1-second updates
        private const double SHORT_FINAL_ALTITUDE_AGL = 500.0;  // Short final phase
        private const double SAFETY_ALTITUDE_AGL = 50.0;        // Stop monitoring below this
        private const double MAX_LATERAL_DEVIATION_NM = 8.0;    // Stop if too far off centerline
        private const double MAX_DISTANCE_NM = 50.0;            // Stop if too far from runway
        private const double MIN_DISTANCE_NM = 0.3;             // Stop when landing/landed
        private const double INITIAL_APPROACH_DISTANCE_NM = 10.0; // Phase 1 threshold
        private const double INTERCEPT_DISTANCE_NM = 3.0;       // Phase 2 threshold
        private const double SHORT_FINAL_DISTANCE_NM = 1.0;     // Phase 4 threshold
        private const double ALIGNMENT_TOLERANCE_DEGREES = 3.0; // Aligned with runway heading
        private const double INTERCEPT_ANGLE_TOLERANCE = 30.0;  // Detect if on intercept heading

        /// <summary>
        /// Calculates phase-based visual approach guidance
        /// </summary>
        public static VisualApproachGuidance CalculateGuidance(
            SimConnectManager.AircraftPosition aircraft,
            Runway runway,
            Airport airport)
        {
            // Calculate basic navigation data
            double distanceToThreshold = NavigationCalculator.CalculateDistance(
                aircraft.Latitude, aircraft.Longitude, runway.StartLat, runway.StartLon);

            // Use perpendicular distance to extended centerline (fixes the critical bug)
            double lateralDistanceNM = NavigationCalculator.CalculatePerpendicularDistance(aircraft, runway);

            // Calculate if aircraft is ahead or behind the runway using along-track distance
            double bearingToAircraft = NavigationCalculator.CalculateBearing(
                runway.StartLat, runway.StartLon, aircraft.Latitude, aircraft.Longitude);
            double bearingDifference = Math.Abs(bearingToAircraft - runway.HeadingMag);
            if (bearingDifference > 180) bearingDifference = 360 - bearingDifference;
            bool isBehindRunway = bearingDifference > 90; // Aircraft is behind the runway threshold

            double verticalDeviation = CalculateVerticalDeviation(aircraft, runway, airport, distanceToThreshold);
            double agl = aircraft.Altitude - airport.Altitude;

            // Calculate heading alignment
            double headingDifference = Math.Abs(aircraft.HeadingMagnetic - runway.HeadingMag);
            if (headingDifference > 180) headingDifference = 360 - headingDifference;
            bool isAligned = headingDifference <= ALIGNMENT_TOLERANCE_DEGREES;

            // Determine lateral state based on perpendicular distance
            LateralState lateral = GetLateralState(lateralDistanceNM, aircraft, runway);
            VerticalState vertical = GetVerticalState(verticalDeviation);

            // Determine approach phase
            ApproachPhase phase = DetermineApproachPhase(
                distanceToThreshold, lateralDistanceNM, agl, isAligned, isBehindRunway);

            // Check if monitoring should continue
            var (shouldContinue, stopReason) = ShouldContinueMonitoring(
                agl, lateralDistanceNM, distanceToThreshold, isBehindRunway);

            // Determine update frequency based on phase
            int updateIntervalMs = GetUpdateInterval(phase, agl);

            // Calculate direct-to heading for Phase 1 and 2
            double interceptHeading = 0;
            double distanceToIntercept = 0;
            string turnDirection = "";

            if (phase == ApproachPhase.InitialApproach || phase == ApproachPhase.InterceptTurn)
            {
                // Simplified: Calculate point 12nm behind threshold on extended centerline
                double reciprocalHeading = (runway.HeadingMag + 180.0) % 360.0;
                var (centerlinePointLat, centerlinePointLon) = NavigationCalculator.CalculatePointAtDistance(
                    runway.StartLat, runway.StartLon, reciprocalHeading, 12.0);

                // Calculate direct bearing to this centerline point
                interceptHeading = NavigationCalculator.CalculateBearing(
                    aircraft.Latitude, aircraft.Longitude, centerlinePointLat, centerlinePointLon);

                distanceToIntercept = NavigationCalculator.CalculateDistance(
                    aircraft.Latitude, aircraft.Longitude, centerlinePointLat, centerlinePointLon);

                // Determine turn direction
                double headingDiff = interceptHeading - aircraft.HeadingMagnetic;
                if (headingDiff > 180) headingDiff -= 360;
                if (headingDiff < -180) headingDiff += 360;
                turnDirection = headingDiff > 0 ? "right" : "left";
            }

            return new VisualApproachGuidance
            {
                Phase = phase,
                LateralState = lateral,
                VerticalState = vertical,
                LateralDistanceNM = lateralDistanceNM,
                VerticalDeviation = verticalDeviation,
                DistanceToThreshold = distanceToThreshold,
                AGL = agl,
                ShouldContinue = shouldContinue,
                StopReason = stopReason,
                UpdateIntervalMs = updateIntervalMs,
                IsAligned = isAligned,
                HeadingDifference = headingDifference,
                InterceptHeading = interceptHeading,
                DistanceToIntercept = distanceToIntercept,
                TurnDirection = turnDirection,
                IsBehindRunway = isBehindRunway
            };
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
        /// Determines lateral guidance state based on perpendicular distance to centerline
        /// </summary>
        private static LateralState GetLateralState(double lateralDistanceNM,
            SimConnectManager.AircraftPosition aircraft, Runway runway)
        {
            if (lateralDistanceNM <= LATERAL_TOLERANCE_NM)
                return LateralState.Aligned;

            // Determine which side of centerline the aircraft is on
            // Calculate cross-track bearing
            double bearingToThreshold = NavigationCalculator.CalculateBearing(
                aircraft.Latitude, aircraft.Longitude, runway.StartLat, runway.StartLon);

            double crossTrack = bearingToThreshold - runway.HeadingMag;
            while (crossTrack > 180) crossTrack -= 360;
            while (crossTrack < -180) crossTrack += 360;

            // Positive cross-track = aircraft needs to turn right to reach centerline
            // Negative cross-track = aircraft needs to turn left to reach centerline
            return crossTrack > 0 ? LateralState.Right : LateralState.Left;
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
        /// Determines the current approach phase
        /// </summary>
        private static ApproachPhase DetermineApproachPhase(
            double distanceToThreshold,
            double lateralDistanceNM,
            double agl,
            bool isAligned,
            bool isBehindRunway)
        {
            // Must be behind runway to be in approach phases
            if (!isBehindRunway)
                return ApproachPhase.FinalApproach; // Past threshold, just provide basic guidance

            // Phase 4: Short Final (<1nm and <500ft AGL)
            if (distanceToThreshold < SHORT_FINAL_DISTANCE_NM && agl < SHORT_FINAL_ALTITUDE_AGL)
                return ApproachPhase.ShortFinal;

            // Phase 3: Final Approach (aligned within ±3° and <10nm)
            if (isAligned && distanceToThreshold < INITIAL_APPROACH_DISTANCE_NM)
                return ApproachPhase.FinalApproach;

            // Phase 2: Intercept/Base Turn (3-10nm, not aligned)
            if (distanceToThreshold >= INTERCEPT_DISTANCE_NM &&
                distanceToThreshold < INITIAL_APPROACH_DISTANCE_NM &&
                !isAligned)
                return ApproachPhase.InterceptTurn;

            // Phase 1: Initial Approach (>10nm)
            if (distanceToThreshold >= INITIAL_APPROACH_DISTANCE_NM)
                return ApproachPhase.InitialApproach;

            // Default to final approach for close-in but not aligned
            return ApproachPhase.FinalApproach;
        }

        /// <summary>
        /// Determines if monitoring should continue based on safety criteria
        /// </summary>
        private static (bool shouldContinue, string stopReason) ShouldContinueMonitoring(
            double agl, double lateralDistanceNM, double distanceToThreshold, bool isBehindRunway)
        {
            // Stop if too low (landing/landed)
            if (agl < SAFETY_ALTITUDE_AGL)
                return (false, "Below minimum altitude - landing or landed");

            // Stop if too far from runway
            if (distanceToThreshold > MAX_DISTANCE_NM)
                return (false, $"Too far from runway - {distanceToThreshold:F1} miles (maximum {MAX_DISTANCE_NM} miles)");

            // Stop if too close (past threshold without landing)
            if (distanceToThreshold < MIN_DISTANCE_NM && !isBehindRunway)
                return (false, "Past runway threshold");

            // Stop if way off centerline
            if (lateralDistanceNM > MAX_LATERAL_DEVIATION_NM)
                return (false, $"Too far off centerline - {lateralDistanceNM:F1} miles");

            return (true, "");
        }

        /// <summary>
        /// Determines update interval based on phase and altitude
        /// </summary>
        private static int GetUpdateInterval(ApproachPhase phase, double agl)
        {
            switch (phase)
            {
                case ApproachPhase.ShortFinal:
                    return 1000; // 1 second on short final
                case ApproachPhase.FinalApproach:
                    return agl <= CRITICAL_ALTITUDE_AGL ? 1000 : 2000; // 1-2 seconds
                case ApproachPhase.InterceptTurn:
                    return 2000; // 2 seconds during intercept
                case ApproachPhase.InitialApproach:
                    return 3000; // 3 seconds on initial approach
                default:
                    return 3000;
            }
        }

        /// <summary>
        /// Formats the guidance into a phase-appropriate announcement
        /// </summary>
        public static string FormatGuidanceAnnouncement(VisualApproachGuidance guidance)
        {
            switch (guidance.Phase)
            {
                case ApproachPhase.InitialApproach:
                    return FormatInitialApproachAnnouncement(guidance);

                case ApproachPhase.InterceptTurn:
                    return FormatInterceptTurnAnnouncement(guidance);

                case ApproachPhase.FinalApproach:
                    return FormatFinalApproachAnnouncement(guidance);

                case ApproachPhase.ShortFinal:
                    return FormatShortFinalAnnouncement(guidance);

                default:
                    return FormatFinalApproachAnnouncement(guidance);
            }
        }

        private static string FormatInitialApproachAnnouncement(VisualApproachGuidance guidance)
        {
            string vertical = GetVerticalText(guidance.VerticalState);

            return $"Initial approach. Turn {guidance.TurnDirection} to heading {guidance.InterceptHeading:000}, " +
                   $"{guidance.DistanceToIntercept:F1} miles to centerline point, " +
                   $"{guidance.DistanceToThreshold:F1} miles to threshold. {vertical}";
        }

        private static string FormatInterceptTurnAnnouncement(VisualApproachGuidance guidance)
        {
            string vertical = GetVerticalText(guidance.VerticalState);

            return $"{guidance.DistanceToThreshold:F1} miles from threshold. " +
                   $"Turn {guidance.TurnDirection} to heading {guidance.InterceptHeading:000}. {vertical}";
        }

        private static string FormatFinalApproachAnnouncement(VisualApproachGuidance guidance)
        {
            string lateral = GetLateralText(guidance.LateralState);
            string vertical = GetVerticalText(guidance.VerticalState);

            // Combine lateral and vertical if needed
            if (guidance.LateralState != LateralState.Aligned || guidance.VerticalState != VerticalState.OnSlope)
            {
                string combined = "";
                if (guidance.LateralState != LateralState.Aligned)
                    combined = lateral;
                if (guidance.VerticalState != VerticalState.OnSlope)
                {
                    if (!string.IsNullOrEmpty(combined))
                        combined += ", " + vertical;
                    else
                        combined = vertical;
                }
                return $"{guidance.DistanceToThreshold:F1} miles. {combined}";
            }
            else
            {
                return $"{guidance.DistanceToThreshold:F1} miles. Aligned, on slope";
            }
        }

        private static string FormatShortFinalAnnouncement(VisualApproachGuidance guidance)
        {
            string lateral = GetLateralText(guidance.LateralState);
            string vertical = GetVerticalText(guidance.VerticalState);

            if (guidance.LateralState == LateralState.Aligned && guidance.VerticalState == VerticalState.OnSlope)
            {
                return $"Short final. {guidance.DistanceToThreshold:F1} miles. Aligned, on slope";
            }
            else
            {
                string corrections = "";
                if (guidance.LateralState != LateralState.Aligned)
                    corrections = lateral;
                if (guidance.VerticalState != VerticalState.OnSlope)
                {
                    if (!string.IsNullOrEmpty(corrections))
                        corrections += ", " + vertical;
                    else
                        corrections = vertical;
                }
                return $"Short final. {guidance.DistanceToThreshold:F1} miles. {corrections}";
            }
        }

        private static string GetLateralText(LateralState state)
        {
            switch (state)
            {
                case LateralState.Left: return "Left of centerline";
                case LateralState.Right: return "Right of centerline";
                case LateralState.Aligned: return "Aligned";
                default: return "Aligned";
            }
        }

        private static string GetVerticalText(VerticalState state)
        {
            switch (state)
            {
                case VerticalState.Up: return "Above glideslope";
                case VerticalState.Down: return "Below glideslope";
                case VerticalState.OnSlope: return "On glideslope";
                default: return "On glideslope";
            }
        }
    }

    public class VisualApproachGuidance
    {
        public ApproachPhase Phase { get; set; }
        public LateralState LateralState { get; set; }
        public VerticalState VerticalState { get; set; }
        public double LateralDistanceNM { get; set; }         // Distance off centerline (NM)
        public double VerticalDeviation { get; set; }         // Feet above/below glideslope
        public double DistanceToThreshold { get; set; }       // Nautical miles
        public double AGL { get; set; }                       // Feet above ground level
        public bool ShouldContinue { get; set; }              // Continue monitoring?
        public string StopReason { get; set; } = "";          // Reason for stopping
        public int UpdateIntervalMs { get; set; }             // Update frequency in milliseconds
        public bool IsAligned { get; set; }                   // Aligned with runway heading
        public double HeadingDifference { get; set; }         // Degrees from runway heading
        public double InterceptHeading { get; set; }          // Recommended intercept heading
        public double DistanceToIntercept { get; set; }       // Miles to intercept point
        public string TurnDirection { get; set; } = "";       // "left" or "right"
        public bool IsBehindRunway { get; set; }              // Is aircraft behind the threshold
    }

    public enum ApproachPhase
    {
        InitialApproach,   // >10nm - vectoring to intercept
        InterceptTurn,     // 3-10nm, not aligned - turn to final
        FinalApproach,     // <10nm, aligned - normal approach guidance
        ShortFinal         // <1nm, <500ft AGL - critical phase
    }

    public enum LateralState
    {
        Left,      // Aircraft is left of centerline, turn right to correct
        Right,     // Aircraft is right of centerline, turn left to correct
        Aligned    // On centerline
    }

    public enum VerticalState
    {
        Up,        // Descend to correct
        Down,      // Climb to correct
        OnSlope    // On glideslope
    }
