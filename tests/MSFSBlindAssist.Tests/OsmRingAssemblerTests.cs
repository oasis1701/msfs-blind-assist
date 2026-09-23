using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A multipolygon's outline from its outer member ways (review OV-3). OSM splits a long boundary
/// across ways that share their end nodes, listed in any member order and stored in either
/// direction, and two outer rings may TOUCH at a single node; the joiner must close them into the
/// same rings whatever the order — the returned ring may only start at another vertex or run the
/// other way round.
/// </summary>
public class OsmRingAssemblerTests
{
    // A hexagon near EDDF, P0 to P5 clockwise from the north-west corner.
    private static readonly LatLon P0 = new(50.0460, 8.5930), P1 = new(50.0460, 8.5950), P2 = new(50.0450, 8.5960),
                                   P3 = new(50.0440, 8.5950), P4 = new(50.0440, 8.5930), P5 = new(50.0450, 8.5920);
    private static readonly LatLon[] Hexagon = { P0, P1, P2, P3, P4, P5 };

    // A small triangle well away from the hexagon, given closed.
    private static readonly LatLon T0 = new(50.0470, 8.5930), T1 = new(50.0470, 8.5932), T2 = new(50.0468, 8.5932);

    // Two rings that TOUCH at the single node X, which OSM allows: the kite A-B-X-C to the west of
    // X and, to its east, the much smaller triangle X-D-E or diamond X-D-E-F. The kite is the outline.
    private static readonly LatLon A = new(50.0450, 8.5910), B = new(50.0465, 8.5930), X = new(50.0450, 8.5950),
                                   C = new(50.0435, 8.5930), D = new(50.0455, 8.5960), E = new(50.0450, 8.5970),
                                   F = new(50.0445, 8.5960);
    private static readonly LatLon[] Kite = { A, B, X, C };

    private static IReadOnlyList<LatLon> Way(params LatLon[] pts) => pts;

    private static IReadOnlyList<LatLon>? Ring(params IReadOnlyList<LatLon>[] ways) => OsmRingAssembler.LargestRing(ways);

    [Fact]
    public void A_closed_way_is_a_ring_on_its_own()
        => Assert.Equal(Hexagon, Ring(Way(P0, P1, P2, P3, P4, P5, P0)));

    [Fact]
    public void Two_open_ways_are_joined_end_to_end_reversing_the_one_stored_backwards()
        // The first ends at P3; the second ENDS at P3 too, so it is walked backwards.
        => Assert.Equal(Hexagon, Ring(Way(P0, P1, P2, P3), Way(P0, P5, P4, P3)));

    [Fact]
    public void Member_order_and_direction_do_not_matter()
    {
        // Listed first, third, second — and the middle way stored backwards.
        Assert.Equal(Hexagon, Ring(Way(P0, P1, P2), Way(P4, P5, P0), Way(P4, P3, P2)));
        // Every other order gives the same ring too, give or take where it starts and which way
        // round it runs.
        AssertSameRingInEveryOrder(Hexagon, Way(P0, P1, P2), Way(P4, P5, P0), Way(P4, P3, P2));
    }

    [Fact]
    public void Of_two_rings_the_larger_is_the_outline()
        => Assert.Equal(Hexagon, Ring(Way(T0, T1, T2, T0), Way(P0, P1, P2, P3, P4, P5, P0)));

    [Fact]
    public void A_chain_that_never_closes_is_dropped_and_a_closed_way_still_counts()
        // Two ways that meet at P2 but never get back to P0 (a broken relation), and a closed
        // triangle: the triangle is the only ring there is.
        => Assert.Equal(new[] { T0, T1, T2 }, Ring(Way(P0, P1, P2), Way(P2, P3, P4), Way(T0, T1, T2, T0)));

    [Fact]
    public void Nothing_that_closes_is_no_outline()
    {
        Assert.Null(Ring(Way(P0, P1, P2), Way(P2, P3, P4)));   // open for good
        Assert.Null(Ring());                                    // no outer way at all
        Assert.Null(Ring(Way(P0), Way(P1, P0, P1)));            // one point; out and back is no area
    }

    [Fact]
    public void A_closed_way_touching_an_open_chain_is_never_spliced_into_it_in_any_member_order()
        // The closed triangle X-D-E touches the ring A-B-X + X-C-A at X. Left in the join pool, a
        // closed way was spliced into whichever chain reached X first: member order (ABX) (XDEX)
        // (XCA) came back as the seven-vertex figure-eight A,B,X,D,E,X,C.
        => AssertSameRingInEveryOrder(Kite, Way(A, B, X), Way(X, D, E, X), Way(X, C, A));

    [Fact]
    public void Two_rings_of_open_ways_touching_at_a_node_stay_two_rings_in_any_member_order()
        // The same touch with the small ring split into two open ways, X-D-E and E-F-X: a chain
        // that comes back to X has closed a ring there and must not run on through it — member
        // order (ABX) (XDE) (XCA) (EFX) came back as the eight-vertex figure-eight A,B,X,D,E,F,X,C.
        => AssertSameRingInEveryOrder(Kite, Way(A, B, X), Way(X, C, A), Way(X, D, E), Way(E, F, X));

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>Every member order of <paramref name="ways"/> must give <paramref name="expected"/> —
    /// starting at any vertex and running either way round, the only freedom member order may leave.</summary>
    private static void AssertSameRingInEveryOrder(IReadOnlyList<LatLon> expected, params IReadOnlyList<LatLon>[] ways)
    {
        foreach (var order in Orders(ways))
        {
            var ring = Ring(order);
            Assert.True(ring != null && IsSameRing(expected, ring),
                $"member order {string.Join(" ", order.Select(w => "(" + Labels(w) + ")"))} gave {(ring == null ? "no ring" : Labels(ring))}");
        }
    }

    /// <summary>The same cyclic sequence of vertices, from any starting vertex, either way round.</summary>
    private static bool IsSameRing(IReadOnlyList<LatLon> expected, IReadOnlyList<LatLon> actual)
    {
        int n = expected.Count;
        if (actual.Count != n) return false;
        for (int start = 0; start < n; start++)
        {
            bool forward = true, backward = true;
            for (int i = 0; i < n; i++)
            {
                forward &= actual[(start + i) % n] == expected[i];
                backward &= actual[(start - i + n) % n] == expected[i];
            }
            if (forward || backward) return true;
        }
        return false;
    }

    /// <summary>Every ordering of <paramref name="items"/>.</summary>
    private static IEnumerable<T[]> Orders<T>(T[] items)
    {
        if (items.Length <= 1) { yield return items; yield break; }
        for (int i = 0; i < items.Length; i++)
        {
            T[] rest = items.Where((_, j) => j != i).ToArray();
            foreach (T[] tail in Orders(rest))
                yield return new[] { items[i] }.Concat(tail).ToArray();
        }
    }

    /// <summary>The points' names ("ABXC", hexagon corners as digits), for a failure message.</summary>
    private static string Labels(IEnumerable<LatLon> pts)
    {
        var known = new Dictionary<LatLon, string>
        {
            [A] = "A", [B] = "B", [C] = "C", [D] = "D", [E] = "E", [F] = "F", [X] = "X",
            [P0] = "0", [P1] = "1", [P2] = "2", [P3] = "3", [P4] = "4", [P5] = "5",
        };
        return string.Concat(pts.Select(p => known.TryGetValue(p, out var name) ? name : "?"));
    }
}
