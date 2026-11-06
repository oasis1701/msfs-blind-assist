using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The standby panel's Actual Pitch and Bank (2026-10-26): they read the stock attitude
/// instrument, which sits at -20 / +29 unpowered on a level aeroplane, in the sim's sign.
/// </summary>
public class CowsDA40ActualAttitudeTests
{
    [Theory]
    // The sim's pitch is nose-down positive; the standby horizon's is nose-up positive.
    [InlineData("DA40_STBY_TRUE_PITCH", -3.13, "3.1 degrees")]
    [InlineData("DA40_STBY_TRUE_PITCH", 2.0, "-2.0 degrees")]
    [InlineData("DA40_STBY_TRUE_PITCH", 0.04, "0.0 degrees")]
    // The bank keeps the sim's sign, as ATT_GYRO_REL_BANK does.
    [InlineData("DA40_STBY_TRUE_BANK", 15.26, "15.3 degrees")]
    [InlineData("DA40_STBY_TRUE_BANK", -0.04, "0.0 degrees")]
    public void ActualAttitudeReadsInTheStandbyHorizonsSign(string key, double simDegrees, string expected)
    {
        Assert.Equal(expected, TestCulture.Run("sv-SE",
            () => CowsDA40Definition.TrueAttitudeText(key, simDegrees)));
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void ActualAttitudeReadsTheAeroplaneNotTheInstrument(DA40Variant variant)
    {
        var vars = new CowsDA40Definition(variant).GetVariables();

        Assert.Equal("PLANE PITCH DEGREES", vars["DA40_STBY_TRUE_PITCH"].Name);
        Assert.Equal("PLANE BANK DEGREES", vars["DA40_STBY_TRUE_BANK"].Name);
    }
}
