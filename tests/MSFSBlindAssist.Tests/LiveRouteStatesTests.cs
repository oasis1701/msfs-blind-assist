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
    [InlineData(TaxiGuidanceState.ProgressiveHold)]
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
    public void A_progressive_taxi_terminator_hold_is_live_so_a_refused_next_leg_keeps_the_summary()
    {
        // PR #238 review, Important B (a re-fix): ProgressiveHold was first EXCLUDED from
        // IsRouteLive, on the reasoning that it -- like the rollout/backtrack states below --
        // "drives its own callouts" and is not a state a pilot would normally be re-opening
        // Taxi Assist's Calculate button from. That reasoning does not hold for ProgressiveHold
        // specifically: it is EXACTLY the state HandleArrival's Progressive Taxi intercept sets
        // when a leg reaches its terminator (hold short of a runway/taxiway, a cleared
        // crossing, or end of taxiway) -- tone off, terminator sentence spoken, and per the
        // enum's own doc "the pilot sets the next leg," i.e. pressing Calculate again FROM this
        // state is the normal, designed way of flying a Progressive Taxi route leg by leg.
        //
        // Excluding it meant TaxiAssistForm's progError refusal site
        // (keepSummary: _guidanceManager.HasLiveRoute) always evaluated false while the pilot
        // sat in a terminator hold, so a refused NEXT leg silently wiped leg one's route
        // summary -- the pilot's only re-readable record of the route they are physically
        // holding at the end of, seconds ago, mid-taxi, with _route and the progressive
        // terminator both still live. Unlike TaxiGuidanceState.Arrived (which this member
        // exists to exclude), a ProgressiveHold carries none of that staleness: an Arrived
        // flight can sit for the rest of a session with a route nobody is flying any more, but
        // a ProgressiveHold is definitionally still mid-taxi.
        Assert.True(LiveRouteStates.IsRouteLive(TaxiGuidanceState.ProgressiveHold));
    }

    [Fact]
    public void The_rollout_and_backtrack_states_are_not_counted()
    {
        // These, unlike ProgressiveHold above, really are states a pilot would not normally be
        // re-opening Taxi Assist's Calculate button from -- a landing rollout or a runway
        // backtrack drives its own callouts and is not an invitation to plan a new route.
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.LandingRollout));
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.BacktrackingOnRunway));
        Assert.False(LiveRouteStates.IsRouteLive(TaxiGuidanceState.BacktrackDeparture));
    }
}
