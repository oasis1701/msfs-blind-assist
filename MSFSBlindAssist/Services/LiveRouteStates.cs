namespace MSFSBlindAssist.Services;

/// <summary>
/// Whether <c>TaxiGuidanceManager</c> is still actively flying a route -- the question
/// <c>TaxiAssistForm.ShowRouteFailure</c>'s <c>keepSummary</c> decision needs in order to
/// protect the route-summary box from being overwritten by a failed Calculate's failure
/// reason, when the pilot is in fact still taxiing (or holding, or lining up) on a
/// PREVIOUSLY loaded route.
///
/// <para>PR #238 review follow-up (Important 3). That decision used to read
/// <c>TaxiGuidanceManager.CurrentRoute != null</c> (i.e. the manager's private <c>_route</c>
/// field) -- but <c>HandleArrival</c> sets <c>State</c> to <see
/// cref="TaxiGuidanceState.Arrived"/> and never nulls <c>_route</c>; only
/// <c>StopGuidance</c> and the landing-rollout re-route paths do. So after an arrival with
/// docking off and no Stop pressed, <c>_route</c> stays set for the rest of the session, and
/// the NEXT leg's failed Calculate wrongly kept the COMPLETED flight's stale summary
/// ("Taxi to Gate A12 via B, C, total 1.2 km…") on screen instead of replacing it with the
/// new failure's reason -- exactly the staleness <c>ShowRouteFailure</c>'s own doc says the
/// summary box must never show.</para>
///
/// <para>The fix reads the guidance STATE instead: only <see
/// cref="TaxiGuidanceState.RouteLoaded"/>, <see cref="TaxiGuidanceState.Taxiing"/>, <see
/// cref="TaxiGuidanceState.HoldShort"/> and <see cref="TaxiGuidanceState.LiningUp"/> count
/// as "a route actually being flown" for this purpose -- <see
/// cref="TaxiGuidanceState.Arrived"/>, <see cref="TaxiGuidanceState.Inactive"/> and the
/// terminal/self-contained states (<see cref="TaxiGuidanceState.ProgressiveHold"/>, the
/// rollout and backtrack states, which drive their own callouts and are not states a pilot
/// would normally be re-opening the taxi planner's Calculate button from) do not. Same
/// shape, same reason, as <c>Services/RunwayIncursionWatch</c>: which state the pilot
/// happens to be in should not decide the answer by accident of an unrelated field's
/// lifecycle -- the state itself should. Pure -- <c>LiveRouteStatesTests</c>.</para>
/// </summary>
public static class LiveRouteStates
{
    public static bool IsRouteLive(TaxiGuidanceState state) =>
        state is TaxiGuidanceState.RouteLoaded or TaxiGuidanceState.Taxiing
            or TaxiGuidanceState.HoldShort or TaxiGuidanceState.LiningUp;
}
