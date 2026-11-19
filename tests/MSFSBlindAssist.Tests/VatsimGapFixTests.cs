using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the pure logic added by the VATSIM instruction-gap fixes (2026-08-31):
/// the runway-as-taxiway leg detector (P3) and the LAHSO hold-point geometry (P5).
/// </summary>
public class VatsimGapFixTests
{
    // ── P3: MatchRunwayDesignator ───────────────────────────────────────────

    private static readonly string[] RunwayIds = { "07L", "25R", "16", "34" };

    [Theory]
    [InlineData("25R", "25R")]      // bare designator
    [InlineData("25r", "25R")]      // case-insensitive
    [InlineData("07L", "07L")]      // padded zero as stored
    [InlineData("7L", "07L")]       // unpadded matches padded (CleanRunway rule)
    [InlineData("RWY 16", "16")]    // prefixed
    [InlineData("RUNWAY 34", "34")]
    [InlineData("RW34", "34")]
    public void MatchRunwayDesignator_matches(string leg, string expected)
    {
        Assert.Equal(expected, TaxiGuidanceManager.MatchRunwayDesignator(leg, RunwayIds));
    }

    [Theory]
    [InlineData("A")]        // plain taxiway letter
    [InlineData("B10")]      // letter+digits taxiway — NOT digit-first, never a designator
    [InlineData("25L")]      // designator shape but not a runway at this airport
    [InlineData("161")]      // three digits — not a designator
    [InlineData("16X")]      // invalid side letter
    [InlineData("")]
    [InlineData("  ")]
    public void MatchRunwayDesignator_rejects(string leg)
    {
        Assert.Null(TaxiGuidanceManager.MatchRunwayDesignator(leg, RunwayIds));
    }

    // ── P5: LahsoHold.Compute ───────────────────────────────────────────────

    // Synthetic airport at 0°N: landing runway 36 runs due north 3000 m;
    // crossing runway 09/27 runs due east through a point 2000 m down the
    // landing run. 1° lat ≈ 111,132 m.
    private const double M_PER_DEG = 111132.0;

    private static Runway NorthRunway(double thresholdOffsetFt = 0) => new()
    {
        RunwayID = "36",
        StartLat = 0, StartLon = 0,
        EndLat = 3000.0 / M_PER_DEG, EndLon = 0,
        ThresholdOffset = thresholdOffsetFt
    };

    private static Runway CrossingRunway() => new()
    {
        RunwayID = "09",
        StartLat = 2000.0 / M_PER_DEG, StartLon = -1000.0 / M_PER_DEG,
        EndLat = 2000.0 / M_PER_DEG, EndLon = 1000.0 / M_PER_DEG
    };

    [Fact]
    public void Lahso_intersecting_runways_hold_point_short_of_crossing()
    {
        var hold = LahsoHold.Compute(NorthRunway(), CrossingRunway());
        Assert.NotNull(hold);
        // Intersection is 2000 m (6561.7 ft) down the run; hold is 250 ft short.
        Assert.Equal(6561.7 - 250.0, hold!.StopFromThresholdFeet, 0);
        Assert.Equal("09", hold.CrossingRunwayId);
        // Hold point sits ON the landing centerline (lon 0), short of the crossing.
        Assert.Equal(0.0, hold.Longitude, 6);
        Assert.True(hold.Latitude < 2000.0 / M_PER_DEG);
        Assert.True(hold.Latitude > 1900.0 / M_PER_DEG);
    }

    [Fact]
    public void Lahso_displaced_threshold_shortens_the_available_distance()
    {
        // 1000 ft displaced threshold: same physical intersection, but the
        // distance is measured from the painted threshold — the same anchor
        // GetLandingExits uses for DistanceFromThresholdFeet.
        var hold = LahsoHold.Compute(NorthRunway(thresholdOffsetFt: 1000), CrossingRunway());
        Assert.NotNull(hold);
        Assert.Equal(6561.7 - 250.0 - 1000.0, hold!.StopFromThresholdFeet, 0);
    }

    [Fact]
    public void Lahso_parallel_runway_yields_null()
    {
        var parallel = new Runway
        {
            RunwayID = "36R",
            StartLat = 0, StartLon = 500.0 / M_PER_DEG,
            EndLat = 3000.0 / M_PER_DEG, EndLon = 500.0 / M_PER_DEG
        };
        Assert.Null(LahsoHold.Compute(NorthRunway(), parallel));
    }

    [Fact]
    public void Lahso_crossing_beyond_pavement_yields_null()
    {
        // Crossing runway's extended centerline would intersect, but its
        // pavement ends 200 m short of the landing centerline.
        var offside = new Runway
        {
            RunwayID = "09",
            StartLat = 2000.0 / M_PER_DEG, StartLon = -1000.0 / M_PER_DEG,
            EndLat = 2000.0 / M_PER_DEG, EndLon = -200.0 / M_PER_DEG
        };
        Assert.Null(LahsoHold.Compute(NorthRunway(), offside));
    }

    [Fact]
    public void Lahso_hold_behind_threshold_yields_null()
    {
        // Crossing 50 m past the threshold: the 250 ft setback puts the hold
        // behind the threshold — unusable, refused.
        var nearThreshold = new Runway
        {
            RunwayID = "09",
            StartLat = 50.0 / M_PER_DEG, StartLon = -1000.0 / M_PER_DEG,
            EndLat = 50.0 / M_PER_DEG, EndLon = 1000.0 / M_PER_DEG
        };
        Assert.Null(LahsoHold.Compute(NorthRunway(), nearThreshold));
    }

    [Fact]
    public void Lahso_same_runway_yields_null()
    {
        Assert.Null(LahsoHold.Compute(NorthRunway(), NorthRunway()));
    }
}
