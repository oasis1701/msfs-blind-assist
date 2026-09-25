using MSFSBlindAssist.Aircraft.Learjet35;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Learjet's characteristic speeds: the AFM stall figures at max gross, scaled by the
/// square root of weight, and the published limitations verbatim.
/// </summary>
public class Lj35SpeedsTests
{
    [Fact]
    public void AtMaxGrossTheStallSpeedsAreTheVendorsAfmFigures()
    {
        Assert.Equal(129, Lj35Speeds.StallClean(18300), 0.01);
        Assert.Equal(105, Lj35Speeds.StallFullFlap(18300), 0.01);
    }

    [Theory]
    [InlineData(15000, 124)]   // within a knot of the real 35A Vref chart
    [InlineData(12000, 111)]
    [InlineData(18300, 137)]   // 136.5, spoken rounded away from zero
    public void VrefFlaps40FollowsTheChart(double weight, int expectedKt) =>
        Assert.Equal(expectedKt, Math.Round(Lj35Speeds.Vref40(weight), MidpointRounding.AwayFromZero));

    [Fact]
    public void LighterIsSlowerAndNeverBelowFullFlapStall()
    {
        Assert.True(Lj35Speeds.StallClean(12000) < Lj35Speeds.StallClean(18300));
        Assert.True(Lj35Speeds.Vref20(14000) > Lj35Speeds.Vref40(14000));
        Assert.True(Lj35Speeds.Vr(14000) < Lj35Speeds.V2(14000));
    }

    [Fact]
    public void AnUnreadWeightFallsBackToMaxGrossAndSaysSo()
    {
        Assert.Equal(Lj35Speeds.Vref40(18300), Lj35Speeds.Vref40(null));
        Assert.Contains("gross weight not read yet", Lj35Speeds.ComposeVref(null));
        Assert.Contains("gross weight not read yet", Lj35Speeds.ComposeVref(0));
    }

    [Fact]
    public void ReadoutsSayWhichKindOfNumberTheyAre()
    {
        Assert.Contains("Estimated from the stall speeds", Lj35Speeds.ComposeTakeoff(16000));
        Assert.Contains("Computed from the stall speed at weight", Lj35Speeds.ComposeVref(16000));
        Assert.Contains("as published", Lj35Speeds.ComposeBarberPole());
        Assert.Contains("as published", Lj35Speeds.ComposeFlapLimits());
        Assert.Contains("as published", Lj35Speeds.ComposeGearLimits());
    }

    [Fact]
    public void TheWordingCarriesTheNumbers()
    {
        Assert.Equal("Vmo 300 knots, Mmo point 81, as published for the 35A.", Lj35Speeds.ComposeBarberPole());
        Assert.Equal("Flap limits: 8 and 20 degrees 200 knots, 40 degrees 150 knots, as published.", Lj35Speeds.ComposeFlapLimits());
        Assert.Equal("Gear: operate below 200 knots, extended limit 260 knots, as published.", Lj35Speeds.ComposeGearLimits());
        Assert.Equal("Stall at 15000 pounds: clean 117, flaps 40 95 knots. AFM at 18300 pounds: 129 and 105.", Lj35Speeds.ComposeStall(15000));
        Assert.Equal("Takeoff, flaps 8, at 18300 pounds: rotate 133, V2 146 knots. Estimated from the stall speeds.", Lj35Speeds.ComposeTakeoff(18300));
    }
}
