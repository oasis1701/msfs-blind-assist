using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Does this landing-exit handoff route drive back across the runway we just landed on?"
///
/// <para>Motivating defect (KATL 26R, 2026-08-27). The aircraft rolled past its planned
/// exit B1 (south side) without turning, so the overshoot monitor retargeted to the only
/// remaining exit, A at 8,843 ft — which leaves the runway on the NORTH side. The taxi
/// graph holds no runway edges, so A* cannot route along the runway to A's junction; it
/// routed B1 south, west along B, then north on H across the 08L threshold, 15-20 m inside
/// the runway's own threshold at 22 kt. 427 m and a 180 degree arc, which the pilot heard
/// as "very windy and curvy", and which left them on the wrong side of the field.</para>
///
/// <para><see cref="RolloutExitGate.IsHandoffRouteReachable"/> cannot catch this: it
/// measures only the FIRST segment's cross-track, and B1 started right at the aircraft.
/// Commit 425217ca records the same limitation from the other direction.</para>
///
/// <para>Pure (segments + a centerline in, bool out) so the rule is unit-testable without
/// a graph or a live position.</para>
/// </summary>
public static class RolloutRunwayReCrossing
{
    /// <summary>
    /// The graph centerline for the runway just landed on, or null when the graph does not
    /// carry it. Matched on EITHER designator through
    /// <see cref="RouteRunwayCrossings.NormalizeDesignator"/> — 26R and 08L are one piece
    /// of pavement, which is exactly the semantics wanted here, and the normalizer also
    /// makes "8L" and "08L" the same string.
    /// </summary>
    public static TaxiGraph.RunwayCenterline? FindLandingRunwayCenterline(
        IReadOnlyList<TaxiGraph.RunwayCenterline>? centerlines, string? runwayId)
    {
        if (centerlines is null || string.IsNullOrWhiteSpace(runwayId)) return null;
        string want = RouteRunwayCrossings.NormalizeDesignator(runwayId);
        foreach (var c in centerlines)
        {
            if (string.Equals(RouteRunwayCrossings.NormalizeDesignator(c.Name1), want, StringComparison.OrdinalIgnoreCase)
                || string.Equals(RouteRunwayCrossings.NormalizeDesignator(c.Name2), want, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    /// <summary>
    /// True when any segment from <paramref name="fromSegmentIndex"/> onward crosses the
    /// runway's centerline between its thresholds.
    ///
    /// <para>Uses <see cref="TaxiGraph.EdgeCrossesRunwayStatic"/> — a segment-vs-segment
    /// intersection, NOT a point-on-pavement test. The point test silently missed every
    /// crossing whose flanking nodes sit more than half-width + 5 m out (KBOS 33L via K/B/C,
    /// docs/taxi-guidance.md), which is most of them.</para>
    ///
    /// <para>Judged from <paramref name="fromSegmentIndex"/> because that is the segment
    /// the tone is about to steer at — a crossing already behind the aircraft is history,
    /// not a route it is about to fly.</para>
    /// </summary>
    public static bool RouteReCrossesRunway(
        IReadOnlyList<TaxiRouteSegment>? segments,
        int fromSegmentIndex,
        TaxiGraph.RunwayCenterline? runway)
    {
        if (segments is null || runway is null) return false;
        if (fromSegmentIndex < 0 || fromSegmentIndex >= segments.Count) return false;

        for (int i = fromSegmentIndex; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s?.FromNode is null || s.ToNode is null) continue;
            if (TaxiGraph.EdgeCrossesRunwayStatic(
                    s.FromNode.Latitude, s.FromNode.Longitude,
                    s.ToNode.Latitude, s.ToNode.Longitude,
                    runway.Lat1, runway.Lon1, runway.Lat2, runway.Lon2))
                return true;
        }
        return false;
    }
}
