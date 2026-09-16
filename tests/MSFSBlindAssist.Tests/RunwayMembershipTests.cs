// The runway checks outside the crossing pass use the same runway shape: Where-Am-I (5 m margin),
// takeoff-assist detection (strict, no margin), and the END a hold node is named after. Fixture: a
// 3,000 m runway 09/27, 200 ft wide, whose 09 start row sits 600 m inside the pavement (a displaced
// threshold) — built through TaxiGraph.Build with the runway table, as production does.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class RunwayMembershipTests
{
    private static TaxiGraph Build()
    {
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(600), Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = Lat(0), Longitude = Lon(3000), Heading = 270 },
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "09", Heading = 90, StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(3000), Width = 200.0 },
            new Runway { RunwayID = "27", Heading = 270, StartLat = Lat(0), StartLon = Lon(3000), EndLat = Lat(0), EndLon = Lon(0), Width = 200.0 },
        };
        return TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts, runways);
    }

    [Fact]
    public void Where_am_I_reports_the_runway_on_the_displaced_threshold()
    {
        var g = Build();
        Assert.Single(g.RunwayCenterlines);
        Assert.Equal("Runway 09", g.DescribeLocation(Lat(0), Lon(200)));
    }

    [Fact]
    public void Where_am_I_uses_the_real_half_width_plus_five_metres()
    {
        var g = Build();
        Assert.Equal("Runway 09", g.DescribeLocation(Lat(34), Lon(1000)));   // 30.48 + 5 = 35.48
        Assert.Equal("", g.DescribeLocation(Lat(37), Lon(1000)));
    }

    [Fact]
    public void Takeoff_detection_is_strict_on_the_real_half_width()
    {
        var g = Build();

        Assert.True(g.TryGetRunwayAtPosition(Lat(29), Lon(1000), 90.0, out string id, out _, out double thrLon, out _));
        Assert.Equal("09", id);
        Assert.Equal(Lon(600), thrLon, 9);   // the threshold point is still the start row
        Assert.False(g.TryGetRunwayAtPosition(Lat(31), Lon(1000), 90.0, out _, out _, out _, out _));
        Assert.True(g.TryGetRunwayAtPosition(Lat(0), Lon(200), 90.0, out _, out _, out _, out _));
    }

    [Fact]
    public void A_hold_node_is_named_after_the_pavement_end_it_is_nearer()
    {
        // 1,700 m along the pavement is nearer the 27 end; measured between the start rows
        // (600..3000 m) it would have read as nearer 09.
        var g = Build();
        Assert.Equal("27", TaxiGraph.MatchHoldShortRunwayName(Lat(60), Lon(1700), g.RunwayCenterlines, 150.0));
    }
}
