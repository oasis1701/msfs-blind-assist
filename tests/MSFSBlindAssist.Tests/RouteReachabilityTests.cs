// Characterization tests for RouteReachability — the decision LoadRoute and the recalculation
// make before a route is adopted, so a pilot is never steered in a straight line to a destination
// on a piece of taxi network they are not on.
//
// Rules pinned:
//   - Aircraft on runway pavement, or within no edge's pavement tolerance: Unchanged (landing
//     roll-outs and the GCLP S5 shape keep today's behaviour).
//   - Aircraft on the destination's own component: Unchanged. If ANY edge of the destination's
//     component holds the aircraft, that wins over a nearer edge of another component.
//   - Aircraft on another component, destination on the main component: LeavingUnconnectedPosition.
//   - Aircraft on another component, destination not on the main component: DestinationNotConnected.
//   - Fabricated stand bridges never hold the aircraft.
//
// Geometry sits on the equator (metres = degrees x 111132 in both axes).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteReachabilityTests
{
    private const double M = 1.0 / 111132.0;

    private static TaxiPath Taxiway(string name, double latN, double lonE, double lat2N, double lon2E,
                                    double widthFt = 75.0) => new()
    {
        Name = name, Type = "T", Width = widthFt, StartType = "N", EndType = "N",
        StartLat = latN * M, StartLon = lonE * M, EndLat = lat2N * M, EndLon = lon2E * M,
    };

    private static TaxiGraph Build(List<TaxiPath> paths, List<StartPosition>? starts = null, List<Runway>? runways = null) =>
        TaxiGraph.Build(paths, new List<ParkingSpot>(), starts ?? new List<StartPosition>(), runways);

    private static TaxiNode NodeAt(TaxiGraph g, double latN, double lonE) =>
        g.Nodes.Values.Single(n => Math.Abs(n.Latitude - latN * M) < 1e-9 && Math.Abs(n.Longitude - lonE * M) < 1e-9);

    /// <summary>Main taxiway U along the equator (0-240 m E, 5 nodes) plus a 2-node taxiway island
    /// S5 at 200 m N. S5 is not a stand stub, so it is never bridged.</summary>
    private static TaxiGraph MainAndIsland(double mainWidthFt = 75.0, double islandLatN = 200,
                                           double islandFromE = 100, double islandToE = 160,
                                           double islandWidthFt = 75.0)
    {
        var paths = Enumerable.Range(0, 4)
            .Select(i => Taxiway("U", 0, i * 60, 0, (i + 1) * 60, mainWidthFt)).ToList();
        paths.Add(Taxiway("S5", islandLatN, islandFromE, islandLatN, islandToE, islandWidthFt));
        return Build(paths);
    }

    /// <summary>Runway 09/27 on the equator from (0, 10 m E) to (0, 1010 m E) — never literally
    /// (0, 0), which RunwayShape.For (routed through since the RunwayPavement rewrite in Task 3)
    /// treats as an unset pavement end and silently falls back to the start-row half-width
    /// instead of the runway table's own width (see RunwayFixture's own "never (0, 0)" comment,
    /// and OrphanParkingIslandBridgeTests' identical fix for the identical reason). At (0,0) this
    /// left Runway09's widthFt argument dead: every call site here happens to pass 150, and
    /// 150 ft / 2 = 75 ft is numerically identical to the fixed 75 ft start-row default the
    /// fallback substitutes, so the tests kept passing while silently testing the DEFAULT
    /// half-width rather than the width this fixture claims to set. The 10 m offset only moves
    /// the runway itself; every taxiway/node coordinate in RunwayAirport() below is unchanged.
    /// </summary>
    private static List<StartPosition> Starts09And27() => new()
    {
        new() { RunwayName = "09", Type = "R", Heading = 90, Latitude = 0, Longitude = 10 * M },
        new() { RunwayName = "27", Type = "R", Heading = 270, Latitude = 0, Longitude = 1010 * M },
    };

    private static List<Runway> Runway09(double widthFt) => new()
    {
        new() { RunwayID = "09", StartLat = 0, StartLon = 10 * M, EndLat = 0, EndLon = 1010 * M, Width = widthFt },
    };

    /// <summary>Runway 09/27 (0-1000 m E, 150 ft wide), main taxiway B 100 m north of it, and a
    /// taxiway island S5 30 m south of the centreline.</summary>
    private static TaxiGraph RunwayAirport() => Build(new List<TaxiPath>
    {
        Taxiway("B", 100, 0, 100, 300),
        Taxiway("B", 100, 300, 100, 600),
        Taxiway("S5", -30, 400, -30, 460),
    }, Starts09And27(), Runway09(150));

    [Fact]
    public void On_the_destinations_own_network_is_unchanged()
    {
        var g = MainAndIsland();
        Assert.Equal(ReachabilityClass.Unchanged,
            RouteReachability.Classify(g, 5 * M, 130 * M, NodeAt(g, 0, 240).NodeId));
    }

    [Fact]
    public void On_the_main_network_with_the_destination_on_an_island_is_not_connected()
    {
        var g = MainAndIsland();
        Assert.Equal(ReachabilityClass.DestinationNotConnected,
            RouteReachability.Classify(g, 5 * M, 130 * M, NodeAt(g, 200, 100).NodeId));
    }

    [Fact]
    public void On_an_island_with_the_destination_on_the_main_network_is_leaving()
    {
        var g = MainAndIsland();
        Assert.Equal(ReachabilityClass.LeavingUnconnectedPosition,
            RouteReachability.Classify(g, 205 * M, 130 * M, NodeAt(g, 0, 120).NodeId));
    }

    [Fact]
    public void Off_every_edge_is_unchanged()
    {
        var g = MainAndIsland();
        Assert.Equal(ReachabilityClass.Unchanged,
            RouteReachability.Classify(g, 100 * M, 130 * M, NodeAt(g, 200, 100).NodeId));
    }

    [Fact]
    public void On_runway_pavement_is_unchanged_even_beside_an_island()
    {
        // GCLP S5 shape: 10 m south of the centreline (on the pavement) and 20 m from island S5.
        var g = RunwayAirport();
        int destination = NodeAt(g, 100, 300).NodeId;
        Assert.Equal(ReachabilityClass.Unchanged,
            RouteReachability.Classify(g, -10 * M, 430 * M, destination));

        // The same island DOES hold an aircraft off the pavement, 10 m from S5.
        Assert.Equal(ReachabilityClass.LeavingUnconnectedPosition,
            RouteReachability.Classify(g, -40 * M, 430 * M, destination));
    }

    [Fact]
    public void A_destination_network_edge_within_tolerance_wins_over_a_nearer_edge()
    {
        // Main edge 100 ft wide (tolerance 30.24 m) at 29 m; island edge at 11 m. Destination on main.
        var g = MainAndIsland(mainWidthFt: 100, islandLatN: 40);
        Assert.Equal(ReachabilityClass.Unchanged,
            RouteReachability.Classify(g, 29 * M, 130 * M, NodeAt(g, 0, 120).NodeId));
    }

    [Fact]
    public void The_25_metre_floor_decides_for_a_narrow_edge()
    {
        // Island S5 is 20 ft wide (tolerance = the 25 m floor), far east of the main network.
        var g = MainAndIsland(islandLatN: 0, islandFromE: 1000, islandToE: 1060, islandWidthFt: 20);
        int destination = NodeAt(g, 0, 120).NodeId;
        Assert.Equal(ReachabilityClass.LeavingUnconnectedPosition,
            RouteReachability.Classify(g, 24 * M, 1030 * M, destination));
        Assert.Equal(ReachabilityClass.Unchanged,
            RouteReachability.Classify(g, 26 * M, 1030 * M, destination));
    }

    [Fact]
    public void A_bogus_huge_width_is_capped_at_300_feet()
    {
        // Island S5 claims 4000 ft; capped to 300 ft its tolerance is 60.72 m.
        var g = MainAndIsland(islandLatN: 0, islandFromE: 1000, islandToE: 1060, islandWidthFt: 4000);
        int destination = NodeAt(g, 0, 120).NodeId;
        Assert.Equal(ReachabilityClass.LeavingUnconnectedPosition,
            RouteReachability.Classify(g, 60 * M, 1030 * M, destination));
        Assert.Equal(ReachabilityClass.Unchanged,
            RouteReachability.Classify(g, 62 * M, 1030 * M, destination));
    }

    [Fact]
    public void A_stand_bridge_never_holds_the_aircraft()
    {
        var g = new TaxiGraph();
        g.Nodes[1] = new TaxiNode { NodeId = 1, Latitude = 0, Longitude = 0, ComponentId = 0 };
        g.Nodes[2] = new TaxiNode { NodeId = 2, Latitude = 0, Longitude = 40 * M, ComponentId = 0 };
        g.Adjacency[1] = new List<TaxiEdge>
        {
            new() { FromNodeId = 1, ToNodeId = 2, DistanceMeters = 40, WidthFeet = 60, PathType = TaxiGraph.StandBridgePathType },
        };
        g.Adjacency[2] = new List<TaxiEdge>
        {
            new() { FromNodeId = 2, ToNodeId = 1, DistanceMeters = 40, WidthFeet = 60, PathType = TaxiGraph.StandBridgePathType },
        };

        Assert.Null(RouteReachability.FindAircraftComponent(g, 5 * M, 20 * M, destinationComponentId: 1));
    }

    [Fact]
    public void A_graph_that_Build_did_not_finish_is_unchanged()
    {
        var g = new TaxiGraph();   // MainComponentId stays -1
        g.Nodes[1] = new TaxiNode { NodeId = 1, Latitude = 0, Longitude = 0, ComponentId = 0 };
        Assert.Equal(ReachabilityClass.Unchanged, RouteReachability.Classify(g, 0, 0, 1));
    }

    [Fact]
    public void A_first_leg_clear_of_runways_is_allowed_with_its_gap()
    {
        var g = RunwayAirport();
        var start = new TaxiNode { Latitude = 100 * M, Longitude = 300 * M };

        var result = RouteReachability.CheckFirstLeg(g, 160 * M, 300 * M, start);

        Assert.False(result.CrossesRunway);
        Assert.Equal("", result.RunwayDesignator);
        Assert.InRange(result.GapMeters, 59.9, 60.1);
    }

    [Fact]
    public void A_first_leg_across_a_runway_is_refused_and_names_the_runway()
    {
        var g = RunwayAirport();
        var start = new TaxiNode { Latitude = 100 * M, Longitude = 300 * M };

        var result = RouteReachability.CheckFirstLeg(g, -40 * M, 300 * M, start);

        Assert.True(result.CrossesRunway);
        Assert.Equal("09", result.RunwayDesignator);
        Assert.InRange(result.GapMeters, 139.9, 140.1);
    }

    [Fact]
    public void A_first_leg_along_the_pavement_without_crossing_is_refused()
    {
        var g = RunwayAirport();
        var start = new TaxiNode { Latitude = 15 * M, Longitude = 260 * M };

        Assert.True(RouteReachability.CheckFirstLeg(g, 10 * M, 200 * M, start).CrossesRunway);
    }
}
