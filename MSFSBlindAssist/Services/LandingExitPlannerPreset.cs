using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>The airport and runway the Landing Exit Planner opens with. Both null: it opens empty.</summary>
public readonly record struct LandingExitPlannerPresetResult(string? Icao, string? RunwayId);

/// <summary>
/// What the Landing Exit Planner pre-fills (PR #236 review, finding F11). The ILS destination wins
/// when one is set; otherwise the loaded flight plan's arrival airport and runway, when it names
/// both. With neither, the runway box used to fall back to the airport's first runway, and a plan
/// made against that default is how issue #234 began (OMDB: planned on 12L, landed on 30L).
///
/// <para>Pure — <c>LandingExitPlannerPresetTests</c>.</para>
/// </summary>
public static class LandingExitPlannerPreset
{
    public static LandingExitPlannerPresetResult Resolve(
        string? ilsIcao, string? ilsRunwayId,
        string? flightPlanArrivalIcao, string? flightPlanArrivalRunway)
    {
        if (!string.IsNullOrWhiteSpace(ilsIcao))
            return new LandingExitPlannerPresetResult(
                ilsIcao.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(ilsRunwayId) ? null : ilsRunwayId.Trim());

        if (!string.IsNullOrWhiteSpace(flightPlanArrivalIcao) && !string.IsNullOrWhiteSpace(flightPlanArrivalRunway))
            return new LandingExitPlannerPresetResult(
                flightPlanArrivalIcao.Trim().ToUpperInvariant(), flightPlanArrivalRunway.Trim());

        return new LandingExitPlannerPresetResult(null, null);
    }

    /// <summary>True when two runway designators name the same runway end after
    /// <see cref="RouteRunwayCrossings.NormalizeDesignator"/>: "09" equals "9", "30L" equals "30l".
    /// A blank designator matches nothing.</summary>
    public static bool DesignatorsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(RouteRunwayCrossings.NormalizeDesignator(a),
                             RouteRunwayCrossings.NormalizeDesignator(b),
                             StringComparison.Ordinal);
    }
}
