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

    // ---- RunwayShape-backed fixes: width cap, mis-pair rejection, degenerate skip -------------
    // These three use RunwayFixture (base lat/lon 0.01, never (0, 0) — see its own comment) rather
    // than this file's own equator fixture, because they deliberately probe RunwayShape.For's
    // (0, 0) "unset pavement" sentinel and its mis-pair rejection, which the equator fixture above
    // would trip on by coincidence rather than by design.

    [Fact]
    public void A_malformed_2001_foot_width_is_capped_so_a_point_off_the_centerline_is_no_longer_swallowed()
    {
        // fs2024's worst observed runway.width defect: 2001 ft gives an uncapped ~304.95 m
        // half-width. RunwayShape.MaxPlausibleHalfWidthMeters caps it to 60.96 m, so a point
        // 50 m off the centreline (inside the cap) is still swallowed like any wide runway, but
        // one 70 m off (beyond the cap, still well inside the raw malformed value) is not.
        double malformedHalfWidth = 2001.0 * 0.3048 / 2.0;
        var runway = RunwayFixture.EastWest(halfWidthM: malformedHalfWidth);
        var centerlines = new List<TaxiGraph.RunwayCenterline> { runway };

        Assert.True(RunwayPavement.IsOnPavement(
            RunwayFixture.Lat(50.0), RunwayFixture.Lon(1500.0), centerlines));
        Assert.False(RunwayPavement.IsOnPavement(
            RunwayFixture.Lat(70.0), RunwayFixture.Lon(1500.0), centerlines));
    }

    [Fact]
    public void A_mis_paired_pavement_line_falls_back_to_the_start_rows()
    {
        // EDVQ-style: the heading pass paired these start rows with ANOTHER runway's pavement,
        // 150 m to the side (mirrors RunwayShapeTests' own
        // Start_rows_off_the_pavement_axis_mean_the_pavement_belongs_to_another_runway, from the
        // other direction). RunwayShape.For must discard the mis-paired pavement and fall back to
        // the start rows, so the bogus pavement location counts for nothing here.
        var runway = RunwayFixture.EastWest();
        runway.PavementLat1 = RunwayFixture.Lat(150.0);
        runway.PavementLat2 = RunwayFixture.Lat(150.0);
        var centerlines = new List<TaxiGraph.RunwayCenterline> { runway };

        // On the real (start-row) runway line.
        Assert.True(RunwayPavement.IsOnPavement(
            RunwayFixture.Lat(0.0), RunwayFixture.Lon(1500.0), centerlines));

        // Near the discarded, mis-paired pavement location: not this runway's pavement.
        Assert.False(RunwayPavement.IsOnPavement(
            RunwayFixture.Lat(150.0), RunwayFixture.Lon(1500.0), centerlines));
    }

    [Fact]
    public void A_degenerate_runway_is_skipped_entirely()
    {
        // Start AND pavement rows collapsed to a single point, but still carrying a plausible
        // 30 m half-width — a malformed row with no real axis must contribute nothing, not a
        // phantom circular "pavement" around that one point.
        var degenerate = new TaxiGraph.RunwayCenterline
        {
            Name1 = "09", Name2 = "27",
            Lat1 = RunwayFixture.BaseLat, Lon1 = RunwayFixture.BaseLon,
            Lat2 = RunwayFixture.BaseLat, Lon2 = RunwayFixture.BaseLon,
            HalfWidthMeters = 30.0,
            PavementLat1 = RunwayFixture.BaseLat, PavementLon1 = RunwayFixture.BaseLon,
            PavementLat2 = RunwayFixture.BaseLat, PavementLon2 = RunwayFixture.BaseLon,
            PavementHalfWidthMeters = 30.0,
        };
        var centerlines = new List<TaxiGraph.RunwayCenterline> { degenerate };

        // 10 m from the collapsed point: well inside a naive 30 m radius, but there is no real
        // runway here.
        Assert.False(RunwayPavement.IsOnPavement(
            RunwayFixture.Lat(10.0), RunwayFixture.Lon(0.0), centerlines));

        bool touches = RunwayPavement.SegmentTouchesPavement(
            RunwayFixture.Lat(-10.0), RunwayFixture.Lon(0.0),
            RunwayFixture.Lat(10.0), RunwayFixture.Lon(0.0),
            centerlines, out string designator);
        Assert.False(touches);
        Assert.Equal("", designator);
    }

    [Fact]
    public void A_segment_crossing_two_runways_is_named_after_the_nearer_crossing()
    {
        // Two parallel east-west runways, 300 m apart (09L at lat 0, 09R at lat 300 m).
        // The leg runs north-south at Lon 500 m, crossing 09L first (100 m from `a`) and
        // then 09R (400 m from `a`). The FARTHER runway (09R) is listed FIRST, so a naive
        // "first touched in list order" pick would name it instead of the nearer one.
        var near = new TaxiGraph.RunwayCenterline
        {
            Lat1 = 0, Lon1 = 0, Lat2 = 0, Lon2 = FarLon, Name1 = "09L", Name2 = "27L",
            HalfWidthMeters = 22.5,
            PavementLat1 = 0, PavementLon1 = 0, PavementLat2 = 0, PavementLon2 = FarLon,
            PavementHalfWidthMeters = 22.5,
        };
        var far = new TaxiGraph.RunwayCenterline
        {
            Lat1 = 300 * M, Lon1 = 0, Lat2 = 300 * M, Lon2 = FarLon, Name1 = "09R", Name2 = "27R",
            HalfWidthMeters = 22.5,
            PavementLat1 = 300 * M, PavementLon1 = 0, PavementLat2 = 300 * M, PavementLon2 = FarLon,
            PavementHalfWidthMeters = 22.5,
        };
        var centerlines = new List<TaxiGraph.RunwayCenterline> { far, near };

        bool touches = RunwayPavement.SegmentTouchesPavement(
            -100 * M, 500 * M, 400 * M, 500 * M, centerlines, out string designator);

        Assert.True(touches);
        Assert.Equal("09L", designator);
    }

    // ---- Unnamed runways must never name a refusal (they must still be TOUCHED) --------------

    [Fact]
    public void An_unnamed_runway_nearer_the_start_does_not_block_a_named_one_farther_along()
    {
        // Two parallel east-west runways, 300 m apart. The leg crosses the UNNAMED one first
        // (100 m from `a`) and the NAMED one second (400 m from `a`). Mirrors the "nearer
        // crossing wins" test above, except the nearer one this time has no designator at
        // either end — a naive "nearest touch always wins" pick (the pre-fix behaviour) leaves
        // designator blank instead of falling through to the named runway behind it.
        //
        // Lon 500 (not the runway's own midpoint, 1500 of its 3000 m length) keeps NameAt's
        // near/far pick well clear of its own tie-break boundary — at the exact midpoint two
        // independently-projected RunwayShapes (this one at north 0, the named one at north 300)
        // can legitimately disagree by float noise on which side of "along <= length / 2" a point
        // falls, which is a projection-precision detail this test has nothing to do with.
        var unnamed = RunwayFixture.EastWest(name1: "", name2: "");
        var named = RunwayFixture.EastWest(name1: "09R", name2: "27R", northM: 300.0);
        var centerlines = new List<TaxiGraph.RunwayCenterline> { unnamed, named };

        bool touches = RunwayPavement.SegmentTouchesPavement(
            RunwayFixture.Lat(-100.0), RunwayFixture.Lon(500.0),
            RunwayFixture.Lat(400.0), RunwayFixture.Lon(500.0),
            centerlines, out string designator);

        Assert.True(touches);
        Assert.Equal("09R", designator);
    }

    [Fact]
    public void Touching_only_an_unnamed_runway_still_refuses_but_names_nothing()
    {
        // The geometry is real (a bridge or first leg across it must still be refused), but with
        // nothing to call it, the designator comes back empty rather than a stale/wrong name.
        var unnamed = RunwayFixture.EastWest(name1: "", name2: "");
        var centerlines = new List<TaxiGraph.RunwayCenterline> { unnamed };

        bool touches = RunwayPavement.SegmentTouchesPavement(
            RunwayFixture.Lat(40.0), RunwayFixture.Lon(1500.0),
            RunwayFixture.Lat(-40.0), RunwayFixture.Lon(1500.0),
            centerlines, out string designator);

        Assert.True(touches);
        Assert.Equal("", designator);
    }
}
