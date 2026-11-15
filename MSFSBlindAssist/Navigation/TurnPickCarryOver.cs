using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>Where a pick stands relative to the aircraft's segment. Public so a public test theory can take it.</summary>
public enum TurnPickPhase { Ahead, InTurn, Made }

/// <summary>
/// What a recalculation does with each Turn pick: keep one still ahead, report one missed in the
/// turn, and keep the blocks of those already made. Pure.
/// </summary>
internal static class TurnPickCarryOver
{
    /// The rows a rebuilt prefix must still follow before the pick's row: the entries of remainingSequence
    /// (a suffix of originalSequence) whose original index is below pickRowIndex. Empty when the remaining
    /// sequence already starts at or after that row.
    public static List<string> PrefixRows(IReadOnlyList<string> originalSequence,
        IReadOnlyList<string> remainingSequence, int pickRowIndex)
    {
        int start = Math.Max(0, originalSequence.Count - remainingSequence.Count);
        int count = Math.Min(pickRowIndex, originalSequence.Count) - start;
        return count <= 0 ? new List<string>() : originalSequence.Skip(start).Take(count).ToList();
    }

    /// The picks at or after `first`, with both segment indices shifted so that `first`'s
    /// FirstSegmentAfterDeparture is the index in newRoute of the first segment whose FromNode is
    /// first.DepartureNodeId. Empty when newRoute has no such segment.
    public static List<AppliedTurnPick> Rebase(IReadOnlyList<AppliedTurnPick> picks,
        AppliedTurnPick first, TaxiRoute newRoute)
    {
        int at = newRoute.Segments.FindIndex(s => s.FromNode.NodeId == first.DepartureNodeId);
        if (at < 0) return new List<AppliedTurnPick>();
        int shift = at - first.FirstSegmentAfterDeparture;
        return picks.Where(p => p.RowIndex >= first.RowIndex)
            .OrderBy(p => p.RowIndex)
            .Select(p => new AppliedTurnPick
            {
                RowIndex = p.RowIndex,
                Side = p.Side,
                TaxiwayName = p.TaxiwayName,
                DepartureNodeId = p.DepartureNodeId,
                JoinNodeId = p.JoinNodeId,
                FirstSegmentAfterDeparture = p.FirstSegmentAfterDeparture + shift,
                FirstTaxiwaySegment = p.FirstTaxiwaySegment + shift,
                ChangedRoute = p.ChangedRoute,
                IsOffChart = p.IsOffChart,
                OffChartLengthMeters = p.OffChartLengthMeters,
                BlockedNodeIds = p.BlockedNodeIds,
            })
            .ToList();
    }

    // Ahead: index < FirstSegmentAfterDeparture; InTurn: index < FirstTaxiwaySegment; else Made.
    public static TurnPickPhase PhaseOf(AppliedTurnPick pick, int currentSegmentIndex)
        => currentSegmentIndex < pick.FirstSegmentAfterDeparture ? TurnPickPhase.Ahead
         : currentSegmentIndex < pick.FirstTaxiwaySegment ? TurnPickPhase.InTurn
         : TurnPickPhase.Made;

    /// The earliest pick still Ahead, or null.
    public static AppliedTurnPick? FirstAhead(TaxiRoute? route, int currentSegmentIndex)
        => route?.TurnPicks
            .Where(p => PhaseOf(p, currentSegmentIndex) == TurnPickPhase.Ahead)
            .OrderBy(p => p.RowIndex)
            .FirstOrDefault();

    /// Node ids from the pick's departure node to the end of the route, and every off-chart pair
    /// inside it, in route order.
    public static (List<int> NodeIds, List<(int From, int To)> OffChartPairs) TailFrom(
        TaxiRoute route, AppliedTurnPick pick)
    {
        var nodeIds = new List<int>();
        var pairs = new List<(int From, int To)>();
        int start = Math.Max(0, pick.FirstSegmentAfterDeparture);
        for (int i = start; i < route.Segments.Count; i++)
        {
            var seg = route.Segments[i];
            if (nodeIds.Count == 0) nodeIds.Add(seg.FromNode.NodeId);
            nodeIds.Add(seg.ToNode.NodeId);
            if (seg.IsOffChartTurn) pairs.Add((seg.FromNode.NodeId, seg.ToNode.NodeId));
        }
        return (nodeIds, pairs);
    }

    /// Picks InTurn at this index (to be reported as missed), in row order.
    public static List<AppliedTurnPick> Missed(TaxiRoute? route, int currentSegmentIndex)
        => route == null
            ? new List<AppliedTurnPick>()
            : route.TurnPicks
                .Where(p => PhaseOf(p, currentSegmentIndex) == TurnPickPhase.InTurn)
                .OrderBy(p => p.RowIndex)
                .ToList();

    /// Picks Ahead on the route being replaced that the adopted route does not carry (matched by row,
    /// since a kept pick is a rebased copy), in row order: each must be said as "could not be kept".
    public static List<AppliedTurnPick> NotKept(TaxiRoute? replaced, int currentSegmentIndex, TaxiRoute adopted)
        => replaced == null
            ? new List<AppliedTurnPick>()
            : replaced.TurnPicks
                .Where(p => PhaseOf(p, currentSegmentIndex) == TurnPickPhase.Ahead
                         && !adopted.TurnPicks.Any(k => k.RowIndex == p.RowIndex))
                .OrderBy(p => p.RowIndex)
                .ToList();

    /// alreadyBlocked (the manager's running set: a recalculation drops made picks from the new route,
    /// so their junctions survive only there) union the BlockedNodeIds of every pick that is Made at
    /// this index, with nothing removed: this is the set committed for later recalculations. A
    /// recalculation that starts at one of these nodes exempts it on its own router only, never here,
    /// or the junction would stay unblocked once its made pick has left the route. Always a new set,
    /// never the caller's instance; a null route or one with no picks gives a copy of alreadyBlocked.
    public static HashSet<int> BlocksToKeep(TaxiRoute? route, int currentSegmentIndex,
        IReadOnlySet<int> alreadyBlocked)
    {
        var keep = new HashSet<int>(alreadyBlocked);
        if (route != null)
            foreach (var p in route.TurnPicks)
                if (PhaseOf(p, currentSegmentIndex) == TurnPickPhase.Made)
                    keep.UnionWith(p.BlockedNodeIds);
        return keep;
    }

    /// Row picks from the planner's rows: a row with no taxiway is skipped and does not consume an index.
    /// turnIndex: 0 Automatic, 1 Left, 2 Right.
    public static Dictionary<int, TurnSide> FromRows(IReadOnlyList<(bool HasTaxiway, int TurnIndex)> rows)
    {
        var picks = new Dictionary<int, TurnSide>();
        int index = 0;
        foreach (var (hasTaxiway, turnIndex) in rows)
        {
            if (!hasTaxiway) continue;
            if (turnIndex == 1) picks[index] = TurnSide.Left;
            else if (turnIndex == 2) picks[index] = TurnSide.Right;
            index++;
        }
        return picks;
    }

    /// Re-keys picks after rows were removed from the sequence (removedRowIndices in the old numbering).
    public static Dictionary<int, TurnSide> WithoutRows(IReadOnlyDictionary<int, TurnSide> picks,
        IReadOnlyCollection<int> removedRowIndices)
    {
        var result = new Dictionary<int, TurnSide>();
        foreach (var (row, side) in picks)
        {
            if (removedRowIndices.Contains(row)) continue;
            result[row - removedRowIndices.Count(r => r < row)] = side;
        }
        return result;
    }
}
