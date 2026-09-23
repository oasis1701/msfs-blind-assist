// Runway traffic classification for the ground-traffic runway watch. PR #247 review R4 (parallel
// runways: a 28R arrival was reported as on final to 28L) and R6 (an aircraft in the flare over the
// pavement was invisible, so "no traffic seen on final" could be said seconds before it landed).

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayTrafficClassificationTests
{
    private const double DegLonPerMetreAt50 = 1.0 / (111320.0 * 0.642788);
    private const double DegLatPerMetre = 1.0 / 110540.0;

    private static TaxiGraph.RunwayCenterline EastWest(string n1, string n2, double lat) => new()
    {
        Name1 = n1, Name2 = n2, Lat1 = lat, Lon1 = 0.0, Lat2 = lat, Lon2 = 0.042,
        HeadingDeg1 = 90, HalfWidthMeters = 22.5,
    };

    private static RunwayShape Shape(TaxiGraph.RunwayCenterline cl) => RunwayShape.For(cl);

    // ── R4: parallel runways ────────────────────────────────────────────────────────────

    [Fact]
    public void A_close_parallel_arrival_fools_a_single_runway_test()
    {
        // The defect itself: 229 m apart (KSFO 28L/28R), a 3 nm final to the south runway is
        // inside the north runway's cone.
        var north = Shape(EastWest("09L", "27R", 50.0 + 229 * DegLatPerMetre));
        double lon = -3 * 1852 * DegLonPerMetreAt50;
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(north, 50.0, lon, false, 92, 950);
        Assert.Equal(RunwayTrafficKind.OnFinal, fix.Kind);
        Assert.Equal("09L", fix.Designator);
    }

    [Fact]
    public void A_close_parallel_arrival_is_assigned_to_its_own_runway()
    {
        var shapes = new[]
        {
            Shape(EastWest("09L", "27R", 50.0 + 229 * DegLatPerMetre)),
            Shape(EastWest("09R", "27L", 50.0)),
        };
        double lon = -3 * 1852 * DegLonPerMetreAt50;
        var a = Assert.Single(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, lon, false, 92, 950));
        Assert.Equal(1, a.ShapeIndex);
        Assert.Equal(RunwayTrafficKind.OnFinal, a.Fix.Kind);
        Assert.Equal("09R", a.Fix.Designator);
    }

    [Fact]
    public void A_wider_parallel_is_disambiguated_far_out_too()
    {
        var shapes = new[]
        {
            Shape(EastWest("09L", "27R", 50.0 + 1400 * DegLatPerMetre)),
            Shape(EastWest("09R", "27L", 50.0)),
        };
        double lon = -5.5 * 1852 * DegLonPerMetreAt50;
        Assert.Equal(RunwayTrafficKind.OnFinal,
            GroundTrafficLogic.ClassifyAgainstRunway(shapes[0], 50.0, lon, false, 90, 1700).Kind);
        var a = Assert.Single(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, lon, false, 90, 1700));
        Assert.Equal(1, a.ShapeIndex);
    }

    [Fact]
    public void Nothing_on_final_anywhere_assigns_nothing()
    {
        var shapes = new[] { Shape(EastWest("09", "27", 50.0)) };
        double lon = -3 * 1852 * DegLonPerMetreAt50;
        Assert.Empty(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, lon, false, 270, 950));
    }

    // ── R6: over the pavement ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(90.0, "09")]
    [InlineData(270.0, "27")]
    public void An_aircraft_in_the_flare_is_landing_on_the_end_it_flies_toward(double heading, string designator)
    {
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Shape(EastWest("09", "27", 50.0)),
            50.0, 0.003, false, heading, 30);
        Assert.Equal(RunwayTrafficKind.Landing, fix.Kind);
        Assert.Equal(designator, fix.Designator);
        Assert.Equal(0.0, fix.DistanceNm);
    }

    [Fact]
    public void A_go_around_high_over_the_runway_is_not_landing()
        => Assert.Equal(RunwayTrafficKind.None, GroundTrafficLogic.ClassifyAgainstRunway(
            Shape(EastWest("09", "27", 50.0)), 50.0, 0.003, false, 90, 1000).Kind);

    [Fact]
    public void An_aircraft_crossing_over_the_runway_is_not_landing()
        => Assert.Equal(RunwayTrafficKind.None, GroundTrafficLogic.ClassifyAgainstRunway(
            Shape(EastWest("09", "27", 50.0)), 50.0, 0.003, false, 0, 100).Kind);

    [Fact]
    public void Low_but_well_off_the_pavement_edge_is_not_landing()
        => Assert.Equal(RunwayTrafficKind.None, GroundTrafficLogic.ClassifyAgainstRunway(
            Shape(EastWest("09", "27", 50.0)), 50.0 + 120 * DegLatPerMetre, 0.003, false, 90, 100).Kind);

    [Fact]
    public void Landing_counts_for_the_best_runway_assignment()
    {
        var shapes = new[] { Shape(EastWest("09", "27", 50.0)) };
        var a = Assert.Single(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, 0.003, false, 90, 30));
        Assert.Equal(RunwayTrafficKind.Landing, a.Fix.Kind);
    }

    // ── threshold distance ──────────────────────────────────────────────────────────────

    [Fact]
    public void Final_distance_is_measured_to_a_displaced_threshold()
    {
        // Pavement starts 400 m west of the 09 threshold (the start row).
        var cl = EastWest("09", "27", 50.0);
        cl.PavementLat1 = 50.0; cl.PavementLon1 = -400 * DegLonPerMetreAt50;
        cl.PavementLat2 = 50.0; cl.PavementLon2 = 0.042;
        cl.PavementHalfWidthMeters = 22.5;
        var shape = Shape(cl);
        Assert.True(shape.UsesPavement);

        double lon = -400 * DegLonPerMetreAt50 - 3 * 1852 * DegLonPerMetreAt50;  // 3 nm before the pavement
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(shape, 50.0, lon, false, 90, 950);
        Assert.Equal(RunwayTrafficKind.OnFinal, fix.Kind);
        Assert.InRange(fix.DistanceNm, 3.19, 3.24);   // 3 nm + ~400 m, not 3.0
    }

    // ── on the ground ───────────────────────────────────────────────────────────────────

    [Fact]
    public void On_the_ground_at_an_intersection_is_on_both_runways()
    {
        var shapes = new[]
        {
            Shape(EastWest("09", "27", 50.0)),
            Shape(new TaxiGraph.RunwayCenterline
            {
                Name1 = "04", Name2 = "22", Lat1 = 49.99, Lon1 = 0.01, Lat2 = 50.01, Lon2 = 0.03,
                HeadingDeg1 = 45, HalfWidthMeters = 22.5,
            }),
        };
        var all = GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, 0.02, true, 90, 0);
        Assert.Equal(new[] { 0, 1 }, all.Select(a => a.ShapeIndex));
        Assert.All(all, a => Assert.Equal(RunwayTrafficKind.OnRunway, a.Fix.Kind));
    }
}
