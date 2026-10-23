using MSFSBlindAssist.FirstOfficer.Fenix;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix TO CONFIG verdict, shared by the First Officer and the panel's button (PR 160 second
/// review): an amber takeoff-config fault lights the master CAUTION and was spoken as "normal", and an
/// unread light defaulted to 0 and said "normal" too.
/// </summary>
public class FenixTakeoffConfigVerdictTests
{
    [Theory]
    [InlineData(1.0, 0.0, "Takeoff config: check configuration.")]
    [InlineData(1.0, 1.0, "Takeoff config: check configuration.")]   // the warning outranks the caution
    [InlineData(0.0, 1.0, "Takeoff config: caution. Check the ECAM.")]
    [InlineData(0.0, 0.0, "Takeoff config normal.")]
    public void ReadLights_GiveTheirVerdict(double warning, double caution, string expected)
        => Assert.Equal(expected, FenixTakeoffConfigVerdict.Text(warning, caution));

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(0.0, null)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    public void AnUnreadLight_IsNeverNormal(double? warning, double? caution)
        => Assert.Equal("Takeoff config: no result read. Check the ECAM.", FenixTakeoffConfigVerdict.Text(warning, caution));

    // A lit light is a definite answer even when the other one is unread.
    [Fact]
    public void ALitWarning_IsSpoken_WhateverTheCautionReads()
        => Assert.Equal("Takeoff config: check configuration.", FenixTakeoffConfigVerdict.Text(1.0, null));
}
