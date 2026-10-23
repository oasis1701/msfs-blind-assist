using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The MD-11 AUTO FLIGHT button never disengages the autopilot — with it engaged, each push SWAPS
/// AP1 and AP2 (TFDi Forward Panel). So the universal auto-engage presses only when the export
/// reads definitively off, and an unread export is null (indeterminate), never false.
/// </summary>
public class Md11AutopilotEngageTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, false)]
    [InlineData(1.0, true)]
    [InlineData(2.0, true)]
    [InlineData(3.0, true)]
    public void Engaged_ReadsTheExport(double? apState, bool? expected)
        => Assert.Equal(expected, Md11AutopilotEngage.Engaged(apState));

    [Fact]
    public void Engaged_TreatsNaNAsUnknown()
        => Assert.Null(Md11AutopilotEngage.Engaged(double.NaN));

    [Theory]
    [InlineData(null, false)]      // unknown: never press a swap button blind
    [InlineData(0.0, true)]
    [InlineData(1.0, false)]       // engaged: a press would swap AP1/AP2
    [InlineData(3.0, false)]
    public void ShouldPress_OnlyWhenDefinitelyOff(double? apState, bool expected)
        => Assert.Equal(expected, Md11AutopilotEngage.ShouldPress(apState));

    [Fact]
    public void MinimumEngageHeight_Is400FeetPerTfdi()
        => Assert.Equal(400, new TFDiMD11Definition().MinimumAutopilotEngageAltitudeAgl);

    [Fact]
    public void IsAutopilotEngaged_WithNoSim_IsNull()
    {
        using var def = new TFDiMD11Definition();
        var sim = new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero);
        Assert.Null(def.IsAutopilotEngaged(sim));   // nothing cached: indeterminate, never false
    }
}
