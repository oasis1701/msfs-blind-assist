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

    [Fact]
    public void A_gate_destination_is_not_changed()
    {
        var g = Build();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0.002, 0.000), NodeAt(g, DestLat, DestLon),
            new List<string> { "K4" }, destinationIsRunway: false);
        Assert.NotNull(route);
        Assert.DoesNotContain("M7", route!.Segments.Select(s => s.TaxiwayName));
    }
}
