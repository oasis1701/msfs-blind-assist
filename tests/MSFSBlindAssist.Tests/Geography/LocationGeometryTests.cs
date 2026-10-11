using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Tests.Geography;

public class LocationGeometryTests
{
    // A 2°×2° square around (0, 0) with a 1°×1° hole in the middle, GeoJSON order [lon, lat].
    private static readonly double[] Outer = { -1, -1, 1, -1, 1, 1, -1, 1, -1, -1 };
    private static readonly double[] Hole = { -0.5, -0.5, 0.5, -0.5, 0.5, 0.5, -0.5, 0.5, -0.5, -0.5 };

    [Fact]
    public void Inside_the_outer_ring_and_outside_the_hole_is_inside()
    {
        var rings = new[] { Outer, Hole };
        Assert.True(LocationGeometry.InPolygon(rings, 0.8, 0.8));
        Assert.False(LocationGeometry.InPolygon(rings, 0, 0));
        Assert.False(LocationGeometry.InPolygon(rings, 2, 2));
    }

    [Fact]
    public void A_polygon_touching_the_antimeridian_contains_points_next_to_it()
    {
        // Natural Earth splits shapes at ±180°: this is the eastern half of a sea.
        double[] eastHalf = { 170, 50, 180, 50, 180, 60, 170, 60, 170, 50 };
        Assert.True(LocationGeometry.InPolygon(new[] { eastHalf }, 55, 179.99));
        Assert.False(LocationGeometry.InPolygon(new[] { eastHalf }, 55, -179.99));
    }

    [Fact]
    public void A_polygon_spanning_the_whole_globe_is_not_folded()
    {
        double[] band = { -180, 70, 180, 70, 180, 90, -180, 90, -180, 70 }; // an Arctic cap
        Assert.True(LocationGeometry.InPolygon(new[] { band }, 80, 0));
        Assert.True(LocationGeometry.InPolygon(new[] { band }, 80, 179));
        Assert.False(LocationGeometry.InPolygon(new[] { band }, 60, 0));
    }

    [Fact]
    public void Nearest_point_on_an_open_line_ignores_the_closing_segment()
    {
        // An open L: (0,0) → (0,1) → (1,1). Closed, it would add the segment back to (0,0).
        double[] line = { 0, 0, 0, 1, 1, 1 };
        var open = LocationGeometry.NearestOnLine(line, closed: false, lat: 0.1, lon: 0.6);
        var closed = LocationGeometry.NearestOnLine(line, closed: true, lat: 0.1, lon: 0.6);
        Assert.True(open.DistanceNm > closed.DistanceNm);
        Assert.Equal(0.1, open.Lat, 2);   // nearest on the line is the vertical leg at lon 0
        Assert.Equal(0.0, open.Lon, 2);
    }

    [Fact]
    public void Nearest_point_search_wraps_across_the_antimeridian()
    {
        double[] coast = { -179.9, 10, -179.9, 11 };
        var hit = LocationGeometry.NearestOnLine(coast, closed: false, lat: 10.5, lon: 179.9);
        Assert.InRange(hit.DistanceNm, 11, 13); // 0.2° of longitude at 10.5°N ≈ 11.8 nm, not 21,000
        Assert.Equal(-179.9, hit.Lon, 3);
    }

    [Fact]
    public void Great_circle_and_bearing_come_from_the_shared_navigation_math()
    {
        Assert.Equal(60.0, LocationGeometry.GreatCircleNm(0, 0, 1, 0), 0);
        Assert.Equal(90.0, LocationGeometry.BearingTrue(0, 0, 0, 1), 3);
    }

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-190, 170)]
    [InlineData(180, 180)]
    [InlineData(-180, 180)]
    public void WrapDelta_folds_into_minus_180_exclusive_to_180(double input, double expected)
    {
        Assert.Equal(expected, LocationGeometry.WrapDelta(input), 9);
    }
}
