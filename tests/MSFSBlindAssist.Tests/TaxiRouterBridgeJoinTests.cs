// Characterization tests for a clearance whose consecutive taxiways share no node, so the router
// links them itself (TaxiRouter.FindRunwayBridge and the free leg after it).
//
// KEWR, 2026-11-26, "A11, A12, A, AA" to runway 04L: A11 and A12 meet only through an unnamed link.
// The bridge scored A12's nodes by the straight-line gap plus the cost on to the destination by ANY
// path, and picked the dead end of a curved A12 branch (node 693), which has an unnamed link of its
// own towards the runway. The free leg to it ran along A12, turned onto the cross-link F2 (never
// cleared) and out along the branch to its end, and the A12 leg then came straight back the same
// way: "Sharp right now, taxiway F2" and a 95 m out-and-back the guidance never mentioned.
//
// Equator fixture: metres = degrees x 111132, x east and y north.
//   X  s(0,0) - xa(50,0) - x1(100,0), then a link from x1 down to Y's west end y0(130,-40).
//   Y  y0 - y1(250,-40) - y2(350,-40) - J(400,-40) - y5(600,-40) - K(800,-40), and from J a branch
//      b1(370,-75) - b2(350,-90) - b3(340,-130) that ends at b3.
//   F2 y2 - b2, a 50 m cross-link between Y and its branch.
//   an unnamed link from b3 south to c1(340,-400) and east to the destination d(800,-400).
//   Z  K - z1(800,-200) - d.
// From b3 the unnamed link is the cheapest way to d (730 m), so a bridge scored by the straight gap
// plus that cost picks b3, 364 m from s.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiRouterBridgeJoinTests
{
    private const double M = 111132.0;

    private static TaxiPath P(double x1, double y1, double x2, double y2, string name) => new()
    {
        StartLat = y1 / M, StartLon = x1 / M, EndLat = y2 / M, EndLon = x2 / M,
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75.0,
    };

    private static int NodeAt(TaxiGraph g, double x, double y)
        => g.Nodes.Values.OrderBy(n => TaxiGraph.FastDistanceMeters(y / M, x / M, n.Latitude, n.Longitude)).First().NodeId;

    /// <param name="link">The name of the link from X's end to Y ("" for none, as at KEWR).</param>
    private static TaxiGraph Field(string link = "")
    {
        var paths = new List<TaxiPath>
        {
            P(0, 0, 50, 0, "X"), P(50, 0, 100, 0, "X"),
            P(100, 0, 130, -40, link),
            P(130, -40, 250, -40, "Y"), P(250, -40, 350, -40, "Y"), P(350, -40, 400, -40, "Y"),
            P(400, -40, 600, -40, "Y"), P(600, -40, 800, -40, "Y"),
            P(400, -40, 370, -75, "Y"), P(370, -75, 350, -90, "Y"), P(350, -90, 340, -130, "Y"),
            P(350, -40, 350, -90, "F2"),
            P(340, -130, 340, -400, ""), P(340, -400, 800, -400, ""),
            P(800, -40, 800, -200, "Z"), P(800, -200, 800, -400, "Z"),
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
    }

    private static List<int> NodeIds(TaxiRoute route)
    {
        var ids = new List<int> { route.Segments[0].FromNode.NodeId };
        ids.AddRange(route.Segments.Select(s => s.ToNode.NodeId));
        return ids;
    }

    private static string Flown(TaxiRoute route)
        => string.Join(",", route.Segments.Select(s => s.TaxiwayName.Length == 0 ? "-" : s.TaxiwayName));

    private static TaxiRoute? Route(TaxiGraph g)
        => new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 800, -400), new List<string> { "X", "Y", "Z" });

    [Fact]
    public void The_fixture_has_no_node_shared_by_the_two_cleared_taxiways()
    {
        // Without this the tests below would not reach the bridge at all.
        var g = Field();
        Assert.DoesNotContain(g.Nodes.Values, n => n.TaxiwayNames.Contains("X") && n.TaxiwayNames.Contains("Y"));
    }

    [Fact]
    public void A_bridge_between_two_cleared_taxiways_never_goes_out_and_back_along_the_next_one()
    {
        var g = Field();
        var route = Route(g);
        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var ids = NodeIds(route);
        for (int i = 2; i < ids.Count; i++)
            Assert.True(ids[i] != ids[i - 2], $"out and back at node {ids[i - 1]}: {Flown(route)}");
        Assert.Equal(ids.Count, ids.Distinct().Count());
        for (int i = 1; i < route.Segments.Count; i++)
        {
            double turn = Math.Abs(route.Segments[i].BearingDegrees - route.Segments[i - 1].BearingDegrees) % 360.0;
            if (turn > 180.0) turn = 360.0 - turn;
            Assert.True(turn < TaxiLeadIn.DoubleBackTurnDegrees, $"{turn:F0} degree turn at segment {i}: {Flown(route)}");
        }
    }

    [Fact]
    public void A_bridge_joins_the_next_taxiway_where_it_first_reaches_it_and_drives_it_on()
    {
        var g = Field();
        var route = Route(g)!;
        var ids = NodeIds(route);
        // Y is joined at its west end, driven east to the junction with Z, and the cross-link and
        // the branch are never used.
        Assert.Contains(NodeAt(g, 130, -40), ids);
        Assert.DoesNotContain("F2", route.Segments.Select(s => s.TaxiwayName));
        Assert.DoesNotContain(NodeAt(g, 340, -130), ids);
        Assert.Equal(NodeAt(g, 800, -400), ids[^1]);
        Assert.Equal("X,X,-,Y,Y,Y,Y,Y,Z,Z", Flown(route));
    }

    [Fact]
    public void A_bridge_never_leaves_from_beyond_the_turn_off_and_comes_back_along_the_cleared_taxiway()
    {
        // The exit end of the same score. X runs on past its link to Y, and a node of X beyond the link
        // is nearer Y's far end in a straight line than the link is long round: the bridge left from
        // there (x2 to y1, scored 924 m against 941 from the start to y0), and the route drove X out
        // past the link and straight back (KBIF "D, E, B, A": 440 m out along D and back).
        //   X  s(0,0) - j(100,0) - x2(400,0) - x3(600,-80); an unnamed link j - y0(100,-100).
        //   Y  y0 - y1(600,-100);  Z  y1 - z1(600,-300) - d(600,-400).
        var paths = new List<TaxiPath>
        {
            P(0, 0, 100, 0, "X"), P(100, 0, 400, 0, "X"), P(400, 0, 600, -80, "X"),
            P(100, 0, 100, -100, ""),
            P(100, -100, 600, -100, "Y"),
            P(600, -100, 600, -300, "Z"), P(600, -300, 600, -400, "Z"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 600, -400), new List<string> { "X", "Y", "Z" })!;
        Assert.Null(route.ConstrainedFallbackReason);
        var ids = NodeIds(route);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(NodeAt(g, 400, 0), ids);
        Assert.Equal("X,-,Y,Z,Z", Flown(route));
    }

    [Fact]
    public void A_bridge_leaving_from_a_loop_beyond_the_turn_off_does_not_drive_out_to_it()
    {
        // The same with a turning loop at the tip (OIII "B, D": 268 m east along B to a loop by runway
        // 11R and back west over the same nodes), so the way back is no straight retrace. Only the loop's
        // tip is within the bridge's 400 m of Y, so the bridge leaves from there.
        //   X  s(0,0) - j(100,0) - x2(400,0) - a(440,0) - u(450,-60); unnamed u - b(420,-60) - x2.
        //   an unnamed link j - y0(100,-600);  Y  y0 - y1(480,-400);  Z  y1 - z1(480,-500) - d(480,-600).
        var paths = new List<TaxiPath>
        {
            P(0, 0, 100, 0, "X"), P(100, 0, 400, 0, "X"), P(400, 0, 440, 0, "X"), P(440, 0, 450, -60, "X"),
            P(450, -60, 420, -60, ""), P(420, -60, 400, 0, ""),
            P(100, 0, 100, -600, ""),
            P(100, -600, 480, -400, "Y"),
            P(480, -400, 480, -500, "Z"), P(480, -500, 480, -600, "Z"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 480, -600), new List<string> { "X", "Y", "Z" })!;
        Assert.Null(route.ConstrainedFallbackReason);
        var ids = NodeIds(route);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain(NodeAt(g, 450, -60), ids);
        Assert.Equal("X,-,Y,Z,Z", Flown(route));
    }

    [Fact]
    public void A_bridge_never_unwinds_the_approach_to_the_first_cleared_taxiway()
    {
        // The approach from the aircraft to the first cleared taxiway is not that taxiway's leg. Where X is
        // reached only to go straight back, the route must still reach it: route loading sees the
        // turn-back there and leaves X out with a word ([RTE-39]). Unwound past X's entry, the route never
        // touched X, the lead-in was refused, and the route began on X 430 m away in a straight line
        // (ZJHK "S2, B, B10" from Gate 12).
        //   start s(0,0), an unnamed approach s - e(200,0);  X  e - xe(200,80), a stub.
        //   an unnamed link s - y0(0,-100);  Y  y0 - y1(300,-100);  Z  y1 - d(300,-300).
        var paths = new List<TaxiPath>
        {
            P(0, 0, 200, 0, ""), P(200, 0, 200, 80, "X"),
            P(0, 0, 0, -100, ""), P(0, -100, 300, -100, "Y"), P(300, -100, 300, -300, "Z"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 300, -300), new List<string> { "X", "Y", "Z" })!;
        Assert.Null(route.ConstrainedFallbackReason);
        Assert.Contains(NodeAt(g, 200, 0), NodeIds(route));
        Assert.True(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "X"), Flown(route));
    }

    [Fact]
    public void A_named_link_the_bridge_needs_is_said_in_the_route_summary()
    {
        // The two cleared taxiways meet only through a link the clearance never named: the pilot
        // hears it with the route, not first as a turn call.
        var g = Field(link: "L1");
        var route = Route(g)!;
        Assert.Equal("X,X,L1,Y,Y,Y,Y,Y,Z,Z", Flown(route));
        Assert.Equal(" From X, taxi via L1 to reach Y.",
            RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y", "Z" }, route.Segments));
    }

    [Fact]
    public void An_unnamed_link_between_two_cleared_taxiways_adds_nothing_to_the_summary()
    {
        var g = Field();
        var route = Route(g)!;
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y", "Z" }, route.Segments));
    }

    [Fact]
    public void Cleared_taxiways_that_meet_add_nothing_to_the_summary_whatever_the_route_crosses()
    {
        // A short piece of another taxiway at a junction on the way between two cleared taxiways that
        // DO meet is not a link the router had to find, and stays unsaid as before.
        var paths = new List<TaxiPath>
        {
            P(0, 0, 100, 0, "X"), P(100, 0, 110, 0, "W"), P(110, 0, 200, 0, "X"), P(200, 0, 300, 0, "Y"),
            P(100, 0, 100, 80, "W"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 300, 0), new List<string> { "X", "Y" })!;
        Assert.Equal("X,W,X,Y", Flown(route));
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y" }, route.Segments));
    }

    [Fact]
    public void A_named_link_taken_because_the_junction_is_closed_is_said_too()
    {
        // X and Y DO meet, at J, but J is closed to the router (as a Turn pick closes the charted way), so
        // it links them itself over W. Read from the graph alone, "they meet" kept the sentence back and
        // the pilot met W first as a turn call; read from the route, it never passed a node of both.
        //   X  s(0,0) - J(200,0);  Y  J - y1(400,0) - d(600,0);  W  x1(100,0) - w(100,-60) - y1, a loop round J.
        var paths = new List<TaxiPath>
        {
            P(0, 0, 100, 0, "X"), P(100, 0, 200, 0, "X"),
            P(200, 0, 400, 0, "Y"), P(400, 0, 600, 0, "Y"),
            P(100, 0, 100, -60, "W"), P(100, -60, 400, -60, "W"), P(400, -60, 400, 0, "W"),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
        var router = new TaxiRouter(g);
        router.BlockedNodes.Add(NodeAt(g, 200, 0));
        var route = router.FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 600, 0), new List<string> { "X", "Y" })!;
        Assert.Null(route.ConstrainedFallbackReason);
        Assert.DoesNotContain(NodeAt(g, 200, 0), NodeIds(route));
        Assert.Equal(" From X, taxi via W to reach Y.",
            RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y" }, route.Segments));
    }

    [Fact]
    public void A_taxiway_crossed_inside_one_cleared_taxiways_run_is_not_a_link_between_two()
    {
        // The old KEWR route's shape: A11, an unnamed link, A12, F2, A12 again. F2 lies inside A12's
        // leg, not between A11 and A12, so the sentence about links between cleared taxiways has
        // nothing to say about it (the route itself no longer takes it).
        static TaxiNode N(int id, params string[] names) => new() { NodeId = id, TaxiwayNames = new(names, StringComparer.OrdinalIgnoreCase) };
        var nodes = new[] { N(1, "A11"), N(2, "A11"), N(3, "A12"), N(4, "A12", "F2"), N(5, "A12", "F2"), N(6, "A12") };
        var names = new[] { "A11", "", "A12", "F2", "A12" };
        var segments = names.Select((n, i) => new TaxiRouteSegment
        {
            FromNode = nodes[i], ToNode = nodes[i + 1], TaxiwayName = n, DistanceMeters = 50,
        }).ToList();
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "A11", "A12" }, segments));
    }

    [Fact]
    public void A_bridge_over_a_runway_between_two_cleared_taxiways_is_unchanged()
    {
        // The case the bridge was built for (OMDB "K14, M17"): K14 ends at a runway's hold line on its
        // side, M17 starts at the hold line on the other, and an unnamed edge crosses the pavement.
        // The free leg reaches M17 only at its own end, so nothing is cut.
        var paths = new List<TaxiPath>
        {
            P(0, -300, 0, -150, "K14"), P(0, -150, 0, -60, "K14"),
            P(0, -60, 0, 60, ""),
            P(0, 60, 0, 150, "M17"), P(0, 150, 300, 150, "M17"), P(300, 150, 600, 150, "M17"),
            P(600, 150, 600, 300, "M18"),
        };
        var runways = new List<Runway>
        {
            new() { AirportICAO = "TEST", RunwayID = "09", StartLat = 0, StartLon = -1000 / M, EndLat = 0, EndLon = 1000 / M,
                    Length = 6562, Width = 148, Heading = 90 },
            new() { AirportICAO = "TEST", RunwayID = "27", StartLat = 0, StartLon = 1000 / M, EndLat = 0, EndLon = -1000 / M,
                    Length = 6562, Width = 148, Heading = 270 },
        };
        var starts = runways.Select(r => new StartPosition
        {
            RunwayName = r.RunwayID, Latitude = r.StartLat, Longitude = r.StartLon, Heading = r.Heading,
        }).ToList();
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, -300), NodeAt(g, 600, 300),
            new List<string> { "K14", "M17", "M18" })!;
        Assert.Null(route.ConstrainedFallbackReason);
        Assert.Equal("K14,K14,-,M17,M17,M17,M18", Flown(route));
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "K14", "M17", "M18" }, route.Segments));
    }

    // ── Where a bridge leg meets the route (AppendBridgeLeg) ─────────────────────────────────────

    [Fact]
    public void Taking_an_out_and_back_out_moves_where_the_leg_begins_to_the_node_it_turned_back_at()
    {
        // The route stands at 4, having come 2, 3, 4; the leg goes straight back to 3 and on to 9. The
        // edge 3-4 is dropped, so the leg now begins at 3, index 2: the junction a last taxiway is
        // entered by, and the floor for a bridge leaving it, are read there, never at the old index 3.
        var path = new List<int> { 1, 2, 3, 4 };
        int refined = 0;
        int begin = TaxiRouter.AppendBridgeLeg(path, new List<int> { 4, 3, 9 }, unwind: true, floor: 0, "test", ref refined);
        Assert.Equal(new[] { 1, 2, 3, 9 }, path);
        Assert.Equal(2, begin);
        Assert.Equal(3, path[begin]);
        Assert.Equal(1, refined);
    }

    [Fact]
    public void Taking_a_loop_out_moves_where_the_leg_begins_to_the_node_the_route_was_taken_back_to()
    {
        // The leg leaves 5 and comes back round to 3, already driven: the loop 4, 5, 6, 7 goes, and the
        // leg begins at 3.
        var path = new List<int> { 1, 2, 3, 4, 5 };
        int refined = 0;
        int begin = TaxiRouter.AppendBridgeLeg(path, new List<int> { 5, 6, 7, 3, 8 }, unwind: true, floor: 0, "test", ref refined);
        Assert.Equal(new[] { 1, 2, 3, 8 }, path);
        Assert.Equal(2, begin);
    }

    [Fact]
    public void A_leg_joined_with_nothing_taken_out_begins_at_the_seam()
    {
        var path = new List<int> { 1, 2, 3 };
        int refined = 0;
        int begin = TaxiRouter.AppendBridgeLeg(path, new List<int> { 3, 4, 5 }, unwind: true, floor: 0, "test", ref refined);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, path);
        Assert.Equal(2, begin);
        Assert.Equal(0, refined);
    }

    [Fact]
    public void Nothing_below_the_floor_is_taken_out()
    {
        // The out-and-back reaches below the floor (index 2, node 3): only the edge above it goes.
        var path = new List<int> { 1, 2, 3, 4 };
        int refined = 0;
        int begin = TaxiRouter.AppendBridgeLeg(path, new List<int> { 4, 3, 2, 1, 0 }, unwind: true, floor: 2, "test", ref refined);
        Assert.Equal(new[] { 1, 2, 3, 2, 1, 0 }, path);
        Assert.Equal(2, begin);
    }

    // ── The route as scored against the refined one (KeepAsScored) ───────────────────────────────

    private static TaxiRoute Made(string? fellBack, params (int from, int to, string name, double m)[] legs)
    {
        var nodes = new Dictionary<int, TaxiNode>();
        TaxiNode Node(int id) => nodes.TryGetValue(id, out var n) ? n : nodes[id] = new TaxiNode { NodeId = id };
        var route = new TaxiRoute { ConstrainedFallbackReason = fellBack };
        foreach (var (from, to, name, m) in legs)
            route.Segments.Add(new TaxiRouteSegment { FromNode = Node(from), ToNode = Node(to), TaxiwayName = name, DistanceMeters = m });
        route.TotalDistanceMeters = route.Segments.Sum(s => s.DistanceMeters);
        return route;
    }

    private static readonly List<string> XYZ = new() { "X", "Y", "Z" };

    [Fact]
    public void A_refined_route_that_fell_back_gives_way_to_one_as_scored_that_did_not()
    {
        // KNZY "B2, A3, A": the early join landed on a junction from which A has no leg.
        var refined = Made("No path on 'Z'", (1, 2, "X", 100), (2, 9, "", 100));
        var asScored = Made(null, (1, 2, "X", 100), (2, 3, "Y", 300), (3, 9, "Z", 100));
        Assert.True(TaxiRouter.KeepAsScored(refined, asScored, XYZ, endNodeId: 9));
        Assert.False(TaxiRouter.KeepAsScored(refined, Made("too", (1, 9, "", 100)), XYZ, endNodeId: 9));
    }

    [Fact]
    public void A_route_as_scored_that_fell_back_never_stands()
    {
        var refined = Made(null, (1, 2, "X", 100), (2, 3, "Y", 100), (3, 9, "Z", 100));
        Assert.False(TaxiRouter.KeepAsScored(refined, Made("No intersection", (1, 9, "", 50)), XYZ, endNodeId: 9));
    }

    [Fact]
    public void A_route_as_scored_with_an_out_and_back_the_refined_one_lacks_never_stands()
    {
        // KEWR: out along Y to 4 and straight back, even though it reads shorter on paper.
        var refined = Made(null, (1, 2, "X", 100), (2, 3, "", 50), (3, 5, "Y", 300), (5, 9, "Z", 100));
        var asScored = Made(null, (1, 2, "X", 100), (2, 3, "", 50), (3, 4, "Y", 10), (4, 3, "Y", 10), (3, 5, "Y", 200), (5, 9, "Z", 100));
        Assert.False(TaxiRouter.KeepAsScored(refined, asScored, XYZ, endNodeId: 9));
    }

    [Fact]
    public void A_loop_both_routes_share_is_no_reason_to_refuse_the_route_as_scored()
    {
        // A gap chained through its own nodes on X (7, 8, 7), a leg no bridge touches, is in both builds.
        // The refined route left Y undriven (KABQ "R, Q, P, M, B"); the route as scored drives it.
        var refined = Made(null, (1, 7, "X", 50), (7, 8, "X", 10), (8, 7, "X", 10), (7, 2, "X", 50), (2, 9, "Z", 400));
        var asScored = Made(null, (1, 7, "X", 50), (7, 8, "X", 10), (8, 7, "X", 10), (7, 2, "X", 50), (2, 3, "Y", 100), (3, 9, "Z", 300));
        Assert.True(TaxiRouter.KeepAsScored(refined, asScored, XYZ, endNodeId: 9));
    }

    [Fact]
    public void A_route_as_scored_that_drives_more_cleared_taxiways_stands_within_the_repeat_limit()
    {
        // EBBR "E4, T" to 01: the refined route never drove Y. Within 25 % + 100 m it stands, even when it
        // ends short of the destination ([RTE-17]); beyond it, it does not.
        var refined = Made(null, (1, 2, "X", 400), (2, 9, "Z", 400));
        var near = Made(null, (1, 2, "X", 400), (2, 3, "Y", 500), (3, 8, "Z", 100));
        var far = Made(null, (1, 2, "X", 400), (2, 3, "Y", 900), (3, 9, "Z", 400));
        Assert.True(TaxiRouter.KeepAsScored(refined, near, XYZ, endNodeId: 9));
        Assert.False(TaxiRouter.KeepAsScored(refined, far, XYZ, endNodeId: 9));
    }

    [Fact]
    public void A_route_as_scored_that_drives_fewer_cleared_taxiways_never_stands()
    {
        var refined = Made(null, (1, 2, "X", 100), (2, 3, "Y", 100), (3, 9, "Z", 100));
        var asScored = Made(null, (1, 2, "X", 100), (2, 9, "Z", 50));
        Assert.False(TaxiRouter.KeepAsScored(refined, asScored, XYZ, endNodeId: 9));
    }

    [Fact]
    public void Driving_the_same_taxiways_the_route_as_scored_must_reach_as_far_and_be_no_longer()
    {
        var refined = Made(null, (1, 2, "X", 100), (2, 3, "Y", 100), (3, 9, "Z", 100));
        // Shorter only because it stops short of the destination.
        Assert.False(TaxiRouter.KeepAsScored(refined, Made(null, (1, 2, "X", 100), (2, 3, "Y", 100), (3, 8, "Z", 50)), XYZ, endNodeId: 9));
        // More of a taxiway nobody cleared.
        Assert.False(TaxiRouter.KeepAsScored(refined,
            Made(null, (1, 2, "X", 100), (2, 4, "W", 40), (4, 3, "Y", 50), (3, 9, "Z", 100)), XYZ, endNodeId: 9));
        // Longer.
        Assert.False(TaxiRouter.KeepAsScored(refined, Made(null, (1, 2, "X", 100), (2, 3, "Y", 150), (3, 9, "Z", 100)), XYZ, endNodeId: 9));
        // The same length and no more of other taxiways: it stands.
        Assert.True(TaxiRouter.KeepAsScored(refined, Made(null, (1, 2, "X", 100), (2, 3, "Y", 100), (3, 9, "Z", 100)), XYZ, endNodeId: 9));
    }
}
