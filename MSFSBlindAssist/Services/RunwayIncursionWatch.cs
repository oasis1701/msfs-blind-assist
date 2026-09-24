namespace MSFSBlindAssist.Services;

/// <summary>
/// Whether the runway-incursion warning still runs on the frames where taxi guidance has NO route
/// to follow.
///
/// <para><c>TaxiGuidanceManager.UpdatePosition</c> returns early without a route, and the warning
/// used to be rescued from that early return for one state only: <c>Arrived</c>, the normal end of
/// a landing-exit route, so the pilot still hears "Runway crossing ahead. Hold short." on the way
/// to the stand (runway 16/34 at EIDW lies east of the S6 exit).</para>
///
/// <para>There are now three more ways to finish on the airfield with no route and keep taxiing:
/// the runway-end countdown's "Runway vacated" close-out, and both backtrack endings. All three
/// land in <c>Taxiing</c>. Which of those states the pilot happens to be in should not decide
/// whether they are warned about a runway ahead of them — having the airport's map should.
/// Pure — <c>RunwayIncursionWatchTests</c>.</para>
/// </summary>
public static class RunwayIncursionWatch
{
    public static bool RunsWithoutARoute(TaxiGuidanceState state, bool hasGraph)
        => hasGraph
           && (state == TaxiGuidanceState.Arrived || state == TaxiGuidanceState.Taxiing);
}
