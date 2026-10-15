using MSFSBlindAssist.Services;
using End = MSFSBlindAssist.Services.LandingFlareAssistManager.RolloutEnd;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the manual landing assist's rollout ends and what it says
/// (LandingFlareAssistManager.DecideRolloutEnd). With no landing rollout running in taxi guidance it ends
/// below 40 kt, or once the aircraft is laterally clear of the runway; during a landing rollout it ends
/// only when stopped, silently, because exit guidance's own handoffs end it at a real exit. The speech:
/// silent when taxi guidance already steers, queued when taxi guidance ran during the rollout so the
/// assist never cuts taxi guidance's instructions off, and unchanged when no taxi guidance ran.
///
/// 2026-10-15 (KSEA 16R): the 55 kt end that applied while exit guidance ran retired, and so did the
/// 20°/90 kt heading turn-off, which ended the cue — and said "Rollout guidance complete" — in the middle
/// of the very veer the cue exists for. Position tells a veer from a turn-off; a heading swing cannot.
/// </summary>
public class LandingFlareRolloutEndTests
{
    private static End Decide(double gs, bool clear = false, bool inRollout = false,
        bool taxiSteering = false, bool taxiSeen = false)
        => LandingFlareAssistManager.DecideRolloutEnd(gs, clear, inRollout, taxiSteering, taxiSeen);

    [Fact]
    public void Without_taxi_guidance_it_ends_below_40_kt_and_interrupts_as_before()
    {
        Assert.Equal(End.Interrupting, Decide(39.9));
        Assert.Equal(End.Continue, Decide(40.0));
    }

    [Fact]
    public void Without_a_landing_rollout_leaving_the_runway_sideways_ends_it()
    {
        Assert.Equal(End.Interrupting, Decide(60.0, clear: true));
        Assert.Equal(End.Continue, Decide(60.0, clear: false));
    }

    // KSEA 16R, 2026-10-15: 70 kt, 14.8° off the runway heading, still on the pavement. The cue must keep
    // the tone there: no heading swing ends it any more.
    [Fact]
    public void The_KSEA_veer_on_the_pavement_keeps_the_cue()
    {
        Assert.Equal(End.Continue, Decide(70.2, inRollout: true, taxiSeen: true));
        Assert.Equal(End.Continue, Decide(70.2));
    }

    [Fact]
    public void During_a_landing_rollout_it_ends_only_when_stopped_and_silently()
    {
        Assert.Equal(End.Continue, Decide(54.9, inRollout: true, taxiSeen: true));
        Assert.Equal(End.Continue, Decide(10.0, inRollout: true, taxiSeen: true));
        Assert.Equal(End.Continue, Decide(3.0, inRollout: true, taxiSeen: true));
        Assert.Equal(End.Silent, Decide(2.9, inRollout: true, taxiSeen: true));
    }

    // During a landing rollout, leaving the runway is exit guidance's handoff (exitedLaterally, turnBegun),
    // whose takeover ends the cue silently; the cue never ends itself there, so it cannot clip that speech.
    [Fact]
    public void During_a_landing_rollout_leaving_the_runway_sideways_is_left_to_taxi_guidance()
        => Assert.Equal(End.Continue, Decide(40.0, clear: true, inRollout: true, taxiSeen: true));

    [Fact]
    public void After_taxi_guidance_ran_the_end_callout_is_queued()
        => Assert.Equal(End.Queued, Decide(39.0, taxiSeen: true));

    [Fact]
    public void Taxi_guidance_already_steering_ends_it_silently_at_any_speed()
    {
        Assert.Equal(End.Silent, Decide(85.0, taxiSteering: true, taxiSeen: true));
        // Below every end limit too: steering must win over Queued, not only over Continue.
        Assert.Equal(End.Silent, Decide(30.0, clear: true, taxiSteering: true, taxiSeen: true));
    }
}
