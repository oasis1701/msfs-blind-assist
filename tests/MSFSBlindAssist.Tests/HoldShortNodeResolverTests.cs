// Characterization tests for MSFSBlindAssist.Navigation.HoldShortNodeResolver.
//
// Safety-critical: this resolver picks the graph node where a Progressive Taxi
// "Hold short of runway" terminator ends the route. Tests build a synthetic
// TaxiGraph (TaxiGraph.Build accepts plain lists — no SQLite needed) using a
// runway that runs due TRUE NORTH from (37.0, -122.0) so the along-track axis
// is pure latitude offset and the cross-track axis is pure longitude offset —
// this keeps the fixture geometry hand-verifiable while still exercising the
// resolver's real RunwayFrame projection.
//
// This is characterization, not spec verification: values are derived by
// reasoning about the source and confirmed by running the tests; if a literal
// ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HoldShortNodeResolverTests
{
    private const double REF_LAT = 37.0;
    private const double BASE_LON = -122.0;
    private const double DEG_TO_M_LAT = 111320.0; // matches RunwayFrame's own constant

    // "Along" = metres north of the runway start threshold (REF_LAT, BASE_LON).
    private static double LatN(double alongM) => REF_LAT + alongM / DEG_TO_M_LAT;

    // "Lateral west" = metres WEST of the runway centerline longitude. Facing
    // north (runway heading 0), west is geographically LEFT — matches
    // RunwayFrame's documented +left/-right SignedCrossTrack convention.
    private static double LonW(double lateralWestM)
    {
        double degToMLon = DEG_TO_M_LAT * Math.Cos(REF_LAT * Math.PI / 180.0);
        return BASE_LON - lateralWestM / degToMLon;
    }

    // Runway.Width = 200 ft -> LegacySetbackMetres = 100 m, TrueHalfWidthMetres = 30.48 m,
    // hsFloorM (designated-node floor) = 30.48 + 15 = 45.48 m.
    private static Runway MakeRunway() => new Runway
    {
        RunwayID = "09",
        Heading = 0.0,
        HeadingMag = 0.0,
        StartLat = REF_LAT,
        StartLon = BASE_LON,
        EndLat = LatN(3000),
        EndLon = BASE_LON,
        Length = 3000.0 / 0.3048,
        Width = 200.0,
    };

    private static TaxiPath Edge(double alongA, double lateralA, string typeA,
                                  double alongB, double lateralB, string typeB, string name) => new TaxiPath
    {
        StartLat = LatN(alongA), StartLon = LonW(lateralA), StartType = typeA,
        EndLat = LatN(alongB), EndLon = LonW(lateralB), EndType = typeB,
        Name = name,
        Width = 50,
    };

    private static TaxiNode NodeNear(TaxiGraph graph, double alongM, double lateralWestM)
    {
        double lat = LatN(alongM), lon = LonW(lateralWestM);
        TaxiNode? best = null; double bestD = double.MaxValue;
        foreach (var n in graph.Nodes.Values)
        {
            double d = TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best!;
    }

    // Aircraft is parked 300 m west (well off the pavement) of the runway at
    // along=500 m -- forces the |acSignedCT| >= halfWidthTrueM branch so the
    // near-side sign is purely Math.Sign(acSignedCT), independent of heading.
    private const double AC_ALONG = 500.0;
    private const double AC_LATERAL = 300.0;

    // --- Tier 1: designated node on the cleared taxiway, closest wins --------

    [Fact]
    public void ResolveNearSide_tier1_prefers_the_designated_node_closest_to_the_runway()
    {
        var runway = MakeRunway();
        var paths = new List<TaxiPath>
        {
            Edge(AC_ALONG, 110, "", AC_ALONG, 90, "HS", "Q"),
            Edge(AC_ALONG, 90, "HS", AC_ALONG, 120, "HS", "Q"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var closer = NodeNear(graph, AC_ALONG, 90);
        var farther = NodeNear(graph, AC_ALONG, 120);
        closer.HoldShortName = "runway 09";
        farther.HoldShortName = "runway 09";

        var result = HoldShortNodeResolver.ResolveNearSide(
            graph, runway, LatN(AC_ALONG), LonW(AC_LATERAL), aircraftHeadingMag: 0, lastTaxiway: "Q");

        Assert.NotNull(result);
        Assert.Equal(closer.NodeId, result!.NodeId);
    }

    [Fact]
    public void ResolveNearSide_accepts_a_designated_node_named_for_the_reciprocal_runway()
    {
        var runway = MakeRunway(); // "09"
        var paths = new List<TaxiPath>
        {
            Edge(AC_ALONG, 110, "", AC_ALONG, 90, "HS", "Q"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var plain = NodeNear(graph, AC_ALONG, 110);
        var hs = NodeNear(graph, AC_ALONG, 90);
        hs.HoldShortName = "runway 27"; // reciprocal of "09" -- same pavement

        var result = HoldShortNodeResolver.ResolveNearSide(
            graph, runway, LatN(AC_ALONG), LonW(AC_LATERAL), 0, "Q");

        Assert.NotNull(result);
        Assert.Equal(hs.NodeId, result!.NodeId);
        Assert.NotEqual(plain.NodeId, result.NodeId);
    }

    [Fact]
    public void ResolveNearSide_excludes_a_designated_node_named_for_a_different_runway()
    {
        var runway = MakeRunway(); // "09"
        var paths = new List<TaxiPath>
        {
            Edge(AC_ALONG, 110, "", AC_ALONG, 90, "HS", "Q"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var plain = NodeNear(graph, AC_ALONG, 110);
        var hs = NodeNear(graph, AC_ALONG, 90);
        hs.HoldShortName = "runway 18"; // neither "09" nor its reciprocal "27"

        var result = HoldShortNodeResolver.ResolveNearSide(
            graph, runway, LatN(AC_ALONG), LonW(AC_LATERAL), 0, "Q");

        // Falls through to tier 3 (plain node on the cleared taxiway) since the
        // mis-named HS node is excluded from the designated candidate list.
        Assert.NotNull(result);
        Assert.Equal(plain.NodeId, result!.NodeId);
    }

    // --- Tier 3: plain node on the cleared taxiway (no HS candidate there) ---

    [Fact]
    public void ResolveNearSide_tier3_falls_back_to_the_plain_node_on_the_cleared_taxiway()
    {
        var runway = MakeRunway();
        var paths = new List<TaxiPath>
        {
            Edge(AC_ALONG, 110, "", AC_ALONG + 20, 115, "", "Q"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var closer = NodeNear(graph, AC_ALONG, 110);

        var result = HoldShortNodeResolver.ResolveNearSide(
            graph, runway, LatN(AC_ALONG), LonW(AC_LATERAL), 0, "Q");

        Assert.NotNull(result);
        Assert.Equal(closer.NodeId, result!.NodeId);
    }

    // --- Tier 4/5: no candidate on the cleared taxiway at all -----------------

    [Fact]
    public void ResolveNearSide_tier4_prefers_a_designated_node_at_the_legacy_pick_junction()
    {
        var runway = MakeRunway();
        // lastTaxiway "Q" is not present anywhere in the graph -- only "R" exists.
        var paths = new List<TaxiPath>
        {
            Edge(AC_ALONG, 105, "", AC_ALONG, 110, "HS", "R"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var plain = NodeNear(graph, AC_ALONG, 105); // legacy pick (min lateral >= 100 floor)
        var hs = NodeNear(graph, AC_ALONG, 110);
        hs.HoldShortName = "runway 09";

        var result = HoldShortNodeResolver.ResolveNearSide(
            graph, runway, LatN(AC_ALONG), LonW(AC_LATERAL), 0, "Q");

        // The designated node at the SAME junction (along-diff 0) out-ranks the
        // plain legacy pick, even though it isn't on the cleared taxiway.
        Assert.NotNull(result);
        Assert.Equal(hs.NodeId, result!.NodeId);
        Assert.NotEqual(plain.NodeId, result.NodeId);
    }

    [Fact]
    public void ResolveNearSide_tier5_prefers_the_nearby_plain_node_over_a_designated_node_far_down_the_runway()
    {
        var runway = MakeRunway();
        var paths = new List<TaxiPath>
        {
            // "R": the legacy pick, at along=500. Lateral values kept safely clear
            // of the legacyMinLateralM=100 boundary (not exactly 100) so the pick
            // isn't sensitive to floating-point noise in the cross-track projection.
            Edge(AC_ALONG, 105, "", AC_ALONG, 110, "", "R"),
            // Connector linking the two chains into one connected component.
            Edge(AC_ALONG, 110, "", AC_ALONG + 200, 95, "", ""),
            // "S": a designated node 200 m further down the runway (past the
            // HS_JUNCTION_ALONG_M=75m window) -- must NOT out-rank the plain pick.
            Edge(AC_ALONG + 200, 95, "", AC_ALONG + 200, 90, "HS", "S"),
        };
        var graph = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
        var plain = NodeNear(graph, AC_ALONG, 105);
        var hsFar = NodeNear(graph, AC_ALONG + 200, 90);
        hsFar.HoldShortName = "runway 09";

        var result = HoldShortNodeResolver.ResolveNearSide(
            graph, runway, LatN(AC_ALONG), LonW(AC_LATERAL), 0, "Q");

        Assert.NotNull(result);
        Assert.Equal(plain.NodeId, result!.NodeId);
        Assert.NotEqual(hsFar.NodeId, result.NodeId);
    }

    // --- Pure helper functions -----------------------------------------------

    [Theory]
    [InlineData(0.0, 90.0, -1)]   // aircraft heading east relative to a north runway -> right/east side
    [InlineData(0.0, 270.0, 1)]  // aircraft heading west -> left/west side
    [InlineData(0.0, 0.0, 1)]    // runway-parallel heading -> deterministic +1
    public void HeadingExitSideSign_returns_the_side_the_aircraft_heading_points_toward(
        double runwayHeadingMag, double aircraftHeadingMag, int expected)
    {
        var runway = new Runway { HeadingMag = runwayHeadingMag };
        Assert.Equal(expected, HoldShortNodeResolver.HeadingExitSideSign(runway, aircraftHeadingMag));
    }

    [Theory]
    [InlineData(200.0, 100.0)] // width/2, read as metres (deliberate ft/m quirk)
    [InlineData(0.0, 30.0)]    // unknown width -> default 30 m
    [InlineData(20.0, 15.0)]   // width/2=10 clamped up to the 15 m floor
    public void LegacySetbackMetres_reads_width_in_feet_as_metres(double widthFt, double expected)
    {
        var runway = new Runway { Width = widthFt };
        Assert.Equal(expected, HoldShortNodeResolver.LegacySetbackMetres(runway), 3);
    }

    [Fact]
    public void TrueHalfWidthMetres_converts_feet_to_metres_correctly()
    {
        var runway = new Runway { Width = 200.0 };
        Assert.Equal(30.48, HoldShortNodeResolver.TrueHalfWidthMetres(runway), 2);
    }

    [Fact]
    public void TrueHalfWidthMetres_defaults_when_width_is_unknown()
    {
        var runway = new Runway { Width = 0.0 };
        Assert.Equal(23.0, HoldShortNodeResolver.TrueHalfWidthMetres(runway));
    }
}
