using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Will the landing exit plan start exit guidance at the next touchdown at an airport
/// (LandingExitPlanner.WillStartGuidance)? The landing assist's rollout cue stays silent at touchdown only
/// then, because the planner's touchdown sentence is the one utterance. A plan for another airport, or one
/// already used this session (it starts guidance once per plan), says nothing there, so the cue confirms
/// itself instead.
/// </summary>
public class LandingExitPlannerStartsGuidanceTests
{
    [Fact]
    public void A_fresh_plan_at_this_airport_starts_guidance_whatever_the_icao_case()
    {
        Assert.True(LandingExitPlanner.WillStartGuidance(true, false, "KSEA", "KSEA"));
        Assert.True(LandingExitPlanner.WillStartGuidance(true, false, "ksea", "KSEA"));
    }

    [Fact]
    public void A_plan_for_another_airport_does_not()
        => Assert.False(LandingExitPlanner.WillStartGuidance(true, false, "KBFI", "KSEA"));

    [Fact]
    public void A_plan_already_used_this_session_does_not()
        => Assert.False(LandingExitPlanner.WillStartGuidance(true, true, "KSEA", "KSEA"));

    [Fact]
    public void No_plan_does_not()
        => Assert.False(LandingExitPlanner.WillStartGuidance(false, false, null, "KSEA"));

    // LandingExitPlanner.AwaitsTouchdown: the landing assist holds its touchdown sentence while the plan still
    // has to decide the touchdown, and no longer: once guidance started, or the plan's touchdown was settled
    // (the airborne edge cleared, as "Runway not identified" clears it).
    [Fact]
    public void A_plan_awaits_the_touchdown_only_until_it_has_decided_it()
    {
        Assert.True(LandingExitPlanner.AwaitsTouchdown(hasPendingExit: true, wasAirborne: true, activatedThisLanding: false));
        Assert.False(LandingExitPlanner.AwaitsTouchdown(hasPendingExit: true, wasAirborne: true, activatedThisLanding: true));
        Assert.False(LandingExitPlanner.AwaitsTouchdown(hasPendingExit: true, wasAirborne: false, activatedThisLanding: false));
        Assert.False(LandingExitPlanner.AwaitsTouchdown(hasPendingExit: false, wasAirborne: true, activatedThisLanding: false));
    }
}
