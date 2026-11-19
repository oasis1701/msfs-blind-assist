using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

public class Md11FoSwitchingTests
{
    [Theory]
    [InlineData(1.0, 1.0, true)]
    [InlineData(1.4, 1.0, true)]
    [InlineData(1.6, 1.0, false)]
    [InlineData(double.NaN, 1.0, false)]
    public void AtPosition(double v, double t, bool expected) => Assert.Equal(expected, Md11FoSwitching.AtPosition(v, t));

    [Theory]
    [InlineData(0.0, 2.0, 1)]
    [InlineData(2.0, 0.0, -1)]
    [InlineData(1.0, 1.0, 0)]
    public void Direction(double c, double t, int expected) => Assert.Equal(expected, Md11FoSwitching.Direction(c, t));

    [Theory]
    [InlineData(1.0, 2.0, 1, Md11FoMove.Toward)]
    [InlineData(1.0, 0.0, 1, Md11FoMove.Away)]
    [InlineData(1.0, 1.0, 1, Md11FoMove.None)]
    [InlineData(1.0, 0.0, -1, Md11FoMove.Toward)]
    public void Classify(double before, double after, int wanted, Md11FoMove expected)
        => Assert.Equal(expected, Md11FoSwitching.Classify(before, after, wanted));

    [Theory]
    [InlineData(0.0, 0, 2, true)]
    [InlineData(2.0, 0, 2, true)]
    [InlineData(1.0, 0, 2, false)]
    public void AtEndStop(double v, int min, int max, bool expected) => Assert.Equal(expected, Md11FoSwitching.AtEndStop(v, min, max));

    [Fact]
    public void StepCap_CoversAWrongWayRoundTripPlusMargin() => Assert.Equal(6, Md11FoSwitching.StepCap(0, 2));

    [Theory]
    [InlineData(1.0, 1.0, true)]     // AUX pump ON lamp lit -> on
    [InlineData(0.0, 1.0, false)]
    [InlineData(1.0, 0.0, false)]    // PACK OFF lamp lit -> pack off
    [InlineData(0.0, 0.0, true)]
    [InlineData(2.0, 1.0, true)]     // ignition B lamp reads 2
    [InlineData(4.0, 1.0, true)]     // override lamp reads 4
    public void LampState(double lamp, double litMeans, bool expected)
        => Assert.Equal(expected, Md11FoSwitching.LampState(lamp, litMeans));

    [Fact]
    public void LampState_UnreadIsNull()
    {
        Assert.Null(Md11FoSwitching.LampState(null, 1));
        Assert.Null(Md11FoSwitching.LampState(double.NaN, 1));
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(20.0, 1)]      // Up / Extended (0/EXT)
    [InlineData(46.91, 2)]     // Dial-A-Flap detent (TFDi ReadyToFly parks it here)
    [InlineData(38.0, 2)]
    [InlineData(65.0, 2)]
    [InlineData(70.0, 3)]      // 28
    [InlineData(82.0, 4)]      // 35
    [InlineData(100.0, 5)]     // 50
    public void FlapDetentIndex(double rng, int expected) => Assert.Equal(expected, Md11FoSwitching.FlapDetentIndex(rng));

    [Fact]
    public void FlapDetentIndex_UnreadIsNull() => Assert.Null(Md11FoSwitching.FlapDetentIndex(double.NaN));

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(25, true)]
    [InlineData(26, false)]
    public void DialAFlapRange(int d, bool ok) => Assert.Equal(ok, Md11FoSwitching.IsDialAFlapDegrees(d));

    [Theory]
    [InlineData(10, 0.0)]
    [InlineData(15, 33.3335)]
    [InlineData(25, 100.0)]
    public void DialRaw(int deg, double raw) => Assert.Equal(raw, Md11FoSwitching.DialRawFor(deg), 3);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]   // Min — a landing setting: Captain's
    [InlineData(4, false)]
    public void AutobrakeTargets_OnlyTakeoffOrOff(int t, bool ok) => Assert.Equal(ok, Md11FoSwitching.IsFoAutobrakeTarget(t));

    [Fact]
    public void MayPullStarter_OnlyWithTheSwitchInAndTheEngineStill()
    {
        Assert.True(Md11FoSwitching.MayPullStarter(0, 0));
        Assert.False(Md11FoSwitching.MayPullStarter(1, 0));        // already pulled: never click again
        Assert.False(Md11FoSwitching.MayPullStarter(0, 20));       // turning (or popped in after start)
        Assert.Null(Md11FoSwitching.MayPullStarter(null, 0));      // switch unread
        Assert.Null(Md11FoSwitching.MayPullStarter(0, double.NaN));// N2 unread
    }

    // The ground spoilers, from TFDi's own code (md11host.wasm): the lever click calls
    // SetSpoilerArm(pull == 0). Arming needs the pull at 0 AND the travel at 0. Disarming from
    // 1 needs the travel at 0. Disarming from 2 (deployed on landing) sets the pull to 1, and
    // Aircraft::PreUpdate's spring then runs the travel back to RET and zeroes the pull when it
    // arrives — the same retract TFDi uses when throttle 2 is advanced. So one click stows.
    [Theory]
    [InlineData(0.0, 0.0, 1, Md11FoSpoilerAction.Click)]        // arm, lever retracted
    [InlineData(1.0, 0.0, 1, Md11FoSpoilerAction.None)]         // already armed
    [InlineData(1.0, 0.0, 0, Md11FoSpoilerAction.Click)]        // disarm after takeoff
    [InlineData(0.0, 0.0, 0, Md11FoSpoilerAction.None)]         // already down
    [InlineData(2.0, 50.0, 0, Md11FoSpoilerAction.Stow)]        // after landing: stow
    [InlineData(2.0, 50.0, 1, Md11FoSpoilerAction.StowThenArm)] // deployed, then asked to arm
    [InlineData(0.0, 25.0, 1, Md11FoSpoilerAction.Refuse)]      // speedbrake out: the Captain's lever
    [InlineData(0.0, 1.9, 1, Md11FoSpoilerAction.Click)]        // within the detent tolerance of RET
    public void SpoilerAction(double pull, double travel, int want, Md11FoSpoilerAction expected)
        => Assert.Equal(expected, Md11FoSwitching.SpoilerAction(pull, travel, want));

    [Fact]
    public void SpoilerAction_NeverClicksBlind()
    {
        Assert.Equal(Md11FoSpoilerAction.Refuse, Md11FoSwitching.SpoilerAction(null, 0, 1));
        Assert.Equal(Md11FoSpoilerAction.Refuse, Md11FoSwitching.SpoilerAction(double.NaN, 0, 0));
        Assert.Equal(Md11FoSpoilerAction.Refuse, Md11FoSwitching.SpoilerAction(0, null, 1));      // arming needs the travel
        Assert.Equal(Md11FoSpoilerAction.Refuse, Md11FoSwitching.SpoilerAction(0.5, 0, 1));       // between states
    }

    [Fact]
    public void WxrOffReadKey_IsRegisteredOnRequestUnderItsOwnKey()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        var def = vars[TFDiMD11Definition.FoWxrOffReadKey];
        Assert.Equal("MD11_PED_WXR_OFF_BT", def.Name);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.True(def.ExcludeFromMonitorManager);
        Assert.False(def.IsAnnounced);
    }

    [Fact]
    public void WxrOffButtonItself_StaysWriteOnly()
        => Assert.Equal(UpdateFrequency.Never, new TFDiMD11Definition().GetVariables()["MD11_PED_WXR_OFF_BT"].UpdateFrequency);
}
