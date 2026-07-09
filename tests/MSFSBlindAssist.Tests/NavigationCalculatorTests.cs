// Characterization tests for MSFSBlindAssist.Navigation.NavigationCalculator.
//
// Bearing/distance/intercept/glideslope math, exercised with hand-verifiable
// cardinal-direction and on-centerline cases so the expected values can be
// reasoned about directly from the formulas rather than re-deriving trig by
// hand. This is characterization, not spec verification: if a literal ever
// disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class NavigationCalculatorTests
{
    // --- CalculateBearing ------------------------------------------------

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]     // due north
    [InlineData(0, 0, -1, 0, 180)]  // due south
    [InlineData(0, 0, 0, 1, 90)]    // due east
    [InlineData(0, 0, 0, -1, 270)]  // due west
    public void CalculateBearing_returns_cardinal_directions(
        double lat1, double lon1, double lat2, double lon2, double expected)
    {
        Assert.Equal(expected, NavigationCalculator.CalculateBearing(lat1, lon1, lat2, lon2), 1);
    }

    // --- CalculateMagneticBearing -----------------------------------------

    [Fact]
    public void CalculateMagneticBearing_subtracts_east_variation()
    {
        // True bearing 0 (north), 10 deg east variation -> magnetic 350.
        double mag = NavigationCalculator.CalculateMagneticBearing(0, 0, 1, 0, magneticVariation: 10);
        Assert.Equal(350.0, mag, 1);
    }

    [Fact]
    public void CalculateMagneticBearing_adds_west_variation()
    {
        // True bearing 0 (north), -10 deg (west) variation -> magnetic 10.
        double mag = NavigationCalculator.CalculateMagneticBearing(0, 0, 1, 0, magneticVariation: -10);
        Assert.Equal(10.0, mag, 1);
    }

    // --- CalculateDistance ---------------------------------------------------

    [Fact]
    public void CalculateDistance_one_degree_of_latitude_is_about_sixty_nautical_miles()
    {
        double nm = NavigationCalculator.CalculateDistance(0, 0, 1, 0);
        Assert.Equal(60.04, nm, 1);
    }

    [Fact]
    public void CalculateDistance_is_zero_for_the_same_point()
    {
        Assert.Equal(0.0, NavigationCalculator.CalculateDistance(37.5, -122.3, 37.5, -122.3), 6);
    }

    // --- CalculateTouchdownAimPoint ------------------------------------------

    [Fact]
    public void CalculateTouchdownAimPoint_projects_along_runway_heading_by_the_given_distance()
    {
        var (lat, lon) = NavigationCalculator.CalculateTouchdownAimPoint(
            thresholdLat: 0, thresholdLon: 0, runwayTrueHeading: 0, touchdownDistanceFeet: 6076.12);

        double distNM = NavigationCalculator.CalculateDistance(0, 0, lat, lon);
        double bearing = NavigationCalculator.CalculateBearing(0, 0, lat, lon);

        Assert.Equal(1.0, distNM, 2);   // 6076.12 ft = 1 NM
        Assert.Equal(0.0, bearing, 0.5); // projected due north (the runway heading)
    }

    // --- CalculateCrossTrackError (negative = left, positive = right) --------

    [Fact]
    public void CalculateCrossTrackError_is_zero_on_the_extended_centerline()
    {
        // Aircraft due north of the threshold, on the localizer heading (0/north).
        double dev = NavigationCalculator.CalculateCrossTrackError(1, 0, 0, 0, localizerHeading: 0);
        Assert.Equal(0.0, dev, 1);
    }

    [Fact]
    public void CalculateCrossTrackError_is_positive_to_the_right_of_centerline()
    {
        // Aircraft east of the threshold at the same latitude -- right when facing north.
        double dev = NavigationCalculator.CalculateCrossTrackError(0, 0.01, 0, 0, localizerHeading: 0);
        Assert.Equal(90.0, dev, 1);
    }

    [Fact]
    public void CalculateCrossTrackError_is_negative_to_the_left_of_centerline()
    {
        // Aircraft west of the threshold at the same latitude -- left when facing north.
        double dev = NavigationCalculator.CalculateCrossTrackError(0, -0.01, 0, 0, localizerHeading: 0);
        Assert.Equal(-90.0, dev, 1);
    }

    [Theory]
    [InlineData(2.0, true)]
    [InlineData(-2.0, true)]
    [InlineData(2.1, false)]
    [InlineData(-2.1, false)]
    public void IsOnLocalizer_uses_a_two_degree_tolerance(double crossTrackError, bool expected)
    {
        Assert.Equal(expected, NavigationCalculator.IsOnLocalizer(crossTrackError));
    }

    // --- CalculateInterceptHeading / CalculateAngledInterceptHeading --------

    [Fact]
    public void CalculateInterceptHeading_points_toward_the_threshold_when_already_on_centerline()
    {
        // Aircraft due south of the threshold, exactly on the extended centerline.
        double heading = NavigationCalculator.CalculateInterceptHeading(
            aircraftLat: -1, aircraftLon: 0,
            runwayThresholdLat: 0, runwayThresholdLon: 0,
            localizerHeading: 0, crossTrackError: 0, magneticVariation: 0);

        Assert.Equal(0.0, heading, 1);
    }

    [Fact]
    public void CalculateAngledInterceptHeading_returns_bearing_to_threshold_when_already_on_centerline()
    {
        double heading = NavigationCalculator.CalculateAngledInterceptHeading(
            aircraftLat: -1, aircraftLon: 0,
            runwayThresholdLat: 0, runwayThresholdLon: 0,
            localizerTrueHeading: 0, targetInterceptAngle: 30, magneticVariation: 0);

        Assert.Equal(0.0, heading, 1);
    }

    // --- CalculateDistanceToLocalizer -----------------------------------------

    [Fact]
    public void CalculateDistanceToLocalizer_is_zero_on_the_extended_centerline()
    {
        double distNM = NavigationCalculator.CalculateDistanceToLocalizer(
            aircraftLat: -1, aircraftLon: 0,
            runwayThresholdLat: 0, runwayThresholdLon: 0,
            localizerHeading: 0);

        Assert.Equal(0.0, distNM, 2);
    }

    // --- CalculateExtensionHeading ---------------------------------------------

    [Fact]
    public void CalculateExtensionHeading_returns_the_reciprocal_of_runway_heading()
    {
        double heading = NavigationCalculator.CalculateExtensionHeading(
            localizerTrueHeading: 0, magneticVariation: 0);

        Assert.Equal(180.0, heading, 1);
    }

    [Fact]
    public void CalculateExtensionHeading_applies_magnetic_variation_before_reversing()
    {
        // True heading 90, 10 deg east variation -> magnetic runway heading 80,
        // extension (reciprocal) = 260.
        double heading = NavigationCalculator.CalculateExtensionHeading(
            localizerTrueHeading: 90, magneticVariation: 10);

        Assert.Equal(260.0, heading, 1);
    }

    // --- CalculateGlideslopeDeviation -------------------------------------------

    [Fact]
    public void CalculateGlideslopeDeviation_is_zero_at_the_threshold_on_the_correct_altitude()
    {
        double dev = NavigationCalculator.CalculateGlideslopeDeviation(
            aircraftAltitudeMSL: 1000, distanceFromThresholdNM: 0,
            glideslopePitch: 3.0, thresholdElevationMSL: 1000);

        Assert.Equal(0.0, dev, 3);
    }

    [Fact]
    public void CalculateGlideslopeDeviation_is_positive_above_the_glidepath()
    {
        double dev = NavigationCalculator.CalculateGlideslopeDeviation(
            aircraftAltitudeMSL: 5000, distanceFromThresholdNM: 5,
            glideslopePitch: 3.0, thresholdElevationMSL: 0);

        Assert.True(dev > 0);
    }

    [Fact]
    public void CalculateGlideslopeDeviation_is_negative_below_the_glidepath()
    {
        double dev = NavigationCalculator.CalculateGlideslopeDeviation(
            aircraftAltitudeMSL: 100, distanceFromThresholdNM: 5,
            glideslopePitch: 3.0, thresholdElevationMSL: 0);

        Assert.True(dev < 0);
    }

    [Fact]
    public void CalculateGlideslopeDeviation_with_antenna_position_matches_threshold_based_at_the_threshold()
    {
        // At the threshold coordinates themselves, both the antenna-based and
        // threshold-based paths should agree closely (antenna sits at the threshold).
        double devThreshold = NavigationCalculator.CalculateGlideslopeDeviation(
            aircraftAltitudeMSL: 1000, distanceFromThresholdNM: 0,
            glideslopePitch: 3.0, thresholdElevationMSL: 1000);

        double devAntenna = NavigationCalculator.CalculateGlideslopeDeviation(
            aircraftAltitudeMSL: 1000, distanceFromThresholdNM: 0,
            glideslopePitch: 3.0, thresholdElevationMSL: 1000,
            glideslopeAntennaLat: 0, glideslopeAntennaLon: 0, glideslopeAntennaAltMSL: 1000,
            aircraftLat: 0, aircraftLon: 0);

        Assert.Equal(devThreshold, devAntenna, 3);
    }

    // --- Range checks ------------------------------------------------------------

    [Theory]
    [InlineData(10.0, 10, true)]
    [InlineData(10.1, 10, false)]
    [InlineData(0.0, 10, true)]
    public void IsWithinILSRange_compares_distance_to_the_published_range(
        double distanceNM, int rangeNM, bool expected)
    {
        Assert.Equal(expected, NavigationCalculator.IsWithinILSRange(distanceNM, rangeNM));
    }

    [Theory]
    [InlineData(10.0, 10, true)]
    [InlineData(10.1, 10, false)]
    public void IsWithinGlideslopeRange_compares_distance_to_the_published_range(
        double distanceNM, int rangeNM, bool expected)
    {
        Assert.Equal(expected, NavigationCalculator.IsWithinGlideslopeRange(distanceNM, rangeNM));
    }

    // --- IsApproachingFromBehind -------------------------------------------------

    [Fact]
    public void IsApproachingFromBehind_is_false_when_approaching_normally()
    {
        // Localizer heading 090 (east); aircraft west of the threshold, closing in
        // from the correct side.
        bool behind = NavigationCalculator.IsApproachingFromBehind(
            aircraftLat: 0, aircraftLon: -1, aircraftMagneticHeading: 90,
            runwayThresholdLat: 0, runwayThresholdLon: 0,
            runwayEndLat: 0, runwayEndLon: 1,
            localizerTrueHeading: 90, magneticVariation: 0);

        Assert.False(behind);
    }

    [Fact]
    public void IsApproachingFromBehind_is_true_when_past_the_threshold_on_the_wrong_side()
    {
        // Aircraft east of the threshold AND past the far end -- behind the runway
        // relative to the localizer heading (090).
        bool behind = NavigationCalculator.IsApproachingFromBehind(
            aircraftLat: 0, aircraftLon: 2, aircraftMagneticHeading: 90,
            runwayThresholdLat: 0, runwayThresholdLon: 0,
            runwayEndLat: 0, runwayEndLon: 1,
            localizerTrueHeading: 90, magneticVariation: 0);

        Assert.True(behind);
    }

    [Fact]
    public void IsApproachingFromBehind_is_false_when_too_close_for_a_reliable_bearing()
    {
        // ~600 ft (0.1 NM) is the minimum-distance safety cutoff.
        bool behind = NavigationCalculator.IsApproachingFromBehind(
            aircraftLat: 0.0001, aircraftLon: 0.0001, aircraftMagneticHeading: 90,
            runwayThresholdLat: 0, runwayThresholdLon: 0,
            runwayEndLat: 0, runwayEndLon: 1,
            localizerTrueHeading: 90, magneticVariation: 0);

        Assert.False(behind);
    }
}
