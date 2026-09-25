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
/// begins, but pulled back onto the runway_end centerline first (EGKK's rows sit ~110 m to the
/// SIDE of their own runway). All rows per runway end are offered: TaxiGraph.PickFullLengthStart
/// picks the full-length (furthest-back) one and REJECTS a row past 40 % of the runway. When no
/// usable row exists the physical pavement start is used.</para>
///
/// <para>The entry node is NOT a bare FindNearestNode: nothing guarantees a taxiway MEETS the
/// runway at the lineup point (LPPT 20's start row sits on a 1955 ft displaced threshold with the
/// nearest node 201 m OFF TO THE SIDE). FindRunwayLineupEntryNode resolves to a real runway
/// ENTRANCE instead, preferring one at or behind the lineup point, and is identical to
/// FindNearestNode whenever that node is within RUNWAY_REACH_MAX_CROSS_M of the centerline.</para>
/// </summary>
public static class RunwayLineupTarget
{
    public sealed record Result(double LineupLat, double LineupLon, TaxiNode? EntryNode);

    /// <summary>Half-width fallback when the runway row carries no width (feet).</summary>
    public const double DefaultRunwayWidthFeet = 150.0;

    /// <param name="startsForRunway">Every start row for this runway end (may be null/empty).</param>
    /// <param name="anchorLat">The aircraft's position when known — the reachability anchor;
    /// null when SimConnect has not reported one (never (0,0)).</param>
    public static Result Resolve(TaxiGraph graph, Runway rwy, IEnumerable<StartPosition>? startsForRunway,
                                 double? anchorLat, double? anchorLon)
    {
        StartPosition? start = startsForRunway == null ? null
            : TaxiGraph.PickFullLengthStart(startsForRunway, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);

        double lineupLat, lineupLon;
        if (start != null)
        {
            (lineupLat, lineupLon) = TaxiGraph.SnapStartToRunwayCenterline(
                start.Latitude, start.Longitude, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon);
        }
        else
        {
            lineupLat = rwy.StartLat;
            lineupLon = rwy.StartLon;
        }

        double halfWidthM = (rwy.Width > 0 ? rwy.Width : DefaultRunwayWidthFeet) * 0.3048 / 2.0;
        var entry = graph.FindRunwayLineupEntryNode(
            lineupLat, lineupLon,
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            halfWidthM, Services.TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M,
            anchorLat, anchorLon);
        return new Result(lineupLat, lineupLon, entry);
    }
}
