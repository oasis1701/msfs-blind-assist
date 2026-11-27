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
            RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y", "Z" }, route.Segments, g.TaxiwaysMeet));
    }

    [Fact]
    public void An_unnamed_link_between_two_cleared_taxiways_adds_nothing_to_the_summary()
    {
        var g = Field();
        var route = Route(g)!;
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y", "Z" }, route.Segments, g.TaxiwaysMeet));
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
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "X", "Y" }, route.Segments, g.TaxiwaysMeet));
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
        Assert.Equal("", RouteTaxiwaySequence.LinkClause(new List<string> { "K14", "M17", "M18" }, route.Segments, g.TaxiwaysMeet));
    }
}
