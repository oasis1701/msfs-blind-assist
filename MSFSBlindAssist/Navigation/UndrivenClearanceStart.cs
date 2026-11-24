using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The taxiways at the front of a clearance that a route from the aircraft's own position can only
/// reach by turning straight back. VTBS 02R, 2026-11-18: cleared E7, E8, D6 after landing, but the
/// aircraft vacated one exit early, at E10. The router met E7 only at its junction with E8, so the
/// route ran up the parallel, out along E8 to that junction and straight back.
/// </summary>
public static class UndrivenClearanceStart
{
    /// <summary>
    /// How many leading taxiways to leave out so that the route from <paramref name="entryNodeId"/>
    /// no longer doubles back at a cleared taxiway it never drives. Zero when the router cannot
    /// honour the clearance, with or without them. The last cleared taxiway decides where a runway
    /// route ends [RTE-17] and always stays.
    /// </summary>
    /// <param name="route">The route built for the clearance with that many taxiways left out, so the
    /// caller need not build it again. When the router cannot honour the full clearance, its answer
    /// for the full clearance (a shortest-path fallback, or null).</param>
    /// <param name="fullClearanceRoute">The route the caller already built from this node for the full
    /// clearance (choosing a runway start builds it), taken as the first answer instead of asking the
    /// router again; null to build it here.</param>
    public static int Count(TaxiRouter router, int entryNodeId, int destinationNodeId,
        IReadOnlyList<string> taxiwaySequence, bool destinationIsRunway, out TaxiRoute? route,
        TaxiRoute? fullClearanceRoute = null)
    {
        var remaining = taxiwaySequence.ToList();
        int confirmed = 0;
        route = null;
        while (true)
        {
            var trial = remaining.Count == taxiwaySequence.Count && fullClearanceRoute != null
                ? fullClearanceRoute
                : router.FindConstrainedPath(entryNodeId, destinationNodeId, remaining, destinationIsRunway);
            if (trial is not { Segments.Count: > 0 } || trial.ConstrainedFallbackReason != null)
            {
                if (remaining.Count == taxiwaySequence.Count) route = trial;
                return confirmed;
            }
            confirmed = taxiwaySequence.Count - remaining.Count;
            route = trial;

            if (remaining.Count == 1 || !TaxiLeadIn.DoublesBackAtUndrivenStart(trial, remaining[0])) return confirmed;
            remaining.RemoveAt(0);
        }
    }

    /// <summary>False when one of the pilot's per-row picks (a hold short of any kind, a Turn) is
    /// keyed to one of the first <paramref name="count"/> taxiways: leaving those out would lose it.</summary>
    public static bool PicksAllow(int count, params IEnumerable<int>?[] pickRows)
        => !pickRows.Any(rows => rows != null && rows.Any(row => row < count));

    /// <summary>
    /// False when a Turn pick is keyed to one of the first <paramref name="count"/> taxiways or to the
    /// first one that stays. A Turn pick names the turn ONTO its row's taxiway from the row before; on
    /// the first taxiway that stays, that row is one being left out, and from wherever the route now
    /// arrives the same turn can be the other way round.
    /// </summary>
    public static bool TurnPicksAllow(int count, IEnumerable<int>? turnPickRows)
        => turnPickRows == null || !turnPickRows.Any(row => row <= count);

    /// <summary>
    /// What the pilot hears when the route still turns straight back at the first cleared taxiway:
    /// it could not be left out (a pick keyed to it, the only cleared taxiway, or a clearance the
    /// router cannot honour without it). The route is kept, on the pavement, and said.
    /// </summary>
    public static string TurnBackNote(string taxiway)
        => $"Taxiway {taxiway} is not on the way from here. The route goes out to it and turns back the way it came.";

    /// <summary>What the pilot hears about the taxiways left out.</summary>
    public static string Note(IReadOnlyList<string> dropped)
    {
        if (dropped.Count == 1) return $"Taxiway {dropped[0]} is not on the way from here and was left out.";
        string list = string.Join(", ", dropped.Take(dropped.Count - 1)) + " and " + dropped[^1];
        return $"Taxiways {list} are not on the way from here and were left out.";
    }
}
