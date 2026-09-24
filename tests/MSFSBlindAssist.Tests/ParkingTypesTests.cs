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
        Assert.True(ParkingTypes.IsCargo(6)); Assert.False(ParkingTypes.IsCargo(7)); Assert.False(ParkingTypes.IsCargo(8));
        Assert.True(ParkingTypes.IsDock(12)); Assert.True(ParkingTypes.IsFuel(16)); Assert.True(ParkingTypes.IsVehicle(17));
    }

    [Theory]
    [InlineData(7, true)] [InlineData(8, true)]
    [InlineData(6, false)] [InlineData(4, false)] [InlineData(10, false)] [InlineData(16, false)]
    public void Military_types(int type, bool expected) => Assert.Equal(expected, ParkingTypes.IsMilitary(type));

    [Fact]
    public void No_type_belongs_to_two_families()
    {
        // 7 (RAMP_MIL_CARGO) sat in IsCargo while the taxi form's filter and the TCAS label called it
        // military, so the surroundings said "Cargo ramp" about a military ramp. One type, one family.
        for (int t = 0; t <= 17; t++)
        {
            int families = new[]
            {
                ParkingTypes.IsGate(t), ParkingTypes.IsGaRamp(t), ParkingTypes.IsCargo(t), ParkingTypes.IsMilitary(t),
                ParkingTypes.IsDock(t), ParkingTypes.IsFuel(t), ParkingTypes.IsVehicle(t),
            }.Count(b => b);
            Assert.True(families <= 1, $"type {t} is in {families} families");
        }
    }

    [Theory]
    [InlineData(6, "Ramp Cargo")] [InlineData(7, "Ramp Military")] [InlineData(8, "Ramp Military")]
    [InlineData(2, "Ramp GA")] [InlineData(15, "Ramp GA")] [InlineData(12, "Dock")]
    [InlineData(9, "Gate Small")] [InlineData(14, "Gate Extra")]
    [InlineData(16, "Other")] [InlineData(17, "Other")] [InlineData(1, "Other")]
    public void The_filter_category_agrees_with_the_named_families(int type, string expected)
        => Assert.Equal(expected, new ParkingSpot { Type = type }.GetFilterCategory());
}
