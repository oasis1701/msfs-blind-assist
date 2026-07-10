// Characterization tests for MSFSBlindAssist.Services.TaxiAugment.TaxiGeo.
//
// No dedicated probe exists; cases derived by reading the geometry helpers -- in
// particular the antimeridian-safe WrapDeltaDeg/MidpointLon/PointToSegmentMeters math
// called out in the class's own doc comment as deliberately NOT delegating to
// TaxiGraph's non-antimeridian-safe equirectangular helper -- and confirmed by running
// the tests. This is characterization, not spec verification: if a literal ever
// disagrees with actual output, the test must be corrected to match real output, not
// the other way around.

using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class TaxiGeoTests
{
    [Fact]
    public void HaversineMeters_of_a_zero_length_segment_is_zero()
    {
        Assert.Equal(0.0, TaxiGeo.HaversineMeters(1.0, 1.0, 1.0, 1.0), 6);
    }

    [Fact]
    public void HaversineMeters_one_degree_of_latitude_is_about_111_km()
    {
        double d = TaxiGeo.HaversineMeters(0.0, 0.0, 1.0, 0.0);
        Assert.InRange(d, 110_000, 112_000);
    }

    [Theory]
    [InlineData(0.0, 0.0, 1.0, 0.0, 0.0)]    // due north
    [InlineData(0.0, 0.0, 0.0, 1.0, 90.0)]   // due east
    [InlineData(0.0, 0.0, -1.0, 0.0, 180.0)] // due south
    [InlineData(0.0, 0.0, 0.0, -1.0, 270.0)] // due west
    public void BearingDeg_cardinal_directions(double lat1, double lon1, double lat2, double lon2, double expected)
    {
        Assert.Equal(expected, TaxiGeo.BearingDeg(lat1, lon1, lat2, lon2), 1);
    }

    [Fact]
    public void BearingDeg_is_always_normalized_into_0_360()
    {
        double b = TaxiGeo.BearingDeg(1.0, 1.0, 0.0, -1.0);
        Assert.InRange(b, 0.0, 360.0);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]     // identical bearings
    [InlineData(0.0, 90.0, 90.0)]   // perpendicular
    [InlineData(0.0, 180.0, 0.0)]   // opposite headings on an undirected line == same line
    [InlineData(10.0, 200.0, 10.0)] // 190 deg apart -> undirected diff is 10
    public void BearingDiffMod180_treats_the_line_as_undirected(double a, double b, double expected)
    {
        Assert.Equal(expected, TaxiGeo.BearingDiffMod180(a, b), 6);
    }

    [Theory]
    [InlineData(190.0, -170.0)]
    [InlineData(-190.0, 170.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(0.0, 0.0)]
    public void WrapDeltaDeg_wraps_into_minus180_to_180(double raw, double expected)
    {
        Assert.Equal(expected, TaxiGeo.WrapDeltaDeg(raw), 6);
    }

    [Fact]
    public void MidpointLon_of_two_nearby_points_is_the_simple_average()
    {
        Assert.Equal(0.0005, TaxiGeo.MidpointLon(0.0, 0.001), 6);
    }

    [Fact]
    public void MidpointLon_across_the_antimeridian_is_180_not_0()
    {
        // Midpoint of 179.9 and -179.9 must be ±180 (the short way across the dateline),
        // never the naive arithmetic average of 0.0.
        double m = TaxiGeo.MidpointLon(179.9, -179.9);
        Assert.True(Math.Abs(Math.Abs(m) - 180.0) < 1e-6, $"expected +-180, got {m}");
    }

    [Fact]
    public void PointToSegmentMeters_of_a_point_on_the_segment_is_zero()
    {
        double d = TaxiGeo.PointToSegmentMeters(0.0, 0.0005, 0.0, 0.0, 0.0, 0.001);
        Assert.True(d < 0.01, $"expected ~0, got {d}");
    }

    [Fact]
    public void PointToSegmentMeters_clamps_to_the_nearest_endpoint_beyond_the_segment()
    {
        // Point is well past the segment's B endpoint along the same line -- distance must
        // equal the distance to B, not a projection past the segment.
        double toEndpoint = TaxiGeo.HaversineMeters(0.0, 0.002, 0.0, 0.001);
        double d = TaxiGeo.PointToSegmentMeters(0.0, 0.002, 0.0, 0.0, 0.0, 0.001);
        Assert.True(Math.Abs(d - toEndpoint) < 1.0, $"expected ~{toEndpoint}, got {d}");
    }

    [Fact]
    public void PointToSegmentMeters_handles_a_degenerate_zero_length_segment()
    {
        // len2 <= 1e-9 guard: a and b coincide -> t forced to 0, distance is just point-to-a.
        double d = TaxiGeo.PointToSegmentMeters(0.001, 0.001, 0.0, 0.0, 0.0, 0.0);
        double direct = TaxiGeo.HaversineMeters(0.001, 0.001, 0.0, 0.0);
        Assert.True(Math.Abs(d - direct) < 1.0, $"expected ~{direct}, got {d}");
    }

    [Fact]
    public void PointToSegmentMeters_near_the_antimeridian_does_not_blow_up()
    {
        // A segment spanning 179.999 -> -179.999 (crossing the dateline) with a raw-subtraction
        // projection would compute a ~360 deg delta and produce a huge bogus distance. The
        // wrapped math must keep this small.
        double d = TaxiGeo.PointToSegmentMeters(0.0, 180.0, 0.0, 179.999, 0.0, -179.999);
        Assert.True(d < 500.0, $"expected a small distance near the dateline, got {d}");
    }
}
