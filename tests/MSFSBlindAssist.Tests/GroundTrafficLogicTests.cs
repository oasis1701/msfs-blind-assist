// Characterization tests for MSFSBlindAssist.Services.GroundTrafficLogic — the pure geometry and
// phrasing behind the ground traffic monitor: closest point of approach, relative motion, runway
// occupancy / final classification, route projection and spoken aircraft names.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficLogicTests
{
    // --- Closest point of approach ------------------------------------------------

    [Fact]
    public void HeadOn_ClosesToZero_AtDistanceOverClosingSpeed()
    {
        // Traffic 200 m ahead (north), closing at 10 m/s.
        var (t, d) = GroundTrafficLogic.ClosestApproach(0, 200, 0, -10);
        Assert.Equal(20.0, t, 3);
        Assert.Equal(0.0, d, 3);
    }

    [Fact]
    public void Opening_ReturnsTimeZero_AndCurrentDistance()
    {
        var (t, d) = GroundTrafficLogic.ClosestApproach(0, 200, 0, 10);
        Assert.Equal(0.0, t);
        Assert.Equal(200.0, d, 3);
    }

    [Fact]
    public void ParallelTaxiway_PassesAtItsOffset()
    {
        // Head-on on a taxiway 80 m to the side: closest approach is the 80 m offset.
        var (t, d) = GroundTrafficLogic.ClosestApproach(80, 300, 0, -10);
        Assert.Equal(30.0, t, 3);
        Assert.Equal(80.0, d, 3);
    }

    [Fact]
    public void Velocity_NorthAndEast()
    {
        var (vx, vy) = GroundTrafficLogic.Velocity(0, 10);
        Assert.Equal(0.0, vx, 6);
        Assert.Equal(5.14444, vy, 4);
        (vx, vy) = GroundTrafficLogic.Velocity(90, 10);
        Assert.Equal(5.14444, vx, 4);
        Assert.Equal(0.0, vy, 6);
    }

    // --- Relative motion ----------------------------------------------------------

    [Theory]
    [InlineData(0, 180, 10, 0, TrafficMotion.HeadOn)]
    [InlineData(0, 180, 10, 150, TrafficMotion.OppositeDirection)] // behind us, going the other way
    [InlineData(0, 10, 10, 0, TrafficMotion.SameDirection)]
    [InlineData(0, 90, 10, 0, TrafficMotion.CrossingLeftToRight)]
    [InlineData(0, 270, 10, 0, TrafficMotion.CrossingRightToLeft)]
    [InlineData(350, 80, 10, 0, TrafficMotion.CrossingLeftToRight)] // wraps through 360
    [InlineData(0, 90, 1, 0, TrafficMotion.Stopped)]
    public void ClassifyMotion(double own, double traffic, double gs, double rel, TrafficMotion expected)
        => Assert.Equal(expected, GroundTrafficLogic.ClassifyMotion(own, traffic, gs, rel));

    [Theory]
    [InlineData(0, "from ahead")]
    [InlineData(300, "from the left")]
    [InlineData(60, "from the right")]
    [InlineData(180, "from behind")]
    public void DescribeSide(double rel, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.DescribeSide(rel));

    // --- Spoken names ---------------------------------------------------------------

    [Theory]
    [InlineData("B77W", "777")]
    [InlineData("B738", "737")]
    [InlineData("A20N", "A320")]
    [InlineData("A321", "A321")]
    [InlineData("B38M", "737 MAX")]
    [InlineData("B747", "747")]
    [InlineData("", "")]
    public void SpokenType(string raw, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.SpokenType(raw));

    [Theory]
    [InlineData("Delta", "DAL123", "A20N", "Delta A320")]
    [InlineData("", "DAL123", "B738", "DAL 123, 737")]
    [InlineData("", "N12345", "", "N12345")]
    [InlineData("", "", "B77W", "777")]
    [InlineData("", "", "", "traffic")]
    [InlineData("Speedbird", "", "", "Speedbird traffic")]
    public void SpokenName(string airline, string callsign, string type, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.SpokenName(airline, callsign, type));

    // --- Route projection -------------------------------------------------------------

    [Fact]
    public void ProjectOntoRoute_ReportsLateralRouteDistanceAndTaxiway()
    {
        // Two legs: 0.001° north on A (~110 m), then 0.001° east on B.
        var route = new[]
        {
            new GroundTrafficRoutePoint(50.000, 0.000, "A", 0),
            new GroundTrafficRoutePoint(50.001, 0.000, "A", 110.54),
            new GroundTrafficRoutePoint(50.001, 0.001, "B", 110.54 + 71.55),
        };
        // A point halfway along leg B, 10 m south of it.
        var p = GroundTrafficLogic.ProjectOntoRoute(route, 50.001 - 10 / 110540.0, 0.0005);
        Assert.NotNull(p);
        Assert.Equal("B", p!.Value.Taxiway);
        Assert.Equal(10.0, p.Value.LateralMetres, 0);
        Assert.Equal(110.54 + 35.8, p.Value.RouteMetres, 0);
    }

    [Fact]
    public void ProjectOntoRoute_NeedsTwoPoints()
        => Assert.Null(GroundTrafficLogic.ProjectOntoRoute(
               new[] { new GroundTrafficRoutePoint(50, 0, "A", 0) }, 50, 0));

    // --- Runway occupancy and final ------------------------------------------------------

    // Runway 09/27 at 50°N, ~3 km long, running due east from (50, 0).
    private static RunwayShape Runway0927() => RunwayShape.For(new TaxiGraph.RunwayCenterline
    {
        Name1 = "09", Name2 = "27",
        Lat1 = 50.0, Lon1 = 0.0, Lat2 = 50.0, Lon2 = 0.042,
        HeadingDeg1 = 90, HalfWidthMeters = 22.5,
    });

    private const double DegLonPerMetreAt50 = 1.0 / (111320.0 * 0.642788);

    [Fact]
    public void OnGround_OnThePavement_IsOnRunway()
    {
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Runway0927(), 50.0, 0.02, true, 90, 0);
        Assert.Equal(RunwayTrafficKind.OnRunway, fix.Kind);
    }

    [Fact]
    public void OnGround_BesideThePavement_IsNothing()
    {
        // 60 m north of the centreline: a parallel taxiway or a hold.
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Runway0927(), 50.0 + 60 / 110540.0, 0.02, true, 90, 0);
        Assert.Equal(RunwayTrafficKind.None, fix.Kind);
    }

    [Fact]
    public void Airborne_ThreeMilesWest_HeadingEast_IsOnFinalFor09()
    {
        double lon = -3 * 1852 * DegLonPerMetreAt50;
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Runway0927(), 50.0, lon, false, 92, 950);
        Assert.Equal(RunwayTrafficKind.OnFinal, fix.Kind);
        Assert.Equal("09", fix.Designator);
        Assert.Equal(3.0, fix.DistanceNm, 1);
    }

    [Fact]
    public void Airborne_EastOfRunway_HeadingWest_IsOnFinalFor27()
    {
        double lon = 0.042 + 2 * 1852 * DegLonPerMetreAt50;
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Runway0927(), 50.0, lon, false, 270, 600);
        Assert.Equal(RunwayTrafficKind.OnFinal, fix.Kind);
        Assert.Equal("27", fix.Designator);
    }

    [Theory]
    [InlineData(270, 950)]  // departing west, climbing out — pointed away from the runway
    [InlineData(92, 5000)]  // overflying far above a final
    public void Airborne_NotLanding_IsNothing(double heading, double height)
    {
        double lon = -3 * 1852 * DegLonPerMetreAt50;
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Runway0927(), 50.0, lon, false, heading, height);
        Assert.Equal(RunwayTrafficKind.None, fix.Kind);
    }

    [Fact]
    public void Airborne_BeyondSixMiles_IsNothing()
    {
        double lon = -8 * 1852 * DegLonPerMetreAt50;
        var fix = GroundTrafficLogic.ClassifyAgainstRunway(Runway0927(), 50.0, lon, false, 90, 2000);
        Assert.Equal(RunwayTrafficKind.None, fix.Kind);
    }

    // --- Departure queue: has the aircraft ahead pulled away? ---------------------

    [Fact]
    public void QueueDeparted_SpeedEdge_FiresOnTheSampleItStartsRolling()
    {
        // Stopped last sample, rolling this one — no gap has opened yet.
        Assert.True(GroundTrafficLogic.QueueDeparted(previousGs: 0.0, gs: 3.0,
                                                     distFt: 400, stoppedGapFt: 400));
    }

    [Fact]
    public void QueueDeparted_StillStopped_IsSilent()
    {
        Assert.False(GroundTrafficLogic.QueueDeparted(0.0, 0.2, 400, 400));
    }

    [Fact]
    public void QueueDeparted_Creeper_FiresOnTheOpeningGap_NotTheSpeedEdge()
    {
        // The EGLL case: it eased forward to 2 kt and held there, so by the time the gap has
        // opened its PREVIOUS sample is already above the stopped gate — the speed edge is gone
        // for good. 400 ft baseline, now 470 ft: 70 ft of gap.
        Assert.False(GroundTrafficLogic.QueueDeparted(2.0, 2.0, 430, 400));  // 30 ft: not yet
        Assert.True(GroundTrafficLogic.QueueDeparted(2.0, 2.0, 470, 400));   // 70 ft: departed
    }

    [Fact]
    public void QueueDeparted_OurOwnPushbackOpeningTheGap_IsNotTheQueueMoving()
    {
        // Gap wide open, but the aircraft ahead is parked: we are the one that moved.
        Assert.False(GroundTrafficLogic.QueueDeparted(0.0, 0.0, 600, 400));
    }

    [Fact]
    public void QueueDeparted_NoStoppedBaseline_NeedsTheSpeedEdge()
    {
        // Never seen stopped ahead of us (NaN baseline): the gap trigger has nothing to measure,
        // so a steady 2 kt taxi past us must not read as a queue departure.
        Assert.False(GroundTrafficLogic.QueueDeparted(2.0, 2.0, 500, double.NaN));
        Assert.True(GroundTrafficLogic.QueueDeparted(0.0, 2.0, 500, double.NaN));
    }

    // --- Phrasing helpers (PR #247 review L12) -------------------------------------------

    [Theory]
    [InlineData(TrafficMotion.Stopped, "stopped")]
    [InlineData(TrafficMotion.SameDirection, "same direction")]
    [InlineData(TrafficMotion.HeadOn, "head-on")]
    [InlineData(TrafficMotion.OppositeDirection, "opposite direction")]
    [InlineData(TrafficMotion.CrossingLeftToRight, "crossing left to right")]
    [InlineData(TrafficMotion.CrossingRightToLeft, "crossing right to left")]
    public void DescribeMotion_WordsEveryMotion(TrafficMotion motion, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.DescribeMotion(motion));

    [Theory]
    [InlineData(0, "ahead")]
    [InlineData(20, "ahead")]
    [InlineData(21, "ahead and to the right")]
    [InlineData(70, "ahead and to the right")]
    [InlineData(71, "to the right")]
    [InlineData(110, "to the right")]
    [InlineData(111, "behind and to the right")]
    [InlineData(160, "behind and to the right")]
    [InlineData(161, "behind")]
    [InlineData(180, "behind")]
    [InlineData(200, "behind and to the left")]
    [InlineData(270, "to the left")]
    [InlineData(340, "ahead")]
    [InlineData(-30, "ahead and to the left")]
    public void DescribeDirection_Bands(double relBearing, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.DescribeDirection(relBearing));

    [Fact]
    public void RunwayLabel_JoinsDesignators()
    {
        Assert.Equal("Runway 27", GroundTrafficLogic.RunwayLabel(new[] { "27" }));
        Assert.Equal("Runway 27L and 27R", GroundTrafficLogic.RunwayLabel(new[] { "27L", "27R" }));
    }
}
