// "Is this point on mapped pavement?" (KMEM 36L 2026-09-26: ~13 s in the grass, up to 55 m right of
// the centerline, with nothing said).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class PavementMapTests
{
    private static readonly PavementMap Kmem = PavementMap.Build(KmemRunway36LFixture.BuildGraph());

    private static bool On(double alongFt, double lateralM)
    {
        var (lat, lon) = KmemRunway36LFixture.PointAt(alongFt, lateralM);
        return Kmem.IsOnMappedPavement(lat, lon);
    }

    [Fact]
    public void The_runway_counts_to_ten_metres_past_its_edge()
    {
        Assert.True(On(5000, 0));
        Assert.True(On(7000, 34.0));    // half-width 25.0 m + 10 m
        Assert.False(On(7000, 40.0));
        Assert.False(On(7000, -40.0));
    }

    [Fact]
    public void Kmem_the_grass_between_36L_and_taxiway_M_is_off_pavement()
        => Assert.False(On(7300, 52.0));   // where the aircraft ran at 01:23:36

    [Fact]
    public void Kmem_M7_counts_as_pavement()
        => Assert.True(On(7927, 44.7));    // an M7 node, 44.7 m right of the centerline

    [Fact]
    public void Without_runways_only_taxiways_count()
    {
        const double M = 111132.0;
        var g = TaxiGraph.Build(new List<TaxiPath>
        {
            new() { Type = "T", Width = 60, Name = "A", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = 200 / M },
        }, new List<ParkingSpot>(), new List<StartPosition>());
        var map = PavementMap.Build(g);
        Assert.True(map.IsOnMappedPavement(20 / M, 100 / M));    // 9.1 m half-width + 15 m, floored at 25 m
        Assert.False(map.IsOnMappedPavement(30 / M, 100 / M));
    }

    private static PavementMap OneEdge(double widthFt)
    {
        const double M = 111132.0;
        var g = TaxiGraph.Build(new List<TaxiPath>
        {
            new() { Type = "T", Width = widthFt, Name = "A", StartLat = 0, StartLon = 0, EndLat = 0, EndLon = 400 / M },
        }, new List<ParkingSpot>(), new List<StartPosition>());
        return PavementMap.Build(g);
    }

    [Fact]
    public void A_taxiway_with_no_width_reaches_as_far_as_the_off_route_detector_says()
    {
        // PavementTolerance reads a missing width as 75 ft: 11.4 m + 15 m = 26.4 m (the map's own copy said 40 m).
        const double M = 111132.0;
        var map = OneEdge(0);
        Assert.True(map.IsOnMappedPavement(25 / M, 200 / M));
        Assert.False(map.IsOnMappedPavement(30 / M, 200 / M));
    }

    [Fact]
    public void A_wide_apron_edge_reaches_as_far_as_the_off_route_detector_says()
    {
        // 300 ft: 45.7 m + 15 m = 60.7 m (the map's own copy capped the half-width at 25 m: 40 m).
        const double M = 111132.0;
        var map = OneEdge(300);
        Assert.True(map.IsOnMappedPavement(55 / M, 200 / M));
        Assert.False(map.IsOnMappedPavement(65 / M, 200 / M));
    }
}
