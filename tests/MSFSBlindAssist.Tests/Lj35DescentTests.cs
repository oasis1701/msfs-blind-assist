using MSFSBlindAssist.Aircraft.Learjet35;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The Learjet's estimated top of descent: a three-degree path, said as an estimate.</summary>
public class Lj35DescentTests
{
    [Fact]
    public void ThreeDegreesFromCruiseToTheDefaultTarget()
    {
        // 35,000 - 1,500 = 33,500 ft at 318 ft/nm = 105.3 nm of descent; 300 nm out leaves 194.7.
        Assert.Equal("Top of descent in 195 miles, three degrees to 1500 feet, no GNS VNAV target set.",
            Lj35Descent.ComposeTopOfDescent(35000, null, 300));
    }

    [Fact]
    public void UsesTheGnsTargetWhenItHasOne()
    {
        Assert.Equal("Top of descent in 97 miles, three degrees to the GNS target of 3000 feet.",
            Lj35Descent.ComposeTopOfDescent(34000, 3000, 194.5));
    }

    [Fact]
    public void AZeroTargetIsNoTarget() =>
        Assert.Contains("no GNS VNAV target set", Lj35Descent.ComposeTopOfDescent(20000, 0, 100));

    [Fact]
    public void PastTheTopOfDescentSaysByHowMuch() =>
        Assert.Equal("Past top of descent by 5.3 miles, three degrees to 1500 feet, no GNS VNAV target set.",
            Lj35Descent.ComposeTopOfDescent(20000, null, 52.9));

    [Fact]
    public void NoRouteDistanceMeansNoAnswer() =>
        Assert.Equal("Distance to destination not computed, so no top of descent.", Lj35Descent.ComposeTopOfDescent(35000, null, null));

    [Fact]
    public void AlreadyLowEnoughIsNotADescent() =>
        Assert.Equal("Already at or below the descent target altitude.", Lj35Descent.ComposeTopOfDescent(1800, null, 40));
}
