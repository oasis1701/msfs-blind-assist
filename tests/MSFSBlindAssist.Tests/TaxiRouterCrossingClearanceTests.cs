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

    // --- Second review of 2026-11-26 --------------------------------------------------------

    [Fact]
    public void A_single_cleared_taxiway_is_not_driven_over_a_runway_the_way_on_only_cuts_a_corner_of()
    {
        // The shared-runway gate, for the way on: T is joined at its apron end and left at K, and T
        // goes on north over 09/27. The way on runs east along K and bends up round 09/27's east
        // end, where one edge cuts a corner of the pavement with both its ends clear of it on the
        // south side: no crossing, and no node on the runway. Counted as a runway the way on
        // crosses, it sent the pilot over 09/27 on T and round by N and P (5.7 km for 5.0), where
        // the plain route crosses no runway at all.
        var paths = new List<TaxiPath>
        {
            P(-0.001, 0.000, -0.001, 0.005, "Z"),
            P(-0.001, 0.005, 0.002, 0.005, "T"), P(0.002, 0.005, 0.0037, 0.005, "T"),
            P(0.0037, 0.005, 0.0043, 0.005, ""), P(0.0043, 0.005, 0.006, 0.005, "T"),
            P(0.002, 0.000, 0.002, 0.005, "K"), P(0.002, 0.005, 0.002, 0.018, "K"),
            P(0.002, 0.018, 0.00365, 0.0190, "K"), P(0.00365, 0.0190, 0.0039, 0.0202, "K"),      // the corner
            P(0.0039, 0.0202, 0.002, 0.0215, "K"),
            P(0.002, 0.0215, 0.002, 0.038, "K"), P(0.002, 0.038, 0.002, 0.039, "K"),
            P(0.002, 0.039, 0.001, 0.040, "E1"),
            P(0.006, 0.005, 0.006, 0.038, "N"), P(0.006, 0.038, 0.002, 0.038, "P"),
        };
        var runways = new List<Runway>
        {
            R("09", 0.004, 0.000, 0.004, 0.020), R("27", 0.004, 0.020, 0.004, 0.000),
            new() { AirportICAO = "TEST", RunwayID = "36", StartLat = 0.000, StartLon = 0.040, EndLat = 0.010, EndLon = 0.040, Length = 3646, Width = 197, Heading = 360 },
            new() { AirportICAO = "TEST", RunwayID = "18", StartLat = 0.010, StartLon = 0.040, EndLat = 0.000, EndLon = 0.040, Length = 3646, Width = 197, Heading = 180 },
        };
        var g = BuildField(paths, runways);
        // The premise: neither end of the corner edge is on 09/27, and the edge between them is.
        var south = g.RunwayCenterlines.Single(r => r.Name1 is "09" or "27");
        Assert.False(RunwayShape.For(south).Contains(0.00365, 0.0190, 0.0));
        Assert.False(RunwayShape.For(south).Contains(0.0039, 0.0202, 0.0));
        Assert.True(RunwayShape.For(south).Contains(0.003775, 0.0196, 0.0));
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

    [Fact]
    public void A_fillet_is_not_skipped_for_a_node_more_than_a_kilometer_down_the_taxiway()
    {
        // The 1 km bound, when skipping the fillet leaves a node that is NOT the junction. R1's
        // nearest node to the destination is the fillet beside the junction; its next nearest is
        // R1's far end, 1.1 km away, which is still 27 m nearer the destination than the junction
        // is. The bound was read only when the skip left the junction itself, so the route drove
        // R1 to that far end. The fillet pick stands, as for the long R1 above.
        var paths = new List<TaxiPath>
        {
            P(0.002, 0.000, 0.002, 0.010, "R"), P(0.002, 0.010, 0.002, 0.0105, "R"), P(0.002, 0.0105, 0.002, 0.020, "R"),
            P(0.002, 0.010, 0.0015, 0.010, "R1"), P(0.0015, 0.010, -0.0075, 0.01295, "R1"),
            P(0.002, 0.0105, 0.0015, 0.010, "R1"),                                           // the fillet
            P(0.002, 0.000, 0.003, 0.000, "W"), P(0.003, 0.000, 0.003, 0.030, "P"), P(0.003, 0.030, 0.0025, 0.030, "Y"),
        };
        var g = BuildField(paths, new List<Runway>());
        int junction = NodeAt(g, 0.002, 0.010), farEnd = NodeAt(g, -0.0075, 0.01295), dest = NodeAt(g, 0.0025, 0.030);
        // The premise: the far end is nearer the destination than the junction, and over 1 km from it.
        double ToDest(int id) => TaxiGraph.CalculateDistanceMeters(g.Nodes[id].Latitude, g.Nodes[id].Longitude, 0.0025, 0.030);
        Assert.True(ToDest(farEnd) < ToDest(junction));
        Assert.True(ToDest(NodeAt(g, 0.002, 0.0105)) < ToDest(farEnd));
        Assert.True(TaxiGraph.CalculateDistanceMeters(0.002, 0.010, -0.0075, 0.01295) > 1000.0);

        var route = new TaxiRouter(g).FindConstrainedPath(junction, dest, new List<string> { "R1" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Equal(NodeAt(g, 0.002, 0.0105), route!.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_lone_taxiway_routed_from_a_node_on_the_destination_runway_is_not_driven_away_from_it()
    {
        // [RTE-33] for the lone taxiway: A meets the runway at a node on its pavement, and the route
        // is loaded from that node with only "A" cleared. The node is A's nearest to the destination,
        // so the branch-off rule read A as a stub and drove it 222 m away from the runway the
        // aircraft stands on, ending there. The route has reached its runway: it goes on to the
        // destination.
        var paths = new List<TaxiPath>
        {
            P(0.006, 0.010, 0.008, 0.010, "A"), P(0.008, 0.010, 0.010, 0.010, "A"),
            P(0.008, 0.010, DestLat, DestLon, ""),                                           // along the runway
        };
        var runways = new List<Runway> { R("09L", 0.008, 0.000, 0.008, 0.030), R("27R", 0.008, 0.030, 0.008, 0.000) };
        var g = BuildField(paths, runways);
        Assert.Contains(g.RunwayCenterlines, r => RunwayShape.For(r).Contains(0.008, 0.010, 0.0));
        int dest = NodeAt(g, DestLat, DestLon);

        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.008, 0.010), dest,
            new List<string> { "A" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.DoesNotContain("A", route.Segments.Select(s => s.TaxiwayName));
        Assert.Equal(dest, route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void The_shared_runway_gate_does_not_count_the_runway_of_a_destination_past_its_recorded_end()
    {
        // Rule (13), on the gate itself: no route through FindConstrainedPath reaches it with a
        // destination past its runway's end and that runway met inside the walk bound (a far end
        // that near the destination runway is the taxiway's nearest node to it, and no stub).
        // The destination is on the north runway's centerline 56 m past its recorded end, on no
        // pavement. The taxiway going on and the way on both stand on that runway's pavement, and
        // on no other. That is the destination's own runway, not one the taxiway leads over.
        var runways = Runways();
        var paths = new List<TaxiPath>
        {
            P(0.006, 0.027, 0.007, 0.028, "T"),
            P(0.007, 0.028, 0.008, 0.0295, ""), P(0.008, 0.0295, 0.008, 0.0305, ""),
            P(0.006, 0.027, 0.008, 0.029, ""), P(0.008, 0.029, 0.008, 0.0295, ""),
            P(0.004, 0.010, 0.004, 0.011, ""),                                               // on the south runway
        };
        var g = BuildField(paths, runways);
        int a = NodeAt(g, 0.006, 0.027), b = NodeAt(g, 0.007, 0.028), c = NodeAt(g, 0.008, 0.0295),
            e = NodeAt(g, 0.008, 0.029), dest = NodeAt(g, 0.008, 0.0305);
        Assert.DoesNotContain(g.RunwayCenterlines, r => RunwayShape.For(r).Contains(0.008, 0.0305, 0.0));
        var router = new TaxiRouter(g);
        var along = new List<int> { a, b };
        var onward = new List<int> { b, c, dest };
        var wayOn = new List<int> { a, e, c, dest };

        Assert.False(router.LeadsOverARunwayTheWayOnCrosses(along, onward, wayOn, dest));
        // The same paths to a destination on the OTHER runway: the north runway is then one the
        // taxiway leads over and the way on stands on too.
        Assert.True(router.LeadsOverARunwayTheWayOnCrosses(along, onward, wayOn, NodeAt(g, 0.004, 0.010)));
    }

    [Fact]
    public void A_second_centerline_of_the_destination_runway_is_not_another_runway()
    {
        // KBOS shape: the navdata carries two sets of start rows for the runway, so the graph has two
        // centerlines named 09L/27R. With a runway-table row for that name the graph snaps both
        // sets onto one line and the two copies are one shape; here the table has no such row, the
        // second set lies 60 m south of the first, and the destination node stands on the first
        // copy only. N2 is a stub off M whose way on reaches the destination over the second
        // copy's pavement. That copy is the destination's own runway under another row, not a
        // runway N2 leads over: matched by reference, it was, and the stub was routed on (the EIDW
        // shape, lost).
        var paths = new List<TaxiPath>
        {
            P(0.006, 0.010, 0.006, 0.020, "M"), P(0.006, 0.020, 0.006, 0.029, "M"),
            P(0.006, 0.029, DestLat, DestLon, "M15"),
            P(0.006, 0.020, 0.0070, 0.020, "N2"),
            P(0.0070, 0.020, 0.0070, 0.0175, ""), P(0.0070, 0.0175, 0.0074, 0.0185, ""), P(0.0074, 0.0185, DestLat, DestLon, ""),
        };
        var starts = new List<StartPosition>
        {
            new() { RunwayName = "09L", Latitude = 0.008, Longitude = 0.000, Heading = 90 },
            new() { RunwayName = "27R", Latitude = 0.008, Longitude = 0.030, Heading = 270 },
            new() { RunwayName = "09L", Latitude = 0.00746, Longitude = 0.000, Heading = 90 },
            new() { RunwayName = "27R", Latitude = 0.00746, Longitude = 0.030, Heading = 270 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, new List<Runway>());
        Assert.Equal(2, g.RunwayCenterlines.Count(r => r.Name1 is "09L" or "27R"));
        Assert.Equal(1, g.RunwayCenterlines.Count(r => RunwayShape.For(r).Contains(DestLat, DestLon, 0.0)));
        Assert.Equal(1, g.RunwayCenterlines.Count(r => RunwayShape.For(r).Contains(0.0074, 0.0185, 0.0)));
        var router = new TaxiRouter(g);
        var dist = router.ComputeGraphDistancesFrom(NodeAt(g, DestLat, DestLon));
        Assert.True(dist[NodeAt(g, 0.006, 0.020)] < dist[NodeAt(g, 0.0070, 0.020)]);

        var route = router.FindConstrainedPath(NodeAt(g, 0.006, 0.010), NodeAt(g, DestLat, DestLon),
            new List<string> { "M", "N2" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.Equal(NodeAt(g, 0.0070, 0.020), route.Segments[^1].ToNode.NodeId);
    }

    [Fact]
    public void A_last_taxiway_whose_way_on_makes_the_route_much_longer_still_ends_the_route()
    {
        // The length limit at the last-taxiway call site, alone. X is a stub off K to the south
        // runway's hold, past K's best exit (K9). Over the runway from there the only way on is
        // Y, a 3.4 km loop round to M's east end: one crossing, all new ground, so the onward-leg
        // test accepts it, and it makes 7.3 km of 4.9. A last cleared taxiway is never dropped
        // ([RTE-17]), so X stays the end of the route; it is not carried round the loop.
        var paths = FieldWithoutK4(
            P(0.002, 0.000, 0.002, 0.010, "K"), P(0.002, 0.010, 0.002, 0.025, "K"), P(0.002, 0.025, 0.002, 0.0295, "K"),
            P(0.002, 0.0295, 0.0037, 0.0295, "X"), P(0.0037, 0.0295, 0.0043, 0.0295, ""),
            P(0.0043, 0.0295, 0.005, 0.045, "Y"), P(0.005, 0.045, 0.006, 0.030, "Y"));
        var g = BuildField(paths, Runways());
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "X" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        Assert.DoesNotContain("Y", route.Segments.Select(s => s.TaxiwayName));
        Assert.Equal(NodeAt(g, 0.0037, 0.0295), route.Segments[^1].ToNode.NodeId);
    }

    // --- A way on must CROSS the runway, not wander on it (2026-11-26) ---------------------------
    //
    // OMDB "K, K7" to 30R on navdata's own names. K7's name runs on round the curve it turns onto
    // runway 12R/30L by, so its far end is the tip of that curve, on the pavement. From there the
    // only way on follows the curve onto the centerline, turns back and leaves by the curve of
    // the taxiway opposite: 430 m and a reversal on the runway, where straight over from the node
    // the curves part at is 117 m. K7 below is that shape at lon 0.015: the split node S, the
    // curve's tip T, the centerline nodes C1 and C2, the far taxiway's curve D and its split F.

    private const double SLat = 0.0035, TLat = 0.0038, VeeTLat = 0.0039, TLon = 0.0153, C2Lon = 0.0166, FLat = 0.0045;

    /// <summary>K and M, K7 from K up to its split node S, and (unless told not to) K9 further
    /// east, the shorter way to the destination. The crossing itself is added by the test.</summary>
    private static List<TaxiPath> FieldWithK7(bool k9 = true)
    {
        var paths = new List<TaxiPath>
        {
            P(0.002, 0.000, 0.002, 0.015, "K"), P(0.002, 0.015, 0.002, 0.025, "K"), P(0.002, 0.025, 0.002, 0.030, "K"),
            P(0.006, 0.000, 0.006, 0.012, "M"), P(0.006, 0.012, 0.006, 0.015, "M"), P(0.006, 0.015, 0.006, 0.016, "M"),
            P(0.006, 0.016, 0.006, 0.025, "M"), P(0.006, 0.025, 0.006, 0.029, "M"), P(0.006, 0.029, 0.006, 0.030, "M"),
            P(0.006, 0.029, DestLat, DestLon, "M15"),
            P(0.002, 0.015, SLat, 0.015, "K7"),
        };
        if (k9)
            paths.AddRange(new[]
            {
                P(0.002, 0.025, 0.0037, 0.025, "K9"), P(0.0037, 0.025, 0.0043, 0.025, ""), P(0.0043, 0.025, 0.006, 0.025, "M13"),
            });
        return paths;
    }

    /// <summary>K7 on round its curve to the tip T on the pavement, the curve on to the centerline,
    /// 89 m along it, a turn of 169 degrees, and off by the far taxiway's curve to its split F:
    /// 262 m on the pavement. M9 runs from F to M, west of F so that K9 stays the shorter way.</summary>
    private static TaxiPath[] Curves(double farJoinsMAt = 0.012) => new[]
    {
        P(SLat, 0.015, TLat, TLon, "K7"),
        P(TLat, TLon, 0.004, 0.0158, ""), P(0.004, 0.0158, 0.004, C2Lon, ""),
        P(0.004, C2Lon, 0.0042, 0.0158, ""), P(0.0042, 0.0158, FLat, 0.015, ""),
        P(FLat, 0.015, 0.006, farJoinsMAt, "M9"),
    };

    /// <summary>The two curves as a tight V meeting on the centerline: 70 m on the pavement, and a
    /// turn of 144 degrees. The tip of K7's curve is (VeeTLat, TLon).</summary>
    private static TaxiPath[] VeeCurves() => new[]
    {
        P(SLat, 0.015, VeeTLat, TLon, "K7"), P(VeeTLat, TLon, 0.004, 0.0156, ""),
        P(0.004, 0.0156, 0.0041, TLon, ""), P(0.0041, TLon, FLat, 0.015, ""),
        P(FLat, 0.015, 0.006, 0.012, "M9"),
    };

    /// <summary>K7's curve onto the centerline, 244 m along it, and off AHEAD by a curve to a far
    /// taxiway further east (no turn back): 364 m on the pavement. M9 also runs from F, opposite
    /// K7, to M.</summary>
    private static TaxiPath[] CurvesToAnExitFurtherOn() => new[]
    {
        P(SLat, 0.015, TLat, TLon, "K7"),
        P(TLat, TLon, 0.004, 0.0158, ""), P(0.004, 0.0158, 0.004, 0.018, ""),
        P(0.004, 0.018, 0.0042, 0.0185, ""), P(0.0042, 0.0185, FLat, 0.019, ""),
        P(FLat, 0.019, 0.006, 0.016, "M9"), P(FLat, 0.015, 0.006, 0.012, "M9"),
    };

    /// <summary>The link straight over the runway, from K7's split S to the far split F.</summary>
    private static TaxiPath Link(string name) => P(SLat, 0.015, FLat, 0.015, name);

    private static TaxiGraph K7Graph(IEnumerable<TaxiPath> crossing, bool k9 = true)
    {
        var paths = FieldWithK7(k9);
        paths.AddRange(crossing);
        return BuildField(paths, Runways());
    }

    private static void AssertGoesStraightOverFromTheSplit(TaxiGraph g, TaxiRoute? route, double tipLat)
    {
        AssertDrivesThenReaches(g, route, "K7", "M9");
        var ids = NodeIds(route!);
        int split = ids.IndexOf(NodeAt(g, SLat, 0.015));
        Assert.True(split >= 0, "K7 is driven to the node its curves part at");
        Assert.Equal(NodeAt(g, FLat, 0.015), ids[split + 1]);
        Assert.DoesNotContain(NodeAt(g, tipLat, TLon), ids);
    }

    private static List<int> NodeIds(TaxiRoute route)
        => new[] { route.Segments[0].FromNode.NodeId }.Concat(route.Segments.Select(s => s.ToNode.NodeId)).ToList();

    [Fact]
    public void The_curve_fixture_makes_K7_a_branch_whose_far_end_is_the_tip_of_its_curve()
    {
        // The premise of the tests below: the K junction is K7's node nearest the destination by
        // taxi distance (the terminal rule fires), and the curve's tip its nearest by position.
        var g = K7Graph(Curves().Append(Link("")));
        var dist = new TaxiRouter(g).ComputeGraphDistancesFrom(NodeAt(g, DestLat, DestLon));
        Assert.True(dist[NodeAt(g, 0.002, 0.015)] < dist[NodeAt(g, SLat, 0.015)]);
        Assert.True(dist[NodeAt(g, 0.002, 0.015)] < dist[NodeAt(g, TLat, TLon)]);
        var south = g.RunwayCenterlines.Single(r => r.Name1 is "09R" or "27L");
        Assert.True(RunwayShape.For(south).Contains(TLat, TLon, 0.0));
        Assert.False(RunwayShape.For(south).Contains(SLat, 0.015, 0.0));
        Assert.False(RunwayShape.For(south).Contains(FLat, 0.015, 0.0));
    }

    [Theory]
    [InlineData("")]     // OMDB K7: the link over the runway has no name
    [InlineData("M9")]   // EDDF M14: it carries the name of the taxiway on the far side
    public void A_last_taxiway_whose_name_runs_round_its_curve_is_left_where_the_straight_link_starts(string link)
    {
        var g = K7Graph(Curves().Append(Link(link)));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K7" }, destinationIsRunway: true);

        AssertGoesStraightOverFromTheSplit(g, route, TLat);
        Assert.DoesNotContain(NodeAt(g, 0.004, C2Lon), NodeIds(route!));
    }

    [Fact]
    public void A_turn_back_on_the_runway_is_enough_to_look_for_the_straight_link()
    {
        // The two curves meet on the centerline: onto it, a turn of 144 degrees, and off again,
        // 70 m on the pavement in all, less than many a straight crossing puts there. So the
        // length on the pavement says nothing here, and the turn alone has to.
        var g = K7Graph(VeeCurves().Append(Link("")));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K7" }, destinationIsRunway: true);

        AssertGoesStraightOverFromTheSplit(g, route, VeeTLat);
    }

    [Fact]
    public void Driving_along_the_runway_is_enough_to_look_for_the_straight_link()
    {
        // No turn back: the way on from the tip of K7's curve runs 244 m along the centerline and
        // leaves ahead, by a taxiway further east (KATL "L10" to 28 and KDTW "U8" to 22R do this
        // on two exits). 364 m of it lie on the pavement, where a crossing puts 45 to 60
        // ([TURN-7]). The link is taken although the route it makes is 79 m LONGER: the way along
        // the runway is the shortcut.
        var g = K7Graph(CurvesToAnExitFurtherOn().Append(Link("")));
        var router = new TaxiRouter(g);
        var dist = router.ComputeGraphDistancesFrom(NodeAt(g, DestLat, DestLon));
        Assert.True(dist[NodeAt(g, 0.002, 0.015)] < dist[NodeAt(g, SLat, 0.015)], "K7 is still a branch off the destination");
        var route = router.FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K7" }, destinationIsRunway: true);

        AssertGoesStraightOverFromTheSplit(g, route, TLat);
        Assert.DoesNotContain(NodeAt(g, 0.004, 0.018), NodeIds(route!));
    }

    [Fact]
    public void A_lone_taxiway_the_aircraft_stands_at_the_end_of_goes_straight_over()
    {
        // OMDB "K14" to 30R from K14's own last straight node: the crossing is straight ahead, and
        // the node the leg starts at is itself the place to leave it. Routed "along it" first, the
        // route took the curve 29 m to its tip and 204 m along the runway from there. No K9 here,
        // so the split node is K7's nearest to the destination and the route starts on it.
        var g = K7Graph(Curves(farJoinsMAt: 0.015).Append(Link("")), k9: false);
        int split = NodeAt(g, SLat, 0.015);
        var route = new TaxiRouter(g).FindConstrainedPath(split, NodeAt(g, DestLat, DestLon),
            new List<string> { "K7" }, destinationIsRunway: true);

        Assert.NotNull(route);
        Assert.Null(route!.ConstrainedFallbackReason);
        var ids = NodeIds(route);
        Assert.Equal(split, ids[0]);
        Assert.Equal(NodeAt(g, FLat, 0.015), ids[1]);
        Assert.DoesNotContain(NodeAt(g, TLat, TLon), ids);
        Assert.Equal(NodeAt(g, DestLat, DestLon), ids[^1]);
    }

    [Fact]
    public void A_taxiway_is_not_left_short_of_its_far_end_for_another_taxiway_that_crosses_elsewhere()
    {
        // No straight link, and a taxiway X off the split node that crosses the same runway 220 m
        // west, 224 m shorter than the way round K7's curve. Leaving K7 for it is a crossing by
        // every other test, and it is not K7's: the pilot was cleared over the runway on K7. With
        // nothing better on K7 itself the way on from its far end stands, as before.
        var g = K7Graph(Curves().Concat(new[]
        {
            P(SLat, 0.015, SLat, 0.013, "X"), P(SLat, 0.013, FLat, 0.013, ""), P(FLat, 0.013, 0.006, 0.012, "X"),
        }));
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K", "K7" }, destinationIsRunway: true);

        AssertDrivesThenReaches(g, route, "K7", "M9");
        Assert.DoesNotContain("X", route!.Segments.Select(s => s.TaxiwayName));
        Assert.Contains(NodeAt(g, TLat, TLon), NodeIds(route));
    }
}
