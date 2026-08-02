// Characterization tests for the docking completion SQUARENESS gate
// (DockingGeometry.IsSquare / StopMaxHeadingErrorDeg).
//
// Live regression this pins (KJFK gate 20, FBW A380, 2026-08-01): the aircraft reached
// the stop band on the centerline but sitting 17.4 degrees askew (aircraft 43.4 true vs
// gate axis 60.8) and STILL got "GSX docking complete." — GSX then refused to register
// the park and offered "reposition". The stop gate checked lateral offset and along-track
// only, never orientation.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DockingSquareGateTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(6.9)]
    [InlineData(-6.9)]
    [InlineData(7.0)]
    public void Small_misalignment_counts_as_square(double deg)
        => Assert.True(DockingGeometry.IsSquare(deg));

    [Theory]
    [InlineData(7.1)]
    [InlineData(-7.1)]
    [InlineData(17.4)]   // the live KJFK gate 20 failure
    [InlineData(-30.0)]
    public void Larger_misalignment_is_not_square(double deg)
        => Assert.False(DockingGeometry.IsSquare(deg));

    [Fact]
    public void The_live_kjfk_park_would_now_be_rejected()
    {
        // acHdgTrue 43.4, gate axis 60.8 -> aircraft is 17.4 deg LEFT of the axis.
        double headingOff = DockingGeometry.NormalizeDeg180(43.4 - 60.8);
        Assert.False(DockingGeometry.IsSquare(headingOff));
        // ...and the advisory tells the pilot to keep turning RIGHT (negated error).
        Assert.Equal("Right, 15 degrees.", DockingGuidanceManager.SteerPhrase(-headingOff));
    }

    [Fact]
    public void Wraparound_is_handled()
    {
        // 359 vs 2 degrees is a 3-degree error, not 357.
        Assert.True(DockingGeometry.IsSquare(DockingGeometry.NormalizeDeg180(359.0 - 2.0)));
        Assert.False(DockingGeometry.IsSquare(DockingGeometry.NormalizeDeg180(350.0 - 10.0)));
    }

    [Theory]
    [InlineData(17.4, "Nose 18 degrees right of the gate heading")]
    [InlineData(-17.4, "Nose 18 degrees left of the gate heading")]
    [InlineData(30.0, "Nose 30 degrees right of the gate heading")]
    public void Askew_description_names_the_side_and_ceils_the_degrees(double deg, string expected)
        => Assert.Equal(expected, DockingGuidanceManager.AskewDescription(deg));

    [Fact]
    public void Askew_description_never_speaks_a_number_inside_the_square_gate()
    {
        // 7.4 deg is NOT square, but an "F0" format would say "7 degrees" — and 7 IS square,
        // so the phrase would name a number that reads as acceptable. Ceiling says 8.
        Assert.False(DockingGeometry.IsSquare(7.4));
        Assert.Equal("Nose 8 degrees right of the gate heading",
                     DockingGuidanceManager.AskewDescription(7.4));
    }

    [Fact]
    public void Askew_description_normalizes_wraparound()
        // 350 vs 10 degrees is 20 deg left, not 340 deg right.
        => Assert.Equal("Nose 20 degrees left of the gate heading",
                        DockingGuidanceManager.AskewDescription(350.0 - 10.0));
}
