using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The taxiway names of a route, in order, as a pilot hears them.
///
/// <para>ONE owner. Three places built this list with the same hand-copied loop: the route
/// summary spoken by <c>LoadRoute</c>, the "Route changed" callout's via-list, and the
/// remaining-sequence half of the recalculation's no-op guard. The first two SPEAK their
/// result, and <see cref="RouteChangedCallout"/> exists precisely so those two cannot drift
/// on how a route is described — leaving the builder of the list they both name as untested
/// inline code in three copies left the drift one level down, where the next tweak (skipping
/// connector names, say) would land in one copy and not the others.</para>
/// </summary>
public static class RouteTaxiwaySequence
{
    /// <summary>
    /// Distinct CONSECUTIVE taxiway names from <paramref name="start"/> onward: unnamed
    /// segments are dropped, and a run of segments sharing a name contributes it once.
    ///
    /// <para>Only ADJACENT repeats collapse. A route that genuinely leaves a taxiway and
    /// returns to it later names it twice, which is what the pilot taxis and what ATC cleared.
    /// Comparison is <c>OrdinalIgnoreCase</c>, matching every other taxiway-name comparison in
    /// the route pipeline (<c>TaxiGraph.BuildCanonicalTaxiwayNames</c> folds case variants onto
    /// one spelling as names enter the graph, which is what makes that safe).</para>
    /// </summary>
    /// <param name="segments">The route's segments. Null or empty yields an empty list.</param>
    /// <param name="start">First segment index to read; values below 0 are treated as 0, so a
    /// caller may pass a segment cursor straight in.</param>
    public static List<string> DistinctConsecutive(
        IReadOnlyList<TaxiRouteSegment>? segments, int start = 0)
    {
        var names = new List<string>();
        if (segments == null) return names;

        for (int i = Math.Max(0, start); i < segments.Count; i++)
        {
            string name = segments[i].TaxiwayName;
            if (string.IsNullOrEmpty(name)) continue;
            if (names.Count == 0 || !names[^1].Equals(name, StringComparison.OrdinalIgnoreCase))
                names.Add(name);
        }

        return names;
    }
}
