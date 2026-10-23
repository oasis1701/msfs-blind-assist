using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Where a departure on a runway BEGINS and which graph node a route to it must end on — the
/// ONE owner of that maths, shared by TaxiAssistForm's destination list and the route briefing's
/// taxi-out plan so the two can never disagree about where a runway starts.
///
/// <para>The `start` table is navdatareader's curated "where MSFS spawns an aircraft if you
/// select runway X" value, which correctly accounts for displaced thresholds. It is ALSO the
/// source TaxiGraph builds RunwayCenterlines from, and TakeoffAssist's cross-track math reads
/// those centerlines; anchoring the lineup target here keeps taxi-lineup and TakeoffAssist on
/// the same physical position. The row is trusted for WHERE ALONG the runway the departure
/// begins, but pulled back onto the runway_end centerline first: EGKK's rows sit ~110 m to the
/// SIDE of their own runway, which would otherwise aim the lineup target — the thing a blind
/// pilot steers by — off the pavement, and feed the same error into the route destination node
/// and the holding-point envelope's near end. All rows per runway end are offered:
/// TaxiGraph.PickFullLengthStart picks the full-length (furthest-back) one and REJECTS a row
/// past 40 % of the runway. When no usable row exists the physical pavement start is
/// used.</para>
///
/// <para>The entry node is NOT a bare FindNearestNode: nothing guarantees a taxiway MEETS the
/// runway at the lineup point (LPPT 20's start row sits on a 1955 ft displaced threshold with the
/// nearest node 201 m OFF TO THE SIDE). FindRunwayLineupEntryNode resolves to a real runway
/// ENTRANCE instead, accepting only an entrance at or behind the lineup point, and is identical to
/// FindNearestNode whenever that node is within RUNWAY_REACH_MAX_CROSS_M of the centerline.</para>
/// </summary>
public static class RunwayLineupTarget
{
    public sealed record Result(double LineupLat, double LineupLon, TaxiNode? EntryNode);

    /// <summary>Full runway width in feet assumed when the runway row carries none; halved
    /// below, and <see cref="TaxiGraph.FindRunwayLineupEntryNode"/> searches within that
    /// half-width plus 5 m.</summary>
    private const double FallbackRunwayWidthFeet = 150.0;

    /// <param name="startsForRunway">Every start row for this runway end (may be null/empty).</param>
    /// <param name="anchorLat">The aircraft's position when known — the reachability anchor;
    /// null when SimConnect has not reported one (never (0,0)).</param>
    public static Result Resolve(TaxiGraph graph, Runway rwy, IEnumerable<StartPosition>? startsForRunway,
                                 double? anchorLat, double? anchorLon)
    {
        // TaxiGraph.DepartureLineupPoint: PickFullLengthStart + SnapStartToRunwayCenterline, and a
        // snapped point BEHIND the pavement edge kept only where taxi pavement reaches back to it
        // (a starter extension) — otherwise moved forward to the edge (Taxi2Gate LFPG 09L).
        var (lineupLat, lineupLon) = graph.DepartureLineupPoint(startsForRunway, rwy);

        double halfWidthM = (rwy.Width > 0 ? rwy.Width : FallbackRunwayWidthFeet) * 0.3048 / 2.0;
        var entry = graph.FindRunwayLineupEntryNode(
            lineupLat, lineupLon,
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            halfWidthM, Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M,
            anchorLat, anchorLon);
        return new Result(lineupLat, lineupLon, entry);
    }
}
