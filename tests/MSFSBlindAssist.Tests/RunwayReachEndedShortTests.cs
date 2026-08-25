// Characterization tests for the SECOND half of the runway-reach test — "the route ended
// short of the runway" — added 2026-08-24 after digging into the PHNL 04L failure.
//
// Background: the load-time reach check probes the DESTINATION NODE. TaxiRouter deliberately
// ends a runway route on the LAST CLEARED TAXIWAY when that taxiway does not connect to the
// destination (`lastTaxiwayTerminal`, runway destinations only) — the EIDW N2 / LFPG R1
// behaviour — and `_destinationNodeId` is never reassigned. So such a route leaves the
// destination node sitting on the runway while the route itself stops hundreds of metres away.
// That is the real PHNL 04L failure (clearance ended on a taxiway paralleling 04L, guidance held
// ~456 m off, lineup tone panned for four minutes), and moving the probe to the destination node
// in 2026-06-16 — to stop the LPPT 02 false positive — took the protection with it.
//
// Two independent "the route is fine" signals guard the new warning; BOTH must fail before it
// fires:
//   1. the end is a hold-short node named for the destination runway (TaxiGuidanceManager
//      .RouteEndIsRunwayHold) — covers set-back CAT II/III holds and sparse GA fields
//   2. the end is within RUNWAY_REACH_MAX_WALK_M of the runway PAVEMENT by graph distance
//      (TaxiGraph.GraphWalkToRunwayPavement) — covers airports whose navdata has no hold names
//
// Fixture idiom (shared with BacktrackEntryTests / RunwayLineupEntryTests): a synthetic
// east-west runway on the equator, where the code's equirectangular constant (111132 m/deg,
// cos(0)=1) makes along-track metres = degrees-of-longitude x 111132.
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the test to
// match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayReachEndedShortTests
{
    private const double M_PER_DEG = 111132.0;
    private const double FarLon = 0.027;        // ~3000 m runway
    private const double HalfWidthM = 22.5;
    private const double EntryLon = 0.00063;    // a connector meets the runway ~70 m along
    private const double HoldLat = 0.0006;      // ~67 m north — the hold line on that connector
    private const double SpineLat = 0.00450;    // ~500 m north — a parallel taxiway, no connector

    /// <summary>
    /// A connector running from a parallel spine down onto the runway, with an intermediate node
    /// at ~67 m north standing in for its hold line. The spine itself never touches the runway,
    /// so a route terminated on it is the PHNL shape.
    /// </summary>
    private static TaxiGraph Build()
    {
        var paths = new List<TaxiPath>
        {
            // Connector: spine -> hold -> runway.
            new TaxiPath { Name = "F", StartLat = SpineLat, StartLon = EntryLon, EndLat = HoldLat, EndLon = EntryLon },
            new TaxiPath { Name = "F", StartLat = HoldLat,  StartLon = EntryLon, EndLat = 0,       EndLon = EntryLon },
            // Parallel spine, running the length of the runway, touching it nowhere.
            new TaxiPath { Name = "S", StartLat = SpineLat, StartLon = EntryLon, EndLat = SpineLat, EndLon = 0.012 },
            new TaxiPath { Name = "S", StartLat = SpineLat, StartLon = 0.012,    EndLat = SpineLat, EndLon = 0.020 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static TaxiNode NodeAt(TaxiGraph g, double lat, double lon) =>
        g.Nodes.Values.Single(n => Math.Abs(n.Latitude - lat) < 1e-6 && Math.Abs(n.Longitude - lon) < 1e-6);

    private static double Walk(TaxiGraph g, TaxiNode from) =>
        g.GraphWalkToRunwayPavement(from.NodeId, 0, 0, 0, FarLon, HalfWidthM, 1500.0);

    [Fact]
    public void A_node_on_the_runway_is_zero_away()
    {
        var g = Build();
        Assert.Equal(0.0, Walk(g, NodeAt(g, 0, EntryLon)), 3);
    }

    [Fact]
    public void The_hold_on_the_connector_is_one_short_taxi_from_the_pavement()
    {
        // ~67 m — a normal departure hold. Well inside the threshold, so a route ending here
        // must never warn even though it never contained the destination node.
        var g = Build();
        double walk = Walk(g, NodeAt(g, HoldLat, EntryLon));

        Assert.InRange(walk, 60.0, 75.0);
        Assert.True(walk <= TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M);
    }

    [Fact]
    public void A_parallel_taxiway_with_no_connector_is_far_by_graph_even_where_it_is_near_in_a_straight_line()
    {
        // The PHNL shape. The spine's far end is ~500 m off the centerline in a straight line but
        // must be walked back along the spine and down the connector to reach the runway, so the
        // graph answer is far larger — which is exactly why the perpendicular cannot be the test.
        var g = Build();
        var farEnd = NodeAt(g, SpineLat, 0.020);

        double walk = Walk(g, farEnd);
        Assert.True(walk > TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M,
            $"expected the parallel-taxiway end to exceed the threshold, got {walk:F0} m");

        // Straight-line perpendicular is only ~500 m — under a naive 400 m distance rule this
        // would look no worse than a set-back hold.
        Assert.InRange(TaxiGraph.FastDistanceMeters(farEnd.Latitude, farEnd.Longitude, 0, 0.020), 480.0, 520.0);
    }

    [Fact]
    public void An_end_disconnected_from_the_runway_reports_infinity_not_zero()
    {
        // A node with no path to the pavement at all must read as "cannot reach", never as
        // "already there" — the manager maps this onto its search bound, the strongest possible
        // does-not-reach answer.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { Name = "X", StartLat = SpineLat, StartLon = 0.005, EndLat = SpineLat, EndLon = 0.009 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var n = NodeAt(g, SpineLat, 0.005);

        Assert.True(double.IsPositiveInfinity(
            g.GraphWalkToRunwayPavement(n.NodeId, 0, 0, 0, FarLon, HalfWidthM, 1500.0)));
    }

    [Fact]
    public void The_search_bound_is_honoured()
    {
        var g = Build();
        var farEnd = NodeAt(g, SpineLat, 0.020);

        // With a bound below the true walk the answer must be "cannot reach", not a wrong number.
        Assert.True(double.IsPositiveInfinity(
            g.GraphWalkToRunwayPavement(farEnd.NodeId, 0, 0, 0, FarLon, HalfWidthM, 100.0)));
    }

    [Fact]
    public void RouteEndIsRunwayHold_accepts_this_runways_hold_and_rejects_another_runways()
    {
        var hold = new TaxiNode { NodeId = 1, Type = TaxiNodeType.HoldShort, HoldShortName = "04L" };

        Assert.True(TaxiGuidanceManager.RouteEndIsRunwayHold(hold, "Runway 04L"));
        Assert.True(TaxiGuidanceManager.RouteEndIsRunwayHold(hold, "04L"));
        Assert.False(TaxiGuidanceManager.RouteEndIsRunwayHold(hold, "Runway 08R"));
    }

    [Fact]
    public void RouteEndIsRunwayHold_rejects_a_plain_node_and_an_unnamed_hold()
    {
        // A plain taxiway node is the PHNL end — it must not claim to be a hold.
        Assert.False(TaxiGuidanceManager.RouteEndIsRunwayHold(
            new TaxiNode { NodeId = 1, Type = TaxiNodeType.Normal, HoldShortName = "04L" }, "Runway 04L"));

        // An UNNAMED hold could belong to any runway, so it must not vouch for this one; the
        // walk test is what covers navdata with no hold names.
        Assert.False(TaxiGuidanceManager.RouteEndIsRunwayHold(
            new TaxiNode { NodeId = 2, Type = TaxiNodeType.HoldShort, HoldShortName = "" }, "Runway 04L"));
        Assert.False(TaxiGuidanceManager.RouteEndIsRunwayHold(
            new TaxiNode { NodeId = 3, Type = TaxiNodeType.ILSHoldShort, HoldShortName = null }, "Runway 04L"));
    }

    [Fact]
    public void FindCenterlineByName_matches_either_end_and_tolerates_the_Runway_prefix()
    {
        var g = new TaxiGraph();
        g.RunwayCenterlines.Add(new TaxiGraph.RunwayCenterline
        {
            Name1 = "04L", Name2 = "22R", Lat1 = 0, Lon1 = 0, Lat2 = 0, Lon2 = FarLon, HalfWidthMeters = HalfWidthM,
        });

        Assert.NotNull(g.FindCenterlineByName("04L"));
        Assert.NotNull(g.FindCenterlineByName("22R"));
        Assert.NotNull(g.FindCenterlineByName("Runway 04L"));
        Assert.Null(g.FindCenterlineByName("08R"));
        Assert.Null(g.FindCenterlineByName(""));
    }
}
