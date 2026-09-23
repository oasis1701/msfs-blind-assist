using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// A multipolygon's OUTLINE from its outer member ways (review OV-3). OSM splits a long outer
/// boundary across several ways that share their end nodes, listed in no particular order and
/// stored in either direction, so the OPEN ways are joined end to end — a way whose END meets the
/// chain is walked backwards — until each chain closes. Every ring competes on area and the
/// LARGEST is the outline; a chain that never closes (a broken relation) is dropped, a closed ring
/// whose net (signed) shoelace sum is a negligible FRACTION of its gross (unsigned) one — an
/// out-and-back retrace, which encloses nothing but can leave a small floating-point residual
/// rather than summing to exactly 0 — never wins either (<see cref="AbsArea"/>), and if nothing
/// closes — or nothing that closed encloses a real area — the answer is null and the caller keeps
/// the relation's bounds centre, as before. An EXACT duplicate member (the same vertex sequence,
/// either direction — a relation can list one way twice) is dropped before any of this runs
/// (<see cref="DropExactDuplicates"/>): left in, the greedy join can retrace the duplicate instead
/// of reaching the way that would actually close the ring, and with only one copy left there is
/// nothing else a way could be mistaken for.
///
/// <para>Two outer rings may TOUCH at a single node — something real OSM data can contain — and
/// the join must never run through that node into a figure-eight, whose shape (and so the answer)
/// would depend on member order. So a way that is closed on its own is pulled out as a ring right
/// away — a cheap shortcut, not the only thing standing between this and a figure-eight: left in
/// the join pool instead, the rule below would still split it back off the moment a chain returned
/// to its shared node. And when a way's END brings the chain back to a node it already passed, that
/// loop is split off as a ring of its own and the chain goes on from the node. Once exact
/// duplicates are dropped, where every END node is shared by at most two open ways and no ring
/// passes through the same node twice — a self-touching ring (OSM does not allow one) is outside
/// the promise — member order and way direction then decide nothing but which vertex the returned
/// ring starts at and which way round it runs, which no reader of a footprint cares about. A stray
/// spur — an open way whose far end matches no other way's endpoint — is harmless either way:
/// attached at an interior vertex of another way it is never examined by the join at all, and
/// attached at a real junction it makes that node's open-way count three, which the end-node rule
/// above already excludes.</para>
///
/// <para>Endpoints are matched EXACTLY: two members that meet share one OSM node, which Overpass
/// prints with the same coordinates every time. Inner ways are never passed in — an outline is what
/// a pilot is on or beside, and a readout has no use for the courtyard.</para>
/// </summary>
internal static class OsmRingAssembler
{
    /// <summary>The largest ring the <paramref name="outerWays"/> close into — at least three
    /// vertices enclosing a real area, closing duplicate dropped — or null when nothing closes, or
    /// every closed ring's area is negligible next to its own gross shoelace sum (an out-and-back
    /// retrace; see <see cref="AbsArea"/>). Ways of fewer than two points are ignored; on exactly
    /// equal areas the ring found first wins; the longitude scale used to compare areas is taken
    /// ONCE per call, from the first vertex of the first (post-deduplication) way, so near-equal
    /// rings compare on the same scale. Exact duplicate ways are dropped first
    /// (<see cref="DropExactDuplicates"/>).</summary>
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

        // ONE scale for the whole call: comparing rings measured at different latitudes on their
        // own scales can rank them differently than a shared scale would.
        double lonScale = origin.HasValue ? Math.Cos(origin.Value.Lat * Math.PI / 180.0) : 1.0;

        List<LatLon>? best = null;
        double bestArea = 0.0;
        foreach (var ring in rings)
        {
            ring.RemoveAt(ring.Count - 1);                  // the closing duplicate
            if (ring.Count < 3) continue;                   // out and back is no area
            double area = AbsArea(ring, lonScale, out double grossArea);
            // A retrace's net (signed, cancelling) sum sums to EXACTLY 0 in floating point only
            // when GUARANTEED to: its one cancelling pair of shoelace terms is ADJACENT in the
            // running sum (a single interior vertex). With two or more, other terms land between
            // the pair and rounding USUALLY — not always — leaves a residual instead: small in
            // absolute terms, but a real polygon's net area is nowhere near a billionth of its own
            // GROSS (every term taken absolute) sum, so judging the ratio catches it whichever way
            // a retrace's own net area happens to land — a small residual or exactly 0.
            if (area <= 1e-9 * grossArea) continue;
            if (area > bestArea) { best = ring; bestArea = area; }
        }
        return best;
    }

    /// <summary>Two ways are the SAME MEMBER when their vertex sequences are identical, either
    /// forwards or backwards — a relation can list one way twice (an OSM data quirk). Only the
    /// FIRST occurrence of each is kept; a way with fewer than two points is never treated as a
    /// duplicate of anything (there is nothing to compare beyond its single point, and it is
    /// dropped for real a few lines later regardless).</summary>
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

    /// <summary>Shoelace area in a local equirectangular frame, at the longitude scale <paramref
    /// name="lonScale"/> shared by every ring in this call. Only ever COMPARED, between rings of one
    /// relation, so its units do not matter; translated to the ring's own first vertex so a small
    /// ring does not lose its area to cancellation between large coordinates (translation does not
    /// change the area). <paramref name="grossArea"/> is the same sum with every term taken
    /// ABSOLUTE before adding — the scale the caller judges the net area's significance against, to
    /// tell a real polygon from an out-and-back retrace whose cancelling terms leave a small
    /// floating-point residual instead of summing to exactly 0.</summary>
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
