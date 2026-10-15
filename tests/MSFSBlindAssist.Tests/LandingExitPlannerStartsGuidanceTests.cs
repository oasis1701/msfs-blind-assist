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
}
