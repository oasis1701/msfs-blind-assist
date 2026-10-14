using MSFSBlindAssist.Services;
using End = MSFSBlindAssist.Services.LandingFlareAssistManager.RolloutEnd;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the manual landing assist's rollout ends and what it says
/// (LandingFlareAssistManager.DecideRolloutEnd). The 40 kt and 20°/90 kt limits are today's and must
/// not move. The speech: silent when taxi guidance already steers, queued when taxi guidance ran during
/// the rollout so the assist never cuts taxi guidance's instructions off, and unchanged when no taxi
/// guidance ran. The 55 kt end that used to apply while exit guidance ran retired on 2026-10-15: during
/// a landing rollout the cue shares the tone with exit guidance instead, and ends only by the takeover
/// or the turn-off.
/// </summary>
public class LandingFlareRolloutEndTests
{
    private static End Decide(double gs, double hdg = 0.0, bool inRollout = false,
        bool taxiSteering = false, bool taxiSeen = false)
        => LandingFlareAssistManager.DecideRolloutEnd(gs, hdg, inRollout, taxiSteering, taxiSeen);

    [Fact]
    public void Without_taxi_guidance_it_ends_below_40_kt_and_interrupts_as_before()
    {
        Assert.Equal(End.Interrupting, Decide(39.9));
        Assert.Equal(End.Continue, Decide(40.0));
    }

    [Fact]
    public void A_turn_off_beyond_20_degrees_ends_it_only_below_90_kt()
    {
        Assert.Equal(End.Interrupting, Decide(89.9, hdg: 20.1));
        Assert.Equal(End.Interrupting, Decide(89.9, hdg: -20.1));
        Assert.Equal(End.Continue, Decide(90.0, hdg: 20.1));
        Assert.Equal(End.Continue, Decide(60.0, hdg: 20.0));
    }

    // KSEA 16R, 2026-10-15: the cue must keep the centerline below 55 kt too; exit guidance takes the
    // tone for the exit by ownership (RolloutExitGate.ExitGuidanceOwnsTone), not by a speed.
    [Fact]
    public void During_a_landing_rollout_there_is_no_speed_end()
    {
        Assert.Equal(End.Continue, Decide(54.9, inRollout: true, taxiSeen: true));
        Assert.Equal(End.Continue, Decide(10.0, inRollout: true, taxiSeen: true));
        Assert.Equal(End.Continue, Decide(0.0, inRollout: true, taxiSeen: true));
    }

    [Fact]
    public void During_a_landing_rollout_the_turn_off_still_ends_it_queued()
        => Assert.Equal(End.Queued, Decide(40.0, hdg: 20.1, inRollout: true, taxiSeen: true));

    [Fact]
    public void After_taxi_guidance_ran_the_end_callout_is_queued()
        => Assert.Equal(End.Queued, Decide(39.0, taxiSeen: true));

    [Fact]
    public void Taxi_guidance_already_steering_ends_it_silently_at_any_speed()
    {
        Assert.Equal(End.Silent, Decide(85.0, hdg: 2.0, taxiSteering: true, taxiSeen: true));
        // Below every end limit too: steering must win over Queued, not only over Continue.
        Assert.Equal(End.Silent, Decide(30.0, hdg: 25.0, taxiSteering: true, taxiSeen: true));
    }
}
