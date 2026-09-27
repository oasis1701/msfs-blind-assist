namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The runway a Progressive Taxi leg holds short of, or null. A "hold short of runway" terminator
/// names it outright. A "hold at named point" terminator takes it from the resolved node's own
/// <c>HoldShortName</c> (Build already labelled every hold node, including CAT III holds far back
/// from the runway); only a point OSM tags as a runway or ILS hold may fall back to the nearest
/// centreline within <see cref="TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M"/>, and an intermediate hold never
/// names a runway (PR #247 review R13: the unconditional fallback turned intermediate holds on
/// parallel taxiways into runway holds).
/// </summary>
public static class ProgressiveHoldRunwayResolver
{
    /// <param name="term">The leg's terminator.</param>
    /// <param name="finalNodeHoldShortName">The route's final node's <c>HoldShortName</c>, or null.</param>
    /// <param name="finalNodeLat">The final node's latitude, or null when no route is loaded.</param>
    /// <param name="finalNodeLon">The final node's longitude, or null when no route is loaded.</param>
    /// <param name="runways">The airport's runway centrelines.</param>
    public static string? Resolve(ProgressiveTerminator? term, string? finalNodeHoldShortName,
        double? finalNodeLat, double? finalNodeLon, IReadOnlyList<TaxiGraph.RunwayCenterline> runways)
    {
        if (term is null) return null;

        if (term.Type == ProgressiveTerminatorType.HoldShortRunway)
            return string.IsNullOrWhiteSpace(term.Target) ? null : term.Target;

        if (term.Type != ProgressiveTerminatorType.HoldAtNamedPoint) return null;

        string kind = term.HoldingPointKind.ToLowerInvariant();
        if (kind == "intermediate") return null;

        var named = RouteRunwayCrossings.ExtractRunwayDesignators(finalNodeHoldShortName);
        if (named.Count > 0) return named[0];

        if ((kind == "runway" || kind == "ils") && finalNodeLat is double lat && finalNodeLon is double lon)
            return TaxiGraph.MatchHoldShortRunwayName(lat, lon, runways, TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M);

        return null;
    }
}
