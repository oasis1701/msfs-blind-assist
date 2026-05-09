namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Shared runway centerline geometry used by both TakeoffAssistManager
/// (during takeoff roll) and TaxiGuidanceManager (during runway lineup).
///
/// Sign convention (documented, consistent across the whole codebase):
///   • NavigationCalculator.CalculateCrossTrackError returns signedError in degrees:
///       positive  = aircraft RIGHT of runway centerline
///       negative  = aircraft LEFT  of runway centerline
///   • This helper returns CrossTrackFeet with INVERTED sign so that:
///       positive CrossTrackFeet = aircraft LEFT  of centerline
///       negative CrossTrackFeet = aircraft RIGHT of centerline
///     (This matches the long-standing formatter convention in
///      TakeoffAssistManager.FormatCenterlineAnnouncement: "crossTrackFeet > 0 ? left : right".)
///
/// Use <see cref="LeftRightLabel(double)"/> anywhere you need the direction
/// string so all call sites stay in sync.
/// </summary>
public static class RunwayCenterlineTracker
{
    private const double NM_TO_FEET = 6076.12;

    public readonly struct Result
    {
        /// <summary>Perpendicular distance from centerline, signed: +left / -right.</summary>
        public double CrossTrackFeet { get; init; }
        /// <summary>Absolute perpendicular distance to centerline in feet (always >= 0).</summary>
        public double AbsCrossTrackFeet { get; init; }
        /// <summary>Heading error relative to runway heading, normalized to [-180, +180].</summary>
        public double HeadingErrorDeg { get; init; }
        /// <summary>Distance from aircraft to the runway threshold in meters.</summary>
        public double DistToThresholdMeters { get; init; }
        /// <summary>Great-circle bearing from aircraft to threshold (degrees, 0-360).</summary>
        public double BearingToThresholdDeg { get; init; }
    }

    /// <summary>
    /// Compute all centerline tracking values in one call.
    /// Pass TRUE heading values for geographic correctness.
    /// </summary>
    public static Result Compute(
        double aircraftLat, double aircraftLon,
        double aircraftHeadingTrue,
        double thresholdLat, double thresholdLon,
        double runwayHeadingTrue)
    {
        double bearingToThreshold = NavigationCalculator.CalculateBearing(
            aircraftLat, aircraftLon, thresholdLat, thresholdLon);

        double distNM = NavigationCalculator.CalculateDistance(
            aircraftLat, aircraftLon, thresholdLat, thresholdLon);
        double distMeters = distNM * 1852.0;

        double perpDistNM = NavigationCalculator.CalculateDistanceToLocalizer(
            aircraftLat, aircraftLon, thresholdLat, thresholdLon, runwayHeadingTrue);
        double perpDistFeet = perpDistNM * NM_TO_FEET;

        double signedDeg = NavigationCalculator.CalculateCrossTrackError(
            aircraftLat, aircraftLon, thresholdLat, thresholdLon, runwayHeadingTrue);

        // Apply canonical sign: + = left, - = right (see class remarks)
        double signedFeet = signedDeg > 0 ? -perpDistFeet : perpDistFeet;

        double headingError = runwayHeadingTrue - aircraftHeadingTrue;
        while (headingError > 180) headingError -= 360;
        while (headingError < -180) headingError += 360;

        return new Result
        {
            CrossTrackFeet = signedFeet,
            AbsCrossTrackFeet = perpDistFeet,
            HeadingErrorDeg = headingError,
            DistToThresholdMeters = distMeters,
            BearingToThresholdDeg = bearingToThreshold,
        };
    }

    /// <summary>
    /// Returns "left", "right", or "center" for a signed CrossTrackFeet value.
    /// </summary>
    public static string LeftRightLabel(double crossTrackFeet, double centerToleranceFeet = 25.0)
    {
        if (System.Math.Abs(crossTrackFeet) <= centerToleranceFeet) return "center";
        return crossTrackFeet > 0 ? "left" : "right";
    }
}
