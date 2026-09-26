using MSFSBlindAssist.FirstOfficer.MD11;
using Xunit;
using A = MSFSBlindAssist.FirstOfficer.MD11.Md11FoFlightPhaseMonitor.LandingLightAction;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The pure halves of the MD-11 phase monitor (the class itself needs a ScreenReaderAnnouncer,
/// which has no test constructor — the iFly monitor's seam, reused).
/// </summary>
public class Md11FoFlightPhaseMonitorTests
{
    [Fact]
    public void ClimbingThrough10300_TurnsTheLandingLightsOff_Once()
    {
        var (a1, l1) = Md11FoFlightPhaseMonitor.Evaluate10kCrossing(9_000, true, false, null, true);
        Assert.Equal(A.None, a1);
        Assert.False(l1);
        var (a2, l2) = Md11FoFlightPhaseMonitor.Evaluate10kCrossing(10_400, true, false, l1, true);
        Assert.Equal(A.TurnOff, a2);
        Assert.True(l2);
        var (a3, _) = Md11FoFlightPhaseMonitor.Evaluate10kCrossing(10_600, true, false, l2, true);
        Assert.Equal(A.None, a3);
    }

    [Fact]
    public void DescendingThrough9700_TurnsTheLandingLightsOn()
    {
        var (a, l) = Md11FoFlightPhaseMonitor.Evaluate10kCrossing(9_600, false, true, true, true);
        Assert.Equal(A.TurnOn, a);
        Assert.False(l);
    }

    [Fact]
    public void Disabled_TakesNoAction_ButTheLatchStillTracks()
    {
        var (a, l) = Md11FoFlightPhaseMonitor.Evaluate10kCrossing(10_400, true, false, false, false);
        Assert.Equal(A.None, a);
        Assert.True(l);                 // re-enabling above 10,000 must not fire a stale crossing
    }

    [Fact]
    public void InsideTheHysteresisBand_HoldsTheLatch()
    {
        var (a, l) = Md11FoFlightPhaseMonitor.Evaluate10kCrossing(10_100, true, false, false, true);
        Assert.Equal(A.None, a);
        Assert.False(l);
    }

    [Fact]
    public void LandingLightPositions_AreRetractedAndOn()
    {
        Assert.Equal(0, Md11FoFlightPhaseMonitor.LandingLightsRetracted);
        Assert.Equal(2, Md11FoFlightPhaseMonitor.LandingLightsOn);
        Assert.Equal(15, Md11FoFlightPhaseMonitor.TransitionDialAFlapDeg);
    }

    [Theory]
    [InlineData(true, true, "Transition altitude. Altimeters standard, Dial-A-Flap 15.")]
    [InlineData(true, false, "Transition altitude. Altimeters standard. Set Dial-A-Flap 15.")]
    [InlineData(false, true, "Transition altitude. Dial-A-Flap 15. Set standard altimeters.")]
    [InlineData(false, false, "Transition altitude. Set standard altimeters and Dial-A-Flap 15.")]
    public void TransitionAltitudeSentence_SaysWhatWasDoneAndAsksForTheRest(bool std, bool daf, string expected)
        => Assert.Equal(expected, Md11FoFlightPhaseMonitor.TransitionAltitudeSentence(std, daf));

    [Fact]
    public void TransitionLevel_IsAnnounceOnly()
        => Assert.Equal("Transition level. Set local altimeter pressure now.", Md11FoFlightPhaseMonitor.TransitionLevelSentence);

    [Fact]
    public void AutoManager_StoresTheFlapSettingAndNeverThrows()
    {
        var m = new Md11FoAutoManager { AutoFlapsEnabled = true };
        Assert.True(m.AutoFlapsEnabled);
        m.Reset();
        m.Update(5_000, -800, 3_000, 180, onGround: false);   // no executor, no actions: a no-op
    }
}
