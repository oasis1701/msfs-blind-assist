using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.KbosHcJunctionFixture;

namespace MSFSBlindAssist.Tests;

public class TurnPickCarryOverTests
{
    private static AppliedTurnPick Pick(int row = 1, int afterDeparture = 4, int firstTaxiway = 5,
        IReadOnlyList<int>? blocked = null)
        => new AppliedTurnPick
        {
            RowIndex = row, Side = TurnSide.Left, TaxiwayName = "C",
            FirstSegmentAfterDeparture = afterDeparture, FirstTaxiwaySegment = firstTaxiway,
            BlockedNodeIds = blocked ?? Array.Empty<int>(),
        };

    private static TaxiRoute RouteWith(params AppliedTurnPick[] picks)
    {
        var r = new TaxiRoute();
        r.TurnPicks.AddRange(picks);
        return r;
    }

    private static List<int> Nodes(TaxiRoute route)
    {
        var ids = new List<int> { route.Segments[0].FromNode.NodeId };
        ids.AddRange(route.Segments.Select(s => s.ToNode.NodeId));
        return ids;
    }

    /// The route and pick of the KBOS H / connector / C case: planned from H, Left onto C, off-chart.
    private static (TaxiGraph G, TaxiRouter Router, TaxiRoute Route, AppliedTurnPick Pick) KbosPlanned()
    {
        var g = Build();
        var router = new TaxiRouter(g);
        string[] seq = { "H", "C" };
        var r0 = router.FindConstrainedPath(Node(g, HNorth), Node(g, CEast), seq.ToList());
        Assert.NotNull(r0);
        var ctx = new TurnPickContext(g, PavementMap.Build(g), seq, Node(g, CEast), DestinationIsRunway: false,
            CEast.Lat, CEast.Lon, 0.0, null, 0, MaxLengthRatio: 2.0, MaxLengthPadMeters: 500.0);
        var res = TurnPickApplier.Apply(r0!, new Dictionary<int, TurnSide> { [1] = TurnSide.Left }, ctx);
        return (g, router, res.Route, Assert.Single(res.Applied));
    }

    [Theory]
    [InlineData(0, TurnPickPhase.Ahead)]
    [InlineData(3, TurnPickPhase.Ahead)]
    [InlineData(4, TurnPickPhase.InTurn)]
    [InlineData(5, TurnPickPhase.Made)]
    [InlineData(9, TurnPickPhase.Made)]
    public void Phase_follows_the_segment_index(int index, TurnPickPhase expected)
        => Assert.Equal(expected, TurnPickCarryOver.PhaseOf(Pick(), index));

    [Fact]
    public void A_charted_pick_at_a_junction_node_is_never_in_turn()
    {
        var pick = Pick(afterDeparture: 4, firstTaxiway: 4);
        Assert.Equal(TurnPickPhase.Ahead, TurnPickCarryOver.PhaseOf(pick, 3));
        Assert.Equal(TurnPickPhase.Made, TurnPickCarryOver.PhaseOf(pick, 4));
    }

    [Fact]
    public void Tail_runs_from_the_departure_node_to_the_end_and_names_the_off_chart_pair()
    {
        var (g, _, route, pick) = KbosPlanned();

        var (nodeIds, pairs) = TurnPickCarryOver.TailFrom(route, pick);

        Assert.Equal(Node(g, X), nodeIds.First());
        Assert.Equal(Node(g, CEast), nodeIds.Last());
        Assert.Equal((Node(g, X), Node(g, Y)), Assert.Single(pairs));

        // A route with no off-chart segment names no pair.
        var plain = new TaxiRouter(g).FindConstrainedPath(Node(g, HNorth), Node(g, CWest), new List<string> { "H", "C" });
        Assert.NotNull(plain);
        var plainPick = Pick(afterDeparture: 0, firstTaxiway: 1);
        var (_, plainPairs) = TurnPickCarryOver.TailFrom(plain!, plainPick);
        Assert.Empty(plainPairs);
    }

    [Fact]
    public void Blocks_to_keep_are_those_of_made_picks_and_earlier_ones_with_nothing_removed()
    {
        var made = Pick(row: 1, afterDeparture: 2, firstTaxiway: 3, blocked: new[] { 10, 11, 12 });
        var inTurn = Pick(row: 2, afterDeparture: 6, firstTaxiway: 8, blocked: new[] { 20 });
        var ahead = Pick(row: 3, afterDeparture: 9, firstTaxiway: 10, blocked: new[] { 30 });
        var route = RouteWith(made, inTurn, ahead);

        var keep = TurnPickCarryOver.BlocksToKeep(route, 7, new HashSet<int>());

        Assert.Equal(new HashSet<int> { 10, 11, 12 }, keep);

        // Earlier recalculations' blocks are kept too. A recalculation that starts at a made turn's
        // junction exempts it on its own router only; the committed set still holds it, or the junction
        // would be unblocked for good once the made pick has left the route.
        var earlier = new HashSet<int> { 40, 12 };
        var withEarlier = TurnPickCarryOver.BlocksToKeep(route, 7, earlier);
        Assert.Equal(new HashSet<int> { 10, 11, 12, 40 }, withEarlier);
    }

    [Fact]
    public void Nothing_to_keep_on_a_null_route_or_a_route_without_picks()
    {
        Assert.Null(TurnPickCarryOver.FirstAhead(null, 0));
        Assert.Empty(TurnPickCarryOver.Missed(null, 0));
        Assert.Empty(TurnPickCarryOver.BlocksToKeep(null, 0, new HashSet<int>()));

        var empty = new TaxiRoute();
        Assert.Null(TurnPickCarryOver.FirstAhead(empty, 0));
        Assert.Empty(TurnPickCarryOver.Missed(empty, 0));
        Assert.Empty(TurnPickCarryOver.BlocksToKeep(empty, 0, new HashSet<int>()));

        // alreadyBlocked passes through whole, as a new, equal set.
        var already = new HashSet<int> { 5, 7 };
        foreach (var r in new TaxiRoute?[] { null, empty })
        {
            var result = TurnPickCarryOver.BlocksToKeep(r, 0, already);
            Assert.Equal(new HashSet<int> { 5, 7 }, result);
            Assert.False(ReferenceEquals(result, already));
        }
        Assert.Equal(new HashSet<int> { 5, 7 }, already);
    }

    [Fact]
    public void First_ahead_is_the_earliest_and_missed_are_those_in_turn()
    {
        var a = Pick(row: 1, afterDeparture: 2, firstTaxiway: 4);
        var b = Pick(row: 2, afterDeparture: 6, firstTaxiway: 7);
        var route = RouteWith(b, a);

        Assert.Same(b, TurnPickCarryOver.FirstAhead(route, 3));
        Assert.Null(TurnPickCarryOver.FirstAhead(route, 6));
        Assert.Same(a, Assert.Single(TurnPickCarryOver.Missed(route, 3)));
        Assert.Empty(TurnPickCarryOver.Missed(route, 5));
    }

    [Fact]
    public void Not_kept_are_the_picks_still_ahead_the_adopted_route_does_not_carry()
    {
        var made = Pick(row: 0, afterDeparture: 1, firstTaxiway: 2);
        var inTurn = Pick(row: 1, afterDeparture: 3, firstTaxiway: 5);
        var aheadA = Pick(row: 2, afterDeparture: 7, firstTaxiway: 8);
        var aheadB = Pick(row: 3, afterDeparture: 9, firstTaxiway: 10);
        var replaced = RouteWith(aheadB, made, aheadA, inTurn);

        // A route carrying none of them: both picks still ahead, in row order; made and in-turn are not "not kept".
        Assert.Equal(new[] { aheadA, aheadB }, TurnPickCarryOver.NotKept(replaced, 4, new TaxiRoute()));

        // A route carrying a rebased copy of one keeps it (matched by row, not by instance).
        var keepsA = RouteWith(Pick(row: 2, afterDeparture: 0, firstTaxiway: 1));
        Assert.Equal(new[] { aheadB }, TurnPickCarryOver.NotKept(replaced, 4, keepsA));

        var keepsBoth = RouteWith(Pick(row: 2, afterDeparture: 0, firstTaxiway: 1),
                                  Pick(row: 3, afterDeparture: 2, firstTaxiway: 3));
        Assert.Empty(TurnPickCarryOver.NotKept(replaced, 4, keepsBoth));

        // Nothing to say for a null or pick-less route being replaced.
        Assert.Empty(TurnPickCarryOver.NotKept(null, 0, new TaxiRoute()));
        Assert.Empty(TurnPickCarryOver.NotKept(new TaxiRoute(), 0, new TaxiRoute()));
    }

    [Fact]
    public void Prefix_rows_are_the_remaining_rows_before_the_picks_row()
    {
        var original = new[] { "E", "M", "F", "H", "C" };

        Assert.Equal(new[] { "F", "H" }, TurnPickCarryOver.PrefixRows(original, new[] { "F", "H", "C" }, 4));
        Assert.Empty(TurnPickCarryOver.PrefixRows(original, new[] { "C" }, 4));
        Assert.Equal(new[] { "E", "M", "F", "H" },
            TurnPickCarryOver.PrefixRows(original, original, 4));
        Assert.Empty(TurnPickCarryOver.PrefixRows(original, new[] { "H", "C" }, 2));
    }

    [Fact]
    public void Rebase_shifts_both_indices_by_where_the_departure_node_now_sits()
    {
        var (g, router, route, pick) = KbosPlanned();
        Assert.Equal(3, pick.FirstSegmentAfterDeparture);
        Assert.Equal(4, pick.FirstTaxiwaySegment);

        var nodes = Nodes(route).Skip(1).ToList();
        var newRoute = router.BuildRouteThrough(nodes, new[] { (Node(g, X), Node(g, Y)) });
        Assert.NotNull(newRoute);

        var rebased = Assert.Single(TurnPickCarryOver.Rebase(route.TurnPicks, pick, newRoute!));
        Assert.Equal(2, rebased.FirstSegmentAfterDeparture);
        Assert.Equal(3, rebased.FirstTaxiwaySegment);
        Assert.Equal(pick.DepartureNodeId, rebased.DepartureNodeId);

        // A route that no longer holds the departure node carries nothing over.
        var without = router.BuildRouteThrough(nodes.SkipWhile(id => id != Node(g, Y)).ToList());
        Assert.NotNull(without);
        Assert.Empty(TurnPickCarryOver.Rebase(route.TurnPicks, pick, without!));
    }

    [Fact]
    public void From_rows_skips_rows_without_a_taxiway()
    {
        var picks = TurnPickCarryOver.FromRows(new[] { (true, 0), (false, 1), (true, 2), (true, 1) });

        Assert.Equal(new Dictionary<int, TurnSide> { [1] = TurnSide.Right, [2] = TurnSide.Left }, picks);
    }

    [Fact]
    public void Without_rows_re_keys_and_drops_the_removed_rows_own_picks()
    {
        var picks = new Dictionary<int, TurnSide>
        {
            [0] = TurnSide.Left, [2] = TurnSide.Right, [3] = TurnSide.Left,
        };

        Assert.Equal(new Dictionary<int, TurnSide>
            { [0] = TurnSide.Left, [1] = TurnSide.Right, [2] = TurnSide.Left },
            TurnPickCarryOver.WithoutRows(picks, new[] { 1 }));
        Assert.Equal(new Dictionary<int, TurnSide> { [0] = TurnSide.Left, [2] = TurnSide.Left },
            TurnPickCarryOver.WithoutRows(picks, new[] { 2 }));
    }
}
