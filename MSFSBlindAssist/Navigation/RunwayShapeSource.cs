namespace MSFSBlindAssist.Navigation;

/// <summary>Which already-built geometry answers a runway-pavement probe for one airport.</summary>
public enum RunwayShapeSourceKind { None, ActiveGraph, WhereAmIGraph, Memo }

/// <summary>
/// The runway shapes <c>TaxiGuidanceManager</c> last memoised for ONE airport, as ONE value: the
/// airport, the database generation they were read under (<c>TaxiGuidanceManager.DatabaseGeneration</c>),
/// the graph they were built from, and the shapes. It replaced three fields that every writer and
/// every clear had to keep in step. An EMPTY <see cref="Shapes"/> is a real answer — an airport
/// with no runways, where "not on a runway" is exactly right.
/// </summary>
public sealed record RunwayShapeMemo(string Icao, long Generation, TaxiGraph? SourceGraph, IReadOnlyList<RunwayShape> Shapes);

/// <summary>
/// The pure decision behind <c>TaxiGuidanceManager.IsOnRunwayPavement</c>: given the airport being
/// probed and what the two graph caches and the runway-shape memo currently hold, which one
/// answers (<see cref="Choose"/>) and the memo to hold afterwards (<see cref="Resolve"/>). Out here
/// because the manager cannot be built in a test (its constructor opens a steering tone) and
/// because the memo's whole point is an ORDERING that is easy to get wrong.
///
/// <para>The memo is the rung that keeps the probe from going blind. The Where-Am-I graph is
/// dropped by <c>OnAirportDataUpdated</c> whenever the online taxiway-name fetch lands — and the
/// probe's OWN warm-up is what starts that fetch, so the sequence is the ordinary one: warm, answer,
/// lose the graph seconds later, answer null for the rest of the minute the warm-up retry waits.
/// Null does not silence the passing callouts, so that minute permitted them ON A RUNWAY. Runway
/// pavement does not depend on taxiway NAMES, so shapes built before the fetch are still exactly
/// right after it.</para>
///
/// <para>A graph for the airport always outranks the memo: it is fresher and it is what the memo
/// would be rebuilt from. The memo is dropped with the graph cache it shadows
/// (<c>ClearWhereAmICache</c>, which a database switch calls — the same airport can carry
/// different runway geometry in two databases) and replaced as soon as a graph for a different
/// airport is probed — never by the name fetch, which is the one invalidation it must outlive.</para>
///
/// <para>THE DATABASE GENERATION. A database switch (<c>ClearWhereAmICache</c>) drops the
/// Where-Am-I graph and the memo, moves <c>TaxiGuidanceManager.DatabaseGeneration</c>, and
/// deliberately leaves active guidance's own graph in place — a route being flown keeps its graph.
/// That graph was built from the PREVIOUS database, so once the generation has moved it neither
/// answers nor re-seeds the memo: it once did both, and the memo it re-seeded — the old runways,
/// filed under the new database — outlived <c>StopGuidance</c> for the rest of the session. A memo
/// of another generation is never read either.</para>
/// </summary>
public static class RunwayShapeSource
{
    /// <summary>Which source answers for <paramref name="icao"/>.</summary>
    /// <param name="currentGeneration">The manager's database generation now.</param>
    /// <param name="activeGraphIcao">The airport the active guidance graph holds, or null when there is no graph.</param>
    /// <param name="activeGraphGeneration">The generation that graph was installed under.</param>
    /// <param name="whereAmIGraphIcao">The airport the Where-Am-I graph holds, or null when none is cached.</param>
    /// <param name="memo">The runway-shape memo, or null.</param>
    public static RunwayShapeSourceKind Choose(string icao, long currentGeneration,
        string? activeGraphIcao, long activeGraphGeneration, string? whereAmIGraphIcao, RunwayShapeMemo? memo)
    {
        if (string.IsNullOrWhiteSpace(icao)) return RunwayShapeSourceKind.None;
        if (activeGraphGeneration == currentGeneration && Holds(activeGraphIcao, icao)) return RunwayShapeSourceKind.ActiveGraph;
        if (Holds(whereAmIGraphIcao, icao)) return RunwayShapeSourceKind.WhereAmIGraph;
        if (memo != null && memo.Generation == currentGeneration && Holds(memo.Icao, icao)) return RunwayShapeSourceKind.Memo;
        return RunwayShapeSourceKind.None;
    }

    /// <summary>
    /// The whole probe step, run by the manager under its lock: which shapes answer for
    /// <paramref name="icao"/> — null when nothing held can say — and the memo to hold afterwards.
    /// A GRAPH that answers re-seeds the memo from its own centrelines, once per graph instance
    /// (<see cref="RunwayShape.For"/> allocates, and a 2 s poll must not rebuild the set every
    /// tick), which is what lets the probe keep answering after that graph is dropped. Nothing else
    /// is written here — in particular never from an active graph of an older generation.
    /// </summary>
    public static (IReadOnlyList<RunwayShape>? Shapes, RunwayShapeMemo? Memo) Resolve(
        string icao, long currentGeneration,
        TaxiGraph? activeGraph, string? activeGraphIcao, long activeGraphGeneration,
        TaxiGraph? whereAmIGraph, string? whereAmIGraphIcao,
        RunwayShapeMemo? memo)
    {
        var source = Choose(icao, currentGeneration,
            activeGraph != null ? activeGraphIcao : null, activeGraphGeneration,
            whereAmIGraph != null ? whereAmIGraphIcao : null, memo);
        TaxiGraph? graph = source switch
        {
            RunwayShapeSourceKind.ActiveGraph => activeGraph,
            RunwayShapeSourceKind.WhereAmIGraph => whereAmIGraph,
            _ => null,
        };
        if (graph == null)
        {
            if (source == RunwayShapeSourceKind.Memo) return (memo!.Shapes, memo);
            return (null, memo);
        }
        if (memo != null && ReferenceEquals(memo.SourceGraph, graph) && memo.Generation == currentGeneration)
            return (memo.Shapes, memo);
        var reseeded = new RunwayShapeMemo(icao, currentGeneration, graph, RunwayPavement.BuildShapes(graph.RunwayCenterlines));
        return (reseeded.Shapes, reseeded);
    }

    private static bool Holds(string? held, string icao) => held != null && string.Equals(held, icao, StringComparison.OrdinalIgnoreCase);
}
