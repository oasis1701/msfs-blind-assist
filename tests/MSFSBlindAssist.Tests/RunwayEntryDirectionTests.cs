using MSFSBlindAssist.Navigation;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>EGLL 09R via NB10 (2026-09-24): which way a route faces as it arrives on its runway.</summary>
public class RunwayEntryDirectionTests
{
    private const double Lat0 = 51.4648, Lon0 = -0.4859;
    // Local metres (east, north) → lat/lon near EGLL.
    private static (double, double) P(double east, double north) =>
        (Lat0 + north / 111195.0, Lon0 + east / (111195.0 * Math.Cos(Lat0 * Math.PI / 180.0)));

    // NB10's west curve: comes south, bends right, ends heading west (the 27L entry).
    private static readonly (double, double)[] WestCurve =
        { P(84, 111), P(84, 41), P(81, 29), P(72, 17), P(59, 7), P(43, 2), P(11, 2) };
    // NB10's east curve: comes south, bends left, ends heading east (the 09R entry).
    private static readonly (double, double)[] EastCurve =
        { P(84, 111), P(84, 41), P(87, 27), P(96, 14), P(113, 4), P(122, 2), P(153, 1) };

    [Fact]
    public void WestCurveIsReversedFor09R() =>
        Assert.True(RunwayEntryDirection.IsReversed(RunwayEntryDirection.ArrivalBearing(WestCurve), 89.7));

    [Fact]
    public void EastCurveIsForwardFor09R()
    {
        double? b = RunwayEntryDirection.ArrivalBearing(EastCurve);
        Assert.False(RunwayEntryDirection.IsReversed(b, 89.7));
        Assert.True(RunwayEntryDirection.IsForward(b, 89.7));
    }

    [Fact]
    public void PerpendicularEntryIsNeitherReversedNorRejected()
    {
        var straight = new[] { P(84, 111), P(84, 41), P(84, 2) };
        double? b = RunwayEntryDirection.ArrivalBearing(straight);
        Assert.False(RunwayEntryDirection.IsReversed(b, 89.7));
        Assert.True(RunwayEntryDirection.IsForward(b, 89.7));
    }

    [Fact]
    public void EntryAngledBackFortyFiveDegreesIsNotReversed()
    {
        // A rapid entry angled back against the takeoff direction (EGLL 09L AB11 shape): 135° off.
        var angled = new[] { P(100, 60), P(40, 0) };
        Assert.False(RunwayEntryDirection.IsReversed(RunwayEntryDirection.ArrivalBearing(angled), 89.7));
    }

    [Fact]
    public void ShortStubCannotDecideTheArrival() =>
        Assert.Null(RunwayEntryDirection.ArrivalBearing(new[] { P(0, 0), P(-4, 0) }));

    [Fact]
    public void UTurnOnTheRunwayIsAHairpin()
    {
        var uturn = new[] { P(84, 111), P(84, 41), P(43, 2), P(11, 2), P(60, 2) };
        Assert.True(RunwayEntryDirection.HairpinsNearEnd(uturn, 300));
        Assert.False(RunwayEntryDirection.HairpinsNearEnd(EastCurve, 300));
    }
}
