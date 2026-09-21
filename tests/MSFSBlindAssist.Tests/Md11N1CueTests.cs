// Characterization tests for Md11N1Cue (Aircraft/MD11/Md11N1Cue.cs) — the MD-11's once-per-roll
// "N1 70 percent" take-off cue, fed the per-frame airspeed and the three engine N1 exports.
//
// The safety-shaped contracts pinned here, in the spirit of TakeoffVSpeedCalloutsTests:
// - it arms only on the ground, slow (or with no airspeed sample yet), with every engine at idle,
//   so a mid-roll connect stays silent and a reverse-thrust rollout can NEVER fire;
// - it fires exactly once per roll, on the first engine to reach 70 %, on the ground;
// - any airborne sample disarms it, so a go-around or an idle descent never re-arms it;
// - a rejected take-off re-arms once the engines are idle AND the aircraft is slow;
// - a context reset drops the arm and keeps the samples, so a static run-up after the reset still
//   speaks while a flight load into the cruise cannot.

using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

public class Md11N1CueTests
{
    /// <summary>A cue standing still on the ground with all three engines at idle: armed for the roll.</summary>
    private static Md11N1Cue NewArmed()
    {
        var cue = new Md11N1Cue();
        cue.OnIas(0, onGround: true);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 25, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG2_N1", 25, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG3_N1", 25, onGround: true));
        Assert.True(cue.IsArmed);
        return cue;
    }

    [Fact]
    public void ATakeoffRoll_SpeaksOnceAtSeventy_OnTheFirstEngineThere()
    {
        var cue = NewArmed();
        Assert.Equal("N1 70 percent", Md11N1Cue.Sentence);   // the sentence the latch spoke, unchanged

        Assert.False(cue.OnN1("MD11_ENG1_N1", 40, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG1_N1", 69.9, onGround: true));
        Assert.True(cue.OnN1("MD11_ENG2_N1", 70.0, onGround: true));    // engine 2 is first to 70: speaks
        Assert.False(cue.IsArmed);

        // The other two crossing 70, the rest of the roll, and the climb-out: silent.
        Assert.False(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG3_N1", 95, onGround: true));
        cue.OnIas(60, onGround: true);
        cue.OnIas(150, onGround: true);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 96, onGround: true));
        cue.OnIas(170, onGround: false);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 90, onGround: false));
        Assert.False(cue.IsArmed);
    }

    [Fact]
    public void AReverseThrustRollout_IsSilent_AndTheNextTakeoffReArms()
    {
        var cue = NewArmed();
        Assert.True(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));      // take-off
        cue.OnIas(200, onGround: false);                                 // airborne
        Assert.False(cue.IsArmed);

        // Approach: idle, then a thrust swing. Never re-arms in the air (HEAD's latch re-armed at < 60).
        Assert.False(cue.OnN1("MD11_ENG1_N1", 30, onGround: false));
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 75, onGround: false));

        // Touchdown at 140 kt, full reverse on all three: on the ground but fast — silent.
        cue.OnIas(140, onGround: true);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 85, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG2_N1", 85, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG3_N1", 85, onGround: true));
        cue.OnIas(80, onGround: true);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 80, onGround: true));

        // Below the arm speed with reverse still in: not idle, so still not armed.
        cue.OnIas(35, onGround: true);
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 55, onGround: true));     // engine 1 stowed, 2 and 3 still at 85
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG2_N1", 30, onGround: true));
        Assert.False(cue.IsArmed);                                       // engine 3 still at 85
        Assert.False(cue.OnN1("MD11_ENG3_N1", 30, onGround: true));
        Assert.True(cue.IsArmed);                                        // every engine idle, slow, on the ground

        // The next take-off speaks again, once.
        Assert.False(cue.OnN1("MD11_ENG1_N1", 50, onGround: true));
        Assert.True(cue.OnN1("MD11_ENG1_N1", 71, onGround: true));
        Assert.False(cue.OnN1("MD11_ENG2_N1", 71, onGround: true));
    }

    [Fact]
    public void AGoAroundAndAnIdleDescent_NeverReArmInTheAir()
    {
        var cue = NewArmed();
        Assert.True(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));
        cue.OnIas(180, onGround: false);

        Assert.False(cue.OnN1("MD11_ENG1_N1", 45, onGround: false));    // idle descent: HEAD's latch re-armed here
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 92, onGround: false));    // go-around thrust: HEAD spoke here
        Assert.False(cue.OnN1("MD11_ENG1_N1", 40, onGround: false));
        Assert.False(cue.OnN1("MD11_ENG1_N1", 80, onGround: false));
        Assert.False(cue.IsArmed);
    }

    [Fact]
    public void ARejectedTakeoff_ReArmsOnceIdleAndSlow_NotBefore()
    {
        var cue = NewArmed();
        Assert.True(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));

        cue.OnIas(90, onGround: true);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 30, onGround: true));     // throttles closed at 90 kt: idle but fast
        Assert.False(cue.IsArmed);

        cue.OnIas(20, onGround: true);                                   // slowed: the airspeed sample re-judges with the idle N1
        Assert.True(cue.IsArmed);
        Assert.True(cue.OnN1("MD11_ENG1_N1", 70, onGround: true));      // the second attempt speaks
    }

    [Fact]
    public void AMidRollConnect_OrAFirstN1AlreadySpooled_StaysSilent()
    {
        var cue = new Md11N1Cue();
        cue.OnIas(60, onGround: true);                                   // connected mid-roll
        Assert.False(cue.OnN1("MD11_ENG1_N1", 55, onGround: true));
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));

        var late = new Md11N1Cue();
        late.OnIas(0, onGround: true);
        Assert.False(late.IsArmed);                                      // no N1 sample yet: nothing to arm on
        Assert.False(late.OnN1("MD11_ENG1_N1", 72, onGround: true));    // first N1 ever is already past 60: no arm, no fire
        Assert.False(late.IsArmed);
    }

    [Fact]
    public void WithNoAirspeedSampleYet_AnIdleEngineOnTheGroundArms()
    {
        // No IAS is no evidence of being fast; the ground flag and the idle N1 are enough.
        var cue = new Md11N1Cue();
        Assert.False(cue.OnN1("MD11_ENG1_N1", 25, onGround: true));
        Assert.True(cue.IsArmed);
        Assert.True(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));
    }

    [Fact]
    public void AfterAContextReset_AStaticRunUpWithNoAirspeedChange_StillSpeaks()
    {
        // A runway spawn: brakes held, spool to 70 %, then TOGA — the airspeed never changes, so
        // the per-frame feed never re-delivers. The cue must still speak.
        var cue = NewArmed();
        cue.Reset();
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 25, onGround: true));     // no OnIas since the reset
        Assert.True(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 50, onGround: true));
        Assert.True(cue.OnN1("MD11_ENG1_N1", 72, onGround: true));
    }

    [Fact]
    public void AfterAContextReset_AnAirspeedJitterAloneReArms_BecauseTheEngineSamplesAreKept()
    {
        // Reset drops the arm, not the samples: a single feed of either kind is enough to re-arm.
        var cue = NewArmed();
        cue.Reset();
        Assert.False(cue.IsArmed);
        cue.OnIas(0.3, onGround: true);                                  // wind jitter on a parked aircraft
        Assert.True(cue.IsArmed);
    }

    [Fact]
    public void AContextReset_DropsTheArm_SoAFlightLoadIntoTheAirCannotFire()
    {
        // Armed at the gate, then a flight load into the cruise. SIM_ON_GROUND has not been
        // delivered yet, so the definition's ground flag is stale-true when the first N1 arrives.
        var cue = NewArmed();
        cue.Reset();
        Assert.False(cue.OnN1("MD11_ENG1_N1", 88, onGround: true));     // HEAD spoke "N1 70 percent" in the cruise here
        Assert.False(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 90, onGround: false));

        // A load into an idle descent can arm on the stale flag — and the delivery that could fire
        // reads the flag at fire time, so once it has caught up the arm is dropped, not spoken.
        cue.Reset();
        Assert.False(cue.OnN1("MD11_ENG1_N1", 30, onGround: true));
        Assert.True(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_ENG1_N1", 75, onGround: false));
        Assert.False(cue.IsArmed);
    }

    [Theory]
    [InlineData("MD11_ENG1_N1", 0)]
    [InlineData("MD11_ENG2_N1", 1)]
    [InlineData("MD11_ENG3_N1", 2)]
    [InlineData("MD11_APU_N1", -1)]
    [InlineData("MD11_IAS", -1)]
    [InlineData("MD11_OVHD_TANK_1_VAL", -1)]
    public void EngineIndex_MapsExactlyTheThreeEngineExports(string varName, int expectedIndex)
    {
        Assert.Equal(expectedIndex, Md11N1Cue.EngineIndex(varName));
    }

    [Fact]
    public void AVarThatIsNotAnEngineExport_NeitherFires_NorDisturbsTheArm()
    {
        // The silent read-out branch hands the cue EVERY silent var; only the three engines count.
        var cue = NewArmed();
        Assert.False(cue.OnN1("MD11_APU_N1", 95, onGround: true));
        Assert.True(cue.IsArmed);
        Assert.False(cue.OnN1("MD11_OVHD_TANK_1_VAL", 95, onGround: true));
        Assert.True(cue.IsArmed);
        Assert.True(cue.OnN1("MD11_ENG3_N1", 95, onGround: true));      // a real engine still fires
    }
}
