// Characterization tests for a clearance whose LAST taxiway is a link over ANOTHER runway.
// OMDB 2026-11-23 (a pilot's logs, then replayed on a second navdata build): the south apron to
// Runway 30R "via K, K7" and "via K4". K4 and K7 cross runway 12R/30L, and a crossing further east
// is the shorter way to 30R, so the router read each as a dead-end branch: it stopped the route at
// the 12R/30L hold (which guidance then spoke as "Hold short of Runway 30R" and lined up on), or
// never drove K4 at all ("K4 is not on the way from here").
//
// Equator fixture: metres = degrees x 111132. Two parallel runways (centrelines at lat 0.004 and
// 0.008), taxiway K south of both, M between them. K4 (west) and K9 (east) cross the south runway;
// K4's far side (M7) angles back west, which is what makes K9 the shorter way to the destination
// on the north runway's east end.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiRouterCrossingClearanceTests
{
    private static TaxiPath P(double la1, double lo1, double la2, double lo2, string name) => new()
    {
        StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75.0,
    };

    private static Runway R(string id, double la1, double lo1, double la2, double lo2) => new()
    {
        AirportICAO = "TEST", RunwayID = id, StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        Length = 10938, Width = 197, Heading = lo2 > lo1 ? 90 : 270,
    };

    private static int NodeAt(TaxiGraph g, double lat, double lon)
        => g.Nodes.Values.OrderBy(n => TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude)).First().NodeId;

    private const double DestLat = 0.008, DestLon = 0.029;

    /// <param name="k4Crossing">The name of the edge that carries K4 over the south runway.</param>
    private static List<TaxiPath> TwoRunwayField(string k4Crossing = "") => new()
    {
        // K, south of both runways.
        P(0.002, 0.000, 0.002, 0.005, "K"), P(0.002, 0.005, 0.002, 0.025, "K"), P(0.002, 0.025, 0.002, 0.030, "K"),
        // M, between the runways.
        P(0.006, 0.000, 0.006, 0.002, "M"), P(0.006, 0.002, 0.006, 0.010, "M"), P(0.006, 0.010, 0.006, 0.020, "M"),
        P(0.006, 0.020, 0.006, 0.025, "M"), P(0.006, 0.025, 0.006, 0.029, "M"), P(0.006, 0.029, 0.006, 0.030, "M"),
        // K4: K to the south runway's edge; an unnamed edge over the pavement (no node on it); M7 on to M.
        P(0.002, 0.005, 0.0037, 0.005, "K4"), P(0.0037, 0.005, 0.0043, 0.005, k4Crossing), P(0.0043, 0.005, 0.006, 0.002, "M7"),
        // K9: the same further east, straight over.
        P(0.002, 0.025, 0.0037, 0.025, "K9"), P(0.0037, 0.025, 0.0043, 0.025, ""), P(0.0043, 0.025, 0.006, 0.025, "M13"),
        // M15 onto the north runway: the destination.
        P(0.006, 0.029, DestLat, DestLon, "M15"),
    };

    private static List<Runway> Runways() => new()
    {
        R("09R", 0.004, 0.000, 0.004, 0.030), R("27L", 0.004, 0.030, 0.004, 0.000),
        R("09L", 0.008, 0.000, 0.008, 0.030), R("27R", 0.008, 0.030, 0.008, 0.000),
    };

    private static TaxiGraph Build(params TaxiPath[] extra)
    {
        var paths = TwoRunwayField();
        paths.AddRange(extra);
        return BuildField(paths, Runways());
    }

    private static TaxiGraph BuildField(List<TaxiPath> paths, List<Runway> runways)
    {
        // The graph pairs runway START rows into centrelines; the runway rows give them their pavement.
        var starts = runways.Select(r => new StartPosition
        {
            RunwayName = r.RunwayID, Latitude = r.StartLat, Longitude = r.StartLon, Heading = r.Heading,
        }).ToList();
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
        Assert.Equal(runways.Count / 2, g.RunwayCenterlines.Count);
        return g;
    }

    private static void AssertDrivesThenReaches(TaxiGraph g, TaxiRoute? route, string cleared, string farSide)
    {
        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var names = route.Segments.Select(s => s.TaxiwayName).ToList();
        string flown = string.Join(",", names.Select(n => n.Length == 0 ? "-" : n));
        Assert.True(names.Contains(cleared), $"{cleared} is driven: {flown}");
        Assert.True(names.Contains(farSide), $"{farSide} is driven: {flown}");
        Assert.True(names.IndexOf(cleared) < names.IndexOf(farSide), "the cleared link comes before its far side");
        Assert.Equal(NodeAt(g, DestLat, DestLon), route.Segments[^1].ToNode.NodeId);
        Assert.DoesNotContain("K9", names);
    }

    [Fact]
    public void The_fixture_makes_the_other_crossing_the_shorter_way()
    {
        // Without this the tests below would pass for the wrong reason: the router's rule only
        // fires when the cleared link's nearest node to the destination is the junction it is entered by.
        var g = Build();
        var dist = new TaxiRouter(g).ComputeGraphDistancesFrom(NodeAt(g, DestLat, DestLon));
        Assert.True(dist[NodeAt(g, 0.002, 0.005)] < dist[NodeAt(g, 0.0037, 0.005)]);
    }

    [Fact]
    public void A_last_taxiway_over_another_runway_is_driven_and_the_route_goes_on_to_the_destination()
    {
        var g = Build();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
    }

    [Fact]
    public void A_single_cleared_taxiway_over_another_runway_is_driven_from_a_start_away_from_it()
    {
        var g = Build();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
    }

    [Fact]
    public void A_single_cleared_taxiway_over_another_runway_is_driven_from_a_start_on_it()
    {
        var g = Build();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.005), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
    }

    [Fact]
    public void A_last_taxiway_that_dead_ends_short_of_the_destination_runway_still_ends_the_route()
    {
        // EIDW "N, N2" to 28R: the stub's only way on is back through its own junction.
        var g = Build(P(0.006, 0.020, 0.0075, 0.020, "N2"));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.006, 0.010), NodeAt(g, DestLat, DestLon),
            new List<string> { "M", "N2" }, destinationIsRunway: true);
        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.0075, 0.020), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_that_leads_on_without_meeting_another_runway_still_ends_the_route()
    {
        // PHNL 04L shape: the cleared taxiway does lead on, but only the long way round (in this
        // fixture 1.5 km west along Y, then over the south runway and back). It does not lead onto
        // another runway, so it stays a route that stops short and is warned about, as before.
        var g = Build(P(0.006, 0.010, 0.0050, 0.010, "H"),
                      P(0.0050, 0.010, 0.0050, 0.000, "Y"), P(0.0050, 0.000, 0.006, 0.000, "Y"));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.006, 0.020), NodeAt(g, DestLat, DestLon),
            new List<string> { "M", "H" }, destinationIsRunway: true);
        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.0050, 0.010), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_that_ends_back_at_the_runway_just_crossed_still_ends_the_route()
    {
        // EGKK "…J, G" to 08R (VirtualPilot A/B, 2026-11-24): the route comes OVER the other runway
        // to reach G, and G's far end is back at that runway's edge. The only way on from there is
        // over the runway again and round by ground the route has already used, and the route took
        // it: two more crossings. A way on that reuses any of the route is refused.
        var g = Build(P(0.0043, 0.025, 0.0048, 0.0255, ""), P(0.0048, 0.0255, 0.0043, 0.026, "G"),
                      P(0.0043, 0.026, 0.0037, 0.026, ""), P(0.0037, 0.026, 0.002, 0.025, ""));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K9", "G" }, destinationIsRunway: true);
        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.0043, 0.026), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_to_another_runways_hold_is_not_sent_over_that_runway_and_back()
    {
        // The destination is on a third runway SOUTH of K, reached along K with no crossing at all.
        // K4 is then a stub to the south runway's hold. The only way on from that hold is over the
        // runway, along M and back over it at K9: all new ground, and the runway is met within a
        // few metres, so nothing but the "same runway met twice" refusal keeps the terminus.
        var runways = Runways();
        runways.Add(R("09C", 0.000, 0.000, 0.000, 0.030));
        runways.Add(R("27C", 0.000, 0.030, 0.000, 0.000));
        var paths = TwoRunwayField();
        paths.Add(P(0.002, 0.030, 0.000, 0.029, "S"));
        var g = BuildField(paths, runways);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, 0.000, 0.029),
            new List<string> { "K", "K4" }, destinationIsRunway: true);
        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.0037, 0.005), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_that_keeps_its_name_over_the_other_runway_is_routed_on_too()
    {
        // The crossing edge carries the taxiway's own name, so its far end is already past the
        // runway and the way on meets no other runway: the crossing is inside the cleared leg.
        var g = BuildField(TwoRunwayField(k4Crossing: "K4"), Runways());
        var dist = new TaxiRouter(g).ComputeGraphDistancesFrom(NodeAt(g, DestLat, DestLon));
        Assert.True(dist[NodeAt(g, 0.002, 0.005)] < dist[NodeAt(g, 0.0043, 0.005)]);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
    }

    [Fact]
    public void A_single_cleared_taxiway_over_another_runway_is_not_driven_backwards_from_its_far_end()
    {
        // The aircraft is already at K4's hold. The route goes on over the runway from there; it
        // does not run back down K4 to K and cross at K9.
        var g = Build();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.0037, 0.005), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);
        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var names = route.Segments.Select(s => s.TaxiwayName).ToList();
        Assert.Contains("M7", names);
        Assert.DoesNotContain("K", names);
        Assert.DoesNotContain("K9", names);
        Assert.Equal(NodeAt(g, DestLat, DestLon), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_single_cleared_taxiway_is_driven_towards_the_runway_not_down_its_longer_apron_arm()
    {
        // K4 also runs south of K to the apron, and that arm is the longer one. From the junction
        // the taxiway is driven to the end nearest the destination, as a last taxiway of several is.
        var g = Build(P(0.002, 0.005, -0.001, 0.005, "K4"));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.005), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
        Assert.Equal(NodeAt(g, 0.0037, 0.005), route!.Segments[0].ToNode.NodeId);
    }

    // OMDB "K4" to 30R, 2026-11-24, live. The online names carry K4 on south of K to the apron
    // taxiway Z (15 nodes carry the name; navdata's own names give it 9, starting at K). The route
    // then JOINS K4 at its apron end and drives it up to K, K is still K4's node nearest the
    // destination, and the way on left it there: "via K4, then K, K9 ... crossing runway 12R at K9".
    private static TaxiPath[] ApronArmFromZ() => new[]
    {
        P(0.002, 0.005, -0.001, 0.005, "K4"),
        P(-0.001, 0.000, -0.001, 0.005, "Z"), P(-0.001, 0.005, -0.001, 0.010, "Z"),
    };

    [Fact]
    public void A_single_cleared_taxiway_joined_at_its_apron_end_is_driven_over_the_runway_too()
    {
        var g = Build(ApronArmFromZ());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, -0.001, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
        Assert.DoesNotContain("K", route!.Segments.Select(s => s.TaxiwayName));
    }

    [Fact]
    public void A_single_cleared_taxiway_is_driven_over_the_runway_from_a_start_on_its_apron_arm()
    {
        var g = Build(ApronArmFromZ());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, -0.001, 0.005), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);
        AssertDrivesThenReaches(g, route, "K4", "M7");
        Assert.DoesNotContain("K", route!.Segments.Select(s => s.TaxiwayName));
    }

    [Fact]
    public void A_single_cleared_taxiway_left_for_a_way_on_over_no_runway_of_its_own_keeps_its_route()
    {
        // T is joined at its apron end and left at K, and the way on runs east along K to a runway
        // at the far end of the field without crossing anything. T itself goes on north over 09/27,
        // and from its far end there IS a way round to the destination over new ground (N, P), so
        // the onward-leg test alone would accept it: the pilot would be taken over a runway the
        // route had no reason to cross. Only a runway the way on crosses as well names the crossing.
        var paths = new List<TaxiPath>
        {
            P(-0.001, 0.000, -0.001, 0.005, "Z"),
            P(-0.001, 0.005, 0.002, 0.005, "T"), P(0.002, 0.005, 0.0037, 0.005, "T"),
            P(0.0037, 0.005, 0.0043, 0.005, ""), P(0.0043, 0.005, 0.006, 0.005, "T"),
            P(0.002, 0.000, 0.002, 0.005, "K"), P(0.002, 0.005, 0.002, 0.038, "K"), P(0.002, 0.038, 0.002, 0.039, "K"),
            P(0.002, 0.039, 0.001, 0.040, "E1"),
            P(0.006, 0.005, 0.006, 0.038, "N"), P(0.006, 0.038, 0.002, 0.038, "P"),
        };
        var runways = new List<Runway>
        {
            R("09", 0.004, 0.000, 0.004, 0.030), R("27", 0.004, 0.030, 0.004, 0.000),
            new() { AirportICAO = "TEST", RunwayID = "36", StartLat = 0.000, StartLon = 0.040, EndLat = 0.010, EndLon = 0.040, Length = 3646, Width = 197, Heading = 360 },
            new() { AirportICAO = "TEST", RunwayID = "18", StartLat = 0.010, StartLon = 0.040, EndLat = 0.000, EndLon = 0.040, Length = 3646, Width = 197, Heading = 180 },
        };
        var g = BuildField(paths, runways);
        int dest = NodeAt(g, 0.001, 0.040);

        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, -0.001, 0.000), dest,
            new List<string> { "T" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var names = route.Segments.Select(s => s.TaxiwayName).ToList();
        Assert.Contains("T", names);
        Assert.Contains("K", names);
        Assert.DoesNotContain("N", names);
        Assert.DoesNotContain("P", names);
        Assert.Equal(dest, route.Segments[^1].ToNode.NodeId);
    }

    // --- Review of 2026-11-25 ---------------------------------------------------------------

    private static List<string> Names(TaxiRoute route)
    {
        var names = new List<string>();
        foreach (var s in route.Segments)
        {
            string n = s.TaxiwayName.Length == 0 ? "-" : s.TaxiwayName;
            if (names.Count == 0 || names[^1] != n) names.Add(n);
        }
        return names;
    }

    private static List<TaxiPath> FieldWithoutK4(params TaxiPath[] k) => new List<TaxiPath>(k)
    {
        P(0.006, 0.000, 0.006, 0.002, "M"), P(0.006, 0.002, 0.006, 0.010, "M"), P(0.006, 0.010, 0.006, 0.020, "M"),
        P(0.006, 0.020, 0.006, 0.025, "M"), P(0.006, 0.025, 0.006, 0.029, "M"), P(0.006, 0.029, 0.006, 0.030, "M"),
        P(0.002, 0.025, 0.0037, 0.025, "K9"), P(0.0037, 0.025, 0.0043, 0.025, ""), P(0.0043, 0.025, 0.006, 0.025, "M13"),
        P(0.006, 0.029, DestLat, DestLon, "M15"),
    };

    [Fact]
    public void A_single_cleared_taxiway_the_aircraft_is_on_is_not_driven_away_from_its_exit()
    {
        // The aircraft is on K with K's exit to the destination (K9) AHEAD of it. K's other end has a
        // link, L, over the same runway. The far end of a taxiway whose best exit is its node nearest
        // the destination is the node farthest from that exit, which here is BEHIND the aircraft, and
        // the way on from there is all new ground over another runway: the route turned the pilot
        // round, 4.7 km for 2.8. The router is not told which way the aircraft faces, so what refuses
        // it is the length the way on adds.
        var paths = FieldWithoutK4(
            P(0.002, 0.001, 0.002, 0.010, "K"), P(0.002, 0.010, 0.002, 0.025, "K"),
            P(0.002, 0.001, 0.0037, 0.001, "L"), P(0.0037, 0.001, 0.0043, 0.001, ""), P(0.0043, 0.001, 0.006, 0.002, "L"));
        var g = BuildField(paths, Runways());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.010), NodeAt(g, DestLat, DestLon),
            new List<string> { "K" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(new[] { "K", "K9", "-", "M13", "M", "M15" }, Names(route!));
        Assert.Equal(NodeAt(g, 0.002, 0.025), route!.Segments[0].ToNode.NodeId);   // east, towards the exit
    }

    [Theory]
    [InlineData(0.010)]
    [InlineData(0.000)]
    public void A_single_cleared_taxiway_is_not_driven_past_its_exit_to_a_much_longer_crossing(double startLon)
    {
        // K runs on 500 m past its best exit (K9) towards the destination, and a link there, X,
        // crosses the same runway and reaches M only by a 3.3 km loop: 6.2 km for 2.8. The same
        // length limit as a repeated taxiway's (25 % and 100 m over the plain route) refuses it.
        var paths = FieldWithoutK4(
            P(0.002, 0.000, 0.002, 0.010, "K"), P(0.002, 0.010, 0.002, 0.025, "K"), P(0.002, 0.025, 0.002, 0.0295, "K"),
            P(0.002, 0.0295, 0.0037, 0.0295, "X"), P(0.0037, 0.0295, 0.0043, 0.0295, ""),
            P(0.0043, 0.0295, 0.005, 0.045, "X"), P(0.005, 0.045, 0.006, 0.030, "X"));
        var g = BuildField(paths, Runways());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, startLon), NodeAt(g, DestLat, DestLon),
            new List<string> { "K" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(new[] { "K", "K9", "-", "M13", "M", "M15" }, Names(route!));
    }

    [Fact]
    public void A_last_taxiway_whose_way_on_goes_onto_another_runway_and_back_off_the_same_side_ends_the_route()
    {
        // K4 is a stub to the south runway's hold. Its only way on runs onto that runway at a node
        // on the centreline and leaves by K5 on the SAME side: one continuous stretch of pavement,
        // so "met twice" never sees it, and it is within the walk bound. The classifier calls it an
        // entry, not a crossing, and only a crossing of another runway is a way on.
        var runways = Runways();
        runways.Add(R("09C", 0.000, 0.000, 0.000, 0.030));
        runways.Add(R("27C", 0.000, 0.030, 0.000, 0.000));
        var paths = new List<TaxiPath>
        {
            P(0.002, 0.000, 0.002, 0.005, "K"), P(0.002, 0.005, 0.002, 0.007, "K"), P(0.002, 0.007, 0.002, 0.030, "K"),
            P(0.002, 0.005, 0.0037, 0.005, "K4"),
            P(0.0037, 0.005, 0.004, 0.006, ""), P(0.004, 0.006, 0.0037, 0.007, ""),
            P(0.0037, 0.007, 0.002, 0.007, "K5"),
            P(0.002, 0.030, 0.000, 0.029, "S"),
        };
        var g = BuildField(paths, runways);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, 0.000, 0.029),
            new List<string> { "K", "K4" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.0037, 0.005), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_whose_way_on_crosses_another_runway_once_but_far_on_still_ends_the_route()
    {
        // The walk bound, alone: T is a stub north of K. From its far end the only way on runs 440 m
        // west before it crosses the south runway, once, and goes on by new ground. One crossing, so
        // "met twice" does not refuse it, and the route it makes is within the length limit (4.6 km
        // for 3.9): only the 400 m bound keeps T the terminus.
        // K has no node at T's junction in the base field: split it there.
        var paths = TwoRunwayField();
        paths.RemoveAll(p => p.Name == "K" && p.StartLon == 0.005);
        paths.AddRange(new[]
        {
            P(0.002, 0.005, 0.002, 0.015, "K"), P(0.002, 0.015, 0.002, 0.025, "K"),
            P(0.002, 0.015, 0.003, 0.015, "T"),
            P(0.003, 0.015, 0.003, 0.011, ""), P(0.003, 0.011, 0.0037, 0.011, ""),
            P(0.0037, 0.011, 0.0043, 0.011, ""), P(0.0043, 0.011, 0.006, 0.020, ""),
        });
        var g = BuildField(paths, Runways());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "T" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.003, 0.015), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_way_on_that_first_meets_a_second_runway_the_destination_node_is_on_ends_the_route()
    {
        // The destination node is on TWO runways' pavement: the north runway and a wide 18/36 through
        // its threshold. N2 is a stub off M whose way on reaches the destination along 18/36. Both
        // runways are the destination's, so that is no crossing of ANOTHER runway and the stub stays
        // the terminus (the EIDW shape), whichever of the two the navdata lists first.
        var runways = Runways();
        runways.Add(new Runway { AirportICAO = "TEST", RunwayID = "18", StartLat = 0.012, StartLon = 0.029, EndLat = 0.0065, EndLon = 0.029, Length = 2005, Width = 600, Heading = 180 });
        runways.Add(new Runway { AirportICAO = "TEST", RunwayID = "36", StartLat = 0.0065, StartLon = 0.029, EndLat = 0.012, EndLon = 0.029, Length = 2005, Width = 600, Heading = 360 });
        var paths = TwoRunwayField();
        paths.AddRange(new[]
        {
            P(0.006, 0.025, 0.0075, 0.025, "N2"),
            // On from N2's far end: 350 m to 18/36's pavement, then along it to the destination.
            P(0.0075, 0.025, 0.0072, 0.0289, ""), P(0.0072, 0.0289, 0.0067, 0.0297, ""), P(0.0067, 0.0297, DestLat, DestLon, ""),
        });
        var g = BuildField(paths, runways);
        var router = new TaxiRouter(g);
        // The premise: N2's junction is nearer the destination than its far end, so N2 is a branch.
        var dist = router.ComputeGraphDistancesFrom(NodeAt(g, DestLat, DestLon));
        Assert.True(dist[NodeAt(g, 0.006, 0.025)] < dist[NodeAt(g, 0.0075, 0.025)]);

        var route = router.FindConstrainedPath(NodeAt(g, 0.006, 0.010), NodeAt(g, DestLat, DestLon),
            new List<string> { "M", "N2" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.0075, 0.025), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_destination_on_the_centreline_just_past_the_runways_recorded_end_is_still_that_runways()
    {
        // An entry stub may end up to 100 m past the end the navdata records ([STR-20]). That node
        // is on no pavement; it is the north runway's all the same, so "K, K4" is still driven
        // over the south runway and on, not ended at K4's hold for want of a destination runway.
        var paths = TwoRunwayField();
        paths.RemoveAll(p => p.Name == "M15");
        paths.Add(P(0.006, 0.029, 0.008, 0.0305, "M15"));                       // 56 m past the east end
        var g = BuildField(paths, Runways());
        int dest = NodeAt(g, 0.008, 0.0305);
        Assert.DoesNotContain(g.RunwayCenterlines, r => RunwayShape.For(r).Contains(0.008, 0.0305, 0.0));

        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), dest,
            new List<string> { "K", "K4" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Contains("M7", route.Segments.Select(s => s.TaxiwayName));
        Assert.Equal(dest, route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_single_cleared_taxiway_joined_beyond_its_junction_is_driven_on_over_the_runway()
    {
        // OMDB "K4" to 30R from the west apron on navdata's own names (replayed 2026-11-25): the route
        // comes along K from the west and takes K4's western fillet, which joins K4's own line
        // BEYOND the K junction. The step drove K4 back down to that junction, the leg from there to
        // the far end then reused ground already driven, and the taxiway was left for K and K9. The
        // route keeps the fillet and goes on up K4 from where it joined.
        var paths = FieldWithoutK4(
            P(0.002, 0.000, 0.002, 0.004, "K"), P(0.002, 0.004, 0.002, 0.005, "K"),
            P(0.002, 0.005, 0.002, 0.025, "K"), P(0.002, 0.025, 0.002, 0.030, "K"),
            P(0.002, 0.005, 0.003, 0.005, "K4"), P(0.003, 0.005, 0.0037, 0.005, "K4"),
            P(0.002, 0.004, 0.003, 0.005, "K4"),                                             // the fillet
            P(0.0037, 0.005, 0.0043, 0.005, ""), P(0.0043, 0.005, 0.006, 0.002, "M7"));
        var g = BuildField(paths, Runways());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);

        AssertDrivesThenReaches(g, route, "K4", "M7");
        var nodes = route!.Segments.Select(s => s.ToNode.NodeId).ToList();
        Assert.DoesNotContain(NodeAt(g, 0.002, 0.005), nodes);                // never down to the K junction
        Assert.Equal(nodes.Count, nodes.Distinct().Count());                  // and no node twice
    }

    [Fact]
    public void A_gate_destination_is_not_changed()
    {
        var g = Build();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: false);
        Assert.NotNull(route);
        Assert.DoesNotContain("M7", route!.Segments.Select(s => s.TaxiwayName));
    }

    // --- Review of 2026-11-26 ---------------------------------------------------------------

    [Fact]
    public void A_lone_taxiway_routed_from_its_own_junction_is_driven_to_its_far_end_not_to_a_fillet_beside_it()
    {
        // A recalculation trimmed to "R1" with the aircraft at the R/R1 junction (the LFPG shape).
        // R1 runs south, away from the destination, so its far end is the node farthest from the
        // junction. A fillet named R1 ends on R just east of the junction, nearer the destination
        // than the junction itself: taken as R1's far end, it ended the route 56 m along R1, which
        // was never driven. The destination is reached only round by the west (W, P, Q), so the
        // junction stays R1's node nearest it by taxi distance. No runways: the route ends on R1.
        var paths = new List<TaxiPath>
        {
            P(0.002, 0.000, 0.002, 0.010, "R"), P(0.002, 0.010, 0.002, 0.0105, "R"), P(0.002, 0.0105, 0.002, 0.020, "R"),
            P(0.002, 0.010, 0.0015, 0.010, "R1"), P(0.0015, 0.010, 0.0005, 0.010, "R1"),
            P(0.002, 0.0105, 0.0015, 0.010, "R1"),                                           // the fillet
            P(0.002, 0.000, 0.003, 0.000, "W"), P(0.003, 0.000, 0.003, 0.012, "P"), P(0.003, 0.012, 0.0025, 0.012, "Q"),
        };
        var g = BuildField(paths, new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.010), NodeAt(g, 0.0025, 0.012),
            new List<string> { "R1" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.0005, 0.010), route.Segments[^1].ToNode.NodeId);
    }

    // The destination of the three fillet tests below: north-east of the junction, reached only round
    // by the west, so the junction stays the cleared taxiway's node nearest it by taxi distance.
    private static TaxiPath[] RoundByTheWest() => new[]
    {
        P(0.002, 0.000, 0.003, 0.000, "W"), P(0.003, 0.000, 0.003, 0.012, "P"), P(0.003, 0.012, 0.0025, 0.012, "Y"),
    };

    [Fact]
    public void A_node_a_lone_taxiway_runs_on_through_is_no_fillet_and_stays_its_far_end()
    {
        // Link Q meets B twice, at the junction and 67 m east, and B runs on through that second
        // junction (the LFPG "B" at QB9 and KORD "A" at G2 shape). It is B's node nearest the
        // destination, a pick as before: taken for a fillet, the route was driven 500 m down B.
        var paths = new List<TaxiPath>(RoundByTheWest())
        {
            P(0.002, 0.000, 0.002, 0.010, "R"),
            P(0.002, 0.010, 0.002, 0.0106, "B"), P(0.002, 0.0106, 0.002, 0.0145, "B"),
            P(0.002, 0.010, 0.0025, 0.0103, "Q"), P(0.0025, 0.0103, 0.002, 0.0106, "Q"),
        };
        var g = BuildField(paths, new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.010), NodeAt(g, 0.0025, 0.012),
            new List<string> { "B" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.002, 0.0106), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_fillet_beside_the_junction_of_a_long_taxiway_stays_its_far_end()
    {
        // As the R1 test, but R1 runs 1.56 km south: skipping the fillet leaves only its farthest node,
        // the far side of the field (OMDB "M" from M4: 4.6 km), so the fillet pick stands.
        var paths = new List<TaxiPath>(RoundByTheWest())
        {
            P(0.002, 0.000, 0.002, 0.010, "R"), P(0.002, 0.010, 0.002, 0.0105, "R"), P(0.002, 0.0105, 0.002, 0.020, "R"),
            P(0.002, 0.010, 0.0015, 0.010, "R1"), P(0.0015, 0.010, -0.012, 0.010, "R1"),
            P(0.002, 0.0105, 0.0015, 0.010, "R1"),                                           // the fillet
        };
        var g = BuildField(paths, new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.010), NodeAt(g, 0.0025, 0.012),
            new List<string> { "R1" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.002, 0.0105), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_taxiways_end_on_the_junctions_taxiway_far_from_the_junction_is_its_real_far_end()
    {
        // D5 leaves D and rejoins it 333 m east: its far end stands on D too, where D5's name ends,
        // but no fillet is that far from the junction (WSSS "D5": 371 m, which the skip cut to 19 m).
        var paths = new List<TaxiPath>
        {
            P(0.002, 0.000, 0.002, 0.010, "D"), P(0.002, 0.010, 0.002, 0.013, "D"), P(0.002, 0.013, 0.002, 0.020, "D"),
            P(0.002, 0.010, 0.0035, 0.010, "D5"), P(0.0035, 0.010, 0.0035, 0.013, "D5"), P(0.0035, 0.013, 0.002, 0.013, "D5"),
            P(0.002, 0.000, 0.001, 0.000, "W"), P(0.001, 0.000, 0.001, 0.0145, "S"),
        };
        var g = BuildField(paths, new List<Runway>());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.010), NodeAt(g, 0.001, 0.0145),
            new List<string> { "D5" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.002, 0.013), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_that_starts_on_another_runway_and_leaves_its_far_side_goes_on()
    {
        // EBBR "B8, B9" and EDDF "L3" (fs2024 navdata, 2026-11-26): K runs onto runway 10/28 from the
        // south and L leaves the junction ON its pavement to the north. The classifier gives a path
        // that starts on a runway no passage, so the last leg alone shows no crossing, but the route
        // does cross 10/28 there, and L is driven and routed on to the destination.
        var paths = new List<TaxiPath>
        {
            P(0.0025, 0.006, 0.0025, 0.010, "K"), P(0.0025, 0.010, 0.0039, 0.010, "K"),
            P(0.0039, 0.010, 0.0055, 0.012, "L"),
            P(0.0039, 0.010, 0.0045, 0.010, "N1"), P(0.0045, 0.010, 0.006, 0.010, "N1"),
            P(0.006, 0.010, 0.006, 0.0295, "N"), P(0.006, 0.0295, 0.006, 0.030, "G1"),
            P(0.0055, 0.012, 0.009, 0.012, "U"), P(0.009, 0.012, 0.009, 0.0295, "V"), P(0.009, 0.0295, 0.006, 0.0295, "V2"),
        };
        var runways = new List<Runway>
        {
            R("10", 0.004, 0.000, 0.004, 0.020), R("28", 0.004, 0.020, 0.004, 0.000),
            new() { AirportICAO = "TEST", RunwayID = "36", StartLat = -0.004, StartLon = 0.030, EndLat = 0.008, EndLon = 0.030, Length = 4375, Width = 197, Heading = 360 },
            new() { AirportICAO = "TEST", RunwayID = "18", StartLat = 0.008, StartLon = 0.030, EndLat = -0.004, EndLon = 0.030, Length = 4375, Width = 197, Heading = 180 },
        };
        var g = BuildField(paths, runways);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.0025, 0.006), NodeAt(g, 0.006, 0.030),
            new List<string> { "K", "L" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var names = route.Segments.Select(s => s.TaxiwayName).ToList();
        Assert.True(names.IndexOf("L") < names.IndexOf("U"), string.Join(",", names));
        Assert.Equal(NodeAt(g, 0.006, 0.030), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_whose_way_on_only_clips_a_corner_of_another_runway_ends_the_route()
    {
        // T is a stub off K that branches off the destination. Its only way on runs north round the
        // EAST END of runway 09/27: one edge (C) cuts a corner of the pavement with both its ends clear
        // of it, and meets the centreline's line only past the runway's end. That is no crossing, and
        // the classifier gives it no passage; the pavement walk alone saw another runway 100 m on,
        // and the route was carried on round it.
        var paths = new List<TaxiPath>
        {
            P(0.002, -0.015, 0.002, 0.008, "K"), P(0.002, 0.008, 0.002, 0.015, "K"),
            P(0.002, 0.015, 0.009, 0.015, "N"),
            P(0.009, 0.005, 0.009, 0.015, "H"), P(0.009, 0.015, 0.009, 0.020, "H"),
            P(0.009, 0.020, 0.010, 0.020, "G1"),
            P(0.002, 0.008, 0.0036, 0.0108, "T"),
            P(0.0036, 0.0108, 0.0045, 0.0095, "C"),                                          // the corner
            P(0.0045, 0.0095, 0.0045, 0.005, "W"), P(0.0045, 0.005, 0.009, 0.005, "V"),
        };
        var runways = new List<Runway>
        {
            R("09", 0.004, 0.000, 0.004, 0.010), R("27", 0.004, 0.010, 0.004, 0.000),
            R("10", 0.010, 0.000, 0.010, 0.030), R("28", 0.010, 0.030, 0.010, 0.000),
        };
        var g = BuildField(paths, runways);
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, -0.015), NodeAt(g, 0.010, 0.020),
            new List<string> { "K", "T" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.0036, 0.0108), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_way_on_whose_route_goes_onto_a_runway_and_back_off_the_same_side_ends_the_route()
    {
        // The runway-entry count of OnwardLegKeepsTheRouteShort, alone. K runs north onto runway
        // 10/28 and L leaves from the junction ON its pavement, back to the south: over the whole
        // route that is an entry, but the last leg starts on the pavement, so the onward-leg test
        // sees no passage there. The way on from L's far end crosses 09/27 248 m on and reaches the
        // destination within the length limit (3.9 km for 3.5); the plain way from the junction goes
        // north off 10/28, a crossing. Only the entry count keeps L the terminus.
        var paths = new List<TaxiPath>
        {
            P(0.0025, 0.006, 0.0025, 0.010, "K"), P(0.0025, 0.010, 0.0039, 0.010, "K"),
            P(0.0039, 0.010, 0.0025, 0.012, "L"),
            P(0.0039, 0.010, 0.0045, 0.010, "N1"), P(0.0045, 0.010, 0.006, 0.010, "N1"),
            P(0.006, 0.010, 0.006, 0.0295, "N"),
            P(-0.0025, 0.0295, 0.002, 0.0295, "E"), P(0.002, 0.0295, 0.006, 0.0295, "E"),
            P(0.002, 0.0295, 0.002, 0.030, "G1"),
            P(0.0025, 0.012, -0.0025, 0.012, "S1"), P(-0.0025, 0.012, -0.0025, 0.0295, "S"),
        };
        var runways = new List<Runway>
        {
            R("10", 0.004, 0.000, 0.004, 0.020), R("28", 0.004, 0.020, 0.004, 0.000),
            R("09", 0.000, 0.000, 0.000, 0.020), R("27", 0.000, 0.020, 0.000, 0.000),
            new() { AirportICAO = "TEST", RunwayID = "36", StartLat = -0.004, StartLon = 0.030, EndLat = 0.008, EndLon = 0.030, Length = 4375, Width = 197, Heading = 360 },
            new() { AirportICAO = "TEST", RunwayID = "18", StartLat = 0.008, StartLon = 0.030, EndLat = -0.004, EndLon = 0.030, Length = 4375, Width = 197, Heading = 180 },
        };
        var g = BuildField(paths, runways);
        Assert.Contains(g.RunwayCenterlines, r => RunwayShape.For(r).Contains(0.0039, 0.010, 0.0));   // the junction is on 10/28
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.0025, 0.006), NodeAt(g, 0.002, 0.030),
            new List<string> { "K", "L" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.DoesNotContain("S1", route.Segments.Select(s => s.TaxiwayName));
        Assert.Equal(NodeAt(g, 0.0025, 0.012), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_single_cleared_taxiway_whose_leg_is_gap_chained_through_a_node_twice_is_not_driven_on()
    {
        // PassesANodeTwice, alone (the LEMD "LA" shape). K4's data has a 44 m gap: K to a dead end,
        // then on from further north to the hold. The only way to the second piece is back down to
        // K, west and up W, so gap chaining drives K4 as K, a, K, c, b, hold: the junction twice.
        // The way on from the hold over the south runway is otherwise accepted (4.6 km for 3.9).
        var paths = TwoRunwayField();
        paths.RemoveAll(p => p.Name == "K4" || (p.Name == "K" && p.StartLon == 0.000));
        paths.AddRange(new[]
        {
            P(0.002, 0.000, 0.002, 0.0045, "K"), P(0.002, 0.0045, 0.002, 0.005, "K"),
            P(0.002, 0.005, 0.0025, 0.005, "K4"), P(0.0029, 0.005, 0.0037, 0.005, "K4"),
            P(0.002, 0.0045, 0.0029, 0.005, "W"),
        });
        var g = BuildField(paths, Runways());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.DoesNotContain("M7", route.Segments.Select(s => s.TaxiwayName));
        var nodes = route.Segments.Select(s => s.ToNode.NodeId).ToList();
        Assert.Equal(nodes.Count, nodes.Distinct().Count());
        Assert.Equal(NodeAt(g, DestLat, DestLon), nodes[^1]);
    }

    [Fact]
    public void A_destination_on_two_runways_still_lets_a_last_taxiway_over_another_runway_go_on()
    {
        // Rule (3) alone: the destination node stands on the north runway AND on a wide 18/36 through
        // its threshold. Both are the destination's, so "K, K4" is driven over the south runway and
        // on, as without 18/36. Counting only one of them as the destination's makes the other a
        // runway the route ends on, an entry, and K4 stayed the terminus whichever was listed first.
        var runways = Runways();
        runways.Add(new Runway { AirportICAO = "TEST", RunwayID = "18", StartLat = 0.012, StartLon = 0.029, EndLat = 0.0065, EndLon = 0.029, Length = 2005, Width = 600, Heading = 180 });
        runways.Add(new Runway { AirportICAO = "TEST", RunwayID = "36", StartLat = 0.0065, StartLon = 0.029, EndLat = 0.012, EndLon = 0.029, Length = 2005, Width = 600, Heading = 360 });
        var g = BuildField(TwoRunwayField(), runways);
        Assert.Equal(2, g.RunwayCenterlines.Count(r => RunwayShape.For(r).Contains(DestLat, DestLon, 0.0)));

        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K4" }, destinationIsRunway: true);

        AssertDrivesThenReaches(g, route, "K4", "M7");
    }
}
