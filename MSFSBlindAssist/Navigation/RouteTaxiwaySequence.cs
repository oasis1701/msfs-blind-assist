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

    /// <summary>
    /// The taxiways a route goes on to use AFTER the ones the pilot entered, in order.
    ///
    /// <para>The router follows the entered taxiways and then finds its own way to the
    /// destination ("after the last taxiway: A* to the destination node"), so a route can run
    /// well past the clearance the pilot typed. LOWW, 2026-11-14: "L" entered for Runway 16;
    /// the route ran L, W, EX23, E, EX33, D, and the summary said "via L" for a route whose
    /// last 888 of 2,001 m were on taxiways nobody had named.</para>
    ///
    /// <para>The entered names are walked against <see cref="DistinctConsecutive"/> in the order
    /// they were entered ("A, C, A" matches the second A second), and everything after the last
    /// one matched is returned. An entered taxiway the route only touches at a junction has no
    /// segment of its own: it is skipped, never allowed to end the walk. A junction the last one
    /// crosses is not "after" it (<see cref="JunctionCrossingMaxMetres"/>). Names BEFORE or BETWEEN
    /// the entered ones are not returned: the lead-in clause (<see cref="TaxiLeadIn"/>) owns the
    /// first, and a connector crossed between two cleared taxiways is deliberately not read out
    /// against the clearance the pilot read back.</para>
    ///
    /// <para>When NONE of the entered names is on the route (one entered taxiway, touched only
    /// at a junction) there is no last entered taxiway to start after, and nothing is returned:
    /// the whole route would put the lead-in behind "then" as well.</para>
    /// </summary>
    public static List<string> BeyondEntered(
        IReadOnlyList<string>? entered, IReadOnlyList<TaxiRouteSegment>? segments)
    {
        if (entered == null || entered.Count == 0) return new List<string>();

        var runs = NamedRuns(segments);
        var actual = runs.ConvertAll(r => r.Name);
        int next = 0;
        bool matchedAny = false;
        foreach (string name in entered)
        {
            for (int i = next; i < actual.Count; i++)
            {
                if (actual[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    next = i + 1;
                    matchedAny = true;
                    break;
                }
            }
        }
        if (!matchedAny) return new List<string>();

        next = PastJunctionCrossings(runs, next);
        return actual.GetRange(next, actual.Count - next);
    }

    /// <summary>
    /// The longest stretch on other names that still counts as CROSSING a junction on the last
    /// entered taxiway rather than leaving it. Where a taxiway crosses another, the navdata names
    /// the junction's short pieces for the crossing taxiway, and the summary then read the rest of
    /// the very taxiway the pilot entered as something new: OMDB, 2026-11-24, "via K4, then Z, K,
    /// K4, M7B, M, M13" for K4 driven straight over Z and K; LOWW the same day, "via L, then EX2, L,
    /// W, EX23". A way round by another taxiway and back is longer than a junction is wide.
    /// </summary>
    public const double JunctionCrossingMaxMetres = 120.0;

    /// <summary>
    /// Moves the start of the tail past every junction the last matched taxiway crosses: a return
    /// to that same taxiway after no more than <see cref="JunctionCrossingMaxMetres"/> on other
    /// names (unnamed pavement between counted). A stretch with no measured length is not judged,
    /// so a route built without distances reads exactly as before.
    /// </summary>
    private static int PastJunctionCrossings(List<(string Name, double Metres, double UnnamedBefore)> runs, int next)
    {
        string last = runs[next - 1].Name;
        while (true)
        {
            double gap = 0.0;
            int back = -1;
            for (int j = next; j < runs.Count; j++)
            {
                gap += runs[j].UnnamedBefore;
                if (gap > JunctionCrossingMaxMetres) break;
                if (runs[j].Name.Equals(last, StringComparison.OrdinalIgnoreCase))
                {
                    if (gap > 0.0) back = j;
                    break;
                }
                gap += runs[j].Metres;
                if (gap > JunctionCrossingMaxMetres) break;
            }
            if (back == -1) return next;
            next = back + 1;
        }
    }

    /// <summary>
    /// <see cref="DistinctConsecutive"/> with lengths: each run of segments sharing a name, how far
    /// it runs, and how much unnamed pavement lies between it and the named run before it.
    /// </summary>
    private static List<(string Name, double Metres, double UnnamedBefore)> NamedRuns(
        IReadOnlyList<TaxiRouteSegment>? segments)
    {
        var runs = new List<(string Name, double Metres, double UnnamedBefore)>();
        if (segments == null) return runs;

        double unnamed = 0.0;
        foreach (var seg in segments)
        {
            string name = seg.TaxiwayName;
            if (string.IsNullOrEmpty(name))
            {
                unnamed += seg.DistanceMeters;
                continue;
            }
            if (runs.Count > 0 && runs[^1].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                // Unnamed pavement inside one taxiway's run is part of that run.
                runs[^1] = (runs[^1].Name, runs[^1].Metres + unnamed + seg.DistanceMeters, runs[^1].UnnamedBefore);
            }
            else
            {
                runs.Add((name, seg.DistanceMeters, unnamed));
            }
            unnamed = 0.0;
        }
        return runs;
    }

    /// <summary>
    /// What follows "via" in the route summary when the route honoured an entered clearance:
    /// the entered taxiways exactly as given, then, after "then", whatever
    /// <see cref="BeyondEntered"/> says the route adds to reach the destination. "L, then W,
    /// EX23, E, EX33, D"; just "A, B, K" when the route ends on the clearance.
    /// </summary>
    public static string ViaClause(
        IReadOnlyList<string> entered, IReadOnlyList<TaxiRouteSegment>? segments)
    {
        string list = string.Join(", ", entered);
        var beyond = BeyondEntered(entered, segments);
        return beyond.Count == 0 ? list : $"{list}, then {string.Join(", ", beyond)}";
    }

    /// <summary>
    /// The named taxiways a route takes to get from one cleared taxiway to the next where the route
    /// never passes a node the two share, said after the "via" list: " From B, taxi via L1 to reach
    /// C." Empty when there are none. Where two cleared taxiways share no node the router links them
    /// itself (<c>TaxiRouter.FindRunwayBridge</c>), over whatever pavement joins them, and it does
    /// the same where every node they share is closed to it (a Turn pick's) or out of its reach; a
    /// named link the clearance never gave was then heard first as a turn call ("Sharp right now,
    /// taxiway W"). So the meeting is read from the route, never from the graph: where the route
    /// goes from the one to the other through a node carrying both names, they meet there, a piece
    /// of another taxiway crossed on the way is a junction, and it stays unsaid as
    /// <see cref="BeyondEntered"/> describes. An unnamed link has no name to say, and a taxiway
    /// crossed inside one cleared taxiway's run is not a link between two of them. Worded as the
    /// lead-in clause is (<see cref="TaxiLeadIn"/>, "First taxi via L1 to reach A."), and leads with a
    /// space so it appends the same way.
    /// </summary>
    public static string LinkClause(IReadOnlyList<string>? entered, IReadOnlyList<TaxiRouteSegment>? segments)
    {
        if (entered == null || entered.Count < 2 || segments == null) return "";
        var runs = NamedRunSpans(segments);
        var clause = new System.Text.StringBuilder();
        int next = 0, prevRun = -1;
        string? prevName = null;
        foreach (string name in entered)
        {
            int at = -1;
            for (int i = next; i < runs.Count && at == -1; i++)
                if (runs[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase)) at = i;
            // A cleared taxiway with no run of its own (touched only at a junction) pairs with nothing.
            if (at == -1) { prevRun = -1; prevName = null; continue; }
            if (prevName != null)
            {
                // From the last run of the previous taxiway before this one.
                int from = prevRun;
                for (int i = prevRun + 1; i < at; i++)
                    if (runs[i].Name.Equals(prevName, StringComparison.OrdinalIgnoreCase)) from = i;
                if (!MeetOnTheRoute(segments, runs[from].Last, runs[at].First, prevName, name))
                {
                    var links = new List<string>();
                    for (int i = from + 1; i < at; i++)
                    {
                        string link = runs[i].Name;
                        if (!links.Contains(link, StringComparer.OrdinalIgnoreCase)) links.Add(link);
                    }
                    if (links.Count > 0)
                        clause.Append($" From {prevName}, taxi via {JoinNames(links)} to reach {name}.");
                }
            }
            prevRun = at;
            prevName = name;
            next = at + 1;
        }
        return clause.ToString();
    }

    /// <summary>
    /// Whether the route, between the last segment of one run and the first of the next, passes a node
    /// that carries both taxiway names: the junction it went from the one to the other by.
    /// </summary>
    private static bool MeetOnTheRoute(IReadOnlyList<TaxiRouteSegment> segments, int lastOfFirst, int firstOfNext,
        string a, string b)
    {
        static bool Both(TaxiNode? n, string a, string b)
            => n?.TaxiwayNames != null && n.TaxiwayNames.Contains(a) && n.TaxiwayNames.Contains(b);
        for (int s = lastOfFirst; s < firstOfNext; s++)
            if (Both(segments[s].ToNode, a, b)) return true;
        return Both(segments[firstOfNext].FromNode, a, b);
    }

    /// <summary>
    /// The same runs as <see cref="NamedRuns"/> (unnamed pavement inside one taxiway's run is part of
    /// it), with the index of each run's first and last segment.
    /// </summary>
    private static List<(string Name, int First, int Last)> NamedRunSpans(IReadOnlyList<TaxiRouteSegment> segments)
    {
        var runs = new List<(string Name, int First, int Last)>();
        for (int i = 0; i < segments.Count; i++)
        {
            string name = segments[i].TaxiwayName;
            if (string.IsNullOrEmpty(name)) continue;
            if (runs.Count > 0 && runs[^1].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                runs[^1] = (runs[^1].Name, runs[^1].First, i);
            else
                runs.Add((name, i, i));
        }
        return runs;
    }

    private static string JoinNames(List<string> names)
        => names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
}
