using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// A multipolygon's outline from its outer member ways. OSM splits a boundary across ways that share
/// end nodes, in any order and direction, so open ways are joined end to end until each chain
/// closes; the largest closed ring is the outline. A chain that never closes is dropped, and with
/// nothing closed (or nothing enclosing real area) the answer is null and the caller keeps the
/// relation's bounds centre. Exact duplicate members are dropped first, or the greedy join can
/// retrace one instead of closing the ring.
/// <para>Rings touching at one node must never join into a figure-eight whose shape depends on member
/// order: a way closed on its own is a ring as it stands, and a chain brought back to a node it
/// already passed has that loop split off. Endpoints match exactly (one shared OSM node prints the
/// same coordinates). Inner ways are never passed in.</para>
/// </summary>
internal static class OsmRingAssembler
{
    /// <summary>The largest ring the ways close into (at least three vertices enclosing real area,
    /// closing duplicate dropped), or null. Equal areas: the first found wins. One longitude scale per
    /// call, so rings compare on the same scale.</summary>
    internal static IReadOnlyList<LatLon>? LargestRing(IReadOnlyList<IReadOnlyList<LatLon>> outerWays)
    {
        var ways = DropExactDuplicates(outerWays);

        var rings = new List<List<LatLon>>();              // closed; closing duplicate still on
        var open = new List<IReadOnlyList<LatLon>>();
        LatLon? origin = null;
        foreach (var outer in ways)
        {
            if (origin == null && outer.Count > 0) origin = outer[0];
            if (outer.Count < 2) continue;
            if (outer[0] == outer[^1]) rings.Add(new List<LatLon>(outer));   // closed on its own: never joined
            else open.Add(outer);
        }

        while (open.Count > 0)
        {
            var chain = new List<LatLon>(open[0]);
            open.RemoveAt(0);

            while (chain[0] != chain[^1])
            {
                LatLon end = chain[^1];
                int next = open.FindIndex(w => w[0] == end || w[^1] == end);
                if (next < 0) break;                        // open for good: this chain is dropped
                var way = open[next];
                open.RemoveAt(next);
                if (way[0] == end)
                    for (int k = 1; k < way.Count; k++) chain.Add(way[k]);
                else
                    for (int k = way.Count - 2; k >= 0; k--) chain.Add(way[k]);

                // Back at a node the chain passed before (not its start): another ring touches this
                // one there. Split that loop off as a ring of its own and go on from the node.
                int back = chain.IndexOf(chain[^1], 1);
                if (back < chain.Count - 1)
                {
                    rings.Add(chain.GetRange(back, chain.Count - back));
                    chain.RemoveRange(back + 1, chain.Count - back - 1);
                }
            }
            if (chain[0] == chain[^1]) rings.Add(chain);
        }

        // One scale for the whole call, so rings rank consistently.
        double lonScale = origin.HasValue ? Math.Cos(origin.Value.Lat * Math.PI / 180.0) : 1.0;

        List<LatLon>? best = null;
        double bestArea = 0.0;
        foreach (var ring in rings)
        {
            ring.RemoveAt(ring.Count - 1);                  // the closing duplicate
            if (ring.Count < 3) continue;                   // out and back is no area
            double area = AbsArea(ring, lonScale, out double grossArea);
            // An out-and-back retrace can leave a rounding residual rather than exactly 0; no real
            // polygon's net area is a billionth of its gross sum.
            if (area <= 1e-9 * grossArea) continue;
            if (area > bestArea) { best = ring; bestArea = area; }
        }
        return best;
    }

    /// <summary>Keeps the first of ways with identical vertex sequences, forwards or backwards (a
    /// relation can list one way twice). A way under two points is never a duplicate.</summary>
    private static List<IReadOnlyList<LatLon>> DropExactDuplicates(IReadOnlyList<IReadOnlyList<LatLon>> outerWays)
    {
        var kept = new List<IReadOnlyList<LatLon>>();
        foreach (var way in outerWays)
            if (way.Count < 2 || !kept.Any(k => IsSameWay(k, way))) kept.Add(way);
        return kept;
    }

    private static bool IsSameWay(IReadOnlyList<LatLon> a, IReadOnlyList<LatLon> b)
    {
        if (a.Count != b.Count) return false;
        bool forward = true, backward = true;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) forward = false;
            if (a[i] != b[b.Count - 1 - i]) backward = false;
            if (!forward && !backward) return false;   // neither can become true again
        }
        return forward || backward;
    }

    /// <summary>Shoelace area in a local frame at the shared <paramref name="lonScale"/>, translated to
    /// the first vertex against cancellation; only ever compared. <paramref name="grossArea"/> is the
    /// same sum of absolute terms, the scale a retrace's residual is judged against.</summary>
    private static double AbsArea(IReadOnlyList<LatLon> ring, double lonScale, out double grossArea)
    {
        LatLon o = ring[0];
        double sum = 0.0, gross = 0.0;
        for (int i = 0; i < ring.Count; i++)
        {
            LatLon a = ring[i], b = ring[(i + 1) % ring.Count];
            double ax = (a.Lon - o.Lon) * lonScale, ay = a.Lat - o.Lat;
            double bx = (b.Lon - o.Lon) * lonScale, by = b.Lat - o.Lat;
            double term = ax * by - bx * ay;
            sum += term;
            gross += Math.Abs(term);
        }
        grossArea = gross / 2.0;
        return Math.Abs(sum) / 2.0;
    }
}
