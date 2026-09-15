using MSFSBlindAssist.Services;
using End = MSFSBlindAssist.Services.LandingFlareAssistManager.RolloutEnd;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the manual landing assist's rollout ends and what it says
/// (LandingFlareAssistManager.DecideRolloutEnd). The 40/55 kt and 20°/90 kt limits are today's and must
/// not move. What changed is the speech: silent when taxi guidance already steers, queued when taxi
/// guidance ran during the rollout so the assist never cuts taxi guidance's instructions off, and
/// unchanged when no taxi guidance ran.
/// </summary>
public class LandingFlareRolloutEndTests
{
    private static End Decide(double gs, double hdg = 0.0, bool exitGuidance = false,
        bool taxiSteering = false, bool taxiSeen = false)
        => LandingFlareAssistManager.DecideRolloutEnd(gs, hdg, exitGuidance, taxiSteering, taxiSeen);

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

    [Fact]
    public void Landing_exit_rollout_guidance_raises_the_end_speed_to_55_kt()
    {
        Assert.Equal(End.Queued, Decide(54.9, exitGuidance: true, taxiSeen: true));
        Assert.Equal(End.Continue, Decide(55.0, exitGuidance: true, taxiSeen: true));
    }

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
