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

    private static readonly (double Lat, double Lon) Ef = P(450, 0);

    private static TaxiPath Path(string name, (double Lat, double Lon) a, (double Lat, double Lon) b, double widthFt = 75.0)
        => new TaxiPath
        {
            StartLat = a.Lat, StartLon = a.Lon, EndLat = b.Lat, EndLon = b.Lon,
            Name = name, Type = "PT", StartType = "N", EndType = "N", Width = widthFt,
        };

    private static TaxiGraph Graph(List<TaxiPath> paths)
        => TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());

    /// x1AtX: X1 stubs 50 m south of the B nodes at these eastings instead of withX1's three;
    /// extendEast: B goes on from En to Ef, 450 m east.
    private static TaxiGraph Cross(double ringLegMeters = 0, bool withD = false, bool withX1 = false,
        double[]? x1AtX = null, bool extendEast = false)
    {
        var paths = new List<TaxiPath>();
        void Add(string name, (double Lat, double Lon) a, (double Lat, double Lon) b) => paths.Add(Path(name, a, b));

        Add("A", ANorth, J);
        Add("B", J, Wm);
        Add("B", Wm, Wn);
        Add("B", J, Em);
        Add("B", Em, En);
        if (extendEast) Add("B", En, Ef);
        foreach (double x in x1AtX ?? Array.Empty<double>())
            Add("X1", P(x, 0), P(x, -50));
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
        return Graph(paths);
    }

    private static readonly string[] AB = { "A", "B" };

    /// On the FINAL route: each applied pick's indices still point at its own departure and join, none of
    /// its blocked nodes comes back after its join, and the route has this many off-chart legs.
    private static void AssertPicksHold(TaxiRoute route, IReadOnlyList<AppliedTurnPick> applied, int offChartLegs)
    {
        var nodes = Nodes(route);
        foreach (var pick in applied)
        {
            Assert.Equal(pick.DepartureNodeId, route.Segments[pick.FirstSegmentAfterDeparture].FromNode.NodeId);
            Assert.Equal(pick.JoinNodeId, route.Segments[pick.FirstTaxiwaySegment].FromNode.NodeId);
            foreach (int id in nodes.Skip(pick.FirstTaxiwaySegment + 1))
                Assert.DoesNotContain(id, pick.BlockedNodeIds);
        }
        Assert.Equal(offChartLegs, route.Segments.Count(s => s.IsOffChartTurn));
    }

    // Off-chart then charted. A comes south to Pe (a dead end); the only charted way onto B is a
    // connector curving right to Bk, west of the junction, then a reversal. B runs east through B0 (60 m
    // straight ahead of Pe, no link) to Be1, where D crosses: south to T directly, north to Dn and round
    // a ring R back to Ds on D, short of T.
    private static readonly (double Lat, double Lon)
        A0 = P(0, 200), Pe = P(0, 0), Bk = P(-60, -60), B0 = P(0, -60), Be1 = P(150, -60),
        Be2 = P(300, -60), DnO = P(150, 40), DsO = P(150, -160), T = P(150, -260);

    private static TaxiGraph OffChartThenCharted()
    {
        const double w = 98.0;
        return Graph(new List<TaxiPath>
        {
            Path("A", A0, Pe, w),
            Path("", Pe, P(-40, -30), w),
            Path("", P(-40, -30), Bk, w),
            Path("B", P(-300, -60), Bk, w),
            Path("B", Bk, B0, w),
            Path("B", B0, Be1, w),
            Path("B", Be1, Be2, w),
            Path("D", Be1, DnO, w),
            Path("D", Be1, DsO, w),
            Path("D", DsO, T, w),
            Path("R", DnO, P(400, 40), w),
            Path("R", P(400, 40), P(400, -160), w),
            Path("R", P(400, -160), DsO, w),
        });
    }

    [Fact]
    public void An_off_chart_pick_then_a_charted_pick_both_change_the_route()
    {
        var g = OffChartThenCharted();
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, A0, T, seq);
        AssertInOrder(Nodes(r0), Node(g, Pe), Node(g, Bk), Node(g, B0), Node(g, Be1), Node(g, DsO), Node(g, T));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Left)), Ctx(g, seq, T, heading: 180));

        Assert.Empty(res.Refusals);
        Assert.Equal(new[] { 1, 2 }, res.Applied.Select(a => a.RowIndex));
        Assert.True(res.Applied[0].IsOffChart);
        Assert.Equal(Node(g, Pe), res.Applied[0].DepartureNodeId);
        Assert.Equal(Node(g, B0), res.Applied[0].JoinNodeId);
        Assert.False(res.Applied[1].IsOffChart);
        Assert.True(res.Applied[1].ChangedRoute);
        Assert.Equal(Node(g, Be1), res.Applied[1].DepartureNodeId);

        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, Pe), Node(g, B0), Node(g, Be1), Node(g, DnO), Node(g, DsO), Node(g, T));
        Assert.DoesNotContain(Node(g, Bk), nodes);
        AssertPicksHold(res.Route, res.Applied, offChartLegs: 1);
        Assert.Same(res.Applied, res.Route.TurnPicks);
    }

    // Charted then charted. From J, B goes west to Wm and east to Em; D is a loop Wm, T, Ds, Em; north of
    // Em, D goes on to Dn and Dnn, from where a ring R comes back to Ds. The charted route goes west.
    private static readonly (double Lat, double Lon)
        TL = P(-150, -200), DsL = P(150, -200), DnL = P(150, 50), DnnL = P(150, 100);

    private static TaxiGraph ChartedThenCharted() => Graph(new List<TaxiPath>
    {
        Path("A", ANorth, J),
        Path("B", J, Wm),
        Path("B", J, Em),
        Path("D", Wm, TL),
        Path("D", TL, DsL),
        Path("D", DsL, Em),
        Path("D", Em, DnL),
        Path("D", DnL, DnnL),
        Path("R", DnnL, P(250, 100)),
        Path("R", P(250, 100), P(250, -200)),
        Path("R", P(250, -200), DsL),
    });

    [Fact]
    public void A_charted_pick_then_another_charted_pick_both_change_the_route()
    {
        var g = ChartedThenCharted();
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, ANorth, TL, seq);
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, Wm), Node(g, TL) }, Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Left)), Ctx(g, seq, TL, heading: 180));

        Assert.Empty(res.Refusals);
        Assert.Equal(new[] { 1, 2 }, res.Applied.Select(a => a.RowIndex));
        Assert.All(res.Applied, a => Assert.True(a.ChangedRoute && !a.IsOffChart));
        Assert.Equal(new[] { Node(g, J), Node(g, Em) }, res.Applied.Select(a => a.DepartureNodeId));

        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, J), Node(g, Em), Node(g, DnL), Node(g, DnnL), Node(g, DsL), Node(g, TL));
        Assert.DoesNotContain(Node(g, Wm), nodes);
        AssertPicksHold(res.Route, res.Applied, offChartLegs: 0);
    }

    [Fact]
    public void Hold_short_of_taxiway_takes_the_nearest_meeting_beyond_v()
    {
        // X1 meets B at En and again at Ef; v (Em) carries none. The nearest that way is En, not Ef.
        var g = Cross(x1AtX: new[] { 300.0, 450.0 }, extendEast: true);
        var r0 = Route(g, ANorth, Wm, AB);
        var ctx = Ctx(g, AB, Wm, heading: 180,
            terminator: new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, "X1"));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), ctx);

        Assert.Empty(res.Refusals);
        Assert.Equal(Node(g, En), res.DestinationNodeId);
        Assert.Equal(Node(g, En), Nodes(res.Route).Last());
    }

    [Fact]
    public void A_pick_that_throws_is_refused_and_later_picks_still_run()
    {
        // The destination id is not in the graph: completing any option for row 1 throws inside the
        // router. Row 2 (already met, which never routes) must still be judged.
        var g = Cross(withD: true);
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, ANorth, Dn, seq);
        var log = new List<string>();
        var ctx = Ctx(g, seq, Dn, heading: 180) with { DestinationNodeId = 987654, Log = log.Add };

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Right), (2, TurnSide.Left)), ctx);

        Assert.Same(r0, res.Route);
        Assert.Equal(new TurnPickRefusal(1, TurnSide.Right, "B", TurnPickOutcome.NoTurnFound), Assert.Single(res.Refusals));
        Assert.Equal(2, Assert.Single(res.Applied).RowIndex);
        Assert.Contains(log, l => l.Contains("KeyNotFoundException"));
    }

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
    public void A_pick_that_changes_a_fallback_route_keeps_its_fallback_reason()
    {
        var g = Cross(ringLegMeters: 100);
        var r0 = Route(g, ANorth, Wn, AB);
        r0.ConstrainedFallbackReason = "taxiway Q not found";

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, AB, Wn, heading: 180));

        Assert.True(Assert.Single(res.Applied).ChangedRoute);
        Assert.NotSame(r0, res.Route);
        Assert.Equal("taxiway Q not found", res.Route.ConstrainedFallbackReason);
    }

    // A comes south into J; B runs 40 m east (a left turn) to Bm and on to Be; D leaves Bm south. The
    // route A, B, D turns left onto B and right onto D within 80 m, so walked along the route it ends
    // straight ahead (step 1 fails), while walked along B alone it is a clean left (step 2 passes)
    // with the very same nodes.
    private static readonly (double Lat, double Lon) Bm = P(40, 0), Be = P(140, 0), Dso = P(40, -200);

    [Fact]
    public void A_charted_option_that_is_the_route_itself_is_already_met()
    {
        var g = Graph(new List<TaxiPath>
        {
            Path("A", ANorth, J),
            Path("B", J, Bm),
            Path("B", Bm, Be),
            Path("D", Bm, Dso),
        });
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, ANorth, Dso, seq);
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, Bm), Node(g, Dso) }, Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, Dso, heading: 180));

        Assert.Empty(res.Refusals);
        var applied = Assert.Single(res.Applied);
        Assert.False(applied.ChangedRoute);
        Assert.False(applied.IsOffChart);
        Assert.Same(r0, res.Route);
        Assert.Equal(Node(g, J), applied.DepartureNodeId);
    }

    // A first row reached by a lead-in named for no row: A is the lead-in's last node, and the
    // incoming bearing is its last segment's, never the aircraft's heading.
    [Fact]
    public void A_first_row_reached_by_a_lead_in_joins_where_the_lead_in_ends()
    {
        var stand = P(0, 300);
        var g = Graph(new List<TaxiPath>
        {
            Path("L1", stand, ANorth),
            Path("A", ANorth, J),
            Path("B", J, Wm),
            Path("B", J, Em),
        });
        var seq = new[] { "B" };
        var r0 = new TaxiRouter(g).FindShortestPath(Node(g, stand), Node(g, Wm));
        Assert.NotNull(r0);
        Assert.Equal(new[] { Node(g, stand), Node(g, ANorth), Node(g, J), Node(g, Wm) }, Nodes(r0!));

        var join = TurnPickApplier.LocateJoin(r0!, seq, 0);

        Assert.NotNull(join);
        Assert.Equal(Node(g, J), join!.Value.DepartureNodeId);
        Assert.Equal(r0!.Segments[1].BearingDegrees, join.Value.IncomingBearing!.Value, 6);
        Assert.Equal(2, join.Value.FirstSegmentAfterDeparture);
        Assert.Equal(2, join.Value.FirstTaxiwaySegment);

        // Judged from that bearing (south), not the aircraft's heading (north): right is west, as built.
        var res = TurnPickApplier.Apply(r0, Picks((0, TurnSide.Right)), Ctx(g, seq, Wm, heading: 0.0, aircraft: stand));
        Assert.False(Assert.Single(res.Applied).ChangedRoute);
        Assert.Same(r0, res.Route);
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
