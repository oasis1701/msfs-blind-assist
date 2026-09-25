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
            50.0, 0.003, false, heading, 30, climbFpm: -400);
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
        var a = Assert.Single(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, 0.003, false, 90, 30, climbFpm: -400));
        Assert.Equal(RunwayTrafficKind.Landing, a.Fix.Kind);
    }

    // ── climb rate: a departure is not landing (PR #247 B1 review C1, implementer concern 1) ──
    // A departure just after liftoff is low, aligned and over the pavement — the Landing geometry
    // exactly — and was announced "landing runway 27L", interrupting a pilot lined up behind it.

    // 150 ft over the 09 end's pavement, heading 090.
    private static RunwayTrafficFix OverPavement(double? climbFpm, bool recentlyOnGround = false)
        => GroundTrafficLogic.ClassifyAgainstRunway(Shape(EastWest("09", "27", 50.0)),
            50.0, 0.003, false, 90, 150, climbFpm, recentlyOnGround);

    // The 3 nm final GroundTrafficLogicTests.Airborne_ThreeMilesWest_HeadingEast_IsOnFinalFor09 uses.
    private static RunwayTrafficFix ThreeMileFinal(double? climbFpm, bool recentlyOnGround = false)
        => GroundTrafficLogic.ClassifyAgainstRunway(Shape(EastWest("09", "27", 50.0)),
            50.0, -3 * 1852 * DegLonPerMetreAt50, false, 92, 950, climbFpm, recentlyOnGround);

    [Fact]
    public void A_departure_climbing_after_liftoff_is_not_landing()
        => Assert.Equal(RunwayTrafficKind.None, OverPavement(climbFpm: 1500).Kind);

    [Theory]
    [InlineData(300.0, true)]    // the limit itself still lands
    [InlineData(301.0, false)]
    [InlineData(-400.0, true)]
    public void Landing_needs_a_climb_rate_at_or_below_the_limit(double climbFpm, bool landing)
        => Assert.Equal(landing ? RunwayTrafficKind.Landing : RunwayTrafficKind.None, OverPavement(climbFpm).Kind);

    [Fact]
    public void A_known_climb_rules_out_a_final()
        => Assert.Equal(RunwayTrafficKind.None, ThreeMileFinal(climbFpm: 1200).Kind);

    [Fact]
    public void An_unknown_climb_rate_does_not_rule_out_a_final()
    {
        var fix = ThreeMileFinal(climbFpm: null);
        Assert.Equal(RunwayTrafficKind.OnFinal, fix.Kind);
        Assert.Equal("09", fix.Designator);
    }

    [Fact]
    public void A_descent_on_final_is_on_final()
        => Assert.Equal(RunwayTrafficKind.OnFinal, ThreeMileFinal(climbFpm: -700).Kind);

    [Fact]
    public void A_climbing_aircraft_over_the_pavement_is_assigned_to_no_runway()
    {
        var shapes = new[]
        {
            Shape(EastWest("09L", "27R", 50.0 + 229 * DegLatPerMetre)),
            Shape(EastWest("09R", "27L", 50.0)),
        };
        Assert.Empty(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, 0.003, false, 90, 150, climbFpm: 1500));
        // The same geometry descending is the landing it was always meant to catch.
        Assert.Equal(1, Assert.Single(
            GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, 0.003, false, 90, 150, climbFpm: -400)).ShapeIndex);
    }

    [Fact]
    public void An_aircraft_seen_on_the_ground_moments_ago_is_not_landing()
    {
        // The first samples after liftoff, before the climb rate shows (rotation builds it).
        Assert.Equal(RunwayTrafficKind.None, OverPavement(climbFpm: -400, recentlyOnGround: true).Kind);
        Assert.Equal(RunwayTrafficKind.Landing, OverPavement(climbFpm: -400, recentlyOnGround: false).Kind);
    }

    [Fact]
    public void Recently_on_the_ground_does_not_affect_a_final()
        => Assert.Equal(RunwayTrafficKind.OnFinal, ThreeMileFinal(climbFpm: -700, recentlyOnGround: true).Kind);

    [Fact]
    public void ClassifyAgainstRunways_passes_recently_on_the_ground_through()
    {
        var shapes = new[] { Shape(EastWest("09", "27", 50.0)) };
        Assert.Empty(GroundTrafficLogic.ClassifyAgainstRunways(shapes, 50.0, 0.003, false, 90, 150,
            climbFpm: -400, recentlyOnGround: true));
    }

    // ── an unknown climb over the pavement is pending (PR #247 B2 review Important 1) ──────
    // The intake keeps airborne traffic only while a runway is watched, so when a watch's first
    // status is composed every airborne aircraft is on its FIRST sample, with no climb rate. An
    // aircraft in the flare then read as nothing: "Runway 27L: no traffic seen on the runway or on
    // final." and a second later "Delta A320 landing runway 27L." Pending holds that first status
    // back for the aircraft's next sample. (The go-around, crossing and off-the-edge tests above
    // pass no climb rate either: the lateral, height and alignment tests still come first.)

    [Theory]
    [InlineData(90.0, "09")]
    [InlineData(270.0, "27")]
    public void Over_the_pavement_an_unknown_climb_rate_is_landing_pending(double heading, string designator)
    {
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Shape(EastWest("09", "27", 50.0)),
            50.0, 0.003, false, heading, 150, climbFpm: null);
        Assert.Equal(RunwayTrafficKind.LandingPending, fix.Kind);
        Assert.Equal(designator, fix.Designator);
        Assert.Equal(0.0, fix.DistanceNm);
    }

    [Fact]
    public void Seen_on_the_ground_moments_ago_an_unknown_climb_is_not_pending()
        => Assert.Equal(RunwayTrafficKind.None, OverPavement(climbFpm: null, recentlyOnGround: true).Kind);

    [Fact]
    public void A_pending_aircraft_is_decided_on_its_next_sample()
    {
        Assert.Equal(RunwayTrafficKind.LandingPending, OverPavement(climbFpm: null).Kind);
        Assert.Equal(RunwayTrafficKind.None, OverPavement(climbFpm: 1500).Kind);     // climbing: a departure
        Assert.Equal(RunwayTrafficKind.Landing, OverPavement(climbFpm: -400).Kind);  // descending: landing
    }

    [Theory]
    [InlineData(30.0, 1, "09R")]   // 30 m from the south runway's centreline, 70 m from the north one's
    [InlineData(70.0, 0, "09L")]   // 70 m from the south runway's centreline, 30 m from the north one's
    public void A_pending_aircraft_is_assigned_to_the_better_fitting_parallel(
        double northOfSouthM, int shapeIndex, string designator)
    {
        // Parallels 100 m apart: each runway's landing band (half-width + 60 m) holds the aircraft, so
        // either alone would call it pending on itself — the R4 best fit must pick one, exactly as it
        // does for a final or a landing (A_close_parallel_arrival_is_assigned_to_its_own_runway).
        var shapes = new[]
        {
            Shape(EastWest("09L", "27R", 50.0 + 100 * DegLatPerMetre)),
            Shape(EastWest("09R", "27L", 50.0)),
        };
        double lat = 50.0 + northOfSouthM * DegLatPerMetre;
        Assert.All(shapes, s => Assert.Equal(RunwayTrafficKind.LandingPending,
            GroundTrafficLogic.ClassifyAgainstRunway(s, lat, 0.003, false, 90, 150).Kind));

        var a = Assert.Single(GroundTrafficLogic.ClassifyAgainstRunways(shapes, lat, 0.003, false, 90, 150));
        Assert.Equal(shapeIndex, a.ShapeIndex);
        Assert.Equal(RunwayTrafficKind.LandingPending, a.Fix.Kind);
        Assert.Equal(designator, a.Fix.Designator);
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
