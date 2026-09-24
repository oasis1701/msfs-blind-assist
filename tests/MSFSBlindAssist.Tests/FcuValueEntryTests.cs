using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FcuValueEntryTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(359.6, 0)]
    [InlineData(360.0, 0)]
    [InlineData(270.4, 270)]
    public void A_heading_in_range_is_whole_degrees(double value, int expected)
    {
        Assert.True(FcuValueEntry.TryHeading(value, out int heading, out string? error));
        Assert.Equal(expected, heading);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(360.1)]
    public void A_heading_out_of_range_is_refused(double value)
    {
        Assert.False(FcuValueEntry.TryHeading(value, out _, out string? error));
        Assert.Equal("Heading must be between 0 and 360 degrees", error);
    }

    [Theory]
    [InlineData(250.0, 250)]
    [InlineData(100.0, 100)]
    [InlineData(399.0, 399)]
    [InlineData(0.78, 78)]     // Mach travels x100, never truncated to 0
    [InlineData(0.10, 10)]
    public void A_speed_in_range_becomes_the_fcus_internal_value(double value, int expected)
    {
        Assert.True(FcuValueEntry.TrySpeed(value, out int internalSpeed, out _));
        Assert.Equal(expected, internalSpeed);
    }

    [Theory]
    [InlineData(99.0)]
    [InlineData(400.0)]
    [InlineData(0.05)]
    [InlineData(1.5)]
    public void A_speed_out_of_range_is_refused(double value)
    {
        Assert.False(FcuValueEntry.TrySpeed(value, out _, out string? error));
        Assert.Equal("Speed must be 100-399 knots or 0.10-0.99 Mach", error);
    }

    [Theory]
    [InlineData(4500.0)]
    [InlineData(100.0)]
    [InlineData(49000.0)]
    public void An_altitude_in_range_is_accepted_as_typed(double value)
    {
        Assert.True(FcuValueEntry.TryAltitude(value, out double feet, out _));
        Assert.Equal(value, feet);
    }

    [Theory]
    [InlineData(99.0)]
    [InlineData(49001.0)]
    public void An_altitude_out_of_range_is_refused(double value)
    {
        Assert.False(FcuValueEntry.TryAltitude(value, out _, out string? error));
        Assert.Equal("Altitude must be between 100 and 49000 feet", error);
    }
}
