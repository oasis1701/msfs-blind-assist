// tests/MSFSBlindAssist.Tests/RelativeDirectionTests.cs
// Pins the thresholds GroundTrafficMonitor.DescribeDirection carried (20/70/110/160) so the
// ground-traffic phrasing cannot drift now that the surroundings readout shares it.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RelativeDirectionTests
{
    [Theory]
    [InlineData(0, "ahead")]
    [InlineData(20, "ahead")]
    [InlineData(340, "ahead")]
    [InlineData(21, "ahead and to the right")]
    [InlineData(70, "ahead and to the right")]
    [InlineData(-45, "ahead and to the left")]
    [InlineData(90, "to the right")]
    [InlineData(270, "to the left")]
    [InlineData(111, "behind and to the right")]
    [InlineData(-150, "behind and to the left")]
    [InlineData(180, "behind")]
    [InlineData(165, "behind")]
    public void Describe_uses_the_ground_traffic_thresholds(double rel, string expected)
        => Assert.Equal(expected, RelativeDirection.Describe(rel));

    [Theory]
    [InlineData(45, "on the right")]
    [InlineData(135, "on the right")]
    [InlineData(-90, "on the left")]
    [InlineData(225, "on the left")]
    [InlineData(10, "ahead")]
    [InlineData(-170, "behind")]
    public void Side_reports_left_or_right_with_ahead_and_behind_caps(double rel, string expected)
        => Assert.Equal(expected, RelativeDirection.Side(rel));

    [Fact]
    public void Normalize360_wraps_negatives_and_overflow()
    {
        Assert.Equal(350.0, RelativeDirection.Normalize360(-10));
        Assert.Equal(10.0, RelativeDirection.Normalize360(370));
    }

    [Theory]
    [InlineData(-10.0, 350.0)]
    [InlineData(370.0, 10.0)]
    [InlineData(720.0, 0.0)]
    [InlineData(-360.0, 0.0)]
    [InlineData(359.5, 359.5)]
    public void Normalize360_maps_any_angle_into_0_to_360(double input, double expected)
        => Assert.Equal(expected, RelativeDirection.Normalize360(input), 9);
}
