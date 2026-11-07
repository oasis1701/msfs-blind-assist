// Characterization tests for FenixChannelRouteGeometry, the sphere sums D and Shift+D make along the Fenix's route
// (Services/FenixChannelRouteGeometry.cs): the app's own great-circle distance, a point's along-track and cross-track
// distances on a leg, and a turn piece's arc. Each figure is within 0.01 NM.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelRouteGeometryTests
{
    private const double Within = 0.01;

    private static FenixLatLon At(double lat, double lon) => new(lat, lon);

    [Fact]
    public void Distances_follow_the_app_s_formula()
    {
        Assert.Equal(60.04, FenixChannelRouteGeometry.DistanceNm(At(0, 0), At(0, 1)), Within);
        Assert.Equal(38.59, FenixChannelRouteGeometry.DistanceNm(At(50, 8), At(50, 9)), Within);
        Assert.Equal(0, FenixChannelRouteGeometry.DistanceNm(At(50, 8), At(50, 8)), Within);
    }

    [Fact]
    public void A_point_projects_onto_a_leg()
    {
        // The leg runs east along the equator; the point is north of it, on the left.
        (double along, double cross) = FenixChannelRouteGeometry.Project(At(0, 0), At(0, 2), At(0.5, 1));

        Assert.Equal(60.04, along, Within);
        Assert.Equal(-30.02, cross, Within);
    }

    [Fact]
    public void A_point_behind_a_leg_projects_negative()
    {
        (double along, double cross) = FenixChannelRouteGeometry.Project(At(0, 0), At(0, 1), At(0, -0.5));

        Assert.Equal(-30.02, along, Within);
        Assert.Equal(0, cross, Within);
    }

    [Fact]
    public void An_arc_is_its_radius_times_its_turn()
    {
        Assert.Equal(15.71, FenixChannelRouteGeometry.ArcNm(10, 0, 90), Within);
        // The smaller angle: 350 to 10 is a 20 degree turn, never 340.
        Assert.Equal(3.49, FenixChannelRouteGeometry.ArcNm(10, 350, 10), Within);
        Assert.Equal(0, FenixChannelRouteGeometry.ArcNm(10, 90, 90), Within);
    }
}
