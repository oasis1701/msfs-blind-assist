using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// CL-3 (PR #230 review). The cell index DescribeLocation reads is built lazily and must follow the
/// graph's STRUCTURE: rebuilt after a change, never rebuilt without one. It used to decide that by
/// recounting every adjacency list on every query — O(nodes) per Alt+Y / Alt+L to answer a question
/// only a mutation can change — and is now dropped where TaxiGraph's own mutators change the
/// structure (AddEdge, SplitEdgeAt). A stale index is invisible through DescribeLocation today,
/// because the only post-Build change TaxiGraph makes (the painted holding-point projection)
/// subdivides an edge without moving any pavement; <see cref="TaxiGraph.CellIndexBuildCount"/> is
/// the one window onto it.
///
/// <para>These pin the build / drop / rebuild CONTRACT rather than reproduce a wrong answer: the
/// recount they replace already rebuilt exactly when these expect, so before the change they fail
/// only because CellIndexBuildCount does not exist yet.</para>
/// </summary>
public class TaxiGraphCellIndexTests
{
    private const double MetresPerDegLat = 111132.0;

    /// <summary>Taxiway D, one 1,200 m segment due north through 52.0 N, 4.0 E.</summary>
    private static TaxiGraph TaxiwayD()
    {
        double half = 600.0 / MetresPerDegLat;
        var d = new TaxiPath
        {
            Name = "D", Type = "T", Width = 98.0, StartType = "N", EndType = "N",
            StartLat = 52.0 - half, StartLon = 4.0, EndLat = 52.0 + half, EndLon = 4.0,
        };
        return TaxiGraph.Build(new List<TaxiPath> { d }, new List<ParkingSpot>(), new List<StartPosition>(), new List<Runway>());
    }

    [Fact]
    public void The_index_is_built_on_the_first_query_and_not_again_without_a_change()
    {
        var g = TaxiwayD();
        Assert.Equal(0, g.CellIndexBuildCount);   // Build itself never builds it

        Assert.Equal("Taxiway D", g.DescribeLocation(52.0, 4.0));
        Assert.Equal("Taxiway D", g.DescribeLocation(52.0 + 100.0 / MetresPerDegLat, 4.0));
        Assert.Equal("", g.DescribeLocation(52.0, 4.01));   // 684 m east: nothing near, still no rebuild
        Assert.Equal(1, g.CellIndexBuildCount);
    }

    [Fact]
    public void A_subdivision_drops_the_index_and_the_next_query_rebuilds_it_once()
    {
        var g = TaxiwayD();
        g.DescribeLocation(52.0, 4.0);

        // A painted holding point projected onto D: SplitEdgeAt replaces the edge with two halves.
        Assert.NotNull(g.InsertHoldingPointNodeOnEdge(52.0 + 50.0 / MetresPerDegLat, 4.0, maxPerpMeters: 5.0));
        Assert.Equal(1, g.CellIndexBuildCount);   // dropped, not rebuilt eagerly

        Assert.Equal("Taxiway D", g.DescribeLocation(52.0, 4.0));
        Assert.Equal("Taxiway D", g.DescribeLocation(52.0 + 50.0 / MetresPerDegLat, 4.0));
        Assert.Equal(2, g.CellIndexBuildCount);
    }

    [Fact]
    public void A_projection_that_finds_no_edge_changes_nothing_and_drops_nothing()
    {
        var g = TaxiwayD();
        g.DescribeLocation(52.0, 4.0);

        // 60 m east of D: no edge within 5 m, so nothing is inserted and the structure is unchanged.
        double sixtyMetresEast = 60.0 / (MetresPerDegLat * Math.Cos(52.0 * Math.PI / 180.0));
        Assert.Null(g.InsertHoldingPointNodeOnEdge(52.0, 4.0 + sixtyMetresEast, maxPerpMeters: 5.0));

        g.DescribeLocation(52.0, 4.0);
        Assert.Equal(1, g.CellIndexBuildCount);
    }
}
