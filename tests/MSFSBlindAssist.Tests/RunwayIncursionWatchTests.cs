// Whether the runway-incursion warning still runs when guidance has no route to follow.
//
// PR #236 follow-up. Taxi guidance only walks its full per-frame path when it has a route; without
// one it returns early, and the incursion warning was rescued from that early return for exactly
// one state — Arrived, the normal end of a landing-exit route, so the pilot still gets "Runway
// crossing ahead. Hold short." while taxiing to the stand (runway 16/34 at EIDW lies on the way).
//
// Guidance now has three more ways to finish on the airfield with no route: the new "Runway
// vacated" close-out of the runway-end countdown, and both backtrack endings. All three leave the
// pilot taxiing with the airport's map loaded and every warning switched off. The state the pilot
// is in should not decide whether they are told about a runway ahead — having the map should.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayIncursionWatchTests
{
    [Fact]
    public void After_a_landing_exit_arrival_the_warning_still_runs()
        => Assert.True(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.Arrived, hasGraph: true));

    [Fact]
    public void After_vacating_or_backtracking_with_no_route_the_warning_still_runs()
        => Assert.True(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.Taxiing, hasGraph: true));

    [Fact]
    public void Without_the_airports_map_there_is_nothing_to_warn_about()
    {
        Assert.False(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.Arrived, hasGraph: false));
        Assert.False(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.Taxiing, hasGraph: false));
    }

    [Fact]
    public void States_that_own_their_own_callouts_are_left_alone()
    {
        // The rollout and the backtrack drive their own per-frame logic and their own callouts;
        // a Progressive Taxi leg has deliberately reached its terminator and holds.
        Assert.False(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.LandingRollout, hasGraph: true));
        Assert.False(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.BacktrackingOnRunway, hasGraph: true));
        Assert.False(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.ProgressiveHold, hasGraph: true));
        Assert.False(RunwayIncursionWatch.RunsWithoutARoute(TaxiGuidanceState.Inactive, hasGraph: true));
    }
}
