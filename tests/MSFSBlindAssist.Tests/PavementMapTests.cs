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
        Assert.True(map.IsOnMappedPavement(20 / M, 100 / M));    // 9.1 m half-width + 15 m = 24.1 m
        Assert.False(map.IsOnMappedPavement(30 / M, 100 / M));
    }
}
