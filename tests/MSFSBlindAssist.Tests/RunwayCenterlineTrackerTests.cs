// Characterization tests for MSFSBlindAssist.Navigation.RunwayCenterlineTracker.
//
// Pins the documented sign convention -- the class remarks call this a "bug
// magnet": CrossTrackFeet is INVERTED relative to NavigationCalculator's own
// CalculateCrossTrackError (positive = LEFT, negative = RIGHT here, the
// opposite of the underlying calculator). This is characterization, not spec
// verification: if a literal ever disagrees with actual output, the test must
// be corrected to match real output, not the other way around.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayCenterlineTrackerTests
{
    // Runway heading true north (0 deg) from (37.0, -122.0).

    [Fact]
    public void Compute_reports_positive_cross_track_for_an_aircraft_left_of_centerline()
    {
        // Aircraft west of the extended centerline -- geographically LEFT when facing north.
        var result = RunwayCenterlineTracker.Compute(
            aircraftLat: 37.001, aircraftLon: -122.001, aircraftHeadingTrue: 0,
            thresholdLat: 37.0, thresholdLon: -122.0, runwayHeadingTrue: 0);

        Assert.True(result.CrossTrackFeet > 0);
        Assert.Equal("left", RunwayCenterlineTracker.LeftRightLabel(result.CrossTrackFeet));
    }

    [Fact]
    public void Compute_reports_negative_cross_track_for_an_aircraft_right_of_centerline()
    {
        // Aircraft east of the extended centerline -- geographically RIGHT when facing north.
        var result = RunwayCenterlineTracker.Compute(
            aircraftLat: 37.001, aircraftLon: -121.999, aircraftHeadingTrue: 0,
            thresholdLat: 37.0, thresholdLon: -122.0, runwayHeadingTrue: 0);

        Assert.True(result.CrossTrackFeet < 0);
        Assert.Equal("right", RunwayCenterlineTracker.LeftRightLabel(result.CrossTrackFeet));
    }

    [Fact]
    public void Compute_abs_cross_track_feet_is_always_non_negative()
    {
        var left = RunwayCenterlineTracker.Compute(37.001, -122.001, 0, 37.0, -122.0, 0);
        var right = RunwayCenterlineTracker.Compute(37.001, -121.999, 0, 37.0, -122.0, 0);

        Assert.True(left.AbsCrossTrackFeet > 0);
        Assert.True(right.AbsCrossTrackFeet > 0);
        Assert.Equal(left.AbsCrossTrackFeet, Math.Abs(left.CrossTrackFeet), 1);
        Assert.Equal(right.AbsCrossTrackFeet, Math.Abs(right.CrossTrackFeet), 1);
    }

    [Fact]
    public void Compute_reports_zero_heading_error_when_aligned_with_the_runway()
    {
        var result = RunwayCenterlineTracker.Compute(
            aircraftLat: 37.001, aircraftLon: -122.0, aircraftHeadingTrue: 0,
            thresholdLat: 37.0, thresholdLon: -122.0, runwayHeadingTrue: 0);

        Assert.Equal(0.0, result.HeadingErrorDeg, 1);
    }

    [Fact]
    public void Compute_normalizes_heading_error_to_the_plus_minus_180_range()
    {
        // runwayHeading(0) - aircraftHeading(350) = -350 -> normalized to +10.
        var result = RunwayCenterlineTracker.Compute(
            aircraftLat: 37.001, aircraftLon: -122.0, aircraftHeadingTrue: 350,
            thresholdLat: 37.0, thresholdLon: -122.0, runwayHeadingTrue: 0);

        Assert.Equal(10.0, result.HeadingErrorDeg, 1);
    }

    [Fact]
    public void Compute_reports_the_distance_to_threshold_in_metres()
    {
        // ~111.32 m per 0.001 degree of latitude at this latitude.
        var result = RunwayCenterlineTracker.Compute(
            aircraftLat: 37.001, aircraftLon: -122.0, aircraftHeadingTrue: 0,
            thresholdLat: 37.0, thresholdLon: -122.0, runwayHeadingTrue: 0);

        Assert.Equal(111.0, result.DistToThresholdMeters, 0);
    }

    [Theory]
    [InlineData(10.0, "center")]
    [InlineData(-25.0, "center")]
    [InlineData(25.01, "left")]
    [InlineData(-25.01, "right")]
    public void LeftRightLabel_uses_the_centre_tolerance(double crossTrackFeet, string expected)
    {
        Assert.Equal(expected, RunwayCenterlineTracker.LeftRightLabel(crossTrackFeet));
    }
}
