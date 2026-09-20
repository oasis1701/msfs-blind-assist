using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

public class ParkingTypesTests
{
    [Theory]
    [InlineData(9, true)] [InlineData(10, true)] [InlineData(11, true)] [InlineData(13, true)] [InlineData(14, true)]
    [InlineData(12, false)] [InlineData(15, false)] [InlineData(4, false)]
    public void Gate_types(int type, bool expected) => Assert.Equal(expected, ParkingTypes.IsGate(type));

    [Fact]
    public void The_other_families()
    {
        foreach (int t in new[] { 2, 3, 4, 5, 15 }) Assert.True(ParkingTypes.IsGaRamp(t));
        Assert.False(ParkingTypes.IsGaRamp(12));
        Assert.True(ParkingTypes.IsCargo(6)); Assert.True(ParkingTypes.IsCargo(7)); Assert.False(ParkingTypes.IsCargo(8));
        Assert.True(ParkingTypes.IsDock(12)); Assert.True(ParkingTypes.IsFuel(16)); Assert.True(ParkingTypes.IsVehicle(17));
    }

    private static ParkingSpot Spot(string name, int number, string suffix, int type)
        => new() { Name = name, Number = number, Suffix = suffix, Type = type };

    [Theory]
    [InlineData("A", 12, "B", 10, "A 12B")]
    [InlineData("Parking", 0, "", 4, "Parking")]          // a number of 0 is "no number", never "Parking 0"
    [InlineData("", 7, "A", 10, "Gate 7A")]
    [InlineData("", 12, "", 4, "Spot 12")]
    [InlineData("", 0, "", 16, "Parking")]
    public void DescribeIdentity_is_the_name_half_of_Describe(string name, int number, string suffix, int type, string expected)
    {
        var spot = Spot(name, number, suffix, type);
        Assert.Equal(expected, spot.DescribeIdentity());
        Assert.StartsWith(expected + " - ", spot.Describe());
    }
}
