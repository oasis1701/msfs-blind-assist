// The take-off roll callouts need a per-SIM_FRAME airspeed feed — but only on the ground and on a roll
// still in progress. Streamed for the whole flight it cost tens of UI-thread dispatches a second through
// cruise for nothing (found by review, 2026-09-25). Each airframe names its feed
// (IAircraftDefinition.TakeoffCalloutFeedKey) and says when it is needed (TakeoffCalloutFeedNeeded);
// MainForm pauses the subscription while it is not and resumes it at touchdown.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class TakeoffCalloutFeedTests
{
    private static TakeoffVSpeedCallouts ArmedMachine()
    {
        var m = new TakeoffVSpeedCallouts();
        m.SetV1(140); m.SetVR(145); m.SetV2(150);
        m.ProcessSample(10, onGround: true);   // arms
        return m;
    }

    [Fact]
    public void The_machine_needs_samples_on_the_ground_whatever_its_state()
    {
        Assert.True(new TakeoffVSpeedCallouts().NeedsSamples(onGround: true));
    }

    [Fact]
    public void Airborne_and_disarmed_it_needs_none()
    {
        Assert.False(new TakeoffVSpeedCallouts().NeedsSamples(onGround: false));
    }

    [Fact]
    public void Airborne_with_v2_still_to_call_it_still_needs_them()
    {
        var m = ArmedMachine();
        m.ProcessSample(141, onGround: true);
        m.ProcessSample(146, onGround: true);
        m.ProcessSample(148, onGround: false);   // lifted off before V2

        Assert.True(m.NeedsSamples(onGround: false));

        m.ProcessSample(151, onGround: false);   // V2 called: the roll is over
        Assert.False(m.NeedsSamples(onGround: false));
    }

    [Fact]
    public void Each_callout_airframe_names_its_feed()
    {
        Assert.Equal(A380TakeoffCallouts.IasKey, new FlyByWireA380Definition().TakeoffCalloutFeedKey);
        Assert.Equal(MSFSBlindAssist.Aircraft.MD11.Md11TakeoffCallouts.IasKey, new TFDiMD11Definition().TakeoffCalloutFeedKey);
        Assert.Equal("IFLY_IAS", new IFly737MAXDefinition().TakeoffCalloutFeedKey);
        Assert.Null(new FlyByWireA320Definition().TakeoffCalloutFeedKey);
    }

    [Fact]
    public void The_a380_feed_pauses_once_airborne_with_nothing_left_to_call_and_resumes_at_touchdown()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        Assert.True(def.TakeoffCalloutFeedNeeded);                          // parked

        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        def.ProcessSimVarUpdate(A380TakeoffCallouts.IasKey, 250, speech);  // cruise, no roll armed
        Assert.False(def.TakeoffCalloutFeedNeeded);

        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);               // touchdown
        Assert.True(def.TakeoffCalloutFeedNeeded);
    }
}
