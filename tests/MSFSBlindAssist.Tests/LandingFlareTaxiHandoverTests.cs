using MSFSBlindAssist.Services;
using S = MSFSBlindAssist.Services.TaxiGuidanceState;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When taxi guidance TAKES OVER from a landing rollout, for the manual landing assist's silent handover
/// (LandingFlareAssistManager.StepTaxiHandover, fed every TaxiGuidanceManager state change).
///
/// Why this exists: near the runway end the backtrack handoff fires at a 15° turn while the assist ended
/// only at 20° or 40 kt, so two pan tones steered opposite ways and the assist's interrupting "Rollout
/// guidance complete" cut off "End of runway … Turn around". Counting backtracking in the taxi-steering
/// delegate ended the overlap but cut the sentence off within a frame. A takeover is therefore read from
/// the state sequence itself: leaving a landing rollout for anything but a route reload or a stop.
/// </summary>
public class LandingFlareTaxiHandoverTests
{
    private static int Takeovers(params S[] states)
    {
        bool inLandingRollout = false;
        int takeovers = 0;
        foreach (var state in states)
        {
            var step = LandingFlareAssistManager.StepTaxiHandover(inLandingRollout, state);
            inLandingRollout = step.InLandingRollout;
            if (step.TookOver) takeovers++;
        }
        return takeovers;
    }

    [Fact]
    public void Touchdown_passes_through_taxiing_before_the_rollout_without_a_takeover()
        => Assert.Equal(0, Takeovers(S.Inactive, S.RouteLoaded, S.Taxiing, S.LandingRollout));

    [Fact]
    public void A_retarget_route_reload_is_not_a_takeover()
        => Assert.Equal(0, Takeovers(S.LandingRollout, S.RouteLoaded, S.LandingRollout));

    [Fact]
    public void The_exit_handover_is_one_takeover()
        => Assert.Equal(1, Takeovers(S.LandingRollout, S.RouteLoaded, S.Taxiing));

    [Fact]
    public void A_landing_exit_closure_is_exactly_one_takeover()
        => Assert.Equal(1, Takeovers(S.LandingRollout, S.Taxiing, S.Arrived));

    [Fact]
    public void Backtracking_takes_over_once_and_its_later_handoff_does_not_count_again()
        => Assert.Equal(1, Takeovers(S.LandingRollout, S.BacktrackingOnRunway, S.Taxiing));

    [Fact]
    public void The_countdown_runway_vacated_handoff_is_a_takeover()
        => Assert.Equal(1, Takeovers(S.LandingRollout, S.Taxiing));

    [Fact]
    public void A_taxi_stop_is_not_a_takeover_and_ends_the_rollout()
        => Assert.Equal(0, Takeovers(S.LandingRollout, S.Inactive, S.Taxiing));

    [Fact]
    public void A_departure_never_takes_over()
        => Assert.Equal(0, Takeovers(S.Inactive, S.RouteLoaded, S.Taxiing, S.LiningUp));
}
