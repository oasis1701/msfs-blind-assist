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

    [Theory]
    [InlineData("C")]
    [InlineData("")]
    public void Kbos_left_is_honored_across_the_unnamed_stretch_of_C_further_east(string connectorName)
    {
        // The real taxiway C: 900 m east of the junction the navdata leaves 400 m of it unnamed, so the
        // route "H, C" to runway 33L is only followed by chaining across that gap. VirtualPilot, flying
        // the incident on the whole airport, heard "does not lead to Runway 33L": blocking the junction
        // had switched the chaining off for all of C.
        var g = Build(connectorName, eastGap: true);
        var r0 = Route(g, HNorth, CFarEast, HC);
        Assert.Contains(Node(g, K), Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, HC, CFarEast));

        Assert.Empty(res.Refusals);
        var applied = Assert.Single(res.Applied);
        Assert.True(applied.IsOffChart);
        Assert.Equal(Node(g, X), applied.DepartureNodeId);
        Assert.Equal(Node(g, Y), applied.JoinNodeId);
        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, X), Node(g, Y), Node(g, C4), Node(g, CEast), Node(g, GapMid), Node(g, GapEnd), Node(g, CFarEast));
        Assert.DoesNotContain(Node(g, K), nodes);
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
    public void Off_the_network_a_first_row_pick_never_moves_the_route_start()
    {
        // The route starts at N260, the node LoadRoute's first-leg check passed; the aircraft's nearest
        // node is N261. On the network the pick works from N261 and the route starts there; off it
        // (KeepRouteStart) the start stays N260, whatever becomes of the pick.
        var g = Build();
        var r0 = Route(g, N260, CEast, "C");
        var onNetwork = Ctx(g, new[] { "C" }, CEast, heading: 234.5, aircraft: StopPosition);

        var moved = TurnPickApplier.Apply(r0, Picks((0, TurnSide.Left)), onNetwork);
        Assert.Equal(Node(g, N261), Nodes(moved.Route)[0]);

        var r1 = Route(g, N260, CEast, "C");
        var kept = TurnPickApplier.Apply(r1, Picks((0, TurnSide.Left)), onNetwork with { KeepRouteStart = true });
        Assert.Equal(Node(g, N260), Nodes(kept.Route)[0]);
        Assert.All(kept.Applied, p => Assert.Equal(Node(g, N260), p.DepartureNodeId));
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

    // The destination is the ring's south-east corner, short of where the ring comes back onto D: the
    // charted route reaches it down D and along the ring, the picked one up D and round the ring the
    // other way, never touching the arm of D it turned away from.
    private static readonly (double Lat, double Lon) RingSe = P(400, -160);

    [Fact]
    public void An_off_chart_pick_then_a_charted_pick_both_change_the_route()
    {
        var g = OffChartThenCharted();
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, A0, RingSe, seq);
        AssertInOrder(Nodes(r0), Node(g, Pe), Node(g, Bk), Node(g, B0), Node(g, Be1), Node(g, DsO), Node(g, RingSe));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Left)), Ctx(g, seq, RingSe, heading: 180));

        Assert.Empty(res.Refusals);
        Assert.Equal(new[] { 1, 2 }, res.Applied.Select(a => a.RowIndex));
        Assert.True(res.Applied[0].IsOffChart);
        Assert.Equal(Node(g, Pe), res.Applied[0].DepartureNodeId);
        Assert.Equal(Node(g, B0), res.Applied[0].JoinNodeId);
        Assert.False(res.Applied[1].IsOffChart);
        Assert.True(res.Applied[1].ChangedRoute);
        Assert.Equal(Node(g, Be1), res.Applied[1].DepartureNodeId);

        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, Pe), Node(g, B0), Node(g, Be1), Node(g, DnO), Node(g, RingSe));
        Assert.DoesNotContain(Node(g, Bk), nodes);
        Assert.DoesNotContain(Node(g, DsO), nodes);
        AssertPicksHold(res.Route, res.Applied, offChartLegs: 1);
        Assert.Same(res.Applied, res.Route.TurnPicks);
    }

    // The same clearance to T, the south end of D. Up D and round the ring only leads there by coming
    // back onto D at Ds and down the arm the pick turned away from: the second pick is refused, the
    // first stays.
    [Fact]
    public void A_pick_that_only_leads_there_down_the_arm_it_turned_away_from_is_refused()
    {
        var g = OffChartThenCharted();
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, A0, T, seq);

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Left)), Ctx(g, seq, T, heading: 180));

        Assert.True(Assert.Single(res.Applied).IsOffChart);
        Assert.Equal(new TurnPickRefusal(2, TurnSide.Left, "D", TurnPickOutcome.DoesNotLead), Assert.Single(res.Refusals));
        var nodes = Nodes(res.Route);
        AssertInOrder(nodes, Node(g, Pe), Node(g, B0), Node(g, Be1), Node(g, DsO), Node(g, T));
        Assert.DoesNotContain(Node(g, DnO), nodes);
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

    // The first pick (left onto B, east) changes the route: down D's loop to the destination. The second
    // (left onto D, north) would only lead there round the ring and back onto D at Ds, the arm it
    // turned away from: it is refused, and the route the first pick made stays.
    [Fact]
    public void A_charted_pick_stays_when_a_later_one_is_refused_for_the_other_arm()
    {
        var g = ChartedThenCharted();
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, ANorth, TL, seq);
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, Wm), Node(g, TL) }, Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left), (2, TurnSide.Left)), Ctx(g, seq, TL, heading: 180));

        var applied = Assert.Single(res.Applied);
        Assert.Equal(1, applied.RowIndex);
        Assert.True(applied.ChangedRoute && !applied.IsOffChart);
        Assert.Equal(Node(g, J), applied.DepartureNodeId);
        Assert.Equal(new TurnPickRefusal(2, TurnSide.Left, "D", TurnPickOutcome.DoesNotLead), Assert.Single(res.Refusals));
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, Em), Node(g, DsL), Node(g, TL) }, Nodes(res.Route));
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

    // A comes south into J and B goes straight on for 40 m to Bs, where D leaves to the east: a LEFT
    // turn, but onto D. Walked 80 m along the route, that turn used to count as "left onto B".
    [Fact]
    public void A_turn_onto_the_next_taxiway_is_not_this_rows_turn()
    {
        var bs = P(0, -40);
        var de = P(200, -40);
        var g = Graph(new List<TaxiPath>
        {
            Path("A", ANorth, J),
            Path("B", J, bs),
            Path("B", bs, P(0, -140)),
            Path("D", bs, de),
        });
        var seq = new[] { "A", "B", "D" };
        var r0 = Route(g, ANorth, de, seq);
        Assert.Equal(new[] { Node(g, ANorth), Node(g, J), Node(g, bs), Node(g, de) }, Nodes(r0));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, de, heading: 180));

        // B is joined straight ahead: there is no left turn onto it, and the pilot is told so.
        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.NoTurnFound, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);

        // The left turn is the next row's, and is found there.
        var onD = TurnPickApplier.Apply(r0, Picks((2, TurnSide.Left)), Ctx(g, seq, de, heading: 180));
        Assert.Empty(onD.Refusals);
        Assert.False(Assert.Single(onD.Applied).ChangedRoute);
    }

    // VirtualPilot, LTFM, "right onto N1": taxiway N forks at J. The right branch curves round to M in
    // 59 m; from M, N runs on east to a dead end, and a second arm doubles back west (130 degrees) to
    // rejoin the left branch. The destination is west. The only route that "turns right onto N" turns
    // right and then reverses at M: no aircraft taxis that, so the pick is refused, not honored.
    [Fact]
    public void A_pick_whose_route_doubles_back_on_the_taxiway_is_refused()
    {
        var es = P(0, -200);
        var a1 = P(-100, 0);
        var destW = P(-300, 0);
        var b1 = P(10, 25);
        var m = P(35, 45);
        var h1 = P(-45, 60);
        var g = Graph(new List<TaxiPath>
        {
            Path("E", es, J),
            Path("N", J, a1),
            Path("N", a1, destW),
            Path("N", J, b1),
            Path("N", b1, m),
            Path("N", m, P(255, 45)),
            Path("N", m, h1),
            Path("N", h1, a1),
        });
        var seq = new[] { "E", "N" };
        var r0 = Route(g, es, destW, seq);
        Assert.Equal(new[] { Node(g, es), Node(g, J), Node(g, a1), Node(g, destW) }, Nodes(r0));
        // The doubling-back way exists and is short enough to pass the length bound.
        var blockedJ = new TaxiRouter(g);
        blockedJ.BlockedNodes.Add(Node(g, J));
        Assert.NotNull(blockedJ.FindShortestPath(Node(g, b1), Node(g, destW)));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Right)), Ctx(g, seq, destW, heading: 0));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }

    // B comes south through T to J, on taxiway G; the destination is G's west end, a right turn at J.
    // East of J, G reaches Ge and stops there; a second arm of B leads from Ge back up to T, and a third
    // from T down to G west of J. So "left onto G" can only be followed by turning left, taking that arm
    // back to T, where the route has already been, and coming down the other one onto G westbound
    // (VirtualPilot, WMKK "left onto G": a 265 m loop through a node the route had passed). That is not
    // a way of going left. The pick is refused.
    [Fact]
    public void A_pick_is_not_honored_by_looping_back_through_the_route_already_taxied()
    {
        var bn = P(0, 300);
        var t = P(0, 100);
        var gw1 = P(-120, 0);
        var gw = P(-300, 0);
        var ge = P(60, 0);
        var g = Graph(new List<TaxiPath>
        {
            Path("B", bn, t),
            Path("B", t, J),
            Path("G", J, gw1),
            Path("G", gw1, gw),
            Path("G", J, ge),
            Path("B", ge, P(50, 60)),
            Path("B", P(50, 60), t),
            Path("B", t, P(-150, 100)),
            Path("B", P(-150, 100), gw1),
        });
        var seq = new[] { "B", "G" };
        var r0 = Route(g, bn, gw, seq);
        Assert.Equal(new[] { Node(g, bn), Node(g, t), Node(g, J), Node(g, gw1), Node(g, gw) }, Nodes(r0));
        // With only the junction closed, the loop through T is there to be found, and is short enough.
        var loop = new TaxiRouter(g);
        loop.BlockedNodes.Add(Node(g, J));
        var round = loop.FindShortestPath(Node(g, ge), Node(g, gw));
        Assert.NotNull(round);
        Assert.Contains(Node(g, t), Nodes(round!));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, gw, heading: 180));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }

    // B comes south to J on taxiway G; the destination is west. East of J, G reaches Ge 60 m on, and
    // taxiway H runs from Ge back west 25 m south of G, right past the junction, to G's west arm. No
    // node is used twice and no single turn is sharp, but the route turns left and comes straight back
    // past the junction it turned at, 25 m to one side (VirtualPilot, KCLE "left onto G7": guidance,
    // finding itself beside the start of its own route, went round again without end).
    [Fact]
    public void A_pick_is_not_honored_by_a_route_that_comes_back_past_its_own_junction()
    {
        var bn = P(0, 300);
        var gw1 = P(-120, 0);
        var gw = P(-300, 0);
        var ge = P(60, 0);
        var g = Graph(new List<TaxiPath>
        {
            Path("B", bn, J),
            Path("G", J, gw1),
            Path("G", gw1, gw),
            Path("G", J, ge),
            Path("H", ge, P(60, -25)),
            Path("H", P(60, -25), P(-120, -25)),
            Path("H", P(-120, -25), gw1),
        });
        var seq = new[] { "B", "G" };
        var r0 = Route(g, bn, gw, seq);
        Assert.Equal(new[] { Node(g, bn), Node(g, J), Node(g, gw1), Node(g, gw) }, Nodes(r0));
        var round = new TaxiRouter(g);
        round.BlockedNodes.Add(Node(g, J));
        Assert.NotNull(round.FindShortestPath(Node(g, ge), Node(g, gw)));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, gw, heading: 180));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }

    // B comes south to J on taxiway G; the stand lies south-west, beyond a runway that runs east-west
    // 100 m south of G. The charted way is west along G and down K, straight across the runway. East of
    // J, taxiway M leads down ONTO the runway, and the scenery has taxi lines drawn along it back west
    // to K. "Left onto G" would be followed by 450 m along the runway (VirtualPilot, OMDB "left onto
    // N": onto runway 12L/30R at N8 with no stop and 370 m along it; hold placement has no good answer
    // for a route that drives along a runway). A pick never adds that.
    [Fact]
    public void A_pick_is_not_honored_by_a_route_along_a_runway()
    {
        var bn = P(0, 300);
        var gw = P(-300, 0);
        var ge = P(150, 0);
        var kd = P(-300, -200);
        var paths = new List<TaxiPath>
        {
            Path("B", bn, J),
            Path("G", J, gw),
            Path("G", J, ge),
            Path("K", gw, P(-300, -100)),
            Path("K", P(-300, -100), kd),
            Path("M", ge, P(150, -100)),
            Path("", P(150, -100), P(0, -100)),
            Path("", P(0, -100), P(-150, -100)),
            Path("", P(-150, -100), P(-300, -100)),
        };
        var runway = new Runway
        {
            RunwayID = "09", StartLat = P(-600, -100).Lat, StartLon = P(-600, -100).Lon,
            EndLat = P(600, -100).Lat, EndLon = P(600, -100).Lon, Heading = 90, Width = 150, Length = 3937,
        };
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = runway.StartLat, Longitude = runway.StartLon, Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = runway.EndLat, Longitude = runway.EndLon, Heading = 270 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, new List<Runway> { runway });
        var seq = new[] { "B", "G" };
        var r0 = Route(g, bn, kd, seq);
        Assert.Equal(new[] { Node(g, bn), Node(g, J), Node(g, gw), Node(g, P(-300, -100)), Node(g, kd) }, Nodes(r0));
        // The way along the runway is there and short enough for the length bound.
        var round = new TaxiRouter(g);
        round.BlockedNodes.Add(Node(g, J));
        round.BlockedNodes.Add(Node(g, gw));
        var along = round.FindShortestPath(Node(g, ge), Node(g, kd));
        Assert.NotNull(along);
        Assert.Contains(Node(g, P(0, -100)), Nodes(along!));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, kd, heading: 180));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }

    [Fact]
    public void The_other_arm_is_the_taxiway_on_from_the_join_the_way_not_taken()
    {
        var g = Cross(withD: true);
        // Left at J (east, toward Em): the other arm is B's west side, and nothing east or on D.
        Assert.Equal(new[] { Node(g, Wm), Node(g, Wn) }.OrderBy(x => x),
            TurnPickApplier.OtherArm(g, Node(g, J), Node(g, Em), "B").OrderBy(x => x));
        // Joined in the middle of the east side (Em, going on to En): everything back west, J included.
        Assert.Equal(new[] { Node(g, J), Node(g, Wm), Node(g, Wn) }.OrderBy(x => x),
            TurnPickApplier.OtherArm(g, Node(g, Em), Node(g, En), "B").OrderBy(x => x));
        // At the end of a taxiway there is no other way.
        Assert.Empty(TurnPickApplier.OtherArm(g, Node(g, Wn), Node(g, Wm), "B"));
    }

    [Fact]
    public void A_taxiway_that_is_a_ring_has_no_other_arm()
    {
        // B is a square ring; from any corner both ways round reach the same nodes.
        var nw = P(-100, 100);
        var ne = P(100, 100);
        var se = P(100, -100);
        var sw = P(-100, -100);
        var g = Graph(new List<TaxiPath> { Path("B", nw, ne), Path("B", ne, se), Path("B", se, sw), Path("B", sw, nw) });
        Assert.Empty(TurnPickApplier.OtherArm(g, Node(g, nw), Node(g, ne), "B"));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4 }, false)]
    [InlineData(new[] { 1, 2, 3, 2, 5 }, true)]    // out along a taxiway and back over it
    [InlineData(new[] { 1, 2, 3, 4, 1 }, true)]    // round to where it began
    [InlineData(new int[0], false)]
    public void A_route_through_the_same_node_twice_is_a_loop(int[] nodeIds, bool expected)
        => Assert.Equal(expected, TurnPickApplier.VisitsANodeTwice(nodeIds));

    // B comes south to J on taxiway G and the clearance goes on west along G to K ("B, G, K"), a right
    // turn at J. East of J, G reaches Ge, and taxiway H runs from there round to G's west arm: no node
    // the route has been through, no sharp reversal, 80 m clear of the junction. "Left onto G" could be
    // followed that way, turning left and coming back onto G's OTHER arm, westbound after all. That is
    // not a way of going left (the owner's ruling of 2026-11-15, after VirtualPilot: KCLE "left onto
    // G7" came back onto G7's other arm across a runway, and a recalculation on the way circled).
    [Fact]
    public void A_pick_is_not_honored_by_coming_back_onto_the_taxiways_other_arm()
    {
        var bn = P(0, 300);
        var gw1 = P(-120, 0);
        var gw = P(-300, 0);
        var ge = P(60, 0);
        var kd = P(-300, -200);
        var g = Graph(new List<TaxiPath>
        {
            Path("B", bn, J),
            Path("G", J, gw1),
            Path("G", gw1, gw),
            Path("G", J, ge),
            Path("K", gw, kd),
            Path("H", ge, P(60, -80)),
            Path("H", P(60, -80), P(-120, -80)),
            Path("H", P(-120, -80), gw1),
        });
        var seq = new[] { "B", "G", "K" };
        var r0 = Route(g, bn, kd, seq);
        Assert.Equal(new[] { Node(g, bn), Node(g, J), Node(g, gw1), Node(g, gw), Node(g, kd) }, Nodes(r0));

        // The way round is there, and short enough for the length bound.
        var round = new TaxiRouter(g);
        round.BlockedNodes.Add(Node(g, J));
        var loop = round.FindConstrainedPath(Node(g, ge), Node(g, kd), new List<string> { "G", "K" });
        Assert.NotNull(loop);
        Assert.Null(loop!.ConstrainedFallbackReason);
        AssertInOrder(Nodes(loop), Node(g, ge), Node(g, gw1), Node(g, gw), Node(g, kd));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, kd, heading: 180));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }

    // VirtualPilot, ZJHK "right onto C". A lead-in comes south from the stand past N2 (where D leaves to
    // the east) to Top, the head of a small triangle: C curves left from Top to Ce and runs on east; C's
    // base runs 37 m west from Ce to Cw; an unnamed side leads from Cw back up to Top. To the right, C is
    // that 37 m and nothing more. The pick was "honored" by a leg straight from the stand to Ce, a right
    // turn onto the base, and then back UP the lead-in to N2, east along D and onto C eastbound after
    // all. What follows the turn may not go back over the charted way the pick declined.
    [Fact]
    public void A_pick_is_not_honored_by_going_back_up_the_charted_way_it_declined()
    {
        const double w = 200.0;   // wide enough that the straight leg from the stand stays on pavement
        var stand = P(0, 140);
        var n2 = P(0, 60);
        var top = P(0, 36);
        var cm = P(15, 10);
        var ce = P(37, 0);
        var cw = P(0, 0);
        var cx = P(300, 0);
        var cEnd = P(600, 0);
        var g = Graph(new List<TaxiPath>
        {
            Path("", stand, n2, w),
            Path("", n2, top, w),
            Path("C", top, cm, w),
            Path("C", cm, ce, w),
            Path("C", ce, cw, w),
            Path("", cw, top, w),
            Path("C", ce, cx, w),
            Path("C", cx, cEnd, w),
            Path("D", n2, P(300, 60), w),
            Path("D", P(300, 60), cx, w),
        });
        var seq = new[] { "C" };
        var r0 = Route(g, stand, cEnd, seq);
        Assert.Equal(new[] { Node(g, stand), Node(g, n2), Node(g, top), Node(g, cm), Node(g, ce), Node(g, cx), Node(g, cEnd) }, Nodes(r0));
        // The way back up the lead-in and round by D is there and short enough.
        var round = new TaxiRouter(g);
        round.BlockedNodes.Add(Node(g, ce));
        var back = round.FindShortestPath(Node(g, cw), Node(g, cEnd));
        Assert.NotNull(back);
        Assert.Contains(Node(g, n2), Nodes(back!));

        var res = TurnPickApplier.Apply(r0, Picks((0, TurnSide.Right)), Ctx(g, seq, cEnd, heading: 180, aircraft: stand));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }

    // As above, but the way on from Ge is taxiway H, which leaves it 150 degrees back the way the
    // aircraft came and runs round to G's west end without touching the route before J. Turning left
    // onto G and reversing 50 m later is no left turn either, whatever the next taxiway is called.
    [Fact]
    public void A_pick_is_not_honored_by_doubling_back_onto_another_taxiway_straight_after_the_turn()
    {
        var bn = P(0, 300);
        var gw1 = P(-120, 0);
        var gw = P(-300, 0);
        var ge = P(50, 0);
        var g = Graph(new List<TaxiPath>
        {
            Path("B", bn, J),
            Path("G", J, gw1),
            Path("G", gw1, gw),
            Path("G", J, ge),
            Path("H", ge, P(-10, -35)),
            Path("H", P(-10, -35), P(-150, -60)),
            Path("H", P(-150, -60), P(-320, -60)),
            Path("H", P(-320, -60), gw),
        });
        var seq = new[] { "B", "G" };
        var r0 = Route(g, bn, gw, seq);
        Assert.Equal(new[] { Node(g, bn), Node(g, J), Node(g, gw1), Node(g, gw) }, Nodes(r0));
        var roundH = new TaxiRouter(g);
        roundH.BlockedNodes.Add(Node(g, J));
        Assert.NotNull(roundH.FindShortestPath(Node(g, ge), Node(g, gw)));

        var res = TurnPickApplier.Apply(r0, Picks((1, TurnSide.Left)), Ctx(g, seq, gw, heading: 180));

        Assert.Empty(res.Applied);
        Assert.Equal(TurnPickOutcome.DoesNotLead, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
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

        // The turn exists; it misses the pinned holding point, and is said as that, not as no turn.
        Assert.Equal(TurnPickOutcome.MissesHoldingPoint, Assert.Single(res.Refusals).Reason);
        Assert.Same(r0, res.Route);
    }
}
