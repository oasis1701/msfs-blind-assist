// Characterization tests for MSFSBlindAssist.Navigation.RunwayFrame.
//
// This is characterization, not spec verification: values are derived by hand from
// the source formula and confirmed by running the tests; if a literal ever disagrees
// with actual output, the test must be corrected to match real output, not the other
// way around.
//
// RunwayFrame's own doc comment states the convention:
//   SignedCrossTrack positive = LEFT side looking down the runway heading,
//   negative = RIGHT side. Along increases down the runway heading from the
//   start threshold.
// This convention has historically been a bug-magnet (+left/-right sign inversions),
// so every assertion below states in a comment which physical side/direction the
// sign means, not just the number.
//
// All runways are synthesized at the equator (lat 0, lon 0 start) so that
// 1 degree of latitude AND 1 degree of longitude both equal DEG_TO_M_LAT
// (111320.0 m/deg) -- this lets test offsets be computed by hand as
// offsetMetres / 111320.0 degrees, with the two axes interchangeable at refLat=0.
// Tolerance is 1.0 m against 100 m offsets (equirectangular projection error at
// this scale is negligible; 1.0 m keeps the assertions honest without being brittle).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayFrameTests
{
    private const double MPD = 111320.0; // must match RunwayFrame's internal DEG_TO_M_LAT
    private const double Tol = 1.0;

    private static Runway MakeRunway(double headingDeg, double lengthFt = 6000.0,
        double startLat = 0.0, double startLon = 0.0,
        double? endLat = null, double? endLon = null)
    {
        return new Runway
        {
            Heading = headingDeg,
            Length = lengthFt,
            StartLat = startLat,
            StartLon = startLon,
            EndLat = endLat ?? startLat,
            EndLon = endLon ?? startLon,
        };
    }

    // --- North-facing runway (heading 0) -------------------------------------

    [Fact]
    public void SignedCrossTrack_point_west_of_centerline_is_positive_left_when_facing_north()
    {
        var frame = RunwayFrame.For(MakeRunway(0.0), refLat: 0.0);

        // West of a north-facing runway is the pilot's LEFT hand side.
        double cross = frame.SignedCrossTrack(0.0, -100.0 / MPD);

        Assert.Equal(100.0, cross, Tol); // positive = LEFT (west) facing north
    }

    [Fact]
    public void SignedCrossTrack_point_east_of_centerline_is_negative_right_when_facing_north()
    {
        var frame = RunwayFrame.For(MakeRunway(0.0), refLat: 0.0);

        // East of a north-facing runway is the pilot's RIGHT hand side.
        double cross = frame.SignedCrossTrack(0.0, 100.0 / MPD);

        Assert.Equal(-100.0, cross, Tol); // negative = RIGHT (east) facing north
    }

    [Fact]
    public void SignedCrossTrack_point_exactly_on_centerline_is_zero()
    {
        var frame = RunwayFrame.For(MakeRunway(0.0), refLat: 0.0);

        // Same longitude as the start threshold, some distance ahead -> on the centerline.
        double cross = frame.SignedCrossTrack(500.0 / MPD, 0.0);

        Assert.Equal(0.0, cross, 1e-6);
    }

    [Fact]
    public void Along_point_ahead_of_threshold_is_positive_when_facing_north()
    {
        var frame = RunwayFrame.For(MakeRunway(0.0), refLat: 0.0);

        // North of the start threshold is DOWN the runway heading (ahead) when facing north.
        double along = frame.Along(100.0 / MPD, 0.0);

        Assert.Equal(100.0, along, Tol); // positive = ahead of / past the start threshold
    }

    [Fact]
    public void Along_point_behind_threshold_is_negative_when_facing_north()
    {
        var frame = RunwayFrame.For(MakeRunway(0.0), refLat: 0.0);

        // South of the start threshold is BEHIND it (before the runway begins) facing north.
        double along = frame.Along(-100.0 / MPD, 0.0);

        Assert.Equal(-100.0, along, Tol); // negative = behind the start threshold
    }

    // --- Reciprocal heading (south-facing, 180) -- signs rotate with the frame ---

    [Fact]
    public void SignedCrossTrack_sign_flips_side_on_the_reciprocal_heading()
    {
        var frame = RunwayFrame.For(MakeRunway(180.0), refLat: 0.0);

        // Facing SOUTH (reciprocal of the north case above), EAST is now the pilot's LEFT
        // hand side -- the opposite physical side from the north-facing case, even though
        // the point itself is still east of the shared start threshold.
        double cross = frame.SignedCrossTrack(0.0, 100.0 / MPD);

        Assert.Equal(100.0, cross, Tol); // positive = LEFT (east) facing south
    }

    [Fact]
    public void SignedCrossTrack_west_is_right_on_the_reciprocal_heading()
    {
        var frame = RunwayFrame.For(MakeRunway(180.0), refLat: 0.0);

        double cross = frame.SignedCrossTrack(0.0, -100.0 / MPD);

        Assert.Equal(-100.0, cross, Tol); // negative = RIGHT (west) facing south
    }

    [Fact]
    public void Along_direction_flips_with_heading_on_the_reciprocal_runway()
    {
        var frame = RunwayFrame.For(MakeRunway(180.0), refLat: 0.0);

        // Facing south, "ahead" of the start threshold means DECREASING latitude (south),
        // the opposite geographic direction from the north-facing case even though both
        // frames share the same start-threshold coordinate.
        double alongSouthOfStart = frame.Along(-100.0 / MPD, 0.0);
        double alongNorthOfStart = frame.Along(100.0 / MPD, 0.0);

        Assert.Equal(100.0, alongSouthOfStart, Tol);  // positive = ahead, facing south = geographic south
        Assert.Equal(-100.0, alongNorthOfStart, Tol);  // negative = behind, facing south = geographic north
    }

    // --- East-facing runway (heading 90) -- catches axis-swap errors -------------

    [Fact]
    public void SignedCrossTrack_north_is_left_when_facing_east()
    {
        var frame = RunwayFrame.For(MakeRunway(90.0), refLat: 0.0);

        // Facing EAST, north is the pilot's LEFT hand side -- cross-track must now be
        // driven by the LATITUDE delta, not longitude (axis-swap check vs. the north case).
        double cross = frame.SignedCrossTrack(100.0 / MPD, 0.0);

        Assert.Equal(100.0, cross, Tol); // positive = LEFT (north) facing east
    }

    [Fact]
    public void SignedCrossTrack_south_is_right_when_facing_east()
    {
        var frame = RunwayFrame.For(MakeRunway(90.0), refLat: 0.0);

        double cross = frame.SignedCrossTrack(-100.0 / MPD, 0.0);

        Assert.Equal(-100.0, cross, Tol); // negative = RIGHT (south) facing east
    }

    [Fact]
    public void Along_east_of_threshold_is_positive_when_facing_east()
    {
        var frame = RunwayFrame.For(MakeRunway(90.0), refLat: 0.0);

        // Facing east, "ahead" is driven by the LONGITUDE delta (axis-swap check
        // vs. the north-facing Along tests, which are driven by latitude).
        double along = frame.Along(0.0, 100.0 / MPD);

        Assert.Equal(100.0, along, Tol); // positive = ahead, facing east = geographic east
    }

    [Fact]
    public void SignedCrossTrack_on_centerline_facing_east_is_zero()
    {
        var frame = RunwayFrame.For(MakeRunway(90.0), refLat: 0.0);

        // Same latitude as the start threshold, some distance ahead (east) -> on centerline.
        double cross = frame.SignedCrossTrack(0.0, 500.0 / MPD);

        Assert.Equal(0.0, cross, 1e-6);
    }

    // --- ThresholdOffset (displaced threshold) is NOT modeled by RunwayFrame -----
    // RunwayFrame.For only reads Heading/Length/StartLat/StartLon/EndLat/EndLon --
    // Runway.ThresholdOffset is never consulted. This is a genuine surprise worth
    // pinning: the frame's "start" is whatever StartLat/StartLon the caller supplies,
    // unadjusted for a displaced threshold. There is no separate displaced-threshold
    // case to cover because the type simply does not model one.

    [Fact]
    public void ThresholdOffset_field_is_ignored_by_For()
    {
        var noOffset = MakeRunway(0.0);
        noOffset.ThresholdOffset = 0.0;
        var withOffset = MakeRunway(0.0);
        withOffset.ThresholdOffset = 1000.0; // large displaced-threshold value

        var frameNoOffset = RunwayFrame.For(noOffset, refLat: 0.0);
        var frameWithOffset = RunwayFrame.For(withOffset, refLat: 0.0);

        double lat = 50.0 / MPD, lon = 25.0 / MPD;
        Assert.Equal(frameNoOffset.SignedCrossTrack(lat, lon), frameWithOffset.SignedCrossTrack(lat, lon), 1e-9);
        Assert.Equal(frameNoOffset.Along(lat, lon), frameWithOffset.Along(lat, lon), 1e-9);
    }

    // --- LengthM ---------------------------------------------------------------

    [Fact]
    public void LengthM_converts_feet_to_metres_when_length_is_positive()
    {
        var frame = RunwayFrame.For(MakeRunway(0.0, lengthFt: 6000.0), refLat: 0.0);

        Assert.Equal(6000.0 * 0.3048, frame.LengthM, 1e-9);
    }

    [Fact]
    public void LengthM_falls_back_to_threshold_to_threshold_distance_when_length_is_zero()
    {
        double endLat = 1000.0 / MPD; // ~1000 m due north of the start threshold
        var runway = MakeRunway(0.0, lengthFt: 0.0, endLat: endLat, endLon: 0.0);

        var frame = RunwayFrame.For(runway, refLat: 0.0);

        // Fallback is TaxiGraph.CalculateDistanceMeters (great-circle NM*1852), which is a
        // slightly different metres-per-degree constant than this test's own equirectangular
        // MPD offset -- so the expected value is computed via that same function rather than
        // the literal 1000.0, per characterization methodology (source of truth is real output).
        double expected = TaxiGraph.CalculateDistanceMeters(0.0, 0.0, endLat, 0.0);
        Assert.Equal(expected, frame.LengthM, 1e-9);
        Assert.Equal(1000.0, frame.LengthM, 2.0); // sanity: still ~1000 m as constructed
    }
}
