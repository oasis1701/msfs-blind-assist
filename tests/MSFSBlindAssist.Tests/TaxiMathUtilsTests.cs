// Characterization tests for the pure static helpers in
// MSFSBlindAssist.Services.TaxiGuidanceManager.MathUtils.cs: NormalizeAngle,
// RunwayDesignatorsMatch, PerpendicularDistanceToSegmentMeters,
// ComputeTurnVerbalFromHeading, SignedAlongRunwayMeters, AbsLateralFromRunwayMeters.
//
// This is characterization, not spec verification: values are hand-derived from
// reading the source and confirmed by running the tests; if a literal ever
// disagrees with actual output, the test must be corrected to match real output,
// not the other way around. Members under test were promoted private -> internal
// (static, no other signature change) to make them reachable from this project;
// see Properties/InternalsVisibleTo.cs.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TaxiMathUtilsTests
{
    // --- NormalizeAngle ------------------------------------------------------
    // Contract read from source: `while (angle > 180) angle -= 360; while (angle
    // < -180) angle += 360;` -> the result range is [-180, 180], with BOTH
    // endpoints reachable (180 stays 180, since 180 is not > 180; -180 stays
    // -180, since -180 is not < -180).

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(-90, -90)]
    [InlineData(180, 180)]     // boundary: not > 180, so no wrap
    [InlineData(-180, -180)]   // boundary: not < -180, so no wrap
    [InlineData(200, -160)]    // wraps down past +180
    [InlineData(-200, 160)]    // wraps up past -180
    [InlineData(360, 0)]       // full turn collapses to 0
    [InlineData(540, 180)]     // 540 -> 180 in a single while-iteration
    [InlineData(900, 180)]     // 900 needs two iterations (900->540->180)
    [InlineData(-900, -180)]   // symmetric negative multi-wrap
    public void NormalizeAngle_wraps_into_the_closed_range_minus180_to_180(double input, double expected)
    {
        Assert.Equal(expected, TaxiGuidanceManager.NormalizeAngle(input), 9);
    }

    // --- RunwayDesignatorsMatch ------------------------------------------------
    // Contract read from source: `tagged` is first run through
    // RouteRunwayCrossings.ExtractRunwayDesignator (looks for a "runway NN[LRCW]"
    // token); if that finds nothing, it falls back to stripping the literal word
    // "runway" and normalizing what's left. `target` is normalized directly. The
    // two normalized designators match if EQUAL, or if `a`'s RECIPROCAL (+18,
    // L<->R swap) equals `b` -- i.e. by design "09" is considered to match "27"
    // (opposite ends of the same physical pavement), not just padding variants
    // of the same end. See the XML doc on RunwayDesignatorsMatch and
    // RouteRunwayCrossings.Reciprocal.

    [Fact]
    public void RunwayDesignatorsMatch_treats_unpadded_and_padded_forms_as_equal()
    {
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch("09", "9"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_treats_unpadded_and_padded_forms_with_suffix_as_equal()
    {
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch("09L", "9L"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_matches_an_exact_designator()
    {
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch("28R", "28R"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_extracts_the_designator_from_a_runway_prefixed_label()
    {
        // Exercises the ExtractRunwayDesignator branch (label carries more than
        // a bare "runway " prefix), not the Replace-based fallback.
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch("runway 09L at A5", "09L"));
    }

    // SURPRISE (recorded, not "fixed"): the task brief assumed reciprocals
    // ("09" vs "27") must NOT match. Reading the source shows the opposite is
    // true BY DESIGN -- the XML doc explicitly says "09" matches "27" because a
    // hold-short label for either end names the same physical crossing. Pinning
    // the actual (true) result here per the characterization methodology.
    [Fact]
    public void RunwayDesignatorsMatch_matches_the_reciprocal_designator_by_design()
    {
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch("09", "27"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_reciprocal_match_also_swaps_the_LR_suffix()
    {
        // "09L" and "27R" are opposite ends of the SAME strip (L end vs R end
        // flip when you add 18), so this is also a true match.
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch("09L", "27R"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_rejects_a_parallel_runway_with_mismatched_suffix()
    {
        // "09L" and "09R" are two DIFFERENT parallel strips, not reciprocal ends
        // of one strip -- must not match.
        Assert.False(TaxiGuidanceManager.RunwayDesignatorsMatch("09L", "09R"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_rejects_reciprocal_number_with_wrong_suffix_pairing()
    {
        // Reciprocal("09L") is "27R", not "27L" -- a same-suffix reciprocal
        // guess must not match.
        Assert.False(TaxiGuidanceManager.RunwayDesignatorsMatch("09L", "27L"));
    }

    [Fact]
    public void RunwayDesignatorsMatch_rejects_an_unrelated_runway()
    {
        Assert.False(TaxiGuidanceManager.RunwayDesignatorsMatch("09", "18"));
    }

    // --- PerpendicularDistanceToSegmentMeters -----------------------------------
    // Hand-computable geometry: segment on the equator (lat0 = 0) from
    // (0,0) to (0, 1000m-east), using the function's OWN internal constant
    // (METERS_PER_DEG_LAT = 111132.0) for both axes -- at the equator
    // cos(latMidRad) ~= 1, so metersPerDegLon ~= 111132.0 too, matching the
    // constant used to synthesize each point's lon/lat offsets. This makes the
    // degrees-to-meters round trip cancel almost exactly (residual is a
    // sub-millimetre artifact of the tiny non-zero latMidRad in the real cos()
    // call), so a tolerance of 1mm (3 decimal places) is generous but safe.

    private const double MpdEquator = 111132.0;

    private static (double lat, double lon) EquatorPoint(double eastMeters, double northMeters)
        => (northMeters / MpdEquator, eastMeters / MpdEquator);

    [Fact]
    public void PerpendicularDistanceToSegmentMeters_on_segment_projects_perpendicular_distance()
    {
        var a = EquatorPoint(0, 0);
        var b = EquatorPoint(1000, 0);
        var p = EquatorPoint(500, 500); // 500 m east (mid-segment), 500 m north (off to the side)

        double dist = TaxiGuidanceManager.PerpendicularDistanceToSegmentMeters(
            p.lat, p.lon, a.lat, a.lon, b.lat, b.lon);

        Assert.Equal(500.0, dist, 3);
    }

    [Fact]
    public void PerpendicularDistanceToSegmentMeters_clamps_to_the_start_endpoint_beyond_it()
    {
        var a = EquatorPoint(0, 0);
        var b = EquatorPoint(1000, 0);
        var p = EquatorPoint(-500, 300); // 500 m WEST of a (before the segment start), 300 m north

        double dist = TaxiGuidanceManager.PerpendicularDistanceToSegmentMeters(
            p.lat, p.lon, a.lat, a.lon, b.lat, b.lon);

        // Clamped t=0 -> straight-line distance to endpoint a: sqrt(500^2+300^2).
        Assert.Equal(Math.Sqrt(500.0 * 500.0 + 300.0 * 300.0), dist, 3);
    }

    [Fact]
    public void PerpendicularDistanceToSegmentMeters_clamps_to_the_end_endpoint_beyond_it()
    {
        var a = EquatorPoint(0, 0);
        var b = EquatorPoint(1000, 0);
        var p = EquatorPoint(1200, 100); // 200 m EAST of b (past the segment end), 100 m north

        double dist = TaxiGuidanceManager.PerpendicularDistanceToSegmentMeters(
            p.lat, p.lon, a.lat, a.lon, b.lat, b.lon);

        // Clamped t=1 -> straight-line distance to endpoint b: sqrt(200^2+100^2).
        Assert.Equal(Math.Sqrt(200.0 * 200.0 + 100.0 * 100.0), dist, 3);
    }

    // --- ComputeTurnVerbalFromHeading ---------------------------------------
    // Contract read from source: turn = NormalizeAngle(targetBearing - heading);
    // |turn| < 20 -> "continue"; 20 <= |turn| < 60 -> "slight left"/"slight
    // right"; |turn| >= 60 -> "left"/"right". turn < 0 => left, turn > 0 =>
    // right (turn is target-bearing-relative-to-current-heading, so a positive
    // turn means the target is clockwise of current heading = a right turn).

    [Fact]
    public void ComputeTurnVerbalFromHeading_says_continue_under_20_degrees()
    {
        Assert.Equal("continue", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(100, 90));
    }

    [Fact]
    public void ComputeTurnVerbalFromHeading_says_slight_right_at_30_degrees()
    {
        Assert.Equal("slight right", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(120, 90));
    }

    [Fact]
    public void ComputeTurnVerbalFromHeading_says_slight_left_at_minus_30_degrees()
    {
        Assert.Equal("slight left", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(60, 90));
    }

    [Fact]
    public void ComputeTurnVerbalFromHeading_says_right_at_90_degrees()
    {
        Assert.Equal("right", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(180, 90));
    }

    [Fact]
    public void ComputeTurnVerbalFromHeading_says_left_at_minus_90_degrees()
    {
        Assert.Equal("left", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(0, 90));
    }

    // SURPRISE (recorded, not "fixed"): the brief's own narrative example
    // ("heading 350 turning to track 010 says right") assumed the full "right"
    // bucket. Actual math: turn = NormalizeAngle(10 - 350) = NormalizeAngle(-340)
    // = 20 (exactly the slight-turn threshold, which is INCLUSIVE per `< 60`),
    // so the real result is "slight right", not "right". Pinning the true output.
    [Fact]
    public void ComputeTurnVerbalFromHeading_at_the_20_degree_boundary_is_slight_not_full()
    {
        Assert.Equal("slight right", TaxiGuidanceManager.ComputeTurnVerbalFromHeading(10, 350));
    }

    // CLAUDE.md invariant: verbal turn direction must come from the aircraft's
    // CURRENT heading, never the route's static (on-axis-assumed) TurnDirection
    // -- because off-axis, the real turn can be the OPPOSITE sign from what a
    // naive "next segment bearing minus current segment bearing" calc would say.
    //
    // Constructed off-axis case: the route's current segment is nominally
    // bearing 170 deg, turning onto a segment bearing 200 deg. A static calc
    // (as if the aircraft were exactly on-axis at 170) would say:
    //   200 - 170 = +30 deg -> "slight right".
    // But the aircraft is actually way off that axis, heading 350 deg (e.g.
    // it swung wide through the turn). From its REAL current heading:
    //   turn = NormalizeAngle(200 - 350) = NormalizeAngle(-150) = -150 deg
    //   |turn| = 150 >= 60 -> "left".
    // The heading-based result ("left") is the OPPOSITE direction from the
    // static route-direction guess ("slight right") -- exactly the scenario
    // CLAUDE.md's invariant exists to get right: the tone (heading-driven)
    // and the verbal callout must agree, so the verbal must be computed the
    // same way the tone is, not from the route's static intent.
    [Fact]
    public void ComputeTurnVerbalFromHeading_off_axis_can_flip_the_direction_a_static_cue_would_give()
    {
        const double nominalOnAxisSegmentBearing = 170.0; // what a static calc would assume
        const double nextSegmentBearing = 200.0;
        const double actualAircraftHeading = 350.0;       // far off the nominal axis

        double staticTurn = nextSegmentBearing - nominalOnAxisSegmentBearing;
        Assert.Equal(30.0, staticTurn); // static calc: "slight right"

        string actual = TaxiGuidanceManager.ComputeTurnVerbalFromHeading(
            nextSegmentBearing, actualAircraftHeading);

        Assert.Equal("left", actual); // opposite side from the static guess
    }

    // --- SignedAlongRunwayMeters / AbsLateralFromRunwayMeters -----------------
    // Synthetic north-facing runway (runwayHeadingTrueDeg = 0, i.e. the
    // direction of flight/travel is due north), ref point at the equator
    // (0,0) so the same MpdEquator constant applies cleanly.
    //
    // SignedAlongRunwayMeters sign convention (per its XML doc, confirmed by
    // the source: dE*sin(hdg) + dN*cos(hdg), and at hdg=0 that's just dN):
    // POSITIVE = point lies NORTH of ref = "past ref in the direction of
    // flight" (ahead); NEGATIVE = point lies SOUTH of ref = "still upfield of
    // ref" (behind).
    //
    // AbsLateralFromRunwayMeters returns an ABSOLUTE value (dE*cos(hdg) -
    // dN*sin(hdg), then Math.Abs) -- at hdg=0 that's |dE|. It carries NO
    // left/right sign at all despite describing a "side" offset: an east
    // offset and a west offset of the same magnitude produce the SAME
    // number. This is the actual contract, not a bug -- callers needing a
    // side must derive it themselves (this helper only reports magnitude).

    [Fact]
    public void SignedAlongRunwayMeters_north_of_ref_is_positive_ahead_on_a_north_facing_runway()
    {
        var p = EquatorPoint(200, 1000); // 200 m east, 1000 m north of ref
        double along = TaxiGuidanceManager.SignedAlongRunwayMeters(p.lat, p.lon, 0, 0, 0);
        Assert.Equal(1000.0, along, 2);
    }

    [Fact]
    public void SignedAlongRunwayMeters_south_of_ref_is_negative_upfield_on_a_north_facing_runway()
    {
        var p = EquatorPoint(-200, -1000); // 200 m west, 1000 m south of ref
        double along = TaxiGuidanceManager.SignedAlongRunwayMeters(p.lat, p.lon, 0, 0, 0);
        Assert.Equal(-1000.0, along, 2);
    }

    [Fact]
    public void AbsLateralFromRunwayMeters_east_offset_on_a_north_facing_runway()
    {
        var p = EquatorPoint(200, 1000);
        double lat = TaxiGuidanceManager.AbsLateralFromRunwayMeters(p.lat, p.lon, 0, 0, 0);
        Assert.Equal(200.0, lat, 2);
    }

    [Fact]
    public void AbsLateralFromRunwayMeters_is_sign_agnostic_between_east_and_west_offsets()
    {
        var east = EquatorPoint(200, 1000);
        var west = EquatorPoint(-200, -1000);

        double latEast = TaxiGuidanceManager.AbsLateralFromRunwayMeters(east.lat, east.lon, 0, 0, 0);
        double latWest = TaxiGuidanceManager.AbsLateralFromRunwayMeters(west.lat, west.lon, 0, 0, 0);

        Assert.Equal(200.0, latEast, 2);
        Assert.Equal(200.0, latWest, 2); // same magnitude as the east case -- no side encoded
    }
}
