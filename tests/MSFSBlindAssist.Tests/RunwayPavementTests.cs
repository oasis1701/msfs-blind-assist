// Characterization tests for RunwayPavement — the runway geometry both the orphan-stand
// bridge (never draw a bridge onto or across a runway) and route reachability (never start
// a route with a straight leg across a runway) rely on.
//
// Fixture: a 3000 m east-west runway on the equator from (0,0) to (0,FarLon), pavement
// half-width 22.5 m, designators "09" (west end) and "27" (east end). On the equator
// metres = degrees x 111132 in both axes.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayPavementTests
{
    private const double M = 1.0 / 111132.0;      // degrees per metre
    private const double FarLon = 3000 * M;

    private static List<TaxiGraph.RunwayCenterline> Runway() => new()
    {
        new TaxiGraph.RunwayCenterline
        {
            Lat1 = 0, Lon1 = 0, Lat2 = 0, Lon2 = FarLon, Name1 = "09", Name2 = "27",
            HalfWidthMeters = 22.5,
            PavementLat1 = 0, PavementLon1 = 0, PavementLat2 = 0, PavementLon2 = FarLon,
            PavementHalfWidthMeters = 22.5,
        },
    };

    [Fact]
    public void A_point_on_the_centreline_is_on_the_pavement()
    {
        Assert.True(RunwayPavement.IsOnPavement(0, 500 * M, Runway()));
    }

    [Fact]
    public void A_point_inside_the_half_width_is_on_and_one_outside_is_off()
    {
        Assert.True(RunwayPavement.IsOnPavement(20 * M, 500 * M, Runway()));
        Assert.False(RunwayPavement.IsOnPavement(25 * M, 500 * M, Runway()));
    }

    [Fact]
    public void A_point_beyond_the_runway_end_on_the_axis_is_off()
    {
        Assert.False(RunwayPavement.IsOnPavement(0, (3000 + 30) * M, Runway()));
    }

    [Fact]
    public void A_segment_crossing_the_runway_touches_it_and_is_named_after_the_nearer_end()
    {
        bool touches = RunwayPavement.SegmentTouchesPavement(
            40 * M, 500 * M, -40 * M, 500 * M, Runway(), out string designator);

        Assert.True(touches);
        Assert.Equal("09", designator);
    }

    [Fact]
    public void A_segment_running_along_the_centreline_touches_it()
    {
        Assert.True(RunwayPavement.SegmentTouchesPavement(
            0, 1000 * M, 0, 1040 * M, Runway(), out string designator));
        Assert.Equal("09", designator);
    }

    [Fact]
    public void A_segment_parallel_outside_the_half_width_does_not_touch()
    {
        Assert.False(RunwayPavement.SegmentTouchesPavement(
            30 * M, 1000 * M, 30 * M, 1040 * M, Runway(), out string designator));
        Assert.Equal("", designator);
    }

    [Fact]
    public void A_segment_just_past_the_runway_end_touches_only_within_the_half_width()
    {
        // North-south segment 10 m east of the east end: 10 m from the pavement end point.
        Assert.True(RunwayPavement.SegmentTouchesPavement(
            30 * M, (3000 + 10) * M, -30 * M, (3000 + 10) * M, Runway(), out string near));
        Assert.Equal("27", near);

        // The same segment 33 m east of the end is clear of the pavement.
        Assert.False(RunwayPavement.SegmentTouchesPavement(
            30 * M, (3000 + 33) * M, -30 * M, (3000 + 33) * M, Runway(), out _));
    }

    [Fact]
    public void No_centerlines_means_nothing_is_on_a_runway()
    {
        var none = new List<TaxiGraph.RunwayCenterline>();
        Assert.False(RunwayPavement.IsOnPavement(0, 0, none));
        Assert.False(RunwayPavement.SegmentTouchesPavement(1 * M, 0, -1 * M, 0, none, out _));
    }
}
