using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class SurroundingsGeometryTests
{
    // KTIW threshold 17 area; a 100 m x 100 m square apron.
    private static readonly LatLon[] Square =
    {
        new(47.2680, -122.5760), new(47.2680, -122.5747),
        new(47.2689, -122.5747), new(47.2689, -122.5760),
    };

    private static AirportFeature Point(FeatureKind k, double lat, double lon, string name = "X")
        => new() { Kind = k, Name = name, Lat = lat, Lon = lon, Source = FeatureSource.Osm };

    [Fact]
    public void Contains_is_true_inside_and_false_outside()
    {
        Assert.True(SurroundingsGeometry.Contains(Square, 47.26845, -122.57535));
        Assert.False(SurroundingsGeometry.Contains(Square, 47.2700, -122.5800));
    }

    [Fact]
    public void Distance_to_a_footprint_feature_is_zero_inside_and_edge_distance_outside()
    {
        var apron = new AirportFeature
        {
            Kind = FeatureKind.Apron, Name = "Ramp", Lat = 47.26845, Lon = -122.57535,
            Footprint = Square, Source = FeatureSource.Osm
        };
        Assert.Equal(0.0, SurroundingsGeometry.DistanceMetres(47.26845, -122.57535, apron));
        // 47.2680 is the south edge; 0.0009 deg lat ≈ 100 m south of it.
        double d = SurroundingsGeometry.DistanceMetres(47.2671, -122.57535, apron);
        Assert.InRange(d, 95.0, 105.0);
    }

    [Fact]
    public void Distance_to_a_point_feature_is_haversine()
    {
        var f = Point(FeatureKind.Tower, 47.2680, -122.5760);
        double d = SurroundingsGeometry.DistanceMetres(47.2689, -122.5760, f);
        Assert.InRange(d, 95.0, 105.0);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // heading north, target due north → 0
    [InlineData(90.0, -90.0)]   // heading east, target due north → -90 (left)
    [InlineData(270.0, 90.0)]   // heading west, target due north → +90 (right)
    [InlineData(180.0, 180.0)]  // heading south, target north → behind
    public void RelativeBearing_is_signed_and_wrapped(double heading, double expected)
    {
        double rel = SurroundingsGeometry.RelativeBearingDeg(47.0, -122.0, heading, 47.01, -122.0);
        if (Math.Abs(expected) == 180.0) Assert.InRange(Math.Abs(rel), 179.5, 180.5);   // -180 and 180 are the same direction
        else Assert.InRange(rel, expected - 0.5, expected + 0.5);
    }

    [Fact]
    public void Centroid_is_the_vertex_mean()
    {
        var c = SurroundingsGeometry.Centroid(Square);
        Assert.InRange(c.Lat, 47.26844, 47.26846);
        Assert.InRange(c.Lon, -122.57536, -122.57534);
    }

    [Fact]
    public void SpokenName_falls_back_to_the_kind_word()
    {
        Assert.Equal("Hangar", Point(FeatureKind.Hangar, 0, 0, "").SpokenName);
        Assert.Equal("Control tower", Point(FeatureKind.Tower, 0, 0, " ").SpokenName);
        Assert.Equal("Narrows Aviation", Point(FeatureKind.Fbo, 0, 0, "Narrows Aviation").SpokenName);
    }

    private static AirportFeature Poly(params (double lat, double lon)[] ring) => new()
    {
        Kind = FeatureKind.Concourse, Name = "Concourse B", Source = FeatureSource.Osm,
        Lat = ring.Average(p => p.lat), Lon = ring.Average(p => p.lon),
        Footprint = ring.Select(p => new LatLon(p.lat, p.lon)).ToList(),
    };

    [Fact]
    public void Distance_and_bearing_are_taken_to_the_SAME_point_of_a_footprint()
    {
        // A 700 m east-west pier. The aircraft is 60 m south of its wall, 300 m short of its centroid.
        var pier = Poly((0.0010, 0.0000), (0.0010, 0.0063), (0.0014, 0.0063), (0.0014, 0.0000));
        var near = SurroundingsGeometry.Nearest(0.00046, 0.0005, pier);
        Assert.InRange(near.Metres, 55, 65);
        // Due north: the wall beside us, NOT the ~74° to the roof centroid. The nearest point on the
        // wall projects to local x=0 exactly for this geometry (verified independently), so this
        // lands ON the 0/360 seam rather than a few thousandths short of it — accept either
        // representation of due north and reject anything approaching the centroid's ~74°.
        double fromNorth = Math.Min(near.BearingTrueDeg, 360.0 - near.BearingTrueDeg);
        Assert.InRange(fromNorth, 0, 5);
        // Heading east, that is "to the left" — the centroid bearing would have said "ahead".
        Assert.InRange(SurroundingsGeometry.RelativeBearingDeg(near.BearingTrueDeg % 360.0, 90.0), -95, -85);
    }

    [Fact]
    public void Inside_a_footprint_is_zero_metres()
    {
        var pier = Poly((0.0010, 0.0000), (0.0010, 0.0063), (0.0014, 0.0063), (0.0014, 0.0000));
        Assert.Equal(0.0, SurroundingsGeometry.Nearest(0.0012, 0.0030, pier).Metres);
    }

    [Fact]
    public void A_stand_derived_feature_is_as_near_as_its_nearest_member()
    {
        var f = new AirportFeature
        {
            Kind = FeatureKind.Concourse, Name = "Concourse B", Source = FeatureSource.Navdata, Lat = 0.0030, Lon = 0.0,
            Members = new[] { new LatLon(0.0010, 0.0), new LatLon(0.0030, 0.0), new LatLon(0.0050, 0.0) },
        };
        var near = SurroundingsGeometry.Nearest(0.0005, 0.0, f);      // 55 m south of the first gate, 280 m from the centroid
        Assert.InRange(near.Metres, 50, 60);
        Assert.InRange(near.BearingTrueDeg, -0.001, 0.5);
    }

    [Fact]
    public void Single_linkage_follows_a_chain_and_splits_at_a_gap()
    {
        var pts = new[] { new LatLon(0, 0), new LatLon(0, 0.0009), new LatLon(0, 0.0018), new LatLon(0, 0.0200) };   // 100 m, 100 m, then 2 km
        var clusters = SurroundingsGeometry.SingleLinkage(pts, p => p, 150.0);
        Assert.Equal(2, clusters.Count);
        Assert.Equal(3, clusters.Max(c => c.Count));
        Assert.Single(SurroundingsGeometry.SingleLinkage(pts, p => p, 2500.0));
    }

    [Fact]
    public void A_point_is_inside_beyond_the_edge_only_past_the_margin()
    {
        // Equator, where a degree of latitude and of longitude are the same 111,320 m.
        const double M = 111_320.0;
        var square = new[] { new LatLon(0, 0), new LatLon(0, 100 / M), new LatLon(100 / M, 100 / M), new LatLon(100 / M, 0) };
        Assert.True(SurroundingsGeometry.ContainsBeyondEdge(square, 50 / M, 50 / M, 5.0));        // 50 m in
        Assert.False(SurroundingsGeometry.ContainsBeyondEdge(square, 3 / M, 50 / M, 5.0));        // inside, 3 m from the south edge
        Assert.False(SurroundingsGeometry.ContainsBeyondEdge(square, 0, 50 / M, 5.0));            // ON the edge: a ray cast may answer either way
        Assert.False(SurroundingsGeometry.ContainsBeyondEdge(square, 100 / M, 100 / M, 5.0));     // a corner — a node two outlines share
        Assert.False(SurroundingsGeometry.ContainsBeyondEdge(square, -10 / M, 50 / M, 5.0));      // outside
    }
}
