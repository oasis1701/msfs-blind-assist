// Characterization tests for clearances that return to a taxiway they just left ("A, B, A").
// VirtualPilot 2026-09-18: the router let B shrink to nothing at the junction it was entered by,
// then routed the rest of the clearance from the wrong place (OTBH 16L "A, C, A": 9.1 km against a
// 2.5 km direct route). Both readings are now built; the one that honours B is taken unless it
// loops (> 25 % + 100 m longer). Equator fixture: metres = degrees x 111132.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RepeatedTaxiwayClearanceTests
{
    private static TaxiPath P(double la1, double lo1, double la2, double lo2, string name) => new()
    {
        StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75.0,
    };

    private static int NodeAt(TaxiGraph g, double lat, double lon)
        => g.Nodes.Values.OrderBy(n => TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude)).First().NodeId;

    // A runs east along the equator; B is a bypass loop 55 m north from A at 0.003 to A at 0.006.
    private static TaxiGraph Bypass() => TaxiGraph.Build(new List<TaxiPath>
    {
        P(0, 0, 0, 0.003, "A"), P(0, 0.003, 0, 0.006, "A"), P(0, 0.006, 0, 0.010, "A"),
        P(0, 0.003, 0.0005, 0.003, "B"), P(0.0005, 0.003, 0.0005, 0.006, "B"), P(0.0005, 0.006, 0, 0.006, "B"),
    }, new List<ParkingSpot>(), new List<StartPosition>());

    [Fact]
    public void A_bypass_clearance_is_flown_through_the_bypass()
    {
        var g = Bypass();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 0, 0.010),
            new List<string> { "A", "B", "A" });
        Assert.NotNull(route);
        Assert.Contains(route!.Segments, s => s.TaxiwayName == "B");
        Assert.Null(route.ConstrainedFallbackReason);
    }

    [Fact]
    public void Without_the_repeat_the_route_stays_on_A()
    {
        var g = Bypass();
        var route = new TaxiRouter(g).FindConstrainedPath(NodeAt(g, 0, 0), NodeAt(g, 0, 0.010),
            new List<string> { "A" });
        Assert.NotNull(route);
        Assert.DoesNotContain(route!.Segments, s => s.TaxiwayName == "B");
    }
}
