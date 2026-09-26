// A liftoff during landing-exit guidance - a touch-and-go, or a go-around after touchdown.
//
// PR #252 follow-up. Nothing ended the landing rollout at liftoff: it kept measuring the runway the aircraft was
// climbing away from, its steering tone panning and its exit callouts speaking into the climb-out, and the exit
// plan stayed used up, so the next approach had no exit guidance. Now a liftoff that lasts ends that guidance
// and the plan is armed again; a bounce changes nothing.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class LandingExitGoAroundTests
{
    [Theory]
    [InlineData(TaxiGuidanceState.LandingRollout, false)]
    [InlineData(TaxiGuidanceState.LandingRollout, true)]
    [InlineData(TaxiGuidanceState.Taxiing, true)]
    public void A_liftoff_during_landing_exit_guidance_arms_the_check(TaxiGuidanceState state, bool landingExitRoute)
        => Assert.True(LandingExitGoAround.Arms(state, landingExitRoute));

    [Theory]
    [InlineData(TaxiGuidanceState.Inactive, false)]
    [InlineData(TaxiGuidanceState.Taxiing, false)]          // an ordinary taxi route: not a landing
    [InlineData(TaxiGuidanceState.RouteLoaded, true)]
    [InlineData(TaxiGuidanceState.Arrived, true)]
    public void Any_other_guidance_never_arms_it(TaxiGuidanceState state, bool landingExitRoute)
        => Assert.False(LandingExitGoAround.Arms(state, landingExitRoute));

    [Fact]
    public void Still_airborne_when_the_window_closes_ends_the_guidance()
        => Assert.True(LandingExitGoAround.Ends(freshSampleOnGround: false, TaxiGuidanceState.LandingRollout, false));

    [Fact]
    public void Back_on_the_ground_when_the_window_closes_was_a_bounce()
        => Assert.False(LandingExitGoAround.Ends(freshSampleOnGround: true, TaxiGuidanceState.LandingRollout, false));

    [Fact]
    public void Guidance_the_pilot_already_ended_is_left_alone()
        => Assert.False(LandingExitGoAround.Ends(freshSampleOnGround: false, TaxiGuidanceState.Inactive, false));

    [Fact]
    public void An_airborne_sample_holds_the_rollout()
        => Assert.True(LandingExitGoAround.HoldsRollout(false));

    [Theory]
    [InlineData(true)]
    [InlineData(null)]      // unknown air/ground never silences the rollout
    public void A_ground_or_unknown_sample_runs_it(bool? onGround)
        => Assert.False(LandingExitGoAround.HoldsRollout(onGround));

    [Fact]
    public void The_window_is_longer_than_a_bounce()
        => Assert.InRange(LandingExitGoAround.ConfirmMs, 3000, 10000);

    [Fact]
    public void The_pilot_hears_one_short_sentence_saying_whether_the_plan_is_kept()
    {
        Assert.Equal("Exit guidance off, plan kept.", LandingExitGoAround.Message(planKept: true));
        Assert.Equal("Exit guidance off.", LandingExitGoAround.Message(planKept: false));
    }
}
