// Whether TaxiGuidanceManager is still actively flying a route -- the decision
// TaxiAssistForm.ShowRouteFailure's keepSummary parameter needs. PR #238 review follow-up
// (Important 3): this used to be answered by CurrentRoute != null, but HandleArrival sets
// State to Arrived without ever nulling _route (only StopGuidance and the rollout re-route
// paths do), so a completed flight's stale route wrongly protected the NEXT leg's failed
// Calculate summary from being replaced with the new failure's reason.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class LiveRouteStatesTests
{
    [Theory]
    [InlineData(TaxiGuidanceState.RouteLoaded)]
    [InlineData(TaxiGuidanceState.Taxiing)]
    [InlineData(TaxiGuidanceState.HoldShort)]
    [InlineData(TaxiGuidanceState.LiningUp)]
    public void A_route_actually_being_flown_is_live(TaxiGuidanceState state)
        => Assert.True(LiveRouteStates.IsRouteLive(state));

    [Fact]
    public void An_arrival_is_not_live_even_though_CurrentRoute_still_points_at_it()
    {
        // The exact case that was wrong before this fix: HandleArrival never nulls _route,
        // so CurrentRoute != null stayed true through a completed, docking-off arrival with
        // no Stop pressed -- but there is no route left for a failed Calculate to protect.
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.Arrived));
    }

    [Fact]
    public void Inactive_is_not_live()
        => Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.Inactive));

    [Fact]
    public void States_that_drive_their_own_callouts_are_not_counted_either()
    {
        // Progressive Taxi's terminal hold, and the rollout/backtrack states, are not states
        // a pilot would normally be re-opening Taxi Assist's Calculate button from -- the
        // brief's explicit four-state list, not "everything except Inactive/Arrived".
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.ProgressiveHold));
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.LandingRollout));
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.BacktrackingOnRunway));
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.BacktrackDeparture));
    }
}
