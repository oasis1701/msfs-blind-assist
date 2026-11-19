using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The taxiways at the front of a clearance that a route from the aircraft's own position can only
/// reach by turning straight back. VTBS 20L, 2026-11-18: cleared E7, E8, D6 after landing, but the
/// aircraft rolled past E7 and vacated at E9. The router met E7 only at its junction with E8, so the
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
    public static int Count(TaxiRouter router, int entryNodeId, int destinationNodeId,
        IReadOnlyList<string> taxiwaySequence, bool destinationIsRunway)
    {
        var remaining = taxiwaySequence.ToList();
        int confirmed = 0;
        while (true)
        {
            var trial = router.FindConstrainedPath(entryNodeId, destinationNodeId, remaining, destinationIsRunway);
            if (trial is not { Segments.Count: > 0 } || trial.ConstrainedFallbackReason != null) return confirmed;
            confirmed = taxiwaySequence.Count - remaining.Count;

            if (remaining.Count == 1 || !TaxiLeadIn.DoublesBackAtUndrivenStart(trial, remaining[0])) return confirmed;
            remaining.RemoveAt(0);
        }
    }

    /// <summary>False when a hold-short pick is keyed to one of the first <paramref name="count"/>
    /// taxiways: leaving those out would lose the pilot's own stop.</summary>
    public static bool PicksAllow(int count, IEnumerable<int>? holdShortIndices,
        IEnumerable<int>? runwayPickIndices, IEnumerable<int>? taxiwayPickIndices)
        => !new[] { holdShortIndices, runwayPickIndices, taxiwayPickIndices }
            .Any(indices => indices != null && indices.Any(i => i < count));

    /// <summary>Picks are keyed by position in the clearance, so they move with it.</summary>
    public static List<int>? Shift(List<int>? indices, int count)
        => indices?.Select(i => i - count).ToList();

    /// <inheritdoc cref="Shift(List{int}?, int)"/>
    public static Dictionary<int, string>? Shift(Dictionary<int, string>? picks, int count)
        => picks?.ToDictionary(p => p.Key - count, p => p.Value);

    /// <summary>What the pilot hears about the taxiways left out.</summary>
    public static string Note(IReadOnlyList<string> dropped)
    {
        if (dropped.Count == 1) return $"Taxiway {dropped[0]} is not on the way from here and was left out.";
        string list = string.Join(", ", dropped.Take(dropped.Count - 1)) + " and " + dropped[^1];
        return $"Taxiways {list} are not on the way from here and were left out.";
    }
}
