namespace MSFSBlindAssist.Navigation;

/// <summary>Which already-built geometry answers a runway-pavement probe for one airport.</summary>
public enum RunwayShapeSourceKind { None, ActiveGraph, WhereAmIGraph, Memo }

/// <summary>
/// The pure decision behind <c>TaxiGuidanceManager.IsOnRunwayPavement</c>: given the airport being
/// probed and the airports the two graph caches and the runway-shape memo currently hold, which one
/// answers. Out here because the manager cannot be built in a test (its constructor opens a
/// steering tone) and because the memo's whole point is an ORDERING that is easy to get wrong.
///
/// <para>The memo is the rung that keeps the probe from going blind. The Where-Am-I graph is
/// dropped by <c>OnAirportDataUpdated</c> whenever the online taxiway-name fetch lands — and the
/// probe's OWN warm-up is what starts that fetch, so the sequence is the ordinary one: warm, answer,
/// lose the graph seconds later, answer null for the rest of the minute the warm-up retry waits.
/// Null does not silence the passing callouts, so that minute permitted them ON A RUNWAY (a landing
/// with no exit plan, a flight started on the runway). Runway pavement does not depend on taxiway
/// NAMES, so shapes built before the fetch are still exactly right after it.</para>
///
/// <para>A graph for the airport always outranks the memo: it is fresher and it is what the memo
/// would be rebuilt from. The memo is dropped with the graph cache it shadows
/// (<c>ClearWhereAmICache</c>, which a database switch calls — the same airport can carry
/// different runway geometry in two databases) and replaced as soon as a graph for a different
/// airport is probed — never by the name fetch, which is the one invalidation it must outlive.</para>
/// </summary>
public static class RunwayShapeSource
{
    /// <summary>Each argument is the airport that source currently holds, or null when it holds
    /// nothing at all (no graph cached, no shapes memoised).</summary>
    public static RunwayShapeSourceKind Choose(string icao, string? activeGraphIcao, string? whereAmIGraphIcao, string? memoIcao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return RunwayShapeSourceKind.None;
        if (Holds(activeGraphIcao, icao)) return RunwayShapeSourceKind.ActiveGraph;
        if (Holds(whereAmIGraphIcao, icao)) return RunwayShapeSourceKind.WhereAmIGraph;
        if (Holds(memoIcao, icao)) return RunwayShapeSourceKind.Memo;
        return RunwayShapeSourceKind.None;
    }

    private static bool Holds(string? held, string icao) => held != null && string.Equals(held, icao, StringComparison.OrdinalIgnoreCase);
}
