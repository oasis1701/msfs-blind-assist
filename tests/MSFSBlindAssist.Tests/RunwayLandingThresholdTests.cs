using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The painted landing threshold (<see cref="NavigationCalculator.LandingThreshold"/>): the runway end's
/// position is the start of its pavement, and a displaced threshold lies <see cref="Runway.ThresholdOffset"/>
/// feet along the runway from it. The runway is KATL 27R as the MSFS 2024 navdata has it.
/// </summary>
public class RunwayLandingThresholdTests
{
    private const double FtPerNm = 6076.12;

    private static Runway Katl27R(double offsetFt, double lengthFt = 12394.0) => new()
    {
        AirportICAO = "KATL", RunwayID = "27R", Heading = 270.003356933594,
        StartLat = 33.634708404541, StartLon = -84.4072036743164,
        Length = lengthFt, ThresholdOffset = offsetFt,
    };

    [Fact]
    public void A_runway_with_no_displaced_threshold_lands_at_its_end()
    {
        var r = Katl27R(0.0);

        var (lat, lon) = NavigationCalculator.LandingThreshold(r);

        Assert.Equal(r.StartLat, lat);
        Assert.Equal(r.StartLon, lon);
    }

    [Fact]
    public void A_displaced_threshold_lies_its_offset_down_the_runway()
    {
        var r = Katl27R(512.0);

        var (lat, lon) = NavigationCalculator.LandingThreshold(r);

        Assert.Equal(512.0, NavigationCalculator.CalculateDistance(r.StartLat, r.StartLon, lat, lon) * FtPerNm, 0.5);
        Assert.Equal(r.Heading, NavigationCalculator.CalculateBearing(r.StartLat, r.StartLon, lat, lon), 0.01);
    }

    [Fact]
    public void An_offset_that_reaches_the_far_end_is_ignored()
    {
        // KMWH 18 in both navdata builds: 3,333 ft on a 3,321 ft runway.
        var r = Katl27R(3333.0, lengthFt: 3321.0);

        var (lat, lon) = NavigationCalculator.LandingThreshold(r);

        Assert.Equal(r.StartLat, lat);
        Assert.Equal(r.StartLon, lon);
    }

    [Fact]
    public void An_offset_on_a_runway_of_unknown_length_still_applies()
    {
        var r = Katl27R(512.0, lengthFt: 0.0);

        var (lat, lon) = NavigationCalculator.LandingThreshold(r);

        Assert.Equal(512.0, NavigationCalculator.CalculateDistance(r.StartLat, r.StartLon, lat, lon) * FtPerNm, 0.5);
    }
}
