namespace MSFSBlindAssist.Navigation;

/// <summary>Which already-built geometry answers a runway-pavement probe for one airport.</summary>
public enum RunwayShapeSourceKind { None, ActiveGraph, WhereAmIGraph, Memo }

/// <summary>
/// The runway shapes last memoised for one airport, with the database generation they were read
/// under and the graph they came from (null for a runway-rows warm-up). An empty
/// <see cref="Shapes"/> is a real answer: no runways, so "not on a runway".
/// </summary>
public sealed record RunwayShapeMemo(string Icao, long Generation, TaxiGraph? SourceGraph, IReadOnlyList<RunwayShape> Shapes);

/// <summary>
/// The pure decision behind <c>TaxiGuidanceManager.IsOnRunwayPavement</c>: which held geometry
/// answers (active graph, Where-Am-I graph, memo — in that order) and the memo to hold afterwards.
/// <para>The memo keeps the probe answering after the Where-Am-I graph is dropped, which the online
/// taxiway-name fetch does seconds after an Alt+Y/Alt+L builds it. A null answer does not silence
/// the passing callouts, so without the memo they were permitted on a runway for up to a minute.
/// Runway pavement does not depend on taxiway names.</para>
/// <para>A database switch moves the generation and leaves an active route's graph in place; from
/// then on that graph neither answers nor re-seeds the memo, and a memo of another generation is
/// never read.</para>
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
    /// The whole probe step, run under the manager's lock: the shapes that answer (null when nothing
    /// held can) and the memo to hold afterwards. An answering graph re-seeds the memo once per graph
    /// instance, never per poll.
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

    /// <summary>Whether something read under generation <paramref name="readUnder"/> may be stored:
    /// only while no database switch has happened since.</summary>
    public static bool MayStore(long readUnder, long currentGeneration) => readUnder == currentGeneration;

    /// <summary>
    /// The memo after a runway-rows warm-up: a graph-less memo of <paramref name="shapes"/> when
    /// <see cref="MayStore"/> allows it and <paramref name="icao"/> is still the airport the probe is
    /// asked about (<paramref name="trackedIcao"/>), else <paramref name="held"/> unchanged — so a
    /// late warm-up for the previous airport cannot evict the current one's memo.
    /// </summary>
    public static RunwayShapeMemo? Publish(RunwayShapeMemo? held, string icao, long readUnder, long currentGeneration,
        IReadOnlyList<RunwayShape> shapes, string? trackedIcao)
        => MayStore(readUnder, currentGeneration) && Holds(trackedIcao, icao)
            ? new RunwayShapeMemo(icao, readUnder, null, shapes)
            : held;

    private static bool Holds(string? held, string icao) => held != null && string.Equals(held, icao, StringComparison.OrdinalIgnoreCase);
}
