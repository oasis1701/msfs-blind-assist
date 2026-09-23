using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// A multipolygon's OUTLINE from its outer member ways (review OV-3). OSM splits a long outer
/// boundary across several ways that share their end nodes, listed in no particular order and
/// stored in either direction, so the OPEN ways are joined end to end — a way whose END meets the
/// chain is walked backwards — until each chain closes. Every ring competes on area and the
/// LARGEST is the outline; a chain that never closes (a broken relation) is dropped, and if nothing
/// closes the answer is null and the caller keeps the relation's bounds centre, as before.
///
/// <para>Two outer rings may TOUCH at a single node — valid OSM — and the join must never run
/// through that node into a figure-eight, whose shape (and so the answer) depended on member
/// order. So a way that is closed on its own is a ring as it stands and never enters the join
/// (left in the pool it was spliced into whichever open chain reached the node first), and a
/// chain that comes back to a node it already passed has closed a ring THERE: that loop is split
/// off as a ring of its own and the chain goes on from the node. For outer rings that meet only at
/// nodes, member order and way direction then decide nothing but which vertex the returned ring
/// starts at and which way round it runs — which no reader of a footprint cares about.</para>
///
/// <para>Endpoints are matched EXACTLY: two members that meet share one OSM node, which Overpass
/// prints with the same coordinates every time. Inner ways are never passed in — an outline is what
/// a pilot is on or beside, and a readout has no use for the courtyard.</para>
/// </summary>
internal static class OsmRingAssembler
{
    /// <summary>The largest ring the <paramref name="outerWays"/> close into — at least three
    /// vertices, closing duplicate dropped — or null. Ways of fewer than two points are ignored; on
    /// exactly equal areas the ring found first wins.</summary>
    internal static IReadOnlyList<LatLon>? LargestRing(IReadOnlyList<IReadOnlyList<LatLon>> outerWays)
    {
        var rings = new List<List<LatLon>>();              // closed; closing duplicate still on
        var open = new List<IReadOnlyList<LatLon>>();
        foreach (var outer in outerWays)
        {
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

        List<LatLon>? best = null;
        double bestArea = -1.0;
        foreach (var ring in rings)
        {
            ring.RemoveAt(ring.Count - 1);                  // the closing duplicate
            if (ring.Count < 3) continue;                   // out and back is no area
            double area = AbsArea(ring);
            if (area > bestArea) { best = ring; bestArea = area; }
        }
        return best;
    }

    /// <summary>Shoelace area in a local equirectangular frame. Only ever COMPARED, between rings of
    /// one relation, so its units do not matter; measured from the first vertex so a small ring does
    /// not lose its area to cancellation between large coordinates.</summary>
    private static double AbsArea(IReadOnlyList<LatLon> ring)
    {
        LatLon o = ring[0];
        double k = Math.Cos(o.Lat * Math.PI / 180.0);
        double sum = 0.0;
        for (int i = 0; i < ring.Count; i++)
        {
            LatLon a = ring[i], b = ring[(i + 1) % ring.Count];
            double ax = (a.Lon - o.Lon) * k, ay = a.Lat - o.Lat;
            double bx = (b.Lon - o.Lon) * k, by = b.Lat - o.Lat;
            sum += ax * by - bx * ay;
        }
        return Math.Abs(sum) / 2.0;
    }
}
