using Xunit;
using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 777's two external-power buttons are momentary TOGGLES, so a press is only
/// correct on a side whose current state differs from the wanted one. This is the
/// rule the Electrical Power Up flow got wrong: both of its GPU steps shared one
/// "is ANY GPU on?" predicate, so connecting the primary made the secondary step
/// skip itself and the secondary receptacle was never connected all flight.
/// </summary>
public class GroundPowerGateTests
{
    [Theory]
    // connecting (wantOn: true)
    [InlineData(false, true,  true)]   // side off, want on  -> press
    [InlineData(true,  true,  false)]  // side on,  want on  -> already there, pressing would DISCONNECT
    // disconnecting (wantOn: false)
    [InlineData(true,  false, true)]   // side on,  want off -> press
    [InlineData(false, false, false)]  // side off, want off -> already there, pressing would CONNECT
    public void NeedsPress_IsTrue_OnlyWhenTheSideDisagreesWithWhatIsWanted(
        bool sideOn, bool wantOn, bool expected)
        => Assert.Equal(expected, GroundPowerGate.NeedsPress(sideOn, wantOn));

    [Theory]
    [InlineData(false, true,  false)]
    [InlineData(true,  true,  true)]
    [InlineData(true,  false, false)]
    [InlineData(false, false, true)]
    public void ShouldSkip_IsTheInverseOfNeedsPress(bool sideOn, bool wantOn, bool expected)
        => Assert.Equal(expected, GroundPowerGate.ShouldSkip(sideOn, wantOn));

    // The regression itself: connecting side 1 must not decide anything about side 2.
    // Under the old shared "any GPU on" predicate this pair was (skip, skip).
    [Fact]
    public void ConnectingOneSide_DoesNotSuppressTheOther()
    {
        const bool side1On = true, side2On = false;
        Assert.True(GroundPowerGate.ShouldSkip(side1On, wantOn: true));
        Assert.False(GroundPowerGate.ShouldSkip(side2On, wantOn: true));
    }
}
