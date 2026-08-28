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
    // ~150 m north — a parallel taxiway with no connector of its own. Deliberately INSIDE
    // RUNWAY_REACH_MAX_WALK_M in a straight line: the whole point of measuring by graph is
    // that a perpendicular rule would wave this end through.
    private const double SpineLat = 0.00135;
    private const double SpineFarLon = 0.006;   // ~597 m along the spine from the connector

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
            // Parallel spine, running alongside the runway, touching it nowhere. Short enough
            // that the walk to the pavement stays INSIDE the search bound — otherwise the
            // answer is infinity and the test cannot tell a long finite walk from no path
            // at all, which is the property this fixture exists to demonstrate.
            new TaxiPath { Name = "S", StartLat = SpineLat, StartLon = EntryLon, EndLat = SpineLat, EndLon = 0.003 },
            new TaxiPath { Name = "S", StartLat = SpineLat, StartLon = 0.003,    EndLat = SpineLat, EndLon = SpineFarLon },
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
        // The PHNL shape. The spine's far end is only ~150 m off the centerline in a straight
        // line, but must be walked back along the spine and down the connector to reach the
        // runway — so the graph answer is several times larger. That contrast IS the design:
        // a perpendicular rule waves this end through, the graph walk catches it.
        var g = Build();
        var farEnd = NodeAt(g, SpineLat, SpineFarLon);

        double walk = Walk(g, farEnd);

        // A FINITE number, comfortably inside the search bound — this must not pass merely by
        // being infinity, or it would be indistinguishable from the disconnected case below
        // and would still pass if the Dijkstra could only ever return 0 or infinity.
        Assert.True(double.IsFinite(walk), $"expected a finite graph distance, got {walk}");
        Assert.InRange(walk, 700.0, 800.0);
        Assert.True(walk > TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M,
            $"expected the parallel-taxiway end to exceed the threshold, got {walk:F0} m");

        // ...while the straight line to the nearest point on the runway is well UNDER the
        // same threshold, so a naive perpendicular rule would have called this reachable.
        double straightLine = TaxiGraph.FastDistanceMeters(
            farEnd.Latitude, farEnd.Longitude, 0, SpineFarLon);
        Assert.InRange(straightLine, 140.0, 160.0);
        Assert.True(straightLine < TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M);
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
        var farEnd = NodeAt(g, SpineLat, SpineFarLon);

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

    // ---- The centerline must carry the PAVEMENT, not just the start rows -----------------
    //
    // RunwayCenterline is paired from the navdata `start` table, and SnapStartToRunwayCenterline
    // only corrects those rows LATERALLY — the along-track position stays wherever the row sits.
    // At a displaced threshold that is hundreds of metres inside the pavement (LPPT 20: 626 m),
    // so a centerline-framed "is this node on the runway?" test rejects real runway pavement.
    // Measured over the shipped fs2024 DB: 7,123 of 95,989 runway ends (7.42 %) have a start row
    // more than 50 m inboard, and 344 exceed 400 m outright. HalfWidthMeters is likewise a fixed
    // 75 ft default, while 5,127 of 48,040 runways are wider than 150 ft.
    //
    // The walk probe and the entrance picker are the two halves of ONE reach test, so they must
    // agree about where the runway is. The pavement fields carry the runway table's own geometry
    // through to the walk; they are additive, so every existing consumer of Lat1/Lon1/HalfWidth
    // is untouched, and they fall back to the start-row values when Build has no runway table.

    [Fact]
    public void A_centerline_built_without_a_runway_table_falls_back_to_the_start_rows()
    {
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = 0, Longitude = 0, Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = 0, Longitude = FarLon, Heading = 270 },
        };
        var g = TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts);

        var cl = Assert.Single(g.RunwayCenterlines);
        Assert.Equal(cl.Lat1, cl.PavementLat1, 9);
        Assert.Equal(cl.Lon1, cl.PavementLon1, 9);
        Assert.Equal(cl.Lat2, cl.PavementLat2, 9);
        Assert.Equal(cl.Lon2, cl.PavementLon2, 9);
        Assert.Equal(cl.HalfWidthMeters, cl.PavementHalfWidthMeters, 9);
    }

    [Fact]
    public void A_displaced_threshold_start_row_leaves_the_pavement_ends_on_the_runway_table()
    {
        // LPPT 20 to scale: the 09 start row sits 600 m inside the pavement.
        const double DisplacedLon = 600.0 / M_PER_DEG;
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = 0, Longitude = DisplacedLon, Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = 0, Longitude = FarLon, Heading = 270 },
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "09", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = FarLon, Width = 200.0 },
            new Runway { RunwayID = "27", StartLat = 0, StartLon = FarLon, EndLat = 0, EndLon = 0, Width = 200.0 },
        };

        var g = TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts, runways);

        var cl = Assert.Single(g.RunwayCenterlines);
        // The start-row frame is unchanged — every existing consumer still sees what it saw.
        Assert.Equal(DisplacedLon, cl.Lon1, 9);
        // The pavement frame reaches the real threshold and carries the real half-width.
        Assert.Equal(0.0, cl.PavementLon1, 9);
        Assert.Equal(FarLon, cl.PavementLon2, 9);
        Assert.Equal(200.0 * 0.3048 / 2.0, cl.PavementHalfWidthMeters, 6);
    }

    [Fact]
    public void Pavement_behind_a_displaced_threshold_still_counts_as_on_the_runway()
    {
        // A node on the runway 100 m along the PAVEMENT, i.e. 500 m BEHIND a 600 m displaced
        // start row. Framed on the start rows it projects to along = -500 and is rejected by
        // the -50 m floor, so the walk reports "no path to the runway" for an aircraft that is
        // standing on it — which speaks a false "this route stops short" and arms the bailout.
        const double DisplacedLon = 600.0 / M_PER_DEG;
        const double OnPavementLon = 100.0 / M_PER_DEG;

        var paths = new List<TaxiPath>
        {
            new TaxiPath { Name = "A", StartLat = SpineLat, StartLon = OnPavementLon, EndLat = 0, EndLon = OnPavementLon },
        };
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = 0, Longitude = DisplacedLon, Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = 0, Longitude = FarLon, Heading = 270 },
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "09", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = FarLon, Width = 150.0 },
            new Runway { RunwayID = "27", StartLat = 0, StartLon = FarLon, EndLat = 0, EndLon = 0, Width = 150.0 },
        };

        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
        var cl = Assert.Single(g.RunwayCenterlines);
        var onPavement = NodeAt(g, 0, OnPavementLon);

        double walk = g.GraphWalkToRunwayPavement(
            onPavement.NodeId,
            cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2,
            cl.PavementHalfWidthMeters, 1500.0);

        Assert.Equal(0.0, walk);
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

    [Theory]
    // The centerline is named from the `start` table, the destination from `runway_end` —
    // two tables that need not agree on zero-padding, and the DB ecosystem documents
    // unpadded spellings (approach tables, third-party scenery). A miss here is silent and
    // safe-LOOKING: RouteEndWalkToRunwayMeters reads null as "no answer → 0 m → reaches",
    // so the whole ended-short warning simply never fires at that airport.
    [InlineData("9L", "Runway 09L")]
    [InlineData("09L", "Runway 9L")]
    [InlineData("9L", "9L")]
    [InlineData("9", "Runway 09")]
    public void FindCenterlineByName_tolerates_zero_padding_differences(
        string storedName, string lookup)
    {
        var g = new TaxiGraph();
        g.RunwayCenterlines.Add(new TaxiGraph.RunwayCenterline
        {
            Name1 = storedName, Name2 = "27R", Lat1 = 0, Lon1 = 0, Lat2 = 0, Lon2 = FarLon,
            HalfWidthMeters = HalfWidthM,
        });

        Assert.NotNull(g.FindCenterlineByName(lookup));
    }

    [Fact]
    public void FindCenterlineByName_still_refuses_a_different_runway()
    {
        // Normalization must not become a fuzzy match: 09L and 09R are different pavement.
        var g = new TaxiGraph();
        g.RunwayCenterlines.Add(new TaxiGraph.RunwayCenterline
        {
            Name1 = "9L", Name2 = "27R", Lat1 = 0, Lon1 = 0, Lat2 = 0, Lon2 = FarLon,
            HalfWidthMeters = HalfWidthM,
        });

        Assert.Null(g.FindCenterlineByName("Runway 09R"));
        Assert.Null(g.FindCenterlineByName("Runway 10L"));
    }
}
