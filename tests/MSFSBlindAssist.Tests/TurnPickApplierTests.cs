using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.KbosHcJunctionFixture;

namespace MSFSBlindAssist.Tests;

public class TurnPickApplierTests
{
    // ---- helpers ----

    private static TaxiRoute Route(TaxiGraph g, (double Lat, double Lon) from, (double Lat, double Lon) to,
        params string[] seq)
    {
        var r = new TaxiRouter(g).FindConstrainedPath(Node(g, from), Node(g, to), seq.ToList());
        Assert.NotNull(r);
        Assert.Null(r!.ConstrainedFallbackReason);
        return r;
    }

    private static TurnPickContext Ctx(TaxiGraph g, string[] seq, (double Lat, double Lon) to,
        double heading = 0.0, (double Lat, double Lon)? aircraft = null,
        ProgressiveTerminator? terminator = null, int pinnedNodeId = 0)
    {
        var ac = aircraft ?? to;
        return new TurnPickContext(g, PavementMap.Build(g), seq, Node(g, to), DestinationIsRunway: false,
            ac.Lat, ac.Lon, heading, terminator, pinnedNodeId,
            MaxLengthRatio: 2.0, MaxLengthPadMeters: 500.0);
    }

    private static List<int> Nodes(TaxiRoute route)
    {
        var ids = new List<int>();
        if (route.Segments.Count == 0) return ids;
        ids.Add(route.Segments[0].FromNode.NodeId);
        ids.AddRange(route.Segments.Select(s => s.ToNode.NodeId));
        return ids;
    }

    private static Dictionary<int, TurnSide> Picks(params (int Row, TurnSide Side)[] picks)
        => picks.ToDictionary(p => p.Row, p => p.Side);

    private static void AssertInOrder(List<int> nodes, params int[] expected)
    {
        int at = -1;
        foreach (int id in expected)
        {
            int next = nodes.IndexOf(id, at + 1);
            Assert.True(next > at, $"node {id} missing or out of order in [{string.Join(", ", nodes)}]");
            at = next;
        }
    }

    // ---- KBOS H / connector / C ----

    private static readonly string[] HC = { "H", "C" };

    [Theory]
    [InlineData("C")]
    [InlineData("")]
    public void Kbos_planned_from_H_left_builds_the_leg_straight_ahead(string connectorName)
    {
        var g = Build(connectorName);
        var r0 = Route(g, HNorth, CEast, HC);
        Assert.Contains(Node(g, K), Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, HC, CEast));

        Assert.Empty(res.Refusals);
        var applied = Assert.Single(res.Applied);
        Assert.Equal(1, applied.RowIndex);
        Assert.True(applied.IsOffChart);
        Assert.True(applied.ChangedRoute);
        Assert.Equal(Node(g, X), applied.DepartureNodeId);
        Assert.Equal(Node(g, Y), applied.JoinNodeId);
        Assert.InRange(applied.OffChartLengthMeters, 95, 98);
        Assert.Contains(Node(g, X), applied.BlockedNodeIds);
        Assert.Contains(Node(g, Y), applied.BlockedNodeIds);

        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, X), Node(g, Y), Node(g, C4), Node(g, CEast));
        Assert.DoesNotContain(Node(g, K), nodes);
        Assert.DoesNotContain(Node(g, N261), nodes);

        Assert.Equal(1, res.Route.Segments.Count(s => s.IsOffChartTurn));
        Assert.True(res.Route.Segments[applied.FirstSegmentAfterDeparture].IsOffChartTurn);
        Assert.Same(res.Applied, res.Route.TurnPicks);
    }

    [Fact]
    public void Kbos_first_row_from_the_connector_left()
    {
        var g = Build();
        var r0 = Route(g, N261, CEast, "C");
        Assert.Contains(Node(g, K), Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((0, TurnSide.Left)),
            Ctx(g, new[] { "C" }, CEast, heading: 234.5, aircraft: StopPosition));

        Assert.Empty(res.Refusals);
        var applied = Assert.Single(res.Applied);
        Assert.True(applied.IsOffChart);
        Assert.Equal(Node(g, N261), applied.DepartureNodeId);
        Assert.Equal(Node(g, C1), applied.JoinNodeId);
        Assert.Equal(0, applied.FirstSegmentAfterDeparture);

        var nodes = Nodes(res.Route);
        Assert.Equal(new[] { Node(g, N261), Node(g, C1), Node(g, Z), Node(g, Y) }, nodes.Take(4));
        Assert.DoesNotContain(Node(g, K), nodes);
    }

    [Fact]
    public void Kbos_left_does_not_lead_to_the_west_end()
    {
        var g = Build();
        var r0 = Route(g, HNorth, CWest, HC);

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, HC, CWest));

        Assert.Same(r0, res.Route);
        Assert.Empty(res.Applied);
        Assert.Equal(new TurnPickRefusal(1, TurnSide.Left, "C", TurnPickOutcome.DoesNotLead), Assert.Single(res.Refusals));
    }

    [Fact]
    public void Kbos_right_to_the_west_end_is_already_met()
    {
        var g = Build();
        var r0 = Route(g, HNorth, CWest, HC);

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Right)), Ctx(g, HC, CWest));

        Assert.Same(r0, res.Route);
        Assert.Empty(res.Refusals);
        var applied = Assert.Single(res.Applied);
        Assert.False(applied.IsOffChart);
        Assert.False(applied.ChangedRoute);
        Assert.Equal(Node(g, X), applied.DepartureNodeId);
    }

    [Fact]
    public void No_picks_returns_the_route_untouched()
    {
        var g = Build();
        var r0 = Route(g, HNorth, CEast, HC);

        var res = TurnPickApplier.Apply(r0, new Dictionary<int, TurnSide>(), Ctx(g, HC, CEast));

        Assert.Same(r0, res.Route);
        Assert.Empty(res.Applied);
        Assert.Empty(res.Refusals);
        Assert.Empty(r0.TurnPicks);
    }

    // ---- the cross graph: metres from J (x east, y north) ----

    private const double JLat = 37.0, JLon = -122.0;

    private static (double Lat, double Lon) P(double x, double y)
        => (JLat + y / 111132.0, JLon + x / (111132.0 * Math.Cos(JLat * Math.PI / 180.0)));

    private static readonly (double Lat, double Lon)
        J = P(0, 0), ANorth = P(0, 200),
        Wm = P(-150, 0), Wn = P(-300, 0), Em = P(150, 0), En = P(300, 0),
        Dn = P(150, 200), Ds = P(150, -200);

    private static TaxiGraph Cross(double ringLegMeters = 0, bool withD = false, bool withX1 = false)
    {
        var paths = new List<TaxiPath>();
        void Add(string name, (double Lat, double Lon) a, (double Lat, double Lon) b)
            => paths.Add(new TaxiPath
            {
                StartLat = a.Lat, StartLon = a.Lon, EndLat = b.Lat, EndLon = b.Lon,
                Name = name, Type = "PT", StartType = "N", EndType = "N", Width = 75.0,
            });

        Add("A", ANorth, J);
        Add("B", J, Wm);
        Add("B", Wm, Wn);
        Add("B", J, Em);
        Add("B", Em, En);
        if (ringLegMeters > 0)
        {
            var se = P(300, -ringLegMeters);
            var sw = P(-300, -ringLegMeters);
            Add("R", En, se);
            Add("R", se, sw);
            Add("R", sw, Wn);
        }
        if (withD)
        {
            Add("D", Em, Dn);
            Add("D", Em, Ds);
        }
        if (withX1)
        {
            Add("X1", Em, P(150, -50));
            Add("X1", En, P(300, -50));
            Add("X1", Wm, P(-150, -50));
        }
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
    }

    private static readonly string[] AB = { "A", "B" };

    [Fact]
    public void At_a_crossing_node_left_takes_the_east_arm_the_long_way_round()
    {
        var g = Cross(ringLegMeters: 100);
        var r0 = Route(g, ANorth, Wn, AB);
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, Wm), Node(g, Wn) }, Nodes(r0));
        Assert.InRange(r0.TotalDistanceMeters, 495, 505);

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, AB, Wn, heading: 180));

        Assert.Empty(res.Refusals);
        var applied = Assert.Single(res.Applied);
        Assert.False(applied.IsOffChart);
        Assert.True(applied.ChangedRoute);
        Assert.Equal(Node(g, J), applied.DepartureNodeId);
        Assert.Equal(Node(g, J), applied.JoinNodeId);

        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, En), Node(g, Wn));
        Assert.DoesNotContain(Node(g, Wm), nodes);
        Assert.DoesNotContain(res.Route.Segments, s => s.IsOffChartTurn);
        Assert.InRange(res.Route.TotalDistanceMeters, 1290, 1310);
    }

    [Fact]
    public void A_longer_ring_is_refused_as_a_long_detour()
    {
        var g = Cross(ringLegMeters: 300);
        var r0 = Route(g, ANorth, Wn, AB);

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, AB, Wn, heading: 180));

        Assert.Equal(TurnPickOutcome.LongDetour, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
        Assert.Empty(res.Applied);
    }

    [Fact]
    public void Progressive_end_of_taxiway_follows_the_pick()
    {
        var g = Cross();
        var r0 = Route(g, ANorth, Wn, AB);
        var ctx = Ctx(g, AB, Wn, heading: 180,
            terminator: new ProgressiveTerminator(ProgressiveTerminatorType.EndOfTaxiway, ""));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), ctx);

        Assert.Empty(res.Refusals);
        Assert.Equal(Node(g, En), res.DestinationNodeId);
        Assert.Equal(Node(g, En), Nodes(res.Route).Last());
    }

    [Fact]
    public void Progressive_hold_short_of_taxiway_takes_the_first_meeting_that_way()
    {
        var g = Cross(withX1: true);
        var r0 = Route(g, ANorth, Wm, AB);
        var ctx = Ctx(g, AB, Wm, heading: 180,
            terminator: new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, "X1"));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), ctx);

        Assert.Empty(res.Refusals);
        Assert.Equal(Node(g, Em), res.DestinationNodeId);
        Assert.Equal(Node(g, Em), Nodes(res.Route).Last());
    }

    [Fact]
    public void A_pick_already_met_does_not_re_resolve_the_progressive_end()
    {
        var g = Cross();
        var r0 = Route(g, ANorth, Wn, AB);
        var ctx = Ctx(g, AB, Wn, heading: 180,
            terminator: new ProgressiveTerminator(ProgressiveTerminatorType.EndOfTaxiway, ""));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Right)), ctx);

        Assert.Equal(Node(g, Wn), res.DestinationNodeId);
        Assert.Same(r0, res.Route);
        Assert.False(Assert.Single(res.Applied).ChangedRoute);
    }

    [Fact]
    public void A_row_the_route_never_names_gets_no_turn_found()
    {
        var g = Cross();
        var r0 = Route(g, ANorth, Wn, AB);
        var ctx = Ctx(g, new[] { "A", "Q" }, Wn, heading: 180);

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), ctx);

        Assert.Equal(new TurnPickRefusal(1, TurnSide.Left, "Q", TurnPickOutcome.NoTurnFound), Assert.Single(res.Refusals));
        Assert.Same(r0, res.Route);
    }

    [Fact]
    public void Two_picks_are_judged_in_row_order_at_their_own_junctions()
    {
        var g = Cross(withD: true);
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, ANorth, Dn, seq);
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, Em), Node(g, Dn) }, Nodes(r0));

        var both = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Left)), Ctx(g, seq, Dn, heading: 180));
        Assert.Same(r0, both.Route);
        Assert.Empty(both.Refusals);
        Assert.Equal(new[] { 1, 2 }, both.Applied.Select(a => a.RowIndex));
        Assert.Equal(new[] { Node(g, J), Node(g, Em) }, both.Applied.Select(a => a.DepartureNodeId));

        var mixed = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Right)), Ctx(g, seq, Dn, heading: 180));
        Assert.Equal(1, Assert.Single(mixed.Applied).RowIndex);
        Assert.Equal(new TurnPickRefusal(2, TurnSide.Right, "D", TurnPickOutcome.DoesNotLead), Assert.Single(mixed.Refusals));
        Assert.Same(r0, mixed.Route);
    }

    [Fact]
    public void A_pick_that_would_drop_the_pinned_node_is_refused()
    {
        var g = Cross(ringLegMeters: 100);
        var r0 = Route(g, ANorth, Wn, AB);
        Assert.Contains(Node(g, Wm), Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)),
            Ctx(g, AB, Wn, heading: 180, pinnedNodeId: Node(g, Wm)));

        Assert.Equal(TurnPickOutcome.NoTurnFound, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }
}
