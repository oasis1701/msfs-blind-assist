// The one box-margin conversion (review CL-4): AirportFacilities.ContainsPoint, the scenery census's
// cell score and CurrentAirportResolver all grow a navdata box through GrownBox.
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

public class GrownBoxTests
{
    [Fact]
    public void A_box_grows_by_its_margin_in_metres_converted_at_its_own_middle_latitude()
    {
        var g = GrownBox.Of(topLat: 60.5, bottomLat: 59.5, leftLon: 10.0, rightLon: 10.1, marginMetres: 300);
        double dLat = 300 / 111_320.0, dLon = 300 / (111_320.0 * Math.Cos(60.0 * Math.PI / 180.0));
        Assert.Equal(60.5 + dLat, g.Top, 12);
        Assert.Equal(59.5 - dLat, g.Bottom, 12);
        Assert.Equal(10.0 - dLon, g.Left, 12);
        Assert.Equal(10.1 + dLon, g.Right, 12);
    }

    [Fact]
    public void Near_a_pole_the_longitude_margin_is_bounded()
        => Assert.Equal(300 / (111_320.0 * 0.05), GrownBox.Of(89.99, 89.98, 0.0, 0.0, 300).Right, 12);

    [Fact]
    public void A_zero_margin_is_the_bare_box()
        => Assert.Equal(new GrownBox(2.0, 1.0, 10.0, 11.0), GrownBox.Of(2.0, 1.0, 10.0, 11.0, 0));

    [Fact]
    public void The_edge_itself_is_inside_and_a_rectangle_touching_it_reaches_it()
    {
        var g = new GrownBox(Top: 2.0, Bottom: 1.0, Left: 10.0, Right: 11.0);
        Assert.True(g.Contains(2.0, 11.0));
        Assert.False(g.Contains(2.000001, 11.0));
        Assert.True(g.Reaches(bottomLat: 2.0, topLat: 3.0, leftLon: 11.0, rightLon: 12.0));          // corner to corner
        Assert.False(g.Reaches(bottomLat: 2.000001, topLat: 3.0, leftLon: 11.0, rightLon: 12.0));
    }

    [Fact]
    public void Every_consumer_grows_a_box_the_same_way()
    {
        // A tall box at 60° N. CurrentAirportResolver used to convert the margin at the AIRCRAFT's
        // latitude (60.49°) and AirportFacilities at the box's (60.0°): a point 0.0054° east of the box
        // was inside for one and outside for the other. The candidate's reference point is 54 km away,
        // so only the box can make Pick answer.
        var facilities = new AirportFacilities { Icao = "TALL", TopLat = 60.5, BottomLat = 59.5, LeftLon = 10.0, RightLon = 10.1 };
        var candidate = new AirportCandidate("TALL", 60.0, 10.05, 10.0, 10.1, 60.5, 59.5, 10);
        double margin = CurrentAirportResolver.BoxMarginMetres;

        Assert.False(facilities.ContainsPoint(60.49, 10.1054, margin));
        Assert.Null(CurrentAirportResolver.Pick(new[] { candidate }, 60.49, 10.1054));
        Assert.True(facilities.ContainsPoint(60.49, 10.1053, margin));
        Assert.Equal("TALL", CurrentAirportResolver.Pick(new[] { candidate }, 60.49, 10.1053));
    }
}
