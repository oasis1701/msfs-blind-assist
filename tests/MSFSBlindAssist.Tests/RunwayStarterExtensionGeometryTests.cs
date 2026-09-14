// Characterization tests for TaxiGraph.MetersBehindRunwayThreshold — "is this node on the
// runway's own axis, behind its threshold?", the measurement that feeds RunwayReachGate's
// starter-extension verdict (issue #229).
//
// The lateral corridor is the safety property, not an implementation detail: the fault the
// reach gate exists to catch is a clearance that ended on a taxiway PARALLELING the runway
// (PHNL 04L, 655 m of taxiing with no connector), and a parallel taxiway is laterally
// OFFSET. Everything here is really one assertion — that the corridor is what separates the
// two — measured from several directions.
//
// Fixture on the equator (lat 0) so a degree of latitude and of longitude are both
// 111,132 m, TaxiGraph's own constant.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayStarterExtensionGeometryTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    // Runway 09/27, threshold of 09 at (0,0), 3,000 m due east, half-width 30 m — OMDB
    // 12R's own half-width, so the +5 m corridor is the 35 m the real case was measured in.
    private const double LengthM = 3000.0;
    private const double HalfWidthM = 30.0;

    /// <summary>One taxi path, so the graph has a node at each end. Nodes are placed where
    /// the test wants them; the edge itself is irrelevant to the projection.</summary>
    private static TaxiGraph GraphWithNodeAt(double metresAlong, double metresLeft)
    {
        double lat = metresLeft * DEG_PER_M;
        double lon = metresAlong * DEG_PER_M;
        var paths = new List<TaxiPath>
        {
            new TaxiPath
            {
                Name = "X", Type = "PT", Width = 75.0,
                StartLat = lat, StartLon = lon,
                // 200 m away, well clear of Build's 1.5 m node merge, so the placed node
                // survives as its own vertex and is the one NodeAt finds.
                EndLat = lat + 200.0 * DEG_PER_M, EndLon = lon,
            }
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static int NodeAt(TaxiGraph g, double metresAlong, double metresLeft)
    {
        double lat = metresLeft * DEG_PER_M, lon = metresAlong * DEG_PER_M;
        return g.Nodes.Values
                .OrderBy(n => TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude))
                .First().NodeId;
    }

    private static double? Behind(TaxiGraph g, int nodeId) =>
        g.MetersBehindRunwayThreshold(
            nodeId,
            thrLat: 0.0, thrLon: 0.0,
            farLat: 0.0, farLon: LengthM * DEG_PER_M,
            halfWidthMeters: HalfWidthM);

    [Fact]
    public void A_node_on_the_axis_behind_the_threshold_reports_its_distance()
    {
        // The OMDB 12R shape: 699 m behind, 20 m off the centreline, inside the 35 m corridor.
        var g = GraphWithNodeAt(-699.0, 20.0);
        double? behind = Behind(g, NodeAt(g, -699.0, 20.0));

        Assert.NotNull(behind);
        Assert.InRange(behind!.Value, 698.0, 700.0);
    }

    [Fact]
    public void A_parallel_taxiway_behind_the_threshold_reports_nothing()
    {
        // THE safety case. Same 699 m back, but 120 m to the side — a taxiway paralleling the
        // runway, which is the PHNL 04L fault the reach warning exists for. Outside the
        // corridor, so it is indistinguishable from "no answer" and the warning still fires.
        var g = GraphWithNodeAt(-699.0, 120.0);

        Assert.Null(Behind(g, NodeAt(g, -699.0, 120.0)));
    }

    [Fact]
    public void Just_outside_the_corridor_reports_nothing()
    {
        // The corridor is half-width + 5 m = 35 m. 36 m out is off the runway's own pavement.
        var g = GraphWithNodeAt(-400.0, 36.0);

        Assert.Null(Behind(g, NodeAt(g, -400.0, 36.0)));
    }

    [Fact]
    public void A_node_on_the_runway_itself_reports_nothing()
    {
        // "Behind the threshold" is strictly negative along-track: a node ON the runway is
        // already handled by the walk probe returning 0, and must not be reported here.
        var g = GraphWithNodeAt(500.0, 5.0);

        Assert.Null(Behind(g, NodeAt(g, 500.0, 5.0)));
    }

    [Fact]
    public void A_node_off_the_FAR_end_reports_nothing()
    {
        // Off the 27 end, on the axis. It is behind THAT threshold, not this one — which is
        // why the caller has to orient the centreline before measuring, and why measuring
        // from the wrong end would report a far-end overrun as this end's starter extension.
        var g = GraphWithNodeAt(LengthM + 400.0, 5.0);

        Assert.Null(Behind(g, NodeAt(g, LengthM + 400.0, 5.0)));
    }

    [Fact]
    public void A_node_the_graph_does_not_have_reports_nothing()
    {
        var g = GraphWithNodeAt(-699.0, 20.0);

        Assert.Null(g.MetersBehindRunwayThreshold(
            999999, 0.0, 0.0, 0.0, LengthM * DEG_PER_M, HalfWidthM));
    }

    [Fact]
    public void A_degenerate_runway_reports_nothing_rather_than_dividing_by_zero()
    {
        var g = GraphWithNodeAt(-699.0, 20.0);

        Assert.Null(g.MetersBehindRunwayThreshold(
            NodeAt(g, -699.0, 20.0), 0.0, 0.0, 0.0, 0.0, HalfWidthM));
    }
}
